using KSP;
using System;
using System.Linq;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;

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
        public float Smoothed; //from 0 to 1
        public float RotorSpeed; // rotor speed (degree per second)

        //for move
        public float MoveDistance; // max move distance for thruster parts.
        public float RotAngleX; // target rotation angle X for specific rotating parts 
        public float RotAngleY; // target rotation angle Y for specific rotating parts
        public float RotAngleZ; // target rotation angle Z for specific rotating parts
        public bool MoveOn; // true:move parts on, false:move parts off
        public bool EngineState; //true:engine on, false:engine off

        // Transforms and renderers
        public Transform[] Rotors; //rotor transforms
        public Light[] GlowLights; //glow light sources
        public Renderer[] EmissiveRenderers; //emissive renderers(rotor, stator)
        public KSPParticleEmitter[] ParticleEmitters; //EMI (particle emitters)
        public Transform[] MovingParts; // for thrusters
        public Transform[] RotParts; // for specific angle rotating parts

        // for propulsion vectors
        public Vector3d ThrustVector; //combined thrust vector

        public static GNVisualState Empty => new GNVisualState
        {
            // Basic info
            part = null,
            Mode = GNVisualMode.Condenser,
            ParticleColor = Color.black,
            InputLevel = 0f,
            Smoothed = 0f,
            RotorSpeed = 0,
            MoveDistance = 0f,
            RotAngleX = 0f,
            RotAngleY = 0f,
            RotAngleZ = 0f,
            MoveOn = false,
            EngineState = false,

            // Transforms and renderers
            Rotors = Array.Empty<Transform>(),
            GlowLights = Array.Empty<Light>(),
            EmissiveRenderers = Array.Empty<Renderer>(),
            ParticleEmitters = Array.Empty<KSPParticleEmitter>(),
            MovingParts = Array.Empty<Transform>(),
            RotParts = Array.Empty<Transform>(),

            // for propulsion vectors
            ThrustVector = Vector3d.zero
        };
    }

    public static class GNVisuals
    {
        // consts
        private static float StepBase = 0.5f;
        private static float _nextLogAt = 0f;

        public static void SetOff(in GNVisualState vs)// for initialization
        {
            // part is null=> do nothing
            if (vs.part == null) return;

            float smoothed;// for smoothed rotation.

            // stop all visual effects
            UpdateRotor(vs.Rotors, 0f,0f); // no rotation
            UpdateMove(vs.MovingParts,0f, 0f);
            UpdateGlow(vs.EmissiveRenderers, vs.GlowLights, vs.ParticleColor, 0f, out smoothed);
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

            float smoothed;// for smoothed rotation.

            UpdateRotor(vs.Rotors, 0f, 0f); // no rotation
            UpdateMove(vs.MovingParts, level, vs.MoveDistance);
            UpdateGlow(vs.EmissiveRenderers, vs.GlowLights, vs.ParticleColor, level, out smoothed); // Condenser glow
            UpdateParticle(vs.ParticleEmitters, vs.ParticleColor, 0f, vs.part.vessel, vs); // no particle emission
        }

        public static void UpdateVisual(ref GNVisualState vs)
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
            float smoothed;// for smoothed rotation.
            float step = StepBase * Time.deltaTime;

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
                if (brakes && speed < 0.05) lv = 0.1f;/////

                // normal update
                UpdateGlow(vs.EmissiveRenderers, vs.GlowLights, vs.ParticleColor, level, out smoothed);
                UpdateParticle(vs.ParticleEmitters, vs.ParticleColor, lv, vessel, vs);

                // smoothed should be determined by previous step smoothed and step.
                smoothed = Mathf.MoveTowards(vs.Smoothed, lv, step);
            }
            else
            {
                // if brake, less particle emission and light.
                if (brakes && speed < 0.05 && (vs.Mode == GNVisualMode.Drive)) level = 0.1f;

                // normal update
                if (vs.MoveOn) UpdateMove(vs.MovingParts, level, vs.MoveDistance);
                else UpdateMove(vs.MovingParts, 0f, vs.MoveDistance); // Moving parts
                UpdateGlow(vs.EmissiveRenderers, vs.GlowLights, vs.ParticleColor, level, out smoothed);
                if (vs.Mode != GNVisualMode.Condenser) UpdateParticle(vs.ParticleEmitters, vs.ParticleColor, level, vessel, vs);
            }

            // use modified level
            UpdateRotor(vs.Rotors, vs.RotorSpeed, smoothed); // usual
            UpdateRotation(vs.RotParts, vs.RotAngleX, vs.RotAngleY, vs.RotAngleZ, 30f, vs.MoveOn); // specific rotating parts. later determine speed.
            vs.Smoothed = smoothed;
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

        private static void UpdateMove(Transform[] MoveObjects, float level, float dist)// Move Thruster Parts if needed.
        {
            if (MoveObjects == null) return;

            float maxOffset = dist;
            float targetY = -1 * Mathf.Clamp01(level) * maxOffset;  // 目標位置
            float speed = StepBase;                  // m/s など
            float step = speed * Time.deltaTime; // フレーム依存を解決

            for (int i = 0; i < MoveObjects.Length; i++)
            {
                var t = MoveObjects[i];
                if (!t) continue;

                // 目標位置 (Vector3.zero が基準)
                Vector3 target = new Vector3(0f, targetY, 0f);

                // 現在位置
                Vector3 current = t.localPosition;

                // MoveTowards なら絶対に振動しない
                t.localPosition = Vector3.MoveTowards(current, target, step);
            }
        }

        private static void UpdateRotation(Transform[] RotaionObjects, float targetangleX, float targetangleY, float targetangleZ, float speed, bool OnOff)
        {
            if (RotaionObjects == null) return;

            // angle convert to Quaternion
            // if onoff then target angle, else 0 angle.
            var AngleX = targetangleX;
            var AngleY = targetangleY;
            var AngleZ = targetangleZ;

            if (!OnOff)
            {
                AngleX = 0f;
                AngleY = 0f;
                AngleZ = 0f;
            }

            Quaternion target = Quaternion.Euler(AngleX, AngleY, AngleZ);

            for (int i = 0; i < RotaionObjects.Length; i++)
            {
                var t = RotaionObjects[i];

                t.localRotation = Quaternion.RotateTowards(t.localRotation,target, speed * Time.deltaTime); // deltaTime/frame rate independent
            }
        }

        private static void UpdateGlow(Renderer[] renderers, Light[] lights, Color c, float level, out float smoothedLevel) // ← スムーズされたレベルを外に出す
        {
            float step = StepBase * Time.deltaTime;

            // ループに入る前のデフォルト値（renderersが無い時用に一応）
            smoothedLevel = level;

            // 目標色（EmissiveとLight共通）
            Color targetColor = c * level;
            targetColor.a = level;

            // Emissive
            if (renderers != null)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    var r = renderers[i];
                    if (!r) continue;

                    var mat = r.material; // 個別インスタンス
                    if (mat == null || !mat.HasProperty("_EmissiveColor")) continue;

                    // 現在の EmissiveColor を取得（=前フレームの状態）
                    Color now = mat.GetColor("_EmissiveColor");

                    // 各成分を MoveTowards
                    float nr = Mathf.MoveTowards(now.r, targetColor.r, step);
                    float ng = Mathf.MoveTowards(now.g, targetColor.g, step);
                    float nb = Mathf.MoveTowards(now.b, targetColor.b, step);
                    float na = Mathf.MoveTowards(now.a, targetColor.a, step);

                    Color outColor = new Color(nr, ng, nb, na);

                    mat.SetColor("_EmissiveColor", outColor);

                    // このマテリアルに対して適用した最終αを「スムーズ済みレベル」として利用
                    smoothedLevel = na;
                }
            }

            // Light
            if (lights != null)
            {
                float baseIntensity = 10f;
                float baseRange = 5f;
                float targetInt = baseIntensity * level;
                float targetRange = baseRange * level;

                for (int i = 0; i < lights.Length; i++)
                {
                    var l = lights[i];
                    if (!l) continue;

                    float nowInt = l.intensity;
                    float nowRange = l.range;
                    Color nowCol = l.color;

                    // float
                    float newInt = Mathf.MoveTowards(nowInt, targetInt, step);
                    float newRange = Mathf.MoveTowards(nowRange, targetRange, step);

                    // Color成分
                    float lr = Mathf.MoveTowards(nowCol.r, targetColor.r, step);
                    float lg = Mathf.MoveTowards(nowCol.g, targetColor.g, step);
                    float lb = Mathf.MoveTowards(nowCol.b, targetColor.b, step);
                    float la = Mathf.MoveTowards(nowCol.a, targetColor.a, step);

                    Color newColor = new Color(lr, lg, lb, la);

                    l.intensity = newInt;
                    l.range = newRange;
                    l.color = newColor;
                }
            }
        }

        private static void UpdateParticle(KSPParticleEmitter[] emitters, Color particleColor, float level, Vessel vessel, GNVisualState vs)
        {
            // initial check
            if (emitters == null) return;

            // calculation for tMin/tMax
            float levelMult = vessel.ActionGroups[KSPActionGroup.Brakes] ? Mathf.Clamp((float)vs.ThrustVector.magnitude, 0.1f, 1.0f) : level; // 0.05f needed for stable emission of particles for unknown reason.

            // seettings
            float tMin = 7000f * Mathf.Pow(levelMult, 2f);
            float tMax = 9000f * Mathf.Pow(levelMult, 2f);
            float emissionSpeed =3500f;
            float DynamicsEnableLevel = 0.2f;

            for (int i = 0; i < emitters.Length; i++)
            {
                var e = emitters[i];
                if (!e) continue;

                // Smoothly adjust emission rates
                if(!e.enabled) e.enabled = true;
                if(!e.emit) e.emit = true;

                // smooth change
                e.minEmission = Mathf.RoundToInt(
                    Mathf.MoveTowards(e.minEmission, tMin, emissionSpeed * Time.deltaTime)
                );
                e.maxEmission = Mathf.RoundToInt(
                    Mathf.MoveTowards(e.maxEmission, tMax, emissionSpeed * Time.deltaTime)
                );

                e.localVelocity = new Vector3(0f, -1 * Mathf.Lerp(5f, 45f, levelMult), 0f);

                var ps = e.GetComponent<ParticleSystem>();
                var keys = e.colorAnimation; // Color[5]

                // IMPORTANT NOTE:
                // Shuriken ParticleSystem becomes unstable with ForceOverLifetime
                // when particle count is too low.
                // Dynamics is intentionally disabled at low throttle levels.

                // particle coloring system
                SetEmitterColor_PS(ps, particleColor, keys);

                // particle dynamics system
                if (level > DynamicsEnableLevel) // avoid Shuriken buggy behavior when too less particle.
                    SetEmitterDynamics_PS(ps, levelMult, vessel, vs.ThrustVector);
                else
                    SetEmitterDynamicsOff(ps);

                //DumpEmitter(e, "GN");
            }
        }

        private static readonly int TintId = Shader.PropertyToID("_TintColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private static void SetEmitterColor_PS(ParticleSystem ps, Color c, Color[] keys)//KSPParticleEmitter e
        {
            var main = ps.main;

            // αは e.colorAnimation の [0],[2],[4] を使う（無ければデフォルト）
            float a0 = 1f, a2 = 0.35f, a4 = 0.02f;
            //var keys = e.colorAnimation; // Color[5] 
            if (keys != null && keys.Length >= 5) { a0 = keys[0].a; a2 = keys[2].a; a4 = keys[4].a; }

            // startColor（出生色）と、Color Over Lifetime（全期間の色）を設定
            main.startColor = new ParticleSystem.MinMaxGradient(c); // new Color(c.r, c.g, c.b, 1f));

            var colOL = ps.colorOverLifetime;
            colOL.enabled = true;

            var g2 = new Gradient();
            g2.SetKeys(new[]{new GradientColorKey(new Color(c.r,c.g,c.b), 0f), new GradientColorKey(new Color(c.r,c.g,c.b), 1f)}, new[]{new GradientAlphaKey(a0, 0f), new GradientAlphaKey(a2, 0.5f), new GradientAlphaKey(a4, 1f)});
            colOL.color = new ParticleSystem.MinMaxGradient(g2);
        }

        private static void SetEmitterDynamics_PS(ParticleSystem ps, float level, Vessel vessel, Vector3 BackVector) //KSPParticleEmitter e
        {

            var main = ps.main;
            float startSpeed = 45f;
            float accelBase = 100f;

            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startSpeed = startSpeed;    // 例：20f;

            Vector3 vesselBackWorld = -1f * BackVector; //GetCombinedThrustVector(vessel, 1.0f);// -vessel.ReferenceTransform.up.normalized;
            Vector3 vesselBackLocal = ps.transform.InverseTransformDirection(vesselBackWorld);
            vesselBackLocal.Normalize();

            var fol = ps.forceOverLifetime;
            fol.enabled = true;
            fol.space = ParticleSystemSimulationSpace.Local;

            float accel = accelBase * level; // 例：50f * throttle

            fol.x = new ParticleSystem.MinMaxCurve(vesselBackLocal.x * accel);
            fol.y = new ParticleSystem.MinMaxCurve(vesselBackLocal.y * accel);
            fol.z = new ParticleSystem.MinMaxCurve(vesselBackLocal.z * accel);
        }

        private static void SetEmitterDynamicsOff(ParticleSystem ps)
        {
            var fol = ps.forceOverLifetime;
            fol.enabled = false;
        }

        private static string GetPath(Transform t)
        {
            if (!t) return "<null>";
            var sb = new System.Text.StringBuilder(t.name);
            while (t.parent)
            {
                t = t.parent;
                sb.Insert(0, t.name + "/");
            }
            return sb.ToString();
        }

        private static void UpdateRotor(Transform[] rotors, float rotorspeed, float level)// rotate rotors, note that this sub itself doesn't depends on previous state.
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
        private static readonly Color DefaultColor = new Color(1f, 0f, 0.15f, 1f);

        public static Color ParticleColor(in GNVisualState vs)
        {
            if (vs.part == null) return DefaultColor;

            var res = vs.part.Resources["TopologicalDefects"];
            if (res == null || res.maxAmount <= 0) return DefaultColor;

            float ratio = (float)(res.amount / res.maxAmount);
            float r = 1f - ratio;
            float g = ratio;
            float b = (40f * (1 - g) + 170f * g) / 255f;

            return new Color(r, g, b, 1f);
        }
    }

}