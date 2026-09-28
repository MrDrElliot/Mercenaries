using System;
using System.Collections.Generic;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

public enum EObjectiveKind : byte
{
    DestroyTargets,
    ClearSite,
    DeliverVehicle,
    DestroyVehicles,
    CaptureHvt,
}

public sealed class Contract
{
    public int Id;
    public EFaction Employer;
    public string Title = string.Empty;
    public string Description = string.Empty;
    public int Reward;
    public float Standing = 20.0f;
    public EObjectiveKind Kind;
    public readonly List<Structure> Targets = new();
    public Site? TargetSite;
    public EVehicleType DeliverType;
    public EFaction DeliverFaction = EFaction.None;
    public Site? Destination;
    public EFaction VehicleFaction = EFaction.VZ;
    public int Needed;
    public int Progress;
    public HvtTarget? Hvt;
    public bool bCompleted;
    public int Order;

    public string ProgressText => Kind switch
    {
        EObjectiveKind.DestroyTargets => $"{DestroyedCount}/{Targets.Count} destroyed",
        EObjectiveKind.DestroyVehicles => $"{Progress}/{Needed} destroyed",
        EObjectiveKind.ClearSite => TargetSite is { } Place ? $"{Place.AliveMembers} defenders left" : string.Empty,
        EObjectiveKind.CaptureHvt => Hvt is { } Target ? (Target.bResolved ? "resolved" : (Target.Live?.IsSubdued == true ? "subdued, call extraction" : "at large")) : string.Empty,
        _ => string.Empty,
    };

    public int DestroyedCount
    {
        get
        {
            int Count = 0;
            foreach (Structure Target in Targets)
            {
                if (Target.IsDestroyed)
                {
                    ++Count;
                }
            }

            return Count;
        }
    }
}

public sealed class HvtTarget
{
    public string Name = string.Empty;
    public string Title = string.Empty;
    public EFaction Faction = EFaction.VZ;
    public int Bounty;
    public Site Home = null!;
    public bool bResolved;
    public bool bCaptured;
    public bool bExtractionCalled;
    public Soldier? Live;
    public int Guards = 2;

    public void Spawn(Site Place)
    {
        FVector3 At = Place.Center + new FVector3(Mercs.Range(-6.0f, 6.0f), 0.0f, Mercs.Range(-6.0f, 6.0f));
        Soldier? Target = Spawner.SpawnSoldier(Faction, ESoldierRole.Officer, At, Place);
        if (Target is null)
        {
            return;
        }

        Target.bIsHvt = true;
        Target.Hvt = this;
        Target.DisplayName = Name;
        Target.MaxHealth = 220.0f;
        Target.RunSpeed = 5.6f;
        Live = Target;

        for (int Index = 0; Index < Guards; ++Index)
        {
            Spawner.SpawnSoldier(Faction, Index == 0 ? ESoldierRole.Gunner : ESoldierRole.Rifleman, At + new FVector3(Mercs.Range(-3.0f, 3.0f), 0.0f, Mercs.Range(-3.0f, 3.0f)), Place);
        }
    }
}

public sealed class ContractBoard
{
    public readonly List<Contract> All = new();
    public readonly List<HvtTarget> Hvts = new();
    public Contract? Active;
    public HvtTarget? PendingExtraction;
    private int NextId = 1;
    private Entity Beam = Entity.Null;
    private FVector3 BeamAt;

    public Contract Add(EFaction Employer, string Title, string Description, int Reward, EObjectiveKind Kind)
    {
        Contract Job = new()
        {
            Id = NextId++,
            Employer = Employer,
            Title = Title,
            Description = Description,
            Reward = Reward,
            Kind = Kind,
            Order = All.FindAll(Existing => Existing.Employer == Employer).Count,
        };
        All.Add(Job);
        return Job;
    }

    public List<Contract> Offered(EFaction Employer)
    {
        List<Contract> Result = new();
        foreach (Contract Job in All)
        {
            if (Job.Employer == Employer && !Job.bCompleted && Job != Active && Result.Count < 2)
            {
                Result.Add(Job);
            }
        }

        return Result;
    }

    public bool Accept(int Id)
    {
        Contract? Job = All.Find(Candidate => Candidate.Id == Id);
        if (Job is null || Job.bCompleted)
        {
            return false;
        }

        if (Mercs.Factions.MoodOf(Job.Employer) == EMood.Hostile)
        {
            Mercs.Feed.Post($"{Mercs.Factions.Get(Job.Employer).Name} won't hire you right now.", ENewsTone.Bad);
            return false;
        }

        if (Active is not null)
        {
            Mercs.Feed.Post($"Abandoned: {Active.Title}", ENewsTone.Bad);
        }

        Active = Job;
        if (Job.Kind == EObjectiveKind.DestroyVehicles)
        {
            Job.Progress = 0;
        }

        Sfx.Ui(ESfx.RadioCall, 0.6f);
        Mercs.Feed.Announce($"CONTRACT: {Job.Title}", Job.Description, 5.0f);
        return true;
    }

    public void Abandon()
    {
        if (Active is null)
        {
            return;
        }

        Mercs.Feed.Post($"Abandoned: {Active.Title}", ENewsTone.Bad);
        Active = null;
    }

    public FVector3? ObjectivePosition()
    {
        Contract? Job = Active;
        if (Job is null)
        {
            return null;
        }

        switch (Job.Kind)
        {
            case EObjectiveKind.DestroyTargets:
                foreach (Structure Target in Job.Targets)
                {
                    if (!Target.IsDestroyed)
                    {
                        return Target.Position;
                    }
                }

                return null;
            case EObjectiveKind.ClearSite:
                return Job.TargetSite?.Center;
            case EObjectiveKind.DeliverVehicle:
                if (Mercs.Player?.CurrentVehicle is { } Ride && Ride.Type == Job.DeliverType)
                {
                    return Job.Destination?.Center;
                }

                Vehicle? Nearest = null;
                float Best = float.MaxValue;
                foreach (Vehicle Candidate in Mercs.Vehicles)
                {
                    if (Candidate.IsAlive && Candidate.Type == Job.DeliverType)
                    {
                        float Distance = FVector3.Distance(Candidate.Position, Mercs.PlayerPosition);
                        if (Distance < Best)
                        {
                            Best = Distance;
                            Nearest = Candidate;
                        }
                    }
                }

                return Nearest?.Position ?? Job.TargetSite?.Center;
            case EObjectiveKind.DestroyVehicles:
                return Job.TargetSite?.Center;
            case EObjectiveKind.CaptureHvt:
                return Job.Hvt?.Live?.Position ?? Job.Hvt?.Home.Center;
        }

        return null;
    }

    public string ObjectiveText()
    {
        Contract? Job = Active;
        if (Job is null)
        {
            return string.Empty;
        }

        string Where = Job.Kind == EObjectiveKind.DeliverVehicle && Job.Destination is { } Drop ? $" Deliver to {Drop.Name}." : string.Empty;
        return $"{Job.Title}  ({Job.ProgressText}){Where}";
    }

    public void Update(float DeltaTime)
    {
        UpdateBeam();

        Contract? Job = Active;
        if (Job is null)
        {
            return;
        }

        switch (Job.Kind)
        {
            case EObjectiveKind.DestroyTargets:
                if (Job.Targets.Count > 0 && Job.DestroyedCount >= Job.Targets.Count)
                {
                    Complete(Job);
                }

                break;
            case EObjectiveKind.ClearSite:
                if (Job.TargetSite is { } Place && (Place.bCleared || (Place.bActive && Place.AliveMembers == 0 && Place.Members.Count > 0)))
                {
                    Complete(Job);
                }

                break;
            case EObjectiveKind.DeliverVehicle:
                if (Mercs.Player?.CurrentVehicle is { } Ride && Ride.Type == Job.DeliverType && Job.Destination is { } Destination && Geo.FlatDistance(Ride.Position, Destination.Center) < Destination.Radius * 0.8f)
                {
                    Mercs.Player.ExitVehicle();
                    Ride.TakeOver(Job.Employer);
                    Complete(Job);
                }

                break;
            case EObjectiveKind.DestroyVehicles:
                if (Job.Progress >= Job.Needed)
                {
                    Complete(Job);
                }

                break;
            case EObjectiveKind.CaptureHvt:
                if (Job.Hvt is { bResolved: true })
                {
                    Complete(Job);
                }

                break;
        }
    }

    private void UpdateBeam()
    {
        FVector3? Target = ObjectivePosition();
        CWorld World = Mercs.World;
        if (Target is null)
        {
            if (!Beam.IsNull && World.IsValidEntity(Beam))
            {
                World.SetEntityLocation(Beam, new FVector3(0.0f, -900.0f, 0.0f));
            }

            return;
        }

        if (Beam.IsNull || !World.IsValidEntity(Beam))
        {
            MeshKit Kit = new();
            Kit.Tube(FVector3.Zero, new FVector3(0.0f, 140.0f, 0.0f), 0.35f, 0.1f, Palette.Rgb(1.0f, 0.55f, 0.1f), 6, false);
            Beam = World.CreateEntity("ObjectiveBeam", Target.Value);
            Kit.Commit(World.Registry, Beam, true, false);
        }

        FVector3 Ground = Geo.Ground(Target.Value);
        if (FVector3.DistanceSquared(Ground, BeamAt) > 0.25f)
        {
            BeamAt = Ground;
            World.SetEntityLocation(Beam, Ground);
        }
    }

    private void Complete(Contract Job)
    {
        Job.bCompleted = true;
        if (Active == Job)
        {
            Active = null;
        }

        FactionInfo Employer = Mercs.Factions.Get(Job.Employer);
        Mercs.Wallet.AddCash(Job.Reward, $"{Employer.Short} contract paid");
        Mercs.Factions.RewardContract(Job.Employer, Job.Standing);
        Sfx.Ui(ESfx.Objective, 0.6f);
        Mercs.Feed.Announce("CONTRACT COMPLETE", $"{Job.Title}  +${Job.Reward:N0}", 5.0f);

        foreach (SupportItem Item in SupportCatalog.Items)
        {
            if (Item.UnlockFaction == Job.Employer && Item.UnlockContracts == Employer.ContractsDone)
            {
                Mercs.Feed.Post($"UNLOCKED in shop: {Item.Name}", ENewsTone.Good);
            }
        }

        if (Job.Employer == EFaction.Oil)
        {
            Mercs.Wallet.AddFuel(150, "UP bonus fuel");
        }
    }

    public void OnStructureDestroyed(Structure Target, FHit Hit)
    {
    }

    public void OnSiteCleared(Site Place)
    {
    }

    public void OnVehicleDestroyed(Vehicle Ride, FHit Hit)
    {
        if (Active is { Kind: EObjectiveKind.DestroyVehicles } Job && Hit.bByPlayer && Ride.Team == Job.VehicleFaction)
        {
            Job.Progress++;
            Mercs.Feed.Post($"{Job.Title}: {Job.Progress}/{Job.Needed}", ENewsTone.Good);
        }
    }

    public void OnPlayerEnteredVehicle(Vehicle Ride)
    {
        if (Active is { Kind: EObjectiveKind.DeliverVehicle } Job && Ride.Type == Job.DeliverType && Job.Destination is { } Destination)
        {
            Mercs.Feed.Post($"Now drive it to {Destination.Name}.", ENewsTone.Good);
        }
    }

    public void OnSoldierKilled(Soldier Victim, FHit Hit)
    {
        if (Victim.Hvt is not { } Target || Target.bResolved)
        {
            return;
        }

        Mercs.Feed.Announce($"{Target.Name} KILLED", "Verify the body with a photo [E] for half the bounty.", 4.0f);
        if (PendingExtraction == Target)
        {
            PendingExtraction = null;
        }
    }

    public void OnHvtSubdued(Soldier Victim)
    {
    }

    public void CallExtraction(Soldier Victim)
    {
        if (Victim.Hvt is not { } Target || Target.bExtractionCalled)
        {
            return;
        }

        Target.bExtractionCalled = true;
        PendingExtraction = Target;
        FVector3 From = Victim.Position + new FVector3(0.0f, 1.0f, 0.0f);
        Throwables.Throw(EThrowable.Flare, From + new FVector3(1.0f, 0.5f, 0.0f), new FVector3(2.0f, 3.0f, 0.0f), Mercs.Player);
    }

    public void CompleteCapture(HvtTarget Target)
    {
        if (Target.bResolved)
        {
            return;
        }

        if (Target.Live is not { IsSubdued: true } Captive)
        {
            Mercs.Feed.Post("Extraction failed, the target is gone.", ENewsTone.Bad);
            Target.bExtractionCalled = false;
            PendingExtraction = null;
            return;
        }

        Target.bResolved = true;
        Target.bCaptured = true;
        PendingExtraction = null;
        Captive.MarkExtracted();
        Target.Live = null;
        Mercs.Wallet.AddCash(Target.Bounty, $"{Target.Name} captured alive");
        Mercs.Factions.ChangeStanding(EFaction.Allied, 8.0f, "HVT captured");
        Sfx.Ui(ESfx.Objective, 0.6f);
        Mercs.Feed.Announce($"{Target.Name} CAPTURED", $"Full bounty paid: ${Target.Bounty:N0}", 5.0f);
    }

    public void VerifyHvt(Soldier Victim)
    {
        if (Victim.Hvt is not { } Target || Target.bResolved)
        {
            return;
        }

        Target.bResolved = true;
        int Half = Target.Bounty / 2;
        Mercs.Wallet.AddCash(Half, $"{Target.Name} verified");
        Sfx.Ui(ESfx.Objective, 0.6f);
        Mercs.Feed.Announce($"{Target.Name} VERIFIED", $"Half bounty paid: ${Half:N0}. Capture alive for full pay.", 5.0f);
    }
}
