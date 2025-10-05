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
    // 調整用パラメータ（cfgから触れる）
    [KSPField(guiActiveEditor = true, guiName = "Min Emission", isPersistant = true)]
    public float minimumEmission = 7000f;

    [KSPField(guiActiveEditor = true, guiName = "Max Emission", isPersistant = true)]
    public float maximumEmission = 9000f;

    [KSPField(guiActiveEditor = true, guiName = "Bias", isPersistant = true)]
    public float bias = 0.01f; // 常時わずかに出したい時の下駄

    [KSPField(guiActiveEditor = true, guiName = "Throttle Weight", isPersistant = true)]
    public float throttleWeight = 1.0f;

    [KSPField(guiActiveEditor = true, guiName = "RCS Weight", isPersistant = true)]
    public float rcsWeight = 1.0f;

    [KSPField(guiActiveEditor = true, guiName = "Smoothing (1/s)", isPersistant = true)]
    public float smoothRate = 8f; // 大きいほど追従が速い

    // 参照
    private KSPParticleEmitter emitter;
    private float currentMin, currentMax; // スムージング用

    public override void OnStart(StartState state)
    {
        base.OnStart(state);

        // エミッタ取得（保険多め）
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
            enabled = false; // 以降のUpdateを止める（NRE回避）
            return;
        }

        currentMin = emitter.minEmission;
        currentMax = emitter.maxEmission;
    }

    public override void OnUpdate()
    {
        // UI/見た目更新はOnUpdateでOK
        if (!HighLogic.LoadedSceneIsFlight || vessel == null || emitter == null) return;

        // 入力の取得（ctrlStateは時々nullなので保険）
        var cs = vessel.ctrlState;
        float throttle = 0f, rcsMag = 0f;
        if (cs != null)
        {
            throttle = Mathf.Clamp01(cs.mainThrottle);

            // RCS 平行移動入力のベクトル長（0..1）で合成
            // （回転RCSも混ぜたいなら pitch/yaw/roll も含めてOK）
            Vector3 rcsVec = new Vector3(cs.X, cs.Y, cs.Z);
            rcsMag = Mathf.Clamp01(rcsVec.magnitude);
        }

        // 重みづけ＋バイアス → 0..1 にクランプ
        float drive01 = Mathf.Clamp01(throttle * throttleWeight + rcsMag * rcsWeight + bias);

        // ターゲット値
        float targetMin = minimumEmission * drive01;
        float targetMax = maximumEmission * drive01;

        // 反映（intに丸めるならMathf.RoundToInt）
        emitter.minEmission = Mathf.RoundToInt(targetMin);
        emitter.maxEmission = Mathf.RoundToInt(targetMax);
    }
}


