# Architecture

> **RESEARCH AND DEMONSTRATION USE ONLY — NOT A MEDICAL DEVICE. NOT FOR DIAGNOSTIC USE.**

Five libraries and one executable. The shape of the dependency graph is the whole design:
every rule below is enforced by something a compiler checks, not by a convention someone
has to remember.

## The layers

```
                        ┌───────────────────────────────┐
                        │      InterviewTrea.Core       │   net8.0
                        │  geometry · volumes ·         │   depends on nothing
                        │  reslicing · measurements     │
                        └───────────────────────────────┘
                          ▲          ▲          ▲      ▲
              ┌───────────┘          │          │      └─────────────┐
              │                      │          │                    │
┌─────────────┴──────┐  ┌────────────┴─────┐  ┌─┴──────────────────────────────┐
│ InterviewTrea      │  │ InterviewTrea    │  │ InterviewTrea.Applications     │
│        .Dicom      │  │      .Rendering  │  │            .Abstractions       │
│ fo-dicom lives     │  │ volume → byte[]  │  │ the plugin contract            │
│ here and nowhere   │  │ never            │  │ IClinicalApplication,          │
│ else               │  │ System.Windows.* │  │ IApplicationContext,           │
└─────────┬──────────┘  └────────┬─────────┘  │ IOverlayLayer                  │
          │                      │            └───────┬────────────────────────┘
          │                      │                    │
          │                      │            ┌───────┴────────────────────────┐
          │                      │            │ InterviewTrea.Applications     │
          │                      │            │            .Histogram          │
          │                      │            │ reference app (FR-507)         │
          │                      │            └───────┬────────────────────────┘
          │                      │                    │
          └──────────────┬───────┴────────────────────┘
                         ▼
        ┌────────────────────────────────────┐
        │        InterviewTrea.App           │   net8.0-windows, UseWPF
        │  views · view models · App.xaml.cs │   depends on everything
        │  the composition root              │   nothing depends on it
        └────────────────────────────────────┘
```

## Why the arrows point that way

**Core depends on nothing.** Every number this project claims — a distance in millimetres,
an ROI's mean Hounsfield value, where a plane cuts a volume — is computed in a library that
cannot reach a file, a window, or a DICOM tag. That is what makes those numbers testable
against values derived on paper rather than captured from a previous run.

**`net8.0`, not `net8.0-windows`.** NFR-301 says no DICOM, rendering or geometry code may
reference `System.Windows.*`. A project reference cannot stop someone writing
`using System.Windows;`, so the target framework does it instead: those types are not in
the compilation at all. `Directory.Build.props` sets it once and `InterviewTrea.App` is the
only project that overrides it.

**fo-dicom is contained.** It appears in `InterviewTrea.Dicom` and nowhere else. Every
other layer speaks in `Volume`, `VolumeMetadata` and `Point3D`, so replacing the parser
would be a change to one project.

**Rendering returns `byte[]`.** It fills a caller-supplied buffer of 8-bit grey and knows
nothing about bitmaps; the WPF layer wraps that buffer in a `WriteableBitmap` and calls
`WritePixels` once per frame. See [ADR-003](decisions/ADR-003.md).

**The plugin contract depends on Core only.** Everything crossing the seam is a domain
type — volumes, planes, patient millimetres, packed ARGB colours. An application never
learns what a viewport, a bitmap or a window is, which is what lets the shell change all
three without touching a plugin. See [ADR-004](decisions/ADR-004.md).

## Composition

`App.xaml.cs` is the only place anything is constructed. It builds a
`HostApplicationBuilder`, registers the three DICOM services, the series prompt, every
clinical application, the main view model and the window, and starts the host. There is no
service locator and no static singleton; a type that needs a collaborator takes it as a
constructor parameter, which is why the load pipeline could be exercised by a console probe
in Iteration 1, before there was a window to hide it.

Adding a clinical application is one line:

```csharp
services.AddSingleton<IClinicalApplication, HistogramApplication>();
```

## State

There are three pieces of shared state and they live in `MainViewModel`:

- **The volume**, loaded once per study.
- **The crosshair**, a patient-space `Point3D` rather than a slice index. That single
  decision is what makes FR-304's linked views a consequence of the model instead of a
  feature: a millimetre coordinate means the same thing in all four panes, and a rotated
  plane has no slice index at all.
- **The reslice frames**, one row/column axis pair per orientation. That is the entire
  oblique state — no angle, no accumulated rotation matrix, no history.

Everything a pane draws is derived from those three. A viewport view model holds the plane
it is currently showing and the text around its edge; the control holds the bitmap and the
one matrix that composes fit, zoom and pan.

## Threading, and NFR-204

NFR-204 asks that the UI thread never block for more than 50 ms during any interaction.
Three things hold that:

1. **Loading runs on the thread pool.** `LoadAsync` awaits two `Task.Run` passes — the
   header scan and the pixel decode — with the series prompt between them on the UI thread.
   Progress crosses back through `Progress<T>`, which marshals to the captured context, so
   no background code touches a bound property.
2. **Rendering is synchronous but bounded.** An axial slice is 2.4 ms and an oblique one
   1.9 ms measured (see [performance.md](performance.md)), so a render inside a mouse-move
   handler is two orders of magnitude inside the budget. The expensive case is the slab at
   14 ms, still well under.
3. **Nothing allocates per frame.** The bitmap, the pixel buffer and the window/level
   lookup table are reused, so a scroll or a window drag does not produce a collection.

The measured evidence is in `performance.md`; the interaction itself is verified by hand,
which is what NFR-204's own "manual; document your approach" asks for.

## What is deliberately not here

No volume rendering with transfer functions, no curved MPR, no PACS or DICOMweb
connectivity, no secondary-capture export, no multi-study comparison, no non-CT modality
support. These are the Phase 1 §1.4 non-goals, and cutting PACS in particular was a
decision about the demo: it would have introduced a container, a server process and a
network hop into a ten-minute window where none of it could pay off.
