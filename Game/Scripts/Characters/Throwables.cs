using System;
using System.Collections.Generic;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

public enum EThrowable : byte
{
    Grenade,
    C4,
    Beacon,
    Flare,
}

public sealed class ThrowableSystem
{
    private sealed class FThrown
    {
        public Entity Handle;
        public EThrowable Kind;
        public IDamageable? Thrower;
        public float Age;
        public float Fuse;
        public float StillTime;
        public bool bStuck;
        public bool bLanded;
        public ESupportKind Support;
        public float PuffTimer;
        public FVector3 LandedAt;
        public float LandedTime;
        public float BeepTimer;
    }

    public const float Gravity = 9.81f;
    private readonly List<FThrown> Items = new();

    public int ActiveCharges
    {
        get
        {
            int Count = 0;
            foreach (FThrown Item in Items)
            {
                if (Item.Kind == EThrowable.C4)
                {
                    ++Count;
                }
            }

            return Count;
        }
    }

    public static FVector3 LobVelocity(FVector3 From, FVector3 To, float Speed)
    {
        FVector3 Flat = Geo.Flat(To - From);
        float Distance = MathF.Max(Flat.Length, 0.5f);
        float Time = Math.Clamp(Distance / Speed, 0.5f, 2.2f);
        float Rise = To.Y - From.Y;
        float VerticalSpeed = (Rise + 0.5f * Gravity * Time * Time) / Time;
        return Flat / Distance * (Distance / Time) + new FVector3(0.0f, VerticalSpeed, 0.0f);
    }

    public Entity Throw(EThrowable Kind, FVector3 From, FVector3 Velocity, IDamageable? Thrower, ESupportKind Support = ESupportKind.None)
    {
        CWorld World = Mercs.World;
        EntityRegistry Registry = World.Registry;
        float Radius = Kind == EThrowable.C4 ? 0.14f : 0.1f;

        Entity Handle = World.CreateEntity(Kind.ToString(), From);
        MeshKit Kit = new();
        bool bGlow = false;
        switch (Kind)
        {
            case EThrowable.Grenade:
                Kit.Sphere(FVector3.Zero, Radius, Palette.Hex(0x3E4A2A), 6);
                break;
            case EThrowable.C4:
                Kit.Box(FVector3.Zero, new FVector3(0.14f, 0.07f, 0.1f), Palette.Hex(0xCFC6A5));
                Kit.Box(new FVector3(0.0f, 0.075f, 0.0f), new FVector3(0.04f, 0.01f, 0.04f), Palette.Rgb(1.0f, 0.1f, 0.05f));
                break;
            case EThrowable.Beacon:
                Kit.Tube(new FVector3(0, -0.12f, 0), new FVector3(0, 0.12f, 0), Radius, Radius, SupportSystem.BeaconColor(Support), 6);
                bGlow = true;
                break;
            case EThrowable.Flare:
                Kit.Tube(new FVector3(0, -0.12f, 0), new FVector3(0, 0.12f, 0), Radius, Radius, Palette.Rgb(0.2f, 1.0f, 0.3f), 6);
                bGlow = true;
                break;
        }

        Kit.Commit(Registry, Handle, bGlow, false, false);

        using (new FPhysicsBatchScope(World))
        {
            SSphereColliderComponent Collider = Registry.GetOrAdd<SSphereColliderComponent>(Handle)!;
            Collider.Radius = Radius;
            Collider.bAffectsNavigation = false;
            SRigidBodyComponent Body = Registry.GetOrAdd<SRigidBodyComponent>(Handle)!;
            Body.BodyType = EBodyType.Dynamic;
            Body.Mass = 0.6f;
            Body.bOverrideMass = true;
            Body.bUseContinuousCollision = true;
            Body.LinearDamping = 0.1f;
            Body.AngularDamping = 0.6f;
            Body.FrictionOverride = Kind == EThrowable.C4 ? 1.0f : 0.6f;
            Body.RestitutionOverride = Kind == EThrowable.C4 ? 0.0f : 0.3f;
        }

        CPhysicsLibrary.SetLinearVelocity(World, Handle, Velocity);

        Items.Add(new FThrown
        {
            Handle = Handle,
            Kind = Kind,
            Thrower = Thrower,
            Fuse = Kind == EThrowable.Grenade ? 2.6f : 999.0f,
            Support = Support,
        });
        return Handle;
    }

    public void DetonateCharges(IDamageable? Owner)
    {
        bool bAny = false;
        for (int Index = Items.Count - 1; Index >= 0; --Index)
        {
            FThrown Item = Items[Index];
            if (Item.Kind != EThrowable.C4 || (Owner is not null && Item.Thrower != Owner))
            {
                continue;
            }

            FVector3 At = Location(Item);
            Remove(Index);
            Explosion.Detonate(At, 7.5f, 650.0f, Owner, 2.6f);
            bAny = true;
        }

        if (!bAny)
        {
            Mercs.Feed.Post("No charges placed.", ENewsTone.Neutral);
        }
    }

    private static FVector3 Location(FThrown Item) => Mercs.World.IsValidEntity(Item.Handle) ? Mercs.World.GetEntityLocation(Item.Handle) : FVector3.Zero;

    private void Remove(int Index)
    {
        FThrown Item = Items[Index];
        Items.RemoveAt(Index);
        if (Mercs.World.IsValidEntity(Item.Handle))
        {
            Mercs.World.DestroyEntity(Item.Handle);
        }
    }

    public void Update(float DeltaTime)
    {
        for (int Index = Items.Count - 1; Index >= 0; --Index)
        {
            FThrown Item = Items[Index];
            if (!Mercs.World.IsValidEntity(Item.Handle))
            {
                Items.RemoveAt(Index);
                continue;
            }

            Item.Age += DeltaTime;
            FVector3 At = Mercs.World.GetEntityLocation(Item.Handle);

            if (At.Y < Terrain.SeaLevel - 3.0f || Item.Age > 600.0f)
            {
                Remove(Index);
                continue;
            }

            switch (Item.Kind)
            {
                case EThrowable.Grenade:
                    Item.Fuse -= DeltaTime;
                    if (Item.Fuse <= 0.0f)
                    {
                        Remove(Index);
                        Explosion.Detonate(At + new FVector3(0.0f, 0.3f, 0.0f), 6.0f, 260.0f, Item.Thrower, 0.8f);
                    }

                    break;
                case EThrowable.C4:
                    TickCharge(Item, At);
                    Item.BeepTimer -= DeltaTime;
                    if (Item.BeepTimer <= 0.0f)
                    {
                        Item.BeepTimer = 1.4f;
                        Sfx.At(ESfx.C4Beep, At, 0.3f, 25.0f, 1.5f, 0.0f);
                    }

                    break;
                case EThrowable.Beacon:
                case EThrowable.Flare:
                    if (TickBeacon(Item, At, DeltaTime))
                    {
                        Remove(Index);
                    }

                    break;
            }
        }
    }

    private void TickCharge(FThrown Item, FVector3 At)
    {
        if (Item.bStuck)
        {
            return;
        }

        foreach (Vehicle Ride in Mercs.Vehicles)
        {
            if (!Ride.IsAlive || FVector3.Distance(Ride.Position, At) > Ride.Radius + 0.4f)
            {
                continue;
            }

            Item.bStuck = true;
            Mercs.World.Registry.Remove<SRigidBodyComponent>(Item.Handle);
            Mercs.World.Registry.Remove<SSphereColliderComponent>(Item.Handle);
            Mercs.World.SetParent(Item.Handle, Ride.Owner);
            Sfx.At(ESfx.C4Plant, At, 0.5f, 25.0f, 2.0f);
            Mercs.Feed.Post("Charge stuck to vehicle.", ENewsTone.Neutral);
            return;
        }
    }

    private bool TickBeacon(FThrown Item, FVector3 At, float DeltaTime)
    {
        Item.PuffTimer -= DeltaTime;
        if (Item.PuffTimer <= 0.0f)
        {
            Item.PuffTimer = 0.12f;
            Mercs.Fx.Puff(At + new FVector3(0.0f, 0.4f, 0.0f), Mercs.Range(1.0f, 1.8f), 3.5f, false, new FVector3(Mercs.Range(-0.3f, 0.3f), 2.4f, Mercs.Range(-0.3f, 0.3f)));
        }

        if (!Item.bLanded)
        {
            FVector3 Velocity = CPhysicsLibrary.GetLinearVelocity(Mercs.World, Item.Handle);
            Item.StillTime = Velocity.Length < 0.6f ? Item.StillTime + DeltaTime : 0.0f;
            if (Item.StillTime > 0.4f || Item.Age > 4.0f)
            {
                Item.bLanded = true;
                Item.LandedAt = At;
                Sfx.At(ESfx.FlarePop, At, 0.5f, 60.0f, 3.0f);
                if (Item.Kind == EThrowable.Flare)
                {
                    Mercs.Support.OnFlareLanded(At);
                }
                else
                {
                    Mercs.Support.OnBeaconLanded(Item.Support, At);
                }
            }

            return false;
        }

        Item.LandedTime += DeltaTime;
        return Item.LandedTime > 25.0f;
    }
}

public static class Throwables
{
    public static FVector3 LobVelocity(FVector3 From, FVector3 To, float Speed) => ThrowableSystem.LobVelocity(From, To, Speed);

    public static Entity Throw(EThrowable Kind, FVector3 From, FVector3 Velocity, IDamageable? Thrower, ESupportKind Support = ESupportKind.None)
    {
        Sfx.At(ESfx.Throw, From, 0.4f, 25.0f, 2.0f, 0.1f);
        return Mercs.Throwables.Throw(Kind, From, Velocity, Thrower, Support);
    }
}
