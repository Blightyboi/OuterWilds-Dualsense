"""Extracts engine sounds from the Outer Wilds data files and converts them into haptic waveforms.

Usage:
    python tools/extract_haptics.py [--list PATTERN] [--game "<Outer Wilds folder>"] [--out "<mod folder>/haptic-clips"]

OPTIONAL: the mod works without these files (it filters the game's live audio by default). They are only
used when the mod setting "useExtractedClips" is on.

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
MIN_BAND_PEAK = 0.01
MIN_BAND_RMS = 0.01  # quieter than this has nothing to feel, normalising would only amplify noise

# RULE: every sound that drives haptics gets a file here. Add the clip name, run this script.
# Looping engine sounds: loudness is normalised (RMS) because the mod follows the real thrust with its own gain.
LOOPS = [
    "OW_SP_ThrustTranslational",
    "OW_SP_ThrustTranslationalUnderwater",
    "OW_SP_ThrustAfterburn",
    "OW_PR_ThrustTranslational_v2_01",
    "OW_PR_ThrustAfterburn_v2_01",
    "OW_PR_ThrustUnderwater",
    "Jetpack_O2_loop_01",  # jetpack on oxygen: almost pure hiss, handled by envelope_rumble
    "OW_TH_ModelRocketThrustTranslational_01",
    "OW_TH_ModelRocketThrustTranslational_02",
]

# One-shot sounds (ignition, rotational thrusters): normalised by peak so their dynamics survive.
ONE_SHOTS = [
    "Engine_Start_V2",  # ship ignition
    "RotationalThruster01",
    "RotationalThruster02",
    "RotationalThruster03",
    "RotationalThruster04",
    "OW_SP_ThrustRotationalUnderwater_01",
    "OW_SP_ThrustRotationalUnderwater_02",
    "OW_SP_ThrustRotationalUnderwater_03",
    "OW_SP_ThrustRotationalUnderwater_04",
    "OW_SP_ThrustRotationalUnderwater_05",
    "OW_SP_ThrustRotationalUnderwater_06",
    "OW_PR_ThrustRotationalUnderwater_01",
    "OW_PR_ThrustRotationalUnderwater_02",
    "OW_PR_ThrustRotationalUnderwater_03",
    "OW_PR_ThrustRotationalUnderwater_04",
    "OW_PR_ThrustRotationalUnderwater_05",
    "OW_TH_ModelRocketThrustRotational_01",
    "OW_TH_ModelRocketThrustRotational_02",
    "OW_TH_ModelRocketThrustRotational_03",
    "OW_TH_ModelRocketThrustRotational_04",
]
TARGET_PEAK = 0.9


def read_wav(data):
    with wave.open(io.BytesIO(data)) as w:
        channels, width, rate, frames = w.getnchannels(), w.getsampwidth(), w.getframerate(), w.getnframes()
        raw = w.readframes(frames)
    if width != 2:
        raise ValueError("expected 16-bit PCM, got %d-bit" % (width * 8))
    values = struct.unpack("<%dh" % (len(raw) // 2), raw)
    mono = [sum(values[i * channels:(i + 1) * channels]) / (channels * 32768.0) for i in range(frames)]
    return mono, rate


def haptic_wave(mono, rate, one_shot=False):
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
    if one_shot:
        peak = max(abs(v) for v in out)
        if peak < MIN_BAND_PEAK:
            return None, rms
        scale = TARGET_PEAK / peak
        return [max(-1.0, min(1.0, v * scale)) for v in out], rms
    if rms < MIN_BAND_RMS:
        return None, rms
    scale = TARGET_RMS / rms
    return [max(-1.0, min(1.0, v * scale)) for v in out], rms


def envelope_rumble(mono, rate):
    """For clips without energy below 350 Hz (hiss): a low rumble whose loudness follows the sound's envelope."""
    smooth = 1.0 - math.exp(-2.0 * math.pi * 12.0 / rate)
    env = 0.0
    envelope = [0.0] * len(mono)
    for _ in range(2):
        for i, x in enumerate(mono):
            env += smooth * (abs(x) - env)
            envelope[i] = env
    mean = sum(envelope) / len(envelope)
    if mean < 1e-6:
        return None
    out = []
    for i, e in enumerate(envelope):
        t = i / rate
        carrier = 0.65 * math.sin(2 * math.pi * 62.0 * t) + 0.35 * math.sin(2 * math.pi * 97.0 * t)
        out.append(carrier * min(2.0, e / mean))
    rms = math.sqrt(sum(v * v for v in out) / len(out))
    scale = TARGET_RMS / rms
    return [max(-1.0, min(1.0, v * scale)) for v in out]


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
    for one_shot, names in ((False, LOOPS), (True, ONE_SHOTS)):
        for name in names:
            extract(clips, name, args.out, one_shot)


def extract(clips, name, out_dir, one_shot):
    clip = clips.get(name)
    if clip is None:
        print("missing in game files:", name)
        return
    try:
        samples = clip.samples  # {file name: wav bytes}
        mono, rate = read_wav(next(iter(samples.values())))
    except Exception as e:  # noqa: BLE001
        print("could not decode %s: %s" % (name, e))
        return
    shaped, rms = haptic_wave(mono, rate, one_shot)
    note = "scaled" if shaped else ""
    if shaped is None:
        shaped = envelope_rumble(mono, rate)
        if shaped is None:
            print("%s: silent, skipped" % name)
            return
        note = "no energy below %d Hz, envelope rumble" % LOW_PASS_HZ
    write_wav(os.path.join(out_dir, name + ".wav"), shaped, rate)
    print("%s [%s]: %.1f s, %d Hz, band rms %.4f (%s)" % (name, "one-shot" if one_shot else "loop", len(mono) / rate, rate, rms, note))


if __name__ == "__main__":
    sys.exit(main())
