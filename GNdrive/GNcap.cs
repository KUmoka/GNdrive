using UnityEngine;
using System;

public class GNcap : PartModule
{
    [KSPField]
    public float fuelefficiency = 1.1F;
    [KSPField]
    public float particlegrate = 200F;
    [KSPField]
    public float ConvertRatio = 1.25F;
    public Vector4 color = Vector4.zero;


    [KSPField(isPersistant = true)]
    public bool engineIgnited = false;
    public bool flameOut = false;
    public bool depleted = false;
    public bool ecActivated = false;

    //    public bool staged = false;
    public float particleSize = 0.001f;

    private float rotation = 0F;

    private GameObject rotor;
    private GameObject stator;
    [KSPField] public string audioPath = "GNdrive/Audio/GNDriveTypical";
    AudioClip soundClip;
    AudioSource audioSource;
    Transform EMITransform;
    KSPParticleEmitter Emitter;

    [KSPField(guiName = "Engine Status", guiActive = true)]
    private string ES = "Deactivated";

    [KSPField(guiName = "Mass", guiActive = true)]
    private string mass = "N/a";

    [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Max Overload", isPersistant = true), UI_FloatRange(minValue = 0f, maxValue = 2f, stepIncrement = 0.1f)]
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

//    [KSPEvent(name = "Activateec", guiName = "Activate Converter", active = true, guiActive = true)]
//    public void Activateec()
//    {
//        ecActivated = true;
//        Events["Deactivateec"].guiActive = true;
//        Events["Activateec"].guiActive = false;
//    }

//    [KSPEvent(name = "Deactivateec", guiName = "Deactivate Converter", active = true, guiActive = false)]
//    public void Deactivateec()
//    {
//        ecActivated = false;
//        Events["Deactivateec"].guiActive = false;
//        Events["Activateec"].guiActive = true;
//    }

    protected Transform rotorTransform = null;

    public override void OnStart(PartModule.StartState state)
    {
        //Not flight, not active
        if (HighLogic.LoadedSceneIsEditor) return;

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
            if (base.part.FindModelTransform("rotor").gameObject != null)
            {
                stator = base.part.FindModelTransform("stator").gameObject;
                rotor = base.part.FindModelTransform("rotor").gameObject;
            }

            EMITransform = base.part.FindModelTransform("EMI");
            Emitter = EMITransform.gameObject.GetComponent<KSPParticleEmitter>();
            Emitter.emit = false;

        }
        SetupAudio();
    }
    public void SetupAudio()
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
    public void Update()
    {
        if (HighLogic.LoadedSceneIsEditor)
        {
            if (Emitter) Emitter.emit = false;
            Debug.Log("[GN] GNcap_OnUpdate fired");
            color = new Vector4(0F, 0F, 0F, 1F);

            var rr = rotor ? rotor.GetComponent<Renderer>() : null;
            if (rr && rr.material && rr.material.HasProperty("_EmissiveColor"))
                rr.material.SetColor("_EmissiveColor", Color.black);

            var sr = stator ? stator.GetComponent<Renderer>() : null;
            if (sr && sr.material && sr.material.HasProperty("_EmissiveColor"))
                sr.material.SetColor("_EmissiveColor", Color.black);

            rotation = 0;
            return;
        }

        if (audioSource != null)
        {
            bool shouldPlay = !(PauseMenu.isOpen || Time.timeScale == 0 || !engineIgnited);

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

        if (engineIgnited == true)
        {
            color = new Vector4(1F, 0F, 36F / 255F, 1F);
            Vector4 ctrlVec = new Vector4(vessel.ctrlState.X, vessel.ctrlState.Y, vessel.ctrlState.Z, vessel.ctrlState.mainThrottle);
            float rps = Mathf.Lerp(10f, 100f, (ctrlVec.magnitude * 0.5f));
            float step = rps * 1 / 30f;
            rotation += step;
            if (rotation >= 360f) rotation -= 360f;
            Emitter.emit = true;
        }

        if (engineIgnited == false)
        {
            color = new Vector4(0F, 0F, 0F, 1F);
            Emitter.emit = false;
            rotation = 0;
        }

        if (rotor)
        {
            rotor.transform.localEulerAngles = new Vector3(0f, 0f, rotation);
        }

        rotor.GetComponent<Renderer>().material.SetColor("_EmissiveColor", color);
        stator.GetComponent<Renderer>().material.SetColor("_EmissiveColor", color);
        stator.GetComponent<Light>().color = color;
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
        float ID = GetInstanceID();

        if (engineIgnited == true)
        {
            ES = "Activated";
            Events["Deactivate"].guiActive = true;
            Events["Activate"].guiActive = false;
            Emitter.emit = true;
        }
        else
        {
            Events["Deactivate"].guiActive = false;
            Events["Activate"].guiActive = true;
            Emitter.emit = false;
        }

        if (depleted == true)
        {
            ES = "GNparticle depleted";
            Deactivate();
        }

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
            //Color set to zero while engineignited==false
            color = new Vector4(0F, 0F, 0F, 1F);
            rotor.GetComponent<Renderer>().material.SetColor("_EmissiveColor", color);
            stator.GetComponent<Renderer>().material.SetColor("_EmissiveColor", color);
            stator.GetComponent<Light>().color = color;
        }

        double consumption = vessel.GetTotalMass() * Mathf.Abs((controlforce).magnitude) * fuelefficiency * TimeWarp.deltaTime;
        
//		if (ecActivated == true)
//		{
//			float elcconsume = particlegrate * ConvertRatio * TimeWarp.deltaTime;
//			float elcDrawn = this.part.RequestResource("ElectricCharge", elcconsume);
//			float ratio = elcDrawn / elcconsume;
//			float GNDrawn = this.part.RequestResource("GNparticle", -(elcconsume / ConvertRatio) * ratio);
//			float backcharge = this.part.RequestResource("ElectricCharge", -GNDrawn * ConvertRatio - elcDrawn);
//		  //  Debug.Log("elcDrawn" + elcDrawn);
//		  //  Debug.Log("GNDrawn" + GNDrawn);
//		  //  Debug.Log("backcharge " + backcharge);
//
//		}

        double GNconsumtion = this.part.RequestResource("GNparticle", consumption);
        double Egen = this.part.RequestResource("ElectricCharge", -GNconsumtion / 10);

        if (consumption != 0 && Math.Round(GNconsumtion, 5) < Math.Round(consumption, 5))
        {
            depleted = true;
            controlforce = Vector3.zero;
            engineIgnited = false;
            Deactivate();
            part.Resources["GNparticle"].amount = 0;

        }

        //Debug.Log("Consumption " + consumption);
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
            //Color set to non-zero while engineignited==true
            color = new Vector4(0F, 1F, 170F / 255F, 1F);
            rotor.GetComponent<Renderer>().material.SetColor("_EmissiveColor", color);
            stator.GetComponent<Renderer>().material.SetColor("_EmissiveColor", color);
            stator.GetComponent<Light>().color = color;

            rotor.transform.localEulerAngles = new Vector3(90, 0, rotation);
            rotation += 6 * (Mathf.Clamp01(controlforce.magnitude / 100f) + 1) * 120 * TimeWarp.deltaTime;// * (1 + vessel.ctrlState.mainThrottle * rotermultiplier);Mathf.Abs(controlforce.magnitude),1/250*2.5/1=1/100
            while (rotation > 360) rotation -= 360;
            while (rotation < 0) rotation += 360;
        }
    }
}
