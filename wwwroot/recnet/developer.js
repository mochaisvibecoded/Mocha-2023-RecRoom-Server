const recnetFetch = (input, init = {}) => fetch(input, { ...init, credentials: init.credentials ?? 'same-origin' });
const state = {
    snapshot: null,
    requests: [],
    chats: [],
    paused: false,
    cpuHistory: [],
    memoryHistory: [],
    stream: null
};
const byId = id => document.getElementById(id);
const esc = value => String(value ?? '').replace(/[&<>'"]/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' }[char]));
const formatBytes = value => {
    const bytes = Number(value) || 0;
    if (bytes < 1024)
        return `${bytes} B`;
    const units = ['KB', 'MB', 'GB', 'TB'];
    let size = bytes / 1024, index = 0;
    while (size >= 1024 && index < units.length - 1) {
        size /= 1024;
        index++;
    }
    return `${size >= 100 ? size.toFixed(0) : size >= 10 ? size.toFixed(1) : size.toFixed(2)} ${units[index]}`;
};
const formatRate = value => `${formatBytes(value)}/s`;
const formatTime = value => new Intl.DateTimeFormat(undefined, { hour: '2-digit', minute: '2-digit', second: '2-digit' }).format(new Date(value));
const formatUptime = seconds => {
    seconds = Math.max(0, Number(seconds) || 0);
    const days = Math.floor(seconds / 86400), hours = Math.floor(seconds % 86400 / 3600), minutes = Math.floor(seconds % 3600 / 60);
    return days ? `${days}d ${hours}h` : hours ? `${hours}h ${minutes}m` : `${minutes}m`;
};
function mergeBy(items, incoming, key, limit) {
    const map = new Map(items.map(item => [key(item), item]));
    incoming.forEach(item => map.set(key(item), item));
    return [...map.values()].sort((a, b) => new Date(b.at || b.createdAt) - new Date(a.at || a.createdAt)).slice(0, limit);
}
function setStreamState(mode, label) {
    const el = byId('streamState');
    el.className = `stream-state ${mode}`;
    el.innerHTML = `<i></i> ${esc(label)}`;
}
function renderSnapshot(data) {
    state.snapshot = data;
    if (!state.paused)
        state.requests = mergeBy(state.requests, data.requests || [], item => `${item.at}|${item.method}|${item.path}|${item.status}|${item.durationMs}`, 2000);
    state.chats = mergeBy(state.chats, data.chats || [], item => item.messageId, 2000);
    state.cpuHistory.push({ at: data.generatedAt, value: Number(data.server.cpuPercent) || 0 });
    state.memoryHistory.push({ at: data.generatedAt, value: (Number(data.server.memoryBytes) || 0) / 1048576 });
    state.cpuHistory = state.cpuHistory.slice(-120);
    state.memoryHistory = state.memoryHistory.slice(-120);
    const latest = (data.series || []).at(-1) || {};
    const networkRate = (Number(latest.inboundBytes) || 0) + (Number(latest.outboundBytes) || 0);
    const errorRate = data.totals.requests ? data.totals.errors / data.totals.requests * 100 : 0;
    byId('lastUpdated').textContent = formatTime(data.generatedAt);
    byId('onlineMetric').textContent = Number(data.totals.onlinePlayers).toLocaleString();
    byId('socketMetric').textContent = `${Number(data.totals.connectedSockets).toLocaleString()} live sockets`;
    byId('requestMetric').textContent = Number(data.totals.requests).toLocaleString();
    byId('requestRateMetric').textContent = `${Number(latest.requests || 0).toLocaleString()} per second`;
    byId('networkMetric').textContent = formatBytes(Number(data.totals.inboundBytes) + Number(data.totals.outboundBytes));
    byId('networkRateMetric').textContent = formatRate(networkRate);
    byId('resourceMetric').textContent = `${Number(data.server.cpuPercent).toFixed(1)}% / ${formatBytes(data.server.memoryBytes)}`;
    byId('uptimeMetric').textContent = `${formatUptime(data.server.uptimeSeconds)} uptime`;
    byId('errorMetric').textContent = Number(data.totals.errors).toLocaleString();
    byId('errorRateMetric').textContent = `${errorRate.toFixed(2)}% of requests`;
    byId('chatMetric').textContent = Number(data.totals.chatMessages).toLocaleString();
    byId('chatThreadMetric').textContent = `${Number(data.totals.chatThreads).toLocaleString()} threads`;
    renderOnline(data.onlinePlayers || []);
    renderRequests();
    renderChats();
    drawAllCharts();
}
function renderOnline(players) {
    byId('onlineCountBadge').textContent = `${players.length} online`;
    byId('onlinePlayers').innerHTML = players.length ? players.map(player => `
    <article class="online-player">
      <img src="${esc(player.profileImage)}" alt="">
      <div><strong>${esc(player.displayName || player.username || `Player ${player.accountId}`)}</strong><small>${esc(player.room || 'Online')} · ${esc(player.device || 'Unknown')}</small></div>
      <span class="socket-pill">${Number(player.sockets) || 0} WS</span>
    </article>`).join('') : '<div class="empty">Nobody is online right now.</div>';
}
function renderRequests() {
    const filter = byId('requestFilter').value.trim().toLowerCase();
    const rows = state.requests.filter(item => !filter || `${item.status} ${item.method} ${item.path}`.toLowerCase().includes(filter)).slice(0, 500);
    byId('requestRows').innerHTML = rows.length ? rows.map(item => {
        const statusClass = item.status >= 500 ? 'error' : item.status >= 400 ? 'warn' : '';
        return `<tr><td>${formatTime(item.at)}</td><td><span class="status ${statusClass}">${item.status}</span></td><td class="method">${esc(item.method)}</td><td class="request-path" title="${esc(item.path)}">${esc(item.path)}</td><td>${Number(item.durationMs).toLocaleString()} ms</td><td>${formatBytes(item.outboundBytes)}</td></tr>`;
    }).join('') : '<tr><td colspan="6" class="empty-cell">No requests match this filter.</td></tr>';
}
function renderChats() {
    const filter = byId('chatFilter').value.trim().toLowerCase();
    const rows = state.chats.filter(item => !filter || `${item.sender?.displayName} ${item.sender?.username} ${item.body} ${item.threadName} ${(item.members || []).map(x => x.displayName).join(' ')}`.toLowerCase().includes(filter));
    byId('chatRows').innerHTML = rows.length ? rows.map(item => `
    <article class="chat-item">
      <div class="chat-meta"><strong>${esc(item.sender?.displayName || 'Unknown')}</strong><span class="chat-thread">${esc(item.threadName || `Thread ${item.threadId}`)}</span><span class="chat-time">${formatTime(item.createdAt)}</span></div>
      <div class="chat-body">${esc(item.body)}</div>
      <div class="chat-members">${esc((item.members || []).map(member => member.displayName).join(', '))}</div>
    </article>`).join('') : '<div class="empty">No chats match this filter.</div>';
}
function setupCanvas(canvas) {
    const ratio = window.devicePixelRatio || 1;
    const rect = canvas.getBoundingClientRect();
    canvas.width = Math.max(1, Math.round(rect.width * ratio));
    canvas.height = Math.max(1, Math.round(rect.height * ratio));
    const ctx = canvas.getContext('2d');
    ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
    return { ctx, width: rect.width, height: rect.height };
}
function drawChart(canvas, series, lines) {
    const { ctx, width, height } = setupCanvas(canvas), pad = { left: 38, right: 18, top: 16, bottom: 25 }, plotW = width - pad.left - pad.right, plotH = height - pad.top - pad.bottom;
    ctx.clearRect(0, 0, width, height);
    ctx.strokeStyle = 'rgba(130,115,145,.18)';
    ctx.lineWidth = 1;
    ctx.fillStyle = '#786f80';
    ctx.font = '10px Inter, sans-serif';
    for (let i = 0; i <= 4; i++) {
        const y = pad.top + plotH * i / 4;
        ctx.beginPath();
        ctx.moveTo(pad.left, y);
        ctx.lineTo(width - pad.right, y);
        ctx.stroke();
    }
    if (!series.length)
        return;
    const normalized = lines.map(line => ({ line, values: series.map(item => Number(line.value(item)) || 0) }));
    const max = Math.max(1, ...normalized.flatMap(item => item.values)) * 1.12;
    ctx.fillText(max >= 1048576 ? formatBytes(max) : max.toFixed(max < 10 ? 1 : 0), 3, pad.top + 4);
    ctx.fillText('0', 20, pad.top + plotH + 3);
    normalized.forEach(({ line, values }) => {
        ctx.beginPath();
        ctx.strokeStyle = line.color;
        ctx.lineWidth = 2;
        ctx.lineJoin = 'round';
        ctx.lineCap = 'round';
        values.forEach((value, index) => { const x = pad.left + (values.length === 1 ? plotW : plotW * index / (values.length - 1)), y = pad.top + plotH - (value / max * plotH); index ? ctx.lineTo(x, y) : ctx.moveTo(x, y); });
        ctx.stroke();
    });
    const first = new Date(series[0].at), last = new Date(series.at(-1).at);
    ctx.fillStyle = '#786f80';
    ctx.fillText(formatTime(first), pad.left, height - 7);
    const label = formatTime(last);
    ctx.fillText(label, width - pad.right - ctx.measureText(label).width, height - 7);
}
function drawAllCharts() {
    if (!state.snapshot)
        return;
    const series = state.snapshot.series || [];
    drawChart(byId('requestChart'), series, [{ color: '#9c63f5', value: x => x.requests }, { color: '#ffab5c', value: x => x.latencyMs }]);
    drawChart(byId('networkChart'), series, [{ color: '#4da3ff', value: x => x.inboundBytes }, { color: '#42d6a4', value: x => x.outboundBytes }]);
    const resources = state.cpuHistory.map((point, index) => ({ at: point.at, cpu: point.value, memory: state.memoryHistory[index]?.value || 0 }));
    drawChart(byId('resourceChart'), resources, [{ color: '#9c63f5', value: x => x.cpu }, { color: '#f373d1', value: x => x.memory }]);
}
async function loadOlderChats() {
    const button = byId('loadOlderChats'), oldest = state.chats.length ? Math.min(...state.chats.map(item => Number(item.messageId))) : null;
    button.disabled = true;
    button.textContent = 'Loading…';
    try {
        const url = `/recnet/api/developer/chats?take=100${oldest ? `&beforeMessageId=${oldest}` : ''}`;
        const response = await recnetFetch(url);
        if (response.status === 403) {
            location.href = '/recnet/';
            return;
        }
        if (!response.ok)
            throw new Error('Could not load older chats.');
        const data = await response.json();
        state.chats = mergeBy(state.chats, data.messages || [], item => item.messageId, 10000);
        renderChats();
        button.hidden = !data.hasMore;
    }
    catch (error) {
        button.textContent = error.message;
        return;
    }
    button.disabled = false;
    button.textContent = 'Load older messages';
}
function connect() {
    state.stream?.close();
    setStreamState('connecting', 'Connecting');
    const stream = new EventSource('/recnet/api/developer/stream');
    state.stream = stream;
    stream.addEventListener('snapshot', event => {
        setStreamState('', 'Live');
        renderSnapshot(JSON.parse(event.data));
    });
    stream.onerror = () => {
        setStreamState('offline', 'Reconnecting');
    };
}
byId('requestFilter').addEventListener('input', renderRequests);
byId('chatFilter').addEventListener('input', renderChats);
byId('pauseRequests').addEventListener('click', event => {
    state.paused = !state.paused;
    event.currentTarget.classList.toggle('active', state.paused);
    event.currentTarget.textContent = state.paused ? 'Resume' : 'Pause';
});
byId('loadOlderChats').addEventListener('click', loadOlderChats);
window.addEventListener('resize', () => requestAnimationFrame(drawAllCharts));
window.addEventListener('beforeunload', () => state.stream?.close());
connect();
