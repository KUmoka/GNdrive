using KSP;
using System;
using UnityEngine;
using static iT;

namespace GNTechnology
{
    public enum GNVisualMode {Condenser, Drive, CondenserDrive}//When Condenser mode, no particle emission from the parts.
        
    public struct GNVisualState
    {
        // Basic info
        public Part part; //GN tech part
        public GNVisualMode Mode;//Visual mode.Condenser or Drive.
        public Color ParticleColor; //green:original drive, red:tau drive
        public float InputLevel; //from 0 to 1
        public float RotorSpeed; // rotor speed (degree per second)
        public bool EngineState; //true:engine on, false:engine off

        // Transforms and renderers
        public Transform[] Rotors; //rotor transforms
        public Light[] GlowLights; //glow light sources
        public Renderer[] EmissiveRenderers; //emissive renderers(rotor, stator)
        public KSPParticleEmitter[] ParticleEmitters; //EMI (particle emitters)


        public static GNVisualState Empty => new GNVisualState
        {
            // Basic info
            part = null,
            Mode = GNVisualMode.Condenser,
            ParticleColor = Color.black,
            InputLevel = 0f,
            RotorSpeed = 0,
            EngineState = false,

            // Transforms and renderers
            Rotors = Array.Empty<Transform>(),
            GlowLights = Array.Empty<Light>(),
            EmissiveRenderers = Array.Empty<Renderer>(),
            ParticleEmitters = Array.Empty<KSPParticleEmitter>()
        };
    }

    public static class GNVisuals
    {
        public static void SetOff(in GNVisualState vs)// for initialization
        {
            // part is null=> do nothing
            if (vs.part == null) return;

            // stop all visual effects
            UpdateRotor(vs.Rotors, 0f,0f); // no rotation
            UpdateGlow(vs.EmissiveRenderers, vs.GlowLights, vs.ParticleColor, 0f);
            if (vs.ParticleEmitters != null)//no emitters = condenser.
            {
                for (int i = 0; i < vs.ParticleEmitters.Length; i++)
                {
                    var e = vs.ParticleEmitters[i];
                    if (!e) continue;
                    e.enabled = false;
                    e.emit = false;
                    e.minEmission = 0;
                    e.maxEmission = 0;
                }
            }
        }

        private static void RotorOff(in GNVisualState vs, float level)
        {
            // part is null=> do nothing
            if (vs.part == null) return;

            UpdateRotor(vs.Rotors, 0f, 0f); // no rotation
            UpdateGlow(vs.EmissiveRenderers, vs.GlowLights, vs.ParticleColor, level); // Condenser glow
            UpdateParticle(vs.ParticleEmitters, vs.ParticleColor, 0f); // no particle emission
        }

        public static void UpdateVisual(in GNVisualState vs)
        {

            // NRE avoidance
            if (vs.part == null)
            {
                return;
            }

            // variables
            Vessel vessel = vs.part.vessel;
            bool brakes = vessel.ActionGroups[KSPActionGroup.Brakes];
            Vector3 vSrf = (Vector3)vessel.srf_velocity;
            float speed = vSrf.magnitude;

            // Engine:On => update visual effects
            float level = GetLevel(vs);

            // Engine off => stop visual effects of drives
            if (!vs.EngineState && vs.Mode != GNVisualMode.Condenser)
            {
                if (vs.Mode == GNVisualMode.Drive) SetOff(vs);
                if (vs.Mode == GNVisualMode.CondenserDrive) RotorOff(vs, level);
                return;
            }

            // GNCondenserDrive, or else.
            if (vs.Mode == GNVisualMode.CondenserDrive)
            {
                var lv = Mathf.Lerp(0.1f, 1f, Mathf.Clamp01(vs.InputLevel));

                // if brake, less particle emission and light.
                if (brakes && speed < 0.05) lv = 0.1f;

                UpdateRotor(vs.Rotors, vs.RotorSpeed, lv);
                UpdateGlow(vs.EmissiveRenderers, vs.GlowLights, vs.ParticleColor, level);
                UpdateParticle(vs.ParticleEmitters, vs.ParticleColor, lv);
            }
            else
            {
                // if brake, less particle emission and light.
                if (brakes && speed < 0.05 && (vs.Mode == GNVisualMode.Drive)) level = 0.1f;
                UpdateRotor(vs.Rotors, vs.RotorSpeed, level); // usual
                UpdateGlow(vs.EmissiveRenderers, vs.GlowLights, vs.ParticleColor, level);
                if (vs.Mode != GNVisualMode.Condenser) UpdateParticle(vs.ParticleEmitters, vs.ParticleColor, level);
            }
        }

        private static float GetLevel(in GNVisualState vs) // set emission, rotation and particle emission level
        {
            if (vs.Mode == GNVisualMode.Drive)//0-1 input level%
            {
                var x = Mathf.Clamp01(vs.InputLevel);
                return Mathf.Lerp(0.1f, 1f, x);
            }

            var res = vs.part.Resources["GNparticle"];
            if (res == null || res.maxAmount <= 0) return 0f;
            return (float)(res.amount / res.maxAmount);
        }

        private static void UpdateRotor(Transform[] rotors, float rotorspeed, float level)// rotate rotors
        {
            if (rotors == null) return;
            float dt = Time.deltaTime;
            float speed = rotorspeed * level;

            // Rotation control logic here.
            for (int i = 0; i < rotors.Length; i++)
            {
                var t = rotors[i];
                if (t) t.Rotate(0f, speed * dt, 0f, Space.Self);
            }
        }

        private static void UpdateGlow(Renderer[] renderers, Light[] lights, Color c, float level) // update emissive color and light intensity
        {
            Color glow = c;
            glow.a = level; //a = alpha channel = intensity

            // Emissive
            if (renderers != null)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    var r = renderers[i];
                    if (!r) continue;
                    if (r.sharedMaterial != null && r.sharedMaterial.HasProperty("_EmissiveColor"))
                        r.sharedMaterial.SetColor("_EmissiveColor", glow);
                }
            }

            // Light
            if (lights != null)
            {
                float baseIntensity = 10f;
                for (int i = 0; i < lights.Length; i++)
                {
                    var l = lights[i];
                    if (!l) continue;
                    l.range = level;
                    l.color = glow;
                    l.intensity = baseIntensity * level;
                }
            }
        }

        private static void UpdateParticle(KSPParticleEmitter[] emitters, Color particleColor, float level)
        {
            if (emitters == null) return;
            float tMin = 7000f * level * level;
            float tMax = 9000f * level * level;

            for (int i = 0; i < emitters.Length; i++)
            {
                var e = emitters[i];
                if (!e) continue;

                // Smoothly adjust emission rates
                e.enabled = true;
                e.emit = true;
                e.minEmission = (int)Mathf.Lerp(e.minEmission, tMin, 100f * Time.deltaTime); //10f for last
                e.maxEmission = (int)Mathf.Lerp(e.maxEmission, tMax, 100f * Time.deltaTime);
                e.localVelocity = new Vector3(0f, -1 * Mathf.Lerp(5f, 45f, level), 0f);

                // particle coloring system
                SetEmitterColor_PS(e, particleColor);
                //DumpEmitter(e, "GN");
            }
        }

        private static bool ApproximatelyRGB(Color a, Color b, float eps = 1e-3f) => Mathf.Abs(a.r - b.r) < eps && Mathf.Abs(a.g - b.g) < eps && Mathf.Abs(a.b - b.b) < eps;
        private static readonly int TintId = Shader.PropertyToID("_TintColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private static void SetEmitterColor_PS(KSPParticleEmitter e, Color c)
        {
            if (!e) return; // prevents NRE

            var ps = e.GetComponent<ParticleSystem>();
            if (ps == null) return; // no particle system -> return

            // 既存色と同じならスキップ（startColor優先でチェック）
            var main = ps.main;
            Color current = Color.magenta;
            switch (main.startColor.mode)
            {
                case ParticleSystemGradientMode.Color: current = main.startColor.color; break;
                case ParticleSystemGradientMode.TwoColors: current = main.startColor.colorMax; break;
                case ParticleSystemGradientMode.Gradient:
                case ParticleSystemGradientMode.TwoGradients:
                    // gradientが有効なら最初のcolorKeyから推定
                    var col = ps.colorOverLifetime;
                    if (col.enabled && col.color.mode == ParticleSystemGradientMode.Gradient)
                    {
                        var g = col.color.gradient;
                        if (g.colorKeys != null && g.colorKeys.Length > 0) current = g.colorKeys[0].color;
                    }
                    break;
            }
            if (ApproximatelyRGB(current, c)) return;

            // αは e.colorAnimation の [0],[2],[4] を使う（無ければデフォルト）
            float a0 = 1f, a2 = 0.35f, a4 = 0.02f;
            var keys = e.colorAnimation; // Color[5]
            if (keys != null && keys.Length >= 5) { a0 = keys[0].a; a2 = keys[2].a; a4 = keys[4].a; }

            // startColor（出生色）と、Color Over Lifetime（全期間の色）を設定
            main.startColor = new ParticleSystem.MinMaxGradient(c); // new Color(c.r, c.g, c.b, 1f));

            var colOL = ps.colorOverLifetime;
            colOL.enabled = true;

            var g2 = new Gradient();
            g2.SetKeys(new[]{new GradientColorKey(new Color(c.r,c.g,c.b), 0f), new GradientColorKey(new Color(c.r,c.g,c.b), 1f)}, new[]{new GradientAlphaKey(a0, 0f), new GradientAlphaKey(a2, 0.5f), new GradientAlphaKey(a4, 1f)});
            colOL.color = new ParticleSystem.MinMaxGradient(g2);
        }

        // for debug.
        private static void DumpEmitter(KSPParticleEmitter e, string tag = "")
        {
            var r = e.GetComponent<Renderer>();
            var mat = r ? r.sharedMaterial : null;
            var tint = mat && mat.HasProperty("_TintColor") ? mat.GetColor("_TintColor") :
                       mat && mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.magenta;

            Debug.Log($"[GN] {tag} emit={e.emit} doesAnim={(e ? e.doesAnimateColor : false)} " +
                      $"tint={tint} tex={mat?.mainTexture?.name} shader={mat?.shader?.name} " +
                      $"c0={e.colorAnimation[0]} c2={e.colorAnimation[2]} c4={e.colorAnimation[4]} " +
                      $"hasPS={(e.GetComponent<ParticleSystem>() != null)}");
        }
    }

    public static class GNColorDecider
    {
        public static Color ParticleColor(in GNVisualState vs)
        {
            // NPE check
            float TDmax = (float)vs.part.Resources["TopologicalDefects"].maxAmount;

            // NPE avoid
            if (TDmax == 0) return new Color(1f, 0f, 0.15f, 1f);

            float TDnow = (float)vs.part.Resources["TopologicalDefects"].amount;
            float r = 1f - TDnow / TDmax;
            float g = TDnow / TDmax;
            float b = (40f * (1 - g) + 170f * g) / 255f;

            // intermix
            return new Color(r, g, b, 1f); // Red for condenser.
        }
    }
}