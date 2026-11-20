# ushqyr-bench

Python SDK for the Ushqyr AI Bench simulator. Your program receives what the drone's sensors give (FPV camera, IMU,
ToF, battery voltage) and answers with RC channels, as if it held a real transmitter.

```
pip install .            # from this folder; needs Python 3.9+, numpy and msgpack
```

```python
from ushqyr_bench import Sim, RC

with Sim.connect() as sim:                 # address and token come from environment variables
    obs = sim.reset()                      # training mode: back to the start pad; competition: the attempt starts
    for _ in range(15):                    # arming: the switch off first...
        obs = sim.step(RC(arm=False))
    for _ in range(15):                    # ...then on, with the throttle down
        obs = sim.step(RC(arm=True))
    while sim.running:
        frame = obs.camera.image           # numpy array, H x W x 3, RGB
        rc = RC(throttle=0.56, arm=True)   # your controller goes here
        obs = sim.step(rc)                 # the simulator advances one control period
```

The flight controller arms only when the arm switch goes from off to on while the throttle is down, like a real one.

Environment variables: `USHQYR_BENCH_HOST` (default `127.0.0.1`), `USHQYR_BENCH_PORT` (`5757`),
`USHQYR_BENCH_TOKEN` (empty).

A working example is in `bench/examples/color_pid`. The full reference is in `Docs/bench/<ru|kk|en>/sdk.md`, the wire protocol in `protocol.md` beside it, and the quickstart in `quickstart.md`.
`ushqyr_bench.fake.FakeSim` is a stand-in simulator for unit tests of your own code.

## Licences of the dependencies

| Package | Licence | Used by |
|---|---|---|
| numpy | BSD-3-Clause | SDK |
| msgpack | Apache-2.0 | SDK |
| opencv-python | Apache-2.0 | the `color_pid` example |
| pytest | MIT | tests |
| ruff | MIT | lint |
