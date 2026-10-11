// GN Drive V3 0.2.0-alpha. Core units: kg, m, s, N, N.m; budgets: kN, particles.
// Define GN_CORE_ONLY for a build without Unity/KSP.
using System;
using System.Collections.Generic;
using System.Linq;
namespace GNSystems.Core
{
    public static class Numbers
    {
        public const double G = 9.80665, Epsilon = 1e-9;
        public static bool Finite(double x)
        {
            return !double.IsNaN(x) && !double.IsInfinity(x);
        }
        public static double Clamp(double x, double lo, double hi)
        {
            return Math.Max(lo, Math.Min(hi, x));
        }
        public static double Nonnegative(double x, string name)
        {
            if (!Finite(x) || x < 0)
                throw new ArgumentOutOfRangeException(name);
            return x;
        }
        public static double Positive(double x, string name)
        {
            Nonnegative(x, name);
            if (x == 0)
                throw new ArgumentOutOfRangeException(name);
            return x;
        }
        // Relative floating-point tolerance only; a zero budget never funds a positive cost.
        public static bool Covers(double cost, double budget)
        {
            return cost <= budget || (budget > 0 && cost - budget <=
                16 * 2.2204460492503131e-16 * Math.Max(Math.Abs(cost), Math.Abs(budget)));
        }
    }
    public struct Vec
    {
        public readonly double X, Y, Z;
        public Vec(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }
        public bool IsFinite
        {
            get
            {
                return Numbers.Finite(X) && Numbers.Finite(Y) && Numbers.Finite(Z);
            }
        }
        public double Length
        {
            get
            {
                return Math.Sqrt(X * X + Y * Y + Z * Z);
            }
        }
        public Vec Unit
        {
            get
            {
                double n = Length;
                return n > Numbers.Epsilon ? this / n : new Vec();
            }
        }
        public Vec Limit(double n)
        {
            double l = Length;
            return l > n && l > 0 ? this * (n / l) : this;
        }
        public static double Dot(Vec a, Vec b)
        {
            return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        }
        public static Vec operator +(Vec a, Vec b)
        {
            return new Vec(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        }
        public static Vec operator -(Vec a, Vec b)
        {
            return new Vec(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        }
        public static Vec operator -(Vec a)
        {
            return a * -1;
        }
        public static Vec operator *(Vec a, double b)
        {
            return new Vec(a.X * b, a.Y * b, a.Z * b);
        }
        public static Vec operator /(Vec a, double b)
        {
            return a * (1 / b);
        }
    }
    public sealed class DriveDefinition
    {
        public bool Propulsion, Generation, TD, TDBlanket, TransAM, MultiDrive, Field, Gen1;
        public double BlanketECGenerationRate = 200;
        public double Power = 100, MaxGContribution = 5, ParticleGenerationRate = 10, ECPerParticle = 10, ECPerTDSecond = 2, TransAMRatio = 3;
        public double MaxTorqueNm = 10000, ParticlePerNmSecond = 0.0001, FieldReferenceDiameter = 10, FieldParticleRate = 1, Individuality = 0.5;
        public DriveDefinition Copy()
        {
            return (DriveDefinition)MemberwiseClone();
        }
        public void Validate()
        {
            foreach (double v in new[] { Power, MaxGContribution, ParticleGenerationRate, BlanketECGenerationRate, ECPerTDSecond, MaxTorqueNm, FieldParticleRate, Individuality })
                Numbers.Nonnegative(v, "rating");
            Numbers.Positive(ECPerParticle, "ECPerParticle");
            Numbers.Positive(ParticlePerNmSecond, "ParticlePerNmSecond");
            Numbers.Positive(FieldReferenceDiameter, "FieldReferenceDiameter");
            Numbers.Positive(TransAMRatio, "TransAMRatio");
            if (TransAMRatio < 1 || Individuality > 1)
                throw new ArgumentException("Invalid TA ratio or individuality");
        }
    }
    public sealed class DriveState
    {
        public uint Id; public DriveDefinition Definition = new DriveDefinition();
        public bool Engine, AG, Hover, TA, Generate = true, Blanket, BlanketStarved, Joined, Repose, Depleted, TALocked;
        public int Group;
        public double Tank, TankCapacity, TDQuantity, TDCapacity, InternalEC, InternalParticles, Sync = 1, Generated, BlanketECGenerated, BaryonMassKg;
        public bool Original
        {
            get
            {
                return Definition.TD && TDQuantity > 0;
            }
        }
        public bool UsesTDBlanket
        {
            get
            {
                return Definition.TDBlanket && Definition.TD;
            }
        }
        public double TDFraction
        {
            get
            {
                return UsesTDBlanket ? TDResourceFraction : 0;
            }
        }
        public double TDResourceFraction
        {
            get
            {
                if (!Definition.TD || TDCapacity <= 0 || TDQuantity <= 0)
                    return 0;
                return Numbers.Clamp(TDQuantity / TDCapacity, 0, 1);
            }
        }
        public bool Full
        {
            get
            {
                return TankCapacity > 0 && Tank >= TankCapacity - Math.Max(1e-9, TankCapacity * 1e-8);
            }
        }
        public bool Running
        {
            get
            {
                return Definition.Propulsion && Engine && !Repose && !Depleted && TankCapacity > 0;
            }
        }
    }
    public sealed class VesselSettings
    {
        public double ECMargin = 0.2;
        public bool SafeGuard = true, Field;
        public double AccelerationG = 5, RotationShare = 0.2, HoverTime = 0.5, BrakeTime = 0.5, Deadband = 0.02;
    }
    public static class ECReservePolicy
    {
        public static double Draw(double demand, double stored, double capacity, double margin)
        {
            // Zero explicitly restores legacy unrestricted generation.
            if (margin <= 0) return Math.Min(demand, stored);
            double available = Math.Max(0, stored - capacity * margin);
            double range = capacity * (1 - margin);
            double factor = range > 0 ? Numbers.Clamp(available / range, 0, 1) : 0;
            return Math.Min(available, demand * factor);
        }
    }
    public static class GNMultiDriveUnit
    {
        public static bool Unsynchronized(DriveState d)
        {
            return d.Joined && !d.Repose && d.Definition.MultiDrive && d.Group == 0 && d.Sync <= 0.5;
        }
        public static double Coefficient(IList<double> values)
        {
            if (values == null)
                throw new ArgumentNullException("values");
            double[] sorted = values.OrderBy(x => x).ToArray();
            foreach (double x in sorted)
                if (!Numbers.Finite(x) || x < 0 || x > 1)
                    throw new ArgumentOutOfRangeException("individuality");
            if (sorted.Length < 2)
                return 1;
            // Qwen-generated two-pass sample variance, adapted for .NET Framework 4.7.2.
            double total = 0;
            foreach (double x in sorted)
                total += x;
            double mean = total / sorted.Length, sum = 0;
            foreach (double x in sorted)
                sum += (x - mean) * (x - mean);
            return sorted.Length * Math.Pow(1 - Numbers.Clamp(4 * sum / (sorted.Length - 1), 0, 1), sorted.Length);
        }
        public static void Update(IList<DriveState> drives)
        {
            var members = drives.Where(x => x.Joined && !x.Repose && x.Definition.MultiDrive && x.Group == 0).ToList();
            double factor = Coefficient(members.Select(x => x.Definition.Individuality).ToArray());
            foreach (var d in drives)
                d.Sync = members.Contains(d) ? factor : 1;
        }
    }
    public static class GNIndividuality
    {
        public const uint BucketCount = 1024;
        public static double FromPersistentId(uint id)
        {
            // Avalanche adjacent KSP persistent IDs, then quantize to an exactly
            // representable binary fraction. Collisions remain possible by design.
            unchecked
            {
                uint x = id + 0x9E3779B9u;
                x ^= x >> 16;
                x *= 0x85EBCA6Bu;
                x ^= x >> 13;
                x *= 0xC2B2AE35u;
                x ^= x >> 16;
                return (x & (BucketCount - 1)) / (double)BucketCount;
            }
        }
    }
    public static class HoverLaw
    {
        public static double Support(double gravity, double verticalSpeed, double dt, double responseTime, double accelerationLimit, double deadband)
        {
            Numbers.Nonnegative(gravity, "gravity");
            Numbers.Positive(dt, "dt");
            Numbers.Nonnegative(responseTime, "responseTime");
            Numbers.Nonnegative(accelerationLimit, "accelerationLimit");
            Numbers.Nonnegative(deadband, "deadband");
            if (!Numbers.Finite(verticalSpeed))
                throw new ArgumentOutOfRangeException("verticalSpeed");
            if (gravity == 0)
                return 0;
            double v = Math.Abs(verticalSpeed) <= deadband ? 0 : verticalSpeed;
            return Math.Max(0, gravity + Numbers.Clamp(-v / Math.Max(dt, responseTime), -accelerationLimit, accelerationLimit));
        }
    }
    public enum CommandKind
    {
        Engine, AG, Hover, TransAM, Generation, Blanket, Join, Repose, SafeGuard, Field
    }
    public sealed class CommandGate
    {
        readonly Dictionary<string, double> ticks = new Dictionary<string, double>();
        public bool Accept(double tick, string scope, CommandKind kind)
        {
            string key = kind + ":" + scope;
            double previous;
            if (ticks.TryGetValue(key, out previous) && previous == tick) return false;
            ticks[key] = tick;
            return true;
        }
    }
    public static class StateRules
    {
        public static bool GenerationRequested(IEnumerable<DriveState> drives)
        {
            return drives.Any(d => d.Definition.Generation && d.Generate);
        }
        public static void Reconcile(IList<DriveState> drives, VesselSettings settings)
        {
            foreach (var d in drives)
            {
                if (d.Hover) d.AG = false;
                d.Blanket = d.UsesTDBlanket;
            }
            if (!GenerationRequested(drives)) settings.SafeGuard = false;
        }
    }
    public static class Commands
    {
        static string Check(DriveState d, CommandKind kind, bool value, bool gen1)
        {
            if (kind == CommandKind.Engine && value && (!d.Definition.Propulsion || d.Repose || d.TankCapacity <= 0 || (d.Depleted && !d.Full)))
                return "Cannot start: depleted drive requires its own full tank.";
            if ((kind == CommandKind.AG || kind == CommandKind.Hover) && value && (d.Repose || !d.Definition.Propulsion))
                return "Support unavailable.";
            if (kind == CommandKind.TransAM)
            {
                if (!value && d.TA && gen1)
                    return "Gen1 aboard: manual Trans-AM stop refused.";
                if (value && (!d.Definition.TransAM || !d.Running || (d.TALocked && !d.Full)))
                    return "Trans-AM unavailable.";
            }
            if (kind == CommandKind.Repose && value && d.TA)
                return "Cannot repose during Trans-AM.";
            if (kind == CommandKind.Blanket)
                return "TD Blanket is automatic.";
            return null;
        }
        static void Set(DriveState d, CommandKind k, bool v)
        {
            switch (k)
            {
                case CommandKind.Engine:
                    d.Engine = v;
                    if (v)
                        d.Depleted = false;
                    // Propulsion OFF does not implicitly cancel Trans-AM (including Gen1).
                    break;
                case CommandKind.AG:
                    d.AG = v;
                    if (v) d.Hover = false;
                    break;
                case CommandKind.Hover:
                    d.Hover = v;
                    if (v) d.AG = false;
                    break;
                case CommandKind.TransAM:
                    d.TA = v;
                    if (v)
                        d.TALocked = false;
                    break;
                case CommandKind.Generation:
                    d.Generate = v;
                    break;
                case CommandKind.Blanket:
                    break;
                case CommandKind.Repose:
                    d.Repose = v;
                    if (v)
                    {
                        d.Joined = false;
                        d.Engine = d.AG = d.Hover = d.TA = false;
                    }
                    break;
            }
        }
        public static bool Apply(IList<DriveState> drives, VesselSettings settings, uint id, CommandKind kind, bool value, out string reason)
        {
            reason = null;
            if (kind == CommandKind.SafeGuard)
            {
                if (value && !StateRules.GenerationRequested(drives))
                {
                    settings.SafeGuard = false;
                    reason = "SafeGuard unavailable: all particle generation is OFF.";
                    return false;
                }
                settings.SafeGuard = value;
                return true;
            }
            if (kind == CommandKind.Field)
            {
                settings.Field = value;
                return true;
            }
            var d = drives.FirstOrDefault(x => x.Id == id);
            if (d == null)
            {
                reason = "Drive no longer present.";
                return false;
            }
            bool gen1 = drives.Any(x => x.Definition.Gen1);
            if (kind == CommandKind.Join)
            {
                if (!value)
                {
                    d.Joined = false;
                    return true;
                }
                if (d.Repose || !d.Definition.MultiDrive)
                {
                    reason = "Cannot join.";
                    return false;
                }
                var leader = drives.Where(x => x != d && x.Joined && !x.Repose && x.Group == 0).OrderBy(x => x.Id).FirstOrDefault();
                if (leader != null)
                {
                    var kinds = new[] { CommandKind.Engine, CommandKind.AG, CommandKind.Hover, CommandKind.TransAM, CommandKind.Generation };
                    var values = new[] { leader.Engine, leader.AG, leader.Hover, leader.TA, leader.Generate };
                    bool oldEngine = d.Engine, oldDepleted = d.Depleted;
                    for (int i = 0; i < kinds.Length; i++)
                    {
                        if (i == 3 && leader.Engine)
                        {
                            d.Engine = true;
                            d.Depleted = false;
                        }
                        reason = Check(d, kinds[i], values[i], gen1);
                        d.Engine = oldEngine;
                        d.Depleted = oldDepleted;
                        if (reason != null)
                            return false;
                    }
                    for (int i = 0; i < kinds.Length; i++)
                        Set(d, kinds[i], values[i]);
                }
                d.Joined = true;
                d.Group = 0;
                StateRules.Reconcile(drives, settings);
                return true;
            }
            var targets = d.Joined && kind != CommandKind.Repose ? drives.Where(x => x.Joined && x.Group == d.Group && !x.Repose).ToList() : new List<DriveState> { d };
            foreach (var t in targets)
            {
                reason = Check(t, kind, value, gen1);
                if (reason != null)
                    return false;
            }
            foreach (var t in targets)
                Set(t, kind, value);
            StateRules.Reconcile(drives, settings);
            return true;
        }
    }
    public interface IResourcePort
    {
        double Amount
        {
            get;
        }
        double Capacity
        {
            get;
        }
        double FreeSpace
        {
            get;
        }
        double Take(double requested); double Put(double offered);
    }
    public interface IBaryonPort
    {
        double TakeEnergy(double requestedEC);
    }
    public static class MassEnergy
    {
        public const double JoulesPerEC = 1000;
        public const double SpeedOfLight = 299792458;
        public static double KilogramsForEC(double ec)
        {
            Numbers.Nonnegative(ec, "ec");
            return ec * JoulesPerEC / (SpeedOfLight * SpeedOfLight);
        }
        // KSP density is metric tonnes per resource unit. The factors of 1000
        // for tonnes->kg and joules->EC cancel exactly at 1 EC = 1 kJ.
        public static double ECPerResourceUnit(double densityTonnes)
        {
            Numbers.Positive(densityTonnes, "density");
            return densityTonnes * SpeedOfLight * SpeedOfLight;
        }
    }
    public sealed class StepInput
    {
        public double Dt, MassKg, FieldDiameter = 10, Throttle; public bool Active = true, Packed, Brakes, Rcs;
        public Vec Gravity, Velocity, Forward = new Vec(0, 1, 0), Translation, Rotation;
        public readonly List<FieldTarget> Targets = new List<FieldTarget>();
    }
    public sealed class FieldTarget
    {
        public string Id; public Vec ForceN;
    }
    public sealed class EffectPlan
    {
        public Vec SupportKN, BrakeKN, ThrustKN, TorqueNm;
        public double TorqueRate, FieldMaintenance, FieldRepulsionRate, FieldRequestedRate, ReservedPower, MaxG, ConfigurationMaxG, EffectiveAccelerationG;
        public bool Field; public readonly List<FieldTarget> Targets = new List<FieldTarget>();
        public Vec ForceKN
        {
            get
            {
                return SupportKN + BrakeKN + ThrustKN;
            }
        }
        public double Cost(double dt)
        {
            return ForceKN.Length * dt + TorqueRate * dt + (Field ? FieldMaintenance * dt + FieldRepulsionRate * dt : 0);
        }
    }
    public sealed class StepResult
    {
        public EffectPlan Effects = new EffectPlan();
        public double Generated, ECGenerated, BlanketECGenerated, BaryonMassKg, ECTaken, ECUsed, ECStored, ECSpilled, ParticleTaken, Consumed, ParticleStored, ParticleSpilled;
        public bool SupplyLimited, TAForcedOff, BlanketStarved;
        public string FieldStatus;
        public Vec ForceN
        {
            get
            {
                return Effects.ForceKN * 1000;
            }
        }
    }
    public static class GNPropulsionUnit
    {
        public static double Power(DriveState d)
        {
            return d.Running ? d.Definition.Power * d.Sync * (d.TA && d.Definition.TransAM ? d.Definition.TransAMRatio : 1) : 0;
        }
    }
    public static class GNParticleGenerationUnit
    {
        public static double Rate(DriveState d)
        {
            if (!d.Definition.Generation || !d.Generate)
                return 0;
            return d.Definition.ParticleGenerationRate * d.Sync;
        }
    }
    public static class GNTransAMUnit
    {
        public static double AccelerationMultiplier(IEnumerable<DriveState> drives)
        {
            return drives.Any(d => d.Running && d.TA && d.Definition.TransAM) ? 3 : 1;
        }
        public static void ForceOff(IEnumerable<DriveState> drives)
        {
            foreach (var d in drives.Where(x => x.TA))
            {
                d.TA = false;
                if (d.Definition.Gen1)
                    d.TALocked = true;
            }
        }
    }
    public static class GNFieldUnit
    {
        public static double Maintenance(DriveDefinition d, double diameter)
        {
            return d.FieldParticleRate * Math.Pow(diameter / d.FieldReferenceDiameter, 2);
        }
        public static Vec Repulsion(Vec offset, Vec relativeVelocity, double penetration, double otherMassKg, double spring = 2, double damping = 1, double maxAcceleration = 20)
        {
            if (penetration <= 0)
                return new Vec();
            Vec normal = offset.Length > Numbers.Epsilon ? offset.Unit : new Vec(1, 0, 0);
            return normal * (otherMassKg * Numbers.Clamp(spring * penetration - damping * Vec.Dot(relativeVelocity, normal), 0, maxAcceleration));
        }
    }



public class VisualColorTransition
{
    private Vec _current;
    private Vec _target;
    private Vec _start;
    private double _elapsed;
    private readonly double _duration = 1.5;
    private bool _initialized;

    public Vec Step(Vec target, double dt)
    {
        if (dt < 0) dt = 0;

        if (!_initialized)
        {
            _current = target;
            _target = target;
            _start = _current;
            _elapsed = 0;
            _initialized = true;
            return _current;
        }

        double diff = (target - _target).Length;
        if (diff > 1e-9)
        {
            _start = _current;
            _target = target;
            _elapsed = 0;
        }

        _elapsed += dt;
        double progress = _elapsed / _duration;
        if (progress > 1) progress = 1;
        if (progress < 0) progress = 0;

        _current = _start + (_target - _start) * progress;
        return _current;
    }
}

    public static class DriveColorPolicy
    {
        static readonly Vec OriginalGreen = new Vec(0, 1, 0.65);
        static readonly Vec DefaultGeneratorRed = new Vec(1, 0, 0.15);
        public static Vec TDGradient(double fraction, Vec zeroTDColor)
        {
            double t = Numbers.Clamp(fraction, 0, 1);
            return zeroTDColor + (OriginalGreen - zeroTDColor) * t;
        }
        public static Vec NormalFor(DriveState drive, Vec shared)
        {
            return NormalFor(drive, shared, DefaultGeneratorRed);
        }
        public static Vec NormalFor(DriveState drive, Vec shared, Vec zeroTDColor)
        {
            if (drive == null || !drive.Definition.Generation)
                return shared;
            return drive.UsesTDBlanket && drive.TDResourceFraction > 0 ? OriginalGreen : zeroTDColor;
        }
        public static Vec SharedNormal(IEnumerable<DriveState> drives)
        {
            // Colour provenance follows installed capability, including stopped/full generators.
            bool red = false, green = false;
            foreach (var d in drives)
            {
                if (d == null || !d.Definition.Generation) continue;
                if (d.Original) green = true; else red = true;
            }
            return red ? (green ? new Vec(1, 1, 0) : new Vec(1, 0, 0.15)) : new Vec(0, 1, 0.65);
        }
        public static Vec ParseRgb(string raw)
        {
            if (raw == null) throw new FormatException("GeneratorColor must be R,G,B in 0..1.");
            var parts = raw.Split(',');
            if (parts.Length != 3) throw new FormatException("GeneratorColor must have three components.");
            double[] v = new double[3];
            for (int i = 0; i < 3; i++)
                if (!double.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i]) || !Numbers.Finite(v[i]) || v[i] < 0 || v[i] > 1)
                    throw new FormatException("GeneratorColor components must be finite values in 0..1.");
            return new Vec(v[0], v[1], v[2]);
        }
    }
    public static class AfterimagePolicy
    {
        public static double Advance(double elapsed, double duration, double dt, bool accelerating)
        {
            return Numbers.Clamp(elapsed + (accelerating ? 1 : -1) * Math.Max(0, dt), 0, Math.Max(0.01, duration));
        }
        public static double Extension(double elapsed, double duration)
        {
            double t = Numbers.Clamp(elapsed / Math.Max(0.01, duration), 0, 1);
            return t * t * (3 - 2 * t);
        }
        public static double Distance(double diameter, double divisor, double spacing, int index)
        {
            return Math.Max(0, diameter) / Math.Max(1, divisor) * Math.Max(0, spacing) * (index + 1);
        }
    }
    public static class ParticleVisualPolicy
    {
        public static double Blend(double low, double high, double load)
        { return low + (high - low) * Numbers.Clamp(load, 0, 1); }
        public static double SafeRate(double rate, double lifetime, int capacity)
        { return Math.Min(Math.Max(0, rate), Math.Max(0, capacity) * 0.8 / Math.Max(0.05, lifetime)); }
        public static Vec BackflowAcceleration(Vec appliedForce, double magnitude)
        {
            if (!appliedForce.IsFinite || !Numbers.Finite(magnitude) || magnitude <= 0 || appliedForce.Length <= Numbers.Epsilon)
                return new Vec();
            return -appliedForce.Unit * magnitude;
        }
        public static double BackflowFactor(double load)
        { return Numbers.Finite(load) ? Numbers.Clamp(load, 0, 1) : 0; }
    }
    public static class ShieldAppearance
    {
        public static double RimAlpha(double normalDotView, double strength)
        {
            double dot = Math.Max(0, Math.Min(1, Math.Abs(normalDotView)));
            return Math.Pow(1 - dot, 3) * 0.9 * Math.Max(0, Math.Min(1, strength));
        }
    }
    public static class VisualPolicy
    {
        public static double Load(StepInput input, EffectPlan effects, double power)
        {
            double rcs = input.Rcs ? Math.Max(input.Translation.Length, input.Rotation.Length) : 0;
            return Numbers.Clamp(Math.Max(Math.Max(input.Throttle, rcs), power > 0 ? effects.SupportKN.Length / power : 0), 0, 1);
        }
        public static double Rotor(bool running, bool tdGenerating, double load)
        { return running ? 0.1 + 0.9 * Numbers.Clamp(load, 0, 1) : tdGenerating ? 0.1 : 0; }
        public static bool Open(bool running, bool safeGuard) { return running && !safeGuard; }
        public static double Core(bool running, bool safeGuard, double load)
        { return Open(running, safeGuard) ? Numbers.Clamp(load, 0, 1) : 0; }
        public static double CondenserLevel(double amount, double capacity)
        { return Numbers.Finite(amount) && Numbers.Finite(capacity) && capacity > 0 ? Numbers.Clamp(amount / capacity, 0, 1) : 0; }
        public static bool Named(string name, string prefix)
        { return name != null && name.StartsWith(prefix, StringComparison.Ordinal) && name.Length > prefix.Length && name.Substring(prefix.Length).All(c => c >= '0' && c <= '9'); }
    }
    public static class KspControlFrame
    {
        public static Vec Rotation(Vec right, Vec up, Vec forward, double pitch, double yaw, double roll)
        {
            // KSP 1.12.5 ModuleReactionWheel transforms -(pitch, roll, yaw)
            // by vessel.ReferenceTransform.rotation before applying world torque.
            return -right * pitch - up * roll - forward * yaw;
        }
    }
    public static class PropulsionController
    {
        public static EffectPlan Plan(IList<DriveState> drives, VesselSettings settings, StepInput input)
        {
            var p = new EffectPlan();
            var running = drives.Where(x => x.Running).ToList();
            p.MaxG = running.Sum(x => x.Definition.MaxGContribution * x.Sync);
            // Configuration remains available while engines are stopped; actual force uses MaxG.
            p.ConfigurationMaxG = drives.Where(x => x.Definition.Propulsion).Sum(x => x.Definition.MaxGContribution * x.Sync);
            p.EffectiveAccelerationG = Numbers.Clamp(settings.AccelerationG, 0, p.MaxG) * GNTransAMUnit.AccelerationMultiplier(running);
            if (!input.Active || input.Packed || input.MassKg <= 0)
                return p;
            // MaxG and the saved PAW slider remain the normal-mode setting range.
            // Apply TA after clamping so the normal cap cannot cancel the boost.
            double power = running.Sum(GNPropulsionUnit.Power), aLimit = p.EffectiveAccelerationG * Numbers.G;
            double linearLimit = Math.Min(power, input.MassKg * aLimit / 1000), g = input.Gravity.Length;
            Vec up = -input.Gravity.Unit;
            bool hover = running.Any(x => x.Hover), support = hover || running.Any(x => x.AG) || input.Brakes;
            if (support && g > Numbers.Epsilon)
            {
                double verticalSpeed = Vec.Dot(input.Velocity, up);
                // Brakes must support weight even without AG/Hover. At low speed,
                // remove the residual velocity with a bounded one-step force, not
                // the Hover deadband that intentionally preserves small velocities.
                double response = input.Brakes ? (Math.Abs(verticalSpeed) <= settings.Deadband ? 0 : settings.BrakeTime) : settings.HoverTime;
                double a = hover || input.Brakes ? HoverLaw.Support(g, verticalSpeed, input.Dt, response, aLimit, input.Brakes ? 0 : settings.Deadband) : g;
                p.SupportKN = up * Math.Min(linearLimit, a * input.MassKg / 1000);
            }
            double used = p.SupportKN.Length;
            if (input.Brakes)
            {
                Vec speed = support && g > Numbers.Epsilon ? input.Velocity - up * Vec.Dot(input.Velocity, up) : input.Velocity;
                p.BrakeKN = (-speed * (input.MassKg / (1000 * Math.Max(input.Dt, settings.BrakeTime)))).Limit(Math.Max(0, linearLimit - used));
            }
            used += p.BrakeKN.Length;
            double remaining = Math.Max(0, power - used), rotationReserved = input.Rcs ? remaining * Numbers.Clamp(settings.RotationShare, 0, 1) : 0;
            double thrustCapacity = Math.Min(Math.Max(0, linearLimit - used), remaining - rotationReserved);
            if (!input.Brakes)
            {
                Vec throttle = input.Forward.Unit * Numbers.Clamp(input.Throttle, 0, 1);
                Vec translation = input.Rcs ? input.Translation : new Vec();
                if (support && g > Numbers.Epsilon)
                {
                    // Throttle still cannot oppose support. Downward RCS is an explicit AG-only
                    // braking override while ascending; Hover retains its non-downward behavior.
                    if (Vec.Dot(throttle, up) < 0) throttle -= up * Vec.Dot(throttle, up);
                    bool allowDownRcs = !hover && Vec.Dot(input.Velocity, up) > 0;
                    if (!allowDownRcs && Vec.Dot(translation, up) < 0) translation -= up * Vec.Dot(translation, up);
                }
                Vec direction = (throttle + translation).Limit(1);
                p.ThrustKN = direction * thrustCapacity;
            }
            double torqueMax = running.Sum(x => x.Definition.MaxTorqueNm * x.Sync);
            double torqueCostMax = running.Sum(x => x.Definition.MaxTorqueNm * x.Sync * x.Definition.ParticlePerNmSecond);
            double torqueScale = torqueCostMax > 0 ? Math.Min(1, rotationReserved / torqueCostMax) : 0;
            if (input.Rcs)
            {
                Vec rotation = input.Rotation.Limit(1);
                p.TorqueNm = rotation * (torqueMax * torqueScale);
                p.TorqueRate = rotation.Length * torqueCostMax * torqueScale;
            }
            // No redistribution of unused rotation share, even when rotation input is zero.
            p.ReservedPower = used + p.ThrustKN.Length + rotationReserved;
            var field = running.Where(x => x.Definition.Field).ToList();
            if (settings.Field && field.Count > 0)
            {
                p.Field = true;
                p.FieldMaintenance = field.Min(x => GNFieldUnit.Maintenance(x.Definition, input.FieldDiameter));
                double fieldPower = Math.Max(0, field.Sum(GNPropulsionUnit.Power) - p.ReservedPower);
                double demand = input.Targets.Sum(x => 2 * x.ForceN.Length / 1000), scale = demand > 0 ? Math.Min(1, fieldPower / demand) : 0;
                p.FieldRequestedRate = demand;
                foreach (var t in input.Targets)
                    p.Targets.Add(new FieldTarget { Id = t.Id, ForceN = t.ForceN * scale });
                p.FieldRepulsionRate = demand * scale;
            }
            return p;
        }
    }
    public static class Allocator
    {
        static double Fit(Func<double, double> cost, double budget)
        {
            // Allocation must be conservative. Numbers.Covers is appropriate when
            // validating an already computed payment, but using its tolerance here
            // can select a scale whose cost is microscopically above the budget.
            if (cost(1) <= budget)
                return 1;
            double lo = 0, hi = 1;
            for (int i = 0; i < 60; i++)
            {
                double mid = (lo + hi) / 2;
                if (cost(mid) <= budget)
                    lo = mid;
                else
                    hi = mid;
            }
            return lo;
        }
        public static EffectPlan Allocate(EffectPlan plan, double budget, double dt)
        {
            Numbers.Nonnegative(budget, "budget");
            Numbers.Positive(dt, "dt");
            var p = new EffectPlan { MaxG = plan.MaxG, ConfigurationMaxG = plan.ConfigurationMaxG, EffectiveAccelerationG = plan.EffectiveAccelerationG, ReservedPower = plan.ReservedPower, FieldRequestedRate = plan.FieldRequestedRate };
            p.SupportKN = plan.SupportKN.Limit(budget / dt);
            // Reserve particle maintenance immediately after support. It is not a force/Power cost.
            double maintenance = plan.FieldMaintenance * dt;
            p.Field = plan.Field && Numbers.Covers(p.SupportKN.Length * dt + maintenance, budget);
            p.FieldMaintenance = p.Field ? plan.FieldMaintenance : 0;
            double motionBudget = Math.Max(0, budget - (p.Field ? maintenance : 0));
            double b = p.SupportKN.Length * dt >= motionBudget && Vec.Dot(p.SupportKN, plan.BrakeKN) >= 0 ? 0 : Fit(x => (p.SupportKN + plan.BrakeKN * x).Length * dt, motionBudget);
            p.BrakeKN = plan.BrakeKN * b;
            // |base + x*thrust| + x*rotationCost is convex. x=0 is affordable after
            // preceding stages, so the feasible interval starts at 0 even for opposing RCS.
            // Its upper endpoint can be bisected; the cost itself need not be monotonic.
            Vec motionBase = p.SupportKN + p.BrakeKN;
            double s = motionBase.Length * dt >= motionBudget && Vec.Dot(motionBase, plan.ThrustKN) >= 0 ? 0 : Fit(x => (motionBase + plan.ThrustKN * x).Length * dt + plan.TorqueRate * x * dt, motionBudget);
            p.ThrustKN = plan.ThrustKN * s;
            p.TorqueNm = plan.TorqueNm * s;
            p.TorqueRate = plan.TorqueRate * s;
            if (p.Field)
            {
                double remaining = Math.Max(0, budget - (p.ForceKN.Length * dt + p.TorqueRate * dt + maintenance));
                double repulsionCost = plan.FieldRepulsionRate * dt;
                double scale = repulsionCost > 0 ? Math.Min(1, remaining / repulsionCost) : 1;
                p.FieldRepulsionRate = plan.FieldRepulsionRate * scale;
                foreach (var t in plan.Targets) p.Targets.Add(new FieldTarget { Id = t.Id, ForceN = t.ForceN * scale });
            }
            return p;
        }
    }
    public sealed class VesselSimulation
    {
        static double Receipt(double actual, double requested)
        {
            if (!Numbers.Finite(actual) || actual < -1e-9 || actual > requested + 1e-7)
                throw new InvalidOperationException("Invalid resource receipt");
            return Numbers.Clamp(actual, 0, requested);
        }
        public StepResult Step(IList<DriveState> source, VesselSettings settings, StepInput input, IResourcePort particles, IResourcePort ec)
        {
            return Step(source, settings, input, particles, ec, null);
        }
        public StepResult Step(IList<DriveState> source, VesselSettings settings, StepInput input, IResourcePort particles, IResourcePort ec, IBaryonPort baryons)
        {
            Numbers.Positive(input.Dt, "dt");
            Numbers.Nonnegative(input.MassKg, "mass");
            Numbers.Nonnegative(input.FieldDiameter, "diameter");
            Numbers.Nonnegative(settings.AccelerationG, "acceleration");
            Numbers.Nonnegative(settings.RotationShare, "rotation share");
            Numbers.Nonnegative(settings.HoverTime, "hover time");
            Numbers.Nonnegative(settings.BrakeTime, "brake time");
            Numbers.Nonnegative(settings.Deadband, "deadband");
            if (!input.Gravity.IsFinite || !input.Velocity.IsFinite || !input.Forward.IsFinite || !input.Translation.IsFinite || !input.Rotation.IsFinite || !Numbers.Finite(input.Throttle))
                throw new ArgumentException("Non-finite input");
            foreach (var t in input.Targets)
                if (!t.ForceN.IsFinite)
                    throw new ArgumentException("Non-finite field target");
            var drives = source.OrderBy(x => x.Id).ToList();
            if (drives.Select(x => x.Id).Distinct().Count() != drives.Count)
                throw new ArgumentException("Duplicate drive ID");
            foreach (var d in drives)
            {
                d.Definition.Validate();
                Numbers.Nonnegative(d.Tank, "tank");
                Numbers.Nonnegative(d.TankCapacity, "capacity");
                Numbers.Nonnegative(d.TDQuantity, "TD");
                Numbers.Nonnegative(d.TDCapacity, "TD capacity");
                if (d.Full)
                    d.TALocked = false;
                d.Generated = 0;
                d.BlanketECGenerated = 0;
                d.BaryonMassKg = 0;
            }
            StateRules.Reconcile(drives, settings);
            GNMultiDriveUnit.Update(drives);
            var plan = PropulsionController.Plan(drives, settings, input);
            var r = new StepResult();
            double pStart = Numbers.Nonnegative(particles.Amount, "particle amount");
            Numbers.Nonnegative(particles.Capacity, "particle capacity");
            double pFree = Numbers.Nonnegative(particles.FreeSpace, "particle free space");
            double externalECBudget = Numbers.Nonnegative(ec.Amount, "EC amount");
            double ecCapacity = Numbers.Nonnegative(ec.Capacity, "EC capacity");
            Numbers.Nonnegative(settings.ECMargin, "EC margin");
            if (settings.ECMargin > 1) throw new ArgumentOutOfRangeException("EC margin");
            double chargingECFree = Numbers.Nonnegative(ec.FreeSpace, "EC free space");
            double demand = plan.Cost(input.Dt), wanted = demand + pFree, requiredEC = 0;
            var generation = new Dictionary<DriveState, double>();
            foreach (var d in drives)
            {
                double capacity = GNParticleGenerationUnit.Rate(d) * input.Dt;
                Numbers.Nonnegative(capacity, "particle generation capacity");
                double amount = Math.Min(wanted, capacity);
                generation[d] = amount;
                wanted = Math.Max(0, wanted - amount);
                requiredEC += amount * d.Definition.ECPerParticle;
            }
            Numbers.Nonnegative(demand, "effect cost");
            Numbers.Nonnegative(requiredEC, "generation EC demand");
            if (!plan.ForceKN.IsFinite || !plan.TorqueNm.IsFinite) throw new ArgumentException("Effect overflow");
            var blanketDemand = new Dictionary<DriveState, double>();
            // Include same-step furnace consumption even when storage starts full.
            double ecRoom = Numbers.Nonnegative(chargingECFree + requiredEC, "EC receiving budget");
            foreach (var d in drives)
            {
                double capacity = d.UsesTDBlanket ? d.Definition.BlanketECGenerationRate * d.TDFraction * input.Dt : 0;
                Numbers.Nonnegative(capacity, "blanket capacity");
                blanketDemand[d] = capacity;
            }
            foreach (var d in drives)
            {
                double request = Math.Min(ecRoom, blanketDemand[d]);
                double supplied = request > 0 && baryons != null ? Receipt(baryons.TakeEnergy(request), request) : 0;
                d.BlanketStarved = request > 0 && !Numbers.Covers(request, supplied);
                r.BlanketStarved |= d.BlanketStarved;
                d.BlanketECGenerated = supplied;
                d.BaryonMassKg = MassEnergy.KilogramsForEC(supplied);
                r.BlanketECGenerated += supplied;
                r.BaryonMassKg += d.BaryonMassKg;
                r.ECGenerated += supplied;
                ecRoom = Math.Max(0, ecRoom - supplied);
            }
            // Validate derived quantities before making the first irreversible resource write.
            Numbers.Nonnegative(requiredEC, "EC demand");
            Numbers.Nonnegative(r.ECGenerated, "TD generation");
            double ecNeed = Math.Max(0, requiredEC - r.ECGenerated);
            ecNeed = ECReservePolicy.Draw(ecNeed, externalECBudget, ecCapacity, settings.ECMargin);
            r.ECTaken = ecNeed > 0 ? Receipt(ec.Take(ecNeed), ecNeed) : 0;
            double ecScale = requiredEC > 0 ? Math.Min(1, (r.ECGenerated + r.ECTaken) / requiredEC) : 0;
            foreach (var d in drives)
            {
                d.Generated = generation[d] * ecScale;
                r.Generated += d.Generated;
            }
            r.ECUsed = requiredEC * ecScale;
            double ecSurplus = Math.Max(0, r.ECGenerated + r.ECTaken - r.ECUsed);
            r.ECStored = ecSurplus > 0 ? Receipt(ec.Put(ecSurplus), ecSurplus) : 0;
            r.ECSpilled = ecSurplus - r.ECStored;
            double available = r.Generated + (settings.SafeGuard ? 0 : pStart);
            var allocated = Allocator.Allocate(plan, available, input.Dt);
            // Keep the priority reservation funded during the receipt/reallocation pass,
            // even when downward RCS makes the final vector cost smaller than support.
            double settlementBudget = Math.Max(allocated.Cost(input.Dt), allocated.SupportKN.Length * input.Dt + (allocated.Field ? allocated.FieldMaintenance * input.Dt : 0));
            double take = settings.SafeGuard ? 0 : Math.Max(0, Math.Min(available, settlementBudget) - r.Generated);
            r.ParticleTaken = take > 0 ? Receipt(particles.Take(take), take) : 0;
            double paid = r.Generated + r.ParticleTaken;
            r.Effects = Allocator.Allocate(plan, paid, input.Dt);
            r.SupplyLimited = !settings.SafeGuard && demand > paid + 1e-8;
            if (r.SupplyLimited && drives.Any(x => x.TA))
            {
                GNTransAMUnit.ForceOff(drives);
                r.TAForcedOff = true;
                plan = PropulsionController.Plan(drives, settings, input);
                r.Effects = Allocator.Allocate(plan, paid, input.Dt);
            }
            double effectCost = r.Effects.Cost(input.Dt);
            if (!Numbers.Finite(effectCost) || !Numbers.Covers(effectCost, paid)) throw new InvalidOperationException("Resource budget exceeded");
            // Only roundoff (within Covers) can be clipped here, never a genuine shortage.
            r.Consumed = Math.Min(effectCost, paid);
            double surplus = Math.Max(0, paid - r.Consumed);
            r.ParticleStored = surplus > 0 ? Receipt(particles.Put(surplus), surplus) : 0;
            r.ParticleSpilled = surplus - r.ParticleStored;
            if (settings.Field)
            {
                if (!input.Active || input.Packed) r.FieldStatus = "Requested: inactive/packed vessel";
                else if (!r.Effects.Field)
                {
                    r.FieldStatus = plan.Field ? "OFF: maintenance particle shortage" : "OFF: no active Field unit";
                    settings.Field = false;
                }
                else if (!Numbers.Covers(plan.FieldRepulsionRate, r.Effects.FieldRepulsionRate)) r.FieldStatus = "Active: repulsion particle-limited";
                else if (!Numbers.Covers(plan.FieldRequestedRate, plan.FieldRepulsionRate)) r.FieldStatus = "Active: repulsion Power-limited";
                else r.FieldStatus = "Active";
            }
            if (!settings.SafeGuard && input.Active && !input.Packed && plan.ForceKN.Length + plan.TorqueRate > Numbers.Epsilon && r.Effects.ForceKN.Length + r.Effects.TorqueRate <= Numbers.Epsilon)
                foreach (var d in drives.Where(x => x.Running))
                {
                    d.Depleted = true;
                    d.Engine = false;
                    d.TA = false;
                }
            if (!Numbers.Finite(r.Consumed) || r.Consumed > paid + 1e-7)
                throw new InvalidOperationException("Resource budget exceeded");
            return r;
        }
    }
}
#if !GN_CORE_ONLY
namespace GNSystems
{
    using GNSystems.Core;
#if !GN_ADAPTER_TEST
    using UnityEngine;
#endif
    using System.Globalization;

#if !GN_ADAPTER_TEST
    internal sealed class GNTDBlanketSettings
    {
        public string[] ResourceNames = { "IntakeAir", "MonoPropellant", "LiquidFuel", "Oxidizer" };
        static GNTDBlanketSettings current;
        public static GNTDBlanketSettings Current { get { return current ?? (current = Load()); } }
        static GNTDBlanketSettings Load()
        {
            var result = new GNTDBlanketSettings();
            try
            {
                var roots = GameDatabase.Instance.GetConfigNodes("GNDRIVE_SETTINGS");
                if (roots == null || roots.Length == 0) return result;
                if (roots.Length != 1) throw new InvalidOperationException("Expected exactly one GNDRIVE_SETTINGS node");
                var node = roots[0].GetNode("TDBlanket");
                if (node == null) return result;
                var names = node.GetValues("Resource").Where(x => !string.IsNullOrEmpty(x))
                    .Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (names.Length > 0) result.ResourceNames = names;
                Debug.Log("[GN V3] TD Blanket baryons: " + string.Join(", ", result.ResourceNames));
            }
            catch (Exception e) { Debug.LogError("[GN V3] Invalid TD Blanket settings; using defaults: " + e.Message); }
            return result;
        }
    }

    internal sealed class GNVesselGlowSettings
    {
        public bool Enabled = true;
        public float Intensity = 2f;
        public float Transparency = 0.8f;
        public float ShellScale = 1.02f;
        public float ResponseSpeed = 2f;
        public bool AfterimagesEnabled = true;
        public int AfterimageCount = 4;
        public float AfterimageSizeDivisor = 8f;
        public float AfterimageExtendTime = 1f;
        public float AfterimageSpacing = 1f, AfterimageOpacity = 0.3f, AfterimageThreshold = 0.5f;
        public Color TransAMColor = new Color(1, 0.05f, 0.25f, 1);
        public Color TransAMCoreColor = new Color(1, 0.95f, 0.95f, 1);
        public float TransAMCoreIntensity = 4f;
        public bool GNLightEnabled = true;
        public float GNLightIntensity = 10f;
        public float GNLightRange = 5f;
        public float GNLightTransAMIntensity = 20f;
        public float GNLightTransAMRange = 10f;
        public string[] ExcludedObjectKeywords = new string[0];
        static GNVesselGlowSettings current;
        public static GNVesselGlowSettings Current
        {
            get { return current ?? (current = Load()); }
        }
        static GNVesselGlowSettings Load()
        {
            var defaults = new GNVesselGlowSettings();
            try
            {
                var nodes = GameDatabase.Instance.GetConfigNodes("GNDRIVE_SETTINGS");
                if (nodes == null || nodes.Length == 0)
                {
                    Debug.Log("[GN V3] GNDRIVE_SETTINGS not found; using vessel glow defaults.");
                    return defaults;
                }
                if (nodes.Length != 1)
                    throw new InvalidOperationException("Expected exactly one GNDRIVE_SETTINGS node");
                var glow = nodes[0].GetNode("VesselGlow");
                if (glow == null)
                    throw new FormatException("GNDRIVE_SETTINGS/VesselGlow is missing");
                var result = new GNVesselGlowSettings();
                result.TransAMColor = ReadColor(nodes[0], "TransAMColor", result.TransAMColor);
                result.TransAMCoreColor = ReadColor(nodes[0], "TransAMCoreColor", result.TransAMCoreColor);
                result.TransAMCoreIntensity = ReadFloat(nodes[0], "TransAMCoreIntensity", result.TransAMCoreIntensity, 0, 20);
                result.Enabled = ReadBool(glow, "Enabled", result.Enabled);
                result.Intensity = ReadFloat(glow, "Intensity", result.Intensity, 0, 20);
                result.Transparency = ReadFloat(glow, "Transparency", result.Transparency, 0, 1);
                result.ShellScale = ReadFloat(glow, "ShellScale", result.ShellScale, 1, 1.1f);
                result.ResponseSpeed = ReadFloat(glow, "ResponseSpeed", result.ResponseSpeed, 0.01f, 20);
                result.AfterimagesEnabled = ReadBool(glow, "AfterimagesEnabled", result.AfterimagesEnabled);
                string countRaw = glow.GetValue("AfterimageCount");
                int count;
                if (countRaw != null)
                {
                    if (!int.TryParse(countRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out count)) throw new FormatException("Invalid AfterimageCount");
                    result.AfterimageCount = Math.Max(0, Math.Min(16, count));
                }
                result.AfterimageSizeDivisor = ReadFloat(glow, "AfterimageSizeDivisor", result.AfterimageSizeDivisor, 1, 1000);
                result.AfterimageExtendTime = ReadFloat(glow, "AfterimageExtendTime", result.AfterimageExtendTime, 0.01f, 10);
                result.AfterimageSpacing = ReadFloat(glow, "AfterimageSpacing", result.AfterimageSpacing, 0, 100);
                result.AfterimageOpacity = ReadFloat(glow, "AfterimageOpacity", result.AfterimageOpacity, 0, 1);
                result.AfterimageThreshold = ReadFloat(glow, "AfterimageThreshold", result.AfterimageThreshold, 0.01f, 100);
                result.ExcludedObjectKeywords = glow.GetValues("ExcludeObjectKeyword")
                    .Where(x => !string.IsNullOrEmpty(x)).Select(x => x.Trim()).Where(x => x.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var light = nodes[0].GetNode("GNLight");
                if (light != null)
                {
                    result.GNLightEnabled = ReadBool(light, "Enabled", result.GNLightEnabled);
                    result.GNLightIntensity = ReadFloat(light, "Intensity", result.GNLightIntensity, 0, 100);
                    result.GNLightRange = ReadFloat(light, "Range", result.GNLightRange, 0, 100);
                    result.GNLightTransAMIntensity = ReadFloat(light, "TransAMIntensity", result.GNLightTransAMIntensity, 0, 100);
                    result.GNLightTransAMRange = ReadFloat(light, "TransAMRange", result.GNLightTransAMRange, 0, 100);
                }
                Debug.Log("[GN V3] Loaded global vessel glow settings.");
                return result;
            }
            catch (Exception e)
            {
                Debug.LogError("[GN V3] Invalid GNDRIVE_SETTINGS; using defaults: " + e.Message);
                return defaults;
            }
        }
        static bool ReadBool(ConfigNode node, string name, bool fallback)
        {
            string raw = node.GetValue(name);
            if (raw == null) return fallback;
            bool value;
            if (!bool.TryParse(raw, out value)) throw new FormatException("Invalid " + name);
            return value;
        }
        static float ReadFloat(ConfigNode node, string name, float fallback, float minimum, float maximum)
        {
            string raw = node.GetValue(name);
            if (raw == null) return fallback;
            float value;
            if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || float.IsNaN(value) || float.IsInfinity(value))
                throw new FormatException("Invalid " + name);
            return Mathf.Clamp(value, minimum, maximum);
        }
        static Color ReadColor(ConfigNode node, string name, Color fallback)
        {
            string raw = node.GetValue(name);
            if (raw == null) return fallback;
            string[] parts = raw.Split(',');
            if (parts.Length != 3) throw new FormatException("Invalid " + name);
            float[] values = new float[3];
            for (int i = 0; i < values.Length; i++)
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) ||
                    float.IsNaN(values[i]) || float.IsInfinity(values[i]) || values[i] < 0 || values[i] > 1)
                    throw new FormatException("Invalid " + name);
            return new Color(values[0], values[1], values[2], 1);
        }
    }

    // One module per Part; only the vessel coordinator mutates resources or applies forces.
    public class GNModule : PartModule
    {
        [KSPField] public bool HasPropulsion = true;
        [KSPField] public string GeneratorColor = "1,0,0.15";
        internal Color GeneratorTint = new Color(1, 0, 0.15f);
        [KSPField] public bool HasGeneration = false;
        [KSPField] public bool HasTD = false;
        [KSPField] public bool HasTDBlanket = true;
        [KSPField] public bool HasTransAM = true;
        [KSPField] public bool HasMultiDrive = true;
        [KSPField] public bool HasField = false;
        [KSPField] public bool Gen1 = false;
        [KSPField] public double Power = 100;
        [KSPField] public double MaxGContribution = 5;
        [KSPField] public double ParticleGenerationRate = 10;
        [KSPField] public double ECPerParticle = 10;
        [KSPField] public double BlanketECGenerationRate = 200;
        [KSPField] public double ECPerTDSecond = 2;
        [KSPField] public double TransAMRatio = 3;
        [KSPField] public double MaxTorqueNm = 10000;
        [KSPField] public double ParticlePerNmSecond = 0.0001;
        [KSPField] public double FieldReferenceDiameter = 10;
        [KSPField] public double FieldParticleRate = 1;
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Individuality", guiFormat = "F6")] public double Individuality = -1;
        [KSPField] public double InitialInternalEC = 1;
        [KSPField] public double InitialInternalParticles = 1;
        [KSPField] public string RotorTransform = "rotor";
        [KSPField] public string DeployTransform = "";
        [KSPField] public string EmissiveTransform = "";
        [KSPField] public string AudioPath = "";
        // Exact model transform name; empty disables GN particle control.
        [KSPField] public string ParticleTransform = "EMI";
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Particle size"), UI_FloatRange(minValue = 0.001f, maxValue = 1f, stepIncrement = 0.001f)] public float ParticleSize = 0.04f;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Full-load particles /s"), UI_FloatRange(minValue = 0f, maxValue = 3000f, stepIncrement = 10f)] public float ParticleRate = 600f;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Full-load particle speed"), UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 0.1f)] public float ParticleSpeed = 6f;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Particle backflow accel"), UI_FloatRange(minValue = 0f, maxValue = 200f, stepIncrement = 1f)] public float ParticleAcceleration = 20f;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Particle lifetime"), UI_FloatRange(minValue = 0.05f, maxValue = 10f, stepIncrement = 0.05f)] public float ParticleLifetime = 1f;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Full-load cone angle"), UI_FloatRange(minValue = 0f, maxValue = 80f, stepIncrement = 1f)] public float ParticleConeAngle = 12f;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Particle outlet radius"), UI_FloatRange(minValue = 0f, maxValue = 2f, stepIncrement = 0.01f)] public float ParticleRadius = 0.04f;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Ring glow size"), UI_FloatRange(minValue = 0.01f, maxValue = 2f, stepIncrement = 0.01f)] public float ParticleGlowSize = 0.3f;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Ring glow density"), UI_FloatRange(minValue = 0f, maxValue = 1000f, stepIncrement = 10f)] public float ParticleGlowRate = 120f;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Ring glow intensity"), UI_FloatRange(minValue = 0f, maxValue = 2f, stepIncrement = 0.05f)] public float ParticleGlowIntensity = 0.8f;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Rotor max deg/s"), UI_FloatRange(minValue = 0f, maxValue = 3600f, stepIncrement = 5f)] public float RotorSpeed = 60;
        [KSPField(isPersistant = true, guiName = "Condenser max deg/s"), UI_FloatRange(minValue = 0f, maxValue = 3600f, stepIncrement = 5f)] public float CondenserRotorSpeed = 60;
        [KSPField] public float DeployDistance = 0.2f;
        [KSPField] public float RotationAngle = 30;
        [KSPField] public float ArmSpeed = 30;
        [KSPField] public float CoreSpeed = 0.2f;
        [KSPField] public float VisualResponse = 2;
        [KSPField(isPersistant = true)] public bool visualTuning;
        [KSPField(isPersistant = true)] public bool rotorTuningOverride;
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Stage starts GN"), UI_Toggle(enabledText = "Yes", disabledText = "No")] public bool StageStartsEngine = true;
        [KSPField(isPersistant = true, guiName = "Idle particle speed"), UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 0.1f)] public float ParticleIdleSpeed = 1;
        [KSPField(isPersistant = true, guiName = "Idle particles /s"), UI_FloatRange(minValue = 0f, maxValue = 3000f, stepIncrement = 10f)] public float ParticleIdleRate = 60;
        [KSPField(isPersistant = true, guiName = "Idle cone angle"), UI_FloatRange(minValue = 0f, maxValue = 80f, stepIncrement = 1f)] public float ParticleIdleAngle = 35;
        [KSPField(guiActive = false, guiName = "Particle visual load", guiFormat = "F2")] public float particleVisualLoad;
        [KSPField(guiActive = false, guiName = "Effective particles /s", guiFormat = "F1")] public float particleEffectiveRate;
        static readonly string[] TuningFields = { "ParticleSize", "ParticleRate", "ParticleSpeed", "ParticleAcceleration", "ParticleLifetime", "ParticleConeAngle", "ParticleRadius", "ParticleGlowSize", "ParticleGlowRate", "ParticleGlowIntensity", "RotorSpeed", "ParticleIdleSpeed", "ParticleIdleRate", "ParticleIdleAngle", "particleVisualLoad", "particleEffectiveRate", "CondenserRotorSpeed" };
        [KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "Show/hide visual tuning")]
        public void ToggleVisualTuning()
        {
            if (!rotorTuningOverride) { RotorSpeed = VisualValue("RotorSpeed", RotorSpeed); rotorTuningOverride = true; }
            visualTuning = !visualTuning;
            RefreshTuning();
        }
        void RefreshTuning()
        {
            foreach (var name in TuningFields) { Fields[name].guiActive = visualTuning; Fields[name].guiActiveEditor = visualTuning; }
        }
        public override bool IsStageable() { return HasPropulsion; }
        public override bool StagingEnabled() { return HasPropulsion && StageStartsEngine; }
        public override void OnActive()
        {
            base.OnActive();
            if (StageStartsEngine) Send(CommandKind.Engine, true);
        }
        internal float VisualValue(string name, float fallback)
        {
            var profile = profiles.FirstOrDefault(n => n.GetValue("variant") == selectedProfile);
            string raw = profile != null ? profile.GetValue(name) : null;
            if (raw == null && name == "RotationAngle" && part.partInfo != null && part.partInfo.partConfig != null)
            {
                var vm = part.partInfo.partConfig.GetNodes("MODULE").FirstOrDefault(n => n.GetValue("name") == "ModulePartVariants");
                var vn = vm == null ? null : vm.GetNodes("VARIANT").FirstOrDefault(n => n.GetValue("name") == selectedProfile);
                raw = vn == null ? null : vn.GetValue(name);
            }
            if (raw == null) return fallback;
            float value;
            if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || float.IsNaN(value) || float.IsInfinity(value) || value < 0)
                throw new FormatException("Invalid visual setting " + name);
            return value;
        }
        [KSPField(isPersistant = true)] public int schemaVersion = 1;
        [KSPField(isPersistant = true)] public bool initialized = false;
        [KSPField(isPersistant = true)] public double internalEC;
        [KSPField(isPersistant = true)] public double internalParticles;
        [KSPField(isPersistant = true)] public string selectedProfile = "";
        [KSPField(isPersistant = true, guiActive = true, guiName = "Propulsion"), UI_Toggle(enabledText = "ON", disabledText = "OFF")] public bool engineOn;
        [KSPField(isPersistant = true, guiActive = true, guiName = "Anti-gravity"), UI_Toggle(enabledText = "ON", disabledText = "OFF")] public bool agOn;
        [KSPField(isPersistant = true, guiActive = true, guiName = "Hover"), UI_Toggle(enabledText = "ON", disabledText = "OFF")] public bool hoverOn;
        [KSPField(isPersistant = true, guiActive = true, guiName = "Trans-AM"), UI_Toggle(enabledText = "ON", disabledText = "OFF")] public bool taOn;
        [KSPField(isPersistant = true, guiActive = true, guiName = "Generation"), UI_Toggle(enabledText = "ON", disabledText = "OFF")] public bool generationOn = true;
        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "TD Blanket")] public bool blanketOn;
        [KSPField(isPersistant = true)] public bool blanketStarved;
        [KSPField(isPersistant = true, guiActive = true, guiName = "Synchronized"), UI_Toggle(enabledText = "ON", disabledText = "OFF")] public bool joined;
        [KSPField(isPersistant = true)] public int group;
        [KSPField(isPersistant = true, guiActive = true, guiName = "Repose"), UI_Toggle(enabledText = "ON", disabledText = "OFF")] public bool repose;
        [KSPField(isPersistant = true, guiActive = true, guiName = "Depleted")] public bool depleted;
        [KSPField(isPersistant = true)] public bool taLocked;
        [KSPField(isPersistant = true, guiActive = true, guiName = "SafeGuard"), UI_Toggle(enabledText = "ON", disabledText = "OFF")] public bool safeGuard = true;
        [KSPField(isPersistant = true, guiActive = true, guiName = "GN Field"), UI_Toggle(enabledText = "ON", disabledText = "OFF")] public bool fieldOn;
        [KSPField(guiActive = true, guiName = "GN Field status")] public string fieldStatus = "OFF";
        [KSPField(isPersistant = true, guiActive = true, guiName = "Acceleration", guiUnits = " G"), UI_FloatRange(minValue = 0, maxValue = 50, stepIncrement = 0.1f)] public float accelerationG = 5;
        [KSPField(isPersistant = true, guiActive = true, guiName = "Rotation share"), UI_FloatRange(minValue = 0, maxValue = 1, stepIncrement = 0.05f)] public float rotationShare = 0.2f;
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "EC reserve margin", guiFormat = "F2"), UI_FloatRange(minValue = 0, maxValue = 1, stepIncrement = 0.01f)] public float ecMargin = 0.2f;
        [KSPField(guiActive = true, guiName = "Effective accel target", guiUnits = " G", guiFormat = "F2")] public double effectiveAccelerationG;
        [KSPField(guiActive = true, guiName = "GN V3 status")] public string status = "Initializing";
        [KSPField(guiActive = true, guiName = "Particle generation", guiUnits = " /s", guiFormat = "F2")] public double generationRate;
        [KSPField(guiActive = true, guiName = "TD Blanket status")] public string blanketStatus = "OFF";
        [KSPField(guiActive = true, guiName = "Blanket EC", guiUnits = " EC/s", guiFormat = "F2")] public double blanketECRate;
        [KSPField(guiActive = true, guiName = "Baryon use", guiUnits = " kg/s", guiFormat = "E3")] public double baryonRate;
        [KSPField(guiActive = true, guiName = "Vessel consumption", guiUnits = " /s", guiFormat = "F2")] public double consumptionRate;
        [KSPField(guiActive = true, guiName = "Synchronization", guiFormat = "F3")] public double syncFactor = 1;
        internal DriveState State;
        internal string ConfigurationError;
        internal float PublishedAcceleration, PublishedRotation, PublishedECMargin;
        internal GNDriveV3Visuals Visuals;
        DriveDefinition baseline;
        ConfigNode[] profiles = new ConfigNode[0];
        ModulePartVariants variants;
        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            if (node.HasNode("GN_PROFILE"))
                profiles = node.GetNodes("GN_PROFILE");
        }
        public override void OnCopy(PartModule fromModule)
        {
            base.OnCopy(fromModule);
            Individuality = GNIndividuality.FromPersistentId(part.persistentId);
        }
        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            RefreshTuning();
            ConfigureStateToggles();
            bool showBlanket = HasTDBlanket && HasTD;
            Fields["blanketOn"].guiActive = false;
            Fields["blanketOn"].guiActiveEditor = false;
            Fields["blanketStatus"].guiActive = showBlanket;
            Fields["blanketECRate"].guiActive = showBlanket;
            Fields["baryonRate"].guiActive = showBlanket;
            Events["ToggleBlanket"].guiActive = false;
            Events["ToggleBlanket"].guiActiveEditor = false;
            Actions["BlanketAction"].active = false;
            if (HasPropulsion) part.stagingIcon = "LIQUID_ENGINE";
            try
            {
                if (schemaVersion != 1)
                    throw new InvalidOperationException("Unsupported GN state version");
                if (Individuality < 0)
                    Individuality = GNIndividuality.FromPersistentId(part.persistentId);
                if (!initialized)
                {
                    internalEC = Numbers.Nonnegative(InitialInternalEC, "InitialInternalEC");
                    internalParticles = Numbers.Nonnegative(InitialInternalParticles, "InitialInternalParticles");
                    initialized = true;
                }
                baseline = new DriveDefinition
                {
                    Propulsion = HasPropulsion,
                    Generation = HasGeneration,
                    TD = HasTD,
                    TDBlanket = HasTDBlanket,
                    TransAM = HasTransAM,
                    MultiDrive = HasMultiDrive,
                    Field = HasField,
                    Gen1 = Gen1,
                    Power = Power,
                    MaxGContribution = MaxGContribution,
                    ParticleGenerationRate = ParticleGenerationRate,
                    ECPerParticle = ECPerParticle,
                    ECPerTDSecond = ECPerTDSecond,
                    BlanketECGenerationRate = BlanketECGenerationRate,
                    TransAMRatio = TransAMRatio,
                    MaxTorqueNm = MaxTorqueNm,
                    ParticlePerNmSecond = ParticlePerNmSecond,
                    FieldReferenceDiameter = FieldReferenceDiameter,
                    FieldParticleRate = FieldParticleRate,
                    Individuality = Individuality
                };
                if (profiles.Length == 0 && part.partInfo != null && part.partInfo.partConfig != null)
                {
                    var config = part.partInfo.partConfig.GetNodes("MODULE").FirstOrDefault(x => x.GetValue("name") == "GNModule");
                    if (config != null)
                        profiles = config.GetNodes("GN_PROFILE");
                }
                variants = part.FindModuleImplementing<ModulePartVariants>();
                if (HighLogic.LoadedSceneIsEditor || string.IsNullOrEmpty(selectedProfile))
                    selectedProfile = VariantName();
                State = new DriveState
                {
                    Id = part.persistentId,
                    Engine = engineOn,
                    AG = agOn,
                    Hover = hoverOn,
                    TA = taOn,
                    Generate = generationOn,
                    Blanket = blanketOn,
                    BlanketStarved = blanketStarved,
                    Joined = joined,
                    Group = group,
                    Repose = repose,
                    Depleted = depleted,
                    TALocked = taLocked,
                    InternalEC = internalEC,
                    InternalParticles = internalParticles
                };
                ReadProfile();
                ReadResources();
                if (State.Repose)
                {
                    State.Engine = State.AG = State.Hover = State.TA = State.Joined = false;
                }
                PublishedAcceleration = accelerationG;
                PublishedRotation = rotationShare;
                PublishedECMargin = ecMargin;
                Visuals = new GNDriveV3Visuals(this);
                status = "Ready";
                GameEvents.onVariantApplied.Add(VariantApplied);
                GameEvents.onEditorVariantApplied.Add(VariantApplied);
                if (vessel != null)
                {
                    var manager = vessel.FindVesselModuleImplementing<GNDriveV3Vessel>();
                    if (manager != null)
                        manager.Invalidate();
                }
            }
            catch (Exception e) { ConfigurationError = e.Message; status = "ERROR: " + e.Message; Debug.LogError("[GN V3] " + e); }
        }
        string VariantName()
        {
            return variants != null && variants.SelectedVariant != null ? variants.SelectedVariant.Name : "";
        }
        void VariantApplied(Part p, PartVariant variant)
        {
            if (p != part || !HighLogic.LoadedSceneIsEditor || State == null)
                return;
            try
            {
                selectedProfile = variant.Name;
                ReadProfile();
                ConfigurationError = null;
                status = "Ready";
            }
            catch (Exception e) { ConfigurationError = e.Message; status = "ERROR: " + e.Message; }
        }
        void ReadProfile()
        {
            var d = baseline.Copy();
            var matches = profiles.Where(x => x.GetValue("variant") == selectedProfile).ToArray();
            if (matches.Length > 1)
                throw new InvalidOperationException("Duplicate GN_PROFILE variant");
            if (matches.Length == 1)
                foreach (string name in new[] { "Power", "MaxGContribution", "ParticleGenerationRate", "BlanketECGenerationRate", "ECPerParticle", "ECPerTDSecond", "TransAMRatio", "MaxTorqueNm", "ParticlePerNmSecond", "FieldReferenceDiameter", "FieldParticleRate" })
                {
                    string raw = matches[0].GetValue(name);
                    if (raw == null)
                        continue;
                    double value;
                    if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                        throw new FormatException("Invalid GN_PROFILE " + name);
                    typeof(DriveDefinition).GetField(name).SetValue(d, value);
                }
            Vec rgb = DriveColorPolicy.ParseRgb(matches.Length == 1 ? matches[0].GetValue("GeneratorColor") ?? GeneratorColor : GeneratorColor);
            GeneratorTint = new Color((float)rgb.X, (float)rgb.Y, (float)rgb.Z);
            d.Validate();
            State.Definition = d;
        }
        internal void ReadResources()
        {
            if (State == null)
                return;
            State.Id = part.persistentId;
            var p = part.Resources.Get("GNparticle");
            State.Tank = p != null ? p.amount : 0;
            State.TankCapacity = p != null ? p.maxAmount : 0;
            var td = part.Resources.Get("TopologicalDefects");
            State.TDQuantity = td != null ? td.amount : 0;
            State.TDCapacity = td != null ? td.maxAmount : 0;
        }
        internal void Publish(VesselSettings settings, StepResult result, double dt)
        {
            engineOn = State.Engine;
            agOn = State.AG;
            hoverOn = State.Hover;
            taOn = State.TA;
            generationOn = State.Generate;
            blanketOn = State.Blanket;
            blanketStarved = State.BlanketStarved;
            joined = State.Joined;
            group = State.Group;
            repose = State.Repose;
            depleted = State.Depleted;
            taLocked = State.TALocked;
            internalEC = State.InternalEC;
            internalParticles = State.InternalParticles;
            safeGuard = settings.SafeGuard;
            fieldOn = settings.Field;
            accelerationG = PublishedAcceleration = (float)settings.AccelerationG;
            rotationShare = PublishedRotation = (float)settings.RotationShare;
            ecMargin = PublishedECMargin = (float)settings.ECMargin;
            if (result != null)
            {
                if (result.FieldStatus != null) fieldStatus = result.FieldStatus;
                else if (fieldOn) fieldStatus = "Requested";
                else if (!fieldStatus.StartsWith("OFF:")) fieldStatus = "OFF";
                effectiveAccelerationG = result.Effects.EffectiveAccelerationG;
                generationRate = State.Generated / dt;
                blanketECRate = State.BlanketECGenerated / dt;
                baryonRate = State.BaryonMassKg / dt;
                if (!State.Blanket) blanketStatus = "Unavailable";
                else if (State.TDFraction <= 0) blanketStatus = "Standby: no TopologicalDefects";
                else if (State.BlanketStarved) blanketStatus = "Baryon supply limited";
                else blanketStatus = "Automatic: " + (State.TDFraction * 100).ToString("F0", CultureInfo.InvariantCulture) + "% TD";
                consumptionRate = result.Consumed / dt;
                syncFactor = State.Sync;
                var slider = Fields["accelerationG"].uiControlFlight as UI_FloatRange;
                if (slider != null)
                    slider.maxValue = (float)result.Effects.ConfigurationMaxG;
                status = depleted ? "Depleted: fill own tank, then ON" : repose ? "Repose" : result.TAForcedOff ? "Trans-AM supply stop" : result.SupplyLimited ? "Supply limited" : engineOn ? "Running" : "Standby";
                if (GNMultiDriveUnit.Unsynchronized(State))
                    status = status == "Running" ? "Unsynchronized" : status + " / Unsynchronized";
            }
        }
        public override void OnSave(ConfigNode node)
        {
            if (State != null)
            {
                engineOn = State.Engine;
                agOn = State.AG;
                hoverOn = State.Hover;
                taOn = State.TA;
                generationOn = State.Generate;
                blanketOn = State.Blanket;
                blanketStarved = State.BlanketStarved;
                joined = State.Joined;
                group = State.Group;
                repose = State.Repose;
                depleted = State.Depleted;
                taLocked = State.TALocked;
            }
            base.OnSave(node);
        }
        public override void OnUpdate()
        {
            base.OnUpdate();
            if (Visuals != null && HighLogic.LoadedSceneIsFlight)
                Visuals.Update();
        }
        void ConfigureStateToggles()
        {
            BindStateToggle("engineOn", "ToggleEngine", CommandKind.Engine);
            BindStateToggle("agOn", "ToggleAG", CommandKind.AG);
            BindStateToggle("hoverOn", "ToggleHover", CommandKind.Hover);
            BindStateToggle("taOn", "ToggleTA", CommandKind.TransAM);
            BindStateToggle("generationOn", "ToggleGeneration", CommandKind.Generation);
            BindStateToggle("joined", "ToggleJoin", CommandKind.Join);
            BindStateToggle("repose", "ToggleRepose", CommandKind.Repose);
            BindStateToggle("safeGuard", "ToggleSafeGuard", CommandKind.SafeGuard);
            BindStateToggle("fieldOn", "ToggleField", CommandKind.Field);
        }
        void BindStateToggle(string fieldName, string eventName, CommandKind kind)
        {
            // Keep event methods for action groups and old craft, but show only the stateful control.
            Events[eventName].guiActive = false;
            var field = Fields[fieldName];
            var toggle = field.uiControlFlight as UI_Toggle;
            if (toggle == null) return;
            toggle.onFieldChanged = (changed, previous) =>
            {
                bool requested = (bool)changed.GetValue(this);
                // UI edits are requests; the coordinator publishes the accepted state.
                changed.SetValue(previous, this);
                Send(kind, requested);
            };
        }
        void Send(CommandKind kind, bool value)
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || State == null)
                return;
            var manager = vessel.FindVesselModuleImplementing<GNDriveV3Vessel>();
            if (manager == null)
            {
                status = "GN vessel coordinator unavailable";
                return;
            }
            manager.Enqueue(this, kind, value);
        }
        [KSPEvent(guiActive = true, guiName = "Toggle Propulsion")]
        public void ToggleEngine()
        {
            Send(CommandKind.Engine, !engineOn);
        }
        [KSPEvent(guiActive = true, guiName = "Toggle Anti-gravity")]
        public void ToggleAG()
        {
            Send(CommandKind.AG, !agOn);
        }
        [KSPEvent(guiActive = true, guiName = "Toggle Hover")]
        public void ToggleHover()
        {
            Send(CommandKind.Hover, !hoverOn);
        }
        [KSPEvent(guiActive = true, guiName = "Toggle Trans-AM")]
        public void ToggleTA()
        {
            Send(CommandKind.TransAM, !taOn);
        }
        [KSPEvent(guiActive = true, guiName = "Toggle Generation")]
        public void ToggleGeneration()
        {
            Send(CommandKind.Generation, !generationOn);
        }
        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "Toggle TD Blanket")]
        public void ToggleBlanket()
        {
            ToggleGeneration();
        }
        [KSPEvent(guiActive = true, guiName = "Toggle Synchronization")]
        public void ToggleJoin()
        {
            Send(CommandKind.Join, !joined);
        }
        [KSPEvent(guiActive = true, guiName = "Toggle Repose")]
        public void ToggleRepose()
        {
            Send(CommandKind.Repose, !repose);
        }
        [KSPEvent(guiActive = true, guiName = "Toggle SafeGuard")]
        public void ToggleSafeGuard()
        {
            Send(CommandKind.SafeGuard, !safeGuard);
        }
        [KSPEvent(guiActive = true, guiName = "Toggle GN Field")]
        public void ToggleField()
        {
            Send(CommandKind.Field, !fieldOn);
        }
        [KSPAction("Toggle GN Propulsion")]
        public void EngineAction(KSPActionParam p)
        {
            ToggleEngine();
        }
        [KSPAction("Toggle GN Anti-gravity")]
        public void AGAction(KSPActionParam p)
        {
            ToggleAG();
        }
        [KSPAction("Toggle GN Hover")]
        public void HoverAction(KSPActionParam p)
        {
            ToggleHover();
        }
        [KSPAction("Toggle GN Trans-AM")]
        public void TAAction(KSPActionParam p)
        {
            ToggleTA();
        }
        [KSPAction("Toggle GN Generation")]
        public void GenerationAction(KSPActionParam p)
        {
            ToggleGeneration();
        }
        [KSPAction("Toggle TD Blanket")]
        public void BlanketAction(KSPActionParam p)
        {
            ToggleGeneration();
        }
        [KSPAction("Toggle GN Synchronization")]
        public void JoinAction(KSPActionParam p)
        {
            ToggleJoin();
        }
        [KSPAction("Toggle GN Repose")]
        public void ReposeAction(KSPActionParam p)
        {
            ToggleRepose();
        }
        [KSPAction("Toggle GN SafeGuard")]
        public void SafeGuardAction(KSPActionParam p)
        {
            ToggleSafeGuard();
        }
        [KSPAction("Toggle GN Field")]
        public void FieldAction(KSPActionParam p)
        {
            ToggleField();
        }
        public void OnDestroy()
        {
            GameEvents.onVariantApplied.Remove(VariantApplied);
            GameEvents.onEditorVariantApplied.Remove(VariantApplied);
            if (Visuals != null)
                Visuals.Dispose();
        }
    }

#endif
    internal sealed class KspResourcePort : IResourcePort
    {
        readonly List<PartResource> resources = new List<PartResource>();
        readonly Part requestPart;
        readonly int resourceId;
        readonly ResourceFlowMode flowMode;
        public KspResourcePort(Vessel vessel, string name)
        {
            var definition = PartResourceLibrary.Instance.GetDefinition(name);
            if (definition == null)
                throw new InvalidOperationException("Missing resource definition: " + name);
            flowMode = definition.resourceFlowMode;
            resourceId = definition.id;
            if (flowMode != ResourceFlowMode.ALL_VESSEL && flowMode != ResourceFlowMode.STAGE_PRIORITY_FLOW)
                throw new NotSupportedException(name + ": unsupported resource flow " + flowMode);
            if (flowMode == ResourceFlowMode.STAGE_PRIORITY_FLOW)
            {
                requestPart = vessel.rootPart;
                if (requestPart == null) throw new InvalidOperationException("Resource request requires a vessel root Part");
                // Stock ElectricCharge uses stage priority. Delegate actual routing, priority and
                // locks to KSP; do not emulate it by subtracting from our Part-ID-ordered list.
                return;
            }
            foreach (var p in vessel.parts.OrderBy(x => x.persistentId))
            {
                var r = p.Resources.Get(name);
                if (r != null)
                    resources.Add(r);
            }
        }
        // ALL_VESSEL path: locked tanks excluded from both spending and storage.
        // STAGE_PRIORITY_FLOW path: native KSP API handles the connected resource domain.
        static bool CanTake(PartResource r)
        {
            return r.part != null && r.flowState && (r.flowMode == PartResource.FlowMode.Both || r.flowMode == PartResource.FlowMode.Out);
        }
        static bool CanPut(PartResource r)
        {
            return r.part != null && r.flowState && (r.flowMode == PartResource.FlowMode.Both || r.flowMode == PartResource.FlowMode.In);
        }
        public double Amount
        {
            get
            {
                if (requestPart != null)
                {
                    double amount, capacity;
                    requestPart.GetConnectedResourceTotals(resourceId, flowMode, out amount, out capacity, true);
                    return amount;
                }
                return resources.Where(CanTake).Sum(r => r.amount);
            }
        }
        public double Capacity
        {
            get
            {
                if (requestPart != null)
                {
                    double amount, capacity;
                    requestPart.GetConnectedResourceTotals(resourceId, flowMode, out amount, out capacity, false);
                    return capacity;
                }
                // Capacity describes receiving tanks, matching the native pulling=false query.
                return resources.Where(CanPut).Sum(r => r.maxAmount);
            }
        }
        public double FreeSpace
        {
            get
            {
                if (requestPart != null)
                {
                    double amount, capacity;
                    requestPart.GetConnectedResourceTotals(resourceId, flowMode, out amount, out capacity, false);
                    // With pulling=false KSP returns receivable space, not stored amount.
                    return Math.Max(0, amount);
                }
                return resources.Where(CanPut).Sum(r => Math.Max(0, r.maxAmount - r.amount));
            }
        }
        public double Take(double request)
        {
            Numbers.Nonnegative(request, "request");
            if (request == 0) return 0;
            if (requestPart != null) return requestPart.RequestResource(resourceId, request, flowMode);
            double left = request;
            foreach (var r in resources)
            {
                if (!CanTake(r))
                    continue;
                double n = Math.Min(left, Math.Max(0, r.amount));
                r.amount -= n;
                left -= n;
                if (left <= 0)
                    break;
            }
            return request - left;
        }
        public double Put(double offer)
        {
            Numbers.Nonnegative(offer, "offer");
            if (offer == 0) return 0;
            // KSP reports negative accepted amounts for a negative (storage) demand.
            if (requestPart != null) return -requestPart.RequestResource(resourceId, -offer, flowMode);
            double left = offer;
            foreach (var r in resources)
            {
                if (!CanPut(r))
                    continue;
                double n = Math.Min(left, Math.Max(0, r.maxAmount - r.amount));
                r.amount += n;
                left -= n;
                if (left <= 0)
                    break;
            }
            return offer - left;
        }
    }
#if !GN_ADAPTER_TEST
    internal sealed class KspBaryonPort : IBaryonPort
    {
        sealed class Source
        {
            public PartResourceDefinition Definition;
            public double ECPerUnit;
        }
        readonly Part requestPart;
        readonly Source[] sources;
        double energyCredit;
        public KspBaryonPort(Vessel vessel, IEnumerable<string> resourceNames)
        {
            requestPart = vessel == null ? null : vessel.rootPart;
            if (requestPart == null) throw new InvalidOperationException("TD Blanket requires a vessel root Part");
            var list = new List<Source>();
            foreach (string name in resourceNames ?? Enumerable.Empty<string>())
            {
                var definition = PartResourceLibrary.Instance.GetDefinition(name);
                if (definition == null)
                {
                    Debug.LogWarning("[GN V3] TD Blanket resource is not defined: " + name);
                    continue;
                }
                if (!Numbers.Finite(definition.density) || definition.density <= 0)
                {
                    Debug.LogWarning("[GN V3] TD Blanket resource has no positive mass: " + name);
                    continue;
                }
                list.Add(new Source { Definition = definition, ECPerUnit = MassEnergy.ECPerResourceUnit(definition.density) });
            }
            sources = list.ToArray();
        }
        static double MeasurableQuantum(double amount)
        {
            if (amount <= 0) return 0;
            long bits = BitConverter.DoubleToInt64Bits(amount);
            double previous = BitConverter.Int64BitsToDouble(bits - 1);
            return Math.Max(1e-18, 2 * (amount - previous));
        }
        public double TakeEnergy(double requestedEC)
        {
            Numbers.Nonnegative(requestedEC, "requestedEC");
            double needed = Math.Max(0, requestedEC - energyCredit);
            foreach (var source in sources)
            {
                if (needed <= 0) break;
                double amount, capacity;
                requestPart.GetConnectedResourceTotals(source.Definition.id, source.Definition.resourceFlowMode, out amount, out capacity, true);
                amount = Math.Max(0, amount);
                if (amount <= 0) continue;
                // Dense KSP resources can require less than one double ULP per tick.
                // Prepay a tiny but representable quantum and retain unused energy.
                double requestedUnits = Math.Min(amount, Math.Max(needed / source.ECPerUnit, MeasurableQuantum(amount)));
                double acceptedUnits = requestPart.RequestResource(source.Definition.id, requestedUnits, source.Definition.resourceFlowMode);
                if (!Numbers.Finite(acceptedUnits) || acceptedUnits < 0)
                    throw new InvalidOperationException("Invalid TD Blanket resource receipt: " + source.Definition.name);
                acceptedUnits = Math.Min(requestedUnits, acceptedUnits);
                energyCredit += acceptedUnits * source.ECPerUnit;
                needed = Math.Max(0, requestedEC - energyCredit);
            }
            double supplied = Math.Min(requestedEC, energyCredit);
            energyCredit -= supplied;
            return supplied;
        }
    }

    public class GNDriveV3Vessel : VesselModule
    {
        readonly VesselSimulation simulation = new VesselSimulation();
        readonly List<GNModule> modules = new List<GNModule>();
        readonly List<Rigidbody> bodies = new List<Rigidbody>();
        readonly Dictionary<Part, AeroState> protection = new Dictionary<Part, AeroState>();
        readonly List<PendingCommand> pending = new List<PendingCommand>();
        readonly CommandGate commandGate = new CommandGate();
        readonly Dictionary<string, Vessel> targets = new Dictionary<string, Vessel>();
        VesselSettings settings = new VesselSettings();
        KspResourcePort particles, ec;
        KspBaryonPort baryons;
        bool dirty = true, failed;
        double nextBoundsTime, lastTick = double.NaN;
        public double Radius { get; private set; } = 2;
        public StepResult LastResult
        {
            get; private set;
        }
        GNShieldV3Visuals fieldVisuals; bool fieldVisible; GNAeroVisualSuppression aeroVisuals;
        GNVesselRedGlow vesselGlow;
        float vesselGlowTarget;
        Vector3 afterimageAcceleration;
        float vesselGlowTransparency = 0.8f;
        struct AeroState
        {
            public float Maximum, Minimum, Scalar;
        }
        sealed class PendingCommand
        {
            public GNModule Module; public CommandKind Kind; public bool Value;
        }
        public override bool ShouldBeActive()
        {
            return vessel != null && vessel.loaded;
        }
        protected override void OnStart()
        {
            base.OnStart();
            GameEvents.onVesselWasModified.Add(VesselChanged);
            GameEvents.onVesselGoOffRails.Add(VesselChanged);
            GameEvents.onVesselGoOnRails.Add(OnRails);
            GameEvents.onVesselChange.Add(ActiveChanged);
            GameEvents.onPartDie.Add(PartChanged);
            GameEvents.onPartResourceListChange.Add(PartChanged);
        }
        public void Invalidate()
        {
            dirty = true;
        }
        void VesselChanged(Vessel v)
        {
            if (v == vessel)
                dirty = true;
        }
        void PartChanged(Part p)
        {
            if (p != null && p.vessel == vessel)
                dirty = true;
        }
        void OnRails(Vessel v)
        {
            if (v == vessel)
            {
                dirty = true;
                ClearProtection();
                SetFieldVisible(false);
            }
        }
        void ActiveChanged(Vessel v)
        {
            if (v != vessel)
            {
                ClearProtection();
                SetFieldVisible(false);
            }
        }
        public void Enqueue(GNModule m, CommandKind kind, bool value)
        {
            if (failed)
            {
                m.status = "Coordinator stopped; reload after fixing configuration.";
                return;
            }
            string scope = kind == CommandKind.Field || kind == CommandKind.SafeGuard ? "vessel" : m.State.Joined && kind != CommandKind.Join && kind != CommandKind.Repose ? "group0" : m.State.Id.ToString();
            if (commandGate.Accept(Planetarium.GetUniversalTime(), scope, kind))
                pending.Add(new PendingCommand { Module = m, Kind = kind, Value = value });
        }
        void Rebuild()
        {
            ClearProtection();
            if (vesselGlow != null)
                vesselGlow.Rebuild();
            modules.Clear();
            bodies.Clear();
            modules.AddRange(vessel.FindPartModulesImplementing<GNModule>().OrderBy(x => x.part.persistentId));
            if (modules.Any(m => m.ConfigurationError != null))
                throw new InvalidOperationException("GNModule configuration error");
            if (modules.Any(m => m.State == null))
            {
                dirty = true;
                return;
            }
            if (modules.Count > 0)
            {
                var leader = modules[0];
                settings.SafeGuard = leader.safeGuard;
                settings.Field = leader.fieldOn;
                settings.AccelerationG = leader.accelerationG;
                settings.RotationShare = leader.rotationShare;
                settings.ECMargin = leader.ecMargin;
                particles = new KspResourcePort(vessel, "GNparticle");
                ec = new KspResourcePort(vessel, "ElectricCharge");
                baryons = new KspBaryonPort(vessel, GNTDBlanketSettings.Current.ResourceNames);
            }
            var seen = new HashSet<Rigidbody>();
            foreach (var part in vessel.parts)
                if (part.rb != null && seen.Add(part.rb))
                    bodies.Add(part.rb);
            dirty = false;
            nextBoundsTime = 0;
        }
        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || !vessel.loaded || failed)
                return;
            double tick = Planetarium.GetUniversalTime();
            if (tick == lastTick)
                return;
            lastTick = tick;
            try
            {
                if (dirty)
                    Rebuild();
                if (dirty || modules.Count == 0)
                    return;
                foreach (var m in modules)
                    m.ReadResources();
                var states = modules.Select(m => m.State).ToList();
                // First changed control in stable-ID order wins a simultaneous conflicting edit.
                var accel = modules.FirstOrDefault(m => m.accelerationG != m.PublishedAcceleration);
                var rotation = modules.FirstOrDefault(m => m.rotationShare != m.PublishedRotation);
                var margin = modules.FirstOrDefault(m => m.ecMargin != m.PublishedECMargin);
                if (margin != null) settings.ECMargin = Numbers.Clamp(margin.ecMargin, 0, 1);
                if (accel != null)
                    settings.AccelerationG = accel.accelerationG;
                if (rotation != null)
                    settings.RotationShare = rotation.rotationShare;
                string commandMessage = null;
                foreach (var cmd in pending)
                {
                    if (cmd.Module == null || cmd.Module.vessel != vessel)
                        continue;
                    string reason;
                    Commands.Apply(states, settings, cmd.Module.State.Id, cmd.Kind, cmd.Value, out reason);
                    if (reason != null)
                        commandMessage = reason;
                }
                pending.Clear();
                var input = ReadInput();
                var result = simulation.Step(states, settings, input, particles, ec, baryons);
                // Clamp to installed propulsion capability, not currently running capability.
                // Engine shutdown and depletion therefore do not erase the preset.
                settings.AccelerationG = Numbers.Clamp(settings.AccelerationG, 0, result.Effects.ConfigurationMaxG);
                LastResult = result;
                if (input.Active && !input.Packed)
                    ApplyEffects(input, result);
                else
                {
                    ClearProtection();
                    SetFieldVisible(false);
                }
                Vec sharedRgb = DriveColorPolicy.SharedNormal(states);
                double visualPower = states.Where(d => d.Running).Sum(GNPropulsionUnit.Power);
                double visualLoad = VisualPolicy.Load(input, result.Effects, visualPower);
                double usedPower = result.Effects.SupportKN.Length + result.Effects.BrakeKN.Length + result.Effects.ThrustKN.Length + result.Effects.TorqueRate + result.Effects.FieldRepulsionRate;
                double particleLoad = Math.Max(visualLoad, visualPower > 0 ? Numbers.Clamp(usedPower / visualPower, 0, 1) : 0);
                bool vesselTransAMColor = GNTransAMUnit.AccelerationMultiplier(states) > 1;
                var glowSettings = GNVesselGlowSettings.Current;
                bool glowActive = glowSettings.Enabled && modules.Any(m => m.State != null && m.State.Running && m.State.TA && m.State.Definition.TransAM);
                vesselGlowTarget = glowActive ? glowSettings.Intensity : 0;
                vesselGlowTransparency = glowSettings.Transparency;
                afterimageAcceleration = input.Active && !input.Packed && input.MassKg > 0 && result.ForceN.Length > 0
                    ? ToUnity(result.ForceN / input.MassKg + input.Gravity) : Vector3.zero;
                if (vesselGlowTarget > 0 && vesselGlow == null)
                    vesselGlow = new GNVesselRedGlow(vessel, glowSettings.ShellScale, glowSettings.ResponseSpeed, glowSettings.TransAMColor, glowSettings.ExcludedObjectKeywords);
                foreach (var m in modules)
                {
                    m.Publish(settings, result, input.Dt);
                    if (commandMessage != null)
                        m.status = commandMessage;
                    Vec zeroTDColor = new Vec(m.GeneratorTint.r, m.GeneratorTint.g, m.GeneratorTint.b);
                    Vec normalRgb = DriveColorPolicy.NormalFor(m.State, sharedRgb, zeroTDColor);
                    Color color = new Color((float)normalRgb.X, (float)normalRgb.Y, (float)normalRgb.Z);
                    if (m.Visuals != null)
                    {
                        bool blanketGenerating = m.State.Blanket && m.State.BlanketECGenerated > 0;
                        Vec backflow = ParticleVisualPolicy.BackflowAcceleration(result.ForceN, m.State.Running ? Mathf.Clamp(m.ParticleAcceleration, 0, 200) : 0);
                        m.Visuals.SetState(color, (m.State.Running || m.State.Generated > 0), VisualPolicy.Open(m.State.Running, settings.SafeGuard), (float)VisualPolicy.Rotor(m.State.Running, blanketGenerating, visualLoad), (float)VisualPolicy.Core(m.State.Running, settings.SafeGuard, visualLoad), m.State.Running ? (float)particleLoad : 0, ToUnity(backflow), vesselTransAMColor);
                    }
                }
            }
            catch (Exception e)
            {
                failed = true;
                vesselGlowTarget = 0;
                pending.Clear();
                ClearProtection();
                SetFieldVisible(false);
                foreach (var m in modules)
                    if (m != null)
                    {
                        m.status = "ERROR: " + e.Message;
                        if (m.Visuals != null)
                            m.Visuals.SetState(Color.red, false, false);
                    }
                Debug.LogError("[GN V3] Coordinator stopped for " + vessel.vesselName + ": " + e);
            }
        }
        StepInput ReadInput()
        {
            var i = new StepInput
            {
                Dt = TimeWarp.fixedDeltaTime,
                MassKg = vessel.GetTotalMass() * 1000,
                Active = vessel.isActiveVessel,
                Packed = vessel.packed,
                Gravity = ToCore(FlightGlobals.getGeeForceAtPosition(vessel.CoM)),
                Velocity = ToCore(vessel.srf_velocity)
            };
            if (Time.realtimeSinceStartup >= nextBoundsTime)
            {
                MeasureBounds();
                nextBoundsTime = Time.realtimeSinceStartup + 0.5;
            }
            i.FieldDiameter = 2 * Radius;
            targets.Clear();
            var frame = vessel.ReferenceTransform;
            var c = vessel.ctrlState;
            if (frame != null && c != null)
            {
                i.Throttle = c.mainThrottle;
                i.Forward = ToCore(frame.up);
                i.Rcs = vessel.ActionGroups[KSPActionGroup.RCS];
                i.Brakes = vessel.ActionGroups[KSPActionGroup.Brakes];
                // KSP control frame: throttle along +up; translation follows the stock X/Y/Z mapping.
                i.Translation = ToCore(-frame.right * c.X - frame.forward * c.Y - frame.up * c.Z);
                i.Rotation = KspControlFrame.Rotation(ToCore(frame.right), ToCore(frame.up), ToCore(frame.forward), c.pitch, c.yaw, c.roll);
            }
            if (i.Active && !i.Packed && settings.Field)
            {
                foreach (var other in FlightGlobals.VesselsLoaded.OrderBy(x => x.id))
                {
                    if (other == vessel || other == null || other.packed || !other.loaded || other.rootPart == null || other.rootPart.rb == null)
                        continue;
                    var manager = other.FindVesselModuleImplementing<GNDriveV3Vessel>();
                    double radius = manager != null && manager.modules.Count > 0 ? manager.Radius : 2;
                    Vec offset = ToCore(other.CoM - vessel.CoM);
                    double penetration = Radius + radius - offset.Length;
                    if (penetration <= 0)
                        continue;
                    string id = other.id.ToString();
                    targets[id] = other;
                    i.Targets.Add(new FieldTarget { Id = id, ForceN = GNFieldUnit.Repulsion(offset, ToCore(other.srf_velocity - vessel.srf_velocity), penetration, other.GetTotalMass() * 1000) });
                }
            }
            return i;
        }
        void MeasureBounds()
        {
            double radius = 0.5;
            foreach (var p in vessel.parts)
            {
                radius = Math.Max(radius, (p.transform.position - (Vector3)vessel.CoM).magnitude);
                foreach (var renderer in p.FindModelComponents<Renderer>())
                {
                    if (renderer == null || !renderer.enabled || renderer is ParticleSystemRenderer || renderer.name.StartsWith("GN V3 Shield", StringComparison.Ordinal) || renderer.name.StartsWith("GN V3 Vessel Glow", StringComparison.Ordinal) || !renderer.gameObject.activeInHierarchy)
                        continue;
                    var bounds = renderer.bounds;
                    radius = Math.Max(radius, (bounds.center - (Vector3)vessel.CoM).magnitude + bounds.extents.magnitude);
                }
            }
            Radius = radius;
        }
        void ApplyEffects(StepInput input, StepResult result)
        {
            if (input.MassKg <= 0)
                return;
            Vec acceleration = result.ForceN / input.MassKg;
            double rbMass = bodies.Where(b => b != null).Sum(b => (double)b.mass);
            foreach (var rb in bodies)
            {
                if (rb == null)
                    continue;
                // KSP Rigidbody masses are tonnes: rb.mass * m/s2 is kN in Unity's KSP scale.
                rb.AddForce(ToUnity(acceleration) * rb.mass, ForceMode.Force);
                if (rbMass > 0)
                    rb.AddTorque(ToUnity(result.Effects.TorqueNm) * (float)(0.001 * rb.mass / rbMass), ForceMode.Force);
            }
            if (result.Effects.Field)
            {
                Protect();
                SetFieldVisible(true);
                foreach (var target in result.Effects.Targets)
                {
                    Vessel other;
                    if (!targets.TryGetValue(target.Id, out other) || other == null || other.packed)
                        continue;
                    var rbs = new HashSet<Rigidbody>();
                    foreach (var p in other.parts)
                        if (p.rb != null)
                            rbs.Add(p.rb);
                    double mass = rbs.Sum(b => (double)b.mass);
                    if (mass <= 0)
                        continue;
                    foreach (var rb in rbs)
                        rb.AddForce(ToUnity(target.ForceN) * (float)(0.001 * rb.mass / mass), ForceMode.Force);
                }
                double speed = input.Velocity.Length;
                if (speed > 0.1 && vessel.atmDensity > 0)
                {
                    // V2 empirical v^1.5 law, converted from N to KSP units. Natural drag is not a GN expense.
                    double drag = 0.5 * vessel.atmDensity * Math.Pow(speed, 1.5) * 0.2 * Math.PI * Radius * Radius;
                    Vec a = -input.Velocity.Unit * (drag / input.MassKg);
                    foreach (var rb in bodies)
                        if (rb != null)
                            rb.AddForce(ToUnity(a) * rb.mass, ForceMode.Force);
                }
            }
            else
            {
                ClearProtection();
                SetFieldVisible(false);
            }
        }
        void Protect()
        {
            foreach (var p in vessel.parts)
            {
                if (protection.ContainsKey(p) || p.ShieldedFromAirstream)
                    continue;
                protection[p] = new AeroState { Maximum = p.maximum_drag, Minimum = p.minimum_drag, Scalar = p.dragScalar };
                p.ShieldedFromAirstream = true;
                p.maximum_drag = p.minimum_drag = p.dragScalar = 0;
            }
        }
        void ClearProtection()
        {
            foreach (var entry in protection)
            {
                var p = entry.Key;
                if (p == null)
                    continue;
                p.ShieldedFromAirstream = false;
                p.maximum_drag = entry.Value.Maximum;
                p.minimum_drag = entry.Value.Minimum;
                p.dragScalar = entry.Value.Scalar;
            }
            protection.Clear();
        }
        void SetFieldVisible(bool visible)
        {
            fieldVisible = visible;
            if (visible && aeroVisuals == null)
            {
                aeroVisuals = vessel.gameObject.AddComponent<GNAeroVisualSuppression>();
                aeroVisuals.Owner = vessel;
                aeroVisuals.ShouldSuppress = () => fieldVisible && !failed && isActiveAndEnabled;
            }
            if (!visible && aeroVisuals != null) aeroVisuals.Restore();
            if (visible && fieldVisuals == null) fieldVisuals = new GNShieldV3Visuals(vessel.transform);
        }
        public void LateUpdate()
        {
            if (vesselGlow != null)
            {
                bool showGlow = HighLogic.LoadedSceneIsFlight && vessel != null && vessel == FlightGlobals.ActiveVessel && !vessel.packed && !failed;
                vesselGlow.SetTarget(showGlow ? vesselGlowTarget : 0, vesselGlowTransparency);
                vesselGlow.Update(Time.deltaTime);
                vesselGlow.UpdateAfterimages(Time.deltaTime, showGlow && vesselGlowTarget > 0, afterimageAcceleration, (float)(2 * Radius));
            }
            if (fieldVisuals == null || vessel == null) return;
            if (!HighLogic.LoadedSceneIsFlight || vessel != FlightGlobals.ActiveVessel || vessel.packed || failed)
            { fieldVisuals.Hide(); return; }
            Vec sharedRgb = DriveColorPolicy.SharedNormal(modules.Select(m => m.State));
            Color c = new Color((float)sharedRgb.X, (float)sharedRgb.Y, (float)sharedRgb.Z);
            fieldVisuals.Update((Vector3)vessel.CoM, (float)Radius, c, fieldVisible);
        }
        internal static Vec ToCore(Vector3d v)
        {
            return new Vec(v.x, v.y, v.z);
        }
        internal static Vector3 ToUnity(Vec v)
        {
            return new Vector3((float)v.X, (float)v.Y, (float)v.Z);
        }
        public void OnDisable()
        {
            if (fieldVisuals != null) fieldVisuals.Hide();
            vesselGlowTarget = 0;
            if (vesselGlow != null) vesselGlow.Restore();
            ClearProtection();
            SetFieldVisible(false);
        }
        public void OnDestroy()
        {
            GameEvents.onVesselWasModified.Remove(VesselChanged);
            GameEvents.onVesselGoOffRails.Remove(VesselChanged);
            GameEvents.onVesselGoOnRails.Remove(OnRails);
            GameEvents.onVesselChange.Remove(ActiveChanged);
            GameEvents.onPartDie.Remove(PartChanged);
            GameEvents.onPartResourceListChange.Remove(PartChanged);
            ClearProtection();
            if (fieldVisuals != null) fieldVisuals.Dispose();
            if (vesselGlow != null) vesselGlow.Dispose();
            if (aeroVisuals != null) { aeroVisuals.Restore(); Destroy(aeroVisuals); }
        }
    }
    // Displaced current-pose silhouettes; never clone Part components or colliders.
    internal sealed class GNAfterimageVisuals : IDisposable
    {
        sealed class Entry
        {
            public Renderer Source;
            public Mesh Baked;
            public GameObject[] Copies;
        }
        readonly List<Entry> entries = new List<Entry>();
        readonly Material[] materials;
        float accelerationTime;
        Vector3 direction;
        public GNAfterimageVisuals(Renderer[] sources, Material template)
        {
            materials = new Material[GNVesselGlowSettings.Current.AfterimageCount];
            for (int i = 0; i < materials.Length; i++) materials[i] = new Material(template);
            if (materials.Length == 0) return;
            foreach (var source in sources)
            {
                if (source == null) continue;
                var skin = source as SkinnedMeshRenderer;
                var filter = source.GetComponent<MeshFilter>();
                Mesh mesh = skin != null ? skin.sharedMesh : filter == null ? null : filter.sharedMesh;
                if (mesh == null) continue;
                var entry = new Entry { Source = source, Copies = new GameObject[materials.Length] };
                if (skin != null) { entry.Baked = new Mesh(); mesh = entry.Baked; }
                for (int i = 0; i < materials.Length; i++)
                {
                    var go = new GameObject("GN V3 Vessel Glow Afterimage");
                    go.SetActive(false);
                    go.layer = source.gameObject.layer;
                    go.transform.SetParent(source.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = go.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = Enumerable.Repeat(materials[i], source.sharedMaterials.Length).ToArray();
                    renderer.receiveShadows = false;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    entry.Copies[i] = go;
                }
                entries.Add(entry);
            }
        }
        public void Update(float dt, bool enabled, Vector3 acceleration, float vesselDiameter)
        {
            var settings = GNVesselGlowSettings.Current;
            bool accelerating = enabled && settings.AfterimagesEnabled && acceleration.magnitude >= settings.AfterimageThreshold;
            if (accelerating) direction = -acceleration.normalized;
            accelerationTime = (float)AfterimagePolicy.Advance(accelerationTime, settings.AfterimageExtendTime, dt, accelerating);
            float extension = (float)AfterimagePolicy.Extension(accelerationTime, settings.AfterimageExtendTime);
            // Reverse the same curve on release, retaining direction as silhouettes catch up.
            for (int i = 0; i < materials.Length; i++)
            {
                Color c = settings.TransAMColor;
                c.a = settings.AfterimageOpacity * extension * (materials.Length - i) / materials.Length;
                materials[i].SetColor("_TintColor", c);
            }
            foreach (var entry in entries)
            {
                bool visible = extension > 0.001f && entry.Source != null && entry.Source.enabled && entry.Source.gameObject.activeInHierarchy;
                if (visible && entry.Baked != null) ((SkinnedMeshRenderer)entry.Source).BakeMesh(entry.Baked);
                for (int i = 0; i < materials.Length; i++)
                {
                    var go = entry.Copies[i];
                    if (go == null) continue;
                    go.SetActive(visible);
                    if (!visible) continue;
                    // Field diameter excludes all generated effects, preventing recursive size growth.
                    go.transform.position = entry.Source.transform.position + direction * extension * (float)AfterimagePolicy.Distance(vesselDiameter, settings.AfterimageSizeDivisor, settings.AfterimageSpacing, i);
                }
            }
        }
        public void Hide()
        {
            accelerationTime = 0;
            foreach (var e in entries) foreach (var go in e.Copies) if (go != null) go.SetActive(false);
        }
        public void Dispose()
        {
            Hide();
            foreach (var e in entries)
            {
                foreach (var go in e.Copies) if (go != null) UnityEngine.Object.Destroy(go);
                if (e.Baked != null) UnityEngine.Object.Destroy(e.Baked);
            }
            entries.Clear();
            foreach (var m in materials) if (m != null) UnityEngine.Object.Destroy(m);
        }
    }
    internal sealed class GNVesselRedGlow : IDisposable
    {
        readonly Vessel vessel;
        readonly List<GameObject> shells = new List<GameObject>();
        readonly List<GlowSlot> slots = new List<GlowSlot>();
        Renderer[] renderers = new Renderer[0];
        Material shellMaterial, noGlowMaterial;
        Texture2D shellTexture;
        readonly float shellScale, responseSpeed;
        readonly Color transAMColor;
        readonly string[] excludedObjectKeywords;
        float target, level, transparency = 0.8f;
        bool warnedMissingShells;
        GNAfterimageVisuals afterimages;
        sealed class GlowSlot
        {
            public Renderer Renderer;
            public int MaterialIndex;
        }

        public GNVesselRedGlow(Vessel owner, float configuredShellScale, float configuredResponseSpeed, Color configuredColor, string[] configuredExcludedObjectKeywords)
        {
            vessel = owner;
            shellScale = Mathf.Clamp(configuredShellScale, 1, 1.1f);
            responseSpeed = Mathf.Clamp(configuredResponseSpeed, 0.01f, 20);
            transAMColor = new Color(Mathf.Clamp01(configuredColor.r), Mathf.Clamp01(configuredColor.g), Mathf.Clamp01(configuredColor.b), 1);
            excludedObjectKeywords = configuredExcludedObjectKeywords ?? new string[0];
            Rebuild();
        }
        public void Rebuild()
        {
            Restore();
            if (afterimages != null) { afterimages.Dispose(); afterimages = null; }
            DestroyShells();
            slots.Clear();
            warnedMissingShells = false;
            var candidates = vessel == null || vessel.parts == null
                ? new Renderer[0]
                : vessel.parts.SelectMany(p => p == null ? Enumerable.Empty<Renderer>() : p.FindModelComponents<Renderer>().AsEnumerable())
                    .Where(r => r != null && !(r is ParticleSystemRenderer) && !r.name.StartsWith("GN V3 Shield", StringComparison.Ordinal) && !r.name.StartsWith("GN V3 Vessel Glow", StringComparison.Ordinal) && !IsMechanismRenderer(r) && !IsExcludedObject(r))
                    .Distinct().ToArray();
            foreach (var renderer in candidates)
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    slots.Add(new GlowSlot { Renderer = renderer, MaterialIndex = i });
            }
            renderers = slots.Select(s => s.Renderer).Distinct().ToArray();
            BuildShells();
            if (shellMaterial != null && GNVesselGlowSettings.Current.AfterimagesEnabled)
                afterimages = new GNAfterimageVisuals(renderers, shellMaterial);
        }
        public void UpdateAfterimages(float dt, bool enabled, Vector3 acceleration, float vesselDiameter)
        {
            if (afterimages != null) afterimages.Update(dt, enabled, acceleration, vesselDiameter);
        }
        bool IsExcludedObject(Renderer renderer)
        {
            Part part = renderer == null ? null : renderer.GetComponentInParent<Part>();
            if (part == null)
                return false;
            for (Transform t = renderer.transform; t != null && t != part.transform; t = t.parent)
            {
                if (string.Equals(t.name, "flare", StringComparison.OrdinalIgnoreCase))
                    return true;
                foreach (string keyword in excludedObjectKeywords)
                    if (t.name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
            }
            return false;
        }
        static bool IsMechanismRenderer(Renderer renderer)
        {
            Part part = renderer == null ? null : renderer.GetComponentInParent<Part>();
            if (part == null)
                return false;
            GNModule module = part.FindModuleImplementing<GNModule>();
            for (Transform t = renderer.transform; t != null && t != part.transform; t = t.parent)
                if (VisualPolicy.Named(t.name, "GN_Rotor_") || VisualPolicy.Named(t.name, "GN_Stator_") ||
                    VisualPolicy.Named(t.name, "GN_CondenserRotor_") || VisualPolicy.Named(t.name, "GN_CondenserStator_") ||
                    (module != null && !string.IsNullOrEmpty(module.RotorTransform) && t.name == module.RotorTransform))
                    return true;
            return false;
        }
        void BuildShells()
        {
            if (shellMaterial == null)
            {
                Shader shader = Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Particles/Additive");
                if (shader == null)
                {
                    Debug.LogWarning("[GN V3] Additive vessel glow shader unavailable; vessel glow disabled.");
                    return;
                }
                shellTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                shellTexture.SetPixel(0, 0, Color.white);
                shellTexture.Apply(false, true);
                shellMaterial = new Material(shader);
                shellMaterial.mainTexture = shellTexture;
                shellMaterial.SetColor("_TintColor", new Color(1, 0, 0, 0));
                noGlowMaterial = new Material(shader);
                noGlowMaterial.mainTexture = shellTexture;
                noGlowMaterial.SetColor("_TintColor", new Color(0, 0, 0, 0));
            }
            foreach (var source in renderers)
            {
                if (source == null)
                    continue;
                var sourceMaterials = source.sharedMaterials;
                var glowMaterials = new Material[sourceMaterials.Length];
                bool anyGlow = false;
                for (int i = 0; i < glowMaterials.Length; i++)
                {
                    bool eligible = slots.Any(s => s.Renderer == source && s.MaterialIndex == i);
                    glowMaterials[i] = eligible ? shellMaterial : noGlowMaterial;
                    anyGlow |= eligible;
                }
                if (!anyGlow)
                    continue;
                GameObject go = null;
                var skinned = source as SkinnedMeshRenderer;
                if (skinned != null && skinned.sharedMesh != null)
                {
                    go = new GameObject("GN V3 Vessel Glow Skinned");
                    go.transform.SetParent(source.transform.parent, false);
                    go.transform.localPosition = source.transform.localPosition;
                    go.transform.localRotation = source.transform.localRotation;
                    go.transform.localScale = source.transform.localScale * shellScale;
                    var copy = go.AddComponent<SkinnedMeshRenderer>();
                    copy.sharedMesh = skinned.sharedMesh;
                    copy.bones = skinned.bones;
                    copy.rootBone = skinned.rootBone;
                    copy.updateWhenOffscreen = skinned.updateWhenOffscreen;
                    copy.sharedMaterials = glowMaterials;
                    copy.receiveShadows = false;
                }
                else
                {
                    var filter = source.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null)
                        continue;
                    go = new GameObject("GN V3 Vessel Glow Mesh");
                    go.transform.SetParent(source.transform, false);
                    go.transform.localScale = Vector3.one * shellScale;
                    var copyFilter = go.AddComponent<MeshFilter>();
                    copyFilter.sharedMesh = filter.sharedMesh;
                    var copy = go.AddComponent<MeshRenderer>();
                    copy.sharedMaterials = glowMaterials;
                    copy.receiveShadows = false;
                }
                go.layer = source.gameObject.layer;
                go.SetActive(false);
                shells.Add(go);
            }
        }
        void DestroyShells()
        {
            foreach (var shell in shells)
                if (shell != null)
                    UnityEngine.Object.Destroy(shell);
            shells.Clear();
        }
        public void SetTarget(float value, float requestedTransparency)
        {
            target = Mathf.Max(0, value);
            transparency = Mathf.Clamp01(requestedTransparency);
        }
        public void Update(float dt)
        {
            level = Mathf.MoveTowards(level, target, Mathf.Max(0, dt) * responseSpeed);
            if (level <= 0.0001f)
            {
                level = 0;
                Restore();
                return;
            }
            if (shellMaterial != null)
            {
                // Additive output is RGB * alpha. This piecewise form keeps the
                // configured intensity linear without dimming both channels below 1.
                float rgbIntensity = Mathf.Max(1, level);
                float alpha = Mathf.Clamp01(level) * (1 - transparency);
                shellMaterial.SetColor("_TintColor", new Color(transAMColor.r * rgbIntensity, transAMColor.g * rgbIntensity, transAMColor.b * rgbIntensity, alpha));
            }
            int activeShells = 0;
            foreach (var shell in shells)
                if (shell != null)
                {
                    if (!shell.activeSelf)
                        shell.SetActive(true);
                    if (shell.activeInHierarchy)
                        activeShells++;
                }
            if (activeShells == 0 && !warnedMissingShells)
            {
                warnedMissingShells = true;
                Debug.LogWarning("[GN V3] Vessel glow requested but no active glow shells are available; renderers=" + renderers.Length + " shells=" + shells.Count + " level=" + level);
            }
            else if (activeShells > 0)
                warnedMissingShells = false;
        }
        public void Restore()
        {
            if (afterimages != null) afterimages.Hide();
            foreach (var shell in shells)
                if (shell != null)
                    shell.SetActive(false);
        }
        public void Dispose()
        {
            Restore();
            if (afterimages != null) { afterimages.Dispose(); afterimages = null; }
            DestroyShells();
            if (shellMaterial != null) UnityEngine.Object.Destroy(shellMaterial);
            if (noGlowMaterial != null) UnityEngine.Object.Destroy(noGlowMaterial);
            if (shellTexture != null) UnityEngine.Object.Destroy(shellTexture);
            renderers = new Renderer[0];
        }
    }
    // Run after stock aerodynamic LateUpdate. Restore before its next LateUpdate so
    // KSP remains the authority for normal state; never change heating or drag inputs.
    [DefaultExecutionOrder(10000)]
    public sealed class GNAeroVisualSuppression : MonoBehaviour
    {
        public Vessel Owner;
        public Func<bool> ShouldSuppress;
        AerodynamicsFX stock;
        Camera camera;
        Light light;
        FXCamera fx;
        Renderer trailRenderer;
        bool cameraEnabled, lightEnabled, trailHidden, captured;
        float intensity, nextSearch;
        public void Update() { Restore(); }
        public void LateUpdate()
        {
            Restore();
            if (Owner == null || Owner != FlightGlobals.ActiveVessel || Owner.packed || !HighLogic.LoadedSceneIsFlight || ShouldSuppress == null || !ShouldSuppress()) return;
            if (stock == null && Time.unscaledTime >= nextSearch)
            {
                stock = UnityEngine.Object.FindObjectOfType<AerodynamicsFX>();
                nextSearch = Time.unscaledTime + 1;
            }
            fx = stock != null ? stock.fxCamera : null;
            camera = fx != null ? fx.GetComponent<Camera>() : null;
            light = stock != null ? stock.fxLight : null;
            var trail = Owner.GetComponent<ReentryFXParticleTrail>();
            trailRenderer = trail != null && trail.pSys != null ? trail.pSys.GetComponent<ParticleSystemRenderer>() : null;
            captured = true;
            if (fx != null) { intensity = fx.intensity; fx.intensity = 0; }
            if (camera != null) { cameraEnabled = camera.enabled; camera.enabled = false; }
            if (light != null) { lightEnabled = light.enabled; light.enabled = false; }
            if (trailRenderer != null) { trailHidden = trailRenderer.forceRenderingOff; trailRenderer.forceRenderingOff = true; }
        }
        public void Restore()
        {
            if (!captured) return;
            if (fx != null) fx.intensity = intensity;
            if (camera != null) camera.enabled = cameraEnabled;
            if (light != null) light.enabled = lightEnabled;
            if (trailRenderer != null) trailRenderer.forceRenderingOff = trailHidden;
            captured = false;
        }
        public void OnDisable() { Restore(); }
        public void OnDestroy() { Restore(); }
    }
    // Colour-over-lifetime affects already living particles and uses the particle
    // vertex colour, avoiding shader-specific material tint property names.
    internal sealed class GNParticleTint
    {
        readonly Gradient gradient = new Gradient();
        readonly GradientColorKey[] rgb = new GradientColorKey[2];
        readonly GradientAlphaKey[] alpha;
        Color previous;
        bool initialized;
        public GNParticleTint(float peakTime, float peakAlpha)
        { alpha = new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(peakAlpha, peakTime), new GradientAlphaKey(0, 1) }; }
        public void Apply(ParticleSystem particles, Color color)
        {
            if (!initialized || color != previous)
            {
                rgb[0] = new GradientColorKey(color, 0);
                rgb[1] = new GradientColorKey(color, 1);
                gradient.SetKeys(rgb, alpha);
                previous = color; initialized = true;
            }
            var fade = particles.colorOverLifetime;
            fade.enabled = true;
            fade.color = gradient;
        }
    }
    // One visual shell per vessel; no colliders or gameplay resource operations.
    internal sealed class GNShieldV3Visuals : IDisposable
    {
        readonly GameObject root, sphere;
        readonly Mesh mesh;
        readonly Vector3[] vertices, normals;
        readonly Color[] colors;
        readonly Material membrane, specks;
        readonly Texture2D grain, dot;
        readonly ParticleSystem particles;
        readonly GNParticleTint particleTint = new GNParticleTint(0.15f, 0.7f);
        float strength;
        readonly VisualColorTransition colorTransition = new VisualColorTransition();
        public GNShieldV3Visuals(Transform parent)
        {
            root = new GameObject("GN V3 Shield");
            root.transform.SetParent(parent, false);
            root.SetActive(false);
            sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "GN V3 Shield membrane";
            sphere.transform.SetParent(root.transform, false);
            var collider = sphere.GetComponent<Collider>();
            collider.enabled = false;
            UnityEngine.Object.Destroy(collider);
            var filter = sphere.GetComponent<MeshFilter>();
            mesh = UnityEngine.Object.Instantiate(filter.sharedMesh);
            mesh.MarkDynamic();
            filter.sharedMesh = mesh;
            vertices = mesh.vertices; normals = mesh.normals; colors = new Color[vertices.Length];
            var shader = Shader.Find("KSP/Particles/Alpha Blended") ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            var renderer = sphere.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (shader != null)
            {
                grain = MakeTexture(true);
                membrane = new Material(shader);
                membrane.mainTexture = grain;
                membrane.mainTextureScale = new Vector2(8, 4);
                membrane.SetColor("_TintColor", Color.white);
                renderer.sharedMaterial = membrane;
            }
            else { renderer.enabled = false; Debug.LogWarning("[GN V3] Shield membrane shader missing."); }
            var emitter = new GameObject("GN V3 Shield specks");
            emitter.transform.SetParent(root.transform, false);
            particles = emitter.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.playOnAwake = false; main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startLifetime = 0.4f; main.startSpeed = 0;
            main.startSize = new ParticleSystem.MinMaxCurve(0.00125f, 0.00375f);
            main.maxParticles = 10000;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.5f; shape.radiusThickness = 0.1f;
            var emission = particles.emission; emission.rateOverTime = 10000; emission.enabled = false;
            var noise = particles.noise; noise.enabled = true; noise.strength = 0.0015f; noise.frequency = 3; noise.scrollSpeed = 0.25f;
            var fade = particles.colorOverLifetime; fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.7f, 0.15f), new GradientAlphaKey(0, 1) });
            fade.color = gradient;
            var pr = particles.GetComponent<ParticleSystemRenderer>();
            pr.renderMode = ParticleSystemRenderMode.Billboard;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pr.receiveShadows = false;
            shader = Shader.Find("KSP/Particles/Additive") ?? Shader.Find("Legacy Shaders/Particles/Additive");
            if (shader != null)
            {
                dot = MakeTexture(false); specks = new Material(shader);
                specks.mainTexture = dot; specks.SetColor("_TintColor", Color.white); pr.sharedMaterial = specks;
            }
            else { pr.enabled = false; Debug.LogWarning("[GN V3] Shield particle shader missing."); }
            particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        static Texture2D MakeTexture(bool granular)
        {
            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            tex.wrapMode = granular ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            var rng = new System.Random(1701);
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                float dx = (x - 31.5f) / 31.5f, dy = (y - 31.5f) / 31.5f;
                float a = granular ? 0.45f + 0.55f * (float)rng.NextDouble() : Mathf.Pow(Mathf.Clamp01(1 - dx * dx - dy * dy), 3);
                tex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            tex.Apply(false, true); return tex;
        }
        public void Update(Vector3 center, float radius, Color color, bool on)
        {
            Vec blended = colorTransition.Step(new Vec(color.r, color.g, color.b), Time.deltaTime);
            color = new Color((float)blended.X, (float)blended.Y, (float)blended.Z);
            strength = Mathf.MoveTowards(strength, on ? 1 : 0, Time.deltaTime * 0.5f);
            if (!on && strength <= 0) { Hide(); return; }
            root.transform.position = center;
            root.transform.localScale = Vector3.one * (2 * Mathf.Max(0.01f, radius));
            root.SetActive(true);
            var main = particles.main; main.startColor = Color.white;
            particleTint.Apply(particles, color);
            var emission = particles.emission; emission.enabled = on;
            if (on && !particles.isPlaying) particles.Play(false);
            if (!on && particles.isPlaying) particles.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            var fc = FlightCamera.fetch;
            var camera = fc != null ? fc.mainCamera : null;
            if (camera == null) { sphere.SetActive(false); return; }
            sphere.SetActive(true);
            var t = sphere.transform;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 view = (camera.transform.position - t.TransformPoint(vertices[i])).normalized;
                float ndv = Vector3.Dot(t.TransformDirection(normals[i]).normalized, view);
                colors[i] = new Color(color.r, color.g, color.b, (float)ShieldAppearance.RimAlpha(ndv, strength));
            }
            mesh.colors = colors;
            if (membrane != null) membrane.mainTextureOffset = new Vector2(Time.time * 0.017f, Time.time * 0.011f);
        }
        public void Hide()
        {
            strength = 0;
            particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            root.SetActive(false);
        }
        public void Dispose()
        {
            UnityEngine.Object.Destroy(root); UnityEngine.Object.Destroy(mesh);
            if (membrane != null) UnityEngine.Object.Destroy(membrane);
            if (specks != null) UnityEngine.Object.Destroy(specks);
            if (grain != null) UnityEngine.Object.Destroy(grain);
            if (dot != null) UnityEngine.Object.Destroy(dot);
        }
    }
    internal sealed class GNDriveV3Visuals : IDisposable
    {
        readonly GNModule owner;
        readonly Transform[] rotors, deploy, arms, condenserRotors;
        readonly Quaternion[] coreRotations, armRotations;
        readonly float[] armAngles;
        readonly Vector3[] positions;
        readonly Renderer[] renderers, condenserRenderers;
        readonly Light[] lights;
        readonly LightState[] lightStates;
        readonly ParticleSystem[] particles, particleGlows;
        readonly KSPParticleEmitter[] legacy;
        Material particleMaterial; Texture2D particleTexture;
        readonly GNParticleTint particleTint = new GNParticleTint(0.08f, 0.8f);
        readonly GNParticleTint particleGlowTint = new GNParticleTint(0.2f, 1f);
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        AudioSource audio;
        Color color = Color.green, targetColor = Color.green;
        readonly VisualColorTransition colorTransition = new VisualColorTransition();
        bool active, open, transAM; float level, extension, rotorTarget, coreTarget, particleTarget, particleLevel, condenserLevel, transAMLevel;
        Vector3 particleAccelerationWorld;
        struct LightState
        {
            public bool Enabled;
            public Color Color;
            public float Intensity, Range;
        }
        public GNDriveV3Visuals(GNModule module)
        {
            owner = module;
            var transforms = module.part.GetComponentsInChildren<Transform>(true).Where(t => t.GetComponentInParent<Part>() == module.part).ToArray();
            condenserRotors = transforms.Where(t => VisualPolicy.Named(t.name, "GN_CondenserRotor_")).ToArray();
            condenserRenderers = transforms.Where(t => VisualPolicy.Named(t.name, "GN_CondenserRotor_") || VisualPolicy.Named(t.name, "GN_CondenserStator_"))
                .SelectMany(t => t.GetComponentsInChildren<Renderer>(true)).Where(r => r.GetComponentInParent<Part>() == module.part && !(r is ParticleSystemRenderer)).Distinct().ToArray();
            rotors = transforms.Where(t => !VisualPolicy.Named(t.name, "GN_CondenserRotor_") && !VisualPolicy.Named(t.name, "GN_CondenserStator_") && !VisualPolicy.Named(t.name, "GN_Stator_")).Where(t => VisualPolicy.Named(t.name, "GN_Rotor_") || (!string.IsNullOrEmpty(module.RotorTransform) && t.name == module.RotorTransform)).ToArray();
            deploy = transforms.Where(t => VisualPolicy.Named(t.name, "GN_Core_") || (!string.IsNullOrEmpty(module.DeployTransform) && t.name == module.DeployTransform)).ToArray();
            arms = transforms.Where(t => VisualPolicy.Named(t.name, "GN_Arm_")).ToArray();
            positions = deploy.Select(t => t.localPosition).ToArray();
            coreRotations = deploy.Select(t => t.localRotation).ToArray();
            armRotations = arms.Select(t => t.localRotation).ToArray();
            armAngles = new float[arms.Length];
            renderers = transforms.Where(t => VisualPolicy.Named(t.name, "GN_Rotor_") || VisualPolicy.Named(t.name, "GN_Stator_") || (!string.IsNullOrEmpty(module.EmissiveTransform) && t.name == module.EmissiveTransform))
                .SelectMany(t => t.GetComponentsInChildren<Renderer>(true)).Where(r => r.GetComponentInParent<Part>() == module.part && !(r is ParticleSystemRenderer)).Distinct().ToArray();
            renderers = renderers.Except(condenserRenderers).ToArray();
            lights = transforms.Where(t => t.name == "GNLight" || VisualPolicy.Named(t.name, "GNLight_"))
                .SelectMany(t => t.GetComponentsInChildren<Light>(true)).Where(l => l.GetComponentInParent<Part>() == module.part).Distinct().ToArray();
            lightStates = lights.Select(l => new LightState { Enabled = l.enabled, Color = l.color, Intensity = l.intensity, Range = l.range }).ToArray();
            var particleRoots = string.IsNullOrEmpty(module.ParticleTransform) ? new Transform[0] : module.part.FindModelTransforms(module.ParticleTransform);
            legacy = particleRoots.SelectMany(t => t.GetComponentsInChildren<KSPParticleEmitter>(true))
                .Where(p => p.GetComponentInParent<Part>() == module.part).Distinct().ToArray();
            // Retire the selected model effects. Future models need only an empty anchor.
            foreach (var e in legacy) { e.emit = false; e.enabled = false; }
            foreach (var p in particleRoots.SelectMany(t => t.GetComponentsInChildren<ParticleSystem>(true)).Distinct())
                if (p.GetComponentInParent<Part>() == module.part) p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles = particleRoots.Where(t => t.GetComponentInParent<Part>() == module.part).Select(CreateParticles).ToArray();
            particleGlows = particleRoots.Where(t => t.GetComponentInParent<Part>() == module.part).Select(CreateParticleGlow).ToArray();
            Debug.Log("[GN V3] Particle anchors for " + module.part.name + ": " + module.ParticleTransform + " count=" + particles.Length);
            if (!string.IsNullOrEmpty(module.AudioPath))
            {
                var clip = GameDatabase.Instance.GetAudioClip(module.AudioPath);
                if (clip != null)
                {
                    audio = module.gameObject.AddComponent<AudioSource>();
                    audio.clip = clip;
                    audio.loop = true;
                    audio.spatialBlend = 1;
                    audio.playOnAwake = false;
                    audio.volume = 0;
                }
            }
        }
        ParticleSystem CreateParticles(Transform anchor)
        {
            var go = new GameObject("GN V3 particles");
            go.SetActive(false);
            go.transform.SetParent(anchor, false);
            // Unity cone emits along +Z; the model contract uses anchor -Y.
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, Vector3.down);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 4096;
            main.startLifetime = Mathf.Clamp(owner.ParticleLifetime, 0.05f, 10f);
            main.startSpeed = Mathf.Clamp(owner.ParticleSpeed, 0, 100);
            float size = Mathf.Clamp(owner.ParticleSize, 0.001f, 1f);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size * 1.5f);
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = Mathf.Clamp(owner.ParticleConeAngle, 0, 80);
            shape.radius = Mathf.Clamp(owner.ParticleRadius, 0, 2);
            var emission = ps.emission;
            emission.rateOverTime = Mathf.Clamp(owner.ParticleRate, 0, 3000);
            emission.enabled = false;
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.8f, 0.08f), new GradientAlphaKey(0, 1) });
            fade.color = gradient;
            if (particleMaterial == null)
            {
                var shader = Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Particles/Additive");
                if (shader != null)
                {
                    particleTexture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
                    particleTexture.wrapMode = TextureWrapMode.Clamp;
                    for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
                    {
                        float dx = (x - 15.5f) / 15.5f, dy = (y - 15.5f) / 15.5f;
                        float a = Mathf.Pow(Mathf.Clamp01(1 - dx * dx - dy * dy), 3);
                        particleTexture.SetPixel(x, y, new Color(1, 1, 1, a));
                    }
                    particleTexture.Apply(false, true);
                    particleMaterial = new Material(shader);
                    particleMaterial.mainTexture = particleTexture;
                    particleMaterial.SetColor("_TintColor", Color.white);
                }
                else Debug.LogWarning("[GN V3] Additive particle shader unavailable; particles hidden.");
            }
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = particleMaterial;
            renderer.enabled = particleMaterial != null;
            go.SetActive(true);
            return ps;
        }

        ParticleSystem CreateParticleGlow(Transform anchor)
        {
            var go = new GameObject("GN V3 ring glow");
            go.SetActive(false);
            go.transform.SetParent(anchor, false);
            // The circle is perpendicular to the same -Y exhaust axis as the plume.
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, Vector3.down);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 256;
            main.startLifetime = 0.4f;
            main.startSpeed = 0.02f;
            float size = Mathf.Clamp(owner.ParticleGlowSize, 0.01f, 2f);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.7f, size * 1.3f);
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = Mathf.Clamp(owner.ParticleRadius, 0, 2);
            var emission = ps.emission;
            emission.rateOverTime = Mathf.Clamp(owner.ParticleGlowRate, 0, 1000);
            emission.enabled = false;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = size * 0.08f;
            noise.frequency = 2f;
            noise.scrollSpeed = 0.2f;
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = particleMaterial;
            renderer.enabled = particleMaterial != null;
            go.SetActive(true);
            return ps;
        }

public void ApplyParticleSettings(ParticleSystem p, float size, float rate, float speed, float lifetime, float angle, float radius)
{
    float cSize = Mathf.Clamp(size, 0.001f, 1f);
    float cRate = Mathf.Clamp(rate, 0f, 3000f);
    float cSpeed = Mathf.Clamp(speed, 0f, 100f);
    float cLifetime = Mathf.Clamp(lifetime, 0.05f, 10f);
    float cAngle = Mathf.Clamp(angle, 0f, 80f);
    float cRadius = Mathf.Clamp(radius, 0f, 2f);

    var main = p.main;
    main.startSize = new ParticleSystem.MinMaxCurve(cSize * 0.5f, cSize * 1.5f);
    main.startSpeed = cSpeed;
    main.startLifetime = cLifetime;

    var shape = p.shape;
    shape.angle = cAngle;
    shape.radius = cRadius;

    var emission = p.emission;
    emission.rateOverTime = cRate;
}

        void ApplyParticleGlowSettings(ParticleSystem p, float load)
        {
            float size = Mathf.Clamp(owner.ParticleGlowSize, 0.01f, 2f);
            float density = Mathf.Clamp(owner.ParticleGlowRate, 0, 1000) * Mathf.Lerp(0.2f, 1f, load);
            var main = p.main;
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.7f, size * 1.3f);
            main.startColor = new Color(1, 1, 1, Mathf.Clamp01(owner.ParticleGlowIntensity * Mathf.Lerp(0.25f, 1f, load)));
            var shape = p.shape;
            shape.radius = Mathf.Clamp(owner.ParticleRadius, 0, 2);
            var emission = p.emission;
            emission.rateOverTime = density;
        }

        void ApplyParticleAcceleration(ParticleSystem p)
        {
            var force = p.forceOverLifetime;
            float factor = (float)ParticleVisualPolicy.BackflowFactor(particleLevel);
            Vector3 acceleration = particleAccelerationWorld * factor;
            bool enabled = active && acceleration.sqrMagnitude > 1e-8f;
            force.enabled = enabled;
            if (!enabled)
                return;
            Vector3 local = p.transform.InverseTransformDirection(acceleration);
            force.space = ParticleSystemSimulationSpace.Local;
            force.x = new ParticleSystem.MinMaxCurve(local.x);
            force.y = new ParticleSystem.MinMaxCurve(local.y);
            force.z = new ParticleSystem.MinMaxCurve(local.z);
        }

        public void SetState(Color c, bool running, bool deployed, float rotationLevel = 0, float coreLevel = 0, float particleLoad = 0, Vector3 particleAcceleration = default(Vector3), bool transAMActive = false)
        {
            targetColor = c;
            active = running;
            open = deployed;
            particleAccelerationWorld = particleAcceleration;
            transAM = transAMActive;
            rotorTarget = rotationLevel; coreTarget = coreLevel; particleTarget = Mathf.Clamp01(particleLoad);
        }
        public void Update()
        {
            float dt = Time.deltaTime;
            Vec blended = colorTransition.Step(new Vec(targetColor.r, targetColor.g, targetColor.b), dt);
            color = new Color((float)blended.X, (float)blended.Y, (float)blended.Z);

            level = Mathf.MoveTowards(level, rotorTarget, dt * Mathf.Max(0.01f, owner.VisualResponse));
            transAMLevel = Mathf.MoveTowards(transAMLevel, transAM ? 1 : 0, dt * Mathf.Max(0.01f, owner.VisualResponse));
            particleLevel = Mathf.MoveTowards(particleLevel, particleTarget, dt * Mathf.Max(0.01f, owner.VisualResponse));
            float speed = (float)ParticleVisualPolicy.Blend(Mathf.Clamp(owner.ParticleIdleSpeed, 0, owner.ParticleSpeed), owner.ParticleSpeed, particleLevel);
            float rate = (float)ParticleVisualPolicy.Blend(Mathf.Clamp(owner.ParticleIdleRate, 0, owner.ParticleRate), owner.ParticleRate, particleLevel);
            float angle = (float)ParticleVisualPolicy.Blend(Mathf.Clamp(owner.ParticleIdleAngle, owner.ParticleConeAngle, 80), owner.ParticleConeAngle, particleLevel);
            rate = (float)ParticleVisualPolicy.SafeRate(Mathf.Clamp(rate, 0, 3000), Mathf.Clamp(owner.ParticleLifetime, 0.05f, 10), 4096);
            owner.particleVisualLoad = particleLevel;
            owner.particleEffectiveRate = active ? rate : 0;
            float distance = owner.VisualValue("MoveDistance", owner.DeployDistance);
            extension = Mathf.MoveTowards(extension, coreTarget * distance, dt * Mathf.Max(0.001f, owner.CoreSpeed));
            // Rotor and stator are siblings under the core. Stator transforms remain untouched.
            foreach (var t in rotors)
                if (t != null && t.gameObject.activeInHierarchy)
                    t.Rotate(Vector3.up, (owner.rotorTuningOverride ? owner.RotorSpeed : owner.VisualValue("RotorSpeed", owner.RotorSpeed)) * level * dt, Space.Self);
            for (int i = 0; i < deploy.Length; i++)
                if (deploy[i] != null)
                    deploy[i].localPosition = positions[i] + coreRotations[i] * Vector3.down * extension;
            for (int i = 0; i < arms.Length; i++)
                if (arms[i] != null)
                {
                    armAngles[i] = Mathf.MoveTowards(armAngles[i], open ? owner.VisualValue("RotationAngle", owner.RotationAngle) : 0, Mathf.Max(0.01f, owner.ArmSpeed) * dt);
                    arms[i].localRotation = armRotations[i] * Quaternion.AngleAxis(armAngles[i], Vector3.right);
                }
            // Local tank only; independent of propulsion, TA power and SafeGuard.
            var tank = owner.part.Resources.Get("GNparticle");
            float fill = tank == null ? 0 : (float)VisualPolicy.CondenserLevel(tank.amount, tank.maxAmount);
            condenserLevel = Mathf.MoveTowards(condenserLevel, fill, dt * Mathf.Max(0.01f, owner.VisualResponse));
            foreach (var t in condenserRotors)
                if (t != null && t.gameObject.activeInHierarchy)
                    t.Rotate(Vector3.up, Mathf.Max(0, owner.CondenserRotorSpeed) * condenserLevel * dt, Space.Self);
            var visualSettings = GNVesselGlowSettings.Current;
            Color transAMEmission = visualSettings.TransAMCoreColor * visualSettings.TransAMCoreIntensity;
            foreach (var r in condenserRenderers)
                if (r != null)
                {
                    r.GetPropertyBlock(block);
                    Color emission = Color.Lerp(color * condenserLevel, transAMEmission, transAMLevel);
                    block.SetColor("_EmissiveColor", emission);
                    block.SetColor("_EmissionColor", emission);
                    r.SetPropertyBlock(block);
                }
            foreach (var r in renderers)
                if (r != null)
                {
                    r.GetPropertyBlock(block);
                    Color emission = Color.Lerp(color * level, transAMEmission, transAMLevel);
                    block.SetColor("_EmissiveColor", emission);
                    block.SetColor("_EmissionColor", emission);
                    r.SetPropertyBlock(block);
                }
            foreach (var l in lights)
                if (l != null)
                {
                    float blend = Mathf.Clamp01(dt * Mathf.Max(0.01f, owner.VisualResponse));
                    float normalLevel = active ? level : 0;
                    float desiredIntensity = visualSettings.GNLightEnabled ? Mathf.Lerp(visualSettings.GNLightIntensity * normalLevel, visualSettings.GNLightTransAMIntensity, transAMLevel) : 0;
                    float desiredRange = visualSettings.GNLightEnabled ? Mathf.Lerp(visualSettings.GNLightRange * normalLevel, visualSettings.GNLightTransAMRange, transAMLevel) : 0;
                    Color desiredColor = Color.Lerp(color, visualSettings.TransAMColor, transAMLevel);
                    l.intensity = Mathf.Lerp(l.intensity, desiredIntensity, blend);
                    l.range = Mathf.Lerp(l.range, desiredRange, blend);
                    l.color = Color.Lerp(l.color, desiredColor, blend);
                    l.enabled = desiredIntensity > 0.01f || l.intensity > 0.01f;
                }
            foreach (var p in particles)
                if (p != null)
                {
                    ApplyParticleSettings(p, owner.ParticleSize, rate, speed, owner.ParticleLifetime, angle, owner.ParticleRadius);
                    ApplyParticleAcceleration(p);
                    var main = p.main;
                    main.startColor = Color.white;
                    particleTint.Apply(p, color);
                    var emission = p.emission;
                    emission.enabled = active;
                    if (active && !p.isPlaying)
                        p.Play(false);
                    if (!active && p.isPlaying)
                        p.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                }
            foreach (var p in particleGlows)
                if (p != null)
                {
                    ApplyParticleGlowSettings(p, particleLevel);
                    particleGlowTint.Apply(p, color);
                    var emission = p.emission;
                    emission.enabled = active && owner.ParticleGlowIntensity > 0 && owner.ParticleGlowRate > 0;
                    if (emission.enabled && !p.isPlaying)
                        p.Play(false);
                    if (!emission.enabled && p.isPlaying)
                        p.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                }
            foreach (var p in legacy)
                if (p != null)
                {
                    p.emit = false;
                    p.enabled = false;
                }
            if (audio != null)
            {
                audio.volume = level * GameSettings.SHIP_VOLUME;
                if (active && !audio.isPlaying)
                    audio.Play();
                if (!active)
                    audio.Stop();
            }
        }
        public void Dispose()
        {
            for (int i = 0; i < lights.Length; i++)
            {
                var light = lights[i];
                if (light == null)
                    continue;
                light.enabled = lightStates[i].Enabled;
                light.color = lightStates[i].Color;
                light.intensity = lightStates[i].Intensity;
                light.range = lightStates[i].Range;
            }
            foreach (var p in particles) if (p != null) UnityEngine.Object.Destroy(p.gameObject);
            foreach (var p in particleGlows) if (p != null) UnityEngine.Object.Destroy(p.gameObject);
            if (particleMaterial != null) UnityEngine.Object.Destroy(particleMaterial);
            if (particleTexture != null) UnityEngine.Object.Destroy(particleTexture);
            if (audio != null)
                UnityEngine.Object.Destroy(audio);
        }
    }
#endif
}
#endif
