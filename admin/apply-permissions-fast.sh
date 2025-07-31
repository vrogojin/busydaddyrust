#!/bin/bash
# Fast permission application without individual verification
# Usage: ./apply-permissions-fast.sh [permissions_file]

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"

# Default permissions file
PERMISSIONS_FILE="${1:-$ROOT_DIR/mod-configs/default_permissions.txt}"

# Check if permissions file exists
if [ ! -f "$PERMISSIONS_FILE" ]; then
    echo "Error: Permissions file not found: $PERMISSIONS_FILE"
    exit 1
fi

# Check if server is running
CONTAINER_ID=$(docker compose ps -q lgsm)
if [ -z "$CONTAINER_ID" ]; then
    echo "Error: Server container is not running"
    exit 1
fi

echo "=== Fast Permission Application ==="
echo "Permissions file: $PERMISSIONS_FILE"
echo ""

# Ensure default group exists
echo "Ensuring default group exists..."
"$SCRIPT_DIR/rcon.sh" "oxide.group add default" 2>/dev/null || true

# Count permissions
TOTAL_PERMS=$(grep -v -E "^#|^$" "$PERMISSIONS_FILE" 2>/dev/null | wc -l)
echo "Found $TOTAL_PERMS permissions to apply"
echo ""

# Apply all permissions quickly
echo "Applying permissions..."
SUCCESS_COUNT=0
FAIL_COUNT=0

while IFS= read -r permission; do
    # Skip comments and empty lines
    [[ "$permission" =~ ^[[:space:]]*# ]] && continue
    [[ -z "${permission// }" ]] && continue
    
    # Trim whitespace
    permission=$(echo "$permission" | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')
    
    if [ -n "$permission" ] && [[ "$permission" =~ ^[a-zA-Z0-9][a-zA-Z0-9\.]*$ ]]; then
        if "$SCRIPT_DIR/rcon.sh" "oxide.grant group default $permission" >/dev/null 2>&1; then
            echo "  ✓ Granted: $permission"
            SUCCESS_COUNT=$((SUCCESS_COUNT + 1))
        else
            echo "  ✗ Failed: $permission"
            FAIL_COUNT=$((FAIL_COUNT + 1))
        fi
        # Small delay to avoid overwhelming the server
        sleep 0.2
    fi
done < <(grep -v -E "^#|^$" "$PERMISSIONS_FILE")

echo ""
echo "=== Complete ==="
echo ""

# Final verification
echo "Final permissions:"
"$SCRIPT_DIR/rcon.sh" "oxide.show group default" | grep -A 20 "permissions:"