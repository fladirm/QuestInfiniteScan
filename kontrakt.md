# M8 SOTA FINALIZATION CONTRACT

## 0. Neměnné invarianty

Toto se po dobu closure nesmí znovu otevřít:

```text
WORLD TRUTH
    = M8 KernelState

KernelState
    evidence
    packed RGB
    color confidence
    minimal flags

support       = 50 mm
lattice step  = 25 mm
half support  = 25 mm
```

M8 je jediná persistentní geometrická autorita. Žádný persistentní mesh, surfel, normála, TSDF, QEF, Surface Nets, trilineární reconstruction field ani druhá topology DB. Current kontrakt toto explicitně požaduje.

Stejně tak zůstávají:

```text
signed infinite coordinates
2-choice PCG3D hash
block/chunk/tile sparse hierarchy
32768 HOT physical tiles
SSD COLD residency
immutable owned RGB-D observation
exactly-once attempt/retry
one joint four-stream depth truth
```

Current physical tile je 8³ = 512 kernelů a celý HOT pool má 32768 tiles.

A readout má nadále:

```text
buffer 0 = 2,097,152 triangles
buffer 1 = 2,097,152 triangles

logical capacity = 4,194,304 triangles
```

**Oba buffery se zachovají jako kapacita jediného view readoutu.**

---

# 1. S4 — zachovat identitu joint measurement od sensoru až po mutation

Tohle je současný největší reconstruction correctness bug.

S1 už je správně: `StereoRgbdRefine` používá Depth-L pouze jako deterministickou pixelovou lattice, ale endpoint vzniká společným Depth-L + Depth-R + PCA-L + PCA-R solve. Výstupem je **jedna** joint depth a joint normal. Opposite-depth používá point-to-plane a fotometrie world-metric patch; photometrická nejednoznačnost zachová metric prior.

Problém vznikne až zde:

```text
joint pixel P
    ↓
exact H
exact N
    ↓
nearest kernel K
    ↓
AppendSurfaceCandidate(K)

H,N,P jsou ZAHOZENY
```

`AppendSurfaceCandidate` skutečně uloží jen integer kernel coord.

Potom:

```text
Route:
K-center → project → P' → H',N'

Commit:
K-center → project → P'' → H'',N'' → RGB
```

Na šikmé stěně nebo depth hraně neexistuje důvod, aby:

```text
P = P' = P''
H = H' = H''
```

Proto vznikají sousední vrstvy, jiný sklon, chybný replacement a RGB z jiného fyzického místa.

### Finální kontrakt S4

Jeden immutable joint measurement musí mít attempt-local identitu:

```text
Measurement M
    source pixel P
    exact joint endpoint H
    exact joint normal N
    exact joint RGB association
    nearest canonical K
```

Nemusí se ukládat persistentně. Může se znovu rekonstruovat z **původního P** v immutable joint textures; kritické je nikdy už nereprojektovat `center(K)` za účelem získání measurementu.

Low-code implementace má výhodu: joint pixel má x/y po 12 bitech a route metadata potřebují jen několik bitů. Current shader už pixel limituje maskou `0xfff`.

Tedy candidate může nést například:

```text
xyz = candidate/target K
w   = packed {
          pixelX 12
          pixelY 12
          route/authority/flags
      }
```

Pak:

```text
Discover
P → H,N → K
stores K + P

Route
K + P
→ H,N FROM P
→ owner decision

Queue
→ retained winning candidate identity

IntegrateSurface
→ H,N FROM SAME P
→ SURFACE
→ RGB at SAME H
```

`TrySurfaceMeasurementAtKernel()` nesmí být v této cestě geometrickou autoritou.

### Dedup

Dnešní `.z` bit je first-thread-wins. To je bezpečné pouze pro stejné K, ne pro výběr nejlepšího source measurementu.

Finální SOTA variantě nesmí canonical result záviset na GPU scheduling order.

Pokud několik P skončí na jednom target K, winner musí mít **deterministický rank** například:

```text
authority class
→ endpoint→target residual
→ incidence
→ source pixel linear index as final tie-break
```

Rank je attempt-local. Není persistentní geometrií.

### S4 acceptance

Musí projít:

```text
flat front wall
45° wall
depth discontinuity
door frame
thin pole
negative coords
tile/chunk boundary
```

A instrumentačně musí platit:

```text
Discover P
= Route P
= SurfaceCommit P
= RGB P
```

Žádný `K-center → P'` round-trip.

---

# 2. S5 — CARVE musí používat tentýž joint measurement, ne vyrábět nový

Současný CARVE je pořád obrácený.

Dnes:

```text
old carve-active K
    ↓
world center(K)
    ↓
project into joint depth
    ↓
nějaký current pixel P'
    ↓
H'
    ↓
FREE / SURFACE
```

Proto současná observation dokáže vytvořit SURFACE výrok z jednoho pixelu a FREE výrok z jiného.

`sameObservationConflict` je přesně detektor této kontradikce. Safety `.z` bit pak destrukci stejného K přepíše zpátky na SURFACE, ale geometrickou chybu pouze schová.

### Správná semantika

CARVE může klidně dál enumerovat existující carve-active K — to je dobré pro sparse world.

Ale po nalezení candidate pixelu musí být rozhodující **immutable M z joint field**, ne geometrie odvozená z K-centra.

Pro measurement:

```text
M = {
    P,
    origin O,
    endpoint H,
    ray R = normalize(H-O),
    normal N,
    canonical replacement Kr
}
```

starý K dostane FREE pouze pokud:

```text
t = dot(center(K)-O, R)

t > 0
t < distance(O,H) - HalfSupport
```

a zároveň leží v konzervativním ray/support tube measurementu.

Pak:

```text
replacement = canonical(H)
FREE segment = O → H
```

Nikdy:

```text
behind H → FREE
```

A nikdy se nesmí v průběhu této klasifikace vyrábět jiný H.

### Zachovat dvě současné dobré pojistky

Nechat:

1. current-observation `.z` SURFACE precedence;
2. `OFF+1` replacement-continuity clamp.

Current `OFF+1` skutečně zabraňuje vypnutí starého occupied K, pokud replacement není resolved a occupied.

Po S5 mají být ale tyto guardy skoro pasivní.

**Acceptance:**

```text
sameObservationConflict raw = 0
```

ne:

```text
2521 conflicts, ale guard je zastavil
```

To je zásadní rozdíl.

---

# 3. Nehýbat teď evidence konstantami

Current evidence:

```text
ON      512
OFF     128

SURFACE 640
FREE    256

cap     ±2560
clearance full FREE at 150mm
```

Tyto hodnoty teď **zmrazit**.

Current `TrySurfaceMeasurement()` stále vyrábí:

```text
quality =
(1-distance/maxDistance)
× facing
```

a dál se používá `quality²`.

Mám dál výhradu, že jde o heuristickou pseudo-confidence místo čistého rozdělení:

```text
VALID
DISCOVERY
SUPPORT
REVISION
```

ale nesmí se to smíchat se S4/S5.

Nejdřív odstraňme pixelovou nekonzistenci.

Jestli po ní device run stále ukáže pomalé zakládání validních vzdálených surfaces, pak udělat **samostatný E1 CUT** a vyhodnotit odstranění distance multiplieru.

Žádné další náhodné `640 → 800`, `256 → 300`, deadband tuning.

---

# 4. C1 — CARVE broad phase: odstranit 99.7 % zbytečné práce bez změny výsledku

Dodaná evidence ukazuje řádově 150 milionů exact carve evaluations, z nichž zhruba 99.7 % končí `UNKNOWN`.

To není problém exact classifieru; problém je, že do něj pouštíme příliš velký pracovní prostor.

Current `QueryCarveTiles` používá především distance hierarchy.

S3 ale už předem ví:

```text
FREE outside outer mutation cone = impossible
```

Takže Q_SCAN má bezpečně odmítnout tile/node, který vůbec nemůže protínat mutation-authoritative depth volume.

### Broad phase smí používat

```text
max update distance
AND
common depth coverage
AND
outer mutation cone
```

### Broad phase NESMÍ používat

```text
FREE evidence
surface replacement decision
heuristic quality
PCA photometry
kernel owner migration
```

Nemá rozhodovat geometrickou pravdu. Jen dokazuje:

> zde exact mutation **nemůže** nastat.

Pro hierarchy je nejjednodušší reprezentovat outer central depth cone jako konzervativní zúžené frustum planes a dělat AABB/node rejection.

Incidence v Q_SCAN nepoužívat — normála na hierarchy levelu není známá.

Exact FREE zůstává S5.

### C1 target

Ne procento „magicky 0 UNKNOWN“, ale:

```text
exact carve evaluations
↓ řádově

IntegrateCarveTiles
2.1ms → výrazně pod 1ms class
```

bez jediné změny canonical výsledku.

---

# 5. R0 — zrušit současnou chybnou interpretaci M8 jako volumetrického tetra pole

Toto je druhá největší correctness věc.

Current readout používá parity 5-tetra incidence:

```text
occupied current K
+ occupied neighbours
+ occupied opposite tetra vertices
→ boundary?
```

Jenže:

```text
OccupiedFlag != inside-solid
```

M8 integrace sama říká:

> One exact kernel owns SURFACE.

Tedy occupied center je **surface-support sample**, ne hodnota inside/outside volumetrického scalar fieldu.

Proto případ:

```text
opposite0 = false
opposite1 = false
```

nemá žádnou topologickou stranu.

Current shader ji proto dokonce vybere podle pozice XR očí.

A material má `Cull Back`.

Takže identický M8 může po změně view dostat jiný winding.

To je nepřípustné.

---

# 6. R1 — readout musí konečně využít to, co je na M8 zvláštní: 50mm support na 25mm lattice

Tady je podle mě správný společný insight.

M8 není:

```text
25mm independent voxels
```

Je:

```text
50mm overlapping support
centered every 25mm
```

Takže sousední kernely nejsou „jiné nesouvisející voxely“.

Jsou **překrývající se samples stejné lokální basis**.

Právě proto současný readout dělá chybu:

```text
K
K+1

→ dvě samostatné occupied vertices
→ co s nimi?
→ ambiguity
```

Správná otázka je:

```text
Co K + jeho immediate overlapping subvalues
společně říkají o jedné local sheet?
```

Repo mělo tuto ideu historicky dokonce zapsanou jako local half-step boundary patches odvozené pouze z 26 neighbours, se single canonical ownership.

Neznamená to slepě obnovit starou cube implementaci — ta emitovala 12 triangles per K a byla také špatně.

### Finální readout algebra

Nejdřív **CPU oracle**, teprve pak shader.

Vstup jediného oracle:

```text
KernelState K
+
26 immediate KernelStates
```

Výstup:

```text
0..N local shell patches
```

Každý patch musí být úplně deterministický a musí používat pouze immediate M8 support.

Kontrakt:

```text
MAIN K
+
4 canonical shared subvalues
z překrývajících se neighbouring supports
→ jeden local thin shell patch
```

„Subvalue“ není nový uložený údaj.

Je to readout-time odvozená hodnota z existujících sousedních KernelState.

### Povinné geometrické invarianty oracle

**Translation invariance.** Překlad patternu kamkoliv v signed lattice dá přesně tentýž relativní shell.

**Chunk/tile independence.** Stejná lokální konfigurace přes tile/chunk/block boundary musí dát byte-identický output.

**View independence.** Žádná eye position, camera forward ani current view nesmí vstupovat do topology nebo winding.

**Single ownership.** Shared primitive emitne přesně jeden canonical K.

**No distance-two bridging.** K a K+2 oddělené empty center nesmí být spojeny.

**Parallel-sheet preservation.** Dvě blízké samostatné sheets nesmí oracle sloučit.

**Tangent continuity.** Jedna rovná sampled sheet musí dát jeden souvislý skin bez tetra-soup faceting.

**Finite alphabet.** Žádný QEF, iterative minimizer ani persistent mesh. Ideálně fixed small masks/tables generované z jednoho C# authority souboru do HLSL, stejně jako současná canonical geometry.

### Co NESMÍ být R1

Ne:

```text
fit arbitrary surfel
store normal
emit 50mm billboard quad per K
```

To by při 25mm centers vytvořilo masivní overlap a novou geometrii.

Ne:

```text
occupied = inside solid
```

Ne:

```text
camera decides front side
```

R1 je **overlap-shell readout M8**, ne generic surfel renderer.

---

# 7. R1 GPU implementace musí být dělaná pro Adreno, ne desktopovou GPU

Quest 3 má XR2 Gen 2 / Adreno 740 a Meta uvádí přibližně 2 MiB tile memory. Je to tiled renderer: všechny triangles procházejí binningem a vyšší množství překrývající se geometrie zvyšuje raster/depth práci.

Current readout je téměř opačný ideálu.

Even K načte až 14 neighbours, projde až 17 face candidates, dělá nepravidelné sparse world lookups a pak každý triangle zapíše jako tři 16B vertices.

Qualcomm i Meta zdůrazňují, že na Adrenu jsou kritické memory accesses, register footprint, parallel occupancy a flow control; samotný ALU instruction count často není dobrý ukazatel.

### Správná R1 GPU organizace

Tile má:

```text
8×8×8 = 512 occupancy bits
= 64 bytes
```

To je ideální working granule.

Pro visible HOT tile:

```text
load tile occupancy
+ immediate 1-kernel halo
→ groupshared/local cooperative cache

pak 512 K:
→ cheap bitmask operations
→ overlap-shell oracle
→ emit only actual shell
```

Namísto:

```text
každý K
→ znovu traverse chunk/tile refs
→ load neighbour
→ load neighbour
→ load neighbour...
```

Color/evidence `KernelState` načítat až pro K/subcontributors, které skutečně něco emitují.

To je přesně typ přestavby, který může na Adrenu dát mnohem větší efekt než optimalizace několika float násobení.

Workgroup shape se nesmí odhadnout. Qualcomm výslovně doporučuje profilovat různé workgroup sizes/layouts na cílovém GPU.

Testovat minimálně varianty kolem:

```text
64
128
256 threads
```

a vybrat podle Quest 3 device timestampů + AOC/SPIR-V register footprintu.

---

# 8. DVA readout buffery zůstávají JEDEN velký stream

Tady opravuji svůj předchozí návrh definitivně.

Nesmí vzniknout:

```text
buffer0 = old generation
buffer1 = new generation
```

To by nám uřízlo readout kapacitu na polovinu a bylo by to proti účelu těch bufferů.

Current je správně:

```text
logical triangles 0 .. 2,097,151
→ buffer0

logical triangles 2,097,152 .. 4,194,303
→ buffer1
```

To **zůstane**.

Cíl overlap-shell readoutu je naopak snížit triangles per visible surface natolik, aby:

* současný 8m frustum měl obrovskou rezervu;
* 12m stereo-union frustum nebyl problém;
* architektura nebyla fixovaná na 12m a mohla jít dál podle reálné scene density;
* 4.19M capacity zůstala poslední ochranná hranice, ne běžný operating point.

Meta navíc upozorňuje, že triangle count na Questu ovlivňuje binning a triangle spanning více tiles je dražší; redukce triangle soup je tedy přímo správná optimalizace.

---

# 9. R2 — transactional publication BEZ A/B bufferu

Máme ale skutečný publication problém.

Current build může:

```text
začít zapisovat nový readout
↓
některý pozdější neighbour je COLD/unresolved
↓
build FAILED
↓
FinalizeReadout nastaví draw vertex count = 0
```

To způsobí viditelné „scan zmizel“.

Ale pokud bychom při failure nechali staré draw args, nestačí to: current compile mohl **část starých vertex bufferů už přepsat**.

Proto A/B není řešení. Řešení je:

## count/validate → emit

### Pass A: `PreflightReadout`

Bez jediného zápisu do obou vertex bufferů:

```text
visible HOT tiles
+ exact required halo

→ všechny dependencies resident?
→ exact overlap-shell patch/triangle count
→ <= 4,194,304?
```

Pokud ne:

```text
SKIP build
leave:
    old buffer0
    old buffer1
    old drawArgs
completely untouched
```

### Pass B: `EmitReadout`

Pouze když Pass A dokázal:

```text
all dependencies resolved
AND
exact output fits both-buffer logical capacity
```

Pak může shader znovu vyhodnotit stejný deterministic overlap oracle a emitnout output.

Na dobu preflight→emit musí být dependency set residency-pinned stejným frame/build epochem.

Pak je:

```text
post-emit dependency failure
```

programátorský invariant violation, nikoli normální control path.

### Výhoda

```text
2×96 MiB capacity ZACHOVÁNA
+
failed build nikdy nezničí last-good view
+
žádná třetí 96MiB generace
```

Toto je správná transaction semantics pro disposable readout.

---

# 10. Q1 — `QueryM8Readout` je příliš drahý a dělá dvě různé práce najednou

Current query prochází hierarchii:

```text
block
→ 128
→ 64
→ chunk
→ 16
→ tile
```

a na více levelech opakuje stereo frustum a distance child masks.

Device kolem 3.3 ms za samotný query je moc.

### První bezpečný krok

Transformovat draw/dependency planes jednou do **grid space**.

Pak hierarchy test není:

```text
node coord
→ GridToWorld matrix
→ world plane
```

ale přímo:

```text
grid AABB vs grid-space plane
```

Stejně camera distance.

### Druhý krok: oddělit DRAW od WARM

Critical draw path potřebuje vědět jen o HOT GPU worldu.

HOT physical pool je pevně bounded:

```text
max 32768 tiles
```

Proto benchmarknout:

```text
LINEAR HOT SCAN

physicalSlot 0..32767
    valid/hot?
    occupiedCount > 0?
    tile AABB intersects stereo view?
        → visible
```

To je:

* lineární,
* contiguous,
* bez hash pointer chasing,
* GPU-friendly.

Infinite hierarchy zůstane, ale pro jiný úkol:

```text
COLD/WARM discovery
SSD prefetch
```

Tedy finální architektura může být:

```text
RENDER NOW
→ bounded HOT linear query

NEED SOON
→ sparse infinite hierarchy
```

To nijak nezmenšuje infinite world.

Rozhodnout device benchmarkem proti current hierarchy, ne vírou.

---

# 11. Neoptimalizovat dobré kernely, dokud zlobí špatné

Current device profil velmi jasně říká:

```text
Draw                         ~5.1–5.3 ms
CompileReadout               ~3.3–3.7
QueryReadout                 ~3.3
IntegrateCarve               ~2.1
StereoRgbdRefine             ~1.56
QueryCarve                   ~0.9
9× dilation                  ~0.7
depth copy                   ~0.1
DiscoverSurface              ~0.1
IntegrateSurface             ~0.02
```

Tedy:

### `StereoRgbdRefine`

**Nechat algoritmicky být.**

1.56ms za strict four-stream solve není problém. Nesnižovat:

* hypotheses,
* opposite-depth precision,
* PCA cameras,
* point-to-plane,
* 12.5mm metric bound.

Později lze profilovat `8×8`, `16×4`, `32×2` a případně dát FP16 pouze color/luma/chromaticity intermediates. World/depth/projective matematika zůstává FP32.

### Dilation

0.7ms za devět passů není P0.

Neřešit před readoutem/carvem.

### Surface integration

22 µs class.

**Nedotýkat se.**

---

# 12. T1 — oddělit GPU attempt completion od 50ms storage pollu

Current storage pump má:

```csharp
_nextStreamPoll = now + 0.05f;
AsyncGPUReadback.Request(_m8Counters,...)
```

Takže dokončený GPU attempt může být CPU stranou rozpoznaný až desítky ms později.

To prodlužuje držení immutable depth/PCA slots a podporuje pairing backpressure.

Nemá se vracet problematický `GraphicsFence.passed`.

Správně:

```text
každý skutečně submitted observation attempt
→ FinalizeObservation
→ one tiny async completion token/readback
→ callback retires přesně tento attempt
```

Například 4B `attemptCompletedToken`.

To není per-frame GPU readback.

Je to cca 8–15Hz asynchronous completion notification.

Storage:

```text
SSD load/writeback statistics
eviction
throughput counters
```

mohou dál běžet 50ms pumpem.

---

# 13. T2 — telemetry musí měřit to, co tvrdí

Současné PCA copy timings nejsou v main timestamp window spolehlivě zahrnuté. Provider copy a integrator-owned PCA copy jsou v jiné submission/lifecycle pozici.

Proto:

```text
Provider.CopyOwnedHistory
MerkabaIntegrator.CopyOwnedPcaObservation
```

měřit vlastními profiled scopes/submissions.

Ne tvrdit, že jsou zahrnuty v observation timing, když nejsou.

Timestamp samples:

```text
expectedEntries != actualEntries
```

musí být explicitně invalid a nesmějí jít do avg/max statistik.

Obří jeden `Merkaba GPU metrics ...` logcat line rozdělit na několik krátkých stabilních lines, protože dnešní tail s readout/overflow/failure counters může Android uříznout.

A `StereoRgbdRefine` doplnit groupshared-reduced radial counters:

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

Ne global atomic na každý pixel — redukovat uvnitř stávající 8×8 group.

Tím definitivně zjistíme, kde případně mizí center.

---

# 14. F1 — FINE/REFINE je až po automatickém reconstruction closure, ale návrh je správně malý

Přiložený návrh má správný základ:

> FINE nemá distribuovat authority do allocatoru/resolveru/CARVE. Má oříznout **derived joint observation** už na výstupu `StereoRgbdRefine`.

Raw owned depth/PCA zůstávají immutable.

Ve FINE:

```text
four streams
→ normal strict joint solve
→ selectedWorld
→ FineContains(selectedWorld)?
     NO  → joint depth = 0
     YES → publish joint depth + normal
```

Predicate:

```text
v       = P - eyeOrigin
d²      = dot(v,v)
axial   = dot(v,brushAxis)

inside =
    d² <= ToolDepth²
    AND axial >= 0
    AND axial² >= d²*cos²(BrushAngle/2)
```

`brushAxis`:

```text
normalize(cursorPos - cyclopeanEyeOrigin)
```

nikdy head-forward a nikdy controller-forward.

Controller pouze volí cursor.

### FINE early-out

Raw prior lze použít pouze jako conservative precheck s +12.5mm overinclude, protože joint solve smí endpoint posunout maximálně o 12.5mm.

Final exact test nemá toleranci.

### Po S4

Přiložený text předpokládá, že downstream znovu čte maskovanou texture přes kernel center. To po S4 odstraníme.

Ale princip zůstane ještě čistší:

```text
masked joint pixel P
→ preserved P
→ Route/Surface/RGB/CARVE
```

Tedy FINE a S4 se dokonale skládají.

---

# 15. FINE authority

Automatický scan:

```text
current radial mutation cone
```

zůstane.

Manual FINE:

```text
spatial authority = brush
```

takže current automatic radial FOV fade nesmí manual brush znovu zakázat jen proto, že uživatel úmyslně umístil cursor bokem.

Uvnitř brush ale zůstává incidence a strict four-stream validita.

Brush není confidence:

```text
inside  → authority
outside → no observation
```

Žádný `brushWeight`.

### Canonical bounds assertions

Zachovat dvě:

```text
SURFACE:
FineContains(targetKernelCenter)

CARVE:
FineContains(oldKernelCenter)
```

Tak preview a skutečně změněný canonical region odpovídají.

---

# 16. FINE scheduler

`FINE ON + idle`:

```text
depth/PCA provider běží
preview běží
NO observation submit
NO mutation
```

Trigger press:

```text
zahodit možnost použít starý pre-trigger depth

request NEW depth
pair NEW PCA
latch:
    eye origin
    cursor
    brush axis
    angle
    depth
    operation

submit immutable observation
```

Retry používá stejné:

```text
depth L/R
PCA L/R
brush descriptor
```

---

# 17. FINE ERASE není FREE evidence

Tohle je v přiloženém návrhu správně.

Joint depth `0` znamená UNKNOWN, takže ERASE nesmí být implementovaný jako „vymysli free observation“.

ERASE je explicitní user mutation:

```text
FINE_ERASE
→ existing sparse query/residency machinery
→ exact brush containment
→ reset canonical state
```

Musí vyčistit:

```text
KernelState
occupied bit
carve-active bit
attempt candidate bit
tile/global counts
dirty state
```

a persistence nesmí staré data po reloadu resurrectnout.

Cold relevant tile:

```text
load
→ retry same erase descriptor
→ no partial erase
```

Žádné nové persistence schema.

A po C1 může stejný generic cone-AABB broad phase sloužit i FINE erase query; není potřeba nový spatial traversal.

---

# 18. Finální pořadí CUTů

Toto bych zmrazil jako jediný realizační DAG:

### `CUT S4 — JOINT IDENTITY`

Přenést source joint measurement od discovery přes owner routing až po RGB/SURFACE.

**Nic jiného neměnit.**

Acceptance:

```text
no center(K) remeasurement
deterministic dedup
same H/N/RGB
```

### `CUT S5 — SAME-RAY CARVE`

CARVE klasifikuje staré K proti frozen joint ray/endpointu.

Zachovat `.z` safety a `OFF+1`.

Acceptance:

```text
raw sameObservationConflict == 0
FREE never behind H
```

### `CUT C1 — CARVE BROAD PHASE`

Distance + outer mutation cone/common depth coverage do Q_SCAN.

Acceptance:

```text
canonical result identical
exact carve evaluations collapse
IntegrateCarve substantially faster
```

### `CUT R0 — OVERLAP-SHELL ORACLE`

CPU source-of-truth pro:

```text
5cm support / 25mm lattice
main + immediate subvalues
single shared thin shell
```

Testy:

```text
front plane
axis plane
45° plane
arbitrary sloped sampled plane
convex corner
concave corner
doorway
T junction
thin pipe
two parallel close sheets
negative coords
tile/chunk/block boundary
translations
view independence
```

Žádný GPU code, dokud oracle není matematicky uzavřený.

### `CUT R1 — ADRENO OVERLAP-SHELL`

Vygenerovat HLSL z oracle.

Tile-local groupshared occupancy/halo.

Oba 96MiB output buffery zachovat jako jeden logical stream.

Nejdřív zachovat současný 16B vertex ABI, aby geometry a performance změna nebyly smíchané s dalším renderer ABI refaktorem.

### `CUT R2 — TRANSACTIONAL COUNT/EMIT`

```text
query
→ dependency/count preflight
→ only-if-valid emit
→ publish draw args
```

Failure/skip nikdy nepřepíše starý buffer ani draw args.

Žádné A/B.

### `CUT Q1 — READOUT QUERY`

Grid-space frustum math.

A/B benchmark:

```text
current hierarchy DRAW
vs
linear HOT physical scan
```

Hierarchy zachovat pro COLD/warm.

### `CUT T1 — COMPLETION`

Per-attempt async completion token.

50ms pump pouze storage.

### `CUT T2 — TELEMETRY`

PCA timings, valid timestamps, reject reasons, krátké metrics lines.

### `CUT D1 — AUTOMATIC DEVICE CLOSURE`

Teprve tady dlouhý skutečný scan.

### `CUT E1 — EVIDENCE SEMANTICS`, pouze pokud D1 ukáže potřebu

Žádný preventivní threshold hacking.

### `CUT F1 — FINE`

Masked joint observation + cursor/brush + fresh snapshot admission.

### `CUT F2 — ERASE`

Explicit canonical reset přes stejné sparse residency semantics.

### `CUT FINAL`

Long-run + save/load + GLB + readout/frustum stress + documentation cleanup.

---

# 19. Device acceptance pro SOTA

Nechtěl bych to uzavírat jen „vypadá dobře“.

### Reconstruction correctness

Musí projít:

```text
rovná zeď opakovaně zepředu
→ jedna layer

stejná zeď z boku
→ může supportovat
→ nesmí ji přesunout/carvovat

nový prostor z boku
→ discovery funguje

objekt před zdí
→ objekt existuje
→ objekt odstranit
→ background replacement
→ pak foreground zmizí
→ nikdy mezidíra

šikmá zeď
→ žádná K-center roundtrip slope chyba

thin partition / pipe
→ oddělené sheets zůstávají oddělené
```

### Counters

```text
sameObservationConflict        = 0
blockOverflow                  = 0
chunkOverflow                  = 0
hashFull                       = 0
physicalTileStarvation         = 0
observationFailure             = 0
premature free/pagefault       = 0
```

### Readout

Oba output buffery se musí skutečně testovat:

```text
logical triangle count > 2,097,152
```

aby prošel cross-buffer boundary.

Současně:

```text
no gap at buffer switch
one stereo indirect draw
no view-dependent winding
no duplicate shell
no cracks at tile/chunk boundaries
```

A nejméně **12m stereo-union view** musí být běžný acceptance case. Architektura však nesmí obsahovat hard assumption „12m maximum“.

### Performance engineering targets

Ne jako fyzikální garance, ale jako closure targets na Quest 3:

```text
Draw                    ≤ ~2 ms avg
Readout query            ≤ ~1 ms avg
Readout classify+emit    ≤ ~1–1.5 ms avg
Exact CARVE              ≤ ~1 ms avg
StereoRgbdRefine         žádná >10% regrese
```

Celý scanner/readout build frame má zůstat s rezervou pod 13.89ms 72Hz budgetem, ne dnešních 22–27ms spikes.

A hlavně žádné MRSS/Passthrough 30–38ms timeout burst kvůli scanneru.

---

# 20. Co je mimo scope a nesmí se znovu zavléct

Po dobu tohoto closure:

```text
NO TSDF
NO Surface Nets
NO QEF
NO trilinear reconstruction
NO persistent surfels
NO persistent normals
NO mesh chunks
NO readout DB
NO LOD geometry authority
NO reducing 4-stream solve
NO mono fallback
NO shrinking view frustum as “optimization”
NO halving readout capacity for A/B
NO random evidence retuning
```

A FINE se nesmí použít k maskování toho, že automatic scanner je rozbitý.

---

## Výsledný cílový scanner

Po těchto řezech má být dataflow skutečně jen:

```text
OWNED DEPTH L/R + OWNED PCA L/R
                ↓
       ONE JOINT MEASUREMENT
          P, H, N, RGB
                ↓
      deterministic owner
                ↓
        DISCOVERY / SUPPORT /
        authoritative REVISION
                ↓
        exact same-ray CARVE
                ↓
              M8
     50mm overlapping support
       on 25mm signed lattice
                ↓
       local overlap-shell
      from main + subvalues
                ↓
  huge frustum, 2-buffer stream
        one stereo draw
```

A manual FINE pouze vloží před M8:

```text
eye → cursor brush
      ↓
crop joint observation
      ↓
same exact pipeline
```

zatímco ERASE používá tentýž brush jako explicitní canonical reset.

Tohle bych považoval za **finální SOTA kontrakt**. Nevyhazuje nic, co už máme vyřešené; neopravuje scanner dalšími heuristikami; využívá naopak jeho hlavní dosud nevyužitou vlastnost — **50% overlapping Merkaba basis** — a soustředí práci přesně tam, kde device evidence ukazuje reálné chyby a milisekundy: measurement identity, CARVE working set a readout.

---

# R0 SPEC CLARIFICATION — NEZASTAVUJ KVŮLI TOMU IMPLEMENTACI

50 mm support není geometrická kostka ani solid. Je to překrývající se lokální support jednoho M8 sample. Geometrická rozteč/world sampling lattice je 25 mm. Historický donor-union je proto špatně právě tím, že support interpretuje jako objem a vytvoří 50mm slab se dvěma stěnami.

Požadovaný readout je jedna nulově tlustá disposable sheet odvozená z překrývajících se canonical surface samples.

## Přesná semantika

### MAIN

K je canonical occupied surface owner na 25mm lattice. K je jediný hlavní surface sample. Jeho 50mm support neznamená, že celý tento objem je occupied.

### PATCH FOOTPRINT

Jeden MAIN nikdy neemitne 50×50mm billboard.

Jeho lokální dual footprint je 25×25 mm, tj. jedna lattice cell area. Proto sousední patches dlaždicují plochu a nepřekrývají se plošně 4×.

### 4 SUBVALUES

„4 subvalues“ nejsou čtyři nové uložené values ani čtyři další voxely.

Jsou to čtyři shared half-lattice corner samples lokálního 25mm patchu:

```text
            S01 -------- S11
             |            |
             |     K      |
             |            |
            S00 -------- S10
```

Každý Sxy leží tangenciálně přesně na half-step:

```text
±12.5 mm
```

vůči MAIN footprintu.

To je právě místo, kde se využije 50% overlap: tentýž half-step je podporován sousedními 50mm kernels, takže jeho hodnota se odvodí z jejich společné lokální surface evidence, nikoli z jednoho voxelu.

### SUPPORT ≠ SURFACE CONTRIBUTOR

Do subvalue smějí vstoupit pouze canonical surface owners patřící téže lokální sheet.

FREE a UNKNOWN nejsou další surface points.

FREE slouží pouze k určení známé volné strany sheet.

UNKNOWN nesmí vytvořit druhou zadní stěnu.

Tedy:

```text
FREE | SURFACE | UNKNOWN

       ↑
  jediná sheet
```

nikdy:

```text
FREE | [50mm occupied support] | UNKNOWN
      ↑                     ↑
    front                  back
```

### FREE-SIDE / WINDING

Strana surface se určuje výhradně z signed M8 evidence v immediate neighbourhood, ne z kamery.

Negativní evidence = známý free-space side.

Positive occupied owner = surface sample.

Zero/unknown nedává žádnou stranu.

Local free-side signature se odvodí deterministicky z immediate 26-neighbourhood. Camera position, eye position, current frustum ani head pose se nesmějí objevit v topology/winding oracle.

V případě numerického tie musí být pevný canonical tie-break podle lattice axes / lexicographic order, nikdy podle view.

### SLOPE

Čtyři subvalues nesmějí být slepě K ± 12.5mm v jedné rigidní rovině, protože to by z readoutu udělalo voxelové schody.

Každý shared corner musí z overlapping neighbouring surface-owner centers odvodit svůj normal-direction height. Pro příslušný tangent corner použij čtyři bezprostřední tangent columns, které tento half-step sdílejí; v každé smí být zvolen pouze nejbližší surface owner téže sheet v rozsahu max. ±1 lattice normal step. Z jejich normal-coordinate se deterministicky odvodí corner position.

Tím:

```text
flat wall   → všechny 4 corner heights stejné
slope       → corner heights plynule sledují neighbouring owners
```

a nepoužívá se žádný persistent normal, QEF ani fitted surfel.

### SAME-SHEET RULE

Normal-direction soused nesmí být automaticky donor.

Dvě paralelní sheets se nesmějí zprůměrovat.

Contributor je same-sheet pouze pokud:

* je bezprostředně lokální;
* má kompatibilní free-side signature;
* nevyskytuje se mezi ním a MAIN známý FREE separator;
* není dál než ±1 normal lattice step;
* deterministic nearest/tie rule vybere právě jednu branch.

To je zásadní pro tenké příčky a dvě blízké paralelní plochy.

### OUTPUT

Jeden běžný MAIN surface owner:

```text
MAIN + S00/S10/S01/S11
    ↓
exactly one thin patch
    ↓
exactly 2 triangles
```

Patch winding je canonical od známé FREE strany.

Žádný eye-dependent flip.

Žádná backside vzniklá z UNKNOWN.

### SHARED VERTEX INVARIANT

Half-step subvertex musí mít canonical address a contributor set tak, aby dva sousední patches, které odkazují na tentýž subvertex, vypočítaly bitově stejnou position bez komunikace a bez persistentního vertex state.

Tohle musí nejdřív dokazovat CPU oracle.

## R0 CPU ORACLE ACCEPTANCE

Neimplementuj GPU readout, dokud oracle nedokáže:

```text
one flat plane
    → one zero-thickness sheet

translated flat plane
    → byte-identical relative result

45° / arbitrary quantized slope
    → continuous shared corners, no stair-step double skin

two parallel sheets
    → remain two sheets

thin partition
    → both physical sides survive separately

convex / concave corner
    → no bridge across empty/free separator

isolated/missing neighbour
    → deterministic local result, no view fallback

tile/chunk/block boundary
    → identical to same pattern wholly inside a tile

negative coordinates
    → identical translated topology
```

## ONTOLOGY

R0/R1 jsou jen disposable readout:

`KernelState` remains unchanged.

Zakázáno:

```text
persistent normal
persistent subvalue
persistent surfel
mesh topology DB
TSDF
QEF
Surface Nets authority
support-union solid
eye/view-dependent topology
```

## BUFFERS

Dva 96MiB readout buffery NEJSOU A/B generations a nesmějí se tak použít.

Jsou dvě navazující poloviny jednoho logical large-frustum streamu:

```text
buffer0: triangles 0 .. 2,097,151
buffer1: triangles 2,097,152 .. 4,194,303
```

Infinite scan potřebuje celých 4.19M capacity a cílem je naopak později zvětšit použitelný view frustum. Transactional publication se řeší preflight/count → emit, ne rozpůlením kapacity.

Takže blocker R0 není „vymysli nějaký surfel“. Tohle je přesná požadovaná interpretace: 50mm = overlapping support, 25mm = sampling/patch pitch, MAIN = canonical surface owner, 4 SUBVALUES = shared half-step corner readout samples, FREE určuje jedinou stranu, UNKNOWN nikdy nevytváří backside. Nejprve to uzavři jako deterministic CPU oracle a jeho invariant tests, teprve potom překlop 1:1 do Adreno GPU kernelu.

R0 má nejdřív formalizovat oracle a adversarial cases; pokud při convex/concave/T-junction testu narazíš na konfiguraci, kde výše uvedená same-sheet pravidla stále dávají více matematicky platných výsledků, nevymýšlej view heuristic ani nový state — vypiš konkrétní minimální lattice pattern jako jediný explicitní blocker. Zbytek DAGu S4/S5/C1/T1/FINE kvůli tomu nezastavuj.
