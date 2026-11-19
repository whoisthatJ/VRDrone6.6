#!/bin/bash
# The bridge and the pilot in one container. The container ends when the pilot does.
ros2 run ushqyr_ros2_bridge bridge --ros-args -p connect_wait:=120.0 &
python3 color_pid_node.py
wait
