import os
import sys
import numpy as np
from PIL import Image

# Tileable ground layers for the terrain material. Every noise here wraps, so a tile meets its neighbor without a seam.
Size = 512
Rng = np.random.default_rng(1999)
OutDir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "SourceTextures")


def Periodic(Cells, Resolution=Size):
    Grid = Rng.uniform(0.0, 1.0, (Cells, Cells))
    Coords = np.arange(Resolution) / Resolution * Cells
    X, Y = np.meshgrid(Coords, Coords)
    X0, Y0 = np.floor(X).astype(int), np.floor(Y).astype(int)
    Fx, Fy = X - X0, Y - Y0
    Fx, Fy = Fx * Fx * (3.0 - 2.0 * Fx), Fy * Fy * (3.0 - 2.0 * Fy)
    X1, Y1 = (X0 + 1) % Cells, (Y0 + 1) % Cells
    X0, Y0 = X0 % Cells, Y0 % Cells
    Top = Grid[Y0, X0] * (1.0 - Fx) + Grid[Y0, X1] * Fx
    Bottom = Grid[Y1, X0] * (1.0 - Fx) + Grid[Y1, X1] * Fx
    return Top * (1.0 - Fy) + Bottom * Fy


def Fbm(BaseCells, Octaves, Resolution=Size, Gain=0.5):
    Total, Amplitude, Norm = np.zeros((Resolution, Resolution)), 1.0, 0.0
    for Octave in range(Octaves):
        Cells = BaseCells * (2 ** Octave)
        if Cells > Resolution:
            break
        Total += Periodic(Cells, Resolution) * Amplitude
        Norm += Amplitude
        Amplitude *= Gain
    return Total / Norm


def Ridged(BaseCells, Octaves):
    return 1.0 - np.abs(Fbm(BaseCells, Octaves) * 2.0 - 1.0)


def Blur(Field, Radius):
    for Axis in (0, 1):
        Field = sum(np.roll(Field, Shift, axis=Axis) for Shift in range(-Radius, Radius + 1)) / (2 * Radius + 1)
    return Field


def Normalize(Field):
    return (Field - Field.min()) / max(Field.max() - Field.min(), 1e-6)


def Hex(Value):
    return np.array([(Value >> 16) & 255, (Value >> 8) & 255, Value & 255]) / 255.0


def Colorize(Low, High, T):
    T = np.clip(T, 0.0, 1.0)[..., None]
    return Low * (1.0 - T) + High * T


# Stones scattered on a wrapping grid, each a smooth dome, so dirt and gravel read as loose material rather than noise.
def Pebbles(Count, MinRadius, MaxRadius):
    Height = np.zeros((Size, Size))
    Coords = np.arange(Size)
    X, Y = np.meshgrid(Coords, Coords)
    for _ in range(Count):
        Cx, Cy = Rng.uniform(0, Size, 2)
        Radius = Rng.uniform(MinRadius, MaxRadius)
        Dx = (X - Cx + Size / 2) % Size - Size / 2
        Dy = (Y - Cy + Size / 2) % Size - Size / 2
        Stretch = Rng.uniform(0.7, 1.3)
        D = np.sqrt((Dx * Stretch) ** 2 + (Dy / Stretch) ** 2) / Radius
        Height = np.maximum(Height, np.sqrt(np.clip(1.0 - D * D, 0.0, 1.0)) * Rng.uniform(0.6, 1.0))
    return Height


def NormalMap(Height, Strength):
    Dx = (np.roll(Height, -1, axis=1) - np.roll(Height, 1, axis=1)) * 0.5
    Dy = (np.roll(Height, -1, axis=0) - np.roll(Height, 1, axis=0)) * 0.5
    Normal = np.dstack([-Dx * Strength, -Dy * Strength, np.ones_like(Height)])
    Normal /= np.linalg.norm(Normal, axis=2, keepdims=True)
    return Normal * 0.5 + 0.5


def Save(Name, Albedo, Height, NormalStrength):
    Rgba = np.dstack([np.clip(Albedo, 0.0, 1.0), np.clip(Height, 0.0, 1.0)])
    Image.fromarray((Rgba * 255.0 + 0.5).astype(np.uint8), "RGBA").save(os.path.join(OutDir, Name + ".png"))
    Normal = NormalMap(Height, NormalStrength)
    Image.fromarray((Normal * 255.0 + 0.5).astype(np.uint8), "RGB").save(os.path.join(OutDir, Name + "_N.png"))


# Neutral green with blade streaks and clumps; the material tints it toward dry grass and jungle by region.
def Grass():
    Clumps = Fbm(6, 3)
    Streaks = Blur(Periodic(256), 1) * 0.6 + Periodic(128) * 0.4
    Blades = np.clip((Periodic(512) - 0.35) * 2.0, 0.0, 1.0)
    Shade = Clumps * 0.45 + Streaks * 0.35 + Blades * 0.2
    Albedo = Colorize(Hex(0x3F6B26), Hex(0x86A94C), Normalize(Shade) * 1.1 - 0.05)
    Albedo *= (0.88 + 0.12 * Fbm(24, 3))[..., None]
    Height = Normalize(Clumps * 0.5 + Blades * 0.35 + Streaks * 0.15)
    Save("T_Ground_Grass", Albedo, Height, 1.2)


def Rock():
    Cracks = Ridged(4, 5)
    Plates = Fbm(3, 5)
    Grain = Periodic(256) * 0.5 + Periodic(128) * 0.5
    Height = Normalize(Plates * 0.65 + Grain * 0.2 + Cracks ** 4 * 0.15)
    Shade = Plates * 0.45 + Grain * 0.3 + Fbm(12, 3) * 0.25
    Albedo = Colorize(Hex(0x676158), Hex(0x9A9387), Normalize(Shade))
    Albedo *= (1.0 - 0.15 * np.clip((Cracks - 0.93) * 14.0, 0.0, 1.0))[..., None]
    Save("T_Ground_Rock", Albedo, Height, 5.0)


def Sand():
    Coords = np.arange(Size) / Size * 2.0 * np.pi
    X, Y = np.meshgrid(Coords, Coords)
    Warp = Fbm(4, 3) * 4.0
    Ripples = 0.5 + 0.5 * np.sin(Y * 11.0 + X * 3.0 + Warp)
    Grain = Periodic(512) * 0.6 + Periodic(256) * 0.4
    Height = Normalize(Ripples * 0.25 + Grain * 0.75)
    Shade = Ripples * 0.25 + Grain * 0.45 + Fbm(5, 3) * 0.3
    Albedo = Colorize(Hex(0xB9A06D), Hex(0xE6D5A3), Normalize(Shade))
    Speckle = (Periodic(512) > 0.8) * 0.12
    Albedo *= (1.0 - Speckle)[..., None]
    Save("T_Ground_Sand", Albedo, Height, 1.2)


def Dirt():
    Stones = Pebbles(170, 2.5, 7.0)
    Soil = Fbm(8, 5)
    Clods = Fbm(16, 3)
    Height = Normalize(Soil * 0.45 + Clods * 0.2 + Stones * 0.55)
    Albedo = Colorize(Hex(0x5E4630), Hex(0x9C7B52), Normalize(Soil * 0.7 + Clods * 0.3))
    StoneColor = Colorize(Hex(0x66594A), Hex(0x8C8070), Periodic(64))
    StoneMask = np.clip(Stones * 3.0, 0.0, 1.0)[..., None]
    Albedo = Albedo * (1.0 - StoneMask) + StoneColor * StoneMask * (0.75 + 0.25 * Stones[..., None])
    Save("T_Ground_Dirt", Albedo, Height, 5.0)


# Weathered asphalt and concrete with fine aggregate, cracks and stains, flat enough to read as paved from a distance.
def Paved():
    Aggregate = Periodic(512) * 0.5 + Periodic(256) * 0.3 + Periodic(128) * 0.2
    Cracks = Ridged(3, 4)
    CrackMask = np.clip((Cracks - 0.975) * 40.0, 0.0, 1.0)
    Stains = Fbm(4, 4)
    Height = Normalize(Aggregate * 0.7 - CrackMask * 0.2 + Stains * 0.1)
    Albedo = Colorize(Hex(0x4B4945), Hex(0x77736C), Normalize(Aggregate * 0.55 + Stains * 0.45))
    Albedo *= (1.0 - CrackMask * 0.2)[..., None]
    Save("T_Ground_Paved", Albedo, Height, 2.0)


# Region masks read once every few hundred meters, where red dries grass out, green turns it to jungle and blue shifts brightness.
def Macro():
    Masks = [Normalize(Fbm(4, 4, 256)), Normalize(Fbm(3, 4, 256)), Normalize(Fbm(6, 3, 256))]
    Rgb = np.dstack(Masks)
    Image.fromarray((Rgb * 255.0 + 0.5).astype(np.uint8), "RGB").save(os.path.join(OutDir, "T_Ground_Macro.png"))


if __name__ == "__main__":
    os.makedirs(OutDir, exist_ok=True)
    Grass()
    Rock()
    Sand()
    Dirt()
    Paved()
    Macro()
    print("Wrote ground textures to", os.path.abspath(OutDir))
