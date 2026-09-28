import json
import os
import urllib.request

# Rebuilds every decal material under /Game/Content/Decals through the editor's MCP server.
Endpoint = "http://127.0.0.1:8787/mcp"
Folder = "/Game/Content/Decals"
SourceTextures = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "SourceTextures")
Textures = ["T_Scorch", "T_Scorch_E", "T_BulletHoles", "T_BulletHoles_N", "T_Blood"]
EmberColor = {"X": 6.0, "Y": 1.6, "Z": 0.3, "W": 0.0}


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


def Existing(Name):
    return any(Asset["Name"] == Name for Asset in Call("assets.search", {"Contains": Name})["Results"])


# The generator may have redrawn a sheet, so each import replaces the previous one.
def ImportTextures():
    for Name in Textures:
        if Existing(Name):
            Call("assets.delete", {"Asset": f"{Folder}/{Name}", "bForce": True})
        Call("assets.import", {"Source": os.path.abspath(os.path.join(SourceTextures, Name + ".png")), "DestinationFolder": Folder})


class Graph:
    def __init__(self, Name):
        self.Path = f"{Folder}/{Name}"
        if Existing(Name):
            Call("assets.delete", {"Asset": self.Path, "bForce": True})
        self.Output = Call("material.create", {"Folder": Folder, "Name": Name})["OutputNode"]
        Call("assets.set_property", {"Asset": self.Path, "Path": "MaterialType", "Value": json.dumps("Decal")})

    def Node(self, Type, X, Y, **Values):
        Id = Call("material.add_node", {"Material": self.Path, "NodeType": Type, "X": X, "Y": Y})["ID"]
        for Field, Value in Values.items():
            Call("material.set_node_property", {"Material": self.Path, "Node": Id, "Path": Field, "Value": json.dumps(Value)})
        return Id

    def Texture(self, Name, X, Y):
        return self.Node("CMaterialExpression_TextureSample", X, Y, Texture=f"{Folder}/{Name}")

    def Constant(self, Value, X, Y):
        return self.Node("CMaterialExpression_ConstantFloat", X, Y, Value={"X": Value, "Y": 0.0, "Z": 0.0, "W": 0.0})

    def Link(self, FromNode, FromPin, ToPin, ToNode=None):
        Call("material.connect", {"Material": self.Path, "FromNode": FromNode, "FromPin": FromPin,
                                  "ToNode": self.Output if ToNode is None else ToNode, "ToPin": ToPin})

    def Finish(self):
        Call("material.compile", {"Material": self.Path})
        Call("assets.save", {"Asset": self.Path})
        print("Authored", self.Path)


# A 2x2 sheet of blast marks, the cell picked per decal, with embers the game cools through EmissionEnergy.
def Scorch():
    M = Graph("M_Scorch")
    Color = M.Texture("T_Scorch", -400.0, 0.0)
    Ember = M.Texture("T_Scorch_E", -600.0, 500.0)
    Glow = M.Node("CMaterialExpression_ConstantFloat3", -600.0, 750.0, Value=EmberColor)
    Hot = M.Node("CMaterialExpression_Multiplication", -300.0, 600.0)
    M.Link(Ember, "R", "X", Hot)
    M.Link(Glow, "RGB", "Y", Hot)
    M.Link(Color, "RGBA", "Base Color (RGBA)")
    M.Link(Color, "A", "Opacity")
    M.Link(M.Constant(0.95, -400.0, 250.0), "X", "Roughness")
    M.Link(Hot, "", "Emissive (RGB)")
    M.Finish()


# A 2x2 sheet of bullet holes with a matching crater normal sheet.
def BulletHole():
    M = Graph("M_BulletHole")
    Color = M.Texture("T_BulletHoles", -400.0, 0.0)
    Normal = M.Texture("T_BulletHoles_N", -400.0, 250.0)
    M.Link(Color, "RGBA", "Base Color (RGBA)")
    M.Link(Color, "A", "Opacity")
    M.Link(Normal, "RGBA", "Normal Map (XYZ)")
    M.Link(M.Constant(0.85, -400.0, 500.0), "X", "Roughness")
    M.Finish()


# A 2x2 sheet of splatters, a pool and a spray, with a sheen that stops short of mirroring the sky.
def Blood():
    M = Graph("M_Blood")
    Color = M.Texture("T_Blood", -400.0, 0.0)
    M.Link(Color, "RGBA", "Base Color (RGBA)")
    M.Link(Color, "A", "Opacity")
    M.Link(M.Constant(0.55, -400.0, 250.0), "X", "Roughness")
    M.Finish()


if __name__ == "__main__":
    ImportTextures()
    Scorch()
    BulletHole()
    Blood()
