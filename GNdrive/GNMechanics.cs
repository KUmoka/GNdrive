using GNTechnology;
using System.Collections.Generic;
using UnityEngine;
using static GNTechnology.GNSynchronizer;

namespace GNTechnology
{
    public class GNLists : VesselModule
    {
        // future sync mechanism
        public List<GNThrusterSystem> GNThruster = new List<GNThrusterSystem>();
        public List<GNCondenserDriveSystem> GNCondernserDrive = new List<GNCondenserDriveSystem>();
        public List<GNDriveSystem> GNDrive = new List<GNDriveSystem>();
        public List<GNDriveTauSystem> GNDriveTau = new List<GNDriveTauSystem>();
        public List<GNShieldModule> GNShield = new List<GNShieldModule>();

        public override bool ShouldBeActive()
        {
            // GN系 PartModule を1つでも積んでいれば有効化
            // return false;//vessel != null && (vessel.FindPartModulesImplementing<GNBaseSystem>().Count > 0);
            return true;
        }

        protected override void OnStart()
        {
            base.OnStart();
            MakeDriveList();
            Debug.Log("GNLists created");
        }

        private void MakeDriveList()
        {
            // init
            List<GNThrusterSystem> GNThruster = new List<GNThrusterSystem>();
            List<GNCondenserDriveSystem> GNCondernserDrive = new List<GNCondenserDriveSystem>();
            List<GNDriveSystem> GNDrive = new List<GNDriveSystem>();
            List<GNDriveTauSystem> GNDriveTau = new List<GNDriveTauSystem>();
            List<GNShieldModule> GNShield = new List<GNShieldModule>();

            foreach (var p in vessel.parts)
            {
                var th = p.FindModulesImplementing<GNThrusterSystem>();
                var con = p.FindModulesImplementing<GNCondenserDriveSystem>();
                var taus = p.FindModulesImplementing<GNDriveTauSystem>();
                var gns = p.FindModulesImplementing<GNDriveSystem>();
                var shlds = p.FindModulesImplementing<GNShieldModule>();

                foreach (var m in th)
                {
                    GNThruster.Add(m);
                }

                foreach (var m in con)
                {
                    GNCondernserDrive.Add(m);
                }

                foreach (var m in taus)
                {
                    GNDriveTau.Add(m);
                }

                foreach (var m in gns)
                {
                    GNDrive.Add(m);
                }
                foreach (var m in shlds)
                {
                    GNShield.Add(m);
                }
            }

            // Debug.Log($"GNLists: Thruster={GNThruster.Count}, CondenserDrive={GNCondernserDrive.Count}, DriveTau={GNDriveTau.Count}, Drive={GNDrive.Count}, Shield={GNShield.Count}");
        }
    }

    public static class GNVisualAggregator
    {
        public struct Result
        {
            public int TauCount;
            public int GNCount;
            public int CDCount;
            public int TCount;
            public Color SumColor;   // 0..1にクランプ済み
            public Color AvgColor;   // 同上（寄与した個体で平均）
        }

        /// <summary>
        /// Vessel 内の GNDriveTauSystem / GNDriveSystem を集計して色を合成。
        /// 各モジュールは public GNVisualState vs を持っている前提。
        /// </summary>
        public static Result Aggregate(Vessel vessel)
        {
            var r = new Result();
            if (vessel == null) return r;

            Color sum = Color.black;
            float weightSum = 0f;

            foreach (var p in vessel.parts)
            {
                // Tau
                var taus = p.FindModulesImplementing<GNDriveTauSystem>();
                foreach (var m in taus)
                {
                    r.TauCount++;
                    Accumulate(ref sum, ref weightSum, m.vs);
                }

                // 通常GN
                var gns = p.FindModulesImplementing<GNDriveSystem>();
                foreach (var m in gns)
                {
                    r.GNCount++;
                    Accumulate(ref sum, ref weightSum, m.vs);
                }

                // CondenserDrive
                var cd = p.FindModulesImplementing<GNCondenserDriveSystem>();
                foreach (var m in cd)
                {
                    r.CDCount++;
                    Accumulate(ref sum, ref weightSum, m.vs);
                }

                // GNThruster
                var GNThruster = p.FindModulesImplementing<GNThrusterSystem>();
                foreach (var m in GNThruster)
                {
                    r.TCount++;
                    Accumulate(ref sum, ref weightSum, m.vs);
                }
            }

            // 合算は0..1にクランプ、平均は寄与ウェイトで正規化
            r.SumColor = Clamp01(sum);
            r.AvgColor = (weightSum > 0f) ? Clamp01(sum / weightSum) : Color.black;
            return r;
        }

        // 発光に寄与させるルール：Engine ON かつ Driveモード時のみ、InputLevelで重み付け
        private static void Accumulate(ref Color sum, ref float wsum, in GNVisualState vs)
        {
            //if (!vs.EngineState) return; Condenser should accumulate particles, so not related with Engine State.
            if (vs.Mode == GNVisualMode.Condenser) return; // コンデンサ側のvsは無視

            // vs.ParticleColor は linear/gamma いずれでもOK。必要なら Linear 変換をここに。
            sum += vs.ParticleColor * 1; // w;
            wsum += 1; // w;
        }

        private static Color Clamp01(Color c)
        {
            c.r = Mathf.Clamp01(c.r);
            c.g = Mathf.Clamp01(c.g);
            c.b = Mathf.Clamp01(c.b);
            c.a = Mathf.Clamp01(c.a);
            return c;
        }
    }

    public static class GNSynchronizer
    {
        public struct OnOffList
        {
            public bool LengineOn;
            public bool LagOn;
            public bool LtaOn;
            public bool LhvOn;
            public bool LECOn;
            public bool LSgOn;
            public float MaxG;

            public static OnOffList Empty => new OnOffList
            {
                LengineOn = false,
                LagOn = false,
                LtaOn = false,
                LhvOn = false,
                LECOn = false,
                LSgOn = false,
                MaxG = 0f,
            };

            // ★ 比較メソッド（Equals）
            public override bool Equals(object obj)
            {
                if (!(obj is OnOffList)) return false;
                var o = (OnOffList)obj;
                return LengineOn == o.LengineOn
                    && LagOn == o.LagOn
                    && LtaOn == o.LtaOn
                    && LhvOn == o.LhvOn
                    && LECOn == o.LECOn
                    && LSgOn == o.LSgOn
                    && Mathf.Approximately(MaxG, o.MaxG);
            }

            // ★ ハッシュ（Equalsとセット）
            public override int GetHashCode()
            {
                return (LengineOn, LagOn, LtaOn, LhvOn, LECOn, LSgOn, MaxG).GetHashCode();
            }

            // ★ == / != 演算子のオーバーロード
            public static bool operator ==(OnOffList a, OnOffList b) => a.Equals(b);
            public static bool operator !=(OnOffList a, OnOffList b) => !a.Equals(b);
        }
        
        public static bool IsThereOtherSyncDriveTarget(Vessel vessel, uint myID)
        {
            // future: return true when there are other syncing drive.
            foreach (var p in vessel.parts)
            {
                var taus = p.FindModulesImplementing<GNDriveTauSystem>();
                var gns = p.FindModulesImplementing<GNDriveSystem>();
                int count = 0;

                foreach (var m in taus)
                {
                    if (!m.SyOn) continue;
                    count++;
                    if (myID == p.persistentId && count > 1) return true;
                }

                // reset
                count = 0;

                foreach (var m in gns)
                {
                    if (!m.SyOn) continue;
                    count++;
                    if (myID == p.persistentId && count > 1) return true;
                }
            }
            return false;
        }

        public static void SynchronizeOtherTargetDrive(OnOffList OnOffLst, Vessel vessel, uint myID, ref GNPhysicsState ps)
        {
            // no vessel -> return
            if (vessel == null)
            {
                return;
            }

            // variables
            List<float> deviationTau = new List<float>();
            List<float> deviation = new List<float>();
            bool myDriveIsTau = false; // only 1 drive for parts allowed.

            // parts find
            foreach (var p in vessel.parts)
            {
                // Tau drive.
                var taus = p.FindModulesImplementing<GNDriveTauSystem>();

                foreach (var m in taus)
                {
                    if (!m.SyOn) continue;
                    m.engineOn = OnOffLst.LengineOn;
                    m.agOn = OnOffLst.LagOn;
                    m.hvOn = OnOffLst.LhvOn;
                    m.taOn = OnOffLst.LtaOn;
                    m.accel = OnOffLst.MaxG;
                    m.ECOn = OnOffLst.LECOn;
                    m.sgOn = OnOffLst.LSgOn;

                    // List.add
                    deviationTau.Add(m.DriveIndividuality);
                    if (myID == p.persistentId) myDriveIsTau = true;
                }

                // GN drive.
                var gns = p.FindModulesImplementing<GNDriveSystem>();

                foreach (var m in gns)
                {
                    if (!m.SyOn) continue;
                    // engineOn, ECOn, SgOn are not used in GNDriveSystem
                    m.agOn = OnOffLst.LagOn;
                    m.hvOn = OnOffLst.LhvOn;
                    m.taOn = OnOffLst.LtaOn;
                    m.accel = OnOffLst.MaxG;

                    // List.add
                    deviation.Add(m.DriveIndividuality);
                }
            }

            int NTau = deviationTau.Count;
            int Ngn = deviation.Count;

            // No sync.
            if (Ngn <= 1 && NTau <= 1)
            {
                ps.SyncRate = 1f;
                return;
            }

            // calculation.
            if (myDriveIsTau)
            {
                var n1 = VarianceTo01(Variance(deviationTau, Mean(deviationTau)));
                ps.SyncRate = NTau * Mathf.Pow((1 - n1), NTau);
            }
            else if (!myDriveIsTau)
            {
                var n2 = VarianceTo01(Variance(deviation, Mean(deviation)));
                ps.SyncRate = Ngn * Mathf.Pow((1 - n2), Ngn);
            } 
        }

        private static float Mean(IList<float> xs)
        {
            if (xs == null || xs.Count == 0) return 0f;
            float s = 0f;
            for (int i = 0; i < xs.Count; i++) s += xs[i];
            return s / xs.Count;
        }

        private static float Variance(IList<float> xs, float mean)
        {
            if (xs == null || xs.Count <= 1) return 0f;
            float s2 = 0f;
            for (int i = 0; i < xs.Count; i++)
            {
                float d = xs[i] - mean;
                s2 += d * d;
            }
            return s2 / (xs.Count - 1); // 不偏分散
        }
        private static float Clamp01(float v) => Mathf.Clamp01(v);

        private static float VarianceTo01(float variance, float scale = 4f)
        {
            return Clamp01(variance * scale);
        }
    }
}