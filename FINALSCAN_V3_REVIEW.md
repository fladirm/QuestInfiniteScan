# FINALSCAN v3 — revize kontraktu a návrh úprav

> **Stav 2026-09-14:** přijato jako vstup C00 s korekcemi uživatele (capture profily místo 30↔60 přepínání, clock gate A/B/C, dual path textured/planar, fixed-point 32 B surfel, two-level addressing místo dvou hashmap, render varianta A jako default, Quest 3 / 3S jako jeden HW, platform anchors jako observation, kernel gate provisional, C11b oracle). Autorita je nyní `FINALSCAN_V4_CONTRACT.md`.

Datum: 2026-09-14. Vstupy: kompletní lokální audit `simplescan` (větev `fix/merkaba-runtime-root-causes`), `uniscan` (větev `refactor/m8-dual-sphere-flower-rev-b`, HEAD `9cfed011`), `otherscan` (větev `forensic/n4r-cut-e-scheduler`), device evidence (`m16scanner-device-evidence`, `simplescan-evidence`, `otherscan/evidence`, `scanner/`), a webová rešerše (Meta PCA/Depth/OpenXR docs, Unity IUnityGraphicsVulkan, Adreno 740, SOTA surfel/splat papers 2024–2026).

Tento dokument je vstup pro C00. Nic z něj není implementace.

---

## 0. Jak chápu, co má FinalScan být

Jedna věta, kterou musí každý řez ctít:

**Pomalá, ale správně datovaná evidence (PCA L/R + Environment Depth + pose z doby záběru) aktualizuje explicitní metrický svět z orientovaných surfelů, zatímco 72Hz renderer pouze čte poslední kompletní publikovaný snapshot světa, který je vždy residentní v 360° kouli kolem uživatele.**

Z toho plyne pořadí priorit, které kontrakt správně stanovuje a které potvrzuji:

1. **Readout je nedotknutelný.** 72 Hz, žádné čekání na scan, disk, loop closure ani atlas. Otočení hlavy = pouze culling. Toto je hlavní produktová stížnost na donory (doskakování geometrie, blank tiles, 42 fps) a hlavní acceptance FinalScanu.
2. **Metrika a opakovatelnost před hustotou.** Surfel s kovariancí a precision-weighted fúzí, ne voxel s occupancy counterem. Tenká stěna = dva surfel sheety, roh = dva sheety, ne průměr v buňce.
3. **Vzhled je oddělená vrstva.** Keyframy → SurfaceAtlas → volitelný směrový reziduál. Textura nikdy nenutí geometrii subdividovat.
4. **Neomezený svět** přes AnchorGraph → pages → surfely, residency podle polohy (ne pohledu), disk jako authority.
5. **Feature parity + lepší scan UX** — všechny user-facing funkce SimpleScanu (viz §8 níže seznam, který kontrakt dosud nemá) navázané na stabilní identity.
6. **Vše měřené.** Žádné číslo v architektuře bez device receiptu. Donoři opakovaně tvrdili výkon bez měření (retrakce `7ec07b0`).

Kontrakt v3 tomu odpovídá. Níže je, co je v něm **fakticky špatně**, co **chybí**, a co bych **změnil**.

---

## 1. Faktické opravy (kontrakt tvrdí něco, co evidence vyvrací)

### 1.1 Donoři jsou jedno repo, ne dva workspace

`simplescan`, `uniscan` i `otherscan` jsou tři checkouty **téhož** repa `QuestInfiniteScan` (remote `fladirm/QuestInfiniteScan`), jen na různých větvích. Merge-base simplescan/uniscan je `c34d27f` (2026-09-05); uniscan má od té doby 113 commitů, simplescan 2 docs-only.

- Commit `9cfed011` (kontrakt §32 "SimpleScan commit") je **HEAD větve UniScanu** `refactor/m8-dual-sphere-flower-rev-b`, v simplescan checkoutu neexistuje.
- Nativní Vulkan vrstva není v `Runtime/Plugins`, ale v `Runtime/Telemetry/Native/MerkabaVulkanTimestamps.cpp` (simplescan 2 095 ř., uniscan 3 064 ř.). `.so` se nebuildí do repa, ale do host projektu na `/mnt/kingston-unity`.
- **Třetí donor, který kontrakt vůbec nezmiňuje:** `otherscan` (`Runtime/SigmaPrism`, nativní `SigmaVulkanTimestamps.cpp` 4 154 ř., 2026-08-31 až 09-02). Má dva mechanismy, které uniscan nemá a FinalScan potřebuje: **frame-paced sliced jobs** (`kExecutorSliceCount`, `RecordJobSlice`, `SubmitNextSlice`) a **fallback vkCreateDevice bez injektáže**, když injekce queue selže.

**Úprava §32/§64:** formulovat audit per větev (`fix/merkaba-runtime-root-causes`, `refactor/m8-dual-sphere-flower-rev-b`, `forensic/n4r-cut-e-scheduler`) + host projekt na Kingstonu, a `VULKAN_DONOR_LEDGER.md` musí mít tři sloupce donorů, ne dva.

### 1.2 Zařízení v evidenci je Quest 3S, ne Quest 3

Všechny lokální device runy jsou `model:Quest_3S device:panther`, sériové číslo `340YC20G7X0QZ4`. Quest 3 a 3S sdílí XR2 Gen 2 / Adreno 740, ale liší se displej (3S: 1832×1920, Fresnel), FOV, umístění kamer a tím i PCA extrinsics/baseline.

**Úprava §1/§61:** explicitně pojmenovat cílové zařízení. Buď "Quest 3 i 3S, C01 běží na obou", nebo "Quest 3S je dev device, Quest 3 je release target a C01 se opakuje na něm před C32". Kalibrační a memory čísla nesmí být přenášena mezi nimi bez měření.

### 1.3 PCA není 30 fps strop

- Systémové kamery na zařízení běží 72 fps (`SensorService CameraFPS` id 4/5: 730 snímků/10 s), 50 fps v low-light; `onCameraFpsConfigured frameType=Color nominalRateHz=72`.
- Meta native PCA overview (aktualizace 2026-04-21): *Data rate 60 Hz, capture latency 20–40 ms, GPU overhead ~1–2 %/kamera, memory ~45 MB, max 1280×1280*. MRUK v83 `PassthroughCameraAccess.MaxFramerate` default 60.
- Donoři žádali 1280×960@30 a dostali je; app-side dosažené fps nikdo nezměřil.
- 1280×1280 (1:1, širší FOV) existuje od v83; 1280×960 je crop "menší než to, co uživatel vidí".

**Úprava §2:** "30 FPS" nechat jako **dolní mez, při které musí být design korektní**, ale zároveň vyžadovat korektnost při 60 Hz (ring sizing, stale-drop policy, thermal). Navíc: **záměrně nastavit `MaxFramerate` podle scan stavu** (30 při klidu, 60 jen v DETAIL nebo rychlém pohybu) — je to scheduler input, ne konstanta. §61 doplnit "1280×1280 vs 1280×960: FOV, intrinsics, bandwidth".

### 1.4 L/R pairing: evidence potvrzuje nutnost

SimpleScan log: `deltaLms/deltaRms` kvantované na 0 nebo ±20 ms (= jedna perioda 50Hz kamery), `expiredDepth` ~10 % párů, 46 % acceptance při "latest-only" strategii. Kamera HAL hlásí `timestamp is not increasing` (44×) a `92 late Color frame set were dropped`. OpenXR PCA extension je na tomto OS `XR_ERROR_FUNCTION_UNSUPPORTED` → doručení jde přes Camera2 ID 50/51.

**Úprava §4:** doplnit "monotónnost timestampů se musí validovat (HAL ji porušuje)" a "pairing okno je násobek periody kamery, ne pevných 1/30 s".

### 1.5 Pose v čase záběru není samozřejmost

- Managed `PassthroughCameraAccess.GetCameraPose()` nemá parametr času; donor ji latchuje ve stejném render callbacku jako obraz a spoléhá, že odpovídá snímku. Není to ověřeno měřením.
- OpenXR garantuje pouze **≥50 ms** historie pro `xrLocateSpace` v minulosti. PCA latence 20–40 ms (oficiálně) až 40–60 ms (v74) z toho okna spotřebuje většinu. Pose se tedy musí lokalizovat **ihned na callbacku kamery**, ne v příštím render ticku.
- Clock domény: Camera2 `SENSOR_TIMESTAMP` (BOOTTIME nebo "unknown monotonic" podle `TIMESTAMP_SOURCE`, na Questu neověřeno) vs. `XrTime`. Donor převáděl přes `SensorClockMapper` (bracket `OVRPlugin.GetTimeInSeconds` kolem `DateTime.UtcNow`) — hack s nejistotou.

**Úprava §3:** přidat sekci **CLOCK DOMAINS**: jediný kanonický čas = `XrTime` (ns). Camera timestamp → `XrTime` přes `XR_KHR_convert_timespec_time`; probe `SENSOR_INFO_TIMESTAMP_SOURCE` v C01. Pose history ring musí být **vlastní** (plněný z každého XR framu při 72 Hz + `xrLocateSpace` při příchodu snímku), protože runtime garantuje jen 50 ms.

### 1.6 Environment Depth: naměřené parametry

`320×320×2` (per eye), swapchain 4, **25 Hz** (22.6–27.3), data age ~21 ms, `XR_META_environment_depth` SpecVersion 2, hand removal supported. Logická apertura v otherscan 256×192 s offsetem (32,64). Známý bug: dvojí `xrAcquireEnvironmentDepthImageMETA` ve stejném framu → `XR_ERROR_LIMIT_REACHED` (po resume). Depth má vlastní pose a fov per eye a vlastní čas, ne čas PCA ani display.

**Úprava §25:** zapsat, že prior je 4× hrubší než PCA, má vlastní čas/pose/fov a musí se **reprojektovat** do PCA capture pose; acquire přesně jednou za XR frame; po resume subsystem restart (`b34910b`). Hand removal defaultně ON, ale pixely "estimated background" značit jako low-confidence.

### 1.7 Vulkan topologie a extensiony (naměřeno na Quest 3S, driver 512.837.9, API 1.3.295)

- Queue families: **family 0 = graphics|compute|transfer, 4 queues, 4 priority levels; family 1 = 1 queue** (bez reportovaných flagů — pravděpodobně transfer-only/video). **Žádná compute-only family.** Unity si vytváří 1 queue ve family 0. Otherscan probe: `separateComputeFamily=0 dedicatedComputeFamily=0`.
- Přítomno: `synchronization2`, `timeline_semaphore`, **`VK_KHR_pipeline_binary`**, `maintenance5`, `push_descriptor`, `global_priority`, `host_query_reset`, `buffer_device_address`, `ANDROID_external_memory_android_hardware_buffer`, `sampler_ycbcr_conversion`, `queue_family_foreign`, `shader_float16_int8`, `16bit_storage` (uniform 16-bit NE).
- **Chybí `VK_KHR_calibrated_timestamps`** → GPU timestamp ↔ CPU/XrTime korelace se musí dělat ručně (submit-time bracketing).
- `timestampPeriod = 52.083 ns`, 48 validních bitů, `timestampComputeAndGraphics = true`.
- Adreno 7xx má LPAC (low-priority async compute) hardware; zda ho proprietární driver mapuje na druhou VkQueue ve family 0, nebo jen time-slicuje, **nikdo neměřil**. Injekce druhé queue (index 1, priority 0.1) v donoru funguje (`injected=1 … result=0`), ale Unity hlásí `supportsAsyncCompute=False`.

**Úprava §34–36:** přeformulovat očekávání: realistický výsledek je **druhá queue ve stejné family**, ne compute family. C01 musí změřit **skutečný overlap** (timestampy scan jobu vs. Unity frame na stejné časové ose) a vyzkoušet `VK_EXT_global_priority` LOW pro scan queue. Žádný family ownership transfer nebude potřeba (§37 to už říká).

### 1.8 Paměť: naměřené baseline

- Oficiální PSS limit Quest 3/3S: **5,75 GiB** (lmkd).
- m16 baseline hned po XR startu s passthrough + PCA L/R: **PSS 1,73–1,87 GB, z toho Graphics 1,33–1,43 GB**. Bez XR (skybox): 361 MB.
- otherscan: GPU alokace 2085 MiB = 100 %, deterministický OOM blocker.
- SimpleScan flower era: 3,3 GB RSS + 1,8 GB swap při pipeline drain.

**Úprava §47:** zapsat tato čísla jako prior a stanovit, že memory governor pracuje s **~5,75 GiB PSS minus naměřený baseline (~1,8 GB)**, tj. reálně **≤ 3,5 GB** pro svět + atlas + scratch, s rezervou na fragmentaci.

### 1.9 SimpleScan feature parity: co skutečně existuje

Kontrakt §59 říká "pokud SimpleScan má object model import/detection, zachovej". **Object detection/ORT v současném stromu není** (smazáno, existovalo pre-Merkaba na `feature/object-reconstruction`). "Wrist console" existuje jen jako statický HTML mock. Skutečný inventář je v §8 níže.

---

## 2. Co v kontraktu chybí (nové sekce)

### 2.1 Ingest kamery: zero-copy cesta (nová sekce mezi §4 a §5)

Donoři používají MRUK → RGBA8 blit do history ringu. To je CPU/GPU kopie plus YUV→RGBA konverze, kterou stereo nepotřebuje (chce luma). Na zařízení jsou `VK_ANDROID_external_memory_android_hardware_buffer` + `VkSamplerYcbcrConversion` a Camera2 doručuje přes `PcaGpuProviderVulkan`.

Požadavek: C01 ověří, zda nativní Camera2 NDK (`AImageReader` s `AHARDWAREBUFFER_USAGE_GPU_SAMPLED_IMAGE`) s permission `HEADSET_CAMERA` funguje a jaká je latence proti MRUK cestě. Pokud ano, **produkční ingest je nativní zero-copy: AHardwareBuffer → VkImage (Y plane) → pyramid**. MRUK cesta zůstává jen jako měřený fallback, ne druhý scanner core.

### 2.2 Kalibrace, rektifikace, rolling shutter (nová sekce před §26)

- Meta dává pouze pinhole intrinsics (focal, principal, sensor res, lens offset pose). **Distorční model není dokumentován**; není známo, zda jsou snímky undistortované. C01: šachovnice / reprojekční test.
- Stereo search musí běžet na **rektifikovaném páru** (epipolar lines = řádky). Rectification mapy se počítají jednou z extrinsics L/R a aplikují v pyramid stage.
- Přesnost stereo hloubky: σz ≈ z²·σd/(f·b). Při f≈700 px, b≈6,5 cm, σd=0,25 px je σz ≈ 5 cm na 3 m, ≈ 0,5 cm na 1 m. **Instantní L/R stereo nedává sub-cm metriku nad ~1,5 m.** Sub-cm na dálku dává jen temporal baseline z pohybu hlavy (§27) — ta proto není "optional", ale primární zdroj metrické přesnosti pro vzdálené povrchy. Formulaci §27 změnit.
- Rolling shutter: neznámo. Při rychlém yaw se řádky snímku liší o pose; C01/C03 změří skew (řádkový čas × úhlová rychlost) a scheduler bude snímky nad prahem úhlové rychlosti zahazovat nebo řádkově kompenzovat. Dnes to kontrakt vůbec neřeší, přitom je to přímý zdroj "rozmazané statické geometrie při pohybu hlavy" (§68).
- Expozice/ISO metadata: neověřeno, že je Meta vystavuje. Radiometrická normalizace atlasu (§C17) nesmí na nich záviset.

### 2.3 Jak se nativní draw dostane do Unity XR framu (doplnit §19/§44)

Kontrakt říká "native 72Hz Vulkan cull/draw", ale neříká **jak** to jde do Unity URP render passu (multiview/SPI, MSAA, foveation, subpass, tile memory). Donor to řešil hackem: intercept `vkCmdDrawIndexedIndirect` → přepis na `vkCmdDrawIndexedIndirectCount` uvnitř Unity passu, a narazil na Unity 6000.5 sémantiku (`subPassIndex=0` s null renderPass, `stride=0`). Unity V2 interface navíc neexponuje sample count aktivního render passu.

Návrh (rozhodnout v C12 podle měření, ale výchozí je A):

- **A — Native-published, Unity-issued draw.** Native vlastní surfel draw listy, culling a indirect args (GPU-generované). Unity vydá **jeden** `DrawProceduralIndirect` / RenderGraph pass se surfel shaderem (perspective-correct ellipse, analytic depth, depth write). Unity řeší multiview, MSAA, foveation, subpass. Native nikdy nesahá do Unity render passu. SPI vyžaduje zdvojený instance count (donor `f4970df`).
- **B — Plně nativní graphics pipeline** uvnitř Unity passu přes `CommandRecordingState`. Jen pokud A prokazatelně nestíhá; vyžaduje hook `vkCreateRenderPass` pro sample count a Unity `pipelineCache`.

A splňuje všechny invarianty (§38 snapshot, §44 sparse draw list, §14 residency) a eliminuje celou třídu donor bugů kolem render-pass interceptu.

### 2.4 Surfel rasterizace: konkrétní režim (doplnit §19)

"Opaque disk" sám o sobě dává viditelné hranice disků. SOTA (Gaussian-enhanced Surfels 2025, Mobile-GS 2026, Qualcomm sort-free WSR 2025) potvrzuje: **opaque, depth-tested, bez sortingu** jako základ. Přidat:

- Pass 1: visibility/depth (ellipse, analytic depth, depth write, `discard` mimo elipsu — pozor na early-Z na TBDR, měřit).
- Pass 2 (volitelný, měřený): ε-depth blend atributů (EWA váhy) nad depth ε ≈ 2 σz. Bez pass 2 = hard edges, s ním = hladké přechody. Nikdy víc než 2 passy, žádný per-pixel sort.
- Edge AA: MSAA 4× je na Questu levné (tile memory); alpha-to-coverage pro kruhový footprint.

### 2.5 Kernel envelope (nová sekce u §39/§42)

Donoři zabili sami sebe monolitickými shadery: SPIR-V 746 KB–1,6 MB, "Failed to link" -13, cold link 226 s, drain job zabit po 270 s. Adreno navíc odmítá dynamický index do vektoru, Unity limituje 8 UAV per dispatch, groupshared 32 KB, storage binding 128 MiB.

FinalScan gate (build-time, fail-closed): **SPIR-V ≤ 128 KB per pipeline, ≤ 8 k instrukcí v těle, groupshared ≤ 16 KB, ≤ 8 writable bindings, push constants pro per-dispatch parametry** (donor nepoužíval push constants vůbec a hashoval názvy uniformů za běhu). Malé kernely, GPU-generované fronty mezi nimi (§39), ne jeden kernel "udělá vše".

### 2.6 Job model nativního executoru (doplnit §35/§42)

Hlavní naměřený bottleneck donorů není matematika, ale **jeden globální job slot** (`_activeJob`): readout job ~42–99 ms wall, ~54 ms čekání ve frontě, resubmit každý frame → queue trvale obsazená, flush nikdy nedoběhl, 6,2 Hz observation místo 15–20.

FinalScan: **job classes s vlastními fence ringy a bounded in-flight**: `SCAN` (≤2 in flight, quantum ≤ 2–3 ms), `PUBLISH` (page-local, mikro-job), `RESIDENCY/COLD` (sliced, otherscan pattern), `APPEARANCE` (nízká priorita). Submit `SCAN` quantum **hned po Unity frame submit** (idle gap GPU). Persistent descriptors + persistent per-resource import (donor dělal per-job image views, descriptor pooly a `AccessTexture` bariéry — CPU overhead a zdroj lifetime bugů).

### 2.7 Relokalizace přes platformní Spatial Anchors (doplnit §51/§53)

SimpleScan váže session na jeden `OVRSpatialAnchor` UUID a relokalizuje přes `RelocateForLoadedAnchor`. Funguje, je to jediná cross-session relokalizace, kterou Quest poskytuje bez vlastního VIO. FinalScan AnchorGraph: **každý Anchor má volitelně platformní Spatial Anchor UUID**; OPEN nejprve lokalizuje platformní anchory, vlastní loop closure/relokalizace řeší jen intra-session drift a fallback. Nezačínat vlastním visual relocalizerem.

### 2.8 Ground truth pro geometrickou acceptance (doplnit §71)

Kontrakt vyjmenovává metriky, ne způsob získání pravdy. Doplnit protokol: laserový dálkoměr (rozměry místnosti, tloušťka příčky), kalibrační box známých rozměrů, tištěný planární target, opakovaný průchod A/B se stejnou trajektorií. Bez toho je "plane RMS" nezakotvené.

### 2.9 Testovatelnost mimo zařízení (nová sekce)

Donoři měli 13 ze 17 test souborů jako source-text tripwires (`ReadAllText` + `IndexOf`). FinalScan: **kernel logika testovatelná na Linux hostu** (lavapipe/SwiftShader nebo host Vulkan) s deterministickými syntetickými measurementy: hash overflow, multi-surface association, split/merge, free-space. Device jen pro výkon a sensor. Jeden writer per checkout (RISK-5 donorů).

---

## 3. Sekce, které bych přeformuloval

| § | Dnes | Změna |
|---|---|---|
| 2 Capture | "30 FPS" jako produkční předpoklad | 30 = dolní mez korektnosti; design korektní i při 60; `MaxFramerate` je scheduler output |
| 3 PCA latence | pose(captureTimestamp) | + clock domains, + vlastní pose ring (runtime garantuje jen 50 ms), + lokalizace na kamera callbacku |
| 5 Sensor pipeline | "PCA YUV → pyramid/luma" | + zero-copy AHB import (2.1), + rektifikace (2.2), + rolling-shutter gate |
| 7 Surfel | koncept | + konkrétní packed record pro C06: pozice 3×f16 relativně k page (±32 m při 1 mm? ne — page-local ±4 m s 0,25 mm krokem), normal oct16, radius log-encoded u8×2, cov 3×f16 (diag) + f16 off-diag, confidence/evidence u16, flags u8, SurfaceID u32, appearance handle u32 → 32 B. Benchmark rozhodne 32 vs 48 B |
| 11 Hash | "robin-hood/cuckoo podle benchmarku" | Cuckoo vyřadit pro cell hash (lookup fan-out, DyCuckoo degradace); bucketed linear/robin-hood se subgroup (64-wide) kooperací; cuckoo max pro page hash |
| 19 Render | "oriented disk, opaque" | + 2.3 integrace do Unity, + 2.4 pass model |
| 22 Atlas | "surface charts" | Rozhodnout v C17 spike mezi (a) per-page planar cluster charts a (b) per-surfel texel tiles (TextureFusion styl). Doporučuji (a) — coplanar clustery už existují kvůli merge/LOD |
| 25 Env Depth | prior | + naměřené 320²@25 Hz, reprojekce z vlastní pose/fov/čas, single acquire per frame, restart po resume |
| 27 Temporal | "optional" | primární zdroj sub-cm metriky nad ~1,5 m |
| 32 Donor audit | SimpleScan + UniScan | tři větve jednoho repa + host projekt; 9cfed011 je UniScan |
| 34–36 Queues | "možná compute family" | naměřeno: family 0 ×4 queues; C01 měří overlap + global priority; sliced jobs z otherscan |
| 40 Pipeline binaries | "reuse SimpleScan" | mechanismus je v UniScan (`MerkabaPipelineBinary.h`, `a2df123`); **nepřevzít** tvrdé vynucení extensionů při vkCreateDevice (app se jinak nespustí na jiném driveru) ani "no SPIR-V fallback"; klíčovat `pipelineBinaryUUID`+driver; fallback = `VkPipelineCache` (Unity `UnityVulkanInstance.pipelineCache` je k dispozici) + warm-up obrazovka **před** povolením scanu |
| 41 Timestamps | "device timestamps" | + bez `calibrated_timestamps` → manuální GPU↔CPU korelace; 52,083 ns period; per-owner query rangy (donor bug `f2cfe26`) |
| 47 Memory | "změř" | + priory: PSS limit 5,75 GiB, baseline ~1,8 GB, reálně ≤ 3,5 GB |
| 51 Loop closure | vlastní | + platformní Spatial Anchors jako primární cross-session relokalizace |
| 59 Neural | "pokud SimpleScan má detection" | nemá; rozhodnout explicitně, zda ORT object reconstruction (větev `feature/object-reconstruction`, 2026-04) je v parity scope |
| 65 Negative lessons | 7 položek | rozšířit na seznam v §5 níže (všech 7 potvrzeno + 14 dalších) |
| 71 Geometry acceptance | metriky | + ground truth protokol (2.8) |

---

## 4. Upravený commit DAG

Hlavní změna: **renderer a readout sphere musí existovat dřív než fusion**, protože C04 (spin test) bez rendereru nelze provést, a protože každý donor selhal právě na readout cestě. Fusion se vyvíjí proti hotové, změřené readout cestě se syntetickým světem.

```
C00  chore(genesis)     inventář 3 větví + host projekt, donor ledger (3 sloupce),
                        feature parity seed (§8), risk register, tento dokument
C01  feat(platform)     probe: PCA cadence L/R/L+R @30/@60, 960 vs 1280, capture→app latency,
                        TIMESTAMP_SOURCE + XrTime konverze, pose@capture reprojekční test,
                        rolling shutter skew, distorze/rektifikace, Env Depth cadence,
                        queue families + overlap měření + global priority,
                        Camera2 NDK zero-copy AHB import, pipeline_binary UUID,
                        timestampPeriod, PSS baseline per komponenta, kernel link envelope
C02  feat(vulkan)       nativní host: intercept (s fallbackem), job classes + fence ringy,
                        persistent descriptors, push constants, timestamps, error lifecycle,
                        sleep/wake/device-lost, pipeline cache + binary (optional)
C03  feat(sensor)       PCA L/R ring, XrTime, pose ring, pairing, skew, stale drop, gating
C04  feat(geometry)     metric surfel ABI + page serialization + syntetický world generátor
C05  feat(render)       Unity-issued indirect draw nativních draw listů, ellipse raster,
                        depth write, MSAA, SPI, LOD skeleton — měřeno na syntetickém světě
C06  feat(render)       sparse immutable page publication (FRONT/BACK, generation)
C07  feat(residency)    INNER/WARM/PREFETCH, syntetické pages, spin test → zero turn pop
C08  feat(addressing)   page hash + cell hash, generations, overflow, host testy
C09  feat(measurement)  Env Depth prior (timestamp-correct, reprojected)
C10  feat(measurement)  rektifikace + bounded stereo + subpixel + uncertainty
C11  feat(measurement)  temporal baseline (primární pro >1,5 m)
C12  feat(mapping)      surfel fusion: association, create/update, split/merge
C13  feat(mapping)      free space + transient gating
C14  feat(residency)    disk-backed streaming, journal
C15  feat(memory)       governor
C16–C18 appearance     keyframes, atlas, directional residual
C19  feat(mapping)      AnchorGraph + platformní Spatial Anchors
C20  feat(mapping)      loop closure / relokalizace
C21  feat(lifecycle)    sleep/wake/tracking loss/reopen (vč. Env Depth restart, single acquire)
C22  feat(ux)           scan flow + quality guidance
C23  feat(tools)        DETAIL + ERASE
C24  feat(annotation)   notes / paint / measurements / plan view
C25  feat(models)       object library, ALIGN, artifact viewer parity
C26  feat(storage)      transactional autosave, recovery, OPEN
C27–C28 export          mesh + texture bake + GLB/glTF + 3D Tiles
C29  feat(parity)       zero unexplained missing
C30  perf(vulkan)       overlap, indirect, binaries, zero hidden sync
C31  perf(residency)    1 h run
C32  release
```

Pravidlo, které přidat do §80: **žádný řez C09–C13 nesmí být přijat bez timestampů na stejné časové ose jako Unity frame** (zabránit tomu, co se stalo SimpleScanu: HUD "Active", integrační stage 0 invokací za celou 9minutovou session).

---

## 5. Rozšířený seznam negativních lekcí (§65)

Všech 7 položek kontraktu je potvrzeno v kódu/commitech. Doplnit:

**Nativní vrstva**
8. `vkGetFenceStatus(NULL)` → SIGSEGV v Adreno driveru; sync objekty musí existovat před jakýmkoli `AccessBuffer/AccessTexture` (ty zapisují bariéry do Unity command streamu i při pozdějším selhání validace).
9. `FailJob` měnil stav během prepare → use-after-free okno; stav publikuje výhradně vlastník fáze, terminální stav není tranzientní.
10. Cross-thread flagy jako plain fieldy; `PollJob` lock-free na main threadu vs. `DestroyJob` pod mutexem.
11. Vynucení `pipeline_binary`/`maintenance5` při `vkCreateDevice` → device creation fail na jiném driveru; BINARY_ONLY bez SPIR-V fallbacku.
12. Monolitické SPIR-V 0,75–1,6 MB: link fail -13, cold link 226 s, RSS 3,3 GB při drain.
13. Per-job image views/descriptor pools/uniform buffery; žádné push constants; uniform hashing názvů za běhu.
14. Draw intercept spoléhal na Unity render-pass sémantiku (`subPassIndex`, `stride`), která se v 6000.5 změnila.
15. Injekce queue bez fallbacku (uniscan) — otherscan fallback je správný vzor.
16. Literály offsetů argument bufferů přežívají i po fixu (`kFlowerPageWorkArgsOffset`, `kFlowerColdArgsOffset`).

**Unity/host**
17. `GraphicsFence` `AsyncQueueSynchronisation` hází `NotSupportedException` každý frame (2 622×, 1 567×/12 s v m16); jen `CPUSynchronisation` nebo async readback tokeny.
18. Čtení producer-owned PCA/Env Depth textury mimo callback, kde se latchují metadata, vidí předchozí snímek.
19. Dvojí `xrAcquireEnvironmentDepthImageMETA` per frame; Env Depth subsystem mrtvý po resume.
20. Výjimka mimo try/catch nechá nativní lease držený navždy → `IsBusy` blokuje SAVE/LOAD.
21. Unity: max 8 UAV per dispatch; Adreno: žádný dynamický index do vektoru; SPI zdvojení instance count.

**Proces**
22. Tvrzení o měření bez device receiptu (retrakce `7ec07b0`); telemetry, která injektuje práci do měřené cesty (`0770fd2`).
23. Source-text testy místo behaviorálních.
24. Dva agenti v jednom checkoutu.
25. Zastaralé README/ALGORITHM a mrtvá konstanta zamrzlá v persistence headeru.
26. Aktivní UI ≠ GPU mutace: celá session s 0 integračními invokacemi a HUD "Active".

---

## 6. Co C01 musí změřit navíc oproti §61

- `SENSOR_INFO_TIMESTAMP_SOURCE`, převod na `XrTime`, jitter.
- Reprojekční chyba pose@capture při rychlém yaw (managed `GetCameraPose` vs. nativní `xrLocateSpace`).
- Rolling shutter řádkový čas (nebo důkaz global shutter).
- Distorční reziduál pinhole modelu; rektifikační chyba L/R.
- PCA baseline L/R z extrinsics (metrika stereo σz).
- App-side dosažené fps L, R, L+R při 30 a 60, při 1280×960 a 1280×1280, s thermal profilem 15 min.
- Camera2 NDK zero-copy dostupnost a latence vs. MRUK.
- Druhá queue: overlap (timestampy obou queue na jedné ose), efekt `global_priority` LOW, vliv na Unity frame time.
- Kernel link envelope: největší SPIR-V, který linkuje pod 1 s / 5 s.
- PSS per komponenta: XR only / +passthrough / +L / +L+R / +Env Depth / +UI / +plugin.
- `pipelineBinaryUUID`, chování po OS update (dva buildy OS, pokud dostupné).

---

## 7. Co z donorů převzít (seed pro VULKAN_DONOR_LEDGER)

| Mechanismus | Zdroj | Převzít | Odmítnout |
|---|---|---|---|
| V2 intercept `AddInterceptInitialization` + hook `vkCreateDevice` | uniscan cpp:508–917 | ano, priorita registrovaná tak, aby Meta OpenXR interceptor běžel | — |
| Injekce queue ve family 0 | uniscan/otherscan | ano | uniscan bez fallbacku |
| Fallback na původní `vkCreateDevice` | otherscan :539–560 | ano | — |
| Fence lifetime + prepare ownership | 9cfed011 | invarianty ano | tvar kódu ne (3 fences/job, per-job objekty) |
| Timestamp query pool per owner | simplescan `f2cfe26` | ano | globální sdílený range |
| Async readback tokeny místo GraphicsFence | `ba286ac`, `57ab534` | ano pro C# stranu | — |
| Generator-derived ABI (reflection SPIR-V → .inc, cross-check C#/HLSL) | uniscan generator :319–401, 623–667 | princip ano | FNV uniform hashing za běhu, chybějící push constants |
| Frame-paced sliced jobs | otherscan `RecordJobSlice/SubmitNextSlice` | ano | — |
| Pipeline binary CAPTURE/BINARY_ONLY, Java bootstrap ContentProvider | uniscan `MerkabaPipelineBinary.h`, `a2df123` | workflow ano | force extensions, no-fallback |
| Draw intercept `vkCmdDrawIndexedIndirect` → Count | uniscan `MerkabaFlowerDraw.h` | ne (viz 2.3 A) | celý mechanismus |
| Producer-owned textury kopírovat v témže callbacku | simplescan RGBD-6 | ano | — |
| L/R history ring + pairing joint spread | simplescan RGBD-1 | ano | latest-only |
| Env Depth restart po resume, single acquire | `b34910b` | ano | — |
| Session ↔ Spatial Anchor UUID, relocation matrix | simplescan `MerkabaPersistence`, `RoomAnchorManager` | ano, zobecnit na AnchorGraph | jeden anchor per session |
| Journal + checkpoint + `File.Replace` publish + CRC per tile | `MerkabaSsdStore` | princip ano | 28B header/8192B payload formát, ChunkSize=32 v headeru |
| SAF picker, GLB writer, 3D Tiles writer, web viewer | simplescan | funkčně ano | vazba na membránu |

---

## 8. Feature parity seed (co SimpleScan skutečně má)

Scan: START/STOP, FINE (trigger) / ERASE (grip) brush s přesným válcem podél controller ray, cursor z Env Depth hitu, opacity slider, occlusion toggle.
Sessions: NEW/OPEN/SAVE/SAVE AS/RENAME/DELETE, overlay journal s recovery ("Recovered Scan"), session ↔ spatial anchor, fail-closed při anchor mismatch.
Export: GLB (SAF picker), 3D Tiles ZIP (128/256 MB leaves), Three.js web viewer.
Artifact viewer: open GLB/ZIP, plan view, opacity, World Lock, ALIGN 1:1 přes anchor, 6DoF grab, two-hand rotate/scale, anotace point/line/plane/note (`annotations.json`), měření.
Paint: brush/surface/3D/spray/line/eraser/eyedropper, HSV wheel, swatches, undo/redo (`design.json`).
Object library: import GLB (SHA-256 content-addressed), place, select, duplicate, hide, lock, snap.
Input: levý thumbstick click = menu, pravý trigger = select, hands jako pose source, world-space UI Toolkit panel, controller ray.
Diagnostika: FPS, chunks/kernels, mesh readout, checker (contr říká hide by default).
Neexistuje: object detection/ORT (jen historická větev), wrist console (jen HTML mock), autosave jako explicitní user feature (jen journal).

FinalScan §55 musí každou z těchto položek vázat na `AnchorID + SurfaceID/chart coords`, a §22 (scan UX) musí explicitně říct, čím bude **lepší**: guidance coverage/quality (kde chybí evidence, kde je vysoká nejistota), DETAIL jako viditelný lokální stav, stabilní obraz bez popu, okamžitý OPEN.

---

## 9. Otevřené rozhodnutí, které musí padnout před C02

1. **Cílové zařízení**: Quest 3, 3S, nebo obě (1.2).
2. **Ingest**: nativní Camera2 zero-copy jako produkční cesta, MRUK jen fallback (2.1) — potvrdit po C01.
3. **Render integrace**: varianta A (Unity-issued draw) jako výchozí (2.3).
4. **ORT object reconstruction** v parity scope ano/ne (1.9).
5. **Horizon OS minimum**: v83+ (1280×1280, dual camera 60 Hz) nebo v76+ (Store minimum).
