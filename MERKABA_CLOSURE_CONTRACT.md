# M8 SPHERE–FLOWER — ZÁVĚREČNÝ OPRAVNÝ KONTRAKT

> Uzavírací implementační kontrakt pro /mnt/aidisk/prace/uniscan nad
> d60e053468c5e592d5635f370f6f0089bea2f8d2 a jeho rozpracovanými opravami.
> Nadřazenou algoritmickou autoritou zůstává
> M8-DUAL-SPHERE-FLOWER-CLOSED-PRODUCTION-CONTRACT-REV-C.md.
> Tento dokument nahrazuje předchozí návrh execution closure. Neotevírá
> geometrii, ontologii, číselné invarianty ani persistentní scan ABI REV-C.

Normativní odkazy míří na REV-C. Odkazy na zdroje používají soubor a funkci;
historické řádky nejsou identitou funkce. Rozpracované změny se nejprve
dokončí, nikoli zahodí nebo implementují podruhé.

Implementační stav a receipts zůstávají v implementační části lasttrue.md.
Immutable prefix REV-C se tímto dokumentem nemění. Neslučitelný návrh není
povolená „execution optimalizace“.

Engineering rozpočty níže jsou výslovně zvolené cíle. Nejsou vydávány za
matematické důsledky Flower geometrie.

---

# §1 Výchozí evidence a skutečně otevřené mezery

## 1.1 Artefakt, zařízení, startup

~~~text
HEAD                 d60e053468c5e592d5635f370f6f0089bea2f8d2
mandatory base       c34d27f0ecb51500b12209ed5d2fe72b893726f5
APK                  92 206 786 B
APK SHA256           ff3b6b2b9e7accb8ed02cb61b0d3889e282c9c502da318815175e643bf47f903
native .so           13 358 760 B; native kód + zapečené SPIR-V
native pipelines     25
testované zařízení   Quest_3S / Adreno 740
cílový HW profil     quest_guide.md; vlastnosti skutečného zařízení se načítají
~~~

Artefakt je compiler checkpoint, nikoli runtime acceptance. Android native
build není Unity Quest APK build.

Záznam spuštění z 2026-09-07:

~~~text
18:09:07   plugin loguje Vulkan inicializaci
           multiDrawIndirect / drawIndirectFirstInstance / drawIndirectCount:
           zařízení podporuje, Unity device je neměl zapnuté
18:12:58   Adreno: Failed to link shaders / Pipeline create failed
           vkCreateComputePipelines = -13
           konkrétní pipeline starý log neidentifikuje
~~~

Plugin logging tedy není úplně mrtvý. Chybí spolehlivá identifikace a časování
jednotlivých pipeline. Dlouhá synchronní inicializace blokovala UnityMain.
Z těchto dat neplyne RAM OOM ani konkrétní viník mezi shadery.

VK_NULL_HANDLE u aplikačního pipeline cache znamená absenci vlastního cache,
nikoli důkaz, že driver při každém startu překládá všech 13,36 MB knihovny.

Rozpracované zdrojové opravy už obsahují vyjednání indirect features,
background inicializaci native pipelines a omezení duplicitních stereo/R3
callsites. Jejich přítomnost není důkazem opraveného APK; RUN_08 je musí
dokončit a ověřit, ne přepsat další paralelní cestou.

## 1.2 Shader evidence

Historické velikosti ukazují problém, nikoli jeho jedinou příčinu:

~~~text
DrainObservationRefinement     přibližně 5,02 MB
CompactDirtyFlowerSymbols      přibližně 3,61 MB
FlowerCommit                   přibližně 2,10 MB
StereoFlowerRefine             přibližně 1,59 MB
UpdateObservationDual          přibližně 0,62 MB
~~~

Počet instrukcí těla funkcí po spirv-opt -O se NESMÍ porovnávat s počtem všech
instrukcí neoptimalizovaného modulu. Brána měří tentýž SPIR-V, který native
generátor skutečně balí, s totožnými defines, flags a verzí compileru.

Rozpracovaná stereo oprava má samostatný receipt: 1 589 116 → 863 296 B při
zachování reflection ABI. To není výsledek celého RUN_08 ani device profilu.

## 1.3 Validace a stav funkcionalit

~~~text
poslední dohledaná úplná suita:
  2026-09-07 14:02:10Z–14:10:43Z
  357 testů, 353 passed, 4 failed
  před nejnovějšími opravami; současný strom není full-suite PASS

historický SPIR-V audit:
  69 entrypointů PASS pro tehdejší kontrolované vlastnosti
  nebyl to důkaz přijatelné velikosti, driver compile ani runtime výkonu
~~~

Test receipt:
 /mnt/kingston-unity/Builds/UniscanR1Order/Run07Repair03/TestResults/merkaba-results.xml

Otevřené práce mají konkrétního vlastníka:

~~~text
native startup/features, velikost shaderů, batching a fronta     RUN_08
THROUGH/ambiguity/fine invalidation, skutečný completion fixture RUN_09
společná prezentace, optical consumer, export a native package  RUN_10
chybějící souborové/UI přenosy, nikoli slepý cherry-pick          RUN_11
durable replay, session/design/notes/anchor consumers           RUN_12
UI nad skutečnými funkcemi a měřenými daty                       RUN_13
úplná suita, standard validators, APK a device acceptance       RUN_14
~~~

---

# §2 Jedna ontologie, jeden evaluátor, omezené provedení

Persistentní scan truth je přesně:

~~~text
M8 KernelState
sparse SEE_THROUGH / excavation nad stejnými adresami
FlowerDetail
ThreadAtlas
~~~

Flower symbols, COMPLETED, DIRT, draw pages, presentation mesh a exportní
atlas jsou odvozené. Stránka není nový svět ani náhrada manifestové transakce.

CPU a HLSL backend stejného generovaného evaluátoru nejsou dvě autority.
Druhá autorita vzniká jinými predicates, jiným ownershipem, alternativním
surface solverem nebo jinými vstupy vydávanými za tentýž snapshot.

Execution refaktor musí:

- rozdělit nezávislou práci mezi lanes/WG;
- zachovat skutečné parent/child a globální publikační závislosti;
- používat generated incidence a integer identity místo hledání geometrie;
- sdílet callsites a zkracovat živost intervalového scratche;
- udržet paměť a pending workset omezené skutečným kvantem;
- přepojit všechny consumers před odstraněním nahrazené cesty.

Samotný počet funkcí, poměr aritmetických helperů k lookupům ani přítomnost
[loop] nedokazují chybnou implementaci. Malý culling kernel neřeší stejnou
práci jako intervalová Flower closure. Optimalizace se prokazuje emitted
modulem, zachovanou sémantikou a měřením na zařízení.

Zakázáno: generic meshing, float weld, fit, epsilon, druhý geometry solver,
CPU readback pro rozhodování scanneru, trvalý fallback nebo feature flag
mezi starou a novou rekonstrukcí.

---

# §3 Zmrazené invarianty

Tento seznam je závazek zachování, nikoli tvrzení, že všechny jeho důkazy
v aktuálním APK prošly.

~~~text
KernelState = 16 B; pozice implicitní
tile  = 8³ kernelů  = 0,2 m
chunk = 4³ tiles   = 32³ kernelů = 0,8 m
block = 8³ chunks  = 256³ kernelů = 6,4 m

J = 2K+d; 13 canonical line classes; generated 26/72/48 incidence
RootOwner = unique canonical half-open ownership
PetalUsesRoot = generated incidence, nikoli rovnost s RootOwner
CERTAIN_BOUNDARY jen z exact equality proof; celý interval se zachovává
R1 = existence; R2 = shape innovation; R3 = branch/junction closure
pred/obs se differencují; peer evidence a shared incidence intersectují
R2 ancestry transport je generated; V není vstup R2 world geometry

L0–L2 = skutečná geometrie
L3–L5 = vždy úplný logical skin 7+49+343 = 399 pozic
57 split bits = scan-authored signal subdivision, nikoli existence
3 ordered chamber descents vždy; clipped parent support, 36 transition rows
Fibonacci = immutable recursive thread ordering, nikdy scheduling/LOD
RGB = captured signal; V = aditivní A3/A4/A5 innovations
V nemění L2 vertices, silhouette ani depth

one WG per touched tile; nynější source FlowerCommit má 128 lanes × 4 ownery
StereoFlowerRefine 8×8; depth certificate se skutečnými global barriers
count → reserve → emit; snapshot je jedna bounded synchronní transakce a nepřežívá ji
SCAN/draw hot path GPU-only; CPU oracle/storage/export nejsou geometry fallback

jedna serialized native job chain, bez dalších GPU queues
generational symbol arena; atomická per-page publication
static indexed carrier fan: 7 sites / 18 indexů
normal/chart frame pochází z evaluované L2 geometry, ne world frame codegenu
head rotation = cull/draw; žádná residency/page/SSD změna
~~~

FREE je aktuálně prokázaný negativní pohled, nikoli nevratně prázdný svět.
Nový strict endpoint obnovuje svůj FULL support a má same-observation
precedenci. Neviděný FULL sám není direct positive endpoint. DIRT vzniká
výhradně podle amended REV-C excavation predicates, ne obecným mesherem.

Manifest v4, sparse generace, parent epochs a stávající durable transakce
zůstávají. Odstraněné OverlapShell, CanonicalGeometry, ExportMembrane,
MeshReadout a whole-world FRONT/BACK se nevracejí.

---

# §4 RUN_08 — execution a startup

~~~text
ID=RUN_08
DEPENDS_ON=d60e053 + zachovaný pracovní strom
NEW_AUTHORITY=žádná
FILES=Tools/shaders/, Tools/unity/generate_merkaba_native_executor_shaders.py,
      Editor/MerkabaSphereFlowerCodegen.cs,
      Runtime/Shaders/Merkaba{World,ObservationBins,Integration,Readout}.compute,
      Runtime/Shaders/MerkabaFlower*.hlsl,
      Runtime/Telemetry/Native/, MerkabaNativeVulkanExecutor.cs,
      MerkabaGridRenderer.cs
~~~

## 4.1 První commit — měřit skutečný artefakt

Rozšířit stávající audit, nikoli vytvářet druhý compiler workflow:

~~~text
production SPIR-V bytes       >512 KiB REVIEW; >1 MiB FAIL
function-body instructions    >20 000 REVIEW; >50 000 FAIL
groupshared                   >16 KiB REVIEW; >32 KiB FAIL
zaznamenat                    entrypoint, hash, compiler/flags, LocalSize,
                              RO/RW bindings, barrier count, velikost konstant
~~~

První commit je měřicí checkpoint; nesmí být označen RUN_08 PASS, pokud
poctivě odhaluje stávající FAIL. Velikostní prahy jsou engineering gate;
jejich splnění nenahrazuje compiler-time ani sustained runtime profil.

Command-graph audit používá skutečný native generator a emitované commands:

~~~text
normal observation     skutečně provedené nonzero dispatches
allocation retry       zvlášť počet a příčina
continuation quantum   zvlášť počet, work items a observation token
barriers               transfer / compute / publication
page batch             claimed / committed / retried / failed
~~~

Cíl normal hot-resident observace bez retry je nejvýše 10 dispatchů.
Certificate reductions, dual a refinement dispatches se nesmějí schovat
do jiného účetního názvu. Nezbytnou global boundary nelze zrušit kvůli číslu;
překročení cíle má explicitní dependency a device-cost receipt.

## 4.2 Reset a globální fáze

Dispatch (1,1,1) znamená jednu workgroup, nikoli jedno vlákno. Reserve,
Resolve a Finalize mohou mít paralelní práci nebo globální publikační význam.

V MerkabaWorld.ResetObservationCounters oddělit transient reset od persistent
allocator/residency counters. CounterCount×4 není bezpečný clear region.

~~~text
prokázaný souvislý transient zero range:
    vkCmdFillBuffer + správné compute/transfer/compute dependencies

hodnotová nebo podmíněná inicializace:
    zachovat generaci/token a provést v existující vhodné fázi

data čtená jinými WG:
    vyrobit před dispatch boundary; WG barrier nestačí
~~~

BlockCount, FreeTileCount, ResidencyEpoch, continuation stage, živé arény
a free lists se při nové observaci plošně nenulují. Finální ancestor-stage
publikace napříč touched tiles zůstává skutečnou globální bariérou.

Allocation retry používá GPU-generated nonzero indirect práci pouze při
allocation miss. Žádné CPU čtení počtů kvůli rozhodnutí, zda pokračovat.

## 4.3 Snapshot jako jedna bounded transakce

Jeden snapshot je jedna bounded synchronní scan transakce. Nikdy se nedrží
kvůli domletí derived refinement práce. Snapshot zpracuje všechnu přímo
adresovanou resident evidence a všechny finite tile-local důsledky dosažitelné
v té transakci, commitne je do canonical world state, označí derived readout
pages dirty a retiruje.

COLD dependency se zažádá a přeskočí; AMBIGUOUS evidence se přeskočí; ani jedno
snapshot nedrží. Kvůli COLD ani AMBIGUOUS se nesmí vymyslet FULL, FREE ani
surface: znamenají „na tento update nemám právo", ne „drž snímek a čekej".

Další geometry, excavation a skin refinement pohánějí POZDĚJŠÍ observace nad
persistentním světem. Žádný per-observation cursor, pending tile, refinement
quantum ani continuation workset nepřežije FinalizeObservation.

Persistentní M8 / dual / FlowerDetail / ThreadAtlas svět JE refinement memory.
Camera observation je evidence, ne pracovní fronta.

root → L1 → L2 → skin zůstávají skutečné dependency barriers, ale všechny čtyři
patří do JEDNOHO snapshot command graphu, ne do čtyř dalších pokusů o tentýž
snímek. Work item nese implicitní Flower adresu, observation token a potřebnou
generation identity; ne nový surface state machine. Child se zpracuje po svých
required ancestors uvnitř téže transakce.

Determinismus je tím silnější, ne slabší: bez krájení nemá pořadí ani kvantum
co změnit. Transient scratch, list nebo receipt smí existovat uvnitř jednoho
GPU submitu; po FinalizeObservation musí být pryč.

## 4.4 Dirty pages — bezpečný batch

MerkabaFlowerPages.TakeDirtyPage a arena leases nelze pouze zavolat z více
WG: současný pop má neatomické writes a rezervace používají BUSY/CAS.

Zachovat stávající tříúrovňovou bitmapu:

~~~text
dirtyCount = součet popcount listových bits
root/summary slouží k přeskočení prázdných podstromů
ordered prefix → bounded DirtyPageQueue
item = physical slot + slot generation + requested source generation
dispatch = počet přijatých položek, jedna WG na stránku
~~~

Každá položka je claimnuta nejvýše jednou. Nové dirty writes během práce
nesmějí zmizet při odstranění starého bitu.

Batch rezervuje arénu bez neomezeného spin-waitu. Každá přijatá rezervace
má receipt; končí přesně jednou v publish nebo vratné abort/retire větvi.
BUSY se vrací do retry a nesmí ztratit dirty bit, sample block ani symbol
block. Totéž platí pro současně publikované stránky a batch counters.

FRONT se přepne pouze po úplném zápisu symbolů, skin programu a dependencies.
Allocation failure nepublikuje prefix stránky ani nezmenšuje draw radius.
Staré bloky se uvolní až po posledním příslušném graphics fence.

Úspěšný batch musí při dostupném rozpočtu publikovat více než jednu stránku;
pouhý dispatch větší než jedna s většinou BUSY není acceptance.

## 4.5 Paralelní práce a scratch

Rozdělit nezávislé owner/carrier/site položky mezi lanes; zachovat pořadí
direct mask → finite completion → generated children. R1/R2/R3 scratch
recyklovat po dokončení fáze. Nepřenášet všechny kandidáty v lokálních
polích jednoho lane.

Prefix nad 512 ownery implementovat paralelně; scan zůstává integer,
exclusive a deterministický v canonical pořadí.

Celý maximální page výsledek se do groupshared nevejde: samotných
512×128×16 B direct symbolů je 1 MiB. Cache je proto omezená na packet,
jehož velikost je odvozená z dostupného scratche. Count/emit nad jedním
immutable vstupem smí zůstat tam, kde cache zvýší paměť nebo register pressure.

Sousední coverage pass není redundantní kopie centrálního owner passu.
Odstranit skutečně opakované výpočty a duplicitní callsites, nikoli potřebné
evidence. Přesun konečného cyklu na groupID nezaručuje správné dependencies.

Zachovat jednu WG/touched tile. Volba 128 nebo 256 lanes je execution parametr
ověřený na Adrenu, ne změna Flower ontology. Cíl groupshared 12–16 KiB;
vyšší využití musí mít profil. Přesná occupancy neplyne jen z počtu bajtů.

## 4.6 Codegen a read-only data

Čistá konečná kombinatorika se generuje jednou: incidence, transport,
ownership, child/chamber transitions, thread permutations a offsety.
Dynamické ABC/root/interval výpočty nad měřením zůstávají analytické.

Pokud emitted SPIR-V materializuje velké constant arrays do lokální paměti
nebo větvení, emitovat jeden immutable read-only blob s generated offsets
a typed accessors. Zachovat přesné bit patterns a hash CPU/HLSL dat.

Přepojit scanner, compaction, vertex, fragment i native resource bindings.
Žádná stará embedded tabulka jako fallback. Malá skalární konstanta nemusí
být nahrazena random buffer fetch.

Read-only SRV není writable UAV, ale stále se počítá do odpovídajících
Vulkan descriptor limits. Hlídají se odděleně RW budget i všechny skutečné
device limits; samotný přesun RW→RO nesmaže descriptor z Vulkan layoutu.

## 4.7 Dual, counters, queue

Dual traversal začíná jen ve frozen frustum/range. Whole-support certificate
na uniform node zapíše ALL_THROUGH a zastaví sestup. MIXED rozvíjí pouze
nutné potomky v existujícím block/chunk/tile indexu.

Counter ABI projít podle consumers. Mrtvé sloty fyzicky odstranit; živé
THROUGH decrement/OFF counters přejmenovat, nikoli smazat podle slova CARVE.
Přegenerovat C#/HLSL/native a diagnostický reader současně.
Změna execution binding/layoutu zvedne native ABI verzi a invalidační klíč
pipeline cache; staré resource aliases se neuchovávají. Persistent scan ABI
a record version se kvůli čítačům nemění.

Mezi bounded jobs/kvanty platí priorita FINE/ERASE → observation →
dirty page → WARM. In-flight Vulkan práci nelze preemptovat CPU flagem.
Renderer nesmí vyhladovět kvůli jedné dlouho žijící observaci; nabídne se
omezené page kvantum na bezpečné hranici. CPU SSD progression běží nezávisle.

## 4.8 Pipeline startup a cache

Dokončit rozpracovanou inicializační cestu:

~~~text
ověřit supported AND enabled required features
vytvořit pipeline mimo blokující UnityMain inicializaci
READY zveřejnit až po úplném úspěchu
FAILED nese entrypoint, VkResult a compile time; UI nečeká donekonečna
shutdown cancel → join → destroy; žádné Unity API z workeru
~~~

Cache je pouze obnovitelný výkonový artefakt. Persistovat přes bounded
soubor, compatibility header a atomic rename. Klíč zahrnuje zařízení,
driver/cache UUID a shader/native ABI hash. Neplatný cache se zahodí;
neexistuje fallback geometry ani alternativní shader semantics.

Logovat přes fungující release/native log sink: cold/warm cache, každý
pipeline compile time, chybu, startup total. Nepředpokládat, že absence
konkrétního řádku znamená nefunkčnost celého loggingu.

Na skutečném zařízení načíst/logovat properties/features. Jeden bound SSBO
nejvýše 128 MiB pro cílový profil, groupshared nejvýše 32 KiB; world banks
neslučovat. Build runner musí omezit souběh a paměť shader compilerů.
Vyšší host RAM ani přesun čekání do workeru nejsou oprava přerostlého shaderu.

~~~text
PROOFS=stejné symboly/intervaly/ABI; eager/drain; queue ownership;
       žádné ztracené dirty/arena receipts při BUSY/retry
PERF=cold/warm startup; compile time; všechny GPU stages;
     pages/quantum; register/shared pressure; CPU storage/submission; PSS
ACCEPTANCE=shader gate PASS; command graph doložený; více úspěšných pages/batch;
           žádný startup hang ani nepovolené Vulkan features
ROLLBACK_BOUNDARY=ucelená execution změna; žádná druhá production cesta
~~~

---

# §5 RUN_09 — direct, excavation, completion a invalidace

~~~text
ID=RUN_09
DEPENDS_ON=RUN_08
NEW_AUTHORITY=žádná
FILES=MerkabaFlowerCommit.hlsl, MerkabaFlowerSupport.hlsl,
      MerkabaReadout.compute, MerkabaSphereFlowerReader.cs,
      MerkabaSphereFlowerAuthority.cs / codegen a příslušní consumers
~~~

## 5.1 Full-support THROUGH a R1 hysterese

Zachovat REV-C §14.1:

~~~text
seed + úplný THROUGH:
    okamžitě clear

stabilní R1 + úplný THROUGH:
    okamžitě nepřípustná presentation/export geometry v certified FREE
    invalidovat R3/R2-dependent fine state podle contractu
    stávající evidence decrement jednou za immutable observation
    persistentní R1 clear až po překročení OFF hysterese

partial / mixed / COLD support:
    není důkaz ghost; žádný destruktivní decrement ani clear z této příčiny
~~~

Nezavádět unconditional clear stabilního R1 ani nové ON/OFF hodnoty.
Latentní konfliktní evidence není povolená drawable surface uvnitř FREE.

Ověřit skutečné napojení fine invalidation i při decrementu, který ještě
nezmění Occupied/PlaneValid flags. Strukturální delete mění sparse owner
epoch; kompatibilní plane refinement zachová epoch a přepočítá descendants.
R2/R3 invalidation se nesmí zvrhnout v odstranění všech nesouvisejících dat.

Strict endpoint v dříve FREE prostoru obnovuje podporu podle REV-C před
same-observation THROUGH klasifikací. Dynamicky přidaný objekt se musí
naskenovat; FREE není historicky nevratná maska.

## 5.2 Dual query a neúspěšná kompakce

U SupportCoverDual zachovat tři významy:

~~~text
0 = žádný prokázaný THROUGH v požadovaném krytu
1 = celý požadovaný kryt prokazatelně THROUGH
2 = smíšený / COLD / nedostatečný důkaz
~~~

Hodnota 2 není veto ani důkaz FULL. Chybějící readCell authority je chyba
callsite, ne povolený default vyrábějící prázdný model.

Propagovat REQUIRED support ambiguity až do výsledku page transakce.
Pouhé vymazání wedge bitu a úspěšná publikace tenčí stránky je zakázané.
Rozlišit:

~~~text
COLD required support:
    request residency; retry page

plně residentní, ale neprokazatelný support:
    AMBIGUOUS + příslušný evidence/refinement důvod; žádný load busy-loop

nepotřebný cold halo:
    nesmí sám blokovat stránku
~~~

FRONT pointer při neúspěchu zůstává. Jeho kreslitelnost se však nadále řídí
source/slot generations, owner epochs a aktuálním veto/source-invalid guardem.
Zachování alokace nesmí znovu zobrazit starý povrch uvnitř nového FREE.

## 5.3 Unique completion a DIRT

Dokončit skutečný Parent48 admission → boundary → unique candidate consumer,
nikoli pouze test maskového helperu.

~~~text
direct mask je frozen
knot ownership je unique
knot sharing plyne z generated incidence, ne průniku interior podmínek petalů
0 / 1 / >1 valid candidates → unresolved / COMPLETED / ambiguous
COMPLETED nikdy neseeduje další completion
THROUGH okamžitě ruší derived completion
~~~

DIRT používá pouze existující §13.4 excavation faces, free-side ownership,
interval support a exact direct triangle coverage. Žádné obecné triangulování
FREE/FULL hranice. DIRT není nový M8 endpoint, nemá measured RGBV a nemůže
sloužit jako direct completion donor.

## 5.4 Důkazy

Doplnit nebo obnovit existující behaviorální testy:

~~~text
ClassifyFreeCell: všech 65 536 packed kombinací včetně invalid states
ClassifyDirtFace: všech 9 dvojic normalizovaných stavů
CoverDual: úplný důkaz / mixed / COLD; no false THROUGH
full-support contradiction: seed, stable hysteresis, fine invalidation
dynamic endpoint po dřívějším FREE; same-observation direct precedence
skutečný generated Parent48 unique-hole positive fixture
0/1/>1 a nonrecursive completion; boundary roots a shared incidence
~~~

Jeden certifikovaný overlap support může dokazovat pokrytou elementary cell;
jediný vzorkovaný screen-space roh nedokazuje celý projected support.
Finite enumerace nenahrazuje interval containment, projection edge,
zero-false-THROUGH ani required scene fixtures REV-C §29.

~~~text
ACCEPTANCE=žádný ambiguous cover vydaný za veto/FULL; žádná tichá díra publikací;
           R1 hysterese zachována; přidaný objekt přijímán;
           skutečný unique completion a DIRT consumer prokázány
ROLLBACK_BOUNDARY=celý dotčený direct/dual consumer řetězec
~~~

---

# §6 RUN_10 — jedna Flower prezentace, draw a export

~~~text
ID=RUN_10
DEPENDS_ON=RUN_09
NEW_AUTHORITY=žádná
~~~

## 6.1 Společný program, nikoli svět ze stránek

~~~text
M8 + dual + FlowerDetail + ThreadAtlas @ frozen source cut
                          ↓
      jeden generated Flower page/evaluation program
                 ↙                         ↘
      GPU resident publication       CPU bounded snapshot evaluation
                 ↓                         ↓
            procedural draw          GLB / 3D Tiles serializer
~~~

REV-C §27 ponechává CPU exporter nad týmž Sphere–Flower evaluátorem.
GPU scanner zůstává GPU-only. Exportní CPU backend není fallback scanneru.

Sdílet predicates, canonical ownership, interval midpoint, child transport,
skin addressing a position conversion. Ne duplikovat solver pod názvem
PageEmit. Storage reader pouze dodává stejné canonical records.

Resident page je immutable derived snapshot se zdrojovými dependencies,
ne persistentní scan truth. Lze ji znovu použít pouze pro odpovídající source
cut. Symboly neobsahují XYZ; i aktuální stránka vyžaduje evaluaci pozic,
případný bake, serializaci a IO. Reuse šetří klasifikaci, ne celý export.

## 6.2 Snapshot a determinismus

Export nejprve výhradně uzavře příjem nového scanu, FINE/ERASE a ostatních
canonical mutací včetně změny session, designu, poznámek a referencovaných
assetů. Dokončí již rozpracovanou immutable observation, její fine práci,
SSD append i ACK a jediný běžný durable commit. Teprve potom čte zdroj.
Tento operation gate drží po celou dobu exportu, ne jen při pořízení cutu.
Zafixuje:

~~~text
manifest CommitGeneration a ValidEnds
aktivní M8/dual base generations
owner epochs a fine records
anchor/grid metadata
snapshot designu, poznámek a referencovaných assetů
contract/codegen/serializer version a export options
~~~

Export nesmí spojovat stránky různých generací ani přebírat novější fine
data do starších M8 parents. Již běžící kompakce se před cutem dokončí nebo
bezpečně zruší a retire; nová se během exportu nepřipustí. Scanner, FINE/ERASE,
změna session i ostatní canonical mutace zůstávají pozastavené až do konce
exportu včetně dokončení všech jeho workerů a cleanup. Čte se tento neměnný
aktuální durable cut za drženým gatem, nikoli historický MVCC snapshot.
Uvolnění v finally umožní další explicitní operaci; samo znovu nespustí scan.

CPU/GPU parity vyžaduje totožné inputs a dostupnost required evidence.
GPU COLD versus CPU plně načtené SSD nejsou totožný testovací vstup.
Neúplný region musí být pojmenovaně unresolved, nikdy tiše vynechaný.

Pořadí výstupu je canonical logical address → owner → carrier → wedge/site.
Není dáno pořadím GPU publikace, dokončení IO ani časem běhu.

## 6.3 Geometrie, chart a indexy

Použít existující canonical L2 chart:

~~~text
H=(0,0)
R0=(1,0), R1=(1,1), R2=(0,1)
R3=(-1,0), R4=(-1,-1), R5=(0,-1)
Ww=(H,Rw,R(w+1 mod 6))
~~~

World pozice sedmi sites pocházejí z téhož ordered knot evaluatoru jako VS.
Chart convention dává codegen; world frame dává konkrétní evaluovaná L2
geometry. Normála není součástí KnotAddress ani důvodem rozpojit polohu.

Exportní material batch zapisuje:

~~~text
vertexBase(s) = 7 * ordinal carrieru v daném primitive batchi
vertex(s,i)   = stejná canonical position a případně chart UV
index offset  = exclusive prefix součtu 3*popcount(activeMask)
indices       = pouze aktivní wedges, s jejich generated winding/reverse mask
~~~

DIRT používá svůj již definovaný presentation symbol/evaluator, nikoli
nuceně sedm sites direct carrieru. COMPLETED/DIRT provenance se zachová
s granularitou odpovídajících aktivních wedges, ne jednou nepravdivou nálepkou
pro smíšený carrier.

Žádný Dictionary nad float vertices ani exportní weld. Sdílená canonical
identity dává totožnou polohu. Případná duplicita exportního vertex tuple
kvůli odlišnému UV/materialu nevytváří nový canonical knot.

Live draw zůstává podle REV-C §24.6–24.7 static fan 7/18 a indexed indirect.
Export smí vynechat neaktivní presentation trojúhelníky. Stejná aktivní plocha
nevyžaduje doslovně totožný index buffer s live maskovaným fanem.

## 6.4 Dokončit skutečný live RGBV consumer

V MerkabaGrid.shader je nutné dokončit tok analytického micro-normal až do
výstupní barvy podle níže výslovně povoleného V-1. Certifikovaný specular/V-2
není tímto dodatkem implementován ani nahrazen.

~~~text
address       vždy tři generated descents
draw split    exact hierarchical union RGB/V
RGB           captured linear radiance
V             A3ψ3 + A4ψ4 + A5ψ5; žádné replacement-V
gradient      derivace basis přes skutečný L2 chart/world frame
optical       pouze CERTAIN program; jinak correction = 0
geometry      žádná L3–L5 vertex/depth/silhouette změna
~~~

Neinterpretovat pole CaptureView jako camera origin/direction. Nese
capture/view residual podle existujícího optical ABI; jeho význam se nesmí
změnit kvůli bake. Nový capture-ray persistent field se nezavádí.

Nepřidávat odhad albeda, ambient field ani nové optické fitting pravidlo.
Presentation formula se řídí REV-C §23; geometrie a scan evidence na ní
nesmějí záviset. Pokud část certifikované optical formule nemá v současném
kódu uzavřený consumer, je to explicitní implementační gap tohoto runu,
ne povolení vymyslet další materiálovou autoritu.

Pro relative diffuse consumer platí přímo uzavřený výraz REV-C §23.2:

~~~text
E0 = max(Nbar · L, 0)
EF = sum(wi * max(Ni · L, 0)) / sum(wi)
lower(E0) > 0 → Cdiff = Ccapture * EF/E0
jinak        → Cdiff = Ccapture
~~~

Ni a wi pocházejí z analytického V pole a skutečně pokrytého footprintu;
L je explicitní presentation vstup, ne odhad zachyceného osvětlení.
OPTICAL_VALID smí být nastaven jen existujícím evidence predicate. Projít
celý řetězec producer → persistence → compact sample → fragment; samotná
existence polí nebo nastavování nuly není implementace certifikované větve.

### 6.4.1 Výslovný prezentační dodatek V-1

V-1 používá pouze existující analytický micro-normal a explicitní material
vstup `float4 _M8PresentationLight`: xyz je světový směr L, w je 0 nebo 1.
Výchozí hodnota je (0,0,0,0). Žádná kamera, CaptureView, albedo nebo ambient
se za L nedosazuje. Směr se normalizuje stejným pořadím FP32 operací na CPU
i v HLSL; neplatný nebo nulový zapnutý směr není platný prezentační vstup.

~~~text
color = Ccapture
pokud enabled && (sample.Flags & OPTICAL_VALID):
    L  = normalize(explicitPresentationLight.xyz)
    e0 = dot(N, L)
    ef = dot(Nmicro, L)
    pokud e0 > 2^-5:
        color *= saturate(max(ef, 0) / e0)
~~~

Pevný práh 2^-5 je výslovně povolená prezentační policy V-1, nikoli
geometrický epsilon ani důkaz optické certifikace. Vypnuté světlo nebo
chybějící OPTICAL_VALID vrací původní captured RGB bitově beze změny.
V-1 je bodový consumer; nevydává svůj vzorek za přesný footprint průměr
EF z obecného výrazu výše.
V-1 nevytváří OPTICAL_VALID, nemění jeho producer a nečte sample.Optical
ani CaptureView. V-2/specular zůstává oddělený; nepřidává se nový BRDF.
V mění pouze analytický normal, nikdy vertex, depth, silhouette nebo
ddx/ddy normal. CPU `RelativeDiffuse` je twin téhož výpočtu vedle
`SkinMicroNormal`. Default unlit export zůstává capture/off; prezentace,
která výslovně požaduje V-1, musí předat tutéž explicitní L.

Pro graph V s nulovou hranicí platí vektorový plošný integrál
`integral(Nmicro dAsurface) = Aplanar * N`. Normalizovaný směr integrálu
je tedy N, ale velikost area-average je `Aplanar/Asurface`, nikoli 1.
Toto tvrzení nezaměňuje normalizovaný směr za plošný průměr a nezavádí
momenty, Gram tabulky ani další footprint solver.

## 6.5 Portable preview a přesný native payload

Standardní unlit glTF reprodukuje base color. Neumí náš procedurální
Flower RGBV shader. Default preview proto obsahuje:

~~~text
L0–L2 evaluated geometry
captured RGB jako COLOR_0 nebo PNG atlas
CONFIRMED / COMPLETED / DIRT provenance
design strokes/objects jako presentation nodes
poznámky jako metadata
~~~

Plný V, optical intervals a přesná subdivision se zachovají v native records
podle §6.9. Po importu těchto records náš readout používá běžný evaluator.

Nelze požadovat přesný viditelný mikroreliéf v libovolném unlit glTF vieweru.
KHR_materials_unlit ignoruje normalTexture i ostatní nebázové PBR vlastnosti;
jejich fallback není totéž jako podporovaný unlit vzhled.
Viz [Khronos KHR_materials_unlit](https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_materials_unlit/README.md).

Default export nepeče smyšlený světelný pohled ani nepřidává neúčinnou
normalTexture jako příslib paritního mikroreliéfu. Optional presentation
materiály nejsou podmínkou této closure a nevytvářejí druhý export solver.

## 6.5.1 Signál a sampling

Rozlišení preview vychází ze skutečných scan-authored split masks, nikdy
z camera LOD ani whole-level validDepth. Pro full RGBV diagnostiku a native
payload se používá union RGB/V. Pro samotný unlit RGB atlas není V-only split
důvodem vyrábět barevné texely, které se neliší.

~~~text
RGB uniform → bez atlasu, COLOR_0 = captured RGB
RGB detail  → atlas přes společný RGB skin evaluator
V-only      → RGB může zůstat uniform; úplné V zůstává v native payloadu
DIRT        → support material, bez skin atlasu
~~~

Maxima 7/49/343 popisují logical sites. Částečné splits a clipped footprints
nemají automaticky tolik neprázdných terminal oblastí. Exportní sampling
depth je pouze odvozený parametr bake; nesmí se vrátit do readoutu/persistence
jako validDepth nebo drawDepth.

Pevný konečný atlas je presentation approximation: quantization a filtering
nemohou přesně reprezentovat každý skok RGB a analytický V ve všech bodech.
K=4 texely/oblast není Nyquist důkaz.

## 6.5.2 Bounded atlas bez packeru

Použít jednu uniformní mřížku buněk pro každou zvolenou velikost; alokace
je canonical ordinal, dělení a zbytek. Žádný shelf/bin packer, materiál
na wedge ani per-wedge 256² texture.

Default preview policy:

~~~text
nejhlubší explicitní RGB split      cell
L3                                  16
L4                                  32
L5                                  64
atlas page                          2048²
gutter                              2 texely uvnitř každé buňky
resident atlas pages                nejvýše 2
PNG                                 sRGB8 RGB; alpha dle materiálu
mip chain                           negenerovat v této closure
~~~

Jde o engineering preset, nikoli bezeztrátový geometrický důkaz. Full native
data tím nejsou zmenšena. Kvalita se ověří na fine RGB hranách; její změna
nesmí měnit canonical subdivision.

UV používá shared chart sites, nikoli nový geometric unwrap:

~~~text
perRow = pageSize / cell
col = ordinal % perRow; row = ordinal / perRow
o = (col*cell + 2, row*cell + 2)
uv = (o + (chartSite*0.5 + 0.5)*(cell-4)) / pageSize
~~~

Uvnitř hexagonu evaluovat zachycený RGB signál ve stanovených texelových
vzorcích. Filtering provádět v linear-light a teprve poté jednou sRGB encode.
Generated chamber boundaries a half-open ownership se nemění kvůli texelu.

Gutter/outside texely prodlužují hraniční presentation signál projekcí
na šest hran chartu, deterministicky při rovnosti. Je to atlas edge extension,
nikoli nový scan footprint, morphology nebo certificate. Dva gutter texely
nejsou obecný důkaz bezpečnosti libovolných mipů/anisotropního filtru.

## 6.5.3 Materiály bez dvojího násobení

~~~text
texturovaný carrier:
    baseColorFactor = white
    COLOR_0 = white nebo atribut chybí
    baseColorTexture = úplný captured RGB atlas

uniformní carrier:
    baseColorFactor = white
    COLOR_0 = captured RGB
    bez TEXCOORD_0 a bez baseColorTexture

DIRT:
    bílý COLOR_0 × DirtSupportLinearRgba factor
    bez měřené radiance, bez skin atlasu
~~~

Barva se násobí právě jednou. Současný správný postup writeru se nesmí změnit
na rootRGB × bakedRGB. Materiál je jeden na atlas page; uniform a DIRT mají
vlastní batches. Příslušná provenance je v primitive/face metadata.

GLB a 3D Tiles používají PNG default. KTX2, Draco a meshopt nejsou potřebné
k uzavření a nevytvářejí povinný nový on-device encoding stack.

## 6.6 Jeden bounded export sweep

Použít stávající CPU snapshot reader a jeden společný evaluator jako
default exportní backend. Zrušit export-only opravy nebo jinou geometrii,
ne storage IO nutné pro jeho vstupy. Nevytvářet druhý thermal scheduler ani
CPU/GPU přepínání algoritmu.

~~~text
source cursor:
    canonical logical page/owner/carrier order @ snapshot G

kvantum:
    načíst omezený required context
    vyhodnotit společný page program
    emitovat bounded presentation batch
    zapsat/spoolnout; uvolnit scratch; yield/cancellation point
~~~

Reuse hot page smí vynechat klasifikaci pouze při shodném snapshotu a
kompletních dependencies. Pokud se použije GPU materialization, je to tentýž
program a explicitní export job; readback je pouze bounded transport jeho
výsledku, nikdy scanner decision loop.

Rozpočty:

~~~text
export scratch / readback packet   nejvýše 8 MiB
resident atlas pages              nejvýše 2
IO/context window                 bounded podle aktuálního capacity ABI
step                               bounded carrier/texel packet + yield
CancellationToken                 všechny veřejné export calls a vnitřní IO
~~~

Stránka se může rozdělit na více packetů bez změny její identity nebo pořadí.
Rozpočet se počítá ze skutečných strides, nikoli z počtu stránek. Produkce
nesmí alokovat world-sized snapshot, List všech triangles ani plný atlas
pro celý svět.

Wall-clock budget je kooperativní: nerozbije uprostřed PNG encode nebo disk
write nevratnou práci. Velikost nedělitelného packetu musí být sama bounded.

## 6.7 Streaming, limity, cancel/resume

GLB spooluje accessors a obrazy na disk a finalizuje je jedním průchodem.
Celkovou délku kontroluje před zápisem: GLB v2 má 32bitový total length.
Streaming tento formátový limit neodstraňuje. Nad limit vrátit adresnou chybu
a nabídnout 3D Tiles, ne vytvořit přetečený GLB.
Viz [Khronos glTF 2.0 Specification](https://github.com/KhronosGroup/glTF/blob/main/specification/2.0/Specification.adoc#glb-file-format-specification).

3D Tiles používá bounded leaves, root-relative transforms/RTC a serializaci
po listech. Výchozí seskupení je canonical chunk:

~~~text
1 chunk = 4³ tiles = 0,8 m
~~~

Po překročení existujícího leaf byte budgetu rozdělit deterministicky podle
existing tile/owner/carrier ordinalů. Žádný nový prostorový strom, geometry
LOD solver nebo „výjimka“ s neomezeným listem. Prázdný list se nevydává.

ZIP může být ZIP64; exportní knihovna a importní reader musí umět skutečnou
velikost balíčku. Zápis je streamovaný s bounded per-entry spool tam, kde ho
writer potřebuje. Staging není zakázané slovo; zakázaná je druhá kompletní
kopie celé scény a full-archive RAM buffer.

Jeden export receipt/journal obsahuje:

~~~text
source G + held current base/log ends + document/assets revision
contract/codegen/options hash
next canonical cursor
completed entry lengths/checksums
pending / written / unresolved region s důvodem
~~~

Po restartu pokračovat jen po opětovném výhradním zastavení mutací a ověření
stejných dostupných source records, cutu a document/assets revision.
Chybějící nebo změněný zdroj znamená RESUME_SOURCE_UNAVAILABLE, nikoli
pokračování nad novým světem. Nevzniká povinnost historického MVCC ani
automatického uchovávání starých verzí. Neúplný poslední spool/ZIP entry se
ignoruje nebo obnoví od posledního ověřeného boundary; není automaticky
dokončený.

Cancel je signál workerům, nikoli předčasné uvolnění source gate. Již
rozpracovaný canonical drain/commit se bezpečně dokončí včetně SSD ACK;
export awaitne všechny spuštěné read/evaluate/write workery a teprve potom
v finally uvolní gate. Stejné pořadí platí při chybě a lifecycle teardown.
CancellationToken se přenáší přes geometry sweep, bake, writers i archive IO.
Cancel zachová obnovitelný receipt nebo výslovně zahodí pouze vlastní staging.
Finální název se publikuje až atomickým dokončením; předchozí publikovaný
výstup při chybě zůstává. Unresolved region znamená PARTIAL s vyjmenovanými
mezerami, nikoli COMPLETE.

Deterministické soubory vyžadují stejné serializační pořadí, encoder options,
PNG metadata i ZIP timestamps. Shodná geometrie sama nezaručuje stejné bytes.

## 6.8 Live draw není exportní mesher

Zachovat static generated fan a canonical KnotAddress. Nevydávat
3*popcount(active) live index stream jako drobnou opravu — měnil by frozen
REV-C draw ABI a dynamic-index zákaz.

Dopad discard, LRZ, multiview a forward materialu se měří na Adrenu.
Ze samotného slova discard nelze odvodit úplnou ztrátu LRZ ani slíbit konkrétní
násobek fps. Exportní odstranění inactive triangles nemění live topology.

## 6.9 Jeden obnovitelný balíček, dvě úrovně kompatibility

Native balíček je nově dopojovaný consumer existujícího session store,
nikoli již hotová vlastnost současného GLB.

Payload obsahuje:

~~~text
konkrétní committed session manifest
manifestem vybrané base files a log prefixy do ValidEnds
všech 13 scan record kinds beze změny jejich ABI
design.json snapshot
snapshot annotations
referencované content-addressed asset blobs + jejich index
package version, offsets/lengths, hashes a anchor metadata
~~~

Design, poznámky a asset library nejsou automaticky součástí třinácti scan
record kinds. Jejich konzistentní snapshot se pořídí a ověří výslovně.

~~~text
GLB:
    optional application extension M8_flower_thread
    odkaz na versioned package manifest a payload bufferViews
    extensionsUsed ano; extensionsRequired ne

3D Tiles ZIP:
    stejné native soubory + package manifest
    tileset.json extras odkazuje na manifest
~~~

Neznámé optional extension může standardní čtenář ignorovat a zobrazit
preview. M8_flower_thread se neprezentuje jako registrované Khronos rozšíření.

Importer kontroluje verze, délky, CRC/hash, adresy, ParentEpoch a asset refs
před aktivací. ZIP paths nesmějí uniknout cílovému adresáři; dekomprese,
manifest i jednotlivé entry mají kontrolované limity.

Importuje se do nové validované session/read-only model instance. Import
nesmí přepsat aktivní neuložený scan ani slučovat mesh vertices do M8.
Aktivace používá obyčejnou OPEN/anchor/residency/readout cestu.

~~~text
náš soubor s validními native records:
    restore canonical state → ordinary page compiler/readout

cizí nebo preview-only GLB/Tiles:
    Mesh + preview shader; UI výslovně označí náhled
~~~

Při obnově stejného snapshotu, stejné kotvy a stejných presentation vstupů
musí náš renderer vyhodnotit stejnou geometrii/RGBV. Rozdílný pohled,
lokalizace nebo 8bitový preview není bit-identický screenshot.

Paint preview používá stávající stroke geometry generator; design objects
používají vlastní asset nodes. To nejsou nové scanner geometry authorities.

## 6.10 Změny souborů a odstranění reliktů

~~~text
MerkabaExporter.cs
    jeden snapshot cursor, bounded sweep, cancellation/receipt, package

MerkabaGlbWriter.cs / MerkabaTilesetWriter.cs
    presentation serialization, deterministic batching, format limits

MerkabaFlowerMaterialBake.cs
    nahradit per-wedge bake carrier atlasem nad společným skin evaluátorem

MerkabaFlowerPresentation.cs / MerkabaSphereFlowerReader.cs /
MerkabaGrid.Reader.cs / MerkabaSsdStore.cs
    sjednotit skutečně odlišné evaluation/admission rules
    ponechat potřebný snapshot/context reader a shared CPU evaluator
    odstranit nahrazené export-only repair/weld/source duplicates

MerkabaFlowerVertex.hlsl / Geometry / SkinReadout / SkinEvaluation
    společné canonical formulas/tables, skutečně spotřebovaný RGBV výsledek

MerkabaArtifactViewer.cs / persistence a package reader
    validace native payloadu, isolated import, obyčejný readout consumer
~~~

Nový PageEmit nebo Atlas soubor vznikne jen jako přesun jednoho uceleného
odpovědného mechanismu. Žádná wrapper/helper vrstva kolem zachované druhé
rekonstrukce. Žádný automatický delete správné funkce podle názvu „CPU“.

~~~text
PROOFS=CPU/HLSL page/knot/skin parity nad stejným G a inputs;
       deterministic active ownership/winding/RTC;
       RGB se nenásobí dvakrát; atlas exact evaluator ve vzorkovacích bodech;
       native payload round trip včetně V/design/assets/annotations;
       bounds/cancel/resume a pojmenované unresolved regions
ACCEPTANCE=jedna sémantika evaluátoru; bounded export bez mesh repair;
           standard-valid barevný preview; přesný native obnovitelný signal;
           import našeho payloadu používá ordinary readout;
           žádný slib přesného V v nepodporujícím unlit vieweru
ROLLBACK_BOUNDARY=celý přepojený consumer; stará export geometry není fallback
~~~

---

# §7 RUN_11 — dopojení UI, pickeru a rodičovských oprav

~~~text
ID=RUN_11
DEPENDS_ON=d60e053
NEW_AUTHORITY=žádná
~~~

Chybějící ancestor commit není důkaz chybějící funkce. Funkcionality
a4613eb / 98009a0 se ve forku částečně implementovaly jinak. Nepřenášet celý
commit ani jeho starou export/readout architekturu.

## 7.1 Skutečné mezery

~~~text
export name:
    doplnit export-name field a pojmenované overloads všech formátů

Save-As:
    ACTION_CREATE_DOCUMENT, správný MIME, cancellation/error stav
    streamovat soubor; žádný byte[] celého APK/modelu

URI:
    existující MIME/permission request flags zachovat
    persistable grant skutečně převzít jen pokud ho provider udělil
    bezpečný fallback je opětovná volba souboru, ne kopie geometry authority

viewer:
    existující ReadPackageIndex / CollectTiles a streaming import ponechat
    doplnit bounded manifest/path/length validaci a native payload větev §6.9
    nekopírovat druhou tileset čtečku jen kvůli chybějícím názvům helperů
~~~

## 7.2 Session workspace

ActiveDesignPath, rebind a save-before-release už mají consumers.
Doplnit jen chybějící:

~~~text
annotations/session binding
dirty Save-As / new session přepnutí bez cizího workspace
display-frame transform a input priority
paint-row-* per-tool inspector wiring
~~~

Zdrojové oblasti: MerkabaPersistence, MerkabaArtifactViewer, RoomScanner,
ControllerRayDriver, DebugMenuController, DebugMenu UXML/USS a Android picker.

~~~text
ACCEPTANCE=pojmenovaný export + skutečný Save-As; bounded picker/viewer;
           design i notes patří správné session; žádné duplicate readers
ROLLBACK_BOUNDARY=samostatný ucelený consumer patch, ne celý parent commit
~~~

---

# §8 RUN_12 — persistence, session a anchor

~~~text
ID=RUN_12
DEPENDS_ON=RUN_10,RUN_11
NEW_AUTHORITY=žádná
~~~

## 8.1 Zachovat hotové jádro

Manifest v4/208 B, CRC, anchor metadata, ValidEnds, třináct record kinds,
generation-bound append a epoch replay se neruší.

FinishObservationDurableCutAsync čeká na whole-source drain a skutečné
append ACK. Manifest se publikuje poslední po flush/fsync. Page fence ani
page publication nejsou durable generation frontier.

~~~text
OPEN:
    manifest validation před změnou světa
    replay jen committed prefix
    stale ParentEpoch odmítnout
    required SCAN residency
    anchor localization
    DRAW/WARM pokračují nezávisle
~~~

Epoch wrap/rebase musí mít complete-capture/purge receipt. Kompatibilní
plane update nesmí zbytečně zahazovat fine state.

## 8.2 Application documents a save stav

Design/annotations jsou aplikační dokumenty, ne nový scan record kind.
Při SAVE/session switch/export cutu:

~~~text
zmrazit revizi designu/notes a seznam asset refs
durably uložit dokumenty atomickým file replacementem
dokončit canonical scan manifest cut
zveřejnit úspěch až po obou receipts
~~~

Nedeklarovat bez změny manifest ABI crash-atomickou transakci scan+paint.
Po chybě přiznat neúplný SAVE; exportní package manifest musí přesně odkazovat
na ty revize dokumentů, které skutečně obsahuje.

Thumbnail vytvořit jako bounded derived model preview po SAVE nebo zobrazit
jednoznačný placeholder. Automatický capture HMD/passthrough není nutná
součást této closure. thumbnailPath neukazuje na neexistující soubor.

## 8.3 Anchor a importní větve

Zachovat skutečnou transformaci:

~~~text
anchorNow * inverse(anchorAtSave) * sceneGridToWorld
~~~

Jednotkový round trip ověřuje implementaci matice v jejím numerickém
representation boundu; libovolný 1e-6 práh není fyzická záruka XR lokalizace.

Přesné world umístění po OPEN vyžaduje dostupný a úspěšně lokalizovaný anchor.
Při neúspěchu explicitní stav/retry/manual ALIGN podle existujícího UX;
žádná domnělá identita transformace. Cizí glTF není anchorovaný canonical scan.

~~~text
PROOFS=manifest/CRC/tail replay; epochs/rebase; failed flush;
       document switch/Save-As; package restore a anchor transforms
SYSTEM=SAVE → OPEN → export obou formátů → native import → ALIGN →
       FINE → ERASE → SAVE → OPEN; preview-only větev odděleně
ACCEPTANCE=žádná záměna session/workspace; žádná falešná durable/frontier;
           fine RGB/V přežijí; lokalizační failure je viditelný
~~~

---

# §9 RUN_13 — ovládání bez nové runtime autority

~~~text
ID=RUN_13
DEPENDS_ON=RUN_11; finální wiring export/import čeká na RUN_10/RUN_12
NEW_AUTHORITY=žádná
FILES=DebugMenu.uxml/.uss, DebugMenuController.cs,
      ControllerRayDriver.cs, stávající viewer/operation handlery
~~~

## 9.1 Zachovat a přeuspořádat

Současný panel již má taby, primární scan akci a viewer stav/gesta.
Zachovat jejich wiring; přeuspořádat je podle lokální šablony
/home/wraith/Stažené/uniscan-wrist-console.html, nikoli tvrdit, že neexistují.

~~~text
top bar      session name → Library; undo/redo; overflow
tabs         Scan | Refine | Design | View
Scan         Start/Stop, skutečné scanner/anchor/residency stavy
Refine       Refine/Erase, Radius/Reach
Design       Paint/Objects; sedm nástrojů; inspector aktivního nástroje
View         model/package stav, Open/Close, Plan/World lock/ALIGN,
             opacity a existující Measure/Notes
Library      Scans / Exports / Models, name/format/Save-As/cancel/result
operation    skutečná aktuální stage, progress a konkrétní failure
diagnostics  overflow; mimo primární produkční plochu
~~~

Jedna primární akce na tab, hit target nejméně 44 px, primární 54 px.
Session file operations nejsou namačkané pod Start. Inspector nezobrazuje
neaktivní tool properties. Barva akcentu nesmí přepsat význam error/ambiguous.

Použít reálné UI Toolkit controls, flex a ověřené USS. HTML grid/gap nebo
inline SVG hit regions nejsou automaticky podporovaná UXML implementace.
Ring má sedm skutečných hit targets; žádný nový generický UI framework.

## 9.2 Žádné vymyšlené metriky

~~~text
coverage:
    jen skutečně měřené údaje nad výslovně ohraničenou doménou
    chybějící dual FULL není změřený „unseen volume“
    MIXED není automaticky root ambiguity

certified volume:
    unique elementary cells / collapsed disjoint nodes
    ne součet osminásobně překrytých M8 supports
    neprovedené měření se ukáže jako unavailable, ne odhad

geometry/skin:
    L2 geometry a fixed L3–L5 signal; žádné „validDepth“ ovládání

notes:
    zachovat existující session annotations a transformaci
    neslibovat automatické přežití smazaného/revytvořeného sheetu
~~~

Nový raymarch „peel count“ ani nový persistent knot-note attachment nejsou
nutné k této closure. Nevydávat je za levnou UI vazbu; nezavádět kvůli nim
novou geometrickou pravdu, identity repair nebo unsupported ovládací prvek.

Coverage mapa se vede nad skutečnými současnými elementy a handlery,
ne nad magickým počtem 121 z jiného commitu. Každý odstraněný control má
doloženou náhradu nebo chybějící backend.

~~~text
ACCEPTANCE=scan/refine/erase/design/view/library ovladatelné;
           žádný mrtvý control nebo vymyšlený údaj;
           sessions/import/export/cancel mají viditelný stav;
           controller a two-hand interaction zachovány
ROLLBACK_BOUNDARY=UI/wiring; žádná změna canonical authorities
~~~

---

# §10 RUN_14 — úplná validace a APK

~~~text
ID=RUN_14
DEPENDS_ON=RUN_08,RUN_09,RUN_10,RUN_11,RUN_12,RUN_13
~~~

## 10.1 Code a oracle closure

Po dokončení funkčních změn jeden úplný průchod:

~~~text
1  source review dotčených authority/consumer řetězců
2  generated tables a CPU/HLSL parity
3  complete test suite, ne součet focused běhů
4  standard GLB/Tiles/package validation
5  opravy skutečných failures, znovu relevantní brány
6  Unity Quest Android build
7  instalace a device acceptance
~~~

Odstranit tests asertující smazanou architekturu; zachovat nebo nahradit jejich
behaviorální invariant. Passing mask helper není passing completion fixture.
Nevymazat failující pozitivní test jen proto, aby suita zezelenala.

Required REV-C §29 zůstává celý: interval rounding, boundary/ownership,
R2/R3, subtree/thread/chambers, V, epochs, negative/boundary coordinates,
same-observation eager/drain, dual certificate, hole/ghost a export/live.

Symmetry/codegen kontrola ověří 48 signed permutations nad 26/72/48 incidence
včetně winding. Nepřidává runtime solver ani libovolnou cross-shell heuristiku.

## 10.2 Brány a receipts

~~~text
Tools/shaders/audit_merkaba_compute_spirv.sh
Tools/shaders/audit_merkaba_command_graph.sh
Tools/unity/run_merkaba_tests.sh
Tools/gltf/validate_merkaba_glb.sh
Tools/unity/build_merkaba_apk.sh
~~~

Neexistující gate se implementuje v příslušném runu; pouhý uvedený název není
důkaz běhu. Každý receipt obsahuje commit + diff hash, tools, příkaz, artefakt
hash, test counts a outcome. Full-suite ze starého stromu není PASS nového.

Build použije /mnt/kingston-unity a stávající runbook. Omezit počet souběžných
Unity/compiler procesů, měřit peak RSS, stopnout runaway compile s diagnózou.
Nepřepisovat Unity projekt do jiného IDE/workspace a nezaměnit native.so build
za hotové APK.

## 10.3 Device acceptance

~~~text
startup cold/warm; všechny pipeline compile times a enabled features
SCAN / STOP / RESUME / WAKE
same observation drained při stojící kameře
FINE / ERASE / dynamicky přidaný i odebraný objekt
SAVE / OPEN / anchor failure + recovery
GLB / 3D Tiles / native package / cizí preview
design, notes, library, controller/two-hand
head rotation vs translation, cold/warm, multi-room/large-space
required geometry/sensor/ghost/hole/skin fixtures REV-C
~~~

Měřit timestampy všech skutečných stages, retry/continuation count,
page throughput, GPU draw/cull, CPU storage/publication/submission,
paměť každé arény a process PSS. Při passthrough ověřit sustained průběh,
ne jen několik sekund bez thermal zátěže.

Neproběhlé zařízení = DEVICE ACCEPTANCE PENDING. Neúspěšná pipeline = FAIL.
APK, které čeká minuty a nic nekreslí, není částečný runtime PASS.

---

# §11 DAG, commits a definice hotovo

~~~text
RUN_08  execution/startup                     ← checkpoint + pracovní strom
RUN_09  direct/dual/completion                ← RUN_08
RUN_10  shared presentation/export/package    ← RUN_09
RUN_11  picker/UI/session consumers           ← checkpoint; nezávislé
RUN_12  durable/session/anchor closure         ← RUN_10 + RUN_11
RUN_13  UI                                   ← RUN_11; final wiring 10/12
RUN_14  complete validation/APK/device        ← všechny předchozí
~~~

RUN_08 navazuje na už rozpracované startup/shader opravy. RUN_11 a čisté UI
práce mohou probíhat souběžně v oddělených souborech; nelze jimi přepsat
rozpracovaný consumer jiného runu. Žádný nový dispatch/refactor framework.

Po každém uceleném runu commit a aktuální lasttrue cursor. Compiler checkpoint
nebo měřicí gate commit se tak výslovně nazývá; nikdy nepředstírá uzavřený
PASS. Relevantní build/parity nutné pro bezpečný cutover zůstávají bránou;
kompletní validační průchod je RUN_14.

Každý run předá:

~~~text
FILES_CHANGED
AUTHORITY/CONSUMERS
LEGACY_REMOVED
INVARIANTS
TEST/BUILD/PERF receipts nebo přesné NOT RUN
REMAINING_GAPS s vlastníkem
NEXT_RUN
~~~

Hotovo znamená:

~~~text
REV-C ontology/algebra beze změny
žádný alternate geometry/export solver ani legacy fallback
shader size/ABI gates PASS; startup na zařízení bez hang/link failure
bounded command graph a více úspěšných pages/batch
GPU-only scan/refinement/readout; storage/export transport oddělen
R1/dual/epochs/completion/dynamic scene správně
L2 geometry + fixed 399 skin + nested V skutečně spotřebované rendererem
standard-valid barevný portable preview
přesný native RGBV/session/design/assets/notes payload a ordinary-readout import
bounded/cancellable/resumable export bez tichých mezer
SAVE/OPEN a anchor stavy poctivé, workspace nepřechází mezi sessions
úplná suita PASS
Unity Quest APK BUILD PASS
DEVICE ACCEPTANCE PASS na identifikovaném zařízení a artefaktu
~~~

Cílová cesta:

~~~text
stereo interval evidence
        ↓
M8 + sparse excavation + FlowerDetail + ThreadAtlas
        ↓
jedna generated Flower sémantika
        ↓
bounded GPU pages/draw     bounded snapshot export/native package
        ↓                              ↓
procedural prezentace        portable preview + přesná native obnova
~~~

Žádná stránka ani textura není nový canonical svět. Optimalizuje se provedení
stejné informace, nikoli její význam.
