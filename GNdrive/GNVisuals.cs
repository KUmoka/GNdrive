using KSP;
using System;
using UnityEngine;

namespace GNTechnology
{
    public enum GNVisualMode {Condenser, Drive}//When Condenser mode, no particle emission from the parts.
        
    public struct GNVisualState
    {
        public Part part; //GN tech part
        public Transform[] Rotors; //rotor transforms
        public Light[] GlowLights; //glow light sources
        public Renderer[] EmissiveRenderers; //emissive renderers(rotor, stator)
        public KSPParticleEmitter[] ParticleEmitters; //EMI (particle emitters)
        public GNVisualMode Mode;//Visual mode.Condenser or Drive.
        public Color ParticleColor; //green:original drive, red:tau drive
        public float InputLevel; //from 0.01 to 1
        public bool EngineState; //true:engine on, false:engine off

        public static GNVisualState Empty => new GNVisualState
        {
            part = null,
            Rotors = Array.Empty<Transform>(),
            GlowLights = Array.Empty<Light>(),
            EmissiveRenderers = Array.Empty<Renderer>(),
            ParticleEmitters = Array.Empty<KSPParticleEmitter>(),
            Mode = GNVisualMode.Condenser,
            ParticleColor = Color.black,
            InputLevel = 0f,
            EngineState = false
        };
    }

    public static class GNVisuals
    {
        public static void SetOff(in GNVisualState s)
        {
            if (s.part == null) return;

            UpdateRotor(s.Rotors, 0f);
            UpdateGlow(s.EmissiveRenderers, s.GlowLights, s.ParticleColor, 0f);
            if (s.ParticleEmitters != null)
                for (int i = 0; i < s.ParticleEmitters.Length; i++)
                {
                    var e = s.ParticleEmitters[i];
                    if (!e) continue;
                    e.enabled = false;
                    e.emit = false;
                    e.minEmission = 0;
                    e.maxEmission = 0;
                }
        }

        public static void UpdateVisual(in GNVisualState s)
        {
            if (s.part == null)
            { 
                Debug.LogError("[GN] GNVisualState.part is null!");
                return; 
            }

            if (!s.EngineState && s.Mode == GNVisualMode.Drive)
            {
                Debug.Log("[GN] Engine is off. Stop all visual effects.");
                UpdateRotor(s.Rotors, 0f);
                UpdateGlow(s.EmissiveRenderers, s.GlowLights, s.ParticleColor, 0f);
                UpdateParticle(s.ParticleEmitters, s.ParticleColor, 0f);
                return;
            }

            float level = GetLevel(s);
            level = Mathf.Clamp01(level);

            UpdateRotor(s.Rotors, level);
            UpdateGlow(s.EmissiveRenderers, s.GlowLights, s.ParticleColor, level);
            UpdateParticle(s.ParticleEmitters, s.ParticleColor, level);
        }

        private static float GetLevel(in GNVisualState s)
        {
            if (s.Mode == GNVisualMode.Drive)//0-1 input level%
            {
                var x = Mathf.Clamp01(s.InputLevel);
                return Mathf.Lerp(0.1f, 1f, x);
            }

            var res = s.part.Resources["GNparticle"];
            if (res == null || res.maxAmount <= 0) return 0f;
            return (float)(res.amount / res.maxAmount);
        }

        private static void UpdateRotor(Transform[] rotors, float level)
        {
            if (rotors == null) return;
            float dt = Time.deltaTime;
            float speed = 120f * level;

            for (int i = 0; i < rotors.Length; i++)
            {
                var t = rotors[i];
                if (t) t.Rotate(0f, speed * dt, 0f, Space.Self);
            }
        }

        private static void UpdateGlow(Renderer[] renderers, Light[] lights, Color c, float level)
        {
            var glow = c;
            glow.a = level;

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
                Debug.Log("[GN] ParticleEmitter found.");

                // Smoothly adjust emission rates
                e.enabled = true;
                e.emit = true;
                e.minEmission = (int)Mathf.Lerp(e.minEmission, tMin, 10f);
                e.maxEmission = (int)Mathf.Lerp(e.maxEmission, tMax, 10f);
                e.localVelocity = new Vector3(0f, -1 * Mathf.Lerp(5f, 45f, level), 0f);

                //Future update will change particle color.
                //now particle color is depends on the model.
            }
        }
    }
}
