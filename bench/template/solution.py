"""Solution template: connect, arm, take off and hover. Replace `decide` with your own flying.

The simulator's address and token come from the environment (USHQYR_BENCH_HOST, USHQYR_BENCH_PORT,
USHQYR_BENCH_TOKEN); `Sim.connect()` reads them. The program must exit by itself when the attempt is over.
"""
from ushqyr_bench import RC, BenchError, Sim


def decide(obs, state):
    """One control step: sensors in, RC channels out (throttle 0..1, the sticks -1..1)."""
    if obs.t < 1.0:
        # Arming: the AUX1 switch off first, then on with the throttle down.
        return RC(throttle=0.0, arm=obs.t > 0.5)
    if obs.tof.valid:
        state["height"] = obs.tof.range
    height = state.get("height", 0.0)
    return RC(throttle=max(0.0, min(1.0, 0.3 + 0.25 * (1.0 - height))), arm=True)


def main() -> int:
    try:
        with Sim.connect(wait=120.0) as sim:
            print(f"connected: mode {sim.mode}, {sim.control_hz:.0f} Hz")
            obs = sim.reset()
            state = {}
            while sim.running:
                obs = sim.step(decide(obs, state))
            print(f"attempt over: {obs.status}, simulator time {obs.t:.2f} s")
    except BenchError as e:
        print("stopped:", e)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
