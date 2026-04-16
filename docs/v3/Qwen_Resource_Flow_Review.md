**Defect 1 — ALL_VESSEL `Capacity` includes non-puttable tanks**

```csharp
// Capacity (ALL_VESSEL path):
return resources.Where(r => r.part != null && r.flowState).Sum(r => r.maxAmount);
```

The filter admits `FlowMode.Out` tanks (give-only), which `FreeSpace` correctly excludes via `CanPut`. The STAGE_PRIORITY_FLOW path uses `pulling = false` (sinks only) for both `Capacity` and `FreeSpace`, so the two paths disagree on what `Capacity` means. Fix: use `CanPut` as the filter to match the other path and `FreeSpace`.

**No second definite defect found.** Signs in `Put` (negate negative-return from `RequestResource`) and `Take` (positive-return as-is) are consistent with the stated KSP convention. Routing delegation is correct: STAGE_PRIORITY defers to KSP, ALL_VESSEL manipulates `PartResource.amount` directly. Receipt values are bounded and non-negative in both paths.
