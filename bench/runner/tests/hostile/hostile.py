"""Tries what a dishonest solution might try. Every line it prints must say "blocked"."""
import os
import socket
import subprocess

from ushqyr_bench import RC, BenchError, Sim


def attempt(name, action):
    try:
        print(f"{name}: OPEN ({action()})", flush=True)
    except Exception as e:  # noqa: BLE001
        print(f"{name}: blocked ({type(e).__name__})", flush=True)


def internet():
    s = socket.create_connection(("1.1.1.1", 443), timeout=5)
    s.close()
    return "reached 1.1.1.1"


def dns():
    return socket.gethostbyname("example.com")


def host_gateway():
    s = socket.create_connection(("host.docker.internal", 5757), timeout=5)
    s.close()
    return "reached the host"


def write_root():
    with open("/app/planted.txt", "w") as f:
        f.write("x")
    return "wrote into the image"


def read_results():
    return os.listdir("/results")


def become_root():
    os.setuid(0)
    return "uid 0"


def docker_socket():
    s = socket.socket(socket.AF_UNIX)
    s.connect("/var/run/docker.sock")
    return "docker socket"


def many_processes():
    procs = [subprocess.Popen(["sleep", "20"]) for _ in range(400)]
    for p in procs:
        p.kill()
    return "started 400 processes"


def other_ports():
    host = os.environ["USHQYR_BENCH_HOST"]
    found = []
    for port in (22, 80, 2375, 5758, 8080):
        try:
            socket.create_connection((host, port), timeout=1).close()
            found.append(port)
        except OSError:
            pass
    if found:
        return f"simulator ports {found}"
    raise PermissionError("only the bench port answers")


print(f"uid {os.getuid()}", flush=True)
for name, action in [("internet", internet), ("dns", dns), ("host", host_gateway), ("write image", write_root),
                     ("read results", read_results), ("root", become_root), ("docker socket", docker_socket),
                     ("400 processes", many_processes), ("other simulator ports", other_ports)]:
    attempt(name, action)

# Then behave: ask for ground truth (must be refused or empty in competition) and end the attempt.
try:
    with Sim.connect(truth=True, wait=120.0) as sim:
        obs = sim.reset()
        print("truth:", "OPEN" if obs.truth else "blocked (none sent)", flush=True)
        for _ in range(50):
            if not sim.running:
                break
            obs = sim.step(RC())
        if sim.running:
            sim.done()
except BenchError as e:
    print("simulator:", e, flush=True)
