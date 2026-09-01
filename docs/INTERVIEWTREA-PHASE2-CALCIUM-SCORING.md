# InterviewTrea — Coronary Calcium Scoring

**Phase 2 Specification: Clinical Application Plugin**

> Prerequisite: Phase 1 complete and merged, including FR-500 (the plugin host) and the reference Histogram application. This document assumes `IClinicalApplication`, `IApplicationContext`, and `IOverlayLayer` already exist and are proven.

---

## 0. Document purpose

This is the requirements and design specification for a coronary artery calcium scoring application that docks into the InterviewTrea viewer as a plugin.

Everything in Phase 1 still governs. In particular **§1.6 of the Phase 1 document — the ten-minute demo principle — applies unchanged**, and it is the reason several obvious features are cut below.

Requirement IDs continue the Phase 1 scheme and append to the same `docs/traceability.md`. They do not restart.

---

## 1. Project overview

### 1.1 What it is

A clinical application, hosted inside the viewer, that computes an Agatston coronary artery calcium score from a non-contrast cardiac CT. The user works the same semi-automated workflow a technologist uses on a real scoring platform: the software finds every candidate calcification above the density threshold, the user clicks the ones that belong to a coronary artery and assigns them to a vessel, and the application scores only the assigned lesions and produces a report.

### 1.2 Why this, and why as a plugin

CT VScore is not a standalone product — it is an application that runs inside Vitrea Advanced Visualization. Building it as a plugin, on a host you already built, is the part of this project that demonstrates you read the product architecture rather than the feature list. The one-line DI registration is the payoff for all of Phase 1's FR-500 work.

The algorithm is also the best pure computer science on the whole project. Connected-component labelling over a 3D grid with union-find, a scoring rule defined by a published standard, and a correctness argument you can prove analytically against a phantom. That combination — a real algorithm plus a spec plus a proof — is close to what verification of medical device software actually looks like.

### 1.3 What "done" looks like

The user opens a cardiac CT in the viewer, picks "Calcium scoring" from the Applications menu, and a tool panel appears. Candidate calcifications are outlined on the axial view. The user clicks four or five of them, assigning each to LM, LAD, LCx, or RCA. Per-vessel and total Agatston scores update live. A summary panel shows the total, the reference risk category, and the calcium volume. Exporting produces a CSV and a PNG carrying the disclaimer.

And when asked "how do you know that number is right", you open a test that scores an analytic phantom whose Agatston score you computed by hand, and a table comparing your output against the published reference scores for ten real cases.

### 1.4 Explicit non-goals

| Not building | Why |
|---|---|
| Automatic coronary artery segmentation | Weeks of work and the real difficulty of the product. The manual assignment step is the authentic legacy workflow, not a shortcut |
| Any machine learning or AI inference | Thresholding and connected components only. Nothing that needs a model file or a runtime |
| Contrast-enhanced CT or CT angiography | The 130 HU threshold is defined for non-contrast acquisitions and is meaningless with contrast |
| MESA percentile ranking by age, sex, and race | De-identified research data has shifted or absent demographics. Reporting a percentile from unreliable inputs would be worse than reporting nothing — see §6.4 |
| Calcium mass score | Requires a calibration phantom in the scan. Not available in public data |
| DICOM Structured Report output or STOW-RS write-back | Deferred here from Phase 1, and ruled out again by the ten-minute demo principle: it needs a server. See §11 |
| Plaque characterisation, stenosis grading, vessel centrelines | Different products entirely |

### 1.5 Regulatory posture

Sharper than Phase 1, because this one produces a number that looks clinical.

- **RQ-5**: The tool panel shall display, adjacent to the score, the text `Research use only. Not a diagnostic result.`
- **RQ-6**: The risk category shall be labelled as a reference band from published thresholds, never as a patient risk assessment or a diagnosis.
- **RQ-7**: Every exported artifact (CSV, PNG, on-screen report) shall carry the RQ-1 disclaimer.
- **RQ-8**: The application shall not display a percentile, a risk probability, or any recommendation.

Say this in the interview and mean it: the moment software outputs a number a clinician might act on, its verification burden changes completely. That is why this phase has a phantom test suite and a validation table, and it is the honest reason the scope is this narrow.

---

## 2. Data

This is the one hard dependency in Phase 2. **Start it before you write any code.**

### 2.1 Primary: Stanford AIMI COCA

The COCA collection (Coronary Calcium and chest CTs) is gated non-contrast cardiac CT with per-lesion calcium annotations and reference Agatston scores. It is the only readily available public dataset that gives you *ground truth to validate against*, which is what makes the correctness argument in §8 possible.

- Available through the Stanford AIMI shared datasets programme.
- Requires a free account and agreement to a research use agreement. Approval is not instant.
- **Register on day one.** If it falls through, §2.3 is your fallback and it costs you the validation table.

### 2.2 What a scoreable series looks like

Not every CT can be scored. The application must check and refuse politely.

- Non-contrast. Contrast-enhanced series will light up like a Christmas tree at 130 HU.
- ECG-gated cardiac acquisition, covering the heart.
- Slice thickness of 3 mm is the standard the Agatston score was defined against. Thinner slices are scoreable with a thickness correction (see FR-604) but the score drifts.
- 120 kVp. Low-kVp scans inflate HU and therefore inflate the score. You cannot correct for this; detect it and warn.

### 2.3 Fallback if COCA does not come through

Use any non-contrast chest CT from TCIA. Coronary calcification is common enough in an older population that you will find scoreable cases. What you lose is ground truth — you can still prove the *algorithm* is correct with phantoms (§8.1), but you cannot show agreement with a reference (§8.2).

If this happens, say so plainly in the README and the interview. "I validated the scoring engine analytically against phantoms but could not obtain reference-scored clinical data in time" is a perfectly respectable sentence. Inventing a validation table would not be.

### 2.4 Curated demo case

Pick one series, in advance, that scores somewhere in the 100–400 range with four or five clearly separated lesions across at least two vessels. Rehearse on that case and only that case. A demo case with a score of zero has nothing to show, and one with forty confluent lesions is unclickable in the time available.

---

## 3. The Agatston score

Implement to this specification. Every number below is part of the published standard, not a choice you get to make.

### 3.1 The rule

1. Consider only voxels with **HU ≥ 130**.
2. Work **slice by slice, in 2D**. Find connected regions of above-threshold pixels within each slice.
3. Discard any region with **area < 1 mm²**.
4. For each surviving region, find its **peak HU** and take the density weight:

| Peak HU in region | Weight |
|---|---|
| 130 – 199 | 1 |
| 200 – 299 | 2 |
| 300 – 399 | 3 |
| ≥ 400 | 4 |

5. Region score = **area in mm² × weight**.
6. Total = sum of all region scores across all slices.

### 3.2 Why 2D and not 3D

This is the detail that makes you sound like you read the source rather than a blog post. Agatston defined the score in 1990 on 3 mm non-overlapping electron beam CT slices, and the rule operates per slice by construction. A 3D connected-component score is a different quantity and will not agree with published values.

So: **score in 2D, group in 3D.** The 2D pass produces the number. A separate 3D pass links slice-level regions into anatomical lesions purely so the user can click one lesion and assign the whole thing to a vessel. Keeping those two passes conceptually separate is the correct design and a genuinely good thing to explain.

### 3.3 Two parameters you must derive, not hardcode

**Minimum region size in pixels.** The 1 mm² floor is an area, not a pixel count. At 0.68 mm pixel spacing one pixel is 0.46 mm², so the floor is about 2.2 pixels and the practical rule is ≥3 contiguous pixels. At different spacing it is a different count. Compute it from `PixelSpacing` at load time.

**Slice thickness correction.** The score assumes 3 mm slices. For other thicknesses, scale each region's contribution by `sliceThickness / 3.0`. Without this, a 0.625 mm acquisition produces a score roughly five times too high. Document it, test it, and mention it — it is exactly the kind of unit-correctness trap that separates code that runs from code that is right.

### 3.4 Connectivity, and why scores differ between vendors

You must choose 4-connectivity or 8-connectivity for the 2D labelling. Both appear in the literature. 8-connectivity merges diagonally touching pixels into one region, producing fewer and larger regions, which changes both the area filter outcome and the peak-HU weight assignment. The resulting scores differ by a few percent.

Pick one, write ADR-006 explaining the choice, and make it a named constant. This is one of the real reasons calcium scores are not interchangeable across scoring platforms, and being able to say that unprompted is worth more than another feature.

### 3.5 Reference categories

Display the band, labelled per RQ-6.

| Total Agatston | Band |
|---|---|
| 0 | No detectable calcification |
| 1 – 10 | Minimal |
| 11 – 100 | Mild |
| 101 – 400 | Moderate |
| > 400 | Extensive |

### 3.6 Calcium volume score

Cheap to add and worth having: total volume of above-threshold voxels within assigned lesions, in mm³, computed as voxel count × voxel volume. Report it alongside the Agatston score. It gives you a second number to sanity-check the first against.

---

## 4. Architecture

### 4.1 Project layout

Two new projects. Note the split — the scoring engine has no UI dependency and no plugin dependency, so it can be tested standalone.

```
src/
├── InterviewTrea.Scoring/                    # Pure algorithm. Depends on Core only.
│   ├── Thresholding.cs
│   ├── ConnectedComponents2D.cs              # union-find, per slice
│   ├── LesionGrouper3D.cs                    # links 2D regions into 3D lesions
│   ├── AgatstonCalculator.cs
│   ├── ScoringParameters.cs                  # threshold, min area, connectivity, thickness
│   └── Models/                               # Region2D, Lesion3D, VesselScore, StudyScore
│
└── InterviewTrea.Applications.CalciumScoring/    # The plugin.
    ├── CalciumScoringApplication.cs          # IClinicalApplication
    ├── CalciumScoringSession.cs              # IApplicationSession
    ├── ViewModels/
    ├── Views/                                # Tool panel
    └── Overlays/CandidateOverlayLayer.cs     # IOverlayLayer

tests/
├── InterviewTrea.Scoring.Tests/
└── InterviewTrea.Scoring.Validation/         # Against COCA reference scores
```

### 4.2 Registration

The entire integration into the host:

```csharp
services.AddSingleton<IClinicalApplication, CalciumScoringApplication>();
```

One line. If it turns out to need more than that, the Phase 1 contract was wrong and it is worth understanding why before you patch around it.

### 4.3 Read-only context

- **FR-901**: The application shall not mutate the volume. `IApplicationContext.Volume` is read-only and the scoring engine works on copies of any derived buffer it needs.
- **FR-902**: Disposing the session shall remove all overlays and free the candidate label map.
- **FR-903**: The viewer shall remain fully functional — scrolling, windowing, measuring — while the application is active.

FR-903 matters for the demo. A plugin that takes over the window is a modal dialog with extra steps, not a hosted application.

### 4.4 Applicability check

`CanRun` should return false, with a stated reason, when the series is not scoreable:

- Modality is not CT
- Slice thickness exceeds 3.5 mm
- Series description or protocol suggests contrast (look for "contrast", "angio", "CTA", "arterial" — imperfect, and say so)
- KVP tag (0018,0060) is present and not 120

Refusing gracefully on an inappropriate series is a demo beat in its own right. See §10.

---

## 5. Algorithm design

### 5.1 Pipeline

```
Volume (HU)
   │
   ├─ threshold at 130 HU                         → binary mask
   │
   ├─ per slice: 2D connected components          → List<Region2D>
   │     union-find, chosen connectivity
   │
   ├─ filter: area < 1 mm² discarded              → candidate regions
   │
   ├─ link regions across slices                  → List<Lesion3D>
   │     overlap in-plane between adjacent slices
   │
   ├─ user assigns lesions to vessels             → assigned lesions only
   │
   └─ Agatston: Σ (area × weight × thickness/3)   → per-vessel + total
```

### 5.2 Connected components with union-find

Two-pass labelling. First pass scans the slice in raster order; for each above-threshold pixel it looks at already-visited neighbours, takes the smallest existing label or allocates a new one, and unions the neighbour labels together. Second pass resolves every label to its root and accumulates per-region area and peak HU.

Union-find with path compression and union by rank gives you near-constant amortised time per operation. On a 512 × 512 slice with a few hundred above-threshold pixels this is trivially fast — the whole volume labels in well under a second — but implementing it properly rather than flood-filling is the point. It is also the single most likely thing you will be asked to whiteboard.

**Have ready:** what union-find is, why path compression matters, what the inverse Ackermann bound means informally, and why you chose it over BFS flood fill. The honest answer to the last one is that for this data size either works and you picked union-find because the two-pass structure suits raster scanning — do not oversell it.

### 5.3 3D lesion grouping

Simpler than it sounds. Two regions on adjacent slices belong to the same lesion if their in-plane bounding boxes overlap and any pixel position is shared. Run a second union-find, this time over the set of 2D regions rather than pixels.

Remember this affects only labelling and clicking. It must not touch the score.

### 5.4 Performance

- **NFR-401**: Full threshold, label, filter, and group pass over a 512 × 512 × 300 volume shall complete in under 2 seconds.
- **NFR-402**: Assigning a lesion to a vessel shall update all displayed scores in under 50 ms.
- **NFR-403**: Overlay rendering shall not reduce viewport frame rate below the Phase 1 NFR-200 targets.

NFR-402 is the one that matters for feel. Recompute incrementally — each lesion's score is fixed once labelled, so assignment is a sum over a changed set, not a re-run of the pipeline.

---

## 6. Functional requirements

### FR-600 — Scoring engine

| ID | Requirement |
|---|---|
| FR-601 | The engine shall identify candidate regions as 2D connected components of voxels with HU ≥ 130, within a single slice. |
| FR-602 | The engine shall discard regions with area below 1 mm², where the pixel-count threshold is derived from PixelSpacing at runtime. |
| FR-603 | The engine shall assign a density weight from peak HU per §3.1 and compute region score as area × weight. |
| FR-604 | The engine shall scale each region's contribution by `sliceThickness / 3.0`. |
| FR-605 | The engine shall use a single, named, documented connectivity setting for 2D labelling. |
| FR-606 | The engine shall group 2D regions into 3D lesions by inter-slice pixel overlap, without affecting the score. |
| FR-607 | The engine shall compute per-vessel subtotals for LM, LAD, LCx, and RCA, and a total across all assigned lesions. |
| FR-608 | The engine shall compute calcium volume in mm³ over assigned lesions. |
| FR-609 | The engine shall expose all parameters (threshold, minimum area, connectivity, reference thickness) through a `ScoringParameters` object with standard values as defaults. |
| FR-610 | The engine shall have no dependency on WPF, on the plugin contract, or on fo-dicom. |

FR-609 exists so you can demonstrate parameter sensitivity in the interview: raise the threshold to 150 and watch the score fall. It shows the number is a construct of a convention, not a physical measurement.

### FR-700 — Workflow and interaction

| ID | Requirement |
|---|---|
| FR-701 | The application shall appear in the Applications menu and start from it. |
| FR-702 | On start, the application shall run the pipeline and outline all candidate regions on the axial viewport. |
| FR-703 | Unassigned candidates shall be outlined in a neutral colour; assigned lesions shall take their vessel's colour. |
| FR-704 | Clicking a candidate shall select its whole 3D lesion, not the single slice region. |
| FR-705 | The tool panel shall offer LM, LAD, LCx, RCA, and Unassign for the selected lesion. |
| FR-706 | The tool panel shall show a live table of per-vessel scores, total score, band, and calcium volume. |
| FR-707 | Selecting a lesion shall move the viewer crosshairs to its centroid, and the other viewports shall follow. |
| FR-708 | The panel shall list assigned lesions with vessel, slice range, area, peak HU, and score, and allow selecting one to jump to it. |
| FR-709 | The application shall support clearing all assignments. |
| FR-710 | The application shall refuse an unscoreable series with a specific stated reason per §4.4. |

FR-707 is a small thing that makes the demo feel like a real product. Click a lesion, all three planes fly to it.

### FR-800 — Reporting

| ID | Requirement |
|---|---|
| FR-801 | The application shall render an on-screen summary: per-vessel scores, total, band, volume, and the parameters used. |
| FR-802 | The summary shall state the scoring parameters explicitly, so the number is reproducible. |
| FR-803 | The application shall export the lesion table to CSV. |
| FR-804 | The application shall export the summary as a PNG carrying the RQ-1 disclaimer. |
| FR-805 | No export shall omit the disclaimer or the parameters. |

FR-802 is the requirement to be proud of. A calcium score without its acquisition and processing parameters is not a reproducible measurement, and printing them is what a real reporting tool does.

### 6.4 On percentiles

MESA percentile ranking by age, sex, and race is the obvious next feature and it is deliberately absent (RQ-8). The reason is data integrity: de-identified research data has shifted dates and often missing or unreliable demographics, so any percentile you computed would be built on inputs you cannot trust.

Have that answer ready, because someone will ask why it is not there. "I could compute it, but I could not defend the inputs, so I reported the absolute score and the band instead" is a much better answer than a percentile you cannot justify.

---

## 7. User interface

Keep it to one panel. The ten-minute principle applies.

```
┌─ Calcium scoring ─────────────┐
│ Research use only.            │
│ Not a diagnostic result.      │
├───────────────────────────────┤
│ Candidates found:  47         │
│ Assigned:           6         │
├───────────────────────────────┤
│ Selected lesion               │
│   Slices 88–94                │
│   Peak 412 HU · 24.6 mm²      │
│   [ LM ][ LAD ][ LCx ][ RCA ] │
│   [ Unassign ]                │
├───────────────────────────────┤
│ LM                      0.0   │
│ LAD                    88.4   │
│ LCx                    12.1   │
│ RCA                    64.9   │
│ ─────────────────────────     │
│ Total                 165.4   │
│ Band                   Moderate│
│ Volume              142.7 mm³ │
├───────────────────────────────┤
│ Threshold 130 HU · 8-conn     │
│ Slice 3.0 mm · min 1.0 mm²    │
├───────────────────────────────┤
│ [ Clear ] [ CSV ] [ PNG ]     │
└───────────────────────────────┘
```

Vessel colours should be distinguishable at a glance and consistent between the overlay and the table. Do not rely on colour alone — the lesion list carries the vessel name in text as well.

---

## 8. Testing and validation

This section is the reason Phase 2 is worth building. Two independent arguments for correctness.

### 8.1 Analytic phantoms — proves the algorithm

Extend `InterviewTrea.TestData` with calcium phantoms whose Agatston score you can compute by hand.

| Phantom | Construction | Expected |
|---|---|---|
| `SingleDiscPhantom` | One disc, radius 3 mm, 250 HU, on 3 slices, 3 mm thickness | area 28.27 mm² × weight 2 × 3 slices = 169.6 |
| `SubThresholdPhantom` | Disc at 125 HU | 0 — below the 130 HU threshold |
| `TinyLesionPhantom` | Region of 0.8 mm² at 400 HU | 0 — below the area floor |
| `WeightBoundaryPhantom` | Discs with peak exactly 199, 200, 299, 300, 399, 400 HU | Weights 1, 2, 2, 3, 3, 4 |
| `TwoVesselPhantom` | Two separated lesions of known score | Per-vessel subtotals correct after assignment |
| `ThinSlicePhantom` | Same disc at 1.0 mm slices | One third of the 3 mm score, per FR-604 |
| `DiagonalTouchPhantom` | Two pixel clusters touching only at a corner | One region under 8-connectivity, two under 4 |
| `ConfluentPhantom` | Lesion spanning 6 slices with varying peak HU | Per-slice weights differ; total is the sum, not one weight applied to the whole |

`WeightBoundaryPhantom` and `ConfluentPhantom` are the two that catch real bugs. Off-by-one on a weight boundary, or applying one weight to a whole 3D lesion instead of per slice, are the mistakes that produce a plausible-looking wrong number.

### 8.2 Reference agreement — proves the implementation

If COCA came through: run the pipeline over at least ten annotated cases, assign lesions, and compare your total against the published reference score. Commit the result as a table in `docs/validation.md` with absolute and percentage difference per case.

Expect disagreement, and do not hide it. Sources: your connectivity choice, whether the reference used a different minimum-area convention, and any manual assignment differences. A table showing you within a few percent on most cases with an honest paragraph explaining the outliers is a far stronger artifact than a table showing perfect agreement, which nobody will believe.

- **NFR-404**: `docs/validation.md` shall report per-case agreement against reference scores for at least ten cases, or shall state plainly that reference data could not be obtained.

### 8.3 Traceability

Every FR-600, FR-700, FR-800, FR-900 and NFR-400 requirement gets a row in the existing `docs/traceability.md`. Same table, appended, not a second document. The point of a traceability matrix is that there is one.

---

## 9. Build plan

Three iterations, roughly 9–13 working days.

### Iteration 6 — Scoring engine (target: ~4–5 days)

Headless. No UI at all.

- `InterviewTrea.Scoring` project, `ScoringParameters`, domain models.
- Thresholding, 2D union-find labelling, area filter derived from PixelSpacing.
- Density weights, thickness correction, per-region scoring.
- 3D lesion grouping.
- Per-vessel and total aggregation, volume score.
- The full phantom suite from §8.1.
- ADR-006 on the connectivity choice.

**Done when:** every phantom test passes and a console harness prints a total score for a real cardiac CT with all lesions assigned to a single vessel.

Do not start the UI until the phantoms are green. A wrong number displayed beautifully is worse than no UI.

### Iteration 7 — Plugin and workflow (target: ~4–5 days)

- `CalciumScoringApplication` implementing `IClinicalApplication`; one-line DI registration.
- `CanRun` applicability check with stated reasons (FR-710).
- Candidate overlay layer, vessel colouring (FR-702/703).
- Hit testing: click a region, select the 3D lesion (FR-704).
- Tool panel, vessel assignment, live score table (FR-705/706).
- Crosshair navigation to lesion centroid (FR-707), lesion list (FR-708).
- Incremental recomputation to hit NFR-402.

**Done when:** you can score the §2.4 demo case end to end in under two minutes.

### Iteration 8 — Reporting and validation (target: ~2–3 days)

- Summary panel with parameters (FR-801/802).
- CSV and PNG export with disclaimers (FR-803/804/805).
- `docs/validation.md` (§8.2).
- Traceability rows appended.
- README updated with the calcium scoring section and a screenshot.
- Revised demo script (§10) rehearsed to time.

**Done when:** the combined ten-minute demo runs clean three times without notes.

---

## 10. Revised demo script

Phase 2 does not extend the ten minutes. It competes for them, and it wins — the plugin is the more interesting half. Phase 1's beats compress.

| Time | Beat |
|---|---|
| 0:00–1:00 | Open the cardiac series, 2×2 MPR appears, scroll the axial |
| 1:00–2:00 | Click a structure, crosshairs snap; cycle window presets |
| 2:00–3:00 | Rotate to an oblique plane; note the interpolation and its cost |
| 3:00–3:45 | One ROI measurement, and the phantom test that proves it |
| 3:45–4:15 | Applications menu — here is the plugin contract |
| 4:15–5:00 | Launch calcium scoring; candidates outlined; explain 130 HU and connected components |
| 5:00–6:30 | Assign four lesions to vessels; scores update live; crosshairs fly to each lesion |
| 6:30–7:00 | Summary panel; point at the printed parameters and say why they are there |
| 7:00–7:45 | Raise the threshold to 150 and watch the score move; the number is a convention |
| 7:45–8:30 | Try the unscoreable series; graceful refusal with a reason |
| 8:30–9:15 | Phantom tests on screen; the hand-computed expected value |
| 9:15–10:00 | `validation.md` and `traceability.md` |

The 7:00 beat is the strongest thirty seconds in the demo. Changing one parameter and watching a clinical-looking number move demonstrates, in one gesture, that you understand what the score is and is not.

Cut order if you are running long: the oblique beat, then the unscoreable-series beat. Never cut the last two.

---

## 11. Ruling on the deferred item

STOW-RS write-back was deferred from Phase 1 to here on the reasoning that a calcium score report would give it a purpose. Having written the phase, the answer is still no.

It requires a DICOM server, which the ten-minute demo principle rules out, and the artifact it would produce — a DICOM Structured Report — is not something you could show on screen without a second application to read it. The CSV and PNG exports cover the demonstrable need.

Record this as ADR-007 rather than silently dropping it. "Deferred once, then cut with a reason" is a better story than a feature that quietly vanished.

---

## 12. Risk register

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| COCA access not granted in time | Medium | High | Register on day one. Fallback in §2.3 costs the validation table but not the phantom proof |
| Scores disagree badly with reference | Medium | Medium | Expected. Investigate connectivity and area-floor conventions first, then document honestly. Disagreement you can explain is fine |
| Demo case has too many candidates to click | Medium | Medium | Curate the case in advance per §2.4 and rehearse only on it |
| Non-cardiac calcium (spine, aorta, ribs) clutters the overlay | High | Low | Expected and correct behaviour — the user assigns only coronary lesions. Explain it as the reason the workflow is semi-automated |
| Phase 2 starts with Phase 1 unfinished | Medium | High | Do not begin Iteration 6 until Phase 1 tests are green. A half-built host with a plugin bolted on demos worse than the host alone |

---

## 13. Interview preparation

On top of the Phase 1 questions:

1. **"Walk me through the algorithm."** Threshold, 2D connected components, area filter, weight from peak HU, area × weight, sum. Sixty seconds, no notes.
2. **"Why 2D components and not 3D?"** Because Agatston is defined per slice on 3 mm acquisitions. 3D grouping exists only for assignment. This is the answer that shows you read the standard.
3. **"How do you know the score is right?"** Two arguments: analytic phantoms with hand-computed expected values, and agreement against reference scores. Show both files.
4. **"Why is the vessel assignment manual?"** Because automatic coronary segmentation is the actual hard problem in the product, and a manual assignment step is the authentic legacy workflow rather than a shortcut. Do not pretend you would have done segmentation with more time — say it is a different project.
5. **"What is union-find and why did you use it?"** Path compression, union by rank, near-constant amortised time. Then the honest part: at this data size flood fill would also work, and you chose union-find because the two-pass raster structure suits it.
6. **"Why can two systems report different scores for the same scan?"** Connectivity convention, minimum-area convention, slice thickness handling, kVp. This question is a gift and most candidates cannot answer it.
7. **"What would you build next?"** Automatic heart region localisation to suppress spine and rib candidates — not full coronary segmentation, just a bounding region. Small, useful, honest about its limits.

---

## Appendix — Reference values

| Item | Value |
|---|---|
| Attenuation threshold | 130 HU |
| Minimum lesion area | 1 mm² |
| Reference slice thickness | 3 mm |
| Reference tube voltage | 120 kVp |
| Density weight 1 | 130–199 HU |
| Density weight 2 | 200–299 HU |
| Density weight 3 | 300–399 HU |
| Density weight 4 | ≥ 400 HU |
| Vessels scored | LM, LAD, LCx, RCA |
| Band: none | 0 |
| Band: minimal | 1–10 |
| Band: mild | 11–100 |
| Band: moderate | 101–400 |
| Band: extensive | > 400 |
