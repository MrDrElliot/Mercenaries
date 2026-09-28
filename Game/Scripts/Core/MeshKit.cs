using System;
using System.Collections.Generic;
using Lumina;
using LuminaSharp;

namespace Mercenaries;

public static class Materials
{
    public const string SolidPath = "/Game/Content/Materials/M_VertexColor.lasset";
    public const string GlowPath = "/Game/Content/Materials/M_VertexColorGlow.lasset";

    private static CMaterialInterface? SolidMaterial;
    private static CMaterialInterface? GlowMaterial;
    private static bool bWarned;

    public static CMaterialInterface? Solid => SolidMaterial ??= Load(SolidPath);

    public static CMaterialInterface? Glow => GlowMaterial ??= Load(GlowPath);

    public static void Reset()
    {
        SolidMaterial = null;
        GlowMaterial = null;
    }

    private static CMaterialInterface? Load(string Path)
    {
        CMaterialInterface? Loaded = Asset.Load<CMaterialInterface>(Path);
        if (Loaded is null && !bWarned)
        {
            bWarned = true;
            Debug.LogWarning($"Mercenaries: material '{Path}' is missing, geometry will draw with the engine default.");
        }

        return Loaded;
    }
}

// The engine's mesh builder with this game's two vertex-color materials picked by a glow flag.
public sealed class MeshKit : MeshBuilder
{
    public CStaticMesh? BuildStaticMesh(CWorld World, bool bGlow = false) => BuildStaticMesh(World, bGlow ? Materials.Glow : Materials.Solid);

    public static void Show(EntityRegistry Registry, Entity Target, CStaticMesh? Mesh, bool bCastShadow = true)
    {
        SStaticMeshComponent? Component = Registry.GetOrAdd<SStaticMeshComponent>(Target);
        if (Component is not null && Mesh is not null)
        {
            Component.StaticMesh = Mesh;
            Component.bCastShadow = bCastShadow;
        }
    }

    public bool Commit(EntityRegistry Registry, Entity Target, bool bGlow = false, bool bCastShadow = true)
    {
        SDynamicMeshComponent? Mesh = Registry.GetOrAdd<SDynamicMeshComponent>(Target);
        if (Mesh is null)
        {
            return false;
        }

        Mesh.bGenerateTangents = false;
        Mesh.bFastMeshletBuild = true;
        Mesh.bCastShadow = bCastShadow;
        return CommitTo(Mesh, bGlow ? Materials.Glow : Materials.Solid);
    }
}
