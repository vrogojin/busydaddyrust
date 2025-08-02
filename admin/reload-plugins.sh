#!/bin/bash

# Reload all Oxide plugins using rcon.sh
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"

echo "Reloading all Oxide plugins..."
"$SCRIPT_DIR/rcon.sh" "oxide.reload *"

if [ $? -eq 0 ]; then
    echo "All plugins reloaded successfully"
else
    echo "Failed to reload plugins"
    exit 1
fi