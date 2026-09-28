using System;
using System.Collections.Generic;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

// Plants built once per variant and shared by every instance. Vertex alpha is how far M_Foliage lets the wind move a vertex.
public static class FoliageShapes
{
    public const int Variants = 3;

    private static readonly FVector4 Bark = Palette.Hex(0x4E3826);
    private static readonly FVector4 PalmBark = Palette.Hex(0x8A6E4B);
    private static readonly FVector4 Cut = Palette.Hex(0xC9A66B);

    public static float HeightOf(EFoliageKind Kind, float Scale) => Kind switch
    {
        EFoliageKind.JungleTree => 10.0f * Scale,
        EFoliageKind.Palm => 8.0f * Scale,
        EFoliageKind.Bush => 1.2f * Scale,
        _ => 6.5f * Scale,
    };

    public static void Draw(MeshKit Kit, EFoliageKind Kind, int Variant)
    {
        Random Rng = new(4099 + (int)Kind * 131 + Variant * 17);
        switch (Kind)
        {
            case EFoliageKind.Tree:
                Broadleaf(Kit, Rng, HeightOf(Kind, 1.0f), Palette.Hex(0x4C8C35), false);
                break;
            case EFoliageKind.JungleTree:
                Broadleaf(Kit, Rng, HeightOf(Kind, 1.0f), Palette.Hex(0x3C7A31), true);
                break;
            case EFoliageKind.Palm:
                Palm(Kit, Rng, HeightOf(Kind, 1.0f));
                break;
            case EFoliageKind.Bush:
                Bush(Kit, Rng);
                break;
        }
    }

    public static void Stump(MeshKit Kit)
    {
        FVector3[] Path = { FVector3.Zero, new(0.0f, 0.25f, 0.0f), new(0.0f, 0.5f, 0.0f) };
        float[] Radii = { 0.34f, 0.26f, 0.24f };
        Kit.Sweep(Path, Radii, 8, Along => Sway(Palette.Shade(Bark, 0.8f + 0.2f * Along), 0.0f), false, false);
        Kit.Cylinder(new FVector3(0.0f, 0.49f, 0.0f), 0.23f, 0.02f, Cut, 8);
    }

    private static void Broadleaf(MeshKit Kit, Random Rng, float Height, FVector4 Leaf, bool bJungle)
    {
        float TrunkTop = Height * (bJungle ? 0.55f : 0.34f);
        float Phase = Range(Rng, 0.0f, MathF.Tau);
        float Wobble = bJungle ? 0.12f : 0.3f;

        const int TrunkRings = 7;
        FVector3[] Trunk = new FVector3[TrunkRings];
        float[] TrunkRadii = new float[TrunkRings];
        for (int Ring = 0; Ring < TrunkRings; ++Ring)
        {
            float T = Ring / (float)(TrunkRings - 1);
            Trunk[Ring] = new FVector3(MathF.Sin(T * 2.2f + Phase) * Wobble * T, TrunkTop * T, MathF.Cos(T * 1.7f + Phase) * Wobble * T);
            // Flared at the root, where a trunk meets the ground.
            float Flare = MathF.Pow(1.0f - T, 4.0f) * 0.7f;
            TrunkRadii[Ring] = (bJungle ? 0.34f : 0.26f) * (1.0f - 0.4f * T) * (1.0f + Flare);
        }
        Kit.Sweep(Trunk, TrunkRadii, 8, Along => Sway(Palette.Shade(Bark, 0.7f + 0.35f * Along), 0.05f * Along * Along), false, false);

        if (bJungle)
        {
            Buttresses(Kit, Rng);
        }

        FVector3 CanopyCenter = Trunk[TrunkRings - 1] + new FVector3(0.0f, Height * (bJungle ? 0.2f : 0.26f), 0.0f);
        float Spread = Height * (bJungle ? 0.3f : 0.22f);
        int ClumpCount = 9;

        List<FVector3> Clumps = new() { CanopyCenter + new FVector3(0.0f, Height * (bJungle ? 0.06f : 0.1f), 0.0f) };
        for (int Index = 1; Index < ClumpCount; ++Index)
        {
            float Angle = Index * 2.4f + Range(Rng, -0.4f, 0.4f);
            float Reach = Spread * Range(Rng, 0.55f, 1.0f);
            float Rise = Height * Range(Rng, -0.1f, 0.06f);
            Clumps.Add(CanopyCenter + new FVector3(MathF.Cos(Angle) * Reach, Rise, MathF.Sin(Angle) * Reach));
        }

        // A few branches reach from the trunk top into the outer clumps, so the canopy is held up by something.
        for (int Index = 1; Index < Clumps.Count; Index += 2)
        {
            FVector3 From = Trunk[TrunkRings - 2];
            FVector3 To = Clumps[Index] - new FVector3(0.0f, Height * 0.05f, 0.0f);
            FVector3 Mid = (From + To) * 0.5f + new FVector3(0.0f, Height * 0.06f, 0.0f);
            FVector3[] Branch = { From, Mid, To };
            float[] Radii = { TrunkRadii[TrunkRings - 2] * 0.55f, 0.08f, 0.04f };
            Kit.Sweep(Branch, Radii, 6, Along => Sway(Palette.Shade(Bark, 0.9f), 0.15f * Along), false, false);
        }

        float CanopyRadius = Spread + Height * 0.2f;
        for (int Index = 0; Index < Clumps.Count; ++Index)
        {
            FVector3 Center = Clumps[Index];
            float Size = Height * (bJungle ? Range(Rng, 0.17f, 0.23f) : Range(Rng, 0.2f, 0.27f));
            FVector3 Radii = new(Size, Size * (bJungle ? 0.68f : 0.85f), Size);
            FVector4 Tint = Hue(Leaf, Range(Rng, -0.08f, 0.08f));
            float Seed = Range(Rng, 0.0f, 100.0f);
            Kit.Blob(Center, Radii, 2,
                Direction => (Noise3(Direction * 2.4f + new FVector3(Seed)) - 0.5f) * 0.55f,
                (Direction, Point) => PaintLeaves(Tint, Direction, Point, CanopyCenter, CanopyRadius, Height));
        }
    }

    // Plank roots fanning out from a rainforest trunk.
    private static void Buttresses(MeshKit Kit, Random Rng)
    {
        int Count = 4;
        float Start = Range(Rng, 0.0f, MathF.Tau);
        for (int Index = 0; Index < Count; ++Index)
        {
            float Angle = Start + Index * MathF.Tau / Count + Range(Rng, -0.3f, 0.3f);
            FVector3 Out = new(MathF.Cos(Angle), 0.0f, MathF.Sin(Angle));
            FVector3[] Root = { Out * 0.15f + new FVector3(0.0f, 1.6f, 0.0f), Out * 0.6f + new FVector3(0.0f, 0.6f, 0.0f), Out * 1.25f + new FVector3(0.0f, -0.1f, 0.0f) };
            float[] Radii = { 0.2f, 0.16f, 0.07f };
            Kit.Sweep(Root, Radii, 6, Along => Sway(Palette.Shade(Bark, 0.75f), 0.0f), false, true);
        }
    }

    private static FVector4 PaintLeaves(FVector4 Leaf, FVector3 Direction, FVector3 Point, FVector3 CanopyCenter, float CanopyRadius, float Height)
    {
        // Cavities between clumps and the underside get less sky, which the vertex color carries as baked occlusion.
        float Outer = Mathf.Clamp01((Point - CanopyCenter).Length / CanopyRadius);
        float Up = Direction.Y * 0.5f + 0.5f;
        float Light = 0.62f + 0.3f * Up + 0.22f * Outer;
        float Speckle = (Noise3(Point * 1.9f) - 0.5f) * 0.16f;
        FVector4 Color = Palette.Shade(Leaf, Light + Speckle);
        return Sway(Color, Mathf.Clamp01(0.35f + 0.65f * Outer) * Mathf.Clamp01(Point.Y / Height + 0.2f));
    }

    private static void Palm(MeshKit Kit, Random Rng, float Height)
    {
        float Bend = Range(Rng, 0.9f, 1.8f);
        const int TrunkRings = 12;
        FVector3[] Trunk = new FVector3[TrunkRings];
        float[] Radii = new float[TrunkRings];
        for (int Ring = 0; Ring < TrunkRings; ++Ring)
        {
            float T = Ring / (float)(TrunkRings - 1);
            Trunk[Ring] = new FVector3(Bend * T * T, Height * T, 0.0f);
            // Alternating rings read as the leaf scars a palm trunk is banded with.
            Radii[Ring] = (0.25f - 0.09f * T) * (Ring % 2 == 0 ? 1.0f : 1.07f) * (1.0f + MathF.Pow(1.0f - T, 6.0f) * 0.5f);
        }
        Kit.Sweep(Trunk, Radii, 8, Along => Sway(Palette.Shade(PalmBark, (MathF.Floor(Along * 11.0f) % 2 == 0 ? 0.85f : 1.0f) * (0.75f + 0.25f * Along)), 0.12f * Along * Along), false, true);

        FVector3 Top = Trunk[TrunkRings - 1];
        Kit.Blob(Top + new FVector3(0.0f, 0.1f, 0.0f), new FVector3(0.32f, 0.28f, 0.32f), 1, null,
            (Direction, Point) => Sway(Palette.Shade(Palette.Hex(0x6B6A2E), 0.7f + 0.3f * (Direction.Y * 0.5f + 0.5f)), 0.15f));

        int Nuts = 3 + Rng.Next(0, 3);
        for (int Index = 0; Index < Nuts; ++Index)
        {
            float Angle = Index * MathF.Tau / Nuts + Range(Rng, -0.3f, 0.3f);
            FVector3 At = Top + new FVector3(MathF.Cos(Angle) * 0.24f, -0.22f, MathF.Sin(Angle) * 0.24f);
            Kit.Blob(At, new FVector3(0.14f), 1, null, (Direction, Point) => Sway(Palette.Shade(Palette.Hex(0x5C4A1E), 0.7f + 0.3f * (Direction.Y * 0.5f + 0.5f)), 0.15f));
        }

        int Fronds = 9;
        float Start = Range(Rng, 0.0f, MathF.Tau);
        for (int Index = 0; Index < Fronds; ++Index)
        {
            float Yaw = Start + Index * MathF.Tau / Fronds + Range(Rng, -0.15f, 0.15f);
            float Lift = Range(Rng, 0.25f, 0.75f);
            float Length = Range(Rng, 3.1f, 3.9f);
            Frond(Kit, Top + new FVector3(0.0f, 0.15f, 0.0f), Yaw, Lift, Length, Hue(Palette.PalmLeaves, Range(Rng, -0.06f, 0.06f)));
        }
    }

    // A spine arcing out and drooping, with a serrated V of leaflets either side, drawn from both faces.
    private static void Frond(MeshKit Kit, FVector3 Base, float Yaw, float Lift, float Length, FVector4 Color)
    {
        FVector3 Out = new(MathF.Cos(Yaw), 0.0f, MathF.Sin(Yaw));
        FVector3 Across = new(-Out.Z, 0.0f, Out.X);
        const int Samples = 13;

        FVector3[] Left = new FVector3[Samples];
        FVector3[] Spine = new FVector3[Samples];
        FVector3[] Right = new FVector3[Samples];
        for (int Sample = 0; Sample < Samples; ++Sample)
        {
            float T = Sample / (float)(Samples - 1);
            float Reach = T * Length;
            Spine[Sample] = Base + Out * Reach * 0.92f + new FVector3(0.0f, MathF.Sin(Lift) * Reach - 1.7f * T * T * Length * 0.5f, 0.0f);
            float Width = 0.65f * MathF.Sin(MathF.PI * MathF.Min(T * 1.1f + 0.05f, 1.0f)) * (Sample % 2 == 0 ? 1.0f : 0.72f);
            FVector3 Droop = new(0.0f, -0.28f * Width, 0.0f);
            Left[Sample] = Spine[Sample] - Across * Width + Droop;
            Right[Sample] = Spine[Sample] + Across * Width + Droop;
        }

        for (int Face = 0; Face < 2; ++Face)
        {
            // Both faces lean their normal up, so the underside lit from below does not turn black.
            int[] L = new int[Samples];
            int[] S = new int[Samples];
            int[] R = new int[Samples];
            for (int Sample = 0; Sample < Samples; ++Sample)
            {
                float T = Sample / (float)(Samples - 1);
                FVector4 Shade = Sway(Palette.Shade(Color, (0.7f + 0.45f * T) * (Face == 0 ? 1.0f : 0.8f)), 0.3f + 0.7f * T);
                FVector3 Tilt = (Out * 0.25f + FVector3.Up).Normalized();
                L[Sample] = Kit.AddVertex(Left[Sample], (Tilt - Across * 0.3f).Normalized(), Shade);
                S[Sample] = Kit.AddVertex(Spine[Sample], Tilt, Shade);
                R[Sample] = Kit.AddVertex(Right[Sample], (Tilt + Across * 0.3f).Normalized(), Shade);
            }

            for (int Sample = 0; Sample + 1 < Samples; ++Sample)
            {
                if (Face == 0)
                {
                    Quad(Kit, L[Sample], S[Sample], S[Sample + 1], L[Sample + 1]);
                    Quad(Kit, S[Sample], R[Sample], R[Sample + 1], S[Sample + 1]);
                }
                else
                {
                    Quad(Kit, L[Sample + 1], S[Sample + 1], S[Sample], L[Sample]);
                    Quad(Kit, S[Sample + 1], R[Sample + 1], R[Sample], S[Sample]);
                }
            }
        }
    }

    private static void Bush(MeshKit Kit, Random Rng)
    {
        int Count = 3 + Rng.Next(0, 2);
        for (int Index = 0; Index < Count; ++Index)
        {
            float Angle = Index * 2.4f + Range(Rng, -0.3f, 0.3f);
            float Reach = Index == 0 ? 0.0f : Range(Rng, 0.35f, 0.6f);
            float Size = Range(Rng, 0.5f, 0.8f);
            FVector3 Center = new(MathF.Cos(Angle) * Reach, Size * 0.55f, MathF.Sin(Angle) * Reach);
            FVector4 Tint = Hue(Palette.Hex(0x4C8035), Range(Rng, -0.08f, 0.08f));
            float Seed = Range(Rng, 0.0f, 100.0f);
            Kit.Blob(Center, new FVector3(Size, Size * 0.75f, Size), 2,
                Direction => (Noise3(Direction * 3.0f + new FVector3(Seed)) - 0.5f) * 0.5f,
                (Direction, Point) => Sway(Palette.Shade(Tint, 0.62f + 0.3f * (Direction.Y * 0.5f + 0.5f) + 0.2f * Mathf.Clamp01(Point.Y / 1.2f)), 0.45f * Mathf.Clamp01(Point.Y / 1.0f)));
        }
    }

    private static void Quad(MeshKit Kit, int A, int B, int C, int D)
    {
        Kit.AddTriangle(A, B, C);
        Kit.AddTriangle(A, C, D);
    }

    private static FVector4 Sway(FVector4 Color, float Amount) => new(Color.X, Color.Y, Color.Z, Mathf.Clamp01(Amount));

    // Pushes a green toward yellow for positive shifts and toward blue for negative ones.
    private static FVector4 Hue(FVector4 Color, float Shift) => new(Color.X * (1.0f + Shift * 1.6f), Color.Y, Color.Z * (1.0f - Shift * 1.6f), Color.W);

    private static float Range(Random Rng, float Min, float Max) => Min + (float)Rng.NextDouble() * (Max - Min);

    private static float Noise3(FVector3 P)
    {
        int X = (int)MathF.Floor(P.X);
        int Y = (int)MathF.Floor(P.Y);
        int Z = (int)MathF.Floor(P.Z);
        float FX = Smooth(P.X - X);
        float FY = Smooth(P.Y - Y);
        float FZ = Smooth(P.Z - Z);
        float Lerp(float A, float B, float T) => A + (B - A) * T;
        float Plane(int Layer) => Lerp(Lerp(Hash(X, Y, Layer), Hash(X + 1, Y, Layer), FX), Lerp(Hash(X, Y + 1, Layer), Hash(X + 1, Y + 1, Layer), FX), FY);
        return Lerp(Plane(Z), Plane(Z + 1), FZ);
    }

    private static float Smooth(float T) => T * T * (3.0f - 2.0f * T);

    private static float Hash(int X, int Y, int Z)
    {
        unchecked
        {
            uint H = (uint)(X * 374761393 + Y * 668265263 + Z * 1442695041);
            H = (H ^ (H >> 13)) * 1274126177u;
            H ^= H >> 16;
            return (H & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }
}
