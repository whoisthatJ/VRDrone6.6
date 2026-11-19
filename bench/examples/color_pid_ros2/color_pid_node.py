"""The sample solution as a ROS 2 node: the same `ColorPid` pilot, fed from the bridge's topics.

    ros2 run ushqyr_ros2_bridge bridge &
    python3 color_pid_node.py

The bridge publishes `ushqyr/status` once per control step, after that step's sensor messages. This node gathers what
arrived, runs one step of the pilot when the status comes, and answers with one message on `ushqyr/rc`. It exits when
the attempt is over.
"""
from __future__ import annotations

import json
import math

import numpy as np
import rclpy
from color_pid import ColorPid
from rclpy.node import Node
from rclpy.qos import DurabilityPolicy, QoSProfile, ReliabilityPolicy
from sensor_msgs.msg import Image, Imu, LaserScan, Range
from std_msgs.msg import String
from ushqyr_bench import RC, Battery, Camera, Lidar, Observation, Tof
from ushqyr_bench import Imu as ImuBatch
from ushqyr_msgs.msg import AttemptStatus, RcChannels


def seconds(stamp) -> float:
    return stamp.sec + stamp.nanosec * 1e-9


class ColorPidNode(Node):
    def __init__(self) -> None:
        super().__init__("color_pid")
        sensor = QoSProfile(depth=50, reliability=ReliabilityPolicy.RELIABLE)
        fast = QoSProfile(depth=500, reliability=ReliabilityPolicy.RELIABLE)
        latched = QoSProfile(depth=1, reliability=ReliabilityPolicy.RELIABLE, durability=DurabilityPolicy.TRANSIENT_LOCAL)
        self.pub = self.create_publisher(RcChannels, "ushqyr/rc", 10)
        self.create_subscription(String, "ushqyr/info", self.on_info, latched)
        self.create_subscription(Image, "ushqyr/camera/image_raw", self.on_image, sensor)
        self.create_subscription(Imu, "ushqyr/imu/data_raw", self.on_imu, fast)
        self.create_subscription(Range, "ushqyr/tof/range", self.on_tof, sensor)
        self.create_subscription(LaserScan, "ushqyr/scan", self.on_scan, sensor)
        self.create_subscription(AttemptStatus, "ushqyr/status", self.on_status, sensor)
        self.pilot = None
        self.over = False
        self.started = False
        self.camera, self.tof, self.lidar = Camera(), Tof(), Lidar()
        self.imu_t, self.gyro, self.accel = [], [], []
        # The bridge starts the attempt when the first RC message arrives: keep saying "disarmed" until it does.
        self.hello = self.create_timer(0.2, self.say_hello)

    def say_hello(self) -> None:
        if self.started:
            self.hello.cancel()
        elif self.pilot is not None:
            self.send(RC())

    def send(self, rc: RC) -> None:
        msg = RcChannels()
        msg.channels = rc.channels()
        self.pub.publish(msg)

    def on_info(self, msg: String) -> None:
        if self.pilot is None:
            self.pilot = ColorPid(json.loads(msg.data))
            self.get_logger().info("got the simulator's handshake; the pilot is ready")

    def on_image(self, msg: Image) -> None:
        image = np.frombuffer(bytes(msg.data), np.uint8).reshape(msg.height, msg.width, 3)
        self.camera = Camera(image=image, t=seconds(msg.header.stamp), seq=self.camera.seq + 1, new=True)

    def on_imu(self, msg: Imu) -> None:
        self.imu_t.append(seconds(msg.header.stamp))
        self.gyro.append((msg.angular_velocity.x, msg.angular_velocity.y, msg.angular_velocity.z))
        self.accel.append((msg.linear_acceleration.x, msg.linear_acceleration.y, msg.linear_acceleration.z))

    def on_tof(self, msg: Range) -> None:
        valid = math.isfinite(msg.range)
        self.tof = Tof(range=msg.range if valid else 0.0, valid=valid, t=seconds(msg.header.stamp), new=True)

    def on_scan(self, msg: LaserScan) -> None:
        ranges = np.array([r if math.isfinite(r) else 0.0 for r in msg.ranges], np.float32)
        self.lidar = Lidar(ranges=ranges, t=seconds(msg.header.stamp), new=True)

    def on_status(self, msg: AttemptStatus) -> None:
        self.started = True
        if not msg.running:
            self.get_logger().info(f"attempt over: {msg.status}, the pilot counted {self.pilot.gates_passed if self.pilot else 0} gate(s)")
            self.over = True
            return
        if self.pilot is None:
            return
        imu = ImuBatch(np.array(self.imu_t, np.float32), np.array(self.gyro, np.float32).reshape(-1, 3),
                       np.array(self.accel, np.float32).reshape(-1, 3))
        obs = Observation(t=seconds(msg.stamp), tick=msg.tick, running=True, status=msg.status, camera=self.camera, imu=imu,
                          tof=self.tof, battery=Battery(), lidar=self.lidar)
        self.send(self.pilot.update(obs))
        # What has been used is no longer "new".
        self.imu_t, self.gyro, self.accel = [], [], []
        self.camera.new = self.tof.new = self.lidar.new = False


def main() -> None:
    rclpy.init()
    node = ColorPidNode()
    try:
        while rclpy.ok() and not node.over:
            rclpy.spin_once(node, timeout_sec=0.1)
    except KeyboardInterrupt:
        pass
    node.destroy_node()
    if rclpy.ok():
        rclpy.shutdown()


if __name__ == "__main__":
    main()
