from AuthorTerrain import NewMaterial, Pin

# Vertex color like M_VertexColor, plus wind scaled by vertex alpha, which the game's foliage builder paints per vertex.
ColorCode = r"""
Color = Tint.rgb;
"""

SwayCode = r"""
Mask = Tint.a;
"""


def AuthorFoliage():
    Path, Output, Node, Link, Finish = NewMaterial("M_Foliage", "PBR")
    Shade = Node("CMaterialExpression_CustomSlang", -500.0, 0.0, Title="Foliage Color",
                 Inputs=[Pin("Tint", "Float4", "VertexColor")], Outputs=[Pin("Color", "Float3")], Code=ColorCode)
    Link(Shade, "Color", Output, "Base Color (RGBA)")
    Link(Node("CMaterialExpression_ConstantFloat", -500.0, 200.0, Value={"X": 0.8, "Y": 0.0, "Z": 0.0, "W": 0.0}), "X", Output, "Roughness")
    Link(Node("CMaterialExpression_ConstantFloat", -500.0, 280.0, Value={"X": 0.35, "Y": 0.0, "Z": 0.0, "W": 0.0}), "X", Output, "Specular")

    Sway = Node("CMaterialExpression_CustomSlang", -900.0, 450.0, Title="Sway Mask",
                Inputs=[Pin("Tint", "Float4", "VertexColor")], Outputs=[Pin("Mask", "Float")], Code=SwayCode)
    Wind = Node("CMaterialExpression_WindAnimation", -500.0, 450.0)
    Link(Sway, "Mask", Wind, "Mask")
    Link(Wind, "Offset", Output, "World Position Offset (XYZ)")
    Finish()


if __name__ == "__main__":
    AuthorFoliage()
