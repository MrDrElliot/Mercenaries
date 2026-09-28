using System;
using System.Collections.Generic;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

public sealed class FactionInfo
{
    public EFaction Id;
    public string Name = string.Empty;
    public string Short = string.Empty;
    public FVector4 Color;
    public FVector4 Accent;
    public float Standing;
    public bool bLockedHostile;
    public bool bHasMood;
    public FVector3 Headquarters;
    public float HeadquartersRadius = 45.0f;
    public EFaction[] Rivals = Array.Empty<EFaction>();
    public int ContractsDone;
    public float ProvokedTime;
    public EMood LastMood = EMood.Neutral;

    public string ColorHex => $"#{(int)(Color.X * 255):X2}{(int)(Color.Y * 255):X2}{(int)(Color.Z * 255):X2}";
}

public sealed class WitnessReport
{
    public EFaction Faction;
    public Soldier Reporter = null!;
    public float Amount;
    public float Remaining;
    public string Reason = string.Empty;
}

public sealed class FactionSystem
{
    public const float HostileBelow = -30.0f;
    public const float FriendlyAtOrAbove = 40.0f;
    public const float ReportDelay = 5.5f;
    public const float WitnessRange = 70.0f;
    public const float ProvokeDuration = 25.0f;

    private readonly Dictionary<EFaction, FactionInfo> Infos = new();
    public readonly List<WitnessReport> Reports = new();

    public EFaction DisguiseFaction = EFaction.None;
    public float Suspicion;
    public bool bDisguiseBlown;
    private float UnwitnessedCooldown;

    public static readonly EFaction[] MoodFactions = { EFaction.Allied, EFaction.China, EFaction.Oil, EFaction.Guerrilla, EFaction.Pirate };

    public FactionSystem()
    {
        Add(EFaction.Merc, "Mercenary", "MERC", Palette.Hex(0x2F2F2F), Palette.Hex(0xE07A1F), 100.0f, false);
        Add(EFaction.VZ, "Venezuelan Army", "VZ", Palette.Hex(0x5B6B3A), Palette.Hex(0x2E3A1F), -100.0f, true);
        Add(EFaction.Allied, "Allied Nations", "AN", Palette.Hex(0x3A6FB5), Palette.Hex(0xDADADA), 15.0f, false);
        Add(EFaction.China, "People's Liberation Army", "PLA", Palette.Hex(0xB53A30), Palette.Hex(0xE8C23A), 15.0f, false);
        Add(EFaction.Oil, "Universal Petroleum", "UP", Palette.Hex(0xE0B22A), Palette.Hex(0x1E1E1E), 5.0f, false);
        Add(EFaction.Guerrilla, "PLAV Guerrillas", "PLAV", Palette.Hex(0x3F7F5F), Palette.Hex(0xA8322E), 25.0f, false);
        Add(EFaction.Pirate, "Jamaican Pirates", "PIR", Palette.Hex(0x6B3A8A), Palette.Hex(0xE3D23A), -5.0f, false);
        Add(EFaction.Civilian, "Civilians", "CIV", Palette.Hex(0x8A7F70), Palette.Hex(0x6A5A4A), 0.0f, false);

        Get(EFaction.Allied).Rivals = new[] { EFaction.China };
        Get(EFaction.China).Rivals = new[] { EFaction.Allied };
        Get(EFaction.Oil).Rivals = new[] { EFaction.Guerrilla };
        Get(EFaction.Guerrilla).Rivals = new[] { EFaction.Oil };

        foreach (EFaction Mood in MoodFactions)
        {
            Get(Mood).bHasMood = true;
            Get(Mood).LastMood = MoodOf(Mood);
        }
    }

    private void Add(EFaction Id, string Name, string Short, FVector4 Color, FVector4 Accent, float Standing, bool bLocked)
    {
        Infos[Id] = new FactionInfo { Id = Id, Name = Name, Short = Short, Color = Color, Accent = Accent, Standing = Standing, bLockedHostile = bLocked };
    }

    public FactionInfo Get(EFaction Id) => Infos.TryGetValue(Id, out FactionInfo? Info) ? Info : Infos[EFaction.Civilian];

    public IEnumerable<FactionInfo> All => Infos.Values;

    public EMood MoodOf(EFaction Id)
    {
        if (Id == EFaction.Merc)
        {
            return EMood.Friendly;
        }

        FactionInfo Info = Get(Id);
        if (Info.bLockedHostile)
        {
            return EMood.Hostile;
        }

        if (!Info.bHasMood)
        {
            return EMood.Neutral;
        }

        if (Info.Standing < HostileBelow)
        {
            return EMood.Hostile;
        }

        return Info.Standing >= FriendlyAtOrAbove ? EMood.Friendly : EMood.Neutral;
    }

    public bool IsProvoked(EFaction Id) => Get(Id).ProvokedTime > 0.0f;

    public void Provoke(EFaction Id, float Seconds = ProvokeDuration)
    {
        if (Id is EFaction.Civilian or EFaction.None or EFaction.Merc)
        {
            return;
        }

        FactionInfo Info = Get(Id);
        if (Info.ProvokedTime <= 0.0f && MoodOf(Id) != EMood.Hostile)
        {
            Mercs.Feed.Post($"{Info.Name} are shooting back!", ENewsTone.Alert);
        }

        Info.ProvokedTime = MathF.Max(Info.ProvokedTime, Seconds);
    }

    public static bool AtWar(EFaction A, EFaction B)
    {
        if (A == B || A is EFaction.None or EFaction.Civilian || B is EFaction.None or EFaction.Civilian)
        {
            return false;
        }

        bool Pair(EFaction X, EFaction Y) => (A == X && B == Y) || (A == Y && B == X);

        return Pair(EFaction.VZ, EFaction.Allied)
            || Pair(EFaction.VZ, EFaction.China)
            || Pair(EFaction.VZ, EFaction.Guerrilla)
            || Pair(EFaction.VZ, EFaction.Pirate)
            || Pair(EFaction.Guerrilla, EFaction.Oil);
    }

    public bool IsDisguisedFrom(EFaction Observer)
    {
        return DisguiseFaction != EFaction.None && DisguiseFaction == Observer && !bDisguiseBlown;
    }

    public bool IsHostile(EFaction Observer, IDamageable Target)
    {
        if (!Target.IsAlive || Observer is EFaction.Civilian or EFaction.None)
        {
            return false;
        }

        if (Target.IsPlayerControlled)
        {
            if (IsDisguisedFrom(Observer))
            {
                return false;
            }

            return MoodOf(Observer) == EMood.Hostile || IsProvoked(Observer);
        }

        if (Target.Faction == EFaction.Merc)
        {
            return MoodOf(Observer) == EMood.Hostile || IsProvoked(Observer);
        }

        return AtWar(Observer, Target.Faction);
    }

    // Anything the player does to a faction is only held against them once a surviving witness reports it.
    public void Offense(EFaction Victim, float Amount, FVector3 Where, string Reason, bool bViolent = true)
    {
        FactionInfo Info = Get(Victim);
        if (!Info.bHasMood || Amount <= 0.0f)
        {
            return;
        }

        if (bViolent)
        {
            Provoke(Victim);
        }

        Soldier? Reporter = null;
        float Best = float.MaxValue;
        FVector3 PlayerAt = Mercs.PlayerPosition;
        foreach (Soldier Candidate in Mercs.Soldiers)
        {
            if (!Candidate.IsAlive || Candidate.Faction != Victim || Candidate.bIsHvt)
            {
                continue;
            }

            float Distance = FVector3.Distance(Candidate.Position, Where);
            if (Distance > WitnessRange || Distance >= Best)
            {
                continue;
            }

            FVector3 Eye = Candidate.Position + new FVector3(0.0f, 0.7f, 0.0f);
            bool bSees = Geo.LineOfSight(Eye, Where + new FVector3(0.0f, 1.0f, 0.0f), Candidate.Owner, Mercs.Player?.Owner ?? Entity.Null)
                || Geo.LineOfSight(Eye, PlayerAt + new FVector3(0.0f, 0.6f, 0.0f), Candidate.Owner, Mercs.Player?.Owner ?? Entity.Null);
            if (!bSees)
            {
                continue;
            }

            Best = Distance;
            Reporter = Candidate;
        }

        if (Reporter is null)
        {
            if (UnwitnessedCooldown <= 0.0f)
            {
                Mercs.Feed.Post($"No {Info.Short} witnesses. ({Reason})", ENewsTone.Good);
                UnwitnessedCooldown = 6.0f;
            }

            return;
        }

        foreach (WitnessReport Existing in Reports)
        {
            if (Existing.Reporter == Reporter)
            {
                Existing.Amount = MathF.Min(Existing.Amount + Amount, 40.0f);
                Existing.Reason = Reason;
                return;
            }
        }

        Reports.Add(new WitnessReport { Faction = Victim, Reporter = Reporter, Amount = Amount, Remaining = ReportDelay, Reason = Reason });
        Reporter.BeginReporting();
        Mercs.Feed.Post($"{Info.Short} witness spotted you! Silence them before they report.", ENewsTone.Alert);
    }

    public void ChangeStanding(EFaction Id, float Delta, string Reason)
    {
        FactionInfo Info = Get(Id);
        if (!Info.bHasMood || Delta == 0.0f)
        {
            return;
        }

        Info.Standing = Math.Clamp(Info.Standing + Delta, -100.0f, 100.0f);
        string Sign = Delta > 0.0f ? "+" : string.Empty;
        Mercs.Feed.Post($"{Info.Short} standing {Sign}{Delta:0}  ({Reason})", Delta > 0.0f ? ENewsTone.Good : ENewsTone.Faction);
        CheckMoodChange(Info);
    }

    private void CheckMoodChange(FactionInfo Info)
    {
        EMood Now = MoodOf(Info.Id);
        if (Now == Info.LastMood)
        {
            return;
        }

        Info.LastMood = Now;
        Sfx.Ui(Now switch { EMood.Hostile => ESfx.Alert, EMood.Friendly => ESfx.Objective, _ => ESfx.UiClose }, 0.6f);
        string Word = Now switch
        {
            EMood.Hostile => "HOSTILE",
            EMood.Friendly => "FRIENDLY",
            _ => "NEUTRAL",
        };

        Mercs.Feed.Announce($"{Info.Name}: {Word}", Now == EMood.Hostile ? "They will attack on sight. Bribe them from the PDA." : "Their mood toward you has changed.");
    }

    public int BribeCost(EFaction Id)
    {
        FactionInfo Info = Get(Id);
        if (!Info.bHasMood || Info.Standing >= 0.0f)
        {
            return 0;
        }

        return 10000 + (int)(-Info.Standing * 900.0f) / 1000 * 1000;
    }

    public bool Bribe(EFaction Id)
    {
        int Cost = BribeCost(Id);
        if (Cost <= 0)
        {
            return false;
        }

        FactionInfo Info = Get(Id);
        if (!Mercs.Wallet.Spend(Cost, 0, $"Bribe to {Info.Short}"))
        {
            return false;
        }

        Info.Standing = MathF.Max(Info.Standing, 0.0f);
        Info.ProvokedTime = 0.0f;
        Reports.RemoveAll(Report => Report.Faction == Id);
        CheckMoodChange(Info);
        Mercs.Feed.Post($"{Info.Name} will look the other way.", ENewsTone.Good);
        return true;
    }

    public void RewardContract(EFaction Employer, float Amount)
    {
        FactionInfo Info = Get(Employer);
        Info.ContractsDone++;
        ChangeStanding(Employer, Amount, "contract complete");
        foreach (EFaction Rival in Info.Rivals)
        {
            ChangeStanding(Rival, -Amount * 0.35f, $"you helped {Info.Short}");
        }
    }

    public void SetDisguise(EFaction Faction)
    {
        if (Faction == DisguiseFaction)
        {
            return;
        }

        DisguiseFaction = Faction is EFaction.Merc or EFaction.Civilian or EFaction.None ? EFaction.None : Faction;
        Suspicion = 0.0f;
        bDisguiseBlown = false;
        if (DisguiseFaction != EFaction.None)
        {
            Mercs.Feed.Post($"Disguised as {Get(DisguiseFaction).Short}. Don't get too close or start shooting.", ENewsTone.Good);
        }
    }

    public void RaiseSuspicion(float Amount)
    {
        if (DisguiseFaction == EFaction.None || bDisguiseBlown)
        {
            return;
        }

        Suspicion = Mathf.Clamp01(Suspicion + Amount);
        if (Suspicion >= 1.0f)
        {
            bDisguiseBlown = true;
            Mercs.Feed.Announce("DISGUISE BLOWN", $"{Get(DisguiseFaction).Name} have seen through your cover.", 3.0f);
            Provoke(DisguiseFaction);
        }
    }

    public void Update(float DeltaTime)
    {
        UnwitnessedCooldown -= DeltaTime;

        foreach (FactionInfo Info in Infos.Values)
        {
            if (Info.ProvokedTime > 0.0f)
            {
                Info.ProvokedTime -= DeltaTime;
            }
        }

        for (int Index = Reports.Count - 1; Index >= 0; --Index)
        {
            WitnessReport Report = Reports[Index];
            if (!Report.Reporter.IsAlive)
            {
                Mercs.Feed.Post($"{Get(Report.Faction).Short} witness silenced.", ENewsTone.Good);
                Reports.RemoveAt(Index);
                continue;
            }

            Report.Remaining -= DeltaTime;
            if (Report.Remaining > 0.0f)
            {
                continue;
            }

            Reports.RemoveAt(Index);
            Report.Reporter.EndReporting();
            ChangeStanding(Report.Faction, -Report.Amount, Report.Reason);
        }

        UpdateDisguise(DeltaTime);
    }

    private void UpdateDisguise(float DeltaTime)
    {
        if (DisguiseFaction == EFaction.None || bDisguiseBlown || Mercs.Player is null)
        {
            return;
        }

        FVector3 PlayerAt = Mercs.Player.Position;
        float Closest = float.MaxValue;
        foreach (Soldier Candidate in Mercs.Soldiers)
        {
            if (Candidate.IsAlive && Candidate.Faction == DisguiseFaction)
            {
                Closest = MathF.Min(Closest, FVector3.Distance(Candidate.Position, PlayerAt));
            }
        }

        if (Closest < 18.0f)
        {
            RaiseSuspicion(DeltaTime * (18.0f - Closest) / 18.0f * 0.35f);
        }
        else
        {
            Suspicion = MathF.Max(0.0f, Suspicion - DeltaTime * 0.08f);
        }
    }
}
