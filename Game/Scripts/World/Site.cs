using System;
using System.Collections.Generic;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

public enum ESiteKind : byte
{
    PmcHq,
    FactionHq,
    Outpost,
    Capital,
    Village,
    FuelDepot,
    Refinery,
    Harbor,
    Camp,
}

public sealed class FGarrisonSlot
{
    public ESoldierRole Role;
    public FVector3 Spot;
    public List<FVector3>? Patrol;
}

public sealed class FVehicleSlot
{
    public EVehicleType Type;
    public FVector3 Spot;
    public float Yaw;
    public bool bCrewed;
    public List<FVector3>? Route;
    public Vehicle? Live;
    public bool bLost;
}

// A named place on the island that owns its buildings and streams its garrison in around the player.
public sealed class Site
{
    public const float ActivateRange = 230.0f;
    public const float DeactivateRange = 320.0f;

    public string Name = string.Empty;
    public ESiteKind Kind;
    public EFaction Owner;
    public EFaction OriginalOwner;
    public FVector3 Center;
    public float Radius = 40.0f;
    public FVector3? Contact;
    public FVector3 PlayerSpawn;
    public HvtTarget? Hvt;

    public readonly List<FGarrisonSlot> Garrison = new();
    public readonly List<FVehicleSlot> VehicleSlots = new();
    public readonly List<Structure> Buildings = new();
    public readonly List<Soldier> Members = new();

    public bool bActive;
    public bool bCleared;
    public int Killed;
    private float InactiveTime;
    private Entity Flag = Entity.Null;
    private FVector3 FlagBase;
    private Entity ContactMarker = Entity.Null;

    public bool CanBeLiberated => Kind is ESiteKind.Outpost or ESiteKind.Camp && OriginalOwner == EFaction.VZ;

    public int AliveMembers
    {
        get
        {
            int Count = 0;
            foreach (Soldier Member in Members)
            {
                if (Member.IsAlive)
                {
                    ++Count;
                }
            }

            return Count;
        }
    }

    public void PlaceFlag(FVector3 At)
    {
        FlagBase = At;
        RebuildFlag();
    }

    private void RebuildFlag()
    {
        if (FlagBase == FVector3.Zero)
        {
            return;
        }

        if (Flag.IsNull || !Mercs.World.IsValidEntity(Flag))
        {
            Flag = Mercs.World.CreateEntity($"Flag_{Name}", FlagBase);
        }

        FactionInfo Info = Mercs.Factions.Get(Owner);
        MeshKit Kit = new();
        Kit.Tube(FVector3.Zero, new FVector3(0.0f, 9.0f, 0.0f), 0.08f, 0.06f, Palette.Steel, 6);
        Kit.Box(new FVector3(1.1f, 8.2f, 0.0f), new FVector3(1.1f, 0.7f, 0.03f), Info.Color);
        Kit.Box(new FVector3(1.1f, 8.2f, 0.0f), new FVector3(0.35f, 0.35f, 0.04f), Info.Accent);
        Kit.Commit(Mercs.World.Registry, Flag, false, false);
    }

    public void PlaceContact(FVector3 At)
    {
        Contact = At;
        CWorld World = Mercs.World;
        Entity Desk = World.CreateEntity($"Contact_{Name}", At);
        MeshKit Kit = new();
        FactionInfo Info = Mercs.Factions.Get(Owner);
        Kit.Block(new FVector3(0.0f, 0.0f, 0.0f), new FVector3(1.8f, 0.9f, 0.9f), Palette.WoodDark);
        Kit.Block(new FVector3(0.0f, 0.9f, 0.0f), new FVector3(0.6f, 0.35f, 0.05f), Palette.Hex(0x1B2A33));
        Kit.Block(new FVector3(-1.4f, 0.0f, -0.6f), new FVector3(0.12f, 2.4f, 0.12f), Palette.Steel);
        Kit.Box(new FVector3(-1.4f, 2.6f, -0.6f), new FVector3(0.7f, 0.35f, 0.03f), Info.Color);
        Kit.Commit(World.Registry, Desk);
        ContactMarker = HumanoidBody.BuildMarker(World, Desk, EMarker.Contact, 2.6f);
    }

    public void Update(float DeltaTime)
    {
        HumanoidBody.AnimateMarker(Mercs.World, ContactMarker, 2.6f);

        float Distance = Geo.FlatDistance(Mercs.PlayerPosition, Center);
        if (!bActive && Distance < ActivateRange)
        {
            Activate();
        }
        else if (bActive && Distance > DeactivateRange && !AnyoneFighting())
        {
            Deactivate();
        }

        if (!bActive)
        {
            InactiveTime += DeltaTime;
            if (InactiveTime > 240.0f && !bCleared)
            {
                Killed = 0;
                foreach (FVehicleSlot Slot in VehicleSlots)
                {
                    Slot.bLost = false;
                }
            }
        }
    }

    private bool AnyoneFighting()
    {
        foreach (Soldier Member in Members)
        {
            if (Member.IsAlive && Member.IsInCombat)
            {
                return true;
            }
        }

        return false;
    }

    private void Activate()
    {
        bActive = true;
        InactiveTime = 0.0f;
        EFaction Team = Owner;

        int ToSpawn = Math.Max(0, Garrison.Count - Killed);
        for (int Index = 0; Index < ToSpawn && Index < Garrison.Count; ++Index)
        {
            FGarrisonSlot Slot = Garrison[Index];
            Soldier? Member = Spawner.SpawnSoldier(Team, Slot.Role, Slot.Spot, this, Slot.Patrol);
            if (Member is not null)
            {
                Member.Home = Slot.Spot;
            }
        }

        foreach (FVehicleSlot Slot in VehicleSlots)
        {
            if (Slot.bLost || Slot.Live is { IsAlive: true })
            {
                continue;
            }

            Slot.Live = Spawner.SpawnVehicle(Slot.Type, Kind == ESiteKind.Village ? EFaction.Civilian : Team, Slot.Spot, Slot.Yaw, Slot.bCrewed && !bCleared, Slot.Route, this);
        }

        if (Hvt is not null && !Hvt.bResolved && Hvt.Live is null)
        {
            Hvt.Spawn(this);
        }
    }

    private void Deactivate()
    {
        bActive = false;
        foreach (Soldier Member in Members.ToArray())
        {
            if (Member.IsAlive && !Member.bIsHvt)
            {
                Mercs.World.DestroyEntity(Member.Owner);
            }
        }

        Members.RemoveAll(Member => !Member.bIsHvt);

        foreach (FVehicleSlot Slot in VehicleSlots)
        {
            if (Slot.Live is { } Ride && Ride.IsAlive && !Ride.IsPlayerControlled && FVector3.Distance(Ride.Position, Mercs.PlayerPosition) > DeactivateRange * 0.8f)
            {
                Mercs.World.DestroyEntity(Ride.Owner);
                Slot.Live = null;
            }
        }
    }

    public void AlertSquad(IDamageable Threat, Soldier Source)
    {
        foreach (Soldier Member in Members)
        {
            if (Member != Source && Member.IsAlive && FVector3.DistanceSquared(Member.Position, Source.Position) < 70.0f * 70.0f)
            {
                Member.OnSquadAlert(Threat);
            }
        }
    }

    public void Forget(Soldier Member)
    {
        Members.Remove(Member);
    }

    public void Forget(Vehicle Ride)
    {
        foreach (FVehicleSlot Slot in VehicleSlots)
        {
            if (Slot.Live == Ride)
            {
                Slot.Live = null;
            }
        }
    }

    public void OnVehicleLost(Vehicle Ride)
    {
        foreach (FVehicleSlot Slot in VehicleSlots)
        {
            if (Slot.Live == Ride)
            {
                Slot.bLost = true;
            }
        }
    }

    public void OnStructureLost(Structure Building)
    {
        if (Building.Kind == EStructureKind.Statue && OriginalOwner == EFaction.VZ)
        {
            Mercs.Factions.ChangeStanding(EFaction.Guerrilla, 6.0f, "toppled a VZ statue");
            Mercs.Wallet.AddCash(10000, "statue toppled");
        }
    }

    public void OnMemberDied(Soldier Member)
    {
        if (Member.bIsHvt)
        {
            return;
        }

        Killed++;
        if (!CanBeLiberated || bCleared || AliveMembers > 0 || Owner != EFaction.VZ)
        {
            return;
        }

        bCleared = true;
        Owner = EFaction.Guerrilla;
        RebuildFlag();
        Mercs.Wallet.AddCash(8000, $"{Name} liberated");
        Mercs.Factions.ChangeStanding(EFaction.Guerrilla, 5.0f, $"liberated {Name}");
        Mercs.Feed.Announce($"{Name.ToUpperInvariant()} LIBERATED", "PLAV guerrillas are moving in.", 3.5f);
        Mercs.Contracts.OnSiteCleared(this);
        Killed = Garrison.Count / 2;
    }
}

public static class Spawner
{
    public static Soldier? SpawnSoldier(EFaction Team, ESoldierRole Role, FVector3 At, Site? Home, List<FVector3>? Patrol = null)
    {
        CWorld World = Mercs.World;
        FVector3 Ground = Geo.Ground(At);
        FVector3 Spot = Ground + new FVector3(0.0f, HumanoidBody.FeetOffset + 0.1f, 0.0f);
        Entity Handle = World.CreateEntity($"{Mercs.Factions.Get(Team).Short}_{Role}", Spot);
        Soldier? Member = World.Registry.AddScript<Soldier>(Handle);
        if (Member is null)
        {
            World.DestroyEntity(Handle);
            return null;
        }

        Member.Team = Team;
        Member.Role = Role;
        Member.HomeSite = Home;
        Member.Home = Ground;
        Member.PatrolRoute = Patrol;
        Member.MaxHealth = Role switch
        {
            ESoldierRole.Civilian => 60.0f,
            ESoldierRole.Gunner => 150.0f,
            ESoldierRole.Officer => 140.0f,
            _ => 100.0f,
        };
        Member.SightRange = Role == ESoldierRole.Sniper ? 120.0f : 65.0f;
        Member.RunSpeed = Role == ESoldierRole.Civilian ? 4.5f : (Role == ESoldierRole.Gunner ? 4.2f : 5.2f);
        Member.DisplayName = $"{Mercs.Factions.Get(Team).Short} {Role}";
        Home?.Members.Add(Member);
        return Member;
    }

    public static Vehicle? SpawnVehicle(EVehicleType Type, EFaction Team, FVector3 At, float Yaw, bool bCrewed, List<FVector3>? Route, Site? Home, float DropFrom = 0.0f)
    {
        CWorld World = Mercs.World;
        VehicleDef Def = VehicleDefs.Get(Type);
        FVector3 Spot = Def.bAir && bCrewed ? At + new FVector3(0.0f, 35.0f, 0.0f) : Geo.Ground(At);
        Entity Handle = World.CreateEntity(Def.Name, Spot, Geo.YawRotation(Yaw));
        Vehicle? Ride = World.Registry.AddScript<Vehicle>(Handle);
        if (Ride is null)
        {
            World.DestroyEntity(Handle);
            return null;
        }

        Ride.Type = Type;
        Ride.Team = Team;
        Ride.bCrewed = bCrewed;
        Ride.Route = Route;
        Ride.HomeSite = Home;
        Ride.Home = Geo.Ground(At);
        Ride.DropFrom = DropFrom;
        return Ride;
    }

    public static Structure? SpawnStructure(EStructureKind Kind, EFaction Owner, FVector3 At, float Yaw, FVector3 Size, FVector4 Tint, Site? Home)
    {
        CWorld World = Mercs.World;
        FVector3 Ground = new(At.X, LowestCorner(At, Size, Yaw), At.Z);
        Entity Handle = World.CreateEntity(Kind.ToString(), Ground, Geo.YawRotation(Yaw));
        Structure? Building = World.Registry.AddScript<Structure>(Handle);
        if (Building is null)
        {
            World.DestroyEntity(Handle);
            return null;
        }

        Building.Kind = Kind;
        Building.OwnerFaction = Owner;
        Building.Size = Size;
        Building.Tint = Tint;
        Building.HomeSite = Home;
        Home?.Buildings.Add(Building);
        return Building;
    }

    // Sinks the footprint to its lowest corner so a slope never leaves a building floating.
    private static float LowestCorner(FVector3 At, FVector3 Size, float Yaw)
    {
        FVector3 Right = Geo.RightOf(Yaw) * (Size.X * 0.5f);
        FVector3 Forward = Geo.Heading(Yaw) * (Size.Z * 0.5f);
        float Lowest = Terrain.HeightAt(At.X, At.Z);
        foreach (FVector3 Corner in new[] { Right + Forward, Right - Forward, -Right + Forward, -Right - Forward })
        {
            Lowest = MathF.Min(Lowest, Terrain.HeightAt(At.X + Corner.X, At.Z + Corner.Z));
        }

        return Lowest - 0.05f;
    }
}
