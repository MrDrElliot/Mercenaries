using System;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

// The only entity a level needs, since it builds the island, spawns the mercenary and ticks every game system.
public sealed class GameDirector : EntityScript
{
    [Property(Category = "Game")]
    public EMercenary Mercenary = EMercenary.Mattias;

    [Property(Category = "Game", Tooltip = "Spawn at the PMC. Off drops you outside Maracaibo for testing.")]
    public bool bStartAtPmc = true;

    [Property(Category = "Game")]
    public int StartingCash = 30000;

    [Property(Category = "Game", Tooltip = "0 god mode, 1 go to HVT, 2 attack heli, 3 turn camera, 4 kill nearest soldier, 5 jeep, 6 next site, 7 money and support, 8 VZ squad, 9 VZ tank, minus coastal vantage, equals blast at aim.")]
    public bool bDevCheats = true;

    private int CheatSite;
    private int VantageIndex;
    // A spawned vehicle only becomes enterable once its script is ready, a frame or two later.
    private Vehicle? PendingRide;
    public bool bGodMode;
    private bool bBuilt;
    private float SiteTimer;
    private int CheatPlant = -1;

    public override void OnReady()
    {
        Mercs.Reset(World, this);
        Materials.Reset();
        Controls.Reset();
        Terrain.Reset();
        HumanoidBody.Reset();
        Sfx.Reset();
        Music.Reset();
        Mercs.Wallet.Cash = StartingCash;

        DateTime Started = DateTime.Now;
        new WorldBuilder(World).Build();
        Mercs.Fx.Initialize();
        Gore.Reset();
        Mercs.Ordnance.Initialize();

        Site? Hq = Mercs.Sites.Find(Site => Site.Kind == ESiteKind.PmcHq);
        Site? Capital = Mercs.Sites.Find(Site => Site.Kind == ESiteKind.Capital);
        FVector3 Spawn = bStartAtPmc || Capital is null ? (Hq?.PlayerSpawn ?? new FVector3(0.0f, 30.0f, 0.0f)) : Geo.Ground(Capital.Center + new FVector3(-40.0f, 0.0f, -40.0f)) + new FVector3(0.0f, 1.5f, 0.0f);

        Entity PlayerEntity = World.CreateEntity("Mercenary", Spawn);
        MercPlayer? Player = Registry.AddScript<MercPlayer>(PlayerEntity);
        if (Player is not null)
        {
            Player.Merc = Mercenary;
        }

        Entity CameraEntity = World.CreateEntity("MercCamera", Spawn + new FVector3(0.0f, 3.0f, -5.0f));
        MercCamera? Camera = Registry.AddScript<MercCamera>(CameraEntity);
        if (Camera is not null && Capital is not null)
        {
            Camera.Yaw = Geo.YawOf(Capital.Center - Spawn);
        }

        AddScript<MercsHud>();
        Sfx.StartAmbience();

        bBuilt = true;
        double Seconds = (DateTime.Now - Started).TotalSeconds;
        Debug.Log($"Mercenaries: island built in {Seconds:0.00}s, {Mercs.Sites.Count} sites.");
        Mercs.Feed.Announce("MERCENARIES", $"{MercPlayer.MercName(Mercenary)} lands in Venezuela. Solano stiffed you. Make him pay.", 7.0f);
        Mercs.Feed.Post("Visit a faction contact (blue marker) for contracts, or press [Tab] for the PDA.", ENewsTone.Good);
        Mercs.Feed.Post("Press [F1] for the controls.", ENewsTone.Neutral);
    }

    public override void OnDetach()
    {
        if (Mercs.Director == this)
        {
            Sfx.Shutdown();
            Music.Shutdown();
            Mercs.Trailer.Shutdown();
            Mercs.Destruction.Shutdown();
            Mercs.Shutdown();
        }
    }

    public override void OnUpdate(float DeltaTime)
    {
        if (!bBuilt)
        {
            return;
        }

        Mercs.bInfantryNavReady = CNavigationLibrary.IsReady(World);
        Mercs.bVehicleNavReady = CNavigationLibrary.IsReady(World, Mercs.VehicleNavAgent);

        float Step = MathF.Min(DeltaTime, 0.1f);
        Mercs.Time += Step;

        Mercs.Trailer.Update(DeltaTime);
        Mercs.AiStats.Update(DeltaTime);
        if (!Mercs.Trailer.IsClockFrozen)
        {
            Mercs.Clock.Update(Step);
        }
        Mercs.Factions.Update(Step);
        Mercs.Feed.Update(Step);
        Mercs.Fx.Update(Step);
        Gore.Update(Step);
        Mercs.Ordnance.Update(Step);
        Mercs.Throwables.Update(Step);
        Mercs.Support.Update(Step);
        Mercs.Contracts.Update(Step);
        Mercs.Destruction.Update(Step);
        Sfx.Update();
        Music.Update();
        Mercs.Fx.Flush();

        if (bDevCheats)
        {
            TickCheats();
        }

        SiteTimer -= Step;
        if (SiteTimer <= 0.0f)
        {
            SiteTimer = 0.5f;
            foreach (Site Place in Mercs.Sites)
            {
                Place.Update(0.5f);
            }
        }
    }

    private void TickCheats()
    {
        MercPlayer? Player = Mercs.Player;
        if (Player is null)
        {
            return;
        }

        float Yaw = MercCamera.Instance?.Yaw ?? 0.0f;
        bool bShift = Controls.KeyDown(EKey.LeftShift);

        if (PendingRide is { IsAlive: true } Ride)
        {
            Player.EnterVehicle(Ride);
            PendingRide = null;
        }

        if (Controls.KeyPressed(EKey.D5))
        {
            Vehicle? Jeep = Spawner.SpawnVehicle(EVehicleType.Jeep, EFaction.Merc, OpenSpotNear(Player, Yaw, 12.0f), Yaw, false, null, null);
            PendingRide = bShift ? Jeep : null;
        }

        // Shift steps through the plant kinds instead, to look at the foliage up close.
        if (Controls.KeyPressed(EKey.D6) && bShift)
        {
            CheatPlant++;
            EFoliageKind Kind = (EFoliageKind)(CheatPlant % ((int)EFoliageKind.Bush + 1));
            if (Mercs.Destruction.PlantOf(Kind, CheatPlant / ((int)EFoliageKind.Bush + 1)) is { } Plant)
            {
                FVector3 Arrival = Geo.Ground(Plant + new FVector3(0.0f, 0.0f, -9.0f)) + new FVector3(0.0f, 1.5f, 0.0f);
                Player.Teleport(Arrival);
                MercCamera.Instance?.SnapBehind(Geo.YawOf(Plant - Arrival));
                Mercs.Feed.Post($"Teleported to a {Kind}", ENewsTone.Neutral);
            }
        }
        else if (Controls.KeyPressed(EKey.D6))
        {
            CheatSite = (CheatSite + 1) % Mercs.Sites.Count;
            Site Place = Mercs.Sites[CheatSite];
            FVector3 Arrival = Geo.Ground(Place.Center + new FVector3(Place.Radius + 12.0f, 0.0f, 0.0f)) + new FVector3(0.0f, 1.5f, 0.0f);
            Player.Teleport(Arrival);
            MercCamera.Instance?.SnapBehind(Geo.YawOf(Place.Center - Arrival));
            Mercs.Feed.Post($"Teleported to {Place.Name}", ENewsTone.Neutral);
        }

        if (Controls.KeyPressed(EKey.D7))
        {
            Mercs.Wallet.AddCash(100000, "dev cheat");
            Mercs.Wallet.AddFuel(1000, "dev cheat");
            foreach (SupportItem Item in SupportCatalog.Items)
            {
                if (!Item.bInstant)
                {
                    Mercs.Support.Inventory[Item.Kind] = Mercs.Support.Count(Item.Kind) + 1;
                }
            }
        }

        // Kills the nearest soldier in front, by a headshot or with Shift by a grenade at their feet, to check gore up close.
        if (Controls.KeyPressed(EKey.K) && NearestInFront(Player, Yaw) is { } Victim)
        {
            FVector3 Head = Victim.Position + new FVector3(0.0f, 0.7f, 0.0f);
            if (bShift)
            {
                Explosion.Detonate(Victim.Position - new FVector3(0.0f, 0.6f, 0.0f), 3.0f, 320.0f, Player, 1.0f, true, Player);
            }
            else
            {
                FVector3 ShotDirection = (Head - (Player.Position + new FVector3(0.0f, 0.6f, 0.0f))).NormalizedOr(Geo.Heading(Yaw));
                Gore.Wound(Head, ShotDirection, 209.0f, Victim.Owner);
                Victim.TakeHit(FHit.From(Player, 209.0f, EDamageKind.Bullet, Head, ShotDirection));
            }
        }

        if (Controls.KeyPressed(EKey.D8))
        {
            FVector3 Drop = OpenSpotNear(Player, Yaw, bShift ? 10.0f : 30.0f);

            for (int Index = 0; Index < 4; ++Index)
            {
                Spawner.SpawnSoldier(EFaction.VZ, Index == 0 ? ESoldierRole.Gunner : ESoldierRole.Rifleman, Geo.RandomAround(Drop, 0.0f, 5.0f), null);
            }
        }

        if (Controls.KeyPressed(EKey.D0))
        {
            bGodMode = !bGodMode;
            Mercs.Feed.Post(bGodMode ? "God mode on" : "God mode off", ENewsTone.Neutral);
        }

        if (Controls.KeyPressed(EKey.D1))
        {
            foreach (HvtTarget Target in Mercs.Contracts.Hvts)
            {
                if (Target.bResolved)
                {
                    continue;
                }

                if (Target.Live is { IsAlive: true } Near && FVector3.Distance(Near.Position, Player.Position) < 15.0f)
                {
                    Near.TakeHit(FHit.From(Player, 45.0f, EDamageKind.Melee, Near.Position, Geo.Heading(Yaw)));
                    break;
                }

                if (Target.Live is { IsAlive: true } Hvt)
                {
                    Player.Teleport(Hvt.Position + Geo.Heading(Yaw + 180.0f) * 1.8f + new FVector3(0.0f, 0.3f, 0.0f));
                    MercCamera.Instance?.SnapBehind(Geo.YawOf(Hvt.Position - Player.Position));
                    break;
                }

                FVector3 Arrival = Geo.Ground(Target.Home.Center + new FVector3(Target.Home.Radius + 110.0f, 0.0f, 0.0f)) + new FVector3(0.0f, 1.5f, 0.0f);
                Player.Teleport(Arrival);
                MercCamera.Instance?.SnapBehind(Geo.YawOf(Target.Home.Center - Arrival));
                break;
            }
        }

        if (Controls.KeyPressed(EKey.D2))
        {
            Vehicle? Heli = Spawner.SpawnVehicle(EVehicleType.AttackHeli, EFaction.Merc, OpenSpotNear(Player, Yaw, 9.0f), Yaw, false, null, null);
            PendingRide = bShift ? Heli : null;
        }

        if (Controls.KeyPressed(EKey.D3) && bShift)
        {
            Mercs.Clock.Skip(3.0f);
            Mercs.Feed.Post($"Time {Mercs.Clock.Hour:00}:{Mercs.Clock.Minute:00}", ENewsTone.Neutral);
        }
        else if (Controls.KeyPressed(EKey.D3) && MercCamera.Instance is { } Camera)
        {
            Camera.Yaw += 90.0f;
        }

        if (Controls.KeyPressed(EKey.D4))
        {
            Soldier? Nearest = null;
            foreach (Soldier Candidate in Mercs.Soldiers)
            {
                if (Candidate.IsAlive && FVector3.Distance(Candidate.Position, Player.Position) < 60.0f && (Nearest is null || FVector3.Distance(Candidate.Position, Player.Position) < FVector3.Distance(Nearest.Position, Player.Position)))
                {
                    Nearest = Candidate;
                }
            }

            Nearest?.TakeHit(FHit.From(Player, 9999.0f, EDamageKind.Bullet, Nearest.Position, Geo.Heading(Yaw)));
        }

        if (Controls.KeyPressed(EKey.Minus))
        {
            VantageIndex = (VantageIndex + 1) % 8;
            float Angle = VantageIndex * MathF.PI * 0.25f;
            FVector3 Vantage = Geo.Ground(new FVector3(MathF.Cos(Angle) * 330.0f, 0.0f, MathF.Sin(Angle) * 330.0f));
            Player.Teleport(Vantage + new FVector3(0.0f, 1.5f, 0.0f));
            MercCamera.Instance?.SnapBehind(Geo.YawOf(-Vantage));
        }

        if (Controls.KeyPressed(EKey.Equal) && MercCamera.Instance is { } Aim)
        {
            Structure? Best = null;
            float BestScore = 0.0f;
            foreach (Structure Candidate in Mercs.Structures)
            {
                FVector3 Offset = Candidate.Position - Aim.ViewPosition;
                float Distance = Offset.Length;
                float Score = FVector3.Dot(Offset / MathF.Max(Distance, 0.1f), Aim.ViewForward) - Distance / 400.0f;
                if (Candidate.IsAlive && !Candidate.bInvulnerable && Distance < 120.0f && Score > BestScore)
                {
                    Best = Candidate;
                    BestScore = Score;
                }
            }

            bool bNearby = Controls.KeyDown(EKey.LeftShift);
            FVector3 Target = bNearby ? Geo.Ground(Player.Position + Geo.Heading(Aim.Yaw) * 6.0f) : (Best is not null ? Best.Position - Aim.ViewForward * (Best.Radius + 1.0f) : Aim.AimPoint);
            FVector3 Look = Target - Aim.ViewPosition;
            Aim.Yaw = Geo.YawOf(Look);
            Aim.Pitch = Mathf.Degrees(MathF.Atan2(Look.Y, Geo.Flat(Look).Length));
            Explosion.Detonate(Target, 9.0f, 900.0f, Player, 1.5f);
        }

        if (Controls.KeyPressed(EKey.D9))
        {
            Vehicle? Tank = Spawner.SpawnVehicle(EVehicleType.Tank, bShift ? EFaction.Merc : EFaction.VZ, OpenSpotNear(Player, Yaw, 9.0f), Yaw, !bShift, null, null);
            PendingRide = bShift ? Tank : null;
        }
    }

    private static Soldier? NearestInFront(MercPlayer Player, float Yaw)
    {
        Soldier? Best = null;
        float BestDistance = 60.0f;
        foreach (Soldier Candidate in Mercs.Soldiers)
        {
            FVector3 Offset = Candidate.Position - Player.Position;
            float Distance = Offset.Length;
            if (Candidate.IsAlive && Distance < BestDistance && FVector3.Dot(Geo.Flat(Offset).NormalizedOr(FVector3.Zero), Geo.Heading(Yaw)) > 0.3f)
            {
                Best = Candidate;
                BestDistance = Distance;
            }
        }
        return Best;
    }

    private static FVector3 OpenSpotNear(MercPlayer Player, float Yaw, float Distance)
    {
        for (float Offset = 0.0f; Offset < 360.0f; Offset += 30.0f)
        {
            FVector3 Candidate = Geo.Ground(Player.Position + Geo.Heading(Yaw + Offset) * Distance);
            bool bClear = Geo.LineOfSight(Player.Position + FVector3.Up, Candidate + FVector3.Up * 1.5f, Player.Owner, Entity.Null);
            foreach (Vehicle Ride in Mercs.Vehicles)
            {
                bClear &= FVector3.Distance(Ride.Position, Candidate) > 7.0f;
            }

            if (bClear)
            {
                return Candidate;
            }
        }

        return Geo.Ground(Player.Position + Geo.Heading(Yaw) * Distance);
    }

    public void OpenPda(EPdaTab Tab)
    {
        MercsHud.Instance?.OpenPda(Tab);
    }

    public void OpenContracts(EFaction Employer)
    {
        MercsHud.Instance?.OpenContracts(Employer);
    }

    public void OnPlayerDamaged(FHit Hit)
    {
        MercsHud.Instance?.OnPlayerDamaged(Hit.Amount);
    }
}
