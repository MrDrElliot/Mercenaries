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


def TextureGuid(Name):
    for Asset in Call("assets.search", {"Contains": Name})["Results"]:
        if Asset["Name"] == Name:
            return Asset["Guid"]
    raise RuntimeError(f"Missing texture {Name}. Import SourceTextures into {Folder}/Textures first.")


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


def Collide(Restitution, Friction, bKill=False):
    return ("SceneCollision", {"Restitution": (Restitution,), "Friction": (Friction,), "bKillOnHit": bKill})


Integrate = ("Integrate", {})


def Emitter(Name, Settings, Modules):
    return {"Name": Name, "Settings": Settings, "Modules": Modules}


def Burst(Count, **Extra):
    return {"SpawnRate": 0.0, "BurstCount": Count, "MaxParticles": Count, "bLooping": False, **Extra}


def Stream(Rate, Max, **Extra):
    return {"SpawnRate": Rate, "BurstCount": 0, "MaxParticles": Max, "bLooping": True, **Extra}


def Effects(Textures):
    Smoke, Flame, Clod = Textures["T_SmokePuff"], Textures["T_Flame"], Textures["T_DirtClod"]

    def Sparks(Count, Speed, Life0, Life1):
        return Emitter("Sparks", Burst(Count, BlendMode="Additive", FacingMode="VelocityAligned", VelocityStretch=0.05, SoftFadeDistance=0.0), [
            Location("Sphere", (0.4, 0.4, 0.4)), BoxVelocity((-Speed, Speed * 0.3, -Speed), (Speed, Speed * 1.6, Speed)),
            Size(0.025, 0.05), Life(Life0, Life1),
            Gravity(0.0, -9.8), Drag(0.6),
            ColorOverLife((0.0, (14.0, 8.0, 3.0, 1.0)), (0.5, (7.0, 2.2, 0.4, 1.0)), (1.0, (1.5, 0.3, 0.05, 0.0))),
            Collide(0.4, 0.3),
            Integrate])

    def Clods(Count, Speed, SizeMax):
        return Emitter("Clods", Burst(Count, BlendMode="Alpha", Texture=Clod, SoftFadeDistance=0.0, bLit=True), [
            Location("Hemisphere", (0.6, 0.6, 0.6)), BoxVelocity((-Speed * 0.5, Speed * 0.5, -Speed * 0.5), (Speed * 0.5, Speed, Speed * 0.5)),
            Size(SizeMax * 0.4, SizeMax), Life(2.5, 3.5), Spin(420.0),
            Gravity(0.0, -9.8),
            ColorOverLife((0.0, (1.0, 1.0, 1.0, 1.0)), (0.85, (1.0, 1.0, 1.0, 1.0)), (1.0, (1.0, 1.0, 1.0, 0.0))),
            Collide(0.25, 0.6),
            Integrate])

    return {
        "P_Explosion": [
            Emitter("Flash", Burst(1, BlendMode="Additive", SoftFadeDistance=1.0), [
                Size(9.0, 9.0), Life(0.16, 0.16),
                ColorOverLife((0.0, (30.0, 20.0, 11.0, 1.0)), (1.0, (6.0, 2.5, 0.8, 0.0))),
                SizeOverLife((0.0, 0.7), (1.0, 1.2)),
                Integrate]),
            Emitter("Fireball", Burst(36, BlendMode="Additive", Texture=Flame, SoftFadeDistance=1.0), [
                Location("Hemisphere", (1.0, 1.0, 1.0)), RadialVelocity(3.0, 9.0),
                Size(1.6, 3.0), Life(0.45, 0.9), Spin(70.0),
                Drag(4.0), Gravity(0.0, 3.0),
                ColorOverLife((0.0, (9.0, 6.5, 3.5, 1.0)), (0.2, (6.0, 2.4, 0.6, 1.0)), (0.6, (1.4, 0.3, 0.05, 0.6)), (1.0, (0.2, 0.04, 0.01, 0.0))),
                SizeOverLife((0.0, 0.5), (0.3, 1.2), (1.0, 1.5)),
                Integrate]),
            Emitter("Smoke", Burst(24, BlendMode="Alpha", Texture=Smoke, SoftFadeDistance=1.0, bLit=True), [
                Location("Hemisphere", (1.2, 1.2, 1.2)), RadialVelocity(1.0, 3.5),
                Size(1.6, 2.6), Life(3.5, 6.0), Spin(15.0),
                Drag(1.5), Gravity(0.0, 1.8), Turbulence((1.0, 0.5, 1.0), 0.25, 0.4),
                ColorOverLife((0.0, (0.1, 0.09, 0.08, 0.0)), (0.05, (0.12, 0.11, 0.1, 1.0)), (0.45, (0.3, 0.29, 0.27, 0.8)), (1.0, (0.5, 0.5, 0.5, 0.0))),
                SizeOverLife((0.0, 0.6), (0.4, 1.3), (1.0, 1.8)),
                Integrate]),
            Sparks(70, 12.0, 0.7, 1.6),
            Clods(24, 14.0, 0.35),
        ],
        "P_Burning": [
            Emitter("Flames", Stream(30.0, 48, BlendMode="Additive", Texture=Flame, SoftFadeDistance=0.6), [
                Location("Disk", (1.0, 0.0, 0.0)), BoxVelocity((-0.3, 1.5, -0.3), (0.3, 3.0, 0.3)),
                Size(0.8, 1.4), Life(0.5, 0.9), Spin(50.0),
                Gravity(0.0, 2.0), Turbulence((1.0, 0.3, 1.0), 0.8, 1.5),
                ColorOverLife((0.0, (4.0, 2.4, 0.8, 0.0)), (0.12, (5.0, 2.4, 0.6, 1.0)), (0.5, (2.5, 0.7, 0.12, 0.7)), (1.0, (0.4, 0.06, 0.01, 0.0))),
                SizeOverLife((0.0, 0.7), (0.4, 1.0), (1.0, 0.3)),
                Integrate]),
            Emitter("Embers", Stream(8.0, 32, BlendMode="Additive", SoftFadeDistance=0.0), [
                Location("Disk", (1.0, 0.0, 0.0)), BoxVelocity((-0.6, 2.0, -0.6), (0.6, 5.0, 0.6)),
                Size(0.03, 0.06), Life(1.5, 3.0),
                Gravity(0.0, 0.5), Turbulence((2.0, 1.0, 2.0), 0.5, 1.0),
                ColorOverLife((0.0, (8.0, 3.0, 0.6, 1.0)), (1.0, (2.0, 0.4, 0.05, 0.0))),
                Integrate]),
            Emitter("Smoke", Stream(9.0, 96, BlendMode="Alpha", Texture=Smoke, SoftFadeDistance=1.0, bLit=True), [
                Location("Disk", (1.2, 0.0, 0.0)), BoxVelocity((-0.4, 2.0, -0.4), (0.4, 3.5, 0.4)),
                Size(1.5, 2.5), Life(6.0, 9.0), Spin(10.0),
                Drag(0.3), Gravity(0.6, 0.4), Turbulence((0.8, 0.2, 0.8), 0.15, 0.3),
                ColorOverLife((0.0, (0.08, 0.08, 0.08, 0.0)), (0.08, (0.1, 0.1, 0.1, 0.8)), (0.6, (0.28, 0.28, 0.28, 0.45)), (1.0, (0.45, 0.45, 0.45, 0.0))),
                SizeOverLife((0.0, 0.5), (1.0, 3.5)),
                Integrate]),
        ],
        "P_CollapseDust": [
            Emitter("Dust", Burst(40, BlendMode="Alpha", Texture=Smoke, SoftFadeDistance=2.0, bLit=True), [
                Location("Box", (6.0, 0.5, 6.0)), BoxVelocity((-4.0, 0.5, -4.0), (4.0, 3.0, 4.0)),
                Size(3.0, 5.0), Life(4.0, 7.0), Spin(12.0),
                Drag(1.2), Gravity(0.0, 0.3), Turbulence((0.8, 0.3, 0.8), 0.12, 0.3),
                ColorOverLife((0.0, (0.6, 0.53, 0.43, 0.0)), (0.1, (0.65, 0.58, 0.48, 0.7)), (1.0, (0.72, 0.68, 0.6, 0.0))),
                SizeOverLife((0.0, 0.6), (1.0, 2.0)),
                Integrate]),
            Clods(30, 9.0, 0.5),
        ],
        "P_ImpactDirt": [
            Emitter("Puff", Burst(4, BlendMode="Alpha", Texture=Smoke, SoftFadeDistance=0.3, bLit=True), [
                ConeVelocity(25.0, 1.0, 3.0),
                Size(0.3, 0.6), Life(0.6, 1.2), Spin(40.0),
                Drag(3.0),
                ColorOverLife((0.0, (0.55, 0.46, 0.35, 0.8)), (1.0, (0.62, 0.55, 0.45, 0.0))),
                SizeOverLife((0.0, 0.5), (1.0, 1.8)),
                Integrate]),
            Emitter("Clods", Burst(6, BlendMode="Alpha", Texture=Clod, SoftFadeDistance=0.0, bLit=True), [
                ConeVelocity(30.0, 3.0, 6.0),
                Size(0.04, 0.09), Life(0.8, 1.2), Spin(500.0),
                Gravity(0.0, -9.8),
                Collide(0.3, 0.5),
                Integrate]),
        ],
        "P_ImpactSparks": [
            Emitter("Flash", Burst(1, BlendMode="Additive", SoftFadeDistance=0.0), [
                Size(0.4, 0.4), Life(0.06, 0.06),
                ColorOverLife((0.0, (20.0, 14.0, 8.0, 1.0)), (1.0, (6.0, 3.0, 1.0, 0.0))),
                Integrate]),
            Emitter("Sparks", Burst(12, BlendMode="Additive", FacingMode="VelocityAligned", VelocityStretch=0.03, SoftFadeDistance=0.0), [
                ConeVelocity(50.0, 4.0, 10.0),
                Size(0.02, 0.04), Life(0.2, 0.5),
                Gravity(0.0, -9.8),
                ColorOverLife((0.0, (14.0, 9.0, 4.0, 1.0)), (1.0, (3.0, 0.8, 0.1, 0.0))),
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
    Textures = {Name: TextureGuid(Name) for Name in ("T_SmokePuff", "T_Flame", "T_DirtClod")}
    Only = set(sys.argv[1:])
    for Name, Emitters in Effects(Textures).items():
        if not Only or Name in Only:
            Author(Name, Emitters)
