#!/bin/bash

# Setup cron jobs for Rust server maintenance
echo "Setting up cron jobs for Rust server..."

# Create cron entries
cat > /tmp/rustserver-cron <<EOF
# Daily restart at 6:00 AM - DISABLED to prevent automatic reboots
# 0 6 * * * /home/linuxgsm/rustserver restart > /home/linuxgsm/log/cron-restart.log 2>&1

# Weekly validation on Sunday at 4:00 AM
0 4 * * 0 /home/linuxgsm/rustserver validate > /home/linuxgsm/log/cron-validate.log 2>&1

# Monthly wipe - DISABLED to prevent conflict with host cron job
# Host system handles monthly wipe at 9:00 PM BST (8:00 PM GMT / 20:00 UTC)
# which is 1-2 hours after official Rust PC wipe time of 7:00 PM GMT / 19:00 UTC
# The host wipe runs: /admin/regenerate-map.sh on first Thursday at 21:00 BST
# 0 19 1-7 * 4 /utils/monthly-wipe.sh > /home/linuxgsm/log/cron-wipe.log 2>&1

# Health check every 5 minutes - DISABLED to prevent automatic restarts
# Crash detection and recovery is handled by monitor-rust-server-enhanced.sh instead
# */5 * * * * pgrep RustDedicated > /dev/null || /home/linuxgsm/rustserver start > /home/linuxgsm/log/cron-healthcheck.log 2>&1
EOF

# Install crontab for linuxgsm user
crontab -u linuxgsm /tmp/rustserver-cron

# Start cron service
service cron start

echo "Cron jobs installed successfully"
crontab -u linuxgsm -l