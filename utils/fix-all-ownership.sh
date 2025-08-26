#!/bin/bash
# Comprehensive ownership fix for all Rust server files
# This prevents LinuxGSM ownership check failures

echo "[$(date)] Starting comprehensive ownership fix..."

# Base directories
chown -R linuxgsm:linuxgsm /home/linuxgsm

# Server files
if [ -d /home/linuxgsm/serverfiles ]; then
    chown -R linuxgsm:linuxgsm /home/linuxgsm/serverfiles
fi

# Oxide specific directories
if [ -d /home/linuxgsm/serverfiles/oxide ]; then
    chown -R linuxgsm:linuxgsm /home/linuxgsm/serverfiles/oxide
    
    # Plugins directory - most common source of ownership issues
    if [ -d /home/linuxgsm/serverfiles/oxide/plugins ]; then
        chown -R linuxgsm:linuxgsm /home/linuxgsm/serverfiles/oxide/plugins
        echo "[$(date)] Fixed ownership of plugin files"
    fi
    
    # Config directory
    if [ -d /home/linuxgsm/serverfiles/oxide/config ]; then
        chown -R linuxgsm:linuxgsm /home/linuxgsm/serverfiles/oxide/config
    fi
    
    # Data directory
    if [ -d /home/linuxgsm/serverfiles/oxide/data ]; then
        chown -R linuxgsm:linuxgsm /home/linuxgsm/serverfiles/oxide/data
    fi
    
    # Logs directory
    if [ -d /home/linuxgsm/serverfiles/oxide/logs ]; then
        chown -R linuxgsm:linuxgsm /home/linuxgsm/serverfiles/oxide/logs
    fi
fi

# LinuxGSM directories
if [ -d /home/linuxgsm/lgsm ]; then
    chown -R linuxgsm:linuxgsm /home/linuxgsm/lgsm
fi

# Log directories
if [ -d /home/linuxgsm/log ]; then
    chown -R linuxgsm:linuxgsm /home/linuxgsm/log
fi

# Custom maps directory
if [ -d /custom-maps ]; then
    chown -R linuxgsm:linuxgsm /custom-maps
fi

# Python venv
if [ -d /home/linuxgsm/.venv ]; then
    chown -R linuxgsm:linuxgsm /home/linuxgsm/.venv
fi

echo "[$(date)] Ownership fix completed"