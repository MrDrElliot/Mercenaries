using System;
using System.Collections.Generic;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

public enum ESfx : byte
{
    GunPistol,
    GunRifle,
    GunSmg,
    GunShotgun,
    GunSniper,
    GunMachineGun,
    GunAutocannon,
    GunTankCannon,
    RocketLaunch,
    GrenadeLaunch,
    ExplosionSmall,
    ExplosionLarge,
    ExplosionHuge,
    ImpactGround,
    ImpactMetal,
    Ricochet,
    HitBody,
    BulletWhiz,
    Reload,
    EmptyClick,
    WeaponSwitch,
    Footstep,
    Land,
    MeleeHit,
    Throw,
    C4Plant,
    C4Beep,
    EngineJeep,
    EngineTruck,
    EngineTank,
    RotorHeli,
    ArtilleryWhistle,
    EngineJet,
    RadioCall,
    FlarePop,
    UiClick,
    UiOpen,
    UiClose,
    Cash,
    Objective,
    Alert,
    Failure,
    Pickup,
    AmbientIsland,
    Collapse,
    WoodBreak,
    GlassShatter,
    FireLoop,
    TreeFall,
    Count,
}

public static class Sfx
{
    private const string Folder = "/Game/Content/Audio/";
    private const float SpeedOfSound = 343.0f;

    private static readonly string[] Names =
    {
        "Gun_Pistol", "Gun_Rifle", "Gun_Smg", "Gun_Shotgun", "Gun_Sniper", "Gun_MachineGun", "Gun_Autocannon", "Gun_TankCannon",
        "Rocket_Launch", "Grenade_Launch", "Explosion_Small", "Explosion_Large", "Explosion_Huge", "Impact_Ground", "Impact_Metal",
        "Ricochet", "Hit_Body", "Bullet_Whiz", "Reload", "Empty_Click", "Weapon_Switch", "Footstep", "Land", "Melee_Hit", "Throw",
        "C4_Plant", "C4_Beep", "Engine_Jeep", "Engine_Truck", "Engine_Tank", "Rotor_Heli", "Artillery_Whistle", "Engine_Jet",
        "Radio_Call", "Flare_Pop", "Ui_Click", "Ui_Open", "Ui_Close", "Cash", "Objective", "Alert", "Failure", "Pickup", "Ambient_Island",
        "Collapse", "Wood_Break", "Glass_Shatter", "Fire_Loop", "Tree_Fall",
    };

    private static readonly CAudioStream?[] Clips = new CAudioStream?[(int)ESfx.Count];
    private static readonly float[] LastPlayed = new float[(int)ESfx.Count];
    private static readonly Dictionary<Vehicle, SoundLoop> Engines = new();
    private static readonly List<Vehicle> Silenced = new();
    private static SoundLoop? Ambience;
    private static bool bWarned;

    public static FVector3 Listener => MercCamera.Instance?.ViewPosition ?? Mercs.PlayerPosition;

    public static void Reset()
    {
        Shutdown();
        Array.Clear(Clips);
        Array.Fill(LastPlayed, -100.0f);
    }

    public static void Shutdown()
    {
        foreach (SoundLoop Loop in Engines.Values)
        {
            Loop.Stop();
        }

        Engines.Clear();
        Ambience?.Stop();
        Ambience = null;
    }

    public static CAudioStream? Clip(ESfx Id)
    {
        CAudioStream? Loaded = Clips[(int)Id];
        if (Loaded is not null && Loaded.IsValid)
        {
            return Loaded;
        }

        Loaded = Asset.Load<CAudioStream>(Folder + Names[(int)Id] + ".lasset");
        Clips[(int)Id] = Loaded;
        if (Loaded is null && !bWarned)
        {
            bWarned = true;
            Debug.LogWarning($"Mercenaries: sound '{Names[(int)Id]}' is missing, run Tools/GenerateSounds.py and import SourceAudio.");
        }

        return Loaded;
    }

    public static PlayingSound Ui(ESfx Id, float Volume = 0.6f, float Pitch = 1.0f)
    {
        CAudioStream? Sound = Clip(Id);
        return Sound is null ? default : LuminaSharp.Sound.PlayOnBus(Sound, EAudioBus.UI, Volume, Pitch);
    }

    // A one-shot in the world, dropped past Reach and muffled and delayed with distance like the real thing.
    public static PlayingSound At(ESfx Id, FVector3 Where, float Volume, float Reach, float NearDistance, float PitchJitter = 0.06f, float MinGap = 0.0f, float Pitch = 1.0f)
    {
        float Distance = FVector3.Distance(Where, Listener);
        if (Distance > Reach || (MinGap > 0.0f && Mercs.Time - LastPlayed[(int)Id] < MinGap))
        {
            return default;
        }

        CAudioStream? Sound = Clip(Id);
        if (Sound is null)
        {
            return default;
        }

        LastPlayed[(int)Id] = Mercs.Time;
        FAudioPlayParams Params = FAudioPlayParams.Default();
        Params.Volume = Volume;
        Params.Pitch = Pitch * (1.0f + Mercs.Range(-PitchJitter, PitchJitter));
        Params.bSpatialized = true;
        Params.Position = Where;
        Params.Bus = EAudioBus.SFX;
        Params.Attenuation.Model = EAudioAttenuationModel.Inverse;
        Params.Attenuation.MinDistance = NearDistance;
        Params.Attenuation.MaxDistance = Reach;
        Params.Attenuation.Rolloff = 1.0f;
        Params.Attenuation.DopplerFactor = 0.0f;
        Params.StartDelaySeconds = Distance > 25.0f ? Distance / SpeedOfSound : 0.0f;

        PlayingSound Voice = LuminaSharp.Sound.PlayEx(Sound, Params);
        Voice.LowPassCutoff = Muffle(Distance, Reach);
        return Voice;
    }

    public static float Muffle(float Distance, float Reach) => Mathf.Lerp(20000.0f, 1500.0f, MathF.Pow(Mathf.Clamp01(Distance / Reach), 0.6f));

    public static void Gun(EWeapon Kind, FVector3 Muzzle, bool bPlayer)
    {
        (ESfx Id, float Volume, float Reach) = Kind switch
        {
            EWeapon.Pistol => (ESfx.GunPistol, 0.55f, 220.0f),
            EWeapon.Smg => (ESfx.GunSmg, 0.5f, 220.0f),
            EWeapon.Shotgun => (ESfx.GunShotgun, 0.8f, 260.0f),
            EWeapon.Sniper => (ESfx.GunSniper, 0.95f, 450.0f),
            EWeapon.MachineGun or EWeapon.VehicleMG => (ESfx.GunMachineGun, 0.7f, 300.0f),
            EWeapon.Autocannon => (ESfx.GunAutocannon, 0.85f, 350.0f),
            EWeapon.TankCannon => (ESfx.GunTankCannon, 1.0f, 600.0f),
            EWeapon.Rocket or EWeapon.HeliRockets => (ESfx.RocketLaunch, 0.8f, 320.0f),
            EWeapon.GrenadeLauncher => (ESfx.GrenadeLaunch, 0.7f, 200.0f),
            _ => (ESfx.GunRifle, 0.65f, 280.0f),
        };

        // Many AI rifles firing at once would otherwise stack into a wall of identical voices.
        At(Id, Muzzle, bPlayer ? Volume : Volume * 0.8f, Reach, 5.0f, 0.05f, bPlayer ? 0.0f : 0.03f);
    }

    public static void Explosion(FVector3 At, float Radius)
    {
        (ESfx Id, float Volume) = Radius switch
        {
            < 6.5f => (ESfx.ExplosionSmall, 0.9f),
            < 10.0f => (ESfx.ExplosionLarge, 1.0f),
            _ => (ESfx.ExplosionHuge, 1.0f),
        };

        Sfx.At(Id, At, Volume, 900.0f, Radius * 2.5f, 0.1f);
    }

    // A round passing close to the listener's head snaps past even when it misses.
    public static void NearMiss(FVector3 From, FVector3 To)
    {
        FVector3 Head = Listener;
        FVector3 Segment = To - From;
        float Along = Mathf.Clamp01(FVector3.Dot(Head - From, Segment) / MathF.Max(Segment.LengthSquared, 1e-4f));
        FVector3 Closest = From + Segment * Along;
        if (Along > 0.05f && Along < 0.98f && FVector3.Distance(Closest, Head) < 3.5f)
        {
            At(ESfx.BulletWhiz, Closest, 0.5f, 10.0f, 1.5f, 0.15f, 0.06f);
        }
    }

    public static void StartAmbience()
    {
        Ambience?.Stop();
        CAudioStream? Sound = Clip(ESfx.AmbientIsland);
        if (Sound is not null)
        {
            FAudioPlayParams Params = FAudioPlayParams.Default();
            Params.Volume = 0.0f;
            Params.bLooping = true;
            Params.Bus = EAudioBus.Ambient;
            Ambience = new SoundLoop(LuminaSharp.Sound.PlayEx(Sound, Params), 0.0f);
            Ambience.Voice.FadeTo(0.35f, 3.0f);
        }
    }

    public static void Update()
    {
        FVector3 Head = Listener;
        foreach (Vehicle Ride in Mercs.Vehicles)
        {
            float Reach = Ride.Definition.bAir ? 420.0f : 160.0f;
            bool bAudible = Ride.EngineRunning && FVector3.DistanceSquared(Ride.Position, Head) < Reach * Reach;
            Engines.TryGetValue(Ride, out SoundLoop? Loop);

            if (!bAudible)
            {
                if (Loop is not null)
                {
                    Loop.Stop();
                    Engines.Remove(Ride);
                }

                continue;
            }

            if (Loop is null)
            {
                Loop = StartEngine(Ride, Reach);
                if (Loop is null)
                {
                    continue;
                }

                Engines[Ride] = Loop;
            }

            float Load = Ride.EngineLoad;
            float Distance = FVector3.Distance(Ride.Position, Head);
            PlayingSound Voice = Loop.Voice;
            Voice.Position = CEntityLibrary.GetRenderLocation(Mercs.World, Ride.Owner);
            Voice.Pitch = Ride.Definition.bAir ? 0.92f + Load * 0.2f : 0.75f + Load * 0.85f;
            Voice.Volume = (Ride.IsPlayerControlled ? 0.55f : 0.75f) * (0.55f + Load * 0.45f);
            Voice.LowPassCutoff = Muffle(Distance, Reach);
        }

        Silenced.Clear();
        foreach (Vehicle Ride in Engines.Keys)
        {
            if (!Mercs.Vehicles.Contains(Ride))
            {
                Silenced.Add(Ride);
            }
        }

        foreach (Vehicle Ride in Silenced)
        {
            Engines[Ride].Stop();
            Engines.Remove(Ride);
        }
    }

    // A looping voice that follows something moving, with doppler, for aircraft the game flies by script.
    public static SoundLoop? StartMover(ESfx Id, FVector3 Where, float Volume, float Reach, float NearDistance)
    {
        CAudioStream? Sound = Clip(Id);
        if (Sound is null)
        {
            return null;
        }

        FAudioPlayParams Params = FAudioPlayParams.Default();
        Params.Volume = Volume;
        Params.bLooping = true;
        Params.bSpatialized = true;
        Params.Position = Where;
        Params.Bus = EAudioBus.SFX;
        Params.Attenuation.Model = EAudioAttenuationModel.Inverse;
        Params.Attenuation.MinDistance = NearDistance;
        Params.Attenuation.MaxDistance = Reach;
        Params.Attenuation.DopplerFactor = 1.0f;
        Params.FadeInSeconds = 1.0f;
        SoundLoop Loop = new(LuminaSharp.Sound.PlayEx(Sound, Params), Reach);
        Loop.Voice.LowPassCutoff = Muffle(FVector3.Distance(Where, Listener), Reach);
        return Loop;
    }

    private static SoundLoop? StartEngine(Vehicle Ride, float Reach)
    {
        ESfx Id = Ride.Type switch
        {
            EVehicleType.AttackHeli or EVehicleType.TransportHeli => ESfx.RotorHeli,
            EVehicleType.Tank or EVehicleType.Apc => ESfx.EngineTank,
            EVehicleType.Truck or EVehicleType.FuelTruck => ESfx.EngineTruck,
            _ => ESfx.EngineJeep,
        };

        CAudioStream? Sound = Clip(Id);
        if (Sound is null)
        {
            return null;
        }

        FAudioPlayParams Params = FAudioPlayParams.Default();
        Params.Volume = 0.0f;
        Params.bLooping = true;
        Params.bSpatialized = true;
        Params.Position = Ride.Position;
        Params.Bus = EAudioBus.SFX;
        Params.Attenuation.Model = EAudioAttenuationModel.Inverse;
        Params.Attenuation.MinDistance = Ride.Definition.bAir ? 12.0f : 5.0f;
        Params.Attenuation.MaxDistance = Reach;
        Params.Attenuation.DopplerFactor = 0.0f;
        Params.StartFrame = (ulong)Mercs.Rng.Next(0, 44100);
        return new SoundLoop(LuminaSharp.Sound.PlayEx(Sound, Params), Reach);
    }
}

public sealed class SoundLoop
{
    public readonly PlayingSound Voice;
    public readonly float Reach;

    public SoundLoop(PlayingSound Voice, float Reach)
    {
        this.Voice = Voice;
        this.Reach = Reach;
    }

    public void Move(FVector3 Where, FVector3 Velocity)
    {
        PlayingSound Live = Voice;
        Live.Position = Where;
        Live.Velocity = Velocity;
        Live.LowPassCutoff = Sfx.Muffle(FVector3.Distance(Where, Sfx.Listener), Reach);
    }

    public void Stop() => Voice.Stop(true, 0.35f);
}
