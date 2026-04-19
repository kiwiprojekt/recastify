// Recastify — iPod auto-reconnect web UI
// Targets iOS 15 Safari: var only, .then(), no arrow functions, no ES modules

var audio = document.getElementById('player');
var bridgeListEl = document.getElementById('bridge-list');
var nowPlayingBar = document.getElementById('now-playing-bar');
var npStatus = document.getElementById('np-status');
var npName = document.getElementById('np-name');
var npStop = document.getElementById('np-stop');

var currentMount = null;
var currentStreamUrl = null;
var pollTimer = null;
var refreshTimer = null;
var POLL_INTERVAL = 2000;
var REFRESH_INTERVAL = 5000;
var MAX_OFFLINE_POLLS = 150;
var pollCount = 0;

// --- Bridge list rendering ---

function fetchBridges() {
    var xhr = new XMLHttpRequest();
    xhr.open('GET', '/api/bridges', true);
    xhr.onreadystatechange = function() {
        if (xhr.readyState !== 4 || xhr.status !== 200) return;
        var data;
        try { data = JSON.parse(xhr.responseText); } catch (e) { return; }
        renderBridges(data.bridges);
    };
    xhr.send();
}

function renderBridges(bridges) {
    var html = '';
    for (var i = 0; i < bridges.length; i++) {
        var b = bridges[i];
        var stateClass = b.state === 'playing' ? 'playing' : (b.state === 'paused' || b.state === 'starting' ? 'paused' : 'offline');
        var stateLabel = b.state === 'playing' ? 'Now Playing' : (b.state === 'starting' ? 'Starting' : (b.state === 'paused' ? 'Paused' : 'Offline'));
        var subtitle = b.state === 'playing' ? 'Tap to listen' : (b.state === 'starting' ? 'Connecting...' : (b.state === 'paused' ? 'Waiting for audio...' : 'Offline'));
        var btnClass = 'btn btn-listen';
        var btnText = 'Listen';
        if (b.state === 'paused') {
            btnClass += ' paused-btn';
            btnText = 'Listen anyway';
        } else if (b.state === 'starting') {
            btnClass += ' paused-btn';
            btnText = 'Listen anyway';
        } else if (b.state === 'offline') {
            btnClass += ' offline-btn';
            btnText = 'Offline';
        }

        html += '<div class="bridge-card">';
        html += '<div class="bridge-card-header">';
        html += '<div class="bridge-status-dot ' + stateClass + '"></div>';
        html += '<span class="bridge-name">' + escapeHtml(b.name) + '</span>';
        html += '<span class="bridge-state-label ' + stateClass + '">' + stateLabel + '</span>';
        html += '</div>';
        html += '<div class="bridge-subtitle">' + escapeHtml(subtitle) + '</div>';

        // Show now playing metadata if available
        if (b.now_playing && b.now_playing.title && b.now_playing.title !== '' && b.now_playing.title !== '--') {
            html += '<div class="bridge-subtitle" style="color:#ffffff;font-size:13px;">' + escapeHtml(b.now_playing.title) + ' — ' + escapeHtml(b.now_playing.artist) + '</div>';
        }

        html += '<div class="bridge-actions">';
        if (b.state !== 'offline') {
            html += '<button class="' + btnClass + '" onclick="listenTo(\'' + escapeAttr(b.mount) + '\', \'' + escapeAttr(b.stream_url) + '\', \'' + escapeAttr(b.name) + '\')">' + btnText + '</button>';
            html += '<a class="btn" style="background:#1a1a1a;border:1px solid #333;min-width:auto;padding:10px 14px;font-size:13px;color:#a0a0a0;text-decoration:none;" href="player.html?bridge=' + encodeURIComponent(b.id) + '">Player</a>';
        } else {
            html += '<button class="' + btnClass + '" disabled>' + btnText + '</button>';
        }
        html += '</div>';
        html += '</div>';
    }
    bridgeListEl.innerHTML = html;
}

function escapeHtml(str) {
    if (!str) return '';
    return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

function escapeAttr(str) {
    if (!str) return '';
    return str.replace(/'/g, "\\'").replace(/"/g, '&quot;');
}

// --- Audio playback + auto-reconnect ---

function listenTo(mount, streamUrl, name) {
    currentMount = mount;
    currentStreamUrl = streamUrl;
    pollCount = 0;
    stopPolling();
    audio.src = streamUrl;
    audio.play();
    showNowPlaying(name, 'playing');
}

function onStreamLost() {
    showNowPlaying(null, 'waiting');
    pollCount = 0;
    startPolling();
}

function startPolling() {
    if (pollTimer) return;
    pollTimer = setInterval(function() {
        pollCount++;
        if (pollCount > MAX_OFFLINE_POLLS) {
            stopPolling();
            showNowPlaying(null, 'offline');
            return;
        }
        var xhr = new XMLHttpRequest();
            xhr.open('GET', '/api/bridges', true);
            xhr.onreadystatechange = function() {
                if (xhr.readyState !== 4 || xhr.status !== 200) return;
                var data;
                try { data = JSON.parse(xhr.responseText); } catch (e) { return; }
                var bridge = null;
                for (var i = 0; i < data.bridges.length; i++) {
                    if (data.bridges[i].mount === currentMount) {
                        bridge = data.bridges[i];
                        break;
                    }
                }
                if (bridge && bridge.state === 'playing') {
                    stopPolling();
                    audio.src = bridge.stream_url;
                    audio.play();
                    showNowPlaying(bridge.name, 'playing');
                }
            };
            xhr.send();
    }, POLL_INTERVAL);
}

function stopPolling() {
    if (pollTimer) {
        clearInterval(pollTimer);
        pollTimer = null;
    }
}

function showNowPlaying(name, state) {
    nowPlayingBar.classList.remove('hidden');
    if (state === 'playing') {
        npStatus.textContent = 'Now playing';
        npName.textContent = name || currentMount || '';
    } else if (state === 'waiting') {
        npStatus.innerHTML = 'Waiting for audio... <span class="waiting-dots"><span>&#9676;</span> <span>&#9676;</span> <span>&#9676;</span></span>';
        npName.textContent = name || currentMount || '';
    } else if (state === 'offline') {
        npStatus.textContent = 'Offline — stream unavailable';
        npName.innerHTML = '<button class="btn-retry" onclick="retryListening()">Retry</button>';
    }
}

function retryListening() {
    if (currentMount && currentStreamUrl) {
        pollCount = 0;
        showNowPlaying(null, 'waiting');
        startPolling();
    }
}

function stopListening() {
    audio.pause();
    audio.src = '';
    currentMount = null;
    currentStreamUrl = null;
    stopPolling();
    nowPlayingBar.classList.add('hidden');
}

// --- Event listeners ---

audio.addEventListener('ended', onStreamLost);
audio.addEventListener('error', onStreamLost);
audio.addEventListener('stalled', function() {
    // stalled fires when data stops arriving — may recover on its own
    // Wait 5 seconds before treating as lost
    setTimeout(function() {
        if (audio.paused || audio.ended || audio.readyState < 3) {
            onStreamLost();
        }
    }, 5000);
});

npStop.addEventListener('click', function() {
    stopListening();
});

// --- Initialize ---

fetchBridges();
refreshTimer = setInterval(fetchBridges, REFRESH_INTERVAL);
