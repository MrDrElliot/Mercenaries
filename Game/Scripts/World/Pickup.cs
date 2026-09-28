using System;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

public enum EPickupKind : byte
{
    Cash,
    Fuel,
    Ammo,
    Health,
    Grenades,
    Charges,
    Weapon,
    Supplies,
}

public sealed class Pickup : EntityScript
{
    [Property(Category = "Pickup")]
    public EPickupKind Kind = EPickupKind.Cash;

    [Property(Category = "Pickup")]
    public int Amount = 1000;

    [Property(Category = "Pickup")]
    public EWeapon WeaponKind = EWeapon.Rifle;

    [Property(Category = "Pickup", Units = "s")]
    public float Lifetime = 90.0f;

    public float FallSpeed;

    private FVector3 Base;
    private float Age;
    private bool bTaken;
    private bool bReady;
    private Entity GlowEntity = Entity.Null;

    public bool IsAvailable => bReady && !bTaken;
    public FVector3 Location => Base;

    public static Pickup? Spawn(EPickupKind Kind, FVector3 At, int Amount, EWeapon Weapon = EWeapon.None, float Lifetime = 90.0f, float FallSpeed = 0.0f)
    {
        CWorld World = Mercs.World;
        Entity Handle = World.CreateEntity($"Pickup_{Kind}", At);
        Pickup? Script = World.Registry.AddScript<Pickup>(Handle);
        if (Script is null)
        {
            World.DestroyEntity(Handle);
            return null;
        }

        Script.Kind = Kind;
        Script.Amount = Amount;
        Script.WeaponKind = Weapon;
        Script.Lifetime = Lifetime;
        Script.FallSpeed = FallSpeed;
        return Script;
    }

    public static Pickup? SpawnCash(FVector3 At, int Amount) => Spawn(EPickupKind.Cash, At, Amount);

    public static Pickup? SpawnWeapon(FVector3 At, EWeapon Weapon) => Weapon == EWeapon.None ? null : Spawn(EPickupKind.Weapon, At, 1, Weapon, 60.0f);

    public static Pickup? NearestWeapon(FVector3 From, float Range)
    {
        Pickup? Best = null;
        float BestDistance = Range;
        foreach (Pickup Candidate in Mercs.Pickups)
        {
            if (!Candidate.IsAvailable || Candidate.Kind != EPickupKind.Weapon)
            {
                continue;
            }

            float Distance = FVector3.Distance(Candidate.Base, From);
            if (Distance < BestDistance)
            {
                Best = Candidate;
                BestDistance = Distance;
            }
        }

        return Best;
    }

    public override void OnReady()
    {
        Base = World.GetEntityLocation(Entity);
        if (FallSpeed <= 0.0f)
        {
            Base = Geo.Ground(Base) + new FVector3(0.0f, 0.35f, 0.0f);
        }

        BuildMesh();
        Mercs.Pickups.Add(this);
        bReady = true;
    }

    public override void OnDetach()
    {
        Mercs.Pickups.Remove(this);
    }

    private void BuildMesh()
    {
        MeshKit Kit = new();
        switch (Kind)
        {
            case EPickupKind.Cash:
                Kit.Box(FVector3.Zero, new FVector3(0.22f, 0.08f, 0.12f), Palette.Cash);
                Kit.Box(new FVector3(0.05f, 0.14f, 0.02f), new FVector3(0.2f, 0.06f, 0.11f), Palette.Shade(Palette.Cash, 1.2f));
                Kit.Box(new FVector3(0.0f, 0.02f, 0.0f), new FVector3(0.04f, 0.1f, 0.13f), Palette.Hex(0xE8E2C8));
                break;
            case EPickupKind.Fuel:
                Kit.Box(FVector3.Zero, new FVector3(0.18f, 0.25f, 0.1f), Palette.FuelRed);
                Kit.Box(new FVector3(0.0f, 0.28f, 0.0f), new FVector3(0.06f, 0.04f, 0.06f), Palette.Gunmetal);
                break;
            case EPickupKind.Ammo:
                Kit.Box(FVector3.Zero, new FVector3(0.3f, 0.15f, 0.18f), Palette.AmmoOlive);
                Kit.Box(new FVector3(0.0f, 0.16f, 0.0f), new FVector3(0.31f, 0.02f, 0.05f), Palette.Hex(0xD9B23A));
                break;
            case EPickupKind.Health:
                Kit.Box(FVector3.Zero, new FVector3(0.25f, 0.15f, 0.15f), Palette.Medic);
                Kit.Box(new FVector3(0.0f, 0.0f, 0.155f), new FVector3(0.12f, 0.035f, 0.005f), Palette.Rgb(0.8f, 0.05f, 0.05f));
                Kit.Box(new FVector3(0.0f, 0.0f, 0.155f), new FVector3(0.035f, 0.12f, 0.005f), Palette.Rgb(0.8f, 0.05f, 0.05f));
                break;
            case EPickupKind.Grenades:
                Kit.Sphere(new FVector3(-0.1f, 0.0f, 0.0f), 0.1f, Palette.Hex(0x3E4A2A), 6);
                Kit.Sphere(new FVector3(0.1f, 0.0f, 0.0f), 0.1f, Palette.Hex(0x3E4A2A), 6);
                break;
            case EPickupKind.Charges:
                Kit.Box(FVector3.Zero, new FVector3(0.18f, 0.08f, 0.12f), Palette.Hex(0xCFC6A5));
                break;
            case EPickupKind.Weapon:
                Kit.Box(FVector3.Zero, new FVector3(0.05f, 0.08f, 0.5f), Palette.Gunmetal);
                Kit.Box(new FVector3(0.0f, -0.1f, -0.15f), new FVector3(0.04f, 0.1f, 0.05f), Palette.Gunmetal);
                break;
            case EPickupKind.Supplies:
                Kit.Box(new FVector3(0.0f, 0.5f, 0.0f), new FVector3(0.7f, 0.5f, 0.7f), Palette.Wood);
                Kit.Box(new FVector3(0.0f, 0.5f, 0.71f), new FVector3(0.2f, 0.2f, 0.01f), Palette.Medic);
                if (FallSpeed > 0.0f)
                {
                    Kit.Tube(new FVector3(0.0f, 1.0f, 0.0f), new FVector3(0.0f, 5.0f, 0.0f), 0.02f, 0.02f, Palette.Medic, 4);
                    Kit.Cone(new FVector3(0.0f, 4.5f, 0.0f), 3.0f, 1.2f, Palette.Hex(0xD66A2A), 10);
                }

                break;
        }

        FVector4 Ring = Kind switch
        {
            EPickupKind.Cash => Palette.Rgb(0.3f, 1.0f, 0.3f),
            EPickupKind.Fuel => Palette.Rgb(1.0f, 0.3f, 0.1f),
            EPickupKind.Weapon => Palette.Rgb(1.0f, 0.8f, 0.2f),
            EPickupKind.Supplies => Palette.Rgb(1.0f, 1.0f, 1.0f),
            _ => Palette.Rgb(0.4f, 0.8f, 1.0f),
        };

        Kit.Commit(Registry, Entity, false, true, false);
        if (!GlowEntity.IsNull)
        {
            return;
        }

        Entity Glow = World.CreateEntity("PickupGlow", FVector3.Zero);
        GlowEntity = Glow;
        World.SetParent(Glow, Entity);
        MeshKit Halo = new();
        float Size = Kind == EPickupKind.Supplies ? 1.1f : 0.4f;
        Halo.Tube(new FVector3(0.0f, -0.3f, 0.0f), new FVector3(0.0f, -0.28f, 0.0f), Size, Size, Ring, 12);
        Halo.Commit(Registry, Glow, true, false, false);
        Registry.Get<STransformComponent>(Glow).SetLocalLocation(FVector3.Zero);
    }

    public override void OnUpdate(float DeltaTime)
    {
        if (!bReady || bTaken || !Mercs.IsRunning)
        {
            return;
        }

        Age += DeltaTime;
        if (Lifetime > 0.0f && Age > Lifetime)
        {
            Consume();
            return;
        }

        if (FallSpeed > 0.0f)
        {
            float Ground = Terrain.HeightAt(Base.X, Base.Z) + 0.3f;
            Base.Y = MathF.Max(Ground, Base.Y - FallSpeed * DeltaTime);
            if (Base.Y <= Ground)
            {
                FallSpeed = 0.0f;
                BuildMesh();
                Mercs.Fx.Puff(Base, 2.0f, 1.5f, false, FVector3.Up);
            }

            World.SetEntityLocation(Entity, Base);
            return;
        }

        bool bSpins = Kind != EPickupKind.Supplies;
        FVector3 Shown = Base + new FVector3(0.0f, bSpins ? 0.15f + MathF.Sin(Age * 2.5f) * 0.08f : 0.0f, 0.0f);
        World.SetEntityLocation(Entity, Shown);
        if (bSpins)
        {
            World.SetEntityRotation(Entity, FQuat.FromEuler(0.0f, Age * 1.8f, 0.0f));
        }

        TryCollect();
    }

    private void TryCollect()
    {
        MercPlayer? Player = Mercs.Player;
        if (Player is null || !Player.IsAlive || Kind == EPickupKind.Weapon)
        {
            return;
        }

        bool bInVehicle = Player.CurrentVehicle is not null;
        float Reach = bInVehicle ? 3.5f : 1.6f;
        if (bInVehicle && Kind is not (EPickupKind.Cash or EPickupKind.Fuel))
        {
            return;
        }

        if (FVector3.Distance(Player.Position, Base) > Reach)
        {
            return;
        }

        switch (Kind)
        {
            case EPickupKind.Cash:
                Mercs.Wallet.AddCash(Amount, "cash");
                break;
            case EPickupKind.Fuel:
                Mercs.Wallet.AddFuel(Amount, "fuel");
                break;
            case EPickupKind.Ammo:
                Player.AddAmmo(0.5f);
                Mercs.Feed.Post("Ammo", ENewsTone.Neutral);
                break;
            case EPickupKind.Health:
                Player.Heal(Amount);
                Mercs.Feed.Post("Medkit", ENewsTone.Good);
                break;
            case EPickupKind.Grenades:
                Player.Grenades = Math.Min(Player.MaxGrenades, Player.Grenades + Amount);
                Mercs.Feed.Post($"+{Amount} grenades", ENewsTone.Neutral);
                break;
            case EPickupKind.Charges:
                Player.Charges = Math.Min(Player.MaxCharges, Player.Charges + Amount);
                Mercs.Feed.Post($"+{Amount} C4", ENewsTone.Neutral);
                break;
            case EPickupKind.Supplies:
                Player.AddAmmo(1.0f);
                Player.Heal(1000.0f);
                Player.Grenades = Player.MaxGrenades;
                Player.Charges = Player.MaxCharges;
                Mercs.Feed.Post("Resupplied: ammo, health, grenades and C4.", ENewsTone.Good);
                break;
        }

        Sfx.Ui(ESfx.Pickup, 0.55f);
        Consume();
    }

    public void Consume()
    {
        if (bTaken)
        {
            return;
        }

        bTaken = true;
        World.SetLifetime(Entity, 0.01f);
    }
}
