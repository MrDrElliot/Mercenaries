using System;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

// Runs after physics so it frames where the player actually ended up this frame, not where they were.
[UpdatePhase(EScriptPhase.PostPhysics)]
public sealed class MercCamera : EntityScript
{
    [Property(Category = "Camera", Min = 0.01f, Max = 2.0f)]
    public float Sensitivity = 0.12f;

    [Property(Category = "Camera", Units = "m")]
    public float FootDistance = 4.3f;

    [Property(Category = "Camera", Units = "m")]
    public float AimDistance = 2.1f;

    [Property(Category = "Camera", Units = "m")]
    public float ShoulderOffset = 0.65f;

    [Property(Category = "Camera", Min = 30.0f, Max = 120.0f)]
    public float FieldOfView = 70.0f;
    private const float HideBodyDistance = 0.9f;

    public static MercCamera? Instance;

    public float Yaw;
    public float Pitch = -12.0f;
    public FVector3 AimPoint;
    public IDamageable? AimTarget;
    public FVector3 ViewPosition;
    public FVector3 ViewForward = FVector3.Forward;

    private float CurrentDistance = 4.3f;
    private float CurrentFov = 70.0f;
    private float CurrentShoulder = 0.65f;
    private FVector3 SmoothedPivot;
    private bool bInitialized;
    private SCameraComponent? Camera => Registry.TryGet<SCameraComponent>(Entity);

    public override void OnReady()
    {
        Instance = this;
        SCameraComponent? Created = Registry.GetOrAdd<SCameraComponent>(Entity);
        if (Created is not null)
        {
            Created.FOV = FieldOfView;
            Created.FarPlane = 30000.0f;
            Created.bAutoActivate = true;
        }

        Registry.GetOrAdd<SAudioListenerComponent>(Entity);
        CCameraLibrary.SetActiveCamera(World, Entity, 0.0f, ECameraBlendFunction.Linear);
    }

    public override void OnDetach()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public override void OnUpdate(float DeltaTime)
    {
        MercPlayer? Player = Mercs.Player;
        if (Player is null || !Mercs.IsRunning)
        {
            return;
        }

        FVector2 Look = Controls.Look(Sensitivity * (Player.IsAiming ? 0.55f : 1.0f));
        Yaw += Look.X;
        Pitch = Math.Clamp(Pitch + Look.Y, -70.0f, 60.0f);

        Vehicle? Ride = Player.CurrentVehicle;
        FVector3 Pivot;
        float Distance;
        float Shoulder;
        float Fov = FieldOfView;

        if (Ride is not null)
        {
            VehicleDef Def = Ride.Definition;
            Pivot = CEntityLibrary.GetRenderLocation(World, Ride.Owner) + new FVector3(0.0f, Def.CameraHeight, 0.0f);
            Distance = Def.CameraDistance;
            Shoulder = 0.0f;
        }
        else
        {
            // The drawn pose, which interpolation keeps between fixed steps; the simulated pose would stutter.
            Pivot = CEntityLibrary.GetRenderLocation(World, Player.ViewTarget) + new FVector3(0.0f, 0.55f, 0.0f);
            Distance = Player.IsAiming ? AimDistance : FootDistance;
            Shoulder = ShoulderOffset;
            if (Player.IsAiming)
            {
                Fov = Player.ActiveWeapon?.Def.ZoomFov ?? 55.0f;
            }
            else if (Player.IsSprinting)
            {
                Fov = FieldOfView + 6.0f;
            }
        }

        if (!bInitialized)
        {
            SmoothedPivot = Pivot;
            CurrentDistance = Distance;
            bInitialized = true;
        }

        if (Ride is not null)
        {
            SmoothedPivot = FVector3.Lerp(SmoothedPivot, Pivot, Damp(12.0f, DeltaTime));
        }
        else
        {
            SmoothedPivot = new FVector3(Pivot.X, Mathf.Lerp(SmoothedPivot.Y, Pivot.Y, Damp(18.0f, DeltaTime)), Pivot.Z);
        }

        CurrentDistance = Mathf.Lerp(CurrentDistance, Distance, Damp(8.0f, DeltaTime));
        CurrentShoulder = Mathf.Lerp(CurrentShoulder, Shoulder, Damp(8.0f, DeltaTime));
        CurrentFov = Mathf.Lerp(CurrentFov, Fov, Damp(10.0f, DeltaTime));

        FVector3 Forward = Geo.Direction(Yaw, Pitch);
        FVector3 Right = Geo.RightOf(Yaw);
        FVector3 Origin = SmoothedPivot + Right * CurrentShoulder;
        FVector3 Desired = Origin - Forward * CurrentDistance;

        Entity IgnoreA = Player.Owner;
        Entity IgnoreB = Ride?.Owner ?? Entity.Null;
        SRayResult[] Hits = CPhysicsLibrary.SphereCast(World, SmoothedPivot, Desired, 0.25f, IgnoreA);
        foreach (SRayResult Hit in Hits)
        {
            Entity HitEntity = new(Hit.Entity);
            if (HitEntity == IgnoreB || HitEntity == IgnoreA)
            {
                continue;
            }

            IDamageable? Blocker = Mercs.FindDamageable(HitEntity);
            if (Blocker is Soldier || (Blocker is not null && (Blocker.Owner == IgnoreB || Blocker.Owner == IgnoreA)))
            {
                continue;
            }

            if (!Registry.Has<SRigidBodyComponent>(HitEntity) || Registry.Get<SRigidBodyComponent>(HitEntity).BodyType != EBodyType.Dynamic)
            {
                Desired = SmoothedPivot + (Desired - SmoothedPivot).NormalizedOr(-Forward) * MathF.Max(0.4f, Hit.Distance - 0.2f);
                break;
            }
        }

        float Floor = Terrain.HeightAt(Desired.X, Desired.Z) + 0.4f;
        Desired.Y = MathF.Max(Desired.Y, MathF.Max(Floor, Terrain.SeaLevel + 0.3f));

        // A wall that shoves the camera into the merc's head would otherwise fill the screen with the back of it.
        Player.SetBodyHidden(Ride is null && FVector3.Distance(Desired, SmoothedPivot) < HideBodyDistance);

        ViewPosition = Desired;
        ViewForward = Forward;
        World.SetEntityLocation(Entity, Desired);
        World.SetEntityRotation(Entity, FQuat.LookRotation(Forward, FVector3.Up));
        Camera?.SetFOV(CurrentFov);

        ResolveAim(Desired, Forward, IgnoreA, IgnoreB);
    }

    private void ResolveAim(FVector3 From, FVector3 Forward, Entity IgnoreA, Entity IgnoreB)
    {
        FVector3 End = From + Forward * 600.0f;
        SRayResult Hit = Geo.Trace(From + Forward * (CurrentDistance * 0.9f), End, IgnoreA, IgnoreB);
        if (Hit.bHit)
        {
            AimPoint = Hit.Location;
            IDamageable? Target = Mercs.FindDamageable(new Entity(Hit.Entity));
            AimTarget = Target is not null && Target.IsAlive ? Target : null;
            return;
        }

        AimPoint = End;
        AimTarget = null;
        for (float Step = 10.0f; Step < 600.0f; Step += 10.0f)
        {
            FVector3 Probe = From + Forward * Step;
            if (Probe.Y < Terrain.HeightAt(Probe.X, Probe.Z))
            {
                AimPoint = Probe;
                break;
            }
        }
    }

    private static float Damp(float Rate, float DeltaTime) => 1.0f - MathF.Exp(-Rate * DeltaTime);

    public void SnapBehind(float NewYaw)
    {
        Yaw = NewYaw;
        Pitch = -12.0f;
        bInitialized = false;
    }
}
