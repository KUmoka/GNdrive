using System;
using System.Runtime.InteropServices;
using System.Linq;
using UnityEngine;

public class Taudrive : PartModule
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
    [KSPField] public string audioPath = "GNdrive/Audio/GNDriveTypical";
    AudioClip soundClip;
    AudioSource audioSource;
    Transform EMITransform;
    KSPParticleEmitter Emitter;

    //    public bool staged = false;
    public float particleSize = 0.001f;
    private float rotation = 0F;
    private GameObject rotor;
    private GameObject stator;

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
            //if (base.part.FindModelTransform("rotor").gameObject != null)
            //{
                //stator = base.part.FindModelTransform("stator").gameObject;
                //rotor = base.part.FindModelTransform("rotor").gameObject;
            //}

            //EMITransform = base.part.FindModelTransform("EMI");
            //Emitter = EMITransform.gameObject.GetComponent<KSPParticleEmitter>();
            //Emitter.emit = false;
        }

        //SetupAudio();

        //var rr = rotor ? rotor.GetComponent<Renderer>() : null;
        //if (rr && rr.material && rr.material.HasProperty("_EmissiveColor"))
        //rr.material.SetColor("_EmissiveColor", Color.black);
        //rr.material.SetColor("_Color", Color.gray);

        //var sr = stator ? stator.GetComponent<Renderer>() : null;
        //if (sr && sr.material && sr.material.HasProperty("_EmissiveColor"))
        //sr.material.SetColor("_EmissiveColor", Color.black);
        //sr.material.SetColor("_Color", Color.gray);
    }

    //public void SetupAudio()
    //{
    //    try
    //    {
    //        if (string.IsNullOrEmpty(audioPath))
    //        {
    //            Debug.LogWarning("[GN] audioPath is empty");
    //            return;
    //        }
    //
    //        soundClip = GameDatabase.Instance.GetAudioClip(audioPath);
    //        if (soundClip == null)
    //        {
    //            Debug.LogError("[GN] AudioClip not found: " + audioPath);
    //            return;
    //        }
    //        Debug.Log("[GN] Sound loaded: " + soundClip.name);

            // AudioSource should be attached to the part if possible, otherwise to the part's root object
    //        var host = part != null ? part.gameObject : this.gameObject;
    //        audioSource = host.GetComponent<AudioSource>() ?? host.AddComponent<AudioSource>();
    //        if (audioSource == null)
    //        {
    //            Debug.LogError("[GN] AudioSource add/get failed");
    //            return;
    //        }

    //        audioSource.clip = soundClip;
    //        audioSource.loop = true;
    //        audioSource.playOnAwake = false;
    //        audioSource.dopplerLevel = 0f;
    //        audioSource.spatialBlend = 1f; // 3D
    //        audioSource.minDistance = 5f;
    //        audioSource.maxDistance = 150f;
    //        audioSource.priority = 128;    // 0 to 256
    //        audioSource.volume = 0f;       // 0..1
    //        audioSource.pitch = 1f;       // uusually 1f
    //    }
    //    catch (Exception e)
    //    {
    //        Debug.LogError("[GN] SetupAudio exception: " + e);
    //        audioSource = null; //avoid repeated error
    //    }
    //}

    public void Update()
    {
        if (HighLogic.LoadedSceneIsEditor)
        {
            //if (Emitter) Emitter.emit = false;

            //var rr = rotor ? rotor.GetComponent<Renderer>() : null;
            //if (rr && rr.material && rr.material.HasProperty("_EmissiveColor"))
            //rr.material.SetColor("_EmissiveColor", Color.black);
            //rr.material.SetColor("_Color", Color.gray);

            //var sr = stator ? stator.GetComponent<Renderer>() : null;
            //if (sr && sr.material && sr.material.HasProperty("_EmissiveColor"))
            //sr.material.SetColor("_EmissiveColor", Color.black);
            //sr.material.SetColor("_Color", Color.gray);

            //rotation = 0;
            return;
        }

        //if (audioSource != null)
        //{
        //    bool shouldPlay = !(PauseMenu.isOpen || Time.timeScale == 0 || !engineIgnited);

        //    if (shouldPlay)
        //    {
        //        if (!audioSource.isPlaying) audioSource.Play();
        //        audioSource.volume = 1.0f;
        //        audioSource.pitch = 1.0f;
        //    }
        //    else
        //    {
        //        if (audioSource.isPlaying) audioSource.Stop();
        //        audioSource.volume = 0f;
        //    }
        //}

        if (engineIgnited == true)
        {
            //color = new Vector4(1F, 0F, 36F / 255F, 1F);
            Vector4 ctrlVec = new Vector4(vessel.ctrlState.X, vessel.ctrlState.Y, vessel.ctrlState.Z, vessel.ctrlState.mainThrottle);
            //float rps = Mathf.Lerp(12f, 150f, (ctrlVec.magnitude * 0.5f));
            //float step = rps * 1 / 30f;//faster rotation
            //rotation += step;
            //if (rotation >= 360f) rotation -= 360f;
            //Emitter.emit = true;
        }

        if (engineIgnited == false)
        {
            //color = new Vector4(0F, 0F, 0F, 1F);
            //Emitter.emit = false;
            //rotation = 0;
        }

        if (rotor)
        {
            //rotor.transform.localEulerAngles = new Vector3(0f, rotation, 0f);
        }

        //rotor.GetComponent<Renderer>().material.SetColor("_EmissiveColor", color);
        //stator.GetComponent<Renderer>().material.SetColor("_EmissiveColor", color);
        //stator.GetComponent<Light>().color = color;
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
                Taudrive drive = null;
                GNdrive gdrive = null;
                if (m.moduleName == "Taudrive")
                {
                    drive = (Taudrive)m;
                    if (drive.agActivated == true)
                    { 
                        enginecount += 1; 
                    }
                    //if (modified == true && drive.GetInstanceID() != GetInstanceID())
                    //{
                    //    if (drive.modified == true)
                    //    {
                    //        agActivated = drive.agActivated;
                    //        engineIgnited = drive.engineIgnited;
                    //        Overload = drive.Overload;
                    //        overloadtemp = drive.Overload;
                    //
                    //    }
                    //    else
                    //    {
                    //        drive.agActivated = agActivated;
                    //        drive.engineIgnited = engineIgnited;
                    //        drive.Overload = Overload;
                    //        drive.overloadtemp = Overload;
                    //    }
                    //}
                }
                else
                    if (m.moduleName == "GNdrive")
                    {
                        gdrive = (GNdrive)m;
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
            //Emitter.emit = true;
        }
        else
        {
            Events["Deactivate"].guiActive = false;
            Events["Activate"].guiActive = true;
            //Emitter.emit = false;
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
        //if (enginecount > maxenginecount)
        //{
        //    ES = "Unsynchronized";
        //   controlforce = Vector3.zero;
        //    gee = Vector3.zero;
        //}

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
            double backcharge = this.part.RequestResource("ElectricCharge",-GNDrawn * ConvertRatio - elcDrawn);
        }

        double GNconsumtion = this.part.RequestResource("GNparticle", consumption);

        if (consumption !=0 && Math.Round(GNconsumtion,5) < Math.Round(consumption,5))
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

        //Debug.Log("Consumption " + consumption);
        //Debug.Log("tefactor " + tefactor);
        //Debug.Log("Time.deltaTime " + TimeWarp.deltaTime);

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

namespace GNTechnology
{
    public class GNVisualTester : PartModule
    {
        // —— テスト入力（VAB/Flight両方で触れるUI）——
        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Engine ON", isPersistant = true),
         UI_Toggle(disabledText = "OFF", enabledText = "ON")]
        public bool engineOn = false;

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Mode", isPersistant = true),
         UI_ChooseOption(options = new[] { "Drive", "Condenser" })]
        public string modeStr = "Drive"; // Drive or Condenser

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Input Level", isPersistant = true),
         UI_FloatRange(minValue = 0f, maxValue = 1f, stepIncrement = 0.01f)]
        public float level = 0.5f; // 0..1

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Particle Color (r,g,b,a)", isPersistant = true)]
        public string particleColorStr = "0,1,0.6,1"; // 例: 緑(オリジナル) / 赤(タウ)なら "1,0,0.15,1"

        // —— 自動検出した参照群（ログ用表示）——
        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Rotors")]
        public string rotorCount = "0";

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Emitters")]
        public string emitterCount = "0";

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Emissive Rens.")]
        public string emissiveCount = "0";

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Lights")]
        public string lightCount = "0";

        // —— 手動リフレッシュ —— 
        [KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "Refresh Visual Targets")]
        public void RefreshTargets() => DiscoverTargets();

        // 内部状態
        private GNVisualState s = GNVisualState.Empty;

        public override void OnInitialize()
        {
            base.OnInitialize();
            GNVisuals.SetOff(s);//initialize
        }

        public override void OnStart(StartState state)
        {
            Debug.Log("[GN] OnStart Run.");
            base.OnStart(state);

            // In Editor, always should be on so enabled = true
            enabled = true;
            engineOn = false;

            s.part = part;
            DiscoverTargets();
            GNVisuals.UpdateVisual(s);//initialize

            //When loaded in editor, turn off all visual effects.
            if (HighLogic.LoadedSceneIsEditor)
            {
                GNVisuals.SetOff(s);
                Debug.Log("[GN] Visuals set to OFF in editor.");
                return;
            }
        }

        public override void OnUpdate()
        {
            //In editor, OnUpdate isn't called(cry). So if statement isn't necessary.
            base.OnUpdate();
            s.Mode = (modeStr == "Drive") ? GNVisualMode.Drive : GNVisualMode.Condenser;
            s.EngineState = engineOn;
            s.InputLevel = level;
            s.ParticleColor = ParseColor(particleColorStr, new Color(0, 1, 0, 1));
            GNVisuals.UpdateVisual(s);
        }

        private void DiscoverTargets()
        {
            // 同名Transformを全部取得（再帰）
            s.Rotors = part.FindModelTransforms("rotor");

            // 発光対象のRenderer：rotor/stator 直下＋子から拾う（Emissiveプロパティ持ち優先）
            var r1 = part.FindModelTransforms("rotor")
                         .Select(t => t ? t.GetComponent<Renderer>() : null);
            var r2 = part.FindModelTransforms("stator")
                         .Select(t => t ? t.GetComponent<Renderer>() : null);
            s.EmissiveRenderers = r1.Concat(r2)
                                    .Where(r => r != null)
                                    .Distinct()
                                    .ToArray();

            // ライトは stator 以下に複数あってもOK
            var stator = part.FindModelTransform("stator");
            s.GlowLights = stator ? stator.GetComponentsInChildren<Light>(true) : Array.Empty<Light>();

            // 粒子は EMI 以下の全KSPParticleEmitterを拾う（複数OK）
            var emis = part.FindModelTransforms("EMI");
            s.ParticleEmitters = emis.SelectMany(t => t ? t.GetComponentsInChildren<KSPParticleEmitter>(true) : Array.Empty<KSPParticleEmitter>())
                                     .Where(e => e != null)
                                     .ToArray();

            // カウント表示
            rotorCount = s.Rotors.Length.ToString();
            emitterCount = s.ParticleEmitters.Length.ToString();
            emissiveCount = s.EmissiveRenderers.Length.ToString();
            lightCount = s.GlowLights.Length.ToString();

            Debug.Log($"[GN] VisualTargets: rotors={rotorCount}, emitters={emitterCount}, emissive={emissiveCount}, lights={lightCount}");
        }

        private static Color ParseColor(string csv, Color fallback)
        {
            try
            {
                var sp = csv.Split(',');
                if (sp.Length < 3) return fallback;
                float r = Parse(sp[0]), g = Parse(sp[1]), b = Parse(sp[2]);
                float a = (sp.Length >= 4) ? Parse(sp[3]) : 1f;
                return new Color(r, g, b, a);
            }
            catch { return fallback; }

            float Parse(string s) =>
                float.TryParse(s.Trim(), System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0f;
        }
    }
}
