#!/bin/bash

# Remove host system cron jobs for Rust server maintenance
# This script safely removes the cron jobs created by setup-host-cron.sh

set -e

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${GREEN}=== Rust Server Host Cron Removal ===${NC}"
echo ""

# Get the absolute path to the admin directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

echo -e "${YELLOW}Project root:${NC} $PROJECT_ROOT"
echo ""

# Check for existing Rust server cron jobs
RESTART_SCRIPT="$PROJECT_ROOT/admin/restart_server.sh"
WIPE_SCRIPT="$PROJECT_ROOT/admin/regenerate-map.sh"

EXISTING_RESTART=$(crontab -l 2>/dev/null | grep -c "$RESTART_SCRIPT" || true)
EXISTING_WIPE=$(crontab -l 2>/dev/null | grep -c "$WIPE_SCRIPT" || true)
EXISTING_HEADER=$(crontab -l 2>/dev/null | grep -c "=== Rust Server Maintenance" || true)

if [ "$EXISTING_RESTART" -eq 0 ] && [ "$EXISTING_WIPE" -eq 0 ] && [ "$EXISTING_HEADER" -eq 0 ]; then
    echo -e "${YELLOW}No Rust server cron jobs found.${NC}"
    echo "Nothing to remove."
    exit 0
fi

echo -e "${YELLOW}Found Rust server cron jobs:${NC}"
if [ "$EXISTING_RESTART" -gt 0 ]; then
    echo "  - Daily restart job"
fi
if [ "$EXISTING_WIPE" -gt 0 ]; then
    echo "  - Monthly wipe job"
fi
echo ""

# Backup existing crontab
BACKUP_FILE="/tmp/crontab_backup_before_removal_$(date +%Y%m%d_%H%M%S).txt"
crontab -l > "$BACKUP_FILE" 2>/dev/null || true
echo -e "${YELLOW}Backed up existing crontab to:${NC} $BACKUP_FILE"
echo ""

# Confirm removal
echo -e "${RED}WARNING: This will remove the Rust server cron jobs.${NC}"
echo "Do you want to proceed? (y/N)"
read -r response

if [[ ! "$response" =~ ^[Yy]$ ]]; then
    echo "Removal cancelled."
    exit 0
fi

# Remove Rust server cron jobs
echo ""
echo "Removing Rust server cron jobs..."

# Create temporary file with filtered crontab
TEMP_CRON="/tmp/filtered_cron_$$"

# Remove the jobs and the header/footer comments
crontab -l 2>/dev/null | \
    sed '/=== Rust Server Maintenance/,/=== End Rust Server Maintenance/d' | \
    grep -v "$RESTART_SCRIPT" | \
    grep -v "$WIPE_SCRIPT" > "$TEMP_CRON" 2>/dev/null || true

# Install the filtered crontab
if [ -s "$TEMP_CRON" ]; then
    crontab "$TEMP_CRON"
else
    # If the file is empty, clear the crontab
    echo "" | crontab - 2>/dev/null || true
fi

rm -f "$TEMP_CRON"

echo -e "${GREEN}✓${NC} Rust server cron jobs removed successfully!"
echo ""

# Show current crontab
if crontab -l 2>/dev/null | grep -q . ; then
    echo -e "${YELLOW}Remaining cron jobs:${NC}"
    echo "----------------------------------------"
    crontab -l
    echo "----------------------------------------"
else
    echo "No cron jobs remaining for current user."
fi

echo ""
echo -e "${GREEN}=== Removal Complete ===${NC}"
echo ""
echo "The cron jobs have been removed. Your server will no longer:"
echo "  - Restart daily at 6:00 AM"
echo "  - Wipe monthly on first Thursday at 19:00 UTC"
echo ""
echo "To reinstall the cron jobs, run: $SCRIPT_DIR/setup-host-cron.sh"