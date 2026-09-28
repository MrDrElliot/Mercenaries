using System;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

public static class Geo
{
    // Yaw 0 faces +Z and positive yaw turns toward +X, matching the character controller.
    public static float YawOf(FVector3 Direction) => Mathf.Degrees(MathF.Atan2(Direction.X, Direction.Z));

    public static FVector3 Heading(float YawDegrees)
    {
        float Radians = Mathf.Radians(YawDegrees);
        return new FVector3(MathF.Sin(Radians), 0.0f, MathF.Cos(Radians));
    }

    public static FVector3 RightOf(float YawDegrees)
    {
        float Radians = Mathf.Radians(YawDegrees);
        return new FVector3(MathF.Cos(Radians), 0.0f, -MathF.Sin(Radians));
    }

    // Pitch is positive when looking up.
    public static FVector3 Direction(float YawDegrees, float PitchDegrees)
    {
        float Yaw = Mathf.Radians(YawDegrees);
        float Pitch = Mathf.Radians(PitchDegrees);
        return new FVector3(MathF.Cos(Pitch) * MathF.Sin(Yaw), MathF.Sin(Pitch), MathF.Cos(Pitch) * MathF.Cos(Yaw));
    }

    public static FQuat YawRotation(float YawDegrees) => FQuat.FromEuler(0.0f, Mathf.Radians(YawDegrees), 0.0f);

    public static FQuat Orientation(float YawDegrees, float PitchDegrees, float RollDegrees)
    {
        FVector3 Forward = Direction(YawDegrees, PitchDegrees);
        FQuat Look = FQuat.LookRotation(Forward, FVector3.Up);
        return RollDegrees == 0.0f ? Look : Look * FQuat.AngleAxis(Mathf.Radians(RollDegrees), FVector3.Forward);
    }

    public static FVector3 Flat(FVector3 V) => new(V.X, 0.0f, V.Z);

    public static float FlatDistance(FVector3 A, FVector3 B)
    {
        float DX = A.X - B.X;
        float DZ = A.Z - B.Z;
        return MathF.Sqrt(DX * DX + DZ * DZ);
    }

    public static FVector3 Ground(float X, float Z) => new(X, Terrain.HeightAt(X, Z), Z);

    public static FVector3 Ground(FVector3 At) => Ground(At.X, At.Z);

    public static FVector3 RandomAround(FVector3 Center, float MinRadius, float MaxRadius)
    {
        float Angle = Mercs.Range(0.0f, Mathf.TwoPi);
        float Distance = Mercs.Range(MinRadius, MaxRadius);
        return Ground(Center.X + MathF.Cos(Angle) * Distance, Center.Z + MathF.Sin(Angle) * Distance);
    }

    public static string CompassName(FVector3 From, FVector3 To)
    {
        float Yaw = YawOf(To - From);
        string[] Names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        int Index = (int)MathF.Round(Mathf.Repeat(Yaw, 360.0f) / 45.0f) % 8;
        return Names[Index];
    }

    public static bool LineOfSight(FVector3 From, FVector3 To, Entity IgnoreA, Entity IgnoreB)
    {
        SRayResult[] Hits = CPhysicsLibrary.RaycastAll(Mercs.World, From, To, IgnoreA, ECollisionProfiles.All);
        foreach (SRayResult Hit in Hits)
        {
            Entity HitEntity = new(Hit.Entity);
            if (HitEntity == IgnoreA || HitEntity == IgnoreB)
            {
                continue;
            }

            IDamageable? Target = Mercs.FindDamageable(HitEntity);
            if (Target is Soldier || Target is MercPlayer)
            {
                continue;
            }

            if (Target is not null && (Target.Owner == IgnoreA || Target.Owner == IgnoreB))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    // The closest hit that is not one of the ignored entities or their children.
    public static SRayResult Trace(FVector3 From, FVector3 To, Entity IgnoreA, Entity IgnoreB)
    {
        SRayResult[] Hits = CPhysicsLibrary.RaycastAll(Mercs.World, From, To, IgnoreA, ECollisionProfiles.All);
        foreach (SRayResult Hit in Hits)
        {
            Entity HitEntity = new(Hit.Entity);
            if (HitEntity == IgnoreA || HitEntity == IgnoreB)
            {
                continue;
            }

            IDamageable? Target = Mercs.FindDamageable(HitEntity);
            if (Target is not null && (Target.Owner == IgnoreA || Target.Owner == IgnoreB))
            {
                continue;
            }

            if (Target is not null && !Target.IsAlive && Target is not Structure)
            {
                continue;
            }

            return Hit;
        }

        return default;
    }
}
