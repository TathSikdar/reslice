# InterviewTrea

A Windows desktop CT visualization workstation in C#/WPF. Loads a DICOM series, reconstructs a 3D volume, renders synchronized axial/coronal/sagittal MPR plus slab MIP, supports oblique reslicing and patient-space measurements, and hosts pluggable clinical applications.

Independent study project. No affiliation with, endorsement by, or code derived from any commercial vendor.

**RESEARCH AND DEMONSTRATION USE ONLY — NOT A MEDICAL DEVICE. NOT FOR DIAGNOSTIC USE.**

## Specifications

- `docs/INTERVIEWTREA-PHASE1-VIEWER.md` — the viewer platform. Authoritative for Phase 1.
- `docs/INTERVIEWTREA-PHASE2-CALCIUM-SCORING.md` — the calcium scoring plugin. Do not start until Phase 1 is complete and its tests are green.

Requirements are numbered (FR-101, NFR-201, RQ-1, DI-3, and so on). Cite the ID in commit messages and in PR descriptions. If a task doesn't map to a requirement, say so before writing code — either we add the requirement or we don't build the thing.

If the spec and I disagree, tell me. Don't silently follow either one.

## How I want you to work with me

I am learning this domain as it is built, and I have to defend every line of it in a live demo. That constraint outranks speed.

- **Explain the approach before implementing.** For anything with real logic — geometry, interpolation, rendering loops, algorithms — describe the plan in a few sentences and wait for me to say go. Boilerplate, wiring, and test scaffolding don't need this.
- **Keep diffs reviewable.** Prefer several small changes I can read over one large one I will skim. If a change is going to exceed roughly 200 lines, stop and propose splitting it.
- **Comment the why, not the what.** `// clamp to volume bounds` is noise. `// slice normal is row × column cosines; DICOM row/column order matters here` is worth having.
- **After each meaningful unit of work, give me a short explanation** of what the code does and why it's shaped that way — five or six sentences, no bullet lists, written so I could repeat it out loud. This is not documentation, it's for me.
- **Push back when I'm wrong.** If I ask for something that violates the architecture, contradicts the spec, or is a bad idea, say so directly before doing it.
- **Never add a visible UI control without asking first.** Every control has to be explainable in a ten-minute demo. See Phase 1 §1.6.

Assume I do not yet know DICOM conventions or WPF rendering internals. Explain domain terminology the first time it appears; don't re-explain it afterwards.

## Architecture rules

Non-negotiable. These are enforced by project references and I want them to stay that way.

- `InterviewTrea.Core` depends on nothing. Pure domain — geometry, volumes, measurements.
- `InterviewTrea.Dicom` depends on Core only. **fo-dicom appears in this project and nowhere else.**
- `InterviewTrea.Rendering` depends on Core only. Produces `byte[]`. **Must never reference `System.Windows.*`.** The WPF layer wraps the buffer in a `WriteableBitmap`.
- `InterviewTrea.Applications.Abstractions` depends on Core only. The plugin contract.
- `InterviewTrea.App` depends on everything. Nothing depends on it.

Other standing rules:

- Composition root is `App.xaml.cs`. Register everything through `Microsoft.Extensions.DependencyInjection`. No service locator, no static singletons.
- MVVM via `CommunityToolkit.Mvvm`. No code-behind logic beyond view concerns.
- No ITK, VTK, or Cornerstone. The interpolation and reslicing are written by hand on purpose — they are the point of the project.
- Volume storage is a flat `short[]` of Hounsfield units, x fastest. Do not switch to `float[]` or `short[,,]` without discussing it.

## Testing rules

- Numeric code needs a phantom test with an **analytically derived** expected value. Not a snapshot, not a golden file, not "whatever it returned the first time."
- Phantom generators live in `tests/InterviewTrea.TestData`.
- Build synthetic `DicomDataset` objects in memory for DICOM tests. Do not commit DICOM files.
- Don't test XAML bindings or view construction. High coverage on Core and Rendering, manual verification of the view layer, and we say so in the README.
- Benchmarks live in `tests/InterviewTrea.Benchmarks` (BenchmarkDotNet), run in Release, excluded from CI. Capture a baseline before optimizing anything.

## DICOM gotchas

Recurring traps. Get these wrong and the image looks plausible while being quietly incorrect.

- **Sort slices by projection of `ImagePositionPatient` (0020,0032) onto the slice normal.** Never by `InstanceNumber` — it is not reliable across manufacturers.
- **Compute slice spacing from successive positions.** `SpacingBetweenSlices` (0018,0088) is often absent or wrong, and `SliceThickness` (0018,0050) is not the same quantity.
- **Convert to HU on load**: `HU = raw * RescaleSlope + RescaleIntercept`. Respect `PixelRepresentation` for signedness. Air should read about −1000; if it reads 0, the intercept was missed.
- **Slice normal is the cross product** of the two direction cosines in `ImageOrientationPatient` (0020,0037) — row cosines first, then column.
- **Anisotropic voxels are normal.** 0.7 × 0.7 × 3.0 mm is typical. Coronal and sagittal views must correct aspect ratio or the patient comes out squashed.
- **Reject rather than guess**: mismatched `FrameOfReferenceUID`, non-uniform spacing, or gantry tilt get a clear error message, not a best-effort resample.
- **Public data is de-identified.** `PatientName` reads like `LIDC-IDRI-0142`, and demographic tags are frequently empty. Never display patient identifiers in the viewport overlay, and never throw on a missing optional tag.

## Visual design

Dark reading-room aesthetic. The interface should disappear so the image doesn't have to compete with it.

- **Image area is true black** (`#000000`). Any lighter and perceived contrast in the greyscale drops.
- Application chrome `#121212`, panels `#1A1A1A`, borders `#2A2A2A` at hairline width. No gradients, no drop shadows, no rounded corners beyond 2px.
- Text `#E0E0E0` primary, `#8A8A8A` secondary. Overlays inside viewports at ~70% opacity so they never fight the image.
- One accent colour for interactive state: amber `#E0A030`. Use it sparingly.
- Crosshair colours identify the plane and stay consistent everywhere: axial `#E05050`, coronal `#50C070`, sagittal `#5090E0`.
- **Monospace for all numeric readouts** (Cascadia Mono or Consolas). Proportional digits jitter as values change during scroll and it looks cheap. Segoe UI for everything else.
- Measurement annotations in the accent colour, with a thin dark outline so they stay legible over both bright bone and black air.

Implementation: every brush, size, and control style lives in `InterviewTrea.App/Themes/Dark.xaml`. **Views reference resources only — no inline colours or sizes, ever.** If you need a new value, add it to the dictionary.

## Conventions

- Conventional commits, with the requirement ID: `feat(dicom): sort slices by IPP projection (FR-103)`.
- One ADR per genuinely contested decision, in `docs/decisions/ADR-NNN.md`. Context, Decision, Consequences. Half a page.
- Append to `docs/traceability.md` in the same change that satisfies a requirement, not later.
- Log notable AI-assistance moments in `docs/ai-assistance-log.md` — especially where you were wrong and I caught it. That file is an interview artifact, so honesty in it is the whole value.
- `data/` is gitignored. Never commit DICOM.

## Commands

```
dotnet build -warnaserror
dotnet test
dotnet test --collect:"XPlat Code Coverage"
dotnet format
dotnet run -c Release --project tests/InterviewTrea.Benchmarks
```

## Don't

- Don't build anything in the Phase 1 §1.4 non-goals list. Volume rendering with transfer functions, curved MPR, PACS/DICOMweb connectivity, secondary-capture export, multi-study comparison, and non-CT modality support are all deliberately out.
- Don't add NuGet packages without asking.
- Don't refactor code I haven't reviewed yet.
- Don't write a "quick fix" that hides a geometry bug. If a number looks wrong, find out why.
