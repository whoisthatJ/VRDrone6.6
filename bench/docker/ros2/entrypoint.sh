#!/bin/bash
set -e
source "/opt/ros/$ROS_DISTRO/setup.bash"
source /opt/ushqyr_ws/install/setup.bash
# Nodes talk only inside this container (it has no network to anyone but the simulator anyway).
if [ "$ROS_DISTRO" = "humble" ]; then export ROS_LOCALHOST_ONLY=1; else export ROS_AUTOMATIC_DISCOVERY_RANGE=LOCALHOST; fi
exec "$@"
