using GNTechnology;
using KSP;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using UnityEngine;
using static GNTechnology.GNSynchronizer;

namespace GNTechnology
{
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

        // variables for control state
        float X = 0f;
        float Y = 0f;
        float Z = 0f;
        float throttle = 0f;

        // struct for Sync
        public OnOffList OnOff = OnOffList.Empty;

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
        [KSPField(guiActive = false, guiActiveEditor = false, guiName = "Synchronize Target Drive", isPersistant = true), UI_Toggle(disabledText = "OFF", enabledText = "ON")]
        public bool SyOn = false;

        // KSP field for indicate states
        [KSPField(guiName = "Engine Status", guiActive = false, isPersistant = true)]
        public string ES = "Deactivated";

        // KSP field for Particle/EC Generation rate (Condenser = 0)
        [KSPField(guiName = "Particle Generation", guiActive = true, guiActiveEditor = true, isPersistant = true)]
        public float ParticleGeneration = 0f;

        [KSPField(guiName = "ElectricCharge Generation", guiActive = false, isPersistant = true)]
        public double ECGeneration = 0f;

        // KSP field for drive individuality
        [KSPField(guiName = "Drive Individuality", guiActive = true, guiActiveEditor = true, isPersistant = true)]
        public float DriveIndividuality = -1f;

        // Emissive Color Changer field
        [KSPField(guiActive = true, guiName = "Tau / GN Count")]
        public string counts = "0 / 0";

        [KSPField(guiActiveEditor = true, guiActive = true, isPersistant = true, guiName = "Use Avg Color")]
        public bool useAverageColor = true;

        [KSPField(guiActiveEditor = true, guiActive = true, isPersistant = true, guiName = "Accel Divided")]
        private float AccelDiv = 1f;

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
            Debug.Log("[GN] GN_Base_System OnStart called.");
            VisualInit();
            PhysicsInit();
            StatusInit();
            EnsureIndividuality();
            DecideColor();

            Debug.Log(listT.Count);
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
            VisualUpdate();
            StatusUpdate();
            UpdateDriveAudio(engineOn);// Update audio based on engine state.
            if (engineOn && vs.Mode == GNVisualMode.Drive) part.force_activate(); // Keep part activated when engine is on in Drive mode.
            DecideColor();
            SyncUpdate();
        }

        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();
            PhysicsUpdate();
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
            ps.ParticlePower = 1f; // default power
        }

        private void StatusInit()
        {
            Fields["ES"].guiActive = true;
            if (HighLogic.LoadedSceneIsEditor)
            {
                Fields["ES"].guiActive = false;
            }
        }

        private void SyncUpdate()
        {
            // Desyncing
            if (!SyOn)
            {
                ps.SyncRate = 1f;
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

            if (OnOff == current) return;
            else OnOff = current;
            GNSynchronizer.SynchronizeOtherTargetDrive(OnOff, vessel, part.persistentId);
        }

        private void VisualUpdate()
        {
            vs.EngineState = engineOn;
            vs.InputLevel = InputLevel();
            GNVisuals.UpdateVisual(vs);
        }

        private void PhysicsUpdate()
        {
            if (!engineOn || vessel == null || vessel.packed) // only update physics when engine is on and vessel is unpacked
            {
                return;
            }

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
            if (engineOn)
            {
                ES = "Activated";
            }
            else if (ps.UnSync)
            {
                ES = "Unsynchronized";
            }
            else
            {
                ES = "Deactivated";
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

            vs.ParticleColor = c;
            //ApplyCondenserEmission(c);
        }

        private void ApplyCondenserEmission(Color c)
        {
            const string emissiveProp = "_EmissiveColor"; // プロジェクトのプロパティ名に合わせて
            foreach (var r in part.FindModelComponents<Renderer>())
            {
                if (!r) continue;
                var mpb = new MaterialPropertyBlock();
                r.GetPropertyBlock(mpb);
                mpb.SetColor(emissiveProp, c);
                r.SetPropertyBlock(mpb);
            }
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

            // parts specific values
            vs.RotorSpeed = 60f; // condenser rotor speed
        }

        private void MakeList()
        {
            // List Initialize
            listT.Clear();
            listL.Clear();
            listR.Clear();
            listE.Clear();

            // Make lists of Lights, Renderers, Emitters, etc. here if needed.
            var allT = part.transform.GetComponentsInChildren<Transform>(true);
            foreach (var t in allT)
            {
                if (t.name.Contains("rotor") && t.GetComponentInParent<Part>() == this.part)
                    listT.Add(t);
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
                if (r.name.Contains("rotor") || r.name.Contains("stator") && r.GetComponentInParent<Part>() == this.part)
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
            if (HighLogic.LoadedSceneIsEditor)
            {
                // Basic PAW
                Fields["engineOn"].guiActive = false;
                Fields["agOn"].guiActive = false;
                Fields["hvOn"].guiActive = false;
                Fields["taOn"].guiActive = false;
                Fields["accel"].guiActive = false;
                Fields["engineOn"].guiActiveEditor = false;
                Fields["agOn"].guiActiveEditor = false;
                Fields["hvOn"].guiActiveEditor = false;
                Fields["taOn"].guiActiveEditor = false;
                Fields["accel"].guiActiveEditor = false;
                Fields["ES"].guiActive = false;

                // For Tau drive,
                Fields["ECOn"].guiActive = false;
                Fields["ECOn"].guiActiveEditor = false;
            }
        }

        protected void PAWActivate(params string[] fieldNames)
        {
            foreach (var name in fieldNames)
            {
                Fields[name].guiActive = true;
                Fields[name].guiActiveEditor = true;
            }
        }

        protected float InputLevel()
        {
            X = Mathf.Abs(vessel.ctrlState.X);
            Y = Mathf.Abs(vessel.ctrlState.Y);
            Z = Mathf.Abs(vessel.ctrlState.Z);
            throttle = vessel.ctrlState.mainThrottle;
            return (X + Y + Z + throttle) * accel / 5f; // AccelDiv;Calculate percentage of accel later!
        }

        protected void SetMaxG(float min, float max)
        {
            UI_FloatRange range = (UI_FloatRange)Fields["accel"].uiControlFlight;
            range.minValue = min;
            range.maxValue = max;
            AccelDiv = range.maxValue;
        }
    }

    public class  GNThrusterSystem : GNBaseSystem // GN thrusters
    {
        [KSPField(guiName = "Max Particle Output", guiActive = true)]
        public float particlepower = 200f;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            // set accel
            SetMaxG(0f, 2f);

            part.stagingIcon = "LIQUID_ENGINE";
            part.stagingIconAlwaysShown = true;
            part.stagingOn = true;

            if (HighLogic.LoadedSceneIsFlight)
            {
                PAWActivate("engineOn", "accel");
            }
            else
            {
                PAWActivate("accel");
            }

            vs.Mode = GNVisualMode.Drive;
            vs.RotorSpeed = 180f; // thruster rotor speed
            ps.ParticlePower = particlepower;
            ps.MaxG = accel;
            Debug.Log($"[GN] vs.Mode={vs.Mode}");
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
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
            SetMaxG(0f, 4f);

            part.stagingIcon = "LIQUID_ENGINE";
            part.stagingIconAlwaysShown = true;
            part.stagingOn = true;

            if (HighLogic.LoadedSceneIsFlight)
            {
                PAWActivate("engineOn", "agOn", "hvOn", "accel");
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

        public override void OnStart(StartState state)
        {
            base.OnStart(state);

            // set accel
            SetMaxG(0f, 5f);

            part.stagingIcon = "LIQUID_ENGINE";
            part.stagingIconAlwaysShown = true;
            part.stagingOn = true;

            if (HighLogic.LoadedSceneIsFlight)
            {
                PAWActivate("engineOn", "agOn", "hvOn", "accel", "SyOn");
                PAWActivate("ECOn");
            }
            else
            {
                PAWActivate("accel", "SyOn", "ECOn");
            }

            vs.Mode = GNVisualMode.Drive;
            vs.RotorSpeed = 180f; // thruster rotor speed
            vs.ParticleColor = new Color(1f, 0f, 0.15f, 1f); // Red for condenser.
            ps.ParticlePower = particlepower;
            ps.MaxG = accel;
            Debug.Log($"[GN] vs.Mode={vs.Mode}");

            // For Twin drive
            CompressIndividuality();
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
            vs.ParticleColor = new Color(1f, 0f, 0.15f, 1f); // Red for condenser, To avoid override on GNBaseSystem Class.
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
            ps.SafeGuard = ps.ECOn;
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
            SetMaxG(0f, 5f);

            part.stagingIcon = "LIQUID_ENGINE";
            part.stagingIconAlwaysShown = true;
            part.stagingOn = true;

            if (HighLogic.LoadedSceneIsFlight)
            {
                PAWActivate("agOn", "hvOn","taOn", "accel", "SyOn"); // Always on Engine, Cannot turn off.
            }
            else
            {
                PAWActivate("accel", "SyOn");
            }

            vs.Mode = GNVisualMode.Drive;
            vs.RotorSpeed = 180f; // thruster rotor speed
            vs.ParticleColor = new Color(0f, 1f, 170f / 255f, 1f); // Original GN Drive color
            ps.ParticlePower = particlepower;
            ps.MaxG = accel;
            Debug.Log($"[GN] vs.Mode={vs.Mode}");

            part.force_activate(); // Keep part activated. 
            engineOn = true; // GN Drive is always on.
            ECOn = true; // GN Drive generates EC through EC consumption calculation method.
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
            ParticleColorSwitcher();
            ps.SafeGuard = false;
            if (part.Resources["GNparticle"].amount < 10) ps.SafeGuard = true; // just in case.
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
            part.force_activate(); // Keep part activated. 
            engineOn = true; // GN Drive is always on.
            ECOn = true;
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
                vs.ParticleColor = new Color(0f, 1f, 170f / 255f, 1f); // Original GN Drive color
            }
        }
    }
}