#!/bin/bash
# Copy and resize logo for WebRustPlus to serve

if [ -f /busydaddyrust-logo.png ]; then
    mkdir -p serverfiles/oxide/data
    
    # Copy original logo
    cp /busydaddyrust-logo.png serverfiles/oxide/data/busydaddyrust-logo-original.png
    
    # Create resized version for server header (512x256)
    # Using dark background color from the logo (#2b2b2b)
    convert /busydaddyrust-logo.png \
        -resize 512x256 \
        -background '#1a1a1a' \
        -gravity center \
        -extent 512x256 \
        serverfiles/oxide/data/busydaddyrust-logo.png
    
    # Also create a smaller version for web interface if needed
    convert /busydaddyrust-logo.png \
        -resize 256x256 \
        serverfiles/oxide/data/busydaddyrust-logo-small.png
    
    echo "Logo processed and saved:"
    echo "  - Original: busydaddyrust-logo-original.png (1024x1024)"
    echo "  - Header: busydaddyrust-logo.png (512x256)"
    echo "  - Small: busydaddyrust-logo-small.png (256x256)"
else
    echo "Logo file not found at /busydaddyrust-logo.png"
fi