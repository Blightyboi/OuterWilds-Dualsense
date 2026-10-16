"""Extracts engine sounds from the Outer Wilds data files and converts them into haptic waveforms.

Usage:
    python tools/extract_haptics.py [--list PATTERN] [--game "<Outer Wilds folder>"] [--out "<mod folder>/haptic-clips"]

The game's AudioClips are compressed in memory, so Unity cannot hand their samples to a mod at runtime.
This script reads them straight from the game files (UnityPy + FMOD), band-limits them to what the
DualSense actuators can reproduce (25-350 Hz), normalises the loudness and writes mono 16-bit WAV files
that the mod loads. The extracted audio is derived from the game's assets: do not redistribute it.
"""
import argparse
import io
import math
import os
import struct
import sys
import wave

import UnityPy

DEFAULT_GAME = r"D:\Steam\steamapps\common\Outer Wilds"
DEFAULT_OUT = r"D:\OWML\Mods\Blightyboi.OuterWilds-Dualsense\haptic-clips"

LOW_PASS_HZ = 350.0
HIGH_PASS_HZ = 25.0
TARGET_RMS = 0.5
MIN_BAND_RMS = 0.01  # quieter than this has nothing to feel, normalising would only amplify noise

# Clips used by the ship and jetpack thruster audio.
WANTED = [
    "OW_SP_ThrustTranslational",
    "OW_SP_ThrustTranslationalUnderwater",
    "OW_PR_ThrustTranslational_v2_01",
    "OW_PR_ThrustAfterburn_v2_01",
    "OW_PR_ThrustUnderwater",
]


def read_wav(data):
    with wave.open(io.BytesIO(data)) as w:
        channels, width, rate, frames = w.getnchannels(), w.getsampwidth(), w.getframerate(), w.getnframes()
        raw = w.readframes(frames)
    if width != 2:
        raise ValueError("expected 16-bit PCM, got %d-bit" % (width * 8))
    values = struct.unpack("<%dh" % (len(raw) // 2), raw)
    mono = [sum(values[i * channels:(i + 1) * channels]) / (channels * 32768.0) for i in range(frames)]
    return mono, rate


def haptic_wave(mono, rate):
    """Band-pass (two low-pass poles, one high-pass), run twice so the loop seam matches, then normalise."""
    lp = 1.0 - math.exp(-2.0 * math.pi * LOW_PASS_HZ / rate)
    hp = 1.0 - math.exp(-2.0 * math.pi * HIGH_PASS_HZ / rate)
    l1 = l2 = h = 0.0
    out = [0.0] * len(mono)
    for _ in range(2):
        for i, x in enumerate(mono):
            l1 += lp * (x - l1)
            l2 += lp * (l1 - l2)
            h += hp * (l2 - h)
            out[i] = l2 - h
    rms = math.sqrt(sum(v * v for v in out) / len(out))
    if rms < MIN_BAND_RMS:
        return None, rms
    scale = TARGET_RMS / rms
    return [max(-1.0, min(1.0, v * scale)) for v in out], rms


def write_wav(path, samples, rate):
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(rate)
        w.writeframes(b"".join(struct.pack("<h", int(v * 32767)) for v in samples))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--game", default=DEFAULT_GAME)
    parser.add_argument("--out", default=DEFAULT_OUT)
    parser.add_argument("--list", metavar="PATTERN", help="only list audio clips whose name contains PATTERN")
    args = parser.parse_args()

    data_dir = os.path.join(args.game, "OuterWilds_Data")
    env = UnityPy.load(data_dir)

    clips = {}
    for obj in env.objects:
        if obj.type.name == "AudioClip":
            clip = obj.read()
            clips.setdefault(clip.m_Name, clip)

    if args.list is not None:
        for name in sorted(clips):
            if args.list.lower() in name.lower():
                print(name)
        return

    os.makedirs(args.out, exist_ok=True)
    for name in WANTED:
        clip = clips.get(name)
        if clip is None:
            print("missing in game files:", name)
            continue
        try:
            samples = clip.samples  # {file name: wav bytes}
            wav_bytes = next(iter(samples.values()))
            mono, rate = read_wav(wav_bytes)
        except Exception as e:  # noqa: BLE001
            print("could not decode %s: %s" % (name, e))
            continue
        shaped, rms = haptic_wave(mono, rate)
        if shaped is None:
            print("%s: no energy in the haptic band (rms %.2e)" % (name, rms))
            continue
        write_wav(os.path.join(args.out, name + ".wav"), shaped, rate)
        print("%s: %.1f s, %d Hz, band rms %.4f -> scaled x%.1f" % (name, len(mono) / rate, rate, rms, TARGET_RMS / rms))


if __name__ == "__main__":
    sys.exit(main())
