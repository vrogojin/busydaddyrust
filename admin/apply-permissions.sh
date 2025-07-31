#!/bin/bash
# Apply permissions to default group with verification and delays
# Usage: ./apply-permissions.sh [permissions_file]

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"

# Default permissions file
PERMISSIONS_FILE="${1:-$ROOT_DIR/mod-configs/default_permissions.txt}"

# Configuration
DELAY_BETWEEN_PERMISSIONS=${DELAY_OVERRIDE:-2}  # 2 seconds between each permission grant (or DELAY_OVERRIDE)
VERIFY_DELAY=${VERIFY_DELAY_OVERRIDE:-1}        # 1 second wait before verification (or VERIFY_DELAY_OVERRIDE)

# Check if permissions file exists
if [ ! -f "$PERMISSIONS_FILE" ]; then
    echo "Error: Permissions file not found: $PERMISSIONS_FILE"
    echo ""
    echo "Usage: $0 [permissions_file]"
    echo "Default: $ROOT_DIR/mod-configs/default_permissions.txt"
    exit 1
fi

# Check if server is running
CONTAINER_ID=$(docker compose ps -q lgsm)
if [ -z "$CONTAINER_ID" ]; then
    echo "Error: Server container is not running"
    exit 1
fi

echo "=== Applying Permissions to Default Group ==="
echo "Permissions file: $PERMISSIONS_FILE"
echo "Delay between permissions: ${DELAY_BETWEEN_PERMISSIONS}s"
echo ""

# First, ensure default group exists
echo "Ensuring default group exists..."
"$SCRIPT_DIR/rcon.sh" "oxide.group add default" 2>/dev/null || true
sleep 1

# Count permissions
TOTAL_PERMS=$(grep -v -E "^#|^$" "$PERMISSIONS_FILE" 2>/dev/null | wc -l)
echo "Found $TOTAL_PERMS permissions to apply"
echo ""

# Get current permissions for comparison
echo "Fetching current permissions..."
CURRENT_PERMS=$("$SCRIPT_DIR/rcon.sh" "oxide.show group default" | grep -A 1 "permissions:" | tail -1)
echo "Current permissions: ${CURRENT_PERMS:-none}"
echo ""

# Apply permissions one by one with verification
SUCCESS_COUNT=0
FAIL_COUNT=0
PERM_NUMBER=0

echo "Applying permissions..."
while IFS= read -r permission; do
    # Skip comments and empty lines
    [[ "$permission" =~ ^[[:space:]]*# ]] && continue
    [[ -z "${permission// }" ]] && continue
    
    # Trim whitespace
    permission=$(echo "$permission" | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')
    
    # Validate permission format
    if [ -n "$permission" ] && [[ "$permission" =~ ^[a-zA-Z0-9][a-zA-Z0-9\.]*$ ]]; then
        PERM_NUMBER=$((PERM_NUMBER + 1))
        echo ""
        echo "[$PERM_NUMBER/$TOTAL_PERMS] Processing: $permission"
        
        # Grant the permission
        echo "  → Granting permission..."
        if "$SCRIPT_DIR/rcon.sh" "oxide.grant group default $permission" >/dev/null 2>&1; then
            # Wait before verification
            sleep $VERIFY_DELAY
            
            # Verify the permission was added
            echo "  → Verifying..."
            if "$SCRIPT_DIR/rcon.sh" "oxide.show group default" 2>/dev/null | grep -q "$permission"; then
                echo "  ✓ Successfully granted: $permission"
                SUCCESS_COUNT=$((SUCCESS_COUNT + 1))
            else
                echo "  ⚠ Granted but not verified: $permission"
                FAIL_COUNT=$((FAIL_COUNT + 1))
            fi
        else
            echo "  ✗ Failed to grant: $permission"
            FAIL_COUNT=$((FAIL_COUNT + 1))
        fi
        
        # Pause between permissions
        if [ $PERM_NUMBER -lt $TOTAL_PERMS ]; then
            echo "  → Waiting ${DELAY_BETWEEN_PERMISSIONS}s before next permission..."
            sleep $DELAY_BETWEEN_PERMISSIONS
        fi
    else
        echo "  - Skipping invalid permission: '$permission'"
    fi
done < "$PERMISSIONS_FILE"

echo ""
echo "=== Permission Application Complete ==="
echo "✓ Successfully granted and verified: $SUCCESS_COUNT"
[ $FAIL_COUNT -gt 0 ] && echo "✗ Failed or unverified: $FAIL_COUNT"
echo ""

# Final verification - show all permissions
echo "Final verification of all permissions:"
echo "====================================="
"$SCRIPT_DIR/rcon.sh" "oxide.show group default" | grep -A 20 "permissions:" || echo "Could not retrieve permissions"
echo ""

# Save default permissions for reference
if [ "$PERMISSIONS_FILE" != "$ROOT_DIR/mod-configs/default_permissions.txt" ]; then
    echo "Saving permissions list to mod-configs/default_permissions.txt..."
    cp "$PERMISSIONS_FILE" "$ROOT_DIR/mod-configs/default_permissions.txt"
fi

echo "Done!"