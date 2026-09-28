using System;
using System.Collections.Generic;
using System.Globalization;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

// Turns the events a cinematic sequence fires into gameplay, so an edit can call its own explosions, airstrikes and title cards.
public sealed class Trailer
{
    private const string TitleRml = """
        <rml>
        <head>
            <style>
                body { width: 100%; height: 100%; }
                #card { position: absolute; left: 0; top: 38%; width: 100%; text-align: center; opacity: 0; }
                #title { display: block; font-size: 84dp; font-weight: bold; color: #f4efe4; letter-spacing: 6dp; }
                #rule { display: block; margin: 10dp auto 12dp auto; width: 360dp; height: 3dp; background-color: #f0922e; }
                #subtitle { display: block; font-size: 26dp; color: #e6dfd2; letter-spacing: 3dp; }
            </style>
        </head>
        <body>
            <div id="card">
                <span id="title"></span>
                <div id="rule"></div>
                <span id="subtitle"></span>
            </div>
        </body>
        </rml>
        """;

    private const float TitleFadeSeconds = 0.6f;

    private UIDocument TitleScreen;
    private float TitleAge;
    private float TitleLength;
    private bool bCinematic;
    private bool bGodBeforeCinematic;
    private bool bClockFrozen;

    public bool IsCinematic => bCinematic;
    public bool IsClockFrozen => bClockFrozen;

    public void Update(float RealDeltaTime)
    {
        CWorld World = Mercs.World;
        bool bPlaying = CSequenceLibrary.IsAnySequencePlaying(World);
        if (bPlaying != bCinematic)
        {
            SetCinematic(bPlaying);
        }

        while (CSequenceLibrary.NextEvent(World))
        {
            Handle(CSequenceLibrary.GetEventName(World), CSequenceLibrary.GetEventPayload(World));
        }

        UpdateTitle(RealDeltaTime);
    }

    public void Shutdown()
    {
        if (TitleScreen.IsValid)
        {
            TitleScreen.Close();
        }
    }

    private void SetCinematic(bool bOn)
    {
        bCinematic = bOn;
        MercsHud.Instance?.SetVisible(!bOn);

        // Nothing in a shot should kill the star, and the edit should not be interrupted by a respawn.
        if (Mercs.Director is { } Director)
        {
            if (bOn)
            {
                bGodBeforeCinematic = Director.bGodMode;
                Director.bGodMode = true;
            }
            else
            {
                Director.bGodMode = bGodBeforeCinematic;
                bClockFrozen = false;
            }
        }
    }

    private void Handle(string Name, string Payload)
    {
        string[] Args = Payload.Split(',', StringSplitOptions.TrimEntries);
        switch (Name)
        {
            // Heights in a payload are above the ground, so a beat can be placed without knowing the terrain.
            case "Explode":
                Explosion.Detonate(Geo.Ground(Point(Args, 0)) + new FVector3(0.0f, Number(Args, 1, 0.0f), 0.0f), Number(Args, 3, 9.0f), Number(Args, 4, 900.0f), Mercs.Player, 1.5f, true, Mercs.Player);
                break;

            case "Support":
                if (Enum.TryParse(Word(Args, 0), true, out ESupportKind Kind))
                {
                    Mercs.Support.OnBeaconLanded(Kind, Geo.Ground(Point(Args, 1)));
                }
                break;

            case "Squad":
                SpawnSquad(Args);
                break;

            case "Vehicle":
                SpawnVehicle(Args);
                break;

            case "Demolish":
                Demolish(Point(Args, 0), Number(Args, 3, 40.0f));
                break;

            case "Time":
                Mercs.Clock.TimeOfDay = Math.Clamp(Number(Args, 0, 12.0f), 0.0f, 24.0f) / 24.0f % 1.0f;
                Mercs.Clock.Skip(0.0f);
                break;

            case "Freeze":
                bClockFrozen = Word(Args, 0) != "off";
                break;

            case "Player":
                if (Mercs.Player is { } Player)
                {
                    Player.Teleport(Geo.Ground(Point(Args, 0)) + new FVector3(0.0f, 1.5f, 0.0f));
                    MercCamera.Instance?.SnapBehind(Number(Args, 3, 0.0f));
                }
                break;

            case "Title":
                ShowTitle(Payload);
                break;

            case "Hud":
                MercsHud.Instance?.SetVisible(Word(Args, 0) == "on");
                break;

            default:
                Debug.LogWarning($"Trailer: nothing handles the sequence event '{Name}'.");
                break;
        }
    }

    private static void SpawnSquad(string[] Args)
    {
        EFaction Team = Enum.TryParse(Word(Args, 0), true, out EFaction Parsed) ? Parsed : EFaction.VZ;
        FVector3 At = Geo.Ground(Point(Args, 1));
        int Count = Math.Clamp((int)Number(Args, 4, 4.0f), 1, 16);
        for (int Index = 0; Index < Count; ++Index)
        {
            ESoldierRole Role = Index == 0 ? ESoldierRole.Gunner : (Index == 1 ? ESoldierRole.RocketTrooper : ESoldierRole.Rifleman);
            Spawner.SpawnSoldier(Team, Role, Geo.RandomAround(At, 0.0f, 5.0f), null);
        }
    }

    private static void SpawnVehicle(string[] Args)
    {
        if (!Enum.TryParse(Word(Args, 0), true, out EVehicleType Type))
        {
            Debug.LogWarning($"Trailer: '{Word(Args, 0)}' is not a vehicle type.");
            return;
        }

        EFaction Team = Enum.TryParse(Word(Args, 1), true, out EFaction Parsed) ? Parsed : EFaction.VZ;
        FVector3 At = Geo.Ground(Point(Args, 2));
        float Yaw = Number(Args, 5, 0.0f);

        // A destination makes it drive, which is what puts a convoy on the move in a shot.
        List<FVector3>? Route = null;
        if (Args.Length >= 8)
        {
            Route = new List<FVector3> { Geo.Ground(new FVector3(Number(Args, 6, 0.0f), 0.0f, Number(Args, 7, 0.0f))) };
        }
        Spawner.SpawnVehicle(Type, Team, At, Yaw, true, Route, null);
    }

    private static void Demolish(FVector3 Near, float Radius)
    {
        Structure? Best = null;
        float BestDistance = Radius;
        foreach (Structure Candidate in Mercs.Structures)
        {
            float Distance = FVector3.Distance(Candidate.Position, Near);
            if (Candidate.IsAlive && !Candidate.bInvulnerable && Distance < BestDistance)
            {
                Best = Candidate;
                BestDistance = Distance;
            }
        }

        if (Best is not null)
        {
            Explosion.Detonate(Best.Position, Best.Radius + 4.0f, 6000.0f, Mercs.Player, 4.0f, true, Mercs.Player);
        }
    }

    private void ShowTitle(string Payload)
    {
        string[] Lines = Payload.Split('|');
        if (!TitleScreen.IsValid)
        {
            TitleScreen = Mercs.World.UI.LoadDocumentFromMemory(TitleRml, "[trailer title]");
            if (!TitleScreen.IsValid)
            {
                Debug.LogError("Trailer: the title card failed to load.");
                return;
            }
        }

        TitleScreen["title"].SetText(Lines.Length > 0 ? Lines[0] : string.Empty);
        TitleScreen["subtitle"].SetText(Lines.Length > 1 ? Lines[1] : string.Empty);
        TitleScreen["rule"].SetVisible(Lines.Length > 1 && Lines[1].Length > 0);
        TitleLength = Lines.Length > 2 && float.TryParse(Lines[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float Seconds) ? Seconds : 3.0f;
        TitleAge = 0.0f;
        TitleScreen.Show(false, false);
        TitleScreen.BringToFront();
    }

    private void UpdateTitle(float RealDeltaTime)
    {
        if (!TitleScreen.IsValid || TitleLength <= 0.0f)
        {
            return;
        }

        TitleAge += RealDeltaTime;
        float FadeIn = Mathf.Clamp01(TitleAge / TitleFadeSeconds);
        float FadeOut = Mathf.Clamp01((TitleLength - TitleAge) / TitleFadeSeconds);
        float Opacity = MathF.Min(FadeIn, FadeOut);
        TitleScreen["card"].SetStyle("opacity", Opacity.ToString("0.000", CultureInfo.InvariantCulture));

        if (TitleAge >= TitleLength)
        {
            TitleLength = 0.0f;
            TitleScreen.Hide();
        }
    }

    private static string Word(string[] Args, int Index) => Index < Args.Length ? Args[Index] : string.Empty;

    private static float Number(string[] Args, int Index, float Default)
    {
        return Index < Args.Length && float.TryParse(Args[Index], NumberStyles.Float, CultureInfo.InvariantCulture, out float Value) ? Value : Default;
    }

    private static FVector3 Point(string[] Args, int First) => new(Number(Args, First, 0.0f), Number(Args, First + 1, 0.0f), Number(Args, First + 2, 0.0f));
}
