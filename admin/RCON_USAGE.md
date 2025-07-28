# Scripts that use rcon.sh

This document lists all scripts in the admin directory that depend on `rcon.sh`.

## Production Scripts

### 1. **export_prod_env.sh**
- Gets list of loaded plugins: `oxide.plugins`
- Exports default group permissions: `oxide.show group default`

### 2. **import_prod_env.sh**
- Creates default group: `oxide.group add default`
- Grants permissions: `oxide.grant group default <permission>`
- Verifies permissions: `oxide.show group default`
- Reloads all plugins: `oxide.reload *`

## Debug/Testing Scripts

### 3. **debug-permissions.sh**
- Tests various permission commands for debugging
- Commands: `oxide.show groups`, `oxide.show group default`, `oxide.show perms`, etc.

### 4. **check-permission-groups.sh**
- Checks permission groups and tests grant/revoke
- Commands: `oxide.show groups`, `oxide.grant`, `oxide.revoke`

### 5. **test-permissions.sh**
- Comprehensive permission system testing
- Tests both full and short command versions (oxide.* and o.*)

## Common RCON Commands Used

1. **Plugin Management:**
   - `oxide.reload *` - Reload all plugins
   - `oxide.plugins` - List loaded plugins

2. **Permission Management:**
   - `oxide.show groups` - Show all groups
   - `oxide.show group default` - Show default group info
   - `oxide.grant group default <permission>` - Grant permission to all players
   - `oxide.revoke group default <permission>` - Revoke permission from all players
   - `oxide.show perms` - Show all available permissions

3. **User Management:**
   - `oxide.usergroup add <player> <group>` - Add player to group
   - `oxide.usergroup remove <player> <group>` - Remove player from group

## Direct Usage

You can also use `rcon.sh` directly:

```bash
./admin/rcon.sh "status"
./admin/rcon.sh "oxide.reload *"
./admin/rcon.sh "oxide.grant group default singularitystorage.use"
```

## Dependencies

The `rcon.sh` script requires:
- Docker compose running with the lgsm container
- The server must be running for RCON commands to work
- Uses the auto-generated or configured RCON password