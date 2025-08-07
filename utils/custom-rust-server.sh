#!/bin/bash

# DESCRIPTION:
#   Dedicated server LGSM startup script which initializes some security best
#   practices for Rust.

set -ex

# Export Docker environment variables for child processes
export SERVERNAME="${SERVERNAME}"
export WORLDSIZE="${WORLDSIZE}"
export PRODUCTION="${PRODUCTION:-false}"

[ -f ./linuxgsm.sh ] || (
  if [ -n "${LINUX_GSM_VERSION:-}" ]; then
    curl -fLo linuxgsm.sh \
      https://raw.githubusercontent.com/GameServerManagers/LinuxGSM/"${LINUX_GSM_VERSION}"/linuxgsm.sh
    chmod 755 linuxgsm.sh
  else
    cp /linuxgsm.sh ./
  fi
)
[ -x ./rustserver ] || ./linuxgsm.sh rustserver
yes Y | ./rustserver install
if ! grep rustoxide lgsm/mods/installed-mods.txt &> /dev/null; then
  ./rustserver mods-install <<< $'rustoxide\nyes\n'
fi
./rustserver mods-update
if [ ! -f 'serverfiles/RustDedicated_Data/Managed/Oxide.Ext.RustEdit.dll' ]; then
  curl -fLo serverfiles/RustDedicated_Data/Managed/Oxide.Ext.RustEdit.dll \
    https://github.com/k1lly0u/Oxide.Ext.RustEdit/raw/master/Oxide.Ext.RustEdit.dll
fi

# remove passwordless sudo access since setup is complete
sudo rm -f /etc/sudoers.d/lgsm

lgsm_cfg=lgsm/config-lgsm/rustserver/rustserver.cfg

# Generate server.cfg based on production/dev mode FIRST (before apply-settings)
if [ -f /server.cfg.template ]; then
  mkdir -p serverfiles/server/rustserver/cfg
  
  if [ "${PRODUCTION}" = "true" ]; then
    echo "Configuring server for PRODUCTION mode (public listing)"
    HOSTNAME="BusyDaddyRust"
    LISTING_CONTROL=""
  else
    echo "Configuring server for DEVELOPMENT mode (hidden from public)"
    HOSTNAME="BusyDaddyRust-dev"
    LISTING_CONTROL="# DEVELOPMENT SERVER - Hidden from public lists\nserver.official false\nserver.stability false"
  fi
  
  # Get server IP for logo URL
  SERVER_IP=$(hostname -I | awk '{print $1}')
  if [ -z "$SERVER_IP" ]; then
    SERVER_IP="busydaddyrust.com"
  fi
  
  # Generate server.cfg from template
  sed -e "s/{{HOSTNAME}}/${HOSTNAME}/g" \
      -e "s|{{LISTING_CONTROL}}|${LISTING_CONTROL}|g" \
      -e "s/{{SERVER_IP}}/${SERVER_IP}/g" \
      /server.cfg.template > serverfiles/server/rustserver/cfg/server.cfg
  
  echo "server.cfg generated for ${PRODUCTION} mode"
elif [ -f /server.cfg ]; then
  # Fallback to direct copy if no template
  mkdir -p serverfiles/server/rustserver/cfg
  cp /server.cfg serverfiles/server/rustserver/cfg/server.cfg
  echo "Custom server.cfg copied to game directory"
fi

# Remove old apply-settings line and add new one with environment variables
sed -i '/apply-settings.sh/d' "$lgsm_cfg" 2>/dev/null || true
echo 'if [ ! "$1" = docker ]; then SERVERNAME="'"${SERVERNAME}"'" WORLDSIZE="'"${WORLDSIZE}"'" /utils/apply-settings.sh; source lgsm/config-lgsm/rustserver/rustserver.cfg docker; fi' >> "$lgsm_cfg"

/utils/get-or-update-plugins.sh

# Copy logo for WebRustPlus to serve
/utils/copy-logo.sh

# Check if validation is needed (marker from crash detection)
if [ -f /home/linuxgsm/VALIDATION_NEEDED ]; then
  echo "Validation marker found - running game file validation before start"
  ./rustserver validate
  rm -f /home/linuxgsm/VALIDATION_NEEDED
fi

# Use enhanced monitor with crash tracking
/utils/monitor-rust-server-enhanced.sh &

# start rust server
./rustserver start
echo Sleeping for 30 seconds...
sleep 30
tail -f log/console/rustserver-console.log \
        log/script/rustserver-steamcmd.log \
        log/script/rustserver-script.log
