using System;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

// The score under gameplay, which comes in while someone is shooting at the player and lets go once the fight has been over a while.
public static class Music
{
    private const string CombatTrack = "/Game/Content/Audio/Music_Combat.lasset";
    private const float CombatVolume = 0.45f;
    private const float FadeInSeconds = 1.5f;
    private const float FadeOutSeconds = 5.0f;
    private const float HoldSeconds = 8.0f;
    private const float ThreatRange = 110.0f;

    private static PlayingSound Combat;
    private static bool bCombatPlaying;
    private static float LastThreatTime = -100.0f;

    public static void Reset()
    {
        Shutdown();
        LastThreatTime = -100.0f;
    }

    public static void Shutdown()
    {
        if (bCombatPlaying)
        {
            Combat.Stop(true, 0.5f);
            bCombatPlaying = false;
        }
    }

    public static void Update()
    {
        // A cinematic brings its own score, so gameplay music steps aside for it.
        bool bCinematic = CSequenceLibrary.IsAnySequencePlaying(Mercs.World);
        if (!bCinematic && IsPlayerUnderThreat())
        {
            LastThreatTime = Mercs.Time;
        }

        bool bWanted = !bCinematic && Mercs.Time - LastThreatTime < HoldSeconds;
        if (bWanted && !bCombatPlaying)
        {
            StartCombat();
        }
        else if (!bWanted && bCombatPlaying)
        {
            Combat.Stop(true, bCinematic ? 0.5f : FadeOutSeconds);
            bCombatPlaying = false;
        }
    }

    private static bool IsPlayerUnderThreat()
    {
        MercPlayer? Player = Mercs.Player;
        if (Player is null)
        {
            return false;
        }

        foreach (Soldier Enemy in Mercs.Soldiers)
        {
            if (Enemy.IsFighting(Player) && FVector3.DistanceSquared(Enemy.Position, Player.Position) < ThreatRange * ThreatRange)
            {
                return true;
            }
        }
        return false;
    }

    private static void StartCombat()
    {
        CAudioStream? Track = Asset.Load<CAudioStream>(CombatTrack);
        if (Track is null)
        {
            return;
        }

        FAudioPlayParams Params = FAudioPlayParams.Default();
        Params.Volume = 0.0f;
        Params.bLooping = true;
        Params.Bus = EAudioBus.Music;
        Combat = Sound.PlayEx(Track, Params);
        Combat.FadeTo(CombatVolume, FadeInSeconds);
        bCombatPlaying = true;
    }
}
