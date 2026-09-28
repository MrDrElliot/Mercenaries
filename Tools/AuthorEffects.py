import json
import sys
import urllib.request

# Rebuilds every particle system under /Game/Content/Effects through the editor's MCP server.
Endpoint = "http://127.0.0.1:8787/mcp"
Folder = "/Game/Content/Effects"


def Call(Tool, Arguments):
    Body = json.dumps({"jsonrpc": "2.0", "id": 1, "method": "tools/call", "params": {"name": Tool, "arguments": Arguments}}).encode()
    Request = urllib.request.Request(Endpoint, data=Body, headers={"Content-Type": "application/json", "Accept": "application/json"})
    with urllib.request.urlopen(Request, timeout=120) as Response:
        Result = json.loads(Response.read().decode())
    if "error" in Result:
        raise RuntimeError(f"{Tool}: {Result['error']['message']}")
    Content = Result["result"]
    Text = "".join(Part.get("text", "") for Part in Content.get("content", []))
    if Content.get("isError"):
        raise RuntimeError(f"{Tool}: {Text}")
    return Content.get("structuredContent", {})


def Vec(X, Y=0.0, Z=0.0, W=0.0):
    return {"X": X, "Y": Y, "Z": Z, "W": W}


def Gradient(*Keys):
    return {"Keys": [{"Time": Time, "Color": Vec(*Color)} for Time, Color in Keys]}


def Curve(*Keys):
    return {"Keys": [{"Time": Time, "Value": Value} for Time, Value in Keys]}


def AssetGuid(Name):
    for Asset in Call("assets.search", {"Contains": Name})["Results"]:
        if Asset["Name"] == Name:
            return Asset["Guid"]
    raise RuntimeError(f"Missing {Name}. Run Tools/GenerateTextures.py and Tools/GenerateMeshes.py, then import SourceTextures and SourceMeshes under {Folder}.")


# A tuple sets that input's constant, and any other value sets the field as is.
def Location(Shape, Size, ConeAngle=None):
    Inputs = {"Shape": Shape, "ShapeSize": Size}
    if ConeAngle is not None:
        Inputs["ConeAngle"] = (ConeAngle,)
    return ("SpawnLocation", Inputs)


def RadialVelocity(MinSpeed, MaxSpeed):
    return ("InitialVelocity", {"Mode": "Radial", "SpeedRange": (MinSpeed, MaxSpeed)})


def ConeVelocity(Angle, MinSpeed, MaxSpeed):
    return ("InitialVelocity", {"Mode": "Cone", "ConeAngle": (Angle,), "SpeedRange": (MinSpeed, MaxSpeed)})


def BoxVelocity(Min, Max):
    return ("InitialVelocity", {"Mode": "Explicit", "VelocityMin": Min, "VelocityMax": Max})


def Size(Min, Max):
    return ("InitialSize", {"SizeRange": (Min, Max)})


def Life(Min, Max):
    return ("Lifetime", {"LifetimeRange": (Min, Max)})


def Spin(MaxSpeed):
    return ("InitialRotation", {"RotationRange": (0.0, 360.0), "RotationSpeedRange": (-MaxSpeed, MaxSpeed)})


def Gravity(X, Y, Z=0.0):
    return ("GravityForce", {"Gravity": (X, Y, Z)})


def Drag(Amount):
    return ("Drag", {"Drag": (Amount,)})


def Turbulence(Strength, Scale, Speed):
    return ("CurlNoiseForce", {"Strength": Strength, "Scale": (Scale,), "Speed": (Speed,)})


def ColorOverLife(*Keys):
    return ("ColorOverLife", {"Gradient": Gradient(*Keys)})


def SizeOverLife(*Keys):
    return ("SizeOverLife", {"Curve.Curve": Curve(*Keys)})


def Collide(Restitution, Friction, bKill=False, Thickness=0.5):
    return ("SceneCollision", {"Restitution": (Restitution,), "Friction": (Friction,), "Thickness": (Thickness,), "bKillOnHit": bKill})


Integrate = ("Integrate", {})


def Emitter(Name, Settings, Modules):
    return {"Name": Name, "Settings": Settings, "Modules": Modules}


def Burst(Count, **Extra):
    return {"SpawnRate": 0.0, "BurstCount": Count, "MaxParticles": Count, "bLooping": False, **Extra}


def Stream(Rate, Max, **Extra):
    return {"SpawnRate": Rate, "BurstCount": 0, "MaxParticles": Max, "bLooping": True, **Extra}


def ShapeCollide(Restitution, Friction, Radius, bKill=False):
    return ("ShapeCollision", {"Restitution": (Restitution,), "Friction": (Friction,), "Radius": (Radius,), "bCollideWithTerrain": True, "bKillOnHit": bKill})


def Tint(R, G, B, A=1.0):
    return ("InitialColor", {"Color": (R, G, B, A)})


# Script-fed emitters take position and velocity from EmitParticle, so they spawn nothing themselves and are never culled.
def Pool(Max, **Extra):
    return {"SpawnRate": 0.0, "BurstCount": 0, "MaxParticles": Max, "bLooping": True, "VisibilityRadius": 0.0, **Extra}


def Effects(Assets):
    Smoke, Flame = Assets["T_SmokePuff"], Assets["T_Flame"]
    Drop = Assets["T_BloodDrop"]
    Rock, Casing = Assets["SM_RockChunk"], Assets["SM_Casing"]

    def Sparks(Count, Speed, Life0, Life1):
        return Emitter("Sparks", Burst(Count, BlendMode="Additive", FacingMode="VelocityAligned", VelocityStretch=0.05, SoftFadeDistance=0.0), [
            Location("Sphere", (0.4, 0.4, 0.4)), BoxVelocity((-Speed, Speed * 0.3, -Speed), (Speed, Speed * 1.6, Speed)),
            Size(0.025, 0.05), Life(Life0, Life1),
            Gravity(0.0, -9.8), Drag(0.6),
            ColorOverLife((0.0, (14.0, 8.0, 3.0, 1.0)), (0.5, (7.0, 2.2, 0.4, 1.0)), (1.0, (1.5, 0.3, 0.05, 0.0))),
            ShapeCollide(0.4, 0.3, 0.02),
            Integrate])

    # Lit rock chunks that bounce on the terrain, drawn as meshes so they read as debris rather than dots.
    def Chunks(Count, Speed, SizeMax, Life0=3.0, Life1=4.5):
        return Emitter("Chunks", Burst(Count, BlendMode="Alpha", RenderMode="Mesh", Mesh=Rock, bLit=True, bWriteDepth=True, bCastShadows=True,
                                       SoftFadeDistance=0.0, SortMode="None"), [
            Location("Hemisphere", (0.6, 0.6, 0.6)), BoxVelocity((-Speed * 0.5, Speed * 0.5, -Speed * 0.5), (Speed * 0.5, Speed, Speed * 0.5)),
            Size(SizeMax * 0.4, SizeMax), Life(Life0, Life1), Spin(420.0),
            Tint(0.42, 0.34, 0.26), Gravity(0.0, -9.8),
            ShapeCollide(0.25, 0.6, SizeMax * 0.4),
            ("DampingOverLife", {"Damping": (0.3,)}),
            Integrate])

    return {
        "P_Explosion": [
            Emitter("Flash", Burst(1, BlendMode="Additive", SoftFadeDistance=1.0, CameraFadeDistance=4.0), [
                Size(6.0, 6.0), Life(0.09, 0.09),
                ColorOverLife((0.0, (12.0, 8.0, 4.0, 1.0)), (1.0, (3.0, 1.2, 0.3, 0.0))),
                SizeOverLife((0.0, 0.6), (1.0, 1.3)),
                Integrate]),
            # Alpha blended so the billows keep their shape and cool from yellow to soot, where additive sprites sum to a white dome.
            Emitter("Fireball", Burst(22, BlendMode="Alpha", Texture=Flame, SoftFadeDistance=1.5, CameraFadeDistance=4.0), [
                Location("Hemisphere", (1.2, 1.2, 1.2)), RadialVelocity(2.5, 7.5),
                Size(2.2, 3.6), Life(0.7, 1.3), Spin(50.0),
                Drag(3.0), Gravity(0.0, 4.0), Turbulence((1.0, 0.6, 1.0), 0.3, 0.8),
                ColorOverLife((0.0, (7.0, 4.6, 1.8, 1.0)), (0.12, (4.5, 1.9, 0.45, 1.0)), (0.35, (1.6, 0.45, 0.08, 0.95)),
                              (0.6, (0.25, 0.08, 0.03, 0.85)), (1.0, (0.05, 0.04, 0.035, 0.0))),
                SizeOverLife((0.0, 0.45), (0.25, 1.1), (1.0, 1.6)),
                Integrate]),
            Emitter("Smoke", Burst(30, BlendMode="Alpha", Texture=Smoke, SoftFadeDistance=1.5, bLit=True, bCastShadows=True, CameraFadeDistance=3.0), [
                Location("Hemisphere", (1.6, 1.6, 1.6)), RadialVelocity(1.0, 3.0),
                Size(2.4, 3.8), Life(5.0, 9.0), Spin(12.0),
                Drag(1.2), Gravity(0.0, 2.4), Turbulence((1.0, 0.5, 1.0), 0.2, 0.35),
                ColorOverLife((0.0, (0.05, 0.045, 0.04, 0.0)), (0.08, (0.07, 0.065, 0.06, 0.95)), (0.5, (0.2, 0.19, 0.18, 0.7)), (1.0, (0.38, 0.37, 0.36, 0.0))),
                SizeOverLife((0.0, 0.5), (0.35, 1.4), (1.0, 2.4)),
                Integrate]),
            # A low ring of dust thrown out along the ground, which is what sells the blast's size.
            Emitter("DustRing", Burst(28, BlendMode="Alpha", Texture=Smoke, SoftFadeDistance=1.0, bLit=True, CameraFadeDistance=3.0), [
                Location("Disk", (1.5, 0.0, 0.0)), RadialVelocity(8.0, 14.0),
                Size(1.2, 2.0), Life(1.2, 2.2), Spin(20.0),
                Drag(3.5), Gravity(0.0, 0.4),
                ColorOverLife((0.0, (0.32, 0.27, 0.2, 0.0)), (0.08, (0.35, 0.3, 0.22, 0.8)), (1.0, (0.45, 0.4, 0.33, 0.0))),
                SizeOverLife((0.0, 0.4), (1.0, 2.2)),
                Integrate]),
            Sparks(70, 12.0, 0.7, 1.6),
            Chunks(16, 14.0, 0.35),
        ],
        "P_Burning": [
            Emitter("Flames", Stream(30.0, 48, BlendMode="Additive", Texture=Flame, SoftFadeDistance=0.6, FacingMode="VerticalAxis"), [
                Location("Disk", (1.0, 0.0, 0.0)), BoxVelocity((-0.3, 1.5, -0.3), (0.3, 3.0, 0.3)),
                Size(0.8, 1.4), Life(0.5, 0.9),
                Gravity(0.0, 2.0), Turbulence((1.0, 0.3, 1.0), 0.8, 1.5),
                ColorOverLife((0.0, (4.0, 2.4, 0.8, 0.0)), (0.12, (5.0, 2.4, 0.6, 1.0)), (0.5, (2.5, 0.7, 0.12, 0.7)), (1.0, (0.4, 0.06, 0.01, 0.0))),
                ("ScaleOverLife", {"Width": {"Curve": Curve((0.0, 0.8), (1.0, 0.4))}, "Height": {"Curve": Curve((0.0, 1.0), (0.5, 1.4), (1.0, 0.6))}}),
                SizeOverLife((0.0, 0.7), (0.4, 1.0), (1.0, 0.3)),
                Integrate]),
            Emitter("Embers", Stream(8.0, 64, BlendMode="Additive", SoftFadeDistance=0.0, RenderMode="Ribbon", RibbonSegments=6, RibbonLength=0.25), [
                Location("Disk", (1.0, 0.0, 0.0)), BoxVelocity((-0.6, 2.0, -0.6), (0.6, 5.0, 0.6)),
                Size(0.03, 0.05), Life(1.5, 3.0),
                Gravity(0.0, 0.5), Turbulence((2.0, 1.0, 2.0), 0.5, 1.0),
                ColorOverLife((0.0, (8.0, 3.0, 0.6, 1.0)), (1.0, (2.0, 0.4, 0.05, 0.0))),
                Integrate]),
            Emitter("Smoke", Stream(9.0, 96, BlendMode="Alpha", Texture=Smoke, SoftFadeDistance=1.0, bLit=True, bCastShadows=True, PrewarmTime=4.0, VisibilityRadius=40.0), [
                Location("Disk", (1.2, 0.0, 0.0)), BoxVelocity((-0.4, 2.0, -0.4), (0.4, 3.5, 0.4)),
                Size(1.5, 2.5), Life(6.0, 9.0), Spin(10.0),
                Drag(0.3), Gravity(0.6, 0.4), Turbulence((0.8, 0.2, 0.8), 0.15, 0.3),
                ColorOverLife((0.0, (0.08, 0.08, 0.08, 0.0)), (0.08, (0.1, 0.1, 0.1, 0.8)), (0.6, (0.28, 0.28, 0.28, 0.45)), (1.0, (0.45, 0.45, 0.45, 0.0))),
                SizeOverLife((0.0, 0.5), (1.0, 3.5)),
                Integrate]),
        ],
        "P_CollapseDust": [
            Emitter("Dust", Burst(40, BlendMode="Alpha", Texture=Smoke, SoftFadeDistance=2.0, bLit=True, bCastShadows=True), [
                Location("Box", (6.0, 0.5, 6.0)), BoxVelocity((-4.0, 0.5, -4.0), (4.0, 3.0, 4.0)),
                Size(3.0, 5.0), Life(4.0, 7.0), Spin(12.0),
                Drag(1.2), Gravity(0.0, 0.3), Turbulence((0.8, 0.3, 0.8), 0.12, 0.3),
                ColorOverLife((0.0, (0.6, 0.53, 0.43, 0.0)), (0.1, (0.65, 0.58, 0.48, 0.7)), (1.0, (0.72, 0.68, 0.6, 0.0))),
                SizeOverLife((0.0, 0.6), (1.0, 2.0)),
                Integrate]),
            Chunks(24, 9.0, 0.5),
        ],
        # Emitter order must match EPoolEmitter in FxSystem.cs.
        "P_EffectPool": [
            Emitter("Dust", Pool(512, BlendMode="Alpha", Texture=Smoke, SoftFadeDistance=0.3, bLit=True), [
                Size(0.3, 0.6), Life(0.6, 1.2), Spin(40.0), Tint(0.55, 0.46, 0.35, 0.8),
                Drag(3.0),
                ("ColorOverLife", {"Gradient": Gradient((0.0, (1.0, 1.0, 1.0, 1.0)), (1.0, (1.1, 1.1, 1.1, 0.0))), "bScaleSpawnColor": True}),
                SizeOverLife((0.0, 0.5), (1.0, 1.8)),
                Integrate]),
            Emitter("Clods", Pool(512, BlendMode="Alpha", RenderMode="Mesh", Mesh=Rock, bLit=True, bWriteDepth=True, SoftFadeDistance=0.0, SortMode="None"), [
                Size(0.04, 0.09), Life(1.5, 2.5), Spin(500.0), Tint(0.4, 0.32, 0.24),
                Gravity(0.0, -9.8), ShapeCollide(0.3, 0.5, 0.03),
                Integrate]),
            Emitter("Sparks", Pool(1024, BlendMode="Additive", FacingMode="VelocityAligned", VelocityStretch=0.03, SoftFadeDistance=0.0, SortMode="None"), [
                Size(0.02, 0.04), Life(0.2, 0.5),
                Gravity(0.0, -9.8), ShapeCollide(0.4, 0.3, 0.01),
                ColorOverLife((0.0, (14.0, 9.0, 4.0, 1.0)), (1.0, (3.0, 0.8, 0.1, 0.0))),
                Integrate]),
            Emitter("Flash", Pool(128, BlendMode="Additive", SoftFadeDistance=0.0, SortMode="None"), [
                Size(0.4, 0.4), Life(0.06, 0.06),
                ColorOverLife((0.0, (20.0, 14.0, 8.0, 1.0)), (1.0, (6.0, 3.0, 1.0, 0.0))),
                Integrate]),
            Emitter("Casings", Pool(256, BlendMode="Alpha", RenderMode="Mesh", Mesh=Casing, bLit=True, bWriteDepth=True, SoftFadeDistance=0.0, SortMode="None"), [
                Size(0.12, 0.12), Life(3.0, 4.0), Spin(900.0), Tint(0.95, 0.7, 0.3),
                Gravity(0.0, -9.8), ShapeCollide(0.35, 0.4, 0.01),
                ("DampingOverLife", {"Damping": (0.5,)}),
                Integrate]),
            Emitter("Trail", Pool(1024, BlendMode="Alpha", Texture=Smoke, SoftFadeDistance=0.5, bLit=True), [
                Size(0.5, 0.7), Life(1.0, 1.6), Spin(40.0), Tint(0.8, 0.8, 0.8, 0.7),
                Drag(1.0), Gravity(0.0, 0.6), Turbulence((0.6, 0.3, 0.6), 0.5, 0.5),
                ("ColorOverLife", {"Gradient": Gradient((0.0, (1.0, 1.0, 1.0, 1.0)), (1.0, (1.0, 1.0, 1.0, 0.0))), "bScaleSpawnColor": True}),
                SizeOverLife((0.0, 0.4), (1.0, 2.2)),
                Integrate]),
            # Droplets die where they land and report it, and the game stains those spots with decals, which lie on slopes and walls where a sprite cannot.
            Emitter("BloodSpray", Pool(4096, BlendMode="Alpha", Texture=Drop, FacingMode="VelocityAligned", VelocityStretch=0.025, SoftFadeDistance=0.0,
                                       SortMode="None", bLit=True, bReportCollisions=True), [
                Size(0.025, 0.06), Life(1.2, 2.0), Tint(0.3, 0.012, 0.018, 1.0),
                Gravity(0.0, -9.8), Drag(0.35),
                Collide(0.0, 1.0, bKill=True, Thickness=0.15),
                Integrate]),
            Emitter("BloodMist", Pool(512, BlendMode="Alpha", Texture=Smoke, SoftFadeDistance=0.3, bLit=True, SortMode="None"), [
                Size(0.25, 0.5), Life(0.35, 0.7), Spin(90.0), Tint(0.35, 0.02, 0.025, 0.85),
                Drag(4.0), Gravity(0.0, -0.6),
                ("ColorOverLife", {"Gradient": Gradient((0.0, (1.0, 1.0, 1.0, 1.0)), (1.0, (0.8, 0.8, 0.8, 0.0))), "bScaleSpawnColor": True}),
                SizeOverLife((0.0, 0.5), (1.0, 1.8)),
                Integrate]),
            Emitter("Gore", Pool(512, BlendMode="Alpha", RenderMode="Mesh", Mesh=Rock, bLit=True, bWriteDepth=True, bCastShadows=True, SoftFadeDistance=0.0, SortMode="None",
                                 bReportCollisions=True), [
                Size(0.05, 0.14), Life(8.0, 12.0), Spin(600.0), Tint(0.36, 0.05, 0.05),
                Gravity(0.0, -9.8), Collide(0.15, 0.8),
                ("DampingOverLife", {"Damping": (0.4,)}),
                Integrate]),
            # A round in flight, stretched along its velocity and killed on the first surface it meets in view.
            Emitter("Tracer", Pool(512, BlendMode="Additive", FacingMode="VelocityAligned", VelocityStretch=0.012, SoftFadeDistance=0.0, SortMode="None"), [
                Size(0.05, 0.05), Life(0.4, 0.4),
                Collide(0.0, 0.0, bKill=True, Thickness=0.5),
                ColorOverLife((0.0, (16.0, 10.0, 4.0, 1.0)), (1.0, (9.0, 4.5, 1.2, 1.0))),
                Integrate]),
            Emitter("Muzzle", Pool(256, BlendMode="Additive", Texture=Flame, SoftFadeDistance=0.0, SortMode="None"), [
                Size(0.5, 0.5), Life(0.05, 0.07), Spin(0.0),
                ColorOverLife((0.0, (18.0, 11.0, 4.0, 1.0)), (1.0, (6.0, 2.5, 0.6, 0.0))),
                SizeOverLife((0.0, 0.7), (1.0, 1.2)),
                Integrate]),
            Emitter("Flame", Pool(768, BlendMode="Additive", Texture=Flame, SoftFadeDistance=0.4, FacingMode="VerticalAxis"), [
                Size(1.0, 1.0), Life(0.4, 0.7),
                Gravity(0.0, 1.5), Turbulence((1.0, 0.3, 1.0), 0.8, 1.5),
                ColorOverLife((0.0, (4.0, 2.4, 0.8, 0.0)), (0.12, (5.0, 2.4, 0.6, 1.0)), (0.5, (2.5, 0.7, 0.12, 0.7)), (1.0, (0.4, 0.06, 0.01, 0.0))),
                SizeOverLife((0.0, 0.7), (0.4, 1.0), (1.0, 0.4)),
                Integrate]),
            # Script picks the tint, soot or dust, and the size the puff grows to; these two differ only in how long they linger.
            Emitter("Smoke", Pool(1024, BlendMode="Alpha", Texture=Smoke, SoftFadeDistance=0.8, bLit=True), [
                Size(1.0, 1.0), Life(1.4, 2.6), Spin(30.0),
                Drag(0.6), Gravity(0.0, 0.4), Turbulence((0.6, 0.3, 0.6), 0.3, 0.4),
                ("ColorOverLife", {"Gradient": Gradient((0.0, (1.0, 1.0, 1.0, 0.0)), (0.12, (1.0, 1.0, 1.0, 1.0)), (1.0, (1.15, 1.15, 1.15, 0.0))), "bScaleSpawnColor": True}),
                SizeOverLife((0.0, 0.4), (1.0, 1.0)),
                Integrate]),
            Emitter("SmokeLong", Pool(768, BlendMode="Alpha", Texture=Smoke, SoftFadeDistance=1.0, bLit=True), [
                Size(1.0, 1.0), Life(4.0, 7.0), Spin(20.0),
                Drag(0.4), Gravity(0.0, 0.6), Turbulence((0.8, 0.3, 0.8), 0.2, 0.3),
                ("ColorOverLife", {"Gradient": Gradient((0.0, (1.0, 1.0, 1.0, 0.0)), (0.08, (1.0, 1.0, 1.0, 1.0)), (1.0, (1.2, 1.2, 1.2, 0.0))), "bScaleSpawnColor": True}),
                SizeOverLife((0.0, 0.35), (1.0, 1.0)),
                Integrate]),
        ],
    }


def InputValue(Value):
    if isinstance(Value, tuple):
        return Vec(*Value)
    return Value


def ApplyModule(System, Index, Module):
    Type, Inputs = Module
    Added = Call("particle.add_module", {"System": System, "Emitter": Index, "ModuleType": Type})
    for Input, Value in Inputs.items():
        Path = f"{Input}.Constant" if isinstance(Value, tuple) else Input
        Call("particle.set_module_property", {"System": System, "Emitter": Index, "Stage": Added["Stage"],
                                              "Index": Added["Index"], "Path": Path, "Value": InputValue(Value)})


def Author(Name, Emitters):
    for Asset in Call("assets.search", {"Contains": Name})["Results"]:
        if Asset["Name"] == Name:
            Call("assets.delete", {"Asset": Asset["Guid"], "bForce": True})

    System = Call("particle.create", {"Folder": Folder, "Name": Name, "bStarterStack": False})["Guid"]
    for Index, Spec in enumerate(Emitters):
        if Index > 0:
            Call("particle.add_emitter", {"System": System, "Name": Spec["Name"], "bStarterStack": False})
        Settings = {"EmitterName": Spec["Name"], **Spec["Settings"]}
        for Path, Value in Settings.items():
            Call("particle.set_emitter_property", {"System": System, "Emitter": Index, "Path": Path, "Value": Value})
        for Module in Spec["Modules"]:
            ApplyModule(System, Index, Module)

    Compiled = Call("particle.compile", {"System": System})
    if not Compiled.get("bSucceeded"):
        raise RuntimeError(f"{Name} failed to compile: {Compiled.get('Errors')}")
    Call("assets.save", {"Asset": System})
    print(f"{Name}: {len(Emitters)} emitters")


if __name__ == "__main__":
    Assets = {Name: AssetGuid(Name) for Name in ("T_SmokePuff", "T_Flame", "T_BloodDrop", "SM_RockChunk", "SM_Casing")}
    Only = set(sys.argv[1:])
    for Name, Emitters in Effects(Assets).items():
        if not Only or Name in Only:
            Author(Name, Emitters)
