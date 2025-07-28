# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a Dockerized Rust Dedicated Server using Linux GSM (LinuxGSM) for game server management. It provides a complete solution for hosting Rust game servers with Oxide/uMod plugin support, custom maps, and administrative tools.

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

## Architecture Overview

### Container Architecture
- Single Docker container running LinuxGSM + Rust Server + Oxide
- Base image: `gameservermanagers/linuxgsm-docker`
- All server management through LinuxGSM commands inside container
- Runs as non-root user (linuxgsm, UID/GID 1000) after setup

### Key Scripts Flow
1. **docker-compose.yml**: Initializes container with complex startup sequence:
   - Installs dependencies (dos2unix, rsync, sudo, etc.)
   - Sets up Python virtual environment for RCON tools
   - Configures permissions and drops sudo after setup
   
2. **utils/custom-rust-server.sh**: Main server startup script that:
   - Installs/updates LinuxGSM and Rust server
   - Manages Oxide/uMod installation
   - Downloads RustEdit extension
   - Applies settings from rust-environment.sh
   - Starts monitoring and launches server

3. **utils/get-or-update-plugins.sh**: Sophisticated plugin manager:
   - SHA256 verification to avoid unnecessary downloads
   - Rate limiting handling with exponential backoff
   - Extracts C# class names from plugin files
   - Syncs custom plugins and removes outdated ones

4. **utils/monitor-rust-server.sh**: Health monitoring that triggers container restart on crash

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
└── rust-environment.sh  # Main server configuration
```

### Volume Mapping Strategy
- **Named volume `lgsm`**: Persistent server data (/home/linuxgsm)
- **Bind mounts**: Configuration and custom content
- **Read-only mounts**: utils/ directory to prevent modifications
- **Port mappings**: 28015/UDP (game), 28016/TCP (RCON), 8000/TCP (maps)

## Configuration

### Server Settings (rust-environment.sh)
Key variables:
- `maxplayers`: Player limit (default: 10)
- `servername`: Server display name
- `worldsize`: Map size 1000-6000 (default: 3000)
- `seed`: Map generation seed
- `ENABLE_RUST_EAC`: Easy Anti-Cheat toggle (disable for Linux clients)
- `uptime_monitoring`: Auto-restart on crash (default: enabled)
- `custom_map_url`: URL for custom map download
- `rconpassword`: Custom RCON password (auto-generated if empty)

### Resource Limits (docker-compose.yml)
```yaml
cpu_count: 2        # CPU cores
mem_limit: 12gb     # Memory limit
```

### Network Configuration
- Game traffic: 28015/UDP
- RCON: 28016/TCP (localhost only by default)
- Custom maps HTTP: 8000/TCP
- Health check: Port 28015 every 60s

## Development Workflow

### Adding/Updating Plugins
1. **uMod plugins**: Add names to `mod-configs/plugins.txt`
2. Run update script: `./admin/get-or-update-oxide-plugins.sh`
3. Configure: Edit JSON files in `mod-configs/`
4. **Custom plugins**: Place .cs files in `custom-mods/`
5. Apply without restart: `./admin/reload-plugins.sh`

### Console Command Execution
Three methods available:
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

### Debugging Issues
1. **Check logs**: 
   ```bash
   docker compose logs -f
   ./admin/shell.sh
   tail -f /home/linuxgsm/log/script/rustserver-script.log
   ```
2. **LinuxGSM commands** (inside container):
   ```bash
   ./rustserver details
   ./rustserver monitor
   ./rustserver console
   ```
3. **Plugin issues**: 
   ```bash
   ./admin/bugfix-oxide-plugins.sh  # Fix Linux compatibility
   ./admin/reload-plugins.sh         # Reload all plugins
   ```

## Important Implementation Details

### Plugin Management Internals
- Plugin names extracted from C# class definitions, not filenames
- SHA256 hashes stored in `/tmp/plugin_hashes/` to detect changes
- Rate limiting: Exponential backoff from 1s to 64s on 429 errors
- Custom plugins in `custom-mods/` override uMod versions

### Startup Sequence
1. Container starts with temporary sudo for setup
2. Installs dependencies and creates Python venv
3. Drops sudo privileges after setup
4. Runs custom-rust-server.sh to initialize server
5. Monitors server health and auto-restarts on crash

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