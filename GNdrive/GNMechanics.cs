using GNTechnology;
using System.Collections.Generic;
using UnityEngine;
using static GNTechnology.GNSynchronizer;

namespace GNTechnology
{
    public static class GNVisualAggregator
    {
        public struct Result
        {
            public int TauCount;
            public int GNCount;
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
            if (vs.Mode != GNVisualMode.Drive) return; // コンデンサ側のvsは無視

            // 0..1に収まる重み（好みでガンマ補正してもOK）
            //float w = 1 //Mathf.Clamp01(vs.InputLevel); Condenser color shouldn't reflects main thrust
            //if (w <= 0f) return;

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
            public float MaxG;

            public static OnOffList Empty => new OnOffList
            {
                LengineOn = false,
                LagOn = false,
                LtaOn = false,
                LhvOn = false,
                LECOn = false,
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
                    && Mathf.Approximately(MaxG, o.MaxG);
            }

            // ★ ハッシュ（Equalsとセット）
            public override int GetHashCode()
            {
                return (LengineOn, LagOn, LtaOn, LhvOn, LECOn, MaxG).GetHashCode();
            }

            // ★ == / != 演算子のオーバーロード
            public static bool operator ==(OnOffList a, OnOffList b) => a.Equals(b);
            public static bool operator !=(OnOffList a, OnOffList b) => !a.Equals(b);
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
            bool myDriveIsTau = false;

            // parts find
            foreach (var p in vessel.parts)
            {
                // prevent drive self overwrite.
                // if (p.persistentId == myID) continue; needs add myself now.

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

                    // List.add
                    deviationTau.Add(m.DriveIndividuality);
                    if (myID == p.persistentId) myDriveIsTau = true;
                }

                // GN drive.
                var gns = p.FindModulesImplementing<GNDriveSystem>();

                foreach (var m in gns)
                {
                    if (!m.SyOn) continue;
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
            if (Ngn == 0 && NTau == 0)
            {
                Debug.LogError("no sync drives");
                ps.SyncRate = 1f;
                return;
            }

            // calculation.
            if (myDriveIsTau)
            {
                var n1 = VarianceTo01(Variance(deviationTau, Mean(deviationTau)));
                ps.SyncRate = NTau * Mathf.Pow((1 - n1), NTau);
                Debug.Log("NTau = " + NTau);
                Debug.Log("n1 = " + n1);

            }
            else if (!myDriveIsTau)
            {
                var n2 = VarianceTo01(Variance(deviation, Mean(deviation)));
                ps.SyncRate = Ngn * Mathf.Pow((1 - n2), Ngn);
                Debug.Log("Ngn = " + Ngn);
                Debug.Log("n2 = " + n2);
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