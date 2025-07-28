#!/bin/bash
# Get the directory where this script is located
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# Go to the project root (parent of admin directory)
cd "$SCRIPT_DIR/.."

echo "Building custom Rust server image..."
docker compose build

echo "Stopping server..."
docker compose down

echo "Starting server with new image..."
docker compose up -d

echo "Server restarted. Waiting for it to fully start..."
sleep 30

echo "You can now use RCON commands inside the container:"
echo "  docker exec $(docker compose ps -q lgsm) rcon-command \"status\""
echo "  docker exec $(docker compose ps -q lgsm) rcon-reload-plugins"
