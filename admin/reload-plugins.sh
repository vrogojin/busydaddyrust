#!/bin/bash

# Get RCON password
RCON_PASS=$(docker exec $(docker compose ps -q lgsm) cat /home/linuxgsm/serverfiles/server/rustserver/cfg/rconpassword 2>/dev/null)

if [ -z "$RCON_PASS" ]; then
    echo "Could not retrieve RCON password"
    exit 1
fi

# Use mcrcon if available, otherwise use rust-rcon or webrcon
docker exec $(docker compose ps -q lgsm) bash -c "
if command -v mcrcon &> /dev/null; then
    echo 'oxide.reload *' | mcrcon -H localhost -P 28016 -p '$RCON_PASS'
else
    # Alternative: use LinuxGSM console command
    echo 'Using LinuxGSM console method...'
    echo 'oxide.reload *' | timeout 5 ./rustserver console
fi
"