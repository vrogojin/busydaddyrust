# Based on the LinuxGSM Docker image
FROM gameservermanagers/linuxgsm-docker

# Switch to root user for installations
USER root

# Install additional packages for RCON support
RUN apt-get update && \
    apt-get install -y \
    python3-pip \
    python3-venv \
    netcat \
    curl \
    jq \
    && rm -rf /var/lib/apt/lists/*

# Install Python packages for WebSocket RCON
RUN python3 -m pip install --upgrade pip && \
    python3 -m pip install websocket-client

# Create RCON utilities directory
RUN mkdir -p /usr/local/bin/rcon-utils

# Add RCON command script
COPY admin/docker-rcon-command.sh /usr/local/bin/rcon-command
RUN chmod +x /usr/local/bin/rcon-command

# Add reload plugins helper
COPY admin/docker-rcon-reload.sh /usr/local/bin/rcon-reload-plugins
RUN chmod +x /usr/local/bin/rcon-reload-plugins

# Add environment variable to indicate custom image
ENV RUST_CUSTOM_IMAGE=1

# The docker-compose.yml specifies user: root, so we don't need to switch back