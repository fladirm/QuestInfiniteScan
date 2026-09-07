Jo. Dohledal jsem **aktuální Meta/Qualcomm dokumentaci a reálný Vulkan dump z Questu 3**. Tohle bych dal Codexu jako **Quest-3 GPU execution contract**, aby už nepsal desktopové shadery.

### Co je na Questu 3 skutečně relevantní

Quest 3 je **Snapdragon XR2 Gen 2 / Adreno 740**, má fyzicky 8 GB RAM, ale aplikace nemá 8 GB k dispozici: aktuální Meta limit je **5.75 GiB PSS pro celý proces**. GPU je integrované, tedy žádná oddělená „8GB VRAM“. ([Meta for Developers][1])

Adreno 740 v Questu 3 je tile-based GPU a XR2 Gen 2 má přibližně **2 MB GMEM/tile memory**. Meta proto doporučuje minimalizovat render-target roundtrips, attachmenty a mezilehlé framebuffer passy. ([Meta for Developers][2])

Reálný Quest-3 Vulkan capture z ledna 2026 uvádí tyto limity:

```text
GPU                         Adreno 740
subgroup min/max            64 / 128

max workgroup invocations   1024
max workgroup size XYZ      1024 / 1024 / 1024
max dispatch groups/dim     65535

groupshared/workgroup RAM   32768 B

max storage binding range   134217728 B   = 128 MiB
max uniform binding         65536 B        = 64 KiB
max immediate/push data     256 B

storage offset alignment    64 B
uniform offset alignment    64 B

max individual buffer       2147483647 B
```

([Gist][3])

Ten dump pochází od stejného autora, který testoval Vulkan přímo na Questu 3/Adreno 740 přes OpenXR. Driver/HzOS se může změnit, takže **tyto hodnoty použij jako Quest-3 target, ale při startu development buildu je stejně jednou načti přes `vkGetPhysicalDeviceProperties2()` a assertni**. ([GitHub][4])

To má pro naše repo několik velmi konkrétních důsledků.

---

## 1. Buffery

### Žádný jeden bound SSBO > 128 MiB

To je důležité.

Může existovat větší Vulkan allocation, ale **jeden storage-buffer descriptor má v tom Quest-3 dumpu limit 128 MiB**. ([Gist][3])

Proto:

```text
GOOD
KernelState bank0   64 MiB
KernelState bank1   64 MiB
KernelState bank2   64 MiB
KernelState bank3   64 MiB

ObservationRecords  32 MiB
```

```text
BAD
KernelStateWorld    256 MiB jako jeden SSBO binding
```

Takže současné bankování world buffers **neslučovat do jednoho obřího SSBO**.

### Hot GPU ABI dělat po 16 bytech

Qualcomm přímo doporučuje na Adrenu **128bitové/vectorized memory accesses**; jsou efektivnější než spoléhat jen na coalescing sousedních malých loadů. ([Qualcomm][5])

Pro nás tedy ideální:

```c
uint4
int4
float4

struct Record {
    uint a;
    uint b;
    uint c;
    uint d;
}; // 16 B
```

Současný `ObservationRecord = 16 B` je přesně dobrý tvar.

Persistentní SSD formát může být hustší, ale **hot compute representation přepakovat do 16B packetů**.

---

## 2. ABI mezi C# / HLSL / native C++

Codexovi bych zakázal chytré implicitní layouty.

Používat:

```text
uint
int
float
uint2/4
int2/4
```

a explicitně:

```text
sizeof
offsetof
stride
static_assert
C# Marshal.SizeOf
SPIR-V reflection
```

Quest capture má `VK_EXT_scalar_block_layout`, ale to není důvod stavět cross-language ABI na tom, že Unity/DXC/native C++ všechny stejně interpretují nějaký exotický packed struct. ([Gist][6])

Pro canonical ABI tedy:

```text
4-byte scalar fields
16-byte record stride where practical
64-byte aligned dynamic buffer offsets
no bool in shared ABI
no C# Vector3 as persistent binary ABI
```

`half` používat na **storage appearance**, třeba RGB intervaly, kde to kontrakt dovoluje. Geometrická/intervalová matematika zůstává FP32/int fixed-point.

---

## 3. Workgroups

Quest 3 dovolí až:

$$
1024\text{ invocations/WG}
$$

a naměřený subgroup range je **64–128**. ([Gist][3])

Ale **1024 není doporučená velikost**.

Qualcomm přímo upozorňuje, že u A7xx může barrierless kernel technicky používat obří WG, ale vysoký register footprint i velké workgroups mohou zhoršit parallelism/cache locality. ([Qualcomm Docs][7])

Pro naše typy kernelů:

```text
image/depth kernels
    [numthreads(8,8,1)]      = 64

linear compact/reduction
    64 / 128 / 256

tile closure 512 items
    prefer 256 threads
    each lane handles 2 kernels
```

Poslední bod je doporučení pro náš M8 tile.

Místo:

```text
512 threads
1 kernel/thread
+ barriers
```

je na Adrenu většinou rozumnější:

```text
256 threads
2 kernelLocal/thread
+ stejné groupshared
+ stejné "one WG per touched tile"
```

protože při barrier kernelu musí workgroup synchronizovat všechny svoje waves. **512 je legální, ale nemá být default jen proto, že tile obsahuje 512 M8 kernelů.**

---

## 4. Groupshared

Hard limit z Quest-3 capture:

$$
\boxed{32\text{ KiB/WG}}
$$

([Gist][3])

Ale Codex nemá targetovat 31.9 KiB.

Qualcomm upozorňuje, že local/workgroup memory je on-chip, ale její použití **automaticky neznamená vyšší výkon** a spotřeba local memory omezuje paralelismus. ([Qualcomm Docs][7])

Pro naše closure kernels bych držel:

```text
target       <= ~16 KiB groupshared
acceptable   16–24 KiB pokud je to skutečně potřeba
hard stop    32 KiB
```

A hlavně **reuse scratch mezi R1/R2/R3 relation phases**, ne:

```text
scratchR1[...]
scratchR2[...]
scratchR3[...]
scratchCompletion[...]
```

současně.

Tj.:

```text
load tile
load halo
R1 scratch
barrier
reuse same memory for R2
barrier
reuse for R3
```

To je přesně vhodné pro FlowerCommit.

---

## 5. ALU a registry

Adreno je typicky mnohem citlivější na **memory traffic + register pressure** než desktopová RTX. Qualcomm výslovně doporučuje minimalizovat register footprint; vysoká registrace omezuje počet současně resident waves. ([Qualcomm Docs][7])

Takže Codex:

```text
DO:
  integer addressing
  bit masks
  fixed generated tables
  FMA/simple FP32
  short-lived temporaries
  branch-uniform loops over fixed R1/R2/R3 classes

DON'T:
  giant structs per lane
  float3[26] local arrays
  13 ABC triples simultaneously per thread
  huge inlined helper chains retaining temporaries
  arbitrary double precision
```

Pro Sphere–Flower je lepší:

```text
for lineClass
    compute ABC
    classify
    consume result
```

ne:

```text
compute ABC[13]
compute roots[26]
compute sectors[26]
then consume
```

To druhé Codex rád udělá a na desktopu to možná přežije; na Adrenu ti register pressure zabije occupancy.

---

## 6. FP16 / FP64 / atomics

Quest-3 Vulkan extension capture obsahuje `VK_KHR_shader_float16_int8`, subgroup support a řadu dalších moderních extension. Samotná přítomnost extension ale **neznamená, že konkrétní feature bit je použitelný přes Unity/DXC cestu**. ([Gist][6])

Proto core scanner:

```text
geometry/intervals       FP32
addresses/masks          uint/int32
persistent phase         fixed-point int32
atomics                  uint/int32
```

Nepředpokládat:

```text
float atomic add
fp16 ALU correctness
64-bit atomic hot path
double
```

Reálný Quest-3 wgpu capture například hlásil `SHADER_FLOAT32_ATOMIC=false`, zatímco Int64 jako typ dostupný byl. ([Gist][3])

Pro deterministickou redukci jsou tedy naše integer `InterlockedMin/Max/And/Or/Add` přesně správná cesta.

---

## 7. Global memory access

Qualcomm:

> 128bit vector load/store + contiguous/coalesced addressing.

([Qualcomm][5])

Takže:

```text
GOOD:
tile slot -> contiguous 512 kernel records
lane i -> kernel i / i+256
uint4 loads
coherent sequential records
compact touched queues
```

```text
BAD:
world hash lookup inside každé relation
random SSBO pointer chasing
global atomic for every petal/root
full-world scan every observation
```

Tvoje současná filosofie:

$$
\text{observation}\to\text{touched tiles}\to\text{one WG/tile}
$$

je přesně Quest-friendly.

---

# 8. GPU queues: hlavně žádný async-compute cirkus

Tohle Meta říká dost jednoznačně.

**Additional Graphics Queue na Questu nepřináší výhodu.** Snapdragon mobile GPU má prakticky unified/single command processor; více queue přidává CPU overhead, frame-time variance a power bez měřitelného GPU gainu. Meta doporučuje Additional Graphics Queue vypnout. ([Meta for Developers][8])

Pro náš scanner tedy:

$$
\boxed{\text{ONE SERIALIZED GPU DEPENDENCY CHAIN}}
$$

Ne:

```text
queue geometry
queue carve
queue detail
queue readout
semaphores všude
```

Ale:

```text
one queue submission / few submissions:

StereoFlower
DepthCertificate
Count
Reserve
Emit
FlowerCommit
Finalize
```

V rámci queue používat **pipeline barriers**, ne CPU waits.

Quest-3 Vulkan capture potvrzuje podporu:

```text
VK_KHR_synchronization2
VK_KHR_timeline_semaphore
```

([Gist][6])

Timeline semaphore bych používal pouze pro **větší ownership boundary**, třeba Unity ↔ native executor / publication generation, ne mezi každými dvěma compute kernely.

Uvnitř jednoho scanner jobu:

```text
dispatch
memory barrier
dispatch
memory barrier
dispatch
```

---

# 9. Dispatch zoo vs jeden monolit

Ani jeden extrém.

Quest-friendly pravidlo:

> **jeden dispatch na skutečný global synchronization boundary; všechno, co lze uzavřít uvnitř jednoho tile WG, zůstává v tom WG.**

Takže například:

```text
Count
Reserve
Emit
FlowerCommit
Finalize
```

dává smysl.

Ale uvnitř `FlowerCommit` nedělat:

```text
R1 dispatch
R2 dispatch
R3 dispatch
completion dispatch
detail dispatch
```

pokud všechno pracuje nad jedním touched tile a stačí groupshared barriers.

Naopak nesnažit se nacpat `Count → Reserve → Emit`, které vyžaduje globální synchronizaci mezi workgroups, do jednoho dispatchu. Vulkan žádný globální WG barrier uvnitř dispatchu nemá.

---

# 10. Raster/readout

Adreno 740 je tile renderer s ~2 MB GMEM, takže readout má být **jeden jednoduchý forward/multiview raster**, ne deferred pipeline. ([Meta for Developers][2])

Pro naši Flower reprezentaci:

```text
procedural L2 carrier
        ↓
vertex shader
        ↓
one fragment shader
        ↓
3 fixed Flower-7 descents
        ↓
one RGBV result
```

To je dobré.

Nedělat:

```text
geometry prepass
normal target
material target
RGB target
microdetail target
combine pass
```

Každý další full-resolution target znamená bandwidth/resolve náklady na tile GPU.

Použít:

* Vulkan;
* multiview/single-pass stereo;
* FFR, pokud vizuálně vyhovuje;
* optimized buffer discards pro MSAA;
* subpass pouze tam, kde další operace čte stejný pixel.

Meta aktuálně doporučuje Vulkan a její nové Quest features se na Vulkan vážou. ([Meta for Developers][9])

---

# 11. Pozor na 8 GB RAM — pro náš scanner není 4GB world „zdarma“

Tohle opravuje jednu naši starší úvahu.

Quest 3 má sice:

```text
8 GB physical
```

ale aktuální **application PSS kill limit je 5.75 GiB**. ([Meta for Developers][10])

Do toho se musí vejít:

```text
Unity/native process
GPU allocations
canonical world
textures
depth/camera resources
ThreadAtlas/FlowerDetail
readout
managed/native heap
```

Takže **4 GB jen pro voxely/world není rozumný design assumption**.

SSD-backed sparse HOT residency, kterou teď stavíme, je správná cesta.

---

# 12. Scanner + passthrough má horší thermal budget než běžná VR hra

Quest 3 dynamicky mění CPU/GPU levels. Aktuální Meta tabulka uvádí GPU levels 0–5; pokud aplikace používá passthrough, vyšší GPU levels jsou omezené. ([Meta for Developers][11])

Takže Codex **nesmí optimalizovat tak, že „na Questu 3 je GPU dost rychlá“**.

Scanner má:

* cameras/depth;
* tracking;
* reconstruction compute;
* raster;
* možná passthrough;

současně.

Design musí fungovat při sustained mobile GPU load, ne jen 30 sekund po spuštění. Meta také výslovně upozorňuje na thermal throttling. ([Meta for Developers][12])

---

## Tohle bych dal Codexu jako krátký permanentní HW invariant

```text
QUEST-3 GPU CONTRACT

Target = XR2 Gen2 / Adreno 740, unified-memory mobile tile GPU.

Measured Quest-3 Vulkan limits:
- 1024 max invocations/WG
- subgroup range 64..128
- 32 KiB max groupshared
- 128 MiB max storage-buffer binding
- 64 KiB max uniform binding
- 256 B push/immediate data
- 64 B dynamic UBO/SSBO offset alignment
- 65535 dispatch groups/dimension

Rules:
- never bind one SSBO >128 MiB; bank/page world state
- hot records prefer aligned 16-byte/uint4 accesses
- use 8x8=64 for image kernels; 64/128/256 for linear work
- 512-thread barrier WG is legal but not default; prefer 256 lanes x2 items
- keep groupshared comfortably below 32 KiB and reuse it between phases
- minimize register lifetime; never materialize all 26/13 Flower candidates per lane
- FP32/int32/fixed-point for canonical math; do not rely on FP16/float atomics
- reduce locally in groupshared; compact once to global memory
- no full-world GPU sweeps for observation work
- one WG per touched tile where tile closure is required
- one GPU queue/dependency chain; no async/multi-graphics-queue architecture
- pipeline barriers between true global phases, not CPU readbacks
- one simple multiview forward procedural readout; no deferred/intermediate-target zoo
- no head-rotation geometry/residency rebuild
- app memory hard limit is 5.75 GiB PSS, not 8 GB usable
- query and log Vulkan limits/features on the actual Quest at startup;
  fail development builds if any required minimum differs.
```

A **nejdůležitější změna pro současný RUN04** je podle mě: hlídat `FlowerCommit` na **register pressure + groupshared**, nikoli počet ALU instrukcí. Na Adrenu je klidně lepší udělat víc jednoduché integer/FMA matematiky, než si „optimalizací“ vytvořit dalších 20 náhodných global loads nebo 100 živých registrů na lane. Qualcomm ostatně přímo popisuje mobilní workloady jako převážně memory-bound. ([Qualcomm][5])

[1]: https://developers.meta.com/horizon/resources/device-optimization-comparison/?utm_source=chatgpt.com "Device-specific optimization (Quest 3 vs Quest 2) | Meta Horizon OS Developers"
[2]: https://developers.meta.com/horizon/documentation/unity/po-advanced-gpu-pipelines/?utm_source=chatgpt.com "Advanced GPU Pipelines and Loads, Stores, and Passes | Meta Horizon OS Developers"
[3]: https://gist.github.com/zonkypop/bc6adcb01e07f41c37fb4db671ae2ed1 "wgpu-info multi draw indirect debugging · GitHub"
[4]: https://github.com/gfx-rs/wgpu/issues/8801 "draw_indexed_indirect silently fails on Quest 3/2 (Adreno 740/650) · Issue #8801 · gfx-rs/wgpu · GitHub"
[5]: https://www.qualcomm.com/news/onq/2016/06/better-opencl-performance-qualcomm-adreno-gpu-memory-optimization?utm_source=chatgpt.com "Better OpenCL performance on Qualcomm Adreno GPU – memory optimization | Qualcomm"
[6]: https://gist.github.com/tcoppex/6ef9f5f60ddae41d6198d2bdf4a40beb "Meta Quest 3 Vulkan extensions · GitHub"
[7]: https://docs.qualcomm.com/bundle/publicresource/80-NB295-11_REV_C_Qualcomm_Snapdragon_Mobile_Platform_Opencl_General_Programming_and_Optimization.pdf?utm_source=chatgpt.com "Qualcomm Snapdragon Mobile Platform OpenCL General Programming and Optimization"
[8]: https://developers.meta.com/horizon/documentation/unity/unity-openxr-settings/?utm_source=chatgpt.com "Unity OpenXR settings reference | Meta Horizon OS Developers"
[9]: https://developers.meta.com/horizon/documentation/unreal/os-vulkan-opengl/?utm_source=chatgpt.com "OpenGL ES and Vulkan | Meta Horizon OS Developers"
[10]: https://developers.meta.com/horizon/essentials/memory-ram/?utm_source=chatgpt.com "Memory / RAM | Meta Horizon OS Developers"
[11]: https://developers.meta.com/horizon/documentation/spatial-sdk/os-cpu-gpu-levels/?utm_source=chatgpt.com "CPU and GPU levels | Meta Horizon OS Developers"
[12]: https://developers.meta.com/horizon/essentials/performance-hardware/?utm_source=chatgpt.com "Performance and hardware | Meta Horizon OS Developers"
