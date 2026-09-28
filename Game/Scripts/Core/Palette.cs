using System;
using Lumina;

namespace Mercenaries;

public static class Palette
{
    public static FVector4 Rgb(float R, float G, float B) => new(R, G, B, 1.0f);

    public static FVector4 Hex(uint Value) => new(((Value >> 16) & 0xFF) / 255.0f, ((Value >> 8) & 0xFF) / 255.0f, (Value & 0xFF) / 255.0f, 1.0f);

    public static FVector4 Shade(FVector4 Color, float Factor) => new(Color.X * Factor, Color.Y * Factor, Color.Z * Factor, Color.W);

    // Authored colors are sRGB, while the vertex color stream is read as linear.
    public static FVector4 ToLinear(FVector4 Color) => new(MathF.Pow(Color.X, 2.2f), MathF.Pow(Color.Y, 2.2f), MathF.Pow(Color.Z, 2.2f), Color.W);

    public static FVector4 Mix(FVector4 A, FVector4 B, float T) => FVector4.Lerp(A, B, T);

    public static readonly FVector4 Skin = Hex(0xC68A64);
    public static readonly FVector4 SkinDark = Hex(0x7A4E33);
    public static readonly FVector4 Gunmetal = Hex(0x2B2D30);
    public static readonly FVector4 Rubber = Hex(0x1A1A1A);
    public static readonly FVector4 Glass = Hex(0x4C6D80);
    public static readonly FVector4 Concrete = Hex(0x9B968C);
    public static readonly FVector4 ConcreteDark = Hex(0x6E6A63);
    public static readonly FVector4 Plaster = Hex(0xD9C9A8);
    public static readonly FVector4 PlasterPink = Hex(0xD6A38F);
    public static readonly FVector4 PlasterBlue = Hex(0x93B3C4);
    public static readonly FVector4 PlasterGreen = Hex(0xA9BF8E);
    public static readonly FVector4 RoofTile = Hex(0x9E4A32);
    public static readonly FVector4 RoofTin = Hex(0x7F8588);
    public static readonly FVector4 Wood = Hex(0x7A5634);
    public static readonly FVector4 WoodDark = Hex(0x4E3620);
    public static readonly FVector4 Sandbag = Hex(0xB39E73);
    public static readonly FVector4 Canvas = Hex(0x6B6A45);
    public static readonly FVector4 Rust = Hex(0x8C4A2A);
    public static readonly FVector4 Steel = Hex(0x8A9096);
    public static readonly FVector4 Grass = Hex(0x5E8C3A);
    public static readonly FVector4 GrassDry = Hex(0x9A9A4E);
    public static readonly FVector4 Jungle = Hex(0x2F5E2A);
    public static readonly FVector4 Sand = Hex(0xD8C58E);
    public static readonly FVector4 Dirt = Hex(0x8B6D47);
    public static readonly FVector4 Road = Hex(0x4A4642);
    public static readonly FVector4 Rock = Hex(0x7C7770);
    public static readonly FVector4 Water = Hex(0x1F5F7A);
    public static readonly FVector4 TreeTrunk = Hex(0x5A3E26);
    public static readonly FVector4 Leaves = Hex(0x2E6B2A);
    public static readonly FVector4 PalmLeaves = Hex(0x3D8A34);
    public static readonly FVector4 Fire = Rgb(1.0f, 0.45f, 0.08f);
    public static readonly FVector4 FireCore = Rgb(1.0f, 0.85f, 0.4f);
    public static readonly FVector4 Smoke = Hex(0x4A4A4A);
    public static readonly FVector4 SmokeLight = Hex(0x9A9A9A);
    public static readonly FVector4 Tracer = Rgb(1.0f, 0.8f, 0.35f);
    public static readonly FVector4 Cash = Hex(0x3FAE49);
    public static readonly FVector4 FuelRed = Hex(0xB02A1E);
    public static readonly FVector4 AmmoOlive = Hex(0x55602F);
    public static readonly FVector4 Medic = Hex(0xE8E8E8);
    public static readonly FVector4 Wreck = Hex(0x2A2522);
}
