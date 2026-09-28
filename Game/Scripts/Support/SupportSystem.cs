using System;
using System.Collections.Generic;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

public enum ESupportKind : byte
{
    None,
    SupplyDrop,
    Artillery,
    JeepDrop,
    ClusterBomb,
    BunkerBuster,
    CarpetBomb,
    TankDrop,
    HeliDrop,
    RebelSquad,
    FuelShipment,
}

public sealed class SupportItem
{
    public ESupportKind Kind;
    public string Name = string.Empty;
    public string Blurb = string.Empty;
    public int Cash;
    public int Fuel;
    public EFaction UnlockFaction = EFaction.None;
    public int UnlockContracts;
    public bool bInstant;
}

public static class SupportCatalog
{
    public static readonly List<SupportItem> Items = new()
    {
        new SupportItem { Kind = ESupportKind.SupplyDrop, Name = "Supply Drop", Blurb = "Parachuted crate: ammo, health, grenades and C4.", Cash = 4000, Fuel = 25 },
        new SupportItem { Kind = ESupportKind.Artillery, Name = "Artillery Strike", Blurb = "Fourteen shells walk across the smoke.", Cash = 15000, Fuel = 80 },
        new SupportItem { Kind = ESupportKind.JeepDrop, Name = "Jeep Delivery", Blurb = "A PMC jeep, air-dropped at the beacon.", Cash = 6000, Fuel = 35 },
        new SupportItem { Kind = ESupportKind.ClusterBomb, Name = "Cluster Bomb", Blurb = "Canister bursts into bomblets over a wide area.", Cash = 30000, Fuel = 120, UnlockFaction = EFaction.Allied, UnlockContracts = 1 },
        new SupportItem { Kind = ESupportKind.BunkerBuster, Name = "Bunker Buster", Blurb = "One huge penetrator. Flattens hardened buildings.", Cash = 70000, Fuel = 220, UnlockFaction = EFaction.Allied, UnlockContracts = 2 },
        new SupportItem { Kind = ESupportKind.CarpetBomb, Name = "Carpet Bomb", Blurb = "A bomber lays a line of heavy bombs.", Cash = 55000, Fuel = 180, UnlockFaction = EFaction.China, UnlockContracts = 1 },
        new SupportItem { Kind = ESupportKind.TankDrop, Name = "Tank Delivery", Blurb = "Main battle tank, delivered by parachute.", Cash = 60000, Fuel = 160, UnlockFaction = EFaction.China, UnlockContracts = 2 },
        new SupportItem { Kind = ESupportKind.HeliDrop, Name = "Attack Chopper", Blurb = "An attack helicopter set down at the beacon.", Cash = 80000, Fuel = 220, UnlockFaction = EFaction.Pirate, UnlockContracts = 1 },
        new SupportItem { Kind = ESupportKind.RebelSquad, Name = "PLAV Rebel Squad", Blurb = "Four guerrillas dropped in to fight the VZ.", Cash = 12000, Fuel = 40, UnlockFaction = EFaction.Guerrilla, UnlockContracts = 1 },
        new SupportItem { Kind = ESupportKind.FuelShipment, Name = "Fuel Shipment", Blurb = "UP sells you 300 fuel, delivered straight to the PMC.", Cash = 12000, Fuel = 0, UnlockFaction = EFaction.Oil, UnlockContracts = 1, bInstant = true },
    };

    public static SupportItem Get(ESupportKind Kind) => Items.Find(Item => Item.Kind == Kind) ?? Items[0];
}

public sealed class SupportSystem
{
    private sealed class FFlight
    {
        public Entity Handle;
        public Entity Rotor;
        public FVector3 From;
        public FVector3 To;
        public float Speed;
        public float Traveled;
        public float Length;
        public Action<FFlight, float>? OnProgress;
        public bool bHelicopter;
        public float HoverTime;
        public FVector3 HoverAt;
        public int Stage;
        public float Timer;
        public Action? OnHover;
        public FVector3 Position;
        public SoundLoop? Engine;
        public FVector3 LastPosition;
    }

    private sealed class FTimed
    {
        public float Delay;
        public Action Run = null!;
    }

    public readonly Dictionary<ESupportKind, int> Inventory = new();
    public ESupportKind Selected = ESupportKind.Artillery;
    private readonly List<FFlight> Flights = new();
    private readonly List<FTimed> Timed = new();

    public SupportSystem()
    {
        Inventory[ESupportKind.Artillery] = 1;
        Inventory[ESupportKind.SupplyDrop] = 1;
    }

    public int Count(ESupportKind Kind) => Inventory.TryGetValue(Kind, out int Value) ? Value : 0;

    public bool IsUnlocked(ESupportKind Kind)
    {
        SupportItem Item = SupportCatalog.Get(Kind);
        return Item.UnlockFaction == EFaction.None || Mercs.Factions.Get(Item.UnlockFaction).ContractsDone >= Item.UnlockContracts;
    }

    public static FVector4 BeaconColor(ESupportKind Kind) => Kind switch
    {
        ESupportKind.SupplyDrop or ESupportKind.JeepDrop or ESupportKind.TankDrop or ESupportKind.HeliDrop => Palette.Rgb(0.2f, 0.6f, 1.0f),
        ESupportKind.RebelSquad => Palette.Rgb(0.2f, 1.0f, 0.3f),
        _ => Palette.Rgb(1.0f, 0.15f, 0.1f),
    };

    public bool Buy(ESupportKind Kind)
    {
        SupportItem Item = SupportCatalog.Get(Kind);
        if (!IsUnlocked(Kind))
        {
            Mercs.Feed.Post($"{Item.Name} is locked. Work for {Mercs.Factions.Get(Item.UnlockFaction).Name}.", ENewsTone.Bad);
            return false;
        }

        if (!Mercs.Wallet.Spend(Item.Cash, 0, $"bought {Item.Name}"))
        {
            return false;
        }

        if (Item.bInstant)
        {
            Mercs.Wallet.AddFuel(300, "UP fuel shipment");
            return true;
        }

        Inventory[Kind] = Count(Kind) + 1;
        if (Count(Selected) <= 0)
        {
            Selected = Kind;
        }

        return true;
    }

    public void CycleSelection()
    {
        List<ESupportKind> Owned = new();
        foreach (SupportItem Item in SupportCatalog.Items)
        {
            if (Count(Item.Kind) > 0)
            {
                Owned.Add(Item.Kind);
            }
        }

        if (Owned.Count == 0)
        {
            Selected = ESupportKind.None;
            Mercs.Feed.Post("No support in inventory.", ENewsTone.Bad);
            return;
        }

        int Index = Owned.IndexOf(Selected);
        Selected = Owned[(Index + 1) % Owned.Count];
        Mercs.Feed.Post($"Support: {SupportCatalog.Get(Selected).Name} (x{Count(Selected)})", ENewsTone.Neutral);
    }

    public void Select(ESupportKind Kind)
    {
        if (Count(Kind) > 0)
        {
            Selected = Kind;
        }
    }

    public bool TryConsume(ESupportKind Kind)
    {
        SupportItem Item = SupportCatalog.Get(Kind);
        if (Count(Kind) <= 0)
        {
            return false;
        }

        if (!Mercs.Wallet.Spend(0, Item.Fuel, $"{Item.Name} fuel"))
        {
            return false;
        }

        Inventory[Kind] = Count(Kind) - 1;
        if (Count(Kind) <= 0)
        {
            ESupportKind Previous = Selected;
            CycleSelection();
            if (Selected == Previous)
            {
                Selected = ESupportKind.None;
            }
        }

        return true;
    }

    public void OnBeaconLanded(ESupportKind Kind, FVector3 Target)
    {
        SupportItem Item = SupportCatalog.Get(Kind);
        Sfx.Ui(ESfx.RadioCall, 0.6f);
        Mercs.Feed.Post($"{Item.Name} inbound. Get clear of the smoke!", ENewsTone.Alert);
        switch (Kind)
        {
            case ESupportKind.Artillery:
                ScheduleArtillery(Target);
                break;
            case ESupportKind.ClusterBomb:
                BomberRun(Target, 1, 0.0f, EOrdnanceLook.Bomb, (At) => ClusterBurst(At));
                break;
            case ESupportKind.CarpetBomb:
                BomberRun(Target, 12, 70.0f, EOrdnanceLook.Bomb, null);
                break;
            case ESupportKind.BunkerBuster:
                BomberRun(Target, 1, 0.0f, EOrdnanceLook.Bomb, (At) => Explosion.Detonate(At, 18.0f, 2600.0f, Mercs.Player, 3.5f));
                break;
            case ESupportKind.SupplyDrop:
                CargoDrop(Target, () => Pickup.Spawn(EPickupKind.Supplies, Target + new FVector3(0.0f, 60.0f, 0.0f), 1, EWeapon.None, 240.0f, 7.0f));
                break;
            case ESupportKind.JeepDrop:
                CargoDrop(Target, () => Spawner.SpawnVehicle(EVehicleType.Jeep, EFaction.Merc, Target, Mercs.Range(0.0f, 360.0f), false, null, null, 60.0f));
                break;
            case ESupportKind.TankDrop:
                CargoDrop(Target, () => Spawner.SpawnVehicle(EVehicleType.Tank, EFaction.Merc, Target, Mercs.Range(0.0f, 360.0f), false, null, null, 60.0f));
                break;
            case ESupportKind.HeliDrop:
                CargoDrop(Target, () => Spawner.SpawnVehicle(EVehicleType.AttackHeli, EFaction.Merc, Target, Mercs.Range(0.0f, 360.0f), false, null, null, 60.0f));
                break;
            case ESupportKind.RebelSquad:
                HelicopterVisit(Target, 3.0f, () =>
                {
                    for (int Index = 0; Index < 4; ++Index)
                    {
                        Spawner.SpawnSoldier(EFaction.Guerrilla, Index == 0 ? ESoldierRole.RocketTrooper : ESoldierRole.Rifleman, Geo.RandomAround(Target, 2.0f, 6.0f), null);
                    }

                    Mercs.Feed.Post("PLAV squad on the ground.", ENewsTone.Good);
                });
                break;
        }
    }

    public void OnFlareLanded(FVector3 Target)
    {
        HvtTarget? Pending = Mercs.Contracts.PendingExtraction;
        if (Pending is null)
        {
            Mercs.Feed.Post("Nobody to extract.", ENewsTone.Bad);
            return;
        }

        Sfx.Ui(ESfx.RadioCall, 0.6f);
        Mercs.Feed.Post("Extraction chopper inbound. Defend the target!", ENewsTone.Alert);
        HelicopterVisit(Target + new FVector3(6.0f, 0.0f, 6.0f), 5.0f, () => Mercs.Contracts.CompleteCapture(Pending));
    }

    private void ScheduleArtillery(FVector3 Target)
    {
        for (int Index = 0; Index < 14; ++Index)
        {
            float Delay = 4.0f + Index * 0.38f + Mercs.Range(0.0f, 0.2f);
            FVector3 Offset = new(Mercs.Range(-16.0f, 16.0f), 0.0f, Mercs.Range(-16.0f, 16.0f));
            Timed.Add(new FTimed
            {
                Delay = MathF.Max(0.0f, Delay - 0.5f),
                Run = () => Sfx.At(ESfx.ArtilleryWhistle, Geo.Ground(Target + Offset) + new FVector3(0.0f, 25.0f, 0.0f), 0.45f, 400.0f, 25.0f, 0.08f),
            });
            Timed.Add(new FTimed
            {
                Delay = Delay,
                Run = () =>
                {
                    FVector3 Impact = Geo.Ground(Target + Offset);
                    FVector3 Start = Impact + new FVector3(-30.0f, 140.0f, -30.0f);
                    OrdnanceSystem.Launch(Start, (Impact - Start).Normalized() * 110.0f, 0.0f, 7.5f, 340.0f, Mercs.Player, Entity.Null, EOrdnanceLook.Shell, 1.4f);
                },
            });
        }
    }

    private void ClusterBurst(FVector3 At)
    {
        Mercs.Fx.Explosion(At + new FVector3(0.0f, 2.0f, 0.0f), 2.5f);
        for (int Index = 0; Index < 18; ++Index)
        {
            FVector3 Velocity = new(Mercs.Range(-11.0f, 11.0f), Mercs.Range(4.0f, 9.0f), Mercs.Range(-11.0f, 11.0f));
            OrdnanceSystem.Launch(At + new FVector3(0.0f, 3.0f, 0.0f), Velocity, 9.8f, 5.0f, 200.0f, Mercs.Player, Entity.Null, EOrdnanceLook.Bomblet, 1.2f);
        }
    }

    private static FVector3 ApproachDirection(FVector3 Target)
    {
        Site? Hq = Mercs.Sites.Find(Site => Site.Kind == ESiteKind.PmcHq);
        FVector3 From = Hq?.Center ?? new FVector3(-400.0f, 0.0f, -400.0f);
        return Geo.Flat(Target - From).NormalizedOr(FVector3.Forward);
    }

    private FFlight Launch(MeshKit Kit, FVector3 From, FVector3 To, float Speed, bool bHelicopter, MeshKit? RotorKit = null)
    {
        CWorld World = Mercs.World;
        Entity Handle = World.CreateEntity(bHelicopter ? "SupportHeli" : "SupportPlane", From, FQuat.LookRotation(Geo.Flat(To - From).NormalizedOr(FVector3.Forward), FVector3.Up));
        Kit.Commit(World.Registry, Handle);
        Entity Rotor = Entity.Null;
        if (RotorKit is not null)
        {
            Rotor = World.CreateEntity("SupportRotor", FVector3.Zero);
            World.SetParent(Rotor, Handle);
            World.Registry.Get<STransformComponent>(Rotor).SetLocalLocation(new FVector3(0.0f, 3.55f, 0.2f));
            RotorKit.Commit(World.Registry, Rotor, false, false);
        }

        FFlight Flight = new() { Handle = Handle, Rotor = Rotor, From = From, To = To, Speed = Speed, Length = FVector3.Distance(From, To), bHelicopter = bHelicopter, Position = From, LastPosition = From };
        Flight.Engine = bHelicopter ? Sfx.StartMover(ESfx.RotorHeli, From, 0.9f, 600.0f, 15.0f) : Sfx.StartMover(ESfx.EngineJet, From, 1.0f, 1200.0f, 40.0f);
        Flights.Add(Flight);
        return Flight;
    }

    private static MeshKit PlaneMesh(bool bBomber)
    {
        MeshKit Kit = new();
        FVector4 Body = bBomber ? Palette.Hex(0x4C5560) : Palette.Hex(0x5D6B4A);
        float Length = bBomber ? 9.0f : 7.0f;
        Kit.Tube(new FVector3(0.0f, 0.0f, -Length), new FVector3(0.0f, 0.0f, Length), 1.1f, 1.0f, Body, 10);
        Kit.Tube(new FVector3(0.0f, 0.0f, Length), new FVector3(0.0f, 0.0f, Length + 2.5f), 1.0f, 0.2f, Body, 10);
        Kit.Box(new FVector3(0.0f, 0.2f, 0.5f), new FVector3(bBomber ? 13.0f : 9.0f, 0.18f, 1.8f), Palette.Shade(Body, 0.9f));
        Kit.Box(new FVector3(0.0f, 0.3f, -Length + 0.8f), new FVector3(3.8f, 0.12f, 1.0f), Palette.Shade(Body, 0.9f));
        Kit.Box(new FVector3(0.0f, 1.8f, -Length + 0.8f), new FVector3(0.12f, 1.8f, 1.1f), Palette.Shade(Body, 0.85f));
        Kit.Box(new FVector3(0.0f, 0.7f, Length - 1.5f), new FVector3(0.7f, 0.4f, 1.2f), Palette.Glass);
        for (int Engine = -2; Engine <= 2; ++Engine)
        {
            if (Engine == 0 || (!bBomber && Math.Abs(Engine) == 2))
            {
                continue;
            }

            Kit.Tube(new FVector3(Engine * 3.6f, -0.3f, 2.2f), new FVector3(Engine * 3.6f, -0.3f, -0.8f), 0.5f, 0.45f, Palette.Gunmetal, 8);
        }

        Kit.Box(new FVector3(0.0f, 0.6f, 0.0f), new FVector3(0.8f, 0.4f, 0.03f), Palette.Hex(0xE07A1F));
        return Kit;
    }

    private void BomberRun(FVector3 Target, int Bombs, float Spread, EOrdnanceLook Look, Action<FVector3>? OnImpact)
    {
        FVector3 Direction = ApproachDirection(Target);
        float Altitude = MathF.Max(Terrain.HeightAt(Target.X, Target.Z), 0.0f) + 75.0f;
        FVector3 From = new FVector3(Target.X, Altitude, Target.Z) - Direction * 700.0f;
        FVector3 To = new FVector3(Target.X, Altitude, Target.Z) + Direction * 700.0f;
        float Speed = 70.0f;
        float Fall = MathF.Sqrt(2.0f * (Altitude - Target.Y) / 9.8f);
        float Lead = Speed * Fall;

        List<float> Releases = new();
        for (int Index = 0; Index < Bombs; ++Index)
        {
            float Offset = Bombs > 1 ? Mathf.Lerp(-Spread * 0.5f, Spread * 0.5f, Index / (float)(Bombs - 1)) : 0.0f;
            Releases.Add(700.0f + Offset - Lead);
        }

        int Next = 0;
        FFlight Flight = Launch(PlaneMesh(Bombs > 1 || Look == EOrdnanceLook.Bomb), From, To, Speed, false);
        Flight.OnProgress = (Self, Traveled) =>
        {
            while (Next < Releases.Count && Traveled >= Releases[Next])
            {
                OrdnanceSystem.Launch(Self.Position - new FVector3(0.0f, 1.5f, 0.0f), Direction * Speed, 9.8f, 10.0f, 650.0f, Mercs.Player, Entity.Null, Look, 1.8f, OnImpact);
                Sfx.At(ESfx.ArtilleryWhistle, Self.Position + Direction * Lead * 0.5f - new FVector3(0.0f, 30.0f, 0.0f), 0.4f, 500.0f, 30.0f, 0.05f, 0.35f, 0.5f);
                ++Next;
            }
        };
    }

    private void CargoDrop(FVector3 Target, Action Drop)
    {
        FVector3 Direction = ApproachDirection(Target);
        float Altitude = MathF.Max(Terrain.HeightAt(Target.X, Target.Z), 0.0f) + 90.0f;
        FVector3 From = new FVector3(Target.X, Altitude, Target.Z) - Direction * 600.0f;
        FVector3 To = new FVector3(Target.X, Altitude, Target.Z) + Direction * 600.0f;
        bool bDropped = false;
        FFlight Flight = Launch(PlaneMesh(false), From, To, 60.0f, false);
        Flight.OnProgress = (Self, Traveled) =>
        {
            if (!bDropped && Traveled >= 600.0f)
            {
                bDropped = true;
                Drop();
            }
        };
    }

    private void HelicopterVisit(FVector3 Target, float Hover, Action OnArrive)
    {
        FVector3 Direction = ApproachDirection(Target);
        FVector3 Ground = Geo.Ground(Target);
        FVector3 From = Ground + new FVector3(0.0f, 60.0f, 0.0f) - Direction * 500.0f;
        FVector3 To = Ground + new FVector3(0.0f, 60.0f, 0.0f) + Direction * 500.0f;
        EntityHull Hull = VehicleDefs.BuildHull(EVehicleType.TransportHeli, Mercs.Factions.Get(EFaction.Merc).Accent);
        FFlight Flight = Launch(Hull.Hull, From, To, 38.0f, true, Hull.Rotor);
        Flight.HoverAt = Ground + new FVector3(0.0f, 1.0f, 0.0f);
        Flight.HoverTime = Hover;
        Flight.OnHover = OnArrive;
    }

    public void Update(float DeltaTime)
    {
        for (int Index = Timed.Count - 1; Index >= 0; --Index)
        {
            Timed[Index].Delay -= DeltaTime;
            if (Timed[Index].Delay <= 0.0f)
            {
                Action Run = Timed[Index].Run;
                Timed.RemoveAt(Index);
                Run();
            }
        }

        for (int Index = Flights.Count - 1; Index >= 0; --Index)
        {
            FFlight Flight = Flights[Index];
            bool bDone = Flight.bHelicopter ? TickHelicopter(Flight, DeltaTime) : TickPlane(Flight, DeltaTime);
            Flight.Engine?.Move(Flight.Position, (Flight.Position - Flight.LastPosition) / MathF.Max(DeltaTime, 1e-3f));
            Flight.LastPosition = Flight.Position;
            if (bDone)
            {
                Flight.Engine?.Stop();
                if (Mercs.World.IsValidEntity(Flight.Handle))
                {
                    Mercs.World.DestroyEntity(Flight.Handle);
                }

                Flights.RemoveAt(Index);
            }
        }
    }

    private static bool TickPlane(FFlight Flight, float DeltaTime)
    {
        Flight.Traveled += Flight.Speed * DeltaTime;
        Flight.Position = FVector3.Lerp(Flight.From, Flight.To, Mathf.Clamp01(Flight.Traveled / Flight.Length));
        Mercs.World.SetEntityLocation(Flight.Handle, Flight.Position);
        Flight.OnProgress?.Invoke(Flight, Flight.Traveled);
        return Flight.Traveled >= Flight.Length;
    }

    private static bool TickHelicopter(FFlight Flight, float DeltaTime)
    {
        CWorld World = Mercs.World;
        if (!Flight.Rotor.IsNull)
        {
            World.Registry.Get<STransformComponent>(Flight.Rotor).SetLocalRotation(FQuat.FromEuler(0.0f, Mercs.Time * 30.0f, 0.0f));
        }

        FVector3 Goal = Flight.Stage switch
        {
            0 => Flight.HoverAt + new FVector3(0.0f, 30.0f, 0.0f),
            1 => Flight.HoverAt,
            2 => Flight.HoverAt,
            3 => Flight.HoverAt + new FVector3(0.0f, 40.0f, 0.0f),
            _ => Flight.To,
        };

        float Speed = Flight.Stage switch
        {
            1 => 8.0f,
            3 => 10.0f,
            _ => Flight.Speed,
        };

        if (Flight.Stage == 2)
        {
            Flight.Timer += DeltaTime;
            if (Flight.Timer >= Flight.HoverTime)
            {
                Flight.OnHover?.Invoke();
                Flight.OnHover = null;
                Flight.Stage = 3;
            }

            Mercs.Fx.Puff(Geo.Ground(Flight.HoverAt) + new FVector3(Mercs.Range(-5, 5), 0.3f, Mercs.Range(-5, 5)), 2.0f, 1.2f, false, new FVector3(Mercs.Range(-3, 3), 0.5f, Mercs.Range(-3, 3)));
            return false;
        }

        FVector3 Offset = Goal - Flight.Position;
        float Distance = Offset.Length;
        if (Distance < 1.0f)
        {
            Flight.Stage++;
            return Flight.Stage > 4;
        }

        Flight.Position += Offset / Distance * MathF.Min(Distance, Speed * DeltaTime);
        World.SetEntityLocation(Flight.Handle, Flight.Position);
        FVector3 Facing = Geo.Flat(Offset);
        if (Facing.LengthSquared > 4.0f)
        {
            World.SetEntityRotation(Flight.Handle, Geo.Orientation(Geo.YawOf(Facing), Flight.Stage is 0 or 4 ? -8.0f : 0.0f, 0.0f));
        }

        return false;
    }
}
