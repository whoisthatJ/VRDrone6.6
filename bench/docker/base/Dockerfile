# Base image for team solutions (CPU): Python, the SDK, NumPy and OpenCV.
# Build (from the repository root):
#   docker build -t ushqyr-bench-base:cpu -f bench/docker/base/Dockerfile bench
FROM python:3.11-slim
ENV PYTHONUNBUFFERED=1 PIP_NO_CACHE_DIR=1
COPY sdk/python /tmp/sdk
RUN pip install /tmp/sdk numpy "opencv-python-headless>=4.5" && rm -rf /tmp/sdk
WORKDIR /app
