#!/usr/bin/env bash
# One-time interactive Claude login for the run container.
#
# Launches a throwaway container off the run-base image with the same
# .claude bind mount the phase containers will use, runs `claude login`
# inside, and exits. The OAuth token persists in the host-side mount,
# so every subsequent phase container reuses it without an Anthropic API key.
#
# Run this once on a new machine, or whenever your saved login expires.
#
# Usage: scripts/claude-login.sh [host-path]
#   host-path defaults to ~/.agentplatform/claude (created if missing).
#   Set AgentPlatform__Docker__ClaudeCredentialsHostPath to the same value
#   so the API knows where to mount it.

set -euo pipefail

HOST_PATH="${1:-$HOME/.agentplatform/claude}"
IMAGE="${AGP_RUN_IMAGE:-agentplatform/run-base:latest}"

mkdir -p "$HOST_PATH"

echo "Mounting $HOST_PATH -> /home/runner/.agp-claude in $IMAGE"
echo "Container HOME is overridden to that path so both ~/.claude/ and ~/.claude.json land in the bind."
echo "Run 'claude login' inside, follow the OAuth flow, then 'exit'."
echo

docker run --rm -it \
    --user runner \
    -e HOME=/home/runner/.agp-claude \
    -v "$HOST_PATH:/home/runner/.agp-claude" \
    --entrypoint /bin/bash \
    "$IMAGE" \
    -lc 'cd "$HOME"; claude login; echo; echo "Done. Credentials persisted at $HOME (host: '"$HOST_PATH"')."; exec bash'
