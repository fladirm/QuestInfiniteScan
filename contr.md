QUEST INFINITE MERKABA
FINAL LOW-CODE PRODUCTION CLOSURE CONTRACT
==========================================

BASELINE
--------
Repository:
    fladirm/QuestInfiniteScan

MANDATORY BASE:
    1b581635c18bedfc1812119e3dff2781c967c4ff

Regression comparison:
    62b690640cbfbe5bdc0a12e42546c3ef31095fbb

GOAL
----
Finish the existing application as one coherent sellable Quest product.

This is NOT:
    a new scanner,
    a refactor project,
    a demo,
    an architecture experiment.

This IS:
    targeted production closure of the existing scanner,
    readout,
    persistence,
    spatial alignment,
    viewer,
    paint/design tools,
    UX.

Implement the complete contract.
Do not answer with another plan.

============================================================
A. NON-NEGOTIABLE EXISTING ARCHITECTURE
============================================================

CANONICAL WORLD TRUTH remains exactly:

    M8 KernelState

Current canonical persistent information remains:

    OccupancyEvidence
    PackedColor
    ColorConfidence
    Flags / measured surface plane

Frozen geometry constants remain:

    SupportSize = 0.050 m
    LatticeStep = 0.025 m

Do not alter the four-stream sensor frontend:

    Depth-L
    Depth-R
    PCA-L
    PCA-R
        ↓
    one joint measurement

Keep:
    signed infinite coordinates
    sparse hierarchy
    HOT/COLD residency
    SSD persistence
    immutable observations
    deterministic retry
    FRONT/BACK publication
    current native Vulkan executor queue
    current GLB writer
    current 3D Tiles packaging
    PC/browser ZIP viewer
    Quest artifact viewer
    plan view
    annotations
    controller/two-hand model manipulation
    spatial package binding

FORBIDDEN:

    TSDF
    Surface Nets
    QEF
    Marching Cubes
    trilinear reconstruction
    persistent surfel authority
    persistent mesh as world truth
    a second geometry database
    GLB -> canonical M8 conversion
    3D Tiles -> canonical M8 conversion
    eye-dependent membrane topology
    camera-dependent winding
    billboard/card fallback
    glyph fallback
    threshold roulette
    reduced stereo solve
    mono fallback
    shrinking view distance as optimization

Derived representations are ONLY:

    M8
      ↓
    deterministic membrane
      ↓
    indexed geometry
      ↓
    readout / GLB / 3D Tiles

GLB/3D Tiles are exports, never truth.

Continuing scanning is done by OPEN SESSION,
never by importing GLB.

============================================================
B. CHANGE DISCIPLINE — LOW CODE
============================================================

Before editing a subsystem:
    read its complete current flow;
    identify the current incorrect authority;
    replace it;
    remove the obsolete authority.

Do not add a second implementation beside it.

Prefer modifying existing files.

Allowed focused new production classes only where ownership is genuinely new:

    MerkabaSessionCatalog.cs
    MerkabaDesignDocument.cs
    MerkabaDesignLibrary.cs
    MerkabaPaintEngine.cs

Do NOT introduce:
    DI framework
    service framework
    backend interfaces
    mode architecture
    scene rewrite
    runtime package dependency
    second GLTF stack

Keep existing component names where changing them would require scene rewiring.
For example DebugMenuController may remain its internal class name,
but it must no longer LOOK or behave like a debug menu.

============================================================
C. READOUT — GLB-LIKE INDEXED MEMBRANE
============================================================

CURRENT GOOD PART TO PRESERVE:

    FRONT/BACK
    GraphicsBuffer
    index buffer
    indirect draw
    one cheap draw per XR frame

The final steady-state rendering path must remain:

    FRONT indexed membrane
        ↓
    DrawMeshInstancedIndirect
        ↓
    raster/depth/environment occlusion

Do NOT replace this with CPU Mesh generation per frame.

------------------------------------------------------------
C1. REMOVE VIEW FROM MEMBRANE TOPOLOGY
------------------------------------------------------------

Delete standard-readout geometry dependence on:

    _M8EyeGridPosition0
    _M8EyeGridPosition1
    6 x 512 x 512 front-depth topology mask
    M8FrontReadoutEyeMask
    M8IsFrontReadoutKernelForEye
    eyeMask in membrane admission
    camera-selected winding

Remove standard geometry use of:

    ProjectReadoutFrontDepth

If native ABI requires the kernel symbol to remain temporarily,
make it an explicit no-op.

Visibility is allowed AFTER membrane construction only:

    ordinary frustum selection
    ordinary depth testing
    Environment Depth occlusion
    renderer culling

View may decide WHAT TO DRAW.

View may NEVER decide:
    what a membrane patch geometrically is,
    which side of the sheet exists,
    its winding,
    whether M8 contains a surface.

------------------------------------------------------------
C2. DELETE CURRENT PIN/GLYPH/NOODLE PATH
------------------------------------------------------------

Remove standard-readout topology from:

    M8ReadoutPin
    sheet-code neighbour stitching
    M8ReadoutPinsCompatible
    M8TryCompatibleReadoutSide
    M8ResolveOneReadoutPin
    M8EmitReadoutGlyph
    M8_READOUT_BUILD_GLYPH

No fallback patch.
No octahedron.
No diamond.
No card.
No noodle.

A valid measured surface gets membrane.
An invalid/unresolved surface gets a diagnostic count and no invented geometry.

------------------------------------------------------------
C3. ONE MEMBRANE ORACLE FOR ALL THREE CONSUMERS
------------------------------------------------------------

There must be ONE mathematical membrane authority:

    CPU oracle
        +
    generated HLSL equivalent

Consumers:

    live readout
    GLB exporter
    3D Tiles exporter

No three different surface algorithms.

Current MerkabaOverlapShell may be renamed but do not duplicate it.

------------------------------------------------------------
C4. FIX CURRENT 50mm CARD
------------------------------------------------------------

Current code effectively uses:

    patch half extent = HalfSupport = 25mm

therefore:
    full local card = 50mm

This is wrong.

Final membrane footprint pitch is the lattice pitch:

    PATCH_PITCH      = LatticeStep = 25mm
    PATCH_HALF_PITCH = LatticeStep * 0.5 = 12.5mm

Introduce:

    internal const float MembranePatchPitch =
        MerkabaConstants.LatticeStep;

    internal const float MembraneHalfPitch =
        MerkabaConstants.LatticeStep * 0.5f;

Never use:

    HalfSupport

as tangent half-width of one rendered patch.

50mm is overlapping SUPPORT.
25mm is readout sampling/patch pitch.

------------------------------------------------------------
C5. DETERMINISTIC MEMBRANE ALGORITHM
------------------------------------------------------------

Each occupied MAIN with a measured plane may own one thin local patch.

Decode:

    measured plane N,d

Determine canonical dominant axis:

    abs(N)

tie order:
    X before Y before Z

The other two lattice axes are tangent lattice axes T0/T1.

One MAIN footprint covers:

    +/- 0.5 lattice step along T0
    +/- 0.5 lattice step along T1

thus four canonical half-step corners:

    C00 C10 C11 C01

For each corner:

1. derive its canonical half-lattice address;
2. enumerate ONLY the four immediately sharing tangent columns;
3. in each column inspect at most:
       normalOffset = -1, 0, +1 lattice step;
4. candidate contributor must:
       be occupied;
       have measured plane;
       have compatible dominant axis;
       be compatible with MAIN sheet;
       not be separated by known FREE;
5. select at most one contributor per column;
6. deterministic selection order:
       same free-side/sheet signature
       then minimum surface-plane residual
       then minimum normal-layer distance
       then lexicographically smallest coordinate;
7. intersect the contributor measured plane with the canonical
   corner's normal-axis line;
8. combine accepted contributor heights deterministically;
9. produce exactly one shared corner position.

Adjacent patches referring to the same physical corner and sheet MUST calculate
bit-identical corner position.

FREE:
    may identify/separate sheet side;
    is never a surface contributor.

UNKNOWN:
    contributes nothing;
    never creates a backside.

No contributor farther than immediate +/-1 local neighbourhood.

No distance-two bridging.

Two parallel sheets separated in the normal direction remain two sheets.

If two mathematically distinct sheet branches coexist:
    MAIN selects the branch nearest its own measured plane;
    FREE separator dominates;
    tie -> lexicographic deterministic branch.

No eye/camera input.

------------------------------------------------------------
C6. ORDINARY PATCH OUTPUT
------------------------------------------------------------

Ordinary patch:

    4 vertices
    6 indices
    2 triangles

Conceptual index order:

    0 = C00
    1 = C10
    2 = C11
    3 = C01

then the existing proven Unity/GLB winding convention.

Do not create six independent vertices merely because there are two triangles
unless the current fixed buffer ABI makes that unavoidable.

Do NOT add a global vertex hash map.

Local duplicate vertices are acceptable if cheaper;
triangle-soup topology is not.

------------------------------------------------------------
C7. CPU ORACLE FIRST
------------------------------------------------------------

Before changing GPU output, CPU oracle tests MUST pass:

    flat plane
    translated flat plane
    45-degree plane
    arbitrary quantized slope
    convex corner
    concave corner
    doorway
    T junction
    thin partition
    two close parallel sheets
    isolated sample
    FREE separator
    UNKNOWN neighbour
    negative coordinates
    tile boundary
    chunk boundary
    block boundary

Required invariants:

    zero-thickness sheet
    no backside from UNKNOWN
    no 50mm cards
    no bridging across FREE
    no merge of parallel sheets
    shared corners identical
    translation invariant
    boundary invariant
    view invariant

Then generate HLSL from the same authority.

Do not hand-maintain a different GPU algorithm.

------------------------------------------------------------
C8. GPU BUILD
------------------------------------------------------------

Reuse existing GPU readout buffers and native executor.

For a selected HOT tile:

    cooperatively cache:
        tile
        immediate membrane halo

Then process its 512 MAIN kernels.

Do not repeatedly traverse sparse hierarchy independently for every neighbour.

Load complete KernelState only when required for an emitting surface;
cheap occupancy/validity information first.

Do not invent additional serial dispatch zoo.

------------------------------------------------------------
C9. SCHEDULER
------------------------------------------------------------

Readout remains SERIAL on the same native queue.

Priority is:

    pending FINE/ERASE
    pending normal observation
    readout rebuild

Scanner mutation always wins over disposable readout.

Readout dirty events are COALESCED.

One or fifty canonical changes while readout waits:
    one subsequent rebuild.

Remove head-motion rebuild condition.

Specifically remove geometry rebuild caused solely by:

    _publishedGridToWorld != _grid.GridToWorldMatrix
    distance(cameraGrid, publishedGrid) > readoutTranslationGuard

GridToWorld is a DRAW transform.

It is not a topology generation.

Do not rebuild at 15Hz just because readoutBuildHz elapsed.

Required conceptual scheduler:

    bool scannerWork =
        integrator.HasPendingObservation ||
        integrator.HasAttemptInFlight ||
        integrator.HasPendingFineErase ||
        integrator.HasFineEraseAttemptInFlight;

    bool buildRequested =
        canonicalDirty ||
        requiredResidencyActuallyChanged;

    if (!nativeJobInFlight &&
        !scannerWork &&
        buildRequested)
    {
        SubmitOneReadoutBuild();
    }

Camera movement may cause streaming/residency work when actually entering new
coverage, but camera movement by itself must not rebuild existing membrane.

------------------------------------------------------------
C10. FRONT/BACK
------------------------------------------------------------

Current FRONT/BACK semantics remain.

Each publication slot retains the existing complete logical readout capacity.

Do not reinterpret:
    FRONT/BACK generations
as:
    first/second half of capacity.

A failed BACK build:

    MUST NOT modify FRONT
    MUST NOT clear FRONT draw args
    MUST NOT produce a visible disappearance

Successful complete BACK:
    atomic publish to FRONT.

============================================================
D. AUTOMATIC SCAN — OWNER AND FAST FILL
============================================================

Do not alter joint RGB-D solve.

------------------------------------------------------------
D1. FIX SHADOW-SHEET OWNER BUG
------------------------------------------------------------

Current logic contains the equivalent of:

    targetCoord = revision ? nearestKernel : bestOwner;

This is wrong.

Attention/revision authority may not change geometric owner.

Replace semantics with:

    if compatible existing owner exists:
        targetCoord = bestOwner;
        authority =
            attention > 0
                ? REVISION
                : SUPPORT;
        replacement = false;

    else:
        targetCoord = nearestKernel;
        authority = DISCOVERY;

The measured signed plane inside KernelState is what represents the
sub-lattice physical position.

Do not create a parallel lattice surface merely because attention says REVISION.

A true owner migration is allowed only when current immutable measurement
proves that previous owner cannot represent the physical surface.

Migration must be one replacement transaction:
    new owner established
    then old owner retired

Never two simultaneous plates.

------------------------------------------------------------
D2. FAST FILL
------------------------------------------------------------

Do NOT change:

    OccupiedOnThreshold
    OccupiedOffThreshold
    SurfaceEvidenceScale
    FreeEvidenceScale

Use the existing current-frame planar-support logic.

For an empty DISCOVERY target:

    strict joint measurement
    AND
    current-frame planar support
    AND
    strict current support

is sufficient for immediate canonical admission.

Equivalent:

    required =
        MERKABA_OCCUPIED_ON -
        max(state.evidence, 0);

    surfaceDelta =
        max(surfaceDelta, required);

Only for that strict coherent discovery case.

Weak/non-strict observations continue using normal evidence.

Goal:
    one sweep across a wall fills a coherent wall rapidly.

Not:
    wait many seconds for postage stamps.

------------------------------------------------------------
D3. HOLES
------------------------------------------------------------

Automatic strict planar observations should rapidly establish missed local
surface owners.

Manual FINE gives explicit user repair.

Do NOT fake-fill canonical holes in live readout.

Export-only inferred membrane closure may remain derived/export-only.

============================================================
E. CARVE — FAST BUT NON-DESTRUCTIVE
============================================================

Keep exact same-ray rules.

Keep same-observation SURFACE precedence.

Do not globally remove OFF+1 protection.

------------------------------------------------------------
E1. CROSS-TILE 3x3x3
------------------------------------------------------------

Current isolated check must work at tile boundaries.

A boundary kernel must not automatically become “not isolated”.

Use current tile + immediate one-kernel halo.

Exact result for a kernel at tile coordinate 0 or 7 must equal the same pattern
placed in the tile interior.

Unresolved required COLD neighbour:
    request/load
    do not destructively guess.

------------------------------------------------------------
E2. CLEAR RULE
------------------------------------------------------------

For one validated FREE observation:

    subtract normal FREE evidence.

Compute:

    candidateEvidence =
        state.evidence - freeDelta

Allow an occupied -> nonoccupied transition ONLY when all are true:

    exact same-ray FREE
    endpoint clearance >= FreeFullClearance
    no same-observation SURFACE for K
    local exact 3x3x3 halo provides no continuing compatible sheet support
    accumulated signed evidence would cross OccupiedOffThreshold

Otherwise clamp existing occupied state to:

    OccupiedOffThreshold + 1

This uses existing signed evidence as repetition memory.

No new persistent carve-confidence structure.

Result:
    one noisy frame cannot erase a wall;
    repeated clear viewpoint quickly deletes isolated stale geometry.

============================================================
F. FINE — HAND TOOL, NOT EYE CONE
============================================================

Remove from FINE:

    EyeOrigin
    cyclopeanEye
    angle
    CosHalfAngleSquared
    eye cone
    soft brush authority weighting

FINE is an exact controller-ray cylinder.

------------------------------------------------------------
F1. EXACT DESCRIPTOR
------------------------------------------------------------

Use one descriptor shared by:
    preview
    CPU admission
    shader mutation
    ERASE

Required data:

    CursorPosition
    SurfaceNormal
    Axis
    Radius
    Length
    Operation

Axis:
    controller ray direction.

Segment:
    CursorPosition
        ->
    CursorPosition + Axis * Length

Exact predicate:

    d = P - CursorPosition
    axial = dot(d, Axis)

    inside =
        0 <= axial <= Length
        AND
        dot(d - Axis*axial,
            d - Axis*axial) <= Radius²

No falloff for canonical authority.

------------------------------------------------------------
F2. CURSOR
------------------------------------------------------------

Cursor is the actual Environment Depth hit of the controller ray.

If no valid Environment Depth hit:
    no FINE mutation
    no jump to hand
    no fallback to arbitrary ray point.

Preview must lie on the measured Environment Depth surface.

------------------------------------------------------------
F3. TARGET SHADER
------------------------------------------------------------

Delete current:

    [numthreads(1,1,1)]
    128 sequential samples
    + 8 sequential binary iterations

Replace with ONE parallel workgroup.

Required shape:

    [numthreads(128,1,1)]

One lane = one ray interval sample.

Each lane computes:
    valid projection
    depth delta
    candidate crossing

Select deterministically:

    first valid front->behind crossing
    then smallest residual
    then smallest lane id

Use groupshared reduction / one atomic rank.

Thread 0 reconstructs:
    final target
    local depth normal.

ONE dispatch.
No 128 dispatches.
No serial binary search.

Keep only one tiny async target readback outstanding.
Immediately request next target after previous completes.
No 15Hz cursor feeling.

------------------------------------------------------------
F4. FAST REFINE AUTHORITY
------------------------------------------------------------

FINE is explicit user editing.

Inside exact cylinder, valid strict four-stream surface does NOT wait through
automatic discovery hysteresis.

Owner:

    if compatible existing same-sheet owner exists:
        target = bestOwner;
    else:
        target = nearestKernel;

This prevents FINE from creating shadow plates while still allowing holes to
be drawn.

For valid FINE measurement:

    surfaceDelta =
        max(surfaceDelta,
            OccupiedOnThreshold -
            max(state.evidence, 0));

Write:
    current measured plane
    current color
from the SAME immutable measurement.

No automatic radial attention.
No soft fineWeight.

Absolutely no canonical mutation outside cylinder.

============================================================
G. ERASE
============================================================

ERASE is not evidence.

ERASE is explicit DELETE.

On trigger:

    exact cylinder
        ↓
    sparse tile query
        ↓
    resolve required COLD tiles
        ↓
    for every contained canonical K:
        reset canonical state

Reset:

    OccupancyEvidence
    PackedColor
    ColorConfidence
    surface-plane flags
    OccupiedFlag
    NeedsCarveFlag
    attempt/touched state where applicable
    occupied counters
    dirty/writeback state

Reuse current EraseFineTiles bookkeeping.
Do not create a second erase implementation.

No IntegrationInterval throttle.

Scheduler:

    while trigger held:
        when previous ERASE job retires
        submit current descriptor immediately

Visual result:
    press -> region disappears.

No countdown.

============================================================
H. SESSION + ANCHOR — ONE AUTHORITY
============================================================

An existing session owns exactly one persisted AnchorUuid.

ANCHOR CREATION is legal ONLY for:

    NEW SESSION

Never for:

    START existing session
    LOAD
    RESUME from sleep
    SAVE
    GLB export
    3D Tiles export
    readout
    ALIGN of an artifact

------------------------------------------------------------
H1. API
------------------------------------------------------------

Replace ambiguous no-argument “ensure anchor” use with semantics equivalent to:

    EnsureSessionAnchorAsync(
        Guid requiredUuid,
        bool allowCreate)

Rules:

    requiredUuid != Empty
        -> localize exactly this UUID
        -> NEVER create another

    requiredUuid == Empty && allowCreate
        -> create new anchor

    requiredUuid == Empty && !allowCreate
        -> fail explicitly

------------------------------------------------------------
H2. NEW SESSION
------------------------------------------------------------

NEW:

    create SessionId
    create session directory
    create persisted spatial anchor
    store UUID
    bind RoomSpaceRoot
    create empty M8 world

------------------------------------------------------------
H3. RESUME AFTER SLEEP
------------------------------------------------------------

Before pause remember:

    active SessionId
    active AnchorUuid
    whether scanning was running

On resume:

    wait tracking valid
    localize same AnchorUuid
    wait anchor tracked
    wait RoomSpaceRoot bound
    restore sensor frontend
    then resume scanning

Never call generic create path.

If localization fails:
    leave scan stopped
    show user:
        "Room anchor not localized"
    offer Retry.

Do NOT silently start in a new coordinate system.

------------------------------------------------------------
H4. LOAD
------------------------------------------------------------

OPEN SESSION:

    quiesce
    read session metadata FIRST
    obtain AnchorUuid
    localize exact anchor
    bind RoomSpaceRoot
    switch storage root
    load M8
    warm readout
    publish membrane
    allow scan continuation

No fresh anchor.

------------------------------------------------------------
H5. EXPORT
------------------------------------------------------------

Remove exporter use of:

    EnsureSpatialAnchorAsync()

Exporter requires active session anchor.

Conceptual replacement:

    Guid uuid = session.AnchorUuid;

    if (uuid == Guid.Empty)
        fail "Active session has no persisted room anchor";

    if (!await anchorManager.EnsureSessionAnchorAsync(uuid, false))
        fail "Active session room anchor could not be localized";

Then:

    AnchorFromPackage =
        SpatialAnchorMatrix.inverse *
        GridToWorldMatrix

Export cannot repair a missing session by creating another anchor.

------------------------------------------------------------
H6. ALIGN 1:1
------------------------------------------------------------

Keep current good artifact-localization concept:

    package AnchorUuid
    package AnchorFromPackage

Artifact alignment uses:
    LocalizeArtifactAnchorAsync()

This MUST NOT replace scanner active anchor.

The same transform algebra is used for:

    active scan
    loaded session
    readout GridToWorld
    GLB package
    3D Tiles package
    design objects
    ALIGN 1:1

For package with valid spatial binding:
    ALIGN 1:1 must restore exact physical registration.

For legacy package without binding:
    display:
        "Legacy model — automatic 1:1 alignment unavailable"

Do not fake successful alignment.

============================================================
I. MULTIPLE NAMED SESSIONS
============================================================

Current single path must become:

    MerkabaScan/
      sessions/
        <uuid>/
          session.json
          merkaba-grid.bin
          merkaba-live.m8log
          design.json
      library/
      exports/

Reuse:

    MerkabaSsdStore(string directory)

Do not rewrite its binary storage format.

Add only session-root switching around it.

`session.json`:

    formatVersion
    sessionId
    displayName
    createdUtc
    modifiedUtc
    anchorUuid

Optional:
    thumbnailPath

------------------------------------------------------------
I1. COMMANDS
------------------------------------------------------------

Production commands:

    NEW
    OPEN
    SAVE
    SAVE AS
    RENAME
    DELETE

SAVE:
    writes active session.

SAVE AS:
    creates another session ID/root;
    same M8 snapshot;
    same anchor UUID;
    same design state.

It does NOT create another room anchor.

OPEN:
    session browser.

External portable session import/export may use SAF,
but GLB remains MODEL, not scan session.

============================================================
J. 3D TILES EMPTY EXPORT
============================================================

Do not patch `CompleteStreamingPackage()` to accept zero leaves.

That exception is correct.

Current failure means:

    StreamOwnedMembranesAsync
        produced zero consumable groups/leaves.

Make the pipeline stop silently discarding the cause.

For each owner group record:

    storedTiles
    nonzeroStates
    occupiedOwners
    measuredOwners
    membranePatches
    ownedPatches
    emittedLeaf

If:

    occupiedOwners > 0
    && measuredOwners > 0
    && ownedPatches == 0

throw immediately with owner chunk address and counts.

Do not `continue` silently.

Invariant chain:

    stored canonical data
      ->
    occupied owners
      ->
    measured membrane
      ->
    owned membrane
      ->
    GLB leaf
      ->
    3D Tiles hierarchy

After corrected shared membrane oracle,
a valid measured occupied owner must produce its deterministic local membrane
unless explicitly rejected by documented export-only partition logic.

GLB and 3D Tiles consume THE SAME measured membrane.

Do not retain a different primitive fallback for 3D Tiles.

Export-only inferred hole patches remain allowed if already validated,
but must use final 25mm/shared-corner footprint, not 50mm cards.

============================================================
K. PRODUCTION UX — REPLACE DEBUG DASHBOARD
============================================================

The current product-facing UX must no longer resemble an engineering monitor.

Keep UI Toolkit and current scene wiring.

Do not import a UI framework.

Redesign existing UXML/USS/controller.

Main interaction model:

    SCAN
    REFINE
    DESIGN
    VIEW

Primary panel:
    compact controller/palm-attached workspace
    approximately existing physical panel footprint
    much less vertical clutter

Visual language:

    dark neutral translucent surface
    one restrained accent color
    14-18px-equivalent corner radius
    no glowing cyan frame around every control
    no giant letter spacing
    no all-caps everywhere
    clear typographic hierarchy
    selected state obvious
    inactive controls quiet
    destructive controls clearly separated

Touch/ray target:
    minimum approx 44-48 px-equivalent height
    primary buttons ~52-56

Primary UI NEVER shows by default:

    FPS
    active chunks
    kernel count
    hash counters
    checker/debug modes
    internal M8 terminology

Move these to:

    Settings / Diagnostics

collapsed by default.

------------------------------------------------------------
K1. TOP BAR
------------------------------------------------------------

Always show:

    current session name
    scanning state
    save dirty/saved state

Context actions:

    undo / redo
        for DESIGN only

    overflow menu:
        diagnostics
        advanced display options

Long operations use one clean progress overlay/card.

No spinner ASCII:
    | / — \

Use ordinary indeterminate/progress presentation.

------------------------------------------------------------
K2. SCAN SCREEN
------------------------------------------------------------

Primary CTA:

    START SCAN
or:
    STOP SCAN

Secondary:

    New
    Open
    Save
    Save As
    Export

Display:
    session name
    Saved / Unsaved
    compact scan status

Readout opacity may remain.

Move:
    checker
    raw mesh readout
    technical occlusion switches

to Diagnostics.

Production standard readout is always membrane.

------------------------------------------------------------
K3. REFINE SCREEN
------------------------------------------------------------

Top segmented tool:

    REFINE | ERASE

Large controls:

    Radius
    Length

Use useful direct units:
    cm
    m/cm

Show exact live cylinder.

REFINE:
    green/teal

ERASE:
    red

No “Brush angle”.

------------------------------------------------------------
K4. DESIGN SCREEN
------------------------------------------------------------

Submodes:

    PAINT
    OBJECTS

Persistent compact bottom/side tool strip.

Context inspector changes with active tool.

This is a design workspace, not annotation debugging.

------------------------------------------------------------
K5. VIEW SCREEN
------------------------------------------------------------

Keep:

    Open Model
    Model / Plan
    opacity
    World Lock
    ALIGN 1:1
    measurements
    notes

Model controls remain:
    right grip 6DoF
    two-hand scale/rotate
    existing gestures

Do not regress them.

============================================================
L. UI MUST WIN RAYCAST
============================================================

Current controller ray must not let Default/model geometry block UI.

Use a dedicated UI layer for menu interaction.

ControllerRayDriver UI probe:
    raycast UI layer only.

Scene/model input MUST begin with:

    if (_rayDriver != null &&
        _rayDriver.IsPointingAtUi)
        return;

before:
    paint
    annotation
    model manipulation
    scene trigger actions.

One trigger press goes to exactly one authority:

    UI first
    else active scene tool

Never both.

Menu must render after scan/model so it cannot be visually buried by them.

Use the existing world-space UI architecture and dedicated UI presentation
ordering/layer; do not solve by continuously moving menu closer to the camera.

Acceptance:
    stand inside model
    open menu
    every visible control is readable and clickable.

============================================================
M. COLOR UI
============================================================

Keep actual HSV wheel.

Do NOT go back to RGB sliders.

Increase wheel visual size from current 112px class to approximately:

    180-220px

Controls:

    HSV wheel = hue/saturation
    Value
    Opacity

Add:

    recent 8 colors
    saved swatches
    eyedropper

When pointer is captured:
    held trigger movement MUST update continuously.

Current PointerCapture stays.

Also fix XR event routing so a captured pointer continues receiving position
updates while trigger remains held.

Pointer release:
    release capture exactly once.

No need for repeated trigger downs.

============================================================
N. PAINT — PRODUCTION TOOLSET
============================================================

Paint remains a DESIGN layer.

It never changes M8.

Do not continue representing all paint as AnnotationRecord + LineRenderer.

Required tools:

    BRUSH
    SURFACE BRUSH
    3D BRUSH
    SPRAY
    LINE
    ERASER
    EYEDROPPER

Tool inspector where applicable:

    Size
    Opacity
    Flow
    Hardness
    Saturation
    Shape:
        Round
        Square

SPRAY additionally:

    Density
    Scatter

------------------------------------------------------------
N1. DESIGN DATA
------------------------------------------------------------

Introduce a small design representation.

Example:

    DesignStroke
    {
        id
        tool
        color
        opacity
        flow
        hardness
        saturation
        radius
        shape
        samples[]
    }

Sample stores what tool needs:
    position
    optional normal
    optional radius/pressure

Stored in SESSION ROOM coordinates.

Keep existing survey annotations separately or migrate compatibly.

Do not rebuild all paint GameObjects after every sample.

------------------------------------------------------------
N2. SURFACE BRUSH
------------------------------------------------------------

Each new controller sample:

    raycast model
    obtain point + normal

Between previous and current hit interpolate dabs so fast motion has no gaps.

Spacing:

    min(
        practical minimum,
        radius * 0.20
    )

Use approximately:
    radius * 0.20

as normal dab spacing.

Project each interpolated dab to surface.

No floating straight-line bridge through space when surface curves.

------------------------------------------------------------
N3. NORMAL BRUSH
------------------------------------------------------------

BRUSH may be a smooth surface-oriented dab/ribbon tool for painting large
areas more naturally than LINE.

Hardness controls edge falloff.

Flow controls accumulation per travelled distance/time.

Opacity is final alpha ceiling.

------------------------------------------------------------
N4. 3D BRUSH
------------------------------------------------------------

Delete current behaviour where first model hit determines a fixed distant
`_spatialPaintDistance`.

Default point is:

    controllerPosition
      +
    controllerForward * 0.20 m

Every frame.

Thus it follows the hand directly.

If later depth adjustment exists, retain it as an explicit modifier,
but default is always 20cm.

Render as:
    instanced dabs
or:
    proper tube/ribbon

not universal view-facing LineRenderer.

------------------------------------------------------------
N5. SPRAY
------------------------------------------------------------

SPRAY is not a line.

For held trigger:

    count =
        deterministic function(dt, flow, density)

Generate independent dabs around tool axis/volume using deterministic seeded
hash/blue-noise style sampling.

Apply:
    scatter
    shape
    radius
    opacity
    flow
    saturation

Do not connect samples.

Same recorded stroke/session must reproduce the same dab set.

------------------------------------------------------------
N6. LINE
------------------------------------------------------------

LINE remains intentional start/end geometry.

Optional surface snap.

This is the one tool for which line geometry is correct.

------------------------------------------------------------
N7. PAINT ERASER
------------------------------------------------------------

Current behaviour deleting an entire touched stroke is forbidden.

Eraser volume acts locally.

For dab-based strokes:
    remove contained dabs.

For sampled/ribbon stroke:
    split retained samples into surviving segments.

Untouched parts remain.

------------------------------------------------------------
N8. EYEDROPPER
------------------------------------------------------------

When aimed at model:

    sample interpolated displayed model vertex color

When aimed at paint:
    sample paint color

Set active paint color.

============================================================
O. DESIGN OBJECT LIBRARY
============================================================

DESIGN / OBJECTS must be usable to prepare and place reusable scene assets.

Required:

    IMPORT GLB
    library browser
    PLACE
    SELECT
    MOVE
    ROTATE
    SCALE
    DUPLICATE
    DELETE
    HIDE/SHOW
    LOCK

No new GLB parser.

Factor/reuse existing proven ArtifactViewer GLB decoder.

------------------------------------------------------------
O1. LIBRARY
------------------------------------------------------------

Store assets under:

    MerkabaScan/library/

Identity:
    SHA-256 of GLB bytes

Metadata:
    id/hash
    displayName
    bounds
    importedUtc

Same GLB imported twice:
    one stored asset.

------------------------------------------------------------
O2. PLACEMENT
------------------------------------------------------------

Selected asset:
    translucent ghost preview.

If ray hits target surface:
    place at surface hit.

Otherwise:
    place 0.50m in front of controller.

After placement:

    right-grip 6DoF manipulation
    reuse existing viewer grab math

Two-hand:
    reuse existing rotate/scale math.

Options:

    Surface snap
    Upright snap
    Grid snap

Do not implement another unrelated gesture system.

------------------------------------------------------------
O3. PERSISTENCE
------------------------------------------------------------

design.json stores instances:

    instanceId
    assetId
    position in session room space
    rotation
    scale
    visible
    locked

plus paint document.

On session reopen/resume:
    localize session anchor
    bind RoomSpaceRoot
    restore design objects
    restore paint

Objects must return to same physical location.

Design objects NEVER enter M8.

============================================================
P. VIEWER / EXPORT FEATURES THAT MUST SURVIVE
============================================================

Do not remove or regress:

    3D Tiles ZIP
    index.html offline PC browser viewer
    Three.js licensing/resources
    GLB export
    Quest artifact viewer
    streamed tiles
    plan style
    opacity
    annotations
    survey point/line/plane
    right-controller manipulation
    two-hand rotate/scale
    World Lock
    ALIGN 1:1
    package spatial binding

============================================================
Q. OBSOLETE PRODUCTION UI/PATHS
============================================================

Production primary UI must not expose:

    "MESH ON/OFF" alternate reconstruction
    coverage checker
    debug FPS
    chunks/kernels
    internal readout compiler modes

These may remain behind DIAGNOSTICS if still useful.

There must be exactly one production scan/readout path:

    M8 -> membrane -> indexed FRONT -> draw

============================================================
R. REQUIRED TESTS
============================================================

MEMBRANE:
    all C7 cases.

READOUT:
    head translation without residency/M8 change
        -> no rebuild

    GridToWorld correction
        -> draw moves
        -> no topology rebuild

    canonical change
        -> dirty
        -> one coalesced build

    scanner observation pending
        -> scanner wins queue

    BACK failure
        -> FRONT unchanged

SCAN:
    flat wall quickly fills
    revisit refines same owner
    no shadow sheet
    angled wall
    thin partition

CARVE:
    one bad FREE keeps wall
    repeated strong clear removes unsupported artifact
    identical tile-boundary behaviour

FINE:
    no EyeOrigin field
    preview predicate == mutation predicate
    outside cylinder byte-identical
    hole can be drawn immediately
    existing compatible owner refined
    no parallel sheet created

ERASE:
    first completed erase operation clears contained volume
    outside byte-identical
    COLD page resolved before completion
    deleted state does not reappear after SAVE/OPEN

ANCHOR:
    only NEW creates
    OPEN never creates
    RESUME never creates
    EXPORT never creates
    saved UUID preserved
    scan/readout/design share RoomSpaceRoot
    artifact ALIGN does not replace active scanner anchor

SESSION:
    create A
    create B
    save A
    save B
    reopen A
    no B data visible
    Save As creates independent storage root
    same intended anchor retained

3D TILES:
    valid nonempty measured scan -> >=1 leaf
    exact stage reported on invariant failure
    ZIP browser viewer files present

UI:
    menu works from inside loaded model
    scene trigger cannot pass through UI
    primary UI contains no debug metrics

COLOR:
    held-trigger wheel drag continuously changes hue/saturation

PAINT:
    fast surface movement has no gaps
    3D brush = ~20cm from hand
    spray has disconnected distributed dabs
    erasing middle of stroke preserves both outer pieces
    save/reopen paint identical

OBJECTS:
    duplicate import dedupes bytes
    placed transform round-trips
    anchor resume restores physical location

============================================================
S. GREP/STRUCTURAL GATES
============================================================

Production standard path should have zero semantic uses of:

    M8FrontReadoutEyeMask
    M8IsFrontReadoutKernelForEye
    M8EmitReadoutGlyph
    M8_READOUT_BUILD_GLYPH
    FineEyeOrigin
    FineCosHalfAngleSquared
    TryGetCyclopeanEyeOrigin

No:
    PatchHalfExtent = MerkabaConstants.HalfSupport

for membrane footprint.

Search for forbidden architecture:

    Surface Nets
    QEF
    TSDF
    Marching Cubes

No newly introduced production geometry authority.

============================================================
T. BUILD GATES
============================================================

Regenerate all generated HLSL/native payloads from source authorities.

Run existing:

    Tools/unity/run_merkaba_tests.sh
    Tools/shaders/audit_merkaba_compute_spirv.sh

and current GLB/3D Tiles validation.

Build Quest APK only after:

    tests pass
    shader audit passes
    generated files match generators
    git diff manually reviewed

Do not install/test intermediate speculative APKs.

============================================================
U. DEVICE ACCEPTANCE
============================================================

NORMAL SCAN:
    coherent walls fill rapidly
    no shadow plates
    no unexplained wall deletion
    clear revisit removes stale artifacts
    no progressive readout slowdown

READOUT:
    continuous thin membrane
    no cards
    no noodles
    no square view-dependent holes
    no view-dependent disappearing walls
    no eye-dependent topology
    already prepared FRONT draws at GLB-view-class cost

Performance objective:
    recover the previously observed ~50Hz-class behaviour immediately;
    target normal Quest 72Hz budget where sensor workload permits.

FINE:
    cursor stays on Environment Depth
    hand controls tool
    immediate local refine
    exact visible cylinder
    zero edits outside

ERASE:
    press -> contained canonical data gone

SLEEP/WAKE:
    same physical alignment
    scan continues

SESSIONS:
    multiple named scans selectable
    reopen and continue correctly

3D TILES:
    successful nonempty export
    PC ZIP viewer works

ALIGN:
    1:1 package returns to physical room

UX:
    looks like one coherent commercial XR application,
    not a debug screen.

PAINT:
    useful for actual design work.

OBJECTS:
    import -> library -> place -> edit -> save -> reopen works end-to-end.

============================================================
V. NO FAKE COMPLETION
============================================================

Final Codex response must contain:

    base SHA
    final SHA

    changed production files
    new production files

    removed obsolete authorities

    membrane:
        CPU authority
        generated HLSL
        live consumer
        GLB consumer
        3D Tiles consumer

    readout:
        old build triggers
        new build triggers
        actual dispatch sequence
        FRONT/BACK semantics

    scan:
        exact owner-routing change
        exact fast-admission change
        exact carve-clear rule

    FINE:
        descriptor
        target algorithm
        admission rule

    ERASE:
        delete flow

    anchor:
        NEW
        OPEN
        RESUME
        EXPORT
        ALIGN

    session:
        on-disk layout
        commands

    3D Tiles:
        actual proven root cause of empty export
        exact fix

    UX:
        final navigation
        Diagnostics separation

    paint:
        implemented tools
        renderer/data representation
        local erase

    object library:
        import/storage/placement/persistence

    tests actually executed
    shader audit actually executed
    APK build result
    git diff --stat
    git status

If Quest device acceptance was not executed:
    say exactly:
        DEVICE ACCEPTANCE PENDING

Do not claim measured performance without Quest device timestamps.

============================================================
FINAL INVARIANT
============================================================

There is ONE application:

    precise automatic scanner
    +
    explicit local refine/delete
    +
    fast indexed membrane readout
    +
    durable named sessions
    +
    reliable physical room anchor
    +
    GLB/3D Tiles export/view
    +
    polished model/design/paint workspace

There are not separate demo subsystems glued together.

M8 remains world truth.

Everything else is a clean disposable or design-layer consumer of it.
