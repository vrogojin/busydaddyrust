#!/bin/bash
# Startup wrapper that performs ownership fixes only when needed

# Function to check if ownership fix is needed
needs_ownership_fix() {
    # Check key directories
    if [ ! "$(stat -c '%U' /home/linuxgsm)" = linuxgsm ] ||
       [ ! "$(stat -c '%U' /custom-maps)" = linuxgsm ] ||
       [ ! "$(stat -c '%U' /home/linuxgsm/.venv 2>/dev/null)" = linuxgsm ]; then
        return 0  # True, needs fix
    fi
    return 1  # False, doesn't need fix
}

# Only run ownership fixes if needed
if needs_ownership_fix; then
    echo "[$(date)] Running ownership fixes..."
    
    # Fix UID/GID if needed
    lgsm_uid="$(id -u linuxgsm)"
    lgsm_gid="$(id -g linuxgsm)"
    if [ ! "$lgsm_uid" = 1000 ]; then
        sed -i "s/:$lgsm_uid:$lgsm_gid:/:1000:1000:/" /etc/passwd
        sed -i "s/:$lgsm_gid:/:1000:/" /etc/group
    fi
    
    # Fix ownership of key directories
    chown -R linuxgsm: /home/linuxgsm
    chown -R linuxgsm: /custom-maps
    
    # Setup Python venv if needed
    if [ ! -d /home/linuxgsm/.venv ]; then
        python3 -m venv /home/linuxgsm/.venv
        chown -R linuxgsm: /home/linuxgsm/.venv
    fi
    
    # Fix oxide directories
    chown linuxgsm: /home/linuxgsm /home/linuxgsm/serverfiles /home/linuxgsm/serverfiles/oxide
    chown -R linuxgsm: /home/linuxgsm/serverfiles/oxide/config
    
    # Fix oxide data directory if it exists
    if [ -d /home/linuxgsm/serverfiles/oxide/data ]; then
        chown -R linuxgsm: /home/linuxgsm/serverfiles/oxide/data
    fi
    
    echo "[$(date)] Ownership fixes completed"
else
    echo "[$(date)] Ownership is correct, skipping fixes"
fi

# Ensure bash profile has venv activation
grep -F .venv ~linuxgsm/.bash_profile 2>/dev/null || echo 'source /home/linuxgsm/.venv/bin/activate' > ~linuxgsm/.bash_profile
grep -F .venv ~linuxgsm/.bashrc 2>/dev/null || echo 'source /home/linuxgsm/.venv/bin/activate' > ~linuxgsm/.bashrc

# Clean up old linuxgsm.sh
rm -f ~linuxgsm/linuxgsm.sh

# Pass through to the actual server startup
exec su - linuxgsm -c "LINUX_GSM_VERSION=\"${LINUX_GSM_VERSION:-v20.4.1}\" SERVERNAME=\"${SERVERNAME}\" WORLDSIZE=\"${WORLDSIZE}\" SEED=\"${SEED}\" PRODUCTION=\"${PRODUCTION}\" /utils/custom-rust-server.sh"