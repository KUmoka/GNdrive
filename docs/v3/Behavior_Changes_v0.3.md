# V3 behavior changes after the second single-drive test

2026-09-23. These changes override the corresponding rules in the preserved v0.2 specification.

- AG: while vertical velocity along local gravity-up is strictly positive, downward RCS adds actual downward GN thrust, allowing braking stronger than natural gravity. At zero or negative vertical velocity it is suppressed again. Downward throttle remains suppressed; Hover retains its original behavior. Brakes still overrides translation input.
- Field particle priority: support, Field maintenance, braking, thrust/rotation, then repulsion. Maintenance is a particle expense, not a force/Power expense. Existing propulsion and rotation Power reservations remain; repulsion uses remaining Field-capable Power. Insufficient repulsion budget scales forces instead of disabling Field. Insufficient maintenance after support disables Field and requires manual restart. PAW reports maintenance shortage and Power/particle-limited repulsion.
- SafeGuard automatically turns OFF when no particle-capable drive requests generation. Its PAW value is synchronized. Generation ON does not automatically reenable it. EC starvation alone does not disable SafeGuard. An Original drive that cannot disable generation continues requesting generation; another active generation request also prevents automatic OFF.
- Hover ON clears AG on affected drives, including PAW. AG ON likewise clears Hover. Hover OFF does not restore AG. Saved overlapping flags normalize to Hover. Group commands apply the same rules to affected members.

## Corrected implementation defect

Field could stop on its second allocation pass because dividing the paid amount by dt introduced rounding error at the exact maintenance boundary. The new allocator compares particle amounts with a relative floating-point tolerance. A reproducer with support 9.52034948371367 kN, maintenance 79.7713328825642/s, and dt 0.046078520800489242 is covered by regression test 68. This demonstrates a possible cause of the reported symptom, not proof of the exact cause in the user's save.

Opposing RCS also requires keeping support/maintenance reservation funded until the resource receipt is known. Settlement then refunds any unused amount; actual consumption remains the resultant force cost plus torque and Field expenses.

## Game checks

1. AG ON, rising, downward RCS: verify stronger braking; stationary/falling: verify downward input suppression.
2. Field with limited particles: verify repulsion weakens while maintenance remains funded; read GN Field status. With thrust occupying all Power, Field can remain active with zero repulsion.
3. Switch generation OFF on a drive allowed to do so: verify SafeGuard OFF in PAW and stock particle consumption. Switch generation ON: SafeGuard stays OFF until manually enabled.
4. Hover ON: AG immediately OFF in PAW. Reverse switch and save/reload should preserve exclusive flags.

Game execution remains user-verified; automated tests do not substitute for KSP physics/UI tests.

## Trans-AM acceleration change

Trans-AM now multiplies the clamped normal acceleration target by exactly 3, after the normal MaxG clamp. The PAW Acceleration slider/save remains the normal setting; Effective accel target shows the multiplied target, not measured acceleration. The multiplier is independent of cfg TransAMRatio (which still controls Power). At least one running TA-capable drive with TA active enables the vessel-level target multiplier; multiple drives do not stack it. Non-TA engines retain their normal Power contributions. Engine OFF/Repose/depleted/unsupported TA does not enable the boost. Manual exit or particle-shortage TA fallback restores the normal target without editing the saved slider. SafeGuard, particle supply and Power still limit actual acceleration; AG continues to support weight, and Hover/braking only apply the larger cap when needed.

Muse supplied the small AccelerationMultiplier helper; integration and regression tests were completed by the primary agent. Four new cases cover exact cap tripling, exit/unchanged settings, ignored inactive/unsupported TA, same-tick shortage fallback, no stacking and zero setting.

## Braking vertical-stop correction

Brakes now request gravity support even without AG/Hover; their PAW flags remain unchanged. Previously pure velocity damping without gravity feedforward permitted a persistent descent. When support was enabled, Hover deadband also preserved small velocities. Brakes now use BrakeTime away from zero and zero deadband; within configured Deadband magnitude, response time becomes zero so the bounded force cancels the remaining vertical velocity over dt. Rigidbody velocity is never assigned. Normal Hover deadband remains unchanged. Power, acceleration and particle limits still apply; insufficient support cannot hold altitude. Tests 90-92 cover gravity compensation, integrated convergence with and without AG and insufficient acceleration.
