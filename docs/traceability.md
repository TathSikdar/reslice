# Traceability matrix

Every FR and NFR in `INTERVIEWTREA-PHASE1-VIEWER.md` gets a row. Rows are added in the
same change that satisfies the requirement, not retrofitted at the end.

A row marked `Manual` has no automated test and says how it was verified instead.
Empty and honest beats populated and fabricated.

| Req ID | Requirement (short) | Design element | Test(s) | Status |
|---|---|---|---|---|
| NFR-301 | No DICOM/rendering/geometry code references `System.Windows.*` | `Directory.Build.props` — all projects target plain `net8.0`, so WPF types are absent from the compilation. `App` (It. 2) is the only project permitted `net8.0-windows`. | Manual — confirmed `System.Windows.Point` fails to resolve in `InterviewTrea.Core` (CS0246). Note the `System.Windows` *namespace* root does exist in net8.0 via `System.Windows.Input.ICommand`; it is the WPF types that are unavailable. | Done |
| NFR-303 | CI builds and runs all tests on every push to `main` and every PR | `.github/workflows/ci.yml` — restore, `build -warnaserror`, `test` on `windows-latest`, SDK from `global.json` | Manual — the pipeline is its own evidence | Done |
| DQ-1 | Test data shall not be committed; `data/` gitignored | `.gitignore` | Manual — `data/` entry present | Done |
| NFR-101 | 512x512x400 volume under 300 MB managed heap | `Volume` — flat `short[]` of Hounsfield units, x fastest; `short` over `float` is what buys the budget | `AFullChestVolume_FitsTheMemoryBudget` — allocates the real array and asserts 209,715,200 bytes | Done |
| FR-206 (groundwork) | Oblique reslicing shall sample the volume by trilinear interpolation | `Volume.SampleTrilinear` / `SampleNearest`, in continuous voxel coordinates with patient-space overloads. Out of bounds returns `Volume.OutsideValue` (-1024) rather than throwing, because every oblique frame has corners outside the data and the sampler is a hot path. | `VolumeSamplingTests` — analytic expectations throughout: a 100 HU/voxel ramp reads exactly 50 at the midpoint; an `i + 10j + 100k` ramp pins all three axes independently. Mutation-verified against a swapped y/z stride, an inverted lerp weight, a half-voxel offset, and a missing far-face clamp. | Partial — the reslicer that consumes this arrives in Iteration 3 |
