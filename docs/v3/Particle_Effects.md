# V3 particle model contract (2026-09-23)

V3 now creates its own additive soft-dot particle systems. The prior implementation activated every model emitter; it did not create a new particle system. The screenshot alone does not identify which embedded emitter produced the large sideways particles.

## Unity model preparation

- Remove model ParticleSystem and KSPParticleEmitter components. Keep an empty Transform named EMI at each intended outlet.
- Local -Y points outward; V3 rotates its own +Z cone to this direction. Do not rotate an existing correct -Y anchor an additional 90 degrees.
- Prefer unit scale on the anchor and its parents; bake intended model scale. Particle dimensions inherit hierarchy scale.
- Put anchors inside the corresponding variant object so hiding that variant also hides its particles. Distinct outlets may share the EMI name.
- No particle material, texture, light or audio component is required. Do not remove mesh emissive materials or rotor transforms.

GNModule cfg defaults (per outlet):

```
ParticleTransform = EMI
ParticleSize = 0.04
ParticleRate = 600
ParticleSpeed = 6
ParticleAcceleration = 20
ParticleLifetime = 1
ParticleConeAngle = 12
ParticleRadius = 0.04
```

Size is randomized from half to 1.5 times the configured value. Units are local model metres, seconds, particles/second, metres/second squared and degrees. Empty ParticleTransform disables GN particles. Existing selected legacy emitters are stopped to prevent duplication; effects outside selected anchors are not controlled.

Initial effect uses local simulation, so the trail follows craft translation and rotation. This is an initial visual preview, not a persistent world-space particle wake. Particle count is capped at 4096 per outlet. Activation still follows existing drive-running/generation visual state, not throttle-proportional intensity. Colour follows the existing V3 colour selection. Model exports and in-game appearance remain unverified.

Muse Glimmer reviewed the initial emitter-ownership proposal in 10.14 seconds, 634 output tokens (55 reasoning). Useful scope/ownership concerns were adopted. Its suggested legacy API members were not adopted without validation. The subsequent user decision to replace embedded emitters led to the runtime ParticleSystem implementation, which was not covered by that review.

Test: replace DLL and exported model with KSP closed. Check each variant, outlet direction, small dense dots, OFF transition, and TA colour. Log contains [GN V3] Particle anchors with selected anchor count; zero means model/cfg names do not match.

## 2026-09-23 PAW tuning and startup changes

- PAW Show/hide visual tuning exposes particle size, emission rate, speed, lifetime, cone angle, outlet radius and Rotor max deg/s in editor/flight. Values persist in craft/save; changes are applied during flight. Existing particles retain birth properties until they expire. Renderer settings are not globally changed. Simulation still caps live particles at 4096/outlet.
- Opening tuning captures the current profile rotor speed as a persistent PAW override; hiding the sliders does not revert it. Rotor speed is the full-output speed; TD idle runs at 10 percent. Particle sliders do not change GNparticle resource generation or consumption.
- Stage starts GN (default Yes) enables staging activation through the existing Engine ON command, not a toggle. Existing start restrictions and synchronized-group command rules still apply. IsStageable/StagingEnabled and OnActive use the actual KSP PartModule API. In-game staging icon/order needs confirmation.
- TD EC rate >0 enables the existing particle effect even with propulsion OFF and a full GNparticle tank. It starts on the first normal vessel physics update and ramps visible particles naturally, not a prefilled trail. Propulsion is not automatically switched ON.
- Stator identifiers override legacy rotor selection. Stator world poses are preserved across the rotor rotation step, including when nested below a rotor; core and arm movement still carry them. This addresses rotor-parent inheritance, but the exact cause in the user's model has not been confirmed. Prefer sibling rotor/stator hierarchy; external Animators can still conflict.
- Muse authored ApplyParticleSettings, then the returned Unity API code was reviewed and compiled into GNSystems.cs. Saved response: muse-paw-implementation.json.

Runtime checks: show tuning and change one slider; save/reload; hide tuning without speed change; stage once with propulsion OFF and check ON; load a TD-powered OFF drive with full particles; verify stator mesh/collider stays still while rotor spins and core moves. Unity appearance, nested transforms and staging require game tests.

## Load-responsive drive particles (2026-09-24)

Drive particles (not the shield shell) now interpolate speed/rate/cone with a smoothed load 0..1. Load=max(existing throttle/RCS/support visual demand, allocated support+braking+thrust magnitudes+torque cost+Field repulsion cost divided by running total Power). This is a vessel-level visual estimate shared by running drives, not individual measured engine output; input demand may remain high under resource shortage. TD-only or generation-only idle emission uses load0. No physical power/resource calculations changed.

Existing ParticleSpeed/ParticleRate/ParticleConeAngle are the full-load endpoints and keep saved values. New persistent PAW/cfg endpoints: ParticleIdleSpeed=1, ParticleIdleRate=60, ParticleIdleAngle=35. Actual idle speed/rate cannot exceed full-load values; idle angle cannot be narrower than full-load angle. Existing visual tuning toggle exposes all endpoints plus current load and effective rate (per outlet). Smoothing uses VisualResponse (default2 per second). Size/lifetime/radius retain independent settings. Birth settings do not retroactively change particle velocity or age.

Emission capped to 80% of4096/lifetime, with lifetime clamped .05..10. Example: 3000/s and10s lifetime becomes327.68/s. Default600/s at1s is unchanged. This avoids steady-state particle-count saturation rather than assuming more particles solve banding. Live parameter changes can temporarily retain previous long-lived particles; timestep, camera and emission sampling may also produce gaps. In-game artifact elimination remains unverified.

Muse supplied the SafeRate helper. Tests cover interpolation, reversed cone interpolation, clamped load and lifetime-dependent capacity headroom.

## Acceleration backflow (2026-09-24)

While a GN propulsion module is running and the vessel receives a nonzero allocated GN force, every drive particle receives acceleration opposite that force in addition to its outlet velocity. `ParticleAcceleration` is the full-load endpoint; the applied magnitude is multiplied by the same smoothed vessel particle load used by speed/rate/cone, so it continuously rises from zero near idle to the configured value near the running drive group's maximum output. The default is 20m/s2 and the persistent visual-tuning PAW range is 0..200; zero disables it. Direction is recomputed from the paid, allocated vessel force each physics tick and transformed into each emitter's local frame. Propulsion-off TD/generation idle particles receive no backflow, and zero/invalid force disables Force over Lifetime so a stale direction cannot remain. Local particle simulation, outlet cone direction, load-responsive birth settings, resources and physical force calculations are unchanged. Unity/KSP appearance remains an in-game validation item.
