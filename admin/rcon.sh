#!/bin/bash
# RCON wrapper for host system
# Usage: ./rcon.sh "command"

COMMAND="$1"

if [ -z "$COMMAND" ]; then
    echo "Usage: $0 \"command\""
    echo ""
    echo "Examples:"
    echo "  $0 \"status\"                  # Show server status"
    echo "  $0 \"oxide.reload *\"          # Reload all plugins"
    echo "  $0 \"oxide.reload Bradley\"    # Reload specific plugin"
    echo "  $0 \"say Hello World\"         # Broadcast message"
    echo ""
    echo "Special commands:"
    echo "  $0 reload                    # Shortcut for oxide.reload *"
    exit 1
fi

# Handle special shortcuts
if [ "$COMMAND" = "reload" ]; then
    COMMAND="oxide.reload *"
fi

# Get container ID
CONTAINER_ID=$(docker compose ps -q lgsm)

if [ -z "$CONTAINER_ID" ]; then
    echo "Error: Server container is not running"
    exit 1
fi

# Execute RCON command
docker exec $CONTAINER_ID rcon-command "$COMMAND"