**No definite bug.**

I traced every resource path against the three stated invariants:

1. **Budget never exceeded.** Both `Allocator.Allocate` calls receive an explicit budget (`available`, then `paid`), and the final guard `r.Consumed > paid + 1e-7` throws if violated. The first allocation is used only to derive `take`; it is discarded, not consumed.

2. **SafeGuard isolation.** When `settings.SafeGuard` is true, `take` is hard-zeroed (no `particles.Take` call), `r.SupplyLimited` is hard-false (TransAM force-off block is skipped), and `available` excludes `pStart`. Stored particles are never touched and TransAM is never forced off.

3. **No double-take in normal mode.** `take` is computed from the first allocation's cost minus `r.Generated`, then actually withdrawn via `particles.Take(take)`. The second allocation's budget is `paid = r.Generated + r.ParticleTaken`, which is ≤ the first allocation's budget. The port is debited once; the allocator is never called with a budget that includes already-withdrawn particles a second time.

EC accounting (`ecScale`, `ecSurplus`, `r.ECUsed`) and particle surplus (`surplus = paid − r.Consumed`, put-back via `particles.Put`) are internally consistent in all branches.
