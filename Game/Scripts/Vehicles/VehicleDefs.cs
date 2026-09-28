using System;
using System.Collections.Generic;
using Lumina;

namespace Mercenaries;

public enum EVehicleType : byte
{
    Jeep,
    Technical,
    Truck,
    FuelTruck,
    Apc,
    Tank,
    AttackHeli,
    TransportHeli,
    Sedan,
}

public sealed class VehicleDef
{
    public EVehicleType Type;
    public string Name = string.Empty;
    public bool bHeavy;
    public bool bAir;
    public float MaxHealth;
    public float MaxSpeed;
    public float Acceleration;
    public float TurnRate;
    public FVector3 HalfExtents;
    public float Clearance = 0.35f;
    public EWeapon Primary = EWeapon.None;
    public EWeapon Secondary = EWeapon.None;
    public bool bTurret;
    public FVector3 TurretOffset;
    public float MuzzleLength = 2.0f;
    public int Crew = 1;
    public float CameraDistance = 9.0f;
    public float CameraHeight = 3.0f;
    public float CrushSpeed = 4.0f;
}

public static class VehicleDefs
{
    private static readonly Dictionary<EVehicleType, VehicleDef> Defs = new();

    static VehicleDefs()
    {
        Add(new VehicleDef { Type = EVehicleType.Jeep, Name = "Jeep", MaxHealth = 450, MaxSpeed = 30, Acceleration = 14, TurnRate = 95, HalfExtents = new FVector3(0.95f, 0.7f, 2.1f), Crew = 1, CameraDistance = 8.5f });
        Add(new VehicleDef { Type = EVehicleType.Technical, Name = "Technical", MaxHealth = 500, MaxSpeed = 27, Acceleration = 12, TurnRate = 85, HalfExtents = new FVector3(1.0f, 0.75f, 2.4f), Primary = EWeapon.VehicleMG, bTurret = true, TurretOffset = new FVector3(0.0f, 2.1f, -1.2f), MuzzleLength = 1.1f, Crew = 2, CameraDistance = 9.0f });
        Add(new VehicleDef { Type = EVehicleType.Truck, Name = "Cargo Truck", MaxHealth = 800, MaxSpeed = 22, Acceleration = 8, TurnRate = 60, HalfExtents = new FVector3(1.25f, 1.3f, 3.6f), Clearance = 0.5f, Crew = 3, CameraDistance = 11.0f, CameraHeight = 4.0f });
        Add(new VehicleDef { Type = EVehicleType.FuelTruck, Name = "Fuel Truck", MaxHealth = 600, MaxSpeed = 20, Acceleration = 7, TurnRate = 55, HalfExtents = new FVector3(1.25f, 1.3f, 3.8f), Clearance = 0.5f, Crew = 1, CameraDistance = 11.0f, CameraHeight = 4.0f });
        Add(new VehicleDef { Type = EVehicleType.Apc, Name = "APC", bHeavy = true, MaxHealth = 1600, MaxSpeed = 20, Acceleration = 8, TurnRate = 55, HalfExtents = new FVector3(1.4f, 1.0f, 3.3f), Clearance = 0.45f, Primary = EWeapon.Autocannon, Secondary = EWeapon.VehicleMG, bTurret = true, TurretOffset = new FVector3(0.0f, 2.55f, 0.3f), MuzzleLength = 2.2f, Crew = 3, CameraDistance = 11.0f, CameraHeight = 4.0f, CrushSpeed = 3.0f });
        Add(new VehicleDef { Type = EVehicleType.Tank, Name = "Main Battle Tank", bHeavy = true, MaxHealth = 3200, MaxSpeed = 15, Acceleration = 6, TurnRate = 42, HalfExtents = new FVector3(1.7f, 0.95f, 3.5f), Clearance = 0.3f, Primary = EWeapon.TankCannon, Secondary = EWeapon.VehicleMG, bTurret = true, TurretOffset = new FVector3(0.0f, 2.25f, -0.2f), MuzzleLength = 5.2f, Crew = 2, CameraDistance = 12.5f, CameraHeight = 4.5f, CrushSpeed = 2.0f });
        Add(new VehicleDef { Type = EVehicleType.AttackHeli, Name = "Attack Helicopter", bHeavy = true, bAir = true, MaxHealth = 1300, MaxSpeed = 42, Acceleration = 14, TurnRate = 80, HalfExtents = new FVector3(1.3f, 1.3f, 5.2f), Clearance = 0.0f, Primary = EWeapon.HeliRockets, Secondary = EWeapon.Autocannon, Crew = 2, CameraDistance = 16.0f, CameraHeight = 5.0f });
        Add(new VehicleDef { Type = EVehicleType.TransportHeli, Name = "Transport Helicopter", bHeavy = true, bAir = true, MaxHealth = 1500, MaxSpeed = 36, Acceleration = 10, TurnRate = 60, HalfExtents = new FVector3(1.6f, 1.6f, 6.0f), Clearance = 0.0f, Secondary = EWeapon.VehicleMG, Crew = 2, CameraDistance = 18.0f, CameraHeight = 6.0f });
        Add(new VehicleDef { Type = EVehicleType.Sedan, Name = "Sedan", MaxHealth = 300, MaxSpeed = 32, Acceleration = 13, TurnRate = 100, HalfExtents = new FVector3(0.9f, 0.65f, 2.2f), Crew = 1, CameraDistance = 8.0f });
    }

    private static void Add(VehicleDef Def) => Defs[Def.Type] = Def;

    public static VehicleDef Get(EVehicleType Type) => Defs[Type];

    public static EntityHull BuildHull(EVehicleType Type, FVector4 Paint)
    {
        MeshKit Hull = new();
        MeshKit Turret = new();
        MeshKit Rotor = new();
        FVector4 Dark = Palette.Shade(Paint, 0.7f);
        FVector4 Tire = Palette.Rubber;

        void Wheel(float X, float Z, float Radius, float Width)
        {
            Hull.Tube(new FVector3(X - Width * 0.5f, Radius, Z), new FVector3(X + Width * 0.5f, Radius, Z), Radius, Radius, Tire, 10);
            Hull.Box(new FVector3(X + MathF.Sign(X) * (Width * 0.5f + 0.01f), Radius, Z), new FVector3(0.01f, Radius * 0.45f, Radius * 0.45f), Palette.Steel);
        }

        switch (Type)
        {
            case EVehicleType.Jeep:
            case EVehicleType.Technical:
            {
                Wheel(0.85f, 1.35f, 0.42f, 0.3f);
                Wheel(-0.85f, 1.35f, 0.42f, 0.3f);
                Wheel(0.85f, -1.35f, 0.42f, 0.3f);
                Wheel(-0.85f, -1.35f, 0.42f, 0.3f);
                Hull.Box(new FVector3(0.0f, 0.85f, 0.0f), new FVector3(0.9f, 0.3f, 2.1f), Paint);
                Hull.Box(new FVector3(0.0f, 1.2f, 1.35f), new FVector3(0.85f, 0.12f, 0.75f), Dark);
                Hull.Box(new FVector3(0.0f, 1.45f, 0.5f), new FVector3(0.85f, 0.3f, 0.04f), Palette.Glass, FQuat.FromEuler(Mathf.Radians(-20.0f), 0.0f, 0.0f));
                Hull.Box(new FVector3(0.45f, 1.35f, -0.1f), new FVector3(0.3f, 0.25f, 0.3f), Palette.Shade(Paint, 0.5f));
                Hull.Box(new FVector3(-0.45f, 1.35f, -0.1f), new FVector3(0.3f, 0.25f, 0.3f), Palette.Shade(Paint, 0.5f));
                Hull.Box(new FVector3(0.0f, 1.0f, 2.13f), new FVector3(0.8f, 0.15f, 0.05f), Palette.Steel);
                if (Type == EVehicleType.Jeep)
                {
                    Hull.Tube(new FVector3(0.8f, 1.1f, -0.4f), new FVector3(0.8f, 2.0f, -0.4f), 0.05f, 0.05f, Palette.Gunmetal, 6);
                    Hull.Tube(new FVector3(-0.8f, 1.1f, -0.4f), new FVector3(-0.8f, 2.0f, -0.4f), 0.05f, 0.05f, Palette.Gunmetal, 6);
                    Hull.Tube(new FVector3(-0.8f, 2.0f, -0.4f), new FVector3(0.8f, 2.0f, -0.4f), 0.05f, 0.05f, Palette.Gunmetal, 6);
                }
                else
                {
                    Hull.Box(new FVector3(0.0f, 1.3f, -1.4f), new FVector3(0.9f, 0.18f, 0.9f), Dark);
                    Hull.Tube(new FVector3(0.0f, 1.3f, -1.2f), new FVector3(0.0f, 2.0f, -1.2f), 0.08f, 0.08f, Palette.Gunmetal, 6);
                    Turret.Box(new FVector3(0.0f, 0.0f, 0.1f), new FVector3(0.12f, 0.12f, 0.35f), Palette.Gunmetal);
                    Turret.Tube(new FVector3(0.0f, 0.02f, 0.4f), new FVector3(0.0f, 0.02f, 1.1f), 0.04f, 0.04f, Palette.Gunmetal, 6);
                    Turret.Box(new FVector3(0.0f, 0.2f, 0.05f), new FVector3(0.35f, 0.2f, 0.02f), Palette.Steel);
                }

                break;
            }
            case EVehicleType.Sedan:
            {
                Wheel(0.8f, 1.35f, 0.34f, 0.24f);
                Wheel(-0.8f, 1.35f, 0.34f, 0.24f);
                Wheel(0.8f, -1.35f, 0.34f, 0.24f);
                Wheel(-0.8f, -1.35f, 0.34f, 0.24f);
                Hull.Box(new FVector3(0.0f, 0.7f, 0.0f), new FVector3(0.88f, 0.28f, 2.2f), Paint);
                Hull.Box(new FVector3(0.0f, 1.2f, -0.2f), new FVector3(0.8f, 0.25f, 1.1f), Palette.Glass);
                Hull.Box(new FVector3(0.0f, 1.46f, -0.2f), new FVector3(0.78f, 0.03f, 0.95f), Paint);
                break;
            }
            case EVehicleType.Truck:
            case EVehicleType.FuelTruck:
            {
                Wheel(1.05f, 2.4f, 0.55f, 0.4f);
                Wheel(-1.05f, 2.4f, 0.55f, 0.4f);
                Wheel(1.05f, -1.2f, 0.55f, 0.4f);
                Wheel(-1.05f, -1.2f, 0.55f, 0.4f);
                Wheel(1.05f, -2.6f, 0.55f, 0.4f);
                Wheel(-1.05f, -2.6f, 0.55f, 0.4f);
                Hull.Box(new FVector3(0.0f, 0.95f, 0.0f), new FVector3(1.15f, 0.2f, 3.6f), Palette.Gunmetal);
                Hull.Box(new FVector3(0.0f, 1.9f, 2.55f), new FVector3(1.2f, 0.85f, 1.0f), Paint);
                Hull.Box(new FVector3(0.0f, 2.15f, 3.56f), new FVector3(1.05f, 0.35f, 0.03f), Palette.Glass);
                if (Type == EVehicleType.Truck)
                {
                    Hull.Box(new FVector3(0.0f, 1.35f, -1.0f), new FVector3(1.2f, 0.2f, 2.5f), Dark);
                    Hull.Box(new FVector3(0.0f, 2.35f, -1.0f), new FVector3(1.18f, 0.8f, 2.45f), Palette.Canvas);
                }
                else
                {
                    Hull.Tube(new FVector3(0.0f, 2.2f, -3.4f), new FVector3(0.0f, 2.2f, 1.4f), 1.05f, 1.05f, Palette.Steel, 12);
                    Hull.Box(new FVector3(0.0f, 2.2f, -1.0f), new FVector3(1.08f, 0.18f, 0.6f), Palette.FuelRed);
                }

                break;
            }
            case EVehicleType.Apc:
            {
                for (int Axle = 0; Axle < 4; ++Axle)
                {
                    float Z = 2.4f - Axle * 1.6f;
                    Wheel(1.3f, Z, 0.55f, 0.36f);
                    Wheel(-1.3f, Z, 0.55f, 0.36f);
                }

                Hull.Box(new FVector3(0.0f, 1.35f, 0.0f), new FVector3(1.35f, 0.6f, 3.3f), Paint);
                Hull.Box(new FVector3(0.0f, 1.65f, 3.05f), new FVector3(1.2f, 0.35f, 0.4f), Dark, FQuat.FromEuler(Mathf.Radians(35.0f), 0.0f, 0.0f));
                Hull.Box(new FVector3(0.0f, 2.0f, -0.3f), new FVector3(1.2f, 0.12f, 2.6f), Dark);
                Turret.Box(new FVector3(0.0f, 0.0f, 0.0f), new FVector3(0.7f, 0.35f, 0.8f), Paint);
                Turret.Tube(new FVector3(0.0f, 0.05f, 0.6f), new FVector3(0.0f, 0.05f, 2.2f), 0.09f, 0.08f, Palette.Gunmetal, 6);
                break;
            }
            case EVehicleType.Tank:
            {
                Hull.Box(new FVector3(1.35f, 0.6f, 0.0f), new FVector3(0.38f, 0.55f, 3.4f), Palette.Rubber);
                Hull.Box(new FVector3(-1.35f, 0.6f, 0.0f), new FVector3(0.38f, 0.55f, 3.4f), Palette.Rubber);
                for (int Roller = 0; Roller < 6; ++Roller)
                {
                    float Z = 2.6f - Roller * 1.04f;
                    Hull.Tube(new FVector3(1.74f, 0.45f, Z), new FVector3(1.76f, 0.45f, Z), 0.36f, 0.36f, Palette.Gunmetal, 8);
                    Hull.Tube(new FVector3(-1.76f, 0.45f, Z), new FVector3(-1.74f, 0.45f, Z), 0.36f, 0.36f, Palette.Gunmetal, 8);
                }

                Hull.Box(new FVector3(0.0f, 1.25f, 0.0f), new FVector3(1.7f, 0.5f, 3.5f), Paint);
                Hull.Box(new FVector3(0.0f, 1.2f, 3.55f), new FVector3(1.6f, 0.35f, 0.3f), Dark, FQuat.FromEuler(Mathf.Radians(-30.0f), 0.0f, 0.0f));
                Turret.Box(new FVector3(0.0f, 0.0f, 0.0f), new FVector3(1.25f, 0.42f, 1.6f), Paint);
                Turret.Box(new FVector3(0.0f, 0.1f, -1.7f), new FVector3(1.0f, 0.3f, 0.35f), Dark);
                Turret.Tube(new FVector3(0.0f, 0.05f, 1.4f), new FVector3(0.0f, 0.05f, 5.2f), 0.14f, 0.12f, Dark, 8);
                Turret.Tube(new FVector3(0.0f, 0.05f, 4.6f), new FVector3(0.0f, 0.05f, 5.3f), 0.2f, 0.2f, Dark, 8);
                Turret.Cylinder(new FVector3(0.5f, 0.42f, -0.4f), 0.35f, 0.25f, Dark, 8);
                break;
            }
            case EVehicleType.AttackHeli:
            case EVehicleType.TransportHeli:
            {
                bool bTransport = Type == EVehicleType.TransportHeli;
                float Length = bTransport ? 3.5f : 2.6f;
                float Girth = bTransport ? 1.4f : 0.95f;
                Hull.Box(new FVector3(0.0f, 1.6f, 0.4f), new FVector3(Girth, Girth, Length), Paint);
                Hull.Box(new FVector3(0.0f, 1.8f, Length + 0.6f), new FVector3(Girth * 0.8f, Girth * 0.7f, 0.6f), Palette.Glass);
                Hull.Tube(new FVector3(0.0f, 2.0f, -Length + 0.4f), new FVector3(0.0f, 2.3f, -Length - 4.5f), 0.45f, 0.2f, Paint, 8);
                Hull.Box(new FVector3(0.25f, 2.9f, -Length - 4.3f), new FVector3(0.05f, 0.8f, 0.4f), Dark);
                Hull.Box(new FVector3(0.0f, 0.2f, 0.8f), new FVector3(Girth + 0.2f, 0.05f, 1.8f), Palette.Gunmetal);
                Hull.Box(new FVector3(Girth * 0.9f, 0.45f, 0.8f), new FVector3(0.05f, 0.25f, 0.05f), Palette.Gunmetal);
                Hull.Box(new FVector3(-Girth * 0.9f, 0.45f, 0.8f), new FVector3(0.05f, 0.25f, 0.05f), Palette.Gunmetal);
                Hull.Cylinder(new FVector3(0.0f, 1.6f + Girth, 0.2f), 0.25f, 0.5f, Palette.Gunmetal, 8);
                if (!bTransport)
                {
                    Hull.Box(new FVector3(0.0f, 1.3f, 0.2f), new FVector3(2.1f, 0.08f, 0.5f), Dark);
                    Hull.Tube(new FVector3(1.8f, 1.1f, -0.4f), new FVector3(1.8f, 1.1f, 1.0f), 0.2f, 0.2f, Palette.Gunmetal, 8);
                    Hull.Tube(new FVector3(-1.8f, 1.1f, -0.4f), new FVector3(-1.8f, 1.1f, 1.0f), 0.2f, 0.2f, Palette.Gunmetal, 8);
                    Hull.Tube(new FVector3(0.0f, 0.8f, 2.4f), new FVector3(0.0f, 0.8f, 3.4f), 0.06f, 0.06f, Palette.Gunmetal, 6);
                }

                float Span = bTransport ? 8.5f : 7.0f;
                Rotor.Box(FVector3.Zero, new FVector3(Span, 0.04f, 0.28f), Palette.Hex(0x222222));
                Rotor.Box(FVector3.Zero, new FVector3(0.28f, 0.04f, Span), Palette.Hex(0x222222));
                break;
            }
        }

        return new EntityHull(Hull, Turret.IsEmpty ? null : Turret, Rotor.IsEmpty ? null : Rotor);
    }
}

public sealed class EntityHull
{
    public readonly MeshKit Hull;
    public readonly MeshKit? Turret;
    public readonly MeshKit? Rotor;

    public EntityHull(MeshKit Hull, MeshKit? Turret, MeshKit? Rotor)
    {
        this.Hull = Hull;
        this.Turret = Turret;
        this.Rotor = Rotor;
    }
}
