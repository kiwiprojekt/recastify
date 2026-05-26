#!/bin/sh
set -e

MODE="${1:-all-in-one}"
shift 2>/dev/null || true

# ─── Configuration via environment variables ───────────────────────
ICECAST_HOST="${ICECAST_HOST:-localhost}"
ICECAST_PORT="${ICECAST_PORT:-8100}"
ICECAST_SOURCE_PASSWORD="${ICECAST_SOURCE_PASSWORD:-hackme}"
ICECAST_ADMIN_PASSWORD="${ICECAST_ADMIN_PASSWORD:-admin}"
ICECAST_MOUNT="${ICECAST_MOUNT:-/stream}"
AUDIO_BITRATE="${AUDIO_BITRATE:-320k}"
AIRPLAY_NAME="${AIRPLAY_NAME:-Recastify}"
BRIDGE_ID="${BRIDGE_ID:-default}"
MQTT_HOST="${MQTT_HOST:-localhost}"
MQTT_PORT="${MQTT_PORT:-1883}"
PIPE_PATH="/tmp/shairport-sync-audio"

# ─── Helper: generate shairport-sync config from template ──────────
generate_shairport_conf() {
    local template="/opt/config/shairport-sync.conf.template"
    local output="/tmp/shairport-sync.conf"
    if [ -f "$template" ]; then
        sed -e "s|\${AIRPLAY_NAME}|${AIRPLAY_NAME}|g" \
            -e "s|\${BRIDGE_ID}|${BRIDGE_ID}|g" \
            -e "s|\${MQTT_HOST}|${MQTT_HOST}|g" \
            -e "s|\${MQTT_PORT}|${MQTT_PORT}|g" \
            "$template" > "$output"
        echo "[entrypoint] Generated shairport-sync config at $output" >&2
    else
        echo "[entrypoint] WARNING: config template not found at $template" >&2
        output="/etc/shairport-sync.conf"
    fi
    echo "$output"
}

# ─── Helper: start dbus + avahi for mDNS advertisement ────────────
start_avahi() {
    echo "[entrypoint] Starting D-Bus..."
    mkdir -p /run/dbus
    dbus-daemon --system --fork 2>/dev/null || true

    echo "[entrypoint] Starting Avahi daemon..."
    mkdir -p /var/run/avahi-daemon
    avahi-daemon --daemonize --no-chroot 2>/dev/null || true

    # Wait briefly for avahi to be ready
    local i=0
    while [ $i -lt 10 ]; do
        avahi-daemon --check 2>/dev/null && echo "[entrypoint] Avahi ready" && return
        sleep 0.5
        i=$((i+1))
    done
    echo "[entrypoint] WARNING: Avahi may not be ready"
}

# ─── Helper: start Mosquitto MQTT broker ───────────────────────────
start_mosquitto() {
    echo "[entrypoint] Starting Mosquitto on port ${MQTT_PORT}..."
    mosquitto -p "$MQTT_PORT" -d
    echo "[entrypoint] Mosquitto started"
}

# ─── Helper: start ffmpeg encoder in background ───────────────────
start_ffmpeg() {
    rm -f "$PIPE_PATH"
    mkfifo "$PIPE_PATH"
    echo "[bridge] Created audio pipe at $PIPE_PATH"

    (
        while true; do
            echo "[bridge] Starting ffmpeg -> icecast://${ICECAST_HOST}:${ICECAST_PORT}${ICECAST_MOUNT}"
            ffmpeg -hide_banner -loglevel warning \
                -fflags nobuffer \
                -flags +low_delay \
                -probesize 32 \
                -analyzeduration 0 \
                -f s16le -ar 44100 -ac 2 \
                -i "$PIPE_PATH" \
                -c:a libmp3lame -b:a "$AUDIO_BITRATE" \
                -flush_packets 1 \
                -f mp3 \
                -content_type audio/mpeg \
                "icecast://source:${ICECAST_SOURCE_PASSWORD}@${ICECAST_HOST}:${ICECAST_PORT}${ICECAST_MOUNT}" \
                2>&1 || true
            echo "[bridge] ffmpeg exited, restarting in 3s..."
            # Do NOT delete/recreate the pipe — shairport-sync holds the write fd;
            # recreating it would cause ffmpeg to block forever on the new pipe.
            sleep 3
        done
    ) &

    echo "[bridge] ffmpeg loop running (PID $!)"
}

# ─── Helper: start icecast ─────────────────────────────────────────
start_icecast() {
    echo "[all-in-one] Starting Icecast on port ${ICECAST_PORT}..."
    echo "[all-in-one] Port ${ICECAST_PORT} pre-check:"
    ss -tlnp 2>/dev/null | grep ":${ICECAST_PORT}" || echo "[all-in-one] Port ${ICECAST_PORT} is free"

    # icecast2 refuses to run as root — create a dedicated user if needed
    if ! id icecast >/dev/null 2>&1; then
        addgroup -S icecast && adduser -S -G icecast icecast
    fi

    mkdir -p /var/log/icecast /usr/share/icecast/web /usr/share/icecast/admin
    chown -R icecast:icecast /var/log/icecast

    (umask 077; cat > /tmp/icecast.xml <<EOF
<icecast>
    <location>Docker</location>
    <admin>admin@localhost</admin>
    <limits>
        <clients>100</clients>
        <sources>4</sources>
        <queue-size>524288</queue-size>
        <burst-size>65536</burst-size>
    </limits>
    <authentication>
        <source-password>${ICECAST_SOURCE_PASSWORD}</source-password>
        <relay-password>relay</relay-password>
        <admin-user>admin</admin-user>
        <admin-password>${ICECAST_ADMIN_PASSWORD}</admin-password>
    </authentication>
    <hostname>$(hostname)</hostname>
    <listen-socket>
        <port>${ICECAST_PORT}</port>
        <bind-address>0.0.0.0</bind-address>
    </listen-socket>
    <security>
        <changeowner>
            <user>icecast</user>
            <group>icecast</group>
        </changeowner>
    </security>
    <paths>
        <logdir>/var/log/icecast</logdir>
        <webroot>/usr/share/icecast/web</webroot>
        <adminroot>/usr/share/icecast/admin</adminroot>
    </paths>
    <logging>
        <accesslog>-</accesslog>
        <errorlog>-</errorlog>
        <loglevel>3</loglevel>
    </logging>
</icecast>
EOF
)
    icecast -c /tmp/icecast.xml 2>&1 &
    ICECAST_PID=$!

    local i=0
    while [ $i -lt 40 ]; do
        if ! kill -0 "$ICECAST_PID" 2>/dev/null; then
            echo "[all-in-one] ERROR: Icecast process (pid=$ICECAST_PID) died during startup"
            echo "[all-in-one] Port ${ICECAST_PORT} after failure:"
            ss -tlnp 2>/dev/null | grep ":${ICECAST_PORT}" || echo "(nothing)"
            return 1
        fi
        http_code=$(curl -s -o /dev/null -w "%{http_code}" \
            "http://localhost:${ICECAST_PORT}/status-json.xsl" 2>/dev/null || echo "000")
        if [ "$http_code" = "200" ]; then
            echo "[all-in-one] Icecast ready (pid=$ICECAST_PID)"
            return
        fi
        sleep 0.5
        i=$((i+1))
    done
    echo "[all-in-one] WARNING: Icecast not responding after 20s (last http=$http_code)"
}

# ─── Mode dispatcher ──────────────────────────────────────────────
case "$MODE" in
    all-in-one)
        echo "[entrypoint] Mode: all-in-one (Icecast + Mosquitto + Controller + Bridge)"
        start_avahi
        start_icecast
        start_mosquitto

        # Start controller in background
        echo "[all-in-one] Starting controller..."
        cd /app && ICECAST_HOST=localhost MQTT_HOST=localhost /usr/local/bin/controller &

        # Start ffmpeg
        start_ffmpeg

        # Generate shairport-sync config and start
        SHAIRPORT_CONF="$(generate_shairport_conf)"
        echo "[all-in-one] Starting shairport-sync with config $SHAIRPORT_CONF..."
        exec shairport-sync -c "$SHAIRPORT_CONF" "$@"
        ;;

    controller)
        echo "[entrypoint] Mode: controller (Web UI + API only)"
        cd /app && exec /usr/local/bin/controller "$@"
        ;;

    bridge)
        echo "[entrypoint] Mode: bridge (shairport-sync + ffmpeg only)"
        start_avahi
        start_ffmpeg
        SHAIRPORT_CONF="$(generate_shairport_conf)"
        echo "[bridge] Starting shairport-sync with config $SHAIRPORT_CONF..."
        exec shairport-sync -c "$SHAIRPORT_CONF" "$@"
        ;;

    *)
        echo "[entrypoint] Unknown mode: $MODE"
        echo "Usage: entrypoint.sh [all-in-one|controller|bridge]"
        exit 1
        ;;
esac
