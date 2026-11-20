"""Puts a Team Kit together: the simulator's Team Kit build, the bench/ folder and the documentation.

    <kit>/Ushqyr.exe (or Ushqyr.x86_64) and its data      the AI Bench player
    <kit>/bench/                                          SDK, examples, solution template, tools, Docker files, ROS 2
    <kit>/Docs/bench/                                     documentation (ru, kk, en)
    <kit>/Builds/BenchServer/                             optional: the Linux server build, for `submit-check`
    <kit>/README.txt

The paths inside the kit are the repository's own, so every command in the documentation works unchanged.
"""
from __future__ import annotations

import shutil
import zipfile
from pathlib import Path
from typing import Callable, List, Optional

BENCH_PARTS = ["sdk", "examples", "template", "runner", "docker", "ros2", "schemas", ".gitattributes"]
SKIP_NAMES = {"__pycache__", ".pytest_cache", ".ruff_cache", ".venv", "build", "dist", ".git"}
SKIP_SUFFIXES = (".pyc", ".meta", ".egg-info")
# Unity writes these beside a player; they are for the developer, not for a team.
SKIP_BUILD = ("_BurstDebugInformation_DoNotShip", "_BackUpThisFolder_ButDontShipItWithYourGame")

README = """Ushqyr AI Bench: Team Kit {version} ({platform})

RU  Начните с Docs/bench/ru/quickstart.md (30 минут до первого полета).
KK  Docs/bench/kk/quickstart.md бетінен бастаңыз.
EN  Start with Docs/bench/en/quickstart.md (30 minutes to the first flight).

{player}        the simulator (AI Bench only)
bench/          Python SDK, examples, solution template, tools, Docker files, ROS 2 bridge
Docs/bench/     documentation: ru (the reference text), kk, en
{server}"""


class KitError(Exception):
    pass


def _skip(path: Path) -> bool:
    return path.name in SKIP_NAMES or path.name.endswith(SKIP_SUFFIXES)


def _copy(src: Path, dst: Path) -> int:
    """Copies a file or a tree, leaving caches and Unity's .meta files behind. Returns the number of files."""
    if src.is_file():
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, dst)
        return 1
    count = 0
    for item in sorted(src.rglob("*")):
        rel = item.relative_to(src)
        if item.is_dir() or any(_skip(Path(part)) for part in rel.parts):
            continue
        (dst / rel).parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(item, dst / rel)
        count += 1
    return count


def find_player(build: Path) -> Path:
    players = [p for p in (build / "Ushqyr.exe", build / "Ushqyr.x86_64") if p.is_file()]
    if len(players) != 1:
        raise KitError(f"{build} must hold exactly one of Ushqyr.exe, Ushqyr.x86_64 (Unity menu: Ushqyr/Build/Build Team Kit)")
    return players[0]


def make(build: Path, repo: Path, out: Path, version: str = "", server: Optional[Path] = None,
         archive: bool = True, say: Callable[[str], None] = print) -> Path:
    """Assembles the kit in `out` (which must not exist or be empty) and, with `archive`, zips it beside it."""
    player = find_player(build)
    bench, docs = repo / "bench", repo / "Docs" / "bench"
    if not (bench / "sdk").is_dir() or not docs.is_dir():
        raise KitError(f"{repo} is not the repository root (no bench/sdk or Docs/bench)")
    if server is not None and not (server / "UshqyrBench.x86_64").is_file():
        raise KitError(f"no Bench Server build in {server}")
    if out.exists() and any(out.iterdir()):
        raise KitError(f"{out} is not empty")
    out.mkdir(parents=True, exist_ok=True)

    n = 0
    for item in sorted(build.iterdir()):
        if item.name.endswith(SKIP_BUILD):
            continue
        n += _copy(item, out / item.name)
    say(f"simulator: {n} file(s) from {build}")

    n = sum(_copy(bench / part, out / "bench" / part) for part in BENCH_PARTS if (bench / part).exists())
    # The hostile solution is the organisers' containment test, not something a team needs.
    shutil.rmtree(out / "bench" / "runner" / "tests" / "hostile", ignore_errors=True)
    say(f"bench: {n} file(s)")

    n = sum(_copy(item, out / "Docs" / "bench" / item.name) for item in sorted(docs.iterdir())
            if item.suffix == ".md" or (item.is_dir() and item.name in ("ru", "kk", "en")))
    say(f"documentation: {n} file(s)")

    if server is not None:
        n = sum(_copy(item, out / "Builds" / "BenchServer" / item.name) for item in sorted(server.iterdir())
                if not item.name.endswith(SKIP_BUILD))
        say(f"Bench Server build: {n} file(s)")

    platform = "Windows" if player.suffix == ".exe" else "Linux"
    (out / "README.txt").write_text(README.format(
        version=version, platform=platform, player=player.name,
        server="Builds/BenchServer/   the Linux server build, used by `ushqyr-bench build-images`\n" if server else ""),
        encoding="utf-8", newline="\n")

    return make_archive(out, say) if archive else out


def make_archive(out: Path, say: Callable[[str], None] = print) -> Path:
    target = out.with_name(out.name + ".zip")
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for item in sorted(out.rglob("*")):
            if item.is_file():
                info = zipfile.ZipInfo.from_file(item, Path(out.name) / item.relative_to(out))
                info.compress_type = zipfile.ZIP_DEFLATED
                # Keep the Linux player runnable after unzipping on Linux.
                if item.suffix in (".x86_64", ".sh") or item.name == "UnityPlayer.so":
                    info.external_attr = (0o755 | 0o100000) << 16
                with open(item, "rb") as f:
                    z.writestr(info, f.read(), compresslevel=6)
    say(f"archive: {target} ({target.stat().st_size / 1024**2:.0f} MB)")
    return target


def files(out: Path) -> List[str]:
    return sorted(str(p.relative_to(out)).replace("\\", "/") for p in out.rglob("*") if p.is_file())
