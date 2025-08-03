# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Dockerized Rust Dedicated Server using LinuxGSM for game server management. Provides complete solution for hosting Rust game servers with Oxide/uMod plugin support, custom maps, automated monitoring, and administrative tools.

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

# Execute RCON command
./admin/rcon-command.sh "command here"

# Interactive RCON shell
./admin/rcon.sh

# Connect via web: http://localhost:28016
# See admin/RCON_USAGE.md for detailed documentation
```

### Backup Operations
```bash
# Create backup
./admin/backup/create-backup.sh

# List backups
./admin/backup/list-backups.sh

# Restore backup
./admin/backup/restore-backup.sh ./backups/[backup-file].tgz

# Clean old backups (keep last 10)
./admin/backup/clean.sh
```

### Map Management
```bash
# Regenerate world map
./admin/regenerate-map.sh

# Custom maps: Place .map files in custom-maps/
# Access custom map server: http://localhost:8000
```

### Server Validation & Health
```bash
# Validate game files (stops/restarts server)
./admin/validate-server.sh

# Check validation status and crash history
./admin/validation-status.sh

# Export production environment (plugins, permissions, configs)
./admin/export_prod_env.sh

# Import production environment
./admin/import_prod_env.sh [package-file]
```

## Architecture Overview

### Container Architecture
- Single Docker container running LinuxGSM + Rust Server + Oxide
- Base image: `gameservermanagers/linuxgsm-docker`
- All server management through LinuxGSM commands inside container
- Runs as non-root user (linuxgsm, UID/GID 1000) after setup

### Key Scripts Flow
1. **docker-compose.yml**: Initializes container with complex startup sequence:
   - Installs dependencies (dos2unix, rsync, sudo, python3.8-venv, etc.)
   - Sets up Python virtual environment for RCON tools
   - Configures permissions and drops sudo after setup
   
2. **utils/custom-rust-server.sh**: Main server startup script that:
   - Installs/updates LinuxGSM and Rust server
   - Manages Oxide/uMod installation
   - Downloads RustEdit extension
   - Applies settings from rust-environment.sh
   - Checks for VALIDATION_NEEDED marker
   - Starts monitoring and launches server

3. **utils/get-or-update-plugins.sh**: Sophisticated plugin manager:
   - SHA256 verification to avoid unnecessary downloads
   - Rate limiting handling with exponential backoff
   - Extracts C# class names from plugin files
   - Syncs custom plugins and removes outdated ones

4. **utils/monitor-rust-server-enhanced.sh**: Advanced health monitoring with crash tracking:
   - Tracks crashes and triggers validation after 3 crashes in 24 hours
   - Logs crashes to `/home/linuxgsm/log/crash-tracker.log`
   - Creates validation marker file when threshold reached

### Directory Structure
```
├── admin/                # Administrative scripts
│   ├── backup/          # Backup management tools
│   ├── logs/            # Log management tools
│   └── *.sh             # Various admin scripts
├── custom-mods/         # Custom Oxide plugins (.cs files)
├── custom-maps/         # Custom map files (.map)
├── harmony-mods/        # Harmony framework mods
├── harmony-config/      # Harmony mod configurations
├── mod-configs/         # Plugin configurations
│   ├── plugins.txt      # List of uMod plugins to download
│   └── *.json           # Individual plugin configs
├── utils/               # Core scripts (read-only in container)
├── docker-compose.yml   # Container orchestration
├── rust-environment.sh  # Main server configuration
└── .env                 # Environment variable overrides
```

### Volume Mapping Strategy
- **Named volume `lgsm`**: Persistent server data (/home/linuxgsm)
- **Bind mounts**: Configuration and custom content
- **Read-only mounts**: utils/ directory to prevent modifications
- **Port mappings**: 28015/UDP (game), 28016/TCP (RCON), 8000/TCP (maps), 8888/TCP (WebRustPlus)

## Configuration

### Server Settings (rust-environment.sh)
Key variables:
- `maxplayers`: Player limit (default: 50)
- `servername`: Server display name
- `worldsize`: Map size 1000-6000 (default: 3500)
- `seed`: Map generation seed
- `GAMEMODE`: vanilla/hardcore/softcore (affects map visibility, Rust+, sleeping bags, tech tree costs)
- `ENABLE_RUST_EAC`: Easy Anti-Cheat toggle (disable for Linux clients)
- `uptime_monitoring`: Auto-restart on crash (default: enabled)
- `custom_map_url`: URL for custom map download
- `rconpassword`: Custom RCON password (auto-generated if empty)

### Environment Variable Overrides (.env file)
- `SERVERNAME`: Override server name
- `WORLDSIZE`: Override map size
- `SEED`: Force specific map seed

### Resource Limits (docker-compose.yml)
```yaml
cpu_count: 2        # CPU cores
mem_limit: 12gb     # Memory limit
```

### Network Configuration
- Game traffic: 28015/UDP
- RCON: 28016/TCP (localhost only by default)
- Custom maps HTTP: 8000/TCP
- WebRustPlus: 8888/TCP
- Health check: Port 28015 every 10s (15-min start period)

### Scheduled Tasks (Cron)
- Daily server restart: 6:00 AM
- Weekly validation: Sunday 4:00 AM
- Monthly map wipe: First Thursday 9:00 PM (with validation)

## Development Workflow

### Adding/Updating Plugins
1. **uMod plugins**: Add names to `mod-configs/plugins.txt`
2. Run update script: `./admin/get-or-update-oxide-plugins.sh`
3. Configure: Edit JSON files in `mod-configs/`
4. **Custom plugins**: Place .cs files in `custom-mods/`
5. Apply without restart: `./admin/reload-plugins.sh`

### Console Command Execution
Multiple methods available:
1. **RCON** - Returns output, works remotely
   ```bash
   ./admin/rcon-command.sh "oxide.version"
   ```
2. **Direct console** - Full access via tmux, no output capture
   ```bash
   ./admin/console-command.sh "oxide.reload *"
   ```
3. **Console with output** - Captures responses (timing-dependent)
   ```bash
   ./admin/console-command-with-output.sh "plugins"
   ```
4. **Interactive RCON** - Direct RCON shell access
   ```bash
   ./admin/rcon.sh
   # Or specific command: ./admin/rcon.sh "oxide.plugins"
   ```
5. **Docker RCON** - Alternative RCON methods
   ```bash
   ./admin/docker-rcon-command.sh "command"
   ./admin/docker-rcon-reload.sh  # Reload all plugins
   ```

### Debugging Issues
1. **Check logs**: 
   ```bash
   docker compose logs -f
   ./admin/shell.sh
   tail -f /home/linuxgsm/log/script/rustserver-script.log
   tail -f /home/linuxgsm/log/autoheal.log      # Monitor crashes
   tail -f /home/linuxgsm/log/crash-tracker.log # Crash timestamps
   ```
2. **LinuxGSM commands** (inside container):
   ```bash
   ./rustserver details
   ./rustserver monitor
   ./rustserver console
   ./rustserver validate  # Validate game files
   ```
3. **Plugin issues**: 
   ```bash
   ./admin/bugfix-oxide-plugins.sh  # Fix Linux compatibility
   ./admin/reload-plugins.sh         # Reload all plugins
   ./admin/get-or-update-oxide-plugins.sh  # Update/sync plugins
   ```
4. **Permission management**:
   ```bash
   ./admin/apply-permissions.sh      # Apply permissions from files
   ./admin/apply-permissions-fast.sh # Quick permission application
   # Configure delays via DELAY_OVERRIDE and VERIFY_DELAY_OVERRIDE env vars
   ```

## Important Implementation Details

### Plugin Management Internals
- Plugin names extracted from C# class definitions, not filenames
- SHA256 hashes stored in `/tmp/plugin_hashes/` to detect changes
- Rate limiting: Exponential backoff from 1s to 64s on 429 errors
- Custom plugins in `custom-mods/` override uMod versions

### Startup Sequence
1. Container starts with temporary sudo for setup
2. Installs dependencies and creates Python venv for RCON tools
3. Drops sudo privileges after setup
4. Runs custom-rust-server.sh to initialize server
5. Monitors server health with enhanced crash tracking
6. Auto-validates after 3 crashes in 24 hours
7. Checks for VALIDATION_NEEDED marker on restart

### Validation Triggers
- Manual: `./admin/validate-server.sh`
- Automatic: After 3 crashes within 24 hours
- Scheduled: Sunday 4:00 AM (weekly)
- Monthly: First Thursday 9:00 PM (with map wipe)

### Security Considerations
- Container runs as non-root user (linuxgsm)
- RCON bound to localhost by default
- Utils mounted read-only to prevent tampering
- Temporary sudo removed after initialization

### LinuxGSM Integration
- Server configs in: `/home/linuxgsm/lgsm/config-lgsm/rustserver/`
- Game files in: `/home/linuxgsm/serverfiles/`
- Logs in: `/home/linuxgsm/log/`
- All server control through LinuxGSM commands

### Environment Export/Import
The server supports exporting and importing production environments:
- **Export**: Captures non-admin plugins, permissions, and configurations
- **Import**: Applies exported settings to a new or existing server
- Admin plugins (Godmode, Vanish) are excluded from exports for security
- Useful for migrating settings between development and production servers

### Current Custom Mods
- BetterTC - Enhanced Tool Cupboard management
- Bradley - Bradley APC modifications
- ChaosNPCDownloader - NPC management system
- RaidProtection - Raid protection mechanics
- SingularityStorage - Advanced storage solutions
- WebRustPlusWorking - Rust+ web interface
- ZombieHorde - Zombie spawning system