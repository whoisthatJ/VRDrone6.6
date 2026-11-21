"""Fetches the third-party parts of the Education Suite's programming module, at pinned versions.

    python -I Tools/Edu/fetch_third_party.py blockly     Blockly → Assets/StreamingAssets/Edu/Studio/blockly (kept in the repository)
    python -I Tools/Edu/fetch_third_party.py python      Python runtime with numpy, OpenCV and pymavlink → ThirdParty/edu/python (not in the repository)
    python -I Tools/Edu/fetch_third_party.py all

Every download is checked against the hash written in this file; a file that does not match is refused. To move to a
newer version: change the version, run with --print-hashes, read what changed, and write the new hashes here.
Nothing is downloaded when the program runs: this script is run by a developer, with internet, before a build.
"""
import argparse
import hashlib
import io
import json
import shutil
import subprocess
import sys
import tarfile
import urllib.request
import zipfile
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
CACHE = REPO / 'ThirdParty' / 'edu'

BLOCKLY = {
    'version': '13.5.0',
    'url': 'https://registry.npmjs.org/blockly/-/blockly-13.5.0.tgz',
    # What Studio needs: the editor, the standard blocks, the Python generator, three languages, and the licence.
    'files': ['blockly_compressed.js', 'blocks_compressed.js', 'python_compressed.js',
              'msg/en.js', 'msg/ru.js', 'msg/kk.js', 'LICENSE', 'package.json'],
    'folders': ['media'],
}

PYTHON = {
    'version': '3.11.9',
    'url': 'https://www.python.org/ftp/python/3.11.9/python-3.11.9-embed-amd64.zip',
    'packages': ['numpy==2.2.6', 'opencv-python-headless==4.11.0.86', 'pymavlink==2.4.50'],
}

HASHES = REPO / 'Tools' / 'Edu' / 'third_party_hashes.json'


def known():
    return json.loads(HASHES.read_text(encoding='utf-8')) if HASHES.exists() else {}


def download(url, name, algorithm, print_hashes):
    CACHE.mkdir(parents=True, exist_ok=True)
    target = CACHE / name
    if not target.exists():
        print('downloading', url)
        with urllib.request.urlopen(url, timeout=120) as r:
            data = r.read()
        target.write_bytes(data)
    data = target.read_bytes()
    digest = hashlib.new(algorithm, data).hexdigest()
    want = known().get(name)
    if print_hashes:
        print(f'  "{name}": "{algorithm}:{digest}"')
    elif want != f'{algorithm}:{digest}':
        target.unlink()
        sys.exit(f'{name}: hash {algorithm}:{digest} is not the one recorded in {HASHES.name} ({want}). The file was removed.')
    return data


def safe(member_name):
    """Archive members may not climb out of the folder they are unpacked into."""
    p = Path(member_name)
    return not p.is_absolute() and '..' not in p.parts


def blockly(print_hashes):
    data = download(BLOCKLY['url'], f"blockly-{BLOCKLY['version']}.tgz", 'sha512', print_hashes)
    out = REPO / 'Assets' / 'StreamingAssets' / 'Edu' / 'Studio' / 'blockly'
    if out.exists():
        shutil.rmtree(out)
    kept = 0
    with tarfile.open(fileobj=io.BytesIO(data), mode='r:gz') as tar:
        for m in tar.getmembers():
            if not m.isfile() or not m.name.startswith('package/') or not safe(m.name):
                continue
            rel = m.name[len('package/'):]
            if rel in BLOCKLY['files'] or any(rel.startswith(f + '/') for f in BLOCKLY['folders']):
                dest = out / rel
                dest.parent.mkdir(parents=True, exist_ok=True)
                dest.write_bytes(tar.extractfile(m).read())
                kept += 1
    missing = [f for f in BLOCKLY['files'] if not (out / f).exists()]
    print(f'blockly {BLOCKLY["version"]}: {kept} files in {out.relative_to(REPO)}' + (f'; NOT in the package: {missing}' if missing else ''))


def python_runtime(print_hashes):
    data = download(PYTHON['url'], f"python-{PYTHON['version']}-embed-amd64.zip", 'sha256', print_hashes)
    out = CACHE / 'python'
    if out.exists():
        shutil.rmtree(out)
    out.mkdir(parents=True)
    with zipfile.ZipFile(io.BytesIO(data)) as z:
        for info in z.infolist():
            if safe(info.filename):
                z.extract(info, out)
    # The embeddable Python looks for packages only where its ._pth file says; add our folder.
    pth = next(out.glob('python*._pth'))
    pth.write_text(pth.read_text() + 'Lib/site-packages\n')
    site = out / 'Lib' / 'site-packages'
    site.mkdir(parents=True)
    wheels = CACHE / 'wheels'
    wheels.mkdir(exist_ok=True)
    # Wheels only, for exactly this Python and platform; pip checks each file against PyPI's own hashes.
    subprocess.check_call([sys.executable, '-m', 'pip', 'download', '--quiet', '--only-binary', ':all:', '--python-version', '3.11',
                           '--platform', 'win_amd64', '--implementation', 'cp', '--dest', str(wheels)] + PYTHON['packages'])
    recorded = known()
    for wheel in sorted(wheels.glob('*.whl')):
        digest = 'sha256:' + hashlib.sha256(wheel.read_bytes()).hexdigest()
        if print_hashes:
            print(f'  "{wheel.name}": "{digest}"')
        elif recorded.get(wheel.name) != digest:
            sys.exit(f'{wheel.name}: hash is not the one recorded in {HASHES.name}')
        with zipfile.ZipFile(wheel) as z:
            for info in z.infolist():
                if safe(info.filename):
                    z.extract(info, site)
    size = sum(f.stat().st_size for f in out.rglob('*') if f.is_file()) / 1e6
    print(f'python {PYTHON["version"]} with {", ".join(PYTHON["packages"])}: {size:.0f} MB in {out.relative_to(REPO)}')


if __name__ == '__main__':
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('what', choices=['blockly', 'python', 'all'])
    ap.add_argument('--print-hashes', action='store_true', help='print the hashes of what was downloaded instead of checking them')
    a = ap.parse_args()
    if a.what in ('blockly', 'all'):
        blockly(a.print_hashes)
    if a.what in ('python', 'all'):
        python_runtime(a.print_hashes)
