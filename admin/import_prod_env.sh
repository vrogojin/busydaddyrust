#!/bin/bash
# Import production environment settings
# This script imports plugins, permissions, and configs from a package file

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
PACKAGE_FILE="$1"
IMPORT_DIR="/tmp/rust_import_$$"

if [ -z "$PACKAGE_FILE" ] || [ ! -f "$PACKAGE_FILE" ]; then
    echo "Usage: $0 <package_file>"
    echo "Example: $0 rust_prod_env_20241105_120000.tar.gz"
    exit 1
fi

echo "=== Rust Production Environment Import ==="
echo "Package file: $PACKAGE_FILE"
echo "Import directory: $IMPORT_DIR"

# Create import directory and extract package
mkdir -p "$IMPORT_DIR"
echo ""
echo "1. Extracting package..."
tar -xzf "$PACKAGE_FILE" -C "$IMPORT_DIR"
echo "   - Package extracted successfully"

# Display export info if available
if [ -f "$IMPORT_DIR/export_info.txt" ]; then
    echo ""
    echo "Package Information:"
    echo "===================="
    cat "$IMPORT_DIR/export_info.txt"
    echo "===================="
fi

# Check if server is running
CONTAINER_ID=$(docker compose ps -q lgsm)
if [ -z "$CONTAINER_ID" ]; then
    echo ""
    echo "Warning: Server container is not running. Some operations may be limited."
    echo "Consider starting the server with: docker compose up -d"
fi

echo ""
echo "2. Importing plugins..."
# Import plugins list
if [ -f "$IMPORT_DIR/plugins/plugins.txt" ]; then
    # Backup existing plugins.txt
    if [ -f "$ROOT_DIR/mod-configs/plugins.txt" ]; then
        cp "$ROOT_DIR/mod-configs/plugins.txt" "$ROOT_DIR/mod-configs/plugins.txt.backup"
        echo "   - Backed up existing plugins.txt"
    fi
    
    # Merge non-admin plugins with existing admin plugins
    {
        # Keep existing admin plugins
        [ -f "$ROOT_DIR/mod-configs/plugins.txt" ] && grep -E "admin|Admin|ADMIN" "$ROOT_DIR/mod-configs/plugins.txt" || true
        echo ""
        echo "# Imported production plugins"
        cat "$IMPORT_DIR/plugins/plugins.txt"
    } > "$ROOT_DIR/mod-configs/plugins.txt.new"
    
    mv "$ROOT_DIR/mod-configs/plugins.txt.new" "$ROOT_DIR/mod-configs/plugins.txt"
    echo "   - Imported plugins list"
fi

# Import custom plugins
if [ -d "$IMPORT_DIR/plugins/custom" ] && [ "$(ls -A "$IMPORT_DIR/plugins/custom"/*.cs 2>/dev/null)" ]; then
    mkdir -p "$ROOT_DIR/custom-mods"
    cp "$IMPORT_DIR/plugins/custom"/*.cs "$ROOT_DIR/custom-mods/"
    echo "   - Imported custom plugins"
fi

echo ""
echo "3. Importing plugin configurations..."
# Import plugin configurations
if [ -d "$IMPORT_DIR/mod-configs" ] && [ "$(ls -A "$IMPORT_DIR/mod-configs"/*.json 2>/dev/null)" ]; then
    mkdir -p "$ROOT_DIR/mod-configs"
    cp "$IMPORT_DIR/mod-configs"/*.json "$ROOT_DIR/mod-configs/"
    echo "   - Imported plugin configurations"
fi

echo ""
echo "4. Importing server configuration..."
# Import rust-environment.sh settings
if [ -f "$IMPORT_DIR/configs/rust-environment.sh" ]; then
    # Backup existing config
    cp "$ROOT_DIR/rust-environment.sh" "$ROOT_DIR/rust-environment.sh.backup"
    echo "   - Backed up existing rust-environment.sh"
    
    # Extract important settings from imported config
    IMPORT_WORLDSIZE=$(grep "^export worldsize=" "$IMPORT_DIR/configs/rust-environment.sh" | cut -d'=' -f2)
    IMPORT_MAXPLAYERS=$(grep "^export maxplayers=" "$IMPORT_DIR/configs/rust-environment.sh" | cut -d'=' -f2)
    IMPORT_SERVERNAME=$(grep "^export servername=" "$IMPORT_DIR/configs/rust-environment.sh" | cut -d'"' -f2)
    
    # Keep existing worldsize configuration (now handled via .env file)
    
    # Update other settings if found
    if [ -n "$IMPORT_MAXPLAYERS" ]; then
        sed -i "s/^export maxplayers=.*/export maxplayers=$IMPORT_MAXPLAYERS/" "$ROOT_DIR/rust-environment.sh" 2>/dev/null || \
        sed -i '' "s/^export maxplayers=.*/export maxplayers=$IMPORT_MAXPLAYERS/" "$ROOT_DIR/rust-environment.sh"
    fi
    
    echo "   - Updated server configuration"
fi

# Import LinuxGSM configs if available and server is running
if [ -f "$IMPORT_DIR/configs/lgsm_configs.tar.gz" ] && [ -n "$CONTAINER_ID" ]; then
    echo "   - Importing LinuxGSM configurations..."
    docker cp "$IMPORT_DIR/configs/lgsm_configs.tar.gz" "$CONTAINER_ID:/tmp/"
    docker exec "$CONTAINER_ID" bash -c "
        cd /home/linuxgsm/lgsm/config-lgsm
        tar -xzf /tmp/lgsm_configs.tar.gz
        rm /tmp/lgsm_configs.tar.gz
    " 2>/dev/null || echo "   - Warning: Could not import LinuxGSM configs"
fi

echo ""
echo "5. Downloading and updating plugins..."
# Update plugins from uMod
"$SCRIPT_DIR/get-or-update-oxide-plugins.sh"

if [ -n "$CONTAINER_ID" ]; then
    echo ""
    echo "6. Importing permissions..."
    
    # Import default group permissions
    if [ -f "$IMPORT_DIR/permissions/default_permissions.txt" ]; then
        echo "   - Importing default group permissions..."
        
        # Count permissions to import
        PERM_COUNT=$(wc -l < "$IMPORT_DIR/permissions/default_permissions.txt" 2>/dev/null || echo "0")
        echo "   - Found $PERM_COUNT permissions to import"
        
        if [ "$PERM_COUNT" -eq 0 ]; then
            echo "   - Warning: Permission file exists but is empty"
        else
            # Show first few permissions for verification
            echo "   - First few permissions to import:"
            head -5 "$IMPORT_DIR/permissions/default_permissions.txt" | sed 's/^/     - /'
            [ "$PERM_COUNT" -gt 5 ] && echo "     - ... and $((PERM_COUNT - 5)) more"
            
            # First, ensure default group exists (it should always exist but just in case)
            echo "   - Ensuring default group exists..."
            "$SCRIPT_DIR/rcon.sh" "oxide.group add default" 2>/dev/null || true
            
            # Clear existing permissions for default group (optional - uncomment if needed)
            # echo "   - Clearing existing default group permissions..."
            # "$SCRIPT_DIR/rcon.sh" "oxide.revoke group default *" 2>/dev/null || true
            
            # Import each permission
            SUCCESS_COUNT=0
            FAIL_COUNT=0
            echo "   - Granting permissions to default group..."
            while IFS= read -r permission; do
                # Trim whitespace
                permission=$(echo "$permission" | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')
                
                if [ -n "$permission" ] && [[ "$permission" =~ ^[a-zA-Z0-9][a-zA-Z0-9\.]*$ ]]; then
                    if "$SCRIPT_DIR/rcon.sh" "oxide.grant group default $permission" >/dev/null 2>&1; then
                        echo "     ✓ Granted: $permission"
                        ((SUCCESS_COUNT++))
                    else
                        echo "     ✗ Failed: $permission"
                        ((FAIL_COUNT++))
                    fi
                else
                    echo "     - Skipping invalid permission: '$permission'"
                fi
            done < "$IMPORT_DIR/permissions/default_permissions.txt"
            
            echo "   - Permission import complete: $SUCCESS_COUNT succeeded, $FAIL_COUNT failed"
            
            # Verify permissions were applied
            echo "   - Verifying permissions..."
            "$SCRIPT_DIR/rcon.sh" "oxide.show group default" > "$IMPORT_DIR/verify_permissions.txt" 2>&1
            
            # Check if permissions were applied
            if grep -q "permissions:" "$IMPORT_DIR/verify_permissions.txt"; then
                echo "   - Permissions verified successfully"
            else
                echo "   - Warning: Could not verify permissions"
            fi
        fi
    else
        echo "   - No default group permissions found in package"
    fi
    
    echo ""
    echo "7. Reloading plugins..."
    "$SCRIPT_DIR/rcon.sh" "oxide.reload *"
    echo "   - All plugins reloaded"
else
    echo ""
    echo "6. Skipping permissions import (server not running)"
    echo "7. Skipping plugin reload (server not running)"
fi

# Cleanup
rm -rf "$IMPORT_DIR"

echo ""
echo "=== Import Complete ==="
echo ""

# Show summary of what was imported
echo "Import Summary:"
echo "---------------"

# Count plugins
if [ -f "$ROOT_DIR/mod-configs/plugins.txt" ]; then
    PLUGIN_COUNT=$(grep -v -E "^#|^$" "$ROOT_DIR/mod-configs/plugins.txt" | wc -l)
    echo "✓ Plugins imported: $PLUGIN_COUNT"
fi

# Count custom plugins
if [ -d "$ROOT_DIR/custom-mods" ]; then
    CUSTOM_COUNT=$(ls -1 "$ROOT_DIR/custom-mods"/*.cs 2>/dev/null | wc -l)
    [ "$CUSTOM_COUNT" -gt 0 ] && echo "✓ Custom plugins imported: $CUSTOM_COUNT"
fi

# Count configs
if [ -d "$ROOT_DIR/mod-configs" ]; then
    CONFIG_COUNT=$(ls -1 "$ROOT_DIR/mod-configs"/*.json 2>/dev/null | wc -l)
    echo "✓ Plugin configs imported: $CONFIG_COUNT"
fi

# Show permission count if server was running
if [ -n "$CONTAINER_ID" ] && [ -n "$SUCCESS_COUNT" ]; then
    echo "✓ Permissions granted: $SUCCESS_COUNT"
    [ "$FAIL_COUNT" -gt 0 ] && echo "✗ Permissions failed: $FAIL_COUNT"
fi

echo "✓ Server config updated"

echo ""
echo "Next steps:"
echo "1. Review the imported configuration in rust-environment.sh"
echo "2. If the server is not running, start it with: docker compose up -d"
echo "3. Check server logs for any plugin errors: docker compose logs -f"
echo "4. Verify permissions are correctly applied using:"
echo "   ./admin/rcon.sh \"oxide.show group default\""
echo ""
echo "Backup files created:"
echo "- rust-environment.sh.backup"
[ -f "$ROOT_DIR/mod-configs/plugins.txt.backup" ] && echo "- mod-configs/plugins.txt.backup"