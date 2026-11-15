# Measurement contract

The location History work test is a warmed local microbenchmark of a History pass on location data at a fixed Unity frame number. Its compiled delegate calls the production implementation repeatedly. It does not measure inclusive MatrixHistory CPU time in real frames, camera culling, submissions or GPU work. Do not combine it with recorder/native timings.

The previous 212.68 to 52.50 microseconds describe this local fixed-frame pass. Preserve the original evidence and use this scope when quoting it.

`history-work.json` includes a same-thread allocation-counter positive control. It allocates a 65,536-byte managed array between counter reads and keeps the object alive beyond the second read. A counter is usable only if it observes at least the payload size. When `allocationCounter.valid` is false, zero deltas in History and route-overhead samples do not establish zero allocations. Code inspection for explicit allocations is separate evidence.

`-shadow-only-mode A|B` is an external feasibility experiment. A keeps the existing renderer path. B additionally evaluates `XRayShadowInfluence` against offscreen casting candidates using the full camera far plane, without enabling production rejection. Directional sources are placed first as the one cheaper alternative to the original source order; technical runs check that ordering preserves the rejection count. The startup component list does not certify native particle lights, subsequent lifecycle changes or cached-shadow coverage. Every candidate therefore remains submitted. This measures the cost and potential rejection of the rejected prototype; it is not a comparison against an accepted production filter.

`-shadow-only-technical` records candidates, experimental potential rejections, actual renderer counts, fallback, source observations, screenshots and warmed local preparation/check costs separately from the timing series. Standalone candidate gathering duplicates renderer traversal and is reported separately. None of these counts is a GPU shadow-draw measurement. The timing series uses the same prepared matrix path in both modes.


## Prepared submission groups

`-location-batching A|B` compares original source batches against the production grouping plan. A initializes a private runtime set through the regular production API, retaining matrix preparation and shared-batch fixes while clearing derived groups. B uses the prepared production asset. Both modes contain the same instances, matrices, payload, materials, lighting and source batches. No production benchmark flag enables the optimization.

`-batching-counters` installs an external wrapper around the real renderer callback and records its CPU duration plus actual `Graphics.RenderMeshInstanced` call/instance counters. These technical runs are separate from the timing series. The callback scope includes the renderer's preparation and submission work; it excludes subsequent HDRP/native culling, render-thread work and GPU execution. Color instances count source instances selected for color submission, not visible pixels. GPU draw reductions are not inferred from CPU submissions.

The ordinary timing series installs no batching observer or callback wrapper. Its A1/B1/B2/A2 comparison keeps 8 seconds of warmup, four 5-second positions and the established Full/Stable rules. Report callback CPU and full-frame results separately, including cases where fewer submissions do not reduce callback time or establish a repeatable FPS gain.

`-batching-capture` adds overview PNGs to technical runs. Their wind/effect phases are not synchronized, so they are not a pixel-equivalence test. The production Editor tests separately compare an isolated fixed-light shadow fixture with offscreen casters, using original/prepared data and a no-caster positive control.
