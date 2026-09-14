# FINALSCAN v4

## Quest 3 / Quest 3S native realtime metric scanner

## Pre-C01 contract

Stav: **v4 nahrazuje v3 jako autoritu.** Vznikl z `FINALSCAN_V3_REVIEW.md` (vstup C00) a z korekcí přijatých 2026-09-14. Po C01 se z v4 zmrazí `FINALSCAN_ARCHITECTURE_V1.md`; do té doby jsou všechna čísla označená *provisional* nebo *measured*.

Konvence v dokumentu:

* **measured** = existuje device receipt (log, timestamp, meminfo) v lokální evidenci;
* **official** = dokumentace Meta / Khronos / Android / Unity;
* **provisional** = návrhová hodnota, C01 ji potvrdí nebo přepíše;
* **invariant** = nesmí být porušen žádným řezem.

FinalScan vzniká jako nový čistý projekt v `.../prace/finalscan`. Není refactor donorů.

---

# 0. DONOŘI

Donoři jsou **tři větve jednoho repa** `fladirm/QuestInfiniteScan` plus host projekt na `/mnt/kingston-unity/Builds/Uniscan*`:

| Donor | Checkout | Větev | Co dává |
|---|---|---|---|
| SimpleScan | `prace/simplescan` | `fix/merkaba-runtime-root-causes` | produktové funkce, sensor pairing lekce, persistence, export, viewer, paint, library |
| UniScan | `prace/uniscan` | `refactor/m8-dual-sphere-flower-rev-b` (HEAD `9cfed011`) | nejpokročilejší nativní vrstva: intercept, queue injection, pipeline binary, ABI generator, fence/lifetime invarianty |
| OtherScan | `prace/otherscan` | `forensic/n4r-cut-e-scheduler` | frame-paced sliced native jobs, vkCreateDevice fallback, cold upload/encode job families |

Nativní kód donorů: `Runtime/Telemetry/Native/*.cpp` (ne `Runtime/Plugins`). Commit `9cfed011` je UniScan.

Donoři jsou:

* zdroj ověřených Quest/Unity/Vulkan postupů;
* zdroj produktových funkcí;
* zdroj negativních lekcí (§23).

Scanner core se implementuje načisto. Kód přenesený kvůli platformě musí být přejmenovaný, vyčištěný, minimální a doložený v `MIGRATION_LEDGER.md`. Zakázaná jména a koncepty: Merkaba, Flower, Sigma, PRISM, DIRT, TSDF, SurfaceNets, membrane, legacy fallback.

---

# 1. HLAVNÍ PRODUKTOVÝ CÍL

FinalScan musí současně poskytovat:

* metrickou, opakovanými průchody zpřesnitelnou geometrii;
* ostré rohy, tenké stěny se dvěma nezávislými stranami, šikmé a zakřivené povrchy, schody, malé detaily;
* prakticky neomezenou velikost skenu;
* stabilní 72Hz XR readout;
* žádné doskakování geometrie ani textur při pouhém otočení hlavy;
* fotorealistický vzhled blížící se 3DGS;
* lokální DETAIL, ERASE;
* SAVE/OPEN/autosave/recovery;
* GLB/glTF export;
* poznámky, měření, kreslení/malování;
* importované modely a jejich transformace;
* všechny user-facing capability SimpleScanu (§1.1);
* lepší scan UX než SimpleScan (§18.1).

Neimplementuj MVP.

## 1.0 Tvrdý invariant hustoty

**Canonical density may grow arbitrarily; realtime cost may not.**

Květinářství (miliony malých listových surfelů, tenké stonky, pohybující se listy, sklo, celofán) a prázdná
místnost jsou jeden systém. Růst scény smí způsobit jen: víc dat na SSD, víc canonical surfelů, větší lokální
hustotu. Nesmí způsobit: nižší XR FPS úměrně velikosti mapy. 72Hz workload je omezen viewportem, render
budgetem, INNER working setem a screen-space LOD (§13.4). 20Hz scanner je omezen počtem vybraných
measurements (§7.6), bounded candidate search (§9.2) a GPU quantem (§15.4). Nikdy ne počtem surfelů ve světě.

## 1.1 Feature parity seed (measured z SimpleScan stromu)

| Oblast | Funkce |
|---|---|
| Scan | START/STOP; FINE (trigger) / ERASE (grip) brush s válcem podél controller ray; cursor z depth hitu; opacity slider; occlusion toggle |
| Sessions | NEW/OPEN/SAVE/SAVE AS/RENAME/DELETE; journal recovery („Recovered Scan“); session ↔ spatial anchor; fail-closed při anchor mismatch |
| Export | GLB přes SAF picker; 3D Tiles ZIP (128/256 MB leaves); Three.js web viewer |
| Artifact viewer | open GLB/ZIP; plan view; opacity; World Lock; ALIGN 1:1; XRAY/RADAR zobrazení celého modelu i za zdmi (§13.5 módy); 6DoF grab; two-hand rotate/scale; anotace point/line/plane/note; měření |
| Paint | brush/surface/3D/spray/line/eraser/eyedropper; HSV wheel; swatches; undo/redo |
| Object library | import GLB (content-addressed); place, select, duplicate, hide, lock, snap |
| Input | levý thumbstick click = menu; pravý trigger = select; hands jako pose source; world-space UI panel; controller ray |
| Diagnostika | FPS, counters, readout stats (hidden by default) |
| Neexistuje | object detection/ORT (jen historická větev `feature/object-reconstruction`); wrist console (jen HTML mock) |

`FEATURE_PARITY.md` v C00 vychází z této tabulky. ORT object reconstruction je mimo scope (§25).

---

# 2. CÍLOVÉ ZAŘÍZENÍ

**Quest 3 a Quest 3S jsou z hlediska aplikace jeden hardware.** Stejný XR2 Gen 2 / Adreno 740, stejné PCA, stejný Environment Depth. Liší se jen optikou displeje a depth výpočtem, což FinalScan neřeší. Kontrakt nikde nerozlišuje modely; žádný per-model support claim, žádná per-model acceptance.

* Evidence device: `panther` (serial `340YC20G7X0QZ4`), Adreno 740, driver 512.837.9, Vulkan 1.3.295 (measured). GPU pinned 456 MHz při scanu (measured).
* Extrinsics, baseline a intrinsics se čtou z runtime, nikdy hardcoded. To je kalibrační hygiena, ne otázka modelu.
* Horizon OS: **nejnovější dostupná verze k datu vývoje** (k 2026-09-14). Žádná zpětná kompatibilita se staršími verzemi OS; 1280×1280 a dual-camera 60 Hz jsou k dispozici.

---

# 3. TŘI ČASOVÉ DOMÉNY

Nikdy nespojuj display cadence se scan cadence.

## 3.1 Render domain — 72 Hz (invariant)

Latency-critical. Nesmí čekat na PCA, stereo, fusion, disk, loop closure ani canonical update. Renderer vždy kreslí poslední kompletní publikovaný FRONT (§11). **Readout není job** — je to čtení immutable FRONT každých 13,89 ms.

## 3.2 Capture domain — profily, ne cadence

Systémová color cadence na zařízení: 72 Hz nominal za dobrého světla, **50 Hz v low-light**, 25 Hz grayscale, Env Depth 25 Hz (measured). To je HAL/sensor evidence, **není to aplikační kontrakt PCA**.

Aplikační kontrakt PCA (official, Meta 2026-04): data rate 60 Hz, capture latency 20–40 ms, YUV420, max 1280×1280, ~45 MB a 1–2 % GPU per stream.

FinalScan:

```
correct  @ >= 30 Hz usable delivery
optimized @ 60 Hz pokud device/session skutečně drží
nikde nepsat "PCA = 72 Hz"
```

Kamera se nepřekonfigurovává při každém rychlém pohybu. C01 změří stabilní operating points a scheduler mezi nimi přechází jako **coarse state transition** (řád sekund, s hysterezí), ne každých 100 ms:

```
NORMAL_30      výchozí, klid / pomalý pohyb
NORMAL_60      aktivní scan, dostatek světla, thermal OK
DETAIL_60      DETAIL request
LOWLIGHT       sensor běží 50 Hz → 60 nelze zaručit; app request 30 nebo 50 podle C01
THERMAL_30     thermal warning / frame misses
```

Runtime vždy měří skutečnou L cadence, R cadence, timestampy, receive time, pose availability a inter-camera skew.

## 3.3 Scan domain — ~20 Hz maximum (provisional)

Přibližně jeden canonical measurement update za 50 ms. Scanner používá nejnovější koherentní observation a stale frames zahazuje.

```
camera ring → newest valid coherent pair → max ~20 integration ticks/s → stale drop
```

Pokud předchozí scan quantum není dokončeno: žádný backlog, další tick se skipne/merguje, render pokračuje. Nikdy „30 frames/s → 30 jobs/s → několik sekund starý scan“.

---

# 4. SENSOR AUTHORITY

## 4.1 Kanonický čas

Jediný kanonický čas je `XrTime` (ns, OpenXR monotonic). Každý frame má `captureTimestamp` v `XrTime` **plus** `timestampUncertaintyNs`. Frame se integruje pomocí `cameraPose(captureTimestamp)`, nikdy pomocí pose v době příchodu (invariant).

## 4.2 Clock-domain gate (hard sensor-authority gate)

Camera2 `SENSOR_TIMESTAMP` je přímo srovnatelný s jinými subsystémy jen když `SENSOR_INFO_TIMESTAMP_SOURCE == REALTIME` (official Android). C01 musí zjistit skutečný stav a C03 implementuje přesně jednu z cest, nikdy tiše „exact“:

```
A) SENSOR_INFO_TIMESTAMP_SOURCE == REALTIME
   → timespec (CLOCK_BOOTTIME/MONOTONIC) → XrTime přes XR_KHR_convert_timespec_time
   → preferred, uncertainty = jitter převodu (measured)

B) řiditelný timestamp base (Camera2 OutputConfiguration)
   → naměřené mapování REALTIME/MONOTONIC → XrTime
   → validace reprojekčním testem

C) UNKNOWN / neprokazatelné mapování
   → xrLocateSpace ihned na callbacku
   + kalibrovaný clock mapper s explicitní uncertainty
   → nikdy nevydávat za exact; uncertainty propaguje do measurement covariance
```

Frame, jehož `timestampUncertaintyNs` překročí práh (provisional 2 ms), snižuje stereo confidence; nad druhým prahem (provisional 8 ms) se zahazuje.

## 4.3 Vlastní pose ring (invariant)

OpenXR garantuje jen **≥ 50 ms** historie pro `xrLocateSpace` v minulosti (official). PCA latence 20–40 ms z okna spotřebuje většinu. Proto:

* FinalScan drží **vlastní pose ring** plněný při každém XR framu (72 Hz) + `xrLocateSpace` volané **ihned na callbacku kamery**, ne v příštím render ticku;
* ring pokrývá PCA delay + stereo pairing delay + temporal history + safety margin (provisional 2 s);
* managed `GetCameraPose()` se nepoužívá jako authority; C01 změří jeho reprojekční chybu proti nativní lokalizaci.

## 4.4 L/R pairing

Levý a pravý stream nejsou synchronní jen proto, že dorazily ve stejném Unity framu. Evidence: L/R delta kvantovaná na 0 / ±20 ms, 46 % acceptance při „latest-only“, HAL porušuje monotónnost timestampů (measured).

Každý `StereoObservation` obsahuje L/R frame ID, timestamp L/R, pose L/R, skew, motion estimate během skew, intrinsics L/R, extrinsics, exposure metadata pokud existují.

* history ring per eye (ne latest-only), pairing minimalizací joint spread;
* pairing okno = násobek naměřené periody kamery, ne pevných 1/30 s;
* validace monotónnosti timestampů; nemonotónní frame se zahazuje a počítá;
* podle skew: použít přímo / motion-compensate / snížit confidence / odmítnout.

Nikdy neposouvej svět podle receive-time kamery.

## 4.5 Rolling shutter a motion gate

Rolling shutter PCA kamer není dokumentován. C01 změří řádkový čas (nebo prokáže global shutter). C03:

* při známém řádkovém čase řádková kompenzace pose;
* jinak gate podle úhlové rychlosti hlavy: nad prahem (provisional 90°/s) frame není geometry evidence (může zůstat appearance kandidátem s penalizací blur).

## 4.6 Kalibrace a rektifikace

* Meta dává pinhole intrinsics + lens offset pose (official). Distorční model není dokumentován. C01: šachovnice/reprojekční test; residuál nad prahem → FinalScan počítá vlastní distorzní korekci.
* Stereo search běží výhradně na **rektifikovaném páru**. Rektifikační mapy z extrinsics L/R, počítané jednou per profil/rozlišení, aplikované v pyramid stage.
* Baseline L/R se čte z extrinsics za běhu, nikdy hardcoded (§2).
* Exposure/ISO metadata: neověřeno; nic na nich nesmí záviset.

## 4.7 Sensor recording a replay (povinné od C03)

C03 umí zaznamenat sensor sekvenci (PCA L/R, Env Depth, pose ring, timestampy, profily) do souboru a přehrát ji deterministicky na hostu i zařízení. Je to základ pro C11b measurement oracle, host testy a regresní acceptance.

---

# 5. INGEST: TRANSPORT ADAPTERY

Jeden interní typ: `CameraFrameLease` (image handle, timestamp + uncertainty, pose, intrinsics, profil, lease lifetime). Dva sensor transport adaptery do stejného typu; **není to druhý scanner core**.

```
C01-A: nativní Camera2 NDK + AImageReader + AHardwareBuffer probe
       (GPU_SAMPLED_IMAGE, VK_ANDROID_external_memory_android_hardware_buffer,
        VkSamplerYcbcrConversion – obojí na zařízení přítomno, measured)

if funkční s HEADSET_CAMERA + nižší latence/kopie:
    production ingest = zero-copy AHB → VkImage (Y plane) → pyramid
else:
    MRUK/PCA texture path, kopie/import IHNED v producer callbacku,
    kde se latchují metadata (donor lekce RGBD-6)
```

Z existence AHB importu neplyne, že Meta dovolí nativní otevření PCA zařízení. Je to spike, ne předpoklad.

Full 1280² brute-force disparity každý frame je zakázán.

---

# 6. ENVIRONMENT DEPTH

Measured: `320×320×2` per eye, swapchain 4, 25 Hz (22,6–27,3), data age ~21 ms, `XR_META_environment_depth` v2, hand removal supported.

* Depth je **prior a free-space evidence**, ne canonical truth. Je 4× hrubší než PCA.
* Má **vlastní timestamp, pose a fov per eye**. Reprojektuje se do PCA capture pose; nikdy se nepřipojuje k PCA framu jako „depth attached“.
* Acquire přesně jednou za XR frame (donor bug: dvojí acquire → `XR_ERROR_LIMIT_REACHED`).
* Po resume restart subsystému (donor `b34910b`).
* Hand removal ON; pixely „estimated background“ značené low-confidence.
* Nízká confidence nesmí agresivně ničit geometrii.
* C01 charakterizuje **systematickou bias** depth (vs. laser) — plane-fit z tisíců vzorků průměruje šum, ne bias.

---

# 7. MEASUREMENT FRONT-END

Vstupy: PCA L/R lease, Env Depth, pose ring, canonical surface prediction. Výstup: `SurfaceMeasurement[]` (pozice, normála, kovariance, evidence class, zdroj).

## 7.1 Dvě cesty podle informace v obraze

```
TEXTURED / FEATURES
   instantaneous rectified stereo (predicted narrow band)
 + temporal large-baseline stereo (vybrané historické framy)
 → jemná lokální metrická geometrie

LOW-TEXTURE PLANAR
   Environment Depth
 + stereo constraints (kde existují)
 + opakovaný robustní plane fit přes tisíce observations
 → velmi přesný plane offset navzdory šumu jednotlivých vzorků
```

Depth error instantního stereo roste jako z²·σd/(f·b); při krátké baseline nedává sub-cm nad ~1,5 m. Temporal baseline je proto **primární cesta k jemné vzdálené geometrii na texturovaných površích**, ne „optional“. Na bílé zdi ale nemusí najít disparity; tam vládne planar cesta. Scanner nesmí záviset na tom, že zeď má texturu.

## 7.2 Stereo pipeline

```
predicted depth (canonical + Env Depth prior)
→ bounded disparity interval
→ local matching na rektifikovaném páru
→ subpixel refinement
→ confidence + covariance
```

Bez prioru: coarse pyramid → kandidát → local refinement. Full-res refinement pouze na hranách, v DETAIL, při vysoké nejistotě, na high-information pixelech.

## 7.3 Temporal multiview

Klasická geometrická multiview evidence, žádný neural MVS v hot path. Jen vybrané historické framy, bounded work. Použití: vzdálené povrchy, repeat pass, DETAIL, redukce nejistoty.

## 7.4 Uncertainty

Každý measurement nese kovarianci složenou z: disparity σ, timestamp uncertainty (§4.2), pose uncertainty, rolling-shutter residuál, depth prior confidence. Fusion je precision-weighted (§8.5).

## 7.5 Measurement oracle (C11b)

```
recorded sensor sequence (§4.7)
→ deterministický SurfaceMeasurement stream
→ CPU referenční implementace
→ Vulkan result parity (tolerance)
```

Fusion (C12) dostává ověřený measurement interface a neřeší chyby stereo současně s chybami mapování.

---

## 7.6 Information-driven measurement compaction (invariant)

PCA 1280×1280 má ~1,64 M pixelů. Scanner nikdy neposílá všechny pixely × 20 Hz do mapy. Front-end
vybírá bounded počet nejhodnotnějších measurements per tick (provisional 16–64 k), hodnocení:

```
HIGH VALUE: depth discontinuity, vysoká nejistota vůči canonical prediction, nově viděná oblast,
            vysoká curvature, tenký povrch, DETAIL request
LOW VALUE:  konvergovaná rovná plocha, duplicitní observation stejného surfelu ze stejného úhlu
```

Hodnota je funkce canonical predikce (reprojekce FRONT do kamery): kde predikce sedí v rámci σ, pixel
nenese informaci. Tím dostane květinářství víc budgetu než hotová stěna při stejné ceně ticku.
Scan cost tedy závisí na počtu vybraných measurements, ne na počtu surfelů v mapě.

---

# 8. CANONICAL WORLD — METRIC SURFELS

Canonical geometry není TSDF, voxel occupancy, mesh, 3D Gaussian cloud, alpha splats ani neural field. Canonical primitive je **adaptive metric 2D surfel**: orientovaný, anizotropní metrický surface patch.

## 8.1 Candidate ABI (32 B, fixed point) — provisional, ne frozen

```
center_xyz_s16  q0.25mm  page-local      6 B   (±8.19 m kolem page originu)
normal_oct_s16x2                          4 B
tangent_angle_u16                         2 B   (orientace major osy v tangent rovině)
radius_major_minor_u16x2                  4 B   (log encoding)
sigma_n_tmajor_tminor_u16x3               6 B   (log encoding; kovariance je v surfel
                                                 frame diagonální by construction)
evidence_flags_u16                        2 B   (support count, sided/free-space state,
                                                 transient/promoted, removed)
surface_id_u32                            4 B
appearance_handle_u32                     4 B
-------------------------------------------
                                         32 B
```

Důvody:

* fixed point s16 při 0,25 mm dává deterministickou sub-mm reprezentaci bez FP16 variabilního kroku;
* `normal + tangent_angle + major/minor` definuje plný anizotropní orientovaný surfel;
* kovariance diagonální v surfel frame (n, t_major, t_minor) — off-diagonály nejsou potřeba.

C04 porovná 32 B vs 48 B (bandwidth, ALU unpack, fusion accuracy) a teprve pak se ABI zmrazí. Revision/generation žije na úrovni page, ne surfelu.

## 8.2 Adaptivní velikost

Radius vychází z curvature, residualu, lokální diskontinuity, projected depth accuracy, measurement covariance, neighbor coverage. Rovná plocha: několikacentimetrové patches se sub-cm plane localization. Detail: podpora v jednotkách mm, pokud evidence dovolí. Spacing datové struktury není tvrzení o přesnosti. Texturový detail nesmí nutit geometrii subdividovat.

## 8.3 Multi-surface (multi-sheet)

Buňka smí obsahovat více nezávislých surfaces. Nikdy „cell → average one depth“. Tenká zeď = side A || side B; roh = plane A ⊥ plane B. Association podle signed plane distance, normály, visibility ordering, free-space strany, nejistoty. Ne pouze Euclidean distance.

## 8.4 Split / merge

Split: curvature/residual příliš vysoký, diskontinuita, footprint příliš velký vůči evidenci. Merge: koplanární, kompatibilní normála a nejistota, žádná edge evidence, merge nesnižuje required accuracy. Lokální, nikdy page-wide rebuild.

## 8.5 Fusion

```
page address → page-local index → kandidátní surfely → kompatibilita → robust precision-weighted update
```

Při dostatečně odlišné hypotéze: nevynucuj merge, vytvoř druhý sheet.

## 8.6 Free space a dynamika

Lehká free-space evidence oddělená od surfelů, coarse page-local. Ray camera→surface dává free evidence před povrchem. Použití: ghost removal, moved objects, stale geometry, ERASE confirmation. Tenký povrch není odstraněn jedním smoothed-depth miss. Nový kandidát je transientní; promotion až po temporal/static evidence (lidé, ruce, pohybující se objekty se nevypálí).

Husté organické scény vyžadují silnější temporal gating. Každý surfel nese oddělené evidence:

```
staticEvidence     souhlasná pozorování v rámci σ napříč časem
motionEvidence     pozorování mimo σ, konzistentní s pohybem (ne šumem)
geometryVariance   běžný rozptyl residuálů
```

Pravidla: konzistentní → canonical static; nekonzistentní/pohyblivé → transient (renderuje se jen jako
transient overlay, nikdy se nefúzuje do statické geometrie, nevstupuje do exportu); uklidněný list se
později canonicalizuje. Platí pro listy, závěsy, lidi, ruce.

## 8.7 Průhledné, lesklé a kontradiktorní povrchy

Scanner nesmí vymýšlet geometrii. Sklo, celofán, lesklé listy, silná okluze:

```
silná stereo/temporal evidence           → metrická geometrie
slabá / kontradiktorní hloubka           → vysoká nejistota, žádný promoted surfel
pouze appearance evidence                → appearance vrstva, žádný fake surface
```

Environment Depth na skle není authority (§6). Free-space evidence skrz sklo se nezapisuje, dokud stereo
neprokáže průhlednost (kontradikce depth prior vs. stereo → obě strany nejisté).

---

# 9. ADDRESSING: TWO-LEVEL, NE TWO HASHMAPS

## 9.1 Sparse global map (povinná)

```
AnchorID + LogicalPageCoord → PageHash → ResidentPageSlot + generation
```

GPU open addressing s bounded probe count, explicitní overflow, generation protection, zero silent failure, rehash bez frame stallu. Page řádově metry (provisional; ±8,19 m z §8.1 je horní mez).

## 9.2 Page-local index (benchmarked v C08)

Uvnitř resident page **není** dogmaticky druhá hashmapa. C08 benchmarkuje:

```
A) fixed local bucket grid
B) compact Morton directory
C) subgroup-bucketed linear hash (64-wide)
D) robin-hood local hash
```

a vybere nejlevnější pro association + multi-sheet lookup + create/update. Cuckoo je vyloučen pro page-local index (lookup fan-out).

**Adaptivní lokální subdivize (invariant):** bucket nikdy nesmí být „cell → 300 surfelů → prohledat vše“.
Při překročení prahu (provisional 32 surfelů) se bucket lokálně subdivideuje (2×2×2, rekurzivně, max
3 úrovně → 1,5 cm) na micro-buckets; řídké oblasti zůstávají v hrubém bucketu. Je to jen acceleration
index, ne voxelová geometrie; lze kdykoli přestavět z canonical surfelů. Candidate set per measurement je
bounded (provisional ≤ 16 kandidátů; overflow počítán, nikdy tichý).

Renderer tyto struktury per-pixel neprochází; používá compact draw listy (§13).

## 9.3 Stable IDs (invariant)

`AnchorID`, `LogicalPageID`, `ResidentPageSlot`, `SurfaceID`. Fyzický GPU slot není persistent identity. Slot reuse jen s generation increment; stale handle bezpečně odmítnut.

---

# 10. ANCHOR GRAPH A PLATFORMNÍ ANCHORS

Svět není jedna globální float mapa: `AnchorGraph → local Pages → local Surfels`. Anchor drží lokální metrický frame. Loop closure mění `T_world_anchor`, ne miliony souřadnic. Quest tracking je high-rate pose prior; scanner má pomalou correction transform `T_scan = correction · T_quest`. Nepiš vlastní VIO.

```
FinalScan AnchorGraph        = canonical scan localization graph (authority)
Platform Spatial Anchor UUID = external localization observation (hint), ne origin
```

OPEN: zkus platformní anchors → seed AnchorGraph pose → geometrická/obrazová verifikace. Zmizí-li nebo driftne-li OS anchor, projekt existuje dál. Loop system běží low priority: kandidáti (spatial prior, keyframe descriptors, geometry descriptors) → verify (point-to-plane registrace, image consistency) → AnchorGraph optimalizace. Velká korekce: canonical přijme okamžitě, render smí krátce interpolovat (presentation smoothing, ne druhá authority).

---

# 11. IMMUTABLE PUBLICATION (invariant)

Renderer nesmí číst buffer, který scan mutuje. Každá resident page má generation-safe FRONT + inaktivní BACK/COW. Scan píše BACK; po dokončení semaphore → **root-last publish** (nejdřív data, pak generation/root pointer). Není potřeba globální dvojitá kopie mapy. Změna surfelu = sparse update draw recordu, ne rebuild page.

---

# 12. 360° READOUT SPHERE A RESIDENCY

## 12.1 Invariant

Residency není frustum-driven. Kolem uživatele existuje 360° readout sphere. Otočení hlavy nesmí vyvolat disk load, page request, appearance request ani scan rebuild. Head yaw/pitch → jen visibility. Head translation → smí posouvat sphere.

## 12.2 Tři zóny

* **INNER**: 360°, kompletní geometrie, high-quality render representation a appearance mip; radius provisional několik metrů, C07 optimalizuje.
* **WARM**: kompletní geometrie, nižší LOD/mip, připravená na přesun do INNER.
* **PREFETCH**: coarse representation + async read.

Centrum: `predictedPosition = p + velocity · horizon` (bounded). „Full readout“ = v celé INNER existuje okamžitě renderovatelná reprezentace všech známých surfaces; distance LOD dovolen, díra kvůli změně pohledu ne. Canonical se distance nedegraduje; macro surfely/koplanární clustery/nižší mipy jsou derived a zahoditelné.

## 12.3 Movement prefetch

Position + velocity + previous route + adjacency. Při nedostatku bandwidth nejdřív snižuj appearance mip/derived LOD, nikdy nedovol díru v základní geometrii, pokud existuje coarse resident representation.

## 12.4 Turn vs move v telemetry

Counter `orientationResidencyRequests` musí zůstat 0 (invariant, §21.8).

---

# 13. RENDER

## 13.1 Integrace (architektonický default, ne otevřené rozhodnutí)

```
native compute:  cull → LOD → draw list → indirect args
        ↓
published immutable GraphicsBuffer (FRONT)
        ↓
Unity RenderGraph / CommandBuffer: ONE DrawProceduralIndirect (SPI: instance count ×2)
        ↓
Unity vlastní: XR multiview, MSAA, foveation, render pass/subpass, compositor
```

Native nikdy nesahá do Unity render passu. Varianta B (nativní graphics pipeline uvnitř Unity passu přes `CommandRecordingState`) existuje pouze jako **failed-A escape hatch** s explicitním device důkazem, že A nestíhá; nikdy jako paralelní implementace.

## 13.2 Surfel rasterizace

Nepoužívej alpha-composited 3DGS. Adreno/TBDR nesmí platit transparent overdraw.

* Pass 1: perspective-correct oriented ellipse, analytic surface depth, depth write, opaque coverage; early-Z chování s discard na TBDR měřit.
* Pass 2 (volitelný, měřený): ε-depth blend atributů s EWA váhami nad ε ≈ 2σz. Nikdy víc než 2 passy, žádný per-pixel sort.
* Edge AA: MSAA 4× (tile memory), alpha-to-coverage pro footprint.

Gaussian kernel smí řídit coverage/filter; není geometry authority.

## 13.3 Draw path

```
sphere resident pages → frustum cull → optional occlusion → distance LOD → indirect draw
```

Velikost světa na disku není parametr frame cost.

## 13.4 Derived ClusterTree a screen-space LOD (invariant, před fusion)

72Hz renderer nesmí být lineárně závislý na počtu canonical surfelů. Každá resident page udržuje
**derived, disposable** hierarchii nad FRONT surfely:

```
PAGE → cluster → cluster → … → leaf surfel range
```

Cluster node nese jen: bounds (AABB), normal cone (osa + cos úhlu), depth/radius range, coverage
(odhad plochy a plnosti), child range nebo leaf surfel range, representative LOD handle (agregovaný
surfel: střed, normála, poloměr, coverage, barva/appearance). Není to canonical geometrie: lze ji kdykoli
zahodit a přestavět z FRONT (build je bounded PUBLISH/COLD job po publikaci page, ne per frame).

Readout cesta:

```
INNER pages → page cull → cluster traversal (frustum, normal cone, projected screen error)
→ uzel pod prahem chyby: emit representative (coverage LOD)
→ uzel nad prahem: sestup k dětem, leaf: emit skutečné surfely
→ compact visible draw list (bounded: fixed screen-work budget, ne world-element budget)
→ ONE indirect XR draw
```

* Řízení **projected screen error**, ne pevnou vzdáleností: přiblížení hlavy otevře cluster → listy →
  jednotlivé surfely bez disk requestu (vše je v INNER residentní), mění se jen draw list.
* Traversal je GPU-driven (persistent/indirect dispatch), bez CPU sync a bez per-frame rebuildu.
* Budget: provisional 200 k leaf surfelů + několik tisíc agregátů per frame; skutečné číslo určí C05 benchmark.
* Přechod mezi úrovněmi: krátký crossfade coverage nebo hysterezní práh, aby nevznikal pop.

**Coverage-preserving far LOD:** agregát nikdy nenahradí keř neprůhledným diskem. Representative nese
coverage (plnost 0–1) a ve far field se renderuje jako bounded coverage LOD (alpha-to-coverage / hashed
alpha, MSAA), aby zůstaly mezery mezi listy. Near field = skutečné opaque depth-writing surfely. Coverage
agregace se používá jen tam, kde je footprint uzlu malý (provisional < 2 px). Žádný 3DGS overdraw.

Stejné clustery slouží merge/LOD logice a coplanar chart kandidátům (§14).

## 13.5 Headset-aware readout (invariant): využij hloubku a tracking, ne obecný LOD

FinalScan není point-cloud viewer. Je to headset s hloubkou každý frame a s predikovanou pozicí hlavy.
Readout to musí využít:

1. **Occlusion cull proti depth prioru — pouze „prokazatelně za“ (invariant).** Sken se kreslí přes
   passthrough a musí být vidět na místě reálného povrchu: pohled na zeď musí ukázat naskenovanou zeď.
   Depth prior proto **nikdy** nekulluje uzel, který leží v hloubkovém pásu pozorovaného povrchu.
   Pravidlo: uzel se zahodí jen když `nearestDepth(node) > occluderDepth + margin`, kde
   `margin = max(σ_node, 10 cm provisional) + k·|∇depth|`. Co se tím vyhodí: geometrie místností za
   zdí, zadní strany regálů, listy hluboko za bližšími listy. Co se tím nikdy nevyhodí: surfely na
   pozorovaném povrchu ±margin, ani dvě strany tenké příčky. Zdroje occluderu: (a) vlastní depth buffer
   skenu z minulého framu reprojektovaný do predikované pose (chová se jako early-Z), (b) Environment
   Depth (25 Hz, vlastní pose/fov, §6) reprojektované do obou očí, konzervativně (max v dlaždici, ne
   průměr), s marginem rostoucím s gradientem hloubky a s confidence. Env Depth s nízkou confidence
   (sklo, hrany, ruce) není occluder. HZB (hierarchický Z-buffer) se staví z obou zdrojů; test je vždy
   proti pásu, ne proti přesné hloubce. V květinářství je většina surfelů zakrytá bližšími listy o víc než
   margin; tohle je hlavní redukce, ne distance LOD.
2. **Foveace.** Práh screen-space chyby je funkce excentricity v eye bufferu (stejně jako FFR snižuje
   shading v periferii): centrum ~1 px, periferie ~3–4 px (provisional). Geometrický detail v periferii
   nikdo nevidí; budget se soustředí do fovey.
3. **Predikovaná pose + temporální reuse.** Cull běží pro predikovanou display pose s úhlovým marginem
   (provisional 5°). Viditelná množina se mění pomalu: traversal je inkrementální (re-evaluace uzlů na
   hranici frusta / po překročení prahu pohybu), jinak se draw list z minulého framu použije znovu.
4. **Adaptivní screen-work budget.** Z frame timestampů (Phase Sync headroom) se každý frame odvodí
   LOD bias: když GPU headroom klesá, práh chyby roste (méně listů, víc agregátů), nikdy ne vynechaný
   frame. Analogie dynamic resolution, ale pro geometrii.
5. **Jeden cull pro obě oči.** Sjednocené frustum + sdílený draw list (instancing ×2), ne dva traversaly.
6. **Depth prior i pro scanner.** Stejný reprojektovaný canonical depth + Env Depth řídí §7.6: pixel, kde
   predikce sedí v rámci σ, se neintegruje.

**Render módy (invariant):** occlusion podle depth prioru je vlastnost módu, ne světa.

```
SCAN (live)        band-occlusion podle §13.5.1, LOD, sken viditelný na místě reálného povrchu
XRAY / RADAR       žádný depth-prior cull: celý residentní svět v INNER/WARM včetně místností za zdmi,
                   opacity slider, depth test jen mezi surfely; stejná cesta pro zobrazení otevřeného
                   nebo exportovaného modelu ALIGN 1:1 na svět (donor artifact viewer: World Lock,
                   opacity, occlusion toggle) — uživatel musí vidět komplet skenu skrz zdi jako radar
PLAN               půdorys/řez (viewer parity)
```

Přepnutí módu mění jen cull policy a shader, nikdy residency ani canonical data.

Zakázáno: čistý distance-LOD bez occlusion v SCAN módu; occlusion test proti přesné hloubce bez pásu (sken by zmizel na místě reálného povrchu); Env Depth jako occluder bez confidence a marginu; per-eye duplicitní traversal; LOD bez foveace na zařízení s FFR.

---

# 14. APPEARANCE

Tři vrstvy: canonical geometry; captured evidence (keyframes); derived live representation (SurfaceAtlas). Geometrie se nikdy nedensifikuje kvůli textuře.

* **Keyframes**: selektor hodnotí new coverage, displacement, angle diversity, projected texture resolution, sharpness/blur, exposure, DETAIL, relocalization usefulness. Při stání 0 keyframes/s; při scanu jednotky/s. Stationary camera nesmí generovat storage growth.
* **SurfaceAtlas**: lokální k Anchor/Surface charts; albedo, confidence, mipy, optional low-order directional residual. INNER drží tiles residentní v kvalitě, při které otočení nevypadá jako pop (base quality nestreamuje; enhancement smí). C17 spike rozhodne mezi (a) per-page planar cluster charts a (b) per-surfel texel tiles; default (a).
* **View-dependence**: SH1 / několik bounded lobes jen tam, kde měření prokazují view dependence. Raw keyframes zůstávají authority pro export, rebake, DETAIL, relocalization. Live shader má bounded počet texture samples.
* **Current-view enhancement**: volitelný, nesmí být nutný pro úplnost; cache miss nesmí vytvořit pop; crossfade jen z kvalitního atlasu na lepší.
* Radiometrická normalizace nesmí záviset na exposure metadatech (§4.6).

---

# 15. NATIVE EXECUTOR

## 15.1 Unity je host

Unity: XR lifecycle, UI, tools, interakce, scene composition, importované modely, vydání jednoho indirect draw. Hot scanner: native C++/Vulkan. C# neskládá dispatch zoo; scan tick není 30 plugin eventů.

## 15.2 Device a queue acquisition

* `IUnityGraphicsVulkanV2::AddInterceptInitialization` s prioritou, která nechá běžet Meta OpenXR interceptor; hook `vkCreateDevice`.
* Measured topologie: family 0 = graphics|compute|transfer, 4 queues, 4 priority levels; family 1 = 1 queue; **žádná compute-only family**. Unity vytváří 1 queue ve family 0.
* Injekce druhé queue ve family 0 (donor pattern) **s fallbackem na původní create info** (OtherScan pattern). Nikdy tvrdé vynucení extensionů, které způsobí selhání device creation.
* `VK_EXT_global_priority` přítomno: C01 změří efekt LOW priority scan queue.
* C01 změří **skutečný overlap** obou queue na jedné časové ose. Pokud overlap neexistuje nebo snižuje render stabilitu: logické oddělení + bounded scheduling na jedné queue. Architektura musí fungovat i bez druhé queue.
* Žádný family ownership transfer (stejná family).

## 15.3 Synchronizace

Timeline semaphores + synchronization2 (obojí přítomno, measured) + generation counters. Persistent descriptors, persistent per-resource import (ne per-job image views/descriptor pooly). Push constants pro per-dispatch parametry (ne runtime hashing názvů uniformů). Žádný `vkQueueWaitIdle` ani `vkDeviceWaitIdle` v runtime (jen shutdown).

## 15.4 Deadline-aware GPU scheduler

Jeden nativní scheduler, ne jeden `_activeJob`:

```
P0  PUBLISH            sub-ms page-local mikrojoby
P1  SCAN               quantum 1–3 ms (provisional), max 2 in flight
P2  INNER RESIDENCY    preemptuje appearance/cold
P3  APPEARANCE         1–2 ms bounded pieces
P4  COLD / LOOP / COMPACTION   frame-paced slices (OtherScan pattern)
```

Každý job: `class, deadline, maxGpuQuantumUs, dependencyTimeline, resourceLeaseSet, continuationCursor`. Každý stage: bounded item count, continuation, explicitní overflow, resumable. Scan quantum se submituje do idle gapu po Unity frame submit. Nestihne-li scan budget, pokračuje dalším quantem; render se nezastaví. Readout není job (§3.1).

## 15.5 Job lifecycle invarianty (z `9cfed011`, OtherScan, SimpleScan)

1. Všechny sync objekty, na které lze job pollovat, existují **před** jakýmkoli `AccessBuffer/AccessTexture` (ty zapisují bariéry do Unity streamu i při pozdějším selhání validace).
2. Stav jobu publikuje výhradně vlastník fáze, přesně jednou; helpery jen zaznamenávají chybu. Terminální stav není tranzientní.
3. Job, který sáhl na Unity resource, nemůže být `FailedSafe`, dokud graphics fence neprokáže retirement.
4. Cross-thread pole jsou atomics nebo pod mutexem; žádný lock-free poll spoléhající na C# disciplínu.
5. Každý indirect/argument layout má jeden generator-derived zdroj pravdy sdílený C#, HLSL a native; žádné numerické literály offsetů v pluginu.
6. `VK_ERROR_DEVICE_LOST` má explicitní cestu (quarantine + rebuild), ne generic failure.

## 15.6 Kernel envelope (temp build gate, fail-closed)

```
SPIR-V per pipeline   <= 128 KiB      (provisional; C01 link-envelope smí zpřísnit/uvolnit,
                                       nikdy zpět k MB shaderům)
writable bindings     <= 8
groupshared           <= 16 KiB
threads / workgroup   <= 256
žádná unbounded smyčka
žádný dynamický index do vektoru (Adreno)
```

Invariant: **jeden shader = jedna bounded paralelní transformace**, ne scheduler skrytý v HLSL. Donor evidence: 0,75–1,6 MB SPIR-V, link fail -13, cold link 226 s, drain zabit po 270 s.

## 15.7 Pipeline binaries

`VK_KHR_pipeline_binary` je na zařízení (measured). Workflow: CAPTURE na golden device/driver → bundle → release. Klíč: `pipelineBinaryUUID` + driver version. Nekompatibilní binary → fallback `VkPipelineCache` (Unity `UnityVulkanInstance.pipelineCache` k dispozici) + SPIR-V kompilace na worker threadu **během warm-up obrazovky před povolením scanu**. Nikdy runtime compile spike během scanu, nikdy „no fallback“, nikdy force extensions při device creation.

## 15.8 GPU timestamps

Každý native stage má device timestamps (period 52,083 ns, 48 bitů, measured). Bez `VK_KHR_calibrated_timestamps` (chybí, measured) se GPU↔CPU/XrTime korelace dělá submit-time bracketingem. Per-owner query rangy. Telemetry je observační: nikdy neinjektuje práci do měřené cesty.

---

# 16. MEMORY GOVERNOR

* PSS limit: 5,75 GiB (official).
* Baseline po XR startu s passthrough + PCA L/R: 1,73–1,87 GB PSS, Graphics 1,33–1,43 GB (measured, m16).
* **Pracovní budget provisional ≤ 3,5 GB** pro svět + atlas + scratch, s rezervou na fragmentaci. C01 změří per-komponentní baseline (XR / +passthrough / +L / +L+R / +Env Depth / +UI / +plugin).

Priorita residence: visible/readout-critical geometry → celá INNER geometry → INNER appearance → WARM geometry → scan scratch → nearby appearance → prefetch → DETAIL/relocalization caches. Raw keyframes patří na storage. Povinné: keyframe pruning, komprese, virtual/streamed atlas, mip hierarchy, tile budget, duplicate view removal.

---

# 17. PERSISTENCE A OPEN

Canonical: AnchorGraph, surfel pages, free-space, SurfaceIDs, annotations, model instances, keyframe refs, platform anchor observations. Storage: journal → checkpoint → compaction; autosave průběžný; crash nezničí durable scan; COW durable pages. OPEN načte manifest, AnchorGraph, page index, lokální readout sphere; zbytek podle residency. Nikdy „načti celý dům“.

---

# 18. TOOLS

## 18.1 Scan UX (čím je lepší než SimpleScan)

Guidance coverage/quality (kde chybí evidence, kde je vysoká nejistota), DETAIL jako viditelný lokální stav, stabilní obraz bez popu, okamžitý OPEN, profil kamery a thermal stav viditelný, ale ne rušivý.

## 18.2 DETAIL

Lokální request měnící scheduler priority: více full-res stereo, temporal baseline, menší surfel support, více observations, více appearance samples, profil `DETAIL_60`. Nikdy globální hustota.

## 18.3 ERASE

Canonical deletion authority: surface marked removed, render removed, appearance association retired, export nesmí resurrect. Nový scan smí později vytvořit nový surface.

## 18.4 Notes / paint / measurements / models

Přeneseno funkčně (§1.1), vázáno na stabilní identity: note = `AnchorID + SurfaceID` nebo local anchor; paint = surface/chart coordinates; model instance = `AnchorID + transform`. Loop closure anotace neposune špatně.

## 18.5 Export

```
AnchorGraph optimized → surfel sheets → surface constraints → edge/corner extraction
→ adaptive mesh → UV → raw-keyframe multiview bake → paint overlay → GLB/glTF (+ 3D Tiles)
```

Velké scany streaming export.

---

# 19. LIFECYCLE

Sleep/wake, tracking loss, reopen: Env Depth restart po resume, single acquire per frame, anchors on wake, nativní joby při pause dokončit nebo quarantine (nikdy „nechat běžet“), device-lost cesta.

---

# 20. OBSERVABILITY

Řetězec: PCA frame → stereo pair → accepted measurement → SurfaceMeasurement → page lookup → page-local lookup → surfel create/update/delete → publication → resident draw → visible surfel count → appearance sample → presented frame. Musí jít přesně zjistit, kde data zmizela. Counters `orientationResidencyRequests`, `timestampNonMonotonic`, `clockGatePath (A/B/C)`, `captureProfile`, `scanTickSkipped`, `publishGeneration`.

**Device receipt rule (invariant):** žádné tvrzení o výkonu bez GPU timestampů na stejné časové ose jako Unity frame; žádný řez C09–C13 není přijat bez nich. UI „Active“ ≠ GPU mutace.

---

# 21. HARD ACCEPTANCE

1. **Sensor**: měřená cadence per profil; korektnost při 30; clock gate cesta doložená; delayed frame integrován capture pose; rychlý pohyb hlavy nerozmaže statickou geometrii.
2. **Readout**: 72 Hz během i po scanu; rychlé ±180° a 720° rotace; zero geometry pop, zero black tiles, zero residency request z rotace, zero scan work z rotace.
3. **Scan cadence**: ~20 Hz kde workload dovolí; no backlog; latest observation; render nikdy nečeká.
4. **Geometry s ground truth**: scény flat wall, 90° roh, doorway, thin partition obě strany, schody, šikmá zeď, křivý objekt, textured, white wall, relief DETAIL. Pravda: laserový dálkoměr, kalibrační box, tištěný planární target, opakovaný průchod A/B stejnou trajektorií. Metriky: repeat-pass consistency, thickness, plane RMS, edge position, false double surfaces, residual uncertainty.
5. **Large world**: více místností/pater, disk >> resident budget, frame cost neroste s velikostí.
5b. **Dense scene (květinářství)**: syntetický i reálný test s ≥ 3 M canonical surfelů v INNER; frame cost
   roste jen s viditelným screen-work budgetem, ne s počtem surfelů; přiblížení otevírá detail bez disk
   requestu; far LOD zachovává mezery (coverage vs. ground-truth siluety); pohybující se listy nevytvoří
   „kaši“ (transient, ne statická fúze); sklo/celofán bez fake surface.
6. **Memory**: 1 h run, bounded steady regime; žádný GPU cache leak, decoded image leak, staging leak, tombstone explosion, allocator high-water bez reclamation.
7. **GPU**: graphics / scan / transfer měřeno odděleně; druhá queue přijata jen s prokázaným benefitem.
8. **Turn vs move**: `orientationResidencyRequests == 0`.
9. **Thermal**: 5/15/30/60 min; scheduler degraduje acquisition profil, nikdy readout.

---

# 22. ZAKÁZANÉ CESTY

camera receive-time pose; tiché „exact“ camera→XrTime bez gate; unlimited camera backlog; reconfigure kamery při každém pohybu; frustum-driven INNER residency; disk load při otočení; page-wide rebuild; render mesh jako canonical authority; TSDF; marching cubes live; alpha 3DGS overdraw; nativní draw uvnitř Unity render passu jako default; PyTorch/CUDA/neural MVS v hot path; global DETAIL; full-world RAM load; synchronní readback/disk; `vkQueueWaitIdle`/`vkDeviceWaitIdle` v runtime; jeden globální job slot; C# dispatch zoo; runtime pipeline compile spike; force extensions při device creation; SPIR-V nad gate; silent hash overflow; renderer lineárně závislý na počtu canonical surfelů; per-frame rebuild cluster hierarchie; far LOD jako neprůhledný agregát bez coverage; stale physical-slot handles; hardcoded baseline/extrinsics; FP16 canonical center; druhá page-local hashmapa bez benchmarku; source-text testy jako jediná verifikace; dva agenti v jednom checkoutu.

---

# 23. NEGATIVNÍ LEKCE (povinné v C00 ledgeru, každá s citací)

Nativní vrstva: hard-coded argument-buffer ABI (`<64u`, `16u/32u` vs. 48 B; literály přežívají i po fixu); `vkGetFenceStatus(NULL)` SIGSEGV; `FailJob` mění stav během prepare; lock-free `PollJob` vs. mutexed destroy; force extensions → device creation fail; BINARY_ONLY bez fallbacku; monolitické SPIR-V; per-job views/pools/uniformy bez push constants; draw intercept závislý na Unity 6000.5 render-pass sémantice; queue injection bez fallbacku; jeden `_activeJob` (readout 42–99 ms wall, 54 ms fronta, 6,2 Hz observation); dlouhý readout job blokující world work; resubmission job trvale obsazující queue; heavy path skrytá před validatorem.

Unity/host: `GraphicsFence AsyncQueueSynchronisation` → `NotSupportedException` každý frame; čtení producer-owned textur mimo callback; latest-only PCA (46 % acceptance); dvojí Env Depth acquire; Env Depth mrtvý po resume; výjimka mimo try/catch drží lease navždy; 8 UAV limit; dynamický index do vektoru; SPI instance count.

Proces: tvrzení bez device receiptu (`7ec07b0`); telemetry injektující práci (`0770fd2`); sdílený timestamp range (`f2cfe26`); source-text testy; dva agenti v checkoutu; zastaralé docs a mrtvá konstanta v persistence headeru; HUD „Active“ s nula integračními invokacemi.

---

# 24. COMMIT DAG

```
C00   chore(genesis)      inventář 3 větví + host projekt; VULKAN_DONOR_LEDGER (3 sloupce);
                          FEATURE_PARITY z §1.1; negativní lekce §23 s citacemi; risk register;
                          v4 jako autorita; SOTA ledger
C01   feat(platform)      HARDWARE + SENSOR + VULKAN SPIKE (§26); build/install/run na zařízení
C02   feat(vulkan)        nativní host: intercept s fallbackem, queue probe, deadline scheduler
                          P0–P4, fence ringy, persistent descriptors, push constants, timestamps,
                          error/device-lost lifecycle, sleep/wake, pipeline cache + binary
C03   feat(sensor)        sensor authority: XrTime, clock gate A/B/C, pose ring, L/R ring + pairing,
                          monotonicity, rolling-shutter gate, capture profily, recording/replay
C04   feat(geometry)      surfel candidate ABI (32 vs 48 B benchmark), page serialization,
                          syntetický world generátor
C05   feat(render)        Unity-issued indirect draw nativních draw listů, ellipse raster pass 1,
                          MSAA, SPI, LOD skeleton; měřeno na syntetickém světě
C05b  feat(render)        derived ClusterTree per page, screen-space error traversal, coverage far LOD,
                          HZB occlusion (prev depth + Env Depth), foveated error threshold, temporal reuse,
                          adaptive budget from GPU headroom; dense synthetic world benchmark (≥ 3 M surfelů)
C06   feat(render)        immutable page publication FRONT/BACK, root-last publish (+ cluster rebuild job)
C07   feat(residency)     INNER/WARM/PREFETCH na syntetických pages, movement, spin test → zero turn pop
C08   feat(addressing)    PageHash + page-local index benchmark A–D, adaptive bucket subdivision, generations, overflow, host testy
C09   feat(measurement)   Env Depth prior: vlastní čas/pose/fov, reprojekce, bias charakterizace
C10   feat(measurement)   rektifikace + instantaneous narrow-band stereo + subpixel + covariance + information-driven compaction (§7.6)
C11   feat(measurement)   temporal baseline + planar low-texture path
C11b  feat(measurement)   measurement oracle: recorded sequence → CPU reference → Vulkan parity
C12   feat(mapping)       surfel fusion: association, create/update, split/merge, multi-sheet
C13   feat(mapping)       free space + static/motion evidence + transient gating + transparency rules (§8.6–8.7)
C14   feat(residency)     disk-backed streaming, journal, COW pages
C15   feat(memory)        governor na naměřených watermarkách
C16   feat(appearance)    keyframe authority
C17   feat(appearance)    SurfaceAtlas (chart spike a/b), mips, pruning, normalizace
C18   feat(appearance)    bounded directional residual
C19   feat(mapping)       AnchorGraph + platform anchors jako observations
C20   feat(mapping)       loop closure / relokalizace
C21   feat(lifecycle)     sleep/wake/tracking loss/reopen
C22   feat(ux)            scan flow + quality guidance
C23   feat(tools)         DETAIL + ERASE
C24   feat(annotation)    notes / paint / measurements / plan view
C25   feat(models)        object library, ALIGN, artifact viewer parity
C26   feat(storage)       transactional autosave, recovery, OPEN
C27   feat(export)        constrained adaptive mesh
C28   feat(export)        multiview bake + GLB/glTF + 3D Tiles
C29   feat(parity)        zero unexplained missing
C30   perf(vulkan)        overlap, indirect, binaries, zero hidden sync, exact timestamps
C31   perf(residency)     1 h unlimited-world
C32   release             production acceptance §21
```

Řezy C09–C13 nejsou přijaty bez device timestampů (§20).

---

# 25. ROZHODNUTÍ

Rozhodnuto 2026-09-14:

* zařízení: Quest 3 / 3S = jeden HW, žádné rozlišování (§2);
* Horizon OS: nejnovější k datu vývoje (§2);
* ORT object reconstruction: mimo scope (existuje jen na historické větvi);
* ingest: C01-A spike, adaptery do `CameraFrameLease` (§5);
* render integrace: varianta A default, B jen escape hatch (§13.1);
* surfel center: fixed point s16 q0.25 mm, 32 B candidate (§8.1);
* addressing: two-level, page-local index benchmarked (§9);
* capture: profily, ne nervózní přepínání (§3.2);
* clock: gate A/B/C (§4.2);
* geometry: dual path textured/planar (§7.1);
* platform anchors: observation, ne origin (§10);
* kernel gate 128 KiB provisional (§15.6);
* C11b measurement oracle přidán.

Nic dalšího není otevřené. Další rozhodnutí vznikají jen z device evidence C01.

---

# 26. C01 MUSÍ ZMĚŘIT

Sensor: app-side fps L, R, L+R při 30/60, 1280×960 vs 1280×1280, za dobrého světla i low-light; capture→app latency; `SENSOR_INFO_TIMESTAMP_SOURCE` a jitter převodu na XrTime; reprojekční chyba pose@capture (managed vs nativní) při rychlém yaw; rolling shutter řádkový čas; distorční residuál; rektifikační chyba; baseline z extrinsics; Env Depth cadence, latence, bias vs laser; Camera2 NDK zero-copy dostupnost a latence vs MRUK; thermal profil 15 min per capture profil.

Vulkan: queue families, counts, overlap druhé queue na jedné časové ose, efekt global priority LOW, vliv na Unity frame time; extension seznam; timestampPeriod; `pipelineBinaryUUID` a chování po OS update; kernel link envelope (největší SPIR-V pod 1 s / 5 s); raw empty submission cost; PCA texture access; pyramid; stereo kandidát; surfel render syntetického světa; page hash access; transfer overlap.

Memory: PSS per komponenta (XR / +passthrough / +L / +L+R / +Env Depth / +UI / +plugin).

Z toho vznikne `PERFORMANCE_BUDGET.md` a zmrazí se `FINALSCAN_ARCHITECTURE_V1.md`.

---

# 27. PRINCIP, KTERÝ NESMÍ BÝT PORUŠEN

FinalScan není „kamera vytvoří drahý model, který se pak nějak zobrazí“.

Je to: **pomalejší, správně datovaná evidence aktualizuje přesný explicitní surfelový svět, zatímco rychlý renderer pouze čte poslední kompletní residentní snapshot.** Scanner smí mít 50–100 ms information latency. Renderer nesmí mít scan latency.

Hlavní problém donorů nebyl nedostatek matematické sofistikace, ale špatné execution boundaries, timestamps, lifetime, residency a publication. v4 je proto především kontrakt o hranicích.

---

# 28. START INSTRUCTION

Nezačínej implementací surfelu. Pořadí: C00 (audit je hotový v `FINALSCAN_V3_REVIEW.md`, zbývá ledgery a parity soubor) → C01 device spike → zmrazit `FINALSCAN_ARCHITECTURE_V1.md` → C02 → … → C32. Neptej se po každém řezu na schválení; zastav se jen u rozhodnutí v §25 a u destruktivních akcí. Pokud device evidence vyvrátí předpoklad, změň scheduling nebo representation tam, kde je problém. Nezaváděj compatibility fallback ani druhý scanner core.
