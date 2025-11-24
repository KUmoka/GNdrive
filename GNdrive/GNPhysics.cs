using GNTechnology;
using KSP;
using System;
using System.Linq;
using System.Security.Principal;
using UnityEngine;
using UnityEngine.Scripting;
using static FinePrint.ContractDefs;

namespace GNTechnology
{
    public struct GNPhysicsState
    {
        // Basic drive parameter
        public Part part; // the part that module attached on.
        public bool EngineState; // Engine on/off.
        public bool AgOn; // Anti-gravity
        public bool HvOn; // Hover
        public bool TaOn; // TRANS-AM
        public bool ECOn; // EC to particle converter.
        public bool SafeGuard; // limit drive power or not to prevent dry-up.
        public bool Shortage; // whether the GN particle is in shortage.
        //public bool IsFirstUpdate; // whether this is the first update after SafeGuard + engine on.
        public float ParticlePower; // Max power of the drive can exert
        public float ParticleGenRate; // particle generation rate
        public float MaxG; // for max acceleration.
        public double UsedGNParticle; // amount of GN particle used in last update.

        //Twin drive parameter
        public float Individuality;
        public float SyncRate; // for Twin-drive
        public bool UnSync; // below twin drive sync rate, this = true.
        public bool GNdepleted; // whether the EC is depleted.
        public bool ECdepleted; // whether the EC is depleted.

        public static GNPhysicsState Empty => new GNPhysicsState
        {
            // Basic defaults
            EngineState = false,
            AgOn = false,
            HvOn = false,
            TaOn = false,
            ECOn = false,
            SafeGuard = true,
            Shortage = false,
            //IsFirstUpdate = true,
            ParticlePower = 0f,
            ParticleGenRate = 0f,
            MaxG = 0f,
            UsedGNParticle = 0f,

            // Twin drive defaults
            Individuality = 0f,
            SyncRate = 1f,
            UnSync = false,
            GNdepleted = false,
            ECdepleted = false
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
            if (!ps.EngineState) return;// Engine Off
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
            float TotalParticleGenRate = 0f;
            float limitFactor = 1f;
            float accelMag = 0f;
            int driveCount = 0, agCount = 0, hvCount = 0, taCount = 0, pgdrive = 0;

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

            // FP error avoidance
            double epsilon = 1e-2;

            // Break calculation.
            bool brakes = vessel.ActionGroups[KSPActionGroup.Brakes];
            Vector3 vSrf = (Vector3)vessel.srf_velocity;
            float speed = vSrf.magnitude;
            if (speed < 1) speed = 1f; // speed cramp
            float BrakeMag = 1f;
            if (speed == 1) BrakeMag = 0.05f;// speed == 1 is clamp active case.
            Vector3 brakeDir = -vSrf / speed; // unit vector until speed < 1

            // Find active drives per functions.
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
                        if (t.ps.ECOn)
                        {
                            TotalParticleGenRate += t.ps.ParticleGenRate;
                            pgdrive++;
                        } 
                    }
                    else if (m is GNDriveSystem d && d.ps.EngineState)
                    {
                        driveCount++;
                        if (d.ps.AgOn) agCount++;
                        if (d.ps.HvOn) hvCount++;
                        if (d.ps.TaOn) taCount++;
                        TotalParticlePower += d.ps.ParticlePower;
                        TotalParticleGenRate += d.ps.ParticleGenRate;
                        pgdrive++;
                    }
                    else if (m is GNThrusterSystem s && s.ps.EngineState)
                    {
                        driveCount++;
                        if (s.ps.AgOn) agCount++;
                        if (s.ps.HvOn) hvCount++;
                        if (s.ps.TaOn) taCount++;
                        TotalParticlePower += s.ps.ParticlePower;
                    }
                }
            }

            // Sync Rate Bonus calculation (for my drive, bonus is ps.SyncRate * ps.ParticleXXXXX, but particleXXXXX is already added above)
            TotalParticlePower += ps.ParticlePower * (ps.SyncRate - 1);
            TotalParticleGenRate += ps.ParticleGenRate * (ps.SyncRate - 1);

            // hv > ag, disable ag when hv > 0
            if (hvCount > 0) ps.AgOn = false;

            // TRANS-AM mode adjustments
            if (ps.TaOn)
            {
                actualG *= 3f; //Increase actualG in TA mode
                TotalParticlePower += 2f * ps.ParticlePower * ps.SyncRate; // Increase particle power in TA mode, for this drive only, totalparticlepower already includes ps.particlepower, so add 2x here
            }

            // Resource drain calculation
            double mass = vessel.GetTotalMass(); // KSP1.12はdouble

            // Base Thrust, if one adds another force, one shall add like Ag/Hv
            float support = (ps.AgOn ? gLocal : 0f) + (ps.HvOn ? aHover : 0f); // Hv, Ag accel considered here.
            accelMag = (brakes ? brakeDir.magnitude * BrakeMag : ThrustDirection.magnitude) * (actualG - support) + support; // m/s^2, ternary operator

            // consumption calculation
            double consumption = mass * Math.Abs(accelMag) * TimeWarp.fixedDeltaTime; //now include hover consumption, 1G
            TotalParticlePower *= TimeWarp.fixedDeltaTime; // compensation for consumption
            TotalParticleGenRate *= TimeWarp.fixedDeltaTime; // compensation for consumption

            // limit factor calculation. When particle generation on, drive power should suppress sustainable level
            if (ps.SafeGuard)
            {
                //if (pgdrive == 0) pgdrive = 1; // prevent div by 0
                //if (consumption > 0 && consumption > TotalParticleGenRate) limitFactor = (float)(TotalParticleGenRate / (consumption * pgdrive));

                //if (ps.IsFirstUpdate)
                //{
                //    limitFactor = (float)(ps.ParticleGenRate / consumption);
                //} 
                if (consumption > 0 && consumption > ps.UsedGNParticle) limitFactor =(float)(ps.UsedGNParticle / consumption);
            }
            else
            {
                // Drive only, same as SafeGuard.on, but with thruster/condenser drive, limitFactor will increase.
                if (consumption > 0 && consumption > TotalParticlePower) limitFactor = (float)(TotalParticlePower / consumption);
            }

            // consume particle
            double consume = consumption * (double)limitFactor;
            double actualConsumption = ps.part.RequestResource("GNparticle", consume);
            if (actualConsumption < consume - epsilon)
            {
                ps.Shortage = true;
                ps.TaOn = false; // TRANS-AM off
                Debug.Log("GNparticle Shortage: AC=" + actualConsumption);
                Debug.Log("GNparticle Shortage: Con=" + consume + epsilon);
            }
            else
            {
                ps.Shortage = false;
            }

            // --- Engine shut-off check ---
            if (actualConsumption < consume * 0.001f) // not enough particle, 0.1% threshold, for floating point error margin. At this point, GN is empty because actual draw > planned draw * 0.01f
            {
                Debug.Log("GNparticle Empty");
                ps.EngineState = false; // also turn off engine state,
                ps.TaOn = false; // TRANS-AM off
                ps.AgOn = false; // AG off
                ps.HvOn = false; // hover off
                ps.GNdepleted = true; // GN depleted
                return;
            }

            // --- Force application ---
            foreach (Part p2 in vessel.parts)
            {
                if (p2.physicalSignificance != Part.PhysicalSignificance.FULL || p2.rb == null)
                    continue;

                // Anti-gravity, divided by drive count
                if (ps.AgOn && agCount > 0)
                {
                    p2.AddForce(-gee * p2.rb.mass * limitFactor / agCount);
                }
                else
                {
                    gLocal = 0f;
                }

                // Hover calculation.
                if (ps.HvOn && aHover > 0f)
                {
                    float forceN = aHover * p2.rb.mass * limitFactor;  // [N] = [m/s^2] * [kg]
                    p2.AddForce(upHv * forceN / hvCount);
                }
                else
                {
                    aHover = 0f;
                }

                // Main thrust or brakes, it shouldn't be less than 0
                float ThrustBudget = Mathf.Max(0f, actualG - aHover - gLocal);

                // Calc force
                p2.AddForce((brakes ? brakeDir * BrakeMag : ThrustDirection) * ThrustBudget * limitFactor * p2.rb.mass);
            }
        }

        static float _hoverLastA = 0f;      // Previous accel rate [m/s^2]（for through rate）
        const float HoverVHard = 1.0f;      // 強ブレーキ域しきい値 |v|>=これで全力減速
        const float HoverEps = 0.001f;    // デッドバンド（これ以下なら0扱い）
        const float HoverSlewA = 30f;

        private static float ComputeHoverAccel(float v, float gLocal, float dt, float aMax)
        {
            // Ask ChatGPT
            float a_cmd;
            float av = Mathf.Abs(v);

            if (av >= HoverVHard)
                a_cmd = -Mathf.Sign(v) * aMax;                       // Strong break
            else if (av > HoverEps)
                a_cmd = Mathf.Clamp(-v / (2f * dt), -aMax, aMax);    // 1/2 (半減則（次フレで速度を半分に）)
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

    public static class GNGenerationFurnace
    {
        private static double difficulty = 0.1f;

        public static void ParticleSupply(ref GNPhysicsState ps, double dt)
        {
            // Basic Check
            if (!ps.ECOn) return; // EC off -> no generation
            if (ps.part == null) return; // No part -> no generation

            // variables
            double ECReqGen = 0f;
            double ErrorMargin = 0.01f;
            var TD = ps.part.Resources["TopologicalDefects"];
            double lack = (TD.maxAmount - 2 * TD.amount);   // TD shortage
            double GenRateDt = ps.ParticleGenRate * dt;
            bool isNotEnoughPower = false;

            // GN and Tau
            ECReqGen = GenRateDt * lack * difficulty; // EC required proportional to lack of TD
            double GNGen = GenRateDt * ps.SyncRate;
            if (GNGen == 0f ) return; // no generation needed.

            // EC drain
            double Pulled = ps.part.RequestResource("ElectricCharge", ECReqGen); // EC pulled here, if not enough EC, Pulled < ECReqGen

            // EC empty, 1f threshold
            if (Pulled <= 1f && ECReqGen > 0)
            {
                FurnaceCutOff(ref ps);
                return;
            }

            // EC not enough
            if (Pulled < ECReqGen + ErrorMargin) // for fp error margin
            {
                GNGen *= (Pulled / ECReqGen); // scale down GN generation
                isNotEnoughPower = true;
            }

            // GN Generation. particle added here. if not enough space, (Abs) actualAdd < GNGen
            double Aadd = Math.Abs(ps.part.RequestResource("GNparticle", (double)(-1 * GNGen)));
            ps.UsedGNParticle = GNGen; // store used GN particle
            ps.ECdepleted = false; // can draw EC = not empty

            // usual case
            if (Aadd == GNGen) return; // No EC return needed.

            // particle nearly full.
            if (Aadd < GNGen) 
            {
                ps.part.RequestResource("ElectricCharge", ECReqGen * ((Aadd - GNGen) / GNGen)); //Return unused EC
                return;
            }

            // Not enough but some GN generated case
            if (isNotEnoughPower)
            {
                FurnaceCutOff(ref ps);
                return;// Debug.Log("Not enough EC for GN generation.");
            }
        }

        private static void FurnaceCutOff(ref GNPhysicsState ps)
        {
            Debug.Log("EC depleted during GN particle generation.");
            ps.ECOn = false;
            ps.ECdepleted = true;
        }
    }
}