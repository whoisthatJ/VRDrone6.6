"""Zips the macOS build for handing over: python Tools/Mac/package_mac.py

Builds/macOS/Ushqyr.app -> Builds/macOS/Ushqyr_macOS_<version>.zip

A zip made by Windows tools loses the Unix "executable" bit, and a Mac then refuses to start the app. This one sets it
(rwxr-xr-x) on the program and its libraries inside the bundle, and keeps everything else rw-r--r--.
"""
import os
import stat
import zipfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
APP = os.path.join(ROOT, 'Builds', 'macOS', 'Ushqyr.app')
VERSION = open(os.path.join(ROOT, 'Version.txt')).read().strip()
OUT = os.path.join(ROOT, 'Builds', 'macOS', f'Ushqyr_macOS_{VERSION}.zip')
README = """Ushqyr {v} for macOS (Intel and Apple Silicon, macOS 12 or newer)

1. Unzip, and move Ushqyr.app to Applications.
2. The app is not signed with an Apple developer certificate, so on first launch macOS blocks it.
   Either: right-click Ushqyr.app > Open > Open,
   or, in Terminal:  xattr -cr /Applications/Ushqyr.app
   Then start it as usual.

Ushqyr {v} macOS үшін (Intel және Apple Silicon, macOS 12 немесе жаңарақ)
Бірінші іске қосқанда: Ushqyr.app үстінде тінтуірдің оң жақ батырмасы > Open > Open,
немесе Terminal ішінде:  xattr -cr /Applications/Ushqyr.app

Ushqyr {v} для macOS (Intel и Apple Silicon, macOS 12 и новее)
При первом запуске: правый клик по Ushqyr.app > Открыть > Открыть,
или в Терминале:  xattr -cr /Applications/Ushqyr.app
"""


def executable(rel):
    parts = rel.split('/')
    name = parts[-1]
    if 'MacOS' in parts:                       # Contents/MacOS/<program>
        return True
    if name.endswith(('.dylib', '.so')):
        return True
    # Mach-O inside plugin bundles: <x>.bundle/Contents/MacOS/<x> is covered above.
    return False


def main():
    if not os.path.isdir(APP):
        raise SystemExit('no build at ' + APP)
    count = 0
    with zipfile.ZipFile(OUT, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for base, dirs, files in os.walk(APP):
            dirs.sort()
            for d in dirs:
                rel = os.path.relpath(os.path.join(base, d), os.path.dirname(APP)).replace(os.sep, '/') + '/'
                info = zipfile.ZipInfo(rel)
                info.create_system = 3                                   # Unix, so the mode below is honoured
                info.external_attr = (stat.S_IFDIR | 0o755) << 16 | 0x10
                z.writestr(info, b'')
            for f in sorted(files):
                full = os.path.join(base, f)
                rel = os.path.relpath(full, os.path.dirname(APP)).replace(os.sep, '/')
                info = zipfile.ZipInfo.from_file(full, rel)
                info.create_system = 3
                info.external_attr = (stat.S_IFREG | (0o755 if executable(rel) else 0o644)) << 16
                info.compress_type = zipfile.ZIP_DEFLATED
                with open(full, 'rb') as src:
                    z.writestr(info, src.read())
                count += 1
        info = zipfile.ZipInfo('README.txt')
        info.create_system = 3
        info.external_attr = (stat.S_IFREG | 0o644) << 16
        z.writestr(info, README.format(v=VERSION).encode('utf-8'))
    print(f'{count} files -> {OUT} ({os.path.getsize(OUT) / 1048576:.0f} MB)')


if __name__ == '__main__':
    main()
