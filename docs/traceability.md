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
