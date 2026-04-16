# GNdrive class diagram

This document summarizes the main classes, structs, and static helpers in the GNdrive mod as Mermaid class diagrams.

## Overview

```mermaid
classDiagram
    direction LR

    class PartModule {
        <<KSP>>
    }
    class VesselModule {
        <<KSP>>
    }
    class ModuleCargoBay {
        <<KSP>>
    }

    class GNBaseSystem {
        +GNVisualState vs
        +GNPhysicsState ps
        +OnOffList OnOff
        +bool engineOn
        +bool agOn
        +bool hvOn
        +bool taOn
        +float accel
        +bool ECOn
        +bool sgOn
        +bool SyOn
        +GNSystemState previousGNSystemState
        +DriveState ES
        +OnStart(StartState)
        +OnUpdate()
        +OnFixedUpdate()
    }

    class GNThrusterSystem {
        +float particlepower
        +OnActive()
        +OnStart(StartState)
        +OnUpdate()
        +OnFixedUpdate()
    }

    class GNCondenserDriveSystem {
        +float particlepower
        +OnActive()
        +OnStart(StartState)
        +OnUpdate()
        +OnFixedUpdate()
    }

    class GNDriveTauSystem {
        +float particlepower
        +float ManufactureVariance
        +OnActive()
        +OnStart(StartState)
        +OnUpdate()
        +OnFixedUpdate()
    }

    class GNDriveSystem {
        +float particlepower
        +float ManufactureVariance
        +bool isSecondGen
        +bool isSgOn
        +OnActive()
        +OnStart(StartState)
        +OnUpdate()
        +OnFixedUpdate()
    }

    class GNShieldModule {
        +float myradius
        +bool FieldON
        +string texPath
        +bool IsShipChanged
        +OnStart(StartState)
        +OnUpdate()
        +OnFixedUpdate()
    }

    class GNfieldSurfaceModule {
        +bool isActive
        +float fieldRadius
        +int updateInterval
        +float repelK
        +float repelD
        +float maxRepelAccel
        +SetActive(bool)
        +OnStart(StartState)
        +OnUpdate()
        +OnFixedUpdate()
    }

    class GNTestSphereFX {
        +float myradius
        +bool FieldON
        +bool IsShipChanged
        +OnStart(StartState)
        +OnUpdate()
        +OnInactive()
    }

    class GNsword {
        +float fuelconsumption
        +float Maxbladelength
        +float BladeHeat
        +Activate()
        +Deactivate()
        +OnStart(StartState)
        +OnFixedUpdate()
    }

    class GNLists {
        +List~GNThrusterSystem~ GNThruster
        +List~GNCondenserDriveSystem~ GNCondernserDrive
        +List~GNDriveSystem~ GNDrive
        +List~GNDriveTauSystem~ GNDriveTau
        +List~GNShieldModule~ GNShield
        +ShouldBeActive() bool
    }

    class GNVessels {
        +OnStart()
    }

    PartModule <|-- GNBaseSystem
    GNBaseSystem <|-- GNThrusterSystem
    GNBaseSystem <|-- GNCondenserDriveSystem
    GNBaseSystem <|-- GNDriveTauSystem
    GNBaseSystem <|-- GNDriveSystem
    PartModule <|-- GNShieldModule
    PartModule <|-- GNTestSphereFX
    PartModule <|-- GNsword
    ModuleCargoBay <|-- GNfieldSurfaceModule
    VesselModule <|-- GNLists
    VesselModule <|-- GNVessels
```

## Drive state and services

```mermaid
classDiagram
    direction TB

    class DriveState {
        <<enum>>
        Unsynchronized
        Depleted
        Activated
        Deactivated
        Refilled
        Reposed
    }

    class GNSystemState {
        <<struct>>
        +bool EngineOn
        +bool AntigravityOn
        +bool HoveringOn
        +bool TransAMOn
        +float Acceleration
        +bool ECInputOn
        +bool SafetyGuardOn
        +bool SynchronizeOn
        +Empty GNSystemState
    }

    class GNPhysicsState {
        <<struct>>
        +Part part
        +bool EngineState
        +bool AgOn
        +bool HvOn
        +bool TaOn
        +bool ECOn
        +bool SafeGuard
        +bool Shortage
        +float ParticlePower
        +float ParticleGenRate
        +float MaxG
        +float RCSFactor
        +double UsedGNParticle
        +float Individuality
        +float SyncRate
        +bool UnSync
        +bool GNdepleted
        +bool ECdepleted
        +Vector3 ThrustDir
        +Empty GNPhysicsState
    }

    class GNVisualMode {
        <<enum>>
        Condenser
        Drive
        CondenserDrive
    }

    class GNVisualState {
        <<struct>>
        +Part part
        +GNVisualMode Mode
        +Color ParticleColor
        +float InputLevel
        +float Smoothed
        +float RotorSpeed
        +float MoveDistance
        +bool MoveOn
        +bool EngineState
        +Transform[] Rotors
        +Light[] GlowLights
        +Renderer[] EmissiveRenderers
        +KSPParticleEmitter[] ParticleEmitters
        +Transform[] MovingParts
        +Transform[] RotParts
        +Vector3d ThrustVector
        +Empty GNVisualState
    }

    class OnOffList {
        <<struct>>
        +bool LengineOn
        +bool LagOn
        +bool LtaOn
        +bool LhvOn
        +bool LECOn
        +bool LSgOn
        +float MaxG
        +Empty OnOffList
    }

    class GNBaseSystem
    class GNPhysics {
        <<static>>
        +SetOff(GNPhysicsState)
        +UpdatePhysics(GNPhysicsState)
    }
    class GNGenerationFurnace {
        <<static>>
        +ParticleSupply(GNPhysicsState, double)
    }
    class GNVisuals {
        <<static>>
        +SetOff(GNVisualState)
        +UpdateVisual(GNVisualState)
    }
    class GNColorDecider {
        <<static>>
        +ParticleColor(GNVisualState) Color
    }
    class GNVisualAggregator {
        <<static>>
        +Aggregate(Vessel) Result
    }
    class GNSynchronizer {
        <<static>>
        +IsThereOtherSyncDriveTarget(Vessel, uint) bool
        +SynchronizeOtherTargetDrive(OnOffList, Vessel, uint, GNPhysicsState)
    }

    GNBaseSystem *-- GNVisualState
    GNBaseSystem *-- GNPhysicsState
    GNBaseSystem *-- GNSystemState
    GNBaseSystem *-- OnOffList
    GNBaseSystem --> DriveState
    GNVisualState --> GNVisualMode
    GNBaseSystem ..> GNVisuals : visuals
    GNBaseSystem ..> GNPhysics : forces
    GNBaseSystem ..> GNGenerationFurnace : particles
    GNBaseSystem ..> GNSynchronizer : sync
    GNBaseSystem ..> GNVisualAggregator : vessel color
    GNDriveSystem ..> GNColorDecider : color
    GNDriveTauSystem ..> GNColorDecider : color
    GNPhysics ..> GNPhysicsState
    GNGenerationFurnace ..> GNPhysicsState
    GNVisuals ..> GNVisualState
    GNColorDecider ..> GNVisualState
    GNSynchronizer ..> OnOffList
    GNSynchronizer ..> GNPhysicsState
```

## Shield and field modules

```mermaid
classDiagram
    direction LR

    class AeroBackup {
        <<struct>>
        +float maxDrag
        +float minDrag
        +float dragScalar
        +bool dragCubesEnabled
    }

    class GNParticleHelpers {
        <<static>>
        +CreateSphericalShellEmitter(Part, string, float, float, float, float, float, float, bool) ParticleSystem
        +SetParticleTexture(ParticleSystem, string)
        +SetParticleStartColor(ParticleSystem, Color)
        +SetPSPosition(ParticleSystem, Part, Vessel)
        +GetMaxVesselRadiusFromCoM(Vessel) float
    }

    class GNShieldModule {
        +bool FieldON
        +float myradius
        +bool IsShipChanged
    }

    class GNfieldSurfaceModule {
        +bool isActive
        +float fieldRadius
        +SetActive(bool)
    }

    class GNTestSphereFX {
        +bool FieldON
        +float myradius
        +bool IsShipChanged
    }

    class GNVessels {
        +OnStart()
    }

    GNShieldModule *-- GNfieldSurfaceModule : same Part
    GNShieldModule ..> GNParticleHelpers : field VFX
    GNTestSphereFX ..> GNParticleHelpers : test VFX
    GNfieldSurfaceModule *-- AeroBackup : aero backup
    GNVessels ..> GNShieldModule : vessel changed
    GNVessels ..> GNTestSphereFX : vessel changed
```

## Other utility modules

```mermaid
classDiagram
    class GNsword {
        +float fuelconsumption
        +float Maxbladelength
        +float BladeHeat
        +Activate()
        +Deactivate()
        +OnStart(StartState)
        +OnFixedUpdate()
    }

    class PidController {
        -float mKp
        -float mKi
        -float mKd
        -float mClamp
        +PidController(float, float, float, int, float)
        +Reset()
        +Control(float) float
        +Value float
        +Calibrate()
        +Calibrateclamp(float)
    }
```

## Source map

- `GNDriveCore.cs`: `GNBaseSystem`, drive subclasses, `DriveState`, `GNSystemState`
- `GNPhysics.cs`: `GNPhysicsState`, `GNPhysics`, `GNGenerationFurnace`
- `GNVisuals.cs`: `GNVisualState`, `GNVisualMode`, `GNVisuals`, `GNColorDecider`
- `GNMechanics.cs`: `GNLists`, `GNVisualAggregator`, `GNSynchronizer`
- `GNshield.cs`: `AeroBackup`, `GNParticleHelpers`, `GNTestSphereFX`, `GNShieldModule`, `GNfieldSurfaceModule`
- `GNVessels.cs`: `GNVessels`
- `GNsword.cs`: `GNsword`
- `PID.cs`: `PidController`
