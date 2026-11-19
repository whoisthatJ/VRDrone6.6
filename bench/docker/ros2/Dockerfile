# Base image for ROS 2 solutions: ROS 2, the SDK, the bridge and its messages.
# Build (from the repository root), once per ROS 2 version:
#   docker build -t ushqyr-bench-ros2:jazzy  --build-arg ROS_DISTRO=jazzy  -f bench/docker/ros2/Dockerfile bench
#   docker build -t ushqyr-bench-ros2:humble --build-arg ROS_DISTRO=humble -f bench/docker/ros2/Dockerfile bench
ARG ROS_DISTRO=jazzy
FROM ros:${ROS_DISTRO}-ros-base
ARG ROS_DISTRO
ENV DEBIAN_FRONTEND=noninteractive PYTHONUNBUFFERED=1
RUN apt-get update && apt-get install -y --no-install-recommends \
        python3-pip python3-numpy python3-msgpack python3-opencv \
    && rm -rf /var/lib/apt/lists/*
COPY sdk/python /tmp/sdk
# The SDK into the system's Python, where ROS 2 looks. Copied, not pip-installed: the pip of older Ubuntu versions
# can't install a pyproject-only package, and the newer one refuses to touch the system's Python.
RUN cp -r /tmp/sdk/src/ushqyr_bench "$(python3 -c 'import site; print(site.getsitepackages()[0])')/" && rm -rf /tmp/sdk     && python3 -c "import ushqyr_bench, msgpack, numpy, cv2"
COPY ros2 /opt/ushqyr_ws/src
RUN . /opt/ros/${ROS_DISTRO}/setup.sh && cd /opt/ushqyr_ws && colcon build --merge-install \
    && rm -rf build log
COPY docker/ros2/entrypoint.sh /ushqyr_entrypoint.sh
RUN chmod +x /ushqyr_entrypoint.sh
# The solution's container has a read-only file system: ROS 2 writes its logs to /tmp.
ENV ROS_LOG_DIR=/tmp/ros_log ROS_HOME=/tmp/ros_home
WORKDIR /app
ENTRYPOINT ["/ushqyr_entrypoint.sh"]
CMD ["bash"]
