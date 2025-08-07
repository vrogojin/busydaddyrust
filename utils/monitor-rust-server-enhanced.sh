#!/bin/bash

LOGFILE=/home/linuxgsm/log/autoheal.log
CRASHLOG=/home/linuxgsm/log/crash-tracker.log
# Validation now happens on every startup, so we just track crashes for monitoring
CRASH_WINDOW=86400 # Time window in seconds (24 hours)

[ ! -r /rust-environment.sh ] || source /rust-environment.sh

function log_crash() {
  echo "$(date +%s)" >> "$CRASHLOG"
  echo "$(date) - Crash detected and logged" >> "$LOGFILE"
}

function count_recent_crashes() {
  if [ ! -f "$CRASHLOG" ]; then
    echo 0
    return
  fi
  
  local current_time=$(date +%s)
  local count=0
  
  # Count crashes within the time window
  while IFS= read -r crash_time; do
    if [ -n "$crash_time" ] && [ "$crash_time" -ge $((current_time - CRASH_WINDOW)) ]; then
      ((count++))
    fi
  done < "$CRASHLOG"
  
  # Clean up old entries
  local temp_file="/tmp/crash_tracker_temp.log"
  > "$temp_file"
  while IFS= read -r crash_time; do
    if [ -n "$crash_time" ] && [ "$crash_time" -ge $((current_time - CRASH_WINDOW)) ]; then
      echo "$crash_time" >> "$temp_file"
    fi
  done < "$CRASHLOG"
  mv "$temp_file" "$CRASHLOG"
  
  echo $count
}

function log_crash_summary() {
  local crash_count=$(count_recent_crashes)
  echo "$(date) - Crash detected. Recent crashes in 24h: $crash_count" >> "$LOGFILE"
  echo "$(date) - Validation will run automatically on next startup" >> "$LOGFILE"
}

function wait-for-rust() {
  until pgrep "$@"; do
    sleep 1
  done
}

function graceful-kill() {
  echo "$(date) - auto-heal graceful kill" >> "$LOGFILE"
  log_crash
  log_crash_summary
  
  pgrep tail | xargs -- kill
}

function hard-kill() {
  echo "$(date) - auto-heal HARD kill" >> "$LOGFILE"
  log_crash
  log_crash_summary
  
  pgrep tail | xargs -- kill -9
}

function kill-container-on-absence-of() {
  local retry=0
  while true; do
    sleep 10
    if pgrep "$@"; then
      retry=0
    else
      (( retry=retry+1 ))
    fi
    if [ "$retry" -ge 6 ]; then
      hard-kill
    elif [ "$retry" -ge 3 ]; then
      graceful-kill
    fi
  done
}

if [ ! "${uptime_monitoring:-}" = true ]; then
  echo 'RustDedicated will not be monitored.'
  echo 'Set uptime_monitoring=true in rust-environment.sh to enable autoheal.'
  exit
else
  echo 'Monitoring RustDedicated for automatic restart with crash tracking.'
fi

# discard further output
exec &> /dev/null
wait-for RustDedicated
kill-container-on-absence-of RustDedicated