#!/bin/bash

# Setup host system cron jobs for Rust server maintenance
# This script configures daily restarts and monthly wipes synchronized with official Rust/Facepunch schedule

set -e

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${GREEN}=== Rust Server Host Cron Setup ===${NC}"
echo ""

# Get the absolute path to the admin directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

echo -e "${YELLOW}Project root:${NC} $PROJECT_ROOT"
echo ""

# Check if required scripts exist
RESTART_SCRIPT="$PROJECT_ROOT/admin/restart_server.sh"
WIPE_SCRIPT="$PROJECT_ROOT/admin/regenerate-map.sh"

if [ ! -f "$RESTART_SCRIPT" ]; then
    echo -e "${RED}Error: restart_server.sh not found at $RESTART_SCRIPT${NC}"
    echo "Please ensure you're running this from the correct project directory."
    exit 1
fi

if [ ! -f "$WIPE_SCRIPT" ]; then
    echo -e "${RED}Error: regenerate-map.sh not found at $WIPE_SCRIPT${NC}"
    echo "Please ensure you're running this from the correct project directory."
    exit 1
fi

# Make scripts executable
chmod +x "$RESTART_SCRIPT"
chmod +x "$WIPE_SCRIPT"

echo -e "${GREEN}✓${NC} Found required scripts"
echo ""

# Backup existing crontab
BACKUP_FILE="/tmp/crontab_backup_$(date +%Y%m%d_%H%M%S).txt"
crontab -l > "$BACKUP_FILE" 2>/dev/null || true
if [ -s "$BACKUP_FILE" ]; then
    echo -e "${YELLOW}Backed up existing crontab to:${NC} $BACKUP_FILE"
else
    echo "No existing crontab found (this is normal for new installations)"
fi
echo ""

# Create new cron entries
echo -e "${YELLOW}Setting up cron jobs...${NC}"
echo ""

# Check if cron entries already exist
EXISTING_RESTART=$(crontab -l 2>/dev/null | grep -c "$RESTART_SCRIPT" || true)
EXISTING_WIPE=$(crontab -l 2>/dev/null | grep -c "$WIPE_SCRIPT" || true)

if [ "$EXISTING_RESTART" -gt 0 ] || [ "$EXISTING_WIPE" -gt 0 ]; then
    echo -e "${YELLOW}Warning: Found existing Rust server cron jobs${NC}"
    echo "Do you want to remove them and set up fresh? (y/N)"
    read -r response
    if [[ "$response" =~ ^[Yy]$ ]]; then
        # Remove existing Rust-related cron jobs
        crontab -l 2>/dev/null | grep -v "$RESTART_SCRIPT" | grep -v "$WIPE_SCRIPT" | crontab - 2>/dev/null || true
        echo -e "${GREEN}✓${NC} Removed existing Rust server cron jobs"
    else
        echo "Keeping existing cron jobs. Exiting."
        exit 0
    fi
fi

# Create temporary cron file with existing jobs (excluding our scripts)
crontab -l 2>/dev/null | grep -v "$RESTART_SCRIPT" | grep -v "$WIPE_SCRIPT" > /tmp/new_cron 2>/dev/null || true

# Add our cron jobs
cat >> /tmp/new_cron << EOF

# === Rust Server Maintenance (added by setup-host-cron.sh) ===

# Daily restart at 6:00 AM local time
# Rebuilds Docker container and restarts server
0 6 * * * $RESTART_SCRIPT >> $PROJECT_ROOT/admin/logs/daily_restart.log 2>&1

# Official Rust monthly wipe: First Thursday at 19:00 UTC (7PM GMT / 2PM EST)
# IMPORTANT: This runs at exact UTC time regardless of local timezone
# The script checks UTC hour to ensure it runs at the official wipe time
# Winter (GMT): 19:00 local = 19:00 UTC
# Summer (BST): 20:00 local = 19:00 UTC
0 19,20 1-7 * 4 [ \$(date -u +\\%H) -eq 19 ] && [ \$(date +\\%d) -le 7 ] && $WIPE_SCRIPT skip-prompt >> $PROJECT_ROOT/admin/logs/monthly_wipe.log 2>&1

# === End Rust Server Maintenance ===
EOF

# Install the new crontab
crontab /tmp/new_cron
rm /tmp/new_cron

echo -e "${GREEN}✓${NC} Cron jobs installed successfully!"
echo ""

# Create logs directory if it doesn't exist
mkdir -p "$PROJECT_ROOT/admin/logs"
echo -e "${GREEN}✓${NC} Created logs directory at $PROJECT_ROOT/admin/logs"
echo ""

# Display the installed cron jobs
echo -e "${YELLOW}Installed cron jobs:${NC}"
echo "----------------------------------------"
crontab -l | grep -A 10 "=== Rust Server Maintenance" | grep -B 10 "=== End Rust Server Maintenance" || true
echo "----------------------------------------"
echo ""

# Show current time info
echo -e "${YELLOW}Current time information:${NC}"
echo "Local time: $(date)"
echo "UTC time:   $(date -u)"
echo "Timezone:   $(timedatectl 2>/dev/null | grep "Time zone" | awk '{print $3, $4, $5}' || echo "$TZ")"
echo ""

# Calculate next run times
echo -e "${YELLOW}Next scheduled runs:${NC}"

# Next daily restart (6 AM local)
TOMORROW_6AM=$(date -d "tomorrow 06:00" 2>/dev/null || date -v+1d -v6H -v0M -v0S 2>/dev/null || echo "Unable to calculate")
echo "Daily restart: $TOMORROW_6AM"

# Next monthly wipe (first Thursday)
CURRENT_DAY=$(date +%d)
CURRENT_DOW=$(date +%w)  # 0=Sunday, 4=Thursday

if [ "$CURRENT_DOW" -eq 4 ] && [ "$CURRENT_DAY" -le 7 ]; then
    # Today is first Thursday
    CURRENT_HOUR=$(date +%H)
    UTC_HOUR=$(date -u +%H)
    if [ "$UTC_HOUR" -lt 19 ]; then
        echo "Monthly wipe: TODAY at 19:00 UTC ($(date -d "today 19:00" 2>/dev/null || echo "7PM UTC"))"
    else
        # Calculate next month's first Thursday
        NEXT_MONTH=$(date -d "next month" +%Y-%m-01 2>/dev/null || echo "Next month")
        echo "Monthly wipe: First Thursday of next month at 19:00 UTC"
    fi
else
    # Find next first Thursday
    if [ "$CURRENT_DAY" -gt 7 ]; then
        # We're past the first Thursday this month
        echo "Monthly wipe: First Thursday of next month at 19:00 UTC"
    else
        # First Thursday is still coming this month
        echo "Monthly wipe: This month's first Thursday at 19:00 UTC"
    fi
fi

echo ""
echo -e "${GREEN}=== Setup Complete ===${NC}"
echo ""
echo "Log files will be written to:"
echo "  - Daily restarts: $PROJECT_ROOT/admin/logs/daily_restart.log"
echo "  - Monthly wipes:  $PROJECT_ROOT/admin/logs/monthly_wipe.log"
echo ""
echo -e "${YELLOW}Important notes:${NC}"
echo "1. The monthly wipe runs at EXACTLY 19:00 UTC (official Rust wipe time)"
echo "2. This adjusts automatically for daylight saving time"
echo "3. Daily restart runs at 6:00 AM in your local timezone"
echo "4. Ensure the server has proper permissions to run Docker commands"
echo "5. Check logs regularly to ensure cron jobs are running correctly"
echo ""
echo "To view current cron jobs:  crontab -l"
echo "To remove these cron jobs:  crontab -l | grep -v 'Rust Server Maintenance' | crontab -"