# The sample solution as a ROS 2 submission. Check it:
#   ushqyr-bench build-images --ros2 jazzy
#   ushqyr-bench submit-check bench/examples/color_pid_ros2
# (color_pid.py here is a copy of ../color_pid/color_pid.py; a test keeps the two the same.)
# Another ROS 2 version: submit-check ... --build-arg ROS_DISTRO=humble
ARG ROS_DISTRO=jazzy
FROM ushqyr-bench-ros2:${ROS_DISTRO}
COPY color_pid.py color_pid_node.py run.sh ./
CMD ["bash", "run.sh"]
