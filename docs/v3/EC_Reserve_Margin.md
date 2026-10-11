# Generation EC reserve margin

PAW field `EC reserve margin` (`ecMargin` in part configuration/save data) sets a vessel-wide reserve fraction from 0 to 1, in steps of 0.01. Default: 0.20. Changing any GN module updates the vessel setting; each module persists the shared value. On rebuild the stable leader supplies it.

For stored EC E, capacity C, margin m, and the furnace EC deficit D after same-step TD Blanket generation:

```
available = max(0, E - C*m)
factor = clamp(available / (C*(1-m)), 0, 1)
withdrawal = min(available, D*factor)
```

At m=1 no stored EC is withdrawn. At m=0 the reserve feature is disabled and legacy withdrawal min(D,E) applies. Actual particle production scales with EC actually supplied. Same-step Blanket output may feed the furnace directly even at the reserve floor.

Example: with m=0.2, 60% charge permits 50% of the outstanding furnace demand; at 20% charge storage withdrawal stops. The hard available limit also prevents a long step from crossing the reserve floor. Other consumers can still drain EC below it.

External production can establish an equilibrium above the reserve; the exact level and fluctuations depend on demand, update order, and time step. This does not implement Kerbalism background simulation compatibility.

Validation: 134 core tests and 13 resource-adapter tests pass; Release build succeeds. Added coverage includes margin endpoints, long-step reserve protection, direct Blanket supply with margin=1, and convergence under constant external charging. PAW interaction and in-game time-warp behavior require runtime confirmation.
