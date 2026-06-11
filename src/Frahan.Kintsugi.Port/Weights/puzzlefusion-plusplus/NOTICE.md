# NOTICE — vendored upstream tree

This directory vendors the upstream PuzzleFusion++ repository
(https://github.com/eric-zqwang/puzzlefusion-plusplus) for weight
conversion and parity-fixture export. Do not edit; it is reference
material.

## Licence of this tree

Per `LICENSE` in this directory: code, data and model weights are
NOT allowed for commercial usage; for research purposes the terms
follow GPLv3 (`LICENSE_GPL`).

## Jigsaw_matching subtree — licence not audited

`Jigsaw_matching/` is a third-party subtree (Jigsaw, Lu et al.)
vendored inside the upstream PuzzleFusion++ repo. It ships its own
`LICENSE` file (MIT, Copyright (c) 2023 Jiaxin Lu), but this claim
has not been audited against the original Jigsaw repository: it is
unverified whether the MIT grant covers every file in the subtree
(datasets, configs, pretrained-model references) or only the code.

Until audited, treat `Jigsaw_matching/` conservatively under the
parent tree's terms: research-use only, non-commercial. Nothing in
`Jigsaw_matching/` is compiled into or shipped with Frahan
StonePack.

Do not delete this subtree; it is kept for provenance.
