#!/bin/bash

# Monthly wipe script for Rust server
# Runs on first Thursday of each month

echo "$(date) - Starting monthly wipe process"

# Stop the server
echo "Stopping server..."
/home/linuxgsm/rustserver stop

# Wait for server to fully stop
sleep 30

# Backup current save before wipe (optional)
BACKUP_DIR="/home/linuxgsm/backups/pre-wipe"
mkdir -p "$BACKUP_DIR"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
echo "Creating pre-wipe backup..."
tar -czf "$BACKUP_DIR/pre-wipe-$TIMESTAMP.tar.gz" \
    /home/linuxgsm/serverfiles/server/rustserver/*.sav* \
    /home/linuxgsm/serverfiles/server/rustserver/*.map* \
    2>/dev/null || true

# Remove save files for fresh wipe
echo "Removing save files..."
rm -f /home/linuxgsm/serverfiles/server/rustserver/*.sav*
rm -f /home/linuxgsm/serverfiles/server/rustserver/*.map*
rm -f /home/linuxgsm/serverfiles/server/rustserver/player.deaths.*
rm -f /home/linuxgsm/serverfiles/server/rustserver/player.identities.*
rm -f /home/linuxgsm/serverfiles/server/rustserver/player.states.*

# Optionally change map seed for variety (uncomment to enable)
# NEW_SEED=$((RANDOM % 2147483647))
# sed -i "s/seed=[0-9]*/seed=$NEW_SEED/" /home/linuxgsm/lgsm/config-lgsm/rustserver/rustserver.cfg
# echo "New map seed: $NEW_SEED"

# Clear player blueprints if doing a full BP wipe (uncomment for BP wipe)
# echo "Clearing blueprints..."
# rm -f /home/linuxgsm/serverfiles/server/rustserver/player.blueprints.*

# Start the server with fresh map
echo "Starting server with fresh map..."
/home/linuxgsm/rustserver start

echo "$(date) - Monthly wipe completed successfully"

# Announce in server after startup (via RCON after delay)
sleep 120
/usr/local/bin/rcon-command "say MONTHLY WIPE COMPLETE! Fresh map, same great community!" 2>/dev/null || true