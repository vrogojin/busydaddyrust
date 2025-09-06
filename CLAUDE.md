# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Dockerized Rust Dedicated Server using LinuxGSM for game server management. Single-container architecture with Oxide/uMod plugin support, automated crash recovery, and comprehensive administrative tooling.

## Essential Commands

### Server Operations
```bash
# Start server
docker compose up -d

# Stop server  
docker compose down

# View logs
docker compose logs -f

# Restart server (rebuilds Docker image)
./admin/restart_server.sh

# Access container shell
./admin/shell.sh           # as linuxgsm user
./admin/shell.sh root       # as root user
```

### Plugin Management
```bash
# Update/install Oxide plugins (reads from mod-configs/plugins.txt)
./admin/get-or-update-oxide-plugins.sh

# Fix plugin Linux compatibility issues
./admin/bugfix-oxide-plugins.sh

# Reload plugins without restart
./admin/reload-plugins.sh

# Custom plugins: Place .cs files in custom-mods/
# Plugin configs: Edit JSON files in mod-configs/
```

### RCON Access
```bash
# Get RCON password
./admin/get-rcon-pass.sh

# Execute RCON command (if rcon-command.sh exists)
./admin/rcon-command.sh "command here"

# Interactive RCON shell
./admin/rcon.sh

# Connect via web: http://localhost:28016
```

### Backup Operations
```bash
# Create backup
./admin/backup/create-backup.sh

# List backups
./admin/backup/list-backups.sh

# Restore backup
./admin/backup/restore-backup.sh ./backups/[backup-file].tgz
```

### Server Validation & Health
```bash
# Validate game files (stops/restarts server)
./admin/validate-server.sh

# Check validation status and crash history
./admin/validation-status.sh

# Export production environment
./admin/export_prod_env.sh

# Import production environment
./admin/import_prod_env.sh [package-file]
```

### Git Operations
```bash
# Check changes
git status
git diff

# Stage and commit changes
git add -A
git commit -m "message"

# Push to remote
git push origin main
```

## Architecture Overview

### Container Initialization Flow
1. Docker Compose starts with root for setup (installs packages, creates Python venv)
2. Configures linuxgsm user (UID/GID 1000), grants temporary sudo
3. Runs `utils/custom-rust-server.sh` which:
   - Installs/updates LinuxGSM and Rust server
   - Manages Oxide/uMod installation
   - Checks for VALIDATION_NEEDED marker
   - Starts enhanced monitoring
4. Drops sudo privileges after initialization
5. Launches server with crash tracking

### Key Scripts and Their Interactions

**utils/custom-rust-server.sh** → Main orchestrator
- Downloads LinuxGSM → Installs Rust via Steam → Sets up Oxide
- Sources rust-environment.sh for configuration
- Checks validation markers before starting
- Launches monitor-rust-server-enhanced.sh

**utils/monitor-rust-server-enhanced.sh** → Health monitoring
- Tracks crashes to `/home/linuxgsm/log/crash-tracker.log`
- Auto-validates after 3 crashes in 24 hours
- Creates VALIDATION_NEEDED marker when threshold reached
- Implements graceful shutdown with fallback to SIGKILL

**utils/get-or-update-plugins.sh** → Plugin management
- SHA256 verification to detect changes
- Rate limiting with exponential backoff (1s→64s)
- Extracts C# class names from plugin source
- Syncs custom-mods/ overriding uMod versions
- Removes outdated plugins automatically

**utils/startup-wrapper.sh** → Process wrapper
- Ensures proper signal handling for graceful shutdown
- Manages startup sequence and environment

**utils/fix-all-ownership.sh** → Permission fixer
- Corrects file ownership for linuxgsm user
- Ensures proper permissions for server operation

### Volume and Port Strategy
- **Named volume `lgsm`**: Persistent server data
- **Bind mounts**: Configuration and custom content
- **Read-only**: utils/ directory (security)
- **Ports**: 
  - 28015/UDP: Game traffic
  - 28016/TCP: RCON
  - 8000/TCP: Map downloads
  - 8888/TCP: WebRustPlus interface

### Configuration Hierarchy
1. `.env` file → Override key variables
2. `docker-compose.yml` → Container settings
3. `rust-environment.sh` → Primary server config
4. LinuxGSM configs → Generated in container

## Development Workflow

### Adding Plugins
1. **uMod plugins**: Add to `mod-configs/plugins.txt`
2. Run: `./admin/get-or-update-oxide-plugins.sh`
3. **Custom plugins**: Place in `custom-mods/` (overrides uMod)
4. Configure: Edit JSONs in `mod-configs/`
5. Apply: `./admin/reload-plugins.sh`

### Debugging Issues
```bash
# Check logs
docker compose logs -f
tail -f /home/linuxgsm/log/script/rustserver-script.log
tail -f /home/linuxgsm/log/crash-tracker.log

# Inside container
./rustserver details
./rustserver monitor
./rustserver validate

# Fix plugin issues
./admin/bugfix-oxide-plugins.sh
```

### RCON Command Execution
```bash
# RCON (returns output, works remotely)
./admin/rcon.sh
# Then enter commands interactively

# Direct console access requires entering container
./admin/shell.sh
./rustserver console
```

## Important Implementation Details

### Plugin System
- Plugins identified by C# class name, not filename
- SHA256 hashes in `/tmp/plugin_hashes/` detect changes
- Custom plugins in `custom-mods/` override uMod versions
- Rate limiting handles 429 errors with backoff

### Crash Recovery System
- Monitor checks process every 10 seconds
- Logs crashes with timestamps
- Counts crashes within 24-hour window
- Creates VALIDATION_NEEDED marker after 3 crashes
- Next restart triggers automatic validation

### Validation Triggers
- Manual: `./admin/validate-server.sh`
- Automatic: After 3 crashes in 24 hours
- Scheduled: Weekly (Sunday 4 AM) via cron
- Monthly: First Thursday with map wipe

### Security Model
- Container runs as non-root (linuxgsm user)
- RCON bound to localhost by default
- Utils mounted read-only
- Temporary sudo removed after setup
- Admin plugins excluded from production exports

### LinuxGSM Integration Points
- Server configs: `/home/linuxgsm/lgsm/config-lgsm/rustserver/`
- Game files: `/home/linuxgsm/serverfiles/`
- Logs: `/home/linuxgsm/log/`
- All control through LinuxGSM commands (./rustserver)

### RCON Implementation Details
- Internal tool: `rcon-command` installed via Dockerfile (if available)
- WebSocket-based client implementation
- Password auto-discovery from process arguments
- Alias shortcuts: `reload` → `oxide.reload *`

### Backup System Architecture
- Includes: lgsm/, serverfiles/server/, serverfiles/oxide/
- Format: Timestamped .tgz with date format
- Error handling with trap cleanup

### Production Export/Import
- Filters admin plugins (Godmode, Vanish)
- Exports default group permissions via RCON
- Validates plugin configs before export
- Package structure: plugins, configs, permissions

## Configuration Reference

### rust-environment.sh Key Variables
- `maxplayers`: Player limit (default: 50)
- `worldsize`: Map size 1000-6000 (default: 3500)
- `seed`: Map generation seed
- `GAMEMODE`: vanilla/hardcore/softcore (hardcore enabled)
- `ENABLE_RUST_EAC`: EAC toggle (disable for Linux clients)
- `uptime_monitoring`: Auto-restart on crash
- `custom_map_url`: Custom map download URL
- `rconpassword`: Auto-generated if empty

### Docker Resource Configuration
```yaml
cpu_count: 2
mem_limit: 12gb
```

### Environment Variables (.env)
- `SERVERNAME`: Override server name
- `WORLDSIZE`: Override map size
- Variables propagate through Docker stages

### Scheduled Tasks (HOST SYSTEM CRON - BST/GMT timezone)
- Daily restart: 6:00 AM local time (via host cron, rebuilds container)
- Weekly validation: Sunday 4:00 AM UTC (via container cron)
- Monthly wipe: First Thursday at EXACTLY 19:00 UTC (7:00 PM GMT / 2:00 PM EST)
  - Synchronized with official Facepunch/Rust PC wipe time
  - Auto-adjusts for daylight saving (runs at 19:00 GMT in winter, 20:00 BST in summer)
  - Both times = 19:00 UTC exactly
  - Container monthly wipe DISABLED to prevent double-wipe conflicts

## Directory Structure
- `admin/` - Host-side management scripts
- `utils/` - Container-side scripts (read-only mount)
- `custom-mods/` - Custom C# plugins (override uMod)
- `mod-configs/` - Plugin configurations and manifest
- `custom-maps/` - Self-hosted map files
- `harmony-mods/` - Additional mods for custom maps
- `backups/` - Server backup storage
- `admin/logs/` - Validation and tracking logs