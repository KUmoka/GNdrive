using KSP;
using System;
using System.Linq;
using UnityEngine;

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
        public bool MoveOn; // true:move parts on, false:move parts off
        public bool EngineState; //true:engine on, false:engine off

        // Transforms and renderers
        public Transform[] Rotors; //rotor transforms
        public Light[] GlowLights; //glow light sources
        public Renderer[] EmissiveRenderers; //emissive renderers(rotor, stator)
        public KSPParticleEmitter[] ParticleEmitters; //EMI (particle emitters)
        public Transform[] MovingParts; // for thrusters

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
            MoveOn = false,
            EngineState = false,

            // Transforms and renderers
            Rotors = Array.Empty<Transform>(),
            GlowLights = Array.Empty<Light>(),
            EmissiveRenderers = Array.Empty<Renderer>(),
            ParticleEmitters = Array.Empty<KSPParticleEmitter>(),
            MovingParts = Array.Empty<Transform>()
        };
    }

    public static class GNVisuals
    {
        // consts
        private static float StepBase = 0.5f;

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
            UpdateParticle(vs.ParticleEmitters, vs.ParticleColor, 0f, vs.part.vessel); // no particle emission
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
                UpdateParticle(vs.ParticleEmitters, vs.ParticleColor, lv, vessel);

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
                if (vs.Mode != GNVisualMode.Condenser) UpdateParticle(vs.ParticleEmitters, vs.ParticleColor, level, vessel);
            }

            // use modified level
            UpdateRotor(vs.Rotors, vs.RotorSpeed, smoothed); // usual
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
                float targetInt = baseIntensity * level;
                float targetRange = level;

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

        private static void UpdateParticle(KSPParticleEmitter[] emitters, Color particleColor, float level, Vessel vessel)
        {
            // initial check
            if (emitters == null) return;

            // seettings
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
                var ps = e.GetComponent<ParticleSystem>();
                var keys = e.colorAnimation; // Color[5]

                // particle coloring system
                SetEmitterColor_PS(ps, particleColor, keys);

                // particle dynamics system
                SetEmitterDynamics_PS(ps, level, vessel);
                //DumpEmitter(e, "GN");
            }
        }

        private static readonly int TintId = Shader.PropertyToID("_TintColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private static void SetEmitterColor_PS(ParticleSystem ps, Color c, Color[] keys)//KSPParticleEmitter e
        {
            //if (!e) return; // prevents NRE

            //var ps = e.GetComponent<ParticleSystem>();
            //if (ps == null) return; // no particle system -> return
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

        private static void SetEmitterDynamics_PS(ParticleSystem ps, float level, Vessel vessel) //KSPParticleEmitter e
        {
            //if (!e) return; // prevents NRE

            //var ps = e.GetComponent<ParticleSystem>();
            //if (ps == null) return; // no particle system -> return
            var main = ps.main;
            float startSpeed = 45f;
            float accelBase = 100f;

            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startSpeed = startSpeed;    // 例：20f;

            Vector3 vesselBackWorld = GetCombinedThrustVector(vessel, 1.0f);// -vessel.ReferenceTransform.up.normalized;
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

        private static Vector3 GetCombinedThrustVector(Vessel v, float rcsInfluence = 1.0f)
        {
            if (v == null) return Vector3.zero;

            var s = v.ctrlState;
            Transform rt = v.ReferenceTransform;

            // --- RCS入力方向（動かしたい方向） ---
            Vector3 rcsDir =
                rt.right * s.X +   // 左右
                rt.forward * s.Y +   // 前後
                rt.up * s.Z;    // 上下

            // RCSは実推力方向は逆（粒子が流れる方向）
            Vector3 rcsThrust = rcsDir * rcsInfluence;


            // --- メイン推力方向（機体の後方）---
            Vector3 mainThrustDir = -rt.up;  // forwardが前の機体なら rt.forward、上が前なら rt.up
            float mainPower = s.mainThrottle;
            Vector3 mainThrust = mainThrustDir * mainPower;


            // --- 合成 ---
            Vector3 thrustVector = mainThrust + rcsThrust;

            if (thrustVector != Vector3.zero)
                thrustVector.Normalize();

            return thrustVector;
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