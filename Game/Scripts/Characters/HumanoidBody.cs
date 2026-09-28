using System;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

public enum EHeadgear : byte
{
    None,
    Helmet,
    Cap,
    Beret,
    HardHat,
    Bandana,
    Boonie,
}

public enum EMarker : byte
{
    None,
    Witness,
    Hvt,
    Subdued,
    Objective,
    Contact,
}

public static class HumanoidBody
{
    public const float CapsuleRadius = 0.32f;
    public const float CapsuleHalfHeight = 0.58f;
    public const float FeetOffset = CapsuleRadius + CapsuleHalfHeight;

    private static readonly System.Collections.Generic.Dictionary<string, CStaticMesh?> PartMeshes = new();

    // The meshes belong to the world that built them, so a new play session must not reuse the last one's.
    public static void Reset() => PartMeshes.Clear();

    public static EHeadgear HeadgearFor(EFaction Faction, bool bOfficer) => Faction switch
    {
        EFaction.VZ => bOfficer ? EHeadgear.Beret : EHeadgear.Helmet,
        EFaction.Allied => EHeadgear.Helmet,
        EFaction.China => bOfficer ? EHeadgear.Cap : EHeadgear.Helmet,
        EFaction.Oil => EHeadgear.HardHat,
        EFaction.Guerrilla => bOfficer ? EHeadgear.Beret : EHeadgear.Boonie,
        EFaction.Pirate => EHeadgear.Bandana,
        EFaction.Merc => EHeadgear.Cap,
        _ => EHeadgear.None,
    };

    public static void SetupCapsule(EntityRegistry Registry, Entity Target, float MoveSpeed)
    {
        SCharacterPhysicsComponent Capsule = Registry.GetOrAdd<SCharacterPhysicsComponent>(Target)!;
        Capsule.Radius = CapsuleRadius;
        Capsule.HalfHeight = CapsuleHalfHeight;
        Capsule.MaxSlopeAngle = 50.0f;
        Capsule.StepHeight = 0.45f;

        SCharacterControllerComponent Controller = Registry.GetOrAdd<SCharacterControllerComponent>(Target)!;
        Controller.PitchClamp = new FVector2(-80.0f, 80.0f);

        SCharacterMovementComponent Movement = Registry.GetOrAdd<SCharacterMovementComponent>(Target)!;
        Movement.MoveSpeed = MoveSpeed;
        Movement.JumpSpeed = 5.5f;
        Movement.Acceleration = 40.0f;
        Movement.Deceleration = 30.0f;
        Movement.bUseControllerRotation = true;
        Movement.bOrientRotationToMovement = false;

        // Aimed at the chest, so a soldier behind low cover still reads as visible over it.
        SAIStimuliSourceComponent Visible = Registry.GetOrAdd<SAIStimuliSourceComponent>(Target)!;
        Visible.SightTargetOffset = new FVector3(0.0f, 0.4f, 0.0f);
    }

    public static void RemoveCapsule(EntityRegistry Registry, Entity Target)
    {
        Registry.Remove<SPathFollowComponent>(Target);
        Registry.Remove<SRVOAgentComponent>(Target);
        Registry.Remove<SPerceptionComponent>(Target);
        Registry.Remove<SAIStimuliSourceComponent>(Target);
        Registry.Remove<SCharacterControllerComponent>(Target);
        Registry.Remove<SCharacterMovementComponent>(Target);
        Registry.Remove<SCharacterPhysicsComponent>(Target);
    }

    public static Entity Build(CWorld World, Entity Parent, EFaction Faction, EWeapon Weapon, bool bOfficer, float Scale = 1.0f, FVector4? ShirtOverride = null)
        => BuildRig(World, Parent, Faction, Weapon, bOfficer, Scale, ShirtOverride).Body;

    // One mesh per limb, cached by look, so a death can hand each limb to physics on its own without rebuilding geometry.
    public static HumanoidRig BuildRig(CWorld World, Entity Parent, EFaction Faction, EWeapon Weapon, bool bOfficer, float Scale = 1.0f, FVector4? ShirtOverride = null)
    {
        FactionInfo Info = Mercs.Factions.Get(Faction);
        FVector4 Shirt = ShirtOverride ?? Info.Color;
        FVector4 Pants = Faction == EFaction.Civilian ? Palette.Hex(0x3C4A63) : Palette.Shade(Info.Color, 0.62f);
        FVector4 Vest = Faction switch
        {
            EFaction.Merc => Palette.Hex(0x3B3B33),
            EFaction.Civilian => Shirt,
            _ => Palette.Shade(Info.Color, 0.8f),
        };
        // Picked from the entity, so each soldier keeps one face for as long as it exists.
        int Variant = (int)((Parent.Id * 2654435761u) >> 28) % HumanoidShapes.Variants;
        FVector4 Skin = HumanoidShapes.SkinFor(Faction, Variant);
        FVector4 Hair = HumanoidShapes.HairFor(Variant);
        bool bMilitary = Faction != EFaction.Civilian;
        bool bRolledSleeves = Faction is EFaction.Guerrilla or EFaction.Pirate or EFaction.Civilian;
        FVector4 Glove = Faction is EFaction.Merc or EFaction.Allied or EFaction.China ? Palette.Hex(0x23211D) : Skin;

        MeshKit[] Kits = new MeshKit[HumanoidRig.PartCount];
        for (int Index = 0; Index < Kits.Length; ++Index)
        {
            Kits[Index] = new MeshKit();
        }

        HumanoidRig Rig = new() { Scale = Scale };
        float Feet = -FeetOffset;

        for (int Side = 0; Side < 2; ++Side)
        {
            float X = Side == 0 ? 0.11f : -0.11f;
            EBodyPart Part = Side == 0 ? EBodyPart.LegR : EBodyPart.LegL;
            HumanoidShapes.Leg(Kits[(int)Part], X, Feet, Pants, Palette.Rubber, bMilitary && Variant % 2 == 0);
            Rig.SetBounds(Part, new FVector3(X, Feet + 0.4f, 0.03f), new FVector3(0.1f, 0.42f, 0.13f));
        }

        HumanoidShapes.Torso(Kits[(int)EBodyPart.Torso], Feet, Shirt, Pants, Skin, bMilitary ? Vest : null, Palette.Hex(0x2A2418), Faction == EFaction.Merc || bOfficer);
        Rig.SetBounds(EBodyPart.Torso, new FVector3(0.0f, Feet + 1.06f, 0.0f), new FVector3(0.25f, 0.34f, 0.17f));

        EHeadgear Gear = HeadgearFor(Faction, bOfficer);
        bool bBeard = Faction is EFaction.Pirate or EFaction.Guerrilla && Variant != 0;
        HumanoidShapes.Head(Kits[(int)EBodyPart.Head], Feet, Skin, Hair, Gear is EHeadgear.None or EHeadgear.Beret, bBeard);
        HumanoidShapes.Headgear(Kits[(int)EBodyPart.Head], Feet, Gear, HeadgearColor(Faction, Info), Info.Accent, Faction == EFaction.Merc ? Palette.Hex(0xE07A1F) : HeadgearColor(Faction, Info));
        Rig.SetBounds(EBodyPart.Head, new FVector3(0.0f, Feet + 1.6f, 0.01f), new FVector3(0.14f, 0.19f, 0.14f));

        FVector3 ShoulderR = new(0.26f, Feet + 1.31f, 0.0f);
        FVector3 ShoulderL = new(-0.26f, Feet + 1.31f, 0.0f);
        FVector3 HandR;
        FVector3 HandL;
        MeshKit Gun = Kits[(int)EBodyPart.Weapon];
        FVector3 PoleR;
        FVector3 PoleL;
        if (Weapon == EWeapon.None)
        {
            HandR = ShoulderR + new FVector3(0.05f, -0.54f, 0.05f);
            HandL = ShoulderL + new FVector3(-0.05f, -0.54f, 0.05f);
            PoleR = new FVector3(0.2f, 0.0f, -1.0f);
            PoleL = new FVector3(-0.2f, 0.0f, -1.0f);
        }
        else if (Weapon is EWeapon.Rocket)
        {
            HandR = new FVector3(0.18f, Feet + 1.4f, 0.25f);
            HandL = new FVector3(0.05f, Feet + 1.3f, 0.3f);
            PoleR = new FVector3(1.0f, -1.0f, -0.3f);
            PoleL = new FVector3(-1.0f, -1.0f, -0.3f);
            HumanoidShapes.Launcher(Gun, new FVector3(0.2f, Feet + 1.52f, 0.6f), new FVector3(0.2f, Feet + 1.52f, -0.5f), out FVector3 Center, out FVector3 Half);
            Rig.SetBounds(EBodyPart.Weapon, Center, Half);
        }
        else
        {
            HandR = new FVector3(0.12f, Feet + 1.12f, 0.3f);
            HandL = new FVector3(0.05f, Feet + 1.15f, 0.52f);
            PoleR = new FVector3(1.0f, -1.2f, -0.2f);
            PoleL = new FVector3(-1.0f, -1.4f, 0.0f);
            bool bWood = Faction is EFaction.VZ or EFaction.Guerrilla or EFaction.Pirate;
            HumanoidShapes.Firearm(Gun, Weapon, HandR, bWood, out FVector3 Center, out FVector3 Half);
            Rig.SetBounds(EBodyPart.Weapon, Center, Half);
        }

        FVector4 Forearm = bRolledSleeves ? Skin : Palette.Shade(Shirt, 0.95f);
        HumanoidShapes.Arm(Kits[(int)EBodyPart.ArmR], ShoulderR, HandR, PoleR, Shirt, Forearm, Glove);
        HumanoidShapes.Arm(Kits[(int)EBodyPart.ArmL], ShoulderL, HandL, PoleL, Shirt, Forearm, Glove);
        Rig.SetSpan(EBodyPart.ArmR, ShoulderR, HandR, 0.08f);
        Rig.SetSpan(EBodyPart.ArmL, ShoulderL, HandL, 0.08f);

        Rig.Body = World.CreateEntity("Body", FVector3.Zero, null, new FVector3(Scale));
        World.SetParent(Rig.Body, Parent);
        World.Registry.Get<STransformComponent>(Rig.Body).SetLocalLocation(FVector3.Zero);

        string Look = $"{Faction}|{Weapon}|{bOfficer}|{ShirtOverride}|{Variant}";
        for (int Index = 0; Index < HumanoidRig.PartCount; ++Index)
        {
            if (Kits[Index].IsEmpty)
            {
                continue;
            }

            string Key = $"{Look}|{Index}";
            if (!PartMeshes.TryGetValue(Key, out CStaticMesh? Mesh))
            {
                Mesh = Kits[Index].BuildStaticMesh(World, Materials.Solid, 3);
                if (Mesh is not null)
                {
                    World.RetainObject(Mesh);
                }
                PartMeshes[Key] = Mesh;
            }

            Entity Part = World.CreateEntity(((EBodyPart)Index).ToString(), FVector3.Zero);
            World.SetParent(Part, Rig.Body);
            World.Registry.Get<STransformComponent>(Part).SetLocalLocation(FVector3.Zero);
            MeshKit.Show(World.Registry, Part, Mesh, true, false);
            Rig.Parts[Index] = Part;
            Rig.Meshes[Index] = Mesh;
        }

        return Rig;
    }

    private static FVector4 HeadgearColor(EFaction Faction, FactionInfo Info) => Faction switch
    {
        EFaction.Allied => Palette.Hex(0x4F8FD6),
        EFaction.Oil => Palette.Hex(0xF2C230),
        EFaction.Merc => Palette.Hex(0x1E1E1E),
        _ => Palette.Shade(Info.Color, 0.85f),
    };

    public static Entity BuildMarker(CWorld World, Entity Parent, EMarker Kind, float Height = 1.5f)
    {
        FVector4 Color = Kind switch
        {
            EMarker.Witness => Palette.Rgb(1.0f, 0.85f, 0.1f),
            EMarker.Hvt => Palette.Rgb(1.0f, 0.15f, 0.1f),
            EMarker.Subdued => Palette.Rgb(0.2f, 1.0f, 0.35f),
            EMarker.Contact => Palette.Rgb(0.2f, 0.7f, 1.0f),
            _ => Palette.Rgb(1.0f, 0.55f, 0.1f),
        };

        MeshKit Kit = new();
        Kit.Cone(new FVector3(0.0f, 0.0f, 0.0f), 0.16f, -0.3f, Color, 4);
        Kit.Cone(new FVector3(0.0f, 0.0f, 0.0f), 0.16f, 0.3f, Color, 4);

        Entity Marker = World.CreateEntity("Marker", FVector3.Zero);
        World.SetParent(Marker, Parent);
        World.Registry.Get<STransformComponent>(Marker).SetLocalLocation(new FVector3(0.0f, Height, 0.0f));
        Kit.Commit(World.Registry, Marker, true, false, false);
        return Marker;
    }

    public static void AnimateMarker(CWorld World, Entity Marker, float Height)
    {
        if (Marker.IsNull || !World.IsValidEntity(Marker))
        {
            return;
        }

        STransformComponent Transform = World.Registry.Get<STransformComponent>(Marker);
        float Time = Mercs.Time;
        Transform.SetLocalLocation(new FVector3(0.0f, Height + MathF.Sin(Time * 3.0f) * 0.12f, 0.0f));
        Transform.SetLocalRotation(FQuat.FromEuler(0.0f, Time * 2.5f, 0.0f));
    }

    // Lean is extra pitch and roll in degrees on top of the gait, such as a flinch from a hit.
    public static void AnimateWalk(STransformComponent? Body, float Speed, ref float Phase, float DeltaTime, float LeanPitch = 0.0f, float LeanRoll = 0.0f)
    {
        if (Body is null)
        {
            return;
        }

        if (Speed > 0.4f)
        {
            Phase += DeltaTime * (5.0f + Speed * 1.3f);
            float Bob = MathF.Abs(MathF.Sin(Phase)) * 0.05f;
            float Sway = MathF.Sin(Phase) * 3.0f;
            Body.SetLocalTransform(new FTransform(new FVector3(0.0f, Bob, 0.0f),
                FQuat.FromEuler(Mathf.Radians(Speed * 0.8f + LeanPitch), 0.0f, Mathf.Radians(Sway + LeanRoll)), Body.GetLocalScale()));
        }
        else if (Phase != 0.0f || LeanPitch != 0.0f || LeanRoll != 0.0f)
        {
            Phase = 0.0f;
            Body.SetLocalTransform(new FTransform(FVector3.Zero, FQuat.FromEuler(Mathf.Radians(LeanPitch), 0.0f, Mathf.Radians(LeanRoll)), Body.GetLocalScale()));
        }
    }

    private const float MaxStrideDegrees = 32.0f;
    private const float StrideDegreesPerSpeed = 7.0f;
    private const float ArmSwingShare = 0.8f;

    // Legs swing from the hips in step with AnimateWalk's bob, and empty hands swing against them; a weapon keeps both arms on it.
    public static void AnimateLimbs(EntityRegistry Registry, HumanoidRig? Rig, float Speed, float Phase, bool bArmed)
    {
        if (Rig is null || Rig.Body.IsNull)
        {
            return;
        }

        float Stride = Speed > 0.4f ? MathF.Sin(Phase) * MathF.Min(Speed * StrideDegreesPerSpeed, MaxStrideDegrees) : 0.0f;
        SwingPart(Registry, Rig, EBodyPart.LegR, Stride);
        SwingPart(Registry, Rig, EBodyPart.LegL, -Stride);
        if (!bArmed)
        {
            SwingPart(Registry, Rig, EBodyPart.ArmR, -Stride * ArmSwingShare);
            SwingPart(Registry, Rig, EBodyPart.ArmL, Stride * ArmSwingShare);
        }
    }

    // Parts are authored in body space, so turning one about its joint means moving it by what the turn sweeps the joint through.
    private static void SwingPart(EntityRegistry Registry, HumanoidRig Rig, EBodyPart Part, float Degrees)
    {
        Entity Limb = Rig.Parts[(int)Part];
        if (Limb.IsNull || Registry.TryGet<STransformComponent>(Limb) is not { } Transform)
        {
            return;
        }

        FVector3 Joint = FVector3.Zero;
        foreach ((EBodyPart Candidate, FVector3 Pivot, float _) in HumanoidRig.Joints)
        {
            if (Candidate == Part)
            {
                Joint = Pivot;
            }
        }

        FQuat Turn = FQuat.AngleAxis(Mathf.Radians(Degrees), FVector3.Right);
        Transform.SetLocalTransform(new FTransform(Joint - Turn.Rotate(Joint), Turn, FVector3.One));
    }

    private const float SeatedLegDegrees = -78.0f;
    private const float SeatedArmDegrees = -52.0f;

    // Thighs forward under the dash and both hands out on the wheel, with the weapon stowed.
    public static void PoseSeated(EntityRegistry Registry, HumanoidRig? Rig)
    {
        if (Rig is null || Rig.Body.IsNull)
        {
            return;
        }

        Registry.TryGet<STransformComponent>(Rig.Body)?.SetLocalTransform(new FTransform(FVector3.Zero, FQuat.Identity, FVector3.One));
        SwingPart(Registry, Rig, EBodyPart.LegR, SeatedLegDegrees);
        SwingPart(Registry, Rig, EBodyPart.LegL, SeatedLegDegrees);
        SwingPart(Registry, Rig, EBodyPart.ArmR, SeatedArmDegrees);
        SwingPart(Registry, Rig, EBodyPart.ArmL, SeatedArmDegrees);
        SetWeaponShown(Registry, Rig, false);
    }

    // Back to standing, since an armed walk never touches the arms and would keep them on the wheel.
    public static void ResetLimbs(EntityRegistry Registry, HumanoidRig? Rig)
    {
        if (Rig is null || Rig.Body.IsNull)
        {
            return;
        }

        SwingPart(Registry, Rig, EBodyPart.LegR, 0.0f);
        SwingPart(Registry, Rig, EBodyPart.LegL, 0.0f);
        SwingPart(Registry, Rig, EBodyPart.ArmR, 0.0f);
        SwingPart(Registry, Rig, EBodyPart.ArmL, 0.0f);
        SetWeaponShown(Registry, Rig, true);
    }

    private static void SetWeaponShown(EntityRegistry Registry, HumanoidRig Rig, bool bShown)
    {
        Entity Weapon = Rig.Parts[(int)EBodyPart.Weapon];
        if (!Weapon.IsNull && Registry.TryGet<STransformComponent>(Weapon) is { } Transform)
        {
            Transform.SetLocalScale(bShown ? FVector3.One : new FVector3(0.001f));
        }
    }

    public static void PoseDead(STransformComponent? Body, float Yaw)
    {
        Body?.SetLocalTransform(new FTransform(new FVector3(0.0f, -FeetOffset + 0.18f, -0.3f), FQuat.FromEuler(Mathf.Radians(-88.0f), Mathf.Radians(Yaw), 0.0f), Body.GetLocalScale()));
    }

    public static void PoseKneel(STransformComponent? Body)
    {
        Body?.SetLocalTransform(new FTransform(new FVector3(0.0f, -0.42f, 0.0f), FQuat.FromEuler(Mathf.Radians(18.0f), 0.0f, 0.0f), Body.GetLocalScale()));
    }
}
