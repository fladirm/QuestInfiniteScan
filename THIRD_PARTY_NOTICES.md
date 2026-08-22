# Third-party notices

QuestInfiniteScan source is distributed under the MIT license in
[`LICENSE.md`](LICENSE.md). The retained Quest shell derives from
[QuestRoomScan](https://github.com/arghyasur1991/QuestRoomScan), MIT licensed,
copyright Arghya Sur / Genesis, and retains selected Unity/Meta XR lifecycle,
capture and operator-UI infrastructure.

Unity, Universal Render Pipeline, AR Foundation, OpenXR, Meta OpenXR, Meta XR SDK,
MR Utility Kit, Burst, Collections and Mathematics are obtained through Unity/Meta
package systems and remain governed by their respective Unity and Meta terms. They
are not relicensed by this repository.

## Validation/build-only dependencies

| Project | Use | License |
|---|---|---|
| [Khronos glTF Validator](https://github.com/KhronosGroup/glTF-Validator) | Official derivative GLB validation | Apache-2.0 |
| [glTF Transform](https://github.com/donmccurdy/glTF-Transform) | Independent derivative GLB importer check | MIT |

Exact npm resolutions are in `Tools/gltf/package-lock.json`. Validation tools and
their caches are not embedded in the Quest APK. No third-party model weights,
server runtime, CUDA package or room capture is part of Σ-PRISM-16.
