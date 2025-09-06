# Host Cron Setup for Rust Server

This directory contains scripts to manage host system cron jobs for automatic server maintenance.

## Scripts

### `setup-host-cron.sh`
Sets up automatic daily restarts and monthly wipes synchronized with official Rust/Facepunch schedule.

**What it does:**
- Daily server restart at 6:00 AM local time
- Monthly map wipe on first Thursday at exactly 19:00 UTC (official Rust wipe time)
- Creates log files in `admin/logs/` directory
- Backs up existing crontab before making changes

**Usage:**
```bash
cd /path/to/your/rust-server
./admin/setup-host-cron.sh
```

### `remove-host-cron.sh`
Safely removes the cron jobs created by setup-host-cron.sh.

**Usage:**
```bash
cd /path/to/your/rust-server
./admin/remove-host-cron.sh
```

## Schedule Details

### Daily Restart
- **Time:** 6:00 AM in your local timezone
- **Script:** `admin/restart_server.sh`
- **Purpose:** Rebuilds Docker container and restarts server
- **Log:** `admin/logs/daily_restart.log`

### Monthly Wipe
- **Time:** First Thursday at exactly 19:00 UTC
  - This is 7:00 PM GMT (winter) / 8:00 PM BST (summer)
  - This is 2:00 PM EST / 11:00 AM PST
- **Script:** `admin/regenerate-map.sh skip-prompt`
- **Purpose:** Complete map wipe synchronized with official Rust update
- **Log:** `admin/logs/monthly_wipe.log`

## Important Notes

1. **UTC Synchronization:** The monthly wipe uses a smart UTC check to ensure it runs at exactly 19:00 UTC regardless of your local timezone or daylight saving time changes.

2. **First Run:** When you first run `setup-host-cron.sh`, it will:
   - Check for existing Rust server cron jobs
   - Backup your current crontab
   - Install the new cron jobs
   - Create the logs directory

3. **Logs:** Check the log files regularly to ensure cron jobs are running:
   ```bash
   tail -f admin/logs/daily_restart.log
   tail -f admin/logs/monthly_wipe.log
   ```

4. **Permissions:** Ensure your user has permission to:
   - Run Docker commands
   - Execute scripts in the admin directory
   - Write to the logs directory

5. **Production Server Setup:**
   - Copy the entire project to your production server
   - Run `./admin/setup-host-cron.sh`
   - Verify the paths are correct for your setup
   - Check that Docker commands work without sudo (or add user to docker group)

## Verification

To verify the cron jobs are installed:
```bash
crontab -l | grep "Rust Server Maintenance"
```

To check when jobs will next run:
```bash
# For systemd-based systems
systemctl list-timers

# Or check manually
date  # Current local time
date -u  # Current UTC time
```

## Timezone Considerations

The monthly wipe is designed to run at exactly 19:00 UTC by:
- Checking at both 19:00 and 20:00 local time
- Only executing when `date -u +%H` equals 19
- This ensures it works correctly whether you're in GMT or BST

## Troubleshooting

### Cron job not running?
1. Check if cron service is running: `systemctl status cron`
2. Check cron logs: `grep CRON /var/log/syslog`
3. Verify script permissions: `ls -la admin/*.sh`
4. Test scripts manually: `./admin/restart_server.sh`

### Wrong timezone?
1. Check system timezone: `timedatectl`
2. The monthly wipe auto-adjusts for timezone
3. Daily restart uses local time (6 AM)

### Need to modify schedule?
1. Remove existing: `./admin/remove-host-cron.sh`
2. Edit the setup script to adjust times
3. Re-run: `./admin/setup-host-cron.sh`

## Moving to Production

1. **Clone/copy the repository to production server**
   ```bash
   git clone <your-repo> /path/on/production
   cd /path/on/production
   ```

2. **Run the setup script**
   ```bash
   ./admin/setup-host-cron.sh
   ```

3. **Verify installation**
   ```bash
   crontab -l
   ```

4. **Monitor first run**
   ```bash
   tail -f admin/logs/*.log
   ```

## Support

For issues or questions, check:
- The main project README.md
- The CLAUDE.md file for detailed documentation
- Server logs in `admin/logs/`
- Docker logs: `docker compose logs -f`