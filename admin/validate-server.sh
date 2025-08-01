#!/bin/bash

# Validate Rust server game files
# This script stops the server, validates files, and restarts

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR/.."

echo "Starting Rust server validation at $(date)"

# Function to send notification (can be extended to send Discord/email alerts)
notify() {
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] $1"
}

# Check if validation is already running
LOCKFILE="/tmp/rust-validation.lock"
if [ -f "$LOCKFILE" ]; then
    notify "Validation already in progress, skipping..."
    exit 0
fi

# Create lock file
touch "$LOCKFILE"
trap "rm -f $LOCKFILE" EXIT

notify "Stopping Rust server for validation..."
docker compose exec -u linuxgsm lgsm bash -c "cd /home/linuxgsm && ./rustserver stop" || true

# Wait for server to fully stop
sleep 10

notify "Starting game file validation..."
if docker compose exec -u linuxgsm lgsm bash -c "cd /home/linuxgsm && ./rustserver validate"; then
    notify "Validation completed successfully"
    VALIDATION_RESULT="SUCCESS"
else
    notify "Validation failed with errors"
    VALIDATION_RESULT="FAILED"
fi

# Log validation event
echo "$(date '+%Y-%m-%d %H:%M:%S'),VALIDATION,$VALIDATION_RESULT" >> admin/logs/validation.log

notify "Restarting Rust server..."
docker compose restart

notify "Validation process completed"

# Remove lock file
rm -f "$LOCKFILE"