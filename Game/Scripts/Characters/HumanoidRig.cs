using System;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

public enum EBodyPart : byte
{
    Torso,
    Head,
    ArmR,
    ArmL,
    LegR,
    LegL,
    Weapon,
}

// A character drawn as one mesh per limb under a shared Body entity, every part authored in body space.
public sealed class HumanoidRig
{
    public const int PartCount = 7;

    public Entity Body = Entity.Null;
    public float Scale = 1.0f;
    public readonly Entity[] Parts = new Entity[PartCount];
    public readonly CStaticMesh?[] Meshes = new CStaticMesh?[PartCount];
    public readonly FVector3[] Centers = new FVector3[PartCount];
    public readonly FVector3[] Halves = new FVector3[PartCount];

    // Where each limb hangs from the torso, in body space, and how far it may swing there.
    public static readonly (EBodyPart Limb, FVector3 Joint, float SwingDegrees)[] Joints =
    {
        (EBodyPart.Head, new FVector3(0.0f, 1.42f - HumanoidBody.FeetOffset, 0.0f), 45.0f),
        (EBodyPart.ArmR, new FVector3(0.26f, 1.32f - HumanoidBody.FeetOffset, 0.0f), 80.0f),
        (EBodyPart.ArmL, new FVector3(-0.26f, 1.32f - HumanoidBody.FeetOffset, 0.0f), 80.0f),
        (EBodyPart.LegR, new FVector3(0.11f, 0.8f - HumanoidBody.FeetOffset, 0.0f), 55.0f),
        (EBodyPart.LegL, new FVector3(-0.11f, 0.8f - HumanoidBody.FeetOffset, 0.0f), 55.0f),
    };

    public HumanoidRig()
    {
        Array.Fill(Parts, Entity.Null);
    }

    public void SetBounds(EBodyPart Part, FVector3 Center, FVector3 Half)
    {
        Centers[(int)Part] = Center;
        Halves[(int)Part] = Half;
    }

    public void SetSpan(EBodyPart Part, FVector3 From, FVector3 To, float Radius)
    {
        FVector3 Min = FVector3.Min(From, To) - new FVector3(Radius);
        FVector3 Max = FVector3.Max(From, To) + new FVector3(Radius);
        SetBounds(Part, (Min + Max) * 0.5f, (Max - Min) * 0.5f);
    }

    // The limb whose box lies nearest a body-space point, which is the one a bullet struck.
    public EBodyPart PartNearest(FVector3 BodyPoint)
    {
        EBodyPart Best = EBodyPart.Torso;
        float BestDistance = float.MaxValue;
        for (int Index = 0; Index < PartCount; ++Index)
        {
            if (Index == (int)EBodyPart.Weapon || Meshes[Index] is null)
            {
                continue;
            }

            FVector3 Outside = FVector3.Max(FVector3.Abs(BodyPoint - Centers[Index]) - Halves[Index], FVector3.Zero);
            float Distance = Outside.Length;
            if (Distance < BestDistance)
            {
                BestDistance = Distance;
                Best = (EBodyPart)Index;
            }
        }
        return Best;
    }
}
