"""Takes the internet packages out of the Unity project for the school build, and puts them back.

    python -I Tools/Edu/school_manifest.py strip      before building the school version
    python -I Tools/Edu/school_manifest.py restore    afterwards
    python -I Tools/Edu/school_manifest.py status

The school version must not be able to reach the internet (EDU_PLAN F5). Its code is the same; what differs is that
the Unity services packages (sign-in, internet races, online leaderboards) and the engine's analytics module are not
in the project while it is built, so they are not in the program at all. LAN races need none of them.

"strip" keeps a copy of Packages/manifest.json and packages-lock.json beside them (*.full) and "restore" copies them
back. Unity notices the change by itself: wait until it has finished re-importing before building.
"""
import json
import shutil
import sys
from pathlib import Path

PACKAGES = Path(__file__).resolve().parents[2] / 'Packages'
MANIFEST, LOCK = PACKAGES / 'manifest.json', PACKAGES / 'packages-lock.json'
SAVED_MANIFEST, SAVED_LOCK = PACKAGES / 'manifest.json.full', PACKAGES / 'packages-lock.json.full'

# Everything that exists to talk to a server on the internet.
REMOVE = [
    'com.unity.services.multiplayer',   # internet races; brings sign-in, relay and the services core with it
    'com.unity.services.leaderboards',  # online lap boards
    'com.unity.modules.unityanalytics',  # the engine's analytics module
]


def stripped():
    return SAVED_MANIFEST.exists()


def main():
    what = sys.argv[1] if len(sys.argv) > 1 else 'status'
    if what == 'status':
        names = json.loads(MANIFEST.read_text(encoding='utf-8'))['dependencies']
        present = [p for p in REMOVE if p in names]
        print('school manifest (stripped)' if stripped() else 'full manifest', '- internet packages present:', present or 'none')
    elif what == 'strip':
        if stripped():
            sys.exit('already stripped (manifest.json.full exists); run "restore" first')
        shutil.copy2(MANIFEST, SAVED_MANIFEST)
        if LOCK.exists():
            shutil.copy2(LOCK, SAVED_LOCK)
        data = json.loads(MANIFEST.read_text(encoding='utf-8'))
        gone = [p for p in REMOVE if data['dependencies'].pop(p, None) is not None]
        MANIFEST.write_text(json.dumps(data, indent=2) + '\n', encoding='utf-8', newline='\n')
        print('removed:', ', '.join(gone))
    elif what == 'restore':
        if not stripped():
            sys.exit('nothing to restore')
        shutil.move(SAVED_MANIFEST, MANIFEST)
        if SAVED_LOCK.exists():
            shutil.move(SAVED_LOCK, LOCK)
        print('the full manifest is back')
    else:
        sys.exit(__doc__)


if __name__ == '__main__':
    main()
