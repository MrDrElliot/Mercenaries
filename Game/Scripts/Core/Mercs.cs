using System;
using System.Collections.Generic;
using System.Linq;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

public enum EFaction : byte
{
    None,
    Merc,
    VZ,
    Allied,
    China,
    Oil,
    Guerrilla,
    Pirate,
    Civilian,
}

public enum EMood : byte
{
    Hostile,
    Neutral,
    Friendly,
}

public enum EDamageKind : byte
{
    Bullet,
    Explosive,
    Melee,
    Crush,
    Fire,
}

public struct FHit
{
    public float Amount;
    public EDamageKind Kind;
    public FVector3 Point;
    public FVector3 Direction;
    public EFaction SourceFaction;
    public bool bByPlayer;
    public Entity Source;

    public static FHit From(IDamageable? Attacker, float Amount, EDamageKind Kind, FVector3 Point, FVector3 Direction)
    {
        return new FHit
        {
            Amount = Amount,
            Kind = Kind,
            Point = Point,
            Direction = Direction,
            SourceFaction = Attacker?.Faction ?? EFaction.None,
            bByPlayer = Attacker is not null && Attacker.IsPlayerControlled,
            Source = Attacker?.Owner ?? Entity.Null,
        };
    }
}

public interface IDamageable
{
    Entity Owner { get; }
    EFaction Faction { get; }
    bool IsAlive { get; }
    bool IsPlayerControlled { get; }
    FVector3 Position { get; }
    float Radius { get; }
    void TakeHit(in FHit Hit);
}

// Process-wide state for the one running game, rebuilt by GameDirector every time a world starts.
public static class Mercs
{
    public static CWorld World = null!;
    public static GameDirector? Director;
    public static MercPlayer? Player;
    public static Random Rng = new(1977);
    public static float Time;

    public static FactionSystem Factions = new();
    public static Wallet Wallet = new();
    public static ContractBoard Contracts = new();
    public static SupportSystem Support = new();
    public static NewsFeed Feed = new();
    public static FxSystem Fx = new();
    public static OrdnanceSystem Ordnance = new();
    public static ThrowableSystem Throwables = new();
    public static DestructionSystem Destruction = new();
    public static DayNight Clock = new();
    public static Trailer Trailer = new();

    public static readonly List<Soldier> Soldiers = new();
    public static readonly List<Vehicle> Vehicles = new();
    public static readonly List<Structure> Structures = new();
    public static readonly List<Pickup> Pickups = new();
    public static readonly List<Site> Sites = new();
    public static readonly Dictionary<uint, IDamageable> Damageables = new();

    public static bool IsRunning => Director is not null;

    // The vehicle navmesh is baked wider than the infantry one, and queries name it to get paths a truck fits through.
    public const string VehicleNavAgent = "Vehicle";
    public static bool bInfantryNavReady;
    public static readonly AiStatistics AiStats = new();
    public static bool bVehicleNavReady;

    public static void Reset(CWorld NewWorld, GameDirector NewDirector)
    {
        World = NewWorld;
        Director = NewDirector;
        Player = null;
        Rng = new Random(1977);
        Time = 0.0f;
        Factions = new FactionSystem();
        Wallet = new Wallet();
        Contracts = new ContractBoard();
        Support = new SupportSystem();
        Feed = new NewsFeed();
        Fx = new FxSystem();
        Ordnance = new OrdnanceSystem();
        Throwables = new ThrowableSystem();
        Destruction = new DestructionSystem();
        Trailer = new Trailer();
        Soldiers.Clear();
        Vehicles.Clear();
        Structures.Clear();
        Pickups.Clear();
        Sites.Clear();
        Damageables.Clear();
    }

    public static void Shutdown()
    {
        Director = null;
        Player = null;
        Soldiers.Clear();
        Vehicles.Clear();
        Structures.Clear();
        Pickups.Clear();
        Sites.Clear();
        Damageables.Clear();
    }

    public static void Register(IDamageable Target)
    {
        Damageables[Target.Owner.Id] = Target;
    }

    public static void Unregister(IDamageable Target)
    {
        if (Damageables.TryGetValue(Target.Owner.Id, out IDamageable? Existing) && ReferenceEquals(Existing, Target))
        {
            Damageables.Remove(Target.Owner.Id);
        }
    }

    public static IDamageable? FindDamageable(Entity Hit)
    {
        if (Hit.IsNull)
        {
            return null;
        }

        Entity Cursor = Hit;
        for (int Depth = 0; Depth < 6 && !Cursor.IsNull; ++Depth)
        {
            if (Damageables.TryGetValue(Cursor.Id, out IDamageable? Found))
            {
                return Found;
            }

            if (!World.IsValidEntity(Cursor))
            {
                return null;
            }

            Cursor = World.GetParent(Cursor);
        }

        return null;
    }

    public static float Range(float Min, float Max) => Min + (float)Rng.NextDouble() * (Max - Min);

    public static int RangeInt(int Min, int MaxExclusive) => Rng.Next(Min, MaxExclusive);

    public static bool Chance(float Probability) => Rng.NextDouble() < Probability;

    public static T Pick<T>(IReadOnlyList<T> Items) => Items[Rng.Next(Items.Count)];

    public static FVector3 PlayerPosition => Player is not null ? Player.Position : FVector3.Zero;
}

// Time soldiers spend trying to move without getting anywhere, reported to the log so navigation changes can be measured.
public sealed class AiStatistics
{
    private const float ReportInterval = 15.0f;

    public float StalledSeconds;
    private float SinceReport;
    private readonly Dictionary<string, float> ByCause = new();

    public void AddStall(ESoldierState State, bool bOnPath, float DeltaTime)
    {
        StalledSeconds += DeltaTime;
        string Cause = $"{State}/{(bOnPath ? "path" : "direct")}";
        ByCause[Cause] = ByCause.GetValueOrDefault(Cause) + DeltaTime;
    }

    public void Update(float DeltaTime)
    {
        SinceReport += DeltaTime;
        if (SinceReport < ReportInterval)
        {
            return;
        }

        string Causes = string.Join(", ", ByCause.Select(Pair => $"{Pair.Key} {Pair.Value:0.0}"));
        LuminaSharp.Debug.Log($"[AI] {Mercs.Soldiers.Count} soldiers stalled for {StalledSeconds:0.0} soldier-seconds over {SinceReport:0}s ({Causes})");
        StalledSeconds = 0.0f;
        ByCause.Clear();
        SinceReport = 0.0f;
    }
}
