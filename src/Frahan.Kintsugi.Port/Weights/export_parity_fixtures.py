"""
Export layer-by-layer parity fixtures from PuzzleFusion++ PyTorch models.

For each model (encoder / denoiser / verifier), captures one known
deterministic input + a set of reference outputs at intermediate
layer boundaries. Saves them in the same FRKINTSU binary format the
C# WeightReader parses.

The Frahan-side parity test (Frahan.Tests.KintsugiPortParityTests)
loads the same fixture binary, runs the C# port models on the
deterministic input, and asserts every captured layer output matches
to ~1e-3 L_inf tolerance.

UPSTREAM REPO ASSUMPTION
========================
The upstream `puzzlefusion-plusplus` repository has NO `setup.py`
and NO `pyproject.toml`. To import its model definitions you must
either run this script from inside the cloned repo's root, or pass
`--upstream <path/to/cloned/repo>` so we can sys.path-insert it.

USAGE
=====
    # 1. Clone upstream once.
    cd D:\\code_ws\\Template-General\\outputs\\2026-05-01\\frahan_stonepack\\src\\Frahan.Kintsugi.Port\\Weights
    git clone https://github.com/eric-zqwang/puzzlefusion-plusplus

    # 2. Install the inference-only deps in your Python env.
    pip install torch lightning==2.2.2 diffusers==0.21.4 omegaconf hydra-core

    # 3. Capture reference outputs.
    python export_parity_fixtures.py \\
        --upstream  ./puzzlefusion-plusplus \\
        --ckpt-root D:\\code_ws\\Template-General\\outputs\\2026-05-22\\reference\\output \\
        --out       D:\\code_ws\\Template-General\\outputs\\2026-05-22\\reference\\parity_fixtures.bin

If any of the upstream imports fail, the script still writes the
INPUT fixtures (so the C# port test infrastructure stays runnable)
and prints a clear diagnostic. This is GPL-3.0, matching the rest of
the port.
"""
from __future__ import annotations

import argparse
import contextlib
import os
import struct
import sys
import traceback
from pathlib import Path

import torch

MAGIC = b"FRKINTSU"
VERSION = 1
DTYPE_FLOAT32 = 1

# Determinism hyperparameters that MUST be set before any model
# instantiation. The Plan agent flagged BatchNorm / Dropout / Attention
# randomness as the highest-risk parity breakers.
SEED = 42
N_POINTS = 1000          # encoder input size per fragment (upstream training default)
N_FRAGMENTS = 8          # denoiser input batch
TIMESTEP_T = 500         # mid-schedule diffusion step
EMBED_D = 512            # denoiser hidden dim
VERIFIER_D = 256         # verifier hidden dim


# ---------------------------------------------------------------------------
# FRKINTSU writer (must match WeightReader.cs exactly).
# ---------------------------------------------------------------------------

def write_tensor(f, name: str, tensor):
    name_bytes = name.encode("utf-8")
    arr = tensor.detach().cpu().float().contiguous()
    shape = list(arr.shape)
    f.write(struct.pack("<H", len(name_bytes)))
    f.write(name_bytes)
    f.write(struct.pack("<BB", DTYPE_FLOAT32, len(shape)))
    for d in shape:
        f.write(struct.pack("<I", d))
    f.write(arr.flatten().numpy().tobytes())


def export_fixtures(out_path: Path, tensors: dict):
    out_path.parent.mkdir(parents=True, exist_ok=True)
    with open(out_path, "wb") as f:
        f.write(MAGIC)
        f.write(struct.pack("<I", VERSION))
        f.write(struct.pack("<I", len(tensors)))
        f.write(struct.pack("<Q", 0))
        for name, t in tensors.items():
            write_tensor(f, name, t)
    sz = out_path.stat().st_size
    print(f"wrote {out_path}  ({sz:,} bytes, {len(tensors)} tensors)")


# ---------------------------------------------------------------------------
# Deterministic input tensors.
# ---------------------------------------------------------------------------

def build_deterministic_inputs():
    """Return a dict of the inputs we save AND feed to the upstream
    models. Same seed every time, so every C# parity test runs the
    same numbers."""
    torch.manual_seed(SEED)
    if torch.cuda.is_available():
        torch.cuda.manual_seed_all(SEED)
    torch.use_deterministic_algorithms(False)  # cudnn determinism flag

    # Encoder input: point cloud, mean-centred, max-extent normalised.
    pts = torch.rand((N_POINTS, 3)) * 2 - 1
    pts = pts - pts.mean(dim=0, keepdim=True)
    max_extent = pts.abs().max().item() or 1.0
    pts = pts / max_extent
    # Batch dim for the upstream forward: [B=1, N, 3]. Some upstream
    # variants want channels-first [B, 3, N]; we save both.
    pts_bnc = pts.unsqueeze(0)                # [1, N, 3]
    pts_bcn = pts_bnc.permute(0, 2, 1)        # [1, 3, N]

    # Denoiser input: 8 noisy poses (4 quat + 3 trans = 7).
    noisy_poses = torch.randn((N_FRAGMENTS, 7))
    # Quaternion normalisation (the upstream code always operates on
    # unit quaternions; sign ambiguity handled by the parity tests).
    qn = noisy_poses[:, :4].norm(dim=-1, keepdim=True).clamp_min(1e-8)
    noisy_poses[:, :4] = noisy_poses[:, :4] / qn

    # Verifier input. Upstream VerifierTransformer has
    # `edge_feature_emb = nn.Linear(7, embed_dim)` so input is 7-D
    # per edge -- the 4-D quaternion + 3-D translation for the
    # candidate pair. Shape: [B=1, E=1, 7].
    pair_features = torch.randn((1, 1, 7))

    return {
        "parity.input.point_cloud":      pts,            # [N, 3]
        "parity.input.point_cloud_bnc":  pts_bnc,        # [1, N, 3]
        "parity.input.point_cloud_bcn":  pts_bcn,        # [1, 3, N]
        "parity.input.noisy_poses":      noisy_poses,    # [N_frag, 7]
        "parity.input.timestep":         torch.tensor([float(TIMESTEP_T)]),
        "parity.input.pair_features":    pair_features,  # [1, 14]
    }


# ---------------------------------------------------------------------------
# Hook utility. Stores intermediate outputs by name.
# ---------------------------------------------------------------------------

class HookCollector:
    def __init__(self):
        self.captured: dict[str, torch.Tensor] = {}
        self._handles = []

    def hook(self, module, name: str):
        def _fn(_mod, _input, output):
            # Some modules return tuples (e.g. set-abstraction layers
            # return (l_xyz, l_points)). Save each component.
            if isinstance(output, tuple):
                for i, t in enumerate(output):
                    if torch.is_tensor(t):
                        self.captured[f"{name}.tuple{i}"] = t.detach().cpu().contiguous()
            elif torch.is_tensor(output):
                self.captured[name] = output.detach().cpu().contiguous()

        h = module.register_forward_hook(_fn)
        self._handles.append(h)

    def remove_all(self):
        for h in self._handles:
            h.remove()
        self._handles.clear()


# ---------------------------------------------------------------------------
# Upstream model instantiation (best-effort; gracefully degrades).
# ---------------------------------------------------------------------------

def _install_stubs():
    """Pre-inject stub modules for upstream's training-time imports
    that aren't installable on Windows without CUDA toolchain.
    Inference doesn't actually call these classes; the upstream code
    imports them at module-load and we want the import to succeed
    silently. If the inference path ever DOES touch them, we'll get
    a clean AttributeError downstream rather than a confusing
    ModuleNotFoundError up front."""
    import types
    import importlib.machinery

    stubs = {}

    def _make_stub(name: str) -> types.ModuleType:
        m = types.ModuleType(name)
        # CRITICAL: Lightning + others probe `module.__spec__` to
        # determine version + capabilities. Without a real ModuleSpec
        # they raise `ValueError: <name>.__spec__ is None`. We give
        # the stub a benign spec with no loader so importlib treats
        # it as installed-but-unused.
        m.__spec__ = importlib.machinery.ModuleSpec(name, loader=None)
        m.__path__ = []
        m.__version__ = "0.0.0-stub"
        return m

    # chamferdist: training-time loss, C++ extension, hard to build
    # on Windows. Stub returns zero loss; inference doesn't call it.
    if "chamferdist" not in sys.modules:
        m = _make_stub("chamferdist")
        class _StubChamferDistance:
            def __init__(self, *a, **kw): pass
            def __call__(self, *a, **kw):
                import torch
                return torch.tensor(0.0), torch.tensor(0.0)
        m.ChamferDistance = _StubChamferDistance
        sys.modules["chamferdist"] = m
        stubs["chamferdist"] = "stubbed"

    # wandb: logging only. Stub everything as no-op.
    if "wandb" not in sys.modules:
        m = _make_stub("wandb")
        m.init = lambda *a, **kw: None
        m.log  = lambda *a, **kw: None
        m.finish = lambda *a, **kw: None
        m.Image = lambda *a, **kw: None
        m.Video = lambda *a, **kw: None
        m.run = None
        m.config = {}
        sys.modules["wandb"] = m
        stubs["wandb"] = "stubbed"

    # trimesh: mesh I/O for some upstream evaluators. Stub minimally.
    if "trimesh" not in sys.modules:
        m = _make_stub("trimesh")
        m.load = lambda *a, **kw: None
        m.Trimesh = type("Trimesh", (), {"__init__": lambda self, *a, **kw: None})
        sys.modules["trimesh"] = m
        stubs["trimesh"] = "stubbed"

    # torch_cluster: PyTorch C++ extension for FPS / kNN. No Windows
    # wheel for PyTorch 2.11. We stub `fps` with a pure-PyTorch
    # implementation -- O(K*N) but functional.
    if "torch_cluster" not in sys.modules:
        import torch as _t
        m = _make_stub("torch_cluster")

        def _fps_single(x, k, random_start):
            N = x.shape[0]
            if k >= N:
                return _t.arange(N, device=x.device)
            sel = _t.zeros(k, dtype=_t.long, device=x.device)
            sel[0] = _t.randint(N, (1,)).item() if random_start else 0
            dists = _t.full((N,), float("inf"), device=x.device)
            for i in range(1, k):
                d = (x - x[sel[i - 1]]).pow(2).sum(-1)
                dists = _t.minimum(dists, d)
                sel[i] = int(dists.argmax().item())
            return sel

        def fps(x, batch=None, ratio=0.5, random_start=False):
            """Farthest-point sampling. Returns indices into x[N, D]
            (or x[B*N, D] when batched) of the selected points."""
            ratio_f = float(ratio) if not _t.is_tensor(ratio) else float(ratio.item())
            if batch is None:
                k = max(1, int(round(x.shape[0] * ratio_f)))
                return _fps_single(x, k, random_start)
            out = []
            for b in batch.unique():
                local_idx = (batch == b).nonzero(as_tuple=True)[0]
                x_b = x[local_idx]
                k_b = max(1, int(round(x_b.shape[0] * ratio_f)))
                sel = _fps_single(x_b, k_b, random_start)
                out.append(local_idx[sel])
            return _t.cat(out)

        def knn(x, y, k, batch_x=None, batch_y=None, **_):
            """k-NN: for each query point in y, find k nearest in x.
            Returns (target_idx, source_idx) pairs as per the
            torch-cluster API."""
            d = (y.unsqueeze(1) - x.unsqueeze(0)).pow(2).sum(-1)  # [Ny, Nx]
            _, idx = d.topk(k, dim=-1, largest=False)
            row = _t.arange(y.shape[0], device=y.device).unsqueeze(1).expand_as(idx).reshape(-1)
            col = idx.reshape(-1)
            return _t.stack([row, col], dim=0)

        m.fps = fps
        m.knn = knn
        m.knn_graph = knn
        sys.modules["torch_cluster"] = m
        stubs["torch_cluster"] = "stubbed (pure-PyTorch fps + knn)"

    # pytorch3d.transforms: quaternion math + Euler angles. Stub with
    # standard formulas (pytorch3d convention: quaternion is (w,x,y,z)).
    if "pytorch3d" not in sys.modules:
        import torch as _t
        pt3d = _make_stub("pytorch3d")
        tr = _make_stub("pytorch3d.transforms")

        def quaternion_to_matrix(q):
            """q: (..., 4) in (w, x, y, z) order. Returns (..., 3, 3)."""
            w, x, y, z = q.unbind(-1)
            two = 2.0
            R = _t.stack([
                _t.stack([1 - two * (y*y + z*z), two * (x*y - z*w), two * (x*z + y*w)], dim=-1),
                _t.stack([two * (x*y + z*w), 1 - two * (x*x + z*z), two * (y*z - x*w)], dim=-1),
                _t.stack([two * (x*z - y*w), two * (y*z + x*w), 1 - two * (x*x + y*y)], dim=-1),
            ], dim=-2)
            return R

        def quaternion_apply(q, p):
            """Rotate point p by quaternion q. q: (..., 4) (w,x,y,z),
            p: (..., 3). Result: (..., 3)."""
            R = quaternion_to_matrix(q)
            # broadcast matmul along last two dims
            return (R @ p.unsqueeze(-1)).squeeze(-1)

        def matrix_to_quaternion(R):
            """Standard inverse formula (handles trace and per-axis cases)."""
            m00, m01, m02 = R[..., 0, 0], R[..., 0, 1], R[..., 0, 2]
            m10, m11, m12 = R[..., 1, 0], R[..., 1, 1], R[..., 1, 2]
            m20, m21, m22 = R[..., 2, 0], R[..., 2, 1], R[..., 2, 2]
            trace = m00 + m11 + m22
            t1 = 1.0 + trace
            s = (t1.clamp_min(1e-10).sqrt()) * 2.0
            w = 0.25 * s
            x = (m21 - m12) / s
            y = (m02 - m20) / s
            z = (m10 - m01) / s
            return _t.stack([w, x, y, z], dim=-1)

        def matrix_to_euler_angles(R, convention="XYZ"):
            """Tait-Bryan XYZ extrinsic. Returns (..., 3)."""
            if convention != "XYZ":
                # All other conventions deferred to formal impl; not used by inference path.
                raise NotImplementedError(f"convention {convention} not stubbed")
            sy = R[..., 0, 2]
            sy_clamped = sy.clamp(-1.0 + 1e-7, 1.0 - 1e-7)
            ry = sy_clamped.asin()
            cy = ry.cos()
            rx = _t.atan2(-R[..., 1, 2] / cy, R[..., 2, 2] / cy)
            rz = _t.atan2(-R[..., 0, 1] / cy, R[..., 0, 0] / cy)
            return _t.stack([rx, ry, rz], dim=-1)

        def random_quaternions(n, **_):
            q = _t.randn(n, 4)
            return q / q.norm(dim=-1, keepdim=True).clamp_min(1e-8)

        tr.quaternion_to_matrix = quaternion_to_matrix
        tr.quaternion_apply = quaternion_apply
        tr.matrix_to_quaternion = matrix_to_quaternion
        tr.matrix_to_euler_angles = matrix_to_euler_angles
        tr.random_quaternions = random_quaternions
        pt3d.transforms = tr
        sys.modules["pytorch3d"] = pt3d
        sys.modules["pytorch3d.transforms"] = tr
        stubs["pytorch3d"] = "stubbed (quaternion + euler math)"

    return stubs


def _patch_torch_load_weights_only():
    """PyTorch 2.6 flipped the default of `torch.load(..., weights_only=True)`.
    The upstream PuzzleFusion++ checkpoints contain a pickled
    `omegaconf.dictconfig.DictConfig` under the Lightning hyperparameters
    key, which torch.load(weights_only=True) refuses to unpickle. We
    are loading our OWN previously-downloaded weights -- treat them
    as trusted. Two fixes layered:

      1. Add the OmegaConf classes to torch.serialization safe-globals
         (the recommended approach for known-trusted types).
      2. Monkey-patch torch.load to default to weights_only=False --
         the fallback that always works.
    """
    import torch
    try:
        import torch.serialization as _ts
        from omegaconf import DictConfig, ListConfig
        from omegaconf.base import ContainerMetadata, Metadata
        from omegaconf.nodes import AnyNode
        _ts.add_safe_globals([
            DictConfig, ListConfig, ContainerMetadata, Metadata, AnyNode,
        ])
    except Exception:
        pass
    _orig = torch.load
    def _wrap(*args, **kwargs):
        if "weights_only" not in kwargs:
            kwargs["weights_only"] = False
        return _orig(*args, **kwargs)
    torch.load = _wrap


def _load_yaml(path: Path):
    """Load a YAML config via OmegaConf -- returns a proper DictConfig
    whose nested `_target_` keys Hydra can instantiate."""
    from omegaconf import OmegaConf
    return OmegaConf.load(str(path))


def _merge_yaml(*paths: Path):
    """Merge multiple YAML files into one DictConfig (later files
    override earlier ones)."""
    from omegaconf import OmegaConf
    base = None
    for p in paths:
        if not p.exists():
            continue
        cfg = OmegaConf.load(str(p))
        base = cfg if base is None else OmegaConf.merge(base, cfg)
    return base


def _find_ckpts(ckpt_root: Path) -> dict:
    """Recursively find every *.ckpt under ckpt_root and bucket by
    filename keyword. Returns dict with keys 'ae', 'denoiser',
    'verifier' -> first-matching Path or None."""
    if not ckpt_root.exists():
        return {"ae": None, "denoiser": None, "verifier": None}
    all_ckpts = list(ckpt_root.rglob("*.ckpt"))
    print(f"  found {len(all_ckpts)} .ckpt files under {ckpt_root}:")
    for p in all_ckpts:
        print(f"     - {p.relative_to(ckpt_root)}")
    def _match(keywords):
        # Check the full relative path (lowered) -- the model identity
        # usually shows up in the PARENT folder name (autoencoder/,
        # denoiser/, verifier/), not the filename which is typically
        # last.ckpt or epoch_NNNN.ckpt.
        for p in all_ckpts:
            hay = str(p.relative_to(ckpt_root)).lower().replace("\\", "/")
            for kw in keywords:
                if kw in hay:
                    return p
        return None
    return {
        "ae":       _match(["autoencoder", "ae_", "_ae", "vqvae", "vq_vae", "fracture_ae"]),
        "denoiser": _match(["denoiser", "diffusion"]),
        "verifier": _match(["verifier", "verify"]),
    }


def try_load_upstream(upstream: Path, ckpt_root: Path):
    """Return (encoder, denoiser, verifier) or (None, None, None) if
    the upstream environment isn't importable. Each is set to .eval()
    with no_grad already turned on by the caller."""
    sys.path.insert(0, str(upstream.resolve()))

    stubbed = _install_stubs()
    if stubbed:
        print(f"  stubbed modules: {', '.join(stubbed.keys())}")

    _patch_torch_load_weights_only()
    print("  patched torch.load: weights_only=False default + omegaconf safe-globals")

    encoder = denoiser = verifier = None
    diag = []
    ckpt_map = _find_ckpts(ckpt_root)

    # Locate the upstream's YAML configs. These are at <upstream>/config/.
    config_root = upstream / "config"
    if not config_root.exists():
        diag.append(f"upstream config/ not found at {config_root}")
        print(f"  warning: upstream config/ missing -> Hydra instantiate will fail")

    try:
        from omegaconf import OmegaConf
    except ImportError:
        diag.append("omegaconf missing: pip install omegaconf")
        OmegaConf = None  # noqa

    # ---- Encoder (FractureAE wrapping VQVAE wrapping PN2)
    try:
        from puzzlefusion_plusplus.vqvae.model.fracture_ae import FractureAE  # noqa
        # Load actual upstream YAMLs so the nested `_target_` keys are
        # proper DictConfig nodes that Hydra can instantiate.
        ae_cfg = _load_yaml(config_root / "ae" / "vq_vae.yaml")
        ae_ckpt = ckpt_map.get("ae")
        if ae_ckpt is not None:
            try:
                encoder = FractureAE.load_from_checkpoint(str(ae_ckpt), strict=False)
            except Exception:
                encoder = FractureAE.load_from_checkpoint(
                    str(ae_ckpt), strict=False, cfg=ae_cfg,
                )
            encoder.eval()
            print(f"  loaded encoder from {ae_ckpt.name}")
        else:
            diag.append(f"encoder ckpt not found under {ckpt_root} (looked for *autoencoder*, *vqvae*, *fracture_ae*)")
    except Exception as e:  # noqa: BLE001
        diag.append(f"encoder load failed: {e}")
        traceback.print_exc(limit=2)

    # ---- Denoiser
    try:
        from puzzlefusion_plusplus.denoiser.model.denoiser import Denoiser  # noqa
        # Denoiser needs model + encoder + global config sections.
        # Merge all the YAMLs the upstream's main training config
        # would have pulled in.
        den_cfg = _merge_yaml(
            config_root / "ae" / "vq_vae.yaml",
            config_root / "denoiser" / "model.yaml",
            config_root / "denoiser" / "encoder.yaml",
            config_root / "denoiser" / "global_config.yaml",
        )
        den_ckpt = ckpt_map.get("denoiser")
        if den_ckpt is not None:
            try:
                denoiser = Denoiser.load_from_checkpoint(str(den_ckpt), strict=False)
            except Exception:
                denoiser = Denoiser.load_from_checkpoint(
                    str(den_ckpt), strict=False, cfg=den_cfg,
                )
            denoiser.eval()
            print(f"  loaded denoiser from {den_ckpt.name}")
        else:
            diag.append(f"denoiser ckpt not found under {ckpt_root} (looked for *denoiser*, *diffusion*)")
    except Exception as e:  # noqa: BLE001
        diag.append(f"denoiser load failed: {e}")
        traceback.print_exc(limit=2)

    # ---- Verifier
    try:
        from puzzlefusion_plusplus.verifier.model.verifier import Verifier  # noqa
        ver_cfg = _merge_yaml(
            config_root / "verifier" / "model.yaml",
            config_root / "verifier" / "global_config.yaml",
        )
        ver_ckpt = ckpt_map.get("verifier")
        if ver_ckpt is not None:
            # Skip the LightningModule wrapper entirely: instantiate the
            # inner VerifierTransformer directly with our YAML cfg, then
            # load just the matching state_dict keys. The Lightning
            # wrapper saved cfg via `save_hyperparameters(cfg)` which
            # baked in a `${hydra:project_root_path}` interpolation
            # that OmegaConf cannot resolve without the full Hydra
            # runtime. Bypassing the wrapper sidesteps that.
            try:
                import torch
                from puzzlefusion_plusplus.verifier.model.modules.verifier_transformer import (
                    VerifierTransformer,
                )
                verifier_core = VerifierTransformer(ver_cfg)
                ckpt = torch.load(str(ver_ckpt), map_location="cpu", weights_only=False)
                state_dict = ckpt.get("state_dict", ckpt)
                # Strip the `verifier.` prefix that the LightningModule
                # added (Verifier.__init__ does `self.verifier = VerifierTransformer(cfg)`).
                prefix = "verifier."
                inner_state = {
                    k[len(prefix):]: v for k, v in state_dict.items()
                    if k.startswith(prefix)
                }
                missing, unexpected = verifier_core.load_state_dict(inner_state, strict=False)
                verifier_core.eval()
                # Expose under .model so the capture path can find it.
                verifier = type("VerifierWrapper", (), {})()
                verifier.model = verifier_core
                print(f"  loaded verifier from {ver_ckpt.name} (direct VerifierTransformer; "
                      f"{len(missing)} missing, {len(unexpected)} unexpected keys)")
            except Exception as e:
                diag.append(f"verifier load failed: {e}")
                traceback.print_exc(limit=2)
        else:
            diag.append(f"verifier ckpt not found under {ckpt_root} (looked for *verifier*, *verify*)")
    except Exception as e:  # noqa: BLE001
        diag.append(f"verifier load failed: {e}")
        traceback.print_exc(limit=2)

    if diag:
        print("\nWarnings:")
        for d in diag:
            print("  -", d)
    return encoder, denoiser, verifier


# ---------------------------------------------------------------------------
# Capture pipelines.
# ---------------------------------------------------------------------------

@torch.no_grad()
def capture_encoder(encoder, pts_bnc: torch.Tensor) -> dict[str, torch.Tensor]:
    """Capture by calling sub-modules DIRECTLY. We sidestep the upstream
    LightningModule.forward (which expects a training-time data_dict
    with many keys) and instead invoke PN2.sa1, sa2, sa3 in sequence
    with raw tensors. Hooks still fire on each call.

    Upstream PN2 internal structure (from vqvae/model/modules/pn2.py):
        self.sa1 = PointNetSetAbstraction(npoint=256, in_channel=3, ...)
        self.sa2 = PointNetSetAbstraction(npoint=128, in_channel=128+3, ...)
        self.sa3 = PointNetSetAbstraction(npoint=25,  in_channel=256+3, ...)
        sa{i}.forward(xyz [B,3,N], points [B,D,N] or None)
            -> (new_xyz [B,3,npoint], new_points [B,D_out,npoint])
    """
    out = {}
    if encoder is None:
        return out

    # Walk to the PN2 module. FractureAE.ae is VQVAE; VQVAE.pn2 is PN2.
    pn2 = None
    try:
        # Most-common path per upstream code.
        pn2 = encoder.ae.pn2
    except AttributeError:
        pass
    if pn2 is None:
        for attr in ("encoder", "pn2", "model"):
            try:
                cand = getattr(encoder, attr, None)
                if cand is None:
                    continue
                if hasattr(cand, "sa1"):
                    pn2 = cand
                    break
                if hasattr(cand, "pn2") and hasattr(cand.pn2, "sa1"):
                    pn2 = cand.pn2
                    break
            except Exception:
                pass
    if pn2 is None:
        print("  encoder: could not locate PN2 sub-module")
        return out

    pts_bcn = pts_bnc.permute(0, 2, 1).contiguous()  # [B=1, 3, N=1000]
    try:
        sa1_xyz, sa1_pts = pn2.sa1(pts_bcn, None)
        out["parity.encoder.sa1_xyz"] = sa1_xyz.detach().cpu().contiguous()
        out["parity.encoder.sa1_output.tuple1"] = sa1_pts.detach().cpu().contiguous()
        sa2_xyz, sa2_pts = pn2.sa2(sa1_xyz, sa1_pts)
        out["parity.encoder.sa2_xyz"] = sa2_xyz.detach().cpu().contiguous()
        out["parity.encoder.sa2_output.tuple1"] = sa2_pts.detach().cpu().contiguous()
        sa3_xyz, sa3_pts = pn2.sa3(sa2_xyz, sa2_pts)
        out["parity.encoder.sa3_xyz"] = sa3_xyz.detach().cpu().contiguous()
        out["parity.encoder.sa3_output.tuple1"] = sa3_pts.detach().cpu().contiguous()
        # Final compressed features via conv6.
        if hasattr(pn2, "conv6"):
            feat = pn2.conv6(sa3_pts)
            out["parity.encoder.final_features"] = feat.detach().cpu().contiguous()
        print(f"  encoder captured: sa1={sa1_pts.shape}, sa2={sa2_pts.shape}, sa3={sa3_pts.shape}")
    except Exception as e:  # noqa: BLE001
        print(f"  encoder forward failed: {e}")
        traceback.print_exc(limit=2)
    return out


@torch.no_grad()
def capture_denoiser_v2_with_real_encoder_input(
    encoder, denoiser, pts_bnc: torch.Tensor, t_value: float, seed: int = 42
) -> dict[str, torch.Tensor]:
    """RE-CAPTURE v2: feed the REAL encoder output (post conv6 + VQ
    quantisation) as the denoiser's latent input, matching the C#
    Mode=Port inference convention EXACTLY. This is the parity baseline
    for tightening the C# denoiser primitives.

    Inputs:
       encoder:   loaded upstream VQVAE-wrapped FractureAE
       denoiser:  loaded upstream Denoiser LightningModule
       pts_bnc:   point cloud [B=1, N=1000, 3] channels-last
       t_value:   the timestep at which to capture (e.g. 500)
       seed:      RNG seed for the noisy-poses initialisation

    Saves under 'parity.denoiser_v2.*':
       input.noisy_poses [N_MAX, 7]      -- exact poses C# will see
       input.latent      [N_MAX, L, D]   -- VQ-quantised encoder output
       input.xyz         [N_MAX, L, 3]   -- SA3 keypoint positions
       input.part_valids [N_MAX]
       input.scale       [N_MAX, 1]
       input.ref_part    [N_MAX]         -- boolean mask, 1 at anchor
       input.timestep    [1]
       layer0_output ... layer5_output   [N_MAX*L, D]
       final_residual                    [N_MAX, 7]
    """
    out = {}
    if denoiser is None or encoder is None:
        return out
    # Locate inner modules.
    core = None
    for attr in ("denoiser", "model"):
        cand = getattr(denoiser, attr, None)
        if cand is not None and hasattr(cand, "transformer_layers"):
            core = cand
            break
    if core is None:
        print("  denoiser_v2: could not locate transformer_layers")
        return out
    # Locate VQVAE encoder wrapper (FractureAE.ae = VQVAE).
    vqvae = encoder.ae if hasattr(encoder, "ae") else encoder
    if not hasattr(vqvae, "encode"):
        print("  denoiser_v2: could not locate vqvae.encode")
        return out

    N_MAX = 20
    # N_real = 2 fails on PN2's batch-aware ops (encoder isn't a clean
    # B>1 path with this torch_cluster stub). Run B=1 forward per
    # fragment and stack -- matches what the C# port does anyway.
    N_real = 2

    # Build deterministic noisy_poses (matches what C# would feed).
    g = torch.Generator()
    g.manual_seed(seed)
    noisy_poses = torch.randn((1, N_MAX, 7), generator=g)
    noisy_poses[0, 0] = torch.tensor([0., 0., 0., 1., 0., 0., 0.])

    part_valids = torch.zeros((1, N_MAX))
    part_valids[0, :N_real] = 1.0
    scale = torch.ones((1, N_MAX, 1))
    ref_part = torch.zeros((1, N_MAX), dtype=torch.bool)
    ref_part[0, 0] = True

    # Run the encoder ONCE PER FRAGMENT (B=1) and stack the results.
    # This matches the C# port loop exactly.
    try:
        from pytorch3d import transforms as t3d
        z_q_list = []
        xyz_kp_list = []
        for f in range(N_real):
            quat_f = noisy_poses[0, f, 3:]
            quat_f = quat_f / quat_f.norm().clamp_min(1e-8)
            # Rotate the (single) point cloud by this quaternion.
            pts_rot = t3d.quaternion_apply(quat_f, pts_bnc[0])  # [N_pts, 3]
            # vqvae.encode expects [B, N, 3] channels-LAST (it does
            # x = part_pcs.permute(0, 2, 1) internally, expecting that to
            # produce [B, C, N] for the SA layers).
            pts_in = pts_rot.unsqueeze(0).contiguous()  # [1, N, 3]
            enc_out = vqvae.encode(pts_in)
            z_q_list.append(enc_out["z_q"][0].detach())     # [L, D]
            xyz_kp_list.append(enc_out["xyz"][0].detach())   # [L, 3]
        z_q = torch.stack(z_q_list, dim=0)        # [N_real, L, D]
        xyz_kp = torch.stack(xyz_kp_list, dim=0)  # [N_real, L, 3]
    except Exception as e:
        print(f"  denoiser_v2: per-fragment encode failed: {e}")
        traceback.print_exc(limit=2)
        return out

    L = z_q.shape[1]
    D = z_q.shape[2]
    latent = torch.zeros((1, N_MAX, L, D))
    xyz = torch.zeros((1, N_MAX, L, 3))
    latent[0, :N_real] = z_q
    xyz[0, :N_real] = xyz_kp

    # Save inputs.
    out["parity.denoiser_v2.input.noisy_poses"] = noisy_poses[0].detach().cpu()
    out["parity.denoiser_v2.input.latent"] = latent[0].detach().cpu()
    out["parity.denoiser_v2.input.xyz"] = xyz[0].detach().cpu()
    out["parity.denoiser_v2.input.part_valids"] = part_valids[0].detach().cpu()
    out["parity.denoiser_v2.input.scale"] = scale[0].detach().cpu()
    out["parity.denoiser_v2.input.ref_part"] = ref_part[0].to(torch.float32).cpu()
    out["parity.denoiser_v2.input.timestep"] = torch.tensor([float(t_value)])

    # Hook per-layer outputs + PRE-LAYER-0 hook to capture data_emb
    # right before the first transformer layer. This isolates whether
    # the upstream-vs-port divergence is in the pre-layer compute
    # (NeRF embed + shape_embedding + param_fc + ref_part_emb + PE) or
    # in the transformer layers themselves.
    hc = HookCollector()
    pre_layer_capture = {}
    # ---- Intra-layer-0 sub-block capture via monkey-patch.
    # Replace EncoderLayer.forward on layer 0 with an instrumented
    # version that captures (a) after norm1, (b) after self_attn,
    # (c) after residual1, (d) after norm2, (e) after global_attn,
    # (f) after residual2, (g) after norm3, (h) after ff, (i) final.
    intra_capture = {}
    orig_forward = type(core.transformer_layers[0]).forward
    def _instrumented_forward(self, hidden_states, self_mask, gen_mask, timestep):
        intra_capture["L0_pre"] = hidden_states.detach().cpu().clone()
        norm_hidden_states = self.norm1(hidden_states, timestep)
        intra_capture["L0_after_norm1"] = norm_hidden_states.detach().cpu().clone()
        attn_output = self.self_attn(norm_hidden_states, attention_mask=self_mask)
        intra_capture["L0_after_self_attn"] = attn_output.detach().cpu().clone()
        hidden_states = hidden_states + attn_output
        intra_capture["L0_after_resid1"] = hidden_states.detach().cpu().clone()
        norm_hidden_states = self.norm2(hidden_states, timestep)
        intra_capture["L0_after_norm2"] = norm_hidden_states.detach().cpu().clone()
        global_out = self.global_attn(norm_hidden_states, attention_mask=gen_mask)
        intra_capture["L0_after_global_attn"] = global_out.detach().cpu().clone()
        hidden_states = hidden_states + global_out
        intra_capture["L0_after_resid2"] = hidden_states.detach().cpu().clone()
        norm_hidden_states = self.norm3(hidden_states)
        intra_capture["L0_after_norm3"] = norm_hidden_states.detach().cpu().clone()
        ff_output = self.ff(norm_hidden_states)
        intra_capture["L0_after_ff"] = ff_output.detach().cpu().clone()
        hidden_states = ff_output + hidden_states
        return hidden_states
    # Bind on the instance only (not the class), so we don't pollute
    # other layers.
    layer0 = core.transformer_layers[0]
    import types as _types
    layer0.forward = _types.MethodType(_instrumented_forward, layer0)
    def _pre_hook(module, args, kwargs):
        # Upstream calls layers with KEYWORD args, so we must use
        # with_kwargs=True for the hook to receive them.
        hs = None
        if "hidden_states" in kwargs:
            hs = kwargs["hidden_states"]
        elif len(args) >= 1 and torch.is_tensor(args[0]):
            hs = args[0]
        if torch.is_tensor(hs):
            pre_layer_capture["pre_layer0"] = hs.detach().cpu().contiguous()
    h_pre = core.transformer_layers[0].register_forward_pre_hook(_pre_hook, with_kwargs=True)
    for i, blk in enumerate(core.transformer_layers):
        hc.hook(blk, f"parity.denoiser_v2.layer{i}_output")

    # Forward.
    timesteps = torch.tensor([int(t_value)])
    try:
        residuals = core(noisy_poses, timesteps, latent, xyz, part_valids, scale, ref_part)
        out["parity.denoiser_v2.final_residual"] = residuals[0].detach().cpu()
    except Exception as e:
        print(f"  denoiser_v2: forward failed: {e}")
        traceback.print_exc(limit=2)
    if "pre_layer0" in pre_layer_capture:
        out["parity.denoiser_v2.pre_layer0_input"] = pre_layer_capture["pre_layer0"]
    for k, v in intra_capture.items():
        out[f"parity.denoiser_v2.intra_{k}"] = v
    h_pre.remove()
    # Restore the unpatched forward so subsequent calls don't keep
    # capturing. The class-level method is intact.
    if hasattr(layer0, "forward"):
        del layer0.forward

    hc.remove_all()
    out.update(hc.captured)
    print(f"  denoiser_v2: captured {sum(1 for k in out if k.startswith('parity.denoiser_v2.layer'))} layers + final residual")
    return out


@torch.no_grad()
def capture_denoiser(denoiser, encoder_features: torch.Tensor,
                     noisy_poses: torch.Tensor, t_value: float) -> dict[str, torch.Tensor]:
    """Capture per-transformer-layer outputs from the denoiser by
    hooking transformer_layers[i] then driving with a forward call.

    For maximum robustness we try a few input-shape conventions
    (upstream uses N_PART=20 max with a part_valids mask). If a top-
    level forward errors, we still get whatever the hooks captured
    before the crash."""
    out = {}
    if denoiser is None:
        return out

    # Locate the inner DenoiserTransformer.
    core = None
    for attr in ("denoiser", "model"):
        cand = getattr(denoiser, attr, None)
        if cand is not None and hasattr(cand, "transformer_layers"):
            core = cand
            break
    if core is None:
        print("  denoiser: could not locate transformer_layers")
        return out

    hc = HookCollector()
    for i, blk in enumerate(core.transformer_layers):
        hc.hook(blk, f"parity.denoiser.layer{i}_output")
    if hasattr(core, "final_layer"):
        hc.hook(core.final_layer, "parity.denoiser.final_residual")
    elif hasattr(core, "out_proj"):
        hc.hook(core.out_proj, "parity.denoiser.final_residual")

    # Upstream's denoiser was trained with N_PART=20 max. We pad our
    # 8 noisy poses to 20 with valids mask.
    N_MAX = 20
    N_real = noisy_poses.shape[0]
    poses_padded = torch.zeros((1, N_MAX, 7))
    poses_padded[0, :N_real] = noisy_poses
    part_valids = torch.zeros((1, N_MAX))
    part_valids[0, :N_real] = 1.0
    scale = torch.ones((1, N_MAX, 1))
    ref_part = torch.zeros((1,), dtype=torch.long)
    timesteps = torch.tensor([int(t_value)])

    # latent must be [B, N, L=25, D=64]. Our encoder_features came from
    # `pn2.conv6(sa3_pts)` which has CHANNELS-FIRST shape [1, 64, 25].
    # The upstream denoiser expects CHANNELS-LAST [B, N, L, D]. So we
    # transpose (..., 64, 25) -> (..., 25, 64) then tile across the
    # N (parts) dimension.
    if encoder_features is not None:
        ef = encoder_features
        # Squeeze leading singleton if present so shape becomes [C, L] or [L, C].
        if ef.dim() == 3 and ef.shape[0] == 1:
            ef = ef.squeeze(0)
        # Now ef should be 2-D. If channels-first [64, 25] -> transpose.
        if ef.dim() == 2:
            C0, C1 = ef.shape
            if C0 == 64 and C1 == 25:
                ef = ef.transpose(0, 1)  # -> [25, 64]
        # Final per-part feature is [L=25, D=64].
        if ef.dim() == 2 and ef.shape[0] == 25 and ef.shape[1] == 64:
            latent = ef.unsqueeze(0).unsqueeze(0).expand(1, N_MAX, 25, 64).contiguous()
        else:
            latent = torch.zeros((1, N_MAX, 25, 64))
    else:
        latent = torch.zeros((1, N_MAX, 25, 64))
    xyz_kp = torch.zeros((1, N_MAX, 25, 3))

    # ref_part is a per-part BOOLEAN mask of shape [B, N], True at the
    # one anchor. (Originally I had a scalar long index; the upstream
    # _add_ref_part_emb does `ref_part.to(torch.bool)` and uses it for
    # tensor indexing, so it must be the right shape.)
    ref_part = torch.zeros((1, N_MAX), dtype=torch.bool)
    ref_part[0, 0] = True

    try:
        _ = core(poses_padded, timesteps, latent, xyz_kp, part_valids, scale, ref_part)
    except Exception as e:  # noqa: BLE001
        print(f"  denoiser forward failed: {e}")
        traceback.print_exc(limit=2)

    hc.remove_all()
    out.update(hc.captured)
    if out:
        print(f"  denoiser captured {len(out)} layer tensors")
    return out


@torch.no_grad()
def capture_verifier_v2_with_hooks(verifier, seed: int = 99) -> dict[str, torch.Tensor]:
    """V2 verifier capture: hook every layer of the
    transformer_encoder + mlp_out head, using a deterministic
    seed-controlled edge feature input matching what the C# port
    will feed.

    Returns dict with:
      parity.verifier_v2.input.edge_features [E=3, 7]
      parity.verifier_v2.input.edge_indices  [E, 2]
      parity.verifier_v2.input.valid_mask    [E]
      parity.verifier_v2.layer{i}_output     [B=1, E, D] for i in 0..5
      parity.verifier_v2.logits              [B=1, E, 1]
    """
    out = {}
    if verifier is None: return out
    core = getattr(verifier, "model", None) or verifier
    if not hasattr(core, "transformer_encoder"):
        print("  verifier_v2: no transformer_encoder"); return out
    # Deterministic edge features.
    g = torch.Generator()
    g.manual_seed(seed)
    E = 3
    edge_features = torch.randn((1, E, 7), generator=g)
    edge_indices = torch.tensor([[[0, 1], [0, 2], [1, 2]]], dtype=torch.long)
    valid_mask = torch.ones((1, E))
    out["parity.verifier_v2.input.edge_features"] = edge_features[0].detach().cpu()
    out["parity.verifier_v2.input.edge_indices"] = edge_indices[0].to(torch.float32).cpu()
    out["parity.verifier_v2.input.valid_mask"] = valid_mask[0].detach().cpu()
    # Hook each transformer encoder layer.
    hc = HookCollector()
    for i, blk in enumerate(core.transformer_encoder.layers):
        hc.hook(blk, f"parity.verifier_v2.layer{i}_output")
    if hasattr(core, "mlp_out"):
        hc.hook(core.mlp_out, "parity.verifier_v2.logits")
    try:
        _ = core(edge_features, edge_indices, valid_mask)
        print(f"  verifier_v2: captured {sum(1 for k in out if k.startswith('parity.verifier_v2.layer'))} layers")
    except Exception as e:
        print(f"  verifier_v2: forward failed: {e}")
        traceback.print_exc(limit=2)
    hc.remove_all()
    out.update(hc.captured)
    return out


@torch.no_grad()
def capture_verifier(verifier, pair_features: torch.Tensor) -> dict[str, torch.Tensor]:
    out = {}
    if verifier is None:
        return out
    hc = HookCollector()
    try:
        core = verifier.model if hasattr(verifier, "model") else verifier
        # Per Explore agent: VerifierTransformer wraps nn.TransformerEncoder.
        if hasattr(core, "transformer_encoder"):
            te = core.transformer_encoder
            if hasattr(te, "layers"):
                for i, blk in enumerate(te.layers):
                    hc.hook(blk, f"parity.verifier.layer{i}_output")
            hc.hook(te, "parity.verifier.transformer_output")
        if hasattr(core, "mlp_out"):
            hc.hook(core.mlp_out, "parity.verifier.logit")
        elif hasattr(core, "head"):
            hc.hook(core.head, "parity.verifier.logit")
    except Exception as e:  # noqa: BLE001
        print(f"  verifier hook setup partial: {e}")

    try:
        # Upstream VerifierTransformer.forward(edge_features, edge_indices, mask).
        # pair_features already shape [1, 1, 7] from build_deterministic_inputs.
        edge_idx = torch.tensor([[[0, 1]]], dtype=torch.long)
        edge_val = torch.ones((1, 1))
        if hasattr(verifier, "model"):
            _ = verifier.model(pair_features, edge_idx, edge_val)
        else:
            _ = verifier(pair_features, edge_idx, edge_val)
    except Exception as e:  # noqa: BLE001
        print(f"  verifier forward failed: {e}")
        traceback.print_exc(limit=2)

    hc.remove_all()
    out.update(hc.captured)
    if "parity.verifier.logit" in out:
        out["parity.verifier.score"] = torch.sigmoid(out["parity.verifier.logit"])
    return out


# ---------------------------------------------------------------------------
# Entry point.
# ---------------------------------------------------------------------------

def main():
    p = argparse.ArgumentParser()
    p.add_argument("--upstream", type=Path, default=Path("./puzzlefusion-plusplus"),
                   help="Path to cloned upstream repo (with the puzzlefusion_plusplus package inside).")
    p.add_argument("--ckpt-root", type=Path, default=None,
                   help="Path to the extracted upstream output/ directory containing .ckpt files. "
                        "If omitted, only input fixtures are written.")
    p.add_argument("--out", type=Path,
                   default=Path(r"D:\code_ws\Template-General\outputs\2026-05-22\reference\parity_fixtures.bin"),
                   help="Output fixture binary path.")
    p.add_argument("--root", type=Path, default=None,
                   help="(Deprecated alias of --ckpt-root.)")
    p.add_argument("--seed", type=int, default=SEED)
    args = p.parse_args()
    if args.root is not None and args.ckpt_root is None:
        args.ckpt_root = args.root

    fixtures = build_deterministic_inputs()
    print(f"deterministic input fixtures built ({len(fixtures)} tensors).")

    encoder = denoiser = verifier = None
    if args.ckpt_root is not None:
        if not args.upstream.exists():
            print(f"WARNING: upstream repo not found at {args.upstream}; skipping reference capture.")
        else:
            print(f"\nattempting to import upstream models from {args.upstream}")
            print(f"loading checkpoints from {args.ckpt_root}")
            encoder, denoiser, verifier = try_load_upstream(args.upstream, args.ckpt_root)
    else:
        print("--ckpt-root not provided; skipping reference capture (inputs only).")

    print("\ncapturing layer outputs...")
    enc_caps = capture_encoder(encoder, fixtures["parity.input.point_cloud_bnc"])
    fixtures.update(enc_caps)
    # Use the encoder's final features as the denoiser's latent input.
    enc_final = enc_caps.get("parity.encoder.final_features")
    if enc_final is not None and enc_final.dim() == 2:
        enc_final = enc_final.unsqueeze(0)
    den_caps = capture_denoiser(denoiser, enc_final,
                                fixtures["parity.input.noisy_poses"],
                                fixtures["parity.input.timestep"].item())
    fixtures.update(den_caps)
    # V2 capture: feed REAL encoder output (post conv6 + VQ) as the
    # denoiser's latent input -- this is what the C# Mode=Port inference
    # actually feeds. Used to bisect layer-by-layer parity drift in the
    # denoiser.
    den_v2_caps = capture_denoiser_v2_with_real_encoder_input(
        encoder, denoiser,
        fixtures["parity.input.point_cloud_bnc"],
        t_value=500.0, seed=42,
    )
    fixtures.update(den_v2_caps)
    ver_caps = capture_verifier(verifier, fixtures["parity.input.pair_features"])
    fixtures.update(ver_caps)
    ver_v2_caps = capture_verifier_v2_with_hooks(verifier, seed=99)
    fixtures.update(ver_v2_caps)

    n_ref = sum(1 for k in fixtures if k.startswith("parity.encoder.")
                or k.startswith("parity.denoiser.")
                or k.startswith("parity.verifier."))
    print(f"  reference outputs captured: {n_ref} tensors")

    export_fixtures(args.out, fixtures)

    if n_ref == 0:
        print(
            "\nNOTE: zero reference outputs were captured."
            "\nThe C# parity tests will SKIP (not fail) until parity_fixtures.bin"
            "\ncontains parity.encoder.*, parity.denoiser.*, parity.verifier.* tensors."
            "\nSee module docstring for the full capture workflow."
        )
    else:
        print(f"\ndone. Run `cd ../../../tests/Frahan.StonePack.Tests && dotnet run` to validate.")


if __name__ == "__main__":
    main()
