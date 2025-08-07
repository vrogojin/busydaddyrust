#!/bin/bash

# Setup cron jobs for Rust server maintenance
echo "Setting up cron jobs for Rust server..."

# Create cron entries
cat > /tmp/rustserver-cron <<EOF
# Daily restart at 6:00 AM
0 6 * * * /home/linuxgsm/rustserver restart > /home/linuxgsm/log/cron-restart.log 2>&1

# Weekly validation on Sunday at 4:00 AM
0 4 * * 0 /home/linuxgsm/rustserver validate > /home/linuxgsm/log/cron-validate.log 2>&1

# Monthly wipe on first Thursday at 7:00 PM UTC (matches official Rust wipe time)
0 19 1-7 * 4 /utils/monthly-wipe.sh > /home/linuxgsm/log/cron-wipe.log 2>&1

# Health check every 5 minutes
*/5 * * * * pgrep RustDedicated > /dev/null || /home/linuxgsm/rustserver start > /home/linuxgsm/log/cron-healthcheck.log 2>&1
EOF

# Install crontab for linuxgsm user
crontab -u linuxgsm /tmp/rustserver-cron

# Start cron service
service cron start

echo "Cron jobs installed successfully"
crontab -u linuxgsm -l