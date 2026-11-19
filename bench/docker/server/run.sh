#!/bin/sh
# Starts one attempt and exits when it ends. Extra options (-bench-track, -bench-seed, -bench-token, ...) come from
# the command line. Not -nographics: that switch turns rendering off and the camera would send black pictures.
# (Xvfb is started by hand: xvfb-run waits for a signal that never arrives when it is the container's first process.)
Xvfb :99 -screen 0 1280x720x24 +extension GLX -nolisten tcp >/dev/null 2>&1 &
i=0
while [ ! -e /tmp/.X11-unix/X99 ] && [ $i -lt 100 ]; do sleep 0.1; i=$((i + 1)); done
export DISPLAY=:99
if [ "$USHQYR_GPU" != "off" ] && [ -e /dev/dxg ] && [ -e /usr/lib/wsl/lib/libd3d12.so ]; then
    # Docker Desktop on Windows (WSL2) started with the GPU: Mesa draws through Direct3D 12 on the real video card.
    export GALLIUM_DRIVER=d3d12 LD_LIBRARY_PATH=/usr/lib/wsl/lib
    echo "[run.sh] drawing on the GPU through WSL (d3d12)"
else
    # No GPU: Mesa's software renderer. Correct, but tens of times slower than real time.
    export LIBGL_ALWAYS_SOFTWARE=1
    echo "[run.sh] drawing in software (no GPU)"
fi
exec /opt/ushqyr/UshqyrBench.x86_64 -batchmode -force-glcore -screen-width 320 -screen-height 180 \
    -bench -bench-exit -bench-listen-all -bench-out /results -logFile - "$@"
