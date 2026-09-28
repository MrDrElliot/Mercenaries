using System;
using System.Collections.Generic;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

public sealed class WorldBuilder
{
    private readonly CWorld World;
    private readonly EntityRegistry Registry;
    private readonly Dictionary<string, Site> Nodes = new();
    private readonly List<(string From, string To)> Edges = new();
    private readonly Dictionary<(int, int), MeshKit> Decor = new();
    private const float DecorCell = 130.0f;

    public WorldBuilder(CWorld World)
    {
        this.World = World;
        Registry = World.Registry;
    }

    private Site Define(string Key, string Name, ESiteKind Kind, EFaction Owner, float X, float Z, float Radius)
    {
        Site Place = new() { Name = Name, Kind = Kind, Owner = Owner, OriginalOwner = Owner, Center = new FVector3(X, 0.0f, Z), Radius = Radius };
        Nodes[Key] = Place;
        Mercs.Sites.Add(Place);
        return Place;
    }

    public void Build()
    {
        DefineSites();
        DefineRoads();

        foreach (Site Place in Mercs.Sites)
        {
            bool bPaved = Place.Kind is ESiteKind.PmcHq or ESiteKind.FactionHq or ESiteKind.Refinery or ESiteKind.Harbor;
            Terrain.AddPad(Place.Center, Place.Radius, bPaved);
        }

        foreach ((string From, string To) in Edges)
        {
            Terrain.AddRoad(WithPadHeight(Nodes[From].Center), WithPadHeight(Nodes[To].Center), 7.0f);
        }

        using (Profiler.Sample("WorldBuilder.Terrain"))
        {
            Terrain.Build(World, Registry);
        }

        foreach (Site Place in Mercs.Sites)
        {
            Place.Center = Geo.Ground(Place.Center);
        }

        using (Profiler.Sample("WorldBuilder.Sites"))
        {
            BuildLighting();
            BuildPmc(Nodes["PMC"]);
            BuildFactionHq(Nodes["AN"], EFaction.Allied);
            BuildFactionHq(Nodes["CHN"], EFaction.China);
            BuildRefinery(Nodes["OIL"]);
            BuildCamp(Nodes["PLAV"]);
            BuildHarbor(Nodes["PIR"]);
            BuildCapital(Nodes["CAP"]);

            BuildOutpost(Nodes["O1"], true, false, true);
            BuildOutpost(Nodes["O2"], false, true, false);
            BuildOutpost(Nodes["O3"], true, false, false);
            BuildOutpost(Nodes["O4"], false, false, true);
            BuildOutpost(Nodes["O5"], true, true, false);
            BuildOutpost(Nodes["O6"], false, false, false);

            BuildVillage(Nodes["V1"], 7);
            BuildVillage(Nodes["V2"], 6);
            BuildVillage(Nodes["V3"], 5);
            BuildVillage(Nodes["V4"], 6);

            BuildFuelDepot(Nodes["F1"], EFaction.VZ);
            BuildFuelDepot(Nodes["F2"], EFaction.Oil);
            BuildFuelDepot(Nodes["F3"], EFaction.VZ);
        }

        using (Profiler.Sample("WorldBuilder.Patrols"))
        {
            AddPatrols();
        }
        using (Profiler.Sample("WorldBuilder.Decor"))
        {
            ScatterDecor();
            CommitDecor();
        }
        using (Profiler.Sample("WorldBuilder.Foliage"))
        {
            Mercs.Destruction.CommitFoliage();
        }
        DefineHvts();
        DefineContracts();
        BuildNavigation();
    }

    // Baked after every structure stands, so the first bake already routes around them.
    private void BuildNavigation()
    {
        // Climb stays under the capsule's step height, or the mesh routes soldiers up ledges their feet cannot take.
        AddNavVolume("Nav_Infantry", string.Empty, 0.4f, 0.4f, 1.8f, 0.4f, 45.0f);
        AddNavVolume("Nav_Vehicles", Mercs.VehicleNavAgent, 0.8f, 1.8f, 2.6f, 0.7f, 32.0f);
    }

    private void AddNavVolume(string Name, string Agent, float CellSize, float AgentRadius, float AgentHeight, float MaxClimb, float MaxSlope)
    {
        // The sea is a surface rather than a collider, so the volume starts at the waterline or paths would run across the seabed.
        const float HalfHeight = 50.0f;
        FVector3 Center = new(0.0f, Terrain.SeaLevel - 0.5f + HalfHeight, 0.0f);
        Entity Volume = World.CreateEntity(Name, Center);
        SNavMeshComponent Nav = Registry.GetOrAdd<SNavMeshComponent>(Volume)!;
        Nav.Agent = Agent;
        Nav.Center = Center;
        Nav.Extents = new FVector3(Terrain.HalfSize - 40.0f, HalfHeight, Terrain.HalfSize - 40.0f);
        Nav.DynamicRebuildInterval = 1.0f;

        FNavBuildSettings Settings = Nav.Settings;
        Settings.CellSize = CellSize;
        Settings.CellHeight = CellSize * 0.5f;
        Settings.AgentRadius = AgentRadius;
        Settings.AgentHeight = AgentHeight;
        Settings.AgentMaxClimb = MaxClimb;
        Settings.AgentMaxSlopeDeg = MaxSlope;
        Settings.TileSizeVoxels = 64;
        Nav.Settings = Settings;
        Nav.RequestRebuild();
    }

    private static FVector3 WithPadHeight(FVector3 At) => new(At.X, Terrain.PadHeight(At), At.Z);

    private void DefineSites()
    {
        Define("PMC", "PMC Headquarters", ESiteKind.PmcHq, EFaction.Merc, -250, -250, 34);
        Define("AN", "Allied Nations Base", ESiteKind.FactionHq, EFaction.Allied, 270, -200, 40);
        Define("CHN", "PLA Command", ESiteKind.FactionHq, EFaction.China, 215, 250, 40);
        Define("OIL", "UP Refinery", ESiteKind.Refinery, EFaction.Oil, -280, 200, 48);
        Define("PLAV", "PLAV Jungle Camp", ESiteKind.Camp, EFaction.Guerrilla, -110, 255, 30);
        Define("PIR", "Pirate Harbor", ESiteKind.Harbor, EFaction.Pirate, 330, 40, 36);
        Define("CAP", "Maracaibo (VZ Capital)", ESiteKind.Capital, EFaction.VZ, 0, -40, 62);
        Define("O1", "Outpost Cordoba", ESiteKind.Outpost, EFaction.VZ, -150, -125, 26);
        Define("O2", "Radar Hill", ESiteKind.Outpost, EFaction.VZ, 140, -115, 26);
        Define("O3", "Fort Almeida", ESiteKind.Outpost, EFaction.VZ, -195, 70, 26);
        Define("O4", "AA Battery Norte", ESiteKind.Outpost, EFaction.VZ, 110, 135, 26);
        Define("O5", "Southern Checkpoint", ESiteKind.Outpost, EFaction.VZ, 10, -220, 24);
        Define("O6", "Jungle Barracks", ESiteKind.Outpost, EFaction.VZ, -30, 140, 24);
        Define("V1", "San Isidro", ESiteKind.Village, EFaction.Civilian, -120, -330, 30);
        Define("V2", "Puerto Viejo", ESiteKind.Village, EFaction.Civilian, 90, -310, 28);
        Define("V3", "La Cruz", ESiteKind.Village, EFaction.Civilian, -340, -60, 26);
        Define("V4", "El Mirador", ESiteKind.Village, EFaction.Civilian, 220, 55, 28);
        Define("F1", "Fuel Depot Sur", ESiteKind.FuelDepot, EFaction.VZ, -210, -165, 18);
        Define("F2", "UP Pump Station", ESiteKind.FuelDepot, EFaction.Oil, 235, -65, 18);
        Define("F3", "Fuel Depot Centro", ESiteKind.FuelDepot, EFaction.VZ, -85, 60, 18);
    }

    private void DefineRoads()
    {
        string[,] Pairs =
        {
            { "PMC", "V1" }, { "PMC", "F1" }, { "PMC", "V3" }, { "V1", "O5" }, { "F1", "O1" }, { "O1", "CAP" },
            { "O5", "CAP" }, { "O5", "V2" }, { "V2", "AN" }, { "CAP", "O2" }, { "O2", "F2" }, { "F2", "AN" },
            { "F2", "V4" }, { "V4", "PIR" }, { "V4", "O4" }, { "O4", "CHN" }, { "O4", "CAP" }, { "CAP", "F3" },
            { "F3", "O3" }, { "O3", "V3" }, { "O3", "OIL" }, { "F3", "O6" }, { "O6", "PLAV" }, { "PLAV", "OIL" },
            { "O6", "O4" },
        };

        for (int Index = 0; Index < Pairs.GetLength(0); ++Index)
        {
            Edges.Add((Pairs[Index, 0], Pairs[Index, 1]));
        }
    }

    private void BuildLighting()
    {
        Entity Sun = World.CreateEntity("Sun");
        SDirectionalLightComponent Light = Registry.GetOrAdd<SDirectionalLightComponent>(Sun)!;
        Light.Direction = new FVector3(0.45f, 0.72f, 0.35f).Normalized();
        Light.Color = new FVector3(1.0f, 0.95f, 0.86f);
        Light.Intensity = 4.0f;
        Light.bCastShadows = true;
        Light.ShadowMaxDistance = 260.0f;

        Entity Sky = World.CreateEntity("Sky");
        SEnvironmentComponent Environment = Registry.GetOrAdd<SEnvironmentComponent>(Sky)!;
        Environment.bRenderSky = true;
        Environment.bAerialPerspective = true;

        SSkyLightComponent Ambient = Registry.GetOrAdd<SSkyLightComponent>(Sky)!;
        Ambient.bAffectsWorld = true;
        Ambient.bAmbientFromSky = true;
        Ambient.Intensity = 1.0f;

        SCloudComponent Clouds = Registry.GetOrAdd<SCloudComponent>(Sky)!;
        Clouds.bEnabled = true;
        Clouds.Coverage = 0.4f;

        SExponentialHeightFogComponent Fog = Registry.GetOrAdd<SExponentialHeightFogComponent>(Sky)!;
        Fog.bEnabled = true;
        Fog.FogVisibilityDistance = 2600.0f;
        Fog.FogMaxOpacity = 0.35f;
        Fog.FogInscatteringColor = new FVector3(0.55f, 0.65f, 0.75f);

        Mercs.Clock = new DayNight();
        Mercs.Clock.Bind(Sun, Sky);
    }

    private Structure? Place(Site Home, EStructureKind Kind, float X, float Z, float Yaw, FVector3 Size, FVector4? Tint = null, EFaction? Owner = null)
    {
        FVector3 At = Home.Center + new FVector3(X, 0.0f, Z);
        return Spawner.SpawnStructure(Kind, Owner ?? Home.Owner, At, Yaw, Size, Tint ?? Palette.Concrete, Home);
    }

    private void Soldiers(Site Home, params ESoldierRole[] Roles)
    {
        foreach (ESoldierRole Role in Roles)
        {
            FVector3 Spot = Geo.RandomAround(Home.Center, 3.0f, Home.Radius * 0.7f);
            Home.Garrison.Add(new FGarrisonSlot { Role = Role, Spot = Spot });
        }
    }

    private void Parked(Site Home, EVehicleType Type, float X, float Z, float Yaw, bool bCrewed = false, List<FVector3>? Route = null)
    {
        Home.VehicleSlots.Add(new FVehicleSlot { Type = Type, Spot = Home.Center + new FVector3(X, 0.0f, Z), Yaw = Yaw, bCrewed = bCrewed, Route = Route });
    }

    private void Perimeter(Site Home, float Radius, FVector4 Tint, float Height = 2.6f, int GateSide = 0)
    {
        for (int Side = 0; Side < 4; ++Side)
        {
            float Yaw = Side * 90.0f;
            FVector3 Normal = Geo.Heading(Yaw);
            FVector3 Along = Geo.RightOf(Yaw);
            for (int Segment = -2; Segment <= 2; ++Segment)
            {
                if (Side == GateSide && Segment == 0)
                {
                    continue;
                }

                FVector3 Offset = Normal * Radius + Along * (Segment * Radius * 0.4f);
                Place(Home, EStructureKind.Wall, Offset.X, Offset.Z, Yaw + 90.0f, new FVector3(Radius * 0.4f, Height, 0.6f), Tint);
            }
        }
    }

    private void BuildPmc(Site Home)
    {
        Home.PlaceFlag(Home.Center + new FVector3(8.0f, 0.0f, 14.0f));
        Structure? Hq = Place(Home, EStructureKind.Headquarters, 0, -12, 0, new FVector3(18, 9, 12), Palette.Hex(0x9A8F7A));
        Structure? Garage = Place(Home, EStructureKind.Warehouse, 18, 6, 90, new FVector3(14, 7, 10), Palette.Hex(0x7F7466));
        Structure? Pad = Place(Home, EStructureKind.Helipad, -16, 8, 0, new FVector3(14, 0.5f, 14));
        Structure? Tank = Place(Home, EStructureKind.FuelTank, 20, -14, 0, new FVector3(5, 7, 5));
        foreach (Structure? Building in new[] { Hq, Garage, Pad, Tank })
        {
            if (Building is not null)
            {
                Building.bInvulnerable = true;
            }
        }

        Place(Home, EStructureKind.Crate, 4, 4, 15, new FVector3(1.4f, 1.2f, 1.4f), null, EFaction.Merc);
        Place(Home, EStructureKind.Crate, 5.5f, 4.5f, -10, new FVector3(1.2f, 1.0f, 1.2f), null, EFaction.Merc);
        Home.PlaceContact(Home.Center + new FVector3(-4.0f, 0.0f, -4.0f));
        Home.PlayerSpawn = Home.Center + new FVector3(0.0f, HumanoidBody.FeetOffset + 0.3f, 4.0f);
        Parked(Home, EVehicleType.Jeep, 10, 14, 30);
        Parked(Home, EVehicleType.Truck, 22, 18, 90);
        Pickup.Spawn(EPickupKind.Ammo, Home.Center + new FVector3(2.0f, 0.0f, 2.0f), 1, EWeapon.None, 0.0f);
        Pickup.Spawn(EPickupKind.Health, Home.Center + new FVector3(3.0f, 0.0f, 2.0f), 100, EWeapon.None, 0.0f);
    }

    private void BuildFactionHq(Site Home, EFaction Faction)
    {
        FactionInfo Info = Mercs.Factions.Get(Faction);
        FVector4 Wall = Faction == EFaction.Allied ? Palette.Hex(0xB5B8B0) : Palette.Hex(0xA99C86);
        Mercs.Factions.Get(Faction).Headquarters = Home.Center;
        Home.PlaceFlag(Home.Center + new FVector3(-10.0f, 0.0f, 10.0f));
        Structure? Hq = Place(Home, EStructureKind.Headquarters, 0, -14, 0, new FVector3(20, 10, 13), Wall);
        if (Hq is not null)
        {
            Hq.bInvulnerable = true;
        }

        Place(Home, EStructureKind.Barracks, -20, 6, 90, new FVector3(14, 4, 7), Wall);
        Place(Home, EStructureKind.Barracks, 20, 6, 90, new FVector3(14, 4, 7), Wall);
        Place(Home, EStructureKind.Helipad, 0, 22, 0, new FVector3(14, 0.5f, 14));
        Place(Home, EStructureKind.Tower, 30, 30, 0, new FVector3(3.5f, 9, 3.5f));
        Place(Home, EStructureKind.Tower, -30, 30, 0, new FVector3(3.5f, 9, 3.5f));
        Place(Home, EStructureKind.Tent, -8, 10, 90, new FVector3(6, 3, 4), Palette.Canvas);
        Place(Home, EStructureKind.Warehouse, 26, -22, 0, new FVector3(12, 7, 9), Wall);
        Place(Home, EStructureKind.Container, -26, -22, 0, new FVector3(6, 2.6f, 2.5f), Info.Color);
        Place(Home, EStructureKind.Container, -26, -18, 0, new FVector3(6, 2.6f, 2.5f), Palette.Shade(Info.Color, 0.8f));
        Perimeter(Home, Home.Radius * 0.95f, Wall, 2.6f, 2);
        Home.PlaceContact(Home.Center + new FVector3(4.0f, 0.0f, -5.0f));
        Soldiers(Home, ESoldierRole.Rifleman, ESoldierRole.Rifleman, ESoldierRole.Gunner, ESoldierRole.RocketTrooper, ESoldierRole.Officer, ESoldierRole.Rifleman);

        if (Faction == EFaction.Allied)
        {
            Parked(Home, EVehicleType.Jeep, 8, 30, 0);
            Parked(Home, EVehicleType.Apc, -10, -30, 90);
            Parked(Home, EVehicleType.Truck, 12, -30, 90);
        }
        else
        {
            Parked(Home, EVehicleType.Jeep, 8, 30, 0);
            Parked(Home, EVehicleType.Tank, -12, -30, 90);
            Parked(Home, EVehicleType.Technical, 12, -30, 90);
        }
    }

    private void BuildRefinery(Site Home)
    {
        Mercs.Factions.Get(EFaction.Oil).Headquarters = Home.Center;
        Home.PlaceFlag(Home.Center + new FVector3(-6.0f, 0.0f, 16.0f));
        Structure? Hq = Place(Home, EStructureKind.Headquarters, 0, 18, 180, new FVector3(16, 8, 11), Palette.Hex(0xC9C2B0));
        if (Hq is not null)
        {
            Hq.bInvulnerable = true;
        }

        for (int Index = 0; Index < 4; ++Index)
        {
            Place(Home, EStructureKind.FuelTank, -26 + Index * 9, -18, 0, new FVector3(7, 9, 7));
        }

        Place(Home, EStructureKind.Silo, 28, 6, 0, new FVector3(6, 16, 6), Palette.Steel);
        Place(Home, EStructureKind.Silo, 28, -6, 0, new FVector3(6, 18, 6), Palette.Steel);
        Place(Home, EStructureKind.Warehouse, -24, 12, 90, new FVector3(14, 8, 10), Palette.Hex(0xC9C2B0));
        Home.PlaceContact(Home.Center + new FVector3(4.0f, 0.0f, 9.0f));
        Soldiers(Home, ESoldierRole.Rifleman, ESoldierRole.Rifleman, ESoldierRole.Gunner, ESoldierRole.Rifleman);
        Parked(Home, EVehicleType.FuelTruck, 10, -4, 90);
        Parked(Home, EVehicleType.Jeep, -6, 34, 0);

        for (int Index = 0; Index < 3; ++Index)
        {
            FVector3 Out = Home.Center + new FVector3(-40 - Index * 14, 0, -44 + Index * 9);
            Spawner.SpawnStructure(EStructureKind.Derrick, EFaction.Oil, Out, Index * 30.0f, new FVector3(4, 12, 4), Palette.Rust, Home);
        }
    }

    private void BuildCamp(Site Home)
    {
        Mercs.Factions.Get(EFaction.Guerrilla).Headquarters = Home.Center;
        Home.PlaceFlag(Home.Center + new FVector3(0.0f, 0.0f, 8.0f));
        Structure? Hq = Place(Home, EStructureKind.Shack, 0, -10, 0, new FVector3(9, 4, 7));
        if (Hq is not null)
        {
            Hq.bInvulnerable = true;
        }

        for (int Index = 0; Index < 5; ++Index)
        {
            float Angle = Index * 72.0f;
            FVector3 Offset = Geo.Heading(Angle) * 16.0f;
            Place(Home, EStructureKind.Tent, Offset.X, Offset.Z, Angle, new FVector3(5, 2.8f, 4), Palette.Hex(0x4F6B3A));
        }

        Place(Home, EStructureKind.Tower, 18, 18, 0, new FVector3(3, 8, 3));
        Home.PlaceContact(Home.Center + new FVector3(4.0f, 0.0f, -2.0f));
        Soldiers(Home, ESoldierRole.Rifleman, ESoldierRole.Rifleman, ESoldierRole.RocketTrooper, ESoldierRole.Rifleman, ESoldierRole.Officer);
        Parked(Home, EVehicleType.Technical, -14, 6, 45);
        Parked(Home, EVehicleType.Jeep, -18, -8, 120);
    }

    private void BuildHarbor(Site Home)
    {
        Mercs.Factions.Get(EFaction.Pirate).Headquarters = Home.Center;
        Home.PlaceFlag(Home.Center + new FVector3(-8.0f, 0.0f, 0.0f));
        Structure? Hq = Place(Home, EStructureKind.Warehouse, -14, 0, 90, new FVector3(16, 8, 11), Palette.Hex(0x7A6A58));
        if (Hq is not null)
        {
            Hq.bInvulnerable = true;
        }

        FVector3 DockBase = Home.Center + new FVector3(Home.Radius + 14.0f, 0.0f, 0.0f);
        Spawner.SpawnStructure(EStructureKind.Dock, EFaction.Pirate, new FVector3(DockBase.X, 0.9f, DockBase.Z), 90.0f, new FVector3(6, 1, 36), Palette.Wood, Home);
        for (int Index = 0; Index < 6; ++Index)
        {
            FVector4 Color = new[] { Palette.Hex(0xA13D2D), Palette.Hex(0x2D5FA1), Palette.Hex(0x3D8A45), Palette.Hex(0xC9A227) }[Index % 4];
            Place(Home, EStructureKind.Container, 8 + (Index % 3) * 3, -16 + (Index / 3) * 7, 0, new FVector3(2.5f, 2.6f, 6), Color);
        }

        Place(Home, EStructureKind.Shack, 6, 16, 20, new FVector3(6, 3.5f, 5));
        Place(Home, EStructureKind.Shack, -4, 20, -15, new FVector3(5, 3.5f, 5));
        Home.PlaceContact(Home.Center + new FVector3(-2.0f, 0.0f, 6.0f));
        Soldiers(Home, ESoldierRole.Rifleman, ESoldierRole.Rifleman, ESoldierRole.Gunner, ESoldierRole.Rifleman, ESoldierRole.RocketTrooper);
        Parked(Home, EVehicleType.Technical, 0, -24, 0);
        Parked(Home, EVehicleType.Sedan, -20, 18, 70);
    }

    private void BuildCapital(Site Home)
    {
        Home.PlaceFlag(Home.Center + new FVector3(0.0f, 0.0f, -6.0f));
        Place(Home, EStructureKind.Statue, 0, 0, 180, new FVector3(5, 11, 5), Palette.Concrete, EFaction.VZ);

        FVector4[] Walls = { Palette.Plaster, Palette.PlasterPink, Palette.PlasterBlue, Palette.PlasterGreen };
        int Houses = 0;
        for (int Row = -2; Row <= 2; ++Row)
        {
            for (int Column = -2; Column <= 2; ++Column)
            {
                if (MathF.Abs(Row) <= 0.5f && MathF.Abs(Column) <= 1)
                {
                    continue;
                }

                if (Row >= 1 && Column >= 1)
                {
                    continue;
                }

                float X = Column * 22.0f + Mercs.Range(-2.0f, 2.0f);
                float Z = Row * 22.0f + Mercs.Range(-2.0f, 2.0f);
                EStructureKind Kind = (Houses % 5) switch
                {
                    3 => EStructureKind.Shop,
                    _ => EStructureKind.House,
                };
                if (Houses == 7)
                {
                    Kind = EStructureKind.Church;
                }

                Place(Home, Kind, X, Z, (Houses % 2) * 90.0f, new FVector3(Mercs.Range(8, 11), Mercs.Range(5, 7), Mercs.Range(7, 9)), Walls[Houses % Walls.Length], EFaction.Civilian);
                ++Houses;
            }
        }

        FVector3 Fort = new(38.0f, 0.0f, 36.0f);
        Place(Home, EStructureKind.Headquarters, Fort.X, Fort.Z, 225, new FVector3(20, 12, 14), Palette.Hex(0x8C8A6E), EFaction.VZ);
        Place(Home, EStructureKind.Bunker, Fort.X - 16, Fort.Z - 6, 225, new FVector3(8, 3, 6), null, EFaction.VZ);
        Place(Home, EStructureKind.Bunker, Fort.X - 4, Fort.Z - 18, 225, new FVector3(8, 3, 6), null, EFaction.VZ);
        Place(Home, EStructureKind.AntiAir, Fort.X + 12, Fort.Z - 12, 225, new FVector3(4, 2, 4), null, EFaction.VZ);
        Place(Home, EStructureKind.Tower, Fort.X - 20, Fort.Z + 14, 0, new FVector3(3.5f, 10, 3.5f), null, EFaction.VZ);
        Place(Home, EStructureKind.Barracks, Fort.X + 18, Fort.Z + 10, 135, new FVector3(14, 4, 7), Palette.Hex(0x8C8A6E), EFaction.VZ);
        Place(Home, EStructureKind.Helipad, Fort.X + 2, Fort.Z + 22, 0, new FVector3(14, 0.5f, 14), null, EFaction.VZ);

        Soldiers(Home, ESoldierRole.Rifleman, ESoldierRole.Rifleman, ESoldierRole.Rifleman, ESoldierRole.Gunner, ESoldierRole.Gunner, ESoldierRole.RocketTrooper,
            ESoldierRole.RocketTrooper, ESoldierRole.Sniper, ESoldierRole.Officer, ESoldierRole.Rifleman, ESoldierRole.Rifleman);
        for (int Index = 0; Index < 5; ++Index)
        {
            Home.Garrison.Add(new FGarrisonSlot { Role = ESoldierRole.Civilian, Spot = Geo.RandomAround(Home.Center, 10.0f, 45.0f) });
        }

        Parked(Home, EVehicleType.Tank, Fort.X - 12, Fort.Z + 2, 225, true);
        Parked(Home, EVehicleType.Apc, Fort.X + 4, Fort.Z - 30, 180, true);
        Parked(Home, EVehicleType.AttackHeli, Fort.X + 2, Fort.Z + 22, 0, true);
        Parked(Home, EVehicleType.Sedan, -30, 12, 90);
        Parked(Home, EVehicleType.Sedan, 14, -30, 0);
    }

    private void BuildOutpost(Site Home, bool bFuel, bool bRadar, bool bArmor)
    {
        Home.PlaceFlag(Home.Center + new FVector3(2.0f, 0.0f, 2.0f));
        float Yaw = Geo.YawOf(Nodes["CAP"].Center - Home.Center);
        FVector3 Forward = Geo.Heading(Yaw);
        FVector3 Right = Geo.RightOf(Yaw);
        FVector3 L(float X, float Z) => Right * X + Forward * Z;

        FVector3 A = L(-9, -6);
        Place(Home, EStructureKind.Barracks, A.X, A.Z, Yaw, new FVector3(12, 4, 6), Palette.Hex(0x8C8A6E));
        FVector3 B = L(9, 6);
        Place(Home, EStructureKind.Bunker, B.X, B.Z, Yaw, new FVector3(7, 3, 5));
        FVector3 T1 = L(-16, 14);
        Place(Home, EStructureKind.Tower, T1.X, T1.Z, Yaw, new FVector3(3, 8, 3));
        FVector3 T2 = L(16, -14);
        Place(Home, EStructureKind.Tower, T2.X, T2.Z, Yaw, new FVector3(3, 8, 3));
        for (int Index = -1; Index <= 1; ++Index)
        {
            FVector3 S = L(Index * 5.0f, 16.0f);
            Place(Home, EStructureKind.Sandbags, S.X, S.Z, Yaw, new FVector3(4, 1.2f, 0.8f));
        }

        FVector3 C = L(3, -12);
        Place(Home, EStructureKind.Crate, C.X, C.Z, Yaw + 12, new FVector3(1.4f, 1.3f, 1.4f));
        Place(Home, EStructureKind.Crate, C.X + 1.8f, C.Z, Yaw - 8, new FVector3(1.2f, 1.1f, 1.2f));
        Place(Home, EStructureKind.Tent, L(-2, 6).X, L(-2, 6).Z, Yaw + 90, new FVector3(5, 2.6f, 4), Palette.Canvas);

        if (bFuel)
        {
            FVector3 F = L(12, -10);
            Place(Home, EStructureKind.FuelTank, F.X, F.Z, 0, new FVector3(4, 5, 4));
        }

        if (bRadar)
        {
            FVector3 R = L(-10, 10);
            Place(Home, EStructureKind.Radar, R.X, R.Z, Yaw, new FVector3(6, 9, 6), null)!.Label = "VZ Radar";
            FVector3 Antenna = L(0, -18);
            Place(Home, EStructureKind.Antenna, Antenna.X, Antenna.Z, 0, new FVector3(2, 24, 2));
        }

        Soldiers(Home, ESoldierRole.Rifleman, ESoldierRole.Rifleman, ESoldierRole.Rifleman, ESoldierRole.Gunner, ESoldierRole.RocketTrooper, ESoldierRole.Sniper);
        FVector3 P = L(-6, 18);
        Parked(Home, EVehicleType.Technical, P.X, P.Z, Yaw, true);
        if (bArmor)
        {
            FVector3 Armor = L(6, 20);
            Parked(Home, Home.Name.Contains("AA") ? EVehicleType.Apc : EVehicleType.Tank, Armor.X, Armor.Z, Yaw, true);
        }

        if (Home.Name.Contains("AA"))
        {
            for (int Index = 0; Index < 2; ++Index)
            {
                FVector3 Gun = L(Index == 0 ? -12 : 12, 0);
                Structure? Emplacement = Place(Home, EStructureKind.AntiAir, Gun.X, Gun.Z, Yaw, new FVector3(4, 2, 4));
                if (Emplacement is not null)
                {
                    Emplacement.Label = "AA Gun";
                }
            }
        }
    }

    private void BuildVillage(Site Home, int Count)
    {
        FVector4[] Walls = { Palette.Plaster, Palette.PlasterPink, Palette.PlasterBlue, Palette.PlasterGreen, Palette.Hex(0xE3D6B5) };
        for (int Index = 0; Index < Count; ++Index)
        {
            float Angle = Index * (360.0f / Count) + Mercs.Range(-10.0f, 10.0f);
            FVector3 Offset = Geo.Heading(Angle) * Mercs.Range(Home.Radius * 0.45f, Home.Radius * 0.85f);
            EStructureKind Kind = Index % 4 == 3 ? EStructureKind.Shack : (Index == 0 ? EStructureKind.Church : EStructureKind.House);
            FVector3 Size = Kind == EStructureKind.Shack ? new FVector3(5, 3.2f, 4.5f) : new FVector3(Mercs.Range(7, 10), Mercs.Range(4.5f, 6), Mercs.Range(6, 8));
            Place(Home, Kind, Offset.X, Offset.Z, Angle + 180.0f, Size, Walls[Index % Walls.Length], EFaction.Civilian);
        }

        Place(Home, EStructureKind.Silo, Home.Radius * 0.2f, -Home.Radius * 0.2f, 0, new FVector3(4, 8, 4), Palette.Hex(0xB8B09A), EFaction.Civilian);
        for (int Index = 0; Index < 4; ++Index)
        {
            Home.Garrison.Add(new FGarrisonSlot { Role = ESoldierRole.Civilian, Spot = Geo.RandomAround(Home.Center, 2.0f, Home.Radius * 0.5f) });
        }

        Parked(Home, EVehicleType.Sedan, 4, -6, Mercs.Range(0, 360));
        Parked(Home, EVehicleType.Sedan, -6, 5, Mercs.Range(0, 360));
    }

    private void BuildFuelDepot(Site Home, EFaction Owner)
    {
        Home.PlaceFlag(Home.Center + new FVector3(-8.0f, 0.0f, -8.0f));
        Place(Home, EStructureKind.FuelTank, -6, 4, 0, new FVector3(5, 6, 5));
        Place(Home, EStructureKind.FuelTank, 2, 6, 0, new FVector3(5, 6, 5));
        Place(Home, EStructureKind.Shack, 8, -6, 0, new FVector3(5, 3, 4), Palette.RoofTin);
        for (int Index = 0; Index < 3; ++Index)
        {
            Pickup.Spawn(EPickupKind.Fuel, Home.Center + new FVector3(-4.0f + Index * 1.5f, 0.0f, -6.0f), 50, EWeapon.None, 0.0f);
        }

        Parked(Home, EVehicleType.FuelTruck, 10, 8, 90);
        if (Owner == EFaction.VZ)
        {
            Soldiers(Home, ESoldierRole.Rifleman, ESoldierRole.Rifleman, ESoldierRole.Gunner);
        }
        else
        {
            Soldiers(Home, ESoldierRole.Rifleman, ESoldierRole.Rifleman);
        }
    }

    private List<FVector3> Route(params string[] Keys)
    {
        List<FVector3> Points = new();
        for (int Index = 0; Index < Keys.Length; ++Index)
        {
            FVector3 From = Nodes[Keys[Index]].Center;
            FVector3 To = Nodes[Keys[(Index + 1) % Keys.Length]].Center;
            for (int Step = 0; Step < 4; ++Step)
            {
                Points.Add(Geo.Ground(FVector3.Lerp(From, To, Step / 4.0f)));
            }
        }

        return Points;
    }

    private void AddPatrols()
    {
        Nodes["O1"].VehicleSlots.Add(new FVehicleSlot { Type = EVehicleType.Technical, Spot = Nodes["O1"].Center + new FVector3(0, 0, 25), bCrewed = true, Route = Route("O1", "CAP", "O5", "V1", "PMC", "F1") });
        Nodes["O2"].VehicleSlots.Add(new FVehicleSlot { Type = EVehicleType.Apc, Spot = Nodes["O2"].Center + new FVector3(0, 0, 25), bCrewed = true, Route = Route("O2", "CAP", "O4", "V4", "F2") });
        Nodes["O3"].VehicleSlots.Add(new FVehicleSlot { Type = EVehicleType.Jeep, Spot = Nodes["O3"].Center + new FVector3(20, 0, 0), bCrewed = true, Route = Route("O3", "F3", "O6", "PLAV", "OIL") });
        Nodes["O5"].VehicleSlots.Add(new FVehicleSlot { Type = EVehicleType.Truck, Spot = Nodes["O5"].Center + new FVector3(-20, 0, 0), bCrewed = true, Route = Route("O5", "V2", "AN", "F2", "O2", "CAP") });
        Nodes["AN"].VehicleSlots.Add(new FVehicleSlot { Type = EVehicleType.Jeep, Spot = Nodes["AN"].Center + new FVector3(0, 0, 45), bCrewed = true, Route = Route("AN", "F2", "O2", "CAP", "O5", "V2") });
        Nodes["CHN"].VehicleSlots.Add(new FVehicleSlot { Type = EVehicleType.Technical, Spot = Nodes["CHN"].Center + new FVector3(-45, 0, 0), bCrewed = true, Route = Route("CHN", "O4", "O6", "F3", "CAP", "O4") });
        Nodes["PLAV"].VehicleSlots.Add(new FVehicleSlot { Type = EVehicleType.Technical, Spot = Nodes["PLAV"].Center + new FVector3(20, 0, -20), bCrewed = true, Route = Route("PLAV", "O6", "F3", "O3", "OIL") });
        Nodes["F3"].VehicleSlots.Add(new FVehicleSlot { Type = EVehicleType.Sedan, Spot = Nodes["F3"].Center + new FVector3(0, 0, -20), bCrewed = true, Route = Route("F3", "CAP", "O1", "F1", "PMC", "V3", "O3") });
    }

    private void AddDecor(FVector3 At, Action<MeshKit, FVector3> Draw)
    {
        (int, int) Key = ((int)MathF.Floor(At.X / DecorCell), (int)MathF.Floor(At.Z / DecorCell));
        if (!Decor.TryGetValue(Key, out MeshKit? Kit))
        {
            Kit = new MeshKit();
            Decor[Key] = Kit;
        }

        FVector3 Origin = new(Key.Item1 * DecorCell, 0.0f, Key.Item2 * DecorCell);
        Draw(Kit, At - Origin);
    }

    private bool IsClear(FVector3 At, float Margin)
    {
        foreach (Site Place in Mercs.Sites)
        {
            if (Geo.FlatDistance(Place.Center, At) < Place.Radius + Margin)
            {
                return false;
            }
        }

        foreach (FRoad Road in Terrain.AllRoads)
        {
            FVector3 Segment = Road.To - Road.From;
            float LengthSq = Segment.X * Segment.X + Segment.Z * Segment.Z;
            float T = Mathf.Clamp01(((At.X - Road.From.X) * Segment.X + (At.Z - Road.From.Z) * Segment.Z) / MathF.Max(LengthSq, 1.0f));
            FVector3 Closest = Road.From + Segment * T;
            if (Geo.FlatDistance(Closest, At) < Road.Width + Margin * 0.5f)
            {
                return false;
            }
        }

        return true;
    }

    private void ScatterDecor()
    {
        Random Rng = new(4242);
        for (int Attempt = 0; Attempt < 32000; ++Attempt)
        {
            float X = ((float)Rng.NextDouble() - 0.5f) * Terrain.HalfSize * 1.8f;
            float Z = ((float)Rng.NextDouble() - 0.5f) * Terrain.HalfSize * 1.8f;
            float Height = Terrain.HeightAt(X, Z);
            if (Height < 1.0f)
            {
                continue;
            }

            FVector3 At = new(X, Height - 0.1f, Z);
            if (!IsClear(At, 6.0f))
            {
                continue;
            }

            // Groves and open meadows rather than an even sprinkle, thickening into jungle to the north.
            float Jungle = Mathf.SmoothStep(60.0f, 220.0f, Z);
            float Grove = Mathf.SmoothStep(0.5f, 0.72f, Noise.Fbm(X * 0.0055f + 3.0f, Z * 0.0055f - 11.0f));
            float Density = Mathf.Clamp01(Grove * 0.85f + Jungle * 0.6f + 0.06f);
            float Scale = 0.7f + (float)Rng.NextDouble() * 0.7f;
            float Yaw = (float)Rng.NextDouble() * 360.0f;
            double Roll = Rng.NextDouble();

            if (Height < 3.5f)
            {
                if (Roll < 0.16)
                {
                    Mercs.Destruction.AddFoliage(EFoliageKind.Palm, At, Scale, Yaw);
                }
            }
            else if (Roll < Density * 0.5f)
            {
                Mercs.Destruction.AddFoliage(Jungle > 0.5f ? EFoliageKind.JungleTree : EFoliageKind.Tree, At, Scale, Yaw);
            }
            else if (Roll < Density * 0.9f)
            {
                Mercs.Destruction.AddFoliage(EFoliageKind.Bush, At, Scale, Yaw);
            }
            else if (Roll < Density * 0.9f + 0.012f * (1.0f - Jungle))
            {
                Mercs.Destruction.AddFoliage(EFoliageKind.Palm, At, Scale, Yaw);
            }
            else if (Roll > 0.985)
            {
                AddDecor(At, (Kit, Local) => Kit.Box(Local + new FVector3(0.0f, 0.4f * Scale, 0.0f), new FVector3(1.2f, 0.8f, 1.0f) * Scale, Palette.Shade(Palette.Rock, 0.8f + Scale * 0.2f), FQuat.FromEuler(0.2f, Scale * 3.0f, 0.1f)));
            }
        }
    }

    private void CommitDecor()
    {
        foreach (((int X, int Z) Key, MeshKit Kit) in Decor)
        {
            Entity Cell = World.CreateEntity($"Decor_{Key.X}_{Key.Z}", new FVector3(Key.X * DecorCell, 0.0f, Key.Z * DecorCell));
            Kit.Commit(Registry, Cell);
        }
    }

    private void DefineHvts()
    {
        AddHvt("Gen. Hector Carmona", "Commander, Outpost Cordoba", EFaction.VZ, 60000, "O1", 2);
        AddHvt("Maj. Lucia Vargas", "Garrison chief, Fort Almeida", EFaction.VZ, 50000, "O3", 2);
        AddHvt("Col. Esteban Ruiz", "Air defense commander", EFaction.VZ, 75000, "O4", 3);
        AddHvt("'Blackjack' Marley", "Pirate lieutenant gone rogue", EFaction.Pirate, 45000, "PIR", 1);
        AddHvt("Ramon Solano", "The dictator himself", EFaction.VZ, 250000, "CAP", 4);
    }

    private void AddHvt(string Name, string Title, EFaction Faction, int Bounty, string SiteKey, int Guards)
    {
        HvtTarget Target = new() { Name = Name, Title = Title, Faction = Faction, Bounty = Bounty, Home = Nodes[SiteKey], Guards = Guards };
        Nodes[SiteKey].Hvt = Target;
        Mercs.Contracts.Hvts.Add(Target);
    }

    private List<Structure> Find(string SiteKey, EStructureKind Kind, int Max = 8)
    {
        List<Structure> Result = Nodes[SiteKey].Buildings.FindAll(Building => Building.Kind == Kind);
        return Result.Count > Max ? Result.GetRange(0, Max) : Result;
    }

    private void DefineContracts()
    {
        ContractBoard Board = Mercs.Contracts;

        Contract Radar = Board.Add(EFaction.Allied, "Radar Blackout", "The VZ radar on Radar Hill is spotting our air patrols. Level it.", 40000, EObjectiveKind.DestroyTargets);
        Radar.Targets.AddRange(Find("O2", EStructureKind.Radar));
        Radar.Targets.AddRange(Find("O2", EStructureKind.Antenna));

        Contract Cordoba = Board.Add(EFaction.Allied, "Liberate Cordoba", "Wipe out the VZ garrison holding Outpost Cordoba.", 55000, EObjectiveKind.ClearSite);
        Cordoba.TargetSite = Nodes["O1"];

        Contract Armor = Board.Add(EFaction.Allied, "Armor Hunt", "Destroy three VZ vehicles anywhere on the island.", 60000, EObjectiveKind.DestroyVehicles);
        Armor.Needed = 3;
        Armor.VehicleFaction = EFaction.VZ;
        Armor.TargetSite = Nodes["CAP"];

        Contract Guns = Board.Add(EFaction.China, "Silence the Guns", "The VZ anti-air battery up north keeps our transports grounded. Destroy the AA guns.", 45000, EObjectiveKind.DestroyTargets);
        Guns.Targets.AddRange(Find("O4", EStructureKind.AntiAir));

        Contract Tank = Board.Add(EFaction.China, "Tank Acquisition", "Steal a VZ main battle tank and deliver it to PLA Command.", 70000, EObjectiveKind.DeliverVehicle);
        Tank.DeliverType = EVehicleType.Tank;
        Tank.Destination = Nodes["CHN"];
        Tank.TargetSite = Nodes["O1"];

        Contract Sabotage = Board.Add(EFaction.China, "Allied Supply Sabotage", "Blow the Allied warehouse at their base. Don't let them see who did it.", 50000, EObjectiveKind.DestroyTargets);
        Sabotage.Targets.AddRange(Find("AN", EStructureKind.Warehouse));

        Contract FuelRun = Board.Add(EFaction.Oil, "Oil Recovery", "The VZ stole one of our fuel trucks. Bring a fuel truck to the UP Refinery.", 35000, EObjectiveKind.DeliverVehicle);
        FuelRun.DeliverType = EVehicleType.FuelTruck;
        FuelRun.Destination = Nodes["OIL"];
        FuelRun.TargetSite = Nodes["F1"];

        Contract Caches = Board.Add(EFaction.Oil, "Rebel Nuisance", "PLAV tents at their jungle camp hide weapons. Burn them down.", 45000, EObjectiveKind.DestroyTargets);
        Caches.Targets.AddRange(Find("PLAV", EStructureKind.Tent, 3));

        Contract Statue = Board.Add(EFaction.Guerrilla, "Tear Down the Tyrant", "Topple the statue of Solano in the Maracaibo plaza.", 30000, EObjectiveKind.DestroyTargets);
        Statue.Targets.AddRange(Find("CAP", EStructureKind.Statue));

        Contract Barracks = Board.Add(EFaction.Guerrilla, "Barracks Bust", "Destroy the VZ barracks at Fort Almeida and the Jungle Barracks.", 50000, EObjectiveKind.DestroyTargets);
        Barracks.Targets.AddRange(Find("O3", EStructureKind.Barracks));
        Barracks.Targets.AddRange(Find("O6", EStructureKind.Barracks));

        Contract Pipeline = Board.Add(EFaction.Guerrilla, "Pipeline Payback", "UP bleeds our land dry. Destroy their oil derricks.", 40000, EObjectiveKind.DestroyTargets);
        Pipeline.Targets.AddRange(Find("OIL", EStructureKind.Derrick));

        Contract Heist = Board.Add(EFaction.Pirate, "Chopper Heist", "Steal the VZ attack helicopter from Maracaibo and land it at Pirate Harbor.", 80000, EObjectiveKind.DeliverVehicle);
        Heist.DeliverType = EVehicleType.AttackHeli;
        Heist.Destination = Nodes["PIR"];
        Heist.TargetSite = Nodes["CAP"];

        Contract Convoy = Board.Add(EFaction.Pirate, "Convoy Raid", "Wreck four VZ vehicles. We'll pick the carcasses.", 50000, EObjectiveKind.DestroyVehicles);
        Convoy.Needed = 4;
        Convoy.VehicleFaction = EFaction.VZ;
        Convoy.TargetSite = Nodes["O2"];

        Contract Solano = Board.Add(EFaction.Allied, "The Big One", "Capture Ramon Solano alive at the Maracaibo fortress.", 100000, EObjectiveKind.CaptureHvt);
        Solano.Hvt = Mercs.Contracts.Hvts.Find(Target => Target.Name == "Ramon Solano");
        Solano.Standing = 35.0f;
    }
}
