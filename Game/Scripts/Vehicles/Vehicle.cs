using System;
using System.Collections.Generic;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

public struct FVehicleInput
{
    public float Throttle;
    public float Steer;
    public float Strafe;
    public float Lift;
    public bool bFirePrimary;
    public bool bFireSecondary;
    public bool bBoost;
    public bool bHandbrake;
    // Path following is steering this frame, so the throttle and steer fields are left unused.
    public bool bAutopilot;
    public bool bHasAim;
    public FVector3 AimPoint;
    public float DesiredYaw;
    public bool bHasDesiredYaw;
}

public enum EDriver : byte
{
    None,
    Ai,
    Player,
}

// Ground vehicles are dynamic bodies driven through the engine's raycast wheels, and aircraft are dynamic bodies held up by rotor thrust.
public sealed class Vehicle : EntityScript, IDamageable
{
    [Property(Category = "Vehicle")]
    public EVehicleType Type = EVehicleType.Jeep;

    [Property(Category = "Vehicle")]
    public EFaction Team = EFaction.VZ;

    [Property(Category = "Vehicle")]
    public bool bCrewed = true;

    public List<FVector3>? Route;
    public Site? HomeSite;
    public FVector3 Home;
    public float DropFrom;
    public bool bContractCargo;

    private VehicleDef Def = null!;
    private float Health;
    private float Speed;
    private float Yaw;
    private FVector3 Pos;
    private float TurretYaw;
    private float TurretPitch;
    private float RotorAngle;
    private float RotorSpool;
    private FVehicleInput AirInput;
    private bool bDestroyed;
    private bool bBurning;
    private float BurnTime;
    private float FxTimer;
    private Entity FireFx = Entity.Null;
    private const float VehicleFireScale = 0.6f;
    private const float WreckFireScale = 1.0f;
    private float SinkTime;
    private float FallSpeed;
    private float CrashCooldown;
    private float UpsideDownTime;
    private FQuat BodyRotation = FQuat.Identity;
    private bool bDropping;
    private bool bReady;
    private int PodSide = 1;

    private EDriver Driver = EDriver.None;
    private MercPlayer? Rider;
    private FVehicleInput PlayerInput;

    private WeaponState? PrimaryWeapon;
    private WeaponState? SecondaryWeapon;

    private Entity TurretEntity = Entity.Null;
    private Entity HeadlightEntity = Entity.Null;
    private Entity RotorEntity = Entity.Null;
    private STransformComponent? TurretTransform => TurretEntity.IsNull ? null : Registry.TryGet<STransformComponent>(TurretEntity);
    private STransformComponent? RotorTransform => RotorEntity.IsNull ? null : Registry.TryGet<STransformComponent>(RotorEntity);
    private SVehicleComponent? Drivetrain => Def.bAir || bDestroyed ? null : Registry.TryGet<SVehicleComponent>(Entity);
    private SPathFollowComponent? Autopilot => Def.bAir || bDestroyed ? null : Registry.TryGet<SPathFollowComponent>(Entity);
    private FVector3 AutopilotGoal;
    private bool bAutopiloting;

    private const float WheelTravel = 0.3f;
    private const float SuspensionFrequency = 1.6f;
    // How far the suspension settles under the vehicle's own weight, so the wheels rest where the hull expects them.
    private static readonly float RestSag = 9.81f / MathF.Pow(2.0f * MathF.PI * SuspensionFrequency, 2.0f);

    private IDamageable? AiTarget;
    private bool bAiTargetVisible;
    private float AiThink;
    private int RouteIndex;
    private float OrbitAngle;
    private float AiFleeTime;
    private float BlockedTime;
    private float AiBurst;

    public Entity Owner => Entity;
    public EFaction Faction => Team;
    public bool IsAlive => !bDestroyed && bReady;
    public bool IsPlayerControlled => Rider is not null;
    public FVector3 Position => Pos + new FVector3(0.0f, Def.HalfExtents.Y + Def.Clearance, 0.0f);
    public float Radius => MathF.Max(Def.HalfExtents.X, Def.HalfExtents.Z) * 0.75f;
    public VehicleDef Definition => Def;
    public bool IsCrewed => bCrewed && !bDestroyed && Driver == EDriver.Ai;
    public bool IsEmpty => !bCrewed && Rider is null && !bDestroyed;
    public float HealthFraction => Def is null ? 0.0f : Mathf.Clamp01(Health / Def.MaxHealth);
    public float CurrentSpeed => Def.bAir ? Velocity.Length : MathF.Abs(Speed);
    public float Heading => Yaw;
    public FVector3 GroundPosition => Pos;

    public bool EngineRunning => !bDestroyed && (Driver != EDriver.None || bDropping);

    public float EngineLoad => Mathf.Clamp01(CurrentSpeed / Def.MaxSpeed);
    public bool IsBurning => bBurning;
    public FVector3 Velocity => CPhysicsLibrary.GetLinearVelocity(World, Entity);
    public WeaponState? Primary => PrimaryWeapon;
    public WeaponState? Secondary => SecondaryWeapon;
    public float Altitude => Pos.Y - MathF.Max(Terrain.HeightAt(Pos.X, Pos.Z), Terrain.SeaLevel);

    public override void OnReady()
    {
        Def = VehicleDefs.Get(Type);
        Health = Def.MaxHealth;
        Pos = World.GetEntityLocation(Entity);
        Yaw = Geo.YawOf(World.Registry.Get<STransformComponent>(Entity).GetForward());
        TurretYaw = Yaw;
        if (Home == FVector3.Zero)
        {
            Home = Pos;
        }

        if (DropFrom > 0.0f)
        {
            bDropping = true;
            Pos.Y = Terrain.HeightAt(Pos.X, Pos.Z) + DropFrom;
        }
        else if (!Def.bAir)
        {
            // Dropped onto its suspension from just above the slope, since a hull corner starting inside the ground mesh can be pushed through it.
            Pos.Y = Terrain.HeightAt(Pos.X, Pos.Z) + 0.5f;
        }
        else if (Pos.Y < Terrain.HeightAt(Pos.X, Pos.Z) + 1.0f)
        {
            Pos.Y = MathF.Max(Terrain.HeightAt(Pos.X, Pos.Z), Terrain.SeaLevel);
        }

        OrbitAngle = Mercs.Range(0.0f, Mathf.TwoPi);
        Driver = bCrewed ? EDriver.Ai : EDriver.None;
        PrimaryWeapon = Def.Primary != EWeapon.None ? new WeaponState(Def.Primary) : null;
        SecondaryWeapon = Def.Secondary != EWeapon.None ? new WeaponState(Def.Secondary) : null;

        BodyRotation = Def.bAir ? Geo.Orientation(Yaw, 0.0f, 0.0f) : FQuat.FromToRotation(FVector3.Up, Terrain.NormalAt(Pos.X, Pos.Z)) * Geo.Orientation(Yaw, 0.0f, 0.0f);
        PlaceBody();
        BuildVisuals(PaintFor(Team, Type));
        RotorSpool = Def.bAir && bCrewed ? 1.0f : 0.0f;
        BuildBody(EBodyType.Dynamic);
        if (!Def.bAir)
        {
            BuildDrivetrain();
        }

        SAIStimuliSourceComponent Visible = Registry.GetOrAdd<SAIStimuliSourceComponent>(Entity)!;
        Visible.SightTargetOffset = new FVector3(0.0f, Def.Clearance + Def.HalfExtents.Y, 0.0f);
        ApplyTransform();

        Mercs.Vehicles.Add(this);
        Mercs.Register(this);
        bReady = true;
    }

    public override void OnDetach()
    {
        Mercs.Fx.StopFire(FireFx);
        FireFx = Entity.Null;
        Mercs.Vehicles.Remove(this);
        Mercs.Unregister(this);
        HomeSite?.Forget(this);
        if (Rider is not null)
        {
            Rider.ForceExitVehicle(Pos + new FVector3(0.0f, 2.0f, 0.0f));
        }
    }

    public static FVector4 PaintFor(EFaction Team, EVehicleType Type)
    {
        if (Team == EFaction.Civilian)
        {
            FVector4[] Colors = { Palette.Hex(0xB23A2A), Palette.Hex(0xD9D2C1), Palette.Hex(0x2A5B9A), Palette.Hex(0x3C6E3C), Palette.Hex(0xC9A227) };
            return Colors[Mercs.RangeInt(0, Colors.Length)];
        }

        FVector4 Base = Mercs.Factions.Get(Team).Color;
        return Type is EVehicleType.Tank or EVehicleType.Apc ? Palette.Shade(Base, 0.8f) : Base;
    }

    private void BuildVisuals(FVector4 Paint)
    {
        EntityHull Hulls = VehicleDefs.BuildHull(Type, Paint);
        Hulls.Hull.Commit(Registry, Entity, false, true, false);

        if (Hulls.Turret is not null)
        {
            if (TurretEntity.IsNull)
            {
                TurretEntity = World.CreateEntity("Turret", FVector3.Zero);
                World.SetParent(TurretEntity, Entity);
                TurretTransform?.SetLocalLocation(Def.TurretOffset);
            }

            Hulls.Turret.Commit(Registry, TurretEntity, false, true, false);
        }

        if (!Def.bAir && HeadlightEntity.IsNull)
        {
            HeadlightEntity = World.CreateEntity("Headlights", FVector3.Zero);
            World.SetParent(HeadlightEntity, Entity);
            Registry.Get<STransformComponent>(HeadlightEntity).SetLocalLocation(new FVector3(0.0f, Def.Clearance + Def.HalfExtents.Y, Def.HalfExtents.Z + 0.1f));
            SSpotLightComponent Beam = Registry.GetOrAdd<SSpotLightComponent>(HeadlightEntity)!;
            Beam.LightColor = new FVector3(1.0f, 0.93f, 0.8f);
            Beam.Intensity = 0.0f;
            Beam.InnerConeAngle = 18.0f;
            Beam.OuterConeAngle = 34.0f;
            Beam.Attenuation = 45.0f;
        }

        if (Hulls.Rotor is not null)
        {
            if (RotorEntity.IsNull)
            {
                RotorEntity = World.CreateEntity("Rotor", FVector3.Zero);
                World.SetParent(RotorEntity, Entity);
                float RotorHeight = Type == EVehicleType.TransportHeli ? 3.55f : 3.1f;
                RotorTransform?.SetLocalLocation(new FVector3(0.0f, RotorHeight, 0.2f));
            }

            Hulls.Rotor.Commit(Registry, RotorEntity, false, false, false);
        }
    }

    private void BuildBody(EBodyType BodyType)
    {
        using (new FPhysicsBatchScope(World))
        {
            SBoxColliderComponent Collider = Registry.GetOrAdd<SBoxColliderComponent>(Entity)!;
            Collider.HalfExtent = Def.HalfExtents;
            Collider.TranslationOffset = new FVector3(0.0f, Def.HalfExtents.Y + Def.Clearance, 0.0f);
            Collider.bAffectsNavigation = false;
            SRigidBodyComponent Body = Registry.GetOrAdd<SRigidBodyComponent>(Entity)!;
            Body.BodyType = BodyType;
            Body.Mass = Def.MaxHealth * 3.0f;
            Body.bOverrideMass = true;
            Body.bUseGravity = BodyType == EBodyType.Dynamic;
            Body.bAllowSleeping = true;
            Body.bUseContinuousCollision = BodyType == EBodyType.Dynamic;
            // Low in the hull, which is what keeps a hard turn from rolling the vehicle.
            Body.CenterOfMassOffset = new FVector3(0.0f, -Def.HalfExtents.Y * 0.9f, 0.0f);
            if (!bDestroyed)
            {
                Body.OnContactBegin.Bind(OnHullContact);
            }
        }
    }

    // Skids and bumpers soak up a gentle knock, and anything harder damages the hull and whatever it struck.
    private void OnHullContact(SCollisionEvent Contact)
    {
        IDamageable? Other = Mercs.FindDamageable(Contact.Other);
        float Threshold = Def.bAir ? AirCrashSpeed : GroundCrashSpeed;
        if (bDestroyed || Contact.ImpactSpeed < Threshold || Other is Soldier or MercPlayer || CrashCooldown > 0.0f)
        {
            return;
        }

        // One collision reports from several contact points, so the cooldown keeps it from dealing its damage more than once.
        CrashCooldown = 0.5f;
        float Crash = (Contact.ImpactSpeed - Threshold) * (Def.bAir ? 40.0f : Def.bHeavy ? 8.0f : 20.0f);
        TakeHit(new FHit { Amount = Crash, Kind = EDamageKind.Crush, Point = Contact.Point, Direction = Contact.Normal, Source = Other?.Owner ?? Entity.Null });
        Other?.TakeHit(FHit.From(this, Crash * (Def.bHeavy ? 3.0f : 1.0f), EDamageKind.Crush, Contact.Point, -Contact.Normal));
        Mercs.Fx.Impact(Contact.Point, Contact.Normal, true);
        if (IsPlayerControlled)
        {
            CameraShake.Impact(MathF.Min(0.8f, Contact.ImpactSpeed * 0.05f), 0.3f);
        }
    }

    private void BuildDrivetrain()
    {
        SVehicleComponent Car = Registry.GetOrAdd<SVehicleComponent>(Entity)!;
        Car.Steering = Def.bSkidSteer ? EVehicleSteering.Skid : EVehicleSteering.Wheels;
        Car.MaxSpeed = Def.MaxSpeed;
        Car.MaxReverseSpeed = Def.MaxSpeed * 0.45f;
        Car.Acceleration = Def.Acceleration;
        Car.SkidTurnRate = Def.TurnRate;
        Car.SuspensionFrequency = SuspensionFrequency;
        Car.TireGrip = Def.bHeavy ? 1.5f : 1.2f;
        Car.Wheels.Clear();

        SPathFollowComponent Route = Registry.GetOrAdd<SPathFollowComponent>(Entity)!;
        Route.NavAgent = Mercs.VehicleNavAgent;
        Route.AcceptanceRadius = 4.0f;
        Route.RepathInterval = 2.0f;
        Route.RepathDistance = 4.0f;
        Route.VehicleBrakingDistance = 10.0f;

        Dictionary<(float, float), CStaticMesh?> Meshes = new();
        foreach (FWheelSpec Spec in Def.Wheels)
        {
            Entity Visual = Entity.Null;
            if (Spec.bVisual)
            {
                if (!Meshes.TryGetValue((Spec.Radius, Spec.Width), out CStaticMesh? Mesh))
                {
                    Mesh = VehicleDefs.BuildWheel(Spec.Radius, Spec.Width).BuildStaticMesh(World);
                    Meshes[(Spec.Radius, Spec.Width)] = Mesh;
                }

                Visual = World.CreateEntity("Wheel", FVector3.Zero);
                World.SetParent(Visual, Entity);
                MeshKit.Show(Registry, Visual, Mesh, true, false);
            }

            FVector3 Mount = new(Spec.X, Spec.Radius + WheelTravel - RestSag, Spec.Z);
            Car.AddWheel(Mount, Spec.Radius, WheelTravel, Spec.bSteer, true, Spec.Z < 0.0f, Visual.Id);
        }
    }

    // Lit only after dark with someone at the wheel, which also makes an occupied vehicle visible from afar at night.
    private void UpdateHeadlights()
    {
        if (!HeadlightEntity.IsNull && Registry.TryGet<SSpotLightComponent>(HeadlightEntity) is { } Beam)
        {
            Beam.Intensity = !bDestroyed && Driver != EDriver.None && Mercs.Clock.IsNight ? 60.0f : 0.0f;
        }
    }

    private void PlaceBody()
    {
        World.SetEntityLocation(Entity, Pos);
        World.SetEntityRotation(Entity, BodyRotation);
    }

    // Physics owns a ground vehicle's pose, so the script reads it back rather than integrating its own.
    private void SyncFromBody()
    {
        Pos = World.GetEntityLocation(Entity);
        if (Registry.TryGet<STransformComponent>(Entity) is { } Transform)
        {
            BodyRotation = Transform.GetWorldRotation();
            Yaw = Geo.YawOf(Geo.Flat(BodyRotation.Rotate(FVector3.Forward)).NormalizedOr(Geo.Heading(Yaw)));
        }
    }

    private void ApplyTransform()
    {
        if (TurretTransform is not null)
        {
            float RelativeYaw = Mathf.DeltaAngleDegrees(Yaw, TurretYaw);
            TurretTransform.SetLocalRotation(FQuat.FromEuler(Mathf.Radians(-TurretPitch), Mathf.Radians(RelativeYaw), 0.0f));
        }

        if (RotorTransform is not null)
        {
            RotorTransform.SetLocalRotation(FQuat.FromEuler(0.0f, RotorAngle, 0.0f));
        }
    }

    public FVector3 TurretPivot => Def.bTurret ? Pos + BodyRotation.Rotate(Def.TurretOffset) : Position + Geo.Heading(Yaw) * Def.HalfExtents.Z;

    public float DistanceToHull(FVector3 Point)
    {
        FVector3 Offset = Point - Position;
        float Along = MathF.Max(0.0f, MathF.Abs(FVector3.Dot(Offset, Geo.Heading(Yaw))) - Def.HalfExtents.Z);
        float Side = MathF.Max(0.0f, MathF.Abs(FVector3.Dot(Offset, Geo.RightOf(Yaw))) - Def.HalfExtents.X);
        float Up = MathF.Max(0.0f, MathF.Abs(Offset.Y) - Def.HalfExtents.Y - 1.0f);
        return MathF.Sqrt(Along * Along + Side * Side + Up * Up);
    }

    public FVector3 ExitPoint(float Side)
    {
        FVector3 Right = Geo.RightOf(Yaw);
        FVector3 Candidate = Pos + Right * Side * (Def.HalfExtents.X + 1.3f);
        return Geo.Ground(Candidate) + new FVector3(0.0f, HumanoidBody.FeetOffset + 0.2f, 0.0f);
    }

    public void EnterPlayer(MercPlayer Player)
    {
        Rider = Player;
        Driver = EDriver.Player;
        bCrewed = false;
        AiTarget = null;
        PlayerInput = default;
        Mercs.Factions.SetDisguise(Team);
    }

    public void ExitPlayer()
    {
        Rider = null;
        Driver = EDriver.None;
        PlayerInput = default;
        Mercs.Factions.SetDisguise(EFaction.None);
    }

    public void SetPlayerInput(in FVehicleInput Input)
    {
        PlayerInput = Input;
    }

    public void TakeOver(EFaction NewTeam)
    {
        Team = NewTeam;
    }

    // Light vehicles throw their crew out alive, heavy ones lose it inside the hatch.
    public void EjectCrew(bool bSurvive, IDamageable? Hijacker)
    {
        if (!bCrewed)
        {
            return;
        }

        bCrewed = false;
        Driver = EDriver.None;
        if (!bSurvive || Team == EFaction.Civilian && Mercs.Chance(0.5f))
        {
            return;
        }

        int Count = Math.Min(Def.Crew, Def.bAir ? 0 : 3);
        for (int Index = 0; Index < Count; ++Index)
        {
            FVector3 At = ExitPoint(Index % 2 == 0 ? -1.0f : 1.0f) + Geo.Heading(Yaw) * (Index * 1.2f - 1.0f);
            Soldier? Crew = Spawner.SpawnSoldier(Team, Team == EFaction.Civilian ? ESoldierRole.Civilian : ESoldierRole.Rifleman, At, HomeSite);
            if (Crew is not null && Hijacker is not null)
            {
                Crew.OnSquadAlert(Hijacker);
            }
        }
    }

    public override void OnUpdate(float DeltaTime)
    {
        if (!bReady || !Mercs.IsRunning)
        {
            return;
        }

        if (bDestroyed)
        {
            TickWreck(DeltaTime);
            return;
        }

        PrimaryWeapon?.Tick(DeltaTime);
        SecondaryWeapon?.Tick(DeltaTime);

        FVehicleInput Input = default;
        if (Driver == EDriver.Player)
        {
            Input = PlayerInput;
        }
        else if (Driver == EDriver.Ai)
        {
            float Distance = FVector3.Distance(Pos, Mercs.PlayerPosition);
            if (Distance > 420.0f && !Def.bAir)
            {
                Drivetrain?.SetInput(0.0f, 0.0f);
                return;
            }

            Input = ThinkAi(DeltaTime);
        }

        if (bDropping)
        {
            TickDrop(DeltaTime);
        }
        else if (Def.bAir)
        {
            TickAir(DeltaTime, Input);
        }
        else
        {
            TickGround(DeltaTime, Input);
        }

        if (Driver != EDriver.None)
        {
            AimTurret(Input, DeltaTime);
            if (Input.bFirePrimary)
            {
                FireWeapon(PrimaryWeapon, Input);
            }

            if (Input.bFireSecondary)
            {
                FireWeapon(SecondaryWeapon, Input);
            }
        }

        TickDamageFx(DeltaTime);
        UpdateHeadlights();
        ApplyTransform();
    }

    // Held at a parachute's pace until it is close to the ground, then physics sets it down.
    private void TickDrop(float DeltaTime)
    {
        SyncFromBody();
        if (Pos.Y > Terrain.HeightAt(Pos.X, Pos.Z) + 1.5f)
        {
            CPhysicsLibrary.SetLinearVelocity(World, Entity, new FVector3(0.0f, -7.0f, 0.0f));
            return;
        }

        bDropping = false;
        Mercs.Fx.Puff(Pos + new FVector3(0.0f, 0.5f, 0.0f), 4.0f, 2.5f, false, new FVector3(0.0f, 1.0f, 0.0f));
        Mercs.Feed.Post($"{Def.Name} delivered.", ENewsTone.Good);
    }

    private void TickGround(float DeltaTime, FVehicleInput Input)
    {
        SyncFromBody();
        SVehicleComponent? Car = Drivetrain;
        if (Car is null)
        {
            return;
        }

        bool bFlooding = SinkTime > 0.0f;
        Car.MaxSpeed = Def.MaxSpeed * (Input.bBoost ? 1.2f : 1.0f);
        if (!Input.bAutopilot || bFlooding)
        {
            StopAutopilot();
            Car.SetInput(bFlooding ? 0.0f : Input.Throttle, Input.Steer, bFlooding ? 1.0f : 0.0f, Input.bHandbrake);
        }
        Speed = Car.ForwardSpeed;

        bool bStuck = MathF.Abs(Input.Throttle) > 0.3f && MathF.Abs(Speed) < 0.8f;
        BlockedTime = bStuck ? BlockedTime + DeltaTime : 0.0f;

        TickLanding(Car);
        TickRighting(DeltaTime);
        TickFellThrough();

        FVector3 Forward = Geo.Heading(Yaw);
        CrashCooldown -= DeltaTime;
        if (MathF.Abs(Speed) > 0.3f)
        {
            CheckObstacle(Forward * MathF.Sign(Speed), MathF.Abs(Speed) * DeltaTime);
        }

        CrushInfantry();
        Mercs.Destruction.CrushFoliage(Pos, Forward, Def.HalfExtents.X, Def.HalfExtents.Z, MathF.Abs(Speed), Def.bHeavy);
        CheckWater(DeltaTime);
    }

    // A last resort for a body that tunneled through the ground mesh, which would otherwise fall until it reached the sea floor.
    private void TickFellThrough()
    {
        if (Pos.Y > Terrain.HeightAt(Pos.X, Pos.Z) - 3.0f || Terrain.HeightAt(Pos.X, Pos.Z) < Terrain.SeaLevel)
        {
            return;
        }

        Pos = Geo.Ground(Pos) + new FVector3(0.0f, 1.0f, 0.0f);
        BodyRotation = Geo.Orientation(Yaw, 0.0f, 0.0f);
        PlaceBody();
        CPhysicsLibrary.SetLinearVelocity(World, Entity, FVector3.Zero);
        CPhysicsLibrary.SetAngularVelocity(World, Entity, FVector3.Zero);
    }

    // Nobody can climb out and push, so a vehicle left on its roof is set back on its wheels after a while.
    private void TickRighting(float DeltaTime)
    {
        bool bUpsideDown = BodyRotation.Rotate(FVector3.Up).Y < 0.3f && MathF.Abs(Speed) < 2.0f;
        UpsideDownTime = bUpsideDown ? UpsideDownTime + DeltaTime : 0.0f;
        if (UpsideDownTime < 4.0f)
        {
            return;
        }

        UpsideDownTime = 0.0f;
        Pos = Geo.Ground(Pos) + new FVector3(0.0f, 1.0f, 0.0f);
        BodyRotation = Geo.Orientation(Yaw, 0.0f, 0.0f);
        PlaceBody();
        CPhysicsLibrary.SetLinearVelocity(World, Entity, FVector3.Zero);
        CPhysicsLibrary.SetAngularVelocity(World, Entity, FVector3.Zero);
    }

    // A long fall hurts on the landing, which the suspension alone would soak up without a scratch.
    private void TickLanding(SVehicleComponent Car)
    {
        if (!Car.IsGrounded())
        {
            FallSpeed = MathF.Min(FallSpeed, Velocity.Y);
            return;
        }

        if (FallSpeed < -14.0f)
        {
            TakeHit(new FHit { Amount = (-FallSpeed - 14.0f) * 25.0f, Kind = EDamageKind.Crush, Point = Position, Direction = FVector3.Up });
        }
        FallSpeed = 0.0f;
    }

    private bool CheckObstacle(FVector3 Direction, float Distance)
    {
        FVector3 Right = FVector3.Cross(FVector3.Up, Direction).NormalizedOr(FVector3.Right);
        float Height = Def.Clearance + Def.HalfExtents.Y * 0.8f;
        float Reach = Def.HalfExtents.Z + Distance + 0.4f;
        Entity RiderEntity = Rider?.Owner ?? Entity.Null;

        for (int Lane = -1; Lane <= 1; ++Lane)
        {
            FVector3 From = Pos + new FVector3(0.0f, Height, 0.0f) + Right * (Lane * Def.HalfExtents.X * 0.8f);
            SRayResult Hit = Geo.Trace(From, From + Direction * Reach, Entity, RiderEntity);
            if (!Hit.bHit || Hit.Normal.Y > 0.7f)
            {
                continue;
            }

            IDamageable? Other = Mercs.FindDamageable(new Entity(Hit.Entity));
            if (Other is Soldier or MercPlayer)
            {
                continue;
            }

            if (Other is null && Registry.Has<SRigidBodyComponent>(new Entity(Hit.Entity)) && Registry.Get<SRigidBodyComponent>(new Entity(Hit.Entity)).BodyType == EBodyType.Dynamic)
            {
                continue;
            }

            // Knocked down just ahead of the bumper, so a flimsy prop gives way rather than stopping the hull dead.
            float Impact = MathF.Abs(Speed);
            if (Other is Structure Prop && ((Prop.IsFlimsy && Impact > 5.0f) || (Def.bHeavy && Prop.IsCrushableByArmor && Impact > 2.0f)))
            {
                Prop.TakeHit(FHit.From(this, 99999.0f, EDamageKind.Crush, Hit.Location, Direction));
                continue;
            }

            return true;
        }

        return false;
    }

    private void CrushInfantry()
    {
        if (MathF.Abs(Speed) < Def.CrushSpeed)
        {
            return;
        }

        FVector3 Forward = Geo.Heading(Yaw);
        FVector3 Right = Geo.RightOf(Yaw);
        foreach (Soldier Victim in Mercs.Soldiers)
        {
            if (!Victim.IsAlive)
            {
                continue;
            }

            FVector3 Offset = Victim.Position - Pos;
            if (MathF.Abs(Offset.Y) > 3.0f)
            {
                continue;
            }

            float Along = FVector3.Dot(Offset, Forward);
            float Side = FVector3.Dot(Offset, Right);
            if (MathF.Abs(Along) < Def.HalfExtents.Z + 0.4f && MathF.Abs(Side) < Def.HalfExtents.X + 0.35f)
            {
                Victim.TakeHit(FHit.From(this, 60.0f * MathF.Abs(Speed), EDamageKind.Crush, Victim.Position, Forward * MathF.Sign(Speed)));
            }
        }

        if (Mercs.Player is { } Player && Player.IsAlive && Player.CurrentVehicle is null && !IsPlayerControlled)
        {
            FVector3 Offset = Player.Position - Pos;
            float Along = FVector3.Dot(Offset, Forward);
            float Side = FVector3.Dot(Offset, Right);
            if (MathF.Abs(Offset.Y) < 3.0f && MathF.Abs(Along) < Def.HalfExtents.Z + 0.3f && MathF.Abs(Side) < Def.HalfExtents.X + 0.3f)
            {
                Player.TakeHit(FHit.From(this, 6.0f * MathF.Abs(Speed), EDamageKind.Crush, Player.Position, Forward * MathF.Sign(Speed)));
            }
        }
    }

    private void CheckWater(float DeltaTime)
    {
        if (Terrain.HeightAt(Pos.X, Pos.Z) > Terrain.SeaLevel - 1.2f)
        {
            SinkTime = 0.0f;
            return;
        }

        SinkTime += DeltaTime;
        if (SinkTime > 3.0f)
        {
            Mercs.Feed.Post($"{Def.Name} flooded.", ENewsTone.Bad);
            Destroy(new FHit { Kind = EDamageKind.Crush, Direction = FVector3.Up }, false);
        }
    }

    private const float Gravity = 9.81f;
    private const float RotorSpoolTime = 3.0f;
    private const float ClimbRate = 10.0f;
    private const float MaxVerticalAccel = 8.0f;
    private const float MaxThrustToWeight = 2.2f;
    private const float MaxLeanDegrees = 28.0f;
    private const float LandedAltitude = 0.8f;
    private const float CeilingAltitude = 160.0f;
    private const float AirCrashSpeed = 7.0f;
    private const float GroundCrashSpeed = 9.0f;

    // The rotor needs a driver to keep turning, so an abandoned helicopter winds down and falls out of the sky.
    private void TickAir(float DeltaTime, FVehicleInput Input)
    {
        SyncFromBody();
        AirInput = Input;
        CrashCooldown -= DeltaTime;
        RotorSpool = Mathf.MoveTowards(RotorSpool, Driver != EDriver.None ? 1.0f : 0.0f, DeltaTime / RotorSpoolTime);
        RotorAngle += DeltaTime * 28.0f * RotorSpool;

        if (Pos.Y < Terrain.SeaLevel - 0.5f)
        {
            Mercs.Feed.Post($"{Def.Name} ditched in the sea.", ENewsTone.Bad);
            Destroy(new FHit { Kind = EDamageKind.Crush, Direction = FVector3.Up }, false);
        }
    }

    public override void OnFixedUpdate(float FixedDeltaTime)
    {
        if (bReady && !bDestroyed && !bDropping && Def.bAir && Mercs.IsRunning && RotorSpool > 0.0f)
        {
            Fly(FixedDeltaTime);
        }
    }

    // Thrust pushes along the rotor mast, so the hull leans toward where it wants to go and the lean is what carries it there.
    private void Fly(float DeltaTime)
    {
        FVehicleInput Input = AirInput;
        FQuat Rotation = CPhysicsLibrary.GetBodyRotation(World, Entity);
        FVector3 Velocity = CPhysicsLibrary.GetLinearVelocity(World, Entity);
        FVector3 Spin = CPhysicsLibrary.GetAngularVelocity(World, Entity);
        FVector3 Mast = Rotation.Rotate(FVector3.Up);
        float HeadingYaw = Geo.YawOf(Geo.Flat(Rotation.Rotate(FVector3.Forward)).NormalizedOr(Geo.Heading(Yaw)));
        FVector3 Forward = Geo.Heading(HeadingYaw);
        FVector3 Right = Geo.RightOf(HeadingYaw);

        float Lift = Pos.Y > CeilingAltitude ? MathF.Min(Input.Lift, 0.0f) : Input.Lift;
        bool bParked = Altitude < LandedAltitude && Lift <= 0.0f;

        FVector3 Desired = bParked ? FVector3.Zero : (Forward * Input.Throttle + Right * Input.Strafe) * Def.MaxSpeed * (Input.bBoost ? 1.2f : 1.0f);
        FVector3 Push = Desired - Geo.Flat(Velocity);
        FVector3 HorizontalAccel = Push.Length > Def.Acceleration ? Push.Normalized() * Def.Acceleration : Push;
        float VerticalAccel = Math.Clamp((Lift * ClimbRate - Velocity.Y) * 2.0f, -MaxVerticalAccel, MaxVerticalAccel);
        float Support = bParked ? Gravity * 0.5f : Gravity + VerticalAccel;

        float MaxLean = Support * MathF.Tan(Mathf.Radians(MaxLeanDegrees));
        if (HorizontalAccel.Length > MaxLean)
        {
            HorizontalAccel = HorizontalAccel.Normalized() * MaxLean;
        }

        FVector3 WantedMast = bParked ? FVector3.Up : (HorizontalAccel + FVector3.Up * Support).NormalizedOr(FVector3.Up);
        float Mass = CPhysicsLibrary.GetMass(World, Entity);
        float Thrust = Math.Clamp(Mass * Support / MathF.Max(Mast.Y, 0.5f), 0.0f, Mass * Gravity * MaxThrustToWeight) * RotorSpool;
        CPhysicsLibrary.AddForce(World, Entity, Mast * Thrust);

        float YawRate = Input.bHasDesiredYaw
            ? Math.Clamp(Mathf.DeltaAngleDegrees(HeadingYaw, Input.DesiredYaw) * 2.5f, -Def.TurnRate, Def.TurnRate)
            : Input.Steer * Def.TurnRate;
        if (bParked)
        {
            YawRate = 0.0f;
        }

        FVector3 WantedSpin = FVector3.Cross(Mast, WantedMast) * 4.0f + FVector3.Up * Mathf.Radians(YawRate);
        CPhysicsLibrary.SetAngularVelocity(World, Entity, Spin + (WantedSpin - Spin) * MathF.Min(1.0f, 8.0f * DeltaTime) * RotorSpool);
    }

    private void AimTurret(FVehicleInput Input, float DeltaTime)
    {
        if (!Input.bHasAim)
        {
            TurretYaw = Mathf.MoveTowardsAngleDegrees(TurretYaw, Yaw, 90.0f * DeltaTime);
            TurretPitch = Mathf.MoveTowards(TurretPitch, 0.0f, 30.0f * DeltaTime);
            return;
        }

        FVector3 Pivot = TurretPivot;
        FVector3 Offset = Input.AimPoint - Pivot;
        float DesiredYaw = Geo.YawOf(Offset);
        float DesiredPitch = Mathf.Degrees(MathF.Atan2(Offset.Y, MathF.Max(Geo.Flat(Offset).Length, 0.1f)));
        float TurnSpeed = Type == EVehicleType.Tank ? 55.0f : (Type == EVehicleType.Apc ? 90.0f : 240.0f);
        TurretYaw = Def.bTurret ? Mathf.MoveTowardsAngleDegrees(TurretYaw, DesiredYaw, TurnSpeed * DeltaTime) : Yaw;
        TurretPitch = Math.Clamp(Mathf.MoveTowards(TurretPitch, DesiredPitch, TurnSpeed * 0.5f * DeltaTime), -12.0f, 35.0f);
    }

    private void FireWeapon(WeaponState? Weapon, FVehicleInput Input)
    {
        if (Weapon is null || !Weapon.CanFire)
        {
            return;
        }

        Weapon.Consume();
        Weapon.Magazine = Math.Max(Weapon.Magazine, 1);

        FVector3 Muzzle;
        FVector3 Direction;
        if (Def.bAir)
        {
            FVector3 Right = Geo.RightOf(Yaw);
            PodSide = -PodSide;
            Muzzle = Pos + new FVector3(0.0f, 1.1f, 0.0f) + Right * (Weapon.Kind == EWeapon.HeliRockets ? 1.8f * PodSide : 0.0f) + Geo.Heading(Yaw) * (Weapon.Kind == EWeapon.HeliRockets ? 1.2f : 3.5f);
            bool bFarAim = Input.bHasAim && FVector3.Distance(Input.AimPoint, Muzzle) > 35.0f;
            FVector3 Wanted = bFarAim ? (Input.AimPoint - Muzzle).NormalizedOr(Geo.Heading(Yaw)) : Geo.Direction(Yaw, -8.0f);
            FVector3 Nose = Geo.Direction(Yaw, -8.0f);
            Direction = FVector3.Dot(Wanted, Nose) > 0.8f ? Wanted : FVector3.Slerp(Nose, Wanted, 0.5f).Normalized();
        }
        else
        {
            FVector3 Pivot = TurretPivot;
            Direction = Geo.Direction(TurretYaw, TurretPitch);
            Muzzle = Pivot + Direction * Def.MuzzleLength;
            if (Weapon == SecondaryWeapon && Def.bTurret)
            {
                Muzzle = Pivot + Geo.RightOf(TurretYaw) * 0.5f + Direction * 1.0f;
            }
        }

        Weapons.Fire(this, Weapon.Def, Muzzle, Direction, Rider?.Owner ?? Entity.Null, Driver == EDriver.Ai ? 5.0f : 0.0f);
        if (Weapon.Kind == EWeapon.TankCannon && IsPlayerControlled)
        {
            CameraShake.Impact(0.35f, 0.25f);
        }
    }

    private FVehicleInput ThinkAi(float DeltaTime)
    {
        FVehicleInput Input = default;
        AiThink -= DeltaTime;
        if (AiThink <= 0.0f)
        {
            AiThink = Mercs.Range(0.35f, 0.55f);
            AiTarget = FindTarget(out bAiTargetVisible);
        }

        AiFleeTime -= DeltaTime;
        AiBurst -= DeltaTime;
        if (AiBurst < -1.2f)
        {
            AiBurst = Mercs.Range(0.8f, 1.6f);
        }

        bool bArmed = PrimaryWeapon is not null || SecondaryWeapon is not null;

        if (Def.bAir)
        {
            return ThinkHeli(DeltaTime, bArmed);
        }

        if (AiTarget is not null && bArmed)
        {
            FVector3 Offset = AiTarget.Position - Pos;
            float Distance = Geo.Flat(Offset).Length;
            Input.bHasAim = true;
            Input.AimPoint = AiTarget.Position + new FVector3(0.0f, AiTarget is Soldier or MercPlayer ? 0.2f : 0.6f, 0.0f);

            if (Type == EVehicleType.Technical)
            {
                FVector3 Side = FVector3.Cross(FVector3.Up, Geo.Flat(Offset).NormalizedOr(FVector3.Forward));
                SteerToward(AiTarget.Position - Geo.Flat(Offset).NormalizedOr(FVector3.Forward) * 22.0f + Side * 14.0f, 0.55f, ref Input);
            }
            else if (Distance > 65.0f)
            {
                SteerToward(AiTarget.Position, 0.6f, ref Input);
            }
            else if (Distance < 22.0f)
            {
                Input.Throttle = -0.4f;
            }

            if (bAiTargetVisible)
            {
                float Error = MathF.Abs(Mathf.DeltaAngleDegrees(TurretYaw, Geo.YawOf(Offset)));
                bool bArmor = AiTarget is Vehicle || AiTarget is Structure;
                if (PrimaryWeapon is not null && Error < 6.0f && (bArmor || PrimaryWeapon.Kind != EWeapon.TankCannon || Mercs.Chance(0.02f)))
                {
                    Input.bFirePrimary = Mercs.Chance(PrimaryWeapon.Def.bAutomatic ? 0.7f : 0.08f);
                }

                if (SecondaryWeapon is not null && Error < 12.0f && !bArmor)
                {
                    Input.bFireSecondary = AiBurst > 0.0f;
                }

                if (PrimaryWeapon is { Def.bAutomatic: true })
                {
                    Input.bFirePrimary &= AiBurst > 0.0f;
                }
            }

            return Input;
        }

        if (AiTarget is not null && !bArmed)
        {
            AiFleeTime = 6.0f;
        }

        FollowRoute(ref Input, AiFleeTime > 0.0f ? 1.0f : 0.55f);
        return Input;
    }

    private FVehicleInput ThinkHeli(float DeltaTime, bool bArmed)
    {
        FVehicleInput Input = default;
        FVector3 Center = AiTarget?.Position ?? Home;
        float OrbitRadius = AiTarget is not null ? 48.0f : 90.0f;
        float CruiseAltitude = AiTarget is not null ? 26.0f : 40.0f;

        OrbitAngle += DeltaTime * (AiTarget is not null ? 0.18f : 0.1f);
        FVector3 Goal = Center + new FVector3(MathF.Cos(OrbitAngle) * OrbitRadius, 0.0f, MathF.Sin(OrbitAngle) * OrbitRadius);
        float GoalAltitude = MathF.Max(Terrain.HeightAt(Goal.X, Goal.Z), Terrain.SeaLevel) + CruiseAltitude;
        FVector3 ToGoal = Geo.Flat(Goal - Pos);
        FVector3 Forward = Geo.Heading(Yaw);
        FVector3 Right = Geo.RightOf(Yaw);

        FVector3 Wanted = ToGoal.LengthSquared > 1.0f ? ToGoal.Normalized() * Mathf.Clamp01(ToGoal.Length / 30.0f) : FVector3.Zero;
        Input.Throttle = FVector3.Dot(Wanted, Forward);
        Input.Strafe = FVector3.Dot(Wanted, Right);
        Input.Lift = Math.Clamp((GoalAltitude - Pos.Y) * 0.2f, -1.0f, 1.0f);
        Input.bHasDesiredYaw = true;
        Input.DesiredYaw = AiTarget is not null ? Geo.YawOf(AiTarget.Position - Pos) : Geo.YawOf(ToGoal.LengthSquared > 1.0f ? ToGoal : Forward);

        if (AiTarget is not null && bArmed && bAiTargetVisible)
        {
            Input.bHasAim = true;
            Input.AimPoint = AiTarget.Position;
            float Error = MathF.Abs(Mathf.DeltaAngleDegrees(Yaw, Geo.YawOf(AiTarget.Position - Pos)));
            float Distance = FVector3.Distance(AiTarget.Position, Pos);
            if (Error < 14.0f && Distance < 130.0f)
            {
                Input.bFirePrimary = PrimaryWeapon is not null && AiBurst > 0.0f && Mercs.Chance(0.25f);
                Input.bFireSecondary = SecondaryWeapon is not null && AiBurst > 0.0f;
            }
        }

        return Input;
    }

    private void StopAutopilot()
    {
        if (bAutopiloting)
        {
            Autopilot?.Stop();
            bAutopiloting = false;
        }
    }

    // The vehicle navmesh routes a ground vehicle around what it cannot drive through, and the direct steer covers the time before it bakes.
    private void SteerToward(FVector3 Goal, float Throttle, ref FVehicleInput Input)
    {
        if (!Def.bAir && Mercs.bVehicleNavReady && Autopilot is { } Route)
        {
            if (!bAutopiloting || Geo.FlatDistance(AutopilotGoal, Goal) > 4.0f)
            {
                Route.SetTargetLocation(Goal);
                AutopilotGoal = Goal;
                bAutopiloting = true;
            }

            Route.Speed = Throttle;
            if (Route.IsFollowing() || !Route.DidPathFindingFail())
            {
                Input.bAutopilot = true;
                return;
            }
        }

        FVector3 Offset = Geo.Flat(Goal - Pos);
        float Error = Mathf.DeltaAngleDegrees(Yaw, Geo.YawOf(Offset));
        Input.Steer = Math.Clamp(Error / 35.0f, -1.0f, 1.0f);
        Input.Throttle = Throttle * (1.0f - MathF.Min(MathF.Abs(Input.Steer), 1.0f) * 0.45f);
        if (BlockedTime > 1.0f)
        {
            Input.Throttle = -0.6f;
            Input.Steer = -Input.Steer;
        }
    }

    private void FollowRoute(ref FVehicleInput Input, float Throttle)
    {
        if (Route is not { Count: > 0 })
        {
            if (Geo.FlatDistance(Pos, Home) > 12.0f)
            {
                SteerToward(Home, Throttle * 0.6f, ref Input);
            }

            return;
        }

        FVector3 Goal = Route[RouteIndex % Route.Count];
        if (Geo.FlatDistance(Pos, Goal) < 9.0f)
        {
            RouteIndex = (RouteIndex + 1) % Route.Count;
            Goal = Route[RouteIndex];
        }

        SteerToward(Goal, Throttle, ref Input);
    }

    private IDamageable? FindTarget(out bool bVisible)
    {
        bVisible = false;
        float Range = Def.bAir ? 150.0f : 110.0f;
        IDamageable? Best = null;
        float BestDistance = Range;

        void Consider(IDamageable Candidate)
        {
            if (!Candidate.IsAlive || Candidate == this)
            {
                return;
            }

            float Distance = FVector3.Distance(Candidate.Position, Pos);
            if (Distance >= BestDistance || !Mercs.Factions.IsHostile(Team, Candidate))
            {
                return;
            }

            Best = Candidate;
            BestDistance = Distance;
        }

        if (Mercs.Player is { } Player && Player.IsAlive)
        {
            Consider(Player.CurrentVehicle is { } Ride ? Ride : Player);
        }

        foreach (Vehicle Other in Mercs.Vehicles)
        {
            if (Other != this && Other.IsCrewed && FactionSystem.AtWar(Team, Other.Team))
            {
                Consider(Other);
            }
        }

        if (Best is null)
        {
            foreach (Soldier Other in Mercs.Soldiers)
            {
                if (Other.IsAlive && FactionSystem.AtWar(Team, Other.Faction) && FVector3.DistanceSquared(Other.Position, Pos) < Range * Range)
                {
                    Consider(Other);
                }
            }
        }

        if (Best is not null)
        {
            bVisible = Geo.LineOfSight(TurretPivot + new FVector3(0.0f, 0.3f, 0.0f), Best.Position + new FVector3(0.0f, 0.4f, 0.0f), Entity, Best.Owner);
        }

        return Best;
    }

    private void TickDamageFx(float DeltaTime)
    {
        FxTimer -= DeltaTime;
        float Fraction = HealthFraction;
        FVector3 Engine = Position + Geo.Heading(Yaw) * Def.HalfExtents.Z * 0.6f + new FVector3(0.0f, Def.HalfExtents.Y * 0.8f, 0.0f);
        if (bBurning)
        {
            FireFx = FireFx.IsNull ? Mercs.Fx.StartFire(Engine, VehicleFireScale) : FireFx;
            Mercs.Fx.MoveFire(FireFx, Engine, VehicleFireScale);
        }

        if (FxTimer <= 0.0f && Fraction < 0.6f && FireFx.IsNull)
        {
            FxTimer = bBurning ? 0.06f : 0.18f;
            Mercs.Fx.Puff(Engine, bBurning ? 1.4f : 0.9f, 2.2f, true, new FVector3(0.0f, 2.5f, 0.0f) - Velocity * 0.3f);
            if (bBurning)
            {
                Mercs.Fx.Flame(Engine, Mercs.Range(0.5f, 0.9f));
            }
        }

        if (bBurning)
        {
            BurnTime -= DeltaTime;
            if (BurnTime <= 0.0f)
            {
                Destroy(new FHit { Kind = EDamageKind.Fire, Direction = FVector3.Up }, true);
            }
        }
    }

    public void TakeHit(in FHit Hit)
    {
        if (bDestroyed || !bReady)
        {
            return;
        }

        float Amount = Hit.Amount;
        if (Hit.Kind == EDamageKind.Bullet)
        {
            Amount *= Def.bHeavy ? (Def.bAir ? 0.25f : 0.04f) : 0.4f;
        }

        if (Rider is not null && Hit.Source == Rider.Owner)
        {
            return;
        }

        Health -= Amount;

        if (Hit.bByPlayer && !IsPlayerControlled && Team != EFaction.Civilian)
        {
            if (Driver == EDriver.Ai)
            {
                Mercs.Factions.Provoke(Team);
            }
        }

        if (Driver == EDriver.Ai)
        {
            IDamageable? Attacker = Hit.bByPlayer && Mercs.Player is { } Player ? (Player.CurrentVehicle is { } Ride ? Ride : Player) : Mercs.FindDamageable(Hit.Source);
            if (Attacker is not null && Attacker.IsAlive && Mercs.Factions.IsHostile(Team, Attacker))
            {
                AiTarget = Attacker;
                AiThink = 0.5f;
            }
            else if (PrimaryWeapon is null && SecondaryWeapon is null)
            {
                AiFleeTime = 8.0f;
            }
        }

        if (!bBurning && Health <= Def.MaxHealth * 0.25f && Health > 0.0f)
        {
            bBurning = true;
            BurnTime = Mercs.Range(6.0f, 9.0f);
            if (IsPlayerControlled)
            {
                Mercs.Feed.Announce("VEHICLE ON FIRE", "Bail out before it blows!", 2.5f);
            }
            else if (bCrewed && !Def.bAir)
            {
                EjectCrew(true, Mercs.FindDamageable(Hit.Source));
            }
        }

        if (Health <= 0.0f)
        {
            Destroy(Hit, true);
        }
    }

    private void Destroy(FHit Hit, bool bExplode)
    {
        if (bDestroyed)
        {
            return;
        }

        bDestroyed = true;
        bBurning = false;
        Health = 0.0f;
        EFaction Owners = Team;
        bool bWasCrewed = bCrewed;
        bCrewed = false;
        Driver = EDriver.None;

        MercPlayer? Ejected = Rider;
        if (Rider is not null)
        {
            MercPlayer Player = Rider;
            Rider = null;
            Mercs.Factions.SetDisguise(EFaction.None);
            Player.ForceExitVehicle(ExitPoint(-1.0f));
            Player.TakeHit(new FHit { Amount = 45.0f, Kind = EDamageKind.Explosive, Point = Position, Direction = FVector3.Up });
        }

        if (bExplode)
        {
            float Blast = Type switch
            {
                EVehicleType.FuelTruck => 16.0f,
                EVehicleType.Tank or EVehicleType.Apc or EVehicleType.Truck => 9.0f,
                EVehicleType.AttackHeli or EVehicleType.TransportHeli => 10.0f,
                _ => 7.0f,
            };
            Explosion.Detonate(Position, Blast, Type == EVehicleType.FuelTruck ? 600.0f : 220.0f, Hit.bByPlayer ? (IDamageable?)Mercs.Player : null, 1.0f, true, Ejected);
            Mercs.Fx.Debris(Position, PaintFor(Owners, Type), 5, 9.0f, 0.5f);
        }

        if (Hit.bByPlayer && bWasCrewed && Owners != EFaction.Civilian)
        {
            Mercs.Factions.Offense(Owners, 6.0f, Pos, $"destroyed {Mercs.Factions.Get(Owners).Short} {Def.Name}");
        }

        if (Hit.bByPlayer && Mercs.Chance(0.6f))
        {
            Pickup.SpawnCash(Geo.Ground(Pos) + new FVector3(2.5f, 0.4f, 0.0f), Def.bHeavy ? 5000 : 1500);
        }

        Mercs.Contracts.OnVehicleDestroyed(this, Hit);
        HomeSite?.OnVehicleLost(this);

        BuildVisuals(Palette.Wreck);
        UpdateHeadlights();
        Registry.Remove<SVehicleComponent>(Entity);
        Registry.Remove<SAIStimuliSourceComponent>(Entity);
        Registry.Remove<SRigidBodyComponent>(Entity);
        Registry.Remove<SBoxColliderComponent>(Entity);
        BuildBody(EBodyType.Dynamic);
        CPhysicsLibrary.SetLinearVelocity(World, Entity, Velocity * 0.5f + new FVector3(Mercs.Range(-2.0f, 2.0f), Def.bAir ? 0.0f : Mercs.Range(6.0f, 10.0f), Mercs.Range(-2.0f, 2.0f)));
        CPhysicsLibrary.SetAngularVelocity(World, Entity, new FVector3(Mercs.Range(-2.0f, 2.0f), Mercs.Range(-1.5f, 1.5f), Mercs.Range(-2.0f, 2.0f)));
        World.SetLifetime(Entity, 45.0f);
    }

    private void TickWreck(float DeltaTime)
    {
        FVector3 At = World.GetEntityLocation(Entity) + new FVector3(0.0f, 1.5f, 0.0f);
        Pos = At;
        FireFx = FireFx.IsNull ? Mercs.Fx.StartFire(At, WreckFireScale) : FireFx;
        Mercs.Fx.MoveFire(FireFx, At, WreckFireScale);

        FxTimer -= DeltaTime;
        if (FxTimer <= 0.0f && FireFx.IsNull)
        {
            FxTimer = 0.25f;
            Mercs.Fx.Puff(At, 1.6f, 3.0f, true, new FVector3(0.0f, 2.2f, 0.0f));
            if (Mercs.Chance(0.4f))
            {
                Mercs.Fx.Flame(At, 0.8f);
            }
        }
    }
}
