using System;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

// Vehicle geometry in the vehicle's local frame, +Z forward and Y up from the ground at rest, fitting the collision box and wheel specs in VehicleDefs.
public static class VehicleShapes
{
    private static readonly FVector4 Lamp = Palette.Hex(0xFFF1C2);
    private static readonly FVector4 TailLamp = Palette.Hex(0xB3261E);
    private static readonly FVector4 Seat = Palette.Hex(0x2B2621);
    private static readonly FVector4 RotorBlade = Palette.Hex(0x222222);

    // Rounded shoulders and a steel rim either side, centered on the axle so it can steer and spin about its origin.
    public static void Wheel(MeshKit Kit, FVector3 Center, FVector3 Axis, float Radius, float Width)
    {
        FVector3 Half = Axis * (Width * 0.5f);
        FVector3[] Path = { Center - Half, Center - Half * 0.8f, Center + Half * 0.8f, Center + Half };
        float[] Radii = { Radius * 0.82f, Radius, Radius, Radius * 0.82f };
        Kit.Sweep(Path, Radii, 16, Along => Palette.Rubber, true, true);
        Kit.Tube(Center - Half * 1.03f, Center + Half * 1.03f, Radius * 0.56f, Radius * 0.56f, Palette.Steel, 12);
        Kit.Tube(Center - Half * 1.12f, Center + Half * 1.12f, Radius * 0.2f, Radius * 0.2f, Palette.Gunmetal, 8);
    }

    public static void Build(EVehicleType Type, FVector4 Paint, MeshKit Hull, MeshKit Turret, MeshKit Rotor)
    {
        switch (Type)
        {
            case EVehicleType.Jeep:
            case EVehicleType.Technical:
                Jeep(Hull, Turret, Paint, Type == EVehicleType.Technical);
                break;
            case EVehicleType.Sedan:
                Sedan(Hull, Paint);
                break;
            case EVehicleType.Truck:
            case EVehicleType.FuelTruck:
                Truck(Hull, Paint, Type == EVehicleType.FuelTruck);
                break;
            case EVehicleType.Apc:
                Apc(Hull, Turret, Paint);
                break;
            case EVehicleType.Tank:
                Tank(Hull, Turret, Paint);
                break;
            case EVehicleType.AttackHeli:
            case EVehicleType.TransportHeli:
                Helicopter(Hull, Rotor, Paint, Type == EVehicleType.TransportHeli);
                break;
        }
    }

    private static void Jeep(MeshKit Kit, MeshKit Turret, FVector4 Paint, bool bTechnical)
    {
        FVector4 Dark = Palette.Shade(Paint, 0.7f);
        Kit.Prism(Profile((-2.05f, 0.55f), (2.0f, 0.55f), (2.12f, 0.98f), (1.95f, 1.14f), (0.55f, 1.2f), (-2.05f, 1.12f)), 0.8f, Paint);
        for (int Side = -1; Side <= 1; Side += 2)
        {
            foreach (float Z in new[] { 1.35f, -1.35f })
            {
                Kit.Box(new FVector3(Side * 0.86f, 1.12f, Z), new FVector3(0.17f, 0.04f, 0.56f), Dark);
            }
            Kit.Box(new FVector3(Side * 0.92f, 1.3f, 0.75f), new FVector3(0.06f, 0.05f, 0.03f), Palette.Gunmetal);
        }

        FrontEnd(Kit, Paint, 0.9f, 0.85f, 2.12f, 0.58f);
        Kit.Box(new FVector3(0.0f, 0.62f, -2.12f), new FVector3(0.85f, 0.08f, 0.07f), Palette.Gunmetal);
        for (int Side = -1; Side <= 1; Side += 2)
        {
            Kit.Box(new FVector3(Side * 0.68f, 0.98f, -2.07f), new FVector3(0.07f, 0.05f, 0.02f), TailLamp);
        }

        if (!bTechnical)
        {
            Windshield(Kit, Dark, 0.5f, 1.2f, 0.8f);
            Seats(Kit, 1.22f, -0.2f);
            Kit.Box(new FVector3(0.0f, 1.28f, -1.3f), new FVector3(0.7f, 0.07f, 0.28f), Seat);
            Kit.Box(new FVector3(0.0f, 1.55f, -1.55f), new FVector3(0.7f, 0.26f, 0.05f), Seat);
            FVector3[] Bar = { new(0.75f, 1.12f, -0.75f), new(0.75f, 1.85f, -0.65f), new(0.6f, 2.0f, -0.6f), new(-0.6f, 2.0f, -0.6f), new(-0.75f, 1.85f, -0.65f), new(-0.75f, 1.12f, -0.75f) };
            float[] BarRadii = { 0.045f, 0.045f, 0.045f, 0.045f, 0.045f, 0.045f };
            Kit.Sweep(Bar, BarRadii, 8, Along => Palette.Gunmetal, false, false);
            Wheel(Kit, new FVector3(0.0f, 1.2f, -2.2f), FVector3.Forward, 0.36f, 0.22f);
            return;
        }

        Greenhouse(Kit, Paint, (0.5f, 1.2f), (0.1f, 1.85f), (-0.72f, 1.87f), (-0.78f, 1.2f), 0.8f, 0.7f);
        Seats(Kit, 1.22f, -0.1f);
        Kit.Box(new FVector3(0.0f, 1.16f, -1.45f), new FVector3(0.78f, 0.04f, 0.62f), Dark);
        for (int Side = -1; Side <= 1; Side += 2)
        {
            Kit.Box(new FVector3(Side * 0.78f, 1.32f, -1.45f), new FVector3(0.03f, 0.18f, 0.62f), Paint);
        }
        Kit.Box(new FVector3(0.0f, 1.32f, -2.05f), new FVector3(0.8f, 0.18f, 0.03f), Paint);
        Kit.Tube(new FVector3(0.0f, 1.18f, -1.2f), new FVector3(0.0f, 1.95f, -1.2f), 0.07f, 0.06f, Palette.Gunmetal, 8);

        // A pintle machine gun, drawn about its mount so the turret entity can swing it.
        Turret.Box(new FVector3(0.0f, 0.0f, 0.05f), new FVector3(0.07f, 0.09f, 0.32f), Palette.Gunmetal);
        Turret.Tube(new FVector3(0.0f, 0.03f, 0.35f), new FVector3(0.0f, 0.03f, 1.05f), 0.025f, 0.025f, Palette.Gunmetal, 8);
        Turret.Tube(new FVector3(0.0f, 0.03f, 0.95f), new FVector3(0.0f, 0.03f, 1.12f), 0.04f, 0.035f, Palette.Rubber, 8);
        Turret.Box(new FVector3(0.12f, -0.05f, 0.05f), new FVector3(0.06f, 0.07f, 0.1f), Palette.Hex(0x3D4230));
        Turret.Box(new FVector3(0.0f, 0.12f, 0.2f), new FVector3(0.34f, 0.22f, 0.015f), Palette.Steel);
        Turret.Tube(new FVector3(-0.08f, -0.02f, -0.3f), new FVector3(0.08f, -0.02f, -0.3f), 0.02f, 0.02f, Palette.Rubber, 6);
    }

    private static void Sedan(MeshKit Kit, FVector4 Paint)
    {
        Kit.Prism(Profile((-2.2f, 0.32f), (2.15f, 0.32f), (2.25f, 0.66f), (2.05f, 0.82f), (0.95f, 0.92f), (-1.75f, 0.95f), (-2.22f, 0.8f)), 0.86f, Paint);
        Greenhouse(Kit, Paint, (0.95f, 0.92f), (0.3f, 1.4f), (-1.0f, 1.42f), (-1.6f, 0.95f), 0.82f, 0.66f);
        FrontEnd(Kit, Paint, 0.9f, 0.6f, 2.24f, 0.62f);
        Kit.Box(new FVector3(0.0f, 0.42f, -2.2f), new FVector3(0.88f, 0.08f, 0.06f), Palette.Gunmetal);
        for (int Side = -1; Side <= 1; Side += 2)
        {
            Kit.Box(new FVector3(Side * 0.66f, 0.8f, -2.21f), new FVector3(0.14f, 0.05f, 0.02f), TailLamp);
            Kit.Box(new FVector3(Side * 0.9f, 1.02f, 0.75f), new FVector3(0.06f, 0.05f, 0.03f), Paint);
        }
    }

    private static void Truck(MeshKit Kit, FVector4 Paint, bool bFuel)
    {
        FVector4 Dark = Palette.Shade(Paint, 0.7f);
        for (int Side = -1; Side <= 1; Side += 2)
        {
            Kit.Box(new FVector3(Side * 0.55f, 0.85f, -0.3f), new FVector3(0.1f, 0.12f, 3.3f), Palette.Gunmetal);
        }

        // A cab-over nose, the lower cab painted and the upper one glazed.
        Kit.Prism(Profile((1.7f, 0.95f), (3.5f, 0.95f), (3.6f, 1.55f), (3.55f, 1.8f), (1.7f, 1.8f)), 1.15f, Paint);
        Greenhouse(Kit, Paint, (3.55f, 1.8f), (3.4f, 2.65f), (1.75f, 2.7f), (1.7f, 1.8f), 1.15f, 1.08f);
        FrontEnd(Kit, Paint, 1.22f, 1.1f, 3.6f, 0.85f);
        Kit.Tube(new FVector3(1.05f, 1.8f, 1.6f), new FVector3(1.05f, 3.15f, 1.6f), 0.07f, 0.07f, Palette.Gunmetal, 8);
        Kit.Tube(new FVector3(1.02f, 1.02f, 1.3f), new FVector3(1.02f, 1.02f, 0.35f), 0.26f, 0.26f, Palette.Steel, 10);
        for (int Side = -1; Side <= 1; Side += 2)
        {
            Kit.Box(new FVector3(Side * 1.3f, 2.25f, 3.3f), new FVector3(0.03f, 0.2f, 0.08f), Palette.Gunmetal);
            Kit.Box(new FVector3(Side * 1.0f, 1.0f, -3.55f), new FVector3(0.12f, 0.05f, 0.02f), TailLamp);
        }

        if (!bFuel)
        {
            Kit.Box(new FVector3(0.0f, 1.2f, -1.0f), new FVector3(1.2f, 0.1f, 2.6f), Dark);
            for (int Side = -1; Side <= 1; Side += 2)
            {
                Kit.Box(new FVector3(Side * 1.17f, 1.55f, -1.0f), new FVector3(0.04f, 0.28f, 2.55f), Paint);
            }
            Kit.Box(new FVector3(0.0f, 2.35f, -1.0f), new FVector3(1.17f, 0.82f, 2.5f), Palette.Canvas);
            Kit.Gable(new FVector3(0.0f, 3.17f, -1.0f), new FVector3(5.0f, 0.3f, 2.34f), Palette.Shade(Palette.Canvas, 0.95f), 90.0f);
            for (float Z = -3.2f; Z <= 1.2f; Z += 1.1f)
            {
                Kit.Box(new FVector3(0.0f, 2.35f, Z), new FVector3(1.19f, 0.84f, 0.035f), Palette.Shade(Palette.Canvas, 0.8f));
            }
            return;
        }

        FVector3[] Tank = { new(0.0f, 2.2f, -3.52f), new(0.0f, 2.2f, -3.42f), new(0.0f, 2.2f, -3.2f), new(0.0f, 2.2f, 1.2f), new(0.0f, 2.2f, 1.42f), new(0.0f, 2.2f, 1.52f) };
        float[] TankRadii = { 0.7f, 0.97f, 1.05f, 1.05f, 0.97f, 0.7f };
        Kit.Sweep(Tank, TankRadii, 18, Along => Palette.Steel, true, true);
        FVector3[] Band = { new(0.0f, 2.2f, -1.25f), new(0.0f, 2.2f, -0.75f) };
        float[] BandRadii = { 1.065f, 1.065f };
        Kit.Sweep(Band, BandRadii, 18, Along => Palette.FuelRed, false, false);
        Kit.Box(new FVector3(0.0f, 3.28f, -1.0f), new FVector3(0.3f, 0.03f, 2.3f), Palette.Gunmetal);
        foreach (float Z in new[] { -2.6f, -1.0f, 0.6f })
        {
            Kit.Cylinder(new FVector3(0.0f, 3.2f, Z), 0.28f, 0.14f, Palette.Shade(Palette.Steel, 0.8f), 10);
        }
        for (float Y = 1.3f; Y < 3.3f; Y += 0.28f)
        {
            Kit.Box(new FVector3(0.0f, Y, -3.62f), new FVector3(0.22f, 0.018f, 0.018f), Palette.Gunmetal);
        }
        for (int Side = -1; Side <= 1; Side += 2)
        {
            Kit.Box(new FVector3(Side * 0.22f, 2.3f, -3.62f), new FVector3(0.02f, 1.0f, 0.02f), Palette.Gunmetal);
        }
    }

    private static void Apc(MeshKit Kit, MeshKit Turret, FVector4 Paint)
    {
        FVector4 Dark = Palette.Shade(Paint, 0.72f);
        Kit.Prism(Profile((-3.3f, 0.55f), (2.9f, 0.55f), (3.35f, 1.15f), (2.55f, 1.95f), (-3.2f, 1.95f), (-3.35f, 1.45f)), 1.18f, Paint, default, 1.0f);
        for (int Side = -1; Side <= 1; Side += 2)
        {
            for (float Z = -2.2f; Z <= 1.6f; Z += 1.25f)
            {
                Kit.Box(new FVector3(Side * 1.07f, 1.72f, Z), new FVector3(0.03f, 0.06f, 0.14f), Palette.Glass);
            }
            Kit.Tube(new FVector3(Side * 0.8f, 1.35f, 3.1f), new FVector3(Side * 0.8f, 1.35f, 3.18f), 0.1f, 0.1f, Lamp, 10);
            Kit.Box(new FVector3(Side * 0.8f, 1.5f, 3.12f), new FVector3(0.14f, 0.02f, 0.1f), Palette.Gunmetal);
            Kit.Cylinder(new FVector3(Side * 0.55f, 1.95f, -1.6f), 0.32f, 0.07f, Dark, 10);
        }
        Kit.Box(new FVector3(0.0f, 1.25f, -3.32f), new FVector3(0.55f, 0.45f, 0.04f), Dark);
        Kit.Box(new FVector3(0.0f, 0.75f, 3.1f), new FVector3(0.5f, 0.05f, 0.1f), Palette.Gunmetal);

        Turret.Prism(Profile((-0.8f, -0.6f), (0.85f, -0.6f), (1.0f, -0.2f), (0.6f, 0.25f), (-0.75f, 0.25f)), 0.8f, Paint, default, 0.6f);
        Turret.Tube(new FVector3(0.0f, -0.1f, 0.9f), new FVector3(0.0f, -0.1f, 2.3f), 0.07f, 0.065f, Palette.Gunmetal, 10);
        Turret.Tube(new FVector3(0.0f, -0.1f, 2.1f), new FVector3(0.0f, -0.1f, 2.35f), 0.1f, 0.1f, Palette.Gunmetal, 10);
        Turret.Box(new FVector3(0.45f, 0.35f, 0.1f), new FVector3(0.12f, 0.1f, 0.18f), Dark);
        SmokeLaunchers(Turret, 0.78f, -0.25f, 0.35f);
    }

    private static void Tank(MeshKit Kit, MeshKit Turret, FVector4 Paint)
    {
        FVector4 Dark = Palette.Shade(Paint, 0.7f);
        for (int Side = -1; Side <= 1; Side += 2)
        {
            Kit.Prism(Profile((-3.6f, 0.45f), (-3.2f, 0.05f), (3.2f, 0.05f), (3.65f, 0.45f), (3.4f, 0.95f), (-3.45f, 0.95f)), 0.36f, Palette.Rubber, new FVector3(Side * 1.35f, 0.0f, 0.0f));
            for (int Roller = 0; Roller < 6; ++Roller)
            {
                float Z = 2.6f - Roller * 1.04f;
                Kit.Tube(new FVector3(Side * 1.66f, 0.45f, Z), new FVector3(Side * 1.74f, 0.45f, Z), 0.36f, 0.36f, Palette.Gunmetal, 12);
                Kit.Tube(new FVector3(Side * 1.72f, 0.45f, Z), new FVector3(Side * 1.77f, 0.45f, Z), 0.1f, 0.1f, Dark, 8);
            }
            Kit.Tube(new FVector3(Side * 1.66f, 0.62f, 3.2f), new FVector3(Side * 1.75f, 0.62f, 3.2f), 0.3f, 0.3f, Dark, 10);
            Kit.Tube(new FVector3(Side * 1.66f, 0.55f, -3.2f), new FVector3(Side * 1.75f, 0.55f, -3.2f), 0.28f, 0.28f, Dark, 10);
            Kit.Box(new FVector3(Side * 1.73f, 0.92f, 0.1f), new FVector3(0.04f, 0.2f, 3.15f), Dark);
            Kit.Box(new FVector3(Side * 1.35f, 1.0f, 0.15f), new FVector3(0.4f, 0.03f, 3.5f), Dark);
            Kit.Box(new FVector3(Side * 1.45f, 1.18f, -2.4f), new FVector3(0.28f, 0.15f, 0.5f), Dark);
            Kit.Tube(new FVector3(Side * 1.0f, 1.32f, 3.3f), new FVector3(Side * 1.0f, 1.32f, 3.38f), 0.09f, 0.09f, Lamp, 10);
        }

        Kit.Prism(Profile((-3.5f, 0.75f), (3.05f, 0.75f), (3.75f, 1.15f), (2.3f, 1.72f), (-3.35f, 1.72f), (-3.6f, 1.35f)), 1.32f, Paint);
        for (float Z = -3.2f; Z <= -2.0f; Z += 0.3f)
        {
            Kit.Box(new FVector3(0.0f, 1.73f, Z), new FVector3(0.9f, 0.012f, 0.06f), Palette.Shade(Paint, 0.5f));
        }

        // A wedge turret on a ring, drawn about the turret entity's origin, with the gun along +Z.
        Turret.Prism(Profile((-1.9f, -0.5f), (1.45f, -0.5f), (1.95f, -0.05f), (1.4f, 0.42f), (-1.75f, 0.42f), (-2.05f, 0.05f)), 1.25f, Paint, default, 1.02f);
        Turret.Box(new FVector3(0.0f, 0.02f, 1.95f), new FVector3(0.42f, 0.26f, 0.18f), Dark);
        Turret.Tube(new FVector3(0.0f, 0.05f, 1.9f), new FVector3(0.0f, 0.05f, 5.25f), 0.13f, 0.11f, Dark, 12);
        Turret.Tube(new FVector3(0.0f, 0.05f, 3.2f), new FVector3(0.0f, 0.05f, 3.8f), 0.2f, 0.2f, Dark, 12);
        Turret.Tube(new FVector3(0.0f, 0.05f, 5.1f), new FVector3(0.0f, 0.05f, 5.4f), 0.17f, 0.16f, Dark, 12);
        Turret.Cylinder(new FVector3(0.55f, 0.42f, -0.5f), 0.34f, 0.2f, Dark, 12);
        Turret.Cylinder(new FVector3(0.55f, 0.62f, -0.5f), 0.28f, 0.05f, Palette.Shade(Paint, 0.9f), 12);
        Turret.Tube(new FVector3(0.55f, 0.72f, -0.2f), new FVector3(0.55f, 0.72f, 0.5f), 0.025f, 0.025f, Palette.Gunmetal, 6);
        Turret.Cylinder(new FVector3(-0.55f, 0.42f, -0.2f), 0.26f, 0.08f, Dark, 10);
        Turret.Box(new FVector3(0.0f, 0.1f, -2.2f), new FVector3(1.0f, 0.25f, 0.25f), Dark);
        SmokeLaunchers(Turret, 1.1f, 0.1f, 1.2f);
    }

    private static void Helicopter(MeshKit Kit, MeshKit Rotor, FVector4 Paint, bool bTransport)
    {
        FVector4 Dark = Palette.Shade(Paint, 0.7f);
        float Length = bTransport ? 3.5f : 2.6f;
        float Girth = bTransport ? 1.4f : 0.95f;
        float Axis = 1.6f;

        FVector3[] Body = { new(0.0f, Axis - 0.25f, Length + 1.3f), new(0.0f, Axis - 0.12f, Length + 0.9f), new(0.0f, Axis, Length), new(0.0f, Axis, 0.4f), new(0.0f, Axis + 0.1f, -Length + 0.6f), new(0.0f, Axis + 0.25f, -Length) };
        float[] BodyRadii = { Girth * 0.25f, Girth * 0.7f, Girth * 0.95f, Girth, Girth * 0.85f, Girth * 0.5f };
        Kit.Sweep(Body, BodyRadii, 16, Along => Paint, true, true);
        Kit.Blob(new FVector3(0.0f, Axis + Girth * 0.35f, Length + 0.25f), new FVector3(Girth * 0.72f, Girth * 0.58f, 1.05f), 2, null, (Direction, Point) => Palette.Glass);

        FVector3[] Boom = { new(0.0f, Axis + 0.3f, -Length + 0.4f), new(0.0f, Axis + 0.5f, -Length - 2.0f), new(0.0f, Axis + 0.7f, -Length - 4.6f) };
        float[] BoomRadii = { 0.45f, 0.3f, 0.17f };
        Kit.Sweep(Boom, BoomRadii, 10, Along => Paint, false, true);
        Kit.Prism(Profile((-Length - 4.8f, 2.2f), (-Length - 3.9f, 2.25f), (-Length - 4.3f, 3.4f), (-Length - 4.85f, 3.5f)), 0.04f, Dark);
        Kit.Box(new FVector3(0.0f, 2.3f, -Length - 3.6f), new FVector3(0.9f, 0.03f, 0.25f), Dark);
        Kit.Box(new FVector3(0.12f, 2.95f, -Length - 4.45f), new FVector3(0.02f, 0.7f, 0.08f), RotorBlade);
        Kit.Box(new FVector3(0.12f, 2.95f, -Length - 4.45f), new FVector3(0.02f, 0.08f, 0.7f), RotorBlade);

        Kit.Blob(new FVector3(0.0f, Axis + Girth + 0.05f, 0.1f), new FVector3(0.55f, 0.32f, 1.4f), 2, null, (Direction, Point) => Palette.Shade(Paint, 0.8f + 0.2f * (Direction.Y * 0.5f + 0.5f)));
        for (int Side = -1; Side <= 1; Side += 2)
        {
            Kit.Box(new FVector3(Side * 0.5f, Axis + Girth + 0.05f, 0.9f), new FVector3(0.04f, 0.14f, 0.2f), Palette.Rubber);
        }
        Kit.Cylinder(new FVector3(0.0f, Axis + Girth + 0.3f, 0.2f), 0.14f, 0.6f, Palette.Gunmetal, 10);

        for (int Side = -1; Side <= 1; Side += 2)
        {
            float X = Side * (Girth + 0.25f);
            FVector3[] Skid = { new(X, 0.32f, Length + 0.95f), new(X, 0.2f, Length + 0.6f), new(X, 0.2f, -1.3f) };
            float[] SkidRadii = { 0.05f, 0.055f, 0.055f };
            Kit.Sweep(Skid, SkidRadii, 8, Along => Palette.Gunmetal, true, true);
            foreach (float Z in new[] { Length - 0.2f, -0.8f })
            {
                Kit.Tube(new FVector3(X, 0.2f, Z), new FVector3(Side * Girth * 0.7f, Axis - Girth * 0.6f, Z), 0.045f, 0.045f, Palette.Gunmetal, 6);
            }
        }

        if (bTransport)
        {
            for (int Side = -1; Side <= 1; Side += 2)
            {
                for (float Z = -1.8f; Z <= 1.6f; Z += 0.85f)
                {
                    Kit.Box(new FVector3(Side * Girth * 0.99f, Axis + 0.25f, Z), new FVector3(0.03f, 0.18f, 0.22f), Palette.Glass);
                }
                Kit.Box(new FVector3(Side * Girth * 1.0f, Axis - 0.1f, 1.0f), new FVector3(0.02f, 0.75f, 0.03f), Dark);
            }
        }
        else
        {
            Kit.Box(new FVector3(0.0f, 1.3f, 0.2f), new FVector3(2.1f, 0.06f, 0.5f), Dark);
            for (int Side = -1; Side <= 1; Side += 2)
            {
                Kit.Tube(new FVector3(Side * 1.8f, 1.08f, -0.4f), new FVector3(Side * 1.8f, 1.08f, 1.0f), 0.2f, 0.2f, Palette.Gunmetal, 12);
                Kit.Tube(new FVector3(Side * 1.8f, 1.08f, 0.95f), new FVector3(Side * 1.8f, 1.08f, 1.05f), 0.17f, 0.17f, Palette.Rubber, 12);
                Kit.Box(new FVector3(Side * 1.8f, 1.22f, 0.3f), new FVector3(0.04f, 0.08f, 0.3f), Dark);
            }
            Kit.Blob(new FVector3(0.0f, Axis - Girth * 0.85f, Length + 0.5f), new FVector3(0.22f), 1, null, (Direction, Point) => Palette.Gunmetal);
            Kit.Tube(new FVector3(0.0f, Axis - Girth * 0.85f, Length + 0.5f), new FVector3(0.0f, Axis - Girth * 0.85f, Length + 1.5f), 0.05f, 0.05f, Palette.Gunmetal, 8);
        }

        float Span = bTransport ? 8.5f : 7.0f;
        for (int Blade = 0; Blade < 4; ++Blade)
        {
            float Yaw = Blade * MathF.PI * 0.5f;
            FVector3 Out = new(MathF.Cos(Yaw), 0.0f, MathF.Sin(Yaw));
            Rotor.Box(Out * (Span * 0.5f), new FVector3(MathF.Abs(Out.X) * Span * 0.5f + MathF.Abs(Out.Z) * 0.24f, 0.03f, MathF.Abs(Out.Z) * Span * 0.5f + MathF.Abs(Out.X) * 0.24f), RotorBlade);
        }
        Rotor.Cylinder(new FVector3(0.0f, -0.08f, 0.0f), 0.3f, 0.16f, Palette.Gunmetal, 10);
    }

    // Grille slats between the headlamps and a bumper below, at the vehicle's nose.
    private static void FrontEnd(MeshKit Kit, FVector4 Paint, float HalfWidth, float LampY, float NoseZ, float LampX)
    {
        Kit.Box(new FVector3(0.0f, LampY - 0.02f, NoseZ - 0.02f), new FVector3(LampX - 0.18f, 0.16f, 0.03f), Palette.Gunmetal);
        for (float X = -(LampX - 0.26f); X <= LampX - 0.25f; X += 0.12f)
        {
            Kit.Box(new FVector3(X, LampY - 0.02f, NoseZ + 0.005f), new FVector3(0.02f, 0.14f, 0.012f), Palette.Shade(Paint, 0.85f));
        }
        for (int Side = -1; Side <= 1; Side += 2)
        {
            Kit.Tube(new FVector3(Side * LampX, LampY, NoseZ - 0.04f), new FVector3(Side * LampX, LampY, NoseZ + 0.02f), 0.085f, 0.085f, Lamp, 12);
        }
        Kit.Box(new FVector3(0.0f, LampY - 0.3f, NoseZ + 0.06f), new FVector3(HalfWidth, 0.08f, 0.07f), Palette.Gunmetal);
    }

    // Glass between four side-profile corners, a painted roof on top and pillars up its front and rear edges.
    private static void Greenhouse(MeshKit Kit, FVector4 Paint, (float Z, float Y) FrontLow, (float Z, float Y) FrontHigh, (float Z, float Y) RearHigh, (float Z, float Y) RearLow, float HalfWidth, float TopHalfWidth)
    {
        Kit.Prism(Profile(RearLow, FrontLow, FrontHigh, RearHigh), HalfWidth - 0.01f, Palette.Glass, default, TopHalfWidth - 0.01f);
        Kit.Prism(Profile((RearHigh.Z, RearHigh.Y - 0.01f), (FrontHigh.Z, FrontHigh.Y - 0.01f), (FrontHigh.Z - 0.05f, FrontHigh.Y + 0.05f), (RearHigh.Z + 0.03f, RearHigh.Y + 0.05f)), TopHalfWidth + 0.02f, Paint);
        for (int Side = -1; Side <= 1; Side += 2)
        {
            Strut(Kit, new FVector3(Side * HalfWidth, FrontLow.Y, FrontLow.Z), new FVector3(Side * TopHalfWidth, FrontHigh.Y, FrontHigh.Z), 0.04f, Paint);
            Strut(Kit, new FVector3(Side * HalfWidth, RearLow.Y, RearLow.Z), new FVector3(Side * TopHalfWidth, RearHigh.Y, RearHigh.Z), 0.05f, Paint);
            float MidZ = (FrontLow.Z + RearLow.Z) * 0.5f;
            Strut(Kit, new FVector3(Side * HalfWidth, FrontLow.Y, MidZ), new FVector3(Side * TopHalfWidth, (FrontHigh.Y + RearHigh.Y) * 0.5f, (FrontHigh.Z + RearHigh.Z) * 0.5f), 0.035f, Paint);
        }
    }

    private static void Windshield(MeshKit Kit, FVector4 Frame, float Z, float Y, float HalfWidth)
    {
        FVector3 Top = new(0.0f, Y + 0.55f, Z - 0.2f);
        FVector3 Bottom = new(0.0f, Y, Z);
        FQuat Tilt = FQuat.FromToRotation(FVector3.Up, (Top - Bottom).Normalized());
        Kit.Box((Top + Bottom) * 0.5f, new FVector3(HalfWidth - 0.03f, (Top - Bottom).Length * 0.5f, 0.012f), Palette.Glass, Tilt);
        for (int Side = -1; Side <= 1; Side += 2)
        {
            Strut(Kit, Bottom + new FVector3(Side * HalfWidth, 0.0f, 0.0f), Top + new FVector3(Side * HalfWidth, 0.0f, 0.0f), 0.03f, Frame);
        }
        Strut(Kit, Top - new FVector3(HalfWidth, 0.0f, 0.0f), Top + new FVector3(HalfWidth, 0.0f, 0.0f), 0.03f, Frame);
    }

    private static void Seats(MeshKit Kit, float FloorY, float Z)
    {
        for (int Side = -1; Side <= 1; Side += 2)
        {
            Kit.Box(new FVector3(Side * 0.4f, FloorY + 0.06f, Z), new FVector3(0.24f, 0.07f, 0.24f), Seat);
            Kit.Box(new FVector3(Side * 0.4f, FloorY + 0.32f, Z - 0.24f), new FVector3(0.23f, 0.27f, 0.05f), Seat, FQuat.FromEuler(Mathf.Radians(-10.0f), 0.0f, 0.0f));
        }
    }

    private static void SmokeLaunchers(MeshKit Kit, float X, float Y, float Z)
    {
        for (int Side = -1; Side <= 1; Side += 2)
        {
            for (int Index = 0; Index < 3; ++Index)
            {
                FVector3 Base = new(Side * X, Y + Index * 0.1f, Z);
                Kit.Tube(Base, Base + new FVector3(Side * 0.12f, 0.12f, 0.15f), 0.04f, 0.04f, Palette.Gunmetal, 6);
            }
        }
    }

    private static void Strut(MeshKit Kit, FVector3 From, FVector3 To, float Thickness, FVector4 Color)
    {
        FVector3 Span = To - From;
        FQuat Rotation = FQuat.FromToRotation(FVector3.Up, Span.NormalizedOr(FVector3.Up));
        Kit.Box((From + To) * 0.5f, new FVector3(Thickness, Span.Length * 0.5f, Thickness), Color, Rotation);
    }

    private static FVector2[] Profile(params (float Z, float Y)[] Points)
    {
        FVector2[] Result = new FVector2[Points.Length];
        for (int Index = 0; Index < Points.Length; ++Index)
        {
            Result[Index] = new FVector2(Points[Index].Z, Points[Index].Y);
        }
        return Result;
    }
}
