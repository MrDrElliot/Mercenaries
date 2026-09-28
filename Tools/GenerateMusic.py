import os
import sys
import wave
import numpy as np
from scipy import signal

Rate = 48000
Bpm = 120.0
Beat = 60.0 / Bpm
Bar = Beat * 4.0
Rng = np.random.default_rng(2008)
OutDir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "SourceAudio")

# D minor, one chord per bar, i VI III VII.
Progression = [
    ("Dm", [50, 53, 57, 62], 38),
    ("Bb", [46, 50, 53, 58], 34),
    ("F", [45, 48, 53, 57], 41),
    ("C", [48, 52, 55, 60], 36),
]


def Hz(Note):
    return 440.0 * 2.0 ** ((Note - 69) / 12.0)


def Frames(Seconds):
    return int(round(Seconds * Rate))


def Time(Seconds):
    return np.arange(Frames(Seconds)) / Rate


def Noise(Seconds):
    return Rng.uniform(-1.0, 1.0, Frames(Seconds))


def Filter(Samples, Kind, Cutoff, Order=2):
    Band = np.clip(np.asarray(Cutoff, dtype=float) / (Rate * 0.5), 1e-4, 0.999)
    Sos = signal.butter(Order, Band, btype=Kind, output="sos")
    return signal.sosfilt(Sos, Samples)


def Envelope(Seconds, Attack, Release, Hold=None):
    Count = Frames(Seconds)
    T = np.arange(Count) / Rate
    Rise = np.clip(T / max(Attack, 1e-5), 0.0, 1.0)
    Fall = np.clip((Seconds - T) / max(Release, 1e-5), 0.0, 1.0)
    return Rise * Fall


def Saw(Frequency, Seconds, Phase=0.0):
    T = Time(Seconds)
    Cycles = np.cumsum(np.broadcast_to(Frequency, T.shape)) / Rate + Phase
    return 2.0 * (Cycles - np.floor(Cycles + 0.5))


def Sine(Frequency, Seconds):
    T = Time(Seconds)
    return np.sin(2.0 * np.pi * np.cumsum(np.broadcast_to(Frequency, T.shape)) / Rate)


def SuperSaw(Frequency, Seconds, Voices=7, Cents=14.0):
    Out = np.zeros(Frames(Seconds))
    for Index in range(Voices):
        Spread = (Index / max(Voices - 1, 1) - 0.5) * 2.0 * Cents
        Out += Saw(Frequency * 2.0 ** (Spread / 1200.0), Seconds, Rng.uniform(0.0, 1.0))
    return Out / np.sqrt(Voices)


# A filter sweep without a time-varying filter, by fading between a bright and a dark render of the same tone.
def Sweep(Samples, Bright, Dark, Curve):
    return Filter(Samples, "low", Bright) * Curve + Filter(Samples, "low", Dark) * (1.0 - Curve)


def Drive(Samples, Amount):
    return np.tanh(Samples * Amount) / np.tanh(Amount)


def Kick():
    T = Time(0.55)
    Pitch = 44.0 + 110.0 * np.exp(-T / 0.035)
    Body = Sine(Pitch, 0.55) * np.exp(-T / 0.26)
    Click = Filter(Noise(0.55), "high", 2500.0) * np.exp(-T / 0.004) * 0.35
    return Drive(Body + Click, 1.6)


def Snare():
    T = Time(0.4)
    Rattle = Filter(Noise(0.4), "band", [1400.0, 9000.0]) * np.exp(-T / 0.13)
    Tone = Sine(188.0, 0.4) * np.exp(-T / 0.06) + Sine(330.0, 0.4) * np.exp(-T / 0.04) * 0.5
    return Drive(Rattle * 0.8 + Tone * 0.7, 1.4)


def Hat(Open=False):
    Length = 0.3 if Open else 0.06
    T = Time(Length)
    return Filter(Noise(Length), "high", 7500.0) * np.exp(-T / (0.12 if Open else 0.018)) * 0.5


def Taiko():
    T = Time(1.2)
    Pitch = 58.0 + 45.0 * np.exp(-T / 0.05)
    Body = Sine(Pitch, 1.2) * np.exp(-T / 0.45)
    Skin = Filter(Noise(1.2), "low", 700.0) * np.exp(-T / 0.05) * 0.8
    return Drive(Body + Skin, 1.8)


def Tom(Note):
    T = Time(0.6)
    Pitch = Hz(Note) * (1.0 + 0.5 * np.exp(-T / 0.03))
    return Drive(Sine(Pitch, 0.6) * np.exp(-T / 0.2) + Filter(Noise(0.6), "low", 1500.0) * np.exp(-T / 0.03) * 0.4, 1.5)


def Impact():
    T = Time(3.0)
    Sub = Sine(34.0 + 30.0 * np.exp(-T / 0.12), 3.0) * np.exp(-T / 1.1)
    Crack = Filter(Noise(3.0), "low", 3500.0) * np.exp(-T / 0.35) * 0.7
    return Drive(Sub * 1.2 + Crack + Pad(Taiko(), 3.0) * 0.8, 2.0)


def Pad(Samples, Seconds):
    Out = np.zeros(Frames(Seconds))
    Count = min(len(Samples), len(Out))
    Out[:Count] = Samples[:Count]
    return Out


def Bass(Note, Seconds):
    T = Time(Seconds)
    Tone = Saw(Hz(Note), Seconds) * 0.7 + Sine(Hz(Note) * 0.5, Seconds) * 0.5
    Tone = Sweep(Tone, 900.0, 220.0, np.exp(-T / 0.06))
    return Drive(Tone * Envelope(Seconds, 0.004, 0.03), 1.8)


def Pluck(Note, Seconds=0.22):
    T = Time(Seconds)
    Tone = SuperSaw(Hz(Note), Seconds, Voices=3, Cents=8.0)
    return Sweep(Tone, 4200.0, 700.0, np.exp(-T / 0.05)) * np.exp(-T / 0.09) * Envelope(Seconds, 0.002, 0.02)


def Chord(Notes, Seconds, Attack=0.6, Release=0.8, Cutoff=1800.0):
    Out = np.zeros(Frames(Seconds))
    for Note in Notes:
        Out += SuperSaw(Hz(Note), Seconds)
    return Filter(Out, "low", Cutoff) * Envelope(Seconds, Attack, Release) / len(Notes)


def Stab(Notes, Seconds=0.45):
    T = Time(Seconds)
    Out = np.zeros(Frames(Seconds))
    for Note in Notes:
        Out += SuperSaw(Hz(Note), Seconds, Voices=5) + SuperSaw(Hz(Note - 12), Seconds, Voices=3) * 0.6
    Out = Sweep(Out, 5000.0, 600.0, np.exp(-T / 0.09)) / len(Notes)
    return Drive(Out * np.exp(-T / 0.22) * Envelope(Seconds, 0.006, 0.05), 1.5)


def Braam(Root, Seconds=4.5):
    T = Time(Seconds)
    Bend = 2.0 ** (-np.clip(T / Seconds, 0.0, 1.0) * 1.0 / 12.0)
    Out = np.zeros(Frames(Seconds))
    for Note, Level in ((Root, 1.0), (Root + 7, 0.6), (Root - 12, 1.0), (Root + 12, 0.35)):
        Out += SuperSaw(Hz(Note) * Bend, Seconds, Voices=7, Cents=18.0) * Level
    Out = Sweep(Out, 1400.0, 380.0, np.exp(-T / 0.8))
    return Drive(Out * Envelope(Seconds, 0.02, 1.8) * 0.5, 2.6)


def Riser(Seconds):
    T = Time(Seconds)
    Progress = T / Seconds
    Hiss = np.zeros(Frames(Seconds))
    Block = Frames(0.05)
    Source = Noise(Seconds)
    for Start in range(0, len(Hiss), Block):
        Cutoff = 300.0 * (40.0 ** (Start / len(Hiss)))
        Hiss[Start:Start + Block] = Filter(Source[max(Start - Block, 0):Start + Block], "band", [Cutoff * 0.5, min(Cutoff * 2.0, 20000.0)])[-len(Hiss[Start:Start + Block]):]
    Tone = Sine(180.0 * (8.0 ** Progress), Seconds) * 0.25
    return (Hiss * 0.8 + Tone) * Progress ** 2.5


def Roll(Seconds):
    Out = np.zeros(Frames(Seconds))
    Hit = Snare()
    Time0 = 0.0
    while Time0 < Seconds:
        Progress = Time0 / Seconds
        Start = Frames(Time0)
        Piece = Hit[:len(Out) - Start] * (0.25 + 0.75 * Progress)
        Out[Start:Start + len(Piece)] += Piece
        Time0 += Beat / (2.0 if Progress < 0.5 else (4.0 if Progress < 0.8 else 8.0))
    return Out


# Everything lands in a stereo bus, with per-voice panning and a send to a shared hall.
class Track:
    def __init__(self, Seconds):
        self.Dry = np.zeros((2, Frames(Seconds) + Rate * 4))
        self.Wet = np.zeros_like(self.Dry)

    def Add(self, At, Samples, Level=1.0, Pan=0.0, Send=0.15):
        Start = Frames(At)
        if Start >= self.Dry.shape[1]:
            return
        Samples = Samples[:self.Dry.shape[1] - Start] * Level
        Left = np.sqrt(0.5 * (1.0 - Pan))
        Right = np.sqrt(0.5 * (1.0 + Pan))
        End = Start + len(Samples)
        self.Dry[0, Start:End] += Samples * Left
        self.Dry[1, Start:End] += Samples * Right
        self.Wet[0, Start:End] += Samples * Left * Send
        self.Wet[1, Start:End] += Samples * Right * Send

    def Render(self, Seconds, Wrap=False):
        Tail = Frames(2.8)
        T = np.arange(Tail) / Rate
        Out = self.Dry.copy()
        for Channel in range(2):
            Response = Filter(Rng.normal(0.0, 1.0, Tail), "low", 6000.0) * np.exp(-T / 0.55)
            Response[:Frames(0.02)] = 0.0
            Response /= np.sqrt(np.sum(Response ** 2))
            Out[Channel] += signal.fftconvolve(self.Wet[Channel], Response)[:Out.shape[1]] * 0.9
        Length = Frames(Seconds)
        if Wrap:
            Out[:, :Out.shape[1] - Length] += Out[:, Length:]
        return Out[:, :Length]


def ChordAt(Time0):
    return Progression[int(Time0 // Bar) % len(Progression)]


def Drums(Song, Start, End, Style, Level=1.0):
    Step = Beat / 4.0
    Index = 0
    Time0 = Start
    while Time0 < End - 1e-6:
        Sixteenth = Index % 16
        if Style == "pulse":
            if Sixteenth in (0, 8):
                Song.Add(Time0, Kick(), 0.8 * Level, Send=0.05)
        elif Style in ("groove", "climax"):
            if Sixteenth in (0, 6, 8, 10) or (Style == "climax" and Sixteenth == 14):
                Song.Add(Time0, Kick(), 0.9 * Level, Send=0.05)
            if Sixteenth in (4, 12):
                Song.Add(Time0, Snare(), 0.75 * Level, Send=0.25)
            if Style == "climax" or Sixteenth % 2 == 0:
                Song.Add(Time0, Hat(Sixteenth == 14), 0.35 * Level, Pan=0.35 if Sixteenth % 4 else -0.2, Send=0.08)
            if Sixteenth == 0 and (Index // 16) % 2 == 0:
                Song.Add(Time0, Taiko(), 0.55 * Level, Pan=-0.3, Send=0.35)
        elif Style == "halftime":
            if Sixteenth == 0:
                Song.Add(Time0, Taiko(), 0.8 * Level, Send=0.45)
            if Sixteenth == 8:
                Song.Add(Time0, Kick(), 0.7 * Level, Send=0.2)
        Time0 += Step
        Index += 1


def Bassline(Song, Start, End, Level=0.55):
    Time0 = Start
    while Time0 < End - 1e-6:
        Root = ChordAt(Time0)[2]
        Offbeat = int(round((Time0 - Start) / (Beat / 2.0))) % 2 == 1
        Song.Add(Time0, Bass(Root + (12 if Offbeat else 0), Beat / 2.0 * 0.9), Level, Send=0.03)
        Time0 += Beat / 2.0


def Ostinato(Song, Start, End, Octave=12, Level=0.22):
    Shape = [0, 2, 1, 2, 3, 2, 1, 2]
    Time0 = Start
    Index = 0
    while Time0 < End - 1e-6:
        Notes = ChordAt(Time0)[1]
        Note = Notes[Shape[Index % len(Shape)]] + Octave
        Song.Add(Time0, Pluck(Note), Level, Pan=0.45 if Index % 2 else -0.45, Send=0.3)
        Time0 += Beat / 4.0
        Index += 1


def Pads(Song, Start, End, Level=0.3, Cutoff=1800.0):
    Time0 = Start
    while Time0 < End - 1e-6:
        Length = min(Bar, End - Time0)
        Song.Add(Time0, Chord(ChordAt(Time0)[1], Length + 0.6, Attack=0.5, Release=0.9, Cutoff=Cutoff), Level, Send=0.45)
        Time0 += Bar


def Stabs(Song, Start, End, Level=0.4, Dense=False):
    Time0 = Start
    while Time0 < End - 1e-6:
        Offset = (Time0 - Start) % Bar
        if Offset < 1e-6 or abs(Offset - Beat * 1.5) < 1e-6 or (Dense and abs(Offset - Beat * 3.0) < 1e-6):
            Song.Add(Time0, Stab([Note + 12 for Note in ChordAt(Time0)[1][:3]]), Level, Send=0.3)
        Time0 += Beat / 2.0


def Trailer():
    Length = 64.0
    Song = Track(Length)

    # Opening, a low drone under the title card.
    Song.Add(0.0, Chord([26, 38, 45], 7.0, Attack=2.5, Release=1.5, Cutoff=500.0), 0.5, Send=0.5)
    Song.Add(1.2, Impact(), 0.7, Send=0.6)
    Song.Add(3.5, Riser(2.5), 0.25, Send=0.4)

    # The merc and the crane, a pulse that builds.
    Song.Add(6.0, Taiko(), 0.8, Send=0.5)
    Pads(Song, 6.0, 16.0, 0.25, Cutoff=900.0)
    Ostinato(Song, 8.0, 16.0, Octave=12, Level=0.14)
    Drums(Song, 10.0, 14.0, "pulse")
    Bassline(Song, 12.0, 16.0, 0.4)
    Song.Add(14.0, Roll(2.0), 0.6, Send=0.3)
    Song.Add(13.5, Riser(2.5), 0.35, Send=0.3)

    # The convoy rolls.
    Song.Add(16.0, Impact(), 0.8, Send=0.5)
    Drums(Song, 16.0, 22.0, "groove")
    Bassline(Song, 16.0, 22.0)
    Ostinato(Song, 16.0, 22.0)
    Pads(Song, 16.0, 22.0, 0.22)
    Song.Add(20.0, Riser(2.0), 0.4, Send=0.3)
    for Index, Note in enumerate([50, 47, 45, 43]):
        Song.Add(21.0 + Index * Beat / 2.0, Tom(Note), 0.6, Pan=0.3 - Index * 0.2, Send=0.3)

    # The firefight and the carpet bombing, full drive.
    Song.Add(22.0, Impact(), 0.9, Send=0.5)
    Drums(Song, 22.0, 34.0, "groove")
    Bassline(Song, 22.0, 34.0)
    Ostinato(Song, 22.0, 34.0)
    Pads(Song, 22.0, 34.0, 0.22)
    Stabs(Song, 26.0, 34.0)
    Song.Add(29.5, Taiko(), 0.9, Send=0.5)
    Song.Add(32.5, Riser(2.4), 0.75, Send=0.4)

    # The refinery in slow motion, half time under the blast.
    Song.Add(34.9, Braam(38, 5.0), 0.65, Send=0.5)
    Song.Add(34.9, Impact(), 0.9, Send=0.6)
    Drums(Song, 35.0, 41.0, "halftime")
    Song.Add(35.0, Chord([50, 53, 57, 62, 65], 6.0, Attack=1.2, Release=1.5, Cutoff=1200.0), 0.35, Send=0.6)
    Song.Add(36.7, Impact(), 0.8, Send=0.6)
    Song.Add(39.0, Riser(2.0), 0.45, Send=0.4)
    Song.Add(40.0, Roll(1.0), 0.6, Send=0.3)

    # The capital comes down, the peak.
    Song.Add(41.0, Impact(), 1.0, Send=0.5)
    Drums(Song, 41.0, 46.9, "climax")
    Bassline(Song, 41.0, 46.9, 0.6)
    Ostinato(Song, 41.0, 46.9, Octave=24, Level=0.2)
    Pads(Song, 41.0, 46.9, 0.25)
    Stabs(Song, 41.0, 46.9, 0.45, Dense=True)
    Song.Add(43.8, Taiko(), 1.0, Send=0.5)

    # A cut to black and the hero shot, strings only.
    Song.Add(47.0, Impact(), 0.8, Send=0.7)
    Song.Add(47.0, Chord([38, 50, 53, 57], 4.0, Attack=0.8, Release=1.5, Cutoff=1400.0), 0.35, Send=0.6)
    Song.Add(49.0, Braam(38, 4.0), 0.4, Send=0.6)
    Song.Add(51.0, Chord([34, 46, 50, 53], 4.6, Attack=0.8, Release=1.5, Cutoff=1400.0), 0.35, Send=0.6)
    for Index, Note in enumerate([62, 65, 69, 70, 69, 65, 62, 64]):
        Song.Add(49.0 + Index * Beat, Pluck(Note + 12, 0.5), 0.12, Pan=0.3, Send=0.6)

    # One last build into the end card.
    Song.Add(55.0, Chord([45, 49, 52, 57], 5.0, Attack=1.0, Release=0.5, Cutoff=1600.0), 0.35, Send=0.5)
    Ostinato(Song, 56.0, 60.0, Octave=12, Level=0.16)
    Drums(Song, 57.0, 59.5, "pulse")
    Song.Add(57.0, Riser(3.0), 0.5, Send=0.4)
    Song.Add(58.0, Roll(2.0), 0.7, Send=0.3)
    Song.Add(60.0, Impact(), 1.0, Send=0.7)
    Song.Add(60.0, Braam(38, 4.0), 0.75, Send=0.6)

    Out = Song.Render(Length)
    Fade = np.clip((Length - np.arange(Out.shape[1]) / Rate) / 1.5, 0.0, 1.0)
    return Out * Fade


def Combat():
    Length = Bar * 8.0
    Song = Track(Length)
    Drums(Song, 0.0, Length, "groove")
    Bassline(Song, 0.0, Length)
    Ostinato(Song, 0.0, Length)
    Pads(Song, 0.0, Length, 0.2)
    Stabs(Song, Bar * 4.0, Length, 0.35)
    for Index, Note in enumerate([50, 47, 45, 43]):
        Song.Add(Length - Beat * 2.0 + Index * Beat / 2.0, Tom(Note), 0.55, Pan=0.3 - Index * 0.2, Send=0.3)
    return Song.Render(Length, Wrap=True)


def Write(Name, Stereo, Peak=0.89):
    Stereo = Drive(Stereo / max(np.max(np.abs(Stereo)), 1e-6) * 1.15, 1.2)
    Stereo *= Peak / max(np.max(np.abs(Stereo)), 1e-6)
    Pcm = np.clip(Stereo.T * 32767.0, -32768, 32767).astype(np.int16)
    with wave.open(os.path.join(OutDir, Name + ".wav"), "wb") as File:
        File.setnchannels(2)
        File.setsampwidth(2)
        File.setframerate(Rate)
        File.writeframes(Pcm.tobytes())
    print("Wrote", Name, f"{Stereo.shape[1] / Rate:.1f}s")


if __name__ == "__main__":
    os.makedirs(OutDir, exist_ok=True)
    Write("Music_Trailer", Trailer())
    Write("Music_Combat", Combat())
