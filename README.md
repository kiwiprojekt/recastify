# Recastify

Stream any app's audio from an iPhone/iPad/Mac to a web browser — including devices that predate modern streaming services, like an iPhone 4 (iOS 9.3.6).

```
AirPlay source (like spotify on iPhone)
    │ 
    ▼
shairport-sync  ──PCM pipe──▶  ffmpeg  ──MP3──▶  Icecast
                                                    │
                                                    ▼
                                             web browser
```

| Main | Player | Admin |
|:---:|:---:|:---:|
| ![Main](screens/main.png) | ![Player](screens/player.png) | ![Admin](screens/admin.png) |

---

## The Problem

The Yamaha TSX-130 is a great-sounding mini audio system, but its only input is an Apple 30-pin dock connector — designed for iPods and iPhones from the pre-Lightning era. Those old devices are stuck on iOS 9 or earlier, and every modern streaming service (Spotify, Apple Music, YouTube Music, etc.) has long since dropped support for them.

The hardware is perfectly fine. The speakers sound great. But there's no way to get music from any modern source into it — no Bluetooth, no AUX, no AirPlay, just a 30-pin dock that expects an obsolete iPod.

The goal: **play Spotify (or any other audio source) on the Yamaha through that old iPhone, without jailbreaking or sideloading**

## How It's Solved

Use what even the oldest iOS devices have — Safari and a web browser that can play audio.

1. **Send audio via AirPlay** from the iPhone running Spotify (or Apple Music, YouTube, anything). This uses the native iOS AirPlay button — no app modification, no Spotify involvement.
2. **Receive and re-encode** with [shairport-sync](https://github.com/mikebrady/shairport-sync) (the standard open-source AirPlay receiver) running in Docker. Audio is piped as raw PCM to ffmpeg, re-encoded to MP3, and pushed to an Icecast HTTP stream.
3. **Play the stream** in Safari on the old iPod/iPhone docked in the Yamaha. The web UI auto-reconnects when audio pauses or resumes, shows track metadata and cover art via MQTT, and works as a full-screen Home Screen app.

The iPod sits in the dock, Safari plays the stream, and the Yamaha's speakers and amplifier do the rest.

---

### Components

| Container | Image | Role |
|---|---|---|
| `bridge` | `mikebrady/shairport-sync` + ffmpeg | Receives AirPlay, encodes to MP3, pushes to Icecast |
| `icecast` | `infiniteproject/icecast` | Serves the MP3 HTTP stream |
| `mqtt` | `eclipse-mosquitto:2` | Routes track metadata (title, artist, cover art) from shairport-sync to the controller |
| `controller` | Built from this repo | C# .NET 10 web server — REST API + static web UI |

---

## Quick Start (Single Bridge)

**Requires:** Docker and Docker Compose on a Linux host (or Raspberry Pi). The host must be on the same local network as your iPhone.

```sh
git clone https://github.com/your-org/recastify.git
cd recastify/src

# Optional: set a strong source password
export ICECAST_SOURCE_PASSWORD=mysecretpassword

docker compose up -d
```

### All-in-One (Portainer / Single Container)

A single-container deployment that bundles shairport-sync, ffmpeg, Icecast, Mosquitto, and the controller into one image. Good for Portainer stacks or simple setups.

```sh
docker compose -f docker-compose.portainer.yml up -d
```

Uses `network_mode: host` so the AirPlay receiver is directly discoverable. Icecast listens on port **8100**, the web UI on **3000**.

Then on your iPhone:
1. Open Spotify (or any app).
2. Tap the AirPlay icon and select **Recastify**.
3. Open `http://<server-ip>:3000` on the iPod Touch in Safari.
4. Tap **Listen** on the bridge card.

Add to Home Screen in Safari for a full-screen experience.

---

## Multi-Bridge Setup

Run multiple named AirPlay receivers (e.g. Living Room, Kitchen, Workout) each on its own Icecast mount, all managed from one controller.

```sh
docker compose -f docker-compose.multi.yml up -d
```

Or configure via `config.yaml`:

```yaml
icecast:
  host: icecast
  port: 8000
  source_password: "changeme"

bridges:
  - name: "Living Room"
    mount: "/living-room"
    ip: "192.168.1.101"
    bitrate: "320k"
    enabled: true

  - name: "Kitchen"
    mount: "/kitchen"
    ip: "192.168.1.102"
    bitrate: "256k"
    enabled: true
```

Each bridge gets a separate IP address (via ipvlan networking) so it appears as a distinct AirPlay device on the network.

---

## Environment Variables

All configuration can be done via environment variables. The `config.yaml` file takes precedence when present.

| Variable | Default | Description |
|---|---|---|
| `AIRPLAY_NAME` | `Recastify` | Name shown in the AirPlay device list |
| `ICECAST_HOST` | `localhost` | Icecast hostname (use `icecast` in Docker Compose) |
| `ICECAST_PORT` | `8000` | Icecast port |
| `ICECAST_SOURCE_PASSWORD` | `hackme` | **Change this.** Icecast source password |
| `ICECAST_MOUNT` | `/stream` | Icecast mount point |
| `AUDIO_BITRATE` | `320k` | MP3 bitrate (e.g. `128k`, `192k`, `256k`, `320k`) |
| `BRIDGE_ID` | `default` | Identifier used in API responses and MQTT topics |
| `MQTT_HOST` | `localhost` | Mosquitto hostname |
| `MQTT_PORT` | `1883` | Mosquitto port |
| `CONTROLLER_URL` | `localhost:3000` | Controller address (used by shairport-sync hooks) |
| `WEB_UI_PORT` | `3000` | Port the controller listens on |
| `CONFIG_PATH` | `/app/config.yaml` | Path to config.yaml inside the container |

---

## Web UI

Three pages served at port `3000`:

| URL | Description |
|---|---|
| `/` | Stream picker — lists all bridges with live status and now-playing metadata. Tap **Listen** to start playing, **Player** to open the full-screen player. |
| `/player?bridge=<id>` | Full-screen player — cover art, track title/artist/album, auto-reconnects on stream drop. Designed as an iOS Home Screen app. |
| `/admin` | Admin panel — add, edit, start, stop, and delete bridges. |

### iOS PWA (Add to Home Screen)

The player page supports **Add to Home Screen** on iOS for a full-screen, app-like experience without Safari's address bar. Tested on **iPhone 4 with iOS 9.3.6**.

> **Mute switch:** When running as a Home Screen app (standalone mode), iOS uses the UIWebView audio session which **respects the hardware ringer/silent switch**. If you hear no audio, make sure the mute switch on the side of the device is off. This does not apply when using Safari directly.

---

## REST API

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/bridges` | List all bridges with status and now-playing |
| `POST` | `/api/bridges` | Add a bridge |
| `PUT` | `/api/bridges/:id` | Update bridge config |
| `DELETE` | `/api/bridges/:id` | Remove a bridge |
| `POST` | `/api/bridges/:id/start` | Start a bridge |
| `POST` | `/api/bridges/:id/stop` | Stop a bridge |
| `POST` | `/api/status` | Session hook callback (called by shairport-sync hooks) |
| `GET` | `/api/bridges/:id/art` | Cover art as JPEG/PNG |
| `GET` | `/api/bridges/:id/stream` | Same-origin stream proxy (pipes Icecast MP3 through the controller — needed for iOS PWA which blocks cross-origin audio) |
| `POST` | `/api/bridges/:id/command/:command` | Remote control (play, pause, next, prev) via MQTT |
| `GET` | `/api/health` | Health check |

### Example response

```json
{
  "bridges": [
    {
      "id": "living-room",
      "name": "Living Room",
      "mount": "/living-room",
      "stream_url": "http://192.168.1.50:8000/living-room",
      "state": "playing",
      "listeners": 1,
      "bitrate": "320k",
      "enabled": true,
      "now_playing": {
        "title": "Bohemian Rhapsody",
        "artist": "Queen",
        "album": "A Night at the Opera",
        "artwork_url": "/api/bridges/living-room/art",
        "elapsed_ms": 204000,
        "duration_ms": 355000,
        "updated_at": "2026-04-16T15:30:01Z"
      }
    }
  ]
}
```

---

## Building

### Controller only (development)

```sh
cd src/controller
dotnet build
dotnet run
```

The dev server starts at `http://localhost:3000`.

### Docker image

```sh
cd src
docker build -t recastify .
```

The Dockerfile uses a multi-stage build:
- **Stage 1:** `mcr.microsoft.com/dotnet/sdk:10.0-alpine` — compiles the C# controller with Native AOT to a single Linux binary.
- **Stage 2:** `mikebrady/shairport-sync` (Alpine-based) — adds ffmpeg, Icecast, Mosquitto, and the compiled controller binary.

### Development with hot-reload

`controller/Dockerfile.dev` builds without AOT for faster iteration and is used by `docker-compose.yml` by default.

---

## How Auto-Reconnect Works

When AirPlay stops (e.g. you pause Spotify), ffmpeg stalls on the empty pipe and eventually loses its Icecast connection. The mount disappears. Safari on the iPod sees the stream end.

The web UI handles this gracefully:

1. **shairport-sync hooks** fire a `POST /api/status` to the controller with `state: paused` when play ends, and `state: playing` when it resumes.
2. **The controller** also polls the Icecast status API every 5 seconds to verify which mounts are live.
3. **The iPod web UI** polls `/api/bridges` every 2 seconds. When a bridge transitions from `paused` → `playing`, it automatically reconnects the `<audio>` element — no user action needed.

---

## Legal Notes
- This is intended for personal use on your own network.

---

## License

MIT — see [LICENSE](LICENSE).
