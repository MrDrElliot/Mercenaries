using System;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

// Turns the sun across the island through the engine sky, which lights the night from the moon once the sun has set.
public sealed class DayNight
{
    private const float DayLengthSeconds = 1200.0f;
    // The arc leans south, so noon shadows still fall at an angle instead of straight down.
    private const float ArcTiltDegrees = 30.0f;
    private const float DayIntensity = 4.0f;

    private static readonly FVector3 NoonColor = new(1.0f, 0.95f, 0.86f);
    private static readonly FVector3 LowSunColor = new(1.0f, 0.55f, 0.3f);
    private static readonly FVector3 DayFog = new(0.55f, 0.65f, 0.75f);
    private static readonly FVector3 NightFog = new(0.05f, 0.07f, 0.12f);

    private Entity SunEntity = Entity.Null;
    private Entity SkyEntity = Entity.Null;

    // 0 is midnight, 0.25 sunrise, 0.5 noon and 0.75 sunset.
    public float TimeOfDay = 0.3f;

    public float SunHeight { get; private set; }
    public bool IsNight => SunHeight < -0.05f;
    public int Hour => (int)(TimeOfDay * 24.0f);
    public int Minute => (int)(TimeOfDay * 1440.0f) % 60;

    public void Bind(Entity Sun, Entity Sky)
    {
        SunEntity = Sun;
        SkyEntity = Sky;
        Apply();
    }

    public void Update(float DeltaTime)
    {
        TimeOfDay = (TimeOfDay + DeltaTime / DayLengthSeconds) % 1.0f;
        Apply();
    }

    public void Skip(float Hours)
    {
        TimeOfDay = (TimeOfDay + Hours / 24.0f) % 1.0f;
        Apply();
    }

    private void Apply()
    {
        EntityRegistry Registry = Mercs.World.Registry;
        float Angle = (TimeOfDay - 0.25f) * MathF.Tau;
        float Tilt = Mathf.Radians(ArcTiltDegrees);
        FVector3 ToSun = new(MathF.Cos(Angle), MathF.Sin(Angle) * MathF.Cos(Tilt), MathF.Sin(Angle) * MathF.Sin(Tilt));
        SunHeight = ToSun.Y;

        float Daylight = Mathf.Clamp01(SunHeight / 0.3f);
        if (Registry.TryGet<SDirectionalLightComponent>(SunEntity) is { } Light)
        {
            Light.Direction = ToSun;
            Light.Intensity = DayIntensity;
            Light.Color = FVector3.Lerp(LowSunColor, NoonColor, Daylight);
            Light.bMoonlight = true;
            Light.MoonIntensity = 0.8f;
        }

        // Surfaces take their ambient from the sky, so a brighter night sky is what keeps shadows readable after dark.
        if (Registry.TryGet<SEnvironmentComponent>(SkyEntity) is { } Environment)
        {
            Environment.NightBrightness = 0.08f;
            Environment.NightSkyColor = new FVector3(0.03f, 0.05f, 0.1f);
        }

        if (Registry.TryGet<SSkyLightComponent>(SkyEntity) is { } Ambient)
        {
            Ambient.Intensity = Mathf.Lerp(0.35f, 1.0f, Mathf.Clamp01((SunHeight + 0.1f) / 0.3f));
        }

        if (Registry.TryGet<SExponentialHeightFogComponent>(SkyEntity) is { } Fog)
        {
            Fog.FogInscatteringColor = FVector3.Lerp(NightFog, DayFog, Mathf.Clamp01((SunHeight + 0.1f) / 0.3f));
        }
    }
}
