using Smooth.Algebraics;
using System;
using UnityEngine;

public class GNDriveCore : PartModule
{
    //Function toggles
    [KSPField(isPersistant = false)]//cfg file determines default value.
    public bool GN_thruster = false;
    [KSPField(isPersistant = false)]
    public bool GN_brake = false;
    [KSPField(isPersistant = false)]
    public bool AntiGravity = false;
    [KSPField(isPersistant = false)]
    public bool Hovering = false;
    [KSPField(isPersistant = false)]
    public bool ParticleGeneration = false;
    [KSPField(isPersistant = false)]
    public bool Trans_AM = false;

    //numerical drive specifications.
    [KSPField(isPersistant = false)]
    public float fuel_efficiency = 1f;//condenser drives should be less then 1f.
    [KSPField(isPersistant = false)]
    public float max_acceleration = 10f;//condenser drives are inferior, so this should be less then 10f.
    [KSPField(isPersistant = false)]
    public float max_particle_emission_amount = 2000f;//maximum particle emission rate. If a ship weight too much, max acceleration defined by "max_acceleration" field will not be reached.

    //drive state variables.
    public bool engineIgnited = false;
    public bool flameOut = false;
    public bool agActivated = false;
    public bool depleted = false;
    public bool ecActivated = false;

    //interactive fields.
    [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Max Overload", isPersistant = true), UI_FloatRange(minValue = 0f, maxValue = 5f, stepIncrement = 0.1f)]
    public float Overload = 1f;

    //interactive switches.
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
        emitter.minEmission = emitter.minEmission + Mathf.RoundToInt(smoothRate * (targetMin - currentMin)/fr);
        emitter.maxEmission = emitter.maxEmission + Mathf.RoundToInt(smoothRate * (targetMax - currentMax) /fr);
    }
}


