using GNTechnology;
using UnityEngine;

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

        public static void SynchronizeOtherTargetDrive(OnOffList OnOffLst, Vessel vessel, uint myID)
        {
            if (vessel == null) return;

            foreach (var p in vessel.parts)
            {
                // prevent drive self overwrite.
                if (p.persistentId == myID) continue;

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
                }
            }
        }
    }
}