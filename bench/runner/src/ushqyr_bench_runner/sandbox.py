"""Runs a team's solution the way the qualification server does: the simulator in one container, the solution in
another, on a network with no way out.

The solution's container is treated as hostile:
  * its only network is an internal Docker network on which nothing but the simulator listens;
  * no Linux capabilities, no new privileges, not root, a read-only file system (a small /tmp in memory);
  * limits on memory, processors and the number of processes;
  * nothing of the host is mounted into it. The attempt's files are written by the simulator's container.
"""
from __future__ import annotations

import json
import secrets
import shutil
import subprocess
import time
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, List, Optional, Tuple

SERVER_IMAGE = "ushqyr-bench-server"
BASE_IMAGE = "ushqyr-bench-base:cpu"
SIM_ALIAS = "simulator"


class SandboxError(Exception):
    """Something outside the solution went wrong: Docker missing, an image not built, the simulator not starting."""


@dataclass
class Limits:
    """What a solution's container may use. The defaults are the plan's; the organisers set the real ones."""

    memory: str = "4g"
    cpus: float = 4.0
    pids: int = 256
    tmp: str = "256m"
    shm: str = "256m"
    image_bytes: int = 8 * 1024**3
    build_seconds: float = 1800.0
    wall_seconds: float = 1800.0
    """Real time allowed for one attempt, on top of the simulator's own limits (which count simulator time)."""
    gpu: bool = False


@dataclass
class Outcome:
    folder: Optional[Path]
    result: Optional[Dict]
    solution_exit: Optional[int]
    solution_log: str
    simulator_log: str
    image: str = ""
    image_bytes: int = 0
    notes: List[str] = field(default_factory=list)


def docker(*args: str, timeout: Optional[float] = None, check: bool = True) -> subprocess.CompletedProcess:
    exe = shutil.which("docker")
    if exe is None:
        raise SandboxError("Docker is not installed (the `docker` command was not found)")
    try:
        p = subprocess.run([exe, *args], capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=timeout)
    except subprocess.TimeoutExpired as e:
        raise SandboxError(f"`docker {args[0]}` did not finish in {timeout:.0f} s") from e
    if check and p.returncode != 0:
        raise SandboxError(f"`docker {' '.join(args[:3])}` failed: {(p.stderr or p.stdout).strip()[-800:]}")
    return p


def require_engine() -> None:
    p = docker("info", "--format", "{{.ServerVersion}}", check=False, timeout=60)
    if p.returncode != 0:
        raise SandboxError("the Docker engine is not running (start Docker Desktop)")


def wsl_gpu() -> bool:
    """True on Docker Desktop for Windows (WSL2), where the simulator can draw on the PC's video card."""
    p = docker("info", "--format", "{{.OperatingSystem}}|{{.KernelVersion}}", check=False, timeout=60)
    return p.returncode == 0 and "Docker Desktop" in p.stdout and "WSL" in p.stdout


def simulator_gpu_args() -> List[str]:
    """`docker run` options that let the simulator's container use the video card through WSL."""
    return ["--gpus", "all", "-v", "/usr/lib/wsl:/usr/lib/wsl:ro"]


def image_exists(tag: str) -> bool:
    return docker("image", "inspect", tag, check=False, timeout=60).returncode == 0


def image_size(tag: str) -> int:
    return int(docker("image", "inspect", "--format", "{{.Size}}", tag, timeout=60).stdout.strip())


def image_digest(tag: str) -> str:
    return docker("image", "inspect", "--format", "{{.Id}}", tag, timeout=60).stdout.strip()


def build_base(bench_root: Path) -> None:
    docker("build", "-t", BASE_IMAGE, "-f", str(bench_root / "docker" / "base" / "Dockerfile"), str(bench_root), timeout=3600)


def build_ros2(bench_root: Path, distro: str) -> str:
    tag = f"ushqyr-bench-ros2:{distro}"
    docker("build", "-t", tag, "--build-arg", f"ROS_DISTRO={distro}", "-f", str(bench_root / "docker" / "ros2" / "Dockerfile"),
           str(bench_root), timeout=3600)
    return tag


def build_server(bench_root: Path, build_folder: Path) -> None:
    if not (build_folder / "UshqyrBench.x86_64").is_file():
        raise SandboxError(f"no Bench Server build in {build_folder} (Unity menu: Ushqyr/Build/Build Bench Server (Linux))")
    context = bench_root / "docker" / "server"
    docker("build", "-t", SERVER_IMAGE, "-f", str(context / "Dockerfile"), "--build-context", f"build={build_folder}", str(context),
           timeout=3600)


def build_solution(folder: Path, limits: Limits, build_args: Optional[List[str]] = None) -> Tuple[str, int]:
    """Builds the solution's image from its Dockerfile. Returns (tag, size in bytes)."""
    if not (folder / "Dockerfile").is_file():
        raise SandboxError(f"{folder} has no Dockerfile (start from bench/template)")
    for line in (folder / "Dockerfile").read_text(encoding="utf-8", errors="replace").splitlines():
        words = line.split()
        ours = len(words) >= 2 and words[0].upper() == "FROM" and words[1].startswith("ushqyr-bench-") and "$" not in words[1]
        if ours and not image_exists(words[1]):
            hint = "build-images --ros2 " + words[1].split(":")[-1] if "ros2" in words[1] else "build-images"
            raise SandboxError(f"the base image {words[1]} is not built yet: run `ushqyr-bench {hint}`")
    tag = "ushqyr-solution-" + secrets.token_hex(4)
    extra = [x for arg in build_args or [] for x in ("--build-arg", arg)]
    docker("build", "-t", tag, *extra, str(folder), timeout=limits.build_seconds)
    return tag, image_size(tag)


def load_solution(archive: Path) -> str:
    """Loads a `docker save` file. Returns the image's name."""
    out = docker("load", "-i", str(archive), timeout=1800).stdout
    for line in out.splitlines():
        if line.startswith("Loaded image"):
            return line.split(":", 1)[1].strip()
    raise SandboxError(f"{archive} holds no image")


def solution_args(limits: Limits, network: str, token: str, port: int) -> List[str]:
    """The `docker run` options that make the solution's container harmless. Kept in one place so tests can read it."""
    args = [
        "--network", network,
        "--read-only", "--tmpfs", f"/tmp:rw,noexec,nosuid,size={limits.tmp}",
        "--cap-drop", "ALL", "--security-opt", "no-new-privileges",
        "--user", "10001:10001",
        "--pids-limit", str(limits.pids), "--memory", limits.memory, "--memory-swap", limits.memory, "--cpus", str(limits.cpus),
        # Its own shared memory (ROS 2's middleware uses it between nodes), shared with nobody.
        "--ipc", "private", "--shm-size", limits.shm,
        "-e", f"USHQYR_BENCH_HOST={SIM_ALIAS}", "-e", f"USHQYR_BENCH_PORT={port}", "-e", f"USHQYR_BENCH_TOKEN={token}",
        "-e", "HOME=/tmp",
    ]
    if limits.gpu:
        args += ["--gpus", "all"]
    return args


def run_attempt(image: str, out: Path, track: str, seed: int = 1, team: str = "check", attempt: int = 1,
                limits: Optional[Limits] = None, rules: Optional[Path] = None, key: Optional[Path] = None,
                tracks: Optional[Path] = None, port: int = 5757, sim_args: Optional[List[str]] = None,
                graphics: Optional[str] = None, video: Optional[bool] = None, on_progress=None,
                sim_gpu: bool = False) -> Outcome:
    """One competition attempt of `image` on `track`. The attempt's folder appears under `out`."""
    limits = limits or Limits()
    require_engine()
    if not image_exists(SERVER_IMAGE):
        raise SandboxError(f"the simulator image {SERVER_IMAGE} is not built yet: run `ushqyr-bench build-images`")
    out = out.resolve()
    out.mkdir(parents=True, exist_ok=True)
    before = {p.name for p in out.iterdir()}
    run_id = secrets.token_hex(4)
    network, sim, sol = f"ushqyr-net-{run_id}", f"ushqyr-sim-{run_id}", f"ushqyr-sol-{run_id}"
    token = secrets.token_hex(16)
    outcome = Outcome(None, None, None, "", "", image=image)
    mounts = ["-v", f"{out}:/results"]
    options = ["-bench-mode", "competition", "-bench-track", track, "-bench-seed", str(seed), "-bench-token", token,
               "-bench-port", str(port), "-bench-team", team, "-bench-attempt", str(attempt), "-bench-solution", image_digest(image)]
    if sim_gpu:
        mounts += simulator_gpu_args()
    if graphics:
        options += ["-bench-graphics", graphics]
    if video is not None:
        options += ["-bench-video", "on" if video else "off"]
    if rules is not None:
        mounts += ["-v", f"{rules.resolve()}:/config/rules.json:ro"]
        options += ["-bench-rules", "/config/rules.json"]
    if key is not None:
        mounts += ["-v", f"{key.resolve()}:/config/bench.key:ro"]
        options += ["-bench-key", "/config/bench.key"]
    if tracks is not None:
        # Hidden tracks: a folder of the server, mounted into the simulator only.
        mounts += ["-v", f"{tracks.resolve()}:/tracks:ro"]
        options[options.index("-bench-track") + 1] = f"/tracks/{track}.benchtrack.json"
    try:
        docker("network", "create", "--internal", network, timeout=60)
        docker("run", "-d", "--name", sim, "--network", network, "--network-alias", SIM_ALIAS, "--user", "0:0",
               "--cap-drop", "ALL", "--security-opt", "no-new-privileges", *mounts, SERVER_IMAGE, *options, *(sim_args or []),
               timeout=120)
        docker("run", "-d", "--name", sol, *solution_args(limits, network, token, port), image, timeout=120)
        deadline = time.monotonic() + limits.wall_seconds
        while time.monotonic() < deadline:
            state = docker("inspect", "--format", "{{.State.Running}}", sim, timeout=60).stdout.strip()
            if state != "true":
                break
            if on_progress is not None:
                on_progress(deadline - time.monotonic())
            time.sleep(2.0)
        else:
            outcome.notes.append(f"the attempt was stopped after {limits.wall_seconds:.0f} s of real time")
            # Stop the solution; the simulator sees the lost connection, ends the attempt and writes its files.
            docker("kill", sol, check=False, timeout=60)
            docker("wait", sim, check=False, timeout=180)
        # The simulator ends the attempt; the solution gets a moment to notice and exit by itself.
        try:
            p = docker("wait", sol, timeout=30, check=False)
            outcome.solution_exit = int(p.stdout.strip()) if p.stdout.strip().lstrip("-").isdigit() else None
        except SandboxError:
            outcome.notes.append("the solution did not exit when the attempt ended (it was stopped)")
    finally:
        for name in (sol, sim):
            log = docker("logs", "--tail", "400", name, check=False, timeout=60)
            text = (log.stdout or "") + (log.stderr or "")
            if name == sol:
                outcome.solution_log = text
            else:
                outcome.simulator_log = text
            docker("rm", "-f", name, check=False, timeout=60)
        docker("network", "rm", network, check=False, timeout=60)
    new = sorted((p for p in out.iterdir() if p.is_dir() and p.name not in before), key=lambda p: p.stat().st_mtime)
    for folder in reversed(new):
        if (folder / "result.json").is_file():
            outcome.folder = folder
            outcome.result = json.loads((folder / "result.json").read_text(encoding="utf-8-sig"))
            break
    return outcome
