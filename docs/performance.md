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

None yet. Each one gets a row here with its own before and after, measured on this machine
with this workload.

| Change | Benchmark | Before | After | Speed-up |
|---|---|---|---|---|
| _(none applied)_ | | | | |
