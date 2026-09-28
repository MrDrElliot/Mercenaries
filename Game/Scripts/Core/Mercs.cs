using System;
using System.Collections.Generic;
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

    public static readonly List<Soldier> Soldiers = new();
    public static readonly List<Vehicle> Vehicles = new();
    public static readonly List<Structure> Structures = new();
    public static readonly List<Pickup> Pickups = new();
    public static readonly List<Site> Sites = new();
    public static readonly Dictionary<uint, IDamageable> Damageables = new();

    public static bool IsRunning => Director is not null;

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
