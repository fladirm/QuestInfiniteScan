# M8 SOTA finalization — ověřený realizační plán

> Current production closure authority: [`contr.md`](../contr.md). Read and follow it verbatim; it supersedes conflicting historical notes in this file.

Tento soubor je realizační mapa pro `kontrakt.md`. Není novou specifikací.
Při rozporu vždy vítězí `kontrakt.md`, zejména jeho dodatek
`R0 SPEC CLARIFICATION`.

## Základ a dvoukolová brána

```text
branch       fix/merkaba-runtime-root-causes
base HEAD    c113ab70423691107364862e822bc7bccfdb435f
world truth  pouze M8 KernelState
```

Kolo 1 je pouze zmrazení kontraktu a tato deterministická mapa. Produkční
runtime/shadery se v něm nemění. Kolo 2 smí začít až po úspěchu těchto bran:

```text
full Merkaba EditMode suite                  PASS
Quest SPIR-V / descriptor / alias audit      PASS
git diff --check                             PASS
každý CUT má vyjmenované production files,
ABI, bariéry, paměť, acceptance a commit     ANO
```

Ověřený baseline nad base HEAD:

```text
Tools/unity/run_merkaba_tests.sh              PASS
Tools/shaders/audit_merkaba_compute_spirv.sh  PASS (47 kernels)
Quest writable storage/UAV per kernel         <= 8
```

Každý produkční CUT je jeden samostatný čitelný commit. Po každém CUTu:

```text
focused tests
full Merkaba suite
Quest SPIR-V audit
git diff --check
static absence nahrazené cesty
```

Žádné fallbacky, compatibility větve ani dvě současné autority. Historické
`.codex/*` soubory nejsou autorita pro tento pursuit.

## DAG a nezávislost

```text
S4 -> S5 -> C1 -------------------------+
                                         |
R0 -> R1 -> R2 -> Q1 -------------------+-> D1 -> [E1 pouze z dat]
                                         |
T1 -> T2 --------------------------------+
                                         |
S4 -> F1 -> F2 -------------------------+-> FINAL
```

Implementační pořadí zůstává pořadím kontraktu:

```text
S4, S5, C1, R0, R1, R2, Q1, T1, T2, D1,
E1 pouze pokud jej vyžádají device data, F1, F2, FINAL.
```

Jediná výjimka pro postup: pokud R0 odhalí po předepsaných same-sheet
pravidlech skutečně více matematicky platných výsledků pro minimální
convex/concave/T pattern, R0 vypíše tento minimální pattern jako svůj jediný
explicitní blocker. S4/S5/C1/T1/T2/F1/F2 se kvůli tomu nezastavují. R1/R2/Q1
čekají na uzavřený R0 oracle.

---

## CUT S4 — JOINT IDENTITY

### Ověřený současný defect

`StereoRgbdRefine.compute` už vytváří jediný joint four-stream depth/normal
field na deterministické depth-L pixelové lattice. Identita se ztratí až v
`MerkabaIntegration.compute`:

```text
Discover P -> H,N -> K
AppendSurfaceCandidate ukládá pouze K
Route K-center -> P' -> H',N'
Commit K-center -> P'' -> H'',N'',RGB
```

`QueueResolvedSurfaceCandidates` navíc používá `.z` candidate bit jako
first-invocation-wins dedup.

### Production scope

```text
Runtime/Shaders/MerkabaIntegration.compute
Runtime/Merkaba/MerkabaIntegrator.cs
Runtime/Merkaba/MerkabaGrid.Gpu.cs
Runtime/Merkaba/MerkabaGrid.cs             pouze buffer ownership/API
Runtime/Shaders/MerkabaWorld.hlsl          pouze ABI/helpers, je-li nutné
```

Testy pouze v existujícím Merkaba test assembly. Nahrazené string-contract
testy se přepíší na nový kontrakt; nebudou zachovávat mrtvou cestu.

### Attempt-local measurement ABI

`_M8SurfaceCandidates` zůstane `int4` a 16 B:

```text
xyz = current routed/target canonical K
w:
    bits  0..11  source pixel X
    bits 12..23  source pixel Y
    bits 24..    route, authority, replacement/off-axis flags
```

Všechny route helpery musí při změně route/authority zachovat pixel 24 bitů.
Limit 4095 na osu už odpovídá existujícímu maskování.

`_M8SurfaceQueue` se změní z `uint` na `uint2`, stride 8 B:

```text
x = physical kernel key
y = packed source measurement metadata včetně původního P
```

Kapacita zůstane 1,048,576 položek; buffer naroste 4 -> 8 MiB.

### Deterministický winner bez nové geometrické autority

Vznikne jediný attempt-local scratch rank pro každý physical kernel, rozdělený
stejně jako KernelState do čtyř bank:

```text
4 banks × 4,194,304 uint × 4 B = 64 MiB celkem
jedna banka                         16 MiB
```

Není persistovaný, není KernelState a není world truth. Inicializují se pouze
targety přítomné v immutable candidate listu; není povolen globální clear.

Stávající first-thread-wins kernel se nahradí třemi 64-lane kernely:

```text
InitializeSurfaceWinners
    validní HOT target -> rank[target] = UINT_MAX
    unresolved counting/load request/touched bookkeeping právě jednou

SelectSurfaceWinners
    z P znovu načte přesně H,N z immutable joint textures
    InterlockedMin rank[target] deterministickým rankem

QueueSurfaceWinners
    pouze candidate s rank == rank[target] nastaví .z a appendne uint2
```

Dispatch boundaries jsou device-order bariéry. Žádný spin a žádný divergentní
return před group barrierou.

Rank lower-is-better, přesně:

```text
bits 31..30 authority order: REVISION, SUPPORT, DISCOVERY, invalid
bits 29..26 endpoint-to-target residual bucket, lower first
bits 25..24 incidence bucket, stronger first encoded lower
bits 23..0  packed pixel index, lexicographic final tie-break
```

Residual se normalizuje současným half-supportem; incidence používá už
existující joint H/N, nevytváří novou confidence ani evidence. Quantizační
hranice budou jednou C# authority/test oracle a byte-identický HLSL helper.

Candidates různých target K nemusejí být v globálně stabilním queue pořadí:
mutují disjunktní canonical owners. Identita vítěze každého K stabilní být musí.

### Retry a all-or-nothing

Persistentní per-observation candidate list, `.z` bits a surface queue zůstávají
latched přes unresolved attempts. Winner scratch se může při attemptu znovu
odvodit, protože candidate set a sensor textures jsou immutable. Již zařazený
K se díky `.z` nepřidá podruhé. Existing unresolved SURFACE+CARVE gate před
canonical mutation zůstává beze změny.

### Descriptor/barrier budget

Readonly world/sensor data se binduje SRV aliases. RW pouze tam, kde kernel
skutečně zapisuje. Očekávané writable storage:

```text
InitializeSurfaceWinners  rank banks 4 + counters/tile bookkeeping <= 8
SelectSurfaceWinners      rank banks 4                         <= 4
QueueSurfaceWinners       queue/tile bits/counters             <= 4
```

Skutečný SPIR-V descriptor footprint, ne počet HLSL declarations, je acceptance.
CPU/HLSL stride test musí dokazovat candidate 16 B a queue 8 B.

### Acceptance

```text
Discover P == Route P == SurfaceCommit P == RGB P
flat wall, 45° wall, discontinuity, doorway, thin pole
negative coords a tile/chunk/block hranice
permutace candidate scheduling -> byte-identický winner
žádný production call TrySurfaceMeasurementAtKernel v S4 cestě
žádná first-thread-wins winner semantika
```

Commit: `fix(m8): preserve joint measurement identity`

---

## CUT S5 — SAME-RAY CARVE

### Ověřený současný defect

`ObserveJointDepth(globalCoord, worldPosition, ...)` promítá střed starého K
do joint texture a z výsledku vytváří nový classifier výrok. Chybí exact ray
tube a invariant `FREE never behind endpoint` není vyjádřen jedním frozen M.

### Production scope

```text
Runtime/Shaders/MerkabaIntegration.compute
Runtime/Merkaba/MerkabaIntegrator.cs     jen binding, bude-li nutný
```

Evidence scales, hysteresis, Q_SCAN, NEEDS_CARVE, RGB a S4 ABI se nemění.

### Exact classifier

Joint texture je na referenční depth-L raster lattice, ale její H/N je výsledek
všech čtyř povinných streamů. Geometricky konzistentní frozen measurement je:

```text
P = source pixel v joint field
O = frozen reference depth-eye origin pro tento raster
H = joint endpoint načtený z P
R = normalize(H - O)
N = joint normal načtená z P
Kr = canonical(H)
```

CARVE dál sparse enumeruje existující carve-active K. Projekce center(K) smí
pouze nalézt candidate P v immutable joint field; nesmí vytvářet/requantizovat
jiný endpoint. Pokud je nutné bounded okolí projektovaného P, vybere se M s
nejmenší kolmou vzdáleností k ray tube, pak packed P jako tie-break.

Pro `C=center(K)`:

```text
t       = dot(C - O, R)
closest = O + t*R
perp    = length(C - closest)

FREE pouze když:
    mutation authority platí
    t > 0
    t < length(H-O) - HalfSupport
    perp <= HalfSupport
```

Za H je vždy UNKNOWN. Replacement je výhradně `canonical(H)` téhož M.
Current-observation `.z` SURFACE precedence a `OFF+1` replacement-continuity
clamp zůstávají. RGB se při ordinary FREE nemaže.

### Acceptance

```text
FREE používá immutable P/H/O/R
FREE nikdy za H ani mimo ray tube
sameObservationConflict raw == 0
surface candidate téže observation nikdy neFREEuje
background replacement vznikne před zánikem foreground owneru
negative/boundary patterns byte-identické
```

Commit: `fix(m8): carve along the frozen joint ray`

---

## CUT C1 — CARVE BROAD PHASE

### Production scope

```text
Runtime/Shaders/MerkabaIntegration.compute
Runtime/Merkaba/MerkabaIntegrator.cs
Runtime/Merkaba/MerkabaGrid.cs            jen uniform IDs, bude-li nutné
```

### Exact scope

`M8ScanChildMask` se změní pouze z distance mask na konzervativní:

```text
max update distance
AND common depth coverage
AND outer automatic mutation cone
```

Obě frozen depth-eye outer coverage volumes se na CPU jednou převedou do
grid/kernel-space planes. Hierarchie testuje child AABB přímo proti těmto
planes. Neprovádí GridToWorld na každý node.

Broad phase nesmí číst ani rozhodovat:

```text
evidence, FREE, replacement, owner migration,
PCA photometry, surface normal/incidence
```

Jeho jediný důkaz je „exact S5 mutation zde nemůže nastat“. Exact classifier
zůstává S5.

### Acceptance

Brute-force oracle porovná množinu exact-authoritative kernelů proti hierarchy
výstupu pro translation/rotation, negative coords a všechny boundary levels:

```text
0 false negatives
canonical mutation byte-identická bez a s broad phase
exact carve evaluations řádově klesnou
IntegrateCarve device target výrazně pod 1 ms bez změny výsledku
```

Commit: `perf(m8): bound carve query by mutation coverage`

---

## CUT R0 — OVERLAP-SHELL CPU ORACLE

### Scope a autorita

Žádný GPU readout shader se v R0 nemění. Vznikne jedna C# source-of-truth
autorita/oracle a její adversarial testy, například:

```text
Runtime/Merkaba/MerkabaOverlapShell.cs
Tests/EditMode/MerkabaOverlapShellTests.cs
```

Oracle je disposable readout matematika. Nemění KernelState a nic neukládá.

### Zmrazená geometrická interpretace

```text
support                 50 mm overlapping local support, nikdy solid
lattice/sample pitch    25 mm
ordinary MAIN footprint 25 × 25 mm
corner tangent offset   ±12.5 mm
ordinary output         přesně 1 zero-thickness patch = 2 triangles
```

MAIN je canonical occupied surface owner. FREE/UNKNOWN nejsou surface donors.
FREE pouze určuje jedinou známou stranu a winding. UNKNOWN nevytváří backside.
Topologie/winding nečte camera/eye/frustum/head pose.

### Deterministická formalizace subvalues

R0 nejdřív formalizuje, nikoli odhaduje, tyto kroky:

1. Ze signed immediate 26-neighbourhood odvodí local free-side signature.
   Dominant chart/axis a tie se volí pevnou axis/lexicographic tabulkou.
2. Pro zvolený normal chart určí canonical tangent axes a čtyři globálně
   adresované half-lattice rohy patchu.
3. Každý corner sdílejí právě čtyři immediate tangent columns. V každé column
   může být donor pouze canonical occupied owner kompatibilní same-sheet větve,
   v rozsahu MAIN normal coordinate ±1 lattice step.
4. Známý FREE separator mezi MAIN a donor větví donor zakáže. Normal-direction
   neighbour není automaticky donor.
5. Každá column vybere nejbližší kompatibilní owner; tie je canonical
   lexicographic. Dvě paralelní branches se nikdy neprůměrují dohromady.
6. Corner normal height se z contributor setu odvodí přesnou integer/rational
   funkcí zmrazenou v oracle. Souřadnice se reprezentují v quarter-lattice
   jednotkách (6.25 mm), takže ±half-step je přesně ±2 a sdílený contributor
   set dává bitově stejný vertex bez komunikace.
7. Winding je canonical směrem od známé FREE strany. V numerickém tie rozhodne
   pouze frozen lattice-axis/lexicographic rule.

Konkrétní integer corner reduction (včetně zaokrouhlení) není rozptýlený shader
detail: R0 ji zmrazí jednou v C# oracle a exhaustive permutation testu. Teprve
po důkazu se z ní generuje HLSL.

### Shared-vertex proof

Half-step corner má canonical address složenou z:

```text
normal chart/signature branch
global doubled/quarter-lattice corner coordinate
canonical contributor-column set
```

Dva sousední patches se stejnou branch a adresou musejí nezávisle vypočítat
bitově stejnou position i RGB contributor result. Test permutuje evaluation a
donor enumeration order.

### Povinné acceptance

```text
one flat plane                  jedna zero-thickness sheet
translated plane               byte-identický relative output
45° a arbitrary quantized slope continuous shared corners
two parallel sheets            zůstanou dvě
thin partition                 obě strany zvlášť
convex/concave corner          bez bridge přes FREE/empty separator
T junction                     deterministic branches
isolated/missing neighbour     deterministic, bez view fallbacku
tile/chunk/block boundary      stejný output jako uvnitř tile
negative coordinates           translation identity
no distance-two bridging
single shared ownership
```

Pokud po přesném použití těchto pravidel zůstane více matematicky platných
výsledků, test vytiskne jediný minimální lattice pattern jako R0 blocker.
Nevznikne view heuristic, persistent normal, surfel ani nový state. Ostatní
nezávislé CUTy pokračují.

Commit: `test(m8): freeze the overlap-shell oracle`

---

## CUT R1 — ADRENO OVERLAP-SHELL

### Production scope

```text
Runtime/Shaders/MerkabaReadout.compute
Runtime/Shaders/MerkabaOverlapShell.generated.hlsl
Editor/MerkabaOverlapShellGenerator.cs       nebo existující generator pattern
Runtime/Shaders/MerkabaGrid.shader            pouze raster side contract
Runtime/Merkaba/MerkabaGridRenderer.cs        pouze dispatch shape/binding
```

HLSL tables/helpers se generují z R0 authority. Mrtvá parity-5-tet a historická
cube/donor-union readout větev se odstraní, neuchová vedle nové cesty.

### GPU granule

Jedna visible HOT tile je 8³ = 512 occupancy samples = 64 B bitů. Workgroup
kooperativně načte tile occupancy/evidence a přesný immediate 1-kernel halo do
groupshared. Potom každý MAIN používá pouze cheap local masks/oracle. Sparse
world/hash lookup se nesmí opakovat per neighbour per MAIN.

Color/evidence KernelState se načte až pro emitting MAIN a jeho skutečné
contributors. Výstupní 16B vertex ABI se v tomto CUTu nemění.

Dočasně se na Questu profilují 64/128/256-thread varianty se stejným oracle.
Finální commit obsahuje pouze vítězný kernel/dispatch shape; žádný runtime
selector ani mrtvé varianty.

### Raster a buffery

Oba 96MiB buffery zůstávají dvě navazující poloviny jednoho streamu:

```text
0 .. 2,097,151 triangles           buffer 0
2,097,152 .. 4,194,303 triangles   buffer 1
```

Nejsou A/B. Oracle emituje jednu nulově tlustou sheet s canonical winding.
Pokud fyzický surface musí být viditelný z obou stran, raster je two-sided
(`Cull Off` nebo ekvivalent) nad jedinou geometrií; nesmí se emitnout druhá
backside. Topologie a winding zůstávají view independent.

### Acceptance

GPU output je byte-for-byte oracle-equivalent na celém adversarial corpus,
včetně halo přes tile/chunk/block boundary. Každý kernel skutečný SPIR-V
writable storage <=8, žádná divergentní group barrier, žádný int64/div/mod
helper loop regression. Device timing a AOC/register evidence vyberou shape.

Commit: `perf(m8): emit the overlap shell on Adreno`

---

## CUT R2 — TRANSACTIONAL COUNT/EMIT

### Production scope

```text
Runtime/Shaders/MerkabaReadout.compute
Runtime/Merkaba/MerkabaGridRenderer.cs
Runtime/Merkaba/MerkabaGrid.Gpu.cs    pouze malé preflight counters/args
```

Žádný třetí vertex buffer a žádná A/B generation.

### Dva deterministic passy

`PreflightReadout` používá přesně R1 oracle a:

```text
validuje exact dependency/halo residency
počítá exact triangles bez vertex write
ověří <= 4,194,304
označí/pinne dependency tile build epochem
```

Pokud cokoli chybí/přetéká, nový build se přeskočí a obě staré vertex halves i
draw args zůstanou byte-identické.

`EmitReadout` se spustí jen po úspěšném preflightu, znovu vyhodnotí stejný
oracle, má vlastní emit cursor a teprve pak zapisuje vertex buffers. Build a
draw jsou na stejné graphics queue; pinned dependency epoch drží residency mezi
passy. Post-preflight dependency failure je invariant violation, ne normální
partial publish.

Finalize publikuje nové draw args teprve po successful emit. Neúspěch nikdy
nenuluje last-good draw.

### Acceptance

```text
missing halo po částečném query -> last-good byte-identický
overflow -> last-good byte-identický
success -> exact preflight count == emitted count
crossing 2,097,152 boundary bez gapu
žádný A/B generation nebo třetí stream buffer
```

Commit: `fix(m8): publish readout transactionally`

---

## CUT Q1 — READOUT QUERY

### Phase 1

Draw/dependency frustum planes a camera distance se jednou převedou do grid
space. Hierarchy child AABB test nedělá per-node GridToWorld transform.

### Phase 2 benchmark

Na Questu se se stejnou coverage porovná:

```text
current sparse hierarchy DRAW
linear physical HOT scan 0..32767:
    valid/hot && occupiedCount>0 && tile AABB in stereo view
```

Hierarchy zůstává vždy autoritou pro COLD/WARM prefetch. Pokud linear HOT scan
vyhraje, `RENDER NOW` se oddělí od `NEED SOON`; infinite world se nezmenší.
Pokud nevyhraje, dočasná cesta se odstraní. Finální commit obsahuje právě jednu
draw query autoritu.

Acceptance je shodná visible HOT tile množina, žádné COLD-as-empty, grid-space
translation/boundary tests a device target readout query <= přibližně 1 ms.

Commit: `perf(m8): specialize the readout query`

---

## CUT T1 — PER-ATTEMPT COMPLETION

### Ověřený současný defect

`MerkabaGrid.Storage.PumpStorage` čte celý counter buffer nejvýše po 50 ms a je
současně CPU autoritou pro completed attempt/observation/failure. Tím zbytečně
drží immutable sensor slots.

### Exact mechanismus

Vznikne malý 16B completion record:

```text
attemptCompletedToken
completedObservationTokenOrZero
failureReason
residencyEpoch
```

`FinalizeObservation` jej zapíše. Po každém skutečně submitted attemptu se
vyžádá právě jeden async readback tohoto recordu. Callback:

```text
ověří gpu generation
ověří exact expected attempt token
publikuje pouze CPU completion/accounting
nikdy nesubmituje compute/copy/draw/readback
```

Storage 50ms pump zůstává pro SSD/eviction/metrics, ale přestane být attempt
completion autoritou. Žádný `GraphicsFence.passed`, blocking wait, delay ani
per-frame readback.

Quiesce suspenduje další submission; již vydaný callback může bezpečně dokončit
CPU accounting, ale nesmí enqueue GPU work.

Acceptance: immediate exact retirement, stale callback ignored, unresolved
retry pouze po dependency epoch změně, successful observation právě jednou,
pause/destroy quiesce bez post-marker submitu.

Commit: `perf(m8): retire attempts from an exact token`

---

## CUT T2 — PRAVDIVÁ TELEMETRIE

### Production scope

```text
Runtime/Camera/PassthroughCameraProvider.cs
Runtime/Merkaba/MerkabaIntegrator.cs
Runtime/Telemetry/MerkabaGpuTimestamps.cs
Runtime/Shaders/StereoRgbdRefine.compute
Runtime/Merkaba/MerkabaGrid.Storage.cs      pouze log formatting/counters
```

Provider-owned history copy a integrator-owned PCA observation copy dostanou
vlastní sampled profiled scopes/submissions. Nesmějí se falešně započítat do
main observation window, pokud v něm command skutečně nebyl.

Timestamp sample s `expectedEntries != actualEntries` je invalid a nesmí do
avg/max. Obří metrics line se rozdělí na krátké stabilní lines.

`StereoRgbdRefine` přidá groupshared-reduced counters:

```text
CENTER / MID / EDGE ×
raw plane valid
opposite plane fail
PCA coverage fail
chromaticity fail
census fail
metric fallback
unique photometric winner
final accepted
```

Žádný global atomic per pixel. Protože současný shader early-returnuje, pouze
se jeho control flow přepíše na local reason/result a jednu uniform final group
reduction barrieru; solve matematika se nemění.

Acceptance: všechny produkční kernels se jménem a měřitelnou prací mají validní
timestamp scope, invalid samples nejsou agregované, logs se netruncují a radial
counter sum odpovídá processed pixels.

Commit: `perf(m8): make GPU telemetry complete`

---

## CUT D1 — AUTOMATIC DEVICE CLOSURE

Bez redesignu a bez preventivní změny kódu. Clean build/data/install a scénáře
z `kontrakt.md`: front/angled/off-axis/new space/doorway/thin sheet/object
removal/rotation, long scan, save/load/export.

Povinně zaznamenat všechny named kernel timings, attempt lifecycle, radial
reasons, authority, same-ray conflicts, occupancy transitions, readout counts,
overflow, residency/storage, GPU/Graphics memory, VrApi/GPU%, MRSS/FenceChecker,
KGSL/UCHE a screenshots/GLB.

E1 se otevře pouze pokud tato čistá data prokážou problém evidence semantics.
Konstanty do té doby zůstávají frozen.

---

## CUT F1 — FINE MASKED JOINT OBSERVATION

### Production scope

```text
Runtime/Shaders/StereoRgbdRefine.compute
Runtime/Core/DepthCapture.cs
Runtime/Merkaba/MerkabaIntegrator.cs
Runtime/Core/RoomScanner.cs
Runtime/UI/DebugMenu.uxml
Runtime/UI/DebugMenu.uss
Runtime/UI/DebugMenuController.cs
Runtime/UI/ControllerRayDriver.cs
nový malý disposable preview component/shader pouze pokud stávající neumí cone
```

Nezavádí se brush do allocatoru/resolveru/CARVE jako druhá authority. Raw owned
Depth L/R a PCA L/R zůstávají immutable. Existing joint solve na svém konci:

```text
selectedWorld -> FineContains(selectedWorld)
false -> output joint depth/normal = 0
true  -> publish současný joint výsledek
```

Predicate bez skrytých tolerancí:

```text
v = P-eyeOrigin
d2 = dot(v,v)
axial = dot(v,brushAxis)
inside = d2<=ToolDepth² && axial>=0 &&
         axial²>=d2*cos²(BrushAngle/2)
```

Raw prior smí být jen conservative early-out s +12.5mm overinclude; final exact
test toleranci nemá. Axis je vždy `normalize(cursor-eyeOrigin)`. Controller jen
volí cursor. Preview používá byte-identický descriptor/math.

FINE ON idle: sensory/preview běží, observation submit je přesně nula. Trigger
transition zahodí pre-trigger možnost a vyžádá nový depth + matched PCA L/R;
observation latchne immutable brush descriptor a retry jej nemění.

Manual brush nahrazuje automatic radial spatial authority, ale four-stream
validita a incidence zůstávají. Canonical final guards kontrolují surface target
center a carve old-K center proti stejnému descriptoru.

Acceptance: idle bitwise no-op, nic mimo brush, fresh post-trigger snapshot,
retry same descriptor, preview/mutation shoda, FINE OFF přesně původní scan.

Commit: `feat(scan): add exact fine refine authority`

---

## CUT F2 — EXPLICIT ERASE

ERASE není FREE observation. Reuse existující sparse logical traversal,
residency/load, immutable operation descriptor a exactly-once retry.

Relevant COLD tile se načte; dokud není celý erase dependency set HOT, canonical
mutation je nula. Exact 64-lane erase kernel resetuje uvnitř brush:

```text
KernelState evidence/RGB/confidence/flags
occupied bit
carve-active bit a per-tile count
attempt candidate bit
tile/global occupied counts
dirty state pro durable persistence
```

Nic za ToolDepth ani mimo cone se nedotkne. Plně nulový durable KernelState se
po save/load nesmí resurrectnout. Žádné nové persistence schema a žádný nový
world state.

Acceptance: all-or-nothing přes COLD retry, exact brush bounds, okamžitý reset
bez hysteresis, persistence roundtrip, readout dirty/rebuild, no resurrection.

Commit: `feat(scan): erase canonical data with the fine brush`

---

## FINAL build/push/install gate

Před push/build:

```text
full EditMode suite green
Quest shader compile + SPIR-V green
all kernels writable storage <= 8
CPU/HLSL stride/layout tests green
GLB Khronos validation green
git diff --check green
static absence old paths green
exact updated GPU buffer memory table
```

Build používá ověřený Unity 6000.5.9f1 host/project script. Install je clean:

```text
stop app
clear package data / uninstall
install one release APK
launch only when user requests/device connected
```

Pursuit končí pushnutými samostatnými commits a clean nainstalovanou APK, nebo
jediným přesně reprodukovatelným blockerem v rozsahu kontraktu.
