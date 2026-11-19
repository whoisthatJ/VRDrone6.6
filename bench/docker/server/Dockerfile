# The simulator for the qualification server and for `ushqyr-bench submit-check`.
# Build (from the repository root, after Ushqyr/Build/Build Bench Server (Linux)):
#   docker build -t ushqyr-bench-server -f bench/docker/server/Dockerfile --build-context build=Builds/BenchServer bench/docker/server
FROM ubuntu:24.04
ENV DEBIAN_FRONTEND=noninteractive
# A virtual display and software OpenGL (Mesa llvmpipe): the drone's camera has to render, with or without a GPU.
RUN apt-get update && apt-get install -y --no-install-recommends \
        xvfb xauth libgl1 libgl1-mesa-dri libglx-mesa0 libegl1 libglu1-mesa \
        libx11-6 libxcursor1 libxrandr2 libxi6 libxinerama1 libxext6 libasound2t64 \
        ffmpeg ca-certificates \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build . /opt/ushqyr
COPY run.sh /opt/run.sh
RUN chmod +x /opt/ushqyr/UshqyrBench.x86_64 /opt/run.sh && useradd -m -u 10000 bench && mkdir /results && chown bench /results
USER bench
ENV HOME=/home/bench USHQYR_FFMPEG=/usr/bin/ffmpeg
EXPOSE 5757
VOLUME /results
ENTRYPOINT ["/opt/run.sh"]
