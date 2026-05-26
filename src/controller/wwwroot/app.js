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
var disableStreamProxy = false;
var NO_SLEEP_VIDEO_SRC = 'data:video/mp4;base64,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';

// --- Bridge list rendering ---

function fetchBridges() {
    var xhr = new XMLHttpRequest();
    xhr.open('GET', '/api/bridges', true);
    xhr.onreadystatechange = function() {
        if (xhr.readyState !== 4 || xhr.status !== 200) return;
        var data;
        try { data = JSON.parse(xhr.responseText); } catch (e) { return; }
        disableStreamProxy = !!data.disable_stream_proxy;
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
            var actualStreamUrl = disableStreamProxy ? b.stream_url : ('/api/bridges/' + encodeURIComponent(b.id) + '/stream');
            html += '<button class="' + btnClass + '" onclick="listenTo(\'' + escapeAttr(b.mount) + '\', \'' + escapeAttr(actualStreamUrl) + '\', \'' + escapeAttr(b.name) + '\')">' + btnText + '</button>';
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

    // Play hidden silent video to maintain wake lock on iOS PWA
    var noSleepVideo = document.getElementById('no-sleep-video');
    if (noSleepVideo) {
        noSleepVideo.src = NO_SLEEP_VIDEO_SRC;
        try {
            noSleepVideo.play();
        } catch (e) {
            console.log('Wake lock video play failed:', e);
        }
    }

    audio.src = streamUrl;
    audio.play();
    showNowPlaying(name, 'playing');
}

function onStreamLost() {
    if (!currentMount) return;
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
                    var actualUrl = disableStreamProxy ? bridge.stream_url : ('/api/bridges/' + encodeURIComponent(bridge.id) + '/stream');
                    audio.src = actualUrl;
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

    // Release wake lock on iOS PWA
    var noSleepVideo = document.getElementById('no-sleep-video');
    if (noSleepVideo) {
        noSleepVideo.pause();
        noSleepVideo.src = '';
    }

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
