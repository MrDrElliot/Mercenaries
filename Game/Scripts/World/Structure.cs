using System;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

public enum EStructureKind : byte
{
    House,
    Shack,
    Barracks,
    Bunker,
    Tower,
    Tent,
    FuelTank,
    Radar,
    Antenna,
    Wall,
    Crate,
    Warehouse,
    Hangar,
    Headquarters,
    Statue,
    Silo,
    Container,
    Derrick,
    Sandbags,
    AntiAir,
    Helipad,
    Dock,
    Church,
    Shop,
}

public sealed class Structure : EntityScript, IDamageable
{
    [Property(Category = "Structure")]
    public EStructureKind Kind = EStructureKind.House;

    [Property(Category = "Structure")]
    public EFaction OwnerFaction = EFaction.Civilian;

    [Property(Category = "Structure", Units = "m")]
    public FVector3 Size = new(8.0f, 5.0f, 8.0f);

    [Property(Category = "Structure")]
    public FVector4 Tint = new(0.85f, 0.8f, 0.7f, 1.0f);

    public float MaxHealth;
    public int CashValue;
    public bool bInvulnerable;
    public string Label = string.Empty;
    public Site? HomeSite;

    private float Health;
    private int DamageStage;
    private bool bLastHitByPlayer;
    private bool bDestroyed;
    private bool bReady;
    private FVector3 Base;
    private FQuat Rotation = FQuat.Identity;

    public Entity Owner => Entity;
    public EFaction Faction => OwnerFaction;
    public bool IsAlive => bReady && !bDestroyed;
    public bool IsPlayerControlled => false;
    public FVector3 Position => Base + new FVector3(0.0f, Size.Y * 0.5f, 0.0f);
    public float Radius => MathF.Max(Size.X, Size.Z) * 0.5f;
    public bool IsDestroyed => bDestroyed;
    public float HealthFraction => MaxHealth > 0.0f ? Mathf.Clamp01(Health / MaxHealth) : 1.0f;
    public bool IsFlimsy => Kind is EStructureKind.Crate or EStructureKind.Tent or EStructureKind.Sandbags or EStructureKind.Shack;
    public bool IsExplosive => Kind is EStructureKind.FuelTank or EStructureKind.Derrick or EStructureKind.AntiAir;
    public bool IsFactionProperty => OwnerFaction is not (EFaction.Civilian or EFaction.None or EFaction.Merc);
    public bool IsCrushableByArmor => IsFlimsy || Kind is EStructureKind.Wall or EStructureKind.Container or EStructureKind.Tower or EStructureKind.AntiAir;
    public bool CanBurn => IsExplosive || Kind is EStructureKind.House or EStructureKind.Shop or EStructureKind.Church or EStructureKind.Barracks or EStructureKind.Warehouse or EStructureKind.Hangar or EStructureKind.Headquarters or EStructureKind.Tower or EStructureKind.Radar;
    public bool Topples => Kind is EStructureKind.Tower or EStructureKind.Antenna or EStructureKind.Silo or EStructureKind.Derrick or EStructureKind.Radar or EStructureKind.Statue;
    public bool HasWindows => Kind is EStructureKind.House or EStructureKind.Shop or EStructureKind.Church or EStructureKind.Barracks or EStructureKind.Headquarters;
    public FVector3 BasePosition => Base;
    public FQuat BaseRotation => Rotation;

    public static float DefaultHealth(EStructureKind Kind) => Kind switch
    {
        EStructureKind.Crate or EStructureKind.Sandbags or EStructureKind.Tent => 60.0f,
        EStructureKind.Shack or EStructureKind.Container => 250.0f,
        EStructureKind.House or EStructureKind.Shop or EStructureKind.Antenna or EStructureKind.Tower => 600.0f,
        EStructureKind.FuelTank or EStructureKind.Derrick or EStructureKind.AntiAir => 300.0f,
        EStructureKind.Wall => 500.0f,
        EStructureKind.Radar or EStructureKind.Silo or EStructureKind.Church => 900.0f,
        EStructureKind.Barracks or EStructureKind.Warehouse or EStructureKind.Statue => 1400.0f,
        EStructureKind.Bunker or EStructureKind.Hangar => 2600.0f,
        EStructureKind.Headquarters => 6000.0f,
        _ => 800.0f,
    };

    public override void OnReady()
    {
        Base = World.GetEntityLocation(Entity);
        Rotation = Registry.Get<STransformComponent>(Entity).GetRotation();
        if (MaxHealth <= 0.0f)
        {
            MaxHealth = DefaultHealth(Kind);
        }

        Health = MaxHealth;
        if (CashValue == 0 && Kind is not (EStructureKind.Wall or EStructureKind.Sandbags or EStructureKind.Helipad or EStructureKind.Dock))
        {
            CashValue = (int)(MaxHealth * 4.0f / 500.0f) * 500;
        }

        if (Kind is EStructureKind.Helipad or EStructureKind.Dock)
        {
            bInvulnerable = true;
        }

        RebuildMesh();

        if (Kind != EStructureKind.Helipad)
        {
            FVector3 Half = ColliderHalf();
            using (new FPhysicsBatchScope(World))
            {
                SBoxColliderComponent Collider = Registry.GetOrAdd<SBoxColliderComponent>(Entity)!;
                Collider.HalfExtent = Half;
                Collider.TranslationOffset = new FVector3(0.0f, Half.Y, 0.0f);
                Collider.bAffectsNavigation = true;
                SRigidBodyComponent Body = Registry.GetOrAdd<SRigidBodyComponent>(Entity)!;
                Body.BodyType = EBodyType.Static;
            }
        }

        Mercs.Structures.Add(this);
        Mercs.Register(this);
        bReady = true;
    }

    private FVector3 ColliderHalf()
    {
        FVector3 Half = Size * 0.5f;
        return Kind switch
        {
            EStructureKind.Tower => new FVector3(Half.X * 0.9f, Half.Y, Half.Z * 0.9f),
            EStructureKind.Antenna => new FVector3(0.6f, Half.Y, 0.6f),
            EStructureKind.Statue => new FVector3(Half.X * 0.7f, Half.Y, Half.Z * 0.7f),
            EStructureKind.Dock => new FVector3(Half.X, 0.4f, Half.Z),
            _ => Half,
        };
    }

    public override void OnDetach()
    {
        Mercs.Structures.Remove(this);
        Mercs.Unregister(this);
    }

    private void RebuildMesh()
    {
        MeshKit Kit = new();
        StructureShapes.Build(Kit, Kind, Size, Tint, Mercs.Factions.Get(OwnerFaction), DamageStage);
        Kit.Commit(Registry, Entity);
    }

    public void TakeHit(in FHit Hit)
    {
        if (!IsAlive || bInvulnerable)
        {
            return;
        }

        if (Hit.Kind != EDamageKind.Fire)
        {
            bLastHitByPlayer = Hit.bByPlayer;
        }

        float Amount = Hit.Kind switch
        {
            EDamageKind.Explosive => Hit.Amount,
            EDamageKind.Crush => IsFlimsy ? Hit.Amount : Hit.Amount * 0.1f,
            EDamageKind.Fire => Hit.Amount * 0.5f,
            _ => IsFlimsy || IsExplosive ? Hit.Amount * 0.25f : 0.0f,
        };

        if (Amount <= 0.0f)
        {
            return;
        }

        Health -= Amount;
        if (Health > 0.0f)
        {
            if (Hit.Kind == EDamageKind.Explosive && HealthFraction < 0.5f)
            {
                Mercs.Fx.Puff(Position + new FVector3(0.0f, Size.Y * 0.3f, 0.0f), MathF.Min(Size.X, 6.0f), 3.0f, true, new FVector3(0.0f, 2.0f, 0.0f));
            }

            UpdateDamageStage();
            return;
        }

        Collapse(Hit);
    }

    // Fire eats a building down to its collapse, and a burning fuel store goes up much faster.
    public void Burn(float DeltaTime)
    {
        float Rate = IsExplosive ? 0.07f : 0.025f;
        TakeHit(new FHit { Amount = MaxHealth * Rate * DeltaTime * 2.0f, Kind = EDamageKind.Fire, Point = Position, Direction = FVector3.Up, bByPlayer = bLastHitByPlayer, Source = Entity.Null });
    }

    private void UpdateDamageStage()
    {
        int Stage = HealthFraction < 0.3f ? 2 : (HealthFraction < 0.65f ? 1 : 0);
        if (Stage <= DamageStage || IsFlimsy)
        {
            return;
        }

        DamageStage = Stage;
        if (!IsExplosive)
        {
            RebuildMesh();
        }

        if (HasWindows)
        {
            Sfx.At(ESfx.GlassShatter, Position, 0.6f, 90.0f, 6.0f, 0.1f);
            Mercs.Fx.Debris(Position, Palette.Glass, 4, 5.0f, 0.2f);
        }

        if (Stage == 2)
        {
            Sfx.At(ESfx.WoodBreak, Position, 0.7f, 150.0f, 8.0f, 0.1f);
            Mercs.Fx.Debris(Base + new FVector3(0.0f, Size.Y * 0.8f, 0.0f), Tint, 4, 6.0f, MathF.Min(0.9f, Size.Y * 0.15f));
        }

        if (CanBurn && (Stage == 2 || IsExplosive))
        {
            Mercs.Destruction.Ignite(this);
        }
    }

    public void ShowRubble()
    {
        Mercs.Fx.Stage(Entity, new FTransform(Base, Rotation, FVector3.One));
        MeshKit Rubble = new();
        StructureShapes.Rubble(Rubble, Size, Tint);
        Rubble.Commit(Registry, Entity);
    }

    private void Collapse(FHit Hit)
    {
        bDestroyed = true;
        FVector3 Center = Position;
        FactionInfo Info = Mercs.Factions.Get(OwnerFaction);

        Registry.Remove<SRigidBodyComponent>(Entity);
        Registry.Remove<SBoxColliderComponent>(Entity);

        int Chunks = Math.Clamp((int)(Size.X * Size.Z * Size.Y / 40.0f), 3, 14);
        Mercs.Fx.Debris(Center, Tint, Chunks, 7.0f + Size.Y, MathF.Min(1.2f, Size.Y * 0.18f));
        if (!IsFlimsy && Kind is not (EStructureKind.Wall or EStructureKind.Bunker or EStructureKind.Statue))
        {
            Mercs.Fx.Debris(Center + new FVector3(0.0f, Size.Y * 0.4f, 0.0f), Palette.RoofTile, Chunks / 2 + 1, 6.0f + Size.Y, MathF.Min(1.0f, Size.Y * 0.14f));
        }

        FVector3 Push = Geo.Flat(Center - Hit.Point);
        if (Push.LengthSquared < 0.01f)
        {
            Push = Hit.Direction;
        }

        if (IsExplosive)
        {
            Explosion.Detonate(Center, MathF.Max(9.0f, Size.X), Kind == EStructureKind.FuelTank ? 600.0f : 320.0f, Hit.bByPlayer ? (IDamageable?)Mercs.Player : null, 1.5f);
            Mercs.Destruction.Sink(this, 0.7f);
        }
        else if (IsFlimsy)
        {
            Sfx.At(ESfx.WoodBreak, Center, 0.7f, 120.0f, 5.0f, 0.12f);
            for (int Index = 0; Index < 3; ++Index)
            {
                Mercs.Fx.Puff(Base + new FVector3(Mercs.Range(-0.4f, 0.4f) * Size.X, Mercs.Range(0.2f, 0.8f), Mercs.Range(-0.4f, 0.4f) * Size.Z), MathF.Min(Size.X, 4.0f), Mercs.Range(2.0f, 3.5f), false, new FVector3(0.0f, 1.0f, 0.0f));
            }

            ShowRubble();
        }
        else if (Topples)
        {
            Mercs.Fx.Explosion(Base + new FVector3(0.0f, 1.0f, 0.0f), 2.5f);
            Mercs.Destruction.Topple(this, Push, Hit.bByPlayer);
        }
        else
        {
            Mercs.Fx.Explosion(Center, MathF.Min(Radius, 5.0f) * 0.6f);
            Mercs.Destruction.Sink(this, 1.2f + Size.Y * 0.08f);
        }

        if (Hit.bByPlayer)
        {
            if (CashValue > 0)
            {
                int Bundles = Math.Clamp(CashValue / 2000, 1, 5);
                for (int Index = 0; Index < Bundles; ++Index)
                {
                    Pickup.SpawnCash(Geo.RandomAround(Base, Radius * 0.6f, Radius + 2.0f) + new FVector3(0.0f, 0.4f, 0.0f), CashValue / Bundles);
                }
            }

            if (IsFactionProperty && OwnerFaction != EFaction.VZ)
            {
                float Penalty = Kind is EStructureKind.Headquarters ? 25.0f : (IsFlimsy ? 2.0f : 7.0f);
                Mercs.Factions.Offense(OwnerFaction, Penalty, Center, $"destroyed {Info.Short} property", false);
            }

            if (Kind == EStructureKind.FuelTank)
            {
                Pickup.Spawn(EPickupKind.Fuel, Geo.RandomAround(Base, Radius + 2.0f, Radius + 5.0f), 60);
            }
        }

        HomeSite?.OnStructureLost(this);
        Mercs.Contracts.OnStructureDestroyed(this, Hit);
    }
}

public static class StructureShapes
{
    private static readonly FVector4 BrokenPane = Palette.Hex(0x1C1C1E);

    public static void Build(MeshKit Kit, EStructureKind Kind, FVector3 Size, FVector4 Tint, FactionInfo Owner, int Damage = 0)
    {
        FVector3 H = Size * 0.5f;
        Tint = Palette.Shade(Tint, 1.0f - 0.14f * Damage);
        bool bRoofGone = Damage >= 2;
        FVector4 Accent = Owner.Id is EFaction.Civilian or EFaction.None ? Palette.Shade(Tint, 0.7f) : Owner.Color;
        FVector4 Door = Palette.WoodDark;

        switch (Kind)
        {
            case EStructureKind.House:
            case EStructureKind.Shop:
            case EStructureKind.Church:
            {
                float WallHeight = Kind == EStructureKind.Church ? Size.Y * 0.6f : Size.Y * 0.7f;
                Kit.Block(FVector3.Zero, new FVector3(Size.X, WallHeight, Size.Z), Tint);
                if (bRoofGone)
                {
                    Beams(Kit, Size, WallHeight);
                }
                else
                {
                    Kit.Gable(new FVector3(0.0f, WallHeight, 0.0f), new FVector3(Size.X + 0.6f, Size.Y - WallHeight, Size.Z + 0.6f), Palette.RoofTile);
                }

                Kit.Block(new FVector3(0.0f, 0.0f, H.Z), new FVector3(1.1f, 2.1f, 0.12f), Door);
                Windows(Kit, Size, WallHeight, 1.3f, Damage);
                if (Kind == EStructureKind.Church)
                {
                    Kit.Block(new FVector3(H.X * 0.6f, 0.0f, H.Z - 1.5f), new FVector3(2.4f, Size.Y * 1.3f, 2.4f), Tint);
                    Kit.Cone(new FVector3(H.X * 0.6f, Size.Y * 1.3f, H.Z - 1.5f), 1.9f, 2.5f, Palette.RoofTile, 4);
                }
                else if (Kind == EStructureKind.Shop)
                {
                    Kit.Box(new FVector3(0.0f, 2.5f, H.Z + 0.9f), new FVector3(H.X * 0.9f, 0.06f, 0.9f), Accent, FQuat.FromEuler(Mathf.Radians(12.0f), 0.0f, 0.0f));
                }

                break;
            }
            case EStructureKind.Shack:
            {
                Kit.Block(FVector3.Zero, new FVector3(Size.X, Size.Y * 0.85f, Size.Z), Palette.Wood);
                Kit.Box(new FVector3(0.0f, Size.Y * 0.9f, 0.0f), new FVector3(H.X + 0.3f, 0.06f, H.Z + 0.3f), Palette.RoofTin, FQuat.FromEuler(Mathf.Radians(8.0f), 0.0f, 0.0f));
                Kit.Block(new FVector3(0.0f, 0.0f, H.Z), new FVector3(0.9f, 1.9f, 0.08f), Door);
                break;
            }
            case EStructureKind.Barracks:
            {
                Kit.Block(FVector3.Zero, Size, Tint);
                Kit.Block(new FVector3(0.0f, Size.Y, 0.0f), new FVector3(Size.X + 0.3f, 0.25f, Size.Z + 0.3f), Palette.ConcreteDark);
                Kit.Block(new FVector3(0.0f, 0.0f, H.Z), new FVector3(1.6f, 2.3f, 0.1f), Door);
                Windows(Kit, Size, Size.Y, 1.1f, Damage);
                Kit.Block(new FVector3(-H.X + 0.6f, Size.Y + 0.25f, -H.Z + 0.6f), new FVector3(0.8f, 0.9f, 0.8f), Palette.Steel);
                Kit.Block(new FVector3(H.X * 0.5f, Size.Y * 0.62f, H.Z + 0.01f), new FVector3(2.2f, 1.2f, 0.05f), Accent);
                break;
            }
            case EStructureKind.Bunker:
            {
                Kit.Block(FVector3.Zero, Size, Palette.Concrete);
                Kit.Block(new FVector3(0.0f, Size.Y * 0.55f, H.Z + 0.02f), new FVector3(Size.X * 0.7f, 0.35f, 0.05f), Palette.Hex(0x111111));
                Kit.Block(new FVector3(0.0f, Size.Y, 0.0f), new FVector3(Size.X * 1.08f, 0.4f, Size.Z * 1.08f), Palette.ConcreteDark);
                for (int Index = -2; Index <= 2; ++Index)
                {
                    Kit.Block(new FVector3(Index * Size.X * 0.2f, 0.0f, H.Z + 1.2f), new FVector3(Size.X * 0.19f, 0.8f, 0.8f), Palette.Sandbag);
                }

                break;
            }
            case EStructureKind.Tower:
            {
                float Deck = Size.Y * 0.75f;
                foreach (float SX in new[] { -1.0f, 1.0f })
                {
                    foreach (float SZ in new[] { -1.0f, 1.0f })
                    {
                        Kit.Tube(new FVector3(SX * H.X * 0.9f, 0.0f, SZ * H.Z * 0.9f), new FVector3(SX * H.X * 0.7f, Deck, SZ * H.Z * 0.7f), 0.15f, 0.12f, Palette.WoodDark, 6);
                    }
                }

                Kit.Block(new FVector3(0.0f, Deck, 0.0f), new FVector3(Size.X * 0.9f, 0.25f, Size.Z * 0.9f), Palette.Wood);
                Kit.Block(new FVector3(0.0f, Deck + 0.25f, 0.0f), new FVector3(Size.X * 0.9f, 1.0f, Size.Z * 0.9f), Palette.Sandbag);
                Kit.Gable(new FVector3(0.0f, Size.Y - 0.6f, 0.0f), new FVector3(Size.X, 0.9f, Size.Z), Palette.RoofTin);
                Kit.Tube(new FVector3(H.X * 0.7f, Deck + 1.2f, H.Z * 0.7f), new FVector3(H.X * 0.7f, Size.Y - 0.5f, H.Z * 0.7f), 0.08f, 0.08f, Palette.WoodDark, 4);
                Kit.Tube(new FVector3(-H.X * 0.7f, Deck + 1.2f, -H.Z * 0.7f), new FVector3(-H.X * 0.7f, Size.Y - 0.5f, -H.Z * 0.7f), 0.08f, 0.08f, Palette.WoodDark, 4);
                break;
            }
            case EStructureKind.Tent:
            {
                Kit.Gable(FVector3.Zero, Size, Tint);
                Kit.Block(new FVector3(0.0f, 0.0f, H.Z - 0.05f), new FVector3(0.8f, Size.Y * 0.6f, 0.05f), Palette.Shade(Tint, 0.5f));
                break;
            }
            case EStructureKind.FuelTank:
            {
                float Radius = MathF.Min(H.X, H.Z);
                Kit.Cylinder(FVector3.Zero, Radius, Size.Y * 0.9f, Palette.Steel, 14);
                Kit.Cone(new FVector3(0.0f, Size.Y * 0.9f, 0.0f), Radius, Size.Y * 0.1f, Palette.Shade(Palette.Steel, 0.85f), 14);
                Kit.Cylinder(new FVector3(0.0f, Size.Y * 0.45f, 0.0f), Radius + 0.03f, Size.Y * 0.12f, Palette.FuelRed, 14);
                Kit.Box(new FVector3(Radius + 0.1f, Size.Y * 0.45f, 0.0f), new FVector3(0.05f, Size.Y * 0.45f, 0.25f), Palette.Gunmetal);
                break;
            }
            case EStructureKind.Radar:
            {
                Kit.Block(FVector3.Zero, new FVector3(Size.X, Size.Y * 0.35f, Size.Z), Palette.Concrete);
                Kit.Tube(new FVector3(0.0f, Size.Y * 0.35f, 0.0f), new FVector3(0.0f, Size.Y * 0.75f, 0.0f), 0.35f, 0.3f, Palette.Steel, 8);
                FVector3 Dish = new(0.0f, Size.Y * 0.8f, 0.0f);
                Kit.Tube(Dish, Dish + new FVector3(0.0f, 0.9f, 1.6f), MathF.Min(H.X, H.Z) * 0.9f, MathF.Min(H.X, H.Z) * 0.25f, Palette.Medic, 14);
                Kit.Block(new FVector3(0.0f, Size.Y * 0.35f, H.Z * 0.6f), new FVector3(Size.X * 0.4f, 0.8f, 0.05f), Accent);
                break;
            }
            case EStructureKind.Antenna:
            {
                Kit.Block(FVector3.Zero, new FVector3(2.0f, 0.5f, 2.0f), Palette.Concrete);
                Kit.Tube(new FVector3(0.0f, 0.5f, 0.0f), new FVector3(0.0f, Size.Y, 0.0f), 0.5f, 0.12f, Palette.Rgb(0.8f, 0.25f, 0.2f), 4);
                for (float Y = 3.0f; Y < Size.Y - 2.0f; Y += 4.0f)
                {
                    float Width = 0.5f * (1.0f - Y / Size.Y) + 0.15f;
                    Kit.Box(new FVector3(0.0f, Y, 0.0f), new FVector3(Width, 0.08f, Width), Palette.Medic);
                }

                break;
            }
            case EStructureKind.Wall:
            {
                Kit.Block(FVector3.Zero, Size, Tint);
                Kit.Block(new FVector3(0.0f, Size.Y, 0.0f), new FVector3(Size.X + 0.1f, 0.15f, Size.Z + 0.1f), Palette.Shade(Tint, 0.8f));
                break;
            }
            case EStructureKind.Crate:
            {
                Kit.Block(FVector3.Zero, Size, Palette.Wood);
                Kit.Block(new FVector3(0.0f, Size.Y * 0.4f, H.Z + 0.01f), new FVector3(Size.X * 0.9f, Size.Y * 0.12f, 0.02f), Palette.WoodDark);
                break;
            }
            case EStructureKind.Warehouse:
            case EStructureKind.Hangar:
            {
                float WallHeight = Size.Y * 0.65f;
                Kit.Block(FVector3.Zero, new FVector3(Size.X, WallHeight, Size.Z), Kind == EStructureKind.Hangar ? Palette.Shade(Palette.Steel, 1.0f - 0.14f * Damage) : Tint);
                if (bRoofGone)
                {
                    Beams(Kit, Size, WallHeight);
                }
                else
                {
                    Kit.Gable(new FVector3(0.0f, WallHeight, 0.0f), new FVector3(Size.Z + 0.4f, Size.Y - WallHeight, Size.X + 0.4f), Palette.RoofTin, 90.0f);
                }
                Kit.Block(new FVector3(0.0f, 0.0f, H.Z + 0.02f), new FVector3(Size.X * 0.55f, WallHeight * 0.8f, 0.05f), Palette.Shade(Palette.Steel, 0.6f));
                Kit.Block(new FVector3(0.0f, WallHeight * 0.85f, H.Z + 0.05f), new FVector3(Size.X * 0.4f, 0.8f, 0.05f), Accent);
                break;
            }
            case EStructureKind.Headquarters:
            {
                Kit.Block(FVector3.Zero, new FVector3(Size.X, Size.Y * 0.55f, Size.Z), Tint);
                Kit.Block(new FVector3(0.0f, Size.Y * 0.55f, 0.0f), new FVector3(Size.X * 0.7f, Size.Y * 0.35f, Size.Z * 0.7f), Palette.Shade(Tint, 0.95f));
                Kit.Block(new FVector3(0.0f, Size.Y * 0.9f, 0.0f), new FVector3(Size.X * 0.75f, 0.3f, Size.Z * 0.75f), Palette.ConcreteDark);
                Kit.Block(new FVector3(0.0f, 0.0f, H.Z + 0.02f), new FVector3(3.0f, 3.2f, 0.1f), Door);
                Kit.Block(new FVector3(0.0f, Size.Y * 0.4f, H.Z + 0.05f), new FVector3(Size.X * 0.5f, 1.8f, 0.08f), Accent);
                for (int Floor = 0; Floor < 3; ++Floor)
                {
                    for (int Column = -3; Column <= 3; ++Column)
                    {
                        if (Floor == 0 && Math.Abs(Column) < 1)
                        {
                            continue;
                        }

                        float Y = 1.4f + Floor * Size.Y * 0.28f;
                        if (Y + 1.2f > Size.Y * 0.9f)
                        {
                            continue;
                        }

                        FVector3 WindowSize = new(1.1f, 1.2f, 0.06f);
                        float X = Column * Size.X * 0.13f;
                        FVector4 Pane = Damage > 0 ? BrokenPane : (Floor >= 2 ? Palette.Glass : Palette.Shade(Palette.Glass, 0.8f));
                        if (MathF.Abs(X) + 0.6f < (Floor >= 2 ? Size.X * 0.35f : H.X))
                        {
                            Kit.Block(new FVector3(X, Y, (Floor >= 2 ? Size.Z * 0.35f : H.Z) + 0.02f), WindowSize, Pane);
                        }
                    }
                }

                Kit.Tube(new FVector3(H.X * 0.6f, Size.Y * 0.92f, 0.0f), new FVector3(H.X * 0.6f, Size.Y + 6.0f, 0.0f), 0.08f, 0.08f, Palette.Steel, 4);
                Kit.Box(new FVector3(H.X * 0.6f + 0.9f, Size.Y + 5.2f, 0.0f), new FVector3(0.9f, 0.6f, 0.03f), Owner.Color);
                break;
            }
            case EStructureKind.Statue:
            {
                Kit.Block(FVector3.Zero, new FVector3(Size.X, Size.Y * 0.35f, Size.Z), Palette.Concrete);
                Kit.Block(new FVector3(0.0f, Size.Y * 0.35f, 0.0f), new FVector3(Size.X * 0.3f, Size.Y * 0.3f, Size.Z * 0.2f), Palette.Hex(0x8C7A3A));
                Kit.Block(new FVector3(0.0f, Size.Y * 0.65f, 0.0f), new FVector3(Size.X * 0.36f, Size.Y * 0.22f, Size.Z * 0.22f), Palette.Hex(0x9C8A4A));
                Kit.Sphere(new FVector3(0.0f, Size.Y * 0.93f, 0.0f), Size.X * 0.11f, Palette.Hex(0x9C8A4A), 8);
                Kit.Tube(new FVector3(Size.X * 0.18f, Size.Y * 0.84f, 0.0f), new FVector3(Size.X * 0.36f, Size.Y * 1.1f, 0.0f), Size.X * 0.05f, Size.X * 0.04f, Palette.Hex(0x9C8A4A), 6);
                break;
            }
            case EStructureKind.Silo:
            {
                float Radius = MathF.Min(H.X, H.Z);
                Kit.Cylinder(FVector3.Zero, Radius, Size.Y * 0.85f, Tint, 14);
                Kit.Cone(new FVector3(0.0f, Size.Y * 0.85f, 0.0f), Radius * 1.05f, Size.Y * 0.15f, Palette.RoofTin, 14);
                break;
            }
            case EStructureKind.Container:
            {
                Kit.Block(FVector3.Zero, Size, Tint);
                for (float X = -H.X + 0.4f; X < H.X; X += 0.6f)
                {
                    Kit.Block(new FVector3(X, 0.0f, H.Z + 0.01f), new FVector3(0.08f, Size.Y, 0.03f), Palette.Shade(Tint, 0.75f));
                }

                break;
            }
            case EStructureKind.Derrick:
            {
                Kit.Block(FVector3.Zero, new FVector3(Size.X, 0.5f, Size.Z), Palette.ConcreteDark);
                foreach (float SX in new[] { -1.0f, 1.0f })
                {
                    foreach (float SZ in new[] { -1.0f, 1.0f })
                    {
                        Kit.Tube(new FVector3(SX * H.X * 0.8f, 0.5f, SZ * H.Z * 0.8f), new FVector3(0.0f, Size.Y, 0.0f), 0.12f, 0.08f, Palette.Rust, 4);
                    }
                }

                Kit.Block(new FVector3(0.0f, 0.5f, 0.0f), new FVector3(1.2f, 1.2f, 1.2f), Palette.Hex(0x222222));
                Kit.Cylinder(new FVector3(H.X + 1.5f, 0.0f, 0.0f), 1.3f, 3.0f, Palette.Hex(0xE0B22A), 10);
                break;
            }
            case EStructureKind.Sandbags:
            {
                for (float X = -H.X; X < H.X - 0.1f; X += 0.9f)
                {
                    Kit.Block(new FVector3(X + 0.45f, 0.0f, 0.0f), new FVector3(0.85f, Size.Y * 0.5f, Size.Z), Palette.Sandbag);
                    Kit.Block(new FVector3(X + 0.9f, Size.Y * 0.5f, 0.0f), new FVector3(0.85f, Size.Y * 0.5f, Size.Z * 0.9f), Palette.Shade(Palette.Sandbag, 0.9f));
                }

                break;
            }
            case EStructureKind.AntiAir:
            {
                Kit.Block(FVector3.Zero, new FVector3(Size.X, 0.8f, Size.Z), Palette.Sandbag);
                Kit.Cylinder(new FVector3(0.0f, 0.8f, 0.0f), 0.9f, 0.8f, Accent, 10);
                Kit.Tube(new FVector3(0.3f, 1.4f, 0.0f), new FVector3(0.3f, 3.6f, 2.0f), 0.1f, 0.08f, Palette.Gunmetal, 6);
                Kit.Tube(new FVector3(-0.3f, 1.4f, 0.0f), new FVector3(-0.3f, 3.6f, 2.0f), 0.1f, 0.08f, Palette.Gunmetal, 6);
                break;
            }
            case EStructureKind.Helipad:
            {
                Kit.Block(new FVector3(0.0f, -0.3f, 0.0f), new FVector3(Size.X, 0.4f, Size.Z), Palette.ConcreteDark);
                Kit.Block(new FVector3(-1.2f, 0.1f, 0.0f), new FVector3(0.5f, 0.02f, 4.0f), Palette.Medic);
                Kit.Block(new FVector3(1.2f, 0.1f, 0.0f), new FVector3(0.5f, 0.02f, 4.0f), Palette.Medic);
                Kit.Block(new FVector3(0.0f, 0.1f, 0.0f), new FVector3(2.4f, 0.02f, 0.5f), Palette.Medic);
                break;
            }
            case EStructureKind.Dock:
            {
                Kit.Block(new FVector3(0.0f, 0.0f, 0.0f), new FVector3(Size.X, 0.5f, Size.Z), Palette.Wood);
                for (float Z = -H.Z + 1.0f; Z < H.Z; Z += 4.0f)
                {
                    Kit.Tube(new FVector3(H.X - 0.3f, -4.0f, Z), new FVector3(H.X - 0.3f, 0.6f, Z), 0.2f, 0.2f, Palette.WoodDark, 6);
                    Kit.Tube(new FVector3(-H.X + 0.3f, -4.0f, Z), new FVector3(-H.X + 0.3f, 0.6f, Z), 0.2f, 0.2f, Palette.WoodDark, 6);
                }

                break;
            }
        }

        if (Damage > 0 && Kind is EStructureKind.House or EStructureKind.Shop or EStructureKind.Church or EStructureKind.Barracks or EStructureKind.Bunker or EStructureKind.Warehouse or EStructureKind.Hangar or EStructureKind.Headquarters or EStructureKind.Container)
        {
            Scorch(Kit, Size, Damage);
        }
    }

    private static void Scorch(MeshKit Kit, FVector3 Size, int Damage)
    {
        FVector3 H = Size * 0.5f;
        Random Rng = new((int)(Size.X * 97 + Size.Z * 31 + Size.Y * 7));
        int Count = Damage * 3 + (int)(Size.X / 5.0f);
        for (int Index = 0; Index < Count; ++Index)
        {
            float Across = ((float)Rng.NextDouble() - 0.5f) * 0.8f;
            float Y = (0.1f + 0.5f * (float)Rng.NextDouble()) * Size.Y;
            FVector3 Half = new(0.6f + (float)Rng.NextDouble(), 0.4f + (float)Rng.NextDouble() * 0.8f, 0.02f);
            FVector4 Color = Palette.Shade(Palette.Wreck, 0.3f + 0.25f * (float)Rng.NextDouble() - 0.1f * Damage);
            switch (Rng.Next(4))
            {
                case 0:
                    Kit.Box(new FVector3(Across * Size.X, Y, H.Z + 0.04f), Half, Color);
                    break;
                case 1:
                    Kit.Box(new FVector3(Across * Size.X, Y, -H.Z - 0.04f), Half, Color);
                    break;
                case 2:
                    Kit.Box(new FVector3(H.X + 0.04f, Y, Across * Size.Z), new FVector3(0.02f, Half.Y, Half.X), Color);
                    break;
                default:
                    Kit.Box(new FVector3(-H.X - 0.04f, Y, Across * Size.Z), new FVector3(0.02f, Half.Y, Half.X), Color);
                    break;
            }
        }
    }

    private static void Beams(MeshKit Kit, FVector3 Size, float WallHeight)
    {
        FVector3 H = Size * 0.5f;
        FVector4 Charred = Palette.Hex(0x2A221C);
        for (float X = -H.X + 0.6f; X < H.X; X += 1.8f)
        {
            Kit.Tube(new FVector3(X, WallHeight, -H.Z), new FVector3(X + 0.3f, WallHeight + 0.6f, 0.0f), 0.12f, 0.12f, Charred, 4);
        }

        Kit.Box(new FVector3(0.0f, WallHeight + 0.05f, 0.0f), new FVector3(H.X, 0.05f, 0.15f), Charred);
    }

    private static void Windows(MeshKit Kit, FVector3 Size, float WallHeight, float Height, int Damage = 0)
    {
        FVector4 Pane = Damage > 0 ? BrokenPane : Palette.Glass;
        FVector3 H = Size * 0.5f;
        int Count = Math.Max(1, (int)(Size.X / 3.5f));
        for (int Index = 0; Index < Count; ++Index)
        {
            float X = -H.X + (Index + 0.5f) * Size.X / Count;
            if (MathF.Abs(X) < 1.0f)
            {
                continue;
            }

            Kit.Block(new FVector3(X, WallHeight * 0.45f, H.Z + 0.02f), new FVector3(1.0f, Height, 0.05f), Pane);
            Kit.Block(new FVector3(X, WallHeight * 0.45f, -H.Z - 0.02f), new FVector3(1.0f, Height, 0.05f), Pane);
        }
    }

    public static void Rubble(MeshKit Kit, FVector3 Size, FVector4 Tint)
    {
        FVector3 H = Size * 0.5f;
        int Pieces = Math.Clamp((int)(Size.X * Size.Z / 6.0f), 3, 14);
        Random Rng = new((int)(Size.X * 131 + Size.Z * 17));
        for (int Index = 0; Index < Pieces; ++Index)
        {
            float X = ((float)Rng.NextDouble() - 0.5f) * Size.X;
            float Z = ((float)Rng.NextDouble() - 0.5f) * Size.Z;
            float W = 0.6f + (float)Rng.NextDouble() * MathF.Min(2.5f, H.X);
            float Tall = 0.3f + (float)Rng.NextDouble() * MathF.Min(1.2f, Size.Y * 0.25f);
            FVector4 Color = Palette.Shade(Rng.NextDouble() < 0.3 ? Palette.Wreck : Tint, 0.5f + (float)Rng.NextDouble() * 0.3f);
            Kit.Box(new FVector3(X, Tall * 0.5f, Z), new FVector3(W * 0.5f, Tall * 0.5f, W * 0.4f), Color, FQuat.FromEuler(0.2f, (float)Rng.NextDouble() * 3.0f, 0.15f));
        }

        for (int Index = 0; Index < Math.Min(Pieces / 3, 4); ++Index)
        {
            FVector3 From = new(((float)Rng.NextDouble() - 0.5f) * Size.X, 0.2f, ((float)Rng.NextDouble() - 0.5f) * Size.Z);
            FVector3 To = From + new FVector3(((float)Rng.NextDouble() - 0.5f) * 4.0f, 0.4f + (float)Rng.NextDouble() * 1.2f, ((float)Rng.NextDouble() - 0.5f) * 4.0f);
            Kit.Tube(From, To, 0.12f, 0.1f, Palette.Hex(0x2A221C), 4);
        }
    }
}
