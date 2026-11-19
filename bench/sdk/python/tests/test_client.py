import numpy as np
import pytest

from ushqyr_bench import RC, BenchError, Sim
from ushqyr_bench.fake import FakeSim


def test_rc_maps_sticks_to_microseconds():
    ch = RC(roll=1.0, pitch=-0.5, throttle=0.25, yaw=0.0, arm=True, mode="acro").channels()
    assert ch[:6] == [2000, 1250, 1250, 1500, 2000, 2000]
    assert len(ch) == 16


def test_rc_clamps_and_survives_nan():
    ch = RC(roll=7.0, pitch=float("nan"), throttle=-3.0, yaw=-9.0).channels()
    assert ch[:4] == [2000, 1500, 1000, 1000]
    assert RC().channels()[4] == 1000, "disarmed by default"


def test_raw_channels_are_padded_to_sixteen():
    assert RC.from_us([1100, 1200]).channels() == [1100, 1200] + [1500] * 14


def test_handshake_reset_step_and_time_limit():
    with FakeSim(time_limit=0.5) as fake, Sim.connect(port=fake.port) as sim:
        assert sim.mode == "training"
        assert sim.info["sensors"]["camera"]["width"] == 64
        obs = sim.reset()
        assert obs.running and obs.t == 0.0
        assert obs.camera.new and obs.camera.image.shape == (48, 64, 3)
        assert obs.tof.new and obs.tof.valid and obs.tof.range == pytest.approx(1.0)
        steps = 0
        while sim.running:
            obs = sim.step(RC(throttle=0.5, arm=True))
            steps += 1
            if obs.running:
                assert len(obs.imu) == 5
                assert obs.imu.accel[0, 2] == pytest.approx(9.81, abs=1e-3)
                assert obs.imu.gyro.shape == (5, 3)
        assert steps == 50
        assert obs.status == "time_limit"
        assert fake.received[0][:5] == [1500, 1500, 1500, 1500, 2000]


def test_camera_frame_is_kept_between_new_frames():
    with FakeSim() as fake, Sim.connect(port=fake.port) as sim:
        first = sim.reset()
        second = sim.step(RC())
        assert first.camera.new and not second.camera.new
        assert second.camera.image is not None
        assert np.array_equal(first.camera.image, second.camera.image)
        third = sim.step(RC())
        fourth = sim.step(RC())
        assert not third.camera.new and fourth.camera.new, "a frame every 30 ms"
        assert fourth.camera.seq == 1


def test_step_before_reset_is_refused_locally():
    with FakeSim() as fake, Sim.connect(port=fake.port) as sim:
        with pytest.raises(BenchError):
            sim.step(RC())


def test_done_ends_the_attempt():
    with FakeSim() as fake, Sim.connect(port=fake.port) as sim:
        sim.reset()
        obs = sim.done()
        assert not obs.running and obs.status == "finished"


def test_wrong_token_is_refused():
    with FakeSim(token="secret") as fake:
        with pytest.raises(BenchError) as e:
            Sim.connect(port=fake.port, token="nope")
        assert e.value.code == "token"
        with Sim.connect(port=fake.port, token="secret") as sim:
            assert sim.reset().running


def test_competition_mode_starts_once_and_has_no_ping():
    with FakeSim(mode="competition") as fake, Sim.connect(port=fake.port) as sim:
        assert sim.reset().running
        with pytest.raises(BenchError):
            sim.ping()


def test_ping_round_trip():
    with FakeSim() as fake, Sim.connect(port=fake.port) as sim:
        assert sim.ping(100_000) < 1.0


def test_lidar_scan_arrives_between_imu_and_camera():
    with FakeSim() as fake, Sim.connect(port=fake.port) as sim:
        obs = sim.reset()
        assert obs.lidar.ranges is None
        scans = 0
        for _ in range(31):
            obs = sim.step(RC())
            if obs.lidar.new:
                scans += 1
                assert obs.lidar.ranges.shape == (360,)
                assert float(obs.lidar.ranges[0]) == pytest.approx(5.0)
            if obs.camera.new:
                assert obs.camera.image.shape == (48, 64, 3) and int(obs.camera.image[5, 5, 1]) == 128
        assert scans == 3
        assert not obs.lidar.new and obs.lidar.ranges is not None, "the last scan is kept"


def test_step_without_a_packet():
    with FakeSim() as fake, Sim.connect(port=fake.port) as sim:
        sim.reset()
        sim.step(None)
        assert fake.received[-1] == []
