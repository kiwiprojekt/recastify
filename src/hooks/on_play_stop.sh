#!/bin/sh
# Fire-and-forget notification to controller. Background + timeout
# so shairport-sync is never blocked.
curl -s -m 2 -X POST "http://${CONTROLLER_URL:-localhost:3000}/api/status" \
    -H "Content-Type: application/json" \
    -d "{\"bridge\": \"${BRIDGE_ID:-default}\", \"state\": \"paused\"}" &
