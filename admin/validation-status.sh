#!/bin/bash

# Display validation status and crash information

echo "=== Rust Server Validation Status ==="
echo

# Check last validation
if [ -f admin/logs/validation.log ]; then
    echo "Recent validations:"
    tail -5 admin/logs/validation.log | while IFS=',' read -r timestamp event result; do
        echo "  $timestamp - $result"
    done
else
    echo "No validation history found"
fi

echo

# Check crash count
echo "Checking recent crashes..."
docker compose exec -T lgsm bash -c '
    CRASHLOG=/home/linuxgsm/log/crash-tracker.log
    if [ -f "$CRASHLOG" ]; then
        current_time=$(date +%s)
        count=0
        while IFS= read -r crash_time; do
            if [ -n "$crash_time" ] && [ "$crash_time" -ge $((current_time - 86400)) ]; then
                ((count++))
            fi
        done < "$CRASHLOG"
        echo "Crashes in last 24 hours: $count"
        
        if [ $count -gt 0 ]; then
            echo "Recent crash timestamps:"
            while IFS= read -r crash_time; do
                if [ -n "$crash_time" ] && [ "$crash_time" -ge $((current_time - 86400)) ]; then
                    echo "  $(date -d @$crash_time)"
                fi
            done < "$CRASHLOG"
        fi
    else
        echo "No crash log found"
    fi
    
    # Check if validation is pending
    if [ -f /home/linuxgsm/VALIDATION_NEEDED ]; then
        echo
        echo "⚠️  VALIDATION PENDING - Will run on next server restart"
    fi
'

echo
echo "=== Scheduled Validations ==="
echo "• Weekly: Every Sunday at 4:00 AM"
echo "• Monthly: First Thursday at 9:00 PM (with map wipe)"
echo "• Automatic: After 3 crashes within 24 hours"