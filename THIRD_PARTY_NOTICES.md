# Third-party notices

QuestInfiniteScan source is distributed under the MIT license in [LICENSE.md](LICENSE.md).
The repository derives from and interoperates with the following projects. Their
copyright and license terms remain in force.

## Runtime/source dependencies

| Project | Use | License |
|---|---|---|
| [QuestRoomScan](https://github.com/arghyasur1991/QuestRoomScan) | Upstream Unity mapper, mesher, refinement, persistence, and UI base | MIT, copyright Arghya Sur / Genesis |
| [DiffSoup](https://github.com/kenji-tojo/diffsoup) | Separately installed CUDA optimization worker and artifact/viewer contract | MIT, copyright Kenji Tojo |
| [lasertag](https://github.com/anaglyphs/lasertag) | Architecture inherited through QuestRoomScan | MIT, copyright Julian Triveri and Hazel Roeder |
| [FastAPI](https://github.com/fastapi/fastapi) | Local protocol-v2 HTTP service | MIT |
| [Uvicorn](https://github.com/Kludex/uvicorn) | Local ASGI server | BSD-3-Clause |
| [PyTorch](https://github.com/pytorch/pytorch) | Separately installed DiffSoup tensor/CUDA runtime | BSD-style PyTorch license |

Unity, Universal Render Pipeline, AR Foundation, OpenXR, Meta OpenXR, Meta XR SDK,
MR Utility Kit, Burst, Collections, and Mathematics are obtained through Unity/Meta
package systems and are governed by their respective Unity and Meta terms. They are
not relicensed by this repository.

Optional upstream Gaussian Splatting and AI inference modules remain isolated and
retain the licenses of their original packages and model assets. No third-party model
weights are committed by QuestInfiniteScan.

## Validation/build-only dependencies

| Project | Use | License |
|---|---|---|
| [Khronos glTF Validator](https://github.com/KhronosGroup/glTF-Validator) | Official GLB fixture validation, pinned npm development dependency | Apache-2.0 |
| [glTF Transform](https://github.com/donmccurdy/glTF-Transform) | Independent `NodeIO` consumer check, pinned npm development dependency | MIT |
| pytest | Server test runner | MIT |
| httpx / httpx2 | ASGI/HTTP contract tests | BSD-3-Clause / package-specific terms |

Exact Python and npm resolutions are in `Server/uv.lock` and
`Tools/gltf/package-lock.json`. These tools and their caches are not embedded in the
Quest APK merely because they are used during validation.

If redistributing third-party binaries, Unity/Meta packages, CUDA/PyTorch components,
or model weights, include the license files delivered with those exact artifacts;
this notice is not a substitute for them.
