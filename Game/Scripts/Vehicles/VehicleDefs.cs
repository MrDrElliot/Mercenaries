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

// One physics wheel in the vehicle's local frame, where Y is up from the ground under the vehicle at rest.
public readonly record struct FWheelSpec(float X, float Z, float Radius, float Width, bool bSteer, bool bVisual);

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
    public bool bSkidSteer;

    // Where the driver's hips sit in an open cab, so whoever drives is seen at the wheel; null keeps an enclosed cab's crew out of sight.
    public FVector3? DriverSeat;
    public readonly List<FWheelSpec> Wheels = new();

    // Four wheels at the corners, front pair steering.
    public VehicleDef Axles(float Track, float Front, float Rear, float Radius, float Width, params float[] Middle)
    {
        AddAxle(Track, Front, Radius, Width, true);
        foreach (float Z in Middle)
        {
            AddAxle(Track, Z, Radius, Width, false);
        }
        AddAxle(Track, Rear, Radius, Width, false);
        return this;
    }

    public VehicleDef AddAxle(float Track, float Z, float Radius, float Width, bool bSteer, bool bVisual = true)
    {
        Wheels.Add(new FWheelSpec(Track, Z, Radius, Width, bSteer, bVisual));
        Wheels.Add(new FWheelSpec(-Track, Z, Radius, Width, bSteer, bVisual));
        return this;
    }
}

public static class VehicleDefs
{
    private static readonly Dictionary<EVehicleType, VehicleDef> Defs = new();

    static VehicleDefs()
    {
        Add(new VehicleDef { Type = EVehicleType.Jeep, Name = "Jeep", MaxHealth = 450, MaxSpeed = 30, Acceleration = 14, TurnRate = 95, HalfExtents = new FVector3(0.95f, 0.7f, 2.1f), Crew = 1, CameraDistance = 8.5f, DriverSeat = new FVector3(-0.4f, 1.45f, -0.16f) }
            .Axles(0.85f, 1.35f, -1.35f, 0.42f, 0.3f));
        Add(new VehicleDef { Type = EVehicleType.Technical, Name = "Technical", MaxHealth = 500, MaxSpeed = 27, Acceleration = 12, TurnRate = 85, HalfExtents = new FVector3(1.0f, 0.75f, 2.4f), Primary = EWeapon.VehicleMG, bTurret = true, TurretOffset = new FVector3(0.0f, 2.1f, -1.2f), MuzzleLength = 1.1f, Crew = 2, CameraDistance = 9.0f, DriverSeat = new FVector3(-0.4f, 1.45f, -0.06f) }
            .Axles(0.85f, 1.35f, -1.35f, 0.42f, 0.3f));
        Add(new VehicleDef { Type = EVehicleType.Truck, Name = "Cargo Truck", MaxHealth = 800, MaxSpeed = 22, Acceleration = 8, TurnRate = 60, HalfExtents = new FVector3(1.25f, 1.3f, 3.6f), Clearance = 0.5f, Crew = 3, CameraDistance = 11.0f, CameraHeight = 4.0f }
            .Axles(1.05f, 2.4f, -2.6f, 0.55f, 0.4f, -1.2f));
        Add(new VehicleDef { Type = EVehicleType.FuelTruck, Name = "Fuel Truck", MaxHealth = 600, MaxSpeed = 20, Acceleration = 7, TurnRate = 55, HalfExtents = new FVector3(1.25f, 1.3f, 3.8f), Clearance = 0.5f, Crew = 1, CameraDistance = 11.0f, CameraHeight = 4.0f }
            .Axles(1.05f, 2.4f, -2.6f, 0.55f, 0.4f, -1.2f));
        Add(new VehicleDef { Type = EVehicleType.Apc, Name = "APC", bHeavy = true, MaxHealth = 1600, MaxSpeed = 20, Acceleration = 8, TurnRate = 55, HalfExtents = new FVector3(1.4f, 1.0f, 3.3f), Clearance = 0.45f, Primary = EWeapon.Autocannon, Secondary = EWeapon.VehicleMG, bTurret = true, TurretOffset = new FVector3(0.0f, 2.55f, 0.3f), MuzzleLength = 2.2f, Crew = 3, CameraDistance = 11.0f, CameraHeight = 4.0f, CrushSpeed = 3.0f }
            .AddAxle(1.3f, 2.4f, 0.55f, 0.36f, true).AddAxle(1.3f, 0.8f, 0.55f, 0.36f, true).AddAxle(1.3f, -0.8f, 0.55f, 0.36f, false).AddAxle(1.3f, -2.4f, 0.55f, 0.36f, false));
        VehicleDef Tank = new() { Type = EVehicleType.Tank, Name = "Main Battle Tank", bHeavy = true, MaxHealth = 3200, MaxSpeed = 15, Acceleration = 6, TurnRate = 42, HalfExtents = new FVector3(1.7f, 0.95f, 3.5f), Clearance = 0.3f, Primary = EWeapon.TankCannon, Secondary = EWeapon.VehicleMG, bTurret = true, TurretOffset = new FVector3(0.0f, 2.25f, -0.2f), MuzzleLength = 5.2f, Crew = 2, CameraDistance = 12.5f, CameraHeight = 4.5f, CrushSpeed = 2.0f, bSkidSteer = true };
        for (int Roller = 0; Roller < 6; ++Roller)
        {
            Tank.AddAxle(1.35f, 2.6f - Roller * 1.04f, 0.45f, 0.7f, false, false);
        }
        Add(Tank);
        Add(new VehicleDef { Type = EVehicleType.AttackHeli, Name = "Attack Helicopter", bHeavy = true, bAir = true, MaxHealth = 1300, MaxSpeed = 42, Acceleration = 14, TurnRate = 80, HalfExtents = new FVector3(1.3f, 1.3f, 5.2f), Clearance = 0.0f, Primary = EWeapon.HeliRockets, Secondary = EWeapon.Autocannon, Crew = 2, CameraDistance = 16.0f, CameraHeight = 5.0f });
        Add(new VehicleDef { Type = EVehicleType.TransportHeli, Name = "Transport Helicopter", bHeavy = true, bAir = true, MaxHealth = 1500, MaxSpeed = 36, Acceleration = 10, TurnRate = 60, HalfExtents = new FVector3(1.6f, 1.6f, 6.0f), Clearance = 0.0f, Secondary = EWeapon.VehicleMG, Crew = 2, CameraDistance = 18.0f, CameraHeight = 6.0f });
        Add(new VehicleDef { Type = EVehicleType.Sedan, Name = "Sedan", MaxHealth = 300, MaxSpeed = 32, Acceleration = 13, TurnRate = 100, HalfExtents = new FVector3(0.9f, 0.65f, 2.2f), Crew = 1, CameraDistance = 8.0f }
            .Axles(0.8f, 1.35f, -1.35f, 0.34f, 0.24f));
    }

    private static void Add(VehicleDef Def) => Defs[Def.Type] = Def;

    public static VehicleDef Get(EVehicleType Type) => Defs[Type];

    // Centered on its axle, so the vehicle can steer and spin it about its own origin.
    public static MeshKit BuildWheel(float Radius, float Width)
    {
        MeshKit Wheel = new();
        VehicleShapes.Wheel(Wheel, FVector3.Zero, FVector3.Right, Radius, Width);
        return Wheel;
    }

    public static EntityHull BuildHull(EVehicleType Type, FVector4 Paint)
    {
        MeshKit Hull = new();
        MeshKit Turret = new();
        MeshKit Rotor = new();
        VehicleShapes.Build(Type, Paint, Hull, Turret, Rotor);
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
