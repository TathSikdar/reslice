# InterviewTrea

> **RESEARCH AND DEMONSTRATION USE ONLY — NOT A MEDICAL DEVICE. NOT FOR DIAGNOSTIC USE.**

An independent study project. **No affiliation with, endorsement by, or code derived from
any commercial vendor.** The name is a pun; nothing here is anyone's product.

## What this is

A Windows desktop CT visualization workstation in C#/WPF. It loads a DICOM series from a
local folder, reconstructs a 3D volume of Hounsfield units, and renders synchronized
axial, coronal and sagittal multiplanar reformats with slab projection, oblique
reslicing, and patient-space measurement. The interesting part is that the geometry,
interpolation and reslicing are written by hand rather than delegated to ITK or VTK —
that is the point of the exercise, not an oversight.

## Status

**All five iterations complete.** A folder of DICOM becomes a validated `Volume`, or a
clear typed rejection naming the rule that failed. Four viewports show axial, coronal,
sagittal and a slab projection of the same patient-space point; clicking in any of them
moves the other three, and dragging a crosshair arm rotates the reslice frame so the other
two views re-cut the volume obliquely and live. Distances and ROIs are measured in patient
millimetres, so anisotropic spacing and plane obliquity are inside the numbers rather than
applied as a correction afterwards. The viewer hosts pluggable clinical applications: one
line in the composition root adds one, and there is a reference application in the box.
All three NFR-200 performance targets are met — see
[docs/performance.md](docs/performance.md).

![The 2x2 MPR layout with linked crosshairs](docs/images/mpr-2x2.png)

*LIDC-IDRI-0599, 456 slices at 512 x 512, 0.56 x 0.56 x 0.70 mm, in the lung window the
series itself carries. Note the bottom-right pane: a 20 mm maximum-intensity slab resolves
the vascular tree as continuous branches where the thin axial above it shows the same
vessels as disconnected dots. Data from
[The Cancer Imaging Archive](https://doi.org/10.7937/K9/TCIA.2015.LO9QL9SX), de-identified
by its publishers and used under CC BY 3.0; see [Attribution](#attribution). No DICOM is
committed to this repository, and no patient identifier is displayed in any viewport
(DI-1).*

| Iteration | Goal | State |
|---|---|---|
| 1 | Folder of DICOM to validated `Volume` | Done |
| 2 | Single axial viewport, scroll, window/level | Done |
| 3 | 2x2 MPR with linked crosshairs | Done |
| 4 | Measurement and oblique reslicing | Done |
| 5 | Plugin platform and polish | Done |

The screenshot above is from Iteration 3 and shows the layout before the measurement
tools and the applications dock existed.

## Build and run

Requires the **.NET 8 SDK** (pinned in `global.json`).

```
dotnet build -warnaserror
dotnet test
```

```
dotnet run --project src/InterviewTrea.App
dotnet run --project src/InterviewTrea.App -- data/<your study folder>
```

The optional folder argument skips the dialog. It exists as a demo safeguard: on an
unfamiliar machine, clicking through a folder picker to a path you have not memorised is
the easiest way to lose thirty seconds of a ten-minute slot.

The Iteration 1 console probe exercises the same load pipeline with no window at all,
which is how the composition root got tested before there was a UI to hide it:

```
dotnet run --project tools/InterviewTrea.Probe -- data/<your study folder>
```

It prints what was loaded, or why the series was refused, and sanity-checks that air
reads about -1000 HU.

## Test data

**No DICOM is committed to this repository.** `data/` is gitignored and the test suite
runs entirely against datasets synthesised in memory, so a clean clone passes
`dotnet test` with no downloads.

Running the viewer needs a real study. **[docs/data-setup.md](docs/data-setup.md)** names
the two LIDC-IDRI series this project was verified against, by SeriesInstanceUID, and
gives the single `curl` that retrieves each one from the NBIA API.

Public collections are de-identified by their publishers. No other patient data goes
near this project, ever.

## Architecture

Dependencies point one way. The rule is enforced by target framework, not by convention:
every library targets plain `net8.0`, so WPF types are not merely discouraged in the
rendering and geometry code, they do not exist in the compilation.

**Controls.** Left-click or drag sets the crosshair and moves the other three views with
it; taking hold of a crosshair arm instead rotates the two planes that are not the one you
are pointing at, which is oblique reslicing. Wheel scrolls that view along its own normal,
by the volume's real spacing in that direction. Right-drag sets window and level,
middle-drag pans, Ctrl+wheel zooms about the cursor, Shift+wheel over the slab pane sets
its thickness, and double-click maximizes a pane or restores the grid.

Selecting a tool from the Measure dropdown gives the left button over to drawing — a
distance, an elliptical or a rectangular ROI — because one press cannot mean two things.
The Move tool takes hold of a measurement already drawn: near an end it drags that end,
anywhere else it slides the whole shape. Hovering a measurement thickens it and Delete
removes that one; Clear removes them all; Reset returns the frames to the anatomical
planes and the crosshair to the middle of the volume.

Visible chrome is the regulatory banner, Open Folder, the Applications menu, Reset, the
Measure dropdown, Clear, the two exports, and the window and slab dropdowns. Everything
else is a gesture, and every value a gesture changes is shown in the pane's own overlay.

```
InterviewTrea.Core            depends on nothing
        ^
        |-- InterviewTrea.Dicom          Core only. fo-dicom appears here and nowhere else.
        |-- InterviewTrea.Rendering      Core only. Produces byte[]. Never System.Windows.*
        |-- InterviewTrea.Applications.Abstractions   Core only. The plugin contract.
                ^
                |-- InterviewTrea.Applications.Histogram  Abstractions only. Reference app.
                |
                |-- InterviewTrea.App    depends on everything. Nothing depends on it.
```

`InterviewTrea.App` is the only project targeting `net8.0-windows`; every other one
targets plain `net8.0`, which is what makes the rule enforceable rather than aspirational.
The reference application is the proof: it computes a histogram of the volume and draws a
marker on every plane without referencing WPF, fo-dicom, a viewport or a bitmap. See
[docs/architecture.md](docs/architecture.md).

Volume storage is a flat `short[]` of Hounsfield units with x varying fastest. A
512x512x400 chest study is 200 MB, which is what buys the memory budget; `float` would
double it for no gain, since CT is integer data.

## Testing

Coverage is targeted at the domain and rendering layers — geometry, volume sampling,
DICOM parsing, validation, reslicing — where a defect is silent and the return on a test
is highest. **The view layer is deliberately left to manual verification.** XAML bindings
and view construction are not unit tested, because that is where the return on test
investment collapses and chasing the number would produce tests that assert the
framework works.

Numeric tests use analytically derived expected values, never snapshots or golden files.
A trilinear sample at the midpoint of a 0-to-1000 HU ramp is asserted to be 500 because
that is what interpolation means, not because that is what the code returned the first
time it ran.

The standing verification discipline is mutation: a green test proves nothing until a
deliberate break makes it red. Where a mutation survived, that is recorded in the AI
assistance log along with the gap it exposed.

## Documents

- [Phase 1 specification](docs/INTERVIEWTREA-PHASE1-VIEWER.md) — the viewer platform
- [Phase 2 specification](docs/INTERVIEWTREA-PHASE2-CALCIUM-SCORING.md) — the calcium scoring plugin
- [Traceability matrix](docs/traceability.md) — every requirement, its design element, its test
- [Architecture decisions](docs/decisions/) — one ADR per genuinely contested call
- [AI assistance log](docs/ai-assistance-log.md) — including where the assistant was wrong and was caught
- [Architecture](docs/architecture.md) — the layer diagram and the rules that hold it up
- [Test data setup](docs/data-setup.md)
- [Performance](docs/performance.md) — the NFR-200 targets, measured, with before and after

## Known limitations

Stated plainly, because a vague limitations section is worse than none.

- **A tilted gantry is rejected, not corrected.** Correcting it means resampling onto a
  sheared grid, which is out of scope. The tilt tolerance is roughly 2.5 degrees and has
  only been exercised against synthetic tilt; it wants tuning against a real tilted
  series. See [ADR-001](docs/decisions/ADR-001.md).
- **Non-uniform slice spacing is rejected rather than interpolated.** A study with a
  dropped slice or a duplicate will not load. The message says which.
- **CT only, single-frame only, 16-bit pixel data only.** Enhanced/multi-frame objects
  are refused explicitly rather than mis-parsed as single slices.
- **Compressed transfer syntaxes beyond RLE are not decoded.** fo-dicom handles
  uncompressed and RLE natively; JPEG and JPEG-2000 would need `fo-dicom.Codecs`, which
  is deliberately not referenced until a real series needs it.
- **Peak memory sits at about 2x the volume on a large series, which is the limit rather
  than comfortably inside it.** Above a 77.7 MB runtime baseline the working set is 1.01x
  the volume at 66.5 MB but 1.8x to 2.0x at 228 MB. The 1.01x figure shows there is no
  second copy of the volume; the spread at the top end is transient per-slice pixel
  buffers outrunning collection. Reusing one decode buffer would fix it and has not been
  done.
- **`PixelPaddingValue` (0028,0120) is not read.** GE writes -2048 outside the
  reconstruction circle, so the volume's minimum is a padding value rather than air. It
  clips to black under any window, but an ROI covering the corners of the field of view
  would average a number that was never a measurement. This matters for Phase 2, not
  for viewing.
- **A plugin ships a view model, not a view.** The shell finds a tool panel's control by
  type through a data template it owns, so an application cannot yet bring its own. Making
  that possible means a plugin shipping a WPF resource dictionary, which is a decision
  worth taking when there is a second application to justify it.
- **One application runs at a time.** Two of them contributing tool panels to the same
  dock is a layout question with no obvious answer, and nothing in either phase needs it.
- **An ROI has grab handles on two corners, not four.** They are the two points the drag
  that created it passed through. The other two are more arithmetic for little gain.
- **Overlays draw polylines and text and nothing else.** Every kind added to that contract
  is a kind the shell must render for every application forever, so the bar is what a
  clinical overlay actually consists of: an outline and a label.
- **One optimization is missing an explanation, not a measurement.** Removing a duplicated
  bounds test from the slab loop made it 43% slower, reproducibly, and neither of the two
  hypotheses tested accounts for it. It was reverted and the measurement recorded rather
  than a guess. See [docs/performance.md](docs/performance.md).
- No PACS or DICOMweb connectivity, no volume rendering, no curved MPR, no multi-study
  comparison, no non-CT modalities. These are deliberate non-goals, not gaps.

## Attribution

The screenshots and the acceptance runs use the **LIDC-IDRI** collection from The Cancer
Imaging Archive, de-identified by its publishers and licensed
[CC BY 3.0](https://creativecommons.org/licenses/by/3.0/). No DICOM is redistributed here.

> Armato SG III, McLennan G, Bidaut L, et al. *Data From LIDC-IDRI*. The Cancer Imaging
> Archive, 2015. https://doi.org/10.7937/K9/TCIA.2015.LO9QL9SX
>
> Armato SG III, McLennan G, Bidaut L, et al. "The Lung Image Database Consortium (LIDC)
> and Image Database Resource Initiative (IDRI): A completed reference database of lung
> nodules on CT scans." *Medical Physics* 38(2): 915-931, 2011.
>
> Clark K, Vendt B, Smith K, et al. "The Cancer Imaging Archive (TCIA): Maintaining and
> Operating a Public Information Repository." *Journal of Digital Imaging* 26(6):
> 1045-1057, 2013.
