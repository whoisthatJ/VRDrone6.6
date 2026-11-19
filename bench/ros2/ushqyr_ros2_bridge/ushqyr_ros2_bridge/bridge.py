"""ROS 2 bridge of the Ushqyr AI Bench simulator. It is an ordinary client of the simulator, built on the Python SDK:
the sensors go out as ROS 2 topics, and RC channels come in.

Topics published (frame ids in brackets):
    /clock                       rosgraph_msgs/Clock        simulator time
    ushqyr/camera/image_raw      sensor_msgs/Image          rgb8, 30 Hz                          [camera]
    ushqyr/camera/camera_info    sensor_msgs/CameraInfo     with every image                     [camera]
    ushqyr/imu/data_raw          sensor_msgs/Imu            500 Hz, no orientation               [base_link]
    ushqyr/tof/range             sensor_msgs/Range          downward distance                    [tof]
    ushqyr/scan                  sensor_msgs/LaserScan      2D LiDAR, when the drone has one     [lidar]
    ushqyr/battery               sensor_msgs/BatteryState
    ushqyr/status                ushqyr_msgs/AttemptStatus  once per control step, after its sensor messages
    ushqyr/info                  std_msgs/String            the simulator's handshake as JSON (latched)

Topics subscribed:
    ushqyr/rc                    ushqyr_msgs/RcChannels     16 channels in microseconds
    ushqyr/joy                   sensor_msgs/Joy            axes: roll, pitch, throttle (0..1), yaw; buttons: arm, mode

Parameters:
    host, port, token   where the simulator is (default: USHQYR_BENCH_HOST / _PORT / _TOKEN)
    connect_wait        seconds to keep trying to connect (60)
    wait_for_rc         start the attempt only when the first RC message arrives (true)
    sync_wait           sync mode: seconds of real time to wait for a new RC message in every step (0.2).
                        When none comes in time the step goes without a packet, as a lost one would.
    seed                training mode: seed of the sensor noise (not set)

Body frame: x forward, y left, z up (REP 103). The node exits when the attempt is over.
"""
from __future__ import annotations

import json
import math
import time
from typing import List, Optional

import rclpy
from builtin_interfaces.msg import Time
from rclpy.node import Node
from rclpy.qos import DurabilityPolicy, QoSProfile, ReliabilityPolicy
from rosgraph_msgs.msg import Clock
from sensor_msgs.msg import BatteryState, CameraInfo, Image, Imu, Joy, LaserScan, Range
from std_msgs.msg import String
from ushqyr_bench import RC, BenchError, Observation, Sim
from ushqyr_msgs.msg import AttemptStatus, RcChannels


def stamp(t: float) -> Time:
    t = max(0.0, float(t))
    sec = int(t)
    return Time(sec=sec, nanosec=min(999_999_999, int(round((t - sec) * 1e9))))


def channels_from_rc(values) -> List[int]:
    """RcChannels -> the SDK's list. 0 means "not given": centred sticks, low throttle and switches."""
    defaults = [1500, 1500, 1000, 1500, 1000, 1000] + [1500] * 10
    return [int(v) if v else d for v, d in zip(list(values) + [0] * 16, defaults)]


def channels_from_joy(axes, buttons) -> List[int]:
    """Joy -> channels. axes: roll, pitch, yaw are -1..1, throttle is 0..1. buttons[0] = arm; buttons[1] and [2]
    pick the flight mode (none: Angle, 1: Horizon, 2: Acro)."""
    a = list(axes) + [0.0] * 4
    b = list(buttons) + [0] * 3
    mode = "acro" if b[2] else "horizon" if b[1] else "angle"
    return RC(roll=a[0], pitch=a[1], throttle=a[2], yaw=a[3], arm=bool(b[0]), mode=mode).channels()


class Bridge(Node):
    def __init__(self) -> None:
        super().__init__("ushqyr_bridge")
        self.declare_parameter("host", "")
        self.declare_parameter("port", 0)
        self.declare_parameter("token", "")
        self.declare_parameter("connect_wait", 60.0)
        self.declare_parameter("wait_for_rc", True)
        self.declare_parameter("sync_wait", 0.2)
        self.declare_parameter("seed", -1)

        sensor = QoSProfile(depth=50, reliability=ReliabilityPolicy.RELIABLE)
        fast = QoSProfile(depth=500, reliability=ReliabilityPolicy.RELIABLE)
        latched = QoSProfile(depth=1, reliability=ReliabilityPolicy.RELIABLE, durability=DurabilityPolicy.TRANSIENT_LOCAL)
        self.pub_clock = self.create_publisher(Clock, "/clock", 10)
        self.pub_image = self.create_publisher(Image, "ushqyr/camera/image_raw", sensor)
        self.pub_camera_info = self.create_publisher(CameraInfo, "ushqyr/camera/camera_info", sensor)
        self.pub_imu = self.create_publisher(Imu, "ushqyr/imu/data_raw", fast)
        self.pub_tof = self.create_publisher(Range, "ushqyr/tof/range", sensor)
        self.pub_scan = self.create_publisher(LaserScan, "ushqyr/scan", sensor)
        self.pub_battery = self.create_publisher(BatteryState, "ushqyr/battery", sensor)
        self.pub_status = self.create_publisher(AttemptStatus, "ushqyr/status", sensor)
        self.pub_info = self.create_publisher(String, "ushqyr/info", latched)
        self.create_subscription(RcChannels, "ushqyr/rc", self._on_rc, 10)
        self.create_subscription(Joy, "ushqyr/joy", self._on_joy, 10)

        self.rc: Optional[List[int]] = None
        self.rc_count = 0
        self.sim: Optional[Sim] = None
        self.camera_info = CameraInfo()
        self.sensors: dict = {}

    # ---------- RC in ----------

    def _on_rc(self, msg: RcChannels) -> None:
        self.rc = channels_from_rc(msg.channels)
        self.rc_count += 1

    def _on_joy(self, msg: Joy) -> None:
        self.rc = channels_from_joy(msg.axes, msg.buttons)
        self.rc_count += 1

    # ---------- sensors out ----------

    def _prepare(self, info: dict) -> None:
        self.sensors = info.get("sensors", {})
        cam = self.sensors.get("camera", {})
        ci = CameraInfo()
        ci.header.frame_id = "camera"
        ci.width, ci.height = int(cam.get("width", 0)), int(cam.get("height", 0))
        fx, fy, cx, cy = (float(cam.get(k, 0.0)) for k in ("fx", "fy", "cx", "cy"))
        ci.k = [fx, 0.0, cx, 0.0, fy, cy, 0.0, 0.0, 1.0]
        ci.r = [1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0]
        ci.p = [fx, 0.0, cx, 0.0, 0.0, fy, cy, 0.0, 0.0, 0.0, 1.0, 0.0]
        # Not plumb_bob: the simulator's model goes from the distorted picture to the straight one,
        # x_u = x_d * (1 + k1 * r_d^2) in normalised coordinates. d holds that k1.
        ci.distortion_model = "ushqyr_radial_k1"
        ci.d = [float(cam.get("distortion", {}).get("k1", 0.0))]
        self.camera_info = ci
        self.pub_info.publish(String(data=json.dumps(info)))

    def _publish(self, obs: Observation) -> None:
        now = stamp(obs.t)
        if obs.camera.new and obs.camera.image is not None:
            img = Image()
            img.header.stamp, img.header.frame_id = stamp(obs.camera.t), "camera"
            img.height, img.width = int(obs.camera.image.shape[0]), int(obs.camera.image.shape[1])
            img.encoding, img.is_bigendian, img.step = "rgb8", 0, img.width * 3
            img.data = obs.camera.image.tobytes()
            self.camera_info.header.stamp = img.header.stamp
            self.pub_image.publish(img)
            self.pub_camera_info.publish(self.camera_info)
        for i in range(len(obs.imu)):
            m = Imu()
            m.header.stamp, m.header.frame_id = stamp(float(obs.imu.t[i])), "base_link"
            m.orientation_covariance[0] = -1.0  # no orientation estimate
            g, a = obs.imu.gyro[i], obs.imu.accel[i]
            m.angular_velocity.x, m.angular_velocity.y, m.angular_velocity.z = float(g[0]), float(g[1]), float(g[2])
            m.linear_acceleration.x, m.linear_acceleration.y, m.linear_acceleration.z = float(a[0]), float(a[1]), float(a[2])
            self.pub_imu.publish(m)
        if obs.tof.new:
            tof = self.sensors.get("tof", {})
            r = Range()
            r.header.stamp, r.header.frame_id = stamp(obs.tof.t), "tof"
            r.radiation_type = Range.INFRARED
            r.field_of_view = 0.44
            r.min_range, r.max_range = float(tof.get("min_m", 0.0)), float(tof.get("max_m", 0.0))
            r.range = float(obs.tof.range) if obs.tof.valid else math.inf
            self.pub_tof.publish(r)
        if obs.lidar.new and obs.lidar.ranges is not None:
            lidar = self.sensors.get("lidar", {})
            n = len(obs.lidar.ranges)
            s = LaserScan()
            s.header.stamp, s.header.frame_id = stamp(obs.lidar.t), "lidar"
            s.angle_min = 0.0
            s.angle_increment = math.radians(float(lidar.get("angle_step_deg", 360.0 / max(n, 1))))
            s.angle_max = s.angle_increment * (n - 1)
            rate = float(lidar.get("rate_hz", 10.0))
            s.scan_time = 1.0 / rate
            s.time_increment = s.scan_time / max(n, 1)
            s.range_min, s.range_max = float(lidar.get("min_m", 0.0)), float(lidar.get("max_m", 0.0))
            s.ranges = [float(v) if v > 0.0 else math.inf for v in obs.lidar.ranges]
            self.pub_scan.publish(s)
        if obs.battery.new:
            b = BatteryState()
            b.header.stamp = stamp(obs.battery.t)
            b.voltage = float(obs.battery.voltage)
            b.present = True
            b.percentage = b.current = b.charge = b.capacity = b.design_capacity = math.nan
            self.pub_battery.publish(b)
        self.pub_clock.publish(Clock(clock=now))
        self.pub_status.publish(AttemptStatus(stamp=now, tick=int(obs.tick), running=bool(obs.running), status=str(obs.status)))

    # ---------- the attempt ----------

    def _wait_rc(self, seconds: float) -> Optional[List[int]]:
        """Handles ROS messages until a new RC message has arrived or `seconds` of real time have passed."""
        seen = self.rc_count
        deadline = time.monotonic() + seconds
        while rclpy.ok() and self.rc_count == seen:
            left = deadline - time.monotonic()
            if left <= 0.0:
                return None
            rclpy.spin_once(self, timeout_sec=min(left, 0.05))
        return self.rc

    def run(self) -> int:
        p = {name: self.get_parameter(name).value for name in ("host", "port", "token", "connect_wait", "wait_for_rc", "sync_wait", "seed")}
        try:
            self.sim = Sim.connect(p["host"] or None, p["port"] or None, p["token"] or None, wait=float(p["connect_wait"]))
        except (OSError, BenchError) as e:
            self.get_logger().error(f"can't connect to the simulator: {e}")
            return 1
        sim = self.sim
        self._prepare(sim.info)
        self.get_logger().info(f"connected: mode {sim.mode}, {sim.control_hz:.0f} Hz, time {'realtime' if sim.realtime else 'sync'}")
        if p["wait_for_rc"]:
            self.get_logger().info("waiting for the first message on ushqyr/rc or ushqyr/joy to start the attempt")
            while rclpy.ok() and self.rc_count == 0:
                rclpy.spin_once(self, timeout_sec=0.1)
        try:
            obs = sim.reset(None if int(p["seed"]) < 0 else int(p["seed"]))
            self._publish(obs)
            if sim.realtime:
                sent = self.rc_count
                while rclpy.ok() and obs.running:
                    rclpy.spin_once(self, timeout_sec=0.002)
                    if self.rc_count != sent and self.rc is not None:
                        sent = self.rc_count
                        sim.send(RC.from_us(self.rc))
                    newer = sim.latest()
                    if newer is not None:
                        obs = newer
                        self._publish(obs)
            else:
                wait = float(p["sync_wait"])
                while rclpy.ok() and obs.running:
                    rc = self._wait_rc(wait)
                    obs = sim.step(None if rc is None else RC.from_us(rc))
                    self._publish(obs)
        except BenchError as e:
            self.get_logger().error(f"the simulator stopped the attempt: {e}")
            return 1
        finally:
            sim.close()
        self.get_logger().info(f"attempt over: {obs.status}, simulator time {obs.t:.2f} s")
        # Give the last status message time to leave before the node goes away.
        end = time.monotonic() + 0.5
        while rclpy.ok() and time.monotonic() < end:
            rclpy.spin_once(self, timeout_sec=0.05)
        return 0


def main() -> None:
    rclpy.init()
    node = Bridge()
    try:
        code = node.run()
    except KeyboardInterrupt:
        code = 130
    finally:
        node.destroy_node()
        if rclpy.ok():
            rclpy.shutdown()
    raise SystemExit(code)


if __name__ == "__main__":
    main()
