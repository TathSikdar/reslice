# InterviewTrea — Multiplanar CT Visualization Workstation

**Phase 1 Specification: Core Viewer Platform**

> Project name: **InterviewTrea** (namespace `InterviewTrea`, solution `InterviewTrea.sln`).
>
> The name is a deliberate pun and you should own it as one — if an interviewer smiles at it, that is the joke landing, not a problem. Two things to keep clean regardless: the README must state plainly that this is an independent study project with no affiliation to Canon Medical Systems or Canon Medical Informatics, and no Canon or Vitrea logo, screenshot, icon, or UI asset may appear anywhere in the repository. Inspired-by is fine; passing-off is not.

---

## 0. Document purpose

This document is the complete requirements and design specification for Phase 1 of the project. It is written to be handed to an AI coding assistant (Claude Code, Copilot) as a project brief, and to be read by an interviewer as evidence of how you approach a build.

Phase 2 (a coronary calcium scoring application that docks into this viewer) is specified in a separate document. **Phase 1 must build and pass its tests standalone.** The plugin *host* infrastructure is in scope for Phase 1; the plugin *implementation* is not.

---

## 1. Project overview

### 1.1 What it is

A Windows desktop workstation, written in C#/WPF, that loads a CT study from disk or from a DICOM server and presents it as a synchronized multiplanar reconstruction: axial, coronal, and sagittal views generated from a single reconstructed 3D volume, plus a thick-slab maximum intensity projection view. The user can window/level, scroll, zoom, pan, place measurements, and reslice obliquely.

### 1.2 Why this project

The target team primarily builds Windows desktop applications and the services behind them, for multi-modality advanced visualization. This project exercises exactly that: DICOM ingestion, 3D volume reconstruction, real-time 2D rendering under a memory and latency budget, an MVVM desktop UI, dependency injection, and a plugin architecture for clinical applications.

It is also a genuine computer science problem. You will be doing coordinate transforms, trilinear interpolation, ray marching, and cache-conscious memory access on a ~200 MB volume while trying to hold 60 fps. That gives you real engineering decisions to talk about, which is the entire point.

### 1.3 What "done" looks like for Phase 1

A radiologist-shaped user can open a chest or cardiac CT series, see it in three orthogonal planes with linked crosshairs, switch between lung/soft-tissue/bone window presets, scroll through the volume smoothly, measure a distance in millimetres and an ROI in Hounsfield units, and rotate the reslice plane off-axis — all without the app stuttering or lying about the geometry.

### 1.4 Explicit non-goals for Phase 1

Do not build these. Cutting them is a decision you should be able to defend, not an omission you have to apologize for.

| Not building | Why |
|---|---|
| Full volume rendering with transfer functions | Weeks of work; MIP demonstrates the same ray-marching skill |
| Curved planar reformation / rib unfolding | Separate product, 4–6 uncertain days, and a half-working version reads worse than none |
| Any segmentation or AI inference | Phase 2 uses thresholding only |
| DICOMweb / PACS connectivity (Orthanc, QIDO-RS, WADO-RS) | Deferred by decision. Folder-open only — fewer moving parts to explain in a 10-minute demo, and no server to fail in the room |
| DICOM write-back / STOW-RS / structured reports | Deferred to Phase 2, where a calcium score report gives it a purpose |
| Secondary-capture DICOM export | PNG export covers the demo need at a fraction of the cost |
| Multi-study comparison or hanging protocols | Adds UI complexity, adds no algorithmic depth |
| MR, PET, or ultrasound support | CT only. State this as a deliberate scope boundary |
| User accounts, audit logging, PHI de-identification | Public research data is already de-identified |

### 1.5 Regulatory posture

This is not a medical device and must never look like one.

- **RQ-1**: The application shall display a persistent, non-dismissible banner reading: `RESEARCH AND DEMONSTRATION USE ONLY — NOT A MEDICAL DEVICE. NOT FOR DIAGNOSTIC USE.`
- **RQ-2**: The README shall carry the same statement above the fold.
- **RQ-3**: No output artifact (screenshot export, report) shall be produced without that statement embedded in it.
- **RQ-4**: The README shall state that the project is an independent study exercise with no affiliation to, endorsement by, or code derived from any commercial vendor.

Mention in your interview that you know IEC 62304 exists, that it classifies medical device software by the harm a defect could cause, and that this is why you wrote a traceability matrix (§9) for a hobby project. That single sentence will do more for you than an extra feature.

### 1.6 Governing principle: the ten-minute demo

Phase 1 was scoped against a hard constraint. This software gets roughly ten minutes at the end of an interview, on a machine you may not control, in front of people who will ask about anything they see on screen.

That constraint outranks feature count. Three rules follow from it:

1. **Every visible feature must be one you can explain to the bottom.** If a button exists that you cannot account for down to the algorithm behind it, delete the button. An unexplained control is a trap you built for yourself.
2. **Nothing in the demo may depend on infrastructure.** No servers, no containers, no network. The app opens a folder from local disk. If the venue's machine has no Docker, no internet, and a locked-down firewall, the demo is unaffected.
3. **Failure modes must be visible and calm.** Loading a bad series shows a clear message, not a stack trace or a hang. You will not have time to debug live, so the app has to fail in a way you can narrate.

This principle is also the reason PACS connectivity was cut. It was the highest-value item on paper, and it introduced a Docker container, a server process, and a network hop into a ten-minute window where none of that could pay off.

When a new feature idea appears mid-build, test it against this section before writing any code.

---

## 2. Technology stack

Every choice below maps to something on the job posting. Be ready to say which and why.

| Layer | Choice | Maps to |
|---|---|---|
| Runtime | .NET 8 (LTS) | C# |
| UI | WPF, MVVM pattern | Windows desktop applications |
| MVVM toolkit | CommunityToolkit.Mvvm 8.x | OO design, framework literacy |
| DI container | Microsoft.Extensions.DependencyInjection + Generic Host | "Experience with a Dependency Injection framework" |
| DICOM | fo-dicom 5.x | Domain literacy |
| Testing | xUnit + FluentAssertions | Testing best practices |
| Benchmarking | BenchmarkDotNet | Analytical skills |
| CI | GitHub Actions: restore, build, test on push | Development environment improvements |
| Source control | Git, conventional commits, PRs against `main` | Agile/Scrum |

**Deliberately avoided:** ITK/VTK bindings. They would do the interesting parts for you. Writing the interpolation and reslicing yourself is the reason this project is worth building. Say that out loud when asked.

---

## 3. Solution structure

```
InterviewTrea.sln
├── src/
│   ├── InterviewTrea.Core/                    # Domain. No WPF, no fo-dicom, no I/O.
│   │   ├── Geometry/                    # Vector3D, Matrix3x3, Plane, patient CS
│   │   ├── Volumes/                     # Volume, VolumeMetadata, IVolumeSampler
│   │   └── Measurements/                # Measurement models, statistics
│   │
│   ├── InterviewTrea.Dicom/                   # fo-dicom lives here and nowhere else.
│   │   ├── SeriesLoader.cs
│   │   ├── VolumeBuilder.cs
│   │   └── GeometryValidator.cs
│   │
│   ├── InterviewTrea.Rendering/               # Volume -> pixels. No WPF types.
│   │   ├── ResliceRenderer.cs
│   │   ├── SlabProjectionRenderer.cs
│   │   ├── WindowLevelLut.cs
│   │   └── RenderTarget.cs              # byte[] + width/height/stride
│   │
│   ├── InterviewTrea.Applications.Abstractions/   # Plugin contract. Phase 2 hooks here.
│   │   ├── IClinicalApplication.cs
│   │   ├── IApplicationContext.cs
│   │   └── IOverlayLayer.cs
│   │
│   └── InterviewTrea.App/                     # WPF. Views, ViewModels, composition root.
│       ├── App.xaml(.cs)                # Host builder, DI registration
│       ├── Views/
│       ├── ViewModels/
│       ├── Controls/                    # ViewportControl, MeasurementAdorner
│       └── Services/                    # Dialogs, file picking, app-level state
│
├── tests/
│   ├── InterviewTrea.Core.Tests/
│   ├── InterviewTrea.Dicom.Tests/
│   ├── InterviewTrea.Rendering.Tests/
│   ├── InterviewTrea.Benchmarks/              # BenchmarkDotNet, console exe
│   └── InterviewTrea.TestData/                # Synthetic phantom generators
│
├── docs/
│   ├── architecture.md
│   ├── traceability.md                  # See §9
│   ├── ai-assistance-log.md             # See §11
│   └── decisions/                       # ADR-001.md, ADR-002.md, ...
│
└── .github/workflows/ci.yml
```

**Dependency rule, enforced by project references:** `Core` depends on nothing. `Dicom`, `Rendering`, and `Applications.Abstractions` depend on `Core` only. `App` depends on everything. Nothing depends on `App`. If you find yourself wanting to reference WPF from `Rendering`, you have made a mistake — `Rendering` produces a `byte[]`, and `App` wraps it in a `WriteableBitmap`.

That rule is worth stating explicitly in the interview. It is why your rendering code is unit-testable without a UI thread.

---

## 4. Data acquisition

### 4.1 Primary source: The Cancer Imaging Archive (TCIA)

- Site: `https://www.cancerimagingarchive.net`
- Recommended collection: **LIDC-IDRI** — 1,010 thoracic CT studies, DICOM, thin-slice, widely used. Ideal for lung/bone windowing and MIP.
- Download via the **NBIA Data Retriever** desktop tool, or the TCIA REST API for scripted retrieval.
- Some collections are open-access; others require a free account. Register early so it is not a blocker.

**Pull 3–5 series only.** You need variety (different slice thicknesses, different manufacturers), not volume. A single series is roughly 100–400 files.

### 4.2 Secondary source: fo-dicom test data

The fo-dicom repository ships small sample DICOM files. Useful for unit tests of the parser but far too small to build a volume from. Do not rely on them for the viewer.

### 4.3 Identifiers in public research data

The data is de-identified, and this changes what the UI should display.

TCIA collections replace real patient identifiers with collection-scoped codes. PatientName typically reads `LIDC-IDRI-0142`, PatientID matches it, PatientBirthDate is usually blank or offset, and StudyDate is shifted. There is no meaningful patient name to show, and pretending otherwise produces an overlay that reads `LIDC-IDRI-0142` where a radiologist would expect a person.

- **DI-1**: The viewport overlay shall not display PatientName or PatientBirthDate. It shall display, instead: series description, modality, volume dimensions, voxel spacing, current slice position in mm, window/level, and zoom factor.
- **DI-2**: A separate, collapsed "Study information" panel may display the raw identifier fields verbatim, labelled as de-identified research codes.
- **DI-3**: The loader shall tolerate missing or empty PatientName, PatientBirthDate, StudyDate, and SeriesDescription without throwing. Absent optional tags are normal in public data and must not fail a load.

DI-3 is worth a test. A null-reference crash on a missing SeriesDescription in front of the interviewer would be an unforced error, and "I tested against de-identified data where half the demographic tags are empty" is a good thing to be able to say.

### 4.4 Data hygiene

- **DQ-1**: Test data shall not be committed to the repository. Add `data/` to `.gitignore`.
- **DQ-2**: `docs/data-setup.md` shall document exactly which collection and which series UIDs you used, so the project is reproducible.
- **DQ-3**: TCIA data is already de-identified. Do not add your own patient data, ever, for any reason.

---

## 5. Domain model

### 5.1 Coordinate systems

Get this right first. Every downstream bug traces back to coordinate confusion.

Three spaces exist:

1. **Voxel space** — integer `(i, j, k)` indices into the volume array.
2. **Patient space (LPS)** — millimetres, DICOM's convention: `+x` toward the patient's Left, `+y` toward Posterior, `+z` toward Superior. All measurements are reported in this space.
3. **Screen space** — pixels in a viewport.

The volume carries an affine transform from voxel to patient space, built from DICOM tags:

| Tag | Name | Use |
|---|---|---|
| (0020,0032) | ImagePositionPatient | Patient-space coords of the centre of voxel (0,0) of that slice |
| (0020,0037) | ImageOrientationPatient | Two direction cosines: row direction, then column direction |
| (0028,0030) | PixelSpacing | Row spacing \ column spacing, in mm |
| (0018,0050) | SliceThickness | Nominal slice thickness |
| (0018,0088) | SpacingBetweenSlices | Often absent or wrong — see below |
| (0020,0052) | FrameOfReferenceUID | All slices in a volume must share this |

**Slice spacing must be computed, not trusted.** Take the cross product of the two direction cosines to get the slice normal, project each slice's `ImagePositionPatient` onto that normal, sort by the resulting scalar, and take successive differences. Do not sort by `InstanceNumber` — it is not reliable across manufacturers. This is a real gotcha and a great thing to have discovered yourself.

### 5.2 Core types

```csharp
// InterviewTrea.Core.Geometry
public readonly record struct Point3D(double X, double Y, double Z);
public readonly record struct Vector3D(double X, double Y, double Z);
public readonly record struct Matrix4x4Affine(/* voxel -> patient */);

// InterviewTrea.Core.Volumes
public sealed class Volume
{
    public short[] Voxels { get; }          // Hounsfield units, x fastest, then y, then z
    public int DimX { get; }
    public int DimY { get; }
    public int DimZ { get; }
    public Vector3D Spacing { get; }        // mm per voxel along each axis
    public Point3D Origin { get; }          // patient coords of voxel (0,0,0)
    public Matrix4x4Affine VoxelToPatient { get; }
    public VolumeMetadata Metadata { get; } // patient name, study/series UID, modality, etc.

    public short this[int i, int j, int k] { get; }
    public short SampleNearest(Point3D patientPoint);
    public double SampleTrilinear(Point3D patientPoint);
}
```

**Storage decisions to make and justify:**

- Store HU as `short`, not `float`. CT HU range is roughly −1024 to +3071; `short` covers it and halves memory versus `float`. A 512 × 512 × 400 volume is then ~210 MB. In `float` it would be 420 MB, which starts to hurt.
- Store as a single flat array, not `short[,,]` or jagged. .NET multidimensional arrays have slower indexing and defeat some bounds-check elimination. Compute the index yourself: `k * DimX * DimY + j * DimX + i`.
- X-fastest ordering means axial slices are contiguous, which makes axial rendering a memcpy-shaped operation and coronal/sagittal strided. Note this asymmetry — it will show up in your benchmarks and it is exactly the sort of thing worth mentioning.

### 5.3 HU conversion

Raw stored pixel values are not HU. Convert on load:

```
HU = raw * RescaleSlope (0028,1053) + RescaleIntercept (0028,1052)
```

Respect `PixelRepresentation` (0028,0103) for signed vs unsigned, and `BitsStored`/`HighBit` for values that do not fill the allocated width. Getting this wrong produces an image that looks plausible but whose measurements are silently garbage — which is a useful thing to say about why measurement code needs tests against known phantoms.

---

## 6. Functional requirements

Requirement IDs are referenced by the traceability matrix in §9. Every one of these should map to at least one test.

### FR-100 — Series loading

| ID | Requirement |
|---|---|
| FR-101 | The system shall load a CT series from a user-selected directory of DICOM files. |
| FR-102 | The system shall group files by SeriesInstanceUID and prompt the user if multiple series are present. |
| FR-103 | The system shall sort slices by projection of ImagePositionPatient onto the slice normal, not by InstanceNumber. |
| FR-104 | The system shall convert stored pixel values to Hounsfield units using RescaleSlope and RescaleIntercept. |
| FR-105 | The system shall reject, with a clear message, a series whose slices do not share a FrameOfReferenceUID. |
| FR-106 | The system shall reject, with a clear message, a series with non-uniform slice spacing exceeding 1% variance. |
| FR-107 | The system shall reject, with a clear message, a series acquired with gantry tilt (non-orthogonal ImageOrientationPatient). |
| FR-108 | The system shall display a determinate progress indicator during load and remain responsive throughout. |
| FR-109 | The system shall load a 400-slice, 512×512 series in under 15 seconds on a mid-range laptop. |

Requirements 105–107 matter more than they look. Handling malformed input gracefully instead of crashing or, worse, rendering something subtly wrong, is the difference between a student project and software someone would let near a hospital. Write the validator before you write the renderer.

### FR-200 — Multiplanar display

| ID | Requirement |
|---|---|
| FR-201 | The system shall present four viewports in a 2×2 layout: axial, coronal, sagittal, and slab projection. |
| FR-202 | Each viewport shall render a plane resampled from the reconstructed volume. |
| FR-203 | The system shall support single-viewport maximized mode, toggled by double-click. |
| FR-204 | Each viewport shall display an orientation overlay (A/P/L/R/S/I markers at the edges). |
| FR-205 | Each viewport shall display slice position in mm, current window/level, and zoom factor. Patient identifiers shall not appear — see DI-1. |
| FR-206 | The system shall render oblique planes by trilinear interpolation of the volume. |
| FR-207 | The slab viewport shall support Maximum, Minimum, and Average intensity projection over a user-set slab thickness (1–100 mm). |
| FR-208 | The system shall preserve aspect ratio correctly for anisotropic voxels (e.g. 0.7 × 0.7 × 3.0 mm). |

FR-208 is the one people miss. If your voxels are 3 mm apart in Z and 0.7 mm in-plane, and you render coronal without accounting for it, the patient comes out squashed. Have a test for it.

### FR-300 — Interaction

| ID | Requirement |
|---|---|
| FR-301 | Mouse wheel over a viewport shall scroll through slices along that viewport's normal. |
| FR-302 | Right-drag shall adjust window width (horizontal) and window level (vertical). |
| FR-303 | Middle-drag shall pan; Ctrl+wheel shall zoom about the cursor. |
| FR-304 | The system shall render linked crosshairs: clicking a point in one viewport shall move the other two viewports to intersect that patient-space point. |
| FR-305 | The system shall provide window/level presets: Lung (W1500/L−600), Soft Tissue (W400/L40), Bone (W1800/L400), Brain (W80/L40), Mediastinum (W350/L50). |
| FR-306 | The system shall apply the series' own WindowCenter/WindowWidth (0028,1050/1051) as the initial preset when present. |
| FR-307 | The system shall support oblique reslicing: dragging a crosshair arm shall rotate the reslice plane, updating dependent viewports live. |
| FR-308 | All viewport interactions shall maintain interactive frame rates per NFR-201. |

### FR-400 — Measurement

| ID | Requirement |
|---|---|
| FR-401 | The system shall support a linear distance measurement between two points, reported in mm to one decimal place. |
| FR-402 | Distance shall be computed in patient space, correctly accounting for anisotropic spacing and oblique planes. |
| FR-403 | The system shall support an elliptical ROI reporting area (mm²), mean HU, standard deviation, min HU, and max HU. |
| FR-404 | The system shall support a rectangular ROI with the same statistics. |
| FR-405 | The system shall display a live HU readout for the voxel under the cursor. |
| FR-406 | Measurements shall persist while scrolling and shall be hidden when the current slice is more than half a slice thickness from the measurement's plane. |
| FR-407 | The system shall support deleting an individual measurement and clearing all. |
| FR-408 | The system shall export the measurement list to CSV. |
| FR-409 | The system shall export the active viewport as a PNG with the RQ-1 disclaimer burned into the image. |

### FR-500 — Application platform (plugin host)

This section is the seam Phase 2 plugs into. Build the host now, ship it with zero plugins registered, and prove it works with one trivial example plugin.

| ID | Requirement |
|---|---|
| FR-501 | The system shall define an `IClinicalApplication` contract in `InterviewTrea.Applications.Abstractions`. |
| FR-502 | Clinical applications shall be discovered through the DI container at startup and listed in an Applications menu. |
| FR-503 | Launching an application shall pass it an `IApplicationContext` giving read access to the loaded volume, the current reslice plane, and the measurement store. |
| FR-504 | An active application shall be able to contribute a tool panel to the right-hand dock. |
| FR-505 | An active application shall be able to contribute overlay layers rendered on top of any viewport. |
| FR-506 | The core viewer shall build, run, and pass all tests with no clinical applications registered. |
| FR-507 | The system shall ship one trivial reference application ("Histogram") that displays a HU histogram of the loaded volume, to demonstrate the contract. |

Suggested contract shape:

```csharp
public interface IClinicalApplication
{
    string Id { get; }                 // "interviewtrea.histogram"
    string DisplayName { get; }        // "Volume Histogram"
    string Description { get; }
    bool CanRun(IApplicationContext context);   // e.g. modality/series checks
    IApplicationSession Start(IApplicationContext context);
}

public interface IApplicationSession : IDisposable
{
    object ToolPanelViewModel { get; }              // bound by the shell
    IReadOnlyList<IOverlayLayer> OverlayLayers { get; }
    event EventHandler OverlaysChanged;
}

public interface IApplicationContext
{
    Volume Volume { get; }
    ReslicePlane CurrentPlane { get; }
    IMeasurementStore Measurements { get; }
    event EventHandler PlaneChanged;
}
```

Registration then looks like:

```csharp
services.AddSingleton<IClinicalApplication, HistogramApplication>();
// Phase 2 adds exactly one line here.
```

**This is the most interview-valuable part of Phase 1.** Canon's calcium scoring tool is not a standalone product — it is an application inside the Vitrea platform. Building a viewer that hosts pluggable clinical applications shows you read the product architecture, not just the feature list. When you demo, open the Applications menu and say "this is where the next one goes."

---

## 7. Non-functional requirements

| ID | Requirement | How to verify |
|---|---|---|
| NFR-101 | A 512×512×400 volume shall occupy under 300 MB of managed heap. | Task Manager / dotMemory; assert `Voxels.Length * 2` in a test |
| NFR-102 | Peak memory during load shall not exceed 2× steady-state volume size. | Manual profiling; documented |
| NFR-201 | Axis-aligned slice render shall complete in under 8 ms for a 512×512 viewport. | BenchmarkDotNet or `Stopwatch` in a test |
| NFR-202 | Oblique reslice render shall complete in under 16 ms for a 512×512 viewport. | Same |
| NFR-203 | Slab MIP over 20 mm shall complete in under 33 ms. | Same |
| NFR-204 | The UI thread shall never block for more than 50 ms during any interaction. | Manual; document your approach |
| NFR-301 | No DICOM parsing, rendering, or geometry code shall reference `System.Windows.*`. | Enforced by project references |
| NFR-302 | Line coverage of `InterviewTrea.Core` and `InterviewTrea.Rendering` shall exceed 70%. | `dotnet test --collect:"XPlat Code Coverage"` |
| NFR-303 | CI shall build and run all tests on every push to `main` and every PR. | GitHub Actions |
| NFR-304 | Every NFR-200 target shall have a committed BenchmarkDotNet result in `docs/performance.md`, with before-and-after figures for each optimization applied. | `dotnet run -c Release --project tests/InterviewTrea.Benchmarks` |

### 7.1 How to actually hit the performance numbers

You will not hit NFR-201 with naive per-pixel `Color` objects and LINQ. Techniques, roughly in order of payoff:

1. **Precompute the window/level LUT.** Build a `byte[65536]` mapping every possible `short` HU value to an output grey level. Rebuild only when window/level changes. Per-pixel work becomes one array lookup instead of a clamp-and-scale.
2. **Render into a raw `byte[]`, not a bitmap API.** `InterviewTrea.Rendering` returns a buffer; the WPF layer copies it into a `WriteableBitmap` via `WritePixels` once per frame. Use `PixelFormats.Gray8` — you are rendering greyscale, there is no reason to push four channels.
3. **Use `Span<T>` and avoid bounds checks** in the inner loop. `unsafe` with raw pointers is acceptable here and is a legitimate, defensible choice in a rendering hot path; isolate it to one method and comment why.
4. **Parallelize by scanline.** `Parallel.For` over output rows. Trivially safe since each row writes a disjoint region.
5. **Cache the axis-aligned case.** Axial slices are contiguous memory. Special-case them rather than routing everything through the general oblique sampler.
6. **Do not re-render on every mouse move event.** Coalesce to `CompositionTarget.Rendering` or a throttled dispatcher timer.

Do these in order, measuring after each. Keep the before/after numbers. "I got oblique reslice from 140 ms to 11 ms and here is the breakdown of where it went" is one of the best answers you can give to "tell me about a technical problem you solved."

---

## 8. Testing strategy

### 8.1 Synthetic phantoms — build these first

Before you have a single real DICOM file working, write `InterviewTrea.TestData` with generators that produce volumes with known ground truth:

| Phantom | Contents | Verifies |
|---|---|---|
| `UniformPhantom(hu)` | Every voxel the same value | LUT, statistics, sanity |
| `GradientPhantom` | HU = f(x) linear ramp | Interpolation correctness, orientation |
| `SpherePhantom(radiusMm, insideHu, outsideHu)` | Analytic sphere | Geometry, ROI area, anisotropic spacing |
| `CubePhantom(sizeMm)` | Axis-aligned cube of known dimension | Distance measurement, MPR alignment |
| `CheckerPhantom(period)` | Alternating voxel values | Aliasing, interpolation smoothing |
| `AnisotropicPhantom` | Sphere at 0.7×0.7×3.0 mm spacing | FR-208 aspect correction |

These let you assert real numbers. A distance measurement across a `CubePhantom(50)` must return 50.0 ± 0.5 mm. A trilinear sample halfway between two voxels of 0 and 1000 HU must return 500 ± 1. An ROI over `UniformPhantom(300)` must report mean 300, SD 0.

This is the closest thing to unit-testing a measurement device you can do without a real one, and it is precisely the argument for why medical software gets verified this way. Lead with it.

### 8.2 Test categories

- **Geometry tests** (`InterviewTrea.Core.Tests`) — voxel↔patient transforms round-trip; slice normal from direction cosines; sorting under shuffled input.
- **DICOM tests** (`InterviewTrea.Dicom.Tests`) — HU rescale for signed and unsigned; rejection of mismatched FrameOfReferenceUID; rejection of gantry tilt; non-uniform spacing detection. Build synthetic `DicomDataset` objects in-memory with fo-dicom rather than committing files.
- **Rendering tests** (`InterviewTrea.Rendering.Tests`) — render a `SpherePhantom` axially and assert the bright region is circular and centred; render coronally and assert the same; assert the LUT maps HU 40 with W400/L40 to mid-grey.
- **Measurement tests** — distances and ROI statistics against phantoms, including on oblique planes.
- **Benchmarks** (`InterviewTrea.Benchmarks`) — a separate BenchmarkDotNet console project, not part of the test suite and not run in CI. Benchmark the trilinear sampler in isolation, the axis-aligned fast path, the full oblique render, the LUT application, and the slab MIP. Run it in Release against a real volume loaded from disk.

Capture a baseline in Iteration 3 **before** you optimize anything. Every subsequent optimization gets a re-run and a row in `docs/performance.md` showing mean, standard deviation, and allocations before and after. That file is the evidence behind every performance claim you make in the interview, and it is the difference between "it got about ten times faster" and a table you can put on screen.

### 8.3 What not to test

Do not write tests for XAML bindings or view construction. Do not chase 100% coverage. State in your README that you targeted high coverage on the domain and rendering layers and deliberately left the view layer to manual verification, because that is where the return on test investment collapses. Knowing where to stop testing is a signal of maturity, not laziness.

---

## 9. Traceability

Maintain `docs/traceability.md` as a table. This is the single highest-leverage artifact in the whole project for a medical software interview, and it takes an hour.

| Req ID | Requirement (short) | Design element | Test(s) | Status |
|---|---|---|---|---|
| FR-103 | Sort by IPP projection | `SeriesLoader.SortSlices` | `SortsShuffledSlicesByPosition`, `IgnoresInstanceNumber` | Done |
| FR-104 | HU rescale | `VolumeBuilder.ApplyRescale` | `RescalesSignedPixelData`, `RescalesUnsignedPixelData` | Done |
| FR-206 | Trilinear oblique reslice | `ResliceRenderer.SampleOblique` | `TrilinearMidpointIsMean`, `ObliqueSphereIsCircular` | Done |
| FR-402 | Distance in patient space | `DistanceMeasurement.Compute` | `CubeEdgeIs50mm`, `AnisotropicDistanceCorrect` | Done |
| ... | ... | ... | ... | ... |

Every FR and NFR gets a row. If a row has no test, either write the test or mark it `Manual` with a note on how you verified it. Empty cells are fine as long as they are honest — an interviewer will respect "NFR-204 is verified manually, here's why automating it wasn't worth it" far more than a fabricated test name.

---

## 10. Build plan

Five iterations. Treat each as a sprint: a goal, a definition of done, and a demo. Track them as GitHub issues on a project board so you have something to point at when they ask about Agile.

### Iteration 1 — Foundations (target: ~4–5 days)

**Goal:** turn a folder of DICOM files into a validated `Volume` object.

- Solution scaffold, project references, dependency rule enforced.
- Generic Host + DI wiring in `App.xaml.cs`.
- `InterviewTrea.Core.Geometry`: points, vectors, affine transform, plane.
- `SeriesLoader`: enumerate, group by series, parse headers with fo-dicom.
- `GeometryValidator`: FrameOfReferenceUID, spacing uniformity, gantry tilt (FR-105/106/107).
- `VolumeBuilder`: sort correctly, rescale to HU, pack into `short[]`.
- `InterviewTrea.TestData` phantom generators.
- Tests for all of the above.
- CI pipeline green.

**Done when:** a console test harness prints "Loaded 342 slices, 512×512×342, spacing 0.68×0.68×1.00 mm, HU range −1024..2891" for a real TCIA series, and the test suite passes.

Resist the urge to open a window before this works. Everything downstream is built on the volume being correct.

### Iteration 2 — Single viewport (target: ~4–5 days)

**Goal:** see an axial slice on screen and scroll it.

- `WindowLevelLut` with preset table (FR-305/306).
- `ResliceRenderer` axis-aligned path → `byte[]` (FR-202).
- `ViewportControl`: `WriteableBitmap` host, `Gray8`.
- Main window shell, MVVM plumbing, folder-open command, progress (FR-108).
- Wheel scroll (FR-301), right-drag window/level (FR-302), zoom/pan (FR-303).
- Regulatory banner (RQ-1).

**Done when:** you can open a chest CT, scroll it smoothly, and flip between lung and bone windows.

### Iteration 3 — Multiplanar (target: ~5–7 days)

**Goal:** the 2×2 MPR layout with linked crosshairs. This is the heart of the project.

- Generalize the renderer to arbitrary planes with trilinear interpolation (FR-206).
- Coronal and sagittal viewports, with correct anisotropic aspect handling (FR-208).
- 2×2 layout, maximize-on-double-click (FR-203).
- Orientation markers and info overlays (FR-204/205).
- Crosshair rendering and cross-viewport linking (FR-304).
- Slab projection viewport: MIP, MinIP, Average (FR-207).
- Stand up `InterviewTrea.Benchmarks` and capture a baseline before optimizing anything (NFR-304).
- First performance pass against NFR-201/202/203; re-run benchmarks after each change and record before/after figures.

**Done when:** clicking a nodule in the axial view snaps the coronal and sagittal views to it, and everything stays responsive.

### Iteration 4 — Measurement and oblique (target: ~4–5 days)

**Goal:** the tools that make it a workstation rather than a picture viewer.

- Measurement domain model and store.
- Distance tool (FR-401/402), elliptical and rectangular ROI with statistics (FR-403/404).
- Live HU readout (FR-405).
- Measurement adorner layer, slice-proximity visibility (FR-406), delete/clear (FR-407), CSV export (FR-408).
- Oblique reslicing via crosshair rotation (FR-307). **This is the load-bearing feature of the iteration.** Without it the trilinear sampler is dead code and the algorithmic story collapses; build it before the ROI tools if the week looks tight.

**Done when:** you can measure a known-diameter structure and the number is right, on both an orthogonal and an oblique plane.

### Iteration 5 — Platform and polish (target: ~3–4 days)

**Goal:** the plugin seam and the presentation layer of the project.

- `IClinicalApplication` / `IApplicationContext` / `IOverlayLayer` contracts (FR-501/503).
- DI-based discovery, Applications menu, tool panel dock, overlay pipeline (FR-502/504/505).
- Reference Histogram application (FR-507).
- Verify the app runs clean with no applications registered (FR-506).
- PNG viewport export with burned-in disclaimer (FR-409).
- Final benchmark run; `docs/performance.md` written with before/after tables (NFR-304).
- `docs/traceability.md` completed.
- `docs/architecture.md` with a diagram.
- ADRs for the three or four decisions you actually agonized over.
- README with screenshots, setup instructions, and the disclaimer.
- `docs/ai-assistance-log.md`.

**Done when:** you could hand the repository to a stranger and they could build it, run it, and understand why it is shaped the way it is — and you have run the §14 demo end to end at least three times without notes.

### Stretch goals

None. Every previously listed stretch goal has been ruled on and either promoted into the plan (benchmarks, oblique reslicing) or cut (DICOMweb, secondary-capture export, curved MPR, STOW-RS).

If you find yourself with spare days at the end of Iteration 5, spend them on Phase 2 or on the demo rehearsal in §14 — not on new Phase 1 features. Scope discipline is part of what this project is demonstrating, and an interviewer who asks "what did you decide not to build?" is handing you the best question of the day.

---

## 11. Supporting artifacts

### 11.1 Architecture decision records

Write four to six short ADRs in `docs/decisions/`. Format: Context, Decision, Consequences. Half a page each. Candidates:

- ADR-001: `short[]` flat array over `float[,,]` for voxel storage
- ADR-002: Hand-rolled reslicing instead of VTK/ITK bindings
- ADR-003: Rendering layer returns `byte[]`, not WPF types
- ADR-004: Plugin discovery via DI container rather than assembly scanning
- ADR-005: Rejecting gantry-tilt series instead of resampling them

ADR-005 is worth writing carefully. "I could have resampled tilted volumes, but that's a correctness risk I didn't want to take on unvalidated, so I detect and reject with a clear message" is exactly the reasoning a medical software team wants to hear.

### 11.2 AI assistance log

The posting explicitly asks about GitHub Copilot and Claude Code. Keep `docs/ai-assistance-log.md` with short entries:

```
## Iteration 1
- Used Claude Code to scaffold the fo-dicom tag extraction. It initially assumed
  SpacingBetweenSlices was always present; I replaced this with computed spacing
  from ImagePositionPatient after finding a series where the tag was absent.
- Wrote the geometry transforms by hand — wanted to be certain of the conventions.
- Used Copilot heavily for xUnit boilerplate. High value, low risk.

## Iteration 3
- Asked Claude Code to optimize the oblique sampler. Suggestion to use Span<T>
  and hoist bounds checks was correct and gave ~3x. Suggestion to cache samples
  across frames was wrong for our access pattern; rejected.
```

The pattern to demonstrate is: used it fluently, verified its output, caught it being wrong. That is a far better answer than either "I didn't use AI" or "I used it for everything."

### 11.3 README structure

1. Disclaimer banner
2. One-paragraph what-and-why
3. Screenshot or short GIF of the 2×2 MPR with crosshairs
4. Build and run instructions
5. How to obtain test data (link to TCIA, name the collection)
6. Architecture summary with the dependency diagram
7. Links to traceability, ADRs, AI log
8. Known limitations — be honest and specific; it reads as confidence

---

## 12. Risk register

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Coordinate convention bugs eat days | High | High | Build phantoms and geometry tests in Iteration 1, before any UI |
| Performance targets not met | Medium | Medium | Optimize in the documented order; if you fall short, document the actual numbers and the reason — measured honesty beats a missed claim |
| Scope creep into volume rendering | Medium | High | §1.4 is binding. Reread it when tempted |
| TCIA download or registration delays | Low | High | Start the download on day one, before writing code |
| Oblique reslicing turns out harder than expected | Medium | High | **Not cuttable** (see §1.6). Ship axis-aligned MPR in Iteration 3 so there is always something working, then give oblique the whole of Iteration 4. If time runs out, cut rectangular ROI (FR-404) and CSV export (FR-408) instead |
| WPF rendering pipeline fights you | Low | Medium | `WriteableBitmap` + `WritePixels` is well-trodden; do not attempt custom `DrawingVisual` trees |

---

## 13. Interview preparation

Have crisp, specific answers ready for these. Rehearse them; do not wing it.

1. **"Walk me through the architecture."** Lead with the dependency rule and why rendering has no WPF reference. Two minutes, then stop.
2. **"Why didn't you use VTK?"** Because the reslicing and interpolation are the parts worth being able to explain, and a library would have hidden them.
3. **"How do you know your measurements are correct?"** Phantoms with analytic ground truth. Give the cube-edge example with the actual tolerance.
4. **"What was the hardest bug?"** Have a real one. Slice sorting or the anisotropic aspect ratio are both good candidates — both look correct until you measure something.
5. **"How did you make it fast?"** The LUT, the Gray8 buffer, scanline parallelism, the axis-aligned fast path. Open `docs/performance.md` and read the numbers off it rather than quoting from memory.
6. **"What would you do differently?"** Answer honestly. Something like: "I'd have written the geometry validator before the loader — I spent a day debugging a series that had variable spacing and I was assuming it didn't."
7. **"What would you have built next?"** PACS connectivity over DICOMweb. Say why you cut it — a container and a network hop inside a ten-minute demo — and that the loader is already behind an interface, so swapping folder-open for a WADO-RS retrieve is a new implementation rather than a rewrite.
8. **"How does this relate to what we build?"** Name the product category, not the product. Multi-modality advanced visualization, applications hosted on a viewing platform, image data flowing from an archive. Then show the Applications menu and say what goes there next.

Do not oversell it. Call it a study project that taught you the domain, be precise about what it does and does not do, and let the traceability matrix and the benchmark numbers do the arguing.

---

## 14. The ten-minute demo

Treat this as a deliverable, not an afterthought. Write it into `docs/demo-script.md`, rehearse it until it needs no notes, and time it.

### 14.1 Before you leave the house

- App published as a self-contained single file (`dotnet publish -c Release -r win-x64 --self-contained`) on a USB stick, so it runs with no .NET install.
- One curated series on the same stick, ~200 slices, thin enough to look good and small enough to load in seconds. Pick it in advance and always demo the same one.
- One deliberately broken series in a second folder, for the error-handling moment.
- The app already launched and warmed up if you get the chance. Cold JIT on first render is the ugliest it will ever look.
- A phone photo or screenshot of the 2×2 view, in case the machine refuses to cooperate at all.

### 14.2 Running order

| Time | Beat | What you say |
|---|---|---|
| 0:00–0:45 | Open the folder, volume loads | What DICOM gives you, and that slice order comes from ImagePositionPatient rather than InstanceNumber |
| 0:45–2:00 | 2×2 MPR appears, scroll the axial | There is one volume in memory, and each pane is a plane sampled out of it |
| 2:00–3:00 | Click a structure, crosshairs snap | The click becomes a patient-space point, and the other two panes reslice through it |
| 3:00–4:00 | Cycle window presets | The LUT, and why it is precomputed rather than per-pixel |
| 4:00–5:30 | Rotate to an oblique plane | Trilinear interpolation, and the millisecond cost of leaving the axis-aligned fast path |
| 5:30–6:30 | Measure a distance and an ROI | Measurements are in patient space; here is the phantom test that proves the number |
| 6:30–7:15 | Slab MIP, sweep the thickness | Ray marching, and why full volume rendering was cut |
| 7:15–8:00 | Open the broken series | Deliberate rejection with a clear message, and why rejecting beats guessing |
| 8:00–9:00 | Applications menu, Histogram app | The plugin contract, and what goes here next |
| 9:00–10:00 | Traceability matrix and `performance.md` on screen | How you knew it worked, and how you knew it was fast |

The last two beats are the ones that separate this from a student project. Do not let the demo run long and lose them. If you are over time at 8:00, skip the MIP.

### 14.3 Rehearsal targets

- Run it end to end at least three times, out loud, timed.
- Have someone interrupt you mid-demo with a question, then get back on script.
- Know exactly which frames drop and why, because someone will notice a stutter and ask.
- Prepare the sentence you say if it crashes. Something like: "That is the async load path — let me show you the code that handles it." Recovering calmly reads better than a demo that never breaks.

### 14.4 Things you must be able to answer about anything on screen

For every control, know: what it does, what code runs when it is used, what it costs in milliseconds, and what you would change about it. If any control fails that test during rehearsal, remove the control. Per §1.6, that is not a compromise — it is the design rule.

---

## Appendix A — Key DICOM tags reference

| Tag | Name | Notes |
|---|---|---|
| (0008,0060) | Modality | Expect `CT` |
| (0008,0018) | SOPInstanceUID | Unique per image |
| (0020,000D) | StudyInstanceUID | |
| (0020,000E) | SeriesInstanceUID | Group by this |
| (0020,0052) | FrameOfReferenceUID | Must match across the volume |
| (0020,0013) | InstanceNumber | Do **not** sort by this |
| (0020,0032) | ImagePositionPatient | Sort by projection of this onto slice normal |
| (0020,0037) | ImageOrientationPatient | 6 values: row cosines, then column cosines |
| (0028,0010) | Rows | |
| (0028,0011) | Columns | |
| (0028,0030) | PixelSpacing | Row spacing \ column spacing |
| (0018,0050) | SliceThickness | Nominal; not the same as spacing |
| (0018,0088) | SpacingBetweenSlices | Often absent; compute instead |
| (0028,0100) | BitsAllocated | Usually 16 |
| (0028,0101) | BitsStored | Often 12 for CT |
| (0028,0102) | HighBit | |
| (0028,0103) | PixelRepresentation | 0 unsigned, 1 signed |
| (0028,1052) | RescaleIntercept | Usually −1024 for CT |
| (0028,1053) | RescaleSlope | Usually 1 |
| (0028,1050) | WindowCenter | May be multi-valued |
| (0028,1051) | WindowWidth | May be multi-valued |
| (7FE0,0010) | PixelData | |

## Appendix B — Hounsfield unit reference

Useful for sanity-checking your loader and for choosing window presets.

| Tissue | Approx. HU |
|---|---|
| Air | −1000 |
| Lung parenchyma | −700 to −600 |
| Fat | −120 to −90 |
| Water | 0 (by definition) |
| Cerebrospinal fluid | ~15 |
| Blood | 30 to 45 |
| Muscle | 35 to 55 |
| Soft tissue | 100 to 300 |
| Cancellous bone | 300 to 400 |
| Calcification | 130+ (the threshold Phase 2 uses) |
| Cortical bone | 500 to 1900 |
| Metal implant | 2000+ |

If your loader reports air at 0 instead of −1000, you forgot the rescale intercept.

## Appendix C — Glossary

- **MPR** — Multiplanar reconstruction. Generating arbitrary-plane 2D images from a 3D volume.
- **MIP / MinIP** — Maximum / minimum intensity projection over a slab.
- **HU** — Hounsfield unit. Normalized CT attenuation; water is 0, air is −1000.
- **Window/level** — The contrast mapping from a HU range onto display grey levels. Level is the centre, window is the width.
- **PACS** — Picture Archiving and Communication System. The hospital's image archive.
- **DICOMweb** — RESTful DICOM services: QIDO-RS (query), WADO-RS (retrieve), STOW-RS (store).
- **LPS** — DICOM patient coordinate convention: +x Left, +y Posterior, +z Superior.
- **Frame of reference** — A shared spatial coordinate system; images sharing one can be spatially related.
- **Gantry tilt** — CT acquisition where the scan plane is not perpendicular to the table axis, producing a sheared volume.
