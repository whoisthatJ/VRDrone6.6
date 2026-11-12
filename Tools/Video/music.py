"""Background music for the product video, synthesised here (no samples, no third-party track, nothing to license).

python Tools/Video/music.py [seconds]  -> Recordings/video/voice/music.wav

A calm, steady bed: a warm pad on a four-chord loop, a soft bass, a plucked arpeggio with an echo and a light pulse.
"""
import os
import sys
import wave

import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
OUT = os.path.join(ROOT, 'Recordings', 'video', 'voice', 'music.wav')
RATE = 48000
BPM = 88.0
BEAT = 60.0 / BPM


def hz(semitones_from_a4):
    return 440.0 * 2.0 ** (semitones_from_a4 / 12.0)


# Am7 - Fmaj7 - C - G, one bar (four beats) each; notes as semitones from A4.
CHORDS = [
    {'root': -24, 'pad': [-12, -9, -5, -2], 'arp': [0, 3, 7, 10, 7, 3, 0, 3]},      # Am7
    {'root': -28, 'pad': [-16, -12, -9, -5], 'arp': [-4, 0, 3, 7, 3, 0, -4, 0]},    # Fmaj7
    {'root': -21, 'pad': [-9, -5, -2, 3], 'arp': [3, 7, 10, 15, 10, 7, 3, 7]},      # C
    {'root': -26, 'pad': [-14, -10, -7, -2], 'arp': [-2, 2, 5, 10, 5, 2, -2, 2]},   # G
]


def tone(freq, seconds, harmonics):
    t = np.arange(int(seconds * RATE)) / RATE
    out = np.zeros_like(t)
    for n, amp in harmonics:
        out += amp * np.sin(2 * np.pi * freq * n * t)
    return out


def add(track, at, sound, gain=1.0):
    i = int(at * RATE)
    if i >= len(track):
        return
    n = min(len(sound), len(track) - i)
    track[i:i + n] += sound[:n] * gain


def main(seconds):
    n = int(seconds * RATE)
    track = np.zeros(n + RATE * 4)
    bar = BEAT * 4
    bars = int(seconds / bar) + 2
    rng = np.random.default_rng(7)
    for b in range(bars):
        chord = CHORDS[b % len(CHORDS)]
        start = b * bar
        # Pad: soft, slightly detuned voices that swell in and overlap the next bar.
        length = bar * 1.35
        t = np.arange(int(length * RATE)) / RATE
        env = np.minimum(1.0, t / 0.9) * np.minimum(1.0, (length - t) / 1.1)
        for note in chord['pad']:
            f = hz(note)
            voice = tone(f * 0.997, length, [(1, 1.0), (2, 0.28), (3, 0.1)]) + tone(f * 1.003, length, [(1, 1.0), (2, 0.2)])
            add(track, start, voice * env, 0.035)
        # Bass: the root on the first and third beats, with a short swell.
        for beat in (0, 2):
            tb = np.arange(int(BEAT * 1.9 * RATE)) / RATE
            benv = np.minimum(1.0, tb / 0.02) * np.exp(-tb / 0.9)
            add(track, start + beat * BEAT, tone(hz(chord['root']), BEAT * 1.9, [(1, 1.0), (2, 0.35)]) * benv, 0.16)
        # Arpeggio: eighth notes, plucked, from the third bar on; with a dotted-eighth echo.
        if b >= 2:
            for k, note in enumerate(chord['arp']):
                ta = np.arange(int(0.7 * RATE)) / RATE
                pluck = tone(hz(note), 0.7, [(1, 1.0), (2, 0.4), (3, 0.15), (4, 0.06)]) * np.minimum(1.0, ta / 0.004) * np.exp(-ta / 0.16)
                at = start + k * BEAT / 2 + rng.uniform(-0.004, 0.004)
                level = 0.075 * (1.0 if k % 2 == 0 else 0.7)
                add(track, at, pluck, level)
                add(track, at + BEAT * 0.75, pluck, level * 0.4)
                add(track, at + BEAT * 1.5, pluck, level * 0.18)
        # Pulse: a soft low thump on every beat from the fifth bar on, to carry the picture along.
        if b >= 4:
            for beat in range(4):
                tk = np.arange(int(0.22 * RATE)) / RATE
                sweep = np.sin(2 * np.pi * (48 * tk + 30 * (1 - np.exp(-tk / 0.035)) * 0.035))
                add(track, start + beat * BEAT, sweep * np.exp(-tk / 0.07), 0.12 if beat % 2 == 0 else 0.07)
    track = track[:n]
    # A gentle low-pass (one-pole) so nothing is harsh under a voice.
    a = np.exp(-2 * np.pi * 3800 / RATE)
    out = np.empty_like(track)
    acc = 0.0
    for i in range(0, len(track), 4096):
        chunk = track[i:i + 4096]
        y = np.empty_like(chunk)
        for j, v in enumerate(chunk):
            acc = (1 - a) * v + a * acc
            y[j] = acc
        out[i:i + 4096] = y
    t = np.arange(n) / RATE
    out *= np.minimum(1.0, t / 2.0) * np.minimum(1.0, (seconds - t) / 3.5)
    out = out / np.abs(out).max() * 0.7
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with wave.open(OUT, 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes((out * 32767).astype(np.int16).tobytes())
    print('music', round(seconds, 1), 's ->', OUT)


if __name__ == '__main__':
    main(float(sys.argv[1]) if len(sys.argv) > 1 else 80.0)
