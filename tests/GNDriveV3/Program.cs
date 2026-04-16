using System;
using System.Collections.Generic;
using System.Linq;
using GNSystems.Core;
class Port : IResourcePort
{
    public double Value, Max, TakeFraction = 1, PutFraction = 1; public int Takes, Puts;
    public double Amount
    {
        get
        {
            return Value;
        }
    }
    public double Capacity
    {
        get
        {
            return Max;
        }
    }
    public double FreeSpace
    {
        get
        {
            return Max - Value;
        }
    }
    public double Take(double n)
    {
        Takes++;
        double got = Math.Min(Value, n) * TakeFraction;
        Value -= got;
        return got;
    }
    public double Put(double n)
    {
        Puts++;
        double got = Math.Min(Max - Value, n) * PutFraction;
        Value += got;
        return got;
    }
}
class Fixture
{
    public List<DriveState> Drives = new List<DriveState>(); public VesselSettings Settings = new VesselSettings { SafeGuard = false, AccelerationG = 100, RotationShare = 0 };
    public StepInput Input = new StepInput { Dt = 1, MassKg = 1000, Throttle = 1 };
    public Port P = new Port { Value = 1000, Max = 1000 }, E = new Port { Value = 1000, Max = 1000 };
    public Fixture()
    {
        Drives.Add(NewDrive(1));
    }
    public static DriveState NewDrive(uint id)
    {
        return new DriveState { Id = id, Engine = true, Tank = 1000, TankCapacity = 1000, Definition = new DriveDefinition { Propulsion = true, TransAM = true, MultiDrive = true, Power = 100, MaxGContribution = 100 } };
    }
    public StepResult Step()
    {
        return new VesselSimulation().Step(Drives, Settings, Input, P, E);
    }
    public bool Command(CommandKind k, bool v, uint id = 1)
    {
        string reason;
        return Commands.Apply(Drives, Settings, id, k, v, out reason);
    }
}
class Program
{
    static int passed, failed;
    static void Eq(double a, double b, double eps = 1e-7)
    {
        if (!Numbers.Finite(a) || Math.Abs(a - b) > eps)
            throw new Exception("Expected " + b + ", got " + a);
    }
    static void True(bool b)
    {
        if (!b)
            throw new Exception("Assertion failed");
    }
    static void Throws(Action a)
    {
        try
        {
            a();
        }
        catch (ArgumentException) { return; }
        throw new Exception("Expected argument rejection");
    }
    static void Test(string name, Action test)
    {
        try
        {
            test();
            passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.Message); }
    }
    static Fixture Support()
    {
        var f = new Fixture();
        f.Input.Throttle = 0;
        f.Input.Gravity = new Vec(0, -10, 0);
        f.Drives[0].AG = true;
        return f;
    }
    static void Main()
    {
        Test("01 Hover overrides AG", () => { var f = Support(); f.Input.Dt = 0.02; f.Drives.Add(Fixture.NewDrive(2)); f.Drives[1].Hover = true; f.Input.Velocity = new Vec(0, -2, 0); Eq(f.Step().Effects.SupportKN.Length, 14); });
        Test("02 Multiple AG no double support", () => { var f = Support(); f.Drives.Add(Fixture.NewDrive(2)); f.Drives[1].AG = true; Eq(f.Step().Effects.SupportKN.Length, 10); });
        Test("03 Hover falling increases support", () => Eq(HoverLaw.Support(10, -2, 0.02, 0.5, 100, 0.01), 14));
        Test("04 Hover ascending never pushes down", () => Eq(HoverLaw.Support(10, 100, 0.02, 0.5, 100, 0.01), 0));
        Test("05 Zero gravity no support", () => Eq(HoverLaw.Support(0, -10, 0.02, 0.5, 100, 0.01), 0));
        Test("06 Zero acceleration forbids support", () => { var f = Support(); f.Settings.AccelerationG = 0; Eq(f.Step().ForceN.Length, 0); });
        Test("07 Mass acceleration cap", () => { var f = new Fixture(); f.Settings.AccelerationG = 1; Eq(f.Step().ForceN.Length, 9806.65); });
        Test("08 Power cap", () => { var f = new Fixture(); Eq(f.Step().ForceN.Length, 100000); });
        Test("09 Support first on shortage", () => { var f = Support(); f.Input.Throttle = 1; f.Input.Forward = new Vec(1, 0, 0); f.P.Value = 5; var r = f.Step(); Eq(r.Effects.SupportKN.Length, 5); Eq(r.Effects.ThrustKN.Length, 0); });
        Test("10 Downward thrust removed", () => { var f = Support(); f.Input.Throttle = 1; f.Input.Forward = new Vec(0, -1, 0); var r = f.Step(); Eq(r.Effects.ThrustKN.Length, 0); Eq(r.ForceN.Y, 10000); });
        Test("11 Brakes replace throttle and RCS translation", () => { var f = new Fixture(); f.Input.Brakes = true; f.Input.Rcs = true; f.Input.Velocity = new Vec(2, 0, 0); f.Input.Translation = new Vec(0, 1, 0); var r = f.Step(); Eq(r.Effects.ThrustKN.Length, 0); True(r.ForceN.X < 0); });
        Test("12 Vertical brake merged into support", () => { var f = Support(); f.Input.Brakes = true; f.Input.Velocity = new Vec(0, 2, 0); var r = f.Step(); Eq(r.Effects.SupportKN.Length, 8); Eq(r.Effects.BrakeKN.Length, 0); });
        Test("13 Combined controls length limited", () => { var f = new Fixture(); f.Input.Rcs = true; f.Input.Translation = new Vec(1, 1, 1); True(f.Step().ForceN.Length <= 100000.0001); });
        Test("14 RCS OFF translation and rotation ignored", () => { var f = new Fixture(); f.Input.Throttle = 0; f.Input.Translation = new Vec(1, 0, 0); f.Input.Rotation = new Vec(1, 0, 0); var r = f.Step(); Eq(r.ForceN.Length, 0); Eq(r.Effects.TorqueNm.Length, 0); });
        Test("15 Torque not a translation", () => { var f = new Fixture(); f.Input.Throttle = 0; f.Input.Rcs = true; f.Input.Rotation = new Vec(1, 0, 0); f.Settings.RotationShare = 0.5; var r = f.Step(); Eq(r.ForceN.Length, 0); True(r.Effects.TorqueNm.Length > 0); });
        Test("16 Unused rotation share stays reserved", () => { var f = new Fixture(); f.Input.Rcs = true; f.Settings.RotationShare = 0.5; Eq(f.Step().ForceN.Length, 50000); });
        Test("17 Thrust torque equal shortage scale", () => { var f = new Fixture(); f.Input.Rcs = true; f.Input.Rotation = new Vec(1, 0, 0); f.Settings.RotationShare = 0.5; f.P.Value = 25.5; var r = f.Step(); Eq(r.Effects.ThrustKN.Length / 50, r.Effects.TorqueNm.Length / 10000); Eq(r.Consumed, 25.5); });
        Test("18 Translation charges resultant norm", () => { var f = Support(); f.Input.Throttle = 1; f.Input.Forward = new Vec(1, 0, 0); var r = f.Step(); Eq(r.Consumed, Math.Sqrt(100 + 8100)); });
        Test("19 Power reservations are not vector norm", () => { var f = Support(); f.Input.Throttle = 1; f.Input.Forward = new Vec(1, 0, 0); Eq(f.Step().Effects.ReservedPower, 100); });
        Test("20 Empty tank direct same-step generation", () => { var f = new Fixture(); f.P.Value = 0; f.Drives[0].Tank = 0; f.Drives[0].Definition.Generation = true; f.Drives[0].Definition.ParticleGenerationRate = 100; var r = f.Step(); Eq(r.Consumed, 100); Eq(f.P.Takes, 0); True(!f.Drives[0].Depleted); });
        Test("21 Full tank simultaneous consumption generation", () => { var f = new Fixture(); f.Drives[0].Definition.Generation = true; f.Drives[0].Definition.ParticleGenerationRate = 100; var r = f.Step(); Eq(r.Generated, 100); Eq(f.P.Value, 1000); Eq(f.P.Takes, 0); });
        Test("22 Store actual accepted surplus", () => { var f = new Fixture(); f.Input.Throttle = 0; f.P.Value = 999; f.P.PutFraction = 0.5; f.Drives[0].Definition.Generation = true; var r = f.Step(); Eq(r.Generated, 1); Eq(r.ParticleStored, 0.5); Eq(r.ParticleSpilled, 0.5); });
        Test("23 EC partial supply scales generation", () => { var f = new Fixture(); f.P.Value = 0; f.E.TakeFraction = 0.25; f.Drives[0].Definition.Generation = true; var r = f.Step(); Eq(r.Generated, 2.5); Eq(r.Consumed, 2.5); Eq(f.E.Takes, 1); });
        Test("24 EC recovery restarts generation", () => { var f = new Fixture(); f.Input.Throttle = 0; f.P.Value = 0; f.E.Value = 0; f.Drives[0].Definition.Generation = true; Eq(f.Step().Generated, 0); f.E.Value = 100; True(f.Step().Generated > 0); });
        Test("25 Half TD balances rated generation", () => { var f = new Fixture(); f.Drives[0].Definition.Generation = f.Drives[0].Definition.TD = true; f.Drives[0].TDQuantity = 5; f.E.Value = 0; var r = f.Step(); Eq(r.ECGenerated, 10); Eq(r.Generated, 10); Eq(r.ECTaken, 0); Eq(r.ECStored, 0); });
        Test("26 Low TD still generates", () => { var f = new Fixture(); f.Drives[0].Definition.Generation = f.Drives[0].Definition.TD = true; f.Drives[0].TDQuantity = 1; f.E.Value = 0; var r = f.Step(); Eq(r.Generated, 2); });
        Test("27 Full TD excess EC", () => { var f = new Fixture(); f.Drives[0].Definition.Generation = f.Drives[0].Definition.TD = true; f.Drives[0].TDQuantity = 10; f.E.Value = 0; var r = f.Step(); Eq(r.Generated, 10); Eq(r.ECStored, 10); });
        Test("28 Power-only with full particle storage", () => { var f = new Fixture(); f.Input.Throttle = 0; f.Drives[0].Definition.Generation = f.Drives[0].Definition.TD = true; f.Drives[0].TDQuantity = 10; f.E.Value = 0; var r = f.Step(); Eq(r.Generated, 0); Eq(r.ECStored, 20); });
        Test("29 SafeGuard never takes reserve", () => { var f = new Fixture(); f.Settings.SafeGuard = true; f.Drives[0].Definition.Generation = true; var r = f.Step(); Eq(r.Consumed, 10); Eq(f.P.Takes, 0); });
        Test("30 SafeGuard zero generation no depletion or TA exit", () => { var f = new Fixture(); f.Settings.SafeGuard = true; f.Drives[0].Definition.Generation = true; f.Drives[0].Definition.ParticleGenerationRate = 0; f.Drives[0].TA = true; var r = f.Step(); Eq(r.Consumed, 0); True(!f.Drives[0].Depleted && f.Drives[0].TA); });
        Test("31 No positive local tank cannot run", () => { var f = new Fixture(); f.Drives[0].TankCapacity = 0; Eq(f.Step().ForceN.Length, 0); True(!f.Command(CommandKind.Engine, true)); });
        Test("32 Depleted restart needs own full tank", () => { var f = new Fixture(); var d = f.Drives[0]; d.Depleted = true; d.Engine = false; d.Tank = 10; True(!f.Command(CommandKind.Engine, true)); d.Tank = 1000; True(f.Command(CommandKind.Engine, true)); True(!d.Depleted); });
        Test("33 Partial supply does not latch engine off", () => { var f = new Fixture(); f.P.Value = 1; var r = f.Step(); True(r.SupplyLimited); True(f.Drives[0].Engine && !f.Drives[0].Depleted); });
        Test("34 TA multiplies Power only", () => { var f = new Fixture(); var d = f.Drives[0]; d.TA = true; d.Definition.Generation = d.Definition.TD = true; d.TDQuantity = 5; var r = f.Step(); Eq(r.ForceN.Length, 300000); Eq(r.Generated, 10); Eq(r.ECGenerated, 10); Eq(r.Effects.MaxG, 100); });
        Test("35 TA fallback uses paid budget and refunds", () => { var f = new Fixture(); f.Drives[0].TA = true; f.P.Value = 200; var r = f.Step(); True(r.TAForcedOff); Eq(f.P.Takes, 1); Eq(r.ParticleTaken, 200); Eq(r.Consumed, 100); Eq(r.ParticleStored, 100); Eq(f.P.Value, 100); });
        Test("36 Any Gen1 prevents manual TA stop", () => { var f = new Fixture(); f.Drives[0].TA = true; f.Drives.Add(Fixture.NewDrive(2)); f.Drives[1].Definition.Gen1 = true; f.Drives[1].Engine = false; True(!f.Command(CommandKind.TransAM, false)); });
        Test("37 Gen1 refill TA lock", () => { var f = new Fixture(); var d = f.Drives[0]; d.Definition.Gen1 = true; d.TA = true; d.Tank = 200; f.P.Value = 200; f.Step(); True(d.TALocked); True(!f.Command(CommandKind.TransAM, true)); d.Tank = 1000; True(f.Command(CommandKind.TransAM, true)); });
        Test("38 Repose forbidden during TA", () => { var f = new Fixture(); f.Drives[0].TA = true; True(!f.Command(CommandKind.Repose, true)); True(!f.Drives[0].Repose); });
        Test("39 Repose generates and leaves group", () => { var f = new Fixture(); f.Drives[0].Joined = true; f.Drives[0].Definition.Generation = true; f.P.Value = 0; True(f.Command(CommandKind.Repose, true)); var r = f.Step(); True(r.Generated > 0); Eq(r.ForceN.Length, 0); True(!f.Drives[0].Joined); f.Command(CommandKind.Repose, false); True(!f.Drives[0].Engine); });
        Test("40 Equal individuality n squared aggregate", () => { Eq(GNMultiDriveUnit.Coefficient(new[] { 0.5, 0.5 }) * 2, 4); Eq(GNMultiDriveUnit.Coefficient(new[] { 0.5, 0.5, 0.5 }) * 3, 9); });
        Test("41 Mixed Original Tau sample variance", () => { var f = new Fixture(); f.Drives.Add(Fixture.NewDrive(2)); foreach (var d in f.Drives) d.Joined = true; f.Drives[1].Definition.TD = true; f.Drives[1].TDQuantity = 1; f.Drives[1].Definition.Individuality = 0.6; f.Step(); Eq(f.Drives[0].Sync, 2 * Math.Pow(0.98, 2)); Eq(f.Drives[1].Sync, f.Drives[0].Sync); });
        Test("42 Atomic sync and join rejects incomplete restart", () => { var f = new Fixture(); var b = Fixture.NewDrive(2); f.Drives.Add(b); f.Drives[0].Joined = true; b.Depleted = true; b.Tank = 1; b.Engine = false; True(!f.Command(CommandKind.Join, true, 2)); True(!b.Joined && !b.Engine); b.Joined = true; f.Drives[0].Engine = false; True(!f.Command(CommandKind.Engine, true)); True(!f.Drives[0].Engine && !b.Engine); });
        Test("43 TD OFF conflict leaves only affected member", () => { var f = new Fixture(); var b = Fixture.NewDrive(2); f.Drives.Add(b); foreach (var d in f.Drives) d.Joined = true; f.Drives[0].Definition.TD = true; f.Drives[0].TDQuantity = 1; True(f.Command(CommandKind.Generation, false)); True(!f.Drives[0].Joined && f.Drives[0].Generate); True(b.Joined && !b.Generate); });
        Test("44 Field diameter squared cheapest maintenance", () => { var f = new Fixture(); f.Input.Throttle = 0; f.Input.FieldDiameter = 20; f.Settings.Field = true; f.Drives[0].Definition.Field = true; var b = Fixture.NewDrive(2); b.Definition.Field = true; b.Definition.FieldParticleRate = 3; f.Drives.Add(b); Eq(f.Step().Consumed, 4); });
        Test("45 Field opposite targets no fee cancellation", () => { var f = new Fixture(); f.Input.Throttle = 0; f.Settings.Field = true; f.Drives[0].Definition.Field = true; f.Input.Targets.Add(new FieldTarget { Id = "a", ForceN = new Vec(10000, 0, 0) }); f.Input.Targets.Add(new FieldTarget { Id = "b", ForceN = new Vec(-10000, 0, 0) }); Eq(f.Step().Consumed, 41); });
        Test("46 Inactive and packed conversion only", () => { foreach (bool packed in new[] { false, true }) { var f = new Fixture(); f.Input.Active = packed; f.Input.Packed = packed; f.Drives[0].Definition.Generation = true; f.P.Value = 0; var r = f.Step(); True(r.Generated > 0); Eq(r.ForceN.Length, 0); True(!r.Effects.Field); } });
        Test("47 Actual take shortage scales before effects", () => { var f = new Fixture(); f.P.TakeFraction = 0.1; var r = f.Step(); Eq(r.Consumed, 10); Eq(r.ForceN.Length, 10000); Eq(f.P.Takes, 1); });
        Test("48 Input order independent and duplicate ID refused", () => { var a = new Fixture(); a.Drives.Add(Fixture.NewDrive(2)); a.Drives[1].Definition.Generation = true; var b = new Fixture(); b.Drives.Add(Fixture.NewDrive(2)); b.Drives[1].Definition.Generation = true; b.Drives.Reverse(); Eq(a.Step().Consumed, b.Step().Consumed); a.Drives[1].Id = 1; Throws(() => a.Step()); });
        Test("49 Time step split consistent at constant input", () => { var a = new Fixture(); a.Drives[0].Definition.Generation = true; var ar = a.Step(); var b = new Fixture(); b.Drives[0].Definition.Generation = true; b.Input.Dt = 0.1; double sum = 0; for (int j = 0; j < 10; j++) sum += b.Step().Consumed; Eq(ar.Consumed, sum); Eq(a.P.Value, b.P.Value); Eq(a.E.Value, b.E.Value); });
        Test("50 Random resource conservation 2000 seeds", () => { var rng = new Random(1847); for (int j = 0; j < 2000; j++) { var f = new Fixture(); f.Input.Dt = 0.001 + rng.NextDouble(); f.P.Value = rng.NextDouble() * 1000; f.E.Value = rng.NextDouble() * 1000; f.P.TakeFraction = rng.NextDouble(); f.P.PutFraction = rng.NextDouble(); f.E.TakeFraction = rng.NextDouble(); f.Drives[0].Definition.Generation = true; f.Drives[0].Definition.TD = true; f.Drives[0].TDQuantity = rng.NextDouble() * 10; f.Drives[0].TA = rng.Next(2) == 0; f.Settings.SafeGuard = rng.Next(2) == 0; f.Input.Rcs = true; f.Settings.RotationShare = rng.NextDouble(); f.Input.Rotation = new Vec(rng.NextDouble(), 0, 0); double p0 = f.P.Value, e0 = f.E.Value; var r = f.Step(); Eq(f.P.Value, p0 + r.Generated - r.Consumed - r.ParticleSpilled, 1e-6); Eq(f.E.Value, e0 + r.ECGenerated - r.ECUsed - r.ECSpilled, 1e-6); True(r.Consumed <= r.Generated + r.ParticleTaken + 1e-7); True(f.P.Value >= 0 && f.P.Value <= f.P.Max); } });
        Test("51 Full-tank EC overflow recorded", () => { var f = new Fixture(); f.Input.Throttle = 0; f.Drives[0].Definition.TD = true; f.Drives[0].TDQuantity = 10; var r = f.Step(); Eq(r.ECStored, 0); Eq(r.ECSpilled, 20); });
        Test("52 Field shortage stops and requires manual ON", () => { var f = new Fixture(); f.Input.Throttle = 0; f.Settings.Field = true; f.Drives[0].Definition.Field = true; f.P.Value = 0.5; True(!f.Step().Effects.Field && !f.Settings.Field); f.P.Value = 100; True(!f.Step().Effects.Field); True(f.Command(CommandKind.Field, true)); True(f.Step().Effects.Field); });
        Test("53 Field only own capable Power contributes", () => { var f = new Fixture(); f.Input.Throttle = 0; f.Settings.Field = true; f.Drives[0].Definition.Field = true; f.Drives[0].Definition.Power = 10; f.Drives.Add(Fixture.NewDrive(2)); f.Input.Targets.Add(new FieldTarget { Id = "a", ForceN = new Vec(100000, 0, 0) }); var r = f.Step(); Eq(r.Effects.Targets[0].ForceN.Length, 5000); Eq(r.Consumed, 11); });
        Test("54 Field support consumes capable Power first", () => { var f = Support(); f.Settings.Field = true; f.Drives[0].Definition.Field = true; f.Drives[0].Definition.Power = 10; f.Drives.Add(Fixture.NewDrive(2)); f.Input.Targets.Add(new FieldTarget { Id = "a", ForceN = new Vec(100000, 0, 0) }); Eq(f.Step().Effects.Targets[0].ForceN.Length, 0); });
        Test("55 Zero demand is not depletion", () => { var f = new Fixture(); f.P.Value = 0; f.Input.Throttle = 0; f.Step(); True(!f.Drives[0].Depleted); });
        Test("56 Actual zero supply latches depletion", () => { var f = new Fixture(); f.P.Value = 0; f.Step(); True(f.Drives[0].Depleted && !f.Drives[0].Engine); });
        Test("57 Invalid dt rejected before charging", () => { var f = new Fixture(); f.Input.Dt = double.NaN; Throws(() => f.Step()); Eq(f.P.Takes, 0); Eq(f.E.Takes, 0); });
        Test("58 Single invalid individuality rejected", () => Throws(() => GNMultiDriveUnit.Coefficient(new[] { double.NaN })));
        Test("59 Hover deadband supports weight", () => Eq(HoverLaw.Support(10, 0.001, 0.02, 0.5, 100, 0.01), 10));
        Test("60 Off engine contributes no support or max G", () => { var f = Support(); f.Drives[0].Engine = false; var r = f.Step(); Eq(r.Effects.MaxG, 0); Eq(r.ForceN.Length, 0); });
        Test("61 Duplicate actions same tick scope only once", () => { var g = new CommandGate(); True(g.Accept(1, "vessel", CommandKind.Field)); True(!g.Accept(1, "vessel", CommandKind.Field)); True(g.Accept(2, "vessel", CommandKind.Field)); True(g.Accept(2, "group0", CommandKind.Field)); });
        Test("62 Overflow rejected before EC debit", () => { var f = new Fixture(); f.P.Value = 0; f.Drives[0].Definition.Generation = true; f.Drives[0].Definition.ECPerParticle = double.MaxValue; Throws(() => f.Step()); Eq(f.E.Takes, 0); Eq(f.P.Takes, 0); });
        Test("63 Engine OFF independent of Gen1 TA latch", () => { var f = new Fixture(); f.Drives[0].TA = true; f.Drives[0].Definition.Gen1 = true; True(f.Command(CommandKind.Engine, false)); True(f.Drives[0].TA); Eq(f.Step().ForceN.Length, 0); True(!f.Command(CommandKind.TransAM, false)); });
        Test("64 Ascending AG downward RCS exceeds natural braking", () => { var f = Support(); f.Input.Rcs = true; f.Input.Velocity = new Vec(0, 2, 0); f.Input.Translation = new Vec(0, -1, 0); var r = f.Step(); Eq(r.Effects.ThrustKN.Y, -90); Eq(r.ForceN.Y, -80000); });
        Test("65 Down RCS blocked stationary falling and Hover", () => { foreach (double v in new[] { 0.0, -2.0 }) { var f = Support(); f.Input.Rcs = true; f.Input.Velocity = new Vec(0, v, 0); f.Input.Translation = new Vec(0, -1, 0); Eq(f.Step().Effects.ThrustKN.Length, 0); } var h = Support(); h.Drives[0].Hover = true; h.Input.Rcs = true; h.Input.Velocity = new Vec(0, 2, 0); h.Input.Translation = new Vec(0, -1, 0); Eq(h.Step().Effects.ThrustKN.Length, 0); });
        Test("66 Field maintenance before thrust", () => { var f = new Fixture(); f.Settings.Field = true; f.Drives[0].Definition.Field = true; f.P.Value = 20; var r = f.Step(); True(r.Effects.Field); Eq(r.Effects.ThrustKN.Length, 19); Eq(r.Consumed, 20); });
        Test("67 Repulsion scales without Field stopping", () => { var f = new Fixture(); f.Input.Throttle = 0; f.Settings.Field = true; f.Drives[0].Definition.Field = true; f.Input.Targets.Add(new FieldTarget { Id = "a", ForceN = new Vec(10000,0,0) }); f.P.Value = 6; var r = f.Step(); True(r.Effects.Field && f.Settings.Field); Eq(r.Effects.Targets[0].ForceN.Length, 2500); Eq(r.Consumed, 6); True(r.FieldStatus.Contains("particle-limited")); });
        Test("68 Exact payment rounding preserves Field", () => { var p = new EffectPlan { Field = true, FieldMaintenance = 79.7713328825642, SupportKN = new Vec(0,9.52034948371367,0) }; double dt = 0.046078520800489242; var a = Allocator.Allocate(p,1000,dt); var b = Allocator.Allocate(p,a.Cost(dt),dt); True(b.Field); });
        Test("69 Support wins over Field maintenance", () => { var f = Support(); f.Settings.Field = true; f.Drives[0].Definition.Field = true; f.P.Value = 10; var r = f.Step(); Eq(r.Effects.SupportKN.Length,10); True(!r.Effects.Field); });
        Test("70 Opposing RCS preserves support reservation and Field", () => { var f = Support(); f.Settings.Field = true; f.Drives[0].Definition.Field = true; f.Input.Rcs = true; f.Input.Velocity = new Vec(0,2,0); f.Input.Translation = new Vec(0,-0.1,0); var r = f.Step(); True(r.Effects.Field); Eq(r.Effects.SupportKN.Y,10); Eq(r.Effects.ThrustKN.Y,-9); Eq(r.Consumed,2); Eq(f.P.Value,998); });
        Test("71 Generation OFF clears SafeGuard and uses stock", () => { var f = new Fixture(); f.Drives[0].Definition.Generation = true; f.Settings.SafeGuard = true; True(f.Command(CommandKind.Generation,false)); True(!f.Settings.SafeGuard); True(f.Step().ParticleTaken>0); True(f.Command(CommandKind.Generation,true)); True(!f.Settings.SafeGuard); });
        Test("72 Loaded generation OFF reconciles SafeGuard", () => { var f = new Fixture(); f.Settings.SafeGuard = true; f.Drives[0].Generate = false; f.Step(); True(!f.Settings.SafeGuard); True(!f.Command(CommandKind.SafeGuard,true)); });
        Test("73 EC shortage does not disable SafeGuard", () => { var f = new Fixture(); f.Settings.SafeGuard = true; f.Drives[0].Definition.Generation = true; f.E.Value = 0; var r=f.Step(); True(f.Settings.SafeGuard); Eq(r.Consumed,0); Eq(f.P.Takes,0); });
        Test("74 Other enabled generator retains SafeGuard", () => { var f = new Fixture(); f.Drives[0].Definition.Generation = true; var b=Fixture.NewDrive(2); b.Definition.Generation=true; f.Drives.Add(b); f.Settings.SafeGuard=true; f.Command(CommandKind.Generation,false); True(f.Settings.SafeGuard); });
        Test("75 Hover AG mutually exclusive commands", () => { var f=Support(); True(f.Command(CommandKind.Hover,true)); True(f.Drives[0].Hover && !f.Drives[0].AG); True(f.Command(CommandKind.AG,true)); True(f.Drives[0].AG && !f.Drives[0].Hover); });
        Test("76 Saved Hover AG overlap reconciles", () => { var f=Support(); f.Drives[0].Hover=true; f.Step(); True(f.Drives[0].Hover && !f.Drives[0].AG); f.Command(CommandKind.Hover,false); True(!f.Drives[0].AG); });
        Test("77 Random opposing thrust allocator conservation", () => { var rng=new Random(73); for(int j=0;j<2000;j++) { double budget=rng.NextDouble()*100; var p=new EffectPlan { SupportKN=new Vec(0,rng.NextDouble()*100,0), ThrustKN=new Vec(0,-rng.NextDouble()*200,0), TorqueRate=rng.NextDouble()*20 }; var a=Allocator.Allocate(p,budget,0.02); True(Numbers.Covers(a.Cost(0.02),budget)); } });
        Test("78 Stock pitch yaw roll torque directions through simulation", () => {
            var frames = new[] {
                new[] { new Vec(1,0,0), new Vec(0,1,0), new Vec(0,0,1) },
                new[] { new Vec(0,1,0), new Vec(0,0,1), new Vec(1,0,0) },
                new[] { new Vec(0,0,-1), new Vec(0,1,0), new Vec(1,0,0) }
            };
            foreach(var frame in frames) foreach(double sign in new[] {-1.0,1.0}) for(int axis=0;axis<3;axis++) {
                var f=new Fixture(); f.Input.Throttle=0; f.Input.Rcs=true; f.Settings.RotationShare=0.5;
                f.Input.Rotation=KspControlFrame.Rotation(frame[0],frame[1],frame[2],axis==0?sign:0,axis==1?sign:0,axis==2?sign:0);
                var expected = -frame[axis==0?0:axis==1?2:1]*sign;
                var r=f.Step(); Eq((r.Effects.TorqueNm-expected*10000).Length,0); Eq(r.ForceN.Length,0); Eq(r.Consumed,1);
            }
        });
        Test("79 Combined rotation preserves stock axis signs and cap", () => { var f=new Fixture(); f.Input.Throttle=0; f.Input.Rcs=true; f.Settings.RotationShare=0.5; f.Input.Rotation=KspControlFrame.Rotation(new Vec(1,0,0),new Vec(0,1,0),new Vec(0,0,1),1,-1,1); var r=f.Step(); Eq(r.Effects.TorqueNm.Length,10000); True(r.Effects.TorqueNm.X<0 && r.Effects.TorqueNm.Y<0 && r.Effects.TorqueNm.Z>0); f.Input.Rcs=false; Eq(f.Step().Effects.TorqueNm.Length,0); });
        Test("80 Support alone drives visual load", () => { var f=Support(); var r=f.Step(); Eq(VisualPolicy.Load(f.Input,r.Effects,100),0.1); f.Input.Throttle=0.5; Eq(VisualPolicy.Load(f.Input,r.Effects,100),0.5); });
        Test("81 RCS visual load honors switch and torque input", () => { var i=new StepInput { Throttle=0, Rotation=new Vec(0,0,0.8) }; Eq(VisualPolicy.Load(i,new EffectPlan(),0),0); i.Rcs=true; Eq(VisualPolicy.Load(i,new EffectPlan(),0),0.8); i.Translation=new Vec(2,0,0); Eq(VisualPolicy.Load(i,new EffectPlan(),0),1); });
        Test("82 TD idle rotation survives engine OFF and Repose", () => { var f=new Fixture(); var d=f.Drives[0]; d.Definition.TD=true; d.TDQuantity=1; d.Engine=false; d.Repose=true; True(GNTopologicalDefectsUnit.Rate(d)>0); Eq(VisualPolicy.Rotor(d.Running,GNTopologicalDefectsUnit.Rate(d)>0,1),0.1); d.TDQuantity=0; Eq(VisualPolicy.Rotor(d.Running,GNTopologicalDefectsUnit.Rate(d)>0,1),0); });
        Test("83 Clamp binary while core tracks load", () => { True(VisualPolicy.Open(true,false)); Eq(VisualPolicy.Core(true,false,0),0); Eq(VisualPolicy.Core(true,false,0.6),0.6); True(!VisualPolicy.Open(true,true)); True(!VisualPolicy.Open(false,false)); Eq(VisualPolicy.Core(true,true,1),0); Eq(VisualPolicy.Rotor(true,false,1),1); });
        Test("84 Only numeric mechanism suffixes match", () => { True(VisualPolicy.Named("GN_Rotor_01","GN_Rotor_")); True(!VisualPolicy.Named("GN_Rotor_01Mesh","GN_Rotor_")); True(!VisualPolicy.Named("GN_Rotor_","GN_Rotor_")); True(!VisualPolicy.Named("gn_Rotor_01","GN_Rotor_")); });
        Test("85 TA triples acceleration at normal cap without changing setting", () => { var f=new Fixture(); f.Settings.AccelerationG=1; f.Drives[0].Definition.MaxGContribution=1; var normal=f.Step(); f.Drives[0].TA=true; var boosted=f.Step(); Eq(boosted.ForceN.Length,normal.ForceN.Length*3); Eq(boosted.Effects.EffectiveAccelerationG,3); Eq(f.Settings.AccelerationG,1); f.Command(CommandKind.TransAM,false); Eq(f.Step().ForceN.Length,normal.ForceN.Length); });
        Test("86 Off or unsupported TA cannot boost acceleration", () => { var f=new Fixture(); var d=f.Drives[0]; d.TA=true; d.Engine=false; Eq(GNTransAMUnit.AccelerationMultiplier(f.Drives),1); d.Engine=true; d.Definition.TransAM=false; Eq(GNTransAMUnit.AccelerationMultiplier(f.Drives),1); });
        Test("87 TA fallback restores normal acceleration same tick", () => { var f=new Fixture(); f.Settings.AccelerationG=1; f.Drives[0].TA=true; f.P.Value=20; var r=f.Step(); True(r.TAForcedOff); Eq(r.Effects.EffectiveAccelerationG,1); Eq(r.ForceN.Length,Numbers.G*1000); Eq(f.Settings.AccelerationG,1); });
        Test("88 TA multiplier never stacks and zero stays zero", () => { var f=new Fixture(); f.Drives.Add(Fixture.NewDrive(2)); foreach(var d in f.Drives)d.TA=true; Eq(GNTransAMUnit.AccelerationMultiplier(f.Drives),3); f.Settings.AccelerationG=0; Eq(f.Step().ForceN.Length,0); });
        Test("89 Reported 31 tonne 10000 Power reaches 15G", () => { var f=new Fixture(); f.Input.MassKg=31000; f.Input.Dt=0.02; f.Settings.AccelerationG=5; var d=f.Drives[0]; d.Definition.Power=10000; d.Definition.MaxGContribution=5; Eq(f.Step().ForceN.Length/(31000*Numbers.G),5); d.TA=true; var r=f.Step(); True(!r.TAForcedOff); Eq(r.Effects.EffectiveAccelerationG,15); Eq(r.ForceN.Length/1000,4560.09225); Eq(r.ForceN.Length/(31000*Numbers.G),15); });
        Test("90 Brakes alone counter gravity at rest", () => { var f=new Fixture(); f.Input.Throttle=0; f.Input.Brakes=true; f.Input.Gravity=new Vec(0,-10,0); var r=f.Step(); Eq(r.ForceN.Y,10000); True(!f.Drives[0].AG && !f.Drives[0].Hover); });
        Test("91 Braking descent converges to zero including tiny residual", () => { foreach(bool ag in new[]{false,true}) { var f=new Fixture(); f.Input.Dt=0.02; f.Input.Throttle=0; f.Input.Brakes=true; f.Input.Gravity=new Vec(0,-10,0); f.Input.Velocity=new Vec(0,-5,0); f.Drives[0].AG=ag; f.P.Value=f.P.Max=100000; for(int j=0;j<500;j++){var r=f.Step(); f.Input.Velocity+=(r.ForceN/f.Input.MassKg+f.Input.Gravity)*f.Input.Dt;} Eq(f.Input.Velocity.Y,0,1e-10); f.Input.Velocity=new Vec(0,-0.001,0); var last=f.Step(); Eq((f.Input.Velocity+(last.ForceN/f.Input.MassKg+f.Input.Gravity)*f.Input.Dt).Y,0,1e-10); } });
        Test("92 Brakes cannot hold when acceleration budget insufficient", () => { var f=new Fixture(); f.Input.Brakes=true; f.Input.Gravity=new Vec(0,-10,0); f.Settings.AccelerationG=0.5; True(f.Step().ForceN.Y/1000<10); });
        Test("93 Shield rim transparent front opaque edge", () => { Eq(ShieldAppearance.RimAlpha(1,1),0); Eq(ShieldAppearance.RimAlpha(-1,1),0); Eq(ShieldAppearance.RimAlpha(0,1),0.9); Eq(ShieldAppearance.RimAlpha(0.5,1),0.1125); });
        Test("94 Shield fade and grazing angles stay bounded", () => { Eq(ShieldAppearance.RimAlpha(0,0),0); Eq(ShieldAppearance.RimAlpha(0,0.5),0.45); Eq(ShieldAppearance.RimAlpha(2,1),0); Eq(ShieldAppearance.RimAlpha(0,2),0.9); double last=1; for(int j=0;j<=100;j++){double a=ShieldAppearance.RimAlpha(j/100.0,1); True(a>=0 && a<=last); last=a;} });
        Test("95 Colour transition finishes in 1.5 seconds", () => { var c=new VisualColorTransition(); var green=new Vec(0,1,0.65); var pink=new Vec(1,0,0.7); Eq((c.Step(green,0)-green).Length,0); Eq((c.Step(pink,0.75)-(green+pink)*0.5).Length,0); Eq((c.Step(pink,0.75)-pink).Length,0); Eq((c.Step(pink,10)-pink).Length,0); });
        Test("96 Colour transition interruption starts from displayed colour", () => { var c=new VisualColorTransition(); var green=new Vec(0,1,0.65); var pink=new Vec(1,0,0.7); c.Step(green,0); var mid=c.Step(pink,0.5); Eq((c.Step(green,0)-mid).Length,0); Eq((c.Step(green,0.75)-(mid+green)*0.5).Length,0); Eq((c.Step(green,0.75)-green).Length,0); });
        Test("97 Repeated target messages do not reset colour fade", () => { var c=new VisualColorTransition(); c.Step(new Vec(),0); for(int j=0;j<90;j++)c.Step(new Vec(1,0,1),1.0/60); Eq((c.Step(new Vec(1,0,1),0)-new Vec(1,0,1)).Length,0); });
        Test("98 Particle load endpoints and inverse cone", () => { Eq(ParticleVisualPolicy.Blend(1,6,0),1); Eq(ParticleVisualPolicy.Blend(1,6,1),6); Eq(ParticleVisualPolicy.Blend(60,600,0.5),330); Eq(ParticleVisualPolicy.Blend(35,12,0.5),23.5); Eq(ParticleVisualPolicy.Blend(1,6,-1),1); Eq(ParticleVisualPolicy.Blend(1,6,2),6); });
        Test("99 Particle lifetime budget preserves headroom", () => { Eq(ParticleVisualPolicy.SafeRate(600,1,4096),600); Eq(ParticleVisualPolicy.SafeRate(3000,10,4096),327.68); Eq(ParticleVisualPolicy.SafeRate(0,10,4096),0); for(int j=1;j<=100;j++){double life=j*0.1; True(ParticleVisualPolicy.SafeRate(3000,life,4096)*life<=4096*0.8+1e-9);} });
        Test("100 Condenser fill clamps and handles missing capacity", () => { Eq(VisualPolicy.CondenserLevel(1000,1000),1); Eq(VisualPolicy.CondenserLevel(500,1000),0.5); Eq(VisualPolicy.CondenserLevel(0,1000),0); Eq(VisualPolicy.CondenserLevel(1000,0),0); Eq(VisualPolicy.CondenserLevel(2000,1000),1); Eq(VisualPolicy.CondenserLevel(double.NaN,1000),0); True(!VisualPolicy.Named("GN_CondenserRotor_00","GN_Rotor_")); True(VisualPolicy.Named("GN_CondenserRotor_00","GN_CondenserRotor_")); });
        Test("101 Stopped and full Tau generators retain inherited colour", () => { var f=new Fixture(); var d=f.Drives[0]; d.Definition.Generation=true; d.Definition.TD=false; d.Generate=false; f.Input.Throttle=0; f.Step(); Eq(d.Generated,0); Eq((DriveColorPolicy.SharedNormal(f.Drives)-new Vec(1,0,0.15)).Length,0); d.Generate=true; f.P.Value=f.P.Max; f.Step(); Eq(d.Generated,0); Eq((DriveColorPolicy.SharedNormal(f.Drives)-new Vec(1,0,0.15)).Length,0); d.Definition.Generation=false; Eq((DriveColorPolicy.SharedNormal(f.Drives)-new Vec(0,1,0.65)).Length,0); });
        Test("102 Particle backflow has constant opposite acceleration", () => { var a=ParticleVisualPolicy.BackflowAcceleration(new Vec(3,4,0),20); Eq(a.X,-12); Eq(a.Y,-16); Eq(a.Z,0); Eq(a.Length,20); True(Vec.Dot(a,new Vec(3,4,0))<0); Eq(ParticleVisualPolicy.BackflowAcceleration(new Vec(),20).Length,0); Eq(ParticleVisualPolicy.BackflowAcceleration(new Vec(1,0,0),0).Length,0); Eq(ParticleVisualPolicy.BackflowAcceleration(new Vec(double.NaN,0,0),20).Length,0); });
        Test("103 Particle backflow scales from idle to full load", () => { Eq(ParticleVisualPolicy.BackflowFactor(0),0); Eq(ParticleVisualPolicy.BackflowFactor(0.25),0.25); Eq(ParticleVisualPolicy.BackflowFactor(1),1); Eq(ParticleVisualPolicy.BackflowFactor(-1),0); Eq(ParticleVisualPolicy.BackflowFactor(2),1); Eq(ParticleVisualPolicy.BackflowFactor(double.NaN),0); });
        Test("104 Random full allocator conserves particle budget", () => { var rng=new Random(104); for(int j=0;j<100000;j++) { Func<double> n=()=>rng.NextDouble()*2e6-1e6; double budget=rng.NextDouble()*1e5, dt=0.001+rng.NextDouble()*0.1; var p=new EffectPlan { SupportKN=new Vec(n(),n(),n()), BrakeKN=new Vec(n(),n(),n()), ThrustKN=new Vec(n(),n(),n()), TorqueRate=rng.NextDouble()*1e5, Field=rng.Next(2)==0, FieldMaintenance=rng.NextDouble()*1e5, FieldRepulsionRate=rng.NextDouble()*1e5 }; var a=Allocator.Allocate(p,budget,dt); if(!Numbers.Covers(a.Cost(dt),budget)) throw new Exception("case="+j+" cost="+a.Cost(dt).ToString("R")+" budget="+budget.ToString("R")); } });
        Console.WriteLine("RESULT " + passed + " passed, " + failed + " failed");
        Environment.ExitCode = failed == 0 ? 0 : 1;
    }
}
