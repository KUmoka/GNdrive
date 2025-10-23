using KSP;
using System;
using UnityEngine;

namespace GNTechnology
{
    public enum GNVisualMode {Condenser, Drive}//When Condenser mode, no particle emission from the parts.
        
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

        public static void UpdateVisual(in GNVisualState vs)
        {
            // NRE avoidance
            if (vs.part == null)
            { 
                return; 
            }

            // Engine off => stop all visual effects
            if (!vs.EngineState && vs.Mode == GNVisualMode.Drive)
            {
                SetOff(vs);
                return;
            }

            // Engine:On => update visual effects
            float level = GetLevel(vs);

            UpdateRotor(vs.Rotors, vs.RotorSpeed,level);
            UpdateGlow(vs.EmissiveRenderers, vs.GlowLights, vs.ParticleColor, level);
            if (vs.Mode == GNVisualMode.Drive) UpdateParticle(vs.ParticleEmitters, vs.ParticleColor, level);   
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
            //Debug.Log("[GN] GNparticle amount: " + res.amount + "/" + res.maxAmount);
            return (float)(res.amount / res.maxAmount);
        }

        private static void UpdateRotor(Transform[] rotors, float rotorspeed, float level)// rotate rotors
        {
            //Debug.Log("[GN] Updating rotors.");
            if (rotors == null) return;
            float dt = Time.deltaTime;
            float speed = rotorspeed * level; // magic number should be adjusted later.
            //Debug.Log("[GN] Rotor speed: " + speed);

            // Rotation control logic here.
            //Debug.Log("[GN] Rotors count: " + rotors.Length);
            for (int i = 0; i < rotors.Length; i++)
            {
                //Debug.Log("[GN] Updating rotor " + i);
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
                //Debug.Log("[GN] ParticleEmitter found.");

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
