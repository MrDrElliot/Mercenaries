using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

public enum EFoliageKind : byte
{
    Tree,
    JungleTree,
    Palm,
    Bush,
}

public sealed class DestructionSystem
{
    private sealed class FPlant
    {
        public EFoliageKind Kind;
        public FVector3 At;
        public float Scale;
        public float Yaw;
        public int Variant;
        public bool bAlive = true;
    }

    private sealed class FFalling
    {
        public Entity Handle;
        public Structure? Source;
        public FVector3 Origin;
        public FVector3 Pivot;
        public FVector3 Axis;
        public FVector3 Direction;
        public FQuat Start;
        public float Angle = 0.04f;
        public float Speed;
        public float Length;
        public float Width;
        public bool bLanded;
        public float Life = float.MaxValue;
        public bool bByPlayer;
        public FVector3 Scale = FVector3.One;
    }

    private sealed class FSinking
    {
        public Structure Target = null!;
        public float Age;
        public float Duration;
        public FVector3 TiltAxis;
        public float TiltRadians;
        public float PuffTimer;
    }

    private sealed class FBurning
    {
        public Structure Target = null!;
        public float FlameTimer;
        public float SmokeTimer;
        public SoundLoop? Loop;
        public Entity Effect = Entity.Null;
    }

    private const float FoliageCell = 64.0f;
    private const float StructureFireScale = 1.8f;
    private const int MaxFallenTrees = 28;

    // The engine's instanced foliage draws the plants, and this grid only answers the gameplay queries.
    private readonly Dictionary<(int, int), List<FPlant>> Cells = new();
    // One slot per kind and variant, with the stump after them.
    private const int StumpType = ((int)EFoliageKind.Bush + 1) * FoliageShapes.Variants;
    private readonly int[] FoliageTypes = new int[StumpType + 1];
    private readonly CStaticMesh?[] FoliageMeshes = new CStaticMesh?[StumpType + 1];
    private readonly List<FFalling> Falling = new();
    private readonly List<FSinking> Sinking = new();
    private readonly List<FBurning> Burning = new();
    private readonly List<(int, int)> CellScratch = new();
    private int FallenTrees;

    public void Shutdown()
    {
        foreach (FBurning Fire in Burning)
        {
            Fire.Loop?.Stop();
        }

        Burning.Clear();
    }

    public void AddFoliage(EFoliageKind Kind, FVector3 At, float Scale, float Yaw)
    {
        (int, int) Key = CellOf(At);
        if (!Cells.TryGetValue(Key, out List<FPlant>? Plants))
        {
            Plants = new List<FPlant>();
            Cells[Key] = Plants;
        }

        // From the position, so a rebuilt world grows the same tree in the same place.
        int Variant = (int)((uint)((int)MathF.Floor(At.X * 7.0f) * 73856093 ^ (int)MathF.Floor(At.Z * 7.0f) * 19349663) % FoliageShapes.Variants);
        Plants.Add(new FPlant { Kind = Kind, At = At, Scale = Scale, Yaw = Yaw, Variant = Variant });
    }

    // The Nth living plant of a kind in grid order, for debug tools that want to look at one.
    public FVector3? PlantOf(EFoliageKind Kind, int Index)
    {
        foreach (List<FPlant> Plants in Cells.Values)
        {
            foreach (FPlant Plant in Plants)
            {
                if (Plant.bAlive && Plant.Kind == Kind && Index-- == 0)
                {
                    return Plant.At;
                }
            }
        }
        return null;
    }

    public void CommitFoliage()
    {
        for (int Type = 0; Type < FoliageTypes.Length; ++Type)
        {
            MeshKit Kit = new();
            if (Type == StumpType)
            {
                FoliageShapes.Stump(Kit);
            }
            else
            {
                FoliageShapes.Draw(Kit, (EFoliageKind)(Type / FoliageShapes.Variants), Type % FoliageShapes.Variants);
            }

            FoliageMeshes[Type] = Kit.BuildStaticMesh(Mercs.World, Materials.Foliage, 3);
            bool bCastShadow = Type / FoliageShapes.Variants != (int)EFoliageKind.Bush;
            FoliageTypes[Type] = CFoliageLibrary.AddFoliageType(Mercs.World, FoliageMeshes[Type]!, bCastShadow, 700.0f);
        }

        List<SFoliageInstance> Instances = new();
        foreach (List<FPlant> Plants in Cells.Values)
        {
            foreach (FPlant Plant in Plants)
            {
                Instances.Add(InstanceOf(SlotOf(Plant), Plant.At, Plant.Scale, Plant.Yaw));
            }
        }

        CFoliageLibrary.AddFoliageInstances(Mercs.World, CollectionsMarshal.AsSpan(Instances));
    }

    private static int SlotOf(FPlant Plant) => (int)Plant.Kind * FoliageShapes.Variants + Plant.Variant;

    private SFoliageInstance InstanceOf(int Type, FVector3 At, float Scale, float Yaw)
    {
        FQuat Rotation = FQuat.FromEuler(0.0f, Mathf.Radians(Yaw), 0.0f);
        return new SFoliageInstance
        {
            Position = At,
            Rotation = new FVector4(Rotation.X, Rotation.Y, Rotation.Z, Rotation.W),
            Scale = new FVector3(Scale),
            TypeIndex = FoliageTypes[Type],
        };
    }

    private static (int, int) CellOf(FVector3 At) => ((int)MathF.Floor(At.X / FoliageCell), (int)MathF.Floor(At.Z / FoliageCell));

    private void ForPlantsNear(FVector3 Center, float Radius, Action<FPlant> Visit)
    {
        CellScratch.Clear();
        int MinX = (int)MathF.Floor((Center.X - Radius) / FoliageCell);
        int MaxX = (int)MathF.Floor((Center.X + Radius) / FoliageCell);
        int MinZ = (int)MathF.Floor((Center.Z - Radius) / FoliageCell);
        int MaxZ = (int)MathF.Floor((Center.Z + Radius) / FoliageCell);
        for (int X = MinX; X <= MaxX; ++X)
        {
            for (int Z = MinZ; Z <= MaxZ; ++Z)
            {
                CellScratch.Add((X, Z));
            }
        }

        foreach ((int, int) Key in CellScratch)
        {
            if (!Cells.TryGetValue(Key, out List<FPlant>? Plants))
            {
                continue;
            }

            foreach (FPlant Plant in Plants)
            {
                if (Plant.bAlive && Geo.FlatDistance(Plant.At, Center) < Radius)
                {
                    Visit(Plant);
                }
            }
        }
    }

    public void OnExplosion(FVector3 At, float Radius)
    {
        float Ground = Terrain.HeightAt(At.X, At.Z);
        if (At.Y - Ground < Radius * 0.6f && Ground > Terrain.SeaLevel - 0.2f)
        {
            Mercs.Fx.Scorch(new FVector3(At.X, Ground, At.Z), Terrain.NormalAt(At.X, At.Z), Radius * Mercs.Range(0.7f, 1.0f));
        }

        ForPlantsNear(At, Radius * 0.9f, Plant =>
        {
            float Distance = Geo.FlatDistance(Plant.At, At);
            if (Plant.Kind == EFoliageKind.Bush || Distance < Radius * 0.65f || Mercs.Rng.NextDouble() < 0.5)
            {
                Knock(Plant, Plant.At - At);
            }
        });
    }

    public int CrushFoliage(FVector3 Center, FVector3 Forward, float HalfWidth, float HalfLength, float Speed, bool bHeavy)
    {
        if (Speed < (bHeavy ? 1.5f : 4.0f))
        {
            return 0;
        }

        FVector3 Right = Geo.RightOf(Geo.YawOf(Forward));
        int Count = 0;
        ForPlantsNear(Center, HalfLength + 2.0f, Plant =>
        {
            FVector3 Offset = Plant.At - Center;
            if (MathF.Abs(FVector3.Dot(Offset, Forward)) < HalfLength + 0.6f && MathF.Abs(FVector3.Dot(Offset, Right)) < HalfWidth + 0.4f)
            {
                Knock(Plant, Forward + (Offset - Forward * FVector3.Dot(Offset, Forward)) * 0.15f);
                Count++;
            }
        });

        return Count;
    }

    private void Knock(FPlant Plant, FVector3 Push)
    {
        Plant.bAlive = false;
        CFoliageLibrary.RemoveFoliageInRadius(Mercs.World, Plant.At, 0.05f, FoliageTypes[SlotOf(Plant)]);
        if (Plant.Kind != EFoliageKind.Bush)
        {
            SFoliageInstance Stump = InstanceOf(StumpType, Plant.At, Plant.Scale, Plant.Yaw);
            CFoliageLibrary.AddFoliageInstances(Mercs.World, new ReadOnlySpan<SFoliageInstance>(ref Stump));
        }

        if (Plant.Kind == EFoliageKind.Bush)
        {
            Mercs.Fx.Debris(Plant.At + new FVector3(0.0f, 0.5f, 0.0f), Palette.Jungle, 3, 4.0f, 0.3f);
            Mercs.Fx.Puff(Plant.At + new FVector3(0.0f, 0.6f, 0.0f), 1.6f * Plant.Scale, 1.5f, false, FVector3.Up);
            return;
        }

        if (FallenTrees >= MaxFallenTrees)
        {
            RetireOldestTree();
        }

        FQuat Facing = FQuat.FromEuler(0.0f, Mathf.Radians(Plant.Yaw), 0.0f);
        Entity Handle = Mercs.World.CreateEntity("FallingTree", Plant.At, Facing, new FVector3(Plant.Scale));
        MeshKit.Show(Mercs.World.Registry, Handle, FoliageMeshes[SlotOf(Plant)]);

        FVector3 Direction = Geo.Flat(Push).NormalizedOr(Geo.Heading(Mercs.Range(0.0f, 360.0f)));
        Falling.Add(new FFalling
        {
            Handle = Handle,
            Origin = Plant.At,
            Pivot = Plant.At,
            Axis = FVector3.Cross(FVector3.Up, Direction).NormalizedOr(FVector3.Right),
            Direction = Direction,
            Start = Facing,
            Scale = new FVector3(Plant.Scale),
            Length = FoliageShapes.HeightOf(Plant.Kind, Plant.Scale),
            Width = 1.5f,
            Speed = 0.6f,
            Life = 45.0f,
        });
        FallenTrees++;
        Sfx.At(ESfx.TreeFall, Plant.At + new FVector3(0.0f, 2.0f, 0.0f), 0.6f, 120.0f, 6.0f, 0.12f);
        Mercs.Fx.Debris(Plant.At + new FVector3(0.0f, 0.4f, 0.0f), Palette.TreeTrunk, 2, 3.0f, 0.25f);
    }

    private void RetireOldestTree()
    {
        for (int Index = 0; Index < Falling.Count; ++Index)
        {
            if (Falling[Index].Source is null)
            {
                Falling[Index].Life = MathF.Min(Falling[Index].Life, 3.0f);
                FallenTrees--;
                return;
            }
        }
    }

    public void Topple(Structure Target, FVector3 Push, bool bByPlayer)
    {
        FVector3 Direction = Geo.Flat(Push).NormalizedOr(Geo.Heading(Mercs.Range(0.0f, 360.0f)));
        FVector3 Local = Target.BaseRotation.Inverse() * Direction;
        FVector3 Half = Target.Size * 0.5f;
        float Reach = MathF.Abs(Local.X) * Half.X + MathF.Abs(Local.Z) * Half.Z;
        Falling.Add(new FFalling
        {
            Handle = Target.Owner,
            Source = Target,
            Origin = Target.BasePosition,
            Pivot = Target.BasePosition + Direction * MathF.Min(Reach, 1.5f),
            Axis = FVector3.Cross(FVector3.Up, Direction).NormalizedOr(FVector3.Right),
            Direction = Direction,
            Start = Target.BaseRotation,
            Length = Target.Size.Y,
            Width = MathF.Max(Half.X, Half.Z) + 1.0f,
            Speed = 0.35f,
            bByPlayer = bByPlayer,
        });

        Sfx.At(ESfx.WoodBreak, Target.Position, 0.8f, 200.0f, 8.0f, 0.1f, 0.0f, 0.7f);
    }

    public void Sink(Structure Target, float Duration)
    {
        FVector3 Tilt = Geo.Heading(Mercs.Range(0.0f, 360.0f));
        Sinking.Add(new FSinking
        {
            Target = Target,
            Duration = Duration,
            TiltAxis = FVector3.Cross(FVector3.Up, Tilt).NormalizedOr(FVector3.Right),
            TiltRadians = Mathf.Radians(Mercs.Range(3.0f, 9.0f)),
        });

        Crumble(Target, Duration + 0.5f);
    }

    // The dust, rumble and shake of a collapse, which a shattered structure keeps though it turns to rubble at once.
    public void Crumble(Structure Target, float ShakeDuration)
    {
        Mercs.Fx.CollapseDust(Target.BasePosition, Target.Size);
        float Bulk = Target.Size.X * Target.Size.Y * Target.Size.Z;
        Sfx.At(ESfx.Collapse, Target.Position, Mathf.Clamp(0.4f + Bulk / 1500.0f, 0.45f, 1.0f), 700.0f, MathF.Max(Target.Size.X, 6.0f), 0.1f);
        Shake(Target.Position, MathF.Min(2.0f, 0.4f + Bulk / 1200.0f), ShakeDuration);
    }

    public void Ignite(Structure Target)
    {
        foreach (FBurning Fire in Burning)
        {
            if (Fire.Target == Target)
            {
                return;
            }
        }

        Burning.Add(new FBurning { Target = Target });
    }

    public bool IsBurning(Structure Target) => Burning.Exists(Fire => Fire.Target == Target);

    private static void Shake(FVector3 At, float Strength, float Duration)
    {
        float Distance = FVector3.Distance(At, Sfx.Listener);
        float Falloff = 1.0f - Mathf.Clamp01(Distance / 120.0f);
        if (Falloff > 0.0f)
        {
            CameraShake.Impact(Strength * Falloff * Falloff * 0.35f, Duration);
        }
    }

    public void Update(float DeltaTime)
    {
        TickFalling(DeltaTime);
        TickSinking(DeltaTime);
        TickBurning(DeltaTime);
    }

    private void TickFalling(float DeltaTime)
    {
        CWorld World = Mercs.World;
        for (int Index = Falling.Count - 1; Index >= 0; --Index)
        {
            FFalling Item = Falling[Index];
            if (!World.IsValidEntity(Item.Handle))
            {
                RemoveFalling(Index);
                continue;
            }

            float Sink = 0.0f;
            if (!Item.bLanded)
            {
                float Rest = Mathf.Radians(86.0f);
                Item.Speed += 1.5f * 9.81f / MathF.Max(Item.Length, 1.0f) * MathF.Sin(Item.Angle) * DeltaTime;
                Item.Angle = MathF.Min(Rest, Item.Angle + Item.Speed * DeltaTime);
                if (Item.Angle >= Rest)
                {
                    Item.bLanded = true;
                    Land(Item);
                }
            }
            else
            {
                Item.Life -= DeltaTime;
                if (Item.Life <= 0.0f)
                {
                    World.DestroyEntity(Item.Handle);
                    RemoveFalling(Index);
                    continue;
                }

                Sink = Item.Life < 3.0f ? (3.0f - Item.Life) * 0.6f : 0.0f;
            }

            FQuat Swing = FQuat.AngleAxis(Item.Angle, Item.Axis);
            FVector3 Location = Item.Pivot + Swing * (Item.Origin - Item.Pivot) - new FVector3(0.0f, Sink, 0.0f);
            Mercs.Fx.Stage(Item.Handle, new FTransform(Location, Swing * Item.Start, Item.Scale));
        }
    }

    private void RemoveFalling(int Index)
    {
        if (Falling[Index].Source is null)
        {
            FallenTrees = Math.Max(0, FallenTrees - 1);
        }

        Falling.RemoveAt(Index);
    }

    private void Land(FFalling Item)
    {
        FVector3 Tip = Item.Pivot + Item.Direction * Item.Length;
        int Puffs = Math.Clamp((int)(Item.Length / 2.0f), 3, 12);
        for (int Index = 0; Index < Puffs; ++Index)
        {
            FVector3 Along = Geo.Ground(FVector3.Lerp(Item.Pivot, Tip, (Index + 0.5f) / Puffs));
            Mercs.Fx.Puff(Along + new FVector3(0.0f, 0.5f, 0.0f), MathF.Min(Item.Width, 4.0f) * Mercs.Range(0.5f, 0.8f), Mercs.Range(2.5f, 4.0f), false, new FVector3(Mercs.Range(-1.0f, 1.0f), 0.8f, Mercs.Range(-1.0f, 1.0f)));
        }

        if (Item.Source is null)
        {
            return;
        }

        Structure Source = Item.Source;
        Sfx.At(ESfx.Collapse, Tip, 0.85f, 600.0f, 10.0f, 0.1f);
        Shake(Tip, MathF.Min(1.6f, Item.Length * 0.06f), 0.6f);
        Mercs.Fx.Debris(Tip, Source.Tint, 6, 5.0f, 0.8f);

        FHit Crush = new() { Amount = 450.0f, Kind = EDamageKind.Crush, Direction = Item.Direction, bByPlayer = Item.bByPlayer, Source = Source.Owner, SourceFaction = EFaction.None };
        List<IDamageable> Victims = new();
        foreach (IDamageable Candidate in Mercs.Damageables.Values)
        {
            if (Candidate == Source || !Candidate.IsAlive)
            {
                continue;
            }

            FVector3 Offset = Candidate.Position - Item.Pivot;
            float Along = FVector3.Dot(Offset, Item.Direction);
            if (Along < 0.0f || Along > Item.Length)
            {
                continue;
            }

            float Side = Geo.Flat(Offset - Item.Direction * Along).Length;
            if (Side < Item.Width + Candidate.Radius * 0.5f)
            {
                Victims.Add(Candidate);
            }
        }

        foreach (IDamageable Victim in Victims)
        {
            Crush.Point = Victim.Position;
            Victim.TakeHit(Crush);
        }

        FVector3 Middle = FVector3.Lerp(Item.Pivot, Tip, 0.5f);
        ForPlantsNear(Middle, Item.Length * 0.5f + Item.Width, Plant =>
        {
            FVector3 Offset = Plant.At - Item.Pivot;
            float Along = FVector3.Dot(Offset, Item.Direction);
            if (Along > 0.0f && Along < Item.Length && Geo.Flat(Offset - Item.Direction * Along).Length < Item.Width)
            {
                Knock(Plant, Item.Direction);
            }
        });
    }

    private void TickSinking(float DeltaTime)
    {
        CWorld World = Mercs.World;
        for (int Index = Sinking.Count - 1; Index >= 0; --Index)
        {
            FSinking Item = Sinking[Index];
            Structure Target = Item.Target;
            if (!World.IsValidEntity(Target.Owner))
            {
                Sinking.RemoveAt(Index);
                continue;
            }

            Item.Age += DeltaTime;
            float T = Mathf.Clamp01(Item.Age / Item.Duration);
            FVector3 Size = Target.Size;
            FVector3 Jitter = new(Mercs.Range(-0.06f, 0.06f), 0.0f, Mercs.Range(-0.06f, 0.06f));
            FVector3 Location = Target.BasePosition - new FVector3(0.0f, Size.Y * 1.05f * T * T, 0.0f) + Jitter * (1.0f - T);
            FQuat Rotation = FQuat.AngleAxis(Item.TiltRadians * T, Item.TiltAxis) * Target.BaseRotation;
            Mercs.Fx.Stage(Target.Owner, new FTransform(Location, Rotation, FVector3.One));

            Item.PuffTimer -= DeltaTime;
            if (Item.PuffTimer <= 0.0f && !Mercs.Fx.HasEffect(EEffect.CollapseDust))
            {
                Item.PuffTimer = 0.05f;
                FVector3 Half = Size * 0.5f;
                bool bAlongX = Mercs.Rng.NextDouble() < 0.5;
                float Edge = Mercs.Rng.NextDouble() < 0.5 ? -1.0f : 1.0f;
                FVector3 Local = bAlongX ? new FVector3(Mercs.Range(-Half.X, Half.X), 0.0f, Edge * Half.Z) : new FVector3(Edge * Half.X, 0.0f, Mercs.Range(-Half.Z, Half.Z));
                FVector3 Outward = Target.BaseRotation * (bAlongX ? new FVector3(0.0f, 0.0f, Edge) : new FVector3(Edge, 0.0f, 0.0f));
                FVector3 At = Geo.Ground(Target.BasePosition + Target.BaseRotation * Local) + new FVector3(0.0f, Mercs.Range(0.3f, 1.5f), 0.0f);
                float Scale = MathF.Min(MathF.Min(Size.X, Size.Z), 9.0f);
                Mercs.Fx.Puff(At, Scale * Mercs.Range(0.2f, 0.35f), Mercs.Range(3.0f, 5.0f), false, Outward * Mercs.Range(1.5f, 3.5f) + new FVector3(0.0f, Mercs.Range(0.5f, 1.5f), 0.0f));
            }

            if (T >= 1.0f)
            {
                Sinking.RemoveAt(Index);
                Target.ShowRubble();
                FVector3 Center = Target.BasePosition + new FVector3(0.0f, 1.0f, 0.0f);
                Mercs.Fx.CollapseDust(Target.BasePosition, Target.Size);
                for (int Puff = 0; Puff < 5 && !Mercs.Fx.HasEffect(EEffect.CollapseDust); ++Puff)
                {
                    Mercs.Fx.Puff(Center + new FVector3(Mercs.Range(-1.0f, 1.0f) * Size.X * 0.4f, Mercs.Range(0.0f, 2.0f), Mercs.Range(-1.0f, 1.0f) * Size.Z * 0.4f), MathF.Min(Size.X, 10.0f) * Mercs.Range(0.3f, 0.45f), Mercs.Range(4.0f, 6.0f), false, new FVector3(0.0f, Mercs.Range(0.4f, 1.2f), 0.0f));
                }
            }
        }
    }

    private void TickBurning(float DeltaTime)
    {
        FVector3 Head = Sfx.Listener;
        for (int Index = Burning.Count - 1; Index >= 0; --Index)
        {
            FBurning Fire = Burning[Index];
            Structure Target = Fire.Target;
            if (!Target.IsAlive)
            {
                Fire.Loop?.Stop();
                Mercs.Fx.StopFire(Fire.Effect);
                Burning.RemoveAt(Index);
                continue;
            }

            FVector3 Size = Target.Size;
            float Spread = Mathf.Clamp(Size.X / 6.0f, 0.6f, 2.2f);
            if (Fire.Effect.IsNull)
            {
                Fire.Effect = Mercs.Fx.StartFire(Target.BasePosition + new FVector3(0.0f, Size.Y * 0.85f, 0.0f), Spread * StructureFireScale);
            }

            Fire.FlameTimer -= DeltaTime;
            if (Fire.FlameTimer <= 0.0f && Fire.Effect.IsNull)
            {
                Fire.FlameTimer = 0.08f;
                FVector3 Local = new(Mercs.Range(-0.45f, 0.45f) * Size.X, Mercs.Range(0.45f, 1.0f) * Size.Y, Mercs.Range(-0.45f, 0.45f) * Size.Z);
                Mercs.Fx.Flame(Target.BasePosition + Target.BaseRotation * Local, Mercs.Range(0.6f, 1.3f) * Spread);
            }

            Fire.SmokeTimer -= DeltaTime;
            if (Fire.SmokeTimer <= 0.0f && Fire.Effect.IsNull)
            {
                Fire.SmokeTimer = 0.22f;
                FVector3 Top = Target.BasePosition + new FVector3(Mercs.Range(-0.3f, 0.3f) * Size.X, Size.Y + 1.0f, Mercs.Range(-0.3f, 0.3f) * Size.Z);
                Mercs.Fx.Puff(Top, 2.5f * Spread, Mercs.Range(5.0f, 8.0f), true, new FVector3(Mercs.Range(0.3f, 1.2f), Mercs.Range(2.5f, 4.0f), Mercs.Range(-0.3f, 0.6f)));
            }

            bool bNear = FVector3.DistanceSquared(Target.Position, Head) < 90.0f * 90.0f;
            if (bNear && Fire.Loop is null)
            {
                Fire.Loop = Sfx.StartMover(ESfx.FireLoop, Target.Position, MathF.Min(1.0f, 0.5f + Spread * 0.25f), 90.0f, 6.0f);
            }
            else if (!bNear && Fire.Loop is not null)
            {
                Fire.Loop.Stop();
                Fire.Loop = null;
            }
            else
            {
                Fire.Loop?.Move(Target.Position, FVector3.Zero);
            }

            Target.Burn(DeltaTime);
        }
    }
}
