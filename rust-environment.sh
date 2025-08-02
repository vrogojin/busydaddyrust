######################
# SERVER BOOT SETTINGS
######################

# settings take effect every time the server boots

maxplayers=50
# This value can be overridden by setting SERVERNAME in .env file
servername="${SERVERNAME:-BusyDaddyRust-dev}"

# uncomment this to enable EAC, for Linux clients this must be commented out
ENABLE_RUST_EAC=1

#######################
# GENERATED MAP SUPPORT
#######################

# range: 1-2147483647, used to reproduce a procedural map.
# default: random seed
#     If you change this value, then a new map will be generated on next boot.
#     The old map will still persist unless `./admin-actions/regenerate-map.sh`
#     is called which deletes all maps
# This value can be overridden by setting SEED in .env file
if [ -n "${SEED}" ]; then
    seed="${SEED}"
else
    # If no seed is specified, let Rust use existing world seed or generate random
    unset seed
fi

# range: unknown, used to recover a known setting from an existing map.
#salt=

# default: 3000, range: 1000-6000, map size in meters.
# This value can be overridden by setting WORLDSIZE in .env file
worldsize=${WORLDSIZE:-3500}

####################
# CUSTOM MAP SUPPORT
####################
# If using a custom map, then generated map settings are ignored.

# When self-hosting a map for multiplayer, MAP_BASE_URL is for providing public
# IP address in the URL where clients will connect to download your map.
#MAP_BASE_URL=http://localhost:8000/

# CUSTOM_MAP_URL is for posting a link to a publicly available custom map such
# as a Dropbox download link.
#    Overrides MAP_BASE_URL unless SELF_HOSTING_CUSTOM_MAP=true
#CUSTOM_MAP_URL=https://example.com/some-map.map

# Download the CUSTOM_MAP_URL for self hosting.  Set to true to force
# self-hosting.  The map will be downloaded to the custom-maps/ directory.
#    Overrides CUSTOM_MAP_URL
#SELF_HOST_CUSTOM_MAP=false

###################
# ADVANCED SETTINGS
###################

# Rust server will be monitored.  If the server is down, then the container
# will be killed and automatically restarted by docker-compose.  If you do not
# want the container to die when rust shuts down or crashes, then disable this
# monitoring.
uptime_monitoring=true

# Game mode setting. Options: vanilla, hardcore, softcore
# Leave empty or comment out for default (vanilla) mode
# Note: hardcore mode disables map, Rust+, limits sleeping bags to 5, 
# makes tech tree more expensive, and enables local chat only (100m radius)
GAMEMODE=hardcore
