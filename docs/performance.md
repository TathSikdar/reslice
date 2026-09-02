# Performance (NFR-201 … NFR-203, NFR-304)

Every number here came out of `tests/InterviewTrea.Benchmarks`. Nothing in this file is
estimated, and nothing was measured after an optimization without a matching before.

```
dotnet run -c Release --project tests/InterviewTrea.Benchmarks -- --filter '*'
```

Benchmarks are excluded from CI. A run takes two minutes and its numbers are meaningless
on shared build hardware.

## Machine

| | |
|---|---|
| CPU | AMD Ryzen 5 7600X, 6 cores / 12 threads |
| RAM | 32 GB |
| OS | Windows 11 Pro 10.0.26200 |
| Runtime | .NET 8.0.30, X64 RyuJIT, AVX-512 |
| BenchmarkDotNet | 0.14.0, DefaultJob |

## Workload

512 × 512 × 256 at 0.7 × 0.7 × 1.0 mm — about 134 MB, the shape of a real chest CT and far
too large for any cache. The phantom is a checkerboard on an 8-voxel period, not a uniform
block: a uniform volume is the best case for both the cache and the branch predictor, and a
sphere spends most of its samples in constant air. This is deliberately pessimistic, so a
real study should be no slower.

Output is 512 × 512 in every case. The slab is 20 mm at a 0.7 mm pitch, which is 30 samples
per output pixel — about 7.9 million trilinear fetches per frame.

## Baseline — 2026-09-01, before any optimization

| Benchmark | Requirement | Target | Mean | StdDev | Allocated | Verdict |
|---|---|---|---:|---:|---:|---|
| `AxialFastPath` | NFR-201 | < 8 ms | **0.105 ms** | 0.001 ms | 0 B | Pass, 76× headroom |
| `AxialThroughThePlaneRenderer` | NFR-201 | < 8 ms | **2.354 ms** | 0.014 ms | 2 B | Pass, 3.4× headroom |
| `ObliqueReslice` | NFR-202 | < 16 ms | **1.949 ms** | 0.062 ms | 1 B | Pass, 8× headroom |
| `SlabMaximum20Mm` | NFR-203 | < 33 ms | **81.5 ms** | 0.27 ms | 57 B | **Fail, 2.5× over** |
| `SlabAverage20Mm` | NFR-203 | < 33 ms | **81.4 ms** | 0.65 ms | 57 B | **Fail, 2.5× over** |

Allocation is effectively zero throughout. The handful of bytes reported are
BenchmarkDotNet's own measurement noise, not a per-frame allocation: the buffers, the
lookup table and the plane are all created once in `[GlobalSetup]`.

### Reading the table honestly

**The axial fast path is 22× faster than the general path, and that gap is the entire
argument for keeping both.** An axial slice is one contiguous run of memory already in
display order, so its render is a walk through the window/level table with no interpolation
at all. The general path does seven interpolations per pixel and touches eight scattered
voxels to do it. Both clear NFR-201 comfortably, so the viewports use the general path and
the fast one exists to be measured against — but if the target were 2 ms rather than 8 ms,
this row is where the answer would come from.

**The oblique reslice is faster than the axial one through the same code, which is an
artefact and not a result.** The oblique plane is the same 512 × 512 grid tilted 30°, so
roughly the bottom third of its rows fall outside the volume and return the out-of-bounds
value without ever reading a voxel. Comparing it against the axial row and concluding that
obliqueness is free would be wrong. What the row does support is the weaker and still
useful claim that NFR-202 is met with room to spare.

**NFR-203 is missed by a factor of 2.5, and no optimization has been attempted yet.** That
is the point of a baseline. The arithmetic is unsurprising: 7.9 million trilinear fetches
in 81.5 ms is about 10 ns each, and at 134 MB the volume guarantees a cache miss on nearly
every one. The obvious candidates, in the order the spec recommends, are hoisting the
bounds test out of `SampleTrilinear` (the slab loop already checks the same condition
immediately before calling it, so every sample pays for it twice), specialising the
per-sample `switch` on `SlabMode` out of the inner loop, and parallelising over rows — the
render loop was written so that each row is independent of every other precisely so that
last one stays available.

## Optimizations

Applied in the order §7.1 recommends, measured after each. One of the two was reverted.

| # | Change | Benchmark | Before | After | Result |
|---|---|---|---|---:|---:|---|
| §7.1-3 | Skip the duplicate bounds test: the slab loop already calls `ContainsContinuous` before every sample, so it called an unguarded `SampleTrilinearInside` instead of `SampleTrilinear` | `SlabMaximum20Mm` | 81.4 ms | 116.4 ms | **43% slower — reverted** |
| §7.1-4 | Render slab rows on `Parallel.For` | `SlabMaximum20Mm` | 81.5 ms | 14.1 ms | **5.8x faster — kept** |

### The optimization that made it slower

Removing six double comparisons per sample cost 43%, reproducibly, with a standard
deviation under half a millisecond on both sides. The baseline was re-measured immediately
afterwards on the same machine and came back at 81.4 ms, so this is not thermal drift.

Two hypotheses were tested and both were wrong. Splitting `SampleTrilinear` into a guard
plus an unguarded body was not the cause: duplicating the body into a separate method and
leaving `SampleTrilinear` completely untouched gave the same 116 ms. Inlining was not the
cause either: forcing `[MethodImpl(NoInlining)]` on the unguarded twin gave 116.4 ms again.

So the situation is two methods with identical arithmetic, called from the same site on the
same data, where the one doing strictly less work is reliably half as fast again — and no
explanation that survives contact with the measurements. Most likely it is something below
the source level, code layout or loop alignment, which would need a disassembly or a
hardware-counter profile to confirm.

That work was not done, because the change was worth 43% in the wrong direction and the
next item on the list was worth 5.8x in the right one. **The change was reverted and the
guess about why was not committed to the code.** A comment claiming an alignment effect
that was never verified would be worse than no comment, and the earlier entries in
`docs/ai-assistance-log.md` are all about exactly that failure mode.

The general point is the one the whole file exists for: §7.1 lists its techniques "roughly
in order of payoff", and roughly is doing real work in that sentence. Technique 3 was a
loss here and technique 4 cleared the requirement on its own.

### The optimization that worked

`Parallel.For` over output rows, on six cores. Each row writes a disjoint run of the
destination and reads nothing another row writes, so there is no synchronisation in the
renderer at all — which is why the row start is recomputed from the plane origin instead of
carried over from the previous row. That decision was made when the loop was first written,
before there was any intention to parallelise it, and it is the reason this change was four
lines.

5.8x on six cores is a little under linear, which is what a memory-bound loop should give:
the cores are competing for the same memory bandwidth, so the sixth one cannot be as
productive as the first.

Cost: allocation per frame goes from effectively zero to 4.3 KB, which is `Parallel.For`'s
own range and task bookkeeping. At about seventy frames a second that is 300 KB/s of
generation-0 garbage, which is noise next to the 134 MB volume the loop is already
streaming. A custom partitioner would remove it and is not worth the code until something
measures it as a problem.

The plane renderer was deliberately left serial. At 2.4 ms against an 8 ms target it does
not need the cores, and taking them would make it compete with the slab pane it shares a
window with.

## After — 2026-09-01

| Benchmark | Requirement | Target | Mean | StdDev | Allocated | Verdict |
|---|---|---|---:|---:|---:|---|
| `AxialFastPath` | NFR-201 | < 8 ms | 0.109 ms | 0.001 ms | 0 B | Pass |
| `AxialThroughThePlaneRenderer` | NFR-201 | < 8 ms | **2.406 ms** | 0.020 ms | 2 B | Pass, 3.3x headroom |
| `ObliqueReslice` | NFR-202 | < 16 ms | **1.945 ms** | 0.012 ms | 1 B | Pass, 8.2x headroom |
| `SlabMaximum20Mm` | NFR-203 | < 33 ms | **14.109 ms** | 0.152 ms | 4.3 KB | Pass, 2.3x headroom |
| `SlabAverage20Mm` | NFR-203 | < 33 ms | **14.067 ms** | 0.111 ms | 4.3 KB | Pass, 2.3x headroom |

All three NFR-200 targets are met. The two figures that did not move are unchanged code and
are quoted here only so the table is complete.

## Phase 2 — the 3D view

Measured on LIDC-IDRI-0001: 512 x 512 x 133 at 0.7 x 0.7 x 2.5 mm, so the sampling step is
0.35 mm (half the finest voxel side). Timed with `Stopwatch` in a Release console harness
rather than with BenchmarkDotNet, because each measurement is hundreds of milliseconds and
the run-to-run spread is small next to the budget - the honest description is a
measurement, not a benchmark, and it is labelled as one.

| Frame | Size | Step | Shading | Time | Budget |
|---|---|---|---|---|---|
| Bone, full | 512 x 512 | 0.35 mm | yes | 210 ms | NFR-401: 400 ms |
| Angio, full | 512 x 512 | 0.35 mm | yes | 217 ms | NFR-401: 400 ms |
| Lung, full | 512 x 512 | 0.35 mm | yes | 214 ms | NFR-401: 400 ms |
| Skin, full | 512 x 512 | 0.35 mm | yes | 85 ms | NFR-401: 400 ms |
| Interaction | 256 x 256 | 1.40 mm | no | 11 ms | NFR-402: 50 ms |

Skin is two and a half times faster than the others for the reason early termination
exists: it is a surface preset, so almost every ray stops within a few millimetres of the
skin instead of crossing the whole patient. Lung is the slowest to terminate because
nothing in it is opaque - the rays run to the far side of the volume - which is visible in
the numbers rather than only arguable from the code.

The interaction frame is a quarter of the pixels at four times the step, about a sixteenth
of the work, and it measures at a nineteenth. FR-603's opacity correction is what makes it
the same picture rather than a lighter one.

