using GNTechnology;
using KSP;
using KSP.UI.Screens.Settings.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

//// Basic info
//public Part part; //GN tech part
//public GNVisualMode Mode;//Visual mode.Condenser or Drive.
//public Color ParticleColor; //green:original drive, red:tau drive
//public float InputLevel; //from 0 to 1
//public bool EngineState; //true:engine on, false:engine off

//// Transforms and renderers
//public Transform[] Rotors; //rotor transforms
//public Light[] GlowLights; //glow light sources
//public Renderer[] EmissiveRenderers; //emissive renderers(rotor, stator)
//public KSPParticleEmitter[] ParticleEmitters; //EMI (particle emitters)

namespace GNTechnology
{
    public class GN1 : PartModule
    {
        public override void OnAwake()
        {
            base.OnAwake();
            Debug.Log("[GN] GN1 OnAwake called.");
        }   

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            Debug.Log("[GN] GN1 OnStart called.");
            part.enabled = true;
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
            Debug.Log("[GN] GN1 OnUpdate called.");
        }

        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();
            Debug.Log("[GN] GN1 OnFixedUpdate called.");
        }   
    }

    public class GN2 : GN1
    {
        [KSPField(guiActive = true, guiName = "Engine State", isPersistant = true), UI_Toggle(disabledText = "OFF", enabledText = "ON")]
        public bool engineOn = false;
        [KSPField(guiActive = true, guiName = "Test1", isPersistant = true), UI_Toggle(disabledText = "OFF", enabledText = "ON")]
        public bool transamOn = false;
        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Max Acceleration", isPersistant = true), UI_FloatRange(minValue = 0f, maxValue = 5f, stepIncrement = 0.1f)]
        public float CurrentG = 1f;

        public void Update()
        { 
            part.enabled = engineOn;
        }

        public override void OnAwake()
        {
            base.OnAwake();
            Debug.Log("[GN] GN2 OnAwake called.");
        }   

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            part.enabled = false;
            Debug.Log("[GN] GN2 OnStart called.");
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
            Debug.Log("[GN] GN2 OnUpdate called.");
        }

        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();
            Debug.Log("[GN] GN2 OnFixedUpdate called.");
            Debug.Log("[GN] engineOn: " + engineOn);
        }
    }

    public class GN3 : GN1
    {
        [KSPField(guiActive = true, guiName = "Engine State", isPersistant = true), UI_Toggle(disabledText = "OFF", enabledText = "ON")]
        public bool engineOn = false;
        [KSPField(guiActive = true, guiName = "Test1", isPersistant = true), UI_Toggle(disabledText = "OFF", enabledText = "ON")]
        public bool transamOn = false;
        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Max Acceleration", isPersistant = true), UI_FloatRange(minValue = 0f, maxValue = 5f, stepIncrement = 0.1f)]
        public float CurrentG = 1f;

        public void Update()
        {
            part.enabled = engineOn;
        }

        public override void OnAwake()
        {
            base.OnAwake();
            Debug.Log("[GN] GN3 OnAwake called.");
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            part.enabled = false;
            Debug.Log("[GN] GN3 OnStart called.");
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
            Debug.Log("[GN] GN3 OnUpdate called.");
        }

        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();
            Debug.Log("[GN] GN3 OnFixedUpdate called.");
            Debug.Log("[GN] engineOn: " + engineOn);
            Debug.Log("[GN] TAOn: " + transamOn);
        }
    }
}
