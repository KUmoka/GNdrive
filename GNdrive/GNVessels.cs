using System;
using System.Collections.Generic;
using KSP;
using UnityEngine;

namespace GNTechnology
{
    /// <summary>
    /// Vessel 内の GN 系 PartModule をまとめて管理する VesselModule。
    /// ・GNThruster / CondenserDrive / Tau / GNDrive をキャッシュ
    /// ・必要に応じて「今この船に何基あるか？」を高速に取得可能
    /// </summary>
    public class GNDriveVesselModule : VesselModule
    {
        // ===== キャッシュ用リスト =====
        private readonly List<GNThrusterSystem> _thrusters = new List<GNThrusterSystem>();
        private readonly List<GNCondenserDriveSystem> _condensers = new List<GNCondenserDriveSystem>();
        private readonly List<GNDriveTauSystem> _taus = new List<GNDriveTauSystem>();
        private readonly List<GNDriveSystem> _drives = new List<GNDriveSystem>();
        private readonly List<GNBaseSystem> _allGN = new List<GNBaseSystem>();

        // ===== 公開用プロパティ（読み取りだけ） =====
        public int ThrusterCount { get { return _thrusters.Count; } }
        public int CondenserDriveCount { get { return _condensers.Count; } }
        public int TauDriveCount { get { return _taus.Count; } }
        public int GNDriveCount { get { return _drives.Count; } }
        public int AllGNCount { get { return _allGN.Count; } }

        // =========================================
        //  1) この VesselModule をアタッチするかどうか
        // =========================================
        public override bool ShouldBeActive()
        {
            // GN 系 PartModule が 1 つも無い Vessel にはくっつかなくてよい
            if (vessel == null)
                return false;

            // ここだけは都度スキャン
            return vessel.FindPartModulesImplementing<GNBaseSystem>().Count > 0;
        }

        // =========================================
        //  2) 初期化
        // =========================================
        protected override void OnStart()
        {
            base.OnStart();

            if (vessel == null)
                return;

            Debug.Log("[GN] GNDriveVesselModule OnStart: " + vessel.vesselName);

            // 最初に一度スキャン
            RescanDrives();

            // Vessel 構成変化を監視する GameEvents を登録
            GameEvents.onVesselWasModified.Add(OnVesselWasModified);
            GameEvents.onPartCouple.Add(OnPartCouple);
            GameEvents.onPartUndock.Add(OnPartUndock);
            GameEvents.onPartWillDie.Add(OnPartWillDie);
        }

        // =========================================
        //  3) 破棄時（イベント解除が超重要ぶぅ）
        // =========================================
        public void OnDestroy()
        {
            // vessel が null でも Remove は安全（登録されてなくても問題なし）
            GameEvents.onVesselWasModified.Remove(OnVesselWasModified);
            GameEvents.onPartCouple.Remove(OnPartCouple);
            GameEvents.onPartUndock.Remove(OnPartUndock);
            GameEvents.onPartWillDie.Remove(OnPartWillDie);

            _thrusters.Clear();
            _condensers.Clear();
            _taus.Clear();
            _drives.Clear();
            _allGN.Clear();
        }

        // =========================================
        //  4) GameEvents コールバック
        // =========================================

        private void OnVesselWasModified(Vessel v)
        {
            if (v == null || v != vessel) return;
            // パーツ追加・削除・壊れたなど
            Debug.Log("[GN] Vessel modified: " + v.vesselName);
            RescanDrives();
        }

        private void OnPartCouple(GameEvents.FromToAction<Part, Part> e)
        {
            // ドッキングなどで Vessel 構成が変わったとき
            if (e.to != null && e.to.vessel == vessel ||
                e.from != null && e.from.vessel == vessel)
            {
                Debug.Log("[GN] Part coupled on vessel: " + vessel.vesselName);
                RescanDrives();
            }
        }

        private void OnPartUndock(Part p)
        {
            if (p == null) return;
            if (p.vessel == vessel)
            {
                Debug.Log("[GN] Part undocked from vessel: " + vessel.vesselName);
                RescanDrives();
            }
        }

        private void OnPartWillDie(Part p)
        {
            if (p == null) return;
            if (p.vessel == vessel)
            {
                Debug.Log("[GN] Part will die on vessel: " + vessel.vesselName);
                RescanDrives();
            }
        }

        // =========================================
        //  5) GN 系モジュールの再スキャン
        // =========================================
        private void RescanDrives()
        {
            _thrusters.Clear();
            _condensers.Clear();
            _taus.Clear();
            _drives.Clear();
            _allGN.Clear();

            if (vessel == null || vessel.parts == null)
                return;

            foreach (Part p in vessel.parts)
            {
                if (p == null) continue;

                // GNBaseSystem 派生をまとめて取得
                var gnModules = p.FindModulesImplementing<GNBaseSystem>();
                if (gnModules == null || gnModules.Count == 0) continue;

                foreach (var m in gnModules)
                {
                    if (m == null) continue;

                    _allGN.Add(m);

                    var thr = m as GNThrusterSystem;
                    if (thr != null)
                    {
                        _thrusters.Add(thr);
                        continue;
                    }

                    var cd = m as GNCondenserDriveSystem;
                    if (cd != null)
                    {
                        _condensers.Add(cd);
                        continue;
                    }

                    var tau = m as GNDriveTauSystem;
                    if (tau != null)
                    {
                        _taus.Add(tau);
                        continue;
                    }

                    var gn = m as GNDriveSystem;
                    if (gn != null)
                    {
                        _drives.Add(gn);
                        continue;
                    }
                }
            }

            Debug.Log(
                "[GN] RescanDrives on " + vessel.vesselName +
                " : Thr=" + _thrusters.Count +
                " CD=" + _condensers.Count +
                " Tau=" + _taus.Count +
                " GN=" + _drives.Count
            );
        }

        // =========================================
        //  6) 「さっきの countTypeOfDrives 関数」も内蔵
        // =========================================
        public void CountTypeOfDrives(
            out int gnThrusters,
            out int gnCondenserDrives,
            out int gnDriveTaus,
            out int gnDrives)
        {
            // out は必ず初期化
            gnThrusters = _thrusters.Count;
            gnCondenserDrives = _condensers.Count;
            gnDriveTaus = _taus.Count;
            gnDrives = _drives.Count;
        }
    }
}
