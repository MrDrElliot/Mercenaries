using System;
using System.Collections.Generic;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

public enum ESoldierState : byte
{
    Guard,
    Patrol,
    Alert,
    Combat,
    Flee,
    Report,
    Subdued,
    Dead,
}

public enum ESoldierRole : byte
{
    Rifleman,
    Gunner,
    RocketTrooper,
    Sniper,
    Officer,
    Civilian,
}

public sealed class Soldier : EntityScript, IDamageable
{
    [Property(Category = "Soldier")]
    public EFaction Team = EFaction.VZ;

    [Property(Category = "Soldier")]
    public ESoldierRole Role = ESoldierRole.Rifleman;

    [Property(Category = "Soldier", Min = 1.0f)]
    public float MaxHealth = 100.0f;

    [Property(Category = "Soldier", Units = "m/s")]
    public float RunSpeed = 5.2f;

    [Property(Category = "Soldier", Units = "m")]
    public float SightRange = 65.0f;

    public bool bIsHvt;
    public string DisplayName = "Soldier";
    public HvtTarget? Hvt;
    public Site? HomeSite;
    public FVector3 Home;
    public float HomeRadius = 18.0f;
    public List<FVector3>? PatrolRoute;

    private ESoldierState State = ESoldierState.Guard;
    private WeaponState Weapon = new(EWeapon.Rifle);
    private IDamageable? Target;
    private FVector3 LastKnown;
    private float LastSeenTime = -100.0f;
    private bool bTargetVisible;
    private float ThinkTimer;
    private float StateTime;
    private float BurstLeft;
    private float BurstPause;
    private float StrafeSign = 1.0f;
    private float StrafeTimer;
    private float GrenadeCooldown = 6.0f;
    private float DetourYaw;
    private float DetourTime;
    private float ProbeTimer;
    private FVector3 StuckAnchor;
    private float StuckTimer;
    private int PatrolIndex;
    private FVector3 WanderGoal;
    private float WanderWait;
    private FVector3 FleeFrom;
    private float Stamina = 3.0f;
    private float LastPlayerOffense = -100.0f;
    private float WalkPhase;
    private float CurrentYaw;
    private bool bDead;
    private bool bSubdued;
    private bool bReady;

    private HumanoidRig? Rig;
    private Entity Body => Rig?.Body ?? Entity.Null;
    private STransformComponent? BodyTransform => Body.IsNull ? null : Registry.TryGet<STransformComponent>(Body);
    private Entity Corpse = Entity.Null;
    // Damage landing in one volley counts as one blow, so a shotgun blast tears a body apart the way a single heavy round does.
    private float VolleyDamage;
    private float VolleyTime;
    private const float VolleyWindow = 0.12f;
    private const float LimbAnimationRange = 90.0f;
    private const float FlinchTime = 0.3f;
    private float FlinchLeft;
    private float FlinchPitch;
    private float FlinchRoll;
    private Entity Marker = Entity.Null;
    private EMarker MarkerKind = EMarker.None;
    // Component wrappers are raw storage pointers that go stale when the storage grows, so they are looked up per use.
    private SCharacterControllerComponent? Controller => Registry.TryGet<SCharacterControllerComponent>(Entity);
    private SCharacterMovementComponent? Movement => Registry.TryGet<SCharacterMovementComponent>(Entity);
    private SHealthComponent? Health => Registry.TryGet<SHealthComponent>(Entity);
    private SPathFollowComponent? Follow => Registry.TryGet<SPathFollowComponent>(Entity);
    private FVector3 FollowGoal;
    private FVector3 AbandonedGoal = new(float.MaxValue, 0.0f, 0.0f);
    private bool bFollowing;
    private bool bMovedThisFrame;
    private int FramesAlive;
    private FVector3 CachedPosition;

    public Entity Owner => Entity;
    public EFaction Faction => Team;
    public bool IsAlive => !bDead && !bSubdued;
    public bool IsPlayerControlled => false;
    public FVector3 Position => CachedPosition;
    public float Radius => 0.5f;
    public bool IsDead => bDead;
    public bool IsSubdued => bSubdued;
    public ESoldierState CurrentState => State;
    public bool IsInCombat => State == ESoldierState.Combat;
    public float HealthFraction => Health?.GetHealthFraction() ?? 0.0f;
    public EWeapon WeaponKind => Weapon.Kind;
    public IDamageable? CurrentTarget => Target;

    public static EWeapon WeaponFor(ESoldierRole Role, EFaction Faction) => Role switch
    {
        ESoldierRole.Gunner => EWeapon.MachineGun,
        ESoldierRole.RocketTrooper => EWeapon.Rocket,
        ESoldierRole.Sniper => EWeapon.Sniper,
        ESoldierRole.Officer => EWeapon.Smg,
        ESoldierRole.Civilian => EWeapon.None,
        _ => Faction switch
        {
            EFaction.Pirate => EWeapon.Shotgun,
            EFaction.Guerrilla => Mercs.Chance(0.3f) ? EWeapon.Smg : EWeapon.Rifle,
            EFaction.Oil => EWeapon.Smg,
            _ => EWeapon.Rifle,
        },
    };

    public override void OnReady()
    {
        CachedPosition = World.GetEntityLocation(Entity);
        if (Home == FVector3.Zero)
        {
            Home = CachedPosition;
        }

        Weapon = new WeaponState(WeaponFor(Role, Team));
        State = Role == ESoldierRole.Civilian ? ESoldierState.Patrol : (PatrolRoute is { Count: > 1 } ? ESoldierState.Patrol : ESoldierState.Guard);
        CurrentYaw = Mercs.Range(0.0f, 360.0f);
        ThinkTimer = Mercs.Range(0.0f, 0.4f);
        WanderGoal = CachedPosition;

        using (new FPhysicsBatchScope(World))
        {
            HumanoidBody.SetupCapsule(Registry, Entity, RunSpeed);
        }

        SPathFollowComponent Path = Registry.GetOrAdd<SPathFollowComponent>(Entity)!;
        Path.AcceptanceRadius = 0.7f;
        Path.RepathInterval = 1.5f;
        Path.RepathDistance = 2.0f;
        SRVOAgentComponent Crowd = Registry.GetOrAdd<SRVOAgentComponent>(Entity)!;
        Crowd.Radius = HumanoidBody.CapsuleRadius + 0.1f;
        Crowd.MaxSpeed = RunSpeed;
        Crowd.bClampToNavMesh = true;

        // The engine scans sight in parallel, and the game only decides which of those it sees are enemies.
        SPerceptionComponent Senses = Registry.GetOrAdd<SPerceptionComponent>(Entity)!;
        Senses.SightRadius = SightRange * VehicleSightScale;
        Senses.LoseSightRadius = SightRange * VehicleSightScale * 1.2f;
        Senses.EyeOffset = new FVector3(0.0f, 0.65f, 0.0f);
        Senses.bHearingEnabled = false;
        Senses.bDamageEnabled = false;
        Senses.ForgetTime = 4.0f;
        Senses.UpdateInterval = 0.25f;
        UpdateSightCone();

        Controller?.AddYaw(CurrentYaw);

        SHealthComponent? Created = Registry.GetOrAdd<SHealthComponent>(Entity);
        if (Created is not null)
        {
            Created.MaxHealth = MaxHealth;
            Created.Health = MaxHealth;
        }

        Rig = HumanoidBody.BuildRig(World, Entity, Team, Weapon.Kind, Role == ESoldierRole.Officer || bIsHvt, bIsHvt ? 1.08f : 1.0f);
        if (bIsHvt)
        {
            SetMarker(EMarker.Hvt);
        }

        Mercs.Soldiers.Add(this);
        Mercs.Register(this);
        bReady = true;
    }

    public override void OnDetach()
    {
        Mercs.Soldiers.Remove(this);
        Mercs.Unregister(this);
        HomeSite?.Forget(this);
    }

    public override void OnUpdate(float DeltaTime)
    {
        if (!bReady || !Mercs.IsRunning)
        {
            return;
        }

        // The dead are wherever their torso fell, which the photo check and the pickups look for.
        CachedPosition = bDead && Registry.Valid(Corpse) ? World.GetEntityLocation(Corpse) : World.GetEntityLocation(Entity);
        HumanoidBody.AnimateMarker(World, Marker, 1.45f);

        if (bDead || bSubdued)
        {
            return;
        }

        // Structure bodies are created in a batch after spawning, so the overlap is only meaningful a few frames in.
        if (++FramesAlive == 10)
        {
            EscapeEmbeddedSpawn();
        }

        StateTime += DeltaTime;
        Weapon.Tick(DeltaTime);
        GrenadeCooldown -= DeltaTime;

        float PlayerDistance = FVector3.Distance(CachedPosition, Mercs.PlayerPosition);
        ThinkTimer -= DeltaTime;
        if (ThinkTimer <= 0.0f)
        {
            ThinkTimer = PlayerDistance < 120.0f ? Mercs.Range(0.2f, 0.35f) : Mercs.Range(0.6f, 1.0f);
            Think();
        }

        switch (State)
        {
            case ESoldierState.Guard:
                TickGuard(DeltaTime);
                break;
            case ESoldierState.Patrol:
                TickPatrol(DeltaTime);
                break;
            case ESoldierState.Alert:
                TickAlert(DeltaTime);
                break;
            case ESoldierState.Combat:
                TickCombat(DeltaTime);
                break;
            case ESoldierState.Flee:
                TickFlee(DeltaTime);
                break;
            case ESoldierState.Report:
                TickReport(DeltaTime);
                break;
        }

        // Any state that stopped traveling this frame hands the controller back rather than leaving the old path driving it.
        if (!bMovedThisFrame)
        {
            StopFollowing();
        }
        bMovedThisFrame = false;

        CheckWater();
        float Speed = Movement is not null ? Geo.Flat(Movement.Velocity).Length : 0.0f;
        // Snaps away from the round at once and eases back, and one frame at zero puts the body upright again.
        float Recoil = FlinchLeft > 0.0f ? (FlinchLeft / FlinchTime) * (FlinchLeft / FlinchTime) : 0.0f;
        if (FlinchLeft > 0.0f)
        {
            FlinchLeft -= DeltaTime;
            WalkPhase = FlinchLeft <= 0.0f && WalkPhase == 0.0f ? 1e-4f : WalkPhase;
        }
        HumanoidBody.AnimateWalk(BodyTransform, Speed, ref WalkPhase, DeltaTime, FlinchPitch * Recoil, FlinchRoll * Recoil);
        if (FVector3.DistanceSquared(CachedPosition, Sfx.Listener) < LimbAnimationRange * LimbAnimationRange)
        {
            HumanoidBody.AnimateLimbs(Registry, Rig, Speed, WalkPhase, Weapon.Kind != EWeapon.None);
        }
    }

    private void CheckWater()
    {
        if (CachedPosition.Y < Terrain.SeaLevel - 1.1f)
        {
            Health?.ApplyDamage(20.0f * World.DeltaTime, Entity.Null);
            if (Health is not null && Health.Health <= 0.0f)
            {
                Die(new FHit { Kind = EDamageKind.Crush, Direction = FVector3.Up });
            }
        }
    }

    private void SetState(ESoldierState NewState)
    {
        if (State == NewState)
        {
            return;
        }

        State = NewState;
        UpdateSightCone();
        StateTime = 0.0f;
        StuckTimer = 0.0f;
        StuckAnchor = CachedPosition;
    }

    private void Think()
    {
        if (Role == ESoldierRole.Civilian)
        {
            return;
        }

        if (State == ESoldierState.Report)
        {
            return;
        }

        IDamageable? Seen = Perceive();
        if (Seen is not null)
        {
            if (Target != Seen && State != ESoldierState.Combat)
            {
                HomeSite?.AlertSquad(Seen, this);
            }

            Target = Seen;
            LastKnown = Seen.Position;
            LastSeenTime = Mercs.Time;
            bTargetVisible = true;

            if (bIsHvt && (FVector3.Distance(Seen.Position, CachedPosition) < 28.0f || HealthFraction < 0.6f) && Seen.IsPlayerControlled)
            {
                FleeFrom = Seen.Position;
                SetState(ESoldierState.Flee);
                return;
            }

            if (State != ESoldierState.Combat)
            {
                BurstPause = Mercs.Range(0.6f, 1.3f);
            }

            SetState(ESoldierState.Combat);
            return;
        }

        bTargetVisible = false;
        if (Target is not null && (!Target.IsAlive || !Mercs.Factions.IsHostile(Team, Target)))
        {
            Target = null;
            SetState(PatrolRoute is { Count: > 1 } ? ESoldierState.Patrol : ESoldierState.Guard);
            return;
        }

        if (State == ESoldierState.Combat && Mercs.Time - LastSeenTime > 4.0f)
        {
            SetState(ESoldierState.Alert);
        }
    }

    // Vehicles are spotted from further off, so the engine senses to that range and infantry is trimmed back here.
    private const float VehicleSightScale = 1.4f;
    private const float AlertedSightScale = 1.35f;

    // An alerted soldier watches every direction, as the old check did by skipping the facing test.
    private void UpdateSightCone()
    {
        if (Registry.TryGet<SPerceptionComponent>(Entity) is { } Senses)
        {
            Senses.SightFOVDegrees = State is ESoldierState.Alert or ESoldierState.Combat ? 360.0f : 140.0f;
        }
    }

    private IDamageable? Perceive()
    {
        bool bAlerted = State is ESoldierState.Alert or ESoldierState.Combat;
        float Range = bAlerted ? SightRange * AlertedSightScale : SightRange;

        IDamageable? Best = null;
        float BestScore = float.MaxValue;
        foreach (Entity Seen in CPerceptionLibrary.GetPerceivedTargets(World, Entity))
        {
            if (!CPerceptionLibrary.CanSense(World, Entity, Seen, EAISenseChannel.Sight) || Mercs.FindDamageable(Seen) is not { } Candidate)
            {
                continue;
            }

            // A driving player is fought as the vehicle, which is what the engine sees around him anyway.
            if (Candidate is MercPlayer { CurrentVehicle: { } Driven })
            {
                Candidate = Driven;
            }

            if (!Candidate.IsAlive || Candidate == this)
            {
                continue;
            }

            float Bias;
            if (Candidate.IsPlayerControlled)
            {
                Bias = 0.8f;
                if (!Mercs.Factions.IsHostile(Team, Candidate))
                {
                    continue;
                }
            }
            else if (Candidate is Soldier Other)
            {
                Bias = 1.0f;
                if (Other.Team == Team || !FactionSystem.AtWar(Team, Other.Team))
                {
                    continue;
                }
            }
            else if (Candidate is Vehicle Ride)
            {
                Bias = 1.2f;
                if (!Ride.IsCrewed || !FactionSystem.AtWar(Team, Ride.Faction))
                {
                    continue;
                }
            }
            else
            {
                continue;
            }

            float Distance = FVector3.Distance(Candidate.Position, CachedPosition);
            if (Distance > Range * (Candidate is Vehicle ? VehicleSightScale : 1.0f) || Distance * Bias >= BestScore)
            {
                continue;
            }

            BestScore = Distance * Bias;
            Best = Candidate;
        }

        return Best;
    }

    public void HearNoise(FVector3 Where, IDamageable Source)
    {
        if (bDead || bSubdued || Source.Owner == Entity)
        {
            return;
        }

        if (Role == ESoldierRole.Civilian)
        {
            FleeFrom = Where;
            SetState(ESoldierState.Flee);
            return;
        }

        if (State is ESoldierState.Combat or ESoldierState.Report or ESoldierState.Flee)
        {
            return;
        }

        if (Mercs.Factions.IsHostile(Team, Source))
        {
            LastKnown = Source.Position;
            Target = Source;
        }
        else
        {
            LastKnown = Where;
        }

        LastSeenTime = Mercs.Time - 2.0f;
        SetState(ESoldierState.Alert);
    }

    public void OnSquadAlert(IDamageable Threat)
    {
        if (bDead || bSubdued || Role == ESoldierRole.Civilian || State is ESoldierState.Combat or ESoldierState.Report or ESoldierState.Flee)
        {
            return;
        }

        Target = Threat;
        LastKnown = Threat.Position;
        LastSeenTime = Mercs.Time - 1.0f;
        SetState(ESoldierState.Alert);
    }

    private void TickGuard(float DeltaTime)
    {
        WanderWait -= DeltaTime;
        float GoalDistance = Geo.FlatDistance(CachedPosition, WanderGoal);
        if (GoalDistance > 1.5f && MoveToward(WanderGoal, 0.35f, DeltaTime))
        {
            return;
        }

        if (WanderWait <= 0.0f)
        {
            WanderWait = Mercs.Range(4.0f, 10.0f);
            WanderGoal = Geo.RandomAround(Home, 0.0f, HomeRadius * 0.6f);
        }
        else
        {
            TurnToward(CurrentYaw + MathF.Sin(Mercs.Time * 0.4f + Entity.Id) * 20.0f * DeltaTime, 30.0f, DeltaTime);
        }
    }

    private void TickPatrol(float DeltaTime)
    {
        if (PatrolRoute is not { Count: > 1 })
        {
            if (Role == ESoldierRole.Civilian)
            {
                TickGuard(DeltaTime);
                return;
            }

            SetState(ESoldierState.Guard);
            return;
        }

        FVector3 Goal = PatrolRoute[PatrolIndex % PatrolRoute.Count];
        if (Geo.FlatDistance(CachedPosition, Goal) < 2.5f || !MoveToward(Goal, 0.4f, DeltaTime))
        {
            PatrolIndex = (PatrolIndex + 1) % PatrolRoute.Count;
        }
    }

    private void TickAlert(float DeltaTime)
    {
        if (StateTime > 14.0f)
        {
            Target = null;
            SetState(PatrolRoute is { Count: > 1 } ? ESoldierState.Patrol : ESoldierState.Guard);
            return;
        }

        bool bSearching = Geo.FlatDistance(CachedPosition, LastKnown) > 4.0f && MoveToward(LastKnown, 0.8f, DeltaTime);
        if (!bSearching)
        {
            TurnToward(CurrentYaw + 90.0f, 60.0f, DeltaTime);
        }
    }

    private float PreferredRange => Role switch
    {
        ESoldierRole.Gunner => 26.0f,
        ESoldierRole.RocketTrooper => 38.0f,
        ESoldierRole.Sniper => 70.0f,
        ESoldierRole.Officer => 16.0f,
        _ => Team == EFaction.Pirate ? 12.0f : 20.0f,
    };

    private void TickCombat(float DeltaTime)
    {
        if (Target is null || !Target.IsAlive)
        {
            Target = null;
            SetState(ESoldierState.Alert);
            return;
        }

        FVector3 Aim = Target.Position;
        FVector3 Offset = Aim - CachedPosition;
        float Distance = Geo.Flat(Offset).Length;
        float DesiredYaw = Geo.YawOf(Offset);
        TurnToward(DesiredYaw, 420.0f, DeltaTime);

        if (!bTargetVisible)
        {
            MoveToward(LastKnown, 1.0f, DeltaTime);
            return;
        }

        float Preferred = PreferredRange;
        StrafeTimer -= DeltaTime;
        if (StrafeTimer <= 0.0f)
        {
            StrafeTimer = Mercs.Range(1.5f, 3.5f);
            StrafeSign = Mercs.Chance(0.5f) ? 1.0f : -1.0f;
        }

        float Forward = Distance > Preferred + 6.0f ? 1.0f : (Distance < Preferred - 8.0f ? -0.7f : 0.0f);
        float Strafe = Role == ESoldierRole.Sniper ? 0.0f : StrafeSign * 0.6f;
        FVector3 Local = new(Strafe, 0.0f, Forward);
        if (Local.LengthSquared > 0.01f)
        {
            FVector3 WorldMove = Geo.Heading(CurrentYaw) * Forward + Geo.RightOf(CurrentYaw) * Strafe;
            if (!IsBlocked(WorldMove.NormalizedOr(FVector3.Forward)))
            {
                Controller?.AddMovementInput(Local);
            }
            else
            {
                StrafeSign = -StrafeSign;
            }
        }

        float Facing = MathF.Abs(Mathf.DeltaAngleDegrees(CurrentYaw, DesiredYaw));
        if (Facing < 20.0f && Weapon.Kind != EWeapon.None)
        {
            TryShoot(DeltaTime, Distance);
        }

        if (GrenadeCooldown <= 0.0f && Distance > 9.0f && Distance < 30.0f && Role is ESoldierRole.Rifleman or ESoldierRole.Officer)
        {
            GrenadeCooldown = Mercs.Range(9.0f, 16.0f);
            if (Mercs.Chance(0.45f))
            {
                FVector3 From = CachedPosition + new FVector3(0.0f, 0.8f, 0.0f);
                Throwables.Throw(EThrowable.Grenade, From, Throwables.LobVelocity(From, Aim, 16.0f), this);
            }
        }
    }

    private void TryShoot(float DeltaTime, float Distance)
    {
        if (Weapon.Magazine <= 0)
        {
            if (!Weapon.IsReloading)
            {
                Weapon.Reserve = Math.Max(Weapon.Reserve, Weapon.Def.Magazine * 2);
                Weapon.BeginReload();
            }

            return;
        }

        if (BurstPause > 0.0f)
        {
            BurstPause -= DeltaTime;
            return;
        }

        if (!Weapon.CanFire || Target is null)
        {
            return;
        }

        if (Weapon.Kind == EWeapon.Rocket && Target is Soldier && Distance < 12.0f)
        {
            return;
        }

        if (BurstLeft <= 0.0f)
        {
            BurstLeft = Weapon.Def.bAutomatic ? Mercs.RangeInt(3, 8) : Mercs.RangeInt(1, 3);
        }

        FVector3 Muzzle = CachedPosition + Geo.Heading(CurrentYaw) * 0.5f + new FVector3(0.0f, 0.35f, 0.0f);
        FVector3 AimPoint = Target.Position + new FVector3(0.0f, Target is Vehicle ? 0.8f : 0.25f, 0.0f);
        if (Target is MercPlayer Player && Player.Velocity.LengthSquared > 1.0f)
        {
            AimPoint += Player.Velocity * (Weapon.Def.bProjectile ? Distance / MathF.Max(Weapon.Def.ProjectileSpeed, 1.0f) : 0.05f);
        }

        float Inaccuracy = 4.0f + Distance * 0.06f;
        if (Target is MercPlayer Moving)
        {
            Inaccuracy += MathF.Min(Moving.Velocity.Length * 0.35f, 3.0f);
        }

        Weapon.Consume();
        BurstLeft -= 1.0f;
        if (BurstLeft <= 0.0f)
        {
            BurstPause = Mercs.Range(0.5f, 1.4f) * (Role == ESoldierRole.Sniper ? 3.0f : 1.0f);
        }

        Weapons.Fire(this, Weapon.Def, Muzzle, (AimPoint - Muzzle).NormalizedOr(FVector3.Forward), Entity.Null, Inaccuracy);
    }

    private void TickFlee(float DeltaTime)
    {
        FVector3 Away = Geo.Flat(CachedPosition - FleeFrom).NormalizedOr(Geo.Heading(CurrentYaw));
        MoveToward(CachedPosition + Away * 10.0f, 1.0f, DeltaTime);
        if (StateTime > (bIsHvt ? 12.0f : 8.0f))
        {
            SetState(Role == ESoldierRole.Civilian ? ESoldierState.Patrol : ESoldierState.Alert);
            if (bIsHvt && Mercs.Player is { } Player && FVector3.Distance(Player.Position, CachedPosition) < 25.0f)
            {
                FleeFrom = Player.Position;
                SetState(ESoldierState.Flee);
            }
        }
    }

    public void BeginReporting()
    {
        if (bDead || bSubdued)
        {
            return;
        }

        FleeFrom = Mercs.PlayerPosition;
        SetState(ESoldierState.Report);
        SetMarker(EMarker.Witness);
    }

    public void EndReporting()
    {
        SetMarker(bIsHvt ? EMarker.Hvt : EMarker.None);
        if (!bDead && !bSubdued)
        {
            SetState(ESoldierState.Alert);
        }
    }

    private void TickReport(float DeltaTime)
    {
        if (StateTime < 3.0f)
        {
            FVector3 Away = Geo.Flat(CachedPosition - FleeFrom).NormalizedOr(Geo.Heading(CurrentYaw));
            MoveToward(CachedPosition + Away * 8.0f, 1.0f, DeltaTime);
            return;
        }

        TurnToward(Geo.YawOf(FleeFrom - CachedPosition) + 180.0f, 90.0f, DeltaTime);
    }

    private void TurnToward(float Yaw, float DegreesPerSecond, float DeltaTime)
    {
        float Delta = Mathf.DeltaAngleDegrees(CurrentYaw, Yaw);
        float Step = Math.Clamp(Delta, -DegreesPerSecond * DeltaTime, DegreesPerSecond * DeltaTime);
        CurrentYaw += Step;
        Controller?.AddYaw(Step);
    }

    private bool IsBlocked(FVector3 Direction)
    {
        FVector3 From = CachedPosition + new FVector3(0.0f, 0.2f, 0.0f);
        FVector3 Ahead = From + Direction * 3.0f;
        if (!Terrain.IsLand(Ahead.X, Ahead.Z) && Terrain.IsLand(CachedPosition.X, CachedPosition.Z))
        {
            return true;
        }

        SRayResult Hit = Geo.Trace(From, Ahead, Entity, Entity.Null);
        return Hit.bHit && Hit.Normal.Y < 0.6f && Mercs.FindDamageable(new Entity(Hit.Entity)) is not (Soldier or MercPlayer);
    }

    // False once the soldier has got as close as the navmesh allows, or given up on a goal it kept getting stuck short of.
    private bool MoveToward(FVector3 Goal, float Throttle, float DeltaTime)
    {
        if (Geo.Flat(Goal - CachedPosition).LengthSquared < 0.25f || Geo.FlatDistance(Goal, AbandonedGoal) < 1.5f)
        {
            return false;
        }

        if (Mercs.bInfantryNavReady && Follow is { } Path)
        {
            if (!bFollowing || Geo.FlatDistance(FollowGoal, Goal) > 1.5f)
            {
                Path.SetTargetLocation(Goal);
                FollowGoal = Goal;
                bFollowing = true;
            }

            Path.Speed = Throttle;
            if (Path.IsStuck())
            {
                AbandonedGoal = Goal;
                StopFollowing();
                return false;
            }

            // A consumed path that still reads failed ended at the nearest reachable point, which is as close as this goal gets.
            if (Path.IsAtDestination() || (Path.DidPathFindingFail() && Path.IsFollowing()))
            {
                StopFollowing();
                return false;
            }

            if (Path.IsFollowing() || !Path.DidPathFindingFail())
            {
                bMovedThisFrame = true;
                FVector3 Ahead = Geo.Flat(Path.GetNextCorner() - CachedPosition);
                if (Ahead.LengthSquared > 0.01f)
                {
                    TurnToward(Geo.YawOf(Ahead), 360.0f, DeltaTime);
                }
                return true;
            }
        }

        StopFollowing();
        bMovedThisFrame = true;
        MoveDirect(Goal, Throttle, DeltaTime);
        return true;
    }

    // A spawn point inside a building's footprint embeds the capsule in its collider, where no movement can ever free it.
    private void EscapeEmbeddedSpawn()
    {
        foreach (Entity Hit in CPhysicsLibrary.OverlapSphere(World, CachedPosition, HumanoidBody.CapsuleRadius, Entity))
        {
            if (Mercs.FindDamageable(Hit) is not Structure Building)
            {
                continue;
            }

            FVector3 Away = Geo.Flat(CachedPosition - Building.Position).NormalizedOr(FVector3.Right);
            for (float Distance = Building.Radius * 0.5f; Distance < Building.Radius * 1.5f + 3.0f; Distance += 0.5f)
            {
                FVector3 Candidate = Geo.Ground(Building.Position + Away * Distance) + new FVector3(0.0f, HumanoidBody.FeetOffset + 0.1f, 0.0f);
                if (!Array.Exists(CPhysicsLibrary.OverlapSphere(World, Candidate, HumanoidBody.CapsuleRadius, Entity), Other => Mercs.FindDamageable(Other) is Structure))
                {
                    Controller?.TeleportTo(Candidate);
                    CachedPosition = Candidate;
                    return;
                }
            }
        }
    }

    private void StopFollowing()
    {
        if (bFollowing)
        {
            Follow?.Stop();
            bFollowing = false;
        }
    }

    private void MoveDirect(FVector3 Goal, float Throttle, float DeltaTime)
    {
        FVector3 Offset = Geo.Flat(Goal - CachedPosition);

        float DesiredYaw = Geo.YawOf(Offset);
        DetourTime -= DeltaTime;
        if (DetourTime > 0.0f)
        {
            DesiredYaw = DetourYaw;
        }
        else
        {
            ProbeTimer -= DeltaTime;
            if (ProbeTimer <= 0.0f)
            {
                ProbeTimer = 0.25f;
                if (IsBlocked(Geo.Heading(DesiredYaw)))
                {
                    foreach (float Offset2 in new[] { 45.0f, -45.0f, 90.0f, -90.0f, 135.0f, -135.0f })
                    {
                        if (!IsBlocked(Geo.Heading(DesiredYaw + Offset2)))
                        {
                            DetourYaw = DesiredYaw + Offset2;
                            DetourTime = 0.9f;
                            DesiredYaw = DetourYaw;
                            break;
                        }
                    }
                }
            }
        }

        TurnToward(DesiredYaw, 360.0f, DeltaTime);
        float Alignment = Mathf.Clamp01(1.0f - MathF.Abs(Mathf.DeltaAngleDegrees(CurrentYaw, DesiredYaw)) / 90.0f);
        Controller?.AddMovementInput(new FVector3(0.0f, 0.0f, Throttle * MathF.Max(0.25f, Alignment)));

        StuckTimer += DeltaTime;
        if (StuckTimer > 1.0f)
        {
            if (Geo.FlatDistance(StuckAnchor, CachedPosition) < 0.6f)
            {
                DetourYaw = CurrentYaw + Mercs.Range(100.0f, 260.0f);
                DetourTime = 1.2f;
                Controller?.Jump();
            }

            StuckTimer = 0.0f;
            StuckAnchor = CachedPosition;
        }
    }

    private void SetMarker(EMarker Kind)
    {
        if (Kind == MarkerKind)
        {
            return;
        }

        if (!Marker.IsNull && World.IsValidEntity(Marker))
        {
            World.DestroyEntity(Marker);
        }

        Marker = Entity.Null;
        MarkerKind = Kind;
        if (Kind != EMarker.None)
        {
            Marker = HumanoidBody.BuildMarker(World, Entity, Kind, 1.45f);
        }
    }

    public void TakeHit(in FHit Hit)
    {
        if (bDead || Health is null)
        {
            return;
        }

        if (bSubdued)
        {
            if (Hit.Kind != EDamageKind.Melee)
            {
                Health.ApplyDamage(Hit.Amount, Hit.Source);
                if (Health.Health <= 0.0f)
                {
                    Die(Hit);
                }
            }

            return;
        }

        if (bIsHvt && Hit.Kind == EDamageKind.Melee && Hit.bByPlayer)
        {
            Stamina -= 1.0f;
            Health.ApplyDamage(Hit.Amount * 0.25f, Hit.Source);
            Controller?.Launch(Hit.Direction * 3.0f + new FVector3(0.0f, 2.0f, 0.0f), true, true);
            if (Stamina <= 0.0f && HealthFraction > 0.0f)
            {
                Subdue();
            }

            return;
        }

        VolleyDamage = Mercs.Time - VolleyTime <= VolleyWindow ? VolleyDamage + Hit.Amount : Hit.Amount;
        VolleyTime = Mercs.Time;
        if (Hit.Kind == EDamageKind.Explosive)
        {
            Mercs.Fx.BloodSpray(CachedPosition, (Hit.Direction + FVector3.Up).NormalizedOr(FVector3.Up), 12, 2.0f, 5.0f, 0.8f);
        }

        float Remaining = Health.ApplyDamage(Hit.Amount, Hit.Source);

        if (Hit.bByPlayer && Mercs.Time - LastPlayerOffense > 4.0f && Remaining > 0.0f)
        {
            LastPlayerOffense = Mercs.Time;
            Mercs.Factions.Offense(Team, 2.0f, CachedPosition, $"attacked {Mercs.Factions.Get(Team).Short}");
        }

        if (Hit.Kind == EDamageKind.Explosive)
        {
            Controller?.Launch(Hit.Direction * 7.0f + new FVector3(0.0f, 5.0f, 0.0f), true, true);
        }

        if (Remaining <= 0.0f)
        {
            Die(Hit);
            return;
        }

        if (Hit.Kind is EDamageKind.Bullet or EDamageKind.Melee)
        {
            float Kick = Math.Clamp(Hit.Amount * 0.6f, 6.0f, 18.0f);
            FlinchPitch = FVector3.Dot(Hit.Direction, Geo.Heading(CurrentYaw)) * Kick;
            FlinchRoll = -FVector3.Dot(Hit.Direction, Geo.RightOf(CurrentYaw)) * Kick;
            FlinchLeft = FlinchTime;
        }

        if (Role == ESoldierRole.Civilian)
        {
            FleeFrom = Hit.Point - Hit.Direction * 5.0f;
            SetState(ESoldierState.Flee);
            return;
        }

        IDamageable? Attacker = Mercs.FindDamageable(Hit.Source);
        if (Hit.bByPlayer && Mercs.Player is { } Player)
        {
            Attacker = Player.CurrentVehicle is { } Ride ? Ride : Player;
        }

        if (Attacker is not null && Attacker.IsAlive && State != ESoldierState.Report && Mercs.Factions.IsHostile(Team, Attacker))
        {
            Target = Attacker;
            LastKnown = Attacker.Position;
            LastSeenTime = Mercs.Time;
            if (bIsHvt)
            {
                FleeFrom = Attacker.Position;
                SetState(ESoldierState.Flee);
            }
            else if (State != ESoldierState.Combat)
            {
                SetState(ESoldierState.Combat);
                HomeSite?.AlertSquad(Attacker, this);
            }
        }
    }

    private void Subdue()
    {
        bSubdued = true;
        State = ESoldierState.Subdued;
        HumanoidBody.RemoveCapsule(Registry, Entity);
        HumanoidBody.PoseKneel(BodyTransform);
        SetMarker(EMarker.Subdued);
        Mercs.Feed.Announce($"{DisplayName} SUBDUED", "Press E next to them to call in the extraction chopper.", 4.0f);
        Mercs.Contracts.OnHvtSubdued(this);
    }

    public void MarkExtracted()
    {
        bDead = true;
        Mercs.Unregister(this);
        World.SetLifetime(Entity, 0.05f);
    }

    public void Die(FHit Hit)
    {
        if (bDead)
        {
            return;
        }

        bDead = true;
        bSubdued = false;
        State = ESoldierState.Dead;
        SetMarker(EMarker.None);
        FVector3 Carry = Movement is { } Moving ? Moving.Velocity : FVector3.Zero;
        HumanoidBody.RemoveCapsule(Registry, Entity);

        if (Hit.bByPlayer)
        {
            Mercs.Factions.Offense(Team, Team == EFaction.Civilian ? 0.0f : 8.0f, CachedPosition, $"killed {Mercs.Factions.Get(Team).Short} soldier");
        }

        FVector3 Ground = Geo.Ground(CachedPosition) + new FVector3(0.0f, 0.3f, 0.0f);
        bool bDropsWeapon = Weapon.Kind != EWeapon.None && Mercs.Chance(Weapon.Kind is EWeapon.Rocket or EWeapon.Sniper or EWeapon.MachineGun ? 0.8f : 0.4f);
        if (bDropsWeapon)
        {
            Pickup.SpawnWeapon(Ground + new FVector3(0.6f, 0.0f, 0.3f), Weapon.Kind);
        }

        float Lifetime = bIsHvt ? 600.0f : 25.0f;
        if (Rig is not null)
        {
            Corpse = Gore.Kill(Rig, Hit, Carry, Lifetime, Mercs.Time - VolleyTime <= VolleyWindow ? VolleyDamage : 0.0f, !bDropsWeapon);
        }

        if (Team != EFaction.Civilian && Mercs.Chance(0.35f))
        {
            Pickup.SpawnCash(Ground + new FVector3(-0.4f, 0.0f, -0.3f), Mercs.RangeInt(2, 9) * 250);
        }

        if (Mercs.Chance(0.12f))
        {
            Pickup.Spawn(EPickupKind.Grenades, Ground + new FVector3(0.0f, 0.0f, 0.7f), 2);
        }

        HomeSite?.OnMemberDied(this);
        Mercs.Contracts.OnSoldierKilled(this, Hit);
        World.SetLifetime(Entity, bIsHvt ? 600.0f : 25.0f);
    }
}
