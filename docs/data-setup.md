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

Fill this in once a real study has been loaded, so the acceptance check is reproducible.
It is deliberately empty rather than invented.

| Collection | Patient ID | StudyInstanceUID | SeriesInstanceUID | Slices | Spacing (mm) | Notes |
|---|---|---|---|---|---|---|
| _(not yet run against real data)_ | | | | | | |

## Compressed transfer syntaxes

fo-dicom decodes uncompressed and RLE natively. LIDC-IDRI is mostly uncompressed, but if a
series fails to decode, the `fo-dicom.Codecs` package adds JPEG and JPEG-2000 support. It
is deliberately not referenced until something actually needs it.
