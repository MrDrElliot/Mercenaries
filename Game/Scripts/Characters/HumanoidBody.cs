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
    }

    public static void RemoveCapsule(EntityRegistry Registry, Entity Target)
    {
        Registry.Remove<SCharacterControllerComponent>(Target);
        Registry.Remove<SCharacterMovementComponent>(Target);
        Registry.Remove<SCharacterPhysicsComponent>(Target);
    }

    public static Entity Build(CWorld World, Entity Parent, EFaction Faction, EWeapon Weapon, bool bOfficer, float Scale = 1.0f, FVector4? ShirtOverride = null)
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
        FVector4 Skin = Faction is EFaction.Pirate or EFaction.Guerrilla ? Palette.SkinDark : Palette.Skin;

        MeshKit Kit = new();
        float Feet = -FeetOffset;

        Kit.Box(new FVector3(0.11f, Feet + 0.06f, 0.03f), new FVector3(0.08f, 0.06f, 0.13f), Palette.Rubber);
        Kit.Box(new FVector3(-0.11f, Feet + 0.06f, 0.03f), new FVector3(0.08f, 0.06f, 0.13f), Palette.Rubber);
        Kit.Box(new FVector3(0.11f, Feet + 0.44f, 0.0f), new FVector3(0.085f, 0.32f, 0.1f), Pants);
        Kit.Box(new FVector3(-0.11f, Feet + 0.44f, 0.0f), new FVector3(0.085f, 0.32f, 0.1f), Pants);
        Kit.Box(new FVector3(0.0f, Feet + 0.8f, 0.0f), new FVector3(0.21f, 0.08f, 0.12f), Pants);
        Kit.Box(new FVector3(0.0f, Feet + 1.13f, 0.0f), new FVector3(0.23f, 0.27f, 0.14f), Shirt);
        if (Faction != EFaction.Civilian)
        {
            Kit.Box(new FVector3(0.0f, Feet + 1.12f, 0.0f), new FVector3(0.245f, 0.19f, 0.155f), Vest);
            Kit.Box(new FVector3(0.0f, Feet + 0.87f, 0.0f), new FVector3(0.235f, 0.035f, 0.15f), Palette.Hex(0x2A2418));
        }

        Kit.Box(new FVector3(0.0f, Feet + 1.44f, 0.0f), new FVector3(0.06f, 0.04f, 0.06f), Skin);
        Kit.Box(new FVector3(0.0f, Feet + 1.58f, 0.01f), new FVector3(0.12f, 0.13f, 0.12f), Skin);
        Kit.Box(new FVector3(0.0f, Feet + 1.6f, 0.125f), new FVector3(0.08f, 0.02f, 0.01f), Palette.Hex(0x151515));
        AddHeadgear(Kit, HeadgearFor(Faction, bOfficer), Feet + 1.58f, Info, Faction);

        FVector3 ShoulderR = new(0.28f, Feet + 1.32f, 0.0f);
        FVector3 ShoulderL = new(-0.28f, Feet + 1.32f, 0.0f);
        bool bHeavy = Weapon is EWeapon.Rocket;
        if (Weapon == EWeapon.None)
        {
            Kit.Tube(ShoulderR, ShoulderR + new FVector3(0.04f, -0.55f, 0.04f), 0.065f, 0.055f, Shirt, 6);
            Kit.Tube(ShoulderL, ShoulderL + new FVector3(-0.04f, -0.55f, 0.04f), 0.065f, 0.055f, Shirt, 6);
        }
        else if (bHeavy)
        {
            Kit.Tube(ShoulderR, new FVector3(0.18f, Feet + 1.4f, 0.25f), 0.065f, 0.055f, Shirt, 6);
            Kit.Tube(ShoulderL, new FVector3(0.05f, Feet + 1.3f, 0.3f), 0.065f, 0.055f, Shirt, 6);
            Kit.Tube(new FVector3(0.2f, Feet + 1.52f, -0.5f), new FVector3(0.2f, Feet + 1.52f, 0.6f), 0.08f, 0.08f, Palette.Hex(0x4A5234), 7);
        }
        else
        {
            FVector3 Grip = new(0.12f, Feet + 1.12f, 0.32f);
            FVector3 Fore = new(0.02f, Feet + 1.16f, 0.5f);
            Kit.Tube(ShoulderR, Grip, 0.065f, 0.055f, Shirt, 6);
            Kit.Tube(ShoulderL, Fore, 0.065f, 0.055f, Shirt, 6);
            float Length = Weapon switch
            {
                EWeapon.Pistol => 0.18f,
                EWeapon.Smg => 0.3f,
                EWeapon.Sniper => 0.62f,
                EWeapon.MachineGun => 0.55f,
                _ => 0.45f,
            };
            Kit.Box(new FVector3(0.07f, Feet + 1.16f, 0.35f + Length * 0.3f), new FVector3(0.035f, 0.06f, Length), Palette.Gunmetal);
            if (Weapon == EWeapon.Sniper)
            {
                Kit.Box(new FVector3(0.07f, Feet + 1.25f, 0.4f), new FVector3(0.025f, 0.025f, 0.12f), Palette.Rubber);
            }
        }

        Entity Body = World.CreateEntity("Body", FVector3.Zero, null, new FVector3(Scale));
        World.SetParent(Body, Parent);
        Mercs.World.Registry.Get<STransformComponent>(Body).SetLocalLocation(FVector3.Zero);
        Kit.Commit(World.Registry, Body);
        return Body;
    }

    private static void AddHeadgear(MeshKit Kit, EHeadgear Gear, float HeadY, FactionInfo Info, EFaction Faction)
    {
        FVector4 Color = Faction switch
        {
            EFaction.Allied => Palette.Hex(0x4F8FD6),
            EFaction.Oil => Palette.Hex(0xF2C230),
            EFaction.Merc => Palette.Hex(0x1E1E1E),
            _ => Palette.Shade(Info.Color, 0.85f),
        };

        switch (Gear)
        {
            case EHeadgear.Helmet:
                Kit.Box(new FVector3(0.0f, HeadY + 0.1f, 0.0f), new FVector3(0.15f, 0.08f, 0.15f), Color);
                Kit.Box(new FVector3(0.0f, HeadY + 0.04f, 0.0f), new FVector3(0.155f, 0.025f, 0.155f), Palette.Shade(Color, 0.8f));
                break;
            case EHeadgear.Cap:
                Kit.Box(new FVector3(0.0f, HeadY + 0.13f, 0.0f), new FVector3(0.13f, 0.045f, 0.13f), Color);
                Kit.Box(new FVector3(0.0f, HeadY + 0.1f, 0.17f), new FVector3(0.1f, 0.012f, 0.06f), Faction == EFaction.Merc ? Palette.Hex(0xE07A1F) : Color);
                break;
            case EHeadgear.Beret:
                Kit.Box(new FVector3(0.03f, HeadY + 0.14f, 0.0f), new FVector3(0.14f, 0.035f, 0.13f), Info.Accent);
                break;
            case EHeadgear.HardHat:
                Kit.Box(new FVector3(0.0f, HeadY + 0.12f, 0.0f), new FVector3(0.14f, 0.06f, 0.14f), Color);
                Kit.Box(new FVector3(0.0f, HeadY + 0.07f, 0.02f), new FVector3(0.17f, 0.012f, 0.18f), Color);
                break;
            case EHeadgear.Bandana:
                Kit.Box(new FVector3(0.0f, HeadY + 0.1f, 0.0f), new FVector3(0.13f, 0.05f, 0.13f), Info.Color);
                Kit.Box(new FVector3(0.0f, HeadY + 0.06f, -0.15f), new FVector3(0.04f, 0.08f, 0.03f), Info.Accent);
                break;
            case EHeadgear.Boonie:
                Kit.Box(new FVector3(0.0f, HeadY + 0.12f, 0.0f), new FVector3(0.13f, 0.05f, 0.13f), Color);
                Kit.Box(new FVector3(0.0f, HeadY + 0.07f, 0.0f), new FVector3(0.21f, 0.012f, 0.21f), Color);
                break;
        }
    }

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
        Kit.Commit(World.Registry, Marker, true, false);
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

    public static void AnimateWalk(STransformComponent? Body, float Speed, ref float Phase, float DeltaTime)
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
            Body.SetLocalTransform(new FTransform(new FVector3(0.0f, Bob, 0.0f), FQuat.FromEuler(Mathf.Radians(Speed * 0.8f), 0.0f, Mathf.Radians(Sway)), Body.GetLocalScale()));
        }
        else if (Phase != 0.0f)
        {
            Phase = 0.0f;
            Body.SetLocalTransform(new FTransform(FVector3.Zero, FQuat.Identity, Body.GetLocalScale()));
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
