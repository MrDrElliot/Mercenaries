using System;
using System.Collections.Generic;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

public struct FPad
{
    public FVector3 Center;
    public float Radius;
    public FVector4 Color;
}

public struct FRoad
{
    public FVector3 From;
    public FVector3 To;
    public float Width;
}

public static class Terrain
{
    public const float HalfSize = 520.0f;
    public const int Cells = 130;
    public const int ChunkCells = 26;
    public const float CellSize = HalfSize * 2.0f / Cells;
    public const float IslandRadius = 430.0f;
    public const float SeaLevel = 0.0f;

    private static float[] Heights = Array.Empty<float>();
    private static FVector4[] Colors = Array.Empty<FVector4>();
    private static readonly List<FPad> Pads = new();
    private static readonly List<FRoad> Roads = new();
    private static readonly List<float> PadHeights = new();

    public static IReadOnlyList<FRoad> AllRoads => Roads;

    public static void Reset()
    {
        Heights = Array.Empty<float>();
        Colors = Array.Empty<FVector4>();
        Pads.Clear();
        Roads.Clear();
        PadHeights.Clear();
    }

    public static float PadHeight(FVector3 Center)
    {
        return MathF.Max(RawHeight(Center.X, Center.Z), 2.5f);
    }

    public static void AddPad(FVector3 Center, float Radius, FVector4 Color)
    {
        Pads.Add(new FPad { Center = Center, Radius = Radius, Color = Color });
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

    private static float ShapedHeight(float X, float Z, out float PadWeight, out FVector4 PadColor, out float RoadWeight)
    {
        float Height = RawHeight(X, Z);
        PadWeight = 0.0f;
        PadColor = Palette.Dirt;
        RoadWeight = 0.0f;

        for (int Index = 0; Index < Pads.Count; ++Index)
        {
            FPad Pad = Pads[Index];
            float Distance = MathF.Sqrt((X - Pad.Center.X) * (X - Pad.Center.X) + (Z - Pad.Center.Z) * (Z - Pad.Center.Z));
            float Blend = 1.0f - Mathf.SmoothStep(Pad.Radius, Pad.Radius + 26.0f, Distance);
            if (Blend <= 0.0f)
            {
                continue;
            }

            Height = Mathf.Lerp(Height, PadHeights[Index], Blend);
            float Paint = 1.0f - Mathf.SmoothStep(Pad.Radius - 4.0f, Pad.Radius + 2.0f, Distance);
            if (Paint > PadWeight)
            {
                PadWeight = Paint;
                PadColor = Pad.Color;
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
            Height = Mathf.Lerp(Height, CenterHeight, Blend * 0.85f);
            float Paint = 1.0f - Mathf.SmoothStep(Road.Width * 0.5f - 1.0f, Road.Width * 0.5f + 0.5f, Distance);
            RoadWeight = MathF.Max(RoadWeight, Paint);
        }

        return Height;
    }

    public static void Build(CWorld World, EntityRegistry Registry)
    {
        int Verts = Cells + 1;
        Heights = new float[Verts * Verts];
        Colors = new FVector4[Verts * Verts];

        for (int Row = 0; Row < Verts; ++Row)
        {
            for (int Col = 0; Col < Verts; ++Col)
            {
                float X = -HalfSize + Col * CellSize;
                float Z = -HalfSize + Row * CellSize;
                float Height = ShapedHeight(X, Z, out float PadWeight, out FVector4 PadColor, out float RoadWeight);
                Heights[Row * Verts + Col] = Height;
                Colors[Row * Verts + Col] = SurfaceColor(X, Z, Height, PadWeight, PadColor, RoadWeight);
            }
        }

        using (new FPhysicsBatchScope(World))
        {
            for (int ChunkZ = 0; ChunkZ < Cells / ChunkCells; ++ChunkZ)
            {
                for (int ChunkX = 0; ChunkX < Cells / ChunkCells; ++ChunkX)
                {
                    BuildChunk(World, Registry, ChunkX, ChunkZ);
                }
            }
        }

        BuildSea(World, Registry);
    }

    private static FVector4 SurfaceColor(float X, float Z, float Height, float PadWeight, FVector4 PadColor, float RoadWeight)
    {
        float Variation = Noise.Fbm(X * 0.03f + 11.0f, Z * 0.03f - 7.0f);
        FVector4 Color = Palette.Mix(Palette.Grass, Palette.GrassDry, Mathf.Clamp01(Variation * 1.6f - 0.4f));

        float Jungle = Mathf.SmoothStep(80.0f, 260.0f, Z) * Mathf.Clamp01(Noise.Fbm(X * 0.01f, Z * 0.01f) * 2.0f - 0.3f);
        Color = Palette.Mix(Color, Palette.Jungle, Jungle * 0.8f);

        float Slope = SlopeAt(X, Z);
        Color = Palette.Mix(Color, Palette.Rock, Mathf.SmoothStep(0.35f, 0.7f, Slope));
        Color = Palette.Mix(Color, Palette.Rock, Mathf.SmoothStep(26.0f, 40.0f, Height));
        Color = Palette.Mix(Color, Palette.Sand, 1.0f - Mathf.SmoothStep(1.2f, 3.2f, Height));
        Color = Palette.Mix(Color, Palette.Shade(Palette.Sand, 0.7f), 1.0f - Mathf.SmoothStep(-2.0f, 0.5f, Height));
        Color = Palette.Mix(Color, PadColor, PadWeight * 0.9f);
        Color = Palette.Mix(Color, Palette.Road, RoadWeight);
        return Color;
    }

    private static float SlopeAt(float X, float Z)
    {
        float Step = CellSize;
        float DX = RawHeight(X + Step, Z) - RawHeight(X - Step, Z);
        float DZ = RawHeight(X, Z + Step) - RawHeight(X, Z - Step);
        return MathF.Sqrt(DX * DX + DZ * DZ) / (2.0f * Step);
    }

    private static void BuildChunk(CWorld World, EntityRegistry Registry, int ChunkX, int ChunkZ)
    {
        int Verts = Cells + 1;
        int Side = ChunkCells + 1;
        int StartCol = ChunkX * ChunkCells;
        int StartRow = ChunkZ * ChunkCells;

        FVector3[] Positions = new FVector3[Side * Side];
        FVector3[] Normals = new FVector3[Side * Side];
        FVector4[] VertexColors = new FVector4[Side * Side];
        FVector2[] UVs = new FVector2[Side * Side];
        int[] Indices = new int[ChunkCells * ChunkCells * 6];

        FVector3 Origin = new(-HalfSize + StartCol * CellSize, 0.0f, -HalfSize + StartRow * CellSize);

        for (int Row = 0; Row < Side; ++Row)
        {
            for (int Col = 0; Col < Side; ++Col)
            {
                int GlobalRow = StartRow + Row;
                int GlobalCol = StartCol + Col;
                int Local = Row * Side + Col;
                float Height = Heights[GlobalRow * Verts + GlobalCol];
                Positions[Local] = new FVector3(Col * CellSize, Height, Row * CellSize);
                VertexColors[Local] = Palette.ToLinear(Colors[GlobalRow * Verts + GlobalCol]);
                UVs[Local] = new FVector2(Col * 0.5f, Row * 0.5f);

                float Left = Heights[GlobalRow * Verts + Math.Max(GlobalCol - 1, 0)];
                float Right = Heights[GlobalRow * Verts + Math.Min(GlobalCol + 1, Cells)];
                float Down = Heights[Math.Max(GlobalRow - 1, 0) * Verts + GlobalCol];
                float Up = Heights[Math.Min(GlobalRow + 1, Cells) * Verts + GlobalCol];
                Normals[Local] = new FVector3(Left - Right, 2.0f * CellSize, Down - Up).Normalized();
            }
        }

        int Cursor = 0;
        for (int Row = 0; Row < ChunkCells; ++Row)
        {
            for (int Col = 0; Col < ChunkCells; ++Col)
            {
                int V00 = Row * Side + Col;
                int V10 = V00 + 1;
                int V01 = V00 + Side;
                int V11 = V01 + 1;

                Indices[Cursor++] = V00;
                Indices[Cursor++] = V11;
                Indices[Cursor++] = V10;
                Indices[Cursor++] = V00;
                Indices[Cursor++] = V01;
                Indices[Cursor++] = V11;
            }
        }

        Entity Chunk = World.CreateEntity($"Terrain_{ChunkX}_{ChunkZ}", Origin);
        SDynamicMeshComponent Mesh = Registry.GetOrAdd<SDynamicMeshComponent>(Chunk)!;
        Mesh.bGenerateTangents = false;
        Mesh.bFastMeshletBuild = true;
        Mesh.ClearMesh();
        Mesh.SetPositions(Positions);
        Mesh.SetNormals(Normals);
        Mesh.SetUVs(UVs);
        Mesh.SetColors(VertexColors);
        Mesh.SetIndices(Indices);
        Mesh.AddSection(0, 0, Indices.Length);
        if (Materials.Solid is { } Material)
        {
            Mesh.SetMaterialAtSlot(Material, 0);
        }

        Mesh.Commit();

        SDynamicMeshColliderComponent Collider = Registry.GetOrAdd<SDynamicMeshColliderComponent>(Chunk)!;
        Collider.bConvex = false;
        Collider.bAffectsNavigation = true;
        SRigidBodyComponent Body = Registry.GetOrAdd<SRigidBodyComponent>(Chunk)!;
        Body.BodyType = EBodyType.Static;
    }

    private static void BuildSea(CWorld World, EntityRegistry Registry)
    {
        MeshKit Kit = new();
        float Extent = 3000.0f;
        Kit.Quad(new FVector3(-Extent, 0, -Extent), new FVector3(Extent, 0, -Extent), new FVector3(Extent, 0, Extent), new FVector3(-Extent, 0, Extent), FVector3.Up, Palette.Water);
        Entity Sea = World.CreateEntity("Sea", new FVector3(0.0f, SeaLevel - 0.35f, 0.0f));
        Kit.Commit(Registry, Sea, false, false);
    }

    // Interpolates across the same triangle split the mesh uses, so feet and wheels sit on the drawn surface.
    public static float HeightAt(float X, float Z)
    {
        if (Heights.Length == 0)
        {
            return RawHeight(X, Z);
        }

        float GX = (X + HalfSize) / CellSize;
        float GZ = (Z + HalfSize) / CellSize;
        if (GX < 0.0f || GZ < 0.0f || GX >= Cells || GZ >= Cells)
        {
            return -12.0f;
        }

        int Col = (int)GX;
        int Row = (int)GZ;
        float FX = GX - Col;
        float FZ = GZ - Row;
        int Verts = Cells + 1;
        float H00 = Heights[Row * Verts + Col];
        float H10 = Heights[Row * Verts + Col + 1];
        float H01 = Heights[(Row + 1) * Verts + Col];
        float H11 = Heights[(Row + 1) * Verts + Col + 1];

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
