using System;
using System.Collections.Generic;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

public struct FPad
{
    public FVector3 Center;
    public float Radius;
    public bool bPaved;
}

public struct FRoad
{
    public FVector3 From;
    public FVector3 To;
    public float Width;
}

// Painted layers of M_Terrain, in the order its Custom Slang block samples them.
public enum ETerrainLayer
{
    Grass,
    Rock,
    Sand,
    Dirt,
    Paved,
}

public static class Terrain
{
    public const float HalfSize = 520.0f;
    public const int Resolution = 1025;
    public const float TileSize = HalfSize * 2.0f;
    public const float SampleSpacing = TileSize / (Resolution - 1);
    public const float IslandRadius = 430.0f;
    public const float SeaLevel = 0.0f;
    public const string MaterialPath = "/Game/Content/Materials/M_Terrain.lasset";

    // The heightmap stores 0 to 1, so the terrain entity sits below the seabed and spans every height above it.
    private const float BaseHeight = -16.0f;
    private const float HeightRange = 128.0f;
    private const int LayerCount = 5;

    private static float[] Heights = Array.Empty<float>();
    private static readonly List<FPad> Pads = new();
    private static readonly List<FRoad> Roads = new();
    private static readonly List<float> PadHeights = new();

    public static IReadOnlyList<FRoad> AllRoads => Roads;

    public static void Reset()
    {
        Heights = Array.Empty<float>();
        Pads.Clear();
        Roads.Clear();
        PadHeights.Clear();
    }

    public static float PadHeight(FVector3 Center)
    {
        return MathF.Max(RawHeight(Center.X, Center.Z), 2.5f);
    }

    public static void AddPad(FVector3 Center, float Radius, bool bPaved)
    {
        Pads.Add(new FPad { Center = Center, Radius = Radius, bPaved = bPaved });
        PadHeights.Add(PadHeight(Center));
    }

    public static void AddRoad(FVector3 From, FVector3 To, float Width = 6.0f)
    {
        Roads.Add(new FRoad { From = From, To = To, Width = Width });
    }

    public static bool IsBuilt => Heights.Length > 0;

    public static float RawHeight(float X, float Z)
    {
        float Distance = MathF.Sqrt(X * X + Z * Z) / IslandRadius;
        float Angle = MathF.Atan2(Z, X);
        Distance *= 1.0f + 0.07f * MathF.Sin(3.0f * Angle + 1.0f) + 0.04f * MathF.Sin(7.0f * Angle + 2.0f);

        float Land = 1.0f - Mathf.SmoothStep(0.8f, 1.02f, Distance);
        float Height = Land * 7.0f - (1.0f - Land) * 9.0f + 0.5f;

        float Hills = (Noise.Fbm(X * 0.0065f, Z * 0.0065f) - 0.5f) * 26.0f;
        Height += Hills * Land * Land;

        Height += Land * 42.0f * Gauss(X, Z, -60.0f, 300.0f, 105.0f);
        Height += Land * 24.0f * Gauss(X, Z, 250.0f, -40.0f, 70.0f);
        Height += Land * 18.0f * Gauss(X, Z, -260.0f, -120.0f, 60.0f);
        return Height;
    }

    private static float Gauss(float X, float Z, float CX, float CZ, float Sigma)
    {
        float DX = X - CX;
        float DZ = Z - CZ;
        return MathF.Exp(-(DX * DX + DZ * DZ) / (2.0f * Sigma * Sigma));
    }

    private struct FGroundShape
    {
        public float Height;
        public float PadWeight;
        public bool bPavedPad;
        public float RoadWeight;
        public float RoadShoulder;
    }

    private static FGroundShape ShapeGround(float X, float Z)
    {
        FGroundShape Shape = new() { Height = RawHeight(X, Z) };

        for (int Index = 0; Index < Pads.Count; ++Index)
        {
            FPad Pad = Pads[Index];
            float Distance = MathF.Sqrt((X - Pad.Center.X) * (X - Pad.Center.X) + (Z - Pad.Center.Z) * (Z - Pad.Center.Z));
            float Blend = 1.0f - Mathf.SmoothStep(Pad.Radius, Pad.Radius + 26.0f, Distance);
            if (Blend <= 0.0f)
            {
                continue;
            }

            Shape.Height = Mathf.Lerp(Shape.Height, PadHeights[Index], Blend);
            // A ragged edge, so a site's ground frays into the grass instead of ending on a perfect circle.
            float Fray = (Noise.Value(X * 0.15f, Z * 0.15f) - 0.5f) * 5.0f;
            float Paint = 1.0f - Mathf.SmoothStep(Pad.Radius - 4.0f, Pad.Radius + 2.0f, Distance + Fray);
            if (Paint > Shape.PadWeight)
            {
                Shape.PadWeight = Paint;
                Shape.bPavedPad = Pad.bPaved;
            }
        }

        foreach (FRoad Road in Roads)
        {
            FVector3 Segment = Road.To - Road.From;
            float LengthSq = Segment.X * Segment.X + Segment.Z * Segment.Z;
            if (LengthSq < 1.0f)
            {
                continue;
            }

            float T = Mathf.Clamp01(((X - Road.From.X) * Segment.X + (Z - Road.From.Z) * Segment.Z) / LengthSq);
            float PX = Road.From.X + Segment.X * T;
            float PZ = Road.From.Z + Segment.Z * T;
            float Distance = MathF.Sqrt((X - PX) * (X - PX) + (Z - PZ) * (Z - PZ));
            float Blend = 1.0f - Mathf.SmoothStep(Road.Width * 0.5f, Road.Width * 0.5f + 10.0f, Distance);
            if (Blend <= 0.0f)
            {
                continue;
            }

            float CenterHeight = MathF.Max(Mathf.Lerp(Road.From.Y, Road.To.Y, T) * 0.5f + RawHeight(PX, PZ) * 0.5f, 1.2f);
            Shape.Height = Mathf.Lerp(Shape.Height, CenterHeight, Blend * 0.85f);
            float Paint = 1.0f - Mathf.SmoothStep(Road.Width * 0.5f - 1.0f, Road.Width * 0.5f + 0.5f, Distance);
            float Shoulder = 1.0f - Mathf.SmoothStep(Road.Width * 0.5f, Road.Width * 0.5f + 2.5f, Distance);
            Shape.RoadWeight = MathF.Max(Shape.RoadWeight, Paint);
            Shape.RoadShoulder = MathF.Max(Shape.RoadShoulder, Shoulder);
        }

        return Shape;
    }

    // Bumps a meter or so across, which the old 8 meter mesh could not hold; roads and sites stay graded flat.
    private static float Relief(float X, float Z, float Flatten)
    {
        float Rolling = (Noise.Fbm(X * 0.045f + 31.0f, Z * 0.045f - 17.0f) - 0.5f) * 1.1f;
        float Bumps = (Noise.Value(X * 0.4f, Z * 0.4f) - 0.5f) * 0.18f;
        return (Rolling + Bumps) * (1.0f - Flatten);
    }

    // The island comes out the same on every visit, so the generated fields are kept for the next load instead of regenerated.
    private static int CachedFeatureKey;
    private static float[]? CachedHeights;
    private static float[]? CachedNormalized;
    private static byte[]? CachedWeights;

    private static int FeatureKey()
    {
        HashCode Key = new();
        foreach (FPad Pad in Pads)
        {
            Key.Add(Pad.Center.X);
            Key.Add(Pad.Center.Y);
            Key.Add(Pad.Center.Z);
            Key.Add(Pad.Radius);
            Key.Add(Pad.bPaved);
        }
        foreach (float Height in PadHeights)
        {
            Key.Add(Height);
        }
        foreach (FRoad Road in Roads)
        {
            Key.Add(Road.From.X);
            Key.Add(Road.From.Y);
            Key.Add(Road.From.Z);
            Key.Add(Road.To.X);
            Key.Add(Road.To.Y);
            Key.Add(Road.To.Z);
            Key.Add(Road.Width);
        }
        return Key.ToHashCode();
    }

    public static void Build(CWorld World, EntityRegistry Registry)
    {
        int Key = FeatureKey();
        if (CachedHeights is null || CachedNormalized is null || CachedWeights is null || Key != CachedFeatureKey)
        {
            GenerateFields();
            CachedFeatureKey = Key;
        }
        Heights = CachedHeights!;
        CreateGround(World, Registry, CachedNormalized!, CachedWeights!);
    }

    private static void GenerateFields()
    {
        int Count = Resolution * Resolution;
        Heights = new float[Count];
        FGroundShape[] Shapes = new FGroundShape[Count];

        System.Threading.Tasks.Parallel.For(0, Resolution, Row =>
        {
            float Z = -HalfSize + Row * SampleSpacing;
            for (int Col = 0; Col < Resolution; ++Col)
            {
                float X = -HalfSize + Col * SampleSpacing;
                FGroundShape Shape = ShapeGround(X, Z);
                float Flatten = MathF.Max(Shape.PadWeight, Shape.RoadShoulder);
                Shape.Height += Relief(X, Z, Flatten);
                Shapes[Row * Resolution + Col] = Shape;
                Heights[Row * Resolution + Col] = Shape.Height;
            }
        });

        float[] Normalized = new float[Count];
        byte[] Weights = new byte[LayerCount * Count];
        System.Threading.Tasks.Parallel.For(0, Resolution, Row =>
        {
            Span<float> Layer = stackalloc float[LayerCount];
            float Z = -HalfSize + Row * SampleSpacing;
            for (int Col = 0; Col < Resolution; ++Col)
            {
                int Index = Row * Resolution + Col;
                float X = -HalfSize + Col * SampleSpacing;
                Normalized[Index] = Mathf.Clamp01((Heights[Index] - BaseHeight) / HeightRange);
                PaintLayers(X, Z, Shapes[Index], SlopeAtSample(Row, Col), Layer);
                for (int L = 0; L < LayerCount; ++L)
                {
                    Weights[L * Count + Index] = (byte)(Mathf.Clamp01(Layer[L]) * 255.0f + 0.5f);
                }
            }
        });

        CachedHeights = Heights;
        CachedNormalized = Normalized;
        CachedWeights = Weights;
    }

    private static void CreateGround(CWorld World, EntityRegistry Registry, float[] Normalized, byte[] Weights)
    {
        Entity Ground = World.CreateEntity("Terrain", new FVector3(0.0f, BaseHeight, 0.0f));
        STerrainComponent Surface = Registry.GetOrAdd<STerrainComponent>(Ground)!;
        Surface.Resolution = Resolution;
        Surface.ChunkResolution = 64;
        Surface.TileWorldSize = TileSize;
        Surface.MaxHeight = HeightRange;
        Surface.Layers.Resize(LayerCount);
        Surface.Heightmap.Assign(Normalized);
        Surface.LayerWeights.Assign(Weights);
        if (Asset.Load<CMaterialInterface>(MaterialPath) is { } Material)
        {
            Surface.Material = Material;
        }
        else
        {
            Debug.LogWarning($"Mercenaries: {MaterialPath} is missing, run Tools/AuthorTerrain.py with the editor open.");
        }

        STerrainColliderComponent Collider = Registry.GetOrAdd<STerrainColliderComponent>(Ground)!;
        Collider.bAffectsNavigation = true;
        SRigidBodyComponent Body = Registry.GetOrAdd<SRigidBodyComponent>(Ground)!;
        Body.BodyType = EBodyType.Static;

        // Species come from the material's grass outputs; the component only switches the scatter on and bounds it.
        SGrassComponent Grass = Registry.GetOrAdd<SGrassComponent>(Ground)!;
        Grass.MaxDrawDistance = 150.0f;
        Grass.MaxInstancesPerSpecies = 1u << 17;

        BuildSea(World, Registry);
    }

    // Composited bottom to top, each layer covering what is under it by its own coverage.
    private static void PaintLayers(float X, float Z, FGroundShape Shape, float Slope, Span<float> Layer)
    {
        Layer.Clear();
        Layer[(int)ETerrainLayer.Grass] = 1.0f;

        float Patches = Noise.Fbm(X * 0.021f - 5.0f, Z * 0.021f + 9.0f);
        Cover(Layer, ETerrainLayer.Dirt, Mathf.SmoothStep(0.64f, 0.78f, Patches) * 0.85f);
        Cover(Layer, ETerrainLayer.Dirt, Shape.RoadShoulder * 0.8f);

        float RockNoise = (Noise.Value(X * 0.08f, Z * 0.08f) - 0.5f) * 0.18f;
        Cover(Layer, ETerrainLayer.Rock, MathF.Max(Mathf.SmoothStep(0.6f, 0.95f, Slope + RockNoise), Mathf.SmoothStep(28.0f, 40.0f, Shape.Height)));

        float Beach = 1.0f - Mathf.SmoothStep(1.1f, 2.9f, Shape.Height + (Patches - 0.5f) * 1.5f);
        Cover(Layer, ETerrainLayer.Sand, Beach);

        Cover(Layer, Shape.bPavedPad ? ETerrainLayer.Paved : ETerrainLayer.Dirt, Shape.PadWeight * 0.95f);
        Cover(Layer, ETerrainLayer.Paved, Shape.RoadWeight);
    }

    private static void Cover(Span<float> Layer, ETerrainLayer Top, float Coverage)
    {
        Coverage = Mathf.Clamp01(Coverage);
        for (int L = 0; L < Layer.Length; ++L)
        {
            Layer[L] *= 1.0f - Coverage;
        }
        Layer[(int)Top] += Coverage;
    }

    private static float SlopeAtSample(int Row, int Col)
    {
        int Left = Math.Max(Col - 1, 0);
        int Right = Math.Min(Col + 1, Resolution - 1);
        int Down = Math.Max(Row - 1, 0);
        int Up = Math.Min(Row + 1, Resolution - 1);
        float DX = (Heights[Row * Resolution + Right] - Heights[Row * Resolution + Left]) / ((Right - Left) * SampleSpacing);
        float DZ = (Heights[Up * Resolution + Col] - Heights[Down * Resolution + Col]) / ((Up - Down) * SampleSpacing);
        return MathF.Sqrt(DX * DX + DZ * DZ);
    }

    // The engine's water body, so the sea has waves, shoreline foam and reflections, and floats whatever carries a buoyancy component.
    private static void BuildSea(CWorld World, EntityRegistry Registry)
    {
        Entity Sea = World.CreateEntity("Sea", new FVector3(0.0f, SeaLevel - 0.2f, 0.0f));
        SWaterComponent Water = Registry.GetOrAdd<SWaterComponent>(Sea)!;
        Water.Extent = new FVector2(6000.0f, 6000.0f);
        Water.GridResolution = 512;
        // Out to the camera's far plane, so the sea meets the sky at the horizon instead of ending a few kilometers out.
        Water.HorizonExtent = 60000.0f;
        Water.WaveAmplitude = 0.5f;
        Water.WaveLength = 55.0f;
        Water.WindSpeed = 6.0f;
        Water.Choppiness = 0.55f;
        Water.ShallowColor = new FVector3(0.12f, 0.62f, 0.62f);
        Water.DeepColor = new FVector3(0.02f, 0.16f, 0.30f);
        Water.bBuoyancy = true;
    }

    // Interpolates across the same triangle split the terrain draws, so feet and wheels sit on the drawn surface.
    public static float HeightAt(float X, float Z)
    {
        if (Heights.Length == 0)
        {
            return RawHeight(X, Z);
        }

        float GX = (X + HalfSize) / SampleSpacing;
        float GZ = (Z + HalfSize) / SampleSpacing;
        if (GX < 0.0f || GZ < 0.0f || GX >= Resolution - 1 || GZ >= Resolution - 1)
        {
            return -12.0f;
        }

        int Col = (int)GX;
        int Row = (int)GZ;
        float FX = GX - Col;
        float FZ = GZ - Row;
        float H00 = Heights[Row * Resolution + Col];
        float H10 = Heights[Row * Resolution + Col + 1];
        float H01 = Heights[(Row + 1) * Resolution + Col];
        float H11 = Heights[(Row + 1) * Resolution + Col + 1];

        if (FX >= FZ)
        {
            return H00 + (H10 - H00) * FX + (H11 - H10) * FZ;
        }

        return H00 + (H11 - H01) * FX + (H01 - H00) * FZ;
    }

    public static FVector3 NormalAt(float X, float Z)
    {
        float Step = 1.5f;
        float DX = HeightAt(X + Step, Z) - HeightAt(X - Step, Z);
        float DZ = HeightAt(X, Z + Step) - HeightAt(X, Z - Step);
        return new FVector3(-DX, 2.0f * Step, -DZ).Normalized();
    }

    public static bool IsLand(float X, float Z) => HeightAt(X, Z) > 0.8f;
}

public static class Noise
{
    private static float Hash(int X, int Y)
    {
        unchecked
        {
            uint H = (uint)(X * 374761393 + Y * 668265263);
            H = (H ^ (H >> 13)) * 1274126177u;
            H ^= H >> 16;
            return (H & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }

    public static float Value(float X, float Y)
    {
        int IX = (int)MathF.Floor(X);
        int IY = (int)MathF.Floor(Y);
        float FX = X - IX;
        float FY = Y - IY;
        float SX = FX * FX * (3.0f - 2.0f * FX);
        float SY = FY * FY * (3.0f - 2.0f * FY);
        float A = Hash(IX, IY);
        float B = Hash(IX + 1, IY);
        float C = Hash(IX, IY + 1);
        float D = Hash(IX + 1, IY + 1);
        return Mathf.Lerp(Mathf.Lerp(A, B, SX), Mathf.Lerp(C, D, SX), SY);
    }

    public static float Fbm(float X, float Y)
    {
        float Sum = 0.0f;
        float Amplitude = 0.5f;
        float Frequency = 1.0f;
        for (int Octave = 0; Octave < 4; ++Octave)
        {
            Sum += Value(X * Frequency, Y * Frequency) * Amplitude;
            Frequency *= 2.03f;
            Amplitude *= 0.5f;
        }

        return Sum / 0.9375f;
    }
}
