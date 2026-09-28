using System;
using Lumina;

namespace Mercenaries;

// Overlapping impacts add only what exceeds the shake already running, since engine shakes sum and a chain of blasts would stack into a blur.
public static class CameraShake
{
    private const float MinimumExtra = 0.05f;

    private static float ActiveIntensity;
    private static float ActiveUntil;

    public static void Impact(float Intensity, float Duration)
    {
        float Running = Mercs.Time < ActiveUntil ? ActiveIntensity : 0.0f;
        float Extra = Intensity - Running;
        if (Extra < MinimumExtra)
        {
            return;
        }

        CCameraLibrary.PlayImpactShake(Mercs.World, Extra, Duration);
        ActiveIntensity = Intensity;
        ActiveUntil = MathF.Max(ActiveUntil, Mercs.Time + Duration);
    }
}
