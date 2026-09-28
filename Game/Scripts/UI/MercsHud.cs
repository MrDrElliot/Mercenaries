using System;
using System.Collections.Generic;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

public enum EPdaTab : byte
{
    Shop,
    Factions,
    Contracts,
    Bounties,
    Map,
}

public sealed class MercsHud : EntityScript
{
    [Property(Category = "UI")]
    public string Document = "/Game/Content/UI/MercsHud.rml";

    [Property(Category = "UI", Units = "m")]
    public float RadarRange = 160.0f;

    public static MercsHud? Instance;

    private readonly HudModel Model = new();
    private UIDataModel? Binding;
    private UIDocument Screen;
    private int FeedRevision = -1;
    private float SlowTimer;
    private float RadarTimer;
    private float DamageFlash;
    private int Cursor;

    public bool IsPdaOpen => Model.PdaOpen;

    public override void OnReady()
    {
        Instance = this;
        Binding = World.UI.AddModel("mercs", Model);
        if (!Binding.IsValid)
        {
            Debug.LogError("MercsHud: failed to register the 'mercs' data model.");
            return;
        }

        Screen = World.UI.LoadDocument(Document);
        if (!Screen.IsValid)
        {
            Debug.LogError($"MercsHud: failed to load '{Document}'.");
            return;
        }

        Screen.Show(false, false);
        World.UI.DisableCursor();
    }

    public override void OnDetach()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        Screen.Close();
        Binding?.Dispose();
    }

    // Hidden while a cinematic plays, so a trailer shows the world rather than the interface.
    public void SetVisible(bool bVisible)
    {
        if (!Screen.IsValid)
        {
            return;
        }

        if (bVisible)
        {
            Screen.Show(false, false);
        }
        else
        {
            Screen.Hide();
        }
    }

    public void OnPlayerDamaged(float Amount)
    {
        DamageFlash = MathF.Min(1.0f, DamageFlash + Amount / 60.0f);
    }

    public void OpenPda(EPdaTab Tab)
    {
        Sfx.Ui(Model.PdaOpen ? ESfx.UiClick : ESfx.UiOpen);
        Model.PdaTab = Tab.ToString().ToLowerInvariant();
        Model.PdaOpen = true;
        Controls.Enabled = false;
        World.UI.EnableCursor();
        RefreshPda();
    }

    public void OpenContracts(EFaction Employer)
    {
        Model.ContractEmployer = Employer;
        OpenPda(EPdaTab.Contracts);
    }

    public void ClosePda()
    {
        Sfx.Ui(ESfx.UiClose);
        Model.PdaOpen = false;
        Controls.Enabled = true;
        World.UI.DisableCursor();
    }

    public override void OnUpdate(float DeltaTime)
    {
        if (!Model.IsBound || !Mercs.IsRunning)
        {
            return;
        }

        if (CInputLibrary.WasKeyPressed(World, EKey.Tab))
        {
            if (Model.PdaOpen)
            {
                ClosePda();
            }
            else
            {
                Model.ContractEmployer = EFaction.None;
                OpenPda(EPdaTab.Shop);
            }
        }

        if (Model.PdaOpen && CInputLibrary.WasKeyPressed(World, EKey.Escape))
        {
            ClosePda();
        }

        if (Model.PdaOpen)
        {
            TickPdaKeys();
        }

        if (CInputLibrary.WasKeyPressed(World, EKey.F1))
        {
            Model.Help = !Model.Help;
        }

        RefreshPlayer();
        RefreshWorldState();
        RefreshFeed();

        DamageFlash = MathF.Max(0.0f, DamageFlash - DeltaTime * 1.5f);
        Model.DamageFlash = MathF.Round(DamageFlash * 20.0f) / 20.0f;

        RadarTimer -= DeltaTime;
        if (RadarTimer <= 0.0f)
        {
            RadarTimer = 0.1f;
            RefreshRadar();
        }

        SlowTimer -= DeltaTime;
        if (SlowTimer <= 0.0f)
        {
            SlowTimer = 0.5f;
            RefreshFactions();
            if (Model.PdaOpen)
            {
                RefreshPda();
            }
        }
    }

    private void RefreshPlayer()
    {
        MercPlayer? Player = Mercs.Player;
        if (Player is null)
        {
            return;
        }

        Model.MercName = MercPlayer.MercName(Player.Merc);
        Model.Health = MathF.Round(Player.HealthFraction * 100.0f);
        Model.HealthText = $"{MathF.Ceiling(Player.HealthValue)}";
        Model.LowHealth = Player.HealthFraction < 0.3f;

        WeaponState Weapon = Player.Slots[Player.ActiveSlot];
        WeaponState Other = Player.Slots[1 - Player.ActiveSlot];
        Model.WeaponName = Weapon.Def.Name;
        Model.Ammo = $"{Weapon.Magazine} / {Weapon.Reserve}";
        Model.OtherWeapon = $"{Other.Def.Name}  {Other.Magazine}/{Other.Reserve}";
        Model.Reloading = Weapon.IsReloading;
        Model.Grenades = Player.Grenades;
        Model.Charges = Player.Charges;
        Model.ArmedCharges = Mercs.Throwables.ActiveCharges;
        Model.Aiming = Player.IsAiming;
        Model.HitMarker = Player.HitMarkerTime > 0.0f;

        IDamageable? Aimed = MercCamera.Instance?.AimTarget;
        Model.HostileAim = Aimed is not null && Aimed.Faction != EFaction.Civilian && Mercs.Factions.IsHostile(Aimed.Faction, Player);

        Model.Prompt = Player.InteractPrompt;
        Model.HasPrompt = !string.IsNullOrEmpty(Player.InteractPrompt) && Player.Hijack is null;

        Vehicle? Ride = Player.CurrentVehicle;
        Model.InVehicle = Ride is not null;
        if (Ride is not null)
        {
            Model.VehicleName = Ride.Definition.Name;
            Model.VehicleHealth = MathF.Round(Ride.HealthFraction * 100.0f);
            Model.VehicleSpeed = $"{MathF.Round(Ride.CurrentSpeed * 3.6f)} km/h";
            string Primary = Ride.Primary is { } P ? P.Def.Name : string.Empty;
            string Secondary = Ride.Secondary is { } S ? S.Def.Name : string.Empty;
            Model.VehicleWeapon = string.IsNullOrEmpty(Primary) && string.IsNullOrEmpty(Secondary) ? "Unarmed" : $"LMB {Primary}   RMB {Secondary}".Trim();
        }

        HijackSession? Hijack = Player.Hijack;
        Model.InHijack = Hijack is not null;
        if (Hijack is not null)
        {
            Model.HijackKey = Hijack.Intro > 0.0f ? "..." : HijackSession.KeyName(Hijack.Current);
            Model.HijackProgress = $"{Hijack.Step} / {Hijack.Sequence.Count}";
            Model.HijackTime = MathF.Round(Mathf.Clamp01(Hijack.TimeLeft / Hijack.StepTime) * 100.0f);
        }

        Model.Dead = Player.IsDead;
        Model.RespawnText = Player.IsDead ? $"Evac in {MathF.Ceiling(Player.RespawnTimer)}" : string.Empty;

        if (MercCamera.Instance is { } Camera)
        {
            Model.Compass = Geo.CompassName(FVector3.Zero, Geo.Heading(Camera.Yaw));
        }
    }

    private void RefreshWorldState()
    {
        Model.Cash = $"${Mercs.Wallet.Cash:N0}";
        Model.Fuel = $"{Mercs.Wallet.Fuel}";
        ESupportKind Selected = Mercs.Support.Selected;
        if (Selected == ESupportKind.None || Mercs.Support.Count(Selected) <= 0)
        {
            Model.SupportName = "No support";
            Model.SupportCount = string.Empty;
        }
        else
        {
            SupportItem Item = SupportCatalog.Get(Selected);
            Model.SupportName = Item.Name;
            Model.SupportCount = $"x{Mercs.Support.Count(Selected)}  ({Item.Fuel} fuel)";
        }

        string Objective = Mercs.Contracts.ObjectiveText();
        Model.Objective = Objective;
        Model.HasObjective = !string.IsNullOrEmpty(Objective);
        if (Mercs.Contracts.ObjectivePosition() is { } Target)
        {
            float Distance = Geo.FlatDistance(Target, Mercs.PlayerPosition);
            Model.ObjectiveDistance = $"{MathF.Round(Distance)} m {Geo.CompassName(Mercs.PlayerPosition, Target)}";
        }
        else
        {
            Model.ObjectiveDistance = string.Empty;
        }

        NewsFeed Feed = Mercs.Feed;
        Model.Banner = Feed.Banner;
        Model.BannerSub = Feed.BannerSub;
        Model.HasBanner = !string.IsNullOrEmpty(Feed.Banner);

        FactionSystem Factions = Mercs.Factions;
        Model.HasDisguise = Factions.DisguiseFaction != EFaction.None;
        if (Model.HasDisguise)
        {
            Model.Disguise = Factions.bDisguiseBlown ? $"DISGUISE BLOWN ({Factions.Get(Factions.DisguiseFaction).Short})" : $"Disguised as {Factions.Get(Factions.DisguiseFaction).Short}";
            Model.Suspicion = MathF.Round(Factions.Suspicion * 100.0f);
        }

        Model.HasWitness = Factions.Reports.Count > 0;
        if (Model.HasWitness)
        {
            WitnessReport Report = Factions.Reports[0];
            string Where = Geo.CompassName(Mercs.PlayerPosition, Report.Reporter.Position);
            Model.Witness = $"{Factions.Get(Report.Faction).Short} WITNESS reporting in {MathF.Ceiling(Report.Remaining)}s ({MathF.Round(FVector3.Distance(Report.Reporter.Position, Mercs.PlayerPosition))} m {Where})";
        }
    }

    private void RefreshFeed()
    {
        NewsFeed Feed = Mercs.Feed;
        if (Feed.Revision == FeedRevision)
        {
            return;
        }

        FeedRevision = Feed.Revision;
        Model.FeedRows.Clear();
        foreach (NewsFeed.FEntry Entry in Feed.Entries)
        {
            Model.FeedRows.Add(new FeedRow { Text = Entry.Text, Tone = Entry.Tone.ToString().ToLowerInvariant() });
        }

        Model.Push(nameof(HudModel.Feed));
    }

    private void RefreshFactions()
    {
        Model.FactionRows.Clear();
        Model.FactionRows.Add(BuildFactionRow(EFaction.VZ));
        foreach (EFaction Id in FactionSystem.MoodFactions)
        {
            Model.FactionRows.Add(BuildFactionRow(Id));
        }

        for (int Index = 0; Index < Model.FactionRows.Count; ++Index)
        {
            Model.FactionRows[Index].Focused = Model.PdaOpen && Model.PdaTab == "factions" && Index == Cursor;
        }
        Model.Push(nameof(HudModel.Factions));
    }

    private static FactionRow BuildFactionRow(EFaction Id)
    {
        FactionSystem Factions = Mercs.Factions;
        FactionInfo Info = Factions.Get(Id);
        EMood Mood = Factions.MoodOf(Id);
        int Bribe = Factions.BribeCost(Id);
        List<string> Unlocks = new();
        foreach (SupportItem Item in SupportCatalog.Items)
        {
            if (Item.UnlockFaction == Id)
            {
                Unlocks.Add(Mercs.Support.IsUnlocked(Item.Kind) ? Item.Name : $"{Item.Name} ({Item.UnlockContracts} jobs)");
            }
        }

        return new FactionRow
        {
            Id = (int)Id,
            Name = Info.Name,
            Short = Info.Short,
            Mood = Mood.ToString().ToUpperInvariant(),
            MoodClass = Mood.ToString().ToLowerInvariant(),
            Bar = MathF.Round((Info.Standing + 100.0f) * 0.5f),
            Color = Info.ColorHex,
            Standing = Info.bLockedHostile ? "always hostile" : $"{Info.Standing:0}",
            CanBribe = Bribe > 0 && Info.bHasMood,
            BribeText = Bribe > 0 ? $"Bribe ${Bribe:N0}" : string.Empty,
            Unlocks = Unlocks.Count > 0 ? string.Join(", ", Unlocks) : string.Empty,
            Provoked = Factions.IsProvoked(Id),
        };
    }

    private void RefreshRadar()
    {
        MercPlayer? Player = Mercs.Player;
        MercCamera? Camera = MercCamera.Instance;
        if (Player is null || Camera is null)
        {
            return;
        }

        FVector3 Center = Player.Position;
        FVector3 Right = Geo.RightOf(Camera.Yaw);
        FVector3 Forward = Geo.Heading(Camera.Yaw);
        const float Half = 80.0f;
        float Scale = Half / RadarRange;
        Model.BlipRows.Clear();

        void Add(FVector3 At, string Kind, bool bClamp = false)
        {
            FVector3 Offset = At - Center;
            if (!float.IsFinite(Offset.X) || !float.IsFinite(Offset.Z))
            {
                return;
            }

            float X = FVector3.Dot(Offset, Right) * Scale;
            float Y = -FVector3.Dot(Offset, Forward) * Scale;
            float Length = MathF.Sqrt(X * X + Y * Y);
            if (Length > Half - 5.0f)
            {
                if (!bClamp)
                {
                    return;
                }

                X *= (Half - 5.0f) / Length;
                Y *= (Half - 5.0f) / Length;
            }

            if (Model.BlipRows.Count < 60)
            {
                Model.BlipRows.Add(new BlipRow { X = MathF.Round(Half + X - 4.0f), Y = MathF.Round(Half + Y - 4.0f), Kind = Kind });
            }
        }

        if (Mercs.Contracts.ObjectivePosition() is { } Objective)
        {
            Add(Objective, "objective", true);
        }

        foreach (Site Place in Mercs.Sites)
        {
            if (Place.Contact is { } Contact)
            {
                Add(Contact, "contact");
            }
        }

        foreach (Soldier Member in Mercs.Soldiers)
        {
            if (!Member.IsAlive && !Member.IsSubdued)
            {
                continue;
            }

            string Kind = Member.bIsHvt ? "hvt"
                : Member.CurrentState == ESoldierState.Report ? "witness"
                : Member.Faction == EFaction.Civilian ? "civilian"
                : Mercs.Factions.IsHostile(Member.Faction, Player) ? "hostile"
                : FactionSystem.AtWar(Member.Faction, EFaction.VZ) ? "ally" : "neutral";
            Add(Member.Position, Kind);
        }

        foreach (Vehicle Ride in Mercs.Vehicles)
        {
            if (!Ride.IsAlive || Ride.IsPlayerControlled)
            {
                continue;
            }

            string Kind = Ride.IsEmpty ? "vehicle-empty" : (Mercs.Factions.IsHostile(Ride.Faction, Player) ? "vehicle-hostile" : "vehicle");
            Add(Ride.Position, Kind);
        }

        Model.Push(nameof(HudModel.Blips));
    }

    private void TickPdaKeys()
    {
        string[] Tabs = { "shop", "factions", "contracts", "bounties", "map" };
        EKey[] TabKeys = { EKey.D1, EKey.D2, EKey.D3, EKey.D4, EKey.D5 };
        for (int Index = 0; Index < Tabs.Length; ++Index)
        {
            if (CInputLibrary.WasKeyPressed(World, TabKeys[Index]))
            {
                Model.PdaTab = Tabs[Index];
                Sfx.Ui(ESfx.UiClick);
                Cursor = 0;
                RefreshPda();
            }
        }

        int Rows = Model.PdaTab switch
        {
            "shop" => Model.ShopRows.Count,
            "factions" => Model.FactionRows.Count,
            "contracts" => Model.ContractRows.Count,
            _ => 0,
        };

        if (Rows == 0)
        {
            return;
        }

        bool bMoved = false;
        if (CInputLibrary.WasKeyPressed(World, EKey.Down) || CInputLibrary.WasKeyPressed(World, EKey.S))
        {
            Cursor = (Cursor + 1) % Rows;
            bMoved = true;
        }

        if (CInputLibrary.WasKeyPressed(World, EKey.Up) || CInputLibrary.WasKeyPressed(World, EKey.W))
        {
            Cursor = (Cursor + Rows - 1) % Rows;
            bMoved = true;
        }

        Cursor = Math.Clamp(Cursor, 0, Rows - 1);
        if (bMoved)
        {
            Sfx.Ui(ESfx.UiClick, 0.4f, 1.2f);
        }

        bool bConfirm = CInputLibrary.WasKeyPressed(World, EKey.Enter);
        bool bEquip = CInputLibrary.WasKeyPressed(World, EKey.Space);
        if (bConfirm || bEquip)
        {
            switch (Model.PdaTab)
            {
                case "shop":
                    if (bEquip)
                    {
                        Model.Equip(Model.ShopRows[Cursor].Id);
                    }
                    else
                    {
                        Model.Buy(Model.ShopRows[Cursor].Id);
                    }

                    break;
                case "factions":
                    Model.Bribe(Model.FactionRows[Cursor].Id);
                    break;
                case "contracts":
                    Model.Accept(Model.ContractRows[Cursor].Id);
                    return;
            }
        }
        else if (bMoved)
        {
            RefreshPda();
        }
    }

    public void RefreshPda()
    {
        Model.ShopRows.Clear();
        foreach (SupportItem Item in SupportCatalog.Items)
        {
            bool bUnlocked = Mercs.Support.IsUnlocked(Item.Kind);
            Model.ShopRows.Add(new ShopRow
            {
                Id = (int)Item.Kind,
                Name = Item.Name,
                Blurb = Item.Blurb,
                Price = $"${Item.Cash:N0}",
                FuelCost = Item.bInstant ? "delivered now" : $"{Item.Fuel} fuel per call",
                Owned = Mercs.Support.Count(Item.Kind),
                Locked = !bUnlocked,
                LockText = bUnlocked ? string.Empty : $"Complete {Item.UnlockContracts} {Mercs.Factions.Get(Item.UnlockFaction).Short} contract(s)",
                Selected = Mercs.Support.Selected == Item.Kind,
                Focused = Model.PdaTab == "shop" && Model.ShopRows.Count == Cursor,
            });
        }

        Model.Push(nameof(HudModel.Shop));

        RefreshFactions();

        Model.ContractRows.Clear();
        EFaction Filter = Model.ContractEmployer;
        foreach (EFaction Employer in FactionSystem.MoodFactions)
        {
            if (Filter != EFaction.None && Employer != Filter)
            {
                continue;
            }

            if (Mercs.Factions.MoodOf(Employer) == EMood.Hostile)
            {
                continue;
            }

            foreach (Contract Job in Mercs.Contracts.Offered(Employer))
            {
                FactionInfo Info = Mercs.Factions.Get(Employer);
                Model.ContractRows.Add(new ContractRow { Focused = Model.PdaTab == "contracts" && Model.ContractRows.Count == Cursor, Id = Job.Id, Title = Job.Title, Description = Job.Description, Reward = $"${Job.Reward:N0}", Employer = Info.Name, Color = Info.ColorHex });
            }
        }

        Model.ContractFaction = Filter == EFaction.None ? "All factions (visit a faction contact to see only theirs)" : Mercs.Factions.Get(Filter).Name;
        Model.ContractFactionColor = Filter == EFaction.None ? "#dddddd" : Mercs.Factions.Get(Filter).ColorHex;
        Model.ActiveContract = Mercs.Contracts.Active is { } Active ? $"{Active.Title}: {Active.Description}" : string.Empty;
        Model.Push(nameof(HudModel.Contracts));

        Model.HvtRows.Clear();
        foreach (HvtTarget Target in Mercs.Contracts.Hvts)
        {
            string Status = Target.bCaptured ? "CAPTURED" : Target.bResolved ? "VERIFIED" : Target.Live is { IsSubdued: true } ? "SUBDUED" : Target.Live is { IsDead: true } ? "DEAD, verify body" : "AT LARGE";
            Model.HvtRows.Add(new HvtRow
            {
                Name = Target.Name,
                Title = Target.Title,
                Bounty = $"${Target.Bounty:N0} alive / ${Target.Bounty / 2:N0} dead",
                Status = Status,
                Where = $"{Target.Home.Name}, {MathF.Round(Geo.FlatDistance(Target.Home.Center, Mercs.PlayerPosition))} m {Geo.CompassName(Mercs.PlayerPosition, Target.Home.Center)}",
                Resolved = Target.bResolved,
            });
        }

        Model.Push(nameof(HudModel.Bounties));

        Model.SiteRows.Clear();
        List<Site> Sorted = new(Mercs.Sites);
        Sorted.Sort((A, B) => Geo.FlatDistance(A.Center, Mercs.PlayerPosition).CompareTo(Geo.FlatDistance(B.Center, Mercs.PlayerPosition)));
        foreach (Site Place in Sorted)
        {
            FactionInfo Info = Mercs.Factions.Get(Place.Owner);
            Model.SiteRows.Add(new SiteRow
            {
                Name = Place.Name,
                Owner = Info.Name,
                Color = Info.ColorHex,
                Distance = $"{MathF.Round(Geo.FlatDistance(Place.Center, Mercs.PlayerPosition))} m {Geo.CompassName(Mercs.PlayerPosition, Place.Center)}",
                Status = Place.bCleared ? "liberated" : (Place.Hvt is { bResolved: false } ? "HVT present" : string.Empty),
            });
        }

        Model.Push(nameof(HudModel.Places));
    }
}
