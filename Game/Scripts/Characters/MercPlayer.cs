using System;
using System.Collections.Generic;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

public enum EMercenary : byte
{
    Mattias,
    Jennifer,
    Chris,
}

public sealed class HijackSession
{
    public Vehicle Target = null!;
    public readonly List<EKey> Sequence = new();
    public int Step;
    public float TimeLeft;
    public float StepTime;
    public bool bGrapple;
    public float Intro;

    public EKey Current => Step < Sequence.Count ? Sequence[Step] : EKey.Space;

    public static string KeyName(EKey Key) => Key switch
    {
        EKey.Space => "SPACE",
        _ => Key.ToString(),
    };
}

public sealed class MercPlayer : EntityScript, IDamageable
{
    [Property(Category = "Mercenary")]
    public EMercenary Merc = EMercenary.Mattias;

    [Property(Category = "Mercenary", Units = "m/s")]
    public float RunSpeed = 6.2f;

    [Property(Category = "Mercenary", Units = "m/s")]
    public float SprintSpeed = 9.2f;

    [Property(Category = "Mercenary")]
    public float MaxHealth = 200.0f;

    public readonly WeaponState[] Slots = { new(EWeapon.Rifle), new(EWeapon.Shotgun) };
    public int ActiveSlot;
    public int Grenades = 4;
    public int Charges = 3;
    public int MaxGrenades = 6;
    public int MaxCharges = 5;

    public bool IsAiming { get; private set; }
    public bool IsSprinting { get; private set; }
    public Vehicle? CurrentVehicle { get; private set; }
    public HijackSession? Hijack { get; private set; }
    public string InteractPrompt { get; private set; } = string.Empty;
    public float RespawnTimer { get; private set; }
    public FVector3 Velocity { get; private set; }
    public float HitMarkerTime;

    // Component wrappers are raw storage pointers that go stale when the storage grows, so they are looked up per use.
    private SCharacterControllerComponent? Controller => Registry.TryGet<SCharacterControllerComponent>(Entity);
    private SCharacterMovementComponent? Movement => Registry.TryGet<SCharacterMovementComponent>(Entity);
    private SHealthComponent? Health => Registry.TryGet<SHealthComponent>(Entity);
    private Entity Body = Entity.Null;
    private STransformComponent? BodyTransform => Body.IsNull ? null : Registry.TryGet<STransformComponent>(Body);
    private FVector3 CachedPosition;
    private FVector3 LastPosition;
    private float CurrentYaw;
    private float CombatStance;
    private float MeleeCooldown;
    private float ThrowCooldown;
    private float WalkPhase;
    private float StrideLeft;
    private bool bWasAirborne;
    private float FallSpeed;
    private bool bDead;
    private bool bReady;
    private Action? PendingInteract;

    public Entity Owner => Entity;
    public EFaction Faction => EFaction.Merc;
    public bool IsAlive => !bDead && bReady;
    public bool IsPlayerControlled => true;
    public FVector3 Position => CachedPosition;
    public float Radius => 0.5f;
    public WeaponState? ActiveWeapon => Slots[ActiveSlot];
    public float HealthFraction => Health?.GetHealthFraction() ?? 0.0f;
    public float HealthValue => Health?.Health ?? 0.0f;
    public bool IsDead => bDead;

    public static string MercName(EMercenary Merc) => Merc switch
    {
        EMercenary.Jennifer => "Jennifer Mui",
        EMercenary.Chris => "Chris Jacobs",
        _ => "Mattias Nilsson",
    };

    public override void OnReady()
    {
        switch (Merc)
        {
            case EMercenary.Jennifer:
                SprintSpeed = 10.6f;
                MaxHealth = 170.0f;
                break;
            case EMercenary.Chris:
                MaxGrenades = 9;
                MaxCharges = 8;
                Grenades = 6;
                Charges = 5;
                break;
        }

        CachedPosition = World.GetEntityLocation(Entity);
        LastPosition = CachedPosition;
        using (new FPhysicsBatchScope(World))
        {
            HumanoidBody.SetupCapsule(Registry, Entity, RunSpeed);
        }

        SHealthComponent? Created = Registry.GetOrAdd<SHealthComponent>(Entity);
        if (Created is not null)
        {
            Created.MaxHealth = MaxHealth;
            Created.Health = MaxHealth;
            Created.RegenPerSecond = Merc == EMercenary.Mattias ? 16.0f : 9.0f;
            Created.RegenDelay = 4.0f;
        }

        Body = HumanoidBody.Build(World, Entity, EFaction.Merc, EWeapon.Rifle, false);
        CEntityLibrary.SetTag(World, Entity, "Player");

        Mercs.Player = this;
        Mercs.Register(this);
        bReady = true;
    }

    public override void OnDetach()
    {
        Mercs.Unregister(this);
        if (Mercs.Player == this)
        {
            Mercs.Player = null;
        }
    }

    public override void OnUpdate(float DeltaTime)
    {
        if (!bReady || !Mercs.IsRunning)
        {
            return;
        }

        CachedPosition = CurrentVehicle is not null ? CurrentVehicle.Position : World.GetEntityLocation(Entity);
        Velocity = DeltaTime > 0.0f ? (CachedPosition - LastPosition) / DeltaTime : FVector3.Zero;
        LastPosition = CachedPosition;
        HitMarkerTime -= DeltaTime;

        if (bDead)
        {
            TickRespawn(DeltaTime);
            return;
        }

        foreach (WeaponState Weapon in Slots)
        {
            Weapon.Tick(DeltaTime);
        }

        MeleeCooldown -= DeltaTime;
        ThrowCooldown -= DeltaTime;
        CombatStance -= DeltaTime;

        if (Hijack is not null)
        {
            TickHijack(DeltaTime);
            return;
        }

        if (CurrentVehicle is not null)
        {
            TickVehicle(DeltaTime);
        }
        else
        {
            TickOnFoot(DeltaTime);
        }

        UpdateInteraction();
        if (Controls.Pressed(EControl.Interact))
        {
            PendingInteract?.Invoke();
        }

        if (Controls.Pressed(EControl.CycleSupport))
        {
            Mercs.Support.CycleSelection();
        }

        if (Controls.Pressed(EControl.Detonate))
        {
            Mercs.Throwables.DetonateCharges(this);
        }

        if (CachedPosition.Y < Terrain.SeaLevel - 1.4f && CurrentVehicle is null)
        {
            Health?.ApplyDamage(12.0f * DeltaTime, Entity.Null);
            if (Health is not null && Health.Health <= 0.0f)
            {
                Die();
            }
        }
    }

    private void TickOnFoot(float DeltaTime)
    {
        MercCamera? Camera = MercCamera.Instance;
        float CameraYaw = Camera?.Yaw ?? CurrentYaw;
        FVector2 Move = Controls.Move();
        IsAiming = Controls.Down(EControl.Aim);
        bool bFiring = Controls.Down(EControl.Fire);
        IsSprinting = Controls.Down(EControl.Sprint) && Move.Y > 0.1f && !IsAiming && !bFiring;

        if (IsAiming || bFiring)
        {
            CombatStance = 1.2f;
        }

        if (Movement is not null)
        {
            Movement.MoveSpeed = IsSprinting ? SprintSpeed : (IsAiming ? RunSpeed * 0.55f : RunSpeed);
        }

        FVector3 WorldMove = Geo.Heading(CameraYaw) * Move.Y + Geo.RightOf(CameraYaw) * Move.X;
        float Magnitude = MathF.Min(WorldMove.Length, 1.0f);

        if (CombatStance > 0.0f)
        {
            TurnToward(CameraYaw, 1080.0f, DeltaTime);
            if (Magnitude > 0.05f)
            {
                FVector3 Direction = WorldMove.Normalized();
                Controller?.AddMovementInput(new FVector3(FVector3.Dot(Direction, Geo.RightOf(CurrentYaw)), 0.0f, FVector3.Dot(Direction, Geo.Heading(CurrentYaw))) * Magnitude);
            }
        }
        else if (Magnitude > 0.05f)
        {
            float DesiredYaw = Geo.YawOf(WorldMove);
            TurnToward(DesiredYaw, 720.0f, DeltaTime);
            float Alignment = Mathf.Clamp01(1.0f - MathF.Abs(Mathf.DeltaAngleDegrees(CurrentYaw, DesiredYaw)) / 120.0f);
            Controller?.AddMovementInput(new FVector3(0.0f, 0.0f, Magnitude * MathF.Max(0.3f, Alignment)));
        }

        if (Controls.Pressed(EControl.Jump))
        {
            Controller?.Jump();
        }

        TickWeapons(bFiring);

        if (Controls.Pressed(EControl.Grenade))
        {
            ThrowGrenade();
        }

        if (Controls.Pressed(EControl.PlaceCharge))
        {
            PlaceCharge();
        }

        if (Controls.Pressed(EControl.Melee))
        {
            Melee();
        }

        if (Controls.Pressed(EControl.CallSupport))
        {
            CallSupport();
        }

        float Speed = Movement is not null ? Geo.Flat(Movement.Velocity).Length : 0.0f;
        HumanoidBody.AnimateWalk(BodyTransform, Speed, ref WalkPhase, DeltaTime);
        TickFootsteps(Speed, DeltaTime);

    }

    private void TickFootsteps(float Speed, float DeltaTime)
    {
        SCharacterMovementComponent? Mover = Movement;
        bool bGrounded = Mover is null || Mover.bGrounded;
        if (!bGrounded)
        {
            bWasAirborne = true;
            FallSpeed = MathF.Max(FallSpeed, Mover is not null ? -Mover.Velocity.Y : 0.0f);
            return;
        }

        if (bWasAirborne)
        {
            bWasAirborne = false;
            if (FallSpeed > 3.0f)
            {
                Sfx.At(ESfx.Land, CachedPosition, Mathf.Clamp01(FallSpeed / 10.0f) * 0.7f, 30.0f, 2.0f);
                StrideLeft = 0.6f;
            }

            FallSpeed = 0.0f;
        }

        if (Speed < 0.8f)
        {
            StrideLeft = MathF.Min(StrideLeft, 0.3f);
            return;
        }

        StrideLeft -= Speed * DeltaTime;
        if (StrideLeft <= 0.0f)
        {
            StrideLeft = IsSprinting ? 2.3f : 1.8f;
            Sfx.At(ESfx.Footstep, CachedPosition - new FVector3(0.0f, 0.9f, 0.0f), IsSprinting ? 0.4f : 0.28f, 30.0f, 2.0f, 0.12f);
        }
    }

    private void TurnToward(float Yaw, float DegreesPerSecond, float DeltaTime)
    {
        float Delta = Mathf.DeltaAngleDegrees(CurrentYaw, Yaw);
        float Step = Math.Clamp(Delta, -DegreesPerSecond * DeltaTime, DegreesPerSecond * DeltaTime);
        CurrentYaw += Step;
        Controller?.AddYaw(Step);
    }

    private void TickWeapons(bool bFiring)
    {
        WeaponState Weapon = Slots[ActiveSlot];

        if (Controls.Pressed(EControl.SwitchWeapon) || MathF.Abs(Controls.Wheel()) > 0.5f)
        {
            ActiveSlot = 1 - ActiveSlot;
            Slots[ActiveSlot].Cooldown = MathF.Max(Slots[ActiveSlot].Cooldown, 0.3f);
            Sfx.At(ESfx.WeaponSwitch, CachedPosition, 0.45f, 20.0f, 2.0f);
            return;
        }

        if (Controls.Pressed(EControl.Reload) && Weapon.BeginReload())
        {
            Sfx.At(ESfx.Reload, CachedPosition, 0.5f, 25.0f, 2.0f);
        }

        bool bWants = Weapon.Def.bAutomatic ? bFiring : Controls.Pressed(EControl.Fire);
        if (!bWants)
        {
            return;
        }

        if (Weapon.Magazine <= 0)
        {
            if (Weapon.BeginReload())
            {
                Sfx.At(ESfx.Reload, CachedPosition, 0.5f, 25.0f, 2.0f);
            }
            else if (Controls.Pressed(EControl.Fire))
            {
                Sfx.At(ESfx.EmptyClick, CachedPosition, 0.5f, 15.0f, 2.0f);
                if (Weapon.Reserve <= 0)
                {
                    Mercs.Feed.Post($"{Weapon.Def.Name} is out of ammo.", ENewsTone.Bad);
                }
            }

            return;
        }

        if (!Weapon.CanFire)
        {
            return;
        }

        FVector3 Aim = MercCamera.Instance?.AimPoint ?? CachedPosition + Geo.Heading(CurrentYaw) * 50.0f;
        FVector3 Muzzle = CachedPosition + new FVector3(0.0f, 0.4f, 0.0f) + Geo.Heading(CurrentYaw) * 0.55f + Geo.RightOf(CurrentYaw) * 0.15f;
        FVector3 Direction = (Aim - Muzzle).NormalizedOr(Geo.Heading(CurrentYaw));
        float Spread = IsAiming ? -Weapon.Def.SpreadDegrees * 0.5f : (Velocity.Length > 1.0f ? 1.2f : 0.3f);

        Weapon.Consume();
        Weapons.Fire(this, Weapon.Def, Muzzle, Direction, Entity.Null, Spread);
        if (MercCamera.Instance is { } Camera)
        {
            Camera.Pitch += Weapon.Def.Recoil * (IsAiming ? 0.5f : 1.0f);
            Camera.Yaw += Mercs.Range(-0.3f, 0.3f) * Weapon.Def.Recoil;
        }

        if (MercCamera.Instance?.AimTarget is Soldier or Vehicle)
        {
            HitMarkerTime = 0.15f;
        }
    }

    private void ThrowGrenade()
    {
        if (ThrowCooldown > 0.0f)
        {
            return;
        }

        if (Grenades <= 0)
        {
            Mercs.Feed.Post("No grenades.", ENewsTone.Bad);
            return;
        }

        Grenades--;
        ThrowCooldown = 0.6f;
        CombatStance = 0.8f;
        FVector3 From = HandPosition();
        FVector3 Target = MercCamera.Instance?.AimPoint ?? From + Geo.Heading(CurrentYaw) * 20.0f;
        if (FVector3.Distance(Target, From) > 40.0f)
        {
            Target = From + (Target - From).Normalized() * 40.0f;
        }

        Throwables.Throw(EThrowable.Grenade, From, Throwables.LobVelocity(From, Target, 18.0f), this);
    }

    private void PlaceCharge()
    {
        if (ThrowCooldown > 0.0f)
        {
            return;
        }

        if (Charges <= 0)
        {
            Mercs.Feed.Post("No C4 left.", ENewsTone.Bad);
            return;
        }

        Charges--;
        ThrowCooldown = 0.5f;
        FVector3 From = HandPosition();
        FVector3 Target = MercCamera.Instance?.AimPoint ?? From + Geo.Heading(CurrentYaw) * 6.0f;
        if (FVector3.Distance(Target, From) > 12.0f)
        {
            Target = From + (Target - From).Normalized() * 12.0f;
        }

        Throwables.Throw(EThrowable.C4, From, Throwables.LobVelocity(From, Target, 10.0f), this);
        Mercs.Feed.Post($"C4 placed. [V] to detonate. ({Mercs.Throwables.ActiveCharges} armed)", ENewsTone.Neutral);
    }

    private FVector3 HandPosition() => CachedPosition + new FVector3(0.0f, 0.6f, 0.0f) + Geo.Heading(CurrentYaw) * 0.9f;

    private void CallSupport()
    {
        ESupportKind Kind = Mercs.Support.Selected;
        if (Kind == ESupportKind.None || Mercs.Support.Count(Kind) <= 0)
        {
            Mercs.Feed.Post("No support equipped. Buy some from the PDA shop [Tab].", ENewsTone.Bad);
            return;
        }

        if (ThrowCooldown > 0.0f)
        {
            return;
        }

        if (!Mercs.Support.TryConsume(Kind))
        {
            return;
        }

        ThrowCooldown = 1.0f;
        FVector3 From = HandPosition();
        FVector3 Target = MercCamera.Instance?.AimPoint ?? From + Geo.Heading(CurrentYaw) * 25.0f;
        if (FVector3.Distance(Target, From) > 45.0f)
        {
            Target = From + (Target - From).Normalized() * 45.0f;
        }

        Throwables.Throw(EThrowable.Beacon, From, Throwables.LobVelocity(From, Target, 18.0f), this, Kind);
        Mercs.Feed.Post($"{SupportCatalog.Get(Kind).Name} beacon thrown.", ENewsTone.Neutral);
    }

    private void Melee()
    {
        if (MeleeCooldown > 0.0f)
        {
            return;
        }

        MeleeCooldown = 0.65f;
        FVector3 Forward = Geo.Heading(CurrentYaw);
        Soldier? Best = null;
        float BestDistance = 3.0f;
        foreach (Soldier Candidate in Mercs.Soldiers)
        {
            if (!Candidate.IsAlive)
            {
                continue;
            }

            FVector3 Offset = Candidate.Position - CachedPosition;
            float Distance = Offset.Length;
            if (Distance < BestDistance)
            {
                Best = Candidate;
                BestDistance = Distance;
            }
        }

        if (Best is null)
        {
            return;
        }

        TurnToward(Geo.YawOf(Best.Position - CachedPosition), 3600.0f, 1.0f);
        Sfx.At(ESfx.MeleeHit, Best.Position, 0.7f, 30.0f, 2.0f, 0.1f);
        Best.TakeHit(FHit.From(this, 45.0f, EDamageKind.Melee, Best.Position + new FVector3(0.0f, 0.4f, 0.0f), Forward));
        CCameraLibrary.PlayImpactShake(World, 0.25f, 0.15f);
        HitMarkerTime = 0.2f;
    }

    private void UpdateInteraction()
    {
        InteractPrompt = string.Empty;
        PendingInteract = null;

        if (CurrentVehicle is not null)
        {
            Vehicle Ride = CurrentVehicle;
            if (Ride.Definition.bAir && Ride.Altitude > 4.0f)
            {
                InteractPrompt = "Land to bail out safely  [E] Bail out";
            }
            else
            {
                InteractPrompt = "[E] Exit vehicle";
            }

            PendingInteract = () => ExitVehicle();
            if (Ride.Type == EVehicleType.FuelTruck && Mercs.Sites.Find(Site => Site.Kind == ESiteKind.PmcHq) is { } Hq && Geo.FlatDistance(Ride.Position, Hq.Center) < Hq.Radius)
            {
                InteractPrompt = "[E] Deliver fuel to PMC";
                PendingInteract = () => DeliverFuel(Ride);
            }

            return;
        }

        foreach (Soldier Candidate in Mercs.Soldiers)
        {
            if (!Candidate.bIsHvt || FVector3.Distance(Candidate.Position, CachedPosition) > 3.0f)
            {
                continue;
            }

            if (Candidate.IsSubdued && Candidate.Hvt is { } Captured && !Captured.bExtractionCalled)
            {
                InteractPrompt = $"[E] Call extraction for {Candidate.DisplayName}";
                PendingInteract = () => Mercs.Contracts.CallExtraction(Candidate);
                return;
            }

            if (Candidate.IsDead && Candidate.Hvt is { } Fallen && !Fallen.bResolved)
            {
                InteractPrompt = $"[E] Verify {Candidate.DisplayName} (photo)";
                PendingInteract = () => Mercs.Contracts.VerifyHvt(Candidate);
                return;
            }
        }

        foreach (Site Place in Mercs.Sites)
        {
            if (Place.Contact is { } Contact && FVector3.Distance(Contact, CachedPosition) < 3.5f)
            {
                FactionInfo Info = Mercs.Factions.Get(Place.Owner);
                if (Place.Kind == ESiteKind.PmcHq)
                {
                    InteractPrompt = "[E] PMC shop & armory";
                    PendingInteract = () => Mercs.Director?.OpenPda(EPdaTab.Shop);
                }
                else if (Mercs.Factions.MoodOf(Place.Owner) == EMood.Hostile)
                {
                    InteractPrompt = $"{Info.Name} won't deal with you. Bribe them from the PDA.";
                }
                else
                {
                    InteractPrompt = $"[E] {Info.Name} contracts";
                    FactionInfo Captured = Info;
                    PendingInteract = () => Mercs.Director?.OpenContracts(Captured.Id);
                }

                return;
            }
        }

        Pickup? Weapon = Pickup.NearestWeapon(CachedPosition, 2.2f);
        if (Weapon is not null)
        {
            WeaponDef Def = Weapons.Get(Weapon.WeaponKind);
            InteractPrompt = $"[E] Take {Def.Name} (drops {Slots[ActiveSlot].Def.Name})";
            PendingInteract = () => SwapWeapon(Weapon);
            return;
        }

        Vehicle? Best = null;
        float BestDistance = float.MaxValue;
        foreach (Vehicle Candidate in Mercs.Vehicles)
        {
            if (!Candidate.IsAlive)
            {
                continue;
            }

            bool bAirborne = Candidate.Definition.bAir && Candidate.Altitude > 3.0f;
            float Reach = bAirborne ? 22.0f : 3.5f;
            float Distance = bAirborne ? FVector3.Distance(Candidate.Position, CachedPosition) : Candidate.DistanceToHull(CachedPosition);
            if (Distance < Reach && Distance < BestDistance)
            {
                Best = Candidate;
                BestDistance = Distance;
            }
        }

        if (Best is null)
        {
            return;
        }

        Vehicle Chosen = Best;
        if (Chosen.IsEmpty)
        {
            InteractPrompt = $"[E] Enter {Chosen.Definition.Name}";
            PendingInteract = () => EnterVehicle(Chosen);
        }
        else if (Chosen.IsCrewed)
        {
            bool bGrapple = Chosen.Definition.bAir && Chosen.Altitude > 3.0f;
            InteractPrompt = bGrapple ? $"[E] Grapple and hijack {Chosen.Definition.Name}" : $"[E] Hijack {Chosen.Definition.Name}";
            PendingInteract = () => BeginHijack(Chosen, bGrapple);
        }
    }

    private void SwapWeapon(Pickup Weapon)
    {
        EWeapon Dropped = Slots[ActiveSlot].Kind;
        Slots[ActiveSlot] = new WeaponState(Weapon.WeaponKind, false);
        Weapon.Consume();
        Pickup.SpawnWeapon(Geo.Ground(CachedPosition) + Geo.Heading(CurrentYaw) * 0.8f + new FVector3(0.0f, 0.3f, 0.0f), Dropped);
        Mercs.Feed.Post($"Picked up {Slots[ActiveSlot].Def.Name}.", ENewsTone.Neutral);
    }

    private void DeliverFuel(Vehicle Truck)
    {
        ExitVehicle();
        Mercs.Wallet.AddFuel(500, "fuel truck delivered");
        Truck.TakeOver(EFaction.Merc);
        World.SetLifetime(Truck.Owner, 0.1f);
    }

    public void BeginHijack(Vehicle Target, bool bGrapple)
    {
        if (!Target.Definition.bHeavy && !bGrapple)
        {
            Target.EjectCrew(true, this);
            Mercs.Factions.Offense(Target.Team, 4.0f, Target.Position, $"hijacked {Mercs.Factions.Get(Target.Team).Short} vehicle");
            EnterVehicle(Target);
            return;
        }

        HijackSession Session = new() { Target = Target, bGrapple = bGrapple, Intro = bGrapple ? 0.8f : 0.3f };
        EKey[] Pool = { EKey.W, EKey.A, EKey.S, EKey.D, EKey.Space };
        int Steps = Target.Definition.bAir ? 6 : 5;
        for (int Index = 0; Index < Steps; ++Index)
        {
            Session.Sequence.Add(Pool[Mercs.RangeInt(0, Pool.Length)]);
        }

        Session.StepTime = Target.Definition.bAir ? 1.05f : 1.25f;
        Session.TimeLeft = Session.StepTime;
        Hijack = Session;
        HumanoidBody.RemoveCapsule(Registry, Entity);
        Mercs.Feed.Announce("HIJACK", bGrapple ? "Grappling on! Hit the keys!" : "Hit the keys as they appear!", 1.5f);
    }

    private void TickHijack(float DeltaTime)
    {
        HijackSession Session = Hijack!;
        Vehicle Target = Session.Target;
        if (!Target.IsAlive)
        {
            EndHijack(false);
            return;
        }

        FVector3 Hold = Target.Position + Geo.RightOf(Target.Heading) * (Target.Definition.HalfExtents.X + 0.4f) + new FVector3(0.0f, Target.Definition.HalfExtents.Y * 0.5f, 0.0f);
        World.SetEntityLocation(Entity, Hold);
        CachedPosition = Hold;

        if (Session.Intro > 0.0f)
        {
            Session.Intro -= DeltaTime;
            if (Session.Intro <= 0.0f)
            {
                LogHijackStep(Session);
            }

            return;
        }

        Session.TimeLeft -= DeltaTime;
        foreach (EKey Key in new[] { EKey.W, EKey.A, EKey.S, EKey.D, EKey.Space })
        {
            if (!Controls.KeyPressed(Key))
            {
                continue;
            }

            if (Key != Session.Current)
            {
                EndHijack(false);
                return;
            }

            Session.Step++;
            Session.TimeLeft = Session.StepTime;
            Sfx.At(ESfx.MeleeHit, CachedPosition, 0.6f, 30.0f, 2.0f, 0.15f);
            if (Session.Step >= Session.Sequence.Count)
            {
                EndHijack(true);
            }
            else
            {
                LogHijackStep(Session);
            }

            return;
        }

        if (Session.TimeLeft <= 0.0f)
        {
            EndHijack(false);
        }
    }

    private static void LogHijackStep(HijackSession Session)
    {
        if (Mercs.Director is { bDevCheats: true })
        {
            Debug.Log($"[HijackStep] {Session.Step} {Session.Current}");
        }
    }

    private void EndHijack(bool bSuccess)
    {
        HijackSession Session = Hijack!;
        Hijack = null;
        Vehicle Target = Session.Target;

        if (bSuccess && Target.IsAlive)
        {
            Target.EjectCrew(false, this);
            Mercs.Factions.Offense(Target.Team, 5.0f, Target.Position, $"hijacked {Mercs.Factions.Get(Target.Team).Short} {Target.Definition.Name}");
            Mercs.Feed.Announce("HIJACKED", Target.Definition.Name, 2.0f);
            EnterVehicle(Target);
            return;
        }

        FVector3 Landing = Target.IsAlive ? Target.ExitPoint(1.0f) : Geo.Ground(CachedPosition) + new FVector3(0.0f, 1.2f, 0.0f);
        RestoreOnFoot(Landing);
        TakeHit(new FHit { Amount = 25.0f, Kind = EDamageKind.Crush, Point = CachedPosition, Direction = FVector3.Up, Source = Target.Owner });
        Controller?.Launch(Geo.RightOf(Target.Heading) * 6.0f + new FVector3(0.0f, 4.0f, 0.0f), true, true);
        Mercs.Factions.Provoke(Target.Team);
        Mercs.Feed.Announce("THROWN OFF", "The crew fought you off.", 1.5f);
    }

    public void EnterVehicle(Vehicle Target)
    {
        if (CurrentVehicle is not null || !Target.IsAlive)
        {
            return;
        }

        CurrentVehicle = Target;
        IsAiming = false;
        IsSprinting = false;
        HumanoidBody.RemoveCapsule(Registry, Entity);
        World.SetParent(Entity, Target.Owner);
        Registry.Get<STransformComponent>(Entity).SetLocalLocation(new FVector3(0.0f, 1.2f, 0.0f));
        BodyTransform?.SetLocalScale(new FVector3(0.001f));
        Target.EnterPlayer(this);
        Mercs.Contracts.OnPlayerEnteredVehicle(Target);
    }

    public void ExitVehicle()
    {
        Vehicle? Ride = CurrentVehicle;
        if (Ride is null)
        {
            return;
        }

        FVector3 Exit = Ride.ExitPoint(-1.0f);
        if (Geo.Trace(Ride.Position, Exit, Ride.Owner, Entity).bHit)
        {
            Exit = Ride.ExitPoint(1.0f);
        }

        if (Ride.Definition.bAir && Ride.Altitude > 4.0f)
        {
            Exit = Ride.Position - new FVector3(0.0f, 2.5f, 0.0f);
        }

        Ride.ExitPlayer();
        ForceExitVehicle(Exit);
    }

    public void ForceExitVehicle(FVector3 Exit)
    {
        if (CurrentVehicle is null)
        {
            return;
        }

        CurrentVehicle = null;
        World.DetachFromParent(Entity);
        RestoreOnFoot(Exit);
    }

    private void RestoreOnFoot(FVector3 At)
    {
        World.SetEntityLocation(Entity, At);
        World.SetEntityRotation(Entity, Geo.YawRotation(CurrentYaw));
        using (new FPhysicsBatchScope(World))
        {
            HumanoidBody.SetupCapsule(Registry, Entity, RunSpeed);
        }

        Controller?.AddYaw(CurrentYaw);
        BodyTransform?.SetLocalScale(FVector3.One);
        CachedPosition = At;
    }

    private void TickVehicle(float DeltaTime)
    {
        Vehicle Ride = CurrentVehicle!;
        if (!Ride.IsAlive)
        {
            return;
        }

        FVector2 Move = Controls.Move();
        MercCamera? Camera = MercCamera.Instance;
        FVehicleInput Input = new()
        {
            Throttle = Move.Y,
            Steer = Move.X,
            bBoost = Controls.Down(EControl.Sprint),
            bFirePrimary = Controls.Down(EControl.Fire),
            bFireSecondary = Controls.Down(EControl.Aim),
            bHasAim = Camera is not null,
            AimPoint = Camera?.AimPoint ?? Ride.Position + Geo.Heading(Ride.Heading) * 50.0f,
        };

        if (Ride.Definition.bAir)
        {
            Input.Strafe = Move.X;
            Input.Steer = 0.0f;
            Input.Lift = (Controls.Down(EControl.Jump) ? 1.0f : 0.0f) - (Controls.Down(EControl.Descend) ? 1.0f : 0.0f);
            Input.bHasDesiredYaw = Camera is not null;
            Input.DesiredYaw = Camera?.Yaw ?? Ride.Heading;
        }

        Ride.SetPlayerInput(Input);
        CurrentYaw = Ride.Heading;

        if (Controls.Pressed(EControl.CallSupport))
        {
            CallSupport();
        }
    }

    public void TakeHit(in FHit Hit)
    {
        if (bDead || Health is null || Hijack is not null && Hit.Kind == EDamageKind.Bullet)
        {
            return;
        }

        if (CurrentVehicle is not null && Hit.Kind != EDamageKind.Explosive)
        {
            return;
        }

        float Amount = Mercs.Director is { bGodMode: true } ? 0.0f : Hit.Amount;
        if (Hit.bByPlayer && Hit.Kind == EDamageKind.Explosive)
        {
            Amount *= 0.5f;
        }

        float Remaining = Health.ApplyDamage(Amount, Hit.Source);
        Mercs.Director?.OnPlayerDamaged(Hit);
        if (Mercs.Director is { bDevCheats: true })
        {
            string From = Hit.Source.IsNull || !World.IsValidEntity(Hit.Source) ? "?" : World.GetEntityName(Hit.Source);
            Debug.Log($"[PlayerHit] {Amount:0} {Hit.Kind} from {From} -> {Remaining:0}");
        }

        if (Hit.Kind == EDamageKind.Explosive && CurrentVehicle is null)
        {
            Controller?.Launch(Hit.Direction * 6.0f + new FVector3(0.0f, 4.0f, 0.0f), true, true);
        }

        if (Remaining <= 0.0f)
        {
            Die();
        }
    }

    private void Die()
    {
        if (bDead)
        {
            return;
        }

        Sfx.Ui(ESfx.Failure, 0.7f);

        if (CurrentVehicle is not null)
        {
            Vehicle Ride = CurrentVehicle;
            Ride.ExitPlayer();
            ForceExitVehicle(Ride.ExitPoint(-1.0f));
        }

        if (Hijack is not null)
        {
            Hijack = null;
            RestoreOnFoot(Geo.Ground(CachedPosition) + new FVector3(0.0f, 1.2f, 0.0f));
        }

        bDead = true;
        RespawnTimer = 6.0f;
        HumanoidBody.RemoveCapsule(Registry, Entity);
        HumanoidBody.PoseDead(BodyTransform, 0.0f);
        Mercs.Feed.Announce("MISSION FAILED", $"{MercName(Merc)} is down. Evac to the PMC in a few seconds.", 5.0f);
    }

    private void TickRespawn(float DeltaTime)
    {
        RespawnTimer -= DeltaTime;
        if (RespawnTimer > 0.0f)
        {
            return;
        }

        int Fee = Math.Min(20000, Mercs.Wallet.Cash / 10);
        if (Fee > 0)
        {
            Mercs.Wallet.AddCash(-Fee, "medical evac");
        }

        Site? Hq = Mercs.Sites.Find(Site => Site.Kind == ESiteKind.PmcHq);
        FVector3 Spawn = Hq is not null ? Hq.PlayerSpawn : new FVector3(0.0f, 20.0f, 0.0f);
        bDead = false;
        Health?.Revive(MaxHealth);
        foreach (WeaponState Weapon in Slots)
        {
            Weapon.Refill(0.5f);
            Weapon.Magazine = Weapon.Def.Magazine;
        }

        Grenades = Math.Max(Grenades, 2);
        BodyTransform?.SetLocalTransform(new FTransform(FVector3.Zero, FQuat.Identity, FVector3.One));
        RestoreOnFoot(Spawn);
        Mercs.Factions.SetDisguise(EFaction.None);
    }

    public void AddAmmo(float Fraction)
    {
        foreach (WeaponState Weapon in Slots)
        {
            Weapon.Refill(Fraction);
        }
    }

    public void Heal(float Amount)
    {
        Health?.Heal(Amount, Entity.Null);
    }

    public void Teleport(FVector3 At)
    {
        if (CurrentVehicle is not null)
        {
            return;
        }

        Controller?.TeleportTo(At);
        CachedPosition = At;
        LastPosition = At;
    }
}
