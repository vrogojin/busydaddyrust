#!/bin/bash
# RCON command utility for Rust server
# Usage: rcon-command "command"

COMMAND="$1"
if [ -z "$COMMAND" ]; then
    echo "Usage: rcon-command \"command\""
    echo "Example: rcon-command \"oxide.reload *\""
    exit 1
fi

# Get RCON password from running process
RCON_PASS=$(ps aux | grep -o '+rcon.password [^ ]*' | cut -d' ' -f2 | tr -d '"' | head -1)

if [ -z "$RCON_PASS" ]; then
    echo "Error: Could not find RCON password. Is the server running?"
    exit 1
fi

# Execute command via WebSocket
python3 -c "
import websocket
import json
import socket
import sys

rcon_pass = '$RCON_PASS'
command = '''$COMMAND'''

# Get container IP
container_ip = socket.gethostbyname(socket.gethostname())
ws_url = f'ws://{container_ip}:28016/{rcon_pass}'

try:
    ws = websocket.create_connection(ws_url, timeout=5)
    message = {'Message': command, 'Identifier': 1, 'Stacktrace': ''}
    ws.send(json.dumps(message))
    response = ws.recv()
    data = json.loads(response)
    print(data.get('Message', 'No response'))
    ws.close()
except Exception as e:
    print(f'Error: {e}', file=sys.stderr)
    sys.exit(1)
"