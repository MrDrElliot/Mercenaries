import os
import sys
import wave
import numpy as np
from scipy import signal

Rate = 44100
Rng = np.random.default_rng(1977)
OutDir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "SourceAudio")


def Seconds(Duration):
    return int(Duration * Rate)


def Time(Duration):
    return np.arange(Seconds(Duration)) / Rate


def Noise(Duration):
    return Rng.uniform(-1.0, 1.0, Seconds(Duration))


def Filter(Samples, Kind, Cutoff, Order=2):
    Nyquist = Rate * 0.5
    Band = np.clip(np.asarray(Cutoff, dtype=float) / Nyquist, 1e-4, 0.999)
    B, A = signal.butter(Order, Band, btype=Kind)
    return signal.lfilter(B, A, Samples)


def Decay(Duration, Tau, Attack=0.0005):
    T = Time(Duration)
    Rise = np.clip(T / max(Attack, 1e-6), 0.0, 1.0)
    return Rise * np.exp(-T / Tau)


def Pad(Samples, Duration):
    Out = np.zeros(Seconds(Duration))
    Count = min(len(Samples), len(Out))
    Out[:Count] = Samples[:Count]
    return Out


def Mix(Duration, *Layers):
    Out = np.zeros(Seconds(Duration))
    for Offset, Samples in Layers:
        Samples = FadeEdges(Samples, 0.0, 0.006)
        Start = Seconds(Offset)
        Count = min(len(Samples), len(Out) - Start)
        if Count > 0:
            Out[Start:Start + Count] += Samples[:Count]
    return Out


def Sweep(Duration, StartHz, EndHz, Shape="sine"):
    T = Time(Duration)
    Frequency = StartHz * (EndHz / StartHz) ** (T / Duration)
    Phase = 2.0 * np.pi * np.cumsum(Frequency) / Rate
    if Shape == "square":
        return np.sign(np.sin(Phase))
    if Shape == "saw":
        return 2.0 * ((Phase / (2.0 * np.pi)) % 1.0) - 1.0
    return np.sin(Phase)


def Tone(Duration, Hz, Shape="sine"):
    return Sweep(Duration, Hz, Hz * 1.0000001, Shape)


def Echo(Samples, Duration, Tau, Level, Cutoff=1400.0):
    Tail = Filter(Noise(Duration), "low", Cutoff) * Decay(Duration, Tau, 0.02)
    Wet = signal.fftconvolve(Samples, Tail)[:len(Samples) + Seconds(Duration)]
    Wet /= max(np.max(np.abs(Wet)), 1e-9)
    return Mix(len(Wet) / Rate, (0.0, Samples), (0.0, Wet * Level))


def Soften(Samples, Drive=1.6):
    return np.tanh(Samples * Drive) / np.tanh(Drive)


def Normalize(Samples, Peak=0.89):
    return Samples * (Peak / max(np.max(np.abs(Samples)), 1e-9))


def FadeEdges(Samples, In=0.002, Out=0.01):
    Samples = np.array(Samples, dtype=float)
    FadeIn = min(Seconds(In), len(Samples))
    FadeOut = min(Seconds(Out), len(Samples))
    Samples[:FadeIn] *= np.linspace(0.0, 1.0, FadeIn)
    Samples[len(Samples) - FadeOut:] *= np.linspace(1.0, 0.0, FadeOut)
    return Samples


def MakeLoop(Samples, Crossfade):
    Overlap = Seconds(Crossfade)
    Body = Samples[:-Overlap].copy()
    Ramp = np.linspace(0.0, 1.0, Overlap)
    Body[:Overlap] = Body[:Overlap] * Ramp + Samples[-Overlap:] * (1.0 - Ramp)
    return Body


def Write(Name, Samples, Peak=0.89, bLoop=False):
    Samples = Normalize(Samples, Peak)
    if not bLoop:
        Samples = FadeEdges(Samples)
    Pcm = np.clip(Samples * 32767.0, -32768, 32767).astype(np.int16)
    with wave.open(os.path.join(OutDir, Name + ".wav"), "wb") as File:
        File.setnchannels(1)
        File.setsampwidth(2)
        File.setframerate(Rate)
        File.writeframes(Pcm.tobytes())


def Gunshot(Crack, Body, BodyCutoff, BodyTau, Thump, ThumpTau, TailTau, TailLevel, Length):
    CrackLayer = Filter(Noise(0.02), "high", 2500.0) * Decay(0.02, 0.003) * Crack
    BodyLayer = Filter(Noise(0.4), "low", BodyCutoff) * Decay(0.4, BodyTau) * Body
    ThumpLayer = Sweep(0.3, Thump * 1.8, Thump) * Decay(0.3, ThumpTau) * 0.9
    Dry = Soften(Mix(Length, (0.0, CrackLayer), (0.0, BodyLayer), (0.0, ThumpLayer)), 2.2)
    return Pad(Echo(Dry, TailTau * 5.0, TailTau, TailLevel, 900.0), Length)


def Explosion(Length, Boom, Rumble, Debris):
    Pop = Filter(Noise(0.05), "high", 1200.0) * Decay(0.05, 0.008)
    Blast = Filter(Noise(Length), "low", 700.0) * Decay(Length, Boom, 0.004)
    Sub = Sweep(Length, 70.0, 28.0) * Decay(Length, Boom * 1.3, 0.004)
    Roll = Filter(Noise(Length), "low", 180.0) * Decay(Length, Rumble, 0.08)
    Grains = np.zeros(Seconds(Length))
    for _ in range(int(Debris)):
        Start = Seconds(Rng.uniform(0.05, Length * 0.6))
        Grain = Filter(Noise(0.03), "band", [800.0, 3500.0]) * Decay(0.03, 0.006) * Rng.uniform(0.05, 0.25)
        Grains[Start:Start + len(Grain)] += Grain[:len(Grains) - Start]
    return Soften(Mix(Length, (0.0, Pop * 0.7), (0.0, Blast * 1.2), (0.0, Sub * 1.4), (0.0, Roll * 1.6), (0.0, Grains)), 2.5)


def EngineLoop(FiringHz, Length, Rough, Cutoff, Clatter=0.0):
    Hz = round(FiringHz * Length) / Length
    Length += 0.6
    T = Time(Length)
    Pulses = np.zeros_like(T)
    for Harmonic, Level in ((1, 1.0), (2, 0.55), (3, 0.3), (4, 0.18), (6, 0.08)):
        Pulses += np.sin(2.0 * np.pi * Hz * Harmonic * T + Harmonic * 0.7) * Level
    Throb = 0.75 + 0.25 * np.sin(2.0 * np.pi * Hz * 0.5 * T)
    Grit = Filter(Noise(Length + 0.5), "low", Cutoff)[:len(T)] * Rough
    Out = Pulses * Throb + Grit
    if Clatter > 0.0:
        ClatterHz = round(11.0 * (Length - 0.6)) / (Length - 0.6)
        Gate = (np.sin(2.0 * np.pi * ClatterHz * T) > 0.93).astype(float)
        Out += Filter(Noise(Length + 0.5), "band", [900.0, 3000.0])[:len(T)] * Gate * Clatter
    return MakeLoop(Filter(Soften(Out, 1.4), "low", Cutoff * 2.5)[Seconds(0.3):], 0.3)


def Rotor(Length):
    BladeHz = round(5.5 * Length) / Length
    T = Time(Length + 0.3)
    Phase = (T * BladeHz) % 1.0
    Slap = np.exp(-Phase / 0.05) * Filter(Noise(Length + 0.3), "band", [120.0, 900.0])
    Wash = Filter(Noise(Length + 0.3), "low", 500.0) * 0.35
    Whine = np.sin(2.0 * np.pi * 2350.0 * T) * 0.035 + np.sin(2.0 * np.pi * 4700.0 * T) * 0.015
    return MakeLoop(Slap * 1.4 + Wash + Whine, 0.3)


def Click(Hz, Tau, Level=1.0):
    return (Filter(Noise(0.05), "band", [Hz * 0.6, Hz * 1.6]) * Decay(0.05, Tau)) * Level


def Blip(Notes, NoteLength, Shape="sine", Gap=0.0, Tau=0.12):
    Length = len(Notes) * (NoteLength + Gap) + 0.3
    Layers = []
    for Index, Hz in enumerate(Notes):
        Voice = Tone(NoteLength + 0.25, Hz, Shape) * Decay(NoteLength + 0.25, Tau, 0.004)
        Voice += Tone(NoteLength + 0.25, Hz * 2.0) * Decay(NoteLength + 0.25, Tau * 0.5, 0.004) * 0.25
        Layers.append((Index * (NoteLength + Gap), Voice))
    return Mix(Length, *Layers)


def Build():
    os.makedirs(OutDir, exist_ok=True)

    Write("Gun_Pistol", Gunshot(0.9, 0.8, 2200.0, 0.025, 170.0, 0.03, 0.08, 0.25, 0.7))
    Write("Gun_Rifle", Gunshot(1.0, 1.0, 1700.0, 0.035, 120.0, 0.04, 0.12, 0.3, 0.9))
    Write("Gun_Smg", Gunshot(0.8, 0.8, 2400.0, 0.02, 190.0, 0.025, 0.07, 0.22, 0.6))
    Write("Gun_Shotgun", Gunshot(0.8, 1.3, 1000.0, 0.07, 85.0, 0.08, 0.2, 0.4, 1.4))
    Write("Gun_Sniper", Gunshot(1.4, 1.1, 1500.0, 0.05, 95.0, 0.07, 0.3, 0.5, 2.2))
    Write("Gun_MachineGun", Gunshot(1.0, 1.2, 1300.0, 0.04, 100.0, 0.05, 0.13, 0.3, 0.9))
    Write("Gun_Autocannon", Gunshot(1.0, 1.4, 900.0, 0.06, 70.0, 0.1, 0.18, 0.35, 1.3))
    Write("Gun_TankCannon", Explosion(3.0, 0.18, 0.9, 6) * 0.6 + Pad(Gunshot(1.6, 1.4, 700.0, 0.12, 55.0, 0.25, 0.4, 0.5, 3.0), 3.0))

    Launch = Filter(Noise(1.4), "band", [300.0, 2600.0]) * Decay(1.4, 0.35, 0.05)
    Hiss = Filter(Noise(1.4), "high", 3000.0) * Decay(1.4, 0.25, 0.01) * 0.4
    Write("Rocket_Launch", Mix(1.4, (0.0, Gunshot(0.6, 0.9, 900.0, 0.05, 80.0, 0.06, 0.1, 0.2, 0.6)), (0.02, Launch), (0.02, Hiss)))
    Write("Grenade_Launch", Gunshot(0.4, 1.0, 600.0, 0.04, 110.0, 0.05, 0.08, 0.15, 0.5))

    Write("Explosion_Small", Explosion(2.2, 0.12, 0.5, 18))
    Write("Explosion_Large", Explosion(3.5, 0.2, 0.9, 40))
    Write("Explosion_Huge", Explosion(5.0, 0.3, 1.5, 70))

    Write("Impact_Ground", Filter(Noise(0.12), "low", 2200.0) * Decay(0.12, 0.018))
    Write("Impact_Metal", Pad(Click(2600.0, 0.01), 0.4) + Sweep(0.4, 1900.0, 1700.0) * Decay(0.4, 0.06) * 0.35)
    Ricochet = Sweep(0.45, 4200.0, 1600.0) * Decay(0.45, 0.14, 0.002) * (0.8 + 0.2 * np.sin(2.0 * np.pi * 38.0 * Time(0.45)))
    Write("Ricochet", Mix(0.45, (0.0, Ricochet * 0.6), (0.0, Click(3000.0, 0.005))))
    Write("Hit_Body", Sweep(0.12, 200.0, 110.0) * Decay(0.12, 0.03) + Filter(Noise(0.12), "low", 900.0) * Decay(0.12, 0.02) * 0.6)
    Whiz = Filter(Noise(0.22), "band", [1800.0, 5500.0]) * np.sin(np.linspace(0.0, np.pi, Seconds(0.22))) ** 3
    Write("Bullet_Whiz", Whiz)

    Write("Reload", Mix(0.9, (0.0, Click(1800.0, 0.01)), (0.12, Filter(Noise(0.2), "band", [700.0, 2500.0]) * Decay(0.2, 0.06) * 0.35), (0.55, Click(2400.0, 0.008)), (0.62, Click(1500.0, 0.012, 0.8))))
    Write("Empty_Click", Pad(Click(3500.0, 0.004), 0.15))
    Write("Weapon_Switch", Mix(0.5, (0.0, Filter(Noise(0.25), "band", [400.0, 2000.0]) * Decay(0.25, 0.08, 0.03) * 0.4), (0.22, Click(2200.0, 0.008))))

    Write("Footstep", Filter(Noise(0.14), "band", [500.0, 3800.0]) * Decay(0.14, 0.025, 0.004) + Filter(Noise(0.14), "low", 250.0) * Decay(0.14, 0.02) * 0.8)
    Write("Land", Filter(Noise(0.3), "low", 600.0) * Decay(0.3, 0.05) + Sweep(0.3, 120.0, 60.0) * Decay(0.3, 0.05) * 0.7)
    Write("Melee_Hit", Mix(0.35, (0.0, Filter(Noise(0.15), "band", [300.0, 2000.0]) * Decay(0.15, 0.04, 0.03) * 0.3), (0.07, Sweep(0.2, 160.0, 70.0) * Decay(0.2, 0.04) + Filter(Noise(0.2), "low", 1200.0) * Decay(0.2, 0.02) * 0.7)))
    Write("Throw", Filter(Noise(0.35), "band", [500.0, 2500.0]) * np.sin(np.linspace(0.0, np.pi, Seconds(0.35))) ** 2)

    Write("C4_Plant", Mix(0.5, (0.0, Click(1200.0, 0.02)), (0.15, Tone(0.08, 2400.0) * Decay(0.08, 0.05) * 0.5)))
    Write("C4_Beep", Pad(Tone(0.09, 2800.0) * Decay(0.09, 0.06, 0.002), 0.12))

    Write("Engine_Jeep", EngineLoop(38.0, 2.0, 0.35, 900.0), 0.8, True)
    Write("Engine_Truck", EngineLoop(26.0, 2.0, 0.45, 650.0), 0.8, True)
    Write("Engine_Tank", EngineLoop(19.0, 2.0, 0.6, 420.0, 0.35), 0.85, True)
    Write("Rotor_Heli", Rotor(2.0), 0.85, True)

    T = Time(1.8)
    Whistle = Sweep(1.8, 1900.0, 520.0) * (0.85 + 0.15 * np.sin(2.0 * np.pi * 9.0 * T)) * np.clip(T / 0.3, 0.0, 1.0)
    Write("Artillery_Whistle", Whistle * 0.8 + Filter(Noise(1.8), "band", [800.0, 2400.0]) * 0.1 * np.clip(T / 1.8, 0.0, 1.0))
    T = Time(3.3)
    Roar = Filter(Noise(3.3), "band", [180.0, 4200.0]) + Filter(Noise(3.3), "low", 140.0) * 1.4
    Whine = np.sin(2.0 * np.pi * round(3100.0 * 3.0) / 3.0 * T) * 0.05
    Write("Engine_Jet", MakeLoop(Roar * (0.92 + 0.08 * np.sin(2.0 * np.pi * T)) + Whine, 0.3), 0.8, True)

    Static = Filter(Noise(0.35), "band", [900.0, 4000.0]) * Decay(0.35, 0.12, 0.005) * 0.5
    Write("Radio_Call", Mix(0.9, (0.0, Static), (0.3, Blip([1400.0, 1900.0], 0.08, "square", 0.02, 0.05) * 0.4)))
    Write("Flare_Pop", Mix(2.5, (0.0, Click(900.0, 0.02, 1.2)), (0.05, Filter(Noise(2.4), "high", 2500.0) * Decay(2.4, 1.2, 0.05) * 0.35)))

    Write("Ui_Click", Pad(Tone(0.05, 1500.0) * Decay(0.05, 0.015), 0.1), 0.5)
    Write("Ui_Open", Blip([660.0, 990.0], 0.06, "sine", 0.0, 0.06), 0.55)
    Write("Ui_Close", Blip([990.0, 660.0], 0.06, "sine", 0.0, 0.06), 0.55)
    Write("Cash", Mix(0.9, (0.0, Blip([2093.0, 2637.0], 0.07, "sine", 0.02, 0.2)), (0.0, Click(4000.0, 0.01, 0.6))), 0.7)
    Write("Objective", Blip([523.3, 659.3, 784.0, 1046.5], 0.11, "square", 0.0, 0.18) * 0.6, 0.6)
    Write("Alert", Blip([880.0, 622.0, 880.0, 622.0], 0.12, "square", 0.02, 0.1) * 0.6, 0.55)
    Write("Failure", Blip([392.0, 311.1, 233.1], 0.2, "square", 0.02, 0.25) * 0.6, 0.55)
    Write("Pickup", Blip([1175.0, 1568.0], 0.05, "sine", 0.0, 0.08), 0.6)

    Rumble = Filter(Noise(4.5), "low", 160.0) * Decay(4.5, 1.4, 0.15) * 1.6
    Crumble = Filter(Noise(4.5), "band", [300.0, 2500.0]) * Decay(4.5, 1.0, 0.3) * 0.5
    Chunks = np.zeros(Seconds(4.5))
    for _ in range(90):
        Start = Seconds(Rng.uniform(0.1, 3.2))
        Grain = Filter(Noise(0.06), "band", [250.0, 2200.0]) * Decay(0.06, 0.012) * Rng.uniform(0.1, 0.5)
        Chunks[Start:Start + len(Grain)] += Grain[:len(Chunks) - Start]
    Thump = Sweep(1.0, 60.0, 30.0) * Decay(1.0, 0.3, 0.01) * 1.2
    Write("Collapse", Soften(Mix(4.5, (0.0, Rumble), (0.0, Crumble), (0.0, Chunks), (0.0, Thump)), 2.0))

    Cracks = []
    for Index, Offset in enumerate((0.0, 0.09, 0.2, 0.34)):
        Crack = Filter(Noise(0.12), "band", [900.0, 4500.0]) * Decay(0.12, 0.015, 0.001) * (1.0 - Index * 0.18)
        Crack += Sweep(0.12, 240.0, 150.0) * Decay(0.12, 0.03) * 0.6
        Cracks.append((Offset, Crack))
    Write("Wood_Break", Mix(0.8, *Cracks, (0.05, Filter(Noise(0.6), "band", [400.0, 1800.0]) * Decay(0.6, 0.15, 0.02) * 0.25)))

    Tinkles = np.zeros(Seconds(1.0))
    for _ in range(26):
        Start = Seconds(Rng.uniform(0.02, 0.7))
        Ping = Tone(0.12, Rng.uniform(2800.0, 7500.0)) * Decay(0.12, Rng.uniform(0.015, 0.05), 0.001) * Rng.uniform(0.1, 0.35)
        Tinkles[Start:Start + len(Ping)] += Ping[:len(Tinkles) - Start]
    Write("Glass_Shatter", Mix(1.0, (0.0, Filter(Noise(0.4), "high", 2500.0) * Decay(0.4, 0.06, 0.001)), (0.0, Tinkles)))

    T = Time(4.6)
    Roar = Filter(Noise(4.6), "band", [80.0, 700.0]) * (0.7 + 0.3 * np.sin(2.0 * np.pi * 0.9 * T) * np.sin(2.0 * np.pi * 0.37 * T))
    Pops = np.zeros(Seconds(4.6))
    for _ in range(70):
        Start = Seconds(Rng.uniform(0.0, 4.5))
        Pop = Filter(Noise(0.03), "band", [1200.0, 6000.0]) * Decay(0.03, 0.004, 0.0005) * Rng.uniform(0.2, 0.9)
        Pops[Start:Start + len(Pop)] += Pop[:len(Pops) - Start]
    Write("Fire_Loop", MakeLoop(Roar + Pops, 0.6), 0.7, True)

    Creak = Sweep(0.9, 190.0, 120.0, "saw") * Decay(0.9, 0.5, 0.2) * (0.7 + 0.3 * np.sin(2.0 * np.pi * 14.0 * Time(0.9)))
    Creak = Filter(Creak, "band", [150.0, 1400.0]) * 0.5
    Rustle = Filter(Noise(1.2), "band", [1500.0, 6000.0]) * np.sin(np.linspace(0.0, np.pi, Seconds(1.2))) ** 2 * 0.35
    Landing = Filter(Noise(0.5), "low", 500.0) * Decay(0.5, 0.08) + Sweep(0.5, 110.0, 50.0) * Decay(0.5, 0.1) * 0.9
    Snap = Filter(Noise(0.1), "band", [800.0, 4000.0]) * Decay(0.1, 0.012, 0.001)
    Write("Tree_Fall", Mix(2.6, (0.0, Snap), (0.05, Creak), (0.6, Rustle), (1.6, Landing)))

    Length = 12.0
    T = Time(Length + 1.0)
    Gust = 0.55 + 0.45 * np.sin(2.0 * np.pi * T / (Length + 1.0) * 2.0) * np.sin(2.0 * np.pi * T / 3.7)
    Wind = Filter(Noise(Length + 1.0), "band", [120.0, 900.0]) * Gust
    Insects = Filter(Noise(Length + 1.0), "band", [5200.0, 6400.0]) * (0.5 + 0.5 * np.sin(2.0 * np.pi * 23.0 * T)) * 0.06
    Birds = np.zeros_like(T)
    for Start in (1.3, 4.9, 8.2, 10.6):
        Chirp = np.concatenate([Sweep(0.08, 2600.0, 3600.0) * Decay(0.08, 0.04, 0.01), np.zeros(Seconds(0.05)), Sweep(0.08, 2900.0, 3900.0) * Decay(0.08, 0.04, 0.01)])
        Offset = Seconds(Start)
        Birds[Offset:Offset + len(Chirp)] += Chirp * 0.12
    Write("Ambient_Island", MakeLoop(Wind + Insects + Birds, 1.0), 0.6, True)


if __name__ == "__main__":
    Build()
    print("Wrote", len([Name for Name in os.listdir(OutDir) if Name.endswith(".wav")]), "sounds to", os.path.abspath(OutDir))
