using GNTechnology;
using KSP;
using System;
using System.Linq;
using System.Security.Principal;
using UnityEngine;
using UnityEngine.Scripting;

namespace GNTechnology
{
    public struct GNPhysicsState
    {
        public Part part;
        public bool EngineState;
        public bool AgOn;
        public bool HvOn;
        public bool TaOn;
        public bool UnSync;
        public bool ECOn;
        public bool SafeGuard;
        public float ParticlePower;
        public float ParticleGenRate;
        public float MaxG;

        //Twin drive parameter
        public float Individuality;

        public static GNPhysicsState Empty => new GNPhysicsState
        {
            EngineState = false,
            AgOn = false,
            HvOn = false,
            TaOn = false,
            UnSync = false,
            ECOn = false,
            SafeGuard = true,
            ParticlePower = 0f,
            ParticleGenRate = 0f,
            MaxG = 0f,
            Individuality = 0f
        };
    }

    public static class GNPhysics
    {
        public static void SetOff(in GNPhysicsState ps)
        {
            if (ps.part == null) return;
        }

        [Obsolete]
        public static void UpdatePhysics(ref GNPhysicsState ps)
        {
            // --- Basic NRE prevention ---
            if (ps.part == null) return;
            var vessel = ps.part.vessel;
            if (vessel == null) return;
            if (!HighLogic.LoadedSceneIsFlight) return; //  || !vessel.isActiveVessel 
            if (vessel.ReferenceTransform == null) return; // prevents NRE when switching vessels

            // input state 
            var cs = vessel.ctrlState;
            if (cs == null) return; //no control state, do nothing
            float throttle = cs.mainThrottle;
            float y = -cs.Y;
            float x = -cs.X;
            float z = -cs.Z;
            float actualG = ps.MaxG * 9.8f; // m/s^2
            float TotalParticlePower = 0f;
            float limitFactor = 1f;
            float accelMag = 0f;
            int driveCount = 0, agCount = 0, hvCount = 0, taCount = 0;
            double ECReqGen = 0f;
            double ecUsed = 0f;

            // Normalize input vector
            float norm = Mathf.Sqrt(x * x + y * y + z * z);
            float w = 0f;
            if (norm >= 1e-6f)
            {
                w = (1f - throttle) / norm;
                w = Mathf.Clamp01(w);
            }

            // Vector
            var up = vessel.ReferenceTransform.up;
            var fwd = vessel.ReferenceTransform.forward;
            var rgt = vessel.ReferenceTransform.right;
            Vector3 ThrustDirection = up * z * w + fwd * y * w + rgt * x * w + up * throttle;
            Vector3 gee = FlightGlobals.getGeeForceAtPosition(vessel.transform.position); // m/s^2

            // Hover acceleration calculation
            Vector3d gAcc = gee;
            float gLocal = (float)gAcc.magnitude;
            Vector3 upHv = -(Vector3)gAcc.normalized;   // 上向き（AddForceに掛ける方向）            
            float vVert = (float)vessel.verticalSpeed;// 縦速度（KSPなら vessel.verticalSpeed が地表基準の鉛直速度）
            float aMax = Mathf.Max(0f, ps.MaxG * 9.80665f);// ユーザー設定上限（例：MaxG[G] → [m/s^2]へ換算）ps.MaxG が 1=1G といった意味なら：
            float aHover = ps.HvOn ? ComputeHoverAccel(vVert, gLocal, Time.fixedDeltaTime, aMax) : 0f;// ホバー用の上向き必要加速度を1回だけ計算  

            foreach (Part p1 in vessel.parts)
            {
                foreach (PartModule m in p1.Modules)
                {
                    if (m is GNCondenserDriveSystem c && c.ps.EngineState)
                    {
                        driveCount++;
                        if (c.ps.AgOn) agCount++;
                        if (c.ps.HvOn) hvCount++;
                        if (c.ps.TaOn) taCount++;
                        TotalParticlePower += c.ps.ParticlePower;
                    }
                    else if (m is GNDriveTauSystem t && t.ps.EngineState)
                    {
                        driveCount++;
                        if (t.ps.AgOn) agCount++;
                        if (t.ps.HvOn) hvCount++;
                        if (t.ps.TaOn) taCount++;
                        TotalParticlePower += t.ps.ParticlePower;
                    }
                    else if (m is GNDriveSystem d && d.ps.EngineState)
                    {
                        driveCount++;
                        if (d.ps.AgOn) agCount++;
                        if (d.ps.HvOn) hvCount++;
                        if (d.ps.TaOn) taCount++;
                        TotalParticlePower += d.ps.ParticlePower;
                    }
                }
            }

            // TRANS-AM mode adjustments
            if (ps.TaOn)
            {
                actualG *= 3f; // Increase actualG in TA mode
                TotalParticlePower = TotalParticlePower + 2f * ps.ParticlePower; // Increase particle power in TA mode, for this drive only, totalparticlepower already includes ps.particlepower, so add 2x here
            }

            // Particle Generation furnace.
            {
                // EC required for generation
                var TD = ps.part.Resources["TopologicalDefects"];
                if (ps.ECOn)
                {
                    ECReqGen = ps.ParticleGenRate * (TD.maxAmount - 2 * TD.amount) * 0.1; // EC required proportional to lack of TD

                    if (ps.part.Resources["GNparticle"].amount < ps.part.Resources["GNparticle"].maxAmount - 1 * ps.ParticleGenRate * TimeWarp.fixedDeltaTime)
                    {
                        ps.part.RequestResource("ElectricCharge", ECReqGen * TimeWarp.fixedDeltaTime);
                        ps.part.RequestResource("GNparticle", (double)(-1 * ps.ParticleGenRate * TimeWarp.fixedDeltaTime));
                    }
                    
                    if (ps.part.RequestResource("ElectricCharge", 0.5 * TimeWarp.fixedDeltaTime) <= 0)
                    {
                        ps.ECOn = false;
                    }
                }
            }

            // Resource drain calculation
            double mass = vessel.GetTotalMass(); // KSP1.12はdouble
            if (ps.AgOn)
                accelMag = gee.magnitude + ThrustDirection.magnitude * actualG;  // m/s^2
            else
                accelMag = ThrustDirection.magnitude * actualG;                   // m/s^2

            // consumption calculation
            double consumption = mass * Math.Abs(accelMag) * TimeWarp.fixedDeltaTime;
            TotalParticlePower *= TimeWarp.fixedDeltaTime; // compensation for consumption

            // limit factor calculation
            // 0..1想定,When particle generation on, drive power should suppress sustainable level
            if (consumption > 0 && consumption > TotalParticlePower && ps.SafeGuard) limitFactor = (float)((TotalParticlePower) / consumption);
            if (ps.UnSync)limitFactor = 0.001f; // UnSync mode -> power almost off.Later think this to a better implementation.
            if (ps.part.Resources["GNparticle"].amount < 1)
            {
                limitFactor = 0f; // No Particle -> power off
                ps.EngineState = false; // also turn off engine state,
                ps.TaOn = false; // TRANS-AM off
                ps.AgOn = false; // AG off
                ps.HvOn = false; // hover off
                return;
            }

            //Debug.Log("[GN] consumption =" + consumption);
            //Debug.Log("[GN] TotalParticlePower =" + TotalParticlePower);
            //Debug.Log("[GN] limitFactor =" + limitFactor);
            //Debug.Log("[GN] SafeGuard =" + ps.SafeGuard);

            // --- Force application ---
            foreach (Part p2 in vessel.parts)
            {
                if (p2.physicalSignificance != Part.PhysicalSignificance.FULL || p2.rb == null)
                    continue;

                // Main thrust
                p2.AddForce(ThrustDirection * actualG * limitFactor * p2.rb.mass);

                // Anti-gravity, divided by drive count
                if (ps.AgOn && agCount > 0)
                    p2.AddForce(-gee * p2.rb.mass * limitFactor / agCount);

                // Hover処理はここに（必要なら）
                if (ps.HvOn && aHover > 0f)
                {
                    // ここで limitFactor を掛けるなら、ホバーの効きもUIで同率に制限できます
                    float forceN = aHover * p2.rb.mass * limitFactor;  // [N] = [m/s^2] * [kg]
                    p2.AddForce(upHv * forceN / hvCount);
                    double consumptionHv = p2.rb.mass * aHover / hvCount * TimeWarp.fixedDeltaTime;
                    ps.part.RequestResource("GNparticle", consumptionHv);
                }
            }

            // apply consumption
            ps.part.RequestResource("GNparticle", consumption * limitFactor);
        }

        static float _hoverLastA = 0f;      // 前回の上向き加速度 [m/s^2]（スルーレート用）
        const float HoverVHard = 1.0f;      // 強ブレーキ域しきい値 |v|>=これで全力減速
        const float HoverEps = 0.001f;    // デッドバンド（これ以下なら0扱い）
        const float HoverSlewA = 30f;

        private static float ComputeHoverAccel(float v, float gLocal, float dt, float aMax)
        {


            float a_cmd;
            float av = Mathf.Abs(v);

            if (av >= HoverVHard)
                a_cmd = -Mathf.Sign(v) * aMax;                       // 強ブレーキ
            else if (av > HoverEps)
                a_cmd = Mathf.Clamp(-v / (2f * dt), -aMax, aMax);    // 半減則（次フレで速度を半分に）
            else
                a_cmd = 0f;

            // 重力補償を足して、上向きの必要加速度に
            float a_total = a_cmd + gLocal;

            // 物理的に「下向き推力」は出せないので 0 未満は切る
            if (a_total < 0f) a_total = 0f;

            // 推力スパイク回避のためスルーレートで平滑化
            float maxStep = HoverSlewA * dt;
            a_total = Mathf.MoveTowards(_hoverLastA, a_total, maxStep);
            _hoverLastA = a_total;

            return a_total;
        }
    }
}