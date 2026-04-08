using GNTechnology;
using KSP;
using System;
using System.Linq;
using System.Security.Principal;
using UnityEngine;
using UnityEngine.Scripting;
using VehiclePhysics;
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
        public float RCSFactor; // RCS factor for thruster drives.
        public double UsedGNParticle; // amount of GN particle used in last update.

        //Twin drive parameter
        public float Individuality;
        public float SyncRate; // for Twin-drive
        public bool UnSync; // below twin drive sync rate, this = true.
        public bool GNdepleted; // whether the EC is depleted.
        public bool ECdepleted; // whether the EC is depleted.

        // For brake vectors
        public Vector3 ThrustDir;

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
            RCSFactor = 0f,
            UsedGNParticle = 0f,

            // Twin drive defaults
            Individuality = 0f,
            SyncRate = 1f,
            UnSync = false,
            GNdepleted = false,
            ECdepleted = false,

            //for brake vectors
            ThrustDir = Vector3.zero
        };
    }

    public static class GNPhysics
    {
        // for force calculation
        private struct ForceContext
        {
            public bool AgOn, HvOn, Brakes;
            public int AgCount, HvCount;
            public float LimitFactor;
            public float GLocal;
            public float AHover;
            public Vector3 UpHv;
            public Vector3 BrakeDir;
            public float BrakeMag;
            public Vector3 ThrustDir;
            public float NeededThrustBudget;

            private static ForceContext Empty => new ForceContext
            {
                AgOn = false,
                HvOn = false,
                Brakes = false,
                AgCount = 0,
                HvCount = 0,
                LimitFactor = 1f,
                GLocal = 0f,
                AHover = 0f,
                UpHv = Vector3.zero,
                BrakeDir = Vector3.zero,
                BrakeMag = 0f,
                ThrustDir = Vector3.zero,
                NeededThrustBudget = 0f
            };
        }

        // FP error avoidance
        private static double epsilon = 1e-1;
        private static double fraction = 1e-2;

        public static void SetOff(in GNPhysicsState ps)
        {
            if (ps.part == null) return;
        }

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
            float actualG = ps.MaxG * 9.8f; // m/s^2, theoretical max accel
            float limitFactor = 1f;
            int agCount = 0, hvCount = 0;

            // Normalize input vector
            float norm = Mathf.Sqrt(x * x + y * y + z * z);
            float w = 0f;
            if (norm >= fraction)
            {
                w = (1f - throttle) / norm;
                w = Mathf.Clamp01(w);
            }

            // Vector
            var up = vessel.ReferenceTransform.up;
            var fwd = vessel.ReferenceTransform.forward;
            var rgt = vessel.ReferenceTransform.right;
            Vector3 ThrustDirection = (up * z * w + fwd * y * w + rgt * x * w) + up * throttle; //user can adjust this balance with RCS factor. Pushing forward (positive y) will produce forward thrust.
            Vector3 gee = FlightGlobals.getGeeForceAtPosition(vessel.transform.position); // m/s^2

            // Hover acceleration calculation
            Vector3d gAcc = gee;
            float gLocal = (float)gAcc.magnitude;
            Vector3 upHv = -(Vector3)gAcc.normalized;   // 上向き（AddForceに掛ける方向）            
            float vVert = (float)vessel.verticalSpeed;// 縦速度（KSPなら vessel.verticalSpeed が地表基準の鉛直速度）
            float aMax = Mathf.Max(0f, ps.MaxG * 9.80665f);// ユーザー設定上限（例：MaxG[G] → [m/s^2]へ換算）ps.MaxG が 1=1G といった意味なら：
            float aHover = ps.HvOn ? ComputeHoverAccel(vVert, gLocal, Time.fixedDeltaTime, aMax) : 0f;// ホバー用の上向き必要加速度を1回だけ計算  

            // Break calculation.
            bool brakes = vessel.ActionGroups[KSPActionGroup.Brakes];
            Vector3 vSrf = (Vector3)vessel.srf_velocity;
            float speed = vSrf.magnitude;
            if (speed < 1) speed = 1f; // speed cramp
            float BrakeMag = (speed == 1) ? 0.05f : 1f; // 0.05f is speed is less than 1m/s
            //if (speed == 1) BrakeMag = 0.05f;// speed == 1 is clamp active case.
            Vector3 brakeDir = -vSrf / speed; // unit vector until speed < 1

            // Trans-am mode multiplier
            float taMult = ps.TaOn ? 3f : 1f;

            CalculateDriveCounts(ref agCount, ref hvCount, vessel);

            // hv > ag, disable ag when hv > 0
            if (hvCount > 0)
            {
                ps.AgOn = false;
            }

            // TRANS-AM mode adjustments
            if (ps.TaOn)
            {
                actualG *= 3f; //Increase actualG in TA mode
            }

            // Resource drain calculation
            double mass = vessel.GetTotalMass(); // KSP1.12はdouble
            double UnitConsumption = mass * TimeWarp.fixedDeltaTime; ; // unit particle consumption for Accel=1m/s^2 per second
            double particleGenRateDelta = ps.ParticleGenRate * TimeWarp.fixedDeltaTime;// compensation for consumption
            double particlePowerDelta = ps.ParticlePower * TimeWarp.fixedDeltaTime;// compensation for consumption

            // Actual acceleration magnitude calculation
            double AccelLimit = ((ps.TaOn ? ps.ParticlePower * 3 : ps.ParticlePower) * ps.SyncRate) * TimeWarp.fixedDeltaTime / UnitConsumption; // m/s^2, max accel that drive can provide. Twin-drive sync rate considered here.

            // support calculation
            float agSupport = (ps.AgOn && agCount > 0) ? gLocal / agCount : 0f;
            float hvSupport = (ps.HvOn && hvCount > 0) ? aHover / hvCount : 0f;
            float support = agSupport + hvSupport;
            //float support = (ps.AgOn ? gLocal : 0f) + (ps.HvOn ? aHover : 0f); // Hv, Ag accel considered here.
            double ThrustBudget = Mathf.Max(0f, (float)AccelLimit - support); // m/s^2, max thrust budget after Hv, Ag considered.if not enough, 0.
            double NeededThrustBudget = Math.Min(ThrustBudget, actualG); // m/s^2, needed thrust budget according to actualG.

            // if not enough power for Hv and Ag, limitFactor will be less than 1f.
            if (support > 0)
            {
                limitFactor = Mathf.Min((float)AccelLimit / support, 1f);
            }

            // Ag, Hv > Brake > thrust, priority order.
            double consumption = Math.Min((double)support * UnitConsumption, particlePowerDelta) ; // first, consume for hover and anti-gravity.if not enough power, consume all power for them.
            consumption += (brakes ? NeededThrustBudget * BrakeMag * Mathf.Clamp01(vSrf.magnitude ) * UnitConsumption : NeededThrustBudget * ThrustDirection.magnitude * UnitConsumption);  // then, consume for thrust or brakes.           

            // Particle Consumption calculation with SafeGuard and generation consideration
            double consume = consumption; // Basic assumption

            // case sagfeguard on
            if (ps.SafeGuard && consumption > 0)
            {
                consume = (double)Mathf.Clamp((float)consumption, 0f, (float)particleGenRateDelta * taMult);
                limitFactor = Mathf.Min((float)particleGenRateDelta * taMult / (float)consumption, 1f); // usually, drive exert all the power. so it will be = ps.particleGenRate/ps.particlePower.
            }

            // GN particle actual consumption
            double actualConsumption = ps.part.RequestResource("GNparticle", consume);
            ps.Shortage = ParticleShortageCheck(actualConsumption, consume, epsilon, ref ps, ref limitFactor);

            // --- Engine shut-off check ---
            if (EngineShutOffCheck(actualConsumption, consume, fraction, ref ps))
            {
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
                if (ps.HvOn && aHover > 0)
                {
                    float forceN = aHover * p2.rb.mass * limitFactor;  // [N] = [m/s^2] * [kg]
                    p2.AddForce(upHv * forceN / hvCount);
                }
                else
                {
                    aHover = 0f;
                }

                //// Calc force
                p2.AddForce((brakes ? brakeDir * BrakeMag : ThrustDirection) * (float)NeededThrustBudget * limitFactor * p2.rb.mass);
            }

            ps.ThrustDir = (brakes ? brakeDir : ThrustDirection);
        }

        private static void CalculateDriveCounts(ref int myAgCount, ref int myHvCount, Vessel vessel)
        {
            foreach (Part p1 in vessel.parts)
            {
                foreach (PartModule m in p1.Modules)
                {
                    if (m is GNCondenserDriveSystem c && c.ps.EngineState)
                    {
                        if (c.ps.AgOn) myAgCount++;
                        if (c.ps.HvOn) myHvCount++;
                    }
                    else if (m is GNDriveTauSystem t && t.ps.EngineState)
                    {
                        if (t.ps.AgOn) myAgCount++;
                        if (t.ps.HvOn) myHvCount++;
                    }
                    else if (m is GNDriveSystem d && d.ps.EngineState)
                    {
                        if (d.ps.AgOn) myAgCount++;
                        if (d.ps.HvOn) myHvCount++;
                    }
                    else if (m is GNThrusterSystem s && s.ps.EngineState)
                    {
                        if (s.ps.AgOn) myAgCount++;
                        if (s.ps.HvOn) myHvCount++;
                    }
                }
            }
        }

        private static bool EngineShutOffCheck(double myActualConsumption, double myConsume, double fraction, ref GNPhysicsState ps)
        {

            if (myActualConsumption < myConsume * fraction)
            {
                Debug.Log("GNparticle Empty");
                ps.EngineState = false; // also turn off engine state,
                ps.TaOn = false; // TRANS-AM off
                ps.AgOn = false; // AG off
                ps.HvOn = false; // hover off
                ps.GNdepleted = true; // GN depleted
                return true;
            }

            return false;
        }

        private static bool ParticleShortageCheck(double myActualConsumption, double myConsume, double fraction, ref GNPhysicsState ps, ref float myLimitFactor)
        {

            if (myActualConsumption < myConsume - fraction)
            {
                ps.TaOn = false; //TRANS-AM off
                Debug.Log("GNparticle Shortage: AC=" + myActualConsumption);
                Debug.Log("GNparticle Shortage: Con=" + myConsume + fraction);
                myLimitFactor = (float)(myActualConsumption / myConsume);

                return true;
            }

            return false;
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
        // Edge case problemhere. later fix! -> TRANS-AM mode now won't generaste particles for prevents infinite trans-am mode.

        private static double difficulty = 0.1f;
        private static double fraction = 5e-3; // 0.5% threshold

        public static void ParticleSupply(ref GNPhysicsState ps, double dt)
        {
            // Basic Check
            if (!ps.ECOn) return; // EC off -> no generation
            if (ps.part == null) return; // No part -> no generation

            // variables
            double ECReqGen = 0f;
            double ErrorMargin = 0.01f;
            var TD = ps.part.Resources["TopologicalDefects"];
            //var GNP = ps.part.Resources["GNparticle"];
            var EC = ps.part.Resources["ElectricCharge"];
            double lack = (TD.maxAmount - 2 * TD.amount);   // TD shortage
            double GenRateDt = ps.ParticleGenRate * dt;
            bool isNotEnoughPower = false;
            bool isTaOn = ps.TaOn;

            // GN and Tau
            ECReqGen = GenRateDt * lack * difficulty; // EC required proportional to lack of TD
            double GNGen = GenRateDt * ps.SyncRate;
            if (GNGen == 0f ) return; // no generation needed.

            // EC drain
            double Pulled = ps.part.RequestResource("ElectricCharge", ECReqGen); // EC pulled here, if not enough EC, Pulled < ECReqGen

            // EC empty, 1f threshold. not sure if this is needed.
            if (Pulled <= 1f * dt && ECReqGen > 0)
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

            // EC deplition check, 0.5% threshold
            if (ECDepletionCheck(EC.amount, EC.maxAmount, fraction, ECReqGen, ref ps))
            {
                return;
            }

            // GN Generation. particle added here. if not enough space, (Abs) actualAdd < GNGen
            // only if TRANS-AM off.
            if (isTaOn)
            {
                return;
            }

            double Aadd = Math.Abs(ps.part.RequestResource("GNparticle", (double)(-1 * GNGen)));
            ps.UsedGNParticle = GNGen; // store used GN particle
            ps.ECdepleted = false; // can draw EC = not empty

            // usual case
            if (Aadd == GNGen) return; // No EC return needed.

            // particle nearly full. 
            if (Aadd < GNGen) 
            {
                var ECs = ECReqGen * ((Aadd - GNGen) / GNGen);
                if (ECs < 0) // return only when ECs is negative
                {
                    ps.part.RequestResource("ElectricCharge", ECs); //Return unused EC
                }
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
            //Debug.Log("EC depleted during GN particle generation.");
            ps.ECOn = false;
            ps.ECdepleted = true;
        }

        private static bool ECDepletionCheck(double myECAmount, double myECMax, double myFraction, double myECRecGen, ref GNPhysicsState ps)
        {
            // try pull 1 EC to check amount
            double pulledFraction = ps.part.RequestResource("ElectricCharge", 1d); 

            // if EC = 0.0 then satify this condition.
            if (myECAmount <= myECMax * myFraction && myECRecGen > 0 && pulledFraction < myFraction) 
            {
                FurnaceCutOff(ref ps);
                return true;
            }

            // Check complete, return 1 EC.
            pulledFraction = ps.part.RequestResource("ElectricCharge", -1d); 

            return false;
        }
    }
}