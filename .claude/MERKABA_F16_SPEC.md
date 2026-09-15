# MERKABA F16 — cykloidní klec, uzlová síť, kůže
## Deterministická specifikace, verze 2

2026-09-05, proti `863e75d`. Každá konstanta v tomto dokumentu je **odvozená**, ne
zvolená, a je ověřená výpočtem proti `Runtime/Merkaba/MerkabaCanonicalGeometry.cs`
a `MerkabaConstants.cs`. Kde je potřeba konvence, je to výslovně označené a je
uvedený tie-break.

---

# I. ONTOLOGIE

## I.1 Uzavření

```
M8 kernel = Merkaba          MerkabaCanonicalGeometry.cs
                             oktaedr, vrcholy ±25 mm na osách
                             8 hrotů, apexy (±25,±25,±25)

VŠECH 36 hran Merkaby má JEDNU délku:
    12 hran oktaedru          35,3553 mm
    24 hran hrotů             35,3553 mm
                          λ₀ = 25√2 = 35,3553 mm

λ₀ graf na M8 mřížce = 12 stěnových úhlopříček
    parita (x+y+z) se zachovává  →  ℤ³ = DVĚ prostupující se FCC mřížky
                                     12 nejbližších sousedů, nn = λ₀

Delaunay FCC = tetraedricko-oktaedrická plástev
    všechny stěny rovnostranné, strana λ₀   →   žádná nejednoznačnost úhlopříčky

oktaedry té plástve jsou centrované na OPAČNÉ paritní třídě
    kernel K sedí v oktaedrické dutině FCC mřížky opačné parity
    a jeho 6 osových sousedů (25 mm, opačná parita) jsou vrcholy toho oktaedru
```

> **`MerkabaCanonicalGeometry` je buňka té plástve.** Triangulace není zvolená —
> je vynucená. Proto oktaedr a proto ta „práce navíc".

## I.2 Kernel je graf sousedství

```
vrchol oktaedru (25,0,0)   = střed osového souseda           6 ×,  25 mm
hrana oktaedru  λ₀          = délka i směr stěnového souseda  12 ×, 35,36 mm
apex hrotu (25,25,25)       = střed tělesového souseda         8 ×,  43,30 mm
```

Merkaba nese **všech 26 sousedů** vlastní geometrií. Krychle to neumí: 8 rohů dosáhne
jen na tělesové úhlopříčky, zbylých 18 vazeb nemá kotvu.

## I.3 Klec se kernelu nedotýká

Každá z 12 hran oktaedru spojuje **dvojici osových sousedů K**, ne K. Klec je utkaná
mezi sousedy, kolem K. A K je cuspem klecí, které vlastní jeho vlastní sousedi.

> **Invariant je cusp na kernel, ne obvod na hranu.**
> Kotvou každého oblouku je jiný kernel. Zámek není test, je to identita.

## I.4 Co je dnes špatně

Kernel ukládá **rovinu** (oct normála 20 b + offset 8 b ve `Flags`). Rovina je
per-kernel nezávislý objekt, takže se spojitost musí dobývat zpět testy — dvěma tvrdými
rovnostmi nad kvantizovanou šumící normálou (`DominantAxis`, `CanonicalSheet`).

Změřeno portem `TryResolveCorner` do Pythonu, roviny kvantované přesně jako
`SetSurfacePlane`, okno 13³:

```
orientace              rozporné rohy   max prasklina
osová / 45° / (1,1,1)      0,0 %          0,000 mm     ← středy chartu
30° náklon                42,9 %          0,151 mm
libovolný sklon           62,5 %          0,085 mm
30° náklon, σ = 5 mm      63,3 %         19,230 mm
```

Krychle 30 cm, 866 kernelů slupky: **13 nekompatibilních tříd**, **140 z 866 = 16,2 %**
(hrany a rohy) se ke stěnám nesešije. Cena: 512 × 48 = **24 576 vyhodnocení kandidáta**
na dlaždici, ≈ 73 728 `normalize`. Relikt: outlier přijat na první zásah, uloží rovinu,
a `M8HasContinuingCarveSheet` (test kvantizované kompatibility) ho ochrání, protože
sourozenci z téhož shluku šumu ho splní → `OFF+1 = 129` navždy.

Jedna příčina: *ukládá se nezávislý objekt a spojitost se testuje.*

---

# II. GEOMETRIE

## II.1 Perioda

```
λ₀ = hrana oktaedru               = 25√2 = 35,3553 mm
λ₀ = hrana hrotu                  = 25√2
λ₀ = krok koplanárního souseda    = 25√2
λ₀ = hrana hvězdného tetraedru/2  = 25√2
r  = λ₀/2π = 5,6270 mm            oblouk 2r = 11,2540 mm
```

Uzavřené obvody na kernelu jsou celé počty period: 3 rovníkové čtverce **4,0000 λ₀**,
4 Petrieho šestiúhelníky **6,0000 λ₀**, 8 faců hvězdného tetraedru **6,0000 λ₀**.
Smyčka kernel obtočí a zavře se s nulovou fázovou chybou.

(25 mm dá poměr √2, 20,4124 mm dá √3 — ani jedno nezamyká. λ₀ je jediná perioda,
při které je zámek přesný.)

## II.2 Šestnáct rodin

Na každé z 8 faců ortogonální dvojice směrů odvalování:

```
U = vrchol → střed protilehlé strany   ∝ (−2,1,1)/√6      λ_U = 25√6 = 61,2372 mm
V = kolmice v rovině face              ∝ (0,−1,1)/√2      λ_V = 25√2 = 35,3553 mm
U·V = 0,0e+00      U·n = 0,0e+00      λ_U/λ_V = √3       8 × 2 = 16
medián face = λ_U/2 = 30,6186 mm
```

**U × V je přesně obdélníková elementární buňka trojúhelníkové mřížky roviny face.**
Proto jsou kolmé a proto stačí dvě: tři hranové směry jsou trojúhelníkový primitivní
rám, ortogonální dvojice je obdélníkový popis téže mřížky.

Rovina face (1,1,1) je `x+y+z = 25 mm`, tedy **vrstva sousedů**; A, B, C jsou středy
`K+eₓ, K+e_y, K+e_z` a tvoří elementární trojúhelník té vrstvy. Nosná dráha je natažená
přes středy sousedů.

## II.3 Uzel

Uzel je **cusp**. Cusp je bod nulové rychlosti stopy, tedy kotva. Cuspy leží na
vrcholech oktaedru, které jsou středy sousedů. Proto:

> Uzel = kernel. Nehledá se, načte se.

Všech 16 rodin i všichni sousedi sdílejí u daného kernelu **týž jeden cusp**.

## II.4 Vlastnictví

Uzel se smí pohnout **±12,5 mm** = Voronoiova buňka mřížky. Každý naměřený bod povrchu
patří právě jednomu kernelu:

```
owner(H) = MerkabaSpatial.NearestKernel(H / LatticeStep)
```

Tím padá celé dnešní owner routing (`MerkabaIntegration.compute:475-620`): `bestOwner`,
hledání na `K ± normalStep`, `perpendicularDistance ≤ HalfSupport`,
`alongError ≤ SupportSize`, `unresolvedOwner`. Nahrazuje to jedno zaokrouhlení.

Třídy `DISCOVERY / SUPPORT / REVISION` **zůstávají** — řídí *povolení* mutovat
(S3 attention cone), což je jiná věc než geometrické vlastnictví.

## II.5 Separace plátů je metrická

Dva pláty 50 mm od sebe jsou 2 kroky mřížky. Stěnová úhlopříčka mění dvě osy o 1, takže
kernely obou plátů **nejsou v λ₀ grafu sousedi**. Separace je zabudovaná v topologii,
ne v predikátu klasifikace sheetu. Tenká příčka i dvě blízké rovnoběžné plochy zůstanou
oddělené bez jediného testu.

---

# III. ULOŽENÝ STAV

`KernelState` zůstává 16 B. `OccupancyEvidence`, `PackedColor`, `ColorConfidence`
beze změny. Mění se jen význam `Flags`.

```
b0        OccupiedFlag                 beze změny
b1        NeedsCarveFlag               beze změny
b2  – b10 dx  9 b                      posun uzlu podél x, ±12,5 mm
b11 – b19 dy  9 b
b20 – b28 dz  9 b
b29 – b30 rezerva, musí být nula
b31       NodeValidFlag                nahrazuje SurfacePlaneValidFlag
```

```
encode(d) = clamp((int)floor((d/12.5 + 1.0) * 255.5 + 0.5), 0, 511)
decode(v) = ((float)v / 255.5 - 1.0) * 12.5
rozlišení = 25 mm / 512 = 0,048828 mm na osu      (dnes offset roviny 0,196850 mm)
```

Ukládá se **bod**, ne rovina. Normála se neukládá — je důsledkem sítě. Uzel je
sdílená kotva, takže spojitost je strukturální.

**Zdarma a neuložené:** incidenční graf (kdo s kým, kterou rodinou), zákon oblouku,
fáze. Vše odvozené z `kernel coord + face + rodina + úroveň`.

---

# IV. SKEN

`IntegrateSurfaceCandidates` — nahrazuje se jen druhá půlka (dnešní `SetSurfacePlane`).

```
pro přijaté joint měření s endpointem H:
    K = NearestKernel(H / LatticeStep)          // vlastnictví, jedno zaokrouhlení
    d = H - K * LatticeStep                     // |d|∞ ≤ 12,5 mm z konstrukce
    w = round(q² * 256)                         // stávající q² váha

    pokud !NodeValid:  ulož d, nastav NodeValid, C = w
    jinak:             d ← (d_old * C + d * w) / (C + w)
                       C ← min(C + w, MaximumColorConfidence)
```

`C` je stávající `ColorConfidence` použitá i jako konfidence uzlu (viz otevřený bod 2).

**Evidence a hystereze beze změny:** `ON 512`, `OFF 128`, `SURFACE 640`, `FREE 256`,
cap `±2560`, `MinimumSurfaceQuality 0.25`, fast admission na `OCCUPIED_ON − e`.

Rychlost nepřichází ze změny prahů, ale z toho, že **první platné měření rovnou umístí
uzel**, místo aby se poloha povrchu později rekonstruovala z roviny.

---

# V. ZPŘESNĚNÍ

Uzel je jedno číslo, do kterého zapisují všechna měření padnoucí do jeho buňky. Vážený
běžící průměr konverguje jako `σ/√n` **bez jakéhokoli dodatečného průchodu**, protože
neexistují dvě čísla, která by se musela smiřovat.

Nad tím napětí sítě: oblouk mezi dvěma cuspy musí být cykloida periody λ₀. Uzel, který
to poruší, je pod napětím ze všech incidentních rodin současně.

```
tension(K) = Σ_{Q ∈ λ₀-sousedi(K)}  ( |node(K) − node(Q)| − λ₀ )²
```

Relaxace je projekce na minimum. Protože je F16 geometrie pevná, mapování se generuje
jednou a runtime je pevný FMA stencil (viz otevřený bod 3). **Proto přesnost senzoru
přestává rozhodovat** — kvalita nevzniká z jednoho měření, ale z uzavřenosti sítě.

**Duch se rozpadne geometricky.** Uzel ducha leží v buňce, jejíž λ₀ sousedi buď uzel
nemají, nebo ho mají o řád dál. Oblouk se nezavře. Není to nízká konfidence, je to
nespojitelnost.

---

# VI. CARVE, FINE, ERASE

Struktura beze změny. Tři úpravy:

1. `M8HasContinuingCarveSheet` — dnes test kvantizované kompatibility přes 3×3×3 halo,
   tedy mechanismus reliktů. Nově: pokračující plát existuje ⟺ aspoň jeden λ₀ soused
   má platný uzel a jeho oblouk se zavře. Žádná normála, žádná kvantizace.
2. Přechod occupied → non-occupied maže uzel a `NodeValidFlag` (tam, kde dnes
   `KernelState.ApplyWeighted` maže rovinu).
3. `EraseFineTiles` maže uzel navíc. `FineBrushDescriptor` a válcový predikát netknuté.

---

# VII. READOUT

**Pipeline se nemění.** Stejný nativní executor, stejný handshake, stejné job kindy,
stejná FRONT/BACK publikace, stejný indirect indexed draw, stejná kapacita
2 097 152 trojúhelníků na slot, stejný scheduler v `MerkabaGridRenderer.LateUpdate`.

## VII.1 Výběr

`QueryM8Readout` beze změny (3,309 ms). Merkaby se vybírají existujícími
`_M8TileRecords.occupiedCount` a viditelností dlaždice.

## VII.2 Čtení uzlu

```
node(K) = K * LatticeStep + decode3(flags)      3 shifty + 3 mad
```

Žádný `normalize`, žádný oct-decode, žádný `DominantAxis`, žádný
`NearestGridNormalStep`, žádný sloupcový sken, žádná klasifikace.

## VII.3 Emisní pravidlo

Fabric hrana `(P,Q)` existuje ⟺ P a Q jsou λ₀ sousedi, oba occupied s `NodeValid`.
To je **bitmaskový test nad slovy, která už v groupshared jsou**.

Trojúhelník se emituje jako **stěna plástve**: pro každý occupied kernel `K` a každý
z jeho 8 faců oktaedru je stěna trojice `(K+σ_a e_a, K+σ_b e_b, K+σ_c e_c)`. Emituj,
pokud mají všechny tři platný uzel. Každá stěna patří právě jednomu `K` — **žádné
duplicity, žádné překryvy**.

Winding: z pořadí vrcholů v `MerkabaCanonicalGeometry.DirectionRule`, které už míří
ven ve směru `Offset`. Žádný vstup kamery.

## VII.4 Degenerovaný případ — osově zarovnaný tenký plát

Osově zarovnaná rovina řeže plástev na čtvercovém průřezu: uzly se rozpadnou na dvě
paritní třídy, každá tvoří **čtvercovou mřížku 35,3553 mm**, a čtverec potřebuje
úhlopříčku, která v λ₀ grafu není.

> **Toto je jediné místo v celém návrhu, kde je potřeba konvence.**

```
pravidlo: emituj kratší úhlopříčku podle NAMĚŘENÝCH poloh uzlů
tie-break: přesná rovnost → lexikograficky menší dvojice int3
```

Je to řízené daty, ne tabulkou: na zakřiveném plátu vyhrává ta, která sleduje
zakřivení. Marching cubes a dual contouring řeší totéž case tabulkou nebo hodem mincí.

## VII.5 Výšivka a proč nejsou překryvy

Každý oblouk se klene ze sheetu až o `2r = 11,2540 mm` **ke straně svého vlastníka**.
Dvě úhlopříčky čtverce patří druhým dvěma rohům, klenou se na opačné strany, a proto se
kříží v projekci, ale ve 3D projdou v různých výškách. Útek a osnova, ne z-fight.

## VII.6 Zvlnění zadarmo

Oblouk je uzavřená křivka. Povrch mezi třemi uzly je plát ohraničený třemi cykloidními
oblouky a jeho tvar je analytický. L0 emituje trojúhelníky uzlů; vyklenutí se
vyhodnotí ve vertex/fragment stupni z fáze. Hustá geometrie je potřeba jen na siluetě.

## VII.7 Cena

```
                  dnes                                    klec
klasifikace   512 × 48 = 24 576 vyhodnocení/dlaždici   0
              ≈ 73 728 normalize/dlaždici              0 normalize
uzel          odvozen z roviny (oct-decode)            3 shifty + 3 mad
hrany         sloupcový sken 4×3 s predikáty           512 × 12 bitmask, data už v LDS
krychle 30 cm 3 464 vertexů / 1 732 tri (soup)         866 vertexů / ~1 732 tri (spojené)
```

Čtyřikrát méně vertexů a spojená síť místo soupu — na tileru přesně ten rozdíl, který
dělá `Draw` (dnes 5,115 ms z 13,89 ms rozpočtu na 72 Hz).

---

# VIII. KŮŽE

## VIII.1 Úrovně

```
úroveň   λ_l          oblouk λ_l/π   krok/λ_l   role
L0    35,3553 mm      11,2540 mm         1      naměřená geometrie
L1    17,6777          5,6270            2      naměřená geometrie
L2     8,8388          2,8135            4      přechod
L3     4,4194          1,4067            8      mikropovrch, generovaný
L4     2,2097          0,7034           16      mikropovrch
L5     1,1049          0,3517           32      mikropovrch
```

`krok souseda / λ_l = 2^l` — celé číslo na každé úrovni. Proto žádné UV, žádný atlas,
žádný seam solver, žádná akumulovaná fázová korekce.

Hranice L2 je dána pixel footprintem Questu (~2 mm na 2 m). Výšky oblouků L3–L5
(1,41 / 0,70 / 0,35 mm) jsou reliéfní měřítko omítky, dřeva a textilu. To je „brva".

## VIII.2 Kůže nejde přes frontu

Stavět L3–L5 jako geometrii do vertex bufferu by násobilo práci readout jobu 4^l.
**Kůže se vyhodnocuje per fragment na grafické frontě.** Readout buffer neroste ani
o bajt, pipeline nemění tvar.

Je to možné **jen díky tomu, že fáze je odvozená z adresy**:

```
φ = f(kernel coord, face, rodina U/V, úroveň)
```

Fragment si ji spočítá sám. Kdyby byla fáze uložená, kůže by musela projít frontou.

## VIII.3 Fragment

```
1. pozice na povrchu  →  fáze φ podél nejvýš dvou in-plane rodin
2. úroveň z ddx/ddy(φ)  — mip-like výběr, dá LOD i antialiasing zdarma
3. residuální strom: RGB(φ) a V(φ) na té úrovni
4. V moduluje poloměr valení → analytická derivace oblouku → normála → stínování
```

Nikdy `P = P₀ + N·V`. RGB a `V` jsou dvě čtení téže fáze, takže spolu nemohou driftovat.

## VIII.4 Barva

```
L0   PackedColor v KernelState, už uložená, s ColorConfidence      zdarma
L1+  dyadický residuální strom na oblouku:
        konce    ← zděděné z cuspů (PackedColor sousedních kernelů)
        střed    ← jeden residual
        čtvrtiny ← potomci
```

Akvizice odpovídá reprezentaci: změř střed oblouku; stačí-li predikce z konců, konec;
jinak rozděl. **Detail se přidává lokálně a hierarchicky**, ne globálními harmonikami.

Barva v bodě není `C(u,v)`:

```
C(x) = Σ_γ W_γ(x)·C_γ(φ_γ(x))  /  Σ_γ W_γ(x)
```

Na L4/L5 je rozteč vláken 2,21 / 1,10 mm, tedy pod pixel footprintem — každý pixel
protne dost vláken, aby se vzhled vyhodnotil přímo z nich. Proto tam není žádná
„plocha mezi nitěmi".

Křížení jsou paritní kontrola: u opakního povrchu musí dvě vlákna v témž bodě dát po
normalizaci expozice tutéž barvu. Silné proti texture swimming a color bleed.

## VIII.5 Proč to není gaussian peklo

3DGS platí hustotu **miliony uložených a splatovaných primitiv**, které musí projít
celým pipeline, a jejich parametry se optimalizují SGD proti snímkům. Tady je hustota
**funkce fáze vyhodnocená tam, kde se stejně stínuje**, a parametry jsou odvozené
z deterministické mřížkové konstrukce. Geometrie zůstává na L0/L1 — málo, velkých,
spojených trojúhelníků, tedy nejlepší případ pro tiler.

---

# IX. PERSISTENCE

`MerkabaSsdStore.FormatVersion 3 → 4`. `CheckpointMagic 0x384D4B4D`,
`OverlayMagic 0x474C384D`, `TileRecordHeaderBytes 28`, `CheckpointHeaderBytes 108`,
CRC32 a hustý 16 B payload **beze změny** — velikost záznamu i index dlaždic zůstávají
bajtově identické. Hlavičkové `ChunkSize` se zapisuje dál (je load-bearing, RISK-3).

**Migrace v3 → v4:** dekóduj uloženou rovinu, promítni střed kernelu kolmo na ni, ulož
výsledný posun oříznutý na ±12,5 mm. Přesné, jednosměrné. v3 je po tomto řezu read-only.

---

# X. EXPORT

`MerkabaExportShell → MerkabaExportMembrane → MerkabaGlbWriter / MerkabaTilesetWriter`
si zachovají rozhraní. `MerkabaExportMembrane.Build` volá tutéž CPU autoritu klece jako
readout místo `MerkabaOverlapShell.TryBuildPatch`, a emituje na úrovni dané exportní
tolerancí (výchozí L1; `≤ 1 mm` → L3). Min-cut `SolveSparsePartition` beze změny.
Invariant prázdného tilesetu z `0011dcf` beze změny, jen reportuje počty uzlů.

---

# XI. DETERMINISMUS A PARITA

- CPU autorita klece je jeden C# soubor. HLSL z ní emituje **skutečný překladač**, ne
  ručně psaný string literál s číselnými placeholdery, jak je tomu dnes
  (`MerkabaOverlapShell.BuildGeneratedHlsl()`, RISK-1).
- Diferenciální test pouští CPU autoritu a zkompilovaný `BuildReadoutVertices` nad
  týmiž seedovanými fixturami a porovnává emitované pozice **bitově**.
- Nikde ve výpočtu uzlu, emisi ani stínování nevstupuje kamera, oko ani frustum.
  Frustum smí vybrat jen *které dlaždice* stavět.
- Všechny tie-breaky jsou lexikografické na znaménkovém `int3`.
- `Tools/shaders/audit_merkaba_compute_spirv.sh` musí zůstat zelený: writable storage
  na kernel ≤ 8, žádný RW/read alias.

---

# XII. TESTY

```
UZEL
  encode/decode round trip do 0,048828/2 mm přes celý rozsah
  |d|∞ ≤ 12,5 mm z konstrukce pro každé H
  vážený průměr: n pozorování se σ → σ_uzlu ≤ 1,05 · σ/√n

GEOMETRIE  (rovina, posunutá rovina, 15/30/45/60° náklon, libovolný sklon,
            konvexní roh, konkávní roh, dveře, T-spoj, tenká příčka,
            dvě blízké rovnoběžné plochy, izolovaný vzorek, FREE separátor,
            UNKNOWN soused, záporné souřadnice, hranice tile/chunk/block)

I-1   sdílený uzel má JEDNU uloženou hodnotu, bitově shodnou z obou kernelů,
      při každé orientaci. Případy 30° a libovolný sklon musí měřit 0,000 mm
      (dnes 0,151 resp. 19,230 mm).
I-2   translační invariance
I-3   invariance přes hranici tile/chunk/block
I-4   view invariance: kamera ani oko nevstupují do topologie ani do windingu
I-5   UNKNOWN nevytvoří zadní stěnu
I-6   dva pláty 50 mm od sebe zůstanou dva — bez jediného testu, z topologie
I-7   krychle 30 cm: 866 uzlů → jedna zavřená tkanina, 0 osamocených plošek,
      0 odtržených zkosení, všech 12 hran a 8 rohů spojitých
      (dnes 140 z 866 = 16,2 % se nesešije)
I-8   žádná orientace není privilegovaná: trhliny a díry na 30° == na 0°, 45°, 54,7°
I-9   žádné duplicitní ani překrývající se trojúhelníky: každá stěna patří právě
      jednomu K
I-10  degenerovaný plát: pravidlo kratší úhlopříčky je deterministické a při přesné
      rovnosti lexikografické; výsledek nezávisí na pořadí vláken
I-11  λ_l lock: krok souseda / λ_l == 2^l přesně pro l = 0..5
I-12  klec: všech 36 hran Merkaby má délku λ₀; obvody 4λ₀ / 6λ₀ / 6λ₀

ŠUM
  rovina se σ = 5 mm: RMS chyba tkaniny ≤ σ/2, max prasklina == 0
        (dnes 63,3 % rozporných rohů, max 19,230 mm)
  shluk outlierů 20 cm od povrchu: žádný oblouk se nezavře → nespojený → retirovatelný

REGRESE (beze změny)
  scheduler: posun hlavy bez kanonické a residenční změny → žádná přestavba
  selhaný BACK → FRONT netknutý
  anchor: tvoří jen NEW; OPEN / RESUME / EXPORT nikdy
  session A/B izolace, Save As nezávislost
  3D Tiles: platný neprázdný sken → ≥ 1 leaf
  FINE: preview predikát == mutační predikát; mimo válec bajtově identické
  ERASE: obsažený objem smazán, přežije SAVE/OPEN
```

---

# XIII. ŘEZY

```
F0  CPU autorita klece: λ₀, 16 rodin, uzel encode/decode, emisní pravidlo,
    pravidlo úhlopříčky, relaxace. Kompletní test set XII.
    Žádný shader, žádná runtime změna.        BRÁNA: XII zelené na CPU.
F1  Skutečný C#→HLSL překladač autority + bitově přesný diferenciální test.
    Zavírá RISK-1.                            BRÁNA: diferenciál zelený, SPIR-V audit.
F2  Flags v2, zápis při skenu, persistence v4 a migrace.
                                              BRÁNA: EditMode, v3 checkpoint projde.
F3  Readout z uzlů. Smaž MerkabaOverlapShell, MerkabaSurfaceOrientation a obě
    kvantizace.                               BRÁNA: EditMode, shader audit, generované
                                                     soubory odpovídají generátorům.
F4  Carve / FINE / ERASE na zavřenost oblouku. Export konzumenti.
F5  Quest build a device acceptance proti baseline 2026-08-30:
    Draw 5,115 / CompileReadout 3,664 / QueryReadout 3,309 / IntegrateCarve 2,180 /
    StereoRgbdRefine 1,562 ms; readout job lifetime medián 99,4 ms.
    Akceptace: krychle 30 cm, šikmá stěna, tenká příčka, dveře, roh, dvě rovnoběžné
    plochy, spánek/probuzení, znovuotevření session, export 3D Tiles.
F6  Kůže L1–L5: residuální strom RGB/V, mikroprofil, fragment evaluace.
    Samostatný řez, appearance only, nesahá na geometrii.
```

Nic po F0 nezačíná, dokud F0 není zelené. To je C7 aplikované na tenhle návrh.

---

# XIV. CO SE MAŽE A CO OŽÍVÁ

```
MAŽE SE
  MerkabaOverlapShell.cs                       celý
  MerkabaOverlapShell.generated.hlsl           celý
  MerkabaSurfaceOrientation.generated.hlsl     celý
  KernelState.SetSurfacePlane / DecodeSurfacePlane / ClearSurfacePlane
  KernelState.HasMeasuredSurfacePlane
  MerkabaConstants.SurfacePlane*
  MerkabaWorld.hlsl: MerkabaNearestGridNormalStep, MerkabaGridSheetTangents
  MerkabaIntegration.compute:475-620 owner routing (mimo autoritní třídy)
  MerkabaReadout.compute: M8MembraneDominantAxis / CanonicalSheet /
                          FreeSideSignature / SeparatedByFree / ResolveCorner /
                          TryBuildMembranePatch
  mrtvý include MerkabaCanonicalGeometry.generated.hlsl v MerkabaIntegration

OŽÍVÁ
  MerkabaCanonicalGeometry.cs — dnes referencovaná jen z testů a z jedné velikostní
  konstanty. Nově autorita klece: 8 faců, 12 hran, 6 vrcholů, 8 hrotů, 36 hran λ₀.

BEZE ZMĚNY
  sensor frontend, evidence a hystereze, M8 adresování a residence, nativní executor
  a jeho fronta, FRONT/BACK publikace, scheduler, anchor a session model,
  FINE válec, ERASE, GLB a 3D Tiles, vertex ABI, kapacita readoutu
```

---

# XV. OTEVŘENÉ

1. **Tolerance zavření oblouku** pro `M8HasContinuingCarveSheet` a odmítnutí ducha.
   Potřebuje device měření, ne odhad. Do té doby pojmenovaná konstanta, ne laděná.
2. **`ColorConfidence` pro barvu i konfidenci uzlu.** Pokud se ukáže, že se to musí
   oddělit, bity 29–30 nestačí a je to skutečná ABI otázka, ne reinterpretace.
3. **Relaxace uzlu** — uzavřená forma z pevné F16 geometrie (cíl, pár FMA), nebo
   iterace. Uzavřená forma by měla jít vygenerovat jednou; musí se ověřit.
4. **Rozpočet `Draw`.** Dnes 5,115 ms z 13,89 ms na 72 Hz, největší jediný kernel.
   Vyhodnocování vláken ho zvedne; 4× méně vertexů a spojená síť ho sníží.
   Obchod je věrohodný, ale je to **jediné místo návrhu, kde nevím, jestli vyjde**,
   a rozhodne měření na zařízení, ne úvaha.
5. **Serializovaná nativní fronta** (`queueFence` medián 53,9 ms, job lifetime 99,4 ms,
   z toho ~7 ms práce) je naměřené systémové hrdlo a tento spec se jí nedotýká.
   Zisk je v kvalitě, správnosti a ceně readoutu, ne ve frontě.
