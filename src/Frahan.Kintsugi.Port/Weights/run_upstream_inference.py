"""
Run upstream PuzzleFusion++ inference (auto_aggl.test_denoiser_only) on
the SAME deterministic input the C# Mode=Port port consumes, and
print the predicted transforms.

This is the diagnostic that tells us whether the residual alignment
error in the Frahan Kintsugi assembly is:
  (a) PORT DRIFT  -- upstream PyTorch produces tighter transforms
                     than the C# port; gap is in our primitives.
  (b) MODEL LIMIT -- upstream PyTorch produces similar-quality
                     transforms; this sample is just hard.

If (a) -- consider writing a TorchSharpDenoiserPath.cs that wraps
TorchSharp/libtorch for paper-quality inference.
If (b) -- accept and move on; try other samples, or scan real stone
fragments which sit closer to the training distribution.

USAGE
    cd D:\\code_ws\\Template-General\\outputs\\2026-05-01\\frahan_stonepack\\src\\Frahan.Kintsugi.Port\\Weights
    python run_upstream_inference.py \\
        --upstream  .\\puzzlefusion-plusplus \\
        --ckpt-root D:\\code_ws\\Template-General\\outputs\\2026-05-22\\reference\\extracted\\output \\
        --bb-sample D:\\code_ws\\Template-General\\outputs\\2026-05-22\\reference\\bb_sample_00697.bin \\
        --num-steps 20 \\
        --seed      42
"""
from __future__ import annotations

import argparse
import struct
import sys
from pathlib import Path

import numpy as np
import torch

MAGIC = b"FRKINTSU"
DTYPE_FLOAT32 = 1


def read_frkintsu(path: Path) -> dict:
    """Read a FRKINTSU binary into a {name: np.ndarray} dict."""
    tensors = {}
    with open(path, "rb") as f:
        if f.read(8) != MAGIC: raise ValueError("bad magic")
        version = struct.unpack("<I", f.read(4))[0]
        count = struct.unpack("<I", f.read(4))[0]
        f.read(8)  # reserved
        for _ in range(count):
            (name_len,) = struct.unpack("<H", f.read(2))
            name = f.read(name_len).decode("utf-8")
            dtype, rank = struct.unpack("<BB", f.read(2))
            shape = struct.unpack(f"<{rank}I", f.read(4 * rank))
            n_elems = int(np.prod(shape))
            data = np.frombuffer(f.read(4 * n_elems), dtype=np.float32).copy()
            tensors[name] = data.reshape(shape)
    return tensors


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--upstream", type=Path, default=Path("./puzzlefusion-plusplus"))
    p.add_argument("--ckpt-root", type=Path, required=True)
    p.add_argument("--bb-sample", type=Path, required=True,
                   help="FRKINTSU .bin with bb.input.point_clouds (from extract_breaking_bad_sample.py).")
    p.add_argument("--num-steps", type=int, default=20)
    p.add_argument("--seed", type=int, default=42)
    args = p.parse_args()

    sys.path.insert(0, str(args.upstream.resolve()))

    # Stubs for training-time deps that don't install on Windows.
    import importlib.machinery, types
    for stub_name in ("chamferdist", "wandb", "trimesh"):
        if stub_name not in sys.modules:
            m = types.ModuleType(stub_name)
            m.__spec__ = importlib.machinery.ModuleSpec(stub_name, loader=None)
            m.__path__ = []
            m.__version__ = "0.0.0-stub"
            sys.modules[stub_name] = m
    if "chamferdist" in sys.modules:
        class _CD:
            def __init__(self, *a, **kw): pass
            def __call__(self, *a, **kw): return torch.tensor(0.0), torch.tensor(0.0)
        sys.modules["chamferdist"].ChamferDistance = _CD
    # torch_cluster + pytorch3d stubs (same as export script).
    _install_torch_extras()
    # PyTorch 2.6 safe-globals + weights_only=False default.
    try:
        import torch.serialization as _ts
        from omegaconf import DictConfig, ListConfig
        from omegaconf.base import ContainerMetadata, Metadata
        from omegaconf.nodes import AnyNode
        _ts.add_safe_globals([DictConfig, ListConfig, ContainerMetadata, Metadata, AnyNode])
    except Exception: pass
    _orig_load = torch.load
    def _wrap_load(*a, **kw):
        kw.setdefault("weights_only", False)
        return _orig_load(*a, **kw)
    torch.load = _wrap_load

    # Load models.
    from omegaconf import OmegaConf
    cfg_root = args.upstream / "config"
    print("loading models...")
    encoder = _load_encoder(args.ckpt_root, cfg_root)
    denoiser = _load_denoiser(args.ckpt_root, cfg_root)
    if encoder is None or denoiser is None:
        print("ERROR: model load failed")
        return

    # Read BB sample.
    bb = read_frkintsu(args.bb_sample)
    pc = bb["bb.input.point_clouds"]  # [F, 1000, 3]
    F = pc.shape[0]
    N = pc.shape[1]
    print(f"BB sample: {F} fragments, {N} points each")

    # Build input dict matching upstream auto_aggl convention.
    N_MAX = 20
    pc_padded = np.zeros((1, N_MAX, N, 3), dtype=np.float32)
    pc_padded[0, :F] = pc
    part_valids = np.zeros((1, N_MAX), dtype=np.float32); part_valids[0, :F] = 1.0
    part_scale  = np.ones((1, N_MAX, 1),  dtype=np.float32)
    ref_part    = np.zeros((1, N_MAX), dtype=bool); ref_part[0, 0] = True

    # Deterministic init noise.
    g = torch.Generator(); g.manual_seed(args.seed)
    noisy = torch.randn((1, N_MAX, 7), generator=g)
    # Identity at the ref part.
    noisy[0, 0] = torch.tensor([0., 0., 0., 0., 0., 0., 1.])  # [tx, ty, tz, qx, qy, qz, qw] -- match upstream layout

    pc_t = torch.from_numpy(pc_padded).float()
    valids_t = torch.from_numpy(part_valids).float()
    scale_t = torch.from_numpy(part_scale).float()
    ref_t = torch.from_numpy(ref_part)
    reference_gt_and_rots = torch.zeros_like(noisy)
    reference_gt_and_rots[ref_t] = noisy[ref_t]  # set anchor to its (identity-like) noisy init

    # Set up scheduler.
    sched_cfg = denoiser.cfg.model if hasattr(denoiser, "cfg") else None
    sched = denoiser.noise_scheduler
    sched.set_timesteps(num_inference_steps=args.num_steps)

    print(f"running {args.num_steps} diffusion steps...")
    with torch.no_grad():
        for step, t in enumerate(sched.timesteps):
            timesteps = t.reshape(-1).repeat(N_MAX)
            # Apply rotations to point clouds.
            from pytorch3d import transforms as t3d
            noise_quat = noisy[..., 3:]  # [B, N_MAX, 4]
            noise_quat = noise_quat / noise_quat.norm(dim=-1, keepdim=True).clamp_min(1e-8)
            pc_rot = t3d.quaternion_apply(noise_quat.unsqueeze(2), pc_t)
            # Encode masked fragments only.
            pc_masked = pc_rot[valids_t.bool()]
            enc_out = encoder.ae.encode(pc_masked)
            latent = torch.zeros((1, N_MAX, denoiser.cfg.model.num_point, denoiser.cfg.model.num_dim))
            xyz_kp = torch.zeros((1, N_MAX, denoiser.cfg.model.num_point, 3))
            latent[valids_t.bool()] = enc_out["z_q"]
            xyz_kp[valids_t.bool()] = enc_out["xyz"]
            # Denoise.
            core = denoiser.denoiser if hasattr(denoiser, "denoiser") else denoiser.model
            pred_noise = core(noisy, timesteps, latent, xyz_kp, valids_t, scale_t, ref_t)
            # Scheduler step.
            noisy = sched.step(pred_noise, t, noisy).prev_sample
            noisy[ref_t] = reference_gt_and_rots[ref_t]
            print(f"  step {step + 1}/{args.num_steps} (t={int(t)}) done")

    # Print final transforms.
    print("\nUPSTREAM PYTORCH PREDICTED TRANSFORMS:")
    print("(layout per-row: tx ty tz | qx qy qz qw)")
    for f in range(F):
        v = noisy[0, f].tolist()
        print(f"  frag {f}: trans=({v[0]:+.4f}, {v[1]:+.4f}, {v[2]:+.4f})  "
              f"quat=({v[3]:+.4f}, {v[4]:+.4f}, {v[5]:+.4f}, {v[6]:+.4f})")


def _install_torch_extras():
    import types, importlib.machinery
    if "torch_cluster" not in sys.modules:
        m = types.ModuleType("torch_cluster")
        m.__spec__ = importlib.machinery.ModuleSpec("torch_cluster", loader=None)
        def fps(x, batch=None, ratio=0.5, random_start=False):
            N = x.shape[0]
            K = int(round(N * float(ratio)))
            sel = torch.zeros(K, dtype=torch.long, device=x.device)
            dists = torch.full((N,), float("inf"), device=x.device)
            for i in range(1, K):
                d = (x - x[sel[i-1]]).pow(2).sum(-1)
                dists = torch.minimum(dists, d)
                sel[i] = dists.argmax()
            return sel
        m.fps = fps
        sys.modules["torch_cluster"] = m
    # pytorch3d / transforms via real install assumed; if user has CUDA setup it
    # works; otherwise install via pip install pytorch3d.


def _load_encoder(ckpt_root: Path, cfg_root: Path):
    try:
        from puzzlefusion_plusplus.vqvae.model.fracture_ae import FractureAE
        from omegaconf import OmegaConf
        ae_cfg = OmegaConf.load(str(cfg_root / "ae" / "vq_vae.yaml"))
        ckpts = sorted((ckpt_root).rglob("*autoencoder*last.ckpt"))
        if not ckpts: ckpts = sorted((ckpt_root / "autoencoder").rglob("*.ckpt"))
        if not ckpts: print("no encoder ckpt found"); return None
        m = FractureAE.load_from_checkpoint(str(ckpts[0]), strict=False)
        m.eval()
        return m
    except Exception as e:
        print(f"encoder load error: {e}")
        import traceback; traceback.print_exc(limit=2)
        return None


def _load_denoiser(ckpt_root: Path, cfg_root: Path):
    try:
        from puzzlefusion_plusplus.denoiser.model.denoiser import Denoiser
        from omegaconf import OmegaConf
        ckpts = sorted((ckpt_root).rglob("*denoiser*last.ckpt"))
        if not ckpts: ckpts = sorted((ckpt_root / "denoiser").rglob("*.ckpt"))
        if not ckpts: print("no denoiser ckpt found"); return None
        m = Denoiser.load_from_checkpoint(str(ckpts[0]), strict=False)
        m.eval()
        return m
    except Exception as e:
        print(f"denoiser load error: {e}")
        import traceback; traceback.print_exc(limit=2)
        return None


if __name__ == "__main__":
    main()
