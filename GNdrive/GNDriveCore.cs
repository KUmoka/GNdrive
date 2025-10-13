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
    public class GNCommonUnit : PartModule//Base class for GN units.Update-related things should be done here.
    {
        // Visual and Physics states.
        protected GNVisualState s = GNVisualState.Empty;
        protected GNPhysicalState ps = GNPhysicalState.Empty;

        //variables for audio.
        [KSPField] public string audioPath = "GNdrive/Audio/GNDriveTypical";
        AudioClip soundClip;
        AudioSource audioSource;

        public override void OnInitialize()
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

            //setup necessary basic TransForms, Renderers, etc here.
            //Visual state setup.
            s.part = part;
            s.Rotors = listT.ToArray();
            s.EmissiveRenderers = listR.ToArray();
            s.GlowLights = listL.ToArray();
            s.ParticleEmitters = listE.ToArray();
            s.EngineState = false;
            s.InputLevel = 0f;

            //Physical state setup.
            ps.part = part;

            // Unit specific setup, override in derived classes.
            s.Mode = GNVisualMode.Condenser; // Default mode, can be changed in derived classes.
            s.ParticleColor = new Color(1f, 0f, 0.15f, 1f);// Default color, can be changed in derived classes.
        }

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

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            // Common initialization code for GN units can be added here.
            // make GNVisuals and GNPhysics ready for future use.
            // Also setup AudioSource if needed.
            // In Editor, always should be on so enabled = true
            enabled = true;
            GNVisuals.UpdateVisual(s);//initialize. Is it necessary?
            SetupAudio();

            //When loaded in editor, turn off all visual effects.
            if (HighLogic.LoadedSceneIsEditor)
            {
                GNVisuals.SetOff(s);
                Debug.Log("[GN] Visuals set to OFF in editor.");
                return;
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

        public override void OnUpdate()
        {
            base.OnUpdate();
            // GNVisuals update.
            
            //Audio for drive units.
            if (audioSource != null)
            {
                // Play sound only when it's not condense, the engine is active and not paused.
                bool shouldPlay = !(PauseMenu.isOpen || Time.timeScale == 0 || s.Mode == GNVisualMode.Condenser || !s.EngineState);

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

            GNVisuals.UpdateVisual(s);
        }

        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();
            // GNPhysics update.

        }
    }

    public class GNThrusterUnit : GNCommonUnit
    {

    }

    public class GNCondenserDriveUnit : GNCommonUnit
    {
        
    }

    public class GNDriveTauUnit : GNCommonUnit
    {
        //Just for testing purpose.
        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Engine ON", isPersistant = true), UI_Toggle(disabledText = "OFF", enabledText = "ON")]
        public bool engineOn = false;

        public override void OnInitialize()
        {
            base.OnInitialize();
            s.Mode = GNVisualMode.Drive;
            s.ParticleColor = new Color(1f, 0f, 0.16f, 1f);//Red for Tau drive.
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
        //Just for testing purpose.
        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Engine ON", isPersistant = true),UI_Toggle(disabledText = "OFF", enabledText = "ON")]
        public bool engineOn = false;
        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "TRANS-AM ON", isPersistant = true), UI_Toggle(disabledText = "OFF", enabledText = "ON")]
        public bool transamOn = false;
        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "UnSynchronized", isPersistant = true), UI_Toggle(disabledText = "OFF", enabledText = "ON")]
        public bool unSync = false;

        public override void OnInitialize()
        { 
            base.OnInitialize();
            s.Mode = GNVisualMode.Drive;
            s.ParticleColor = new Color(0f, 1f, 0.6f, 1f);//green for normal drive.
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
            if (transamOn)
            {
                s.ParticleColor = new Color(1F, 0F, 100F / 255F, 1F);//Red for Tau drive.
            }
            else
            {
                s.ParticleColor = new Color(0f, 1f, 0.6f, 1f);//green for normal drive.
            }

            if (unSync)
            {
                s.ParticleColor = new Color(0F, 1F / 4F, 42F / 255F, 1F);//Unsynchronized Color.
                s.InputLevel = 0f; //unsynchronized, no input.

            }
            else
            {
                s.InputLevel = vessel.ctrlState.X + vessel.ctrlState.Y + vessel.ctrlState.Z + vessel.ctrlState.mainThrottle;//for now, just sum them up.This shouldn't affects GN condenser.

            }

            s.EngineState = engineOn;
        }
    }
}

public class ParticleEmissionControl : PartModule
{
    // Adjustable parameters.
    [KSPField(guiActiveEditor = true, guiName = "Min Emission", isPersistant = true)]
    public float minimumEmission = 7000f;

    [KSPField(guiActiveEditor = true, guiName = "Max Emission", isPersistant = true)]
    public float maximumEmission = 9000f;

    [KSPField(guiActiveEditor = true, guiName = "Bias", isPersistant = true)]
    public float bias = 0.01f; //Base emission rate when no input.

    [KSPField(guiActiveEditor = true, guiName = "Throttle Weight", isPersistant = true)]
    public float throttleWeight = 1.0f;

    [KSPField(guiActiveEditor = true, guiName = "RCS Weight", isPersistant = true)]
    public float rcsWeight = 1.0f;

    [KSPField(guiActiveEditor = true, guiName = "Smoothing (1/s)", isPersistant = true)]
    public float smoothRate = 8f; //Larger value means faster response, For future use.

    //Treansform reference.
    private KSPParticleEmitter emitter;
    private float currentMin, currentMax; //For future use, for smoothing.

    //private floats
    private float fr = 600f;

    public override void OnStart(StartState state)
    {
        base.OnStart(state);

        // get emitter reference, assume the transform name is "EMI".
        var tf = part.FindModelTransform("EMI");
        if (tf != null)
        {
            emitter = tf.GetComponent<KSPParticleEmitter>();
            if (emitter == null)
                emitter = tf.GetComponentInChildren<KSPParticleEmitter>(true);
        }
        if (emitter == null)
        {
            Debug.LogWarning("[GN] ParticleEmissionControl: Emitter not found (EMI).");
            enabled = false; //When error, disable this module.
            return;
        }

        currentMin = emitter.minEmission;
        currentMax = emitter.maxEmission;
    }

    public override void OnUpdate()
    {
        // For visual, OnUpdate is enough.
        if (!HighLogic.LoadedSceneIsFlight || vessel == null || emitter == null) return;

        // get input state(sometimes, ctrlState is null).
        var cs = vessel.ctrlState;
        float throttle = 0f, rcsMag = 0f;
        if (cs != null)
        {
            throttle = Mathf.Clamp01(cs.mainThrottle);

            // RCS vector magnitude
            // RCS input is in the range of -1..1 for each axis, so.
            // （if needs, pitch/yaw/roll should be added）
            Vector3 rcsVec = new Vector3(cs.X, cs.Y, cs.Z);
            rcsMag = Mathf.Clamp01(rcsVec.magnitude);
        }

        // get current emitter rate.
        currentMin = emitter.minEmission;
        currentMax = emitter.maxEmission;

        // weight and bias for clamp inputs.
        float drive01 = Mathf.Clamp01(throttle * throttleWeight + rcsMag * rcsWeight + bias);

        // target emission rate.
        float targetMin = minimumEmission * drive01;
        float targetMax = maximumEmission * drive01;

        // apply directly for now.
        emitter.minEmission = emitter.minEmission + Mathf.RoundToInt(smoothRate * (targetMin - currentMin) / fr);
        emitter.maxEmission = emitter.maxEmission + Mathf.RoundToInt(smoothRate * (targetMax - currentMax) / fr);
    }
}

//cache visual parts will be added here.
//handy method for get rotor transforms.
//Debug.Log("[GN] GNCondenserUnit OnStart Run");
//var all = part.transform.GetComponentsInChildren<Transform>(true);
//foreach (var t in all)
//{
//    if (t.name.Contains("rotor")) Debug.Log("[GN] T:" + t.name);
//}

