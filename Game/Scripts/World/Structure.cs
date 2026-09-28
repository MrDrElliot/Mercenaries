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

    // Shells and hits outlive whoever fired them, so a destroyed script answers with no entity rather than throwing.
    public Entity Owner => IsValid ? Entity : Entity.Null;
    public EFaction Faction => OwnerFaction;
    public bool IsAlive => IsValid && bReady && !bDestroyed;
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

    // Bursts into chunks of its own walls from the blast, and the rubble takes its place in the same frame so nothing is drawn twice.
    private void Shatter(FVector3 From, float Strength)
    {
        MeshKit Kit = new();
        StructureShapes.Build(Kit, Kind, Size, Tint, Mercs.Factions.Get(OwnerFaction), DamageStage);
        if (Kit.BuildStaticMesh(World) is { } Snapshot)
        {
            // Surface counts as well as bulk, or a long thin wall breaks into a few slabs the size of the wall itself.
            float Surface = Size.X * Size.Y + Size.Y * Size.Z + Size.X * Size.Z;
            int Pieces = Math.Clamp((int)MathF.Max(Size.X * Size.Y * Size.Z / 30.0f, Surface / 10.0f), 6, 32);
            CDestructionLibrary.ShatterMesh(World, Snapshot, new FTransform(Base, Rotation, FVector3.One), From, Strength, Pieces, 10.0f);
        }

        ShowRubble();
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

        FVector3 Push = Geo.Flat(Center - Hit.Point);
        if (Push.LengthSquared < 0.01f)
        {
            Push = Hit.Direction;
        }

        // Blasts from far off or with no point of their own burst the building from its middle.
        FVector3 BlastPoint = FVector3.Distance(Hit.Point, Center) < Radius * 2.0f ? Hit.Point : Center;

        if (IsExplosive)
        {
            Explosion.Detonate(Center, MathF.Max(9.0f, Size.X), Kind == EStructureKind.FuelTank ? 600.0f : 320.0f, Hit.bByPlayer ? (IDamageable?)Mercs.Player : null, 1.5f);
            Shatter(Center - new FVector3(0.0f, Size.Y * 0.3f, 0.0f), 11.0f);
            Mercs.Destruction.Crumble(this, 1.2f);
        }
        else if (IsFlimsy)
        {
            int Chunks = Math.Clamp((int)(Size.X * Size.Z * Size.Y / 40.0f), 3, 14);
            Mercs.Fx.Debris(Center, Tint, Chunks, 7.0f + Size.Y, MathF.Min(1.2f, Size.Y * 0.18f));
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
            Shatter(BlastPoint, 5.0f + Size.Y * 0.3f);
            Mercs.Destruction.Crumble(this, 1.7f + Size.Y * 0.08f);
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
    private static readonly FVector4 Trim = Palette.Hex(0xE8E2D2);
    private static readonly FVector4 Tank = Palette.Hex(0x1E1F1C);
    private static readonly FVector4[] ShutterColors = { Palette.Hex(0x2F6B5A), Palette.Hex(0x3A5A8C), Palette.Hex(0x8C3A2E), Palette.Hex(0x6E5A2E) };

    private enum EWindowStyle : byte
    {
        Plain,
        Shutters,
        Bars,
    }

    // The four walls of a footprint centered on the origin, each as its outward normal and the direction running along it.
    private static readonly (FVector3 Normal, FVector3 Along)[] Faces =
    {
        (FVector3.Forward, FVector3.Right),
        (-FVector3.Forward, FVector3.Right),
        (FVector3.Right, FVector3.Forward),
        (-FVector3.Right, FVector3.Forward),
    };

    public static void Build(MeshKit Kit, EStructureKind Kind, FVector3 Size, FVector4 Tint, FactionInfo Owner, int Damage = 0)
    {
        FVector3 H = Size * 0.5f;
        Tint = Palette.Shade(Tint, 1.0f - 0.14f * Damage);
        bool bRoofGone = Damage >= 2;
        FVector4 Accent = Owner.Id is EFaction.Civilian or EFaction.None ? Palette.Shade(Tint, 0.7f) : Owner.Color;
        FVector4 Door = Palette.WoodDark;
        // From the footprint, so the same house keeps its shutter color through every damage stage.
        int Seed = (int)(Size.X * 37.0f + Size.Z * 101.0f + Size.Y * 13.0f);

        switch (Kind)
        {
            case EStructureKind.House:
            case EStructureKind.Shop:
            case EStructureKind.Church:
            {
                float WallHeight = Kind == EStructureKind.Church ? Size.Y * 0.6f : Size.Y * 0.7f;
                Plinth(Kit, Size, Palette.Shade(Tint, 0.62f));
                Kit.Block(FVector3.Zero, new FVector3(Size.X, WallHeight, Size.Z), Tint);
                Kit.Block(new FVector3(0.0f, WallHeight - 0.18f, 0.0f), new FVector3(Size.X + 0.14f, 0.18f, Size.Z + 0.14f), Palette.Shade(Tint, 1.12f));
                if (bRoofGone)
                {
                    Beams(Kit, Size, WallHeight);
                }
                else
                {
                    TiledRoof(Kit, Size, WallHeight, Size.Y - WallHeight, Palette.RoofTile);
                    if (Kind == EStructureKind.House && Seed % 3 == 0)
                    {
                        Kit.Block(new FVector3(H.X * 0.5f, WallHeight + (Size.Y - WallHeight) * 0.3f, -H.Z * 0.25f), new FVector3(0.6f, (Size.Y - WallHeight) * 0.9f, 0.6f), Palette.Shade(Tint, 0.9f));
                    }
                }

                FVector4 Shutter = ShutterColors[Math.Abs(Seed) % ShutterColors.Length];
                Doorway(Kit, new FVector3(0.0f, 0.0f, H.Z), FVector3.Forward, FVector3.Right, 1.1f, 2.1f, Door, Trim);
                WindowRows(Kit, Size, WallHeight * 0.52f, 1.0f, 1.25f, Damage, Kind == EStructureKind.House ? EWindowStyle.Shutters : EWindowStyle.Plain, Trim, Shutter);

                if (Kind == EStructureKind.Church)
                {
                    FVector3 Belfry = new(H.X * 0.6f, 0.0f, H.Z - 1.5f);
                    float TowerHeight = Size.Y * 1.3f;
                    Kit.Block(Belfry, new FVector3(2.4f, TowerHeight, 2.4f), Tint);
                    Kit.Block(Belfry + new FVector3(0.0f, TowerHeight - 0.2f, 0.0f), new FVector3(2.6f, 0.2f, 2.6f), Palette.Shade(Tint, 1.12f));
                    foreach ((FVector3 Normal, FVector3 Along) in Faces)
                    {
                        Kit.Box(Belfry + new FVector3(0.0f, TowerHeight - 1.3f, 0.0f) + Normal * 1.21f, AxisHalf(Along, 0.35f, 0.7f, Normal, 0.02f), Palette.Hex(0x16140F));
                    }
                    Kit.Cone(Belfry + new FVector3(0.0f, TowerHeight, 0.0f), 1.9f, 2.5f, Palette.RoofTile, 4);
                    FVector3 Cross = Belfry + new FVector3(0.0f, TowerHeight + 2.9f, 0.0f);
                    Kit.Box(Cross, new FVector3(0.05f, 0.45f, 0.05f), Trim);
                    Kit.Box(Cross + new FVector3(0.0f, 0.12f, 0.0f), new FVector3(0.25f, 0.05f, 0.05f), Trim);
                    Kit.Tube(new FVector3(0.0f, WallHeight + 0.9f, H.Z - 0.05f), new FVector3(0.0f, WallHeight + 0.9f, H.Z + 0.06f), 0.55f, 0.55f, Palette.Glass, 12);
                }
                else if (Kind == EStructureKind.Shop)
                {
                    Awning(Kit, new FVector3(0.0f, 2.55f, H.Z), H.X * 0.9f, Accent);
                    Kit.Box(new FVector3(0.0f, 3.05f, H.Z + 0.06f), new FVector3(H.X * 0.55f, 0.28f, 0.04f), Trim);
                    Kit.Box(new FVector3(0.0f, 3.05f, H.Z + 0.1f), new FVector3(H.X * 0.45f, 0.14f, 0.01f), Accent);
                }

                break;
            }
            case EStructureKind.Shack:
            {
                Kit.Block(FVector3.Zero, new FVector3(Size.X, Size.Y * 0.85f, Size.Z), Palette.Wood);
                for (float X = -H.X + 0.35f; X < H.X; X += 0.35f)
                {
                    foreach (float Side in new[] { -1.0f, 1.0f })
                    {
                        Kit.Box(new FVector3(X, Size.Y * 0.425f, Side * (H.Z + 0.01f)), new FVector3(0.015f, Size.Y * 0.42f, 0.01f), Palette.WoodDark);
                    }
                }
                FQuat Pitch = FQuat.FromEuler(Mathf.Radians(8.0f), 0.0f, 0.0f);
                Kit.Box(new FVector3(0.0f, Size.Y * 0.9f, 0.0f), new FVector3(H.X + 0.3f, 0.05f, H.Z + 0.3f), Palette.RoofTin, Pitch);
                for (float X = -H.X - 0.2f; X <= H.X + 0.2f; X += 0.3f)
                {
                    Kit.Box(new FVector3(X, Size.Y * 0.9f + 0.05f, 0.0f), new FVector3(0.03f, 0.02f, H.Z + 0.3f), (int)MathF.Round(X / 0.3f) % 5 == 0 ? Palette.Shade(Palette.Rust, 0.9f) : Palette.Shade(Palette.RoofTin, 0.88f), Pitch);
                }
                Doorway(Kit, new FVector3(0.0f, 0.0f, H.Z), FVector3.Forward, FVector3.Right, 0.9f, 1.9f, Door, Palette.WoodDark);
                break;
            }
            case EStructureKind.Barracks:
            {
                Plinth(Kit, Size, Palette.ConcreteDark);
                Kit.Block(FVector3.Zero, Size, Tint);
                FlatRoof(Kit, Size, Size.Y, Palette.ConcreteDark, Seed, true);
                Doorway(Kit, new FVector3(0.0f, 0.0f, H.Z), FVector3.Forward, FVector3.Right, 1.6f, 2.3f, Palette.Gunmetal, Palette.ConcreteDark);
                Kit.Box(new FVector3(0.0f, 2.55f, H.Z + 0.6f), new FVector3(1.3f, 0.06f, 0.6f), Palette.ConcreteDark);
                WindowRows(Kit, Size, Size.Y * 0.55f, 1.0f, 1.0f, Damage, EWindowStyle.Bars, Palette.ConcreteDark, Palette.Gunmetal);
                Kit.Block(new FVector3(H.X * 0.5f, Size.Y * 0.62f, H.Z + 0.01f), new FVector3(2.2f, 1.2f, 0.05f), Accent);
                break;
            }
            case EStructureKind.Bunker:
            {
                Kit.Block(FVector3.Zero, Size, Palette.Concrete);
                Kit.Block(new FVector3(0.0f, Size.Y * 0.55f, H.Z + 0.02f), new FVector3(Size.X * 0.7f, 0.35f, 0.05f), Palette.Hex(0x111111));
                Kit.Block(new FVector3(0.0f, Size.Y * 0.55f + 0.35f, H.Z + 0.15f), new FVector3(Size.X * 0.74f, 0.08f, 0.3f), Palette.ConcreteDark);
                Kit.Block(new FVector3(0.0f, Size.Y, 0.0f), new FVector3(Size.X * 1.08f, 0.4f, Size.Z * 1.08f), Palette.ConcreteDark);
                Bags(Kit, new FVector3(0.0f, 0.0f, H.Z + 1.2f), Size.X * 0.95f, 3, Palette.Sandbag);
                break;
            }
            case EStructureKind.Tower:
            {
                float Deck = Size.Y * 0.75f;
                FVector3[] Feet = new FVector3[4];
                FVector3[] Tops = new FVector3[4];
                int Leg = 0;
                foreach (float SX in new[] { -1.0f, 1.0f })
                {
                    foreach (float SZ in new[] { -1.0f, 1.0f })
                    {
                        Feet[Leg] = new FVector3(SX * H.X * 0.9f, 0.0f, SZ * H.Z * 0.9f);
                        Tops[Leg] = new FVector3(SX * H.X * 0.7f, Deck, SZ * H.Z * 0.7f);
                        Kit.Tube(Feet[Leg], Tops[Leg], 0.15f, 0.12f, Palette.WoodDark, 6);
                        Leg++;
                    }
                }
                // Cross bracing on every side between the legs, which is what makes it read as a built tower.
                int[,] Pairs = { { 0, 1 }, { 2, 3 }, { 0, 2 }, { 1, 3 } };
                for (int Pair = 0; Pair < 4; ++Pair)
                {
                    int A = Pairs[Pair, 0];
                    int B = Pairs[Pair, 1];
                    FVector3 LowA = FVector3.Lerp(Feet[A], Tops[A], 0.12f);
                    FVector3 LowB = FVector3.Lerp(Feet[B], Tops[B], 0.12f);
                    FVector3 HighA = FVector3.Lerp(Feet[A], Tops[A], 0.88f);
                    FVector3 HighB = FVector3.Lerp(Feet[B], Tops[B], 0.88f);
                    Kit.Tube(LowA, HighB, 0.06f, 0.06f, Palette.Wood, 4);
                    Kit.Tube(LowB, HighA, 0.06f, 0.06f, Palette.Wood, 4);
                    Kit.Tube(FVector3.Lerp(Feet[A], Tops[A], 0.5f), FVector3.Lerp(Feet[B], Tops[B], 0.5f), 0.05f, 0.05f, Palette.Wood, 4);
                }
                for (float Y = 0.4f; Y < Deck; Y += 0.4f)
                {
                    Kit.Box(new FVector3(0.0f, Y, H.Z * 0.9f + 0.2f), new FVector3(0.3f, 0.03f, 0.03f), Palette.WoodDark);
                }
                Kit.Block(new FVector3(0.0f, Deck, 0.0f), new FVector3(Size.X * 0.95f, 0.25f, Size.Z * 0.95f), Palette.Wood);
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
                foreach (float SX in new[] { -1.0f, 1.0f })
                {
                    foreach (float SZ in new[] { -1.0f, 1.0f })
                    {
                        Kit.Tube(new FVector3(SX * H.X * 0.5f, Size.Y * 0.4f, SZ * H.Z), new FVector3(SX * (H.X + 0.8f), 0.0f, SZ * (H.Z + 0.4f)), 0.012f, 0.012f, Palette.Hex(0xC8BFA0), 3);
                    }
                }
                break;
            }
            case EStructureKind.FuelTank:
            {
                float Radius = MathF.Min(H.X, H.Z);
                float Wall = Size.Y * 0.9f;
                Kit.Cylinder(FVector3.Zero, Radius + 0.15f, 0.25f, Palette.ConcreteDark, 16);
                Kit.Cylinder(FVector3.Zero, Radius, Wall, Palette.Steel, 18);
                Kit.Cone(new FVector3(0.0f, Wall, 0.0f), Radius, Size.Y * 0.1f, Palette.Shade(Palette.Steel, 0.85f), 18);
                Kit.Cylinder(new FVector3(0.0f, Size.Y * 0.45f, 0.0f), Radius + 0.03f, Size.Y * 0.12f, Palette.FuelRed, 18);
                for (float Y = Wall * 0.2f; Y < Wall; Y += Wall * 0.2f)
                {
                    Kit.Cylinder(new FVector3(0.0f, Y, 0.0f), Radius + 0.02f, 0.05f, Palette.Shade(Palette.Steel, 0.8f), 18);
                }
                // Stairs winding up the shell to a railing round the roof.
                int Steps = Math.Max(8, (int)(Wall / 0.3f));
                for (int Step = 0; Step < Steps; ++Step)
                {
                    float Angle = Step * 0.09f;
                    float Y = (Step + 0.5f) * Wall / Steps;
                    FVector3 Out = new(MathF.Cos(Angle), 0.0f, MathF.Sin(Angle));
                    Kit.Box(Out * (Radius + 0.35f) + new FVector3(0.0f, Y, 0.0f), new FVector3(0.28f, 0.03f, 0.28f), Palette.Gunmetal);
                }
                for (int Post = 0; Post < 12; ++Post)
                {
                    float Angle = Post * MathF.Tau / 12.0f;
                    FVector3 Out = new(MathF.Cos(Angle), 0.0f, MathF.Sin(Angle));
                    Kit.Tube(Out * (Radius - 0.1f) + new FVector3(0.0f, Wall, 0.0f), Out * (Radius - 0.1f) + new FVector3(0.0f, Wall + 0.9f, 0.0f), 0.025f, 0.025f, Palette.Gunmetal, 4);
                }
                Kit.Tube(new FVector3(Radius, 0.5f, 0.0f), new FVector3(Radius + 2.0f, 0.5f, 0.0f), 0.15f, 0.15f, Palette.Gunmetal, 8);
                Kit.Tube(new FVector3(Radius + 0.8f, 0.5f, -0.05f), new FVector3(Radius + 0.8f, 0.5f, -0.3f), 0.25f, 0.25f, Palette.FuelRed, 8);
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
                bool bAlongX = Size.X >= Size.Z;
                float Length = bAlongX ? Size.X : Size.Z;
                int Pillars = Math.Max(2, (int)(Length / 3.0f) + 1);
                for (int Index = 0; Index < Pillars; ++Index)
                {
                    float T = -Length * 0.5f + Index * Length / (Pillars - 1);
                    FVector3 At = bAlongX ? new FVector3(T, 0.0f, 0.0f) : new FVector3(0.0f, 0.0f, T);
                    Kit.Block(At, new FVector3(bAlongX ? 0.4f : Size.X + 0.12f, Size.Y + 0.12f, bAlongX ? Size.Z + 0.12f : 0.4f), Palette.Shade(Tint, 0.9f));
                }
                Kit.Block(new FVector3(0.0f, Size.Y, 0.0f), new FVector3(Size.X + 0.1f, 0.12f, Size.Z + 0.1f), Palette.Shade(Tint, 0.8f));
                break;
            }
            case EStructureKind.Crate:
            {
                Kit.Block(FVector3.Zero, Size, Palette.Wood);
                foreach ((FVector3 Normal, FVector3 Along) in Faces)
                {
                    float Across = MathF.Abs(Along.X) > 0.5f ? H.X : H.Z;
                    float Out = MathF.Abs(Normal.X) > 0.5f ? H.X : H.Z;
                    FVector3 Center = Normal * (Out + 0.02f) + new FVector3(0.0f, H.Y, 0.0f);
                    Kit.Box(Center + FVector3.Up * (H.Y - 0.06f), AxisHalf(Along, Across, 0.06f, Normal, 0.02f), Palette.WoodDark);
                    Kit.Box(Center - FVector3.Up * (H.Y - 0.06f), AxisHalf(Along, Across, 0.06f, Normal, 0.02f), Palette.WoodDark);
                    Kit.Box(Center + Along * (Across - 0.06f), AxisHalf(Along, 0.06f, H.Y, Normal, 0.02f), Palette.WoodDark);
                    Kit.Box(Center - Along * (Across - 0.06f), AxisHalf(Along, 0.06f, H.Y, Normal, 0.02f), Palette.WoodDark);
                }
                break;
            }
            case EStructureKind.Warehouse:
            {
                float WallHeight = Size.Y * 0.65f;
                Plinth(Kit, Size, Palette.ConcreteDark);
                Kit.Block(FVector3.Zero, new FVector3(Size.X, WallHeight, Size.Z), Tint);
                Corrugate(Kit, Size, WallHeight, Palette.Shade(Tint, 0.82f));
                if (bRoofGone)
                {
                    Beams(Kit, Size, WallHeight);
                }
                else
                {
                    Kit.Gable(new FVector3(0.0f, WallHeight, 0.0f), new FVector3(Size.Z + 0.4f, Size.Y - WallHeight, Size.X + 0.4f), Palette.RoofTin, 90.0f);
                    Skylights(Kit, Size, WallHeight, Size.Y - WallHeight);
                }
                RollerDoor(Kit, new FVector3(0.0f, 0.0f, H.Z), Size.X * 0.55f, WallHeight * 0.8f);
                Doorway(Kit, new FVector3(H.X * 0.72f, 0.0f, H.Z), FVector3.Forward, FVector3.Right, 0.9f, 2.1f, Palette.Gunmetal, Palette.ConcreteDark);
                Kit.Block(new FVector3(0.0f, WallHeight * 0.85f, H.Z + 0.05f), new FVector3(Size.X * 0.4f, 0.8f, 0.05f), Accent);
                break;
            }
            case EStructureKind.Hangar:
            {
                float WallHeight = Size.Y * 0.45f;
                FVector4 Shell = Palette.Shade(Palette.Steel, 1.0f - 0.14f * Damage);
                Plinth(Kit, Size, Palette.ConcreteDark);
                Kit.Block(FVector3.Zero, new FVector3(Size.X, WallHeight, Size.Z), Shell);
                if (bRoofGone)
                {
                    Beams(Kit, Size, WallHeight);
                }
                else
                {
                    Vault(Kit, H.X, Size.Y - WallHeight, H.Z, WallHeight, Shell, Palette.Shade(Shell, 0.85f));
                }
                RollerDoor(Kit, new FVector3(0.0f, 0.0f, H.Z), Size.X * 0.75f, WallHeight + (Size.Y - WallHeight) * 0.55f);
                Kit.Block(new FVector3(0.0f, Size.Y * 0.82f, H.Z + 0.05f), new FVector3(Size.X * 0.3f, 0.8f, 0.05f), Accent);
                break;
            }
            case EStructureKind.Headquarters:
            {
                float Lower = Size.Y * 0.55f;
                Plinth(Kit, Size, Palette.ConcreteDark);
                Kit.Block(FVector3.Zero, new FVector3(Size.X, Lower, Size.Z), Tint);
                Kit.Block(new FVector3(0.0f, Lower - 0.25f, 0.0f), new FVector3(Size.X + 0.3f, 0.25f, Size.Z + 0.3f), Palette.Shade(Tint, 1.1f));
                Kit.Block(new FVector3(0.0f, Lower, 0.0f), new FVector3(Size.X * 0.7f, Size.Y * 0.35f, Size.Z * 0.7f), Palette.Shade(Tint, 0.95f));
                FlatRoof(Kit, new FVector3(Size.X * 0.7f, Size.Y, Size.Z * 0.7f), Size.Y * 0.9f, Palette.ConcreteDark, Seed, true);
                FlatRoofEdge(Kit, Size, Lower, Palette.ConcreteDark);

                // A columned porch over the entrance, the one grand touch a faction headquarters gets.
                Kit.Box(new FVector3(0.0f, 3.5f, H.Z + 1.3f), new FVector3(2.4f, 0.12f, 1.3f), Palette.Shade(Tint, 1.1f));
                foreach (float X in new[] { -2.0f, -0.7f, 0.7f, 2.0f })
                {
                    Kit.Tube(new FVector3(X, 0.2f, H.Z + 2.35f), new FVector3(X, 3.4f, H.Z + 2.35f), 0.18f, 0.16f, Trim, 10);
                }
                Kit.Block(new FVector3(0.0f, 0.0f, H.Z + 1.3f), new FVector3(5.0f, 0.2f, 2.6f), Palette.ConcreteDark);
                Doorway(Kit, new FVector3(0.0f, 0.0f, H.Z), FVector3.Forward, FVector3.Right, 2.4f, 2.9f, Door, Trim);
                Kit.Block(new FVector3(0.0f, Lower * 0.72f, H.Z + 0.05f), new FVector3(Size.X * 0.5f, 1.4f, 0.08f), Accent);

                FVector4 Pane = Damage > 0 ? BrokenPane : Palette.Glass;
                for (int Floor = 0; Floor < 3; ++Floor)
                {
                    float Y = 1.4f + Floor * Size.Y * 0.28f;
                    bool bUpper = Floor >= 2;
                    FVector3 Block = bUpper ? new FVector3(Size.X * 0.7f, 0.0f, Size.Z * 0.7f) : Size;
                    if (Y + 1.2f > (bUpper ? Size.Y * 0.9f : Lower - 0.3f))
                    {
                        continue;
                    }
                    for (int Column = -3; Column <= 3; ++Column)
                    {
                        float X = Column * Size.X * 0.13f;
                        if (MathF.Abs(X) + 0.7f > Block.X * 0.5f || (Floor == 0 && MathF.Abs(Column) < 1))
                        {
                            continue;
                        }
                        Window(Kit, new FVector3(X, Y + 0.6f, Block.Z * 0.5f), FVector3.Forward, FVector3.Right, 1.1f, 1.2f, Pane, Trim, EWindowStyle.Plain, Trim);
                        Window(Kit, new FVector3(X, Y + 0.6f, -Block.Z * 0.5f), -FVector3.Forward, FVector3.Right, 1.1f, 1.2f, Pane, Trim, EWindowStyle.Plain, Trim);
                    }
                }

                Kit.Tube(new FVector3(H.X * 0.6f, Size.Y * 0.92f, 0.0f), new FVector3(H.X * 0.6f, Size.Y + 6.0f, 0.0f), 0.08f, 0.08f, Palette.Steel, 4);
                Kit.Box(new FVector3(H.X * 0.6f + 0.9f, Size.Y + 5.2f, 0.0f), new FVector3(0.9f, 0.6f, 0.03f), Owner.Color);
                break;
            }
            case EStructureKind.Statue:
            {
                Kit.Block(FVector3.Zero, new FVector3(Size.X, Size.Y * 0.35f, Size.Z), Palette.Concrete);
                Kit.Block(new FVector3(0.0f, Size.Y * 0.35f - 0.2f, 0.0f), new FVector3(Size.X + 0.2f, 0.2f, Size.Z + 0.2f), Palette.Shade(Palette.Concrete, 1.1f));
                Kit.Block(new FVector3(0.0f, Size.Y * 0.35f, 0.0f), new FVector3(Size.X * 0.3f, Size.Y * 0.3f, Size.Z * 0.2f), Palette.Hex(0x8C7A3A));
                Kit.Block(new FVector3(0.0f, Size.Y * 0.65f, 0.0f), new FVector3(Size.X * 0.36f, Size.Y * 0.22f, Size.Z * 0.22f), Palette.Hex(0x9C8A4A));
                Kit.Sphere(new FVector3(0.0f, Size.Y * 0.93f, 0.0f), Size.X * 0.11f, Palette.Hex(0x9C8A4A), 8);
                Kit.Tube(new FVector3(Size.X * 0.18f, Size.Y * 0.84f, 0.0f), new FVector3(Size.X * 0.36f, Size.Y * 1.1f, 0.0f), Size.X * 0.05f, Size.X * 0.04f, Palette.Hex(0x9C8A4A), 6);
                break;
            }
            case EStructureKind.Silo:
            {
                float Radius = MathF.Min(H.X, H.Z);
                Kit.Cylinder(FVector3.Zero, Radius, Size.Y * 0.85f, Tint, 16);
                for (float Y = 1.2f; Y < Size.Y * 0.85f; Y += 1.6f)
                {
                    Kit.Cylinder(new FVector3(0.0f, Y, 0.0f), Radius + 0.03f, 0.08f, Palette.Shade(Tint, 0.8f), 16);
                }
                Kit.Cone(new FVector3(0.0f, Size.Y * 0.85f, 0.0f), Radius * 1.05f, Size.Y * 0.15f, Palette.RoofTin, 16);
                for (int Side = -1; Side <= 1; Side += 2)
                {
                    Kit.Box(new FVector3(Side * 0.22f, Size.Y * 0.43f, Radius + 0.2f), new FVector3(0.03f, Size.Y * 0.43f, 0.03f), Palette.Gunmetal);
                }
                for (float Y = 0.3f; Y < Size.Y * 0.85f; Y += 0.35f)
                {
                    Kit.Box(new FVector3(0.0f, Y, Radius + 0.2f), new FVector3(0.22f, 0.02f, 0.02f), Palette.Gunmetal);
                }
                break;
            }
            case EStructureKind.Container:
            {
                Kit.Block(FVector3.Zero, Size, Tint);
                FVector4 Rib = Palette.Shade(Tint, 0.75f);
                foreach ((FVector3 Normal, FVector3 Along) in Faces)
                {
                    float Across = MathF.Abs(Along.X) > 0.5f ? H.X : H.Z;
                    float Out = MathF.Abs(Normal.X) > 0.5f ? H.X : H.Z;
                    for (float T = -Across + 0.3f; T < Across - 0.1f; T += 0.3f)
                    {
                        Kit.Box(Normal * (Out + 0.012f) + Along * T + new FVector3(0.0f, H.Y, 0.0f), AxisHalf(Along, 0.05f, H.Y - 0.08f, Normal, 0.012f), Rib);
                    }
                }
                bool bLong = Size.X >= Size.Z;
                FVector3 End = bLong ? FVector3.Right : FVector3.Forward;
                FVector3 EndAlong = bLong ? FVector3.Forward : FVector3.Right;
                float EndOut = bLong ? H.X : H.Z;
                float EndAcross = bLong ? H.Z : H.X;
                foreach (float T in new[] { -0.55f, -0.2f, 0.2f, 0.55f })
                {
                    Kit.Box(End * (EndOut + 0.04f) + EndAlong * (T * EndAcross) + new FVector3(0.0f, H.Y, 0.0f), AxisHalf(EndAlong, 0.025f, H.Y - 0.1f, End, 0.025f), Palette.Gunmetal);
                }
                foreach (float SX in new[] { -1.0f, 1.0f })
                {
                    foreach (float SZ in new[] { -1.0f, 1.0f })
                    {
                        Kit.Block(new FVector3(SX * (H.X - 0.08f), 0.0f, SZ * (H.Z - 0.08f)), new FVector3(0.2f, Size.Y + 0.02f, 0.2f), Palette.Shade(Tint, 0.65f));
                    }
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
                for (float Y = 3.0f; Y < Size.Y - 1.0f; Y += 3.0f)
                {
                    float Reach = (1.0f - (Y - 0.5f) / (Size.Y - 0.5f)) * 0.8f;
                    Kit.Box(new FVector3(0.0f, Y, 0.0f), new FVector3(H.X * Reach, 0.05f, H.Z * Reach), Palette.Rust);
                }

                Kit.Block(new FVector3(0.0f, 0.5f, 0.0f), new FVector3(1.2f, 1.2f, 1.2f), Palette.Hex(0x222222));
                Kit.Cylinder(new FVector3(H.X + 1.5f, 0.0f, 0.0f), 1.3f, 3.0f, Palette.Hex(0xE0B22A), 10);
                break;
            }
            case EStructureKind.Sandbags:
            {
                Bags(Kit, FVector3.Zero, Size.X, Math.Max(2, (int)(Size.Y / 0.28f)), Palette.Sandbag);
                break;
            }
            case EStructureKind.AntiAir:
            {
                Bags(Kit, FVector3.Zero, Size.X, 3, Palette.Sandbag);
                Kit.Cylinder(new FVector3(0.0f, 0.8f, 0.0f), 0.9f, 0.8f, Accent, 10);
                Kit.Box(new FVector3(0.0f, 1.9f, 0.3f), new FVector3(0.55f, 0.35f, 0.5f), Palette.Shade(Accent, 0.8f));
                Kit.Tube(new FVector3(0.3f, 1.9f, 0.3f), new FVector3(0.3f, 3.6f, 2.0f), 0.1f, 0.08f, Palette.Gunmetal, 6);
                Kit.Tube(new FVector3(-0.3f, 1.9f, 0.3f), new FVector3(-0.3f, 3.6f, 2.0f), 0.1f, 0.08f, Palette.Gunmetal, 6);
                break;
            }
            case EStructureKind.Helipad:
            {
                Kit.Block(new FVector3(0.0f, -0.3f, 0.0f), new FVector3(Size.X, 0.4f, Size.Z), Palette.ConcreteDark);
                Kit.Tube(new FVector3(0.0f, 0.1f, 0.0f), new FVector3(0.0f, 0.11f, 0.0f), MathF.Min(H.X, H.Z) * 0.8f, MathF.Min(H.X, H.Z) * 0.8f, Palette.Hex(0xE0C23A), 24, false);
                Kit.Tube(new FVector3(0.0f, 0.105f, 0.0f), new FVector3(0.0f, 0.115f, 0.0f), MathF.Min(H.X, H.Z) * 0.74f, MathF.Min(H.X, H.Z) * 0.74f, Palette.ConcreteDark, 24);
                Kit.Block(new FVector3(-1.2f, 0.1f, 0.0f), new FVector3(0.5f, 0.03f, 4.0f), Palette.Medic);
                Kit.Block(new FVector3(1.2f, 0.1f, 0.0f), new FVector3(0.5f, 0.03f, 4.0f), Palette.Medic);
                Kit.Block(new FVector3(0.0f, 0.1f, 0.0f), new FVector3(2.4f, 0.03f, 0.5f), Palette.Medic);
                break;
            }
            case EStructureKind.Dock:
            {
                Kit.Block(new FVector3(0.0f, 0.0f, 0.0f), new FVector3(Size.X, 0.5f, Size.Z), Palette.Wood);
                for (float Z = -H.Z + 0.25f; Z < H.Z; Z += 0.5f)
                {
                    Kit.Box(new FVector3(0.0f, 0.505f, Z), new FVector3(H.X, 0.005f, 0.02f), Palette.WoodDark);
                }
                for (float Z = -H.Z + 1.0f; Z < H.Z; Z += 4.0f)
                {
                    Kit.Tube(new FVector3(H.X - 0.3f, -4.0f, Z), new FVector3(H.X - 0.3f, 0.9f, Z), 0.2f, 0.2f, Palette.WoodDark, 6);
                    Kit.Tube(new FVector3(-H.X + 0.3f, -4.0f, Z), new FVector3(-H.X + 0.3f, 0.9f, Z), 0.2f, 0.2f, Palette.WoodDark, 6);
                }

                break;
            }
        }

        if (Damage > 0 && Kind is EStructureKind.House or EStructureKind.Shop or EStructureKind.Church or EStructureKind.Barracks or EStructureKind.Bunker or EStructureKind.Warehouse or EStructureKind.Hangar or EStructureKind.Headquarters or EStructureKind.Container)
        {
            Scorch(Kit, Size, Damage);
        }
    }

    // Half extents for a box laid on a wall, given sizes along the wall, up it and out of it.
    private static FVector3 AxisHalf(FVector3 Along, float AlongHalf, float UpHalf, FVector3 Normal, float OutHalf)
        => FVector3.Abs(Along) * AlongHalf + FVector3.Up * UpHalf + FVector3.Abs(Normal) * OutHalf;

    private static void Plinth(MeshKit Kit, FVector3 Size, FVector4 Color)
        => Kit.Block(FVector3.Zero, new FVector3(Size.X + 0.14f, 0.45f, Size.Z + 0.14f), Color);

    // A framed pane with a sill and lintel, optionally flanked by shutters or crossed by bars.
    private static void Window(MeshKit Kit, FVector3 Center, FVector3 Normal, FVector3 Along, float Width, float Height, FVector4 Pane, FVector4 Frame, EWindowStyle Style, FVector4 Extra)
    {
        float W = Width * 0.5f;
        float V = Height * 0.5f;
        Kit.Box(Center + Normal * 0.02f, AxisHalf(Along, W, V, Normal, 0.02f), Pane);
        Kit.Box(Center + Normal * 0.05f + FVector3.Up * (V + 0.04f), AxisHalf(Along, W + 0.08f, 0.05f, Normal, 0.05f), Frame);
        Kit.Box(Center + Normal * 0.1f - FVector3.Up * (V + 0.05f), AxisHalf(Along, W + 0.12f, 0.05f, Normal, 0.1f), Frame);
        Kit.Box(Center + Normal * 0.05f + Along * (W + 0.04f), AxisHalf(Along, 0.05f, V, Normal, 0.05f), Frame);
        Kit.Box(Center + Normal * 0.05f - Along * (W + 0.04f), AxisHalf(Along, 0.05f, V, Normal, 0.05f), Frame);
        Kit.Box(Center + Normal * 0.05f, AxisHalf(Along, 0.025f, V, Normal, 0.03f), Frame);

        if (Style == EWindowStyle.Shutters)
        {
            foreach (float Side in new[] { -1.0f, 1.0f })
            {
                FVector3 At = Center + Normal * 0.06f + Along * (Side * (W + 0.1f + W * 0.5f));
                Kit.Box(At, AxisHalf(Along, W * 0.5f, V + 0.02f, Normal, 0.025f), Extra);
                for (float Y = -V + 0.12f; Y < V; Y += 0.16f)
                {
                    Kit.Box(At + Normal * 0.03f + FVector3.Up * Y, AxisHalf(Along, W * 0.45f, 0.012f, Normal, 0.01f), Palette.Shade(Extra, 0.75f));
                }
            }
        }
        else if (Style == EWindowStyle.Bars)
        {
            for (float T = -W + 0.15f; T < W; T += 0.15f)
            {
                Kit.Box(Center + Normal * 0.07f + Along * T, AxisHalf(Along, 0.012f, V, Normal, 0.012f), Extra);
            }
        }
    }

    // Windows spaced along every wall at one height, the front skipping the middle where the door stands.
    private static void WindowRows(MeshKit Kit, FVector3 Size, float Y, float Width, float Height, int Damage, EWindowStyle Style, FVector4 Frame, FVector4 Extra)
    {
        FVector4 Pane = Damage > 0 ? BrokenPane : Palette.Glass;
        FVector3 H = Size * 0.5f;
        float Spacing = Style == EWindowStyle.Shutters ? 3.8f : 3.2f;
        for (int FaceIndex = 0; FaceIndex < Faces.Length; ++FaceIndex)
        {
            (FVector3 Normal, FVector3 Along) = Faces[FaceIndex];
            float Length = MathF.Abs(Along.X) > 0.5f ? Size.X : Size.Z;
            float Out = MathF.Abs(Normal.X) > 0.5f ? H.X : H.Z;
            int Count = Math.Max(1, (int)(Length / Spacing));
            for (int Index = 0; Index < Count; ++Index)
            {
                float T = -Length * 0.5f + (Index + 0.5f) * Length / Count;
                if (FaceIndex == 0 && MathF.Abs(T) < 1.2f)
                {
                    continue;
                }
                if (MathF.Abs(T) + Width * (Style == EWindowStyle.Shutters ? 1.2f : 0.6f) > Length * 0.5f - 0.2f)
                {
                    continue;
                }
                Window(Kit, Normal * Out + Along * T + new FVector3(0.0f, Y, 0.0f), Normal, Along, Width, Height, Pane, Frame, Style, Extra);
            }
        }
    }

    private static void Doorway(MeshKit Kit, FVector3 Base, FVector3 Normal, FVector3 Along, float Width, float Height, FVector4 Door, FVector4 Frame)
    {
        float W = Width * 0.5f;
        Kit.Box(Base + Normal * 0.04f + FVector3.Up * (Height * 0.5f), AxisHalf(Along, W, Height * 0.5f, Normal, 0.04f), Door);
        Kit.Box(Base + Normal * 0.06f + FVector3.Up * (Height + 0.07f), AxisHalf(Along, W + 0.12f, 0.07f, Normal, 0.06f), Frame);
        Kit.Box(Base + Normal * 0.06f + Along * (W + 0.06f) + FVector3.Up * (Height * 0.5f), AxisHalf(Along, 0.06f, Height * 0.5f, Normal, 0.06f), Frame);
        Kit.Box(Base + Normal * 0.06f - Along * (W + 0.06f) + FVector3.Up * (Height * 0.5f), AxisHalf(Along, 0.06f, Height * 0.5f, Normal, 0.06f), Frame);
        Kit.Box(Base + Normal * 0.3f + FVector3.Up * 0.08f, AxisHalf(Along, W + 0.3f, 0.08f, Normal, 0.3f), Palette.ConcreteDark);
        Kit.Box(Base + Normal * 0.1f + Along * (W * 0.65f) + FVector3.Up * (Height * 0.48f), AxisHalf(Along, 0.03f, 0.03f, Normal, 0.03f), Palette.Hex(0xB89A4A));
    }

    // Eaves overhanging the walls, fascia boards along them, courses of tiles down each slope and a cap along the ridge.
    private static void TiledRoof(MeshKit Kit, FVector3 Size, float WallHeight, float RoofHeight, FVector4 Tile)
    {
        const float Overhang = 0.45f;
        float HalfDepth = Size.Z * 0.5f + Overhang;
        float HalfWidth = Size.X * 0.5f + Overhang;
        Kit.Gable(new FVector3(0.0f, WallHeight, 0.0f), new FVector3(HalfWidth * 2.0f, RoofHeight, HalfDepth * 2.0f), Tile);
        foreach (float Side in new[] { -1.0f, 1.0f })
        {
            FVector3 Eave = new(0.0f, WallHeight, Side * HalfDepth);
            FVector3 Ridge = new(0.0f, WallHeight + RoofHeight, 0.0f);
            FVector3 Normal = new FVector3(0.0f, HalfDepth, Side * RoofHeight).Normalized();
            FQuat Lay = FQuat.FromToRotation(FVector3.Up, Normal);
            for (int Course = 1; Course < 6; ++Course)
            {
                FVector3 At = FVector3.Lerp(Eave, Ridge, Course / 6.0f) + Normal * 0.03f;
                Kit.Box(At, new FVector3(HalfWidth, 0.03f, 0.05f), Palette.Shade(Tile, 0.82f), Lay);
            }
            Kit.Box(Eave + new FVector3(0.0f, -0.08f, Side * 0.02f), new FVector3(HalfWidth, 0.1f, 0.03f), Palette.Shade(Tile, 0.6f));
        }
        Kit.Box(new FVector3(0.0f, WallHeight + RoofHeight + 0.02f, 0.0f), new FVector3(HalfWidth + 0.02f, 0.07f, 0.12f), Palette.Shade(Tile, 0.75f));
    }

    // A parapet round the roof edge, with the air conditioners and water tanks that crowd Latin American rooftops.
    private static void FlatRoof(MeshKit Kit, FVector3 Size, float Y, FVector4 Color, int Seed, bool bParapet)
    {
        FVector3 H = Size * 0.5f;
        if (bParapet)
        {
            FlatRoofEdge(Kit, Size, Y, Color);
        }
        Random Rng = new(Seed);
        int Units = 1 + Rng.Next(0, 3);
        for (int Index = 0; Index < Units; ++Index)
        {
            FVector3 At = new(((float)Rng.NextDouble() - 0.5f) * (Size.X - 2.0f), Y, ((float)Rng.NextDouble() - 0.5f) * (Size.Z - 2.0f));
            Kit.Block(At, new FVector3(0.9f, 0.6f, 0.7f), Palette.Shade(Palette.Medic, 0.85f));
            Kit.Tube(At + new FVector3(0.0f, 0.3f, 0.35f), At + new FVector3(0.0f, 0.3f, 0.37f), 0.25f, 0.25f, Palette.Gunmetal, 10);
        }
        if (Rng.NextDouble() < 0.7)
        {
            FVector3 At = new(H.X - 1.2f, Y, -H.Z + 1.2f);
            Kit.Cylinder(At, 0.55f, 1.1f, Tank, 12);
            Kit.Cone(At + new FVector3(0.0f, 1.1f, 0.0f), 0.55f, 0.2f, Tank, 12);
        }
    }

    private static void FlatRoofEdge(MeshKit Kit, FVector3 Size, float Y, FVector4 Color)
    {
        FVector3 H = Size * 0.5f;
        Kit.Block(new FVector3(0.0f, Y, H.Z), new FVector3(Size.X + 0.1f, 0.5f, 0.2f), Color);
        Kit.Block(new FVector3(0.0f, Y, -H.Z), new FVector3(Size.X + 0.1f, 0.5f, 0.2f), Color);
        Kit.Block(new FVector3(H.X, Y, 0.0f), new FVector3(0.2f, 0.5f, Size.Z + 0.1f), Color);
        Kit.Block(new FVector3(-H.X, Y, 0.0f), new FVector3(0.2f, 0.5f, Size.Z + 0.1f), Color);
    }

    // Vertical ribs down the long walls, which is what reads as sheet metal from across a base.
    private static void Corrugate(MeshKit Kit, FVector3 Size, float WallHeight, FVector4 Rib)
    {
        FVector3 H = Size * 0.5f;
        for (float X = -H.X + 0.3f; X < H.X; X += 0.45f)
        {
            Kit.Box(new FVector3(X, WallHeight * 0.5f + 0.2f, -H.Z - 0.02f), new FVector3(0.06f, WallHeight * 0.5f - 0.2f, 0.02f), Rib);
        }
        for (float Z = -H.Z + 0.3f; Z < H.Z; Z += 0.45f)
        {
            foreach (float Side in new[] { -1.0f, 1.0f })
            {
                Kit.Box(new FVector3(Side * (H.X + 0.02f), WallHeight * 0.5f + 0.2f, Z), new FVector3(0.02f, WallHeight * 0.5f - 0.2f, 0.06f), Rib);
            }
        }
    }

    private static void RollerDoor(MeshKit Kit, FVector3 Base, float Width, float Height)
    {
        Kit.Box(Base + new FVector3(0.0f, Height * 0.5f, 0.03f), new FVector3(Width * 0.5f, Height * 0.5f, 0.03f), Palette.Shade(Palette.Steel, 0.62f));
        for (float Y = 0.2f; Y < Height; Y += 0.22f)
        {
            Kit.Box(Base + new FVector3(0.0f, Y, 0.065f), new FVector3(Width * 0.5f, 0.02f, 0.01f), Palette.Shade(Palette.Steel, 0.48f));
        }
        Kit.Box(Base + new FVector3(0.0f, Height + 0.2f, 0.18f), new FVector3(Width * 0.5f + 0.1f, 0.22f, 0.18f), Palette.Shade(Palette.Steel, 0.7f));
    }

    private static void Skylights(MeshKit Kit, FVector3 Size, float WallHeight, float RoofHeight)
    {
        float HalfWidth = Size.X * 0.5f + 0.2f;
        foreach (float Side in new[] { -1.0f, 1.0f })
        {
            FVector3 Normal = new FVector3(Side * RoofHeight, HalfWidth, 0.0f).Normalized();
            FVector3 At = new FVector3(Side * HalfWidth * 0.5f, WallHeight + RoofHeight * 0.5f, 0.0f) + Normal * 0.02f;
            FQuat Lay = FQuat.FromToRotation(FVector3.Up, Normal);
            for (float Z = -Size.Z * 0.35f; Z <= Size.Z * 0.35f; Z += Size.Z * 0.35f)
            {
                Kit.Box(At + new FVector3(0.0f, 0.0f, Z), new FVector3(0.9f, 0.02f, 1.2f), Palette.Shade(Palette.Glass, 1.2f), Lay);
            }
        }
    }

    // A half-round roof over a hangar, springing from the wall tops, with plain gable ends.
    private static void Vault(MeshKit Kit, float HalfWidth, float Rise, float HalfDepth, float BaseY, FVector4 Shell, FVector4 End)
    {
        const int Segments = 12;
        for (int Segment = 0; Segment < Segments; ++Segment)
        {
            float A0 = MathF.PI * Segment / Segments;
            float A1 = MathF.PI * (Segment + 1) / Segments;
            FVector3 P0 = new(MathF.Cos(A0) * HalfWidth, BaseY + MathF.Sin(A0) * Rise, 0.0f);
            FVector3 P1 = new(MathF.Cos(A1) * HalfWidth, BaseY + MathF.Sin(A1) * Rise, 0.0f);
            FVector3 Mid = (P0 + P1) * 0.5f - new FVector3(0.0f, BaseY, 0.0f);
            FVector3 Front = new(0.0f, 0.0f, HalfDepth + 0.3f);
            Kit.Quad(P0 - Front, P1 - Front, P1 + Front, P0 + Front, Mid, Palette.Shade(Shell, Segment % 2 == 0 ? 1.0f : 0.94f));
            foreach (float Side in new[] { -1.0f, 1.0f })
            {
                FVector3 Face = new(0.0f, 0.0f, Side * HalfDepth);
                Kit.Triangle(new FVector3(0.0f, BaseY, Side * HalfDepth), P0 + Face, P1 + Face, new FVector3(0.0f, 0.0f, Side), End);
            }
        }
    }

    private static void Awning(MeshKit Kit, FVector3 Front, float HalfWidth, FVector4 Accent)
    {
        FQuat Slope = FQuat.FromEuler(Mathf.Radians(18.0f), 0.0f, 0.0f);
        int Stripes = Math.Max(4, (int)(HalfWidth * 2.0f / 0.5f));
        for (int Index = 0; Index < Stripes; ++Index)
        {
            float X = -HalfWidth + (Index + 0.5f) * HalfWidth * 2.0f / Stripes;
            Kit.Box(Front + new FVector3(X, 0.0f, 0.8f), new FVector3(HalfWidth / Stripes, 0.03f, 0.85f), Index % 2 == 0 ? Accent : Trim, Slope);
        }
        Kit.Box(Front + new FVector3(0.0f, -0.35f, 1.62f), new FVector3(HalfWidth, 0.12f, 0.02f), Accent);
    }

    // Rows of stitched bags, each slightly offset from the row below the way they are actually laid.
    private static void Bags(MeshKit Kit, FVector3 Center, float Length, int Rows, FVector4 Color)
    {
        float Bag = 0.8f;
        for (int Row = 0; Row < Rows; ++Row)
        {
            float Offset = Row % 2 == 0 ? 0.0f : Bag * 0.5f;
            int Count = Math.Max(1, (int)(Length / Bag));
            for (int Index = 0; Index < Count; ++Index)
            {
                float X = -Length * 0.5f + Bag * 0.5f + Index * Bag + Offset;
                if (X > Length * 0.5f)
                {
                    continue;
                }
                float Shade = 0.88f + 0.12f * ((Index * 7 + Row * 3) % 5) / 4.0f;
                Kit.Blob(Center + new FVector3(X, 0.14f + Row * 0.26f, 0.0f), new FVector3(Bag * 0.52f, 0.15f, 0.28f), 1, null,
                    (Direction, Point) => Palette.Shade(Color, Shade * (0.8f + 0.2f * (Direction.Y * 0.5f + 0.5f))));
            }
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
                    Kit.Box(new FVector3(Across * Size.X, Y, H.Z + 0.14f), Half, Color);
                    break;
                case 1:
                    Kit.Box(new FVector3(Across * Size.X, Y, -H.Z - 0.14f), Half, Color);
                    break;
                case 2:
                    Kit.Box(new FVector3(H.X + 0.14f, Y, Across * Size.Z), new FVector3(0.02f, Half.Y, Half.X), Color);
                    break;
                default:
                    Kit.Box(new FVector3(-H.X - 0.14f, Y, Across * Size.Z), new FVector3(0.02f, Half.Y, Half.X), Color);
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
