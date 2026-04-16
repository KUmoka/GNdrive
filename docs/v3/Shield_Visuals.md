# V3 shield visuals

Implemented in GNSystems.cs: GNShieldV3Visuals, owned once per GNDriveV3Vessel. No changes to resource allocation, force calculation or Field activation rules.

## Appearance

- A collider-free sphere uses per-vertex view-dependent alpha: (1 - abs(N dot V))^3 * 0.9 * strength. Front-facing regions are transparent; the silhouette is denser. It is a Fresnel-like approximation, not a custom physically based shader.
- A procedural repeating grain texture scrolls slowly across the transparent membrane, plus a shell of additive soft particles, approximating the supplied reference's granular luminous surface. The reference image is not used as a texture. No external texture files or Unity model changes are required.
- Particle simulation is Local under the vessel child at CoM. Existing particles follow vessel translation/rotation and changing CoM; they do not remain behind in world space. The visual sphere and particles share this origin.
- Shell: 10000 births/s, lifetime0.4s (~4000 live steady state), cap10000; radiusThickness0.1; zero initial speed and slight local noise. Size averages 0.005 times the field radius with random variation. Radius scaling also scales existing particles.
- Membrane fades over ~2 seconds on normal enable/disable. Births stop immediately on disable and remaining particles expire. Switching away, packing, disabling or coordinator failure hides/clears immediately.
- The current V3 physical radius is used directly, without the old V2 extra 1m margin or radius easing, to preserve correspondence with gameplay. Shield renderers are excluded from size measurement.
- Colour follows active TA magenta, otherwise current generating-drive red/green mix. Existing inactive/packed policy is retained.

## Dependencies and cost

Uses KSP/Particles/Alpha Blended and KSP/Particles/Additive, with Legacy Shaders fallbacks. Missing shader logs a warning and hides the affected layer. Per-frame vertex alpha uses the flight camera. No custom shader bundle. Particle count does not multiply by drive count. Mesh, textures, materials and GameObjects are destroyed with the vessel visual owner. CPU vertex loop reuses arrays; mesh colour upload and Unity particle simulation still carry runtime cost.

## Verification

Release build passed against installed KSP APIs. Core94 + adapter11 tests passed, including front/rim opacity, sign symmetry, fade multiplication, clamping and monotonic rim gradient. No game render or runtime profiling was performed. A Unity screenshot cannot be inferred from these tests. Muse supplied the small RimAlpha implementation; integration and tests were done by the main agent (muse-shield-implementation.json).

In-game check after replacing DLL with KSP closed:
1. Field ON: check visible granular edge, transparent centre and ~2s membrane fade.
2. Orbit camera: brightest/most opaque region stays at the silhouette rather than a fixed spot.
3. Translate/rotate ship: shell particles stay attached to the shield; compare with V2 trails.
4. Field OFF/particle depletion: emission stops and membrane fades, while physics follows immediate actual Field state.
5. TA, ship switch, warp, dock/undock: check colour, cleanup and radius; ensure no runaway radius growth.

Reference-image quality remains an initial approximation pending the user's visual feedback, particularly particle brightness/density, rim width and grain scale.

## 1.5-second colour transitions and stock aero FX suppression

Normal/Trans-AM RGB changes now interpolate linearly for 1.5 seconds from the currently displayed colour. Repeated target updates do not restart the fade; reversal during a fade continues from the current colour. Initial colour snaps to the correct initial state. Drive emission, lights and particle material tint, plus shield membrane/particle tint, use this transition. Particle births are white so the material tint also recolours existing particles. These are visual changes only; TA force/state activation remains immediate.

While the active vessel has an actually enabled Field, GNAeroVisualSuppression runs after stock LateUpdate (DefaultExecutionOrder10000). It captures and temporarily disables the stock aerodynamic FX camera/intensity and light, and hides only that vessel's ReentryFXParticleTrail renderer. It restores captured values before the next stock LateUpdate and on Field OFF, component disable/destroy. Thus KSP still recalculates normal values each frame. Stock AerodynamicsFX is located at most once per second while unavailable. KSP 1.12.5 API/IL confirmed separate FXCamera rendering, public effect fields and a vessel-owned trail component.

Scope: stock condensation/reentry visuals and their light. Heating, drag, wind audio, camera shake, GN particles and shield rendering are not altered. Third-party aerodynamic effects are not targeted. Shader/camera execution ordering and recovery during ship switches need an in-game test; build success does not establish visual suppression. Resources and gameplay protection turn off immediately when Field stops, regardless of the shield's visual fade.

Checks: enter/exit TA and reverse mid-fade; observe live particles. At a speed that produces visible stock aero FX, enable Field, disable it again, switch vessels and warp to verify normal effects return. No game installation files were changed.
