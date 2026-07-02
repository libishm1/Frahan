# ETH1100 subset (16 closed stones)

Deterministic every-68th sample of the ETH dry-stone dataset's 1100 closed
stone meshes (Zenodo record 10038881), bundled so the stone-fit / stone-library
examples run on any machine. `VaultStoneFitter` / `StoneLibrary` fall back to
this folder automatically when the user-supplied ETH path does not exist;
deploy.ps1 ships it beside the plugin as `data/eth1100_subset`.

For real work point ETH Dir at the full dataset (1100 stones).
