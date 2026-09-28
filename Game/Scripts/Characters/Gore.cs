using System;
using System.Collections.Generic;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

// Blood and dismemberment. A death hands every limb to physics, jointed at the torso, and a hard enough hit tears joints or blows limbs apart.
public static class Gore
{
    private const float BleedTime = 4.0f;
    private const float StumpRate = 70.0f;
    private const float LimbRate = 30.0f;
    private const float PoolDelay = 1.2f;
    private const float PoolGrowTime = 6.0f;
    private const float PoolStartSize = 0.3f;
    private const float GibThreshold = 150.0f;
    private const float SeverThreshold = 60.0f;
    private const float HeadPopThreshold = 55.0f;
    private const float LimbSeverThreshold = 40.0f;
    private const float CrushGibThreshold = 900.0f;
    private const float PruneInterval = 1.0f;
    private const float DripInterval = 0.2f;

    // Kilograms per part, in EBodyPart order.
    private static readonly float[] PartMass = { 30.0f, 5.0f, 4.0f, 4.0f, 10.0f, 10.0f, 3.0f };

    private sealed class FBleeder
    {
        public Entity Part;
        public FVector3 Point;
        public FVector3 Direction;
        public float Age;
        public float Life;
        public float Rate;
        public float Owed;
        public float DripTimer;
    }

    private sealed class FPool
    {
        public Entity Anchor;
        public Entity Decal = Entity.Null;
        public float Age;
        public float MaxSize;
    }

    private static readonly HashSet<uint> Gibs = new();
    private static readonly List<FBleeder> Bleeders = new();
    private static readonly List<FPool> Pools = new();
    private static readonly List<uint> Stale = new();
    private static float PruneTimer;

    public static bool IsGib(Entity Candidate) => Gibs.Contains(Candidate.Id);

    public static void Reset()
    {
        Gibs.Clear();
        Bleeders.Clear();
        Pools.Clear();
    }

    // An entry wound mists back toward the shooter, the exit sprays on through, and a wall close behind takes the splatter.
    public static void Wound(FVector3 Point, FVector3 Direction, float Damage, Entity Victim)
    {
        float Strength = Mathf.Clamp01(Damage / 40.0f);
        Mercs.Fx.BloodMist(Point - Direction * 0.05f, -Direction * 0.6f, 1 + (int)(Strength * 2.0f));
        Mercs.Fx.BloodSpray(Point, Direction, 6 + (int)(Strength * 16.0f), 2.5f, 6.0f + Strength * 3.0f, 0.35f);
        Mercs.Fx.BloodSpray(Point, (-Direction + FVector3.Up * 0.4f).NormalizedOr(FVector3.Up), 3, 1.0f, 2.5f, 0.5f);
        SplatterBehind(Point, Direction, Victim, 0.5f + Strength * 0.8f);
        SprayDecals(Point, Direction, 2 + (int)(Strength * 3.0f), 2.5f, 0.2f, 0.45f, Victim);
    }

    // A round into a limb already lying on the ground still bleeds and shoves it.
    public static void HitGib(Entity Gib, FVector3 Point, FVector3 Direction, float Damage)
    {
        Mercs.Fx.BloodSpray(Point, (Direction + FVector3.Up * 0.5f).NormalizedOr(FVector3.Up), 8, 1.5f, 4.0f, 0.5f);
        Mercs.Fx.BloodMist(Point, FVector3.Zero, 1);
        SprayDecals(Point, Direction, 2, 1.5f, 0.15f, 0.35f, Gib);
        CPhysicsLibrary.AddImpulseAtPosition(Mercs.World, Gib, Direction * MathF.Min(Damage, 60.0f) * 1.5f, Point);
    }

    // Replaces the living body with jointed physics limbs and returns the torso, or whatever is left when the torso itself was blown apart.
    public static Entity Kill(HumanoidRig Rig, in FHit Hit, FVector3 Carry, float Lifetime, float Burst, bool bKeepWeapon)
    {
        CWorld World = Mercs.World;
        if (Rig.Body.IsNull || !World.Registry.Valid(Rig.Body) || World.Registry.TryGet<STransformComponent>(Rig.Body) is not { } BodyTransform)
        {
            return Entity.Null;
        }

        FTransform Pose = BodyTransform.GetWorldTransform();
        EBodyPart Struck = Rig.PartNearest(ToBody(Pose, Hit.Point));
        float Force = MathF.Max(Hit.Amount, Burst);

        bool[] Severed = new bool[HumanoidRig.PartCount];
        bool[] Destroyed = new bool[HumanoidRig.PartCount];
        Decide(Hit.Kind, Force, Struck, Severed, Destroyed);

        Entity[] Gib = new Entity[HumanoidRig.PartCount];
        Array.Fill(Gib, Entity.Null);
        for (int Index = 0; Index < HumanoidRig.PartCount; ++Index)
        {
            if (Rig.Meshes[Index] is null || (Index == (int)EBodyPart.Weapon && !bKeepWeapon))
            {
                continue;
            }

            if (Destroyed[Index])
            {
                Explode(ToWorld(Pose, Rig.Centers[Index]), Hit.Direction, Index == (int)EBodyPart.Torso ? 1.0f : 0.5f);
                continue;
            }

            Gib[Index] = SpawnGib(Rig, Index, Pose, Lifetime);
        }

        Entity Torso = Gib[(int)EBodyPart.Torso];
        foreach ((EBodyPart Limb, FVector3 Joint, float Swing) in HumanoidRig.Joints)
        {
            Entity Piece = Gib[(int)Limb];
            FVector3 Pivot = ToWorld(Pose, Joint);
            if (!Torso.IsNull && !Piece.IsNull && !Severed[(int)Limb])
            {
                FVector3 Hang = (ToWorld(Pose, Rig.Centers[(int)Limb]) - Pivot).NormalizedOr(-FVector3.Up);
                CPhysicsLibrary.CreateConeConstraint(World, Torso, Piece, Pivot, Hang, Mathf.Radians(Swing));
                continue;
            }

            if (!Severed[(int)Limb] && !Destroyed[(int)Limb])
            {
                continue;
            }

            // Both ends of a torn joint bleed, the stump harder and for longer than the limb it lost.
            float Loss = Limb == EBodyPart.Head ? 1.4f : 1.0f;
            if (!Torso.IsNull)
            {
                AddBleeder(Torso, Joint, Joint - Rig.Centers[(int)EBodyPart.Torso], BleedTime * Loss, StumpRate * Loss);
            }
            if (!Piece.IsNull)
            {
                AddBleeder(Piece, Joint, Joint - Rig.Centers[(int)Limb], BleedTime * 0.6f, LimbRate);
            }
            Mercs.Fx.BloodSpray(Pivot, FVector3.Up, 18, 2.0f, 6.0f, 0.8f, Carry);
            Mercs.Fx.BloodMist(Pivot, Carry, 2);
            SprayDecals(Pivot, FVector3.Up, 5, 3.0f, 0.2f, 0.5f, Entity.Null);
        }

        Launch(Rig, Pose, Gib, Severed, Hit, Force, Struck, Carry);

        if (Hit.Kind == EDamageKind.Explosive && Force >= SeverThreshold)
        {
            Splash(Pose.Location, Mathf.Clamp01(Force / (GibThreshold * 2.0f)));
        }

        Entity Anchor = !Torso.IsNull ? Torso : Array.Find(Gib, Piece => !Piece.IsNull);
        if (!Anchor.IsNull)
        {
            int Lost = 0;
            for (int Index = 0; Index < HumanoidRig.PartCount; ++Index)
            {
                Lost += Severed[Index] || Destroyed[Index] ? 1 : 0;
            }
            Pools.Add(new FPool { Anchor = Anchor, MaxSize = 1.4f + Lost * 0.35f });
        }

        World.DestroyEntity(Rig.Body);
        Rig.Body = Entity.Null;
        return Anchor;
    }

    // How badly this hit takes the body apart, from a limp fall to a spray of pieces.
    private static void Decide(EDamageKind Kind, float Force, EBodyPart Struck, bool[] Severed, bool[] Destroyed)
    {
        EBodyPart[] Limbs = { EBodyPart.Head, EBodyPart.ArmR, EBodyPart.ArmL, EBodyPart.LegR, EBodyPart.LegL };
        bool bLimb = Struck is not (EBodyPart.Torso or EBodyPart.Weapon);

        switch (Kind)
        {
            case EDamageKind.Explosive when Force >= GibThreshold:
            case EDamageKind.Crush when Force >= CrushGibThreshold:
                foreach (EBodyPart Limb in Limbs)
                {
                    Severed[(int)Limb] = true;
                    Destroyed[(int)Limb] = Mercs.Chance(Limb == EBodyPart.Head ? 0.5f : 0.25f);
                }
                Destroyed[(int)EBodyPart.Torso] = Kind == EDamageKind.Explosive && (Force >= GibThreshold * 1.6f || Mercs.Chance(0.35f));
                break;
            case EDamageKind.Explosive when Force >= SeverThreshold:
            case EDamageKind.Crush:
                for (int Count = Mercs.RangeInt(1, 4); Count > 0; --Count)
                {
                    Severed[(int)Limbs[Mercs.RangeInt(0, Limbs.Length)]] = true;
                }
                break;
            case EDamageKind.Bullet when Struck == EBodyPart.Head && Force >= HeadPopThreshold:
                Destroyed[(int)EBodyPart.Head] = true;
                break;
            case EDamageKind.Bullet when Struck == EBodyPart.Head && Force >= 30.0f:
                Severed[(int)EBodyPart.Head] = Mercs.Chance(0.35f);
                break;
            case EDamageKind.Bullet when bLimb && Force >= LimbSeverThreshold:
                Severed[(int)Struck] = Mercs.Chance(0.7f);
                break;
        }
    }

    private static Entity SpawnGib(HumanoidRig Rig, int Index, FTransform Pose, float Lifetime)
    {
        CWorld World = Mercs.World;
        Entity Gib = World.CreateEntity("Gib", Pose);
        MeshKit.Show(World.Registry, Gib, Rig.Meshes[Index], true, false);

        using (new FPhysicsBatchScope(World))
        {
            SBoxColliderComponent Collider = World.Registry.GetOrAdd<SBoxColliderComponent>(Gib)!;
            Collider.HalfExtent = Rig.Halves[Index];
            Collider.TranslationOffset = Rig.Centers[Index];
            Collider.bAffectsNavigation = false;

            SRigidBodyComponent Body = World.Registry.GetOrAdd<SRigidBodyComponent>(Gib)!;
            Body.BodyType = EBodyType.Dynamic;
            Body.Mass = PartMass[Index] * Rig.Scale * Rig.Scale * Rig.Scale;
            Body.bOverrideMass = true;
            Body.bUseGravity = true;
            Body.bUseContinuousCollision = true;
            Body.LinearDamping = 0.05f;
            Body.AngularDamping = 0.3f;
            Body.FrictionOverride = 0.8f;
            Body.RestitutionOverride = 0.05f;
        }

        World.SetLifetime(Gib, Lifetime);
        Gibs.Add(Gib.Id);
        return Gib;
    }

    private static void Launch(HumanoidRig Rig, FTransform Pose, Entity[] Gib, bool[] Severed, in FHit Hit, float Force, EBodyPart Struck, FVector3 Carry)
    {
        float BlastSpeed = Math.Clamp(Force * 0.04f, 3.0f, 18.0f);
        for (int Index = 0; Index < HumanoidRig.PartCount; ++Index)
        {
            if (Gib[Index].IsNull)
            {
                continue;
            }

            FVector3 Center = ToWorld(Pose, Rig.Centers[Index]);
            FVector3 Velocity = Carry;
            FVector3 Spin = new(Mercs.Range(-1.0f, 1.0f), Mercs.Range(-1.0f, 1.0f), Mercs.Range(-1.0f, 1.0f));
            switch (Hit.Kind)
            {
                case EDamageKind.Explosive:
                    Velocity += (Center - Hit.Point + FVector3.Up * 0.6f).NormalizedOr(FVector3.Up) * BlastSpeed * Mercs.Range(0.7f, 1.2f) * (Severed[Index] ? 1.2f : 0.8f);
                    Spin *= BlastSpeed * 1.5f;
                    break;
                case EDamageKind.Crush:
                    Velocity += Hit.Direction * 6.0f + FVector3.Up * 3.0f;
                    Spin *= 8.0f;
                    break;
                default:
                    Velocity += Hit.Direction * (Index == (int)Struck ? Math.Clamp(Force * 0.08f, 1.5f, 7.0f) : 1.0f);
                    Spin *= Severed[Index] ? 6.0f : 1.5f;
                    break;
            }

            CPhysicsLibrary.SetLinearVelocity(Mercs.World, Gib[Index], Velocity);
            CPhysicsLibrary.SetAngularVelocity(Mercs.World, Gib[Index], Spin);
        }
    }

    // A part blown to nothing leaves a cloud, a fan of chunks and a splash on whatever it was standing over.
    private static void Explode(FVector3 Center, FVector3 Push, float Size)
    {
        FVector3 Out = (Push + FVector3.Up).NormalizedOr(FVector3.Up);
        Mercs.Fx.BloodMist(Center, Push * 0.5f, 3 + (int)(Size * 5.0f));
        Mercs.Fx.BloodSpray(Center, Out, 30 + (int)(Size * 60.0f), 3.0f, 10.0f, 1.0f);
        Mercs.Fx.GoreChunks(Center, Out, 6 + (int)(Size * 14.0f), 9.0f);
        SprayDecals(Center, Out, 8 + (int)(Size * 10.0f), 5.0f, 0.25f, 0.7f, Entity.Null);
        if (Geo.Trace(Center + FVector3.Up * 0.3f, Center - FVector3.Up * 3.0f, Entity.Null, Entity.Null) is { bHit: true } Ground)
        {
            Mercs.Fx.BloodDecal(Ground.Location, Ground.Normal, 1.2f + Size * 1.4f, Mercs.Chance(0.5f) ? EBloodDecal.Splatter : EBloodDecal.SplatterAlt);
        }
    }

    // Walls and ground around a blast take the spatter, found by a ring of rays at chest height.
    private static void Splash(FVector3 Center, float Strength)
    {
        FVector3 From = Center + FVector3.Up * 0.8f;
        int Rays = 6 + (int)(Strength * 6.0f);
        for (int Index = 0; Index < Rays; ++Index)
        {
            float Angle = (Index + Mercs.Range(0.0f, 0.8f)) / Rays * MathF.Tau;
            FVector3 Direction = new FVector3(MathF.Cos(Angle), Mercs.Range(-0.6f, 0.1f), MathF.Sin(Angle)).Normalized();
            SRayResult Hit = Geo.Trace(From, From + Direction * (3.0f + Strength * 3.0f), Entity.Null, Entity.Null);
            if (Hit.bHit && !IsGib(new Entity(Hit.Entity)) && Mercs.FindDamageable(new Entity(Hit.Entity)) is null or Structure)
            {
                Mercs.Fx.BloodDecal(Hit.Location, Hit.Normal, Mercs.Range(0.8f, 1.6f) * (0.6f + Strength), Mercs.Chance(0.5f) ? EBloodDecal.Spray : EBloodDecal.Splatter);
            }
        }
    }

    // Traces where a spray's drops come down, so the marks lie on the ground and walls the drops actually reach.
    private static void SprayDecals(FVector3 Point, FVector3 Direction, int Count, float Reach, float MinSize, float MaxSize, Entity Ignore)
    {
        for (int Index = 0; Index < Count; ++Index)
        {
            FVector3 Flight = new FVector3(Direction.X, 0.0f, Direction.Z) * Mercs.Range(0.2f, 1.0f) + new FVector3(Mercs.Range(-0.5f, 0.5f), -1.0f, Mercs.Range(-0.5f, 0.5f));
            SRayResult Hit = Geo.Trace(Point, Point + Flight.NormalizedOr(-FVector3.Up) * Reach, Ignore, Entity.Null);
            if (Hit.bHit && !IsGib(new Entity(Hit.Entity)) && Mercs.FindDamageable(new Entity(Hit.Entity)) is null or Structure)
            {
                Mercs.Fx.BloodDecal(Hit.Location, Hit.Normal, Mercs.Range(MinSize, MaxSize), Mercs.Chance(0.5f) ? EBloodDecal.Splatter : EBloodDecal.SplatterAlt);
            }
        }
    }

    private static void SplatterBehind(FVector3 Point, FVector3 Direction, Entity Victim, float Size)
    {
        SRayResult Hit = Geo.Trace(Point, Point + Direction * 3.0f, Victim, Entity.Null);
        if (Hit.bHit && !IsGib(new Entity(Hit.Entity)) && Mercs.FindDamageable(new Entity(Hit.Entity)) is null or Structure)
        {
            Mercs.Fx.BloodDecal(Hit.Location, Hit.Normal, Size * Mercs.Range(0.8f, 1.3f), EBloodDecal.Spray);
        }
    }

    private static void AddBleeder(Entity Part, FVector3 BodyPoint, FVector3 BodyDirection, float Life, float Rate)
    {
        Bleeders.Add(new FBleeder { Part = Part, Point = BodyPoint, Direction = BodyDirection.NormalizedOr(FVector3.Up), Life = Life, Rate = Rate });
    }

    public static void Update(float DeltaTime)
    {
        EntityRegistry Registry = Mercs.World.Registry;

        for (int Index = Bleeders.Count - 1; Index >= 0; --Index)
        {
            FBleeder Bleeder = Bleeders[Index];
            Bleeder.Age += DeltaTime;
            if (Bleeder.Age >= Bleeder.Life || !Registry.Valid(Bleeder.Part) || Registry.TryGet<STransformComponent>(Bleeder.Part) is not { } Transform)
            {
                Bleeders.RemoveAt(Index);
                continue;
            }

            // Spurts in time with a failing pulse, weakening until it stops.
            float Pressure = 1.0f - Bleeder.Age / Bleeder.Life;
            float Pulse = 0.55f + 0.45f * MathF.Sin(Bleeder.Age * 9.0f);
            Bleeder.Owed += Bleeder.Rate * Pressure * Pulse * DeltaTime;
            int Count = (int)Bleeder.Owed;
            if (Count <= 0)
            {
                continue;
            }

            Bleeder.Owed -= Count;
            FTransform Pose = Transform.GetWorldTransform();
            FVector3 Direction = Pose.Rotation.Rotate(Bleeder.Direction);
            FVector3 Stump = ToWorld(Pose, Bleeder.Point);
            Mercs.Fx.BloodSpray(Stump, Direction, Count, 0.6f + 2.0f * Pressure, 1.5f + 3.5f * Pressure, 0.3f);

            // A tumbling limb leaves a trail of drips along the ground it crosses.
            Bleeder.DripTimer -= DeltaTime;
            if (Bleeder.DripTimer <= 0.0f)
            {
                Bleeder.DripTimer = DripInterval;
                SprayDecals(Stump, Direction, 1, 2.5f, 0.15f, 0.25f + 0.25f * Pressure, Bleeder.Part);
            }
        }

        for (int Index = Pools.Count - 1; Index >= 0; --Index)
        {
            FPool Pool = Pools[Index];
            Pool.Age += DeltaTime;
            if (Pool.Age < PoolDelay)
            {
                continue;
            }

            if (Pool.Decal.IsNull)
            {
                if (!Registry.Valid(Pool.Anchor))
                {
                    Pools.RemoveAt(Index);
                    continue;
                }

                FVector3 Body = Mercs.World.GetEntityLocation(Pool.Anchor) + FVector3.Up * 0.6f;
                SRayResult Ground = Geo.Trace(Body, Body - FVector3.Up * 3.0f, Pool.Anchor, Entity.Null);
                Pool.Decal = Ground.bHit ? Mercs.Fx.BloodDecal(Ground.Location, Ground.Normal, PoolStartSize, EBloodDecal.Pool) : Entity.Null;
                if (Pool.Decal.IsNull)
                {
                    Pools.RemoveAt(Index);
                }
                continue;
            }

            float Grown = Mathf.Clamp01((Pool.Age - PoolDelay) / PoolGrowTime);
            if (!Registry.Valid(Pool.Decal) || Registry.TryGet<SDecalComponent>(Pool.Decal) is not { } Decal)
            {
                Pools.RemoveAt(Index);
                continue;
            }

            float Size = PoolStartSize + (Pool.MaxSize - PoolStartSize) * (1.0f - (1.0f - Grown) * (1.0f - Grown));
            Decal.Size = new FVector3(Size, Size, Decal.Size.Z);
            if (Grown >= 1.0f)
            {
                Pools.RemoveAt(Index);
            }
        }

        PruneTimer -= DeltaTime;
        if (PruneTimer <= 0.0f)
        {
            PruneTimer = PruneInterval;
            Stale.Clear();
            foreach (uint Id in Gibs)
            {
                if (!Registry.Valid(new Entity(Id)))
                {
                    Stale.Add(Id);
                }
            }
            foreach (uint Id in Stale)
            {
                Gibs.Remove(Id);
            }
        }
    }

    private static FVector3 ToWorld(FTransform Pose, FVector3 BodyPoint)
        => Pose.Location + Pose.Rotation.Rotate(new FVector3(BodyPoint.X * Pose.Scale.X, BodyPoint.Y * Pose.Scale.Y, BodyPoint.Z * Pose.Scale.Z));

    private static FVector3 ToBody(FTransform Pose, FVector3 WorldPoint)
    {
        FVector3 Local = Pose.Rotation.Inverse().Rotate(WorldPoint - Pose.Location);
        return new FVector3(Local.X / Pose.Scale.X, Local.Y / Pose.Scale.Y, Local.Z / Pose.Scale.Z);
    }
}
