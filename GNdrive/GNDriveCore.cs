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
    public enum DriveState //public enum DriveState {Unsynchronized, Depleted, Activated, Deactivated, Refilled}
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
        Refilled
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

        // KSP field for OriginalMaxG
        [KSPField(guiName = "Maximum Acceleration", guiActive = false, guiActiveEditor = true, isPersistant = true)]
        public float MaxAccel = 0f;

        // KSP field for indicate states
        [KSPField(guiName = "ESinternal", guiActive = false, guiActiveEditor = false, isPersistant = true)]
        public DriveState ES = DriveState.Deactivated;
        [KSPField(guiName = "Engine Status", guiActive = false, guiActiveEditor = false)]
        public string ESDisplay = "None";

        // KSP field for Specs (Particle/EC Generation rate (Condenser = 0))
        [KSPField(guiName = "Particle Generation", guiActive = true, guiActiveEditor = true, isPersistant = true)]
        public float ParticleGeneration = 0f;
        [KSPField(guiName = "ElectricCharge Generation", guiActive = false, guiActiveEditor = false, isPersistant = true)]
        public double ECGeneration = 0f;
        [KSPField(guiName = "MoveDistance", guiActive = false, guiActiveEditor = false, isPersistant = true)]
        public float MoveDistance = 0f;
        [KSPField(guiName = "Open Mode", guiActive = false, guiActiveEditor = false, isPersistant = true), UI_Toggle(disabledText = "OFF", enabledText = "ON")]
        public bool MoveOn = false;
        [KSPField(guiName = "IsMove", guiActive = false, guiActiveEditor = false, isPersistant = false)] // internal use, used for MovingParts control.
        public bool IsMove = false;

        // KSP field for drive individuality
        [KSPField(guiName = "Drive Individuality", guiActive = true, guiActiveEditor = true, isPersistant = true)]
        public float DriveIndividuality = -1f;

        // Emissive Color Changer field
        [KSPField(guiActive = true, guiName = "Tau / GN Count")]
        public string counts = "0 / 0";

        [KSPField(guiActiveEditor = true, guiActive = true, isPersistant = true, guiName = "Use Avg Color")]
        public bool useAverageColor = true;

        [KSPField(guiActiveEditor = true, guiActive = true, isPersistant = true, guiName = "Accel Divided")]
        public float AccelDiv = 1f;

        [KSPField(guiActiveEditor = true, guiActive = true, isPersistant = true, guiName = "Synchronize Rate")]
        public float SynchronizeRate = 0f;

        public override void OnAwake()
        {
            base.OnAwake();
            part.enabled = true;
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

            // init
            Debug.Log("[GN] GN_Base_System OnStart called.");
            VisualInit();
            PhysicsInit();
            StatusInit();
            EnsureIndividuality();
            DecideColor();

            // System Initialize
            SystemInit();

            Debug.Log("[GN] SystemInit Completed.");
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
            VisualUpdate();
            StatusUpdate();
            UpdateDriveAudio(engineOn);// Update audio based on engine state.
            DecideColor();
            SyncUpdate();
            ParticleGenerationUpdate();
            ESDisplayUpdate(); // update Engine State display
        }

        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();
            PhysicsUpdate(); // Calc like TRANS-AM on/off judgement should be before particle generation.
            ParticleGenerationFixedUpdate();
        }

        private void SystemInit()
        {
            part.force_activate(); // Keep part activated. 
        }

        private void VisualInit()
        {
            // Initialize part visual state and PAW state.
            SetupAudio();
            MakeList();
            MakeVisualState();// values will be overwritten in each drive modules.
            PAWInitialization();

            // Works when in Editor, no visual effects.
            if (HighLogic.LoadedSceneIsEditor)
            {
                GNVisuals.SetOff(vs);
            }
        }

        private void PhysicsInit()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                return;
            }
            ps.part = part;
            ps.ParticlePower = 1f; // default nonzero negligible value. 
        }

        private void StatusInit()
        {
            // for debug only
            Fields["useAverageColor"].guiActive = false;
            Fields["useAverageColor"].guiActiveEditor = false;
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

        private void ParticleGenerationUpdate()
        {
            // Editor, no need for PG
            if (!HighLogic.LoadedSceneIsFlight || vessel == null) 
                return;

            // Time Warp, particle generation continues in timewarp.
            if (vessel.packed)
                GNGenerationFurnace.ParticleSupply(ref ps, TimeWarp.deltaTime);
        }

        private void ParticleGenerationFixedUpdate()
        {
            ps.ECOn = ECOn;
            GNGenerationFurnace.ParticleSupply(ref ps, TimeWarp.fixedDeltaTime);
        }

        private void VisualUpdate()
        {
            vs.EngineState = engineOn;
            vs.InputLevel = InputLevel();
            vs.MoveOn = MoveOn;
            GNVisuals.UpdateVisual(ref vs);
        }

        private void PhysicsUpdate()
        {
            // only update physics when engine is on and vessel is unpacked
            if (!engineOn || vessel == null || vessel.packed) 
            {
                return;
            }

            // Refilled is ready for active.
            switch (ES)
            {
                case DriveState.Unsynchronized:// drive works at low level
                case DriveState.Activated: // drive functioning
                    PhysicsUpdateSupport();
                    break;

                case DriveState.Depleted:
                    engineOn = false;
                    break;

                default:
                    break;
            }
        }

        private void PhysicsUpdateSupport()
        {
            ps.EngineState = engineOn;
            ps.AgOn = agOn;
            ps.TaOn = taOn;
            ps.HvOn = hvOn;
            ps.MaxG = accel;
            ps.ParticleGenRate = ParticleGeneration;
            ps.ECOn = ECOn;
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
                ES = DriveState.Depleted;
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

        private void EnsureIndividuality()
        {
            if (DriveIndividuality >= 0f) return;    // Already decided -> return,
            DriveIndividuality = GeneratePerPartValue(); // 0..1
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
        }

        private void MakeList()
        {
            // List Initialize
            listT.Clear();
            listL.Clear();
            listR.Clear();
            listE.Clear();
            listM.Clear();

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

            if (shouldPlay)
            {
                if (!audioSource.isPlaying) audioSource.Play();
                audioSource.volume = 1.0f;
                audioSource.pitch = 1.0f;
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

        protected void ESDisplayUpdate()
        {
            ESDisplay = ES.ToString();
        }
    }

    public class GNThrusterSystem : GNBaseSystem // GN thrusters
    {
        [KSPField(guiName = "Max Particle Output", guiActive = true)]
        public float particlepower = 200f;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            // set accel
            SetMaxG(0f, MaxAccel);

            part.stagingIcon = "LIQUID_ENGINE";
            part.stagingIconAlwaysShown = true;
            part.stagingOn = true;

            if (HighLogic.LoadedSceneIsFlight)
            {
                PAWActivate("engineOn", "accel", "ESDisplay");
            }
            else
            {
                PAWActivate("accel");
            }

            vs.Mode = GNVisualMode.Drive;
            vs.RotorSpeed = 180f; // thruster rotor speed
            vs.ParticleColor = new Color(0f, 1f, 170f / 255f, 1f); // Original GN Drive color
            ps.ParticlePower = particlepower;
            ps.MaxG = accel;
            MoveOn = true; // Thruster always open.

            // Unit Off when start.
            GNVisuals.SetOff(vs);

            // Debug
            Debug.Log($"[GN] vs.Mode={vs.Mode}");
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

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            //Set accel
            SetMaxG(0f, MaxAccel);

            part.stagingIcon = "LIQUID_ENGINE";
            part.stagingIconAlwaysShown = true;
            part.stagingOn = true;

            if (HighLogic.LoadedSceneIsFlight)
            {
                PAWActivate("engineOn", "agOn", "hvOn", "accel", "ESDisplay");
            }
            else
            {
                PAWActivate("accel");
            }

            vs.Mode = GNVisualMode.CondenserDrive;
            vs.RotorSpeed = 180f; // thruster rotor speed
            vs.ParticleColor = new Color(0f, 1f, 170f / 255f, 1f); // Original GN Drive color
            ps.ParticlePower = particlepower;
            ps.MaxG = accel;

            // Unit Off when start.
            GNVisuals.SetOff(vs);

            // Debug
            Debug.Log($"[GN] vs.Mode={vs.Mode}");
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
            ps.SafeGuard = false;
        }
    }

    public class GNDriveTauSystem : GNBaseSystem // GN Drive Tau
    {
        [KSPField(guiName = "Max Particle Output", guiActive = true)]
        public float particlepower = 1200f;

        private bool Dep;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            // set accel
            SetMaxG(0f, MaxAccel);

            part.stagingIcon = "LIQUID_ENGINE";
            part.stagingIconAlwaysShown = true;
            part.stagingOn = true;

            if (HighLogic.LoadedSceneIsFlight)
            {
                PAWActivate("agOn", "hvOn", "accel", "SyOn", "ECOn", "sgOn", "DriveIndividuality", "SynchronizeRate", "ParticleGeneration", "ESDisplay");
                if (part.Resources["TopologicalDefects"].amount < 0.5)
                {
                    PAWActivate("engineOn");
                }
                else
                {
                    engineOn = true;
                    ECOn = true;
                }
            }
            else
            {
                PAWActivate("accel", "SyOn", "sgOn", "ECOn", "DriveIndividuality", "ParticleGeneration");
            }

            vs.Mode = GNVisualMode.Drive;
            vs.RotorSpeed = 180f; // thruster rotor speed
            //vs.ParticleColor = ParticleColor(); // Red for condenser.
            vs.ParticleColor = GNColorDecider.ParticleColor(vs);
            ps.ParticlePower = particlepower;
            ps.MaxG = accel;

            // For Twin drive
            CompressIndividuality();

            // Unit Off when start.
            GNVisuals.SetOff(vs);

            // Debug
            Debug.Log($"[GN] vs.Mode={vs.Mode}");
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
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

            Dep = EngineDepleted();
            if (Dep) ps.EngineState = false;
            if (part.Resources["GNparticle"].amount < 1)
            {
                ES = DriveState.Depleted;
            }
            else if (part.Resources["GNparticle"].amount == part.Resources["GNparticle"].maxAmount && ES == DriveState.Depleted)
            {
                ES = DriveState.Refilled;// now enable engine On.
            }
        }

        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();
        }

        private bool EngineDepleted()
        {
            if (ES == DriveState.Depleted && part.Resources["GNparticle"].amount < part.Resources["GNparticle"].maxAmount) return true;
            return false;
        }

        private void CompressIndividuality()
        {
            DriveIndividuality *= 0.5f;
        }
    }

    public class GNDriveSystem : GNBaseSystem // GN Drive (Original)
    {
        [KSPField(guiName = "Max Particle Output", guiActive = true)]
        public float particlepower = 1000f;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            // set accel
            SetMaxG(0f, MaxAccel);

            part.stagingIcon = "LIQUID_ENGINE";
            part.stagingIconAlwaysShown = true;
            part.stagingOn = true;

            if (HighLogic.LoadedSceneIsFlight)
            {
                PAWActivate("agOn", "hvOn","taOn", "accel", "DriveIndividuality", "SynchronizeRate", "ParticleGeneration", "ESDisplay"); // Always on Engine, Cannot turn off.
                if(IsMove)
                    PAWActivate("MoveOn");
            }
            else
            {
                PAWActivate("accel", "DriveIndividuality", "ParticleGeneration");
            }

            vs.RotorSpeed = 180f; // thruster rotor speed
            engineOn = true; // GN Drive is always on.
            ECOn = true; // GN Drive generates EC through EC consumption calculation method.
            vs.Mode = GNVisualMode.Drive;
            //vs.ParticleColor = new Color(0f, 1f, 170f / 255f, 1f); // Original GN Drive color
            vs.ParticleColor = GNColorDecider.ParticleColor(vs);
            ps.ParticlePower = particlepower;
            ps.SafeGuard = false; // No need for safeguard for perpetual drive.
            ps.MaxG = accel;

            ps.SyncRate = 1f;

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

            // GN drive needs TD for actual work.
            if (part.Resources["GNparticle"].amount > 0 || part.Resources["TopologicalDefects"].amount >= 0.5)
            {
                engineOn = true; // GN Drive is always on.
                part.force_activate(); // Keep part activated. 
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
    }
}