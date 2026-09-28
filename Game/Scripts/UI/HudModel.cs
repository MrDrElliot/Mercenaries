using System.Collections.Generic;
using LuminaSharp;

namespace Mercenaries;

public sealed class FeedRow
{
    [Bind] public string Text { get; set; } = string.Empty;
    [Bind] public string Tone { get; set; } = "neutral";
}

public sealed class FactionRow
{
    [Bind] public bool Focused { get; set; }
    [Bind] public int Id { get; set; }
    [Bind] public string Name { get; set; } = string.Empty;
    [Bind] public string Short { get; set; } = string.Empty;
    [Bind] public string Mood { get; set; } = string.Empty;
    [Bind] public string MoodClass { get; set; } = "neutral";
    [Bind] public float Bar { get; set; }
    [Bind] public string Color { get; set; } = "#ffffff";
    [Bind] public string Standing { get; set; } = string.Empty;
    [Bind] public bool CanBribe { get; set; }
    [Bind] public string BribeText { get; set; } = string.Empty;
    [Bind] public string Unlocks { get; set; } = string.Empty;
    [Bind] public bool Provoked { get; set; }
}

public sealed class BlipRow
{
    [Bind] public float X { get; set; }
    [Bind] public float Y { get; set; }
    [Bind] public string Kind { get; set; } = "neutral";
}

public sealed class ShopRow
{
    [Bind] public bool Focused { get; set; }
    [Bind] public int Id { get; set; }
    [Bind] public string Name { get; set; } = string.Empty;
    [Bind] public string Blurb { get; set; } = string.Empty;
    [Bind] public string Price { get; set; } = string.Empty;
    [Bind] public string FuelCost { get; set; } = string.Empty;
    [Bind] public int Owned { get; set; }
    [Bind] public bool Locked { get; set; }
    [Bind] public string LockText { get; set; } = string.Empty;
    [Bind] public bool Selected { get; set; }
}

public sealed class ContractRow
{
    [Bind] public bool Focused { get; set; }
    [Bind] public int Id { get; set; }
    [Bind] public string Title { get; set; } = string.Empty;
    [Bind] public string Description { get; set; } = string.Empty;
    [Bind] public string Reward { get; set; } = string.Empty;
    [Bind] public string Employer { get; set; } = string.Empty;
    [Bind] public string Color { get; set; } = "#ffffff";
}

public sealed class HvtRow
{
    [Bind] public string Name { get; set; } = string.Empty;
    [Bind] public string Title { get; set; } = string.Empty;
    [Bind] public string Bounty { get; set; } = string.Empty;
    [Bind] public string Status { get; set; } = string.Empty;
    [Bind] public string Where { get; set; } = string.Empty;
    [Bind] public bool Resolved { get; set; }
}

public sealed class SiteRow
{
    [Bind] public string Name { get; set; } = string.Empty;
    [Bind] public string Owner { get; set; } = string.Empty;
    [Bind] public string Color { get; set; } = "#ffffff";
    [Bind] public string Distance { get; set; } = string.Empty;
    [Bind] public string Status { get; set; } = string.Empty;
}

public sealed class HudModel : ViewModel
{
    private float HealthValue = 100.0f;
    private string HealthTextValue = string.Empty;
    private bool LowHealthValue;
    private string MercNameValue = string.Empty;
    private string WeaponNameValue = string.Empty;
    private string AmmoValue = string.Empty;
    private string OtherWeaponValue = string.Empty;
    private int GrenadesValue;
    private int ChargesValue;
    private int ArmedChargesValue;
    private bool ReloadingValue;
    private string CashValue = string.Empty;
    private string FuelValue = string.Empty;
    private string SupportNameValue = string.Empty;
    private string SupportCountValue = string.Empty;
    private string ObjectiveValue = string.Empty;
    private string ObjectiveDistanceValue = string.Empty;
    private bool HasObjectiveValue;
    private string PromptValue = string.Empty;
    private bool HasPromptValue;
    private string BannerValue = string.Empty;
    private string BannerSubValue = string.Empty;
    private bool HasBannerValue;
    private bool HostileAimValue;
    private bool HitMarkerValue;
    private bool AimingValue;
    private string DisguiseValue = string.Empty;
    private bool HasDisguiseValue;
    private float SuspicionValue;
    private string WitnessValue = string.Empty;
    private bool HasWitnessValue;
    private bool InVehicleValue;
    private string VehicleNameValue = string.Empty;
    private float VehicleHealthValue;
    private string VehicleWeaponValue = string.Empty;
    private string VehicleSpeedValue = string.Empty;
    private bool InHijackValue;
    private string HijackKeyValue = string.Empty;
    private string HijackProgressValue = string.Empty;
    private float HijackTimeValue;
    private bool DeadValue;
    private string RespawnTextValue = string.Empty;
    private bool HelpValue;
    private bool PdaOpenValue;
    private string PdaTabValue = "shop";
    private string ActiveContractValue = string.Empty;
    private string ContractFactionValue = string.Empty;
    private string ContractFactionColorValue = "#ffffff";
    private float DamageFlashValue;
    private string CompassValue = string.Empty;

    [Bind] public float Health { get => HealthValue; set => Set(ref HealthValue, value); }
    [Bind] public string HealthText { get => HealthTextValue; set => Set(ref HealthTextValue, value); }
    [Bind] public bool LowHealth { get => LowHealthValue; set => Set(ref LowHealthValue, value); }
    [Bind] public string MercName { get => MercNameValue; set => Set(ref MercNameValue, value); }
    [Bind] public string WeaponName { get => WeaponNameValue; set => Set(ref WeaponNameValue, value); }
    [Bind] public string Ammo { get => AmmoValue; set => Set(ref AmmoValue, value); }
    [Bind] public string OtherWeapon { get => OtherWeaponValue; set => Set(ref OtherWeaponValue, value); }
    [Bind] public int Grenades { get => GrenadesValue; set => Set(ref GrenadesValue, value); }
    [Bind] public int Charges { get => ChargesValue; set => Set(ref ChargesValue, value); }
    [Bind] public int ArmedCharges { get => ArmedChargesValue; set => Set(ref ArmedChargesValue, value); }
    [Bind] public bool Reloading { get => ReloadingValue; set => Set(ref ReloadingValue, value); }
    [Bind] public string Cash { get => CashValue; set => Set(ref CashValue, value); }
    [Bind] public string Fuel { get => FuelValue; set => Set(ref FuelValue, value); }
    [Bind] public string SupportName { get => SupportNameValue; set => Set(ref SupportNameValue, value); }
    [Bind] public string SupportCount { get => SupportCountValue; set => Set(ref SupportCountValue, value); }
    [Bind] public string Objective { get => ObjectiveValue; set => Set(ref ObjectiveValue, value); }
    [Bind] public string ObjectiveDistance { get => ObjectiveDistanceValue; set => Set(ref ObjectiveDistanceValue, value); }
    [Bind] public bool HasObjective { get => HasObjectiveValue; set => Set(ref HasObjectiveValue, value); }
    [Bind] public string Prompt { get => PromptValue; set => Set(ref PromptValue, value); }
    [Bind] public bool HasPrompt { get => HasPromptValue; set => Set(ref HasPromptValue, value); }
    [Bind] public string Banner { get => BannerValue; set => Set(ref BannerValue, value); }
    [Bind] public string BannerSub { get => BannerSubValue; set => Set(ref BannerSubValue, value); }
    [Bind] public bool HasBanner { get => HasBannerValue; set => Set(ref HasBannerValue, value); }
    [Bind] public bool HostileAim { get => HostileAimValue; set => Set(ref HostileAimValue, value); }
    [Bind] public bool HitMarker { get => HitMarkerValue; set => Set(ref HitMarkerValue, value); }
    [Bind] public bool Aiming { get => AimingValue; set => Set(ref AimingValue, value); }
    [Bind] public string Disguise { get => DisguiseValue; set => Set(ref DisguiseValue, value); }
    [Bind] public bool HasDisguise { get => HasDisguiseValue; set => Set(ref HasDisguiseValue, value); }
    [Bind] public float Suspicion { get => SuspicionValue; set => Set(ref SuspicionValue, value); }
    [Bind] public string Witness { get => WitnessValue; set => Set(ref WitnessValue, value); }
    [Bind] public bool HasWitness { get => HasWitnessValue; set => Set(ref HasWitnessValue, value); }
    [Bind] public bool InVehicle { get => InVehicleValue; set => Set(ref InVehicleValue, value); }
    [Bind] public string VehicleName { get => VehicleNameValue; set => Set(ref VehicleNameValue, value); }
    [Bind] public float VehicleHealth { get => VehicleHealthValue; set => Set(ref VehicleHealthValue, value); }
    [Bind] public string VehicleWeapon { get => VehicleWeaponValue; set => Set(ref VehicleWeaponValue, value); }
    [Bind] public string VehicleSpeed { get => VehicleSpeedValue; set => Set(ref VehicleSpeedValue, value); }
    [Bind] public bool InHijack { get => InHijackValue; set => Set(ref InHijackValue, value); }
    [Bind] public string HijackKey { get => HijackKeyValue; set => Set(ref HijackKeyValue, value); }
    [Bind] public string HijackProgress { get => HijackProgressValue; set => Set(ref HijackProgressValue, value); }
    [Bind] public float HijackTime { get => HijackTimeValue; set => Set(ref HijackTimeValue, value); }
    [Bind] public bool Dead { get => DeadValue; set => Set(ref DeadValue, value); }
    [Bind] public string RespawnText { get => RespawnTextValue; set => Set(ref RespawnTextValue, value); }
    [Bind] public bool Help { get => HelpValue; set => Set(ref HelpValue, value); }
    [Bind] public bool PdaOpen { get => PdaOpenValue; set => Set(ref PdaOpenValue, value); }
    [Bind] public string PdaTab { get => PdaTabValue; set => Set(ref PdaTabValue, value); }
    [Bind] public string ActiveContract { get => ActiveContractValue; set => Set(ref ActiveContractValue, value); }
    [Bind] public string ContractFaction { get => ContractFactionValue; set => Set(ref ContractFactionValue, value); }
    [Bind] public string ContractFactionColor { get => ContractFactionColorValue; set => Set(ref ContractFactionColorValue, value); }
    [Bind] public float DamageFlash { get => DamageFlashValue; set => Set(ref DamageFlashValue, value); }
    [Bind] public string Compass { get => CompassValue; set => Set(ref CompassValue, value); }

    public readonly List<FeedRow> FeedRows = new();
    public readonly List<FactionRow> FactionRows = new();
    public readonly List<BlipRow> BlipRows = new();
    public readonly List<ShopRow> ShopRows = new();
    public readonly List<ContractRow> ContractRows = new();
    public readonly List<HvtRow> HvtRows = new();
    public readonly List<SiteRow> SiteRows = new();

    [Bind] public IReadOnlyList<FeedRow> Feed => FeedRows;
    [Bind] public IReadOnlyList<FactionRow> Factions => FactionRows;
    [Bind] public IReadOnlyList<BlipRow> Blips => BlipRows;
    [Bind] public IReadOnlyList<ShopRow> Shop => ShopRows;
    [Bind] public IReadOnlyList<ContractRow> Contracts => ContractRows;
    [Bind] public IReadOnlyList<HvtRow> Bounties => HvtRows;
    [Bind] public IReadOnlyList<SiteRow> Places => SiteRows;

    public EFaction ContractEmployer = EFaction.None;

    public void Push(string Name) => NotifyChanged(Name);

    [BindCommand]
    public void Tab(string Name)
    {
        PdaTab = Name;
        MercsHud.Instance?.RefreshPda();
    }

    [BindCommand]
    public void Buy(int Kind)
    {
        Mercs.Support.Buy((ESupportKind)Kind);
        MercsHud.Instance?.RefreshPda();
    }

    [BindCommand]
    public void Equip(int Kind)
    {
        Mercs.Support.Select((ESupportKind)Kind);
        MercsHud.Instance?.RefreshPda();
    }

    [BindCommand]
    public void Bribe(int Faction)
    {
        Mercs.Factions.Bribe((EFaction)Faction);
        MercsHud.Instance?.RefreshPda();
    }

    [BindCommand]
    public void Accept(int Id)
    {
        if (Mercs.Contracts.Accept(Id))
        {
            MercsHud.Instance?.ClosePda();
            return;
        }

        MercsHud.Instance?.RefreshPda();
    }

    [BindCommand]
    public void Abandon()
    {
        Mercs.Contracts.Abandon();
        MercsHud.Instance?.RefreshPda();
    }

    [BindCommand]
    public void Close()
    {
        MercsHud.Instance?.ClosePda();
    }
}
