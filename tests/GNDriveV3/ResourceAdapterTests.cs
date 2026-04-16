// Contract doubles, NOT an implementation of KSP's resource routing algorithm.
// GN_ADAPTER_TEST compiles the actual KspResourcePort from GNSystems.cs against these types.
using System;
using System.Collections.Generic;
using GNSystems;
using GNSystems.Core;

public enum ResourceFlowMode { ALL_VESSEL, STAGE_PRIORITY_FLOW, NO_FLOW }
public class PartResourceDefinition { public int id; public ResourceFlowMode resourceFlowMode; }
public class PartResourceLibrary
{
    public static readonly PartResourceLibrary Instance = new PartResourceLibrary();
    public readonly Dictionary<string, PartResourceDefinition> Definitions = new Dictionary<string, PartResourceDefinition>();
    public PartResourceDefinition GetDefinition(string name)
    {
        PartResourceDefinition value;
        return Definitions.TryGetValue(name, out value) ? value : null;
    }
}
public class PartResource
{
    public enum FlowMode { None, Out, In, Both }
    public Part part;
    public bool flowState = true;
    public FlowMode flowMode = FlowMode.Both;
    public double amount, maxAmount;
}
public class ResourceCollection : Dictionary<string, PartResource>
{
    public PartResource Get(string name)
    {
        PartResource value;
        return TryGetValue(name, out value) ? value : null;
    }
}
public class Part
{
    public uint persistentId;
    public readonly ResourceCollection Resources = new ResourceCollection();
    public double NativeAmount = 100, NativeCapacity = 100, TakeLimit = double.MaxValue, PutLimit = double.MaxValue;
    public double LastDemand;
    public int Requests, Totals, LastId;
    public bool LastPulling;
    public ResourceFlowMode LastMode;
    public double RequestResource(int resourceId, double demand, ResourceFlowMode flowMode)
    {
        Requests++; LastId = resourceId; LastDemand = demand; LastMode = flowMode;
        double actual = demand >= 0 ? Math.Min(demand, Math.Min(NativeAmount, TakeLimit))
            : -Math.Min(-demand, Math.Min(NativeCapacity - NativeAmount, PutLimit));
        NativeAmount -= actual;
        return actual;
    }
    public void GetConnectedResourceTotals(int resourceId, ResourceFlowMode flowMode, out double amount, out double maxAmount, bool pulling)
    {
        Totals++; LastId = resourceId; LastMode = flowMode; LastPulling = pulling;
        amount = NativeAmount; maxAmount = NativeCapacity;
    }
}
public class Vessel { public Part rootPart; public readonly List<Part> parts = new List<Part>(); }

class ResourceAdapterTests
{
    static int passed, failed;
    static void Equal(double actual, double expected)
    { if (!Numbers.Finite(actual) || Math.Abs(actual - expected) > 1e-8) throw new Exception(actual + " != " + expected); }
    static void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
    static void Test(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.Message); }
    }
    static Vessel NewVessel()
    {
        PartResourceLibrary.Instance.Definitions.Clear();
        PartResourceLibrary.Instance.Definitions["ElectricCharge"] = new PartResourceDefinition { id = 7, resourceFlowMode = ResourceFlowMode.STAGE_PRIORITY_FLOW };
        PartResourceLibrary.Instance.Definitions["GNparticle"] = new PartResourceDefinition { id = 8, resourceFlowMode = ResourceFlowMode.ALL_VESSEL };
        var v = new Vessel { rootPart = new Part { persistentId = 1 } };
        v.parts.Add(v.rootPart);
        Add(v.rootPart, "GNparticle", 0, 100);
        return v;
    }
    static PartResource Add(Part p, string name, double amount, double capacity)
    {
        var r = new PartResource { part = p, amount = amount, maxAmount = capacity };
        p.Resources[name] = r; return r;
    }
    static void Main()
    {
        Test("EC stage priority constructor accepted", () => { var v = NewVessel(); new KspResourcePort(v, "ElectricCharge"); Equal(v.rootPart.Requests, 0); });
        Test("EC totals use native mode and direction", () =>
        {
            var v = NewVessel(); v.rootPart.NativeAmount = 40;
            var port = new KspResourcePort(v, "ElectricCharge");
            Equal(port.Amount, 40); Check(v.rootPart.LastPulling);
            Equal(port.Capacity, 100); Check(!v.rootPart.LastPulling);
            Equal(port.FreeSpace, 60); Check(!v.rootPart.LastPulling);
            Equal(v.rootPart.LastId, 7); Check(v.rootPart.LastMode == ResourceFlowMode.STAGE_PRIORITY_FLOW); Equal(v.rootPart.Requests, 0);
        });
        Test("EC take delegates once and returns actual amount", () =>
        {
            var v = NewVessel(); v.rootPart.TakeLimit = 3;
            var sentinel = Add(v.rootPart, "ElectricCharge", 99, 100);
            var port = new KspResourcePort(v, "ElectricCharge");
            Equal(port.Take(20), 3); Equal(v.rootPart.Requests, 1); Equal(v.rootPart.LastDemand, 20);
            Check(v.rootPart.LastMode == ResourceFlowMode.STAGE_PRIORITY_FLOW); Equal(sentinel.amount, 99);
        });
        Test("EC put uses negative demand and positive receipt", () =>
        {
            var v = NewVessel(); v.rootPart.NativeAmount = 95; v.rootPart.PutLimit = 2;
            var port = new KspResourcePort(v, "ElectricCharge");
            Equal(port.Put(20), 2); Equal(v.rootPart.LastDemand, -20); Equal(v.rootPart.Requests, 1); Equal(v.rootPart.NativeAmount, 97);
        });
        Test("Full EC storage returns zero", () => { var v = NewVessel(); Equal(new KspResourcePort(v, "ElectricCharge").Put(10), 0); });
        Test("Zero and invalid requests do not call KSP", () =>
        {
            var v = NewVessel(); var port = new KspResourcePort(v, "ElectricCharge");
            Equal(port.Take(0), 0); Equal(port.Put(0), 0);
            try { port.Take(double.NaN); throw new Exception("NaN accepted"); } catch (ArgumentOutOfRangeException) { }
            try { port.Put(-1); throw new Exception("Negative accepted"); } catch (ArgumentOutOfRangeException) { }
            Equal(v.rootPart.Requests, 0);
        });
        Test("ALL_VESSEL still respects locked and directional tanks", () =>
        {
            var v = NewVessel(); var both = v.rootPart.Resources.Get("GNparticle"); both.amount = 10;
            var p = new Part { persistentId = 2 }; v.parts.Add(p); var locked = Add(p, "GNparticle", 50, 100); locked.flowState = false;
            var p3 = new Part { persistentId = 3 }; v.parts.Add(p3); var input = Add(p3, "GNparticle", 40, 100); input.flowMode = PartResource.FlowMode.In;
            var port = new KspResourcePort(v, "GNparticle"); Equal(port.Amount, 10); Equal(port.Take(20), 10);
            Equal(locked.amount, 50); Equal(input.amount, 40); Equal(port.Put(150), 150); Equal(both.amount, 100); Equal(input.amount, 90);
            Equal(v.rootPart.Requests, 0);
        });
        Test("Unsupported flow still rejected", () =>
        {
            var v = NewVessel(); PartResourceLibrary.Instance.Definitions["ElectricCharge"].resourceFlowMode = ResourceFlowMode.NO_FLOW;
            try { new KspResourcePort(v, "ElectricCharge"); throw new Exception("Unsupported flow accepted"); } catch (NotSupportedException) { }
        });
        Test("Native partial EC drives same-step particle scaling", () =>
        {
            var v = NewVessel(); v.rootPart.TakeLimit = 5;
            var d = new DriveState { Id = 1, Engine = true, TankCapacity = 100, Definition = new DriveDefinition { Propulsion = true, Generation = true, ParticleGenerationRate = 100 } };
            var r = new VesselSimulation().Step(new[] { d }, new VesselSettings { SafeGuard = true },
                new StepInput { Dt = 1, MassKg = 1000, Throttle = 1 }, new KspResourcePort(v, "GNparticle"), new KspResourcePort(v, "ElectricCharge"));
            Equal(r.ECTaken, 5); Equal(r.Generated, 5); Equal(r.Consumed, 5); Equal(v.rootPart.Requests, 1); Equal(r.ParticleTaken, 0);
        });
        Test("TD surplus uses native actual EC acceptance", () =>
        {
            var v = NewVessel(); v.rootPart.NativeAmount = 99;
            var d = new DriveState { Id = 1, TDQuantity = 5, Definition = new DriveDefinition { TD = true, ECPerTDSecond = 2 } };
            var r = new VesselSimulation().Step(new[] { d }, new VesselSettings(), new StepInput { Dt = 1, MassKg = 1000 },
                new KspResourcePort(v, "GNparticle"), new KspResourcePort(v, "ElectricCharge"));
            Equal(r.ECGenerated, 10); Equal(r.ECStored, 1); Equal(r.ECSpilled, 9); Equal(v.rootPart.LastDemand, -10); Equal(v.rootPart.Requests, 1);
        });
        Test("ALL_VESSEL capacity describes receivers only", () =>
        {
            var v = NewVessel(); var resource = v.rootPart.Resources.Get("GNparticle");
            resource.amount = 50; resource.flowMode = PartResource.FlowMode.Out;
            var port = new KspResourcePort(v, "GNparticle");
            Equal(port.Amount, 50); Equal(port.Capacity, 0); Equal(port.FreeSpace, 0); Equal(port.Take(10), 10);
        });
        Console.WriteLine("ADAPTER RESULT " + passed + " passed, " + failed + " failed");
        Environment.ExitCode = failed == 0 ? 0 : 1;
    }
}
