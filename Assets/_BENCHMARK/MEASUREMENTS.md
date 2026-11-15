# Measurement contract

The location History work test is a warmed local microbenchmark of a History pass on location data at a fixed Unity frame number. Its compiled delegate calls the production implementation repeatedly. It does not measure inclusive MatrixHistory CPU time in real frames, camera culling, submissions or GPU work. Do not combine it with recorder/native timings.

The previous 212.68 to 52.50 microseconds describe this local fixed-frame pass. Preserve the original evidence and use this scope when quoting it.

`history-work.json` includes a same-thread allocation-counter positive control. It allocates a 65,536-byte managed array between counter reads and keeps the object alive beyond the second read. A counter is usable only if it observes at least the payload size. When `allocationCounter.valid` is false, zero deltas in History and route-overhead samples do not establish zero allocations. Code inspection for explicit allocations is separate evidence.

`-shadow-only-mode A|B` is an external feasibility experiment. A keeps the existing renderer path. B additionally evaluates `XRayShadowInfluence` against offscreen casting candidates using the full camera far plane, without enabling production rejection. Directional sources are placed first as the one cheaper alternative to the original source order; technical runs check that ordering preserves the rejection count. The startup component list does not certify native particle lights, subsequent lifecycle changes or cached-shadow coverage. Every candidate therefore remains submitted. This measures the cost and potential rejection of the rejected prototype; it is not a comparison against an accepted production filter.

`-shadow-only-technical` records candidates, experimental potential rejections, actual renderer counts, fallback, source observations, screenshots and warmed local preparation/check costs separately from the timing series. Standalone candidate gathering duplicates renderer traversal and is reported separately. None of these counts is a GPU shadow-draw measurement. The timing series uses the same prepared matrix path in both modes.
