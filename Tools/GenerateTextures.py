import os
import sys
import numpy as np
from PIL import Image

Size = 256
Rng = np.random.default_rng(2008)
OutDir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "SourceTextures")


def ValueNoise(Cells):
    Grid = Rng.uniform(0.0, 1.0, (Cells + 1, Cells + 1))
    Coords = np.linspace(0.0, Cells, Size, endpoint=False)
    X, Y = np.meshgrid(Coords, Coords)
    X0, Y0 = X.astype(int), Y.astype(int)
    Fx, Fy = X - X0, Y - Y0
    Fx, Fy = Fx * Fx * (3.0 - 2.0 * Fx), Fy * Fy * (3.0 - 2.0 * Fy)
    Top = Grid[Y0, X0] * (1.0 - Fx) + Grid[Y0, X0 + 1] * Fx
    Bottom = Grid[Y0 + 1, X0] * (1.0 - Fx) + Grid[Y0 + 1, X0 + 1] * Fx
    return Top * (1.0 - Fy) + Bottom * Fy


def Fbm(Octaves, BaseCells):
    Total, Amplitude, Norm = np.zeros((Size, Size)), 1.0, 0.0
    for Octave in range(Octaves):
        Total += ValueNoise(BaseCells * (2 ** Octave)) * Amplitude
        Norm += Amplitude
        Amplitude *= 0.5
    return Total / Norm


def RadialDistance():
    Coords = (np.arange(Size) + 0.5) / Size * 2.0 - 1.0
    X, Y = np.meshgrid(Coords, Coords)
    return np.sqrt(X * X + Y * Y), np.arctan2(Y, X)


def Save(Name, Rgb, Alpha):
    Pixels = np.dstack([np.clip(Channel, 0.0, 1.0) for Channel in (*Rgb, Alpha)])
    Image.fromarray((Pixels * 255.0 + 0.5).astype(np.uint8), "RGBA").save(os.path.join(OutDir, Name + ".png"))


# Decal sheets tile several variants, each drawn at the generator's cell size and packed across then down.
def SaveSheet(Name, Cells, Columns):
    Rows = (len(Cells) + Columns - 1) // Columns
    Sheet = np.zeros((Rows * Size, Columns * Size, 4))
    for Index, Cell in enumerate(Cells):
        Row, Column = divmod(Index, Columns)
        Sheet[Row * Size:(Row + 1) * Size, Column * Size:(Column + 1) * Size] = np.dstack([np.clip(Channel, 0.0, 1.0) for Channel in Cell])
    Image.fromarray((Sheet * 255.0 + 0.5).astype(np.uint8), "RGBA").save(os.path.join(OutDir, Name + ".png"))


def HeightToNormal(Height, Strength):
    Dy, Dx = np.gradient(Height)
    Normal = np.dstack([-Dx * Strength, -Dy * Strength, np.ones_like(Height)])
    Normal /= np.linalg.norm(Normal, axis=2, keepdims=True)
    return Normal * 0.5 + 0.5


def ScorchCell():
    Distance, Angle = RadialDistance()
    Rays = 0.5 + 0.5 * np.sin(Angle * Rng.integers(9, 15) + Fbm(3, 4) * 8.0)
    Warped = Distance + (Fbm(4, 4) - 0.5) * 0.35
    Reach = 0.88 + 0.1 * Rays ** 3
    Alpha = np.clip((Reach - Warped) / 0.6, 0.0, 1.0) ** 1.1
    Alpha *= 0.6 + 0.4 * Fbm(5, 10)
    Char = np.clip(1.0 - Warped / 0.5, 0.0, 1.0)
    Grit = Fbm(5, 12)
    Alpha = np.clip(Alpha + Char * 0.3, 0.0, 1.0)
    Shade = 0.03 + 0.1 * Grit * (1.0 - Char)
    # Embers glow in thin cracks through the charred core, and nowhere past it.
    Cracks = np.clip(1.0 - np.abs(Fbm(4, 9) - 0.5) * 14.0, 0.0, 1.0)
    Ember = Cracks * Char ** 1.5
    return (Shade * 1.1, Shade, Shade * 0.9, Alpha), (Ember, Ember, Ember, Alpha)


def BulletHoleCell():
    Distance, Angle = RadialDistance()
    Hole = 0.1 + 0.03 * Fbm(2, 4)
    Chip = 0.32 + 0.12 * Fbm(3, 5)
    Cracks = np.clip(1.0 - np.abs(np.sin(Angle * Rng.integers(3, 6) + Fbm(3, 3) * 4.0)) * 12.0, 0.0, 1.0) * np.clip(1.0 - Distance / 0.7, 0.0, 1.0)
    InHole = np.clip((Hole - Distance) * 40.0, 0.0, 1.0)
    InChip = np.clip((Chip - Distance) * 20.0, 0.0, 1.0)
    Alpha = np.clip(np.maximum(InChip * (0.55 + 0.45 * Fbm(4, 10)), Cracks * 0.8), 0.0, 1.0)
    Alpha = np.maximum(Alpha, InHole)
    Shade = (0.32 + 0.2 * Fbm(4, 14)) * (1.0 - InHole * 0.95) * (1.0 - Cracks * 0.6)
    Crater = -InHole * 1.0 - InChip * (1.0 - Distance / Chip).clip(0.0, 1.0) * 0.5 - Cracks * 0.15
    return (Shade, Shade * 0.95, Shade * 0.9, Alpha), Crater


def Scorches():
    Cells = [ScorchCell() for _ in range(4)]
    SaveSheet("T_Scorch", [Color for Color, _ in Cells], 2)
    SaveSheet("T_Scorch_E", [Ember for _, Ember in Cells], 2)


def BulletHoles():
    Colors, Normals = [], []
    for _ in range(4):
        Color, Crater = BulletHoleCell()
        Colors.append(Color)
        Normal = HeightToNormal(Crater, 40.0)
        Normals.append((Normal[..., 0], Normal[..., 1], Normal[..., 2], Color[3]))
    SaveSheet("T_BulletHoles", Colors, 2)
    SaveSheet("T_BulletHoles_N", Normals, 2)


def Droplets(Count, MinReach, MaxReach, MinRadius, MaxRadius, Spread=np.pi, Heading=0.0):
    Coords = (np.arange(Size) + 0.5) / Size * 2.0 - 1.0
    X, Y = np.meshgrid(Coords, Coords)
    Alpha = np.zeros((Size, Size))
    for _ in range(Count):
        Angle = Heading + Rng.uniform(-Spread, Spread)
        Reach = Rng.uniform(MinReach, MaxReach)
        Radius = Rng.uniform(MinRadius, MaxRadius)
        # Flung drops stretch along their flight, so the far ones read as streaks.
        Stretch = 1.0 + Reach * 2.5
        Cx, Cy = np.cos(Angle) * Reach, np.sin(Angle) * Reach
        Along = (X - Cx) * np.cos(Angle) + (Y - Cy) * np.sin(Angle)
        Across = -(X - Cx) * np.sin(Angle) + (Y - Cy) * np.cos(Angle)
        Distance = np.sqrt((Along / Stretch) ** 2 + Across ** 2)
        Alpha = np.maximum(Alpha, np.clip((Radius - Distance) / (Radius * 0.35), 0.0, 1.0))
    return Alpha


def BloodColor(Alpha, Thickness):
    Grain = Fbm(4, 10)
    Shade = (0.55 + 0.35 * Grain) * (1.0 - 0.45 * Thickness)
    return (Shade * 0.42, Shade * 0.018, Shade * 0.025, Alpha)


def BloodSplatCell():
    Distance, _ = RadialDistance()
    Warped = Distance + (Fbm(4, 4) - 0.5) * 0.45
    Blob = np.clip((0.34 - Warped) / 0.05, 0.0, 1.0)
    Alpha = np.maximum(Blob, Droplets(38, 0.3, 0.9, 0.015, 0.06))
    return BloodColor(Alpha, np.clip(1.0 - Warped / 0.34, 0.0, 1.0))


def BloodPoolCell():
    Distance, _ = RadialDistance()
    Warped = Distance + (Fbm(3, 3) - 0.5) * 0.3
    Alpha = np.clip((0.78 - Warped) / 0.04, 0.0, 1.0)
    Alpha = np.maximum(Alpha, Droplets(10, 0.75, 0.95, 0.02, 0.05))
    return BloodColor(Alpha, np.clip(1.0 - Warped / 0.78, 0.0, 1.0) ** 0.5)


def BloodSprayCell():
    Coords = (np.arange(Size) + 0.5) / Size * 2.0 - 1.0
    X, _ = np.meshgrid(Coords, Coords)
    Alpha = Droplets(70, 0.05, 0.95, 0.012, 0.05, Spread=0.45, Heading=0.0)
    Alpha = np.maximum(Alpha, Droplets(12, 0.0, 0.25, 0.05, 0.1))
    return BloodColor(Alpha, np.clip(0.5 - X * 0.5, 0.0, 1.0))


def Blood():
    SaveSheet("T_Blood", [BloodSplatCell(), BloodSplatCell(), BloodPoolCell(), BloodSprayCell()], 2)
    # Particles tint these white shapes, so one texture serves every shade of red.
    Distance, _ = RadialDistance()
    Warped = Distance + (Fbm(4, 4) - 0.5) * 0.4
    Splat = np.maximum(np.clip((0.4 - Warped) / 0.06, 0.0, 1.0), Droplets(24, 0.35, 0.9, 0.02, 0.07))
    Save("T_BloodSplat", (np.ones_like(Splat), np.ones_like(Splat), np.ones_like(Splat)), Splat)
    Drop = np.clip((0.8 - Distance) / 0.25, 0.0, 1.0)
    Save("T_BloodDrop", (np.ones_like(Drop), np.ones_like(Drop), np.ones_like(Drop)), Drop)


def SmokePuff():
    Distance, Angle = RadialDistance()
    # A noise-warped edge reads as a billow instead of a disc once dozens of them overlap.
    Warped = Distance + (Fbm(4, 3) - 0.5) * 0.55
    Falloff = np.clip(1.0 - Warped / 0.8, 0.0, 1.0) ** 1.2
    Density = np.clip((Fbm(5, 5) - 0.3) * 2.2, 0.0, 1.0)
    Alpha = Falloff * (0.3 + 0.7 * Density)
    Shade = 0.6 + 0.4 * Fbm(4, 4)
    Save("T_SmokePuff", (Shade, Shade, Shade), Alpha)


def FlameLick():
    Distance, Angle = RadialDistance()
    Falloff = np.clip(1.0 - Distance / 0.85, 0.0, 1.0) ** 1.1
    Turbulence = Fbm(5, 5)
    Core = np.clip(Falloff * (0.55 + 0.9 * Turbulence) - 0.15, 0.0, 1.0)
    Save("T_Flame", (np.ones_like(Core), np.ones_like(Core), np.ones_like(Core)), Core)


def DirtClod():
    Distance, Angle = RadialDistance()
    Edge = 0.45 + 0.35 * Fbm(3, 3)
    Alpha = np.clip((Edge - Distance) * 10.0, 0.0, 1.0)
    Shade = 0.25 + 0.35 * Fbm(4, 8)
    Save("T_DirtClod", (Shade, Shade * 0.8, Shade * 0.6), Alpha)


if __name__ == "__main__":
    os.makedirs(OutDir, exist_ok=True)
    SmokePuff()
    FlameLick()
    DirtClod()
    Scorches()
    BulletHoles()
    Blood()
    print("Wrote textures to", os.path.abspath(OutDir))
