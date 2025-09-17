using System;
using System.Collections.Generic;
using UnityEngine;

[Flags]
public enum GNCapability
{
    None         = 0,
    Thrust       = 1 << 0,  // 推力(加速)を与える
    AntiGravity  = 1 << 1,  // 重力相殺
    HoverAssist  = 1 << 2,  // 垂直速度制御（ホバー）
    Converter    = 1 << 3,  // EC↔GN 変換
    InertiaCtrl  = 1 << 4,  // 慣性制御
    TransAM      = 1 << 5,  // 強化モード
    Particles    = 1 << 6,  // 粒子エフェクト
    Audio        = 1 << 7,  // サウンド
}

public abstract class BaseGNCore : PartModule
{
    // ===== 共通パラメータ =====
    [KSPField(guiActiveEditor = true)] public float fuelefficiency = 1f;

    [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Max Overload", isPersistant = true),
     UI_FloatRange(minValue = 0f, maxValue = 5f, stepIncrement = 0.1f)]
    public float Overload = 1f;

    // 粒子（cfgで調整可）
    [KSPField(guiActiveEditor = true)] public float idleEmission = 5f;     // スロ0でも常時これだけ出す
    [KSPField(guiActiveEditor = true)] public float emissionMult = 30f;     // スロットル倍率
    [KSPField(guiActiveEditor = true)] public float emissionClamp = 500f;   // 上限
    [KSPField] public string emitterTransformNames = "EMI";                 // "EMI,EMI2" など

    // 反同期チェック（GNdriveで使っていた多機数制限、不要なら -1）
    [KSPField(guiActiveEditor = true)] public int maxEngineCount = -1;

    // サウンド
    [KSPField] public string audioPath = "";

    // ===== 状態 =====
    [KSPField(isPersistant = true, guiActive = true, guiName = "Engine Status")]
    public string ES = "Deactivated";

    [KSPField(isPersistant = true)] public bool engineIgnited = false;
    [KSPField(isPersistant = true)] public bool agActivated = false;
    [KSPField(isPersistant = true)] public bool hvActivated = false;
    [KSPField(isPersistant = true)] public bool taActivated = false;   // Trans-AM
    [KSPField(isPersistant = true)] public bool depleted = false;

    protected Vector4 color = Vector4.zero;

    // 粒子/音
    protected readonly List<KSPParticleEmitter> emitters = new List<KSPParticleEmitter>();
    protected AudioSource audioSource;

    // ホバー用PID
    protected PidController brakePid = new PidController(10F, 0.005F, 0.002F, 50, 5);

    // 視覚（回転/ノード）
    protected GameObject rotor, stator;
    protected float rotation;

    // 消費追跡（GNcapが発電に使う）
    protected double lastGNConsumed = 0;

    // 派生が宣言
    protected abstract GNCapability Capabilities { get; }

    // ===== ライフサイクル =====
    public override void OnStart(StartState state)
    {
        base.OnStart(state);
        part.stagingIcon = "LIQUID_ENGINE";
        CacheEmitters();
        if (Has(GNCapability.Audio)) SetupAudio();
        foreach (var e in emitters) { if (!e) continue; e.emit = false; e.minEmission = 0; e.maxEmission = 0; }
        TryInitVisuals();
    }

    public override void OnFixedUpdate()
    {
        if (!HighLogic.LoadedSceneIsFlight || vessel == null) return;

        float t01 = GetThrottle01();
        RefreshUI();

        if (Has(GNCapability.Particles)) UpdateEmitters(t01);
        if (Has(GNCapability.Audio))     UpdateAudio(t01);

        // 多機数チェック（必要なときのみ）
        if (maxEngineCount > 0)
        {
            int count = CountActiveEngines();
            if (count > maxEngineCount)
            {
                ES = "Unsynchronized";
                ApplyForces(Vector3.zero, Vector3.zero);
                return;
            }
        }

        Vector3 controlForce = Vector3.zero;
        Vector3 gee = Vector3.zero;

        if (engineIgnited)
        {
            if (Has(GNCapability.Thrust))
                controlForce += ComputeThrustVector(t01);

            if (Has(GNCapability.AntiGravity))
                gee = ComputeAntiGravityVector();

            if (Has(GNCapability.HoverAssist))
                controlForce -= ComputeHoverAssist(gee); // 既存ロジックは引き算

            if (Has(GNCapability.InertiaCtrl))
                controlForce += ComputeInertiaControl();

            if (Has(GNCapability.TransAM))
                ApplyTransAM(ref controlForce);
        }

        HandleResources(t01, controlForce, gee);
        ApplyForces(controlForce, gee);
        UpdateVisuals();
    }

    // ===== 共通ヘルパ =====
    protected virtual float GetThrottle01()
        => vessel?.ctrlState != null ? Mathf.Clamp01(vessel.ctrlState.mainThrottle * Overload) : 0f;

    protected bool Has(GNCapability c) => (Capabilities & c) != 0;

    protected int CountActiveEngines()
    {
        int c = 0;
        foreach (var p in vessel.Parts)
            foreach (PartModule m in p.Modules)
                if (m is BaseGNCore core && core.engineIgnited) c++;
        return c;
    }

    protected int CountActiveAG()
    {
        int c = 0;
        foreach (var p in vessel.Parts)
            foreach (PartModule m in p.Modules)
                if (m is BaseGNCore core && core.agActivated) c++;
        return Mathf.Max(1, c);
    }

    protected void CacheEmitters()
    {
        emitters.Clear();
        if (!string.IsNullOrEmpty(emitterTransformNames))
        {
            foreach (var raw in emitterTransformNames.Split(','))
            {
                var name = raw.Trim();
                if (string.IsNullOrEmpty(name)) continue;
                var t = part.FindModelTransform(name);
                var e = t ? t.GetComponent<KSPParticleEmitter>() : null;
                if (e) emitters.Add(e);
            }
        }
        if (emitters.Count == 0)
        {
            var all = part.FindModelComponents<KSPParticleEmitter>();
            if (all != null) emitters.AddRange(all);
        }
    }

    protected void UpdateEmitters(float t01)
    {
        if (emitters.Count == 0) return;
        if (engineIgnited)
        {
            float amount = Mathf.Min(idleEmission + t01 * emissionMult, emissionClamp);
            foreach (var e in emitters)
            {
                if (!e) continue;
                e.emit = true;
                e.minEmission = amount * 0.7f;
                e.maxEmission = amount;
            }
        }
        else
        {
            foreach (var e in emitters)
            {
                if (!e) continue;
                e.emit = false; e.minEmission = 0; e.maxEmission = 0;
            }
        }
    }

    protected virtual void SetupAudio()
    {
        if (string.IsNullOrEmpty(audioPath)) return;
        var clip = GameDatabase.Instance.GetAudioClip(audioPath);
        if (!clip) return;
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.clip = clip;
        audioSource.loop = true;
        audioSource.dopplerLevel = 0;
        audioSource.minDistance = 0.1f;
        audioSource.maxDistance = 150;
        audioSource.spatialBlend = 1;
        audioSource.volume = 0;
        audioSource.pitch  = 0.8f;
        audioSource.priority = 128;
    }

    protected void UpdateAudio(float t01)
    {
        if (!audioSource) return;
        if (!engineIgnited || PauseMenu.isOpen || Time.timeScale == 0)
        {
            if (audioSource.isPlaying) audioSource.Stop();
            audioSource.volume = 0;
            return;
        }
        if (!audioSource.isPlaying) audioSource.Play();
        audioSource.volume = Mathf.Clamp01(0.2f + t01 * 0.8f);
        audioSource.pitch  = 0.8f + t01 * 0.4f;
    }

    protected virtual void RefreshUI()
    {
        ES = engineIgnited ? (taActivated ? "Trans-AM" : "Activated") : "Deactivated";
        SetEventVisible("Deactivate",   engineIgnited);
        SetEventVisible("Activate",     !engineIgnited);
        SetEventVisible("Deactivateag", agActivated);
        SetEventVisible("Activateag",   !agActivated);
        SetEventVisible("Deactivatehv", hvActivated && agActivated);
        SetEventVisible("Activatehv",   !hvActivated && agActivated);
        SetEventVisible("Activateta",   engineIgnited && !taActivated);
    }
    void SetEventVisible(string name, bool v)
    {
        if (Events != null && Events.Contains(name)) Events[name].guiActive = v;
    }

    // ===== 物理系フック =====
    protected virtual Vector3 ComputeThrustVector(float t01)
    {
        var cs = vessel?.ctrlState;
        if (cs == null) return Vector3.zero;

        float throttle = Mathf.Clamp01(cs.mainThrottle) * Overload;
        float y = -cs.Y * Overload * 10f;
        float x = -cs.X * Overload * 10f;
        float z = throttle * 10f - cs.Z * Overload * 10f;

        return vessel.ReferenceTransform.up * z
             + vessel.ReferenceTransform.forward * y
             + vessel.ReferenceTransform.right * x;
    }

    protected virtual Vector3 ComputeAntiGravityVector()
    {
        float engines = CountActiveAG();
        return FlightGlobals.getGeeForceAtPosition(vessel.transform.position) / engines;
    }

    protected virtual Vector3 ComputeHoverAssist(Vector3 gee)
    {
        if (!hvActivated || gee == Vector3.zero) return Vector3.zero;
        float vVel = Vector3.Dot(gee.normalized, part.rb.velocity);
        brakePid.Calibrateclamp(Overload);
        return brakePid.Control(vVel) * gee.normalized * 10f / CountActiveAG();
    }

    protected virtual Vector3 ComputeInertiaControl()
    {
        // 必要なら GNdrive から詳細移植
        return Vector3.zero;
    }

    protected virtual void ApplyTransAM(ref Vector3 controlForce)
    {
        controlForce *= 5f;
        color = new Vector4(1f, 0f, 100f / 255f, 1f);
    }

    protected virtual void HandleResources(float t01, Vector3 controlForce, Vector3 gee)
    {
        // 既定：推力/AGに応じGN消費
        float mag = Mathf.Abs((-gee + controlForce).magnitude);
        double consumption = vessel.GetTotalMass() * mag * fuelefficiency * TimeWarp.fixedDeltaTime;

        lastGNConsumed = 0;
        if (consumption > 0)
            lastGNConsumed = part.RequestResource("GNparticle", consumption);

        if (Has(GNCapability.Converter))
            HandleConverter(t01);

        // 枯渇検出
        if (consumption > 0 && Math.Round(lastGNConsumed, 5) < Math.Round(consumption, 5))
        {
            depleted = true;
            engineIgnited = false;
            agActivated = false;
            taActivated = false;
        }
    }

    protected virtual void HandleConverter(float t01) { /* 派生で実装 */ }

    protected virtual void ApplyForces(Vector3 controlForce, Vector3 gee)
    {
        if (!engineIgnited) return;

        if (controlForce != Vector3.zero)
            foreach (var p in vessel.parts)
                if ((p.physicalSignificance == Part.PhysicalSignificance.FULL) && (p.rb != null))
                    p.AddForce(controlForce * p.rb.mass);

        if (agActivated && gee != Vector3.zero)
            foreach (var p in vessel.parts)
                if ((p.physicalSignificance == Part.PhysicalSignificance.FULL) && (p.rb != null))
                    p.AddForce(-gee * p.rb.mass);
    }

    protected virtual void TryInitVisuals()
    {
        var r = part.FindModelTransform("rotor");
        var s = part.FindModelTransform("stator");
        rotor  = r ? r.gameObject : null;
        stator = s ? s.gameObject : null;
    }

    protected virtual void UpdateVisuals()
    {
        if (!rotor || !stator) return;
        // 色
        var col = new Color(color.x, color.y, color.z, color.w);
        var rmr = rotor.GetComponent<Renderer>();
        var smr = stator.GetComponent<Renderer>();
        var slt = stator.GetComponent<Light>();
        if (rmr) rmr.material.SetColor("_EmissiveColor", col);
        if (smr) smr.material.SetColor("_EmissiveColor", col);
        if (slt) slt.color = col;

        // 回転（適当に共通回転、派生でoverride可）
        rotation += 6f * (1f + TimeWarp.deltaTime * 120f);
        while (rotation > 360f) rotation -= 360f;
        rotor.transform.localEulerAngles = new Vector3(0, 0, rotation);
    }

    // ===== 共通イベント/アクション =====
    [KSPAction("Toggle", KSPActionGroup.None, guiName = "Toggle Engine")]
    public void ActionToggle(KSPActionParam _) => (engineIgnited ? Deactivate() : Activate());

    [KSPEvent(name = "Activate", guiName = "Activate Engine", active = true, guiActive = true)]
    public void Activate() { part.force_activate(); engineIgnited = true; }

    [KSPEvent(name = "Deactivate", guiName = "Deactivate Engine", active = true, guiActive = false)]
    public void Deactivate() { engineIgnited = false; }

    [KSPAction("Toggle AG", KSPActionGroup.None, guiName = "Toggle Antigravity")]
    public void ActionToggleAG(KSPActionParam _) => (agActivated ? Deactivateag() : Activateag());

    [KSPEvent(name = "Activateag", guiName = "Activate Antigravity", active = true, guiActive = true)]
    public void Activateag() { part.force_activate(); agActivated = true; }

    [KSPEvent(name = "Deactivateag", guiName = "Deactivate Antigravity", active = true, guiActive = false)]
    public void Deactivateag() { agActivated = false; hvActivated = false; }

    [KSPEvent(name = "Activatehv", guiName = "Activate Hover", active = true, guiActive = false)]
    public void Activatehv() { part.force_activate(); hvActivated = true; }

    [KSPEvent(name = "Deactivatehv", guiName = "Deactivate Hover", active = true, guiActive = false)]
    public void Deactivatehv() { hvActivated = false; }

    [KSPEvent(name = "Activateta", guiName = "Trans-AM", active = true, guiActive = false)]
    public void Activateta() { taActivated = true; color = new Vector4(1f, 0f, 100f/255f, 1f); }
}
