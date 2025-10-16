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
    public class GNCommonUnit : PartModule // Base class for GN units.Update-related things should be done here.
    {
        // Visual and Physics states.
        protected GNVisualState s = GNVisualState.Empty;
        protected GNPhysicalState ps = GNPhysicalState.Empty;

        // KSPFieldでチューニング可能に（cfgから上書き）
        [KSPField(guiActiveEditor = true, guiName = "Overload", isPersistant = true)]
        public float Overload = 1f;

        [KSPField(guiActiveEditor = true, guiName = "FuelEff", isPersistant = true)]
        public float FuelEff = 1f;

        [KSPField(guiActiveEditor = true, guiName = "ParticleRate", isPersistant = true)]
        public float ParticleRate = 0f;

        [KSPField(guiActiveEditor = true, guiName = "Max Sync Engines", isPersistant = true)]
        public int MaxSyncEngines = 0;

        // KSP field for drive control.
        [KSPField(guiActive = true, guiName = "Engine State", isPersistant = true), UI_Toggle(disabledText = "OFF", enabledText = "ON")]
        public bool engineOn = false;

        //variables for audio.
        [KSPField] public string audioPath = "GNdrive/Audio/GNDriveTypical";
        AudioClip soundClip;
        AudioSource audioSource;

        public override void OnStart(StartState state)
        {
            // initialize states.
            base.OnInitialize();
            GNVisuals.SetOff(s);//initialize
            GNPhysics.SetOff(ps);//initialize

            // Transforms, Light, Renderers, Emitters, etc.
            List<Transform> listT = new List<Transform>();
            List<Light> listL = new List<Light>();
            List<Renderer> listR = new List<Renderer>();
            List<KSPParticleEmitter> listE = new List<KSPParticleEmitter>();
            MakeLists(listT, listL, listR, listE);//Make lists of parts here.

            //Visual state setup.setup necessary basic TransForms, Renderers, etc here.
            s.part = part;
            s.Rotors = listT.ToArray();
            s.EmissiveRenderers = listR.ToArray();
            s.GlowLights = listL.ToArray();
            s.ParticleEmitters = listE.ToArray();
            s.EngineState = false;
            s.InputLevel = 0f;

            //Physics state setup.
            ps.part = part;
            ps.fuelEfficiency = FuelEff;
            ps.maxG = 0f; //to be set in derived classes.
            ps.flags = GNFlags.None;
            ps.phaseShift = 0f;//to be set in derived classes.
            ps.particleOutputRate = ParticleRate;

            // Unit specific setup, override in derived classes.派生クラス固有の初期化（後述の virtual に逃がす）
            ConfigureUnit(); // ★これを追加

            // Final initialization.
            enabled = true;
            GNVisuals.UpdateVisual(s);//initialize. Is it necessary?
            GNPhysics.UpdatePhysics(ref ps);//initialize. Is it necessary?
            SetupAudio();

            //When loaded in editor, turn off all visual effects.
            if (HighLogic.LoadedSceneIsEditor)
            {
                GNVisuals.SetOff(s);
                GNPhysics.SetOff(ps);
                Debug.Log("[GN] Visuals set to OFF in editor.");
                return;
            }
        }

        public override void OnUpdate()
        {
            // When loaded in editor, turn off all visual effects.
            if (HighLogic.LoadedSceneIsEditor)
            {
                GNVisuals.SetOff(s);
                return;
            }

            // GNVisuals and audio update.
            base.OnUpdate();
            s.EngineState = engineOn;
            UpdateDriveAudio();
            GNVisuals.UpdateVisual(s);
        }

        public override void OnFixedUpdate()
        {
            // When loaded in editor, turn off all visual effects.
            if (HighLogic.LoadedSceneIsEditor)
            {
                GNPhysics.SetOff(ps);
                return;
            }

            // GNPhysics update.
            base.OnFixedUpdate();
            ps.flags = engineOn ? (ps.flags | GNFlags.Ignited) : (ps.flags & ~GNFlags.Ignited); // 条件式 ? trueのとき: falseのとき. memo : |=, &=, ~ are bitwise operators.
            GNPhysics.UpdatePhysics(ref ps);
        }

        // Unit specific configuration, override in derived classes.
        protected virtual void ConfigureUnit()
        {
            // 既定値（安全側）
            s.Mode = GNVisualMode.Condenser;
            s.ParticleColor = new Color(1f, 0f, 0.15f, 1f);
            ps.maxG = 0f;
            ps.phaseShift = 0f;
        }

        // Helper method to make lists of Transforms, Lights, Renderers, Emitters, etc.
        private void MakeLists(List<Transform> listT, List<Light> listL, List<Renderer> listR, List<KSPParticleEmitter> listE)
        {
            // Make lists of Lights, Renderers, Emitters, etc. here if needed.
            var all = part.transform.GetComponentsInChildren<Transform>(true);
            foreach (var t in all)
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
                    listE.Add(e);
            }

            Debug.Log($"[GN] Found {listT.Count} Transforms, {listL.Count} Lights, {listR.Count} Renderers, {listE.Count} Emitters in {part.name}");

        }

        // Audio setup method.
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

        // Audio update method.
        private void UpdateDriveAudio()
        {
            if (audioSource == null) return;

            bool paused = PauseMenu.isOpen || Time.timeScale == 0;
            bool isCondenserMode = s.Mode == GNVisualMode.Condenser;
            bool engineOff = !s.EngineState;

            bool shouldPlay = !(paused || isCondenserMode || engineOff);

            if (shouldPlay)
            {
                if (!audioSource.isPlaying) audioSource.Play();
                audioSource.volume = 1.0f;
                audioSource.pitch = 1.0f;
            }
            else
            {
                if (audioSource.isPlaying) audioSource.Stop();
                audioSource.volume = 0f;
            }
        }
    }

    public class GNThrusterUnit : GNCommonUnit
    {
        // This unit has partial drive functions.
        [KSPField]
        public int drivePower = 200;

        // Override ConfigureUnit to set specific parameters for GNThrusterUnit
        protected override void ConfigureUnit()
        {
            base.ConfigureUnit();
            s.Mode = GNVisualMode.Drive;
            s.ParticleColor = new Color(0f, 1f, 170f / 255f, 1f); // 旧GNdrive相当
            ps.maxG = 10f;  // 仮
            ps.phaseShift = 0.0f; // 仮
        }
    }

    public class GNCondenserDriveUnit : GNCommonUnit
    {
        // This unit has drive functions.
        [KSPField]
        public int drivePower = 400;

        // Anti-gravity toggle.
        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "AntiGravity", isPersistant = true), UI_Toggle(disabledText = "OFF", enabledText = "ON")]
        public bool antiGravityOn = false;

        // Override ConfigureUnit to set specific parameters for GNThrusterUnit
        protected override void ConfigureUnit()
        {
            base.ConfigureUnit();
            s.Mode = GNVisualMode.Drive;
            s.ParticleColor = new Color(0f, 1f, 170f / 255f, 1f); // 旧GNdrive相当
            ps.maxG = 10f;  // 仮
            ps.phaseShift = 0.0f; // 仮
            ps.particleOutputRate = drivePower;
        }
    }

    public class GNDriveTauUnit : GNCommonUnit
    {
        // This unit has drive functions.
        [KSPField]
        public int drivePower = 1500;

        // Anti-gravity toggle.
        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "AntiGravity", isPersistant = true), UI_Toggle(disabledText = "OFF", enabledText = "ON")]
        public bool antiGravityOn = false;

        // Override ConfigureUnit to set specific parameters for GNThrusterUnit
        protected override void ConfigureUnit()
        {
            base.ConfigureUnit();
            s.Mode = GNVisualMode.Drive;
            s.ParticleColor = new Color(1f, 0f, 40f / 255f, 1f); // 旧GNdrive相当
            ps.maxG = 10f;  // 仮
            ps.phaseShift = 0.0f; // 仮
            ps.particleOutputRate = drivePower;
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            s.Mode = GNVisualMode.Drive;
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
            s.EngineState = engineOn;
            s.InputLevel = vessel.ctrlState.X + vessel.ctrlState.Y + vessel.ctrlState.Z + vessel.ctrlState.mainThrottle;//for now, just sum them up.This shouldn't affects GN condenser.

        }
    }

    public class GNDriveUnit : GNCommonUnit
    {
        // This unit has drive functions.
        [KSPField]
        public int drivePower = 1000;

        // TRANS-AM mode toggle.
        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "TRANS-AM", isPersistant = true), UI_Toggle(disabledText = "OFF", enabledText = "ON")]
        public bool transamOn = false;

        // Anti-gravity toggle.
        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "AntiGravity", isPersistant = true), UI_Toggle(disabledText = "OFF", enabledText = "ON")]
        public bool antiGravityOn = false;

        protected override void ConfigureUnit()
        {
            base.ConfigureUnit();
            s.Mode = GNVisualMode.Drive;
            s.ParticleColor = new Color(0f, 1f, 170f / 255f, 1f); // 旧GNdrive相当
            ps.maxG = 10f;  // 仮
            ps.phaseShift = 0.0f; // 仮
            ps.particleOutputRate = drivePower;
        }

        public override void OnStart(StartState state)
        { 
            base.OnStart(state);
            s.Mode = GNVisualMode.Drive;
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
            if (transamOn)
            {
                s.ParticleColor = new Color(1F, 0F, 100F / 255F, 1F);//Red for Tau drive.
                drivePower = 1000 * 3; //Triple 
            }
            else
            {
                s.ParticleColor = new Color(0f, 1f, 0.6f, 1f);//green for normal drive.
                drivePower = 1000;
            }

            if (false)// This code will use when Unsynchronization feature is implemented.so ignore error here.
            {
                s.ParticleColor = new Color(0F, 1F / 4F, 42F / 255F, 1F);//Unsynchronized Color.
                drivePower = 1;

            }
            else
            {
                s.InputLevel = vessel.ctrlState.X + vessel.ctrlState.Y + vessel.ctrlState.Z + vessel.ctrlState.mainThrottle;//for now, just sum them up.This shouldn't affects GN condenser.

            }

            s.EngineState = engineOn;
        }
    }
}