using System;
using System.Collections.Generic;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

public enum EOrdnanceLook : byte
{
    Rocket,
    Grenade,
    Shell,
    Bomb,
    Bomblet,
}

public static class Explosion
{
    public static void Detonate(FVector3 At, float Radius, float Damage, IDamageable? Attacker, float StructureMultiplier = 1.0f, bool bFx = true, IDamageable? Spare = null)
    {
        if (bFx)
        {
            Mercs.Fx.Explosion(At, Radius);
            Sfx.Explosion(At, Radius);
        }

        Mercs.Destruction.OnExplosion(At, Radius);

        List<IDamageable> Victims = new();
        foreach (IDamageable Candidate in Mercs.Damageables.Values)
        {
            if (!Candidate.IsAlive || Candidate == Spare || Candidate is MercPlayer { CurrentVehicle: not null } || (Candidate is Vehicle && Candidate == Attacker))
            {
                continue;
            }

            float Distance = MathF.Max(0.0f, FVector3.Distance(Candidate.Position, At) - Candidate.Radius);
            if (Distance < Radius)
            {
                Victims.Add(Candidate);
            }
        }

        foreach (IDamageable Victim in Victims)
        {
            float Distance = MathF.Max(0.0f, FVector3.Distance(Victim.Position, At) - Victim.Radius);
            float Falloff = 1.0f - Distance / Radius;
            float Amount = Damage * (0.2f + 0.8f * Falloff * Falloff);
            if (Victim is Structure)
            {
                Amount *= StructureMultiplier;
            }

            bool bAiBlast = Attacker is not null && !Attacker.IsPlayerControlled;
            if (bAiBlast && Victim is MercPlayer)
            {
                Amount *= 0.45f;
            }

            if (bAiBlast && Victim is Soldier && Victim.Faction == Attacker!.Faction)
            {
                continue;
            }

            FVector3 Away = (Victim.Position - At + new FVector3(0.0f, 0.5f, 0.0f)).NormalizedOr(FVector3.Up);
            FHit Hit = FHit.From(Attacker, Amount, EDamageKind.Explosive, Victim.Position, Away);
            Victim.TakeHit(Hit);
        }

        PushDebris(At, Radius);
        ShakeCamera(At, Radius);

        foreach (Soldier Listener in Mercs.Soldiers)
        {
            if (Listener.IsAlive && FVector3.DistanceSquared(Listener.Position, At) < Radius * Radius * 64.0f && Attacker is not null)
            {
                Listener.HearNoise(At, Attacker);
            }
        }

        if (Attacker is not null && Attacker.IsPlayerControlled)
        {
            Mercs.Factions.RaiseSuspicion(1.0f);
        }
    }

    private static void PushDebris(FVector3 At, float Radius)
    {
        Entity[] Nearby = CPhysicsLibrary.OverlapSphere(Mercs.World, At, Radius, Entity.Null);
        foreach (Entity Body in Nearby)
        {
            if (Mercs.FindDamageable(Body) is not null)
            {
                continue;
            }

            if (!Mercs.World.Registry.Has<SRigidBodyComponent>(Body))
            {
                continue;
            }

            SRigidBodyComponent Rigid = Mercs.World.Registry.Get<SRigidBodyComponent>(Body);
            if (Rigid.BodyType != EBodyType.Dynamic)
            {
                continue;
            }

            FVector3 Offset = Mercs.World.GetEntityLocation(Body) - At;
            float Strength = (1.0f - Mathf.Clamp01(Offset.Length / Radius)) * 12.0f;
            CPhysicsLibrary.AddImpulse(Mercs.World, Body, (Offset.NormalizedOr(FVector3.Up) + FVector3.Up) * Strength * MathF.Max(1.0f, Rigid.Mass * 0.5f));
        }
    }

    private static void ShakeCamera(FVector3 At, float Radius)
    {
        FVector3 Listener = Mercs.PlayerPosition;
        float Distance = FVector3.Distance(Listener, At);
        float Reach = Radius * 9.0f;
        if (Distance > Reach)
        {
            return;
        }

        float Intensity = (1.0f - Distance / Reach) * MathF.Min(2.0f, Radius * 0.18f);
        CCameraLibrary.PlayImpactShake(Mercs.World, Intensity, 0.35f + Radius * 0.02f);
    }
}

// Pooled projectiles, advanced by one script-side sweep per frame instead of an entity script each.
public sealed class OrdnanceSystem
{
    private sealed class FShot
    {
        public Entity Handle;
        public EOrdnanceLook Look;
        public bool bActive;
        public FVector3 Position;
        public FVector3 Velocity;
        public float Gravity;
        public float Radius;
        public float Damage;
        public float StructureMultiplier;
        public float Life;
        public float TrailTimer;
        public IDamageable? Shooter;
        public Entity IgnoreExtra;
        public Action<FVector3>? OnImpact;
    }

    private static readonly FVector3 Hidden = new(0.0f, -620.0f, 0.0f);
    private readonly List<FShot> Shots = new();

    public void Initialize()
    {
        CreatePool(EOrdnanceLook.Rocket, 24);
        CreatePool(EOrdnanceLook.Grenade, 16);
        CreatePool(EOrdnanceLook.Shell, 32);
        CreatePool(EOrdnanceLook.Bomb, 24);
        CreatePool(EOrdnanceLook.Bomblet, 32);
    }

    private void CreatePool(EOrdnanceLook Look, int Count)
    {
        MeshKit Kit = new();
        bool bGlow = false;
        switch (Look)
        {
            case EOrdnanceLook.Rocket:
                Kit.Tube(new FVector3(0, 0, -0.4f), new FVector3(0, 0, 0.3f), 0.07f, 0.07f, Palette.Hex(0x4A5234), 6);
                Kit.Tube(new FVector3(0, 0, 0.3f), new FVector3(0, 0, 0.5f), 0.07f, 0.0f, Palette.Hex(0x3A3A3A), 6);
                Kit.Box(new FVector3(0, 0, -0.5f), new FVector3(0.05f, 0.05f, 0.1f), Palette.FireCore);
                break;
            case EOrdnanceLook.Grenade:
                Kit.Sphere(FVector3.Zero, 0.1f, Palette.Hex(0x3E4A2A), 6);
                break;
            case EOrdnanceLook.Shell:
                Kit.Box(FVector3.Zero, new FVector3(0.12f, 0.12f, 0.6f), Palette.FireCore);
                bGlow = true;
                break;
            case EOrdnanceLook.Bomb:
                Kit.Tube(new FVector3(0, 0, -0.9f), new FVector3(0, 0, 0.7f), 0.28f, 0.28f, Palette.Hex(0x4B5320), 8);
                Kit.Tube(new FVector3(0, 0, 0.7f), new FVector3(0, 0, 1.1f), 0.28f, 0.0f, Palette.Hex(0x4B5320), 8);
                Kit.Box(new FVector3(0, 0, -1.0f), new FVector3(0.4f, 0.03f, 0.15f), Palette.Hex(0x3A3F1A));
                Kit.Box(new FVector3(0, 0, -1.0f), new FVector3(0.03f, 0.4f, 0.15f), Palette.Hex(0x3A3F1A));
                break;
            case EOrdnanceLook.Bomblet:
                Kit.Sphere(FVector3.Zero, 0.15f, Palette.Hex(0xC8A020), 6);
                break;
        }

        CStaticMesh? Mesh = Kit.BuildStaticMesh(Mercs.World, bGlow);
        for (int Index = 0; Index < Count; ++Index)
        {
            Entity Handle = Mercs.World.CreateEntity($"Ordnance_{Look}_{Index}", Hidden);
            MeshKit.Show(Mercs.World.Registry, Handle, Mesh, false);
            Shots.Add(new FShot { Handle = Handle, Look = Look });
        }
    }

    public static void Launch(FVector3 Position, FVector3 Velocity, float Gravity, float Radius, float Damage, IDamageable? Shooter, Entity IgnoreExtra, EOrdnanceLook Look, float StructureMultiplier = 1.0f, Action<FVector3>? OnImpact = null)
    {
        Mercs.Ordnance.Fire(Position, Velocity, Gravity, Radius, Damage, Shooter, IgnoreExtra, Look, StructureMultiplier, OnImpact);
    }

    public void Fire(FVector3 Position, FVector3 Velocity, float Gravity, float Radius, float Damage, IDamageable? Shooter, Entity IgnoreExtra, EOrdnanceLook Look, float StructureMultiplier, Action<FVector3>? OnImpact)
    {
        FShot? Slot = null;
        foreach (FShot Shot in Shots)
        {
            if (Shot.Look == Look && !Shot.bActive)
            {
                Slot = Shot;
                break;
            }
        }

        if (Slot is null)
        {
            foreach (FShot Shot in Shots)
            {
                if (Shot.Look == Look && (Slot is null || Shot.Life < Slot.Life))
                {
                    Slot = Shot;
                }
            }

            if (Slot is null)
            {
                return;
            }

            Impact(Slot, Slot.Position);
        }

        Slot.bActive = true;
        Slot.Position = Position;
        Slot.Velocity = Velocity;
        Slot.Gravity = Gravity;
        Slot.Radius = Radius;
        Slot.Damage = Damage;
        Slot.StructureMultiplier = StructureMultiplier;
        Slot.Shooter = Shooter;
        Slot.IgnoreExtra = IgnoreExtra;
        Slot.Life = 12.0f;
        Slot.TrailTimer = 0.0f;
        Slot.OnImpact = OnImpact;
        Place(Slot);
    }

    private static void Place(FShot Shot)
    {
        FVector3 Direction = Shot.Velocity.NormalizedOr(FVector3.Forward);
        FVector3 Up = MathF.Abs(Direction.Y) > 0.95f ? FVector3.Right : FVector3.Up;
        Mercs.Fx.Stage(Shot.Handle, new FTransform(Shot.Position, FQuat.LookRotation(Direction, Up), FVector3.One));
    }

    private void Impact(FShot Shot, FVector3 At)
    {
        Shot.bActive = false;
        Mercs.Fx.Stage(Shot.Handle, new FTransform(Hidden, FQuat.Identity, FVector3.One));
        if (Shot.OnImpact is not null)
        {
            Shot.OnImpact(At);
            return;
        }

        Explosion.Detonate(At, Shot.Radius, Shot.Damage, Shot.Shooter, Shot.StructureMultiplier);
    }

    public void Update(float DeltaTime)
    {
        foreach (FShot Shot in Shots)
        {
            if (!Shot.bActive)
            {
                continue;
            }

            Shot.Life -= DeltaTime;
            FVector3 From = Shot.Position;
            Shot.Velocity += new FVector3(0.0f, -Shot.Gravity * DeltaTime, 0.0f);
            FVector3 To = From + Shot.Velocity * DeltaTime;

            Entity ShooterEntity = Shot.Shooter?.Owner ?? Entity.Null;
            SRayResult Hit = Geo.Trace(From, To, ShooterEntity, Shot.IgnoreExtra);
            if (Hit.bHit)
            {
                Impact(Shot, Hit.Location + Hit.Normal * 0.2f);
                continue;
            }

            float Ground = Terrain.HeightAt(To.X, To.Z);
            if (To.Y <= Ground || To.Y < Terrain.SeaLevel - 0.3f || Shot.Life <= 0.0f)
            {
                Impact(Shot, new FVector3(To.X, MathF.Max(Ground, Terrain.SeaLevel) + 0.2f, To.Z));
                continue;
            }

            Shot.Position = To;
            Place(Shot);

            if (Shot.Look is EOrdnanceLook.Rocket or EOrdnanceLook.Bomb)
            {
                Shot.TrailTimer -= DeltaTime;
                if (Shot.TrailTimer <= 0.0f)
                {
                    Shot.TrailTimer = Shot.Look == EOrdnanceLook.Rocket ? 0.03f : 0.08f;
                    Mercs.Fx.Puff(From, Shot.Look == EOrdnanceLook.Rocket ? 0.6f : 0.9f, 1.2f, false, FVector3.Zero);
                }
            }
        }
    }
}
