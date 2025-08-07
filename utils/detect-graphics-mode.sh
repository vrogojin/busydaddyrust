#!/bin/bash

# Detect if we should use special graphics workarounds
HANG_COUNTER_FILE="/home/linuxgsm/hang_counter"

# Read hang counter
if [ -f "$HANG_COUNTER_FILE" ]; then
    HANG_COUNT=$(cat "$HANG_COUNTER_FILE")
else
    HANG_COUNT=0
fi

# If server has hung more than 2 times, use aggressive workarounds
if [ "$HANG_COUNT" -gt 2 ]; then
    echo "Multiple hangs detected, using fallback graphics mode"
    
    # Set environment variables for software rendering
    export LIBGL_ALWAYS_SOFTWARE=1
    export GALLIUM_DRIVER=llvmpipe
    export DISPLAY=:99
    
    # Start a virtual display if not already running
    if ! pgrep Xvfb > /dev/null; then
        Xvfb :99 -screen 0 1024x768x24 > /dev/null 2>&1 &
    fi
    
    echo "Software rendering enabled"
fi