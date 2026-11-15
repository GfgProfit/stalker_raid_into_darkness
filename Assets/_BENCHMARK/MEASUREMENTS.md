# Measurement contract

The location History work test is a warmed local microbenchmark of a History pass on location data at a fixed Unity frame number. Its compiled delegate calls the production implementation repeatedly. It does not measure inclusive MatrixHistory CPU time in real frames, camera culling, submissions or GPU work. Do not combine it with recorder/native timings.

The previous 212.68 to 52.50 microseconds describe this local fixed-frame pass. Preserve the original evidence and use this scope when quoting it.

`history-work.json` includes a same-thread allocation-counter positive control. It allocates a 65,536-byte managed array between counter reads and keeps the object alive beyond the second read. A counter is usable only if it observes at least the payload size. When `allocationCounter.valid` is false, zero deltas in History and route-overhead samples do not establish zero allocations. Code inspection for explicit allocations is separate evidence.

`-shadow-only-mode A|B` is an external feasibility experiment. A keeps the existing renderer path. B additionally evaluates `XRayShadowInfluence` against offscreen casting candidates using the full camera far plane, without enabling production rejection. Directional sources are placed first as the one cheaper alternative to the original source order; technical runs check that ordering preserves the rejection count. The startup component list does not certify native particle lights, subsequent lifecycle changes or cached-shadow coverage. Every candidate therefore remains submitted. This measures the cost and potential rejection of the rejected prototype; it is not a comparison against an accepted production filter.

`-shadow-only-technical` records candidates, experimental potential rejections, actual renderer counts, fallback, source observations, screenshots and warmed local preparation/check costs separately from the timing series. Standalone candidate gathering duplicates renderer traversal and is reported separately. None of these counts is a GPU shadow-draw measurement. The timing series uses the same prepared matrix path in both modes.


## Prepared submission groups

`-location-batching A|B` compares original source batches against the production grouping plan. A initializes a private runtime set through the regular production API, retaining matrix preparation and shared-batch fixes while clearing derived groups. B uses the prepared production asset. Both modes contain the same instances, matrices, payload, materials, lighting and source batches. No production benchmark flag enables the optimization.

`-batching-counters` installs an external wrapper around the real renderer callback and records its elapsed callback duration plus actual `Graphics.RenderMeshInstanced` call/instance counters. These technical runs are separate from the timing series. The callback scope includes the renderer's preparation and submission work; it excludes subsequent HDRP/native culling, render-thread work and GPU execution. Color instances count source instances selected for color submission, not visible pixels. GPU draw reductions are not inferred from CPU submissions.

The ordinary timing series installs no batching observer or callback wrapper. Its A1/B1/B2/A2 comparison keeps 8 seconds of warmup, four 5-second positions and the established Full/Stable rules. Report elapsed callback and full-frame results separately, including cases where fewer submissions do not reduce callback time or establish a repeatable FPS gain.

`-batching-capture` adds overview PNGs to technical runs. Their wind/effect phases are not synchronized, so they are not a pixel-equivalence test. The production Editor tests separately compare an isolated fixed-light shadow fixture with offscreen casters, using original/prepared data and a no-caster positive control.


Production admission is controlled by the shared `GameConfig.InstanceSubmissionGroupingEnabled` setting, which defaults to false. Mode B retains the prepared plan and does not override this production setting. Enable the candidate explicitly in the root GameConfig before building a future grouped comparison; mode A still selects a private runtime set without groups. General matrix, history, material-binding and Editor preparation fixes remain active independently.

LocationBatching04 uses three states: U without groups, G0 with the correctness fixes and earlier grouping costs, and G1 with the same correctness fixes plus callback caches. Its single main sequence is U1, G0_1, G1_1, G1_2, G0_2, U2. Separate technical runs report actual calls, instances, color instances, elapsed callback samples and memory scope. Elapsed callback duration includes time inside native calls and is not pure CPU execution time. Retained Player source revisions and build GUIDs identify the measured candidates separately from the later production-default decision.
