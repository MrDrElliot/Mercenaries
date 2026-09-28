using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

public enum EFxShape : byte
{
    Tracer,
    Fireball,
    Smoke,
    SmokeLight,
    Spark,
}

// Particle systems in /Game/Content/Effects, named P_ plus the member name.
public enum EEffect : byte
{
    Explosion,
    Burning,
    CollapseDust,
    ImpactDirt,
    ImpactSparks,
}

// Pooled throwaway visuals sharing one static mesh per shape, moved with a single bulk transform call per frame.
public sealed class FxSystem
{
    private sealed class FPooled
    {
        public Entity Handle;
        public bool bActive;
        public float Age;
        public float Life;
        public FVector3 Position;
        public FVector3 Velocity;
        public FQuat Rotation = FQuat.Identity;
        public FVector3 StartScale;
        public FVector3 EndScale;
        public float Drag;
    }

    private sealed class FLight
    {
        public Entity Handle;
        public SPointLightComponent? Light;
        public bool bActive;
        public float Age;
        public float Life;
        public float Peak;
    }

    private sealed class FDebris
    {
        public Entity Handle;
        public float Life;
        public FVector3 Scale;
    }

    private static readonly FVector3 Hidden = new(0.0f, -600.0f, 0.0f);
    private const int MaxLights = 10;
    private const int MaxDebris = 90;
    private const string EffectFolder = "/Game/Content/Effects/";
    private const float ExplosionAuthoredRadius = 6.0f;
    private const float CollapseAuthoredWidth = 12.0f;
    private const float FireFadeLife = 10.0f;
    private const float ImpactsPerSecond = 40.0f;
    private const float ImpactBurst = 12.0f;

    private readonly Dictionary<EFxShape, List<FPooled>> Pools = new();
    private readonly List<FLight> Lights = new();
    private readonly List<FDebris> DebrisPieces = new();
    private readonly List<FPooled> Active = new();
    private readonly List<Entity> MovedEntities = new();
    private readonly List<FTransform> MovedTransforms = new();
    private readonly Dictionary<uint, CStaticMesh?> DebrisMeshes = new();
    private readonly CParticleSystem?[] Effects = new CParticleSystem?[Enum.GetValues<EEffect>().Length];
    private int NextLight;
    private float ImpactTokens = ImpactBurst;

    public void Initialize()
    {
        foreach (EEffect Id in Enum.GetValues<EEffect>())
        {
            CParticleSystem? System = Asset.Load<CParticleSystem>(EffectFolder + "P_" + Id + ".lasset");
            Effects[(int)Id] = System;
            if (System is not null)
            {
                // Nothing else holds the asset between spawns, so without this it is collected and the cached wrapper dies.
                Mercs.World.RetainObject(System);
            }
            else
            {
                Debug.LogWarning($"Mercenaries: effect P_{Id} is missing, run Tools/AuthorEffects.py with the editor open.");
            }
        }

        CreatePool(EFxShape.Tracer, 48);
        CreatePool(EFxShape.Fireball, 96);
        CreatePool(EFxShape.Smoke, 110);
        CreatePool(EFxShape.SmokeLight, 120);
        CreatePool(EFxShape.Spark, 32);

        for (int Index = 0; Index < MaxLights; ++Index)
        {
            Entity Handle = Mercs.World.CreateEntity($"FxLight_{Index}", Hidden);
            SPointLightComponent Light = Mercs.World.Registry.GetOrAdd<SPointLightComponent>(Handle)!;
            Light.Intensity = 0.0f;
            Light.LightColor = new FVector3(1.0f, 0.6f, 0.25f);
            Light.Attenuation = 30.0f;
            Light.bCastShadows = false;
            Lights.Add(new FLight { Handle = Handle, Light = Light });
        }
    }

    private void CreatePool(EFxShape Shape, int Count)
    {
        MeshKit Kit = new();
        bool bGlow = Shape is EFxShape.Tracer or EFxShape.Fireball or EFxShape.Spark;
        switch (Shape)
        {
            case EFxShape.Tracer:
                Kit.Box(FVector3.Zero, new FVector3(0.5f, 0.5f, 0.5f), Palette.Tracer);
                break;
            case EFxShape.Fireball:
                Kit.Sphere(FVector3.Zero, 1.0f, Palette.Fire, 8);
                Kit.Sphere(FVector3.Zero, 0.65f, Palette.FireCore, 6);
                break;
            case EFxShape.Smoke:
                Kit.Sphere(FVector3.Zero, 1.0f, Palette.Smoke, 7);
                break;
            case EFxShape.SmokeLight:
                Kit.Sphere(FVector3.Zero, 1.0f, Palette.SmokeLight, 7);
                break;
            case EFxShape.Spark:
                Kit.Box(FVector3.Zero, new FVector3(0.5f), Palette.FireCore);
                break;
        }

        CStaticMesh? Mesh = Kit.BuildStaticMesh(Mercs.World, bGlow);
        List<FPooled> Pool = new(Count);
        for (int Index = 0; Index < Count; ++Index)
        {
            Entity Handle = Mercs.World.CreateEntity($"Fx_{Shape}_{Index}", Hidden, null, new FVector3(0.01f));
            MeshKit.Show(Mercs.World.Registry, Handle, Mesh, false);
            Pool.Add(new FPooled { Handle = Handle });
        }

        Pools[Shape] = Pool;
    }

    private FPooled? Acquire(EFxShape Shape)
    {
        if (!Pools.TryGetValue(Shape, out List<FPooled>? Pool))
        {
            return null;
        }

        FPooled? Oldest = null;
        foreach (FPooled Item in Pool)
        {
            if (!Item.bActive)
            {
                return Item;
            }

            if (Oldest is null || Item.Age / Item.Life > Oldest.Age / Oldest.Life)
            {
                Oldest = Item;
            }
        }

        if (Oldest is not null)
        {
            Active.Remove(Oldest);
            Oldest.bActive = false;
        }

        return Oldest;
    }

    private void Launch(FPooled Item, FVector3 Position, FVector3 Velocity, FQuat Rotation, FVector3 StartScale, FVector3 EndScale, float Life, float Drag = 0.0f)
    {
        Item.bActive = true;
        Item.Age = 0.0f;
        Item.Life = MathF.Max(0.01f, Life);
        Item.Position = Position;
        Item.Velocity = Velocity;
        Item.Rotation = Rotation;
        Item.StartScale = StartScale;
        Item.EndScale = EndScale;
        Item.Drag = Drag;
        Apply(Item, StartScale);
        Active.Add(Item);
    }

    private void Apply(FPooled Item, FVector3 Scale)
    {
        Stage(Item.Handle, new FTransform(Item.Position, Item.Rotation, Scale));
    }

    // Queued rather than written, so a frame's worth of pooled movement crosses to native once in Flush.
    public void Stage(Entity Handle, FTransform Transform)
    {
        MovedEntities.Add(Handle);
        MovedTransforms.Add(Transform);
    }

    public void Flush()
    {
        if (MovedEntities.Count == 0)
        {
            return;
        }

        CEntityLibrary.SetLocalTransforms(Mercs.World, CollectionsMarshal.AsSpan(MovedEntities), CollectionsMarshal.AsSpan(MovedTransforms));
        MovedEntities.Clear();
        MovedTransforms.Clear();
    }

    // Unit cubes scaled per chunk, a few shades per color, so debris shares meshes instead of building one each.
    private CStaticMesh? DebrisMesh(FVector4 Color, int Shade)
    {
        uint Key = (uint)(Color.X * 255.0f) | ((uint)(Color.Y * 255.0f) << 8) | ((uint)(Color.Z * 255.0f) << 16) | ((uint)Shade << 24);
        if (!DebrisMeshes.TryGetValue(Key, out CStaticMesh? Mesh))
        {
            MeshKit Kit = new();
            Kit.Box(FVector3.Zero, new FVector3(0.5f), Palette.Shade(Color, 0.6f + Shade * 0.2f));
            Mesh = Kit.BuildStaticMesh(Mercs.World);
            DebrisMeshes[Key] = Mesh;
        }

        return Mesh;
    }

    public void Tracer(FVector3 From, FVector3 To, float Width = 0.05f, float Life = 0.06f)
    {
        FVector3 Delta = To - From;
        float Length = Delta.Length;
        if (Length < 0.1f)
        {
            return;
        }

        FPooled? Item = Acquire(EFxShape.Tracer);
        if (Item is null)
        {
            return;
        }

        FQuat Rotation = FQuat.LookRotation(Delta / Length, MathF.Abs(Delta.Y / Length) > 0.95f ? FVector3.Right : FVector3.Up);
        FVector3 Scale = new(Width, Width, Length);
        Launch(Item, From + Delta * 0.5f, FVector3.Zero, Rotation, Scale, new FVector3(Width * 0.3f, Width * 0.3f, Length), Life);
    }

    public void Muzzle(FVector3 Position, float Size = 0.25f)
    {
        FPooled? Item = Acquire(EFxShape.Fireball);
        if (Item is not null)
        {
            Launch(Item, Position, FVector3.Zero, FQuat.Identity, new FVector3(Size), new FVector3(Size * 0.3f), 0.05f);
        }
    }

    public void Spark(FVector3 Position, FVector3 Normal, int Count = 3)
    {
        for (int Index = 0; Index < Count; ++Index)
        {
            FPooled? Item = Acquire(EFxShape.Spark);
            if (Item is null)
            {
                return;
            }

            FVector3 Velocity = (Normal + new FVector3(Mercs.Range(-0.7f, 0.7f), Mercs.Range(-0.2f, 0.8f), Mercs.Range(-0.7f, 0.7f))).NormalizedOr(FVector3.Up) * Mercs.Range(3.0f, 7.0f);
            Launch(Item, Position, Velocity, FQuat.Identity, new FVector3(0.06f), new FVector3(0.01f), Mercs.Range(0.15f, 0.3f), 2.0f);
        }
    }

    public void Puff(FVector3 Position, float Size, float Life, bool bDark, FVector3 Velocity)
    {
        FPooled? Item = Acquire(bDark ? EFxShape.Smoke : EFxShape.SmokeLight);
        if (Item is null)
        {
            return;
        }

        Launch(Item, Position, Velocity, FQuat.Identity, new FVector3(Size * 0.4f), new FVector3(Size), Life, 0.6f);
    }

    public void Flame(FVector3 Position, float Size)
    {
        FPooled? Item = Acquire(EFxShape.Fireball);
        if (Item is null)
        {
            return;
        }

        FVector3 Velocity = new(Mercs.Range(-0.3f, 0.3f), Mercs.Range(1.5f, 3.0f), Mercs.Range(-0.3f, 0.3f));
        Launch(Item, Position, Velocity, FQuat.Identity, new FVector3(Size), new FVector3(Size * 0.2f), Mercs.Range(0.3f, 0.5f));
    }

    public bool HasEffect(EEffect Id) => Effects[(int)Id] is not null;

    private static FTransform EffectTransform(FVector3 Position, float Scale)
        => new(Position, FQuat.AngleAxis(Mercs.Range(0.0f, MathF.Tau), FVector3.Up), new FVector3(Scale));

    // Bullet hits share a budget, so a long burst of fire cannot pile up hundreds of live systems.
    public void Impact(FVector3 Position, FVector3 Normal, bool bMetal)
    {
        CParticleSystem? System = Effects[(int)(bMetal ? EEffect.ImpactSparks : EEffect.ImpactDirt)];
        if (System is null || ImpactTokens < 1.0f)
        {
            Spark(Position, Normal, 2);
            return;
        }

        ImpactTokens -= 1.0f;
        Fx.PlayAligned(System, Position + Normal * 0.03f, Normal);
    }

    public void CollapseDust(FVector3 Base, FVector3 Size)
    {
        if (Effects[(int)EEffect.CollapseDust] is { } System)
        {
            float Scale = Math.Clamp(MathF.Max(Size.X, Size.Z) / CollapseAuthoredWidth, 0.4f, 3.0f);
            Fx.Play(System, EffectTransform(Geo.Ground(Base) + new FVector3(0.0f, 0.5f, 0.0f), Scale));
        }
    }

    // A looping fire the caller moves with MoveFire and ends with StopFire, so it can follow a wreck without inheriting its roll.
    public Entity StartFire(FVector3 Position, float Scale)
        => Effects[(int)EEffect.Burning] is { } System ? Fx.Play(System, new FTransform(Position, FQuat.Identity, new FVector3(Scale)), 0.0f) : Entity.Null;

    public void MoveFire(Entity Fire, FVector3 Position, float Scale)
    {
        if (!Fire.IsNull && Mercs.World.IsValidEntity(Fire))
        {
            Stage(Fire, new FTransform(Position, FQuat.Identity, new FVector3(Scale)));
        }
    }

    public void StopFire(Entity Fire)
    {
        if (!Fire.IsNull && Mercs.World.IsValidEntity(Fire))
        {
            Fx.Stop(Fire);
            Mercs.World.SetLifetime(Fire, FireFadeLife);
        }
    }

    public void Explosion(FVector3 Position, float Radius)
    {
        if (Effects[(int)EEffect.Explosion] is { } System)
        {
            Fx.Play(System, EffectTransform(Position, Math.Clamp(Radius / ExplosionAuthoredRadius, 0.35f, 3.5f)));
            Flash(Position + new FVector3(0.0f, 1.5f, 0.0f), 400.0f * Radius, 0.35f + Radius * 0.02f);
            return;
        }

        int Balls = Math.Clamp((int)(Radius * 0.8f), 2, 7);
        for (int Index = 0; Index < Balls; ++Index)
        {
            FPooled? Item = Acquire(EFxShape.Fireball);
            if (Item is null)
            {
                break;
            }

            FVector3 Offset = new(Mercs.Range(-0.4f, 0.4f) * Radius, Mercs.Range(0.0f, 0.5f) * Radius, Mercs.Range(-0.4f, 0.4f) * Radius);
            float Size = Radius * Mercs.Range(0.35f, 0.6f);
            Launch(Item, Position + Offset, Offset * 0.8f + new FVector3(0.0f, 2.0f, 0.0f), FQuat.Identity, new FVector3(Size * 0.4f), new FVector3(Size * 1.2f), Mercs.Range(0.35f, 0.6f), 1.5f);
        }

        int Puffs = Math.Clamp((int)(Radius * 1.2f), 3, 10);
        for (int Index = 0; Index < Puffs; ++Index)
        {
            FVector3 Offset = new(Mercs.Range(-0.5f, 0.5f) * Radius, Mercs.Range(0.0f, 0.6f) * Radius, Mercs.Range(-0.5f, 0.5f) * Radius);
            Puff(Position + Offset, Radius * Mercs.Range(0.5f, 0.9f), Mercs.Range(2.0f, 4.0f), true, new FVector3(Offset.X * 0.3f, Mercs.Range(1.5f, 4.0f), Offset.Z * 0.3f));
        }

        Spark(Position + new FVector3(0.0f, 0.5f, 0.0f), FVector3.Up, Math.Clamp((int)Radius, 3, 10));
        Flash(Position + new FVector3(0.0f, 1.5f, 0.0f), 400.0f * Radius, 0.35f + Radius * 0.02f);
    }

    public void Flash(FVector3 Position, float Intensity, float Life)
    {
        if (Lights.Count == 0)
        {
            return;
        }

        FLight Item = Lights[NextLight];
        NextLight = (NextLight + 1) % Lights.Count;
        Item.bActive = true;
        Item.Age = 0.0f;
        Item.Life = Life;
        Item.Peak = Intensity;
        Mercs.World.SetEntityLocation(Item.Handle, Position);
        if (Item.Light is not null)
        {
            Item.Light.Intensity = Intensity;
        }
    }

    public void Debris(FVector3 Position, FVector4 Color, int Count, float Speed, float MaxSize = 0.6f)
    {
        CWorld World = Mercs.World;
        EntityRegistry Registry = World.Registry;
        List<Entity> Spawned = new(Count);

        using (new FPhysicsBatchScope(World))
        {
            for (int Index = 0; Index < Count; ++Index)
            {
                if (DebrisPieces.Count >= MaxDebris)
                {
                    FDebris Oldest = DebrisPieces[0];
                    DebrisPieces.RemoveAt(0);
                    if (World.IsValidEntity(Oldest.Handle))
                    {
                        World.DestroyEntity(Oldest.Handle);
                    }
                }

                FVector3 Half = new(Mercs.Range(0.15f, MaxSize), Mercs.Range(0.1f, MaxSize * 0.7f), Mercs.Range(0.15f, MaxSize));
                FVector3 At = Position + new FVector3(Mercs.Range(-1.5f, 1.5f), Mercs.Range(0.3f, 2.5f), Mercs.Range(-1.5f, 1.5f));
                Entity Chunk = World.CreateEntity("Debris", At, FQuat.FromEuler(Mercs.Range(0, 3), Mercs.Range(0, 3), Mercs.Range(0, 3)), Half * 2.0f);
                MeshKit.Show(Registry, Chunk, DebrisMesh(Color, Mercs.Rng.Next(0, 3)), false);

                // The collider scales with the entity, so a unit half extent matches the scaled cube.
                SBoxColliderComponent Collider = Registry.GetOrAdd<SBoxColliderComponent>(Chunk)!;
                Collider.HalfExtent = new FVector3(0.5f);
                Collider.bAffectsNavigation = false;
                SRigidBodyComponent Body = Registry.GetOrAdd<SRigidBodyComponent>(Chunk)!;
                Body.BodyType = EBodyType.Dynamic;
                Body.Mass = 20.0f * Half.X * Half.Y * Half.Z * 8.0f;
                Body.bOverrideMass = true;
                Body.CollisionProfile = new FCollisionProfile { Layer = ECollisionProfiles.Channel5, Mask = ECollisionProfiles.Static | ECollisionProfiles.Channel5 };

                Spawned.Add(Chunk);
                DebrisPieces.Add(new FDebris { Handle = Chunk, Life = Mercs.Range(6.0f, 10.0f), Scale = Half * 2.0f });
            }
        }

        foreach (Entity Chunk in Spawned)
        {
            FVector3 Out = (World.GetEntityLocation(Chunk) - Position).NormalizedOr(FVector3.Up);
            FVector3 Velocity = (Out + new FVector3(0.0f, 1.2f, 0.0f)).Normalized() * Speed * Mercs.Range(0.5f, 1.2f);
            CPhysicsLibrary.SetLinearVelocity(World, Chunk, Velocity);
            CPhysicsLibrary.SetAngularVelocity(World, Chunk, new FVector3(Mercs.Range(-6, 6), Mercs.Range(-6, 6), Mercs.Range(-6, 6)));
        }
    }

    public void Update(float DeltaTime)
    {
        ImpactTokens = MathF.Min(ImpactBurst, ImpactTokens + DeltaTime * ImpactsPerSecond);

        for (int Index = Active.Count - 1; Index >= 0; --Index)
        {
            FPooled Item = Active[Index];
            Item.Age += DeltaTime;
            if (Item.Age >= Item.Life)
            {
                Item.bActive = false;
                Item.Position = Hidden;
                Apply(Item, new FVector3(0.01f));
                Active.RemoveAt(Index);
                continue;
            }

            float T = Item.Age / Item.Life;
            Item.Velocity *= MathF.Max(0.0f, 1.0f - Item.Drag * DeltaTime);
            Item.Position += Item.Velocity * DeltaTime;
            Apply(Item, FVector3.Lerp(Item.StartScale, Item.EndScale, T));
        }

        foreach (FLight Item in Lights)
        {
            if (!Item.bActive)
            {
                continue;
            }

            Item.Age += DeltaTime;
            float T = Mathf.Clamp01(Item.Age / Item.Life);
            if (Item.Light is not null)
            {
                Item.Light.Intensity = Item.Peak * (1.0f - T) * (1.0f - T);
            }

            if (T >= 1.0f)
            {
                Item.bActive = false;
                Mercs.World.SetEntityLocation(Item.Handle, Hidden);
            }
        }

        for (int Index = DebrisPieces.Count - 1; Index >= 0; --Index)
        {
            FDebris Item = DebrisPieces[Index];
            Item.Life -= DeltaTime;
            if (Item.Life > 0.0f)
            {
                if (Item.Life < 0.8f)
                {
                    Mercs.World.Registry.TryGet<STransformComponent>(Item.Handle)?.SetLocalScale(Item.Scale * MathF.Max(Item.Life / 0.8f, 0.01f));
                }

                continue;
            }

            if (Mercs.World.IsValidEntity(Item.Handle))
            {
                Mercs.World.DestroyEntity(Item.Handle);
            }

            DebrisPieces.RemoveAt(Index);
        }
    }
}
