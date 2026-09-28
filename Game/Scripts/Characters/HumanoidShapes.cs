using System;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

// Sculpted body parts for HumanoidBody.BuildRig, in body space with the soles at Feet.
public static class HumanoidShapes
{
    public const int Variants = 4;

    private static readonly FVector4[] SkinTones = { Palette.Hex(0xE3B08A), Palette.Hex(0xC68A64), Palette.Hex(0x9C6848), Palette.Hex(0x6E452D) };
    private static readonly FVector4[] HairColors = { Palette.Hex(0x1C1712), Palette.Hex(0x3A2A1C), Palette.Hex(0x5E4632), Palette.Hex(0x121010) };
    private static readonly FVector4 Eye = Palette.Hex(0x16120F);
    private static readonly FVector4 Wood = Palette.Hex(0x6B4A2B);

    public static FVector4 SkinFor(EFaction Faction, int Variant) => Faction is EFaction.Pirate or EFaction.Guerrilla
        ? SkinTones[2 + Variant % 2]
        : SkinTones[Variant % 3];

    public static FVector4 HairFor(int Variant) => HairColors[Variant % HairColors.Length];

    public static void Leg(MeshKit Kit, float X, float Feet, FVector4 Pants, FVector4 Boot, bool bKneePad)
    {
        FVector3 Hip = new(X, Feet + 0.85f, 0.0f);
        FVector3 Knee = new(X * 1.04f, Feet + 0.47f, 0.035f);
        FVector3 Ankle = new(X, Feet + 0.14f, 0.0f);
        FVector3[] Path = { Hip, (Hip + Knee) * 0.5f + new FVector3(0.0f, 0.0f, 0.012f), Knee, (Knee + Ankle) * 0.5f - new FVector3(0.0f, 0.0f, 0.012f), Ankle };
        float[] Radii = { 0.122f, 0.105f, 0.08f, 0.072f, 0.058f };
        Kit.Sweep(Path, Radii, 8, Along => Palette.Shade(Pants, 1.0f - 0.2f * Along), false, false);

        if (bKneePad)
        {
            Kit.Blob(Knee + new FVector3(0.0f, 0.0f, 0.055f), new FVector3(0.055f, 0.06f, 0.028f), 1, null, (Direction, Point) => Lit(Palette.Rubber, Direction, 1.4f));
        }

        // Cut flat underneath, so the boot stands on its sole instead of rocking on a curve.
        Kit.Blob(new FVector3(X, Feet + 0.085f, 0.035f), new FVector3(0.074f, 0.085f, 0.135f), 2,
            Direction => Direction.Y < 0.0f ? -0.5f * -Direction.Y : 0.0f, (Direction, Point) => Lit(Boot, Direction));
        Kit.Box(new FVector3(X, Feet + 0.012f, 0.035f), new FVector3(0.064f, 0.012f, 0.126f), Palette.Shade(Boot, 0.55f));
    }

    public static void Torso(MeshKit Kit, float Feet, FVector4 Shirt, FVector4 Pants, FVector4 Skin, FVector4? Vest, FVector4 Belt, bool bPack)
    {
        Kit.Blob(new FVector3(0.0f, Feet + 0.86f, 0.0f), new FVector3(0.2f, 0.12f, 0.13f), 2, null, (Direction, Point) => Lit(Pants, Direction));
        Kit.Blob(new FVector3(0.0f, Feet + 1.0f, 0.005f), new FVector3(0.182f, 0.16f, 0.128f), 2, null, (Direction, Point) => Lit(Shirt, Direction));
        Kit.Blob(new FVector3(0.0f, Feet + 1.2f, 0.01f), new FVector3(0.24f, 0.185f, 0.15f), 2, null, (Direction, Point) => Lit(Shirt, Direction));
        for (int Side = -1; Side <= 1; Side += 2)
        {
            Kit.Blob(new FVector3(0.22f * Side, Feet + 1.3f, 0.0f), new FVector3(0.1f, 0.09f, 0.095f), 1, null, (Direction, Point) => Lit(Shirt, Direction));
        }

        FVector3[] Neck = { new(0.0f, Feet + 1.36f, 0.0f), new(0.0f, Feet + 1.47f, 0.005f) };
        float[] NeckRadii = { 0.072f, 0.064f };
        Kit.Sweep(Neck, NeckRadii, 8, Along => Palette.Shade(Skin, 0.85f), false, false);

        Kit.Blob(new FVector3(0.0f, Feet + 0.91f, 0.0f), new FVector3(0.195f, 0.03f, 0.137f), 2, null, (Direction, Point) => Lit(Belt, Direction));
        Kit.Box(new FVector3(0.0f, Feet + 0.91f, 0.136f), new FVector3(0.03f, 0.022f, 0.006f), Palette.Hex(0x8C8062));

        if (Vest is { } Plate)
        {
            Kit.Blob(new FVector3(0.0f, Feet + 1.14f, 0.008f), new FVector3(0.245f, 0.2f, 0.162f), 2, null, (Direction, Point) => Lit(Plate, Direction));
            FVector4 Pouch = Palette.Shade(Plate, 0.82f);
            for (int Index = -1; Index <= 1; ++Index)
            {
                Kit.Box(new FVector3(Index * 0.075f, Feet + 1.03f, 0.165f), new FVector3(0.03f, 0.045f, 0.022f), Pouch);
            }
        }

        if (bPack)
        {
            FVector4 Pack = Palette.Shade(Vest ?? Shirt, 0.72f);
            Kit.Box(new FVector3(0.0f, Feet + 1.14f, -0.2f), new FVector3(0.13f, 0.16f, 0.065f), Pack);
            Kit.Tube(new FVector3(0.11f, Feet + 1.28f, -0.22f), new FVector3(0.13f, Feet + 1.56f, -0.25f), 0.007f, 0.004f, Palette.Rubber, 4);
        }
    }

    public static void Head(MeshKit Kit, float Feet, FVector4 Skin, FVector4 Hair, bool bHair, bool bBeard)
    {
        float Y = Feet + 1.585f;
        Kit.Blob(new FVector3(0.0f, Y + 0.02f, -0.005f), new FVector3(0.103f, 0.118f, 0.113f), 2, null, (Direction, Point) => Lit(Skin, Direction, 0.9f));
        Kit.Blob(new FVector3(0.0f, Y - 0.052f, 0.022f), new FVector3(0.08f, 0.062f, 0.085f), 2, null, (Direction, Point) => Lit(Skin, Direction, 0.9f));
        Kit.Blob(new FVector3(0.0f, Y - 0.005f, 0.113f), new FVector3(0.017f, 0.028f, 0.022f), 1, null, (Direction, Point) => Lit(Skin, Direction, 0.9f));
        for (int Side = -1; Side <= 1; Side += 2)
        {
            Kit.Blob(new FVector3(0.103f * Side, Y + 0.005f, -0.005f), new FVector3(0.016f, 0.032f, 0.022f), 1, null, (Direction, Point) => Lit(Skin, Direction, 0.8f));
            Kit.Blob(new FVector3(0.038f * Side, Y + 0.02f, 0.098f), new FVector3(0.016f, 0.012f, 0.008f), 1, null, (Direction, Point) => Eye);
            Kit.Box(new FVector3(0.04f * Side, Y + 0.047f, 0.103f), new FVector3(0.026f, 0.006f, 0.008f), Palette.Shade(Hair, 0.9f));
        }
        Kit.Box(new FVector3(0.0f, Y - 0.05f, 0.103f), new FVector3(0.024f, 0.004f, 0.005f), Palette.Shade(Skin, 0.55f));

        if (bHair)
        {
            // Set back and up, so its front sinks into the forehead and only the crown and back show.
            Kit.Blob(new FVector3(0.0f, Y + 0.06f, -0.03f), new FVector3(0.109f, 0.088f, 0.106f), 2, null, (Direction, Point) => Lit(Hair, Direction));
        }
        if (bBeard)
        {
            Kit.Blob(new FVector3(0.0f, Y - 0.07f, 0.036f), new FVector3(0.084f, 0.05f, 0.082f), 2, null, (Direction, Point) => Lit(Hair, Direction));
        }
    }

    public static void Headgear(MeshKit Kit, float Feet, EHeadgear Gear, FVector4 Color, FVector4 Accent, FVector4 Bill)
    {
        float Y = Feet + 1.585f;
        // A dome whose lower half is pulled up inside the head, so only a shell shows above the brow.
        Func<FVector3, float> Dome = Direction => Direction.Y < 0.0f ? -0.55f * -Direction.Y : 0.0f;
        switch (Gear)
        {
            case EHeadgear.Helmet:
                Kit.Blob(new FVector3(0.0f, Y + 0.07f, -0.005f), new FVector3(0.14f, 0.11f, 0.15f), 2, Dome, (Direction, Point) => Lit(Color, Direction));
                for (int Side = -1; Side <= 1; Side += 2)
                {
                    Kit.Box(new FVector3(0.104f * Side, Y - 0.02f, 0.0f), new FVector3(0.005f, 0.055f, 0.008f), Palette.Shade(Color, 0.5f));
                }
                break;
            case EHeadgear.Cap:
                Kit.Blob(new FVector3(0.0f, Y + 0.08f, -0.005f), new FVector3(0.113f, 0.068f, 0.12f), 2, Dome, (Direction, Point) => Lit(Color, Direction));
                Kit.Blob(new FVector3(0.0f, Y + 0.06f, 0.115f), new FVector3(0.082f, 0.01f, 0.05f), 1, null, (Direction, Point) => Lit(Bill, Direction));
                break;
            case EHeadgear.Beret:
                Kit.Blob(new FVector3(0.03f, Y + 0.095f, -0.01f), new FVector3(0.125f, 0.042f, 0.12f), 2, Direction => Direction.Y < 0.0f ? -0.3f : 0.0f, (Direction, Point) => Lit(Accent, Direction));
                break;
            case EHeadgear.HardHat:
                Kit.Blob(new FVector3(0.0f, Y + 0.09f, 0.0f), new FVector3(0.132f, 0.1f, 0.142f), 2, Dome, (Direction, Point) => Lit(Color, Direction));
                Kit.Cylinder(new FVector3(0.0f, Y + 0.065f, 0.015f), 0.165f, 0.012f, Palette.Shade(Color, 0.9f), 14);
                break;
            case EHeadgear.Bandana:
                Kit.Blob(new FVector3(0.0f, Y + 0.065f, -0.012f), new FVector3(0.112f, 0.078f, 0.12f), 2, Dome, (Direction, Point) => Lit(Color, Direction));
                Kit.Blob(new FVector3(0.0f, Y + 0.03f, -0.125f), new FVector3(0.03f), 1, null, (Direction, Point) => Lit(Accent, Direction));
                Kit.Box(new FVector3(0.0f, Y - 0.03f, -0.13f), new FVector3(0.025f, 0.06f, 0.01f), Accent);
                break;
            case EHeadgear.Boonie:
                Kit.Blob(new FVector3(0.0f, Y + 0.09f, 0.0f), new FVector3(0.12f, 0.072f, 0.127f), 2, Dome, (Direction, Point) => Lit(Color, Direction));
                Kit.Cylinder(new FVector3(0.0f, Y + 0.055f, 0.0f), 0.205f, 0.012f, Palette.Shade(Color, 0.9f), 16);
                break;
        }
    }

    // Upper arm and forearm meeting at an elbow solved to reach the hand, bent toward Pole.
    public static void Arm(MeshKit Kit, FVector3 Shoulder, FVector3 Hand, FVector3 Pole, FVector4 Sleeve, FVector4 Forearm, FVector4 Glove)
    {
        const float Upper = 0.29f;
        const float Lower = 0.28f;
        FVector3 Reach = Hand - Shoulder;
        float Distance = Math.Clamp(Reach.Length, 0.05f, (Upper + Lower) * 0.999f);
        FVector3 Axis = Reach.NormalizedOr(-FVector3.Up);
        float Along = (Upper * Upper - Lower * Lower + Distance * Distance) / (2.0f * Distance);
        float Out = MathF.Sqrt(MathF.Max(Upper * Upper - Along * Along, 0.0f));
        FVector3 Bend = (Pole - Axis * FVector3.Dot(Pole, Axis)).NormalizedOr(FVector3.Forward);
        FVector3 Elbow = Shoulder + Axis * Along + Bend * Out;
        FVector3 Wrist = Hand - (Hand - Elbow).NormalizedOr(Axis) * 0.045f;

        FVector3[] UpperPath = { Shoulder, (Shoulder + Elbow) * 0.5f, Elbow };
        float[] UpperRadii = { 0.08f, 0.072f, 0.058f };
        Kit.Sweep(UpperPath, UpperRadii, 8, T => Palette.Shade(Sleeve, 1.0f - 0.12f * T), false, false);
        Kit.Blob(Elbow, new FVector3(0.057f), 1, null, (Direction, Point) => Lit(Forearm, Direction));
        FVector3[] LowerPath = { Elbow, (Elbow + Wrist) * 0.5f, Wrist };
        float[] LowerRadii = { 0.055f, 0.05f, 0.04f };
        Kit.Sweep(LowerPath, LowerRadii, 8, T => Palette.Shade(Forearm, 0.92f - 0.08f * T), false, false);
        Kit.Blob(Hand, new FVector3(0.04f, 0.046f, 0.042f), 1, null, (Direction, Point) => Lit(Glove, Direction));
    }

    // Laid along +Z with the grip at HandR and the fore end under HandL, returning the box the weapon occupies.
    public static void Firearm(MeshKit Kit, EWeapon Weapon, FVector3 Grip, bool bWoodFurniture, out FVector3 Center, out FVector3 Half)
    {
        float Barrel = Weapon switch
        {
            EWeapon.Pistol => 0.08f,
            EWeapon.Smg => 0.18f,
            EWeapon.Sniper => 0.52f,
            EWeapon.MachineGun => 0.42f,
            EWeapon.Shotgun => 0.36f,
            _ => 0.32f,
        };
        FVector4 Metal = Palette.Gunmetal;
        FVector4 Furniture = bWoodFurniture ? Wood : Palette.Shade(Palette.Gunmetal, 1.25f);
        FVector3 Axis = new(Grip.X - 0.05f, Grip.Y + 0.045f, 0.0f);

        if (Weapon == EWeapon.Pistol)
        {
            Kit.Box(new FVector3(Axis.X, Axis.Y, Grip.Z + 0.06f), new FVector3(0.018f, 0.028f, 0.09f), Metal);
            Kit.Box(new FVector3(Axis.X, Grip.Y - 0.01f, Grip.Z + 0.0f), new FVector3(0.016f, 0.05f, 0.022f), Palette.Rubber);
            Center = new FVector3(Axis.X, Axis.Y, Grip.Z + 0.05f);
            Half = new FVector3(0.03f, 0.06f, 0.1f);
            return;
        }

        float ReceiverStart = Grip.Z - 0.04f;
        float ReceiverEnd = Grip.Z + 0.2f;
        Kit.Box(new FVector3(Axis.X, Axis.Y, (ReceiverStart + ReceiverEnd) * 0.5f), new FVector3(0.026f, 0.04f, (ReceiverEnd - ReceiverStart) * 0.5f), Metal);
        Kit.Box(new FVector3(Axis.X, Axis.Y - 0.005f, ReceiverEnd + 0.1f), new FVector3(0.024f, 0.03f, 0.1f), Furniture);
        Kit.Tube(new FVector3(Axis.X, Axis.Y + 0.01f, ReceiverEnd), new FVector3(Axis.X, Axis.Y + 0.01f, ReceiverEnd + 0.2f + Barrel), 0.011f, 0.009f, Metal, 6);
        Kit.Box(new FVector3(Axis.X, Grip.Y - 0.02f, Grip.Z), new FVector3(0.016f, 0.05f, 0.022f), Palette.Rubber, FQuat.FromEuler(-0.3f, 0.0f, 0.0f));

        // The stock runs back into the shoulder, which is what makes the weapon read as held.
        Kit.Box(new FVector3(Axis.X + 0.01f, Axis.Y - 0.02f, ReceiverStart - 0.13f), new FVector3(0.022f, 0.045f, 0.13f), Furniture, FQuat.FromEuler(0.12f, 0.0f, 0.0f));

        if (Weapon is EWeapon.Rifle or EWeapon.Smg or EWeapon.Sniper)
        {
            Kit.Box(new FVector3(Axis.X, Axis.Y - 0.08f, Grip.Z + 0.11f), new FVector3(0.016f, 0.06f, 0.025f), Metal, FQuat.FromEuler(0.25f, 0.0f, 0.0f));
        }
        if (Weapon == EWeapon.MachineGun)
        {
            Kit.Box(new FVector3(Axis.X - 0.05f, Axis.Y - 0.04f, Grip.Z + 0.1f), new FVector3(0.035f, 0.05f, 0.05f), Palette.Hex(0x3D4230));
        }
        if (Weapon == EWeapon.Sniper)
        {
            Kit.Tube(new FVector3(Axis.X, Axis.Y + 0.07f, Grip.Z - 0.02f), new FVector3(Axis.X, Axis.Y + 0.07f, Grip.Z + 0.22f), 0.022f, 0.022f, Palette.Rubber, 8);
        }
        else
        {
            Kit.Box(new FVector3(Axis.X, Axis.Y + 0.05f, Grip.Z + 0.02f), new FVector3(0.006f, 0.012f, 0.012f), Metal);
        }

        float Front = ReceiverEnd + 0.2f + Barrel;
        float Back = ReceiverStart - 0.26f;
        Center = new FVector3(Axis.X, Axis.Y, (Front + Back) * 0.5f);
        Half = new FVector3(0.05f, 0.09f, (Front - Back) * 0.5f);
    }

    public static void Launcher(MeshKit Kit, FVector3 Muzzle, FVector3 Tail, out FVector3 Center, out FVector3 Half)
    {
        FVector4 Olive = Palette.Hex(0x4A5234);
        Kit.Tube(Tail, Muzzle, 0.075f, 0.075f, Olive, 10);
        FVector3 Forward = (Muzzle - Tail).NormalizedOr(FVector3.Forward);
        Kit.Blob(Muzzle + Forward * 0.1f, new FVector3(0.07f, 0.07f, 0.13f), 1, null, (Direction, Point) => Lit(Palette.Hex(0x5B5F3A), Direction));
        Kit.Box((Tail + Muzzle) * 0.5f + new FVector3(0.0f, 0.1f, 0.0f), new FVector3(0.015f, 0.03f, 0.05f), Palette.Rubber);
        Center = (Tail + Muzzle) * 0.5f;
        Half = new FVector3(0.08f, 0.08f, (Muzzle - Tail).Length * 0.5f + 0.12f);
    }

    // Soft top-down shading baked into the color, since vertex color is all these meshes carry.
    private static FVector4 Lit(FVector4 Color, FVector3 Direction, float Floor = 0.72f)
    {
        float Up = Direction.Y * 0.5f + 0.5f;
        return Palette.Shade(Color, Floor + (1.08f - Floor) * Up);
    }
}
