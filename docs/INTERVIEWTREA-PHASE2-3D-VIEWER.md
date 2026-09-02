# InterviewTrea — Phase 2: 3D Volume Rendering

> **RESEARCH AND DEMONSTRATION USE ONLY — NOT A MEDICAL DEVICE. NOT FOR DIAGNOSTIC USE.**

---

## 0. Document purpose

This is the complete requirements and design specification for Phase 2. It replaces an
earlier Phase 2 document that specified a coronary calcium scoring application; that scope
was cut, along with the plugin platform that would have hosted it. The project is a CT
viewer and a 3D view of the same volume, and nothing else.

**Do not start Phase 2 until Phase 1 is complete and its tests are green.** Phase 2 adds one
rendering project and one view. It changes nothing Phase 1 relies on, and Phase 1 must
continue to build and pass on its own throughout.

Requirement IDs continue from Phase 1 without overlapping it: functional requirements are
numbered FR-6xx, non-functional NFR-4xx. Phase 1's RQ and DI requirements apply unchanged.

---

## 1. Project overview

### 1.1 What it is

A volume-rendered 3D view of the study already loaded in the viewer. A ray is cast through
the reconstructed volume for every output pixel; along each ray, Hounsfield values are
sampled trilinearly and mapped through a transfer function to a colour and an opacity,
which are composited front to back until the ray saturates or leaves the volume. The user
orbits the camera by dragging, edits the transfer function, and switches between named
presets that make bone, vessels or the airway tree the thing you see.

It is the same volume, the same `short[]`, and the same geometry the MPR panes read. There
is no second copy of the data and no separate import.

### 1.2 Why this, after Phase 1

Phase 1's slab MIP is already a ray march: it steps along a ray and keeps the maximum. Full
volume rendering is that loop carried through to its conclusion — instead of a maximum,
accumulate colour and opacity, which requires a transfer function to say what a density
*means* and a compositing operator to say how contributions combine. Every hard part is a
recognisable extension of code that already exists and is already benchmarked, which is why
this is the right second phase rather than a second product.

It is also the feature that most directly resembles what advanced visualization software is
for. A radiologist reads the 2D planes; the 3D view is what gets shown to the surgeon, the
referring physician and the patient.

### 1.3 What "done" looks like for Phase 2

With a chest CT open, the user switches the layout to include a 3D view, sees a shaded
volume rendering of the thorax, drags to orbit it, picks the Bone preset and sees the rib
cage, picks Angio and sees the vascular tree, drags a control point in the transfer function
editor and watches the classification change — all at an interactive frame rate while
dragging, resolving to a full-resolution image when the mouse stops. The exported PNG
carries the RQ-1 disclaimer like every other export.

### 1.4 Explicit non-goals for Phase 2

| Not building | Why |
|---|---|
| GPU rendering (Direct3D, CUDA, compute shaders) | The algorithm is the point and a GPU path would hide it behind an API. It is also the honest answer to "how would you make this production-fast", which is worth more said than half-built |
| Segmentation, region growing, or AI classification | The transfer function classifies by density. That is a lookup, not a segmentation, and saying so precisely matters |
| Mesh extraction or surface rendering (marching cubes) | A different algorithm answering a different question. Volume rendering keeps the density data; a mesh throws it away |
| Multi-volume or fusion rendering (PET/CT overlay) | One volume, one modality. Phase 1 is CT-only by design and this does not change it |
| Cinematic or path-traced rendering | Beautiful, and weeks of work for an effect nobody reads a scan by |
| Saving or exporting a scene, camera, or transfer function to file | PNG export covers the demo need. A scene format is a serialisation exercise, not a rendering one |
| Curved planar reformation, PACS connectivity, DICOM write-back | Cut in Phase 1 and still cut |

### 1.5 Regulatory posture

Unchanged from Phase 1 and it applies in full. RQ-1 through RQ-4 hold: the banner stays,
the README carries the statement, and no exported artifact leaves without it embedded.

**RQ-5**: A volume-rendered image shall never be presented in a way that implies
measurement. No distance, area or Hounsfield readout is shown on the 3D view, because a
value read off a composited image is a function of the transfer function as much as of the
patient. Measurement stays in the MPR panes, where it is a fact about one plane.

### 1.6 Governing principle

The same one: every visible feature must be explainable to the bottom in a ten-minute demo,
nothing may depend on infrastructure, and failures must be visible and calm. Phase 2 adds
one strong temptation to break it — a transfer function editor can grow controls without
limit — so the rule is stated again here. Four presets and a draggable set of control
points, or fewer. If a control cannot be accounted for down to the compositing arithmetic,
it does not ship.

---

## 2. Technology

No new dependencies. The renderer is C# over the existing `short[]`, parallelised by
scanline with `Parallel.For`, targeting plain `net8.0` like every other library.

`InterviewTrea.Rendering3D` returns a `byte[]` and never references `System.Windows.*`,
exactly as `InterviewTrea.Rendering` does. The one difference is the pixel format: a volume
rendering is colour, so the buffer is BGRA32 rather than Gray8 and the WPF layer wraps it in
a `WriteableBitmap` of the matching format.

---

## 3. Solution structure

One new library and one new set of views. Nothing else moves.

```
src/
├── InterviewTrea.Core/                  unchanged
├── InterviewTrea.Dicom/                 unchanged
├── InterviewTrea.Rendering/             unchanged (2D: Gray8)
├── InterviewTrea.Rendering3D/           NEW. Core only. Returns byte[] in BGRA32.
│   ├── VolumeRaycaster.cs               the ray march and the compositing loop
│   ├── TransferFunction.cs              HU -> BGRA lookup table
│   ├── TransferFunctionPreset.cs        the named presets (FR-604)
│   ├── Camera3D.cs                      orbit camera, patient space
│   └── GradientShader.cs                central-difference normals, Phong (FR-606)
└── InterviewTrea.App/                   + VolumeView, TransferFunctionEditor
```

`Rendering3D` is a separate project from `Rendering` rather than a folder inside it. They
share no code, they have different output formats, and keeping them apart means the 2D
render path — the one with committed benchmark numbers — cannot be disturbed by work on the
3D one.

---

## 4. Domain model

### 4.1 The camera

An orbit camera in patient space, holding four things: a target (the centre of the volume,
or the crosshair), a distance, and two angles — azimuth and elevation. The eye position is
derived from those; there is no free-floating position and no accumulated matrix, for the
same reason the reslice frames in Phase 1 store axes rather than a rotation history.

The projection is **orthographic**, not perspective. Radiology convention is parallel
projection: a perspective view makes near structures larger, which is the one thing a
clinical image must not do. It is also simpler to defend and slightly cheaper — every ray
has the same direction.

### 4.2 The ray march

For each output pixel, a ray enters along the view direction. The volume is an axis-aligned
box in voxel space, so entry and exit come from a slab test (the standard three-axis
`tmin`/`tmax` intersection) performed in voxel coordinates, where the box is axis-aligned
even when the patient geometry is not.

Between entry and exit, step at a fixed interval and at each step:

1. Sample the volume trilinearly — the sampler Phase 1 already has and already tests.
2. Map the sample through the transfer function to a colour and an opacity.
3. Composite front to back with the **over** operator:

```
C_dst += (1 - A_dst) * A_src * C_src
A_dst += (1 - A_dst) * A_src
```

4. Stop early when `A_dst` exceeds a threshold (0.99): everything behind an opaque surface
   is invisible, and this is where most of the performance comes from.

**Opacity correction matters and is the classic bug.** The transfer function's opacity is
defined per unit distance. Change the step size and the same tissue becomes more or less
transparent, so an image rendered at half the step looks different rather than better. The
correction is `A = 1 - (1 - A_ref)^(step / step_ref)`, applied when the step changes — which
is exactly what progressive refinement does (FR-609). A renderer without it produces a
low-resolution preview that does not match the image it resolves to.

### 4.3 The transfer function

A lookup table over the same Hounsfield scale Phase 1 uses: 4096 entries covering −1024 to
3071, each an 8-bit BGRA quad. It is built from a small ordered list of control points —
`(hounsfield, colour, opacity)` — interpolated linearly between them, and rebuilt only when
a point moves. Per-sample work is then one array lookup, which is the same decision as the
window/level LUT in Phase 1 and for the same reason.

### 4.4 Gradient shading

Without shading a volume rendering is a fog bank: correct, and unreadable. Surface shading
needs a normal, and the gradient of the density field is the surface normal wherever there
is a surface. Compute it by central differences on the six neighbouring samples, normalise,
and light it with a single headlight Phong term.

The gradient is six extra trilinear samples per step, which roughly quadruples the cost of a
sample. That is the single biggest performance decision in the phase, and the mitigations
are: only shade where opacity is worth shading (skip samples the transfer function made
nearly transparent), and skip shading entirely during interaction.

---

## 5. Functional requirements

### FR-600 — Volume rendering

| ID | Requirement |
|---|---|
| FR-601 | The system shall render a volume-rendered 3D view of the loaded volume, using the same `Volume` instance the MPR views read. |
| FR-602 | Rendering shall be by ray casting with trilinear sampling and front-to-back **over** compositing, with early ray termination above 0.99 accumulated opacity. |
| FR-603 | Sample opacity shall be corrected for step size, so that changing the step changes the image's resolution and not its appearance. |
| FR-604 | Classification shall be by an editable transfer function mapping Hounsfield units to colour and opacity, held as a lookup table over the −1024..3071 scale. |
| FR-605 | The system shall provide named transfer function presets: Bone, Angio, Lung, and Skin. |
| FR-606 | The user shall be able to edit the transfer function by dragging control points, and the view shall update live. |
| FR-607 | Gradient-based Phong shading shall be applied, with normals from central differences. **Revised:** not toggleable. An unshaded volume rendering is a picture nobody would choose, so the toggle had a right answer and a wrong one, which makes it a default rather than a choice. |
| FR-608 | Left-drag shall orbit the camera; wheel shall zoom; middle-drag shall pan. The projection shall be orthographic. |
| FR-609 | The renderer shall refine progressively: a reduced-resolution image while the camera or transfer function is moving, full resolution when interaction stops. |
| FR-610 | The 3D view shall occupy the fourth pane, and shall be exportable as a PNG under FR-409 like any other view. |
| FR-611 | The 3D view shall show no measurement or Hounsfield readout (RQ-5). |
| FR-612 | With no volume loaded the 3D view shall show the same calm empty state as the MPR panes, not a blank or an error. |
| FR-613 | The 3D view shall provide one clip plane, trimming in from the patient's back by a user-set depth, so that the scanner table can be removed. **Added after Iteration 7**, on evidence: see below. |

**FR-610 needs no new control.** It was specified as a layout selector, which would have
been a ninth visible control whose only job is to rearrange the other eight. Phase 1 §1.6
rules that out, so the 3D view simply *is* the fourth pane, and the slab projection it
displaced moved onto the three planar panes - where a slab was always a property of a
plane rather than a pane of its own. Nothing was lost by the move and one pane was gained.
See [ADR-006](decisions/ADR-006.md).

Phase 2 adds exactly one visible control: the preset dropdown. FR-606's draggable control
points are **deferred** - the presets are the demo need, and an editor is the one feature
§1.6 names as able to grow without limit.

**FR-613 was added because the first real rendering had a slab of scanner table standing
behind the patient.** A CT couch reads between about 0 and 100 HU, which is where soft
tissue reads, so no transfer function can classify it away - classification is by density
and the table has the patient's density. Finding the body and keeping only that is
segmentation, which §1.4 rules out by name. What is left is geometry: the table is behind
the patient and nothing else is, so one plane parallel to the coronal plane separates them.
It is Shift+wheel over the 3D pane and is reported in that pane's overlay, so it adds no
visible control; it is one plane and not a six-sided clip box, because a box is a general
tool and this is a specific one. On a prone study the table is in front of the patient and
this will not touch it, which is stated rather than half-solved.

---

## 6. Non-functional requirements

| ID | Requirement | How to verify |
|---|---|---|
| NFR-401 | A full-resolution 512×512 volume rendering with shading shall complete in under 400 ms on the reference machine. | BenchmarkDotNet, committed to `docs/performance.md` |
| NFR-402 | An interaction-quality frame (quarter resolution, no shading) shall complete in under 50 ms, so that orbiting is continuous. | Same |
| NFR-403 | Rendering shall allocate nothing per frame: the output buffer and the transfer function table are reused. | Benchmark allocation column reads 0 B |
| NFR-404 | The 3D view shall add no more than 8 MB of steady-state managed heap beyond the volume itself. | The output buffer plus a 16 KB table; asserted in a test |
| NFR-405 | `InterviewTrea.Rendering3D` shall not reference `System.Windows.*`. | Enforced by target framework, as in Phase 1 |
| NFR-406 | Line coverage of `InterviewTrea.Rendering3D` shall exceed 70%. | `dotnet test --collect:"XPlat Code Coverage"` |

400 ms for a full frame is deliberately not a 60 fps claim. A CPU ray caster with gradient
shading over a 200 MB volume does not hit 60 fps and saying it would is the wrong instinct;
FR-609's progressive refinement is what makes it feel interactive, and being able to explain
that trade honestly is worth more than a number nobody believes.

---

## 7. Testing strategy

The same rule as Phase 1: **analytically derived expected values, never snapshots.** A
volume rendering is the hardest thing in this project to test by looking at, because a
plausible image is exactly what a subtly wrong renderer produces. Build these:

- **Compositing arithmetic.** The `over` operator, tested directly. Two half-opaque white
  samples over black composite to 0.75 alpha, not 1.0. Front-to-back and back-to-front over
  the same samples give the same result, which is what proves the accumulation is right.
- **Opacity correction.** The same phantom rendered at step *s* and step *s*/2 with
  correction applied must agree to within a tolerance; without correction it must not. The
  second half is what stops the test passing vacuously.
- **Early termination is invisible.** A render with termination at 0.99 and one with no
  termination must agree to within the tolerance the threshold implies. This is the test
  that catches the optimization changing the picture.
- **Ray/box intersection.** A ray along an axis through a phantom of known extent enters and
  exits at the exact millimetre the geometry says. Include a ray that misses entirely and one
  that starts inside the box.
- **A uniform cube renders a rectangle of known size.** With an orthographic camera down a
  patient axis and a transfer function that makes the cube's density opaque, the number of
  non-transparent output pixels is the cube's cross-section divided by the pixel pitch, to
  within one pixel of edge. This is the end-to-end geometry test.
- **The gradient of a known field.** Central differences on `Phantoms.GradientAlongX` must
  give a normal along x, exactly, at every interior voxel. On a uniform phantom the gradient
  is zero everywhere and shading must not divide by it.
- **The transfer function table.** A control point at a Hounsfield value maps to exactly its
  colour; the midpoint between two points is the exact average; values outside the outermost
  points clamp rather than extrapolate.

Phantom generators go in `tests/InterviewTrea.TestData` alongside the existing ones. As in
Phase 1, the view layer is not unit tested and the README says so.

---

## 8. Traceability

Same rule, same file. Append to `docs/traceability.md` in the change that satisfies the
requirement, not afterwards. A Phase 2 section, with the same columns.

---

## 9. Build plan

### Iteration 6 — The ray caster (target: ~4–5 days)

- `Camera3D` with orthographic ray generation, and the ray/box slab intersection.
- `TransferFunction` with control points and the 4096-entry table.
- `VolumeRaycaster`: the march, the over operator, early termination, opacity correction.
- Scanline parallelism, output to a reused BGRA32 buffer.
- Every test in §7 except shading.

**Done when:** a uniform cube renders as a rectangle of the size the geometry predicts, and
the tests say so without anyone looking at the image.

### Iteration 7 — Shading and the view (target: ~4–5 days)

- `GradientShader`: central differences, normalisation, single-headlight Phong (FR-607).
- The WPF view, the orbit/zoom/pan gestures (FR-608), progressive refinement (FR-609).
- The 3D view as the fourth pane, and the slab projection moved to the other three (FR-610, FR-207).
- The four presets (FR-605) and the transfer function editor (FR-606).
- Benchmarks against NFR-401 and NFR-402, committed with before-and-after figures.

**Done when:** the chest study can be orbited continuously, Bone shows ribs, Angio shows
vessels, and the numbers in `docs/performance.md` are measured rather than hoped for.

### Stretch goals

None. If there is time left, spend it on the demo.

---

## 10. Risk register

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| The full-resolution frame is far slower than 400 ms | Medium | Medium | Progressive refinement means the interactive path is what the user feels. Measure early, in Iteration 6, before shading is added — if the unshaded march is already over budget, the step size is the knob |
| Gradient shading quadruples the sample cost | High | Medium | Expected, not a surprise. Skip shading on nearly-transparent samples and skip it entirely during interaction. Both are in the plan |
| The transfer function editor grows without limit | Medium | High | §1.6. Four presets and draggable control points. Every additional control has to survive the ten-minute test before it is written |
| The rendering looks wrong but plausible | Medium | High | This is the real risk and §7 exists for it. No test in Phase 2 may assert on a captured image |
| Opacity correction is forgotten | Medium | High | It has its own requirement (FR-603) and its own test with a non-vacuous negative case |

---

## 11. Demo

Phase 2 earns two beats in the ten-minute demo and should not take more:

| Beat | What you say |
|---|---|
| Switch to the 3D view, orbit the thorax | One volume, read two ways. This is the slab MIP's ray march carried through to colour and opacity |
| Switch presets, drag a control point | The transfer function is a lookup table over the Hounsfield scale, and this is classification by density — not segmentation, and the difference matters |

If asked why it is not on the GPU: because the algorithm is the thing worth being able to
explain, and moving it to a compute shader is a port rather than a redesign — the
compositing, the correction and the termination are the same arithmetic wherever they run.
