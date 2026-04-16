**Review**

V2 is correct in scope: only legacy `KSPParticleEmitter` with EMI in name, and it only touches `localVelocity`. V3 is too broad: starting every `ParticleSystem` and `KSPParticleEmitter` in a part will break stock effects, decouplers, engines, etc. It also never initializes velocity/emission/size, which explains sparse giant sideways particles.

Your proposed fix is better scoped but still risky.

**Concrete risks**

1. **Name filter fragility**: `Contains("EMI")` will match unintended emitters and miss renamed mods. Case sensitivity and localization can break it.
2. **Backing ParticleSystem coupling**: Legacy `KSPParticleEmitter` drives a hidden `ParticleSystem`. Setting legacy fields without syncing the backing system can cause desync, double emission, or Unity warnings.
3. **Hardcoded values**: 500-800 p/s, size 0.025-0.06, lifetime 0.5-1.2s will be wrong for different parts/scale. No per-emitter override.
4. **WorldVelocity zero**: Setting worldVelocity to zero overrides any intentional wind/transform motion. Should be additive, not absolute.
5. **Start/stop side effects**: Blindly starting emitters on part load can cause emission before the part is in world, wasting particles.
6. **Preservation**: `randomVelocity` and textures are preserved, but `emission` and `size` are overwritten every frame if not cached.

**Minimal corrected approach**

Scope to legacy emitters only, run once on init, use configurable defaults.

```csharp
void InitEmitters(Part part, EmitterConfig cfg)
{
    foreach (var e in part.FindModelComponents<KSPParticleEmitter>())
    {
        if (!e.name.Contains("EMI")) continue;
        var ps = e.particleSystem; // backing system
        if (ps == null) continue;

        // Do not start ParticleSystem directly
        e.emit = false; // let KSP control start/stop

        // Legacy fields only
        e.localVelocity = new Vector3(0f, -cfg.speed, 0f);
        e.worldVelocity = Vector3.zero;
        e.minSize = cfg.minSize;
        e.maxSize = cfg.maxSize;
        e.minEmission = cfg.minEmit;
        e.maxEmission = cfg.maxEmit;
        e.minLifetime = cfg.minLife;
        e.maxLifetime = cfg.maxLife;

        // Preserve existing
        // e.randomVelocity, e.texture, e.position untouched
    }
}
```

Run once in `OnStart`/`OnPartReady`, not every Update. Make `EmitterConfig` configurable via ModuleConfig. Avoid touching `ParticleSystem` components directly.
