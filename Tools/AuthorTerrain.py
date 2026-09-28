import json
import os
import urllib.request

# Imports the ground layers GenerateGround.py drew and rebuilds M_Terrain through the editor's MCP server.
Endpoint = "http://127.0.0.1:8787/mcp"
TextureFolder = "/Game/Content/Terrain"
MaterialFolder = "/Game/Content/Materials"
SourceTextures = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "SourceTextures")
SourceMeshes = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "SourceMeshes")
Layers = ["Grass", "Rock", "Sand", "Dirt", "Paved"]

# Sampled per pixel in the order of the game's ETerrainLayer, which is also the order of the painted weights.
TerrainCode = r"""
const float2 P = Pos.xz;
const float Scales[5] = { 0.29, 0.14, 0.2, 0.25, 0.19 };
const float Roughs[5] = { 0.9, 0.8, 0.93, 0.92, 0.84 };
const uint AlbedoMaps[5] = { GrassA, RockA, SandA, DirtA, PavedA };
const uint NormalMaps[5] = { GrassN, RockN, SandN, DirtN, PavedN };

float W[5];
float Painted = 0.0;
[unroll] for (int L = 1; L < 5; ++L)
{
    W[L] = GetTerrainLayerWeight(HeightUV, L);
    Painted += W[L];
}
W[0] = max(GetTerrainLayerWeight(HeightUV, 0), 1.0 - Painted);

const float3 Region = SampleTexture2D(Macro, SAMPLER_LINEAR_WRAP, P / 380.0).rgb;
const float Variation = SampleTexture2D(Macro, SAMPLER_LINEAR_WRAP, P / 47.0).b;
const float2 Dx = ddx(P);
const float2 Dy = ddy(P);

float3 Albedo[5];
float3 Normals[5];
float Height[5];
float Top = -1.0;
[unroll] for (int L = 0; L < 5; ++L)
{
    Albedo[L] = float3(0.0, 0.0, 0.0);
    Normals[L] = float3(0.0, 0.0, 1.0);
    Height[L] = 0.0;
    if (W[L] > 0.004)
    {
        const float S = Scales[L];
        const float2 UV = P * S;
        const float4 Near = SampleTexture2DGrad(AlbedoMaps[L], SAMPLER_ANISO_WRAP, UV, Dx * S, Dy * S);
        const float2x2 Turn = float2x2(0.8, -0.6, 0.6, 0.8) * 0.37;
        const float4 Far = SampleTexture2DGrad(AlbedoMaps[L], SAMPLER_ANISO_WRAP, mul(Turn, UV) + 0.5, mul(Turn, Dx * S), mul(Turn, Dy * S));
        const float Mix = saturate(Variation * 1.4 - 0.2) * 0.6;
        Albedo[L] = lerp(Near.rgb, Far.rgb, Mix);
        Height[L] = lerp(Near.a, Far.a, Mix);
        const float2 Encoded = SampleTexture2DGrad(NormalMaps[L], SAMPLER_ANISO_WRAP, UV, Dx * S, Dy * S).rg * 2.0 - 1.0;
        Normals[L] = float3(Encoded, sqrt(saturate(1.0 - dot(Encoded, Encoded))));
        Top = max(Top, W[L] + Height[L] * 0.5);
    }
}

const float Dry = smoothstep(0.5, 0.85, Region.r);
const float Jungle = smoothstep(60.0, 240.0, P.y) * smoothstep(0.3, 0.65, Region.g);
Albedo[0] *= lerp(float3(1.0, 1.0, 1.0), float3(1.3, 1.12, 0.58), Dry * 0.75);
Albedo[0] *= lerp(float3(1.0, 1.0, 1.0), float3(0.55, 0.78, 0.58), Jungle * 0.85);

float Total = 0.0;
float3 Blended = float3(0.0, 0.0, 0.0);
float3 BlendedNormal = float3(0.0, 0.0, 0.0);
float Rough = 0.0;
[unroll] for (int L = 0; L < 5; ++L)
{
    const float B = W[L] > 0.004 ? max(W[L] + Height[L] * 0.5 - Top + 0.2, 0.0) : 0.0;
    Total += B;
    Blended += Albedo[L] * B;
    BlendedNormal += Normals[L] * B;
    Rough += Roughs[L] * B;
}
Total = max(Total, 1e-4);
Blended /= Total;
Rough /= Total;

const float Wet = 1.0 - smoothstep(0.15, 1.0, Pos.y);
Color = Blended * lerp(0.9, 1.08, Region.b) * lerp(1.0, 0.62, Wet);
Roughness = lerp(Rough, 0.38, Wet);
Normal = normalize(BlendedNormal / Total);
"""


def Call(Tool, Arguments):
    Body = json.dumps({"jsonrpc": "2.0", "id": 1, "method": "tools/call", "params": {"name": Tool, "arguments": Arguments}}).encode()
    Request = urllib.request.Request(Endpoint, data=Body, headers={"Content-Type": "application/json", "Accept": "application/json"})
    with urllib.request.urlopen(Request, timeout=300) as Response:
        Result = json.loads(Response.read().decode())
    if "error" in Result:
        raise RuntimeError(f"{Tool}: {Result['error']['message']}")
    Content = Result["result"]
    Text = "".join(Part.get("text", "") for Part in Content.get("content", []))
    if Content.get("isError"):
        raise RuntimeError(f"{Tool}: {Text}")
    return Content.get("structuredContent", {})


def Existing(Name):
    return any(Asset["Name"] == Name for Asset in Call("assets.search", {"Contains": Name})["Results"])


def ImportTexture(Name, ColorSpace):
    if Existing(Name):
        Call("assets.delete", {"Asset": f"{TextureFolder}/{Name}", "bForce": True})
    Call("assets.import", {"Source": os.path.abspath(os.path.join(SourceTextures, Name + ".png")), "DestinationFolder": TextureFolder,
                           "TextureGroup": "Terrain"})
    Path = f"{TextureFolder}/{Name}"
    Call("assets.set_property", {"Asset": Path, "Path": "ColorSpace", "Value": json.dumps(ColorSpace)})
    Call("assets.save", {"Asset": Path})


def ImportTextures():
    for Layer in Layers:
        ImportTexture(f"T_Ground_{Layer}", "SRGB")
        ImportTexture(f"T_Ground_{Layer}_N", "NormalMap")
    ImportTexture("T_Ground_Macro", "Linear")


def Pin(Name, Type, Default="Zero"):
    return {"Name": Name, "Type": Type, "Default": Default}

# Blades take the ground's tinted grass color under them, at fixed mips because mesh materials also shade in compute.
GrassCode = r"""
const float2 P = Pos.xz;
const float3 Ground = SampleTexture2DLevel(GrassA, SAMPLER_LINEAR_WRAP, P * 0.29, 4.0).rgb;
const float3 Region = SampleTexture2DLevel(Macro, SAMPLER_LINEAR_WRAP, P / 380.0, 0.0).rgb;
const float Dry = smoothstep(0.5, 0.85, Region.r);
const float Jungle = smoothstep(60.0, 240.0, P.y) * smoothstep(0.3, 0.65, Region.g);
float3 Tinted = Ground * lerp(float3(1.0, 1.0, 1.0), float3(1.3, 1.12, 0.58), Dry * 0.75);
Tinted *= lerp(float3(1.0, 1.0, 1.0), float3(0.55, 0.78, 0.58), Jungle * 0.85);
const float Tip = saturate(UV.y);
Color = Tinted * lerp(0.5, 1.15, Tip) * lerp(0.9, 1.08, Region.b);
"""

SwayCode = r"""
const float Tip = saturate(UV.y);
Mask = Tip * Tip * 0.45;
"""


def ImportGrassMesh():
    if Existing("SM_GrassClump"):
        Call("assets.delete", {"Asset": f"{TextureFolder}/SM_GrassClump", "bForce": True})
    Call("assets.import", {"Source": os.path.abspath(os.path.join(SourceMeshes, "SM_GrassClump.obj")), "DestinationFolder": TextureFolder})


def NewMaterial(Name, MaterialType):
    Path = f"{MaterialFolder}/{Name}"
    if Existing(Name):
        Call("assets.delete", {"Asset": Path, "bForce": True})
    Output = Call("material.create", {"Folder": MaterialFolder, "Name": Name})["OutputNode"]
    Call("assets.set_property", {"Asset": Path, "Path": "MaterialType", "Value": json.dumps(MaterialType)})

    def Node(Type, X, Y, **Values):
        Added = Call("material.add_node", {"Material": Path, "NodeType": Type, "X": X, "Y": Y})
        for Field, Value in Values.items():
            Call("material.set_node_property", {"Material": Path, "Node": Added["ID"], "Path": Field, "Value": json.dumps(Value)})
        return Added["ID"]

    def Link(FromNode, FromPin, ToNode, ToPin):
        Call("material.connect", {"Material": Path, "FromNode": FromNode, "FromPin": FromPin, "ToNode": ToNode, "ToPin": ToPin})

    def Finish():
        Compiled = Call("material.compile", {"Material": Path})
        if not Compiled.get("bSucceeded"):
            raise RuntimeError(f"{Name} failed to compile: {Compiled}")
        Call("assets.save", {"Asset": Path})
        print("Authored", Path)

    return Path, Output, Node, Link, Finish


def AuthorGrass():
    ImportGrassMesh()
    Path, Output, Node, Link, Finish = NewMaterial("M_Grass", "PBR")

    Inputs = [Pin("Pos", "Float3", "WorldPosition"), Pin("UV", "Float2", "UV0"), Pin("GrassA", "TextureHandle"), Pin("Macro", "TextureHandle")]
    Shade = Node("CMaterialExpression_CustomSlang", -500.0, 0.0, Title="Grass Color", Inputs=Inputs, Outputs=[Pin("Color", "Float3")], Code=GrassCode)
    Link(Node("CMaterialExpression_TextureHandle", -900.0, 0.0, Texture=f"{TextureFolder}/T_Ground_Grass"), "Handle", Shade, "GrassA")
    Link(Node("CMaterialExpression_TextureHandle", -900.0, 120.0, Texture=f"{TextureFolder}/T_Ground_Macro"), "Handle", Shade, "Macro")
    Link(Shade, "Color", Output, "Base Color (RGBA)")
    Link(Node("CMaterialExpression_ConstantFloat", -500.0, 250.0, Value={"X": 0.8, "Y": 0.0, "Z": 0.0, "W": 0.0}), "X", Output, "Roughness")
    Link(Node("CMaterialExpression_ConstantFloat", -500.0, 330.0, Value={"X": 0.3, "Y": 0.0, "Z": 0.0, "W": 0.0}), "X", Output, "Specular")

    Sway = Node("CMaterialExpression_CustomSlang", -900.0, 450.0, Title="Sway Mask", Inputs=[Pin("UV", "Float2", "UV0")], Outputs=[Pin("Mask", "Float")], Code=SwayCode)
    Wind = Node("CMaterialExpression_WindAnimation", -500.0, 450.0)
    Link(Sway, "Mask", Wind, "Mask")
    Link(Wind, "Offset", Output, "World Position Offset (XYZ)")
    Finish()

    Mesh = f"{TextureFolder}/SM_GrassClump"
    Call("assets.set_property", {"Asset": Mesh, "Path": "Materials[0]", "Value": json.dumps(Path)})
    Call("assets.save", {"Asset": Mesh})

    Type = f"{TextureFolder}/GT_Grass"
    if Existing("GT_Grass"):
        Call("assets.delete", {"Asset": Type, "bForce": True})
    Call("assets.create", {"ClassName": "CGrassType", "Folder": TextureFolder, "Name": "GT_Grass"})
    Settings = {"Mesh": Mesh, "Density": 6.0, "MinWeight": 0.5, "ScaleMin": 0.65, "ScaleMax": 1.15, "AlignToNormal": 0.5,
                "MaxSlopeDegrees": 32.0, "CullDistance": 60.0, "bCastShadow": False, "bReceiveShadow": True, "Seed": 7}
    for Field, Value in Settings.items():
        Call("assets.set_property", {"Asset": Type, "Path": Field, "Value": json.dumps(Value)})
    Call("assets.save", {"Asset": Type})
    return Type


def AuthorMaterial(GrassType):
    Path = f"{MaterialFolder}/M_Terrain"
    if Existing("M_Terrain"):
        Call("assets.delete", {"Asset": Path, "bForce": True})
    Output = Call("material.create", {"Folder": MaterialFolder, "Name": "M_Terrain"})["OutputNode"]
    Call("assets.set_property", {"Asset": Path, "Path": "MaterialType", "Value": json.dumps("Terrain")})

    def Node(Type, X, Y, **Values):
        Added = Call("material.add_node", {"Material": Path, "NodeType": Type, "X": X, "Y": Y})
        for Field, Value in Values.items():
            Call("material.set_node_property", {"Material": Path, "Node": Added["ID"], "Path": Field, "Value": json.dumps(Value)})
        return Added

    def Link(FromNode, FromPin, ToNode, ToPin):
        Call("material.connect", {"Material": Path, "FromNode": FromNode, "FromPin": FromPin, "ToNode": ToNode, "ToPin": ToPin})

    Handles = []
    for Layer in Layers:
        Handles.append((f"{Layer}A", f"T_Ground_{Layer}"))
        Handles.append((f"{Layer}N", f"T_Ground_{Layer}_N"))
    Handles.append(("Macro", "T_Ground_Macro"))

    Inputs = [Pin("Pos", "Float3", "WorldPosition")] + [Pin(Name, "TextureHandle") for Name, _ in Handles]
    Outputs = [Pin("Color", "Float3"), Pin("Roughness", "Float"), Pin("Normal", "Float3")]
    Custom = Node("CMaterialExpression_CustomSlang", -500.0, 0.0, Title="Terrain Layers", Inputs=Inputs, Outputs=Outputs, Code=TerrainCode)

    for Index, (Name, Texture) in enumerate(Handles):
        Handle = Node("CMaterialExpression_TextureHandle", -900.0, -500.0 + Index * 90.0, Texture=f"{TextureFolder}/{Texture}")
        OutPin = [P for P in Handle.get("Pins", []) if P.get("Direction", "Output") == "Output"]
        Link(Handle["ID"], OutPin[0]["Name"] if OutPin else "Handle", Custom["ID"], Name)

    Link(Custom["ID"], "Color", Output, "Base Color (RGBA)")
    Link(Custom["ID"], "Roughness", Output, "Roughness")
    Link(Custom["ID"], "Normal", Output, "Normal Map (XYZ)")

    Node("CMaterialExpression_GrassOutput", -500.0, 600.0, Entries=[{"GrassType": GrassType, "LayerIndex": 0, "DensityScale": 1.0}])

    Compiled = Call("material.compile", {"Material": Path})
    print(json.dumps(Compiled)[:2000])
    Call("assets.save", {"Asset": Path})
    print("Authored", Path)


if __name__ == "__main__":
    ImportTextures()
    AuthorMaterial(AuthorGrass())
