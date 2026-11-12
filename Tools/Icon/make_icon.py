"""Game icon from the garage's own drones.

1. In Unity: Ushqyr > Capture > Render Icon Drones (writes Recordings/icon/<build>_<view>_{k,w}.png:
   each drone on black and on white).
2. python Tools/Icon/make_icon.py sheet            -> Recordings/icon/out/cutouts.png (every drone, every view)
   python Tools/Icon/make_icon.py options          -> Recordings/icon/out/options.png (icon candidates)
   python Tools/Icon/make_icon.py final <build> <view> <style>
       -> Tools/Icon/Ushqyr.png (1024) and Tools/Icon/Ushqyr.ico (16-256)
3. The shipped icon is the 5" Race drone over the stadium at night:
   Ushqyr > Capture > Render Icon Stadium, then
   python Tools/Icon/make_icon.py stadium                 -> Recordings/icon/out/stadium_options.png
   python Tools/Icon/make_icon.py final-stadium night a   -> Tools/Icon/Ushqyr.png and .ico
"""
import os
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
SRC = os.path.join(ROOT, 'Recordings', 'icon')
OUT = os.path.join(SRC, 'out')
BUILDS = ['race5', 'freestyle5', 'toothpick3', 'cinewhoop35', 'whoop65', 'lr7']
VIEWS = ['hero', 'top', 'front']

# The game's own UI colours (UiKit): panel, button, accent.
PANEL = (20, 23, 31)
BUTTON = (46, 51, 66)
ACCENT = (255, 140, 38)


def cutout(build, view):
    """RGBA cut-out from the black- and white-background renders (difference matting)."""
    k = np.asarray(Image.open(os.path.join(SRC, f'{build}_{view}_k.png')).convert('RGB')).astype(np.float64) / 255
    w = np.asarray(Image.open(os.path.join(SRC, f'{build}_{view}_w.png')).convert('RGB')).astype(np.float64) / 255
    alpha = np.clip(1.0 - (w - k).mean(axis=2), 0, 1)
    rgb = np.where(alpha[..., None] > 1e-3, k / np.maximum(alpha[..., None], 1e-3), 0)
    rgba = np.dstack([np.clip(rgb, 0, 1), alpha])
    im = Image.fromarray((rgba * 255 + 0.5).astype(np.uint8), 'RGBA')
    box = im.getbbox()
    return im.crop(box) if box else im


def fit(im, size, scale):
    """The cut-out scaled to 'scale' of a size x size tile, centred."""
    target = size * scale
    f = target / max(im.size)
    im = im.resize((max(1, round(im.width * f)), max(1, round(im.height * f))), Image.LANCZOS)
    tile = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    tile.alpha_composite(im, ((size - im.width) // 2, (size - im.height) // 2))
    return tile


def rounded(size, radius, fill):
    m = Image.new('L', (size * 2, size * 2), 0)
    ImageDraw.Draw(m).rounded_rectangle([0, 0, size * 2 - 1, size * 2 - 1], radius * 2, fill=255)
    m = m.resize((size, size), Image.LANCZOS)
    tile = Image.new('RGBA', (size, size), fill + (255,))
    tile.putalpha(m)
    return tile


def radial(size, inner, outer, centre=(0.5, 0.42), reach=0.85):
    y, x = np.mgrid[0:size, 0:size] / float(size)
    d = np.clip(np.hypot(x - centre[0], y - centre[1]) / reach, 0, 1)[..., None]
    rgb = np.array(inner) * (1 - d) + np.array(outer) * d
    return Image.fromarray(np.dstack([rgb, np.full((size, size, 1), 255)]).astype(np.uint8), 'RGBA')


def shadow(tile, blur, opacity, offset):
    a = tile.split()[3].filter(ImageFilter.GaussianBlur(blur))
    s = Image.new('RGBA', tile.size, (0, 0, 0, 0))
    s.putalpha(a.point(lambda v: int(v * opacity)))
    out = Image.new('RGBA', tile.size, (0, 0, 0, 0))
    out.alpha_composite(s, offset)
    return out


def compose(build, view, style, size=1024):
    """One icon. Styles: 'garage' (dark studio), 'accent' (orange), 'ring' (dark with an accent ring),
    'dusk' (the Strike Range's evening sky colours)."""
    radius = round(size * 0.22)
    drone = fit(cutout(build, view), size, 0.86 if view != 'front' else 0.9)
    if style == 'accent':
        bg = radial(size, (255, 170, 80), (226, 104, 20))
    elif style == 'dusk':
        bg = radial(size, (246, 196, 120), (58, 66, 92), centre=(0.5, 0.78), reach=0.95)
    else:
        bg = radial(size, (58, 64, 82), PANEL)
    if style == 'ring':
        d = ImageDraw.Draw(bg)
        r = size * 0.4
        d.ellipse([size / 2 - r, size / 2 - r, size / 2 + r, size / 2 + r], outline=ACCENT + (255,), width=round(size * 0.035))
    bg.alpha_composite(shadow(drone, size * 0.03, 0.55, (0, round(size * 0.035))))
    bg.alpha_composite(drone)
    mask = rounded(size, radius, (0, 0, 0)).split()[3]
    bg.putalpha(mask)
    return bg


def stadium(time, spot, size=1024, blur=0.006, dim=0.82, scale=1.0):
    """The 5" Race drone over the stadium (IconShots.RenderStadium): the stadium as the game draws it, a little
    soft and dimmed so the drone stands out, with the drone cut out from the same camera's black/white renders."""
    tag = f'stadium_{time}_{spot}'
    bg = Image.open(os.path.join(SRC, tag + '_bg.png')).convert('RGB')
    s = bg.height
    bg = bg.crop(((bg.width - s) // 2, 0, (bg.width + s) // 2, s)).resize((size, size), Image.LANCZOS)
    bg = bg.filter(ImageFilter.GaussianBlur(size * blur))
    bg = Image.fromarray((np.asarray(bg).astype(np.float64) * dim).astype(np.uint8)).convert('RGBA')
    k = np.asarray(Image.open(os.path.join(SRC, tag + '_k.png')).convert('RGB')).astype(np.float64) / 255
    w = np.asarray(Image.open(os.path.join(SRC, tag + '_w.png')).convert('RGB')).astype(np.float64) / 255
    alpha = np.clip(1.0 - (w - k).mean(axis=2), 0, 1)
    rgb = np.where(alpha[..., None] > 1e-3, k / np.maximum(alpha[..., None], 1e-3), 0)
    drone = Image.fromarray((np.dstack([np.clip(rgb, 0, 1), alpha]) * 255 + 0.5).astype(np.uint8), 'RGBA')
    hs = drone.height
    drone = drone.crop(((drone.width - hs) // 2, 0, (drone.width + hs) // 2, hs))
    box = drone.getbbox()
    drone = fit(drone.crop(box), size, 0.9 * scale)
    bg.alpha_composite(shadow(drone, size * 0.035, 0.45, (0, round(size * 0.03))))
    bg.alpha_composite(drone)
    bg.putalpha(rounded(size, round(size * 0.22), (0, 0, 0)).split()[3])
    return bg


def stadium_options():
    os.makedirs(OUT, exist_ok=True)
    cell, pad = 300, 24
    times, spots = ['night', 'dusk', 'day'], ['a', 'b', 'c']
    img = Image.new('RGB', (3 * (cell + pad) + pad, 3 * (cell + pad + 40) + pad), (236, 236, 238))
    for j, t in enumerate(times):
        for i, sp in enumerate(spots):
            x, y = pad + i * (cell + pad), pad + j * (cell + pad + 40)
            icon = stadium(t, sp, 512)
            big = icon.resize((cell, cell), Image.LANCZOS)
            img.paste(big, (x, y), big)
            for k, small in enumerate((48, 32, 16)):
                tile = icon.resize((small, small), Image.LANCZOS)
                img.paste(tile, (x + k * 60, y + cell + 6 + (48 - small) // 2), tile)
    img.save(os.path.join(OUT, 'stadium_options.png'))


def sheet():
    os.makedirs(OUT, exist_ok=True)
    cell = 360
    img = Image.new('RGBA', (cell * len(BUILDS), cell * len(VIEWS)), PANEL + (255,))
    for i, b in enumerate(BUILDS):
        for j, v in enumerate(VIEWS):
            img.alpha_composite(fit(cutout(b, v), cell, 0.9), (i * cell, j * cell))
    img.convert('RGB').save(os.path.join(OUT, 'cutouts.png'))


def options(picks):
    os.makedirs(OUT, exist_ok=True)
    cell, pad = 300, 24
    cols = 4
    rows = (len(picks) + cols - 1) // cols
    img = Image.new('RGB', (cols * (cell + pad) + pad, rows * (cell + pad + 40) + pad), (236, 236, 238))
    for n, (b, v, s) in enumerate(picks):
        x, y = pad + (n % cols) * (cell + pad), pad + (n // cols) * (cell + pad + 40)
        big = compose(b, v, s, 512).resize((cell, cell), Image.LANCZOS)
        img.paste(big, (x, y), big)
        # The same icon at taskbar sizes, to judge how it reads small.
        for k, small in enumerate((48, 32, 16)):
            t = compose(b, v, s, 512).resize((small, small), Image.LANCZOS)
            img.paste(t, (x + k * 60, y + cell + 6 + (48 - small) // 2), t)
    img.save(os.path.join(OUT, 'options.png'))


def final(build, view, style):
    icon = compose(build, view, style, 1024)
    icon.save(os.path.join(os.path.dirname(__file__), 'Ushqyr.png'))
    icon.save(os.path.join(os.path.dirname(__file__), 'Ushqyr.ico'), sizes=[(s, s) for s in (16, 24, 32, 48, 64, 128, 256)])


if __name__ == '__main__':
    cmd = sys.argv[1] if len(sys.argv) > 1 else 'sheet'
    if cmd == 'sheet':
        sheet()
    elif cmd == 'options':
        options([
            ('race5', 'hero', 'garage'), ('race5', 'top', 'garage'), ('race5', 'hero', 'accent'), ('race5', 'top', 'ring'),
            ('freestyle5', 'hero', 'garage'), ('cinewhoop35', 'hero', 'garage'), ('lr7', 'top', 'dusk'), ('race5', 'front', 'dusk'),
        ])
    elif cmd == 'stadium':
        stadium_options()
    elif cmd == 'final-stadium':
        # The chosen icon: python Tools/Icon/make_icon.py final-stadium night a
        icon = stadium(sys.argv[2], sys.argv[3], 1024)
        here = os.path.dirname(__file__)
        icon.save(os.path.join(here, 'Ushqyr.png'))
        icon.save(os.path.join(here, 'Ushqyr.ico'), sizes=[(s, s) for s in (16, 24, 32, 48, 64, 128, 256)])
    elif cmd == 'final':
        final(sys.argv[2], sys.argv[3], sys.argv[4])
