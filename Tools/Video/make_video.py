"""Product video: game footage cut to the narration, with feature captions.

1. python Tools/Video/voice.py        -> Recordings/video/voice/*.wav + timing.json
2. python Tools/Video/make_video.py   -> Docs/video/USHQYR_overview_ru.mp4 (1920x1080, 30 fps)
   (it runs Tools/Video/music.py for the music bed)

Footage is the frame sequences in Recordings/clips/<clip>/ (PitchClips, 30 fps).
"""
import glob
import json
import os
import subprocess
import sys
import wave

import imageio_ffmpeg
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
CLIPS = os.path.join(ROOT, 'Recordings', 'clips')
VOICE = os.path.join(ROOT, 'Recordings', 'video', 'voice')
OUT_DIR = os.path.join(ROOT, 'Docs', 'video')
FF = imageio_ffmpeg.get_ffmpeg_exe()
W, H, FPS = 1920, 1080, 30
LEAD, TAIL, FADE = 0.45, 0.45, 0.4      # silence before / after each line, and the cross-fade between shots (s)
MUSIC_GAIN = 0.42                         # music level in the gaps; it drops to 40 % of that under the voice
ACCENT = (255, 140, 38)
BOLD = 'C:/Windows/Fonts/segoeuib.ttf'
REGULAR = 'C:/Windows/Fonts/segoeui.ttf'

# Per narration segment: the shots, as (clip, first frame, share of the segment, playback speed).
# A clip with a single frame is a still (slow push-in).
SHOTS = {
    'intro': [('aerial_expo', 0, 1.0, 1.0)],
    'physics': [('fpv_expo', 20, 0.58, 1.0), ('strike_fpv', 40, 0.42, 1.0)],
    'garage': [('garage', 0, 1.0, 0.78)],
    'race': [('chase_stadium', 30, 0.48, 1.0), ('bots_race', 60, 0.52, 1.0)],
    'strike': [('strike_targets', 40, 0.3, 1.0), ('strike_chase', 100, 0.4, 1.0), ('strike_fpv', 330, 0.3, 1.0)],
    'online': [('menu_lan', 0, 0.36, 1.0), ('bots_chase', 60, 0.64, 1.0)],
    'outro': [('aerial_expo', 120, 1.0, 0.6)],
}


def frames_of(clip):
    files = sorted(glob.glob(os.path.join(CLIPS, clip, '*.jpg')) + glob.glob(os.path.join(CLIPS, clip, '*.png')))
    if not files:
        raise SystemExit(f'no frames for clip {clip}')
    return files


def load(path, zoom=1.0):
    im = Image.open(path).convert('RGB')
    if im.size != (W, H):
        im = im.resize((W, H), Image.LANCZOS)
    if zoom != 1.0:
        w, h = round(W / zoom), round(H / zoom)
        im = im.crop(((W - w) // 2, (H - h) // 2, (W + w) // 2, (H + h) // 2)).resize((W, H), Image.BILINEAR)
    return im


def caption_card(text):
    """A lower-third: an accent bar and the feature name on a dark plate."""
    font = ImageFont.truetype(BOLD, 54)
    pad, bar = 28, 12
    tw = int(font.getlength(text))
    card = Image.new('RGBA', (tw + pad * 2 + bar, 104), (0, 0, 0, 0))
    d = ImageDraw.Draw(card)
    d.rectangle([0, 0, card.width, card.height], fill=(14, 17, 24, 215))
    d.rectangle([0, 0, bar, card.height], fill=ACCENT + (255,))
    d.text((bar + pad, 12), text, font=font, fill=(240, 241, 245, 255))
    return card


def logo(path, height):
    im = Image.open(path).convert('RGBA')
    box = im.getbbox()
    im = im.crop(box) if box else im
    return im.resize((max(1, round(im.width * height / im.height)), height), Image.LANCZOS)


def title_card(sub, icon_path, logos=()):
    """The name, with the game icon and a line under it (intro), or the makers' logos (outro)."""
    card = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(card)
    big, small = ImageFont.truetype(BOLD, 190), ImageFont.truetype(REGULAR, 56)
    icon = Image.open(icon_path).convert('RGBA').resize((300, 300), Image.LANCZOS)
    name = 'USHQYR'
    nw = int(big.getlength(name))
    x = (W - (300 + 60 + nw)) // 2
    y = 150 if logos else 360
    card.alpha_composite(icon, (x, y))
    d.text((x + 360, y + 10), name, font=big, fill=(255, 255, 255, 255))
    sw = int(small.getlength(sub))
    d.rectangle([(W - sw) // 2 - 40, y + 356, (W + sw) // 2 + 40, y + 362], fill=ACCENT + (255,))
    if not logos:
        d.text(((W - sw) // 2, y + 384), sub, font=small, fill=(235, 236, 240, 255))
        return card
    # The makers' logos side by side under the line, the same height, with their names in small type below.
    marks = [logo(p, 210) for p, _ in logos]
    gap = 150
    width = sum(m.width for m in marks) + gap * (len(marks) - 1)
    lx = (W - width) // 2
    tiny = ImageFont.truetype(REGULAR, 34)
    for m, (_, label) in zip(marks, logos):
        card.alpha_composite(m, (lx, y + 400))
        lw = int(tiny.getlength(label))
        d.text((lx + (m.width - lw) // 2, y + 624), label, font=tiny, fill=(215, 217, 224, 255))
        lx += m.width + gap
    return card


def with_alpha(card, a):
    if a >= 0.999:
        return card
    out = card.copy()
    out.putalpha(out.split()[3].point(lambda v: int(v * a)))
    return out


def ramp(t, start, end, fade=0.35):
    """0 -> 1 over 'fade' after start, 1 -> 0 over 'fade' before end."""
    return max(0.0, min(1.0, (t - start) / fade, (end - t) / fade))


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    timing = json.load(open(os.path.join(VOICE, 'timing.json'), encoding='utf-8'))
    icon = os.path.join(ROOT, 'Tools', 'Icon', 'Ushqyr.png')

    # Timeline: each segment lasts its narration plus a breath either side.
    segments = []
    t = 0.0
    for seg in timing:
        length = LEAD + seg['seconds'] + TAIL + (2.6 if seg['id'] == 'outro' else 0.0)  # hold the closing card so the logos can be read
        shots = []
        st = t
        for clip, first, share, speed in SHOTS[seg['id']]:
            shots.append({'files': frames_of(clip), 'first': first, 'start': st, 'length': length * share, 'speed': speed})
            st += length * share
        segments.append({'id': seg['id'], 'start': t, 'length': length, 'voice': seg['file'], 'shots': shots,
                         'caption': caption_card(seg['caption']) if seg['id'] not in ('intro', 'outro') else None})
        t += length
    total = t
    shots = [s for seg in segments for s in seg['shots']]
    intro_card = title_card('Тренажёр операторов БПЛА', icon)
    logos_dir = os.path.join(ROOT, 'Docs', 'pitch', 'logos')
    outro_card = title_card('Multimedia Lab · SDU University', icon, logos=[
        (os.path.join(logos_dir, 'multimedia_lab_white.png'), 'Multimedia Lab'),
        (os.path.join(logos_dir, 'sdu_university.png'), 'SDU University'),
    ])

    def shot_frame(shot, now):
        local = max(0.0, now - shot['start'])
        files = shot['files']
        if len(files) == 1:
            return load(files[0], 1.0 + 0.06 * local / max(shot['length'], 1e-3))
        return load(files[min(len(files) - 1, shot['first'] + int(local * FPS * shot['speed']))])

    # Narration track: each line placed LEAD after its segment starts.
    rate = 48000
    track = np.zeros(int(total * rate) + rate, dtype=np.int16)
    for seg in segments:
        with wave.open(os.path.join(VOICE, seg['voice']), 'rb') as wv:
            data = np.frombuffer(wv.readframes(wv.getnframes()), dtype=np.int16)
        at = int((seg['start'] + LEAD) * rate)
        track[at:at + len(data)] = data
    # Music bed (Tools/Video/music.py, synthesised for the exact length): quiet under the voice, fuller in the gaps.
    subprocess.run([sys.executable, os.path.join(os.path.dirname(__file__), 'music.py'), f'{total:.2f}'], check=True)
    with wave.open(os.path.join(VOICE, 'music.wav'), 'rb') as wv:
        music = np.frombuffer(wv.readframes(wv.getnframes()), dtype=np.int16).astype(np.float64)
    voice = track.astype(np.float64)
    hop = rate // 50
    level = np.sqrt((voice[:len(voice) // hop * hop].reshape(-1, hop) ** 2).mean(axis=1))
    speaking = np.convolve((level > 250).astype(np.float64), np.ones(30) / 30, mode='same')      # ~0.6 s smoothing
    duck = np.repeat(1.0 - 0.6 * np.clip(speaking * 1.5, 0, 1), hop)
    m = min(len(music), len(duck), len(voice))
    mixed = voice.copy()
    mixed[:m] += music[:m] * duck[:m] * MUSIC_GAIN
    track = np.clip(mixed, -32767, 32767).astype(np.int16)
    audio = os.path.join(VOICE, 'track.wav')
    with wave.open(audio, 'wb') as wv:
        wv.setnchannels(1)
        wv.setsampwidth(2)
        wv.setframerate(rate)
        wv.writeframes(track.tobytes())

    out = os.path.join(OUT_DIR, 'USHQYR_overview_ru.mp4')
    ff = subprocess.Popen([FF, '-y', '-loglevel', 'error', '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-s', f'{W}x{H}', '-r', str(FPS), '-i', '-',
                           '-i', audio, '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-crf', '25', '-preset', 'slow',
                           '-c:a', 'aac', '-b:a', '160k', '-shortest', '-movflags', '+faststart', out], stdin=subprocess.PIPE)
    n = int(total * FPS)
    for f in range(n):
        now = f / FPS
        k = max(i for i, s in enumerate(shots) if s['start'] <= now + 1e-6)
        frame = shot_frame(shots[k], now)
        # Cross-fade out of the previous shot.
        into = now - shots[k]['start']
        if k > 0 and into < FADE:
            frame = Image.blend(shot_frame(shots[k - 1], now), frame, into / FADE)
        seg = max((s for s in segments if s['start'] <= now + 1e-6), key=lambda s: s['start'])
        local = now - seg['start']
        frame = frame.convert('RGBA')
        if seg['id'] == 'intro':
            a = ramp(local, 0.0, 4.6, 0.5)
            if a > 0:
                dim = Image.new('RGBA', (W, H), (8, 10, 16, int(150 * a)))
                frame.alpha_composite(dim)
                frame.alpha_composite(with_alpha(intro_card, a))
        elif seg['id'] == 'outro':
            a = min(1.0, local / 0.6)
            frame = Image.alpha_composite(frame.filter(ImageFilter.GaussianBlur(10 * a)), Image.new('RGBA', (W, H), (8, 10, 16, int(170 * a))))
            frame.alpha_composite(with_alpha(outro_card, a))
        elif seg['caption'] is not None:
            a = ramp(local, 0.25, seg['length'] - 0.15)
            if a > 0:
                frame.alpha_composite(with_alpha(seg['caption'], a), (70 - int(30 * (1 - a)), H - 190))
        # Fade from and to black at the very ends.
        edge = min(1.0, now / 0.5, (total - now) / 0.8)
        if edge < 1.0:
            frame = Image.blend(Image.new('RGBA', (W, H), (0, 0, 0, 255)), frame, max(0.0, edge))
        ff.stdin.write(frame.convert('RGB').tobytes())
        if f % 300 == 0:
            print(f'{f}/{n}', flush=True)
    ff.stdin.close()
    ff.wait()
    print('done', out, round(total, 1), 's')


if __name__ == '__main__':
    main()
