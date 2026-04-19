# ── Stage 1: Build C# controller (Native AOT) ──────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS builder
RUN apk add --no-cache clang build-base zlib-dev
WORKDIR /src
COPY controller/Recastify.csproj ./
RUN dotnet restore
COPY controller/ ./
ARG TARGETARCH
RUN if [ "$TARGETARCH" = "arm64" ]; then \
        RID="linux-musl-arm64"; \
    else \
        RID="linux-musl-x64"; \
    fi && \
    dotnet publish -c Release -r $RID /p:PublishAot=true -o /out

# ── Stage 2: Final image based on shairport-sync ───────────────
FROM mikebrady/shairport-sync:classic

# Install ffmpeg + icecast + mosquitto for all-in-one mode
RUN apk add --no-cache ffmpeg icecast mosquitto curl

# Controller binary from build stage
COPY --from=builder /out/controller /usr/local/bin/controller

# Web UI assets
COPY controller/wwwroot/ /app/wwwroot/

# Hook scripts
COPY hooks/ /opt/hooks/
RUN chmod +x /opt/hooks/*.sh

# Shairport-sync config template
COPY config/ /opt/config/

# Entrypoint mode dispatcher
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

EXPOSE 8100 3000

ENTRYPOINT ["/entrypoint.sh"]
CMD ["all-in-one"]
