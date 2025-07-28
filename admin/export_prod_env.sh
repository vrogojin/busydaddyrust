#!/bin/bash
# Export production environment settings
# This script exports plugins, permissions, and configs to a package file

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
EXPORT_DIR="/tmp/rust_export_$TIMESTAMP"
PACKAGE_FILE="$ROOT_DIR/rust_prod_env_$TIMESTAMP.tar.gz"

echo "=== Rust Production Environment Export ==="
echo "Export directory: $EXPORT_DIR"
echo "Package file: $PACKAGE_FILE"

# Create export directory structure
mkdir -p "$EXPORT_DIR"/{plugins,permissions,configs,mod-configs}

# Check if server is running
CONTAINER_ID=$(docker compose ps -q lgsm)
if [ -z "$CONTAINER_ID" ]; then
    echo "Error: Server container is not running"
    exit 1
fi

echo ""
echo "1. Exporting plugin list..."
# Export plugins.txt (excluding admin plugins: Godmode and Vanish)
if [ -f "$ROOT_DIR/mod-configs/plugins.txt" ]; then
    # Filter out admin plugins (Godmode and Vanish) and empty lines/comments
    grep -v -E "^#|^$|^Godmode$|^Vanish$" "$ROOT_DIR/mod-configs/plugins.txt" > "$EXPORT_DIR/plugins/plugins.txt" || true
    echo "   - Exported non-admin plugins list"
else
    echo "   - No plugins.txt found"
fi

# Export custom plugins (excluding Godmode.cs and Vanish.cs if they exist)
if [ -d "$ROOT_DIR/custom-mods" ] && [ "$(ls -A "$ROOT_DIR/custom-mods"/*.cs 2>/dev/null)" ]; then
    mkdir -p "$EXPORT_DIR/plugins/custom"
    # Copy all custom plugins except Godmode and Vanish
    for plugin in "$ROOT_DIR/custom-mods"/*.cs; do
        basename=$(basename "$plugin" .cs)
        if [ "$basename" != "Godmode" ] && [ "$basename" != "Vanish" ]; then
            cp "$plugin" "$EXPORT_DIR/plugins/custom/"
        fi
    done
    CUSTOM_COUNT=$(ls -1 "$EXPORT_DIR/plugins/custom"/*.cs 2>/dev/null | wc -l || echo "0")
    echo "   - Exported $CUSTOM_COUNT custom plugins"
fi

echo ""
echo "2. Getting loaded plugins from server..."
# Get list of loaded plugins from the server
if [ -n "$CONTAINER_ID" ]; then
    echo "   - Fetching loaded plugins list..."
    "$SCRIPT_DIR/rcon.sh" "oxide.plugins" > "$EXPORT_DIR/loaded_plugins_raw.txt" 2>/dev/null || true
    
    # Extract plugin names from the output (format: "PluginName v1.0.0 by Author")
    if [ -f "$EXPORT_DIR/loaded_plugins_raw.txt" ]; then
        grep -E "^[A-Za-z0-9]+ v[0-9\.]+" "$EXPORT_DIR/loaded_plugins_raw.txt" | awk '{print $1}' > "$EXPORT_DIR/loaded_plugins.txt" 2>/dev/null || true
        LOADED_COUNT=$(wc -l < "$EXPORT_DIR/loaded_plugins.txt" 2>/dev/null || echo "0")
        echo "   - Found $LOADED_COUNT loaded plugins"
    fi
else
    echo "   - Warning: Server not running, cannot check loaded plugins"
fi

echo ""
echo "3. Exporting plugin configurations..."
# Export plugin configurations
if [ -d "$ROOT_DIR/mod-configs" ]; then
    # Build list of valid plugins (from plugins.txt, custom-mods, and loaded plugins)
    > "$EXPORT_DIR/valid_plugins.txt"
    
    # Add plugins from plugins.txt
    if [ -f "$ROOT_DIR/mod-configs/plugins.txt" ]; then
        grep -v -E "^#|^$" "$ROOT_DIR/mod-configs/plugins.txt" >> "$EXPORT_DIR/valid_plugins.txt" || true
    fi
    
    # Add custom plugins (extract class names)
    if [ -d "$ROOT_DIR/custom-mods" ]; then
        for plugin in "$ROOT_DIR/custom-mods"/*.cs; do
            [ -f "$plugin" ] || continue
            # Extract class name from C# file
            grep -E "class\s+\w+\s*:\s*(RustPlugin|CovalencePlugin)" "$plugin" | sed -E 's/.*class\s+(\w+).*/\1/' >> "$EXPORT_DIR/valid_plugins.txt" 2>/dev/null || true
        done
    fi
    
    # Add loaded plugins if available
    if [ -f "$EXPORT_DIR/loaded_plugins.txt" ]; then
        cat "$EXPORT_DIR/loaded_plugins.txt" >> "$EXPORT_DIR/valid_plugins.txt"
    fi
    
    # Remove duplicates and sort
    sort -u "$EXPORT_DIR/valid_plugins.txt" -o "$EXPORT_DIR/valid_plugins.txt"
    
    echo "   - Valid plugins list created"
    
    # Copy only configs for valid plugins (non-admin)
    for config in "$ROOT_DIR/mod-configs"/*.json; do
        [ -f "$config" ] || continue
        basename=$(basename "$config" .json)
        
        # Skip admin plugin configs (Godmode and Vanish)
        if [ "$basename" = "Godmode" ] || [ "$basename" = "Vanish" ]; then
            continue
        fi
        
        # Check if this config belongs to a valid plugin
        if grep -q "^${basename}$" "$EXPORT_DIR/valid_plugins.txt"; then
            cp "$config" "$EXPORT_DIR/mod-configs/"
            echo "   - Exported config: $basename.json"
        else
            echo "   - Skipped config for non-existent plugin: $basename.json"
        fi
    done
    
    # Clean up temporary files
    rm -f "$EXPORT_DIR/valid_plugins.txt" "$EXPORT_DIR/loaded_plugins_raw.txt" "$EXPORT_DIR/loaded_plugins.txt"
fi

echo ""
echo "4. Exporting permissions..."
# Export default group permissions (the group that ALL players belong to)
echo "   - Fetching default group permissions..."

# The default group always exists in Oxide/uMod and contains all players
"$SCRIPT_DIR/rcon.sh" "oxide.show group default" > "$EXPORT_DIR/permissions/default_group_raw.txt" 2>&1
# Add a newline to ensure proper file termination
echo "" >> "$EXPORT_DIR/permissions/default_group_raw.txt"

# Extract permissions from the output
> "$EXPORT_DIR/permissions/default_permissions.txt"

# Check if we got a valid response
if [ -f "$EXPORT_DIR/permissions/default_group_raw.txt" ]; then
    # Debug: show first few lines of output
    echo "   - Raw output preview:"
    head -5 "$EXPORT_DIR/permissions/default_group_raw.txt" 2>/dev/null | sed 's/^/     /' || true
    
    # The output format should be something like:
    # Group 'default' players:
    # <list of players>
    # Group 'default' permissions:
    # permission1
    # permission2
    # ...
    
    # Extract permissions - they appear after "permissions:" line
    # The permissions might be in different formats:
    # 1. One per line
    # 2. Comma-separated on one line
    # 3. Space-separated
    
    # First, extract the permissions section
    awk '/[Gg]roup.*permissions:/,/^$/' "$EXPORT_DIR/permissions/default_group_raw.txt" | \
    grep -v "Group.*permissions:" | \
    grep -v "^$" > "$EXPORT_DIR/permissions/temp_perms.txt"
    
    # Now process the extracted section
    if [ -f "$EXPORT_DIR/permissions/temp_perms.txt" ] && [ -s "$EXPORT_DIR/permissions/temp_perms.txt" ]; then
        # Check if permissions are comma-separated (like in your output)
        if grep -q "," "$EXPORT_DIR/permissions/temp_perms.txt"; then
            # Split comma-separated permissions
            tr ',' '\n' < "$EXPORT_DIR/permissions/temp_perms.txt" | \
            sed 's/^[[:space:]]*//;s/[[:space:]]*$//' | \
            grep -E "^[a-zA-Z0-9]+\.[a-zA-Z0-9\.]+" > "$EXPORT_DIR/permissions/default_permissions.txt"
        else
            # Try space-separated or one per line
            tr ' ' '\n' < "$EXPORT_DIR/permissions/temp_perms.txt" | \
            sed 's/^[[:space:]]*//;s/[[:space:]]*$//' | \
            grep -E "^[a-zA-Z0-9]+\.[a-zA-Z0-9\.]+" > "$EXPORT_DIR/permissions/default_permissions.txt"
        fi
        rm -f "$EXPORT_DIR/permissions/temp_perms.txt"
    fi
fi

# Remove duplicates and sort
if [ -f "$EXPORT_DIR/permissions/default_permissions.txt" ]; then
    sort -u "$EXPORT_DIR/permissions/default_permissions.txt" -o "$EXPORT_DIR/permissions/default_permissions.txt" 2>/dev/null || true
fi

# Count permissions
PERM_COUNT=$(wc -l < "$EXPORT_DIR/permissions/default_permissions.txt" 2>/dev/null || echo "0")
echo "   - Found $PERM_COUNT permissions for default group"

# List the permissions for visibility
if [ "$PERM_COUNT" -gt 0 ]; then
    echo "   - Default group permissions:"
    cat "$EXPORT_DIR/permissions/default_permissions.txt" 2>/dev/null | sed 's/^/     - /' || true
else
    echo "   - No permissions found for default group"
    echo "   - This is normal if no permissions have been granted to all players"
    echo "   - Check $EXPORT_DIR/permissions/default_group_raw.txt for raw output"
fi

echo ""
echo "5. Exporting server configuration..."
# Copy rust-environment.sh
cp "$ROOT_DIR/rust-environment.sh" "$EXPORT_DIR/configs/rust-environment.sh"

# Ensure worldsize is set to 6000 in the exported config
sed -i 's/^export worldsize=.*/export worldsize=6000/' "$EXPORT_DIR/configs/rust-environment.sh" 2>/dev/null || \
sed -i '' 's/^export worldsize=.*/export worldsize=6000/' "$EXPORT_DIR/configs/rust-environment.sh" 2>/dev/null || \
echo "export worldsize=6000" >> "$EXPORT_DIR/configs/rust-environment.sh"

echo "   - Exported server configuration (worldsize set to 6000)"

# Export server settings from container if available
if [ -n "$CONTAINER_ID" ]; then
    echo "   - Exporting LinuxGSM configs..."
    docker exec "$CONTAINER_ID" bash -c "
        if [ -d /home/linuxgsm/lgsm/config-lgsm/rustserver ]; then
            tar -czf /tmp/lgsm_configs.tar.gz -C /home/linuxgsm/lgsm/config-lgsm rustserver
        fi
    " 2>/dev/null || true
    
    docker cp "$CONTAINER_ID:/tmp/lgsm_configs.tar.gz" "$EXPORT_DIR/configs/" 2>/dev/null || true
fi

echo ""
echo "6. Creating export summary..."
# Create export summary
cat > "$EXPORT_DIR/export_info.txt" << EOF
Rust Production Environment Export
==================================
Export Date: $(date)
Export Version: 1.0
Server Name: $(grep "export servername=" "$ROOT_DIR/rust-environment.sh" | cut -d'"' -f2 || echo "Unknown")

Contents:
- Non-admin plugins and configurations
- Default group permissions
- Server configuration (worldsize: 6000)
- LinuxGSM configurations

To import this package, use:
./admin/import_prod_env.sh $PACKAGE_FILE
EOF

echo ""
echo "7. Creating package..."
# Create the package
cd "$EXPORT_DIR"
tar -czf "$PACKAGE_FILE" .
cd - > /dev/null

# Cleanup
rm -rf "$EXPORT_DIR"

echo ""
echo "=== Export Complete ==="
echo "Package created: $PACKAGE_FILE"
echo "Size: $(du -h "$PACKAGE_FILE" | cut -f1)"
echo ""
echo "To import on another server, copy this file and run:"
echo "./admin/import_prod_env.sh $PACKAGE_FILE"