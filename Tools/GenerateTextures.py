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
    print("Wrote textures to", os.path.abspath(OutDir))
