using GNTechnology;
using KSP;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Reflection.Emit;
using UnityEngine;

namespace GNTechnology
{
    public class GNBaseSystem : PartModule // Pure condenser, Effect control system implemented here.
    {
        // variables for visual, physics states.
        protected GNVisualState vs = GNVisualState.Empty;
        protected GNPhysicsState ps = GNPhysicsState.Empty;

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
        [KSPField(guiActive = false, guiActiveEditor = false, guiName = "Max-G", isPersistant = true), UI_FloatRange(minValue = 0f, maxValue = 5f, stepIncrement = 0.1f)]
        public float accel = 1f;

        // KSP field for indicate states
        [KSPField(guiName = "Engine Status", guiActive = false)]
        public string ES = "Deactivated";

        public override void OnAwake()
        {
            base.OnAwake();
            part.enabled = true;
        }

        public override void OnStart(StartState state)
        {
            // Check if loaded in editor
            base.OnStart(state);
            Debug.Log("[GN] GN_Base_System OnStart called.");
            VisualInit();
            PhysicsInit();
            StatusInit();
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
            VisualUpdate();
            StatusUpdate();
            UpdateDriveAudio(engineOn);// Update audio based on engine state.
            if (engineOn && vs.Mode == GNVisualMode.Drive) part.force_activate(); // Keep part activated when engine is on in Drive mode.
        }

        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();
            Debug.Log("[GN] GN_Base_System OnFixedUpdate called.");
            PhysicsUpdate();
        }

        public void FixedUpdate()
        {
            // Required to enable physics update in PartModule
            //PhysicsUpdate();
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
            GNPhysics.UpdatePhysics(ps);
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
            // Make lists of Lights, Renderers, Emitters, etc. here if needed.
            var allT = part.transform.GetComponentsInChildren<Transform>(true);
            foreach (var t in allT)
            {
                if (t.name.Contains("rotor"))
                listT.Add(t);
            }

            var allL = part.transform.GetComponentsInChildren<Light>(true);
            foreach (var l in allL) listL.Add(l);//Lights are always added.

            var allR = part.transform.GetComponentsInChildren<Renderer>(true);
            foreach (var r in allR)
            {
                if (r.name.Contains("rotor") || r.name.Contains("stator"))
                listR.Add(r);
            }

            var allE = part.transform.GetComponentsInChildren<KSPParticleEmitter>(true);
            foreach (var e in allE)
            {
                if (e.name.Contains("EMI"))
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
            }
        }

        protected void PAWActivate(params string[] fieldNames)
        {
            foreach (var name in fieldNames)
            {
                Fields[name].guiActive = true;
            }
        }

        protected float InputLevel()
        {
            X = Mathf.Abs(vessel.ctrlState.X);
            Y = Mathf.Abs(vessel.ctrlState.Y);
            Z = Mathf.Abs(vessel.ctrlState.Z);
            throttle = vessel.ctrlState.mainThrottle;
            return X + Y + Z + throttle;
        }
}

    public class  GNThrusterSystem : GNBaseSystem // GN thrusters
    {
        [KSPField(guiName = "Max Particle Output", guiActive = true)]
        public float particlepower = 200f;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
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
        }
    }

    public class GNDriveTauSystem : GNBaseSystem // GN Drive Tau
    {
        [KSPField(guiName = "Max Particle Output", guiActive = true)]
        public float particlepower = 1200f;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
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
            vs.ParticleColor = new Color(1f, 0f, 0.15f, 1f); // Red for condenser.
            ps.ParticlePower = particlepower;
            ps.MaxG = accel;
            Debug.Log($"[GN] vs.Mode={vs.Mode}");
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
        }
    }

    public class GNDriveSystem : GNBaseSystem // GN Drive (Original)
    {
        [KSPField(guiName = "Max Particle Output", guiActive = true)]
        public float particlepower = 1000f;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            part.stagingIcon = "LIQUID_ENGINE";
            part.stagingIconAlwaysShown = true;
            part.stagingOn = true;

            if (HighLogic.LoadedSceneIsFlight)
            {
                PAWActivate("engineOn", "agOn", "hvOn","taOn", "accel");
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
            ParticleColorSwitcher();
        }

        private void ParticleColorSwitcher()
        {
            if (taOn)
            {
                vs.ParticleColor = new Color(1F, 0F, 100F / 255F, 1F);// For Trans-AM drive color
                ps.ParticlePower = particlepower * 3f; // Increase particle power in TA mode
                ps.MaxG = accel * 3f; // Increase MaxG in TA mode
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