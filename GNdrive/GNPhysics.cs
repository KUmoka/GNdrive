using KSP;
using System;
using System.Linq;
using UnityEngine;

namespace GNTechnology
{
    [Flags]
    public enum GNFlags : uint
    {
        None = 0,
        Ignited = 1 << 0, // Engine On/Off
        AntiGravity = 1 << 1, // Antigravity On/Off
        Hover = 1 << 2, // Hover mode On/Off
        TransAM = 1 << 3, // Trans-AM mode On/Off
        InertiaControl = 1 << 4, // Future Use
        Modified = 1 << 5, // Future Use
    }

    public struct GNPhysicalState
    {
        public Part part;            // 対象パーツ（必須）
        public GNFlags flags;        // まとめて渡す

        // チューニング・入力（必要に応じて増やせる）
        public float fuelEfficiency; // 消費係数
        public float particleOutputRate;   // 生成(or 変換)レート
        public float maxG; // Target G for Full throttle, if ship is too heavy, actual G will be lower.
        public float phaseShift; // For Twin-drive Sync rate.

        public static GNPhysicalState Empty => new GNPhysicalState { part = null, flags = GNFlags.None, fuelEfficiency = 0f, particleOutputRate = 0f, maxG = 0f, phaseShift = 0f };
    }

    public static class GNPhysics
    {
        public static void SetOff(in GNPhysicalState ps)
        {
            if (ps.part == null) return;//part is required.

        }

        // Every FixedUpdate
        public static void UpdatePhysics(in GNPhysicalState ps)
        {
            // for TRANS-AM
            var actualParticleOutputMultiplier = 1f;
            var geeMultiplier = 0f;
            var driveCount = 0;
            var ignitedCount = 0;
            var agCount = 0;

            // is this drive on? -> no,return, yes, continue
            if (ps.flags != GNFlags.Ignited) return;

            // is TRANS-AM on? -> output 3x power(particle rate 3x)
            if (ps.flags != GNFlags.TransAM )
            {
                actualParticleOutputMultiplier = 3f;
            }

            // is hover on? -> PID control vertical speed to 0
            if (ps.flags == GNFlags.Hover)
            {
                // PID control vertical speed to 0
            }

            // is Antigravity on? -> cancel gravity(-gee), if hover on, AG should be disabled. Now count how many antigravity-on drives in vessel? -> -gee/agCount, if agCount=0, no -gee
            if (ps.flags == GNFlags.AntiGravity)
            {
                geeMultiplier = 1f;// normal AG
                foreach (Part p in this.vessel.Parts)
                {
                    foreach (PartModule m in p.Modules)
                    {
                        ProtoTaudrive drive = null;
                        ProtoGNdrive gdrive = null;
                        if (m.moduleName == "ProtoTaudrive")
                        {
                            drive = (ProtoTaudrive)m;
                            if (drive.agActivated == true)
                            {
                                enginecount += 1;
                            }
                        }
                        else
                            if (m.moduleName == "ProtoGNdrive")
                        {
                            gdrive = (ProtoGNdrive)m;
                            if (gdrive.agActivated == true)
                            {
                                enginecount += 1;
                            }
                        }
                        //四種類のドライブを全部探して、AGがONの数を数えてそれぞれのドライブパワーを足す
                    }
                }
            }
            
            

            // calculate total drivePower. find every GNDrive/GNDriveTau/GNCondenserDrive/GNThruster -> sum <drives>.drivePower.
            // calculate total mass of vessel -> vessel.GetTotalMass()
            // calculate acceleration = totalDrivePower(m/s * kg/s)/totalMass(kg) -> m/s^2
            // calculate forceDirection = vessel.ReferenceTransform.up * (vessel.ctrlState.mainThrottle - vessel.ctrlState.Z) + vessel.ReferenceTransform.forward * (-vessel.ctrlState.Y) + vessel.ReferenceTransform.right * (-vessel.ctrlState.X);
            // apply acceleration to every part in vessel -> part.AddForce(acceleration * part.rb.mass * forceDirection)
            // apply -gee to every part in vessel -> part.AddForce(-gee * part.rb.mass / agCount) gee = FlightGlobals.getGeeForceAtPosition(this.vessel.transform.position)

        }


        public static void Update(ref GNPhysicalState ps)
        {
            if (!ps.initialized || ps.part == null) return;
            var vessel = ps.vessel ?? ps.part.vessel;
            if (vessel == null || !HighLogic.LoadedSceneIsFlight || !vessel.isActiveVessel) return;

            // --- 入力の取り出し
            var cs = vessel.ctrlState;
            float x = -cs.X * ps.overload * 10f;
            float y = -cs.Y * ps.overload * 10f;
            float z = (cs.mainThrottle - cs.Z) * ps.overload * 10f;

            // --- 重力ベクトル（1基あたり割り）
            int agCount = CountEnginesWithFlag(vessel, GNFlags.AntiGravity);
            Vector3 gee = FlightGlobals.getGeeForceAtPosition(vessel.transform.position);
            if (agCount > 0) gee /= agCount;

            // --- 基本制御力
            Vector3 control =
                vessel.ReferenceTransform.up * z +
                vessel.ReferenceTransform.forward * y +
                vessel.ReferenceTransform.right * x;

            // --- Hover（垂直速度打消し）
            if (Has(ps.flags, GNFlags.AntiGravity) && Has(ps.flags, GNFlags.Hover))
            {
                float vVert = Vector3.Dot(gee.normalized, ps.part.rb.velocity);
                ps.hoverPid.Calibrateclamp(ps.overload);
                Vector3 cancel = ps.hoverPid.Control(vVert) * gee.normalized * 10f / Mathf.Max(1, agCount);
                control -= cancel;
            }

            // --- Trans-AMブースト
            float teFactor = 1f;
            if (Has(ps.flags, GNFlags.TransAM))
            {
                control *= 5f;
                teFactor = Mathf.Max(1f, Mathf.Pow(ps.particleRate, Mathf.Max(0, CountIgnited(vessel) - 1)));
            }

            // --- 同期制限（必要ならカット）
            int ignitedCount = CountIgnited(vessel);
            if (ps.maxSyncEngines > 0 && ignitedCount > ps.maxSyncEngines)
            {
                control = Vector3.zero;
                gee = Vector3.zero;
                teFactor = 0.001f;
            }

            // --- フラグで出力制御
            if (!Has(ps.flags, GNFlags.Ignited)) control = Vector3.zero;
            if (!Has(ps.flags, GNFlags.AntiGravity)) gee = Vector3.zero;

            // --- リソース計算（GN 消費と生成）
            float mass = vessel.GetTotalMass();
            float accelMag = (-gee + control).magnitude;

            // 消費 [units/s] ≒ m * |a| * η
            float consumption = mass * Mathf.Abs(accelMag) * ps.fuelEfficiency;

            // 生成／変換（TransAM時は最低生成量を粒子レート×teFactorまで引き上げる例）
            float particleGen = Has(ps.flags, GNFlags.TransAM) ? ps.particleRate * teFactor : 0f;

            // 実リクエスト（Δt倍）
            double delta = TimeWarp.fixedDeltaTime;
            double requested = (consumption - particleGen) * delta;

            // GNparticle残量反映
            double drawn = ps.part.RequestResource("GNparticle", requested);

            // 枯渇時は停止
            if (requested > 0 && Math.Round(drawn, 5) < Math.Round(requested, 5))
            {
                Set(ref ps, GNFlags.Ignited, false);
                Set(ref ps, GNFlags.AntiGravity, false);
                Set(ref ps, GNFlags.TransAM, false);
                control = Vector3.zero;
                gee = Vector3.zero;
            }

            // --- 力を各Partに加える（KSPのAddForceはパーツ質量でスケール）
            if (Has(ps.flags, GNFlags.Ignited))
            {
                foreach (var p in vessel.parts)
                    if (p.physicalSignificance == Part.PhysicalSignificance.FULL && p.rb != null)
                        p.AddForce(control * p.rb.mass);
            }
            if (Has(ps.flags, GNFlags.AntiGravity))
            {
                foreach (var p in vessel.parts)
                    if (p.physicalSignificance == Part.PhysicalSignificance.FULL && p.rb != null)
                        p.AddForce(-gee * p.rb.mass);
            }

            // --- 慣性制御（簡易版のフック。必要ならここを拡張）
            if (Has(ps.flags, GNFlags.InertiaControl))
            {
                // 例：将来ここでターゲット追従力をcontrolに加算する
                // ps.part.vessel.targetObject ... を参照して拡張
            }

            // --- スモークテスト：常に上向きに +5 m/s^2 をかける
            foreach (var p in ps.vessel.parts)
            {
                if (p.physicalSignificance == Part.PhysicalSignificance.FULL && p.rb != null)
                {
                    // 質量を無視して加速度指定（ForceMode.Acceleration）
                    p.rb.AddForce(Vector3.up * 5f, ForceMode.Acceleration);
                }
            }
        }
    }

}

namespace GNTechnology
{
    public class ProtoTaudrive : PartModule
    {
        [KSPField]
        public float fuelefficiency = 1F;
        [KSPField]
        public float particlegrate = 800F;
        [KSPField]
        public float ConvertRatio = 1F;
        public Vector4 color = Vector4.zero;


        [KSPField(isPersistant = true)]
        public bool engineIgnited = false;
        public bool flameOut = false;
        public bool agActivated = false;
        public bool depleted = false;
        public bool ecActivated = false;

        [KSPField(guiName = "Engine Status", guiActive = true)]
        private string ES = "Deactivated";

        [KSPField(guiName = "Mass", guiActive = true)]
        private string mass = "N/a";

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Max Overload", isPersistant = true), UI_FloatRange(minValue = 0f, maxValue = 5f, stepIncrement = 0.1f)]
        public float Overload = 1f;

        [KSPAction("Toggle", KSPActionGroup.None, guiName = "Toggle Engine")]
        private void ActionActivate(KSPActionParam param)
        {
            if (engineIgnited == true)
            {
                Deactivate();
            }
            else
            {
                Activate();
            }
        }

        [KSPEvent(name = "Activate", guiName = "Activate Engine", active = true, guiActive = true)]
        public void Activate()
        {
            if (depleted == false)
            {
                engineIgnited = true;
                Events["Deactivate"].guiActive = true;
                Events["Activate"].guiActive = false;
            }
        }

        [KSPEvent(name = "Deactivate", guiName = "Deactivate Engine", active = true, guiActive = false)]
        public void Deactivate()
        {
            engineIgnited = false;
            Events["Deactivate"].guiActive = false;
            Events["Activate"].guiActive = true;
        }

        [KSPAction("Toggleag", KSPActionGroup.None, guiName = "Toggle Antigravity")]
        private void Toggleag(KSPActionParam param)
        {
            if (agActivated == true)
            {
                Deactivateag();
            }
            else
            {
                Activateag();
            }

        }

        [KSPEvent(name = "Activateag", guiName = "Activate Antigravity", active = true, guiActive = true)]
        public void Activateag()
        {
            if (depleted == false)
            {
                agActivated = true;
                engineIgnited = true;
                Events["Deactivateag"].guiActive = true;
                Events["Activateag"].guiActive = false;
            }

        }

        [KSPEvent(name = "Deactivateag", guiName = "Deactivate Antigravity", active = true, guiActive = false)]
        public void Deactivateag()
        {
            agActivated = false;
            Events["Deactivateag"].guiActive = false;
            Events["Activateag"].guiActive = true;
        }

        [KSPEvent(name = "Activateec", guiName = "Activate Converter", active = true, guiActive = true)]
        public void Activateec()
        {
            ecActivated = true;
            Events["Deactivateec"].guiActive = true;
            Events["Activateec"].guiActive = false;
        }

        [KSPEvent(name = "Deactivateec", guiName = "Deactivate Converter", active = true, guiActive = false)]
        public void Deactivateec()
        {
            ecActivated = false;
            Events["Deactivateec"].guiActive = false;
            Events["Activateec"].guiActive = true;
        }

        protected Transform rotorTransform = null;

        public override void OnStart(PartModule.StartState state)
        {
            Debug.Log("[GN] OnStart Run");

            part.stagingIcon = "LIQUID_ENGINE";
            base.OnStart(state);
            {
                if (state != StartState.Editor && state != StartState.None)
                {
                    this.enabled = true;
                    this.part.force_activate();
                }
                else
                {
                    this.enabled = false;
                }
            }
        }

        public void Update()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                return;
            }

        }

        public override void OnUpdate()
        {
            base.OnUpdate();
        }

        public override void OnFixedUpdate()
        {
            ES = "Deactivated";
            if (depleted == true && part.Resources["GNparticle"].amount == part.Resources["GNparticle"].maxAmount)
            {
                depleted = false;
            }
            if (!HighLogic.LoadedSceneIsFlight || !vessel.isActiveVessel) return;
            float pitch = vessel.ctrlState.pitch;
            float roll = vessel.ctrlState.roll;
            float yaw = vessel.ctrlState.yaw;
            float throttle = vessel.ctrlState.mainThrottle * Overload;
            float y = -vessel.ctrlState.Y * Overload * 10;
            float x = -vessel.ctrlState.X * Overload * 10;
            float z = throttle * 10 - vessel.ctrlState.Z * Overload * 10;
            float enginecount = 0;
            float tefactor = 1;
            float ID = GetInstanceID();

            if (agActivated == true)
            {
                engineIgnited = true;
            }

            foreach (Part p in this.vessel.Parts)
            {
                foreach (PartModule m in p.Modules)
                {
                    ProtoTaudrive drive = null;
                    ProtoGNdrive gdrive = null;
                    if (m.moduleName == "ProtoTaudrive")
                    {
                        drive = (ProtoTaudrive)m;
                        if (drive.agActivated == true)
                        {
                            enginecount += 1;
                        }
                    }
                    else
                        if (m.moduleName == "ProtoGNdrive")
                    {
                        gdrive = (ProtoGNdrive)m;
                        if (gdrive.agActivated == true)
                        {
                            enginecount += 1;
                        }
                    }
                }
            }

            if (engineIgnited == true)
            {
                ES = "Activated";
                Events["Deactivate"].guiActive = true;
                Events["Activate"].guiActive = false;
            }
            else
            {
                Events["Deactivate"].guiActive = false;
                Events["Activate"].guiActive = true;
            }

            if (agActivated == true)
            {
                ES = "Activated";
                Events["Deactivateag"].guiActive = true;
                Events["Activateag"].guiActive = false;
            }
            else
            {
                Events["Deactivateag"].guiActive = false;
                Events["Activateag"].guiActive = true;
            }

            if (depleted == true)
            {
                ES = "GNparticle depleted";
                Deactivate();
                Deactivateag();
            }

            Vector3 srfVelocity = vessel.GetSrfVelocity();
            float VerticalV;
            VerticalV = (float)vessel.verticalSpeed;
            Vector3 Airspeed = vessel.transform.InverseTransformDirection(srfVelocity);
            Vector3 gee = FlightGlobals.getGeeForceAtPosition(this.vessel.transform.position) / enginecount;
            Vector3 controlforce = vessel.ReferenceTransform.up * z + vessel.ReferenceTransform.forward * y + vessel.ReferenceTransform.right * x;

            if (engineIgnited == false)
            {
                controlforce = Vector3.zero;
            }
            if (agActivated == false)
            {
                gee = Vector3.zero;
            }

            double consumption = vessel.GetTotalMass() * Mathf.Abs((-gee + controlforce).magnitude) * fuelefficiency * TimeWarp.fixedDeltaTime;
            if (ecActivated == true)
            {
                double elcconsume = particlegrate * ConvertRatio * TimeWarp.fixedDeltaTime;
                double elcDrawn = this.part.RequestResource("ElectricCharge", elcconsume);
                double ratio = elcDrawn / elcconsume;
                double GNDrawn = this.part.RequestResource("GNparticle", -(elcconsume / ConvertRatio) * ratio);
                double backcharge = this.part.RequestResource("ElectricCharge", -GNDrawn * ConvertRatio - elcDrawn);
            }

            double GNconsumtion = this.part.RequestResource("GNparticle", consumption);

            if (consumption != 0 && Math.Round(GNconsumtion, 5) < Math.Round(consumption, 5))
            {
                depleted = true;
                controlforce = Vector3.zero;
                gee = Vector3.zero;
                agActivated = false;
                engineIgnited = false;
                Deactivate();
                Deactivateag();
                part.Resources["GNparticle"].amount = 0;

            }

            mass = vessel.GetTotalMass().ToString("R");

            if (engineIgnited == true)
            {
                foreach (Part p in this.vessel.parts)
                {
                    if ((p.physicalSignificance == Part.PhysicalSignificance.FULL) && (p.rb != null))
                    {
                        p.AddForce(controlforce * p.rb.mass);
                    }
                }
            }


            if (agActivated == true)
            {
                foreach (Part p in this.vessel.parts)
                {
                    if ((p.physicalSignificance == Part.PhysicalSignificance.FULL) && (p.rb != null))
                    {
                        p.AddForce(-gee * p.rb.mass);
                    }
                }
            }
        }
    }
    public class ProtoGNdrive : PartModule
    {
        [KSPField]
        public float fuelefficiency = 1F;
        [KSPField]
        public float particlegrate = 1000F;
        [KSPField]
        public float maxenginecount = 2F;

        public Vector4 color = Vector4.zero;

        [KSPField(isPersistant = true)]
        public bool engineIgnited = false;
        public bool flameOut = false;
        public bool agActivated = false;
        public bool hvActivated = false;
        public bool taactivated = false;
        public bool ICactivated = false;
        public bool ICIsActivaed = false;
        public bool modified = false;
        public float overloadtemp = 0;

        private PidController brakePid = new PidController(10F, 0.005F, 0.002F, 50, 5);

        [KSPField(guiName = "Engine Status", guiActive = true)]
        private string ES = "Deactivated";

        [KSPField(guiName = "Mass", guiActive = true)]
        private string mass = "N/a";

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Max Overload", isPersistant = true), UI_FloatRange(minValue = 0f, maxValue = 5f, stepIncrement = 0.1f)]
        public float Overload = 1f;

        [KSPAction("Toggle", KSPActionGroup.None, guiName = "Toggle Engine")]
        private void ActionActivate(KSPActionParam param)
        {
            if (engineIgnited == true)
            {
                Deactivate();
            }
            else
            {
                Activate();
            }
        }

        [KSPEvent(name = "Activate", guiName = "Activate Engine", active = true, guiActive = true)]
        public void Activate()
        {
            this.part.force_activate();
            engineIgnited = true;
            Events["Deactivate"].guiActive = true;
            Events["Activate"].guiActive = false;
            modified = true;

        }

        [KSPEvent(name = "Deactivate", guiName = "Deactivate Engine", active = true, guiActive = false)]
        public void Deactivate()
        {
            engineIgnited = false;
            Events["Deactivate"].guiActive = false;
            Events["Activate"].guiActive = true;
            modified = true;
        }

        [KSPEvent(name = "Activateta", guiName = "Trans-AM", active = true, guiActive = false)]
        public void Activateta()
        {
            taactivated = true;
            Events["Activateta"].guiActive = false;
            modified = true;
        }

        [KSPAction("Toggleag", KSPActionGroup.None, guiName = "Toggle Antigravity")]
        private void Toggleag(KSPActionParam param)
        {
            if (agActivated == true)
            {
                Deactivateag();
            }
            else
            {
                Activateag();
            }

        }

        [KSPEvent(name = "Activateag", guiName = "Activate Antigravity", active = true, guiActive = true)]
        public void Activateag()
        {
            this.part.force_activate();
            agActivated = true;
            Events["Deactivateag"].guiActive = true;
            Events["Activateag"].guiActive = false;
            Events["Activatehv"].guiActive = true;
            modified = true;
            Deactivatehv();
        }

        [KSPEvent(name = "Deactivateag", guiName = "Deactivate Antigravity", active = true, guiActive = false)]
        public void Deactivateag()
        {
            agActivated = false;
            hvActivated = false;
            Events["Deactivateag"].guiActive = false;
            Events["Activateag"].guiActive = true;
            Events["Activatehv"].guiActive = false;
            Events["Deactivatehv"].guiActive = false;
            modified = true;
        }

        [KSPAction("Toggle Hover", KSPActionGroup.None)]
        private void Togglehv(KSPActionParam param)
        {
            if (agActivated == true && !hvActivated)
            {
                Activatehv();
            }
            else
            {
                Deactivatehv();
            }

        }

        [KSPEvent(name = "Activatehv", guiName = "Activate Hover", active = true, guiActive = false)]
        public void Activatehv()
        {
            this.part.force_activate();
            hvActivated = true;
            Events["Deactivatehv"].guiActive = true;
            Events["Activatehv"].guiActive = false;
            modified = true;

        }

        [KSPEvent(name = "Deactivatehv", guiName = "Deactivate Hover", active = true, guiActive = false)]
        public void Deactivatehv()
        {
            hvActivated = false;
            Events["Deactivatehv"].guiActive = false;
            Events["Activatehv"].guiActive = true;
            modified = true;
        }
        [KSPAction("Toggle Inertia control", KSPActionGroup.None)]
        private void ICActionActivate(KSPActionParam param)
        {
            if (ICIsActivaed == true)
            {
                ICDeactivate();
            }
            else
            {
                ICActivate();
            }
        }

        [KSPEvent(name = "ICActivate", guiName = "Activate Inertia control", active = true, guiActive = true)]
        public void ICActivate()
        {
            this.part.force_activate();
            ICIsActivaed = true;
            Events["ICDeactivate"].guiActive = true;
            Events["ICActivate"].guiActive = false;
            modified = true;
        }

        [KSPEvent(name = "ICDeactivate", guiName = "Deactivate Inertia control", active = true, guiActive = false)]
        public void ICDeactivate()
        {
            ICIsActivaed = false;
            Events["ICDeactivate"].guiActive = false;
            Events["ICActivate"].guiActive = true;
            modified = true;
        }

        public override void OnStart(PartModule.StartState state)
        {
            part.stagingIcon = "LIQUID_ENGINE";
            if (state != StartState.Editor && state != StartState.None)
            {
                this.enabled = true;
                this.part.force_activate();
            }
            overloadtemp = Overload;
        }

        public void Update()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                return;
            }
        }

        public override void OnFixedUpdate()
        {
            ES = "Deactivated";
            if (!HighLogic.LoadedSceneIsFlight || !vessel.isActiveVessel) return;
            float pitch = vessel.ctrlState.pitch;
            float roll = vessel.ctrlState.roll;
            float yaw = vessel.ctrlState.yaw;
            float throttle = vessel.ctrlState.mainThrottle * Overload;
            float y = -vessel.ctrlState.Y * Overload * 10;
            float x = -vessel.ctrlState.X * Overload * 10;
            float z = throttle * 10 - vessel.ctrlState.Z * Overload * 10;
            float enginecount = 1;
            float agenginecount = 0;
            float tefactor = 1;
            float ID = GetInstanceID();

            if (Overload != overloadtemp)
            {
                modified = true;
            }

            if (agActivated == true)
            {
                engineIgnited = true;
            }

            if (engineIgnited == true)
            {
                foreach (Part p in this.vessel.Parts)
                {
                    foreach (PartModule m in p.Modules)
                    {
                        ProtoGNdrive drive = null;
                        ProtoTaudrive tdrive = null;
                        if (m.moduleName == "ProtoGNdrive")
                        {
                            drive = (ProtoGNdrive)m;
                            if (drive.engineIgnited == true && drive.GetInstanceID() != GetInstanceID())
                            {
                                enginecount += 1;
                                if (modified == true)
                                {
                                    if (drive.modified == true)
                                    {
                                        taactivated = drive.taactivated;
                                        agActivated = drive.agActivated;
                                        Overload = drive.Overload;
                                        overloadtemp = drive.Overload;
                                        hvActivated = drive.hvActivated;
                                        ICactivated = drive.ICactivated;
                                        ICIsActivaed = drive.ICIsActivaed;

                                    }
                                    else
                                    {
                                        drive.taactivated = taactivated;
                                        drive.agActivated = agActivated;
                                        drive.Overload = Overload;
                                        drive.overloadtemp = Overload;
                                        drive.hvActivated = hvActivated;
                                        drive.ICactivated = ICactivated;
                                        drive.ICIsActivaed = ICIsActivaed;
                                    }
                                }
                            }
                            if (drive.agActivated == true)
                            {
                                agenginecount += 1;
                            }
                        }
                        else
                            if (m.moduleName == "Taudrive")
                        {
                            tdrive = (ProtoTaudrive)m;
                            if (tdrive.agActivated == true)
                            {
                                agenginecount += 1;
                            }
                        }
                    }
                }
            }

            modified = false;

            if (engineIgnited == true)
            {
                ES = "Activated";
                Events["Deactivate"].guiActive = true;
                Events["Activate"].guiActive = false;
            }
            else
            {
                Events["Deactivate"].guiActive = false;
                Events["Activate"].guiActive = true;
            }


            if (hvActivated == true && agActivated == true)
            {
                Events["Deactivatehv"].guiActive = true;
                Events["Activatehv"].guiActive = false;
            }
            else
            {
                Events["Deactivatehv"].guiActive = false;
                Events["Activatehv"].guiActive = true;
            }

            if (agActivated == true)
            {
                ES = "Activated";
                Events["Deactivateag"].guiActive = true;
                Events["Activateag"].guiActive = false;
            }
            else
            {
                Events["Deactivateag"].guiActive = false;
                Events["Activateag"].guiActive = true;
                Events["Deactivatehv"].guiActive = false;
                Events["Activatehv"].guiActive = false;
                hvActivated = false;
                if (engineIgnited == false)
                {
                    taactivated = false;
                    Events["Activateta"].guiActive = true;
                }
            }

            if (taactivated == true)
            {
                ES = "Trans-AM";
            }

            Vector3 srfVelocity = vessel.GetSrfVelocity();
            float VerticalV;
            VerticalV = (float)vessel.verticalSpeed;
            //bool break = 
            Vector3 Airspeed = vessel.transform.InverseTransformDirection(srfVelocity);
            Vector3 gee = FlightGlobals.getGeeForceAtPosition(this.vessel.transform.position) / agenginecount;
            float Vvelocity = Vector3.Dot(gee.normalized, part.rb.velocity);
            brakePid.Calibrateclamp(Overload);
            Vector3 VvCancel = hvActivated ? brakePid.Control(Vvelocity) * gee.normalized * 10 / agenginecount : Vector3.zero;
            Vector3 controlforce = vessel.ReferenceTransform.up * z + vessel.ReferenceTransform.forward * y + vessel.ReferenceTransform.right * x - VvCancel;

            if (enginecount > maxenginecount)
            {
                ES = "Unsynchronized";
                controlforce = Vector3.zero;
                gee = Vector3.zero;
                tefactor = 0.001F;
            }
            else
            {
                tefactor = (float)Math.Pow((double)particlegrate, (double)enginecount - 1);
            }

            if (engineIgnited == false)
            {
                controlforce = Vector3.zero;
            }
            if (agActivated == false)
            {
                gee = Vector3.zero;
            }
            float consumption = vessel.GetTotalMass() * Mathf.Abs((-gee + controlforce).magnitude) * fuelefficiency;
            float particlegen = particlegrate * tefactor;

            if (taactivated == true)
            {

                controlforce *= 5;
                consumption = 4 * consumption - 3 * vessel.GetTotalMass() * Mathf.Abs(gee.magnitude) * fuelefficiency;
                Events["Activateta"].guiActive = false;
                consumption = Mathf.Max(particlegen, consumption) + 4;
            }
            else
            {
                if (engineIgnited == true)
                {
                    Events["Activateta"].guiActive = true;
                }

            }

            double reschange = (consumption - particlegen) * TimeWarp.fixedDeltaTime;
            double resourceDrawn = this.part.RequestResource("GNparticle", reschange);

            if (resourceDrawn == 0 && reschange > 0)
            {
                ES = "GNparticle depleted";
                controlforce = Vector3.zero;
                gee = Vector3.zero;
                Deactivate();
                Deactivateag();
                taactivated = false;
            }

            mass = vessel.GetTotalMass().ToString("R");

            if (engineIgnited == true)
            {
                if (this.vessel.ActionGroups.groups[3])
                {
                    if (controlforce.magnitude > Overload)
                    {
                        controlforce = controlforce.normalized * Overload;
                    }

                    if (ICIsActivaed)
                    {
                        InertiaControl();
                    }

                    Vector3 Breakforce = this.vessel.ActionGroups.groups[5] ? (-this.vessel.GetSrfVelocity()).normalized * Mathf.Min(this.vessel.GetSrfVelocity().magnitude / Time.fixedDeltaTime, Overload * 10f) - gee * 0.9f : Vector3.zero;
                    controlforce += Breakforce;

                }
                foreach (Part p in this.vessel.parts)
                {

                    if ((p.physicalSignificance == Part.PhysicalSignificance.FULL) && (p.rb != null))
                    {
                        p.AddForce(controlforce * p.rb.mass);
                    }

                }


                if (agActivated == true)
                {
                    foreach (Part p in this.vessel.parts)
                    {
                        if ((p.physicalSignificance == Part.PhysicalSignificance.FULL) && (p.rb != null))
                        {
                            p.AddForce(-gee * p.rb.mass);
                        }
                    }
                }

            }
            void InertiaControl()
            {

                float DirFlag = 0;
                if (Airspeed.y < 0)
                {
                    DirFlag = 2;
                }
                Vector3 InertiaForce = Vector3.zero;
                float ReD = 5000;
                Vessel target = null;
                if (this.vessel.targetObject != null)
                {
                    target = this.vessel.targetObject.GetVessel();
                    ReD = Vector3.Distance(this.vessel.transform.position, target.transform.position);
                }
                if (target && ReD < 3000)
                {
                    Vector3 RelVel = this.vessel.transform.InverseTransformDirection(this.vessel.rb_velocity - target.rb_velocity);
                    Vector3 yawsForce = (x == 0 ? RelVel.x : -x) * -this.vessel.transform.right;
                    Vector3 pitchsForce = (y == 0 ? RelVel.z : -y) * -this.vessel.transform.forward;
                    Vector3 FrontForce = (RelVel.y - 10 * z < 0 && RelVel.y > 0 ? -z : RelVel.y) * -this.vessel.transform.up;
                    InertiaForce = hvActivated ? Vector3.ProjectOnPlane(yawsForce + pitchsForce + FrontForce, gee.normalized) : yawsForce + pitchsForce + FrontForce;
                    ICactivated = true;
                }
                else
                {
                    if (target && ICactivated)
                    {
                        ICactivated = false;
                        ICDeactivate();
                    }
                    else
                    {
                        Vector3 yawsForce = (x == 0 ? Airspeed.x : -x) * -this.vessel.transform.right;
                        Vector3 pitchsForce = (y == 0 ? Airspeed.z : -y) * -this.vessel.transform.forward;
                        Vector3 FrontForce = Airspeed.y * -this.vessel.transform.up * DirFlag;
                        InertiaForce = hvActivated ? Vector3.ProjectOnPlane(yawsForce + pitchsForce + FrontForce, gee.normalized) : yawsForce + pitchsForce + FrontForce;
                        if (InertiaForce.magnitude > Overload)
                        {
                            InertiaForce = InertiaForce.normalized * Overload * 10;
                        }
                    }
                }
                controlforce += (InertiaForce) / Time.fixedDeltaTime / enginecount;
            }
        }
    }
}
