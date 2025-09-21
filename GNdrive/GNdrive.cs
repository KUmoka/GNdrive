using System;
using UnityEngine;


public class GNdrive : PartModule
{
    [KSPField]
    public float fuelefficiency = 1F;
    [KSPField]
    public float particlegrate = 1000F;
    [KSPField]
    public float maxenginecount = 2F;

    public Vector4 color = Vector4.zero;

    [KSPField(isPersistant = true)]
    public bool engineIgnited = false;
    public bool flameOut = false;
    public bool agActivated = false;
    public bool hvActivated = false;
    public bool taactivated = false;
    public bool ICactivated = false;
    public bool ICIsActivaed = false;
    public bool modified = false;
    public float overloadtemp = 0;
    [KSPField] public string audioPath = "GNdrive/Audio/GNDriveTypical";
    AudioClip soundClip;
    AudioSource audioSource;
    Transform EMITransform;
    KSPParticleEmitter Emitter;

    //    public bool staged = false;
    public float particleSize = 0.001f;

    private float rotation = 0F;
    private PidController brakePid = new PidController(10F, 0.005F, 0.002F, 50, 5);

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
        this.part.force_activate();
        engineIgnited = true;
        Events["Deactivate"].guiActive = true;
        Events["Activate"].guiActive = false;
        modified = true;

    }

    [KSPEvent(name = "Deactivate", guiName = "Deactivate Engine", active = true, guiActive = false)]
    public void Deactivate()
    {
        engineIgnited = false;
        Events["Deactivate"].guiActive = false;
        Events["Activate"].guiActive = true;
        modified = true;
    }

    [KSPEvent(name = "Activateta", guiName = "Trans-AM", active = true, guiActive = false)]
    public void Activateta()
    {
        taactivated = true;
        Events["Activateta"].guiActive = false;
        modified = true;
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
        this.part.force_activate();
        agActivated = true;
        Events["Deactivateag"].guiActive = true;
        Events["Activateag"].guiActive = false;
        Events["Activatehv"].guiActive = true;
        modified = true;
        Deactivatehv();
    }

    [KSPEvent(name = "Deactivateag", guiName = "Deactivate Antigravity", active = true, guiActive = false)]
    public void Deactivateag()
    {
        agActivated = false;
        hvActivated = false;
        Events["Deactivateag"].guiActive = false;
        Events["Activateag"].guiActive = true;
        Events["Activatehv"].guiActive = false;
        Events["Deactivatehv"].guiActive = false;
        modified = true;
    }

    [KSPAction("Toggle Hover", KSPActionGroup.None)]
    private void Togglehv(KSPActionParam param)
    {
        if (agActivated == true && !hvActivated)
        {
            Activatehv();
        }
        else
        {
            Deactivatehv();
        }

    }

    [KSPEvent(name = "Activatehv", guiName = "Activate Hover", active = true, guiActive = false)]
    public void Activatehv()
    {
        this.part.force_activate();
        hvActivated = true;
        Events["Deactivatehv"].guiActive = true;
        Events["Activatehv"].guiActive = false;
        modified = true;

    }

    [KSPEvent(name = "Deactivatehv", guiName = "Deactivate Hover", active = true, guiActive = false)]
    public void Deactivatehv()
    {
        hvActivated = false;
        Events["Deactivatehv"].guiActive = false;
        Events["Activatehv"].guiActive = true;
        modified = true;
    }
    [KSPAction("Toggle Inertia control", KSPActionGroup.None)]
    private void ICActionActivate(KSPActionParam param)
    {
        if (ICIsActivaed == true)
        {
            ICDeactivate();
        }
        else
        {
            ICActivate();
        }
    }

    [KSPEvent(name = "ICActivate", guiName = "Activate Inertia control", active = true, guiActive = true)]
    public void ICActivate()
    {
        this.part.force_activate();
        ICIsActivaed = true;
        Events["ICDeactivate"].guiActive = true;
        Events["ICActivate"].guiActive = false;
        modified = true;
    }

    [KSPEvent(name = "ICDeactivate", guiName = "Deactivate Inertia control", active = true, guiActive = false)]
    public void ICDeactivate()
    {
        ICIsActivaed = false;
        Events["ICDeactivate"].guiActive = false;
        Events["ICActivate"].guiActive = true;
        modified = true;
    }
    protected Transform rotorTransform = null;

    public override void OnStart(PartModule.StartState state)
    {
        part.stagingIcon = "LIQUID_ENGINE";
        if (state != StartState.Editor && state != StartState.None)
        {
            this.enabled = true;
            this.part.force_activate();
        }
        if (base.part.FindModelTransform("rotor").gameObject != null)
        {
            stator = base.part.FindModelTransform("stator").gameObject;
            rotor = base.part.FindModelTransform("rotor").gameObject;
        }

        EMITransform = base.part.FindModelTransform("EMI");
        Emitter = EMITransform.gameObject.GetComponent<KSPParticleEmitter>();
        Emitter.emit = false;

        overloadtemp = Overload;
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
            Debug.Log("[GN] GNdrive_OnUpdate fired");
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
            if (taactivated == true)
            {
                color = new Vector4(1F, 0F, 100F / 255F, 1F);
            }
            else
            {
                color = new Vector4(0F, 1F, 170F / 255F, 1F);
            }                
            Vector4 ctrlVec = new Vector4(vessel.ctrlState.X, vessel.ctrlState.Y, vessel.ctrlState.Z, vessel.ctrlState.mainThrottle);
            float rps = Mathf.Lerp(10f, 100f, (ctrlVec.magnitude * 0.5f));
            float step = rps  * 1 / 30f;
            rotation += step;
            if (rotation >= 360f) rotation -= 360f;
            Emitter.emit = true;
        }

        if (ES == "Unsynchronized")
        {
            color = new Vector4(0F, 1F / 4F, 42F / 255F, 1F);
            Emitter.emit = false;
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
        if (!HighLogic.LoadedSceneIsFlight || !vessel.isActiveVessel) return;
        float pitch = vessel.ctrlState.pitch;
        float roll = vessel.ctrlState.roll;
        float yaw = vessel.ctrlState.yaw;
        float throttle = vessel.ctrlState.mainThrottle * Overload;
        float y = -vessel.ctrlState.Y * Overload * 10;
        float x = -vessel.ctrlState.X * Overload * 10;
        float z = throttle * 10 - vessel.ctrlState.Z * Overload * 10;
        float enginecount = 1;
        float agenginecount = 0;
        float tefactor = 1;
        float ID = GetInstanceID();

        if (Overload != overloadtemp)
        {
            modified = true;
        }

        if (agActivated == true)
        {
            engineIgnited = true;
        }

        if (engineIgnited == true)
        {
            foreach (Part p in this.vessel.Parts)
            {
                foreach (PartModule m in p.Modules)
                {
                    GNdrive drive = null;
                    Taudrive tdrive = null;
                    if (m.moduleName == "GNdrive")
                    {
                        drive = (GNdrive)m;
                        if (drive.engineIgnited == true && drive.GetInstanceID() != GetInstanceID())
                        {
                            enginecount += 1;
                            if (modified == true)
                            {
                                if (drive.modified == true)
                                {
                                    taactivated = drive.taactivated;
                                    agActivated = drive.agActivated;
                                    Overload = drive.Overload;
                                    overloadtemp = drive.Overload;
                                    hvActivated = drive.hvActivated;
                                    ICactivated = drive.ICactivated;
                                    ICIsActivaed = drive.ICIsActivaed;

                                }
                                else
                                {
                                    drive.taactivated = taactivated;
                                    drive.agActivated = agActivated;
                                    drive.Overload = Overload;
                                    drive.overloadtemp = Overload;
                                    drive.hvActivated = hvActivated;
                                    drive.ICactivated = ICactivated;
                                    drive.ICIsActivaed = ICIsActivaed;
                                }
                            }
                        }
                        if (drive.agActivated == true)
                        {
                            agenginecount += 1;
                        }
                    }
                    else
                        if (m.moduleName == "Taudrive")
                    {
                        tdrive = (Taudrive)m;
                        if (tdrive.agActivated == true)
                        {
                            agenginecount += 1;
                        }
                    }
                }
            }
        }

        modified = false;

        if (engineIgnited == true)
        {
            ES = "Activated";
            Events["Deactivate"].guiActive = true;
            Events["Activate"].guiActive = false;
        }
        else
        {
            Events["Deactivate"].guiActive = false;
            Events["Activate"].guiActive = true;
        }


        if (hvActivated == true && agActivated == true)
        {
            Events["Deactivatehv"].guiActive = true;
            Events["Activatehv"].guiActive = false;
        }
        else
        {
            Events["Deactivatehv"].guiActive = false;
            Events["Activatehv"].guiActive = true;
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
            Events["Deactivatehv"].guiActive = false;
            Events["Activatehv"].guiActive = false;
            hvActivated = false;
            if (engineIgnited == false)
            {
                taactivated = false;
                Events["Activateta"].guiActive = true;
            }
        }

        if (taactivated == true)
        {
            ES = "Trans-AM";
        }

        Vector3 srfVelocity = vessel.GetSrfVelocity();
        float VerticalV;
        VerticalV = (float)vessel.verticalSpeed;
        //bool break = 
        Vector3 Airspeed = vessel.transform.InverseTransformDirection(srfVelocity);
        Vector3 gee = FlightGlobals.getGeeForceAtPosition(this.vessel.transform.position) / agenginecount;
        float Vvelocity = Vector3.Dot(gee.normalized, part.rb.velocity);
        brakePid.Calibrateclamp(Overload);
        Vector3 VvCancel = hvActivated ? brakePid.Control(Vvelocity) * gee.normalized * 10 / agenginecount : Vector3.zero;
        Vector3 controlforce = vessel.ReferenceTransform.up * z + vessel.ReferenceTransform.forward * y + vessel.ReferenceTransform.right * x - VvCancel;

        if (enginecount > maxenginecount)
        {
            ES = "Unsynchronized";
            controlforce = Vector3.zero;
            gee = Vector3.zero;
            tefactor = 0.001F;
        }
        else
        {
            tefactor = (float)Math.Pow((double)particlegrate, (double)enginecount - 1);
        }

        if (engineIgnited == false)
        {
            controlforce = Vector3.zero;
        }
        if (agActivated == false)
        {
            gee = Vector3.zero;
        }
        float consumption = vessel.GetTotalMass() * Mathf.Abs((-gee + controlforce).magnitude) * fuelefficiency;
        float particlegen = particlegrate * tefactor;

        if (taactivated == true)
        {

            controlforce *= 5;
            consumption = 4 * consumption - 3 * vessel.GetTotalMass() * Mathf.Abs(gee.magnitude) * fuelefficiency;
            Events["Activateta"].guiActive = false;
            consumption = Mathf.Max(particlegen, consumption) + 4;
        }
        else
        {
            if (engineIgnited == true)
            {
                Events["Activateta"].guiActive = true;
            }

        }

        double reschange = (consumption - particlegen) * TimeWarp.fixedDeltaTime;
        double resourceDrawn = this.part.RequestResource("GNparticle", reschange);

        if (resourceDrawn == 0 && reschange > 0)
        {
            ES = "GNparticle depleted";
            controlforce = Vector3.zero;
            gee = Vector3.zero;
            Deactivate();
            Deactivateag();
            taactivated = false;
        }

        // Debug.Log("Consumption " + consumption);
        // Debug.Log("particlegen " + particlegen);
        // Debug.Log("tefactor " + tefactor);
        // Debug.Log("resourceDrawn" + resourceDrawn);

        mass = vessel.GetTotalMass().ToString("R");

        if (engineIgnited == true)
        {
            if (this.vessel.ActionGroups.groups[3])
            {
                if (controlforce.magnitude > Overload)
                {
                    controlforce = controlforce.normalized * Overload;
                }

                if(ICIsActivaed)
                {
                    InertiaControl();
                }
                
                Vector3 Breakforce = this.vessel.ActionGroups.groups[5] ? (-this.vessel.GetSrfVelocity()).normalized * Mathf.Min(this.vessel.GetSrfVelocity().magnitude / Time.fixedDeltaTime, Overload * 10f) - gee * 0.9f : Vector3.zero;
                controlforce += Breakforce;

            }
            foreach (Part p in this.vessel.parts)
            {

                if ((p.physicalSignificance == Part.PhysicalSignificance.FULL) && (p.rb != null))
                {
                    p.AddForce(controlforce * p.rb.mass);
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
        void InertiaControl()
        {

            float DirFlag = 0;
            if (Airspeed.y < 0)
            {
                DirFlag = 2;
            }
            Vector3 InertiaForce = Vector3.zero;
            float ReD=5000;
            Vessel target=null ;
            if (this.vessel.targetObject != null)
            {
               target = this.vessel.targetObject.GetVessel();
               ReD = Vector3.Distance(this.vessel.transform.position, target.transform.position);
            }
            if (target && ReD<3000)
            {                
                Vector3 RelVel = this.vessel.transform.InverseTransformDirection(this.vessel.rb_velocity-target.rb_velocity);
                Vector3 yawsForce = (x == 0 ? RelVel.x : -x) * -this.vessel.transform.right;
                Vector3 pitchsForce = (y == 0 ? RelVel.z : -y) * -this.vessel.transform.forward;
                Vector3 FrontForce = (RelVel.y - 10 * z < 0 && RelVel.y > 0 ? -z : RelVel.y) * -this.vessel.transform.up;
                InertiaForce = hvActivated ? Vector3.ProjectOnPlane(yawsForce + pitchsForce + FrontForce, gee.normalized) : yawsForce + pitchsForce + FrontForce;
                ICactivated = true;
            }
            else
            {
                if (target && ICactivated)
                {
                    ICactivated = false;
                    ICDeactivate();
                }
                else
                {
                    Vector3 yawsForce = (x == 0 ? Airspeed.x : -x) * -this.vessel.transform.right;
                    Vector3 pitchsForce = (y == 0 ? Airspeed.z : -y) * -this.vessel.transform.forward;
                    Vector3 FrontForce = Airspeed.y * -this.vessel.transform.up * DirFlag;
                    InertiaForce = hvActivated ? Vector3.ProjectOnPlane(yawsForce + pitchsForce + FrontForce, gee.normalized) : yawsForce + pitchsForce + FrontForce;
                    if (InertiaForce.magnitude > Overload)
                    {
                        InertiaForce = InertiaForce.normalized * Overload * 10;
                    }
                }
            }
            controlforce += (InertiaForce) / Time.fixedDeltaTime/enginecount;
        }
    }
}
