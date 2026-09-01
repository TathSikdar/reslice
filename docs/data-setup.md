# Test data setup (DQ-1, DQ-2, DQ-3)

**No DICOM is committed to this repository.** `data/` is gitignored. Everything in the
test suite runs against synthetic datasets built in memory, so a clean clone passes
`dotnet test` with no downloads at all.

Real data is needed for one thing only: the Iteration 1 acceptance check in spec §10,
which asks the probe to load an actual series and report a plausible Hounsfield range.

## DQ-3 — the rule that has no exceptions

Do not add your own patient data, or anyone's, ever, for any reason. The public
collections below are already de-identified by the people who published them. Nothing
else goes in this repository or on this machine.

## Getting a series

1. Install the [NBIA Data Retriever](https://wiki.cancerimagingarchive.net/display/NBIA/Downloading+TCIA+Images).
2. Open a collection on [The Cancer Imaging Archive](https://www.cancerimagingarchive.net/)
   and download one CT study. **LIDC-IDRI** is the usual choice for this project: chest CT,
   large, well curated, and unambiguously public.
3. Unpack it under `data/`. The layout does not matter — the loader recurses, groups by
   SeriesInstanceUID, and offers every candidate series it finds.

## Running the probe

```
dotnet run --project tools/InterviewTrea.Probe -- data/<your study folder>
```

A successful load prints the §10 line and a rescale sanity check:

```
Loaded 342 slices, 512x512x342, spacing 0.68x0.68x1 mm, HU range -1024..2891
209.7 MB, series "CHEST W/O CONTRAST", modality CT
Air reads about -1000 HU, so the rescale looks right.
```

The last line is the one that matters. **If the minimum reads 0 rather than about −1000,
the RescaleIntercept (0028,1052) was not applied**, and every measurement taken from the
volume would be wrong while the image still looked like a normal chest.

A series that cannot be reconstructed prints the reason instead and exits non-zero:

```
Rejected (GantryTilt): The series was acquired with about 12 degrees of gantry tilt: ...
```

## Which series were actually used

| Collection | Patient ID | SeriesInstanceUID | Slices | Spacing (mm) | Scanner | Used for |
|---|---|---|---|---|---|---|
| LIDC-IDRI | LIDC-IDRI-0001 | `1.3.6.1.4.1.14519.5.2.1.6279.6001.179049373636438705059720603192` | 133 | 0.70 x 0.70 x 2.50 | GE LightSpeed Plus | Everyday viewing. Anisotropic, so it is the one that exercises FR-208. |
| LIDC-IDRI | LIDC-IDRI-0599 | `1.3.6.1.4.1.14519.5.2.1.6279.6001.139444426690868429919252698606` | 456 | 0.56 x 0.56 x 0.70 | Siemens Sensation 16 | FR-109 and NFR-102. Over 400 slices, near-isotropic, 228 MB. |

Both are `Creative Commons Attribution 3.0 Unported`. Retrieved directly from the NBIA
REST API rather than through the Data Retriever, which is a single request per series:

```
curl "https://services.cancerimagingarchive.net/nbia-api/services/v1/getImage?SeriesInstanceUID=<uid>" -o series.zip
```

The zip contains one `.dcm` per slice plus a `LICENSE` file. Unzip the whole thing into
`data/<name>/` and leave the licence where it is — the loader reports it as
`Skipped 1 file(s): not a DICOM file`, which is the DI-3 tolerance doing its job.

## What real data showed that synthetic data could not

- **Padding is not air.** LIDC-IDRI-0001 has a Hounsfield minimum of **-2048**, not -1000.
  GE writes -2048 outside the reconstruction circle, so the darkest thing in the volume is
  a padding value rather than air. Air inside the circle reads about -1024. Nothing is
  broken by this - both clip to black under any sensible window - but a region of interest
  that includes the corners of the field of view would be averaging a number that was
  never a measurement. `PixelPaddingValue` (0028,0120) is not read yet.
- **SeriesDescription is often absent.** LIDC-IDRI-0001 has none at all and the probe
  prints `(none)`. This is DI-3 in the wild, not a parse failure.
- **The window comes from the series, not from the presets.** Both series carry their own
  WindowWidth/WindowCenter - W1600/L-600 and W1500/L-500 - and neither is one of the five
  FR-305 presets, so the preset dropdown is correctly blank on load.
- **Spacing that differs on every axis is normal.** 0.70 x 0.70 x 2.50 mm is a 3.6:1
  anisotropy. On the 2.50 mm series the coronal reslice interpolates about 3.6 rows for
  every row of real data, which is what FR-208 is for and what a synthetic isotropic
  phantom can never demonstrate.

## Compressed transfer syntaxes

fo-dicom decodes uncompressed and RLE natively. LIDC-IDRI is mostly uncompressed, but if a
series fails to decode, the `fo-dicom.Codecs` package adds JPEG and JPEG-2000 support. It
is deliberately not referenced until something actually needs it.
