import math
import os
import sys
import numpy as np

Rng = np.random.default_rng(1944)
OutDir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "SourceMeshes")


def WriteObj(Name, Positions, Normals, UVs, Faces):
    with open(os.path.join(OutDir, Name + ".obj"), "w") as File:
        File.write(f"o {Name}\n")
        for P in Positions:
            File.write(f"v {P[0]:.5f} {P[1]:.5f} {P[2]:.5f}\n")
        for N in Normals:
            File.write(f"vn {N[0]:.5f} {N[1]:.5f} {N[2]:.5f}\n")
        for T in UVs:
            File.write(f"vt {T[0]:.5f} {T[1]:.5f}\n")
        for Face in Faces:
            File.write("f " + " ".join(f"{I + 1}/{I + 1}/{I + 1}" for I in Face) + "\n")


def Icosphere(Subdivisions):
    T = (1.0 + math.sqrt(5.0)) / 2.0
    Vertices = [np.array(V, dtype=float) for V in [(-1, T, 0), (1, T, 0), (-1, -T, 0), (1, -T, 0), (0, -1, T), (0, 1, T),
                                                   (0, -1, -T), (0, 1, -T), (T, 0, -1), (T, 0, 1), (-T, 0, -1), (-T, 0, 1)]]
    Vertices = [V / np.linalg.norm(V) for V in Vertices]
    Faces = [(0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
             (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1)]
    for _ in range(Subdivisions):
        Cache = {}

        def Midpoint(A, B):
            Key = (min(A, B), max(A, B))
            if Key not in Cache:
                M = Vertices[A] + Vertices[B]
                Vertices.append(M / np.linalg.norm(M))
                Cache[Key] = len(Vertices) - 1
            return Cache[Key]

        Next = []
        for A, B, C in Faces:
            AB, BC, CA = Midpoint(A, B), Midpoint(B, C), Midpoint(C, A)
            Next += [(A, AB, CA), (B, BC, AB), (C, CA, BC), (AB, BC, CA)]
        Faces = Next
    return Vertices, Faces


# Flat-shaded, so every face gets its own three vertices and reads as a chipped facet.
def FlatMesh(Vertices, Faces):
    Positions, Normals, UVs, OutFaces = [], [], [], []
    for A, B, C in Faces:
        PA, PB, PC = Vertices[A], Vertices[B], Vertices[C]
        N = np.cross(PB - PA, PC - PA)
        N = N / max(np.linalg.norm(N), 1e-9)
        Base = len(Positions)
        for P, UV in ((PA, (0.0, 0.0)), (PB, (1.0, 0.0)), (PC, (0.5, 1.0))):
            Positions.append(P)
            Normals.append(N)
            UVs.append(UV)
        OutFaces.append((Base, Base + 1, Base + 2))
    return Positions, Normals, UVs, OutFaces


def RockChunk():
    Vertices, Faces = Icosphere(1)
    Squash = np.array([1.0, 0.7, 0.85])
    Vertices = [V * Squash * (0.75 + 0.35 * Rng.random()) * 0.5 for V in Vertices]
    WriteObj("SM_RockChunk", *FlatMesh(Vertices, Faces))


def Casing():
    Sides, Radius, Height = 10, 0.06, 0.4
    Vertices, Faces = [], []
    for Ring, Y in enumerate((-Height * 0.5, Height * 0.5)):
        for Side in range(Sides):
            Angle = 2.0 * math.pi * Side / Sides
            Vertices.append(np.array([math.cos(Angle) * Radius, Y, math.sin(Angle) * Radius]))
    Vertices.append(np.array([0.0, -Height * 0.5, 0.0]))
    Vertices.append(np.array([0.0, Height * 0.5, 0.0]))
    Bottom, Top = len(Vertices) - 2, len(Vertices) - 1
    for Side in range(Sides):
        A, B = Side, (Side + 1) % Sides
        Faces += [(A, A + Sides, B + Sides), (A, B + Sides, B), (Bottom, A, B), (Top, B + Sides, A + Sides)]
    WriteObj("SM_Casing", *FlatMesh(Vertices, Faces))


# Tapered blades fanned around a small root patch, each bending over along its facing.
def GrassClump():
    Positions, Normals, UVs, Faces = [], [], [], []
    Segments = 3
    for Blade in range(11):
        Angle = Rng.uniform(0.0, 2.0 * math.pi)
        Reach = Rng.uniform(0.0, 0.14)
        Root = np.array([math.cos(Angle) * Reach, 0.0, math.sin(Angle) * Reach])
        Yaw = Rng.uniform(0.0, 2.0 * math.pi)
        Across = np.array([math.cos(Yaw), 0.0, math.sin(Yaw)])
        Facing = np.array([-math.sin(Yaw), 0.0, math.cos(Yaw)])
        Height = Rng.uniform(0.2, 0.42)
        Lean = Rng.uniform(0.06, 0.24)
        Width = Rng.uniform(0.022, 0.036)
        Normal = Facing * 0.35 + np.array([0.0, 1.0, 0.0]) * 0.65
        Normal /= np.linalg.norm(Normal)
        Base = len(Positions)
        for Row in range(Segments + 1):
            T = Row / Segments
            Center = Root + np.array([0.0, Height * T, 0.0]) + Facing * Lean * T * T
            HalfWidth = Width * (1.0 - T) + 0.002
            for Side in (-1.0, 1.0):
                Positions.append(Center + Across * HalfWidth * Side)
                Normals.append(Normal)
                UVs.append((0.5 + 0.5 * Side, T))
        for Row in range(Segments):
            A = Base + Row * 2
            Faces.append((A, A + 1, A + 3))
            Faces.append((A, A + 3, A + 2))
        # The back is its own faces leaning the other way but still upward, since a flipped normal would point at the ground and go black.
        Back = len(Positions)
        for Index in range(Base, Back):
            Positions.append(Positions[Index])
            Normals.append(Normals[Index] * np.array([-1.0, 1.0, -1.0]))
            UVs.append(UVs[Index])
        for Row in range(Segments):
            A = Back + Row * 2
            Faces.append((A, A + 3, A + 1))
            Faces.append((A, A + 2, A + 3))
    WriteObj("SM_GrassClump", Positions, Normals, UVs, Faces)


if __name__ == "__main__":
    os.makedirs(OutDir, exist_ok=True)
    RockChunk()
    Casing()
    GrassClump()
    print("Wrote meshes to", os.path.abspath(OutDir))
