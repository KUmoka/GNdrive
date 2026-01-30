using GNTechnology;
using KSP;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine;
using static GNTechnology.GNSynchronizer;
using static VehiclePhysics.ProjectPatchAsset;

namespace GNTechnology
{
    public enum DriveState //public enum DriveState {Unsynchronized, Depleted, Activated, Deactivated, Refilled, Reposed}
    {
        [Description("⚠ Unsync!")]
        Unsynchronized,

        [Description("⛽ Depleted")]
        Depleted,

        [Description("🔥 Activated")]
        Activated,

        [Description("⛔ Deactivated")]
        Deactivated,

        [Description("🔋 Refilled")]
        Refilled,

        [Description("⏹️ Reposed")]
        Reposed
    }

    public struct GNSystemState
    {
        public bool EngineOn;
        public bool AntigravityOn;
        public bool HoveringOn;
        public bool TransAMOn;
        public float Acceleration;
        public bool ECInputOn;
        public bool SafetyGuardOn;
        public bool SynchronizeOn;

        public static GNSystemState Empty => new GNSystemState
        {
            EngineOn = false,
            AntigravityOn = false,
            HoveringOn = false,
            TransAMOn = false,
            Acceleration = 0f,
            ECInputOn = false,
            SafetyGuardOn = false,
            SynchronizeOn = false
        };
    }

    public class GNBaseSystem : PartModule // Pure condenser, Effect control system implemented here.
    {
        // variables for visual, physics states.
        public GNVisualState vs = GNVisualState.Empty;
        public GNPhysicsState ps = GNPhysicsState.Empty;

        //Lists
        List<Transform> listT = new List<Transform>();
        List<Light> listL = new List<Light>();
        List<Renderer> listR = new List<Renderer>();
        List<KSPParticleEmitter> listE = new List<KSPParticleEmitter>();
        List<Transform> listM = new List<Transform>();
        List<Transform> listRot = new List<Transform>(); // rotating parts

        // variables for control state
        float X = 0f;
        float Y = 0f;
        float Z = 0f;
        float throttle = 0f;

        // struct for Sync
        public OnOffList OnOff = OnOffList.Empty;

        //KSP field for sync
        [KSPField(guiActiveEditor = false, guiActive = false, isPersistant = false, guiName = "MarkAsDirty")]
        public bool MarkDirty = false;

        //variables for audio.
        [KSPField] public string audioPath = "GNdrive/Audio/GNDriveTypical";
        AudioClip soundClip;
        AudioSource audioSource;

        // KSP Actions
        [KSPAction("Toggle Engine")]
        public void ToggleEngineAction(KSPActionParam param)
        {
            engineOn = !engineOn;
        }
        [KSPAction("Toggle Anti-Gravity")]
        public void ToggleAgAction(KSPActionParam param)
        {
            agOn = !agOn;
        }
        [KSPAction("Toggle Hovering")]
        public void ToggleHvAction(KSPActionParam param)
        {
            hvOn = !hvOn;
        }
        [KSPAction("Toggle TRANS-AM")]
        public void ToggleTaAction(KSPActionParam param)
        {
            taOn = !taOn;
        }
        [KSPAction("Increase Max-G")]
        public void IncreaseMaxGAction(KSPActionParam param)
        {
            accel += 0.1f;
            accel = Mathf.Clamp(accel, 0f, MaxAccel);
        }
        [KSPAction("Decrease Max-G")]
        public void DecreaseMaxGAction(KSPActionParam param)
        {
            accel -= 0.1f;
            accel = Mathf.Clamp(accel, 0f, MaxAccel);
        }
        [KSPAction("Toggle Input Electric Charge")]
        public void ToggleECAction(KSPActionParam param)
        {
            ECOn = !ECOn;
        }
        [KSPAction("Toggle Sustainable Thrust Limit")]
        public void ToggleSgAction(KSPActionParam param)
        {
            sgOn = !sgOn;
        }
        [KSPAction("Toggle Synchronize Target Drive")]
        public void ToggleSyAction(KSPActionParam param)
        {
            SyOn = !SyOn;
        }
        [KSPAction("Toggle OpenMode")]
        public void ToggleMoveOn(KSPActionParam param)
        {
            MoveOn = !MoveOn;
        }

        // Added actions
        [KSPAction("Toggle Repose")]
        public void ToggleRepose(KSPActionParam param)
        {
            Repose = !Repose;
        }

        // KSP fields for engine control.
        [KSPField(guiActive = false, guiActiveEditor = false, guiName = "Engine State", isPersistant = true), UI_Toggle(disabledText = "OFF", enabledText = "ON")]
        public bool engineOn = false;
        [KSPField(guiActive = false, guiActiveEditor = false, guiName = "Anti-Gravity", isPersistant = true), UI_Toggle(disabledText = "OFF", enabledText = "ON")]
        public bool agOn = false;
        [KSPField(guiActive = false, guiActiveEditor = false, guiName = "Hovering", isPersistant = true), UI_Toggle(disabledText = "OFF", enabledText = "ON")]
        public bool hvOn = false;
        [KSPField(guiActive = false, guiActiveEditor = false, guiName = "TRANS-AM", isPersistant = true), UI_Toggle(disabledText = "OFF", enabledText = "ON")]
        public bool taOn = false;
        [KSPField(guiActive = false, guiActiveEditor = false, guiName = "Max-G", isPersistant = true), UI_FloatRange(minValue = 0f, maxValue = 1f, stepIncrement = 0.1f)]
        public float accel = 1f;
        [KSPField(guiActive = false, guiActiveEditor = false, guiName = "Input Electric Charge", isPersistant = true), UI_Toggle(disabledText = "OFF", enabledText = "ON")]
        public bool ECOn = false;
        [KSPField(guiActive = false, guiActiveEditor = false, guiName = "Limit Thrust at sustainable level", isPersistant = true), UI_Toggle(disabledText = "OFF", enabledText = "ON")]
        public bool sgOn = false;
        [KSPField(guiActive = false, guiActiveEditor = false, guiName = "Synchronize Target Drive", isPersistant = true), UI_Toggle(disabledText = "OFF", enabledText = "ON")]
        public bool SyOn = false;

        // RCS
        [KSPField(guiActive = false, guiActiveEditor = false, guiName = "RCS Power Ratio", isPersistant = true), UI_FloatRange(minValue = 0f, maxValue = 1f, stepIncrement = 0.05f)]
        public float RCSpower = 1f;

        // For added actions
        [KSPField(guiActive = false, guiActiveEditor = false, guiName = "GN Repose", isPersistant = true), UI_Toggle(disabledText = "OFF", enabledText = "ON")]
        public bool Repose = false;
        [KSPField(guiActive = false, guiActiveEditor = false, guiName = "System Switch", isPersistant = true)]
        public GNSystemState previousGNSystemState = new GNSystemState();
        private bool previousRepose = false;
        protected string[] ReposeActionList;        // variables for Repose
        protected string[] ReposeFieldList;        // variables for Repose

        // KSP field for OriginalMaxG
        [KSPField(guiName = "Maximum Acceleration", guiActive = false, guiActiveEditor = true, isPersistant = true)]
        public float MaxAccel = 0f;

        // KSP field for indicate states
        [KSPField(guiName = "ESinternal", guiActive = false, guiActiveEditor = false, isPersistant = true)]
        public DriveState ES = DriveState.Deactivated;
        [KSPField(guiName = "Engine Status", guiActive = false, guiActiveEditor = false)]
        public string ESDisplay = "None";

        // KSP field for Specs (Particle/EC Generation rate (Condenser = 0))
        [KSPField(guiName = "Particle Generation", guiActive = true, guiActiveEditor = true, isPersistant = false)]
        public float ParticleGeneration = 0f;
        [KSPField(guiName = "ElectricCharge Generation", guiActive = false, guiActiveEditor = false, isPersistant = false)]
        public double ECGeneration = 0f;
        [KSPField(guiName = "MoveDistance", guiActive = false, guiActiveEditor = false, isPersistant = false)]
        public float MoveDistance = 0f;
        [KSPField(guiName = "AngleOfRotationParts", guiActive = false, guiActiveEditor = false, isPersistant = false)]
        public float RotationAngle = 0f;
        [KSPField(guiName = "Open Mode", guiActive = false, guiActiveEditor = false, isPersistant = true), UI_Toggle(disabledText = "OFF", enabledText = "ON")]
        public bool MoveOn = false;
        [KSPField(guiName = "IsMove", guiActive = false, guiActiveEditor = false, isPersistant = false)] // internal use, used for MovingParts control.
        public bool IsMove = false;

        // KSP field for drive individuality
        [KSPField(guiName = "Drive Individuality", guiActive = true, guiActiveEditor = true, isPersistant = true)]
        public float DriveIndividuality = -1f;
        [KSPField(guiActiveEditor = false, guiActive = false, isPersistant = true, guiName = "Manufactured")]
        public bool Manufactured = false;
        [KSPField(guiActiveEditor = false, guiActive = false, isPersistant = true, guiName = "PreLaunched")]
        public bool PreLaunched = false;
        private bool IsActivated = false;

        // Emissive Color Changer field
        [KSPField(guiActive = true, guiName = "Tau / GN Count")]
        public string counts = "0 / 0";
        [KSPField(guiActiveEditor = true, guiActive = true, isPersistant = true, guiName = "Use Avg Color")]
        public bool useAverageColor = true;
        [KSPField(guiActiveEditor = true, guiActive = true, isPersistant = true, guiName = "Accel Divided")]
        public float AccelDiv = 1f;
        [KSPField(guiActiveEditor = true, guiActive = true, isPersistant = true, guiName = "Synchronize Rate")]
        public float SynchronizeRate = 1f;

        // sound
        private float soundMinVolume = 0.2f;
        private float soundMaxVolume = 0.6f;
        private float soundMinPitch = 0.4f;
        private float soundMaxPitch = 1.0f;

        // debug variables
        private bool _lastEngineOn;

        public void Update()
        {
            if (engineOn && !IsActivated)
            {
                part.force_activate();
                IsActivated = true;
            }
        }

        public override void OnCopy(PartModule fromModule)
        {
            // for symmetric placement
            base.OnCopy(fromModule);
            DriveIndividuality = -1f;
            EnsureIndividuality();
        }

        public override void OnStart(StartState state)
        {
            // Check if loaded in editor
            base.OnStart(state);

            // common component setup, visuals
            Debug.Log("[GN] GN_Base_System OnStart called.");
            Debug.Log("[GN] Visual initialized.");
            VisualInit();
            DecideColor();

            // common component setup, physics
            Debug.Log("[GN] Physics initialized.");
            PhysicsInit();
            StatusInit();
            EnsureIndividuality();

            // Repose related initialization
            InitRepose();

            // Debug System Initialize
            _lastEngineOn = engineOn;
            Debug.Log("[GN] SystemInit Completed.");
            Debug.Log($"[GN] Base Engine online = {engineOn}");
            Debug.Log($"[GN] OnStart state={state} engineOn={engineOn}");
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            // Common Update, visual, (sound)
            VisualUpdate();
            UpdateDriveAudio(engineOn);// Update audio based on engine state.
            DecideColor();
            ESDisplay = ES.ToString(); // update Engine State display

            // Common Update, physics related status
            StatusUpdate();
            SyncUpdate();

            // Repose Update
            UpdateRepose();

            // debug
            if (engineOn != _lastEngineOn)
            {
                Debug.Log($"[GN] engineOn changed {_lastEngineOn} -> {engineOn}  scene={HighLogic.LoadedScene}  partState={part.State}");
                Debug.Log(Environment.StackTrace); // 参考：完全ではないけど手がかりになることがある
                _lastEngineOn = engineOn;
            }
        }

        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();
            PhysicsUpdate(); // Calc like TRANS-AM on/off judgement should be before particle generation.
            ParticleGenerationFixedUpdate();// last
        }

        private void VisualInit() // OnStart
        {
            // Initialize part visual state and PAW state.
            SetupAudio();
            MakeList();
            MakeVisualState();// values will be overwritten in each drive modules.
            PAWInitialization();
            ActionInitialization();

            // Works when in Editor, no visual effects.
            if (HighLogic.LoadedSceneIsEditor)  GNVisuals.SetOff(vs);
        }

        private void PhysicsInit()// OnStart
        {
            //debug
            Debug.Log("[GN] PhysicsInit called.");

            ps.part = part;
            ps.ParticlePower = 1f; // default nonzero negligible value. 
            ps.UsedGNParticle = ps.ParticleGenRate; // initialize used particle rate.
        }

        private void StatusInit()// OnStart
        {
            // for debug only
            Fields["useAverageColor"].guiActive = false;
            Fields["useAverageColor"].guiActiveEditor = false;
        }

        private void EnsureIndividuality()// OnStart
        {
            if (DriveIndividuality >= 0f) return;    // Already decided -> return,
            DriveIndividuality = GeneratePerPartValue(); // 0..1
        }

        private void VisualUpdate()
        {
            vs.EngineState = engineOn;
            vs.InputLevel = InputLevel();
            vs.MoveOn = MoveOn;
            vs.ThrustVector = ps.ThrustDir;
            GNVisuals.UpdateVisual(ref vs);
        }

        private void SyncUpdate()
        {
            // Desyncing
            if (!SyOn)
            {
                ps.SyncRate = 1f;
                SetMaxG(0, MaxAccel * ps.SyncRate);
                return;
            } 

            //Synching
            var current = new OnOffList
            {
                LengineOn = engineOn,
                LagOn = agOn,
                LtaOn = taOn,
                LhvOn = hvOn,
                LECOn = ECOn,
                LSgOn = sgOn,
                MaxG = accel
            };

            // usual state
            if (OnOff == current && IsThereOtherSyncDriveTarget(vessel, part.persistentId))
            {
                MarkDirty = false; //no change for both drive
                return;
            }

            // accel changed by user
            if (!(OnOff == current)) MarkDirty = true;

            // Do sync
            OnOff = current;
            if (MarkDirty) GNSynchronizer.SynchronizeOtherTargetDrive(OnOff, vessel, part.persistentId, ref ps);
            MarkDirty = false;
            SetMaxG(0, MaxAccel * ps.SyncRate);
            SynchronizeRate = ps.SyncRate;
        }

        private void ParticleGenerationFixedUpdate()
        {
            ps.ECOn = ECOn;
            GNGenerationFurnace.ParticleSupply(ref ps, TimeWarp.fixedDeltaTime);
        }

        private void PhysicsUpdate()
        {
            // only update physics when engine is on and vessel is unpacked
            if (!engineOn || vessel == null || vessel.packed) 
            {
                return;
            }

            // Refil Check
            if (ES == DriveState.Depleted)
            {
                engineOn = false;
                return;
            }

            // Drive is doing job here.
            if (engineOn)
            {
                PhysicsUpdateSupport();
                return;
            }
        }

        private void PhysicsUpdateSupport()
        {
            // TRANS-AM power down check


            // Update physics state
            ps.EngineState = engineOn;
            ps.AgOn = agOn;
            ps.TaOn = taOn;
            ps.HvOn = hvOn;
            ps.MaxG = accel;
            ps.ParticleGenRate = ParticleGeneration;
            ps.ECOn = ECOn;
            ps.SafeGuard = sgOn;
            ps.RCSFactor = RCSpower;
            GNPhysics.UpdatePhysics(ref ps);
            DriveStateReflection();
        }

        private void DriveStateReflection()
        {
            // those flags may be affected by Engine State controlled by GNPhysics. So Update is necessary.
            engineOn = ps.EngineState;
            agOn = ps.AgOn;
            taOn = ps.TaOn;
            hvOn = ps.HvOn;
            ECOn = ps.ECOn;
            ECOn = ps.ECOn;
        }

        private void StatusUpdate()
        {
            // Sync check, make better logic here.
            // if (ps.SyncRate < 1) ps.UnSync = true; old llogic
            ps.UnSync = ps.SyncRate < 1f; // right true => left true.

            // Engine State indicator
            if (ps.UnSync)
            {
                ES = DriveState.Unsynchronized;
                return;
            }

            // case Depleted.
            if (ES == DriveState.Depleted)
            {
                return;
            } 

            // case Engine on by user
            if (engineOn)
            {
                ES = DriveState.Activated;
            }
            else if(!engineOn && ES != DriveState.Refilled)
            {
                ES = DriveState.Deactivated;
            }
        }

        private float GeneratePerPartValue()
        {
            uint id = part.persistentId;            // KSP1.4+ available
            unchecked
            {
                uint x = id;
                // xorshift
                x ^= x << 13; x ^= x >> 17; x ^= x << 5;
                // 0..1 normalize
                return x / (float)uint.MaxValue;
            }
        }

        private void DecideColor()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;

            var ag = GNVisualAggregator.Aggregate(vessel);
            counts = $"{ag.TauCount} / {ag.GNCount}";

            var c = useAverageColor ? ag.AvgColor : ag.SumColor;

            vs.ParticleColor = (vs.ParticleColor == Color.black) ? new Color(0f, 1f, 170f / 255f, 1f) : c;
        }

        private void MakeVisualState()
        {
            // default mode, this should be changed in derived classes.
            vs.part = part;
            vs.ParticleColor = new Color(1f, 0f, 0.15f, 1f); // Red for condenser.
            vs.Mode = GNVisualMode.Condenser;
            vs.Rotors = listT.ToArray();
            vs.EmissiveRenderers = listR.ToArray();
            vs.GlowLights = listL.ToArray();
            vs.ParticleEmitters = listE.ToArray();
            vs.MovingParts = listM.ToArray();
            // parts specific values
            vs.RotorSpeed = 60f; // condenser rotor speed
            vs.MoveDistance = MoveDistance; // move distance in meters.

            // Rotating parts specific values
            vs.RotParts = listRot.ToArray();
            vs.RotAngleX = RotationAngle; // rotation angle in degrees.
        }

        private void MakeList()
        {
            // List Initialize
            listT.Clear();
            listL.Clear();
            listR.Clear();
            listE.Clear();
            listM.Clear();
            listRot.Clear();

            // Make lists of Lights, Renderers, Emitters, etc. here if needed.
            var allT = part.transform.GetComponentsInChildren<Transform>(true);
            foreach (var t in allT)
            {
                if (t.name.Contains("rotor") && t.GetComponentInParent<Part>() == this.part)
                    listT.Add(t);// Rotating parts
                if (t.name.Contains("_Move") && t.GetComponentInParent<Part>() == this.part)
                {
                    listM.Add(t);// Moving Parts
                    IsMove = true;
                }
                if (t.name.Contains("rotation") && t.GetComponentInParent<Part>() == this.part)
                    listRot.Add(t);// Specific angle rotating parts
            }

            var allL = part.transform.GetComponentsInChildren<Light>(true);
            foreach (var l in allL)
            {
                if (l.GetComponentInParent<Part>() == this.part) 
                    listL.Add(l);//Lights are always added.
            } 

            var allR = part.transform.GetComponentsInChildren<Renderer>(true);
            foreach (var r in allR)
            {
                if ((r.name.Contains("rotor") || r.name.Contains("stator")) && r.GetComponentInParent<Part>() == this.part)
                    listR.Add(r);
            }

            var allE = part.transform.GetComponentsInChildren<KSPParticleEmitter>(true);
            foreach (var e in allE)
            {
                if (e.name.Contains("EMI") && e.GetComponentInParent<Part>() == this.part)
                {
                    e.emit = false;
                    listE.Add(e);
                }
            }
        }

        private void SetupAudio()
        {
            try
            {
                if (string.IsNullOrEmpty(audioPath))
                {
                    Debug.LogWarning("[GN] audioPath is empty");
                    return;
                }

                soundClip = GameDatabase.Instance.GetAudioClip(audioPath);
                if (soundClip == null)
                {
                    Debug.LogError("[GN] AudioClip not found: " + audioPath);
                    return;
                }
                Debug.Log("[GN] Sound loaded: " + soundClip.name);

                // AudioSource should be attached to the part if possible, otherwise to the part's root object
                var host = part != null ? part.gameObject : this.gameObject;
                audioSource = host.GetComponent<AudioSource>() ?? host.AddComponent<AudioSource>();
                if (audioSource == null)
                {
                    Debug.LogError("[GN] AudioSource add/get failed");
                    return;
                }

                audioSource.clip = soundClip;
                audioSource.loop = true;
                audioSource.playOnAwake = false;
                audioSource.dopplerLevel = 0f;
                audioSource.spatialBlend = 1f; // 3D
                audioSource.minDistance = 5f;
                audioSource.maxDistance = 150f;
                audioSource.priority = 128;    // 0 to 256
                audioSource.volume = 0f;       // 0..1
                audioSource.pitch = 1f;       // uusually 1f
            }
            catch (Exception e)
            {
                Debug.LogError("[GN] SetupAudio exception: " + e);
                audioSource = null; //avoid repeated error
            }
        }

        private void UpdateDriveAudio(bool shouldPlay)
        {
            if (audioSource == null) return;

            // base
            float volume = audioSource.volume;
            float pitch = audioSource.pitch;
            float step = 1f * Time.deltaTime;
            bool brake = false;
            Vector3 vSrf = (Vector3)vessel.srf_velocity;
            float speed = vSrf.magnitude;

            // target
            float tgtVolume = Mathf.Lerp(soundMinVolume, soundMaxVolume, vs.InputLevel);
            float tgtPitch = Mathf.Lerp(soundMinPitch, soundMaxPitch, vs.InputLevel);
            brake = vessel.ActionGroups[KSPActionGroup.Brakes];

            // sound reduction when braking
            if (brake && speed < 0.05f) // reduce sound when brakes are on, speed < 0.05m/s
            {
                tgtVolume = soundMinVolume;
                tgtPitch = soundMinPitch;
            }

            // playsound
            if (shouldPlay)
            {
                if (!audioSource.isPlaying) audioSource.Play();
                //audioSource.volume = Mathf.Lerp(soundMinVolume, soundMaxVolume, vs.InputLevel);
                //audioSource.pitch = Mathf.Lerp(soundMinPitch, soundMaxPitch, vs.InputLevel);
                audioSource.volume = Mathf.MoveTowards(volume, tgtVolume, step);
                audioSource.pitch = Mathf.MoveTowards(pitch, tgtPitch, step);
            }
            else
            {
                audioSource.Stop();
                audioSource.volume = 0f;
            }
        }

        private void PAWInitialization()
        {
            if (Fields == null) return;

            void Hide(string name)
            {
                var f = Fields[name];
                if (f != null)
                {
                    f.guiActive = false;
                    f.guiActiveEditor = false;
                }
                else
                {
                    Debug.LogWarning($"[GN] KSPField '{name}' not found (skipped).");
                }
            }

            // Basic PAW flight & editor
            Hide("engineOn");
            Hide("agOn");
            Hide("hvOn");
            Hide("taOn");
            Hide("accel");
            Hide("ES");
            Hide("SyOn");
            Hide("sgOn");
            Hide("MoveOn");

            Hide("ParticleGeneration");
            Hide("ECGeneration");
            Hide("DriveIndividuality");
            Hide("counts");
            Hide("useAverageColor");
            Hide("AccelDiv");
            Hide("SynchronizeRate");

            // Tau only
            Hide("ECOn");

            // added fields
            Hide("Repose");
            Hide("RCSpower");
        }
        private void ActionInitialization()
        {
            if (Fields == null) return;

            void Hide(string name)
            {
                var Ac = Actions[name];
                if (Ac != null)
                {
                    Ac.active = false;
                }
                else
                {
                    Debug.LogWarning($"[GN] KSPAction '{name}' not found (skipped).");
                }
            }

            // Basic PAW flight & editor
            Hide("ToggleEngineAction");
            Hide("ToggleAgAction");
            Hide("ToggleHvAction");
            Hide("ToggleTaAction");
            Hide("IncreaseMaxGAction");
            Hide("DecreaseMaxGAction");
            Hide("ToggleECAction");
            Hide("ToggleSgAction");
            Hide("ToggleSyAction");
            Hide("ToggleMoveOn");

            // added actions
            Hide("ToggleRepose");
        }

        protected void PAWActivate(params string[] fieldNames)
        {
            foreach (var name in fieldNames)
            {
                var f = Fields[name];
                if (f != null)
                {
                    Fields[name].guiActive = true;
                    Fields[name].guiActiveEditor = true;
                }
            }
        }

        protected void PAWDeactivate(params string[] fieldNames)
        {
            foreach (var name in fieldNames)
            {
                var f = Fields[name];
                if (f != null)
                {
                    Fields[name].guiActive = false;
                    Fields[name].guiActiveEditor = false;
                }
            }
        }

        protected void ActionDeactivate(params string[] names)
        {
            foreach (var name in names)
            {
                var a = Actions[name];
                if (a != null)
                {
                    a.active = false;
                }
            }
        }

        protected void ActionActivate(params string[] names)
        {
            foreach (var name in names)
            {
                var a = Actions[name];
                if (a != null)
                {
                    a.active = true;
                }
            }
        }

        protected float InputLevel()
        {
            X = Mathf.Abs(vessel.ctrlState.X);
            Y = Mathf.Abs(vessel.ctrlState.Y);
            Z = Mathf.Abs(vessel.ctrlState.Z);
            throttle = vessel.ctrlState.mainThrottle;
            //return (X + Y + Z + throttle) * accel / 5f; // AccelDiv;Calculate percentage of accel later!
            return (X + Y + Z + throttle) * accel / MaxAccel;
        }

        protected void SetMaxG(float min, float max)
        {
            // accel field is for drive Control
            ApplyRange(Fields["accel"], min, max, 0.1f);
            accel = Mathf.Clamp(accel, min, max);
            AccelDiv = max;
        }

        private void ApplyRange(BaseField f, float min, float max, float step)
        {
            var e = f.uiControlEditor as UI_FloatRange;
            var fl = f.uiControlFlight as UI_FloatRange;

            if (e != null) { e.minValue = min; e.maxValue = max; e.stepIncrement = step; }if (ps.SyncRate < 1) ps.UnSync = true;
            if (fl != null) { fl.minValue = min; fl.maxValue = max; fl.stepIncrement = step; }
        }

        protected void CompressIndividuality(float rate)
        {
            DriveIndividuality *= rate;
        }

        private void InitRepose()
        {
            Repose = false; // engine will on when Repose is false at start.
            previousRepose = Repose;
            previousGNSystemState = GNSystemState.Empty; // need initialization to work?
            engineOn = false;
        }

        private void UpdateRepose()
        {
            if (previousRepose == Repose)
            {
                return; // no change
            }
            if (Repose)
            {
                PAWDeactivate(ReposeFieldList);
                ActionDeactivate(ReposeActionList);
                retractGNSystemState();
                previousRepose = Repose;
            }
            else
            {
                PAWActivate(ReposeFieldList);
                ActionActivate(ReposeActionList);
                deployGNSystemState();
                previousRepose = Repose;
            }
        }

        private void retractGNSystemState()
        {
            previousGNSystemState = new GNSystemState
            {
                EngineOn = true,
                AntigravityOn = agOn,
                HoveringOn = hvOn,
                TransAMOn = taOn,
                Acceleration = accel,
                ECInputOn = ECOn,
                SafetyGuardOn = sgOn,
                SynchronizeOn = SyOn
            };

            engineOn = true;
            agOn = false;
            hvOn = false;
            taOn = false;
            accel = 0f;
            ECOn = false;
            sgOn = true; // when repose, drive should be safe state.
            SyOn = false;
        }
        
        private void deployGNSystemState()
        {
            engineOn = previousGNSystemState.EngineOn;
            agOn = previousGNSystemState.AntigravityOn;
            hvOn = previousGNSystemState.HoveringOn;
            taOn = previousGNSystemState.TransAMOn;
            accel = previousGNSystemState.Acceleration;
            ECOn = previousGNSystemState.ECInputOn;
            sgOn = previousGNSystemState.SafetyGuardOn;
            SyOn = previousGNSystemState.SynchronizeOn;
        }

        protected void PreLaunchSetup()
        {
            if (PreLaunched) return; // already prelaunched
            engineOn = false;
            agOn = false;
            hvOn = false;
            taOn = false;
            accel = 0f;
            ECOn = false;
            sgOn = true; // safe state
            SyOn = false;
            PreLaunched = true;
        }
    }

    public class GNThrusterSystem : GNBaseSystem // GN thrusters
    {
        [KSPField(guiName = "Max Particle Output", guiActive = true)]
        public float particlepower = 200f;

        public override void OnActive()
        {
            base.OnActive();

            // for staging activation
            engineOn = true;
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            // set accel
            SetMaxG(0f, MaxAccel);

            // for staging icon
            part.stagingIcon = "LIQUID_ENGINE";
            part.stagingIconAlwaysShown = true;
            part.stagingOn = true;

            ReposeFieldList = new string[] { "engineOn", "accel", "ESDisplay", "RCSpower" };
            ReposeActionList = new string[] { "ToggleEngineAction", "IncreaseMaxGAction", "DecreaseMaxGAction" };

            // PAW and Action setup
            if (HighLogic.LoadedSceneIsFlight)
            {
                PAWActivate(ReposeFieldList);
                PAWActivate("Repose");
            }
            else
            {
                PAWActivate("accel");
            }
            ActionActivate(ReposeActionList);

            vs.Mode = GNVisualMode.Drive;
            vs.RotorSpeed = 180f; // thruster rotor speed
            vs.ParticleColor = new Color(0f, 1f, 170f / 255f, 1f); // Original GN Drive color
            ps.ParticlePower = particlepower;
            ps.MaxG = accel;
            sgOn = false;
            MoveOn = true; // Thruster always open.

            // Unit Off when start.
            GNVisuals.SetOff(vs);

            // Debug
            Debug.Log($"[GN] vs.Mode={vs.Mode}");

            // PreLaunch setup
            PreLaunchSetup();

            // Debug
            Debug.Log($"[GN] Engine online = {engineOn}");
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
            if (engineOn && part.Resources["GNparticle"].amount < 1)
            {
                engineOn = false;
            }
        }

        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();
        }
    }

    public class GNCondenserDriveSystem : GNBaseSystem // GN condenser-type drive
    {
        [KSPField(guiName = "Max Particle Output", guiActive = true)]
        public float particlepower = 800f;

        public override void OnActive()
        {
            base.OnActive();

            // for staging activation
            engineOn = true;
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            //Set accel
            SetMaxG(0f, MaxAccel);

            // for staging icon
            part.stagingIcon = "LIQUID_ENGINE";
            part.stagingIconAlwaysShown = true;
            part.stagingOn = true;

            // PAW and Action setup
            ReposeFieldList = new string[] { "engineOn", "agOn", "hvOn", "accel", "ESDisplay", "RCSpower" };
            ReposeActionList = new string[] { "ToggleEngineAction", "ToggleAgAction", "ToggleHvAction", "IncreaseMaxGAction", "DecreaseMaxGAction" };

            if (HighLogic.LoadedSceneIsFlight)
            {
                PAWActivate(ReposeFieldList);
                PAWActivate("Repose");
            }
            else
            {
                PAWActivate("accel");
            }
            ActionActivate(ReposeActionList);

            vs.Mode = GNVisualMode.CondenserDrive;
            vs.RotorSpeed = 180f; // thruster rotor speed
            vs.ParticleColor = new Color(0f, 1f, 170f / 255f, 1f); // Original GN Drive color
            ps.ParticlePower = particlepower;
            ps.MaxG = accel;
            sgOn = false;

            // Unit Off when start.
            GNVisuals.SetOff(vs);

            // Debug
            Debug.Log($"[GN] vs.Mode={vs.Mode}");

            // PreLaunch setup
            PreLaunchSetup();

            // Debug
            Debug.Log($"[GN] Engine online = {engineOn}");
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
            if (hvOn && agOn)
            {
                agOn = false;
                ps.AgOn = false;
                ps.HvOn = true;
            }
            if (engineOn && part.Resources["GNparticle"].amount < 1)
            {
                engineOn = false;
            }
        }

        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();
        }
    }

    public class GNDriveTauSystem : GNBaseSystem // GN Drive Tau
    {
        [KSPField(guiName = "Max Particle Output", guiActive = true)]
        public float particlepower = 1200f;
        [KSPField(guiName = "Manufacture Variance", guiActive = true)]
        public float ManufactureVariance = 0.5f;

        public override void OnActive()
        {
            base.OnActive();

            // for staging activation
            engineOn = true;
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            // set accel
            SetMaxG(0f, MaxAccel);

            // for staging icon
            part.stagingIcon = "LIQUID_ENGINE";
            part.stagingIconAlwaysShown = true;
            part.stagingOn = true;

            // PAW and Action setup
            ReposeFieldList = new string[] { "agOn", "hvOn", "accel", "SyOn", "ECOn", "sgOn", "DriveIndividuality", "SynchronizeRate", "ParticleGeneration", "ESDisplay", "RCSpower" };
            ReposeActionList = new string[] { "ToggleEngineAction", "ToggleAgAction", "ToggleHvAction", "IncreaseMaxGAction", "DecreaseMaxGAction", "ToggleECAction", "ToggleSgAction", "ToggleSyAction" };

            if (HighLogic.LoadedSceneIsFlight)
            {
                PAWActivate(ReposeFieldList);
                if (part.Resources["TopologicalDefects"].amount < 0.5)
                {
                    PAWActivate("engineOn");
                }
                else
                {
                    PAWDeactivate("engineOn");
                    engineOn = true;
                    ECOn = true;
                }
                PAWActivate("Repose");
                if (IsMove)
                {
                    Debug.Log("[GN] GN Drive Move enable detected in flight PAW.");
                    sgOn = true;
                }
            }
            else
            {
                PAWActivate("accel", "SyOn", "sgOn", "ECOn", "DriveIndividuality", "ParticleGeneration");
            }
            ActionActivate(ReposeActionList);

            vs.Mode = GNVisualMode.Drive;
            vs.RotorSpeed = 180f; // thruster rotor speed
            //vs.ParticleColor = ParticleColor(); // Red for condenser.
            vs.ParticleColor = GNColorDecider.ParticleColor(vs);
            ps.ParticlePower = particlepower;
            ps.MaxG = accel;

            // For Twin drive
            if (!Manufactured) CompressIndividuality(ManufactureVariance);
            Manufactured = true; // Mark as manufactured.
            ps.SyncRate = 1f; // initialize sync rate.

            // Unit Off when start.
            GNVisuals.SetOff(vs);

            // Debug
            Debug.Log($"[GN] vs.Mode={vs.Mode}");

            // PreLaunch setup
            PreLaunchSetup();

            // Debug
            Debug.Log($"[GN] Engine online = {engineOn}");
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            // additional Engine State Control
            MoveOn = !sgOn;
            vs.MoveOn = MoveOn;

            //vs.ParticleColor = ParticleColor(); // Red for condenser, To avoid override on GNBaseSystem Class(DecideColor).
            vs.ParticleColor = GNColorDecider.ParticleColor(vs);
            ps.SafeGuard = sgOn;
            if (hvOn && agOn)
            {
                agOn = false;
                ps.AgOn = false;
                ps.HvOn = true;
            }

            // TD enables perpetual drive.
            if (part.Resources["TopologicalDefects"].amount >= 0.5)
            {
                engineOn = true;
                ECOn = true;
                return; // engine won't stop
            }

            // DeplitionCheck
            if (EngineDepleted())
            {
                ps.EngineState = false;
            }

            // Update Engine State based on GNparticle resource
            ES = ParticleDepletionState(part.Resources["GNparticle"].amount, part.Resources["GNparticle"].maxAmount, ref ps, ES);
        }

        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();
        }

        private bool EngineDepleted()
        {
            if (ps.GNdepleted && part.Resources["GNparticle"].amount < part.Resources["GNparticle"].maxAmount)
            {
                return true;
            } 

            return false;
        }

        private DriveState ParticleDepletionState(double amount, double maxAmount, ref GNPhysicsState ps, DriveState PreviousES)
        {
            if (amount < 1)
            {
                ps.GNdepleted = true;
                return DriveState.Depleted;
            }
            else if (amount >= maxAmount && ps.GNdepleted)
            {
                ps.GNdepleted = false;
                return DriveState.Refilled;
            }
            else
            {
                return PreviousES;
            }
        }
    }

    public class GNDriveSystem : GNBaseSystem // GN Drive (Original)
    {
        [KSPField(guiName = "Max Particle Output", guiActive = true)]
        public float particlepower = 1000f;
        [KSPField(guiName = "Manufacture Variance", guiActive = true)]
        public float ManufactureVariance = 1f;
        [KSPField(guiName = "2nd Generation", guiActive = true)]
        public bool isSecondGen = false;
        [KSPField(guiName = "Safety Functiuon", guiActive = true)]
        public bool isSgOn = false;
        public bool TaDisabled = false; // for deactivate TRANS-AM for 2nd Gen Drive

        public override void OnActive()
        {
            base.OnActive();

            // for staging activation
            engineOn = true;
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            // set accel
            SetMaxG(0f, MaxAccel);

            part.stagingIcon = "LIQUID_ENGINE";
            part.stagingIconAlwaysShown = true;
            part.stagingOn = true;

            // force Activate, since GN Drive is always on.
            part.force_activate();

            // PAW and Action setup
            ReposeFieldList = new string[] { "agOn", "hvOn", "taOn", "accel", "DriveIndividuality", "SynchronizeRate", "ParticleGeneration", "ESDisplay", "RCSpower" };
            ReposeActionList = new string[] { "ToggleEngineAction", "ToggleAgAction", "ToggleHvAction", "IncreaseMaxGAction", "DecreaseMaxGAction" };

            if (HighLogic.LoadedSceneIsFlight)
            {
                PAWActivate(ReposeFieldList);
                PAWActivate("Repose");
                if (IsMove)
                {
                    Debug.Log("[GN] GN Drive Move enable detected in flight PAW.");
                    PAWActivate("sgOn");
                    sgOn = true;
                }
            }
            else
            {
                PAWActivate("accel", "DriveIndividuality", "ParticleGeneration");
                Debug.Log("[GN] GN Drive Move disable detected in flight PAW.");
                sgOn = false;
            }
            ActionActivate(ReposeActionList);
            if (IsMove) ActionActivate("ToggleMoveOn");

            // check sg
            isSgOn = sgOn;
            Debug.Log($"[GN] GN Drive isSgOn={isSgOn}");

            vs.RotorSpeed = 180f; // thruster rotor speed
            // engineOn = true; // GN Drive is always on.
            ECOn = true; // GN Drive generates EC through EC consumption calculation method.
            vs.Mode = GNVisualMode.Drive;
            //vs.ParticleColor = new Color(0f, 1f, 170f / 255f, 1f); // Original GN Drive color
            vs.ParticleColor = GNColorDecider.ParticleColor(vs);
            ps.ParticlePower = particlepower;
            ps.SafeGuard = sgOn; // No need for safeguard for perpetual drive.
            ps.MaxG = accel;

            // For Twin drive
            if (!Manufactured) CompressIndividuality(ManufactureVariance);
            Manufactured = true; // Mark as manufactured.
            ps.SyncRate = 1f; // initialize sync rate.

            // Debug
            Debug.Log($"[GN] vs.Mode={vs.Mode}");
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
            ParticleColorSwitcher();
            if (hvOn && agOn)
            {
                agOn = false;
                ps.AgOn = false;
                ps.HvOn = true;
            }
        }

        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();
            SyOn = true; // force sync

            // Power shortage safeguard for 1st gen drive
            sgOn = EngineSafeGuard(sgOn, ps.Shortage, isSecondGen);

            // TRANS-AM Check
            taOn = TransAMControl(ref TaDisabled, taOn, isSecondGen, ps.Shortage);

            // check sg
            isSgOn = sgOn;

            // additional Engine State Control
            MoveOn = !sgOn;
            vs.MoveOn = MoveOn;
            ps.SafeGuard = sgOn;

            // GN drive needs TD for actual work.
            if (part.Resources["GNparticle"].amount > 0 || part.Resources["TopologicalDefects"].amount >= 0.5)
            {
                engineOn = true; // GN Drive is always on.
                ECOn = true; // Particle Generation is always on when there are enough TD.
            }
            else
            {
                ECOn = false; // prevent ElectricCharge drawn when power down.
                engineOn = false; // GN Drive power down.
            }
        }

        private void ParticleColorSwitcher()
        {
            if (taOn)
            {
                vs.ParticleColor = new Color(1F, 0F, 100F / 255F, 1F);// For Trans-AM drive color
            }
            else if (ps.UnSync)
            {
                vs.ParticleColor = new Color(0F, 1F / 4F, 42F / 255F, 1F);//Unsynchronized Color.
            }
            else
            {
                //vs.ParticleColor = new Color(0f, 1f, 170f / 255f, 1f); // Original GN Drive color
                vs.ParticleColor = GNColorDecider.ParticleColor(vs);
            }
        }

        private bool TransAMControl(ref bool myTaDisabled, bool myTAOn, bool myIsSecondGen, bool myShortage)
        {
            // TRANS-AM enable -> TaDisabled = false, once activate TRANS-AM, TaDisabled will keep TRANS-AM On.
            if (myTAOn && !myShortage && myIsSecondGen)
            {
                myTaDisabled = false;
            }

            // sustain TaOn if TaDisabled is false.TaDisabled of 2nd Gen is always true. 
            if (!myShortage && !myTaDisabled)
            {
                return true;
            }

            // TaDisabled is true when power shortage
            if (myShortage)
            {
                myTaDisabled = true;
                return false;
            }
            else
            {
                // default continue previous state, and reset TaDisabled.
                myTaDisabled = false;
                return myTAOn;
            }
        }

        private bool EngineSafeGuard(bool myIsSgOn, bool myShortage, bool myIsSecondGen)
        {
            if (myShortage && !myIsSecondGen)
            {
                return true;
            }
            else
            {
                return myIsSgOn;
            }
        }
    }
}