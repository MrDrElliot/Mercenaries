using System;
using System.Collections.Generic;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

public enum EWeapon : byte
{
    None,
    Pistol,
    Rifle,
    Smg,
    Shotgun,
    Sniper,
    MachineGun,
    Rocket,
    GrenadeLauncher,
    TankCannon,
    VehicleMG,
    HeliRockets,
    Autocannon,
}

public sealed class WeaponDef
{
    public EWeapon Kind;
    public string Name = string.Empty;
    public float Damage;
    public float Interval;
    public float SpreadDegrees;
    public int Pellets = 1;
    public float Range = 150.0f;
    public int Magazine;
    public int MaxReserve;
    public float ReloadTime = 2.0f;
    public bool bAutomatic;
    public bool bProjectile;
    public float ProjectileSpeed;
    public float ProjectileGravity;
    public float BlastRadius;
    public float BlastDamage;
    public float ZoomFov = 55.0f;
    public float Recoil = 0.6f;
    public float NoiseRange = 60.0f;
    public float MuzzleSize = 0.25f;
    public float StructureMultiplier = 1.0f;
}

public sealed class WeaponState
{
    public EWeapon Kind;
    public int Magazine;
    public int Reserve;
    public float Cooldown;
    public float ReloadLeft;

    public WeaponState(EWeapon Kind, bool bFull = true)
    {
        this.Kind = Kind;
        WeaponDef Def = Weapons.Get(Kind);
        Magazine = Def.Magazine;
        Reserve = bFull ? Def.MaxReserve : Def.MaxReserve / 3;
    }

    public WeaponDef Def => Weapons.Get(Kind);

    public bool IsReloading => ReloadLeft > 0.0f;

    public bool CanFire => Cooldown <= 0.0f && ReloadLeft <= 0.0f && Magazine > 0;

    public void Tick(float DeltaTime)
    {
        if (Cooldown > 0.0f)
        {
            Cooldown -= DeltaTime;
        }

        if (ReloadLeft > 0.0f)
        {
            ReloadLeft -= DeltaTime;
            if (ReloadLeft <= 0.0f)
            {
                int Taken = Math.Min(Def.Magazine - Magazine, Reserve);
                Magazine += Taken;
                Reserve -= Taken;
            }
        }
    }

    public bool BeginReload()
    {
        if (IsReloading || Magazine >= Def.Magazine || Reserve <= 0)
        {
            return false;
        }

        ReloadLeft = Def.ReloadTime;
        return true;
    }

    public void Consume()
    {
        Magazine--;
        Cooldown = Def.Interval;
    }

    public void Refill(float Fraction)
    {
        Reserve = Math.Min(Def.MaxReserve, Reserve + (int)MathF.Ceiling(Def.MaxReserve * Fraction));
    }
}

public static class Weapons
{
    private static readonly Dictionary<EWeapon, WeaponDef> Defs = new();

    static Weapons()
    {
        Add(new WeaponDef { Kind = EWeapon.Pistol, Name = "Pistol", Damage = 22, Interval = 0.22f, SpreadDegrees = 1.2f, Magazine = 12, MaxReserve = 96, ReloadTime = 1.3f, Range = 80, ZoomFov = 60, Recoil = 0.8f });
        Add(new WeaponDef { Kind = EWeapon.Rifle, Name = "Assault Rifle", Damage = 18, Interval = 0.1f, SpreadDegrees = 1.4f, Magazine = 30, MaxReserve = 240, ReloadTime = 1.9f, bAutomatic = true, Range = 160, Recoil = 0.45f });
        Add(new WeaponDef { Kind = EWeapon.Smg, Name = "SMG", Damage = 13, Interval = 0.07f, SpreadDegrees = 2.6f, Magazine = 35, MaxReserve = 280, ReloadTime = 1.6f, bAutomatic = true, Range = 90, Recoil = 0.35f });
        Add(new WeaponDef { Kind = EWeapon.Shotgun, Name = "Combat Shotgun", Damage = 11, Pellets = 9, Interval = 0.55f, SpreadDegrees = 6.5f, Magazine = 8, MaxReserve = 48, ReloadTime = 2.4f, Range = 45, Recoil = 2.5f, MuzzleSize = 0.4f });
        Add(new WeaponDef { Kind = EWeapon.Sniper, Name = "Sniper Rifle", Damage = 95, Interval = 1.1f, SpreadDegrees = 0.05f, Magazine = 5, MaxReserve = 30, ReloadTime = 2.6f, Range = 400, ZoomFov = 18, Recoil = 4.0f, NoiseRange = 120 });
        Add(new WeaponDef { Kind = EWeapon.MachineGun, Name = "Machine Gun", Damage = 20, Interval = 0.085f, SpreadDegrees = 2.2f, Magazine = 100, MaxReserve = 300, ReloadTime = 3.6f, bAutomatic = true, Range = 170, Recoil = 0.55f });
        Add(new WeaponDef { Kind = EWeapon.Rocket, Name = "Rocket Launcher", Damage = 0, Interval = 1.2f, SpreadDegrees = 0.2f, Magazine = 1, MaxReserve = 8, ReloadTime = 2.2f, bProjectile = true, ProjectileSpeed = 55, BlastRadius = 7.0f, BlastDamage = 420, Range = 300, ZoomFov = 45, Recoil = 3.0f, MuzzleSize = 0.6f, StructureMultiplier = 1.4f });
        Add(new WeaponDef { Kind = EWeapon.GrenadeLauncher, Name = "Grenade Launcher", Damage = 0, Interval = 0.7f, SpreadDegrees = 0.8f, Magazine = 6, MaxReserve = 24, ReloadTime = 3.0f, bProjectile = true, ProjectileSpeed = 38, ProjectileGravity = 9.8f, BlastRadius = 6.0f, BlastDamage = 220, Range = 200, Recoil = 2.0f, MuzzleSize = 0.4f });
        Add(new WeaponDef { Kind = EWeapon.TankCannon, Name = "Main Cannon", Damage = 0, Interval = 1.8f, SpreadDegrees = 0.15f, Magazine = 1, MaxReserve = 9999, ReloadTime = 0.0f, bProjectile = true, ProjectileSpeed = 110, ProjectileGravity = 2.0f, BlastRadius = 8.5f, BlastDamage = 520, Range = 400, Recoil = 0.0f, MuzzleSize = 1.1f, StructureMultiplier = 1.6f });
        Add(new WeaponDef { Kind = EWeapon.VehicleMG, Name = "Mounted MG", Damage = 22, Interval = 0.09f, SpreadDegrees = 1.8f, Magazine = 9999, MaxReserve = 9999, bAutomatic = true, Range = 180, Recoil = 0.0f });
        Add(new WeaponDef { Kind = EWeapon.HeliRockets, Name = "Rocket Pods", Damage = 0, Interval = 0.32f, SpreadDegrees = 1.4f, Magazine = 9999, MaxReserve = 9999, bAutomatic = true, bProjectile = true, ProjectileSpeed = 70, BlastRadius = 6.0f, BlastDamage = 260, Range = 300, Recoil = 0.0f, MuzzleSize = 0.5f, StructureMultiplier = 1.2f });
        Add(new WeaponDef { Kind = EWeapon.Autocannon, Name = "Autocannon", Damage = 45, Interval = 0.2f, SpreadDegrees = 1.2f, Magazine = 9999, MaxReserve = 9999, bAutomatic = true, Range = 220, Recoil = 0.0f, MuzzleSize = 0.5f });
    }

    private static void Add(WeaponDef Def) => Defs[Def.Kind] = Def;

    public static WeaponDef Get(EWeapon Kind) => Defs.TryGetValue(Kind, out WeaponDef? Def) ? Def : Defs[EWeapon.Pistol];

    public static FVector3 Spread(FVector3 Direction, float Degrees)
    {
        if (Degrees <= 0.0f)
        {
            return Direction;
        }

        float Angle = Mathf.Radians(Degrees) * MathF.Sqrt((float)Mercs.Rng.NextDouble());
        float Roll = Mercs.Range(0.0f, Mathf.TwoPi);
        FVector3 Right = FVector3.Cross(Direction, FVector3.Up).NormalizedOr(FVector3.Right);
        FVector3 Up = FVector3.Cross(Right, Direction).Normalized();
        FVector3 Offset = (Right * MathF.Cos(Roll) + Up * MathF.Sin(Roll)) * MathF.Tan(Angle);
        return (Direction + Offset).Normalized();
    }

    // Returns where the first pellet landed, so callers can aim feedback there.
    public static FVector3 Fire(IDamageable Shooter, WeaponDef Def, FVector3 Muzzle, FVector3 Direction, Entity IgnoreExtra, float ExtraSpread = 0.0f, bool bTracers = true)
    {
        Mercs.Fx.Muzzle(Muzzle + Direction * 0.3f, Def.MuzzleSize);
        Sfx.Gun(Def.Kind, Muzzle, Shooter.IsPlayerControlled);
        FVector3 FirstImpact = Muzzle + Direction * Def.Range;

        if (Def.bProjectile)
        {
            FVector3 Aim = Spread(Direction, Def.SpreadDegrees + ExtraSpread);
            OrdnanceSystem.Launch(Muzzle + Aim * 0.8f, Aim * Def.ProjectileSpeed, Def.ProjectileGravity, Def.BlastRadius, Def.BlastDamage, Shooter, IgnoreExtra, Def.Kind == EWeapon.GrenadeLauncher ? EOrdnanceLook.Grenade : EOrdnanceLook.Rocket, Def.StructureMultiplier);
            AlertNearby(Muzzle, Def.NoiseRange, Shooter);
            return FirstImpact;
        }

        Mercs.Fx.Casing(Muzzle, Direction);

        for (int Pellet = 0; Pellet < Def.Pellets; ++Pellet)
        {
            FVector3 Aim = Spread(Direction, Def.SpreadDegrees + ExtraSpread);
            FVector3 End = Muzzle + Aim * Def.Range;
            SRayResult Hit = Geo.Trace(Muzzle, End, Shooter.Owner, IgnoreExtra);
            FVector3 Impact = Hit.bHit ? Hit.Location : End;
            if (Pellet == 0)
            {
                FirstImpact = Impact;
            }

            if (bTracers && (Pellet == 0 || Pellet % 3 == 0))
            {
                Mercs.Fx.Tracer(Muzzle + Aim * 0.8f, Impact, Shooter.IsPlayerControlled ? 0.05f : 0.04f);
            }

            if (!Shooter.IsPlayerControlled && Pellet == 0)
            {
                Sfx.NearMiss(Muzzle, Impact);
            }

            if (!Hit.bHit)
            {
                continue;
            }

            IDamageable? Target = Mercs.FindDamageable(new Entity(Hit.Entity));
            if (Target is null && Gore.IsGib(new Entity(Hit.Entity)))
            {
                Gore.HitGib(new Entity(Hit.Entity), Hit.Location, Aim, Def.Damage);
                Sfx.At(ESfx.HitBody, Hit.Location, 0.45f, 50.0f, 2.0f, 0.12f, 0.02f);
                continue;
            }

            if (Target is null)
            {
                Mercs.Fx.Impact(Hit.Location, Hit.Normal, false);
                Mercs.Fx.BulletHole(Hit.Location, Hit.Normal);
                Sfx.At(Mercs.Rng.NextDouble() < 0.12 ? ESfx.Ricochet : ESfx.ImpactGround, Hit.Location, 0.45f, 60.0f, 2.0f, 0.12f, 0.02f);
                continue;
            }

            Sfx.At(Target is Soldier or MercPlayer ? ESfx.HitBody : ESfx.ImpactMetal, Hit.Location, 0.55f, 60.0f, 2.0f, 0.12f, 0.02f);

            float Amount = Def.Damage;
            if (Target is MercPlayer && !Shooter.IsPlayerControlled)
            {
                Amount *= 0.4f;
            }

            if (Target is Soldier && Shooter.IsPlayerControlled)
            {
                float Height = Hit.Location.Y - Target.Position.Y;
                if (Height > 0.55f)
                {
                    Amount *= 2.2f;
                }
            }
            else if (Target is not (Soldier or MercPlayer))
            {
                Mercs.Fx.Impact(Hit.Location, Hit.Normal, Target is Vehicle);
                if (Target is Structure)
                {
                    Mercs.Fx.BulletHole(Hit.Location, Hit.Normal);
                }
            }

            if (Target is Soldier or MercPlayer && Target.IsAlive)
            {
                Gore.Wound(Hit.Location, Aim, Amount, Target.Owner);
            }

            Target.TakeHit(FHit.From(Shooter, Amount, EDamageKind.Bullet, Hit.Location, Aim));
        }

        AlertNearby(Muzzle, Def.NoiseRange, Shooter);
        return FirstImpact;
    }

    public static void AlertNearby(FVector3 Where, float Range, IDamageable Source)
    {
        foreach (Soldier Listener in Mercs.Soldiers)
        {
            if (Listener.IsAlive && FVector3.DistanceSquared(Listener.Position, Where) < Range * Range)
            {
                Listener.HearNoise(Where, Source);
            }
        }

        if (Source.IsPlayerControlled)
        {
            Mercs.Factions.RaiseSuspicion(0.2f);
        }
    }
}
