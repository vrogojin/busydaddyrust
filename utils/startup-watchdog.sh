#!/bin/bash

# Startup watchdog to detect and handle asset warmup hangs
# This script monitors the console log and kills/restarts if stuck

CONSOLE_LOG="/home/linuxgsm/log/console/rustserver-console.log"
TIMEOUT=300  # 5 minutes to detect if stuck at asset warmup
CHECK_INTERVAL=30  # Check every 30 seconds

echo "$(date) - Startup watchdog started, monitoring for asset warmup hang"

# Wait for server to start generating logs
sleep 10

# Get initial position in log
if [ -f "$CONSOLE_LOG" ]; then
    LAST_SIZE=$(stat -c%s "$CONSOLE_LOG")
    LAST_LINE=$(tail -1 "$CONSOLE_LOG")
else
    echo "$(date) - Console log not found, waiting..."
    sleep 30
    exit 0
fi

# Monitor for stuck asset warmup
STUCK_COUNT=0
MAX_STUCK_COUNT=$((TIMEOUT / CHECK_INTERVAL))

while [ $STUCK_COUNT -lt $MAX_STUCK_COUNT ]; do
    sleep $CHECK_INTERVAL
    
    # Check if log is growing
    CURRENT_SIZE=$(stat -c%s "$CONSOLE_LOG" 2>/dev/null || echo 0)
    CURRENT_LINE=$(tail -1 "$CONSOLE_LOG" 2>/dev/null || echo "")
    
    # Check for asset warmup hang pattern
    if echo "$CURRENT_LINE" | grep -q "Asset Warmup ([0-9]*/[0-9]*)" && [ "$CURRENT_SIZE" -eq "$LAST_SIZE" ]; then
        STUCK_COUNT=$((STUCK_COUNT + 1))
        echo "$(date) - Possible asset warmup hang detected ($STUCK_COUNT/$MAX_STUCK_COUNT)"
    elif echo "$CURRENT_LINE" | grep -q "Saving complete\|BradleyAPC Spawned\|Steam Item Definitions"; then
        # Server started successfully
        echo "$(date) - Server started successfully, watchdog exiting"
        exit 0
    else
        # Log is growing or changed, reset counter
        if [ "$CURRENT_SIZE" -ne "$LAST_SIZE" ]; then
            STUCK_COUNT=0
        fi
    fi
    
    LAST_SIZE=$CURRENT_SIZE
    LAST_LINE=$CURRENT_LINE
done

# If we get here, server is stuck
echo "$(date) - Server stuck at asset warmup, attempting recovery"

# Kill the stuck RustDedicated process
pkill -9 RustDedicated

# Increment hang counter
HANG_COUNTER_FILE="/home/linuxgsm/hang_counter"
if [ -f "$HANG_COUNTER_FILE" ]; then
    HANG_COUNT=$(cat "$HANG_COUNTER_FILE")
else
    HANG_COUNT=0
fi
echo $((HANG_COUNT + 1)) > "$HANG_COUNTER_FILE"

# Create marker for validation on next start
touch /home/linuxgsm/VALIDATION_NEEDED

echo "$(date) - Killed stuck server, validation will run on next restart"
echo "$(date) - Hang count: $((HANG_COUNT + 1))"
exit 1