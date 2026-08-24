const recnetFetch = (input, init = {}) => fetch(input, { ...init, credentials: init.credentials ?? 'same-origin' });
const app = document.querySelector('#app'), photoDialog = document.querySelector('#photoDialog'), photoContent = document.querySelector('#photoContent');
let currentUser = null;
const esc = s => String(s ?? '').replace(/[&<>'"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' }[c]));
function when(value) { const date = value instanceof Date ? value : new Date(value); return Number.isNaN(date.getTime()) ? 'Unknown date' : new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }).format(date); }
function avatarMarkup(person) { return `<span class="avatar-wrap"><img class="avatar" src="${esc(person.profileImage)}" alt="">${person.verified ? '<span class="verified-badge" title="Verified" aria-label="Verified"><i class="fa-solid fa-circle-check" aria-hidden="true"></i></span>' : ''}</span>`; }
function rrPlusMarkup(person) { return person.hasRRPlus ? '<span class="rrplus-inline" title="RR+ member"><img src="/recnet/rrplus.png" alt="RR+"></span>' : ''; }
async function get(url) { const r = await recnetFetch(url, { headers: { Accept: 'application/json' } }); if (!r.ok) {
    const body = await r.json().catch(() => ({}));
    throw new Error(body.error || body.message || `Could not load this page (${r.status})`);
} return r.json(); }
function inlineMarkdown(value) {
    let text = String(value ?? ''), tokens = [];
    const hold = html => `MOCHATOKEN${tokens.push(html) - 1}END`;
    text = text.replace(/`([^`\n]+)`/g, (_, code) => hold(`<code>${esc(code)}</code>`));
    text = text.replace(/\[([^\]\n]+)\]\((https?:\/\/[^\s)]+)\)/gi, (_, label, url) => hold(`<a href="${esc(url)}" target="_blank" rel="noopener noreferrer">${esc(label)}</a>`));
    text = esc(text)
        .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
        .replace(/__([^_]+)__/g, '<strong>$1</strong>')
        .replace(/~~([^~]+)~~/g, '<del>$1</del>')
        .replace(/(^|[^*])\*([^*\n]+)\*(?!\*)/g, '$1<em>$2</em>')
        .replace(/(^|[^_])_([^_\n]+)_(?!_)/g, '$1<em>$2</em>');
    return text.replace(/MOCHATOKEN(\d+)END/g, (_, index) => tokens[Number(index)] || '');
}
function markdownToHtml(value) {
    const lines = String(value ?? '').replace(/\r/g, '').split('\n'), html = [];
    let paragraph = [], listType = null, inFence = false, fenceLines = [];
    const flushParagraph = () => { if (paragraph.length) {
        html.push(`<p>${paragraph.join('<br>')}</p>`);
        paragraph = [];
    } };
    const closeList = () => { if (listType) {
        html.push(`</${listType}>`);
        listType = null;
    } };
    for (const rawLine of lines) {
        if (/^```/.test(rawLine)) {
            flushParagraph();
            closeList();
            if (inFence) {
                html.push(`<pre><code>${esc(fenceLines.join('\n'))}</code></pre>`);
                fenceLines = [];
                inFence = false;
            }
            else
                inFence = true;
            continue;
        }
        if (inFence) {
            fenceLines.push(rawLine);
            continue;
        }
        if (!rawLine.trim()) {
            flushParagraph();
            closeList();
            continue;
        }
        const heading = rawLine.match(/^(#{1,4})\s+(.+)$/);
        if (heading) {
            flushParagraph();
            closeList();
            const level = heading[1].length;
            html.push(`<h${level}>${inlineMarkdown(heading[2])}</h${level}>`);
            continue;
        }
        if (/^\s*(---+|___+|\*\*\*+)\s*$/.test(rawLine)) {
            flushParagraph();
            closeList();
            html.push('<hr>');
            continue;
        }
        const quote = rawLine.match(/^>\s?(.*)$/);
        if (quote) {
            flushParagraph();
            closeList();
            html.push(`<blockquote>${inlineMarkdown(quote[1])}</blockquote>`);
            continue;
        }
        const unordered = rawLine.match(/^\s*[-+*]\s+(.+)$/), ordered = rawLine.match(/^\s*\d+\.\s+(.+)$/), item = unordered || ordered;
        if (item) {
            flushParagraph();
            const wanted = unordered ? 'ul' : 'ol';
            if (listType !== wanted) {
                closeList();
                listType = wanted;
                html.push(`<${wanted}>`);
            }
            html.push(`<li>${inlineMarkdown(item[1])}</li>`);
            continue;
        }
        closeList();
        paragraph.push(inlineMarkdown(rawLine));
    }
    if (inFence)
        html.push(`<pre><code>${esc(fenceLines.join('\n'))}</code></pre>`);
    flushParagraph();
    closeList();
    return html.join('');
}
function announcementCard(item, admin = false) {
    const kinds = { info: ['fa-circle-info', 'Info'], update: ['fa-wand-magic-sparkles', 'Update'], warning: ['fa-triangle-exclamation', 'Warning'], maintenance: ['fa-screwdriver-wrench', 'Maintenance'] }, kind = String(item.kind || 'info').toLowerCase(), meta = kinds[kind] || kinds.info;
    return `<article class="announcement-card announcement-${esc(kind)}${item.pinned ? ' pinned' : ''}${!item.published ? ' unpublished' : ''}" data-announcement-id="${Number(item.id) || 0}"><header><span class="announcement-kind"><i class="fa-solid ${meta[0]}"></i> ${meta[1]}</span>${item.pinned ? '<span class="announcement-pin"><i class="fa-solid fa-thumbtack"></i> Pinned</span>' : ''}${admin && !item.published ? '<span class="announcement-draft">Unpublished</span>' : ''}<time>${when(item.updatedAt || item.createdAt)}</time></header><h3>${esc(item.title)}</h3><div class="markdown-body">${markdownToHtml(item.bodyMarkdown)}</div>${admin ? `<footer><span>By ${esc(item.authorName || 'Staff')}</span><div><button type="button" class="secondary-button small-button" data-announcement-action="edit" data-announcement-id="${Number(item.id) || 0}"><i class="fa-solid fa-pen"></i> Edit</button><button type="button" class="danger-button small-button" data-announcement-action="delete" data-announcement-id="${Number(item.id) || 0}"><i class="fa-solid fa-trash"></i> Delete</button></div></footer>` : ''}</article>`;
}
function photoCard(p) { const o = p.owner; return `<article class="photo-card"><button class="photo-open" data-photo-path="${esc(p.path)}" aria-label="Open photo"><img class="photo-image" loading="lazy" src="${esc(p.url)}" alt="Community photo"></button><div class="photo-meta">${o ? `${avatarMarkup(o)}<div><div class="owner-row"><a class="owner" href="#user/${o.accountId}">${esc(o.displayName || o.username || 'Player')}</a>${rrPlusMarkup(o)}</div><div class="date">${when(p.takenAt)}</div></div>` : `<div><span class="owner">Community photo</span><div class="date">${when(p.takenAt)}</div></div>`}</div></article>`; }
function featureCard(icon, title, desc) { return `<div class="feature-card"><div class="feature-icon"><i class="fa-solid ${icon}"></i></div><h3>${title}</h3><p>${desc}</p></div>`; }
function howStep(n, title, desc) { return `<div class="how-step"><span class="how-step-num">${n}</span><h3>${title}</h3><p>${desc}</p></div>`; }
async function landing() {
    app.innerHTML = '<div class="loading"><i></i>Loading Mocha</div>';
    const [rooms, photos, events, status, announcements] = await Promise.all([get('/recnet/api/rooms?search=').catch(() => []), get('/recnet/api/photos/newest?take=8').catch(() => []), get('/recnet/api/events').catch(() => []), get('/recnet/api/status').catch(() => ({ status: 'online', rooms: 0, registeredPlayers: 0, onlinePlayers: 0 })), get('/recnet/api/announcements').catch(() => [])]);
    const previewRooms = rooms.slice(0, 6);
    app.innerHTML = `
  <section class="hero landing-hero"><span class="hero-chip">Welcome to</span><h1>Mocha</h1><p>Mocha is a community server built around the games and spaces people made. Log in with your platform account to explore player-made rooms, share photos from your sessions, and pick up new gear from the shop.</p><div class="landing-cta"><button id="landingLogin" class="primary-button">Log in</button><button id="landingRegister" class="ghost-button">Create an account</button></div></section>
  <section class="stats-strip"><div class="stat-pill"><i class="fa-solid fa-door-open"></i><div><strong>${Number(status.rooms || rooms.length).toLocaleString()}</strong><span>Rooms built</span></div></div><div class="stat-pill"><i class="fa-solid fa-users"></i><div><strong>${Number(status.registeredPlayers || 0).toLocaleString()}</strong><span>Players joined</span></div></div><div class="stat-pill"><i class="fa-solid fa-signal"></i><div><strong>${Number(status.onlinePlayers || 0).toLocaleString()}</strong><span>Online now</span></div></div></section>
  ${announcements.length ? `<div class="section-head"><h2>Announcements</h2></div><section class="announcement-feed">${announcements.map(item => announcementCard(item)).join('')}</section>` : ''}
  ${events.length ? `<div class="section-head"><h2>Upcoming events</h2></div><section class="event-row">${events.map(eventCard).join('')}</section>` : ''}
  <div class="section-head"><h2>Rooms people are building</h2><button id="landingSeeRooms">See all rooms</button></div>
  ${previewRooms.length ? `<section class="room-grid home-room-row">${previewRooms.map(roomCard).join('')}</section>` : '<div class="empty">No rooms have been created yet.</div>'}
  <div class="section-head"><h2>Fresh from the community</h2><button id="landingSeePlayers">Find players</button></div>
  ${photos.length ? `<section class="photo-grid">${photos.map(photoCard).join('')}</section>` : '<div class="empty">No photos have been uploaded yet.</div>'}
  <div class="section-head"><h2>How it works</h2></div>
  <section class="how-steps">${howStep(1, 'Create an account', 'Link a platform ID and pick a username. Takes less than a minute.')}${howStep(2, 'Set up your profile', 'Add a profile picture, banner, bio, and cheer badge so people know it&rsquo;s you.')}${howStep(3, 'Jump into rooms', 'Browse what the community has built, hang out, and share the moments worth keeping.')}</section>
  <div class="section-head"><h2>What you can do here</h2></div>
  <section class="landing-features">${featureCard('fa-door-open', 'Explore rooms', 'Browse every room on the server, see who made it and how many people have visited, and jump in.')}${featureCard('fa-image', 'Share photos', 'Post photos from your sessions, cheer the ones you love, and leave comments.')}${featureCard('fa-bag-shopping', 'Shop for gear', 'Spend tokens on a daily rotation of items, from common finds to legendary drops.')}${featureCard('fa-user', 'Customize your profile', 'Set a banner, profile picture, bio, and cheer badge so your nametag stands out.')}</section>
  <section class="cta-band"><div><h2>Ready to jump in?</h2><p>Create an account and start exploring in under a minute.</p></div><button id="landingCtaRegister" class="primary-button">Create an account</button></section>`;
    document.querySelector('#landingLogin').onclick = () => { loginError.textContent = ''; loginDialog.showModal(); };
    const openRegister = () => { loginDialog.close(); registerError.textContent = ''; registerDialog.showModal(); };
    document.querySelector('#landingRegister').onclick = openRegister;
    document.querySelector('#landingCtaRegister').onclick = openRegister;
    document.querySelector('#landingSeeRooms').onclick = () => location.hash = 'rooms';
    document.querySelector('#landingSeePlayers').onclick = () => location.hash = 'users';
}
async function home() {
    app.innerHTML = '<div class="loading"><i></i>Loading your community</div>';
    const [rooms, photos, events, status, announcements] = await Promise.all([get('/recnet/api/rooms?search='), get('/recnet/api/photos/newest?take=24'), get('/recnet/api/events').catch(() => []), get('/recnet/api/status'), get('/recnet/api/announcements').catch(() => [])]);
    const featured = rooms.slice(0, 8), name = currentUser?.displayName || currentUser?.username || 'Player';
    app.innerHTML = `
    <section class="hero home-hero">
      <div class="home-hero-content"><span class="hero-chip">Your community</span><h1>Welcome back, ${esc(name)}!</h1><p>See what people are building, catch the newest moments, and find somewhere to jump in.</p><div class="home-hero-actions"><a class="hero-action primary" href="#rooms"><i class="fa-solid fa-door-open"></i> Explore rooms</a><a class="hero-action" href="#user/${currentUser.accountId}"><i class="fa-solid fa-user"></i> View profile</a></div></div>
      <div class="home-live"><div><strong>${Number(status.onlinePlayers).toLocaleString()}</strong><span>players online</span></div></div>
    </section>
    <section class="home-overview">
      <article class="overview-card"><span class="overview-icon"><i class="fa-solid fa-signal"></i></span><div><strong>${Number(status.onlinePlayers).toLocaleString()}</strong><span>Online now</span></div></article>
      <article class="overview-card"><span class="overview-icon"><i class="fa-solid fa-door-open"></i></span><div><strong>${Number(status.rooms).toLocaleString()}</strong><span>Rooms</span></div></article>
      <article class="overview-card"><span class="overview-icon"><i class="fa-solid fa-camera"></i></span><div><strong>${Number(status.photos).toLocaleString()}</strong><span>Community photos</span></div></article>
      <article class="overview-card"><span class="overview-icon"><i class="fa-solid fa-users"></i></span><div><strong>${Number(status.registeredPlayers).toLocaleString()}</strong><span>Players joined</span></div></article>
    </section>
    ${announcements.length ? `<div class="section-head"><h2>Announcements</h2></div><section class="announcement-feed">${announcements.map(item => announcementCard(item)).join('')}</section>` : ''}
    <div class="section-head"><h2>Quick actions</h2></div>
    <section class="quick-grid"><a class="quick-card" href="#rooms"><i class="fa-solid fa-compass"></i><div><strong>Discover a room</strong><small>Browse everything the community has built.</small></div></a><a class="quick-card" href="#users"><i class="fa-solid fa-user-group"></i><div><strong>Find players</strong><small>Open profiles and see their latest photos.</small></div></a><a class="quick-card" href="#shop"><i class="fa-solid fa-bag-shopping"></i><div><strong>Check the shop</strong><small>See today&rsquo;s item rotation and your balance.</small></div></a></section>
    ${events.length ? `<div class="section-head"><h2>Upcoming events</h2></div><section class="event-row">${events.map(eventCard).join('')}</section>` : ''}
    <div class="section-head"><h2>Featured rooms</h2><button id="seeRooms">See all rooms</button></div>
    ${featured.length ? `<section class="room-grid home-room-row">${featured.map(roomCard).join('')}</section>` : '<div class="empty">No rooms have been created yet.</div>'}
    <div class="section-head"><h2>Latest photos</h2><button id="findPlayers">Find players</button></div>
    ${photos.length ? `<section class="photo-grid">${photos.map(photoCard).join('')}</section>` : '<div class="empty">No photos have been uploaded yet.</div>'}`;
    document.querySelector('#findPlayers').onclick = () => location.hash = 'users';
    document.querySelector('#seeRooms').onclick = () => location.hash = 'rooms';
}
async function users(term = '') { app.innerHTML = `<div class="page-kicker">Community</div><h1 class="page-title">People</h1><div class="subtitle">Discover players and view their latest photos.</div><div class="search-panel"><input class="search" placeholder="Search by display name or username" value="${esc(term)}" autofocus></div><section class="user-grid"></section>`; const input = app.querySelector('.search'), grid = app.querySelector('.user-grid'); let timer; async function load() { grid.innerHTML = '<div class="loading"><i></i>Finding players</div>'; const data = await get('/recnet/api/users?search=' + encodeURIComponent(input.value)); grid.innerHTML = data.length ? data.map(u => `<a class="user-card" href="#user/${u.accountId}">${avatarMarkup(u)}<div><div class="name-row"><div class="name">${esc(u.displayName || u.username || 'Player')}</div>${rrPlusMarkup(u)}</div><div class="muted">@${esc(u.username || 'unknown')} &middot; Level ${u.level} &middot; ${u.photoCount} photos</div><div class="bio">${esc(u.bio || 'No bio yet.')}</div></div></a>`).join('') : '<div class="empty">No players match that search.</div>'; } input.addEventListener('input', () => { clearTimeout(timer); timer = setTimeout(load, 180); }); load(); }
async function profile(id) {
    app.innerHTML = '<div class="loading"><i></i>Loading profile</div>';
    const u = await get('/recnet/api/users/' + encodeURIComponent(id));
    const isOwner = currentUser && Number(currentUser.accountId) === Number(u.accountId);
    app.innerHTML = `<div class="profile-layout"><aside class="profile-side"><div class="profile-banner"${u.bannerImage ? ` style="background-image:url('${esc(u.bannerImage)}')"` : ''}></div><div class="profile-avatar-row">${avatarMarkup(u)}</div><div class="profile-side-body"><h1>${esc(u.displayName || u.username || 'Player')}${rrPlusMarkup(u)}</h1><div class="muted">@${esc(u.username || 'unknown')}</div><div class="chips"><span class="chip">Level ${u.level}</span><span class="chip">${u.photoCount} photos</span>${u.roles.filter(r => r !== 'RRPlus' && r !== 'Influencer').map(r => `<span class="chip">${esc(r)}</span>`).join('')}${u.verified ? '<span class="chip">Verified</span>' : ''}${u.hasRRPlus ? '<span class="chip">RR+</span>' : ''}</div>${isOwner ? '<button id="setBannerButton" class="primary-button" type="button">Set banner</button>' : ''}<div class="profile-side-rule"></div><p class="profile-bio">${esc(u.bio || 'This player has not written a bio yet.')}</p><div class="profile-joined"><i class="fa-solid fa-calendar"></i> Joined ${when(u.createdAt)}</div></div></aside><div class="profile-main"><div class="section-head"><h2>Photos</h2><span class="muted">Newest first</span></div>${u.photos.length ? `<section class="photo-grid">${u.photos.map(photoCard).join('')}</section>` : '<div class="empty">This player has not uploaded any photos yet.</div>'}</div></div>`;
    document.querySelector('#setBannerButton')?.addEventListener('click', async () => {
        const suggestion = u.photos[0]?.path || '';
        const image = prompt('Enter an image path, such as PlayerImages/photo.png', suggestion);
        if (!image)
            return;
        const form = new FormData();
        form.append('bannerImage', image);
        const response = await recnetFetch('/acc/account/me/bannerimage', { method: 'PUT', body: form });
        if (!response.ok) {
            alert('That banner could not be saved.');
            return;
        }
        await profile(id);
    });
}
async function openPhoto(path) {
    if (!photoDialog.open)
        photoDialog.showModal();
    photoContent.innerHTML = '<div class="loading"><i></i>Loading photo</div>';
    try {
        const p = await get('/recnet/api/photos/detail?path=' + encodeURIComponent(path));
        const comments = p.comments.map(c => c.author ? `<article class="comment">${avatarMarkup(c.author)}<div class="comment-body"><div class="comment-name">${esc(c.author.displayName || c.author.username || 'Player')}</div><div class="comment-text">${esc(c.text)}</div><div class="comment-date">${when(c.createdAt)}</div></div></article>` : `<article class="comment"><div class="comment-body"><div class="comment-name">Deleted player</div><div class="comment-text">${esc(c.text)}</div><div class="comment-date">${when(c.createdAt)}</div></div></article>`).join('');
        photoContent.innerHTML = `<div class="photo-stage"><img class="photo-full" src="${esc(p.url)}" alt="Community photo"></div><aside class="photo-side"><div class="photo-author">${p.owner ? `${avatarMarkup(p.owner)}<div><div class="owner-row"><a class="owner" href="#user/${p.owner.accountId}">${esc(p.owner.displayName || p.owner.username || 'Player')}</a>${rrPlusMarkup(p.owner)}</div><div class="date">${when(p.takenAt)}</div></div>` : '<div><strong>Community photo</strong><div class="date">' + when(p.takenAt) + '</div></div>'}</div><div class="photo-actions"><button id="photoCheer" class="cheer-button ${p.cheered ? 'cheered' : ''}"><i class="fa-solid fa-heart"></i><span>${p.cheerCount} ${p.cheerCount === 1 ? 'Cheer' : 'Cheers'}</span></button></div><div class="comment-title">Comments (${p.comments.length})</div><div class="comment-list">${comments || '<div class="no-comments">No comments yet. Be the first!</div>'}</div>${currentUser ? '<form id="commentForm" class="comment-form"><input name="text" maxlength="300" placeholder="Add a comment..." required><button aria-label="Post comment"><i class="fa-solid fa-paper-plane"></i></button></form>' : '<div class="comment-login"><button id="photoLogin">Log in</button> to cheer or comment.</div>'}</aside>`;
        document.querySelector('#photoCheer').onclick = async () => { if (!currentUser) {
            alert('Log in to cheer photos.');
            return;
        } const response = await recnetFetch('/recnet/api/photos/cheer', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ path: p.path }) }); if (response.ok)
            await openPhoto(p.path); };
        document.querySelector('#commentForm')?.addEventListener('submit', async (e) => { e.preventDefault(); const text = e.currentTarget.elements.text.value.trim(); if (!text)
            return; const response = await recnetFetch('/recnet/api/photos/comments', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ path: p.path, text }) }); if (response.ok)
            await openPhoto(p.path); });
        document.querySelector('#photoLogin')?.addEventListener('click', () => { photoDialog.close(); loginDialog.showModal(); });
    }
    catch (error) {
        photoContent.innerHTML = `<div class="empty">${esc(error.message)}</div>`;
    }
}
function eventCard(e) { const starts = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(e.startsAt)); return `<article class="event-card${e.pinned ? ' pinned' : ''}">${e.image ? `<img class="event-image" loading="lazy" src="${esc(e.image)}" alt="">` : ''}<div class="event-body">${e.pinned ? '<span class="event-pinned-badge"><i class="fa-solid fa-thumbtack"></i> Pinned</span>' : ''}<div class="event-date"><i class="fa-solid fa-calendar-days"></i> ${starts}</div><h3>${esc(e.title)}</h3><p>${esc(e.description || '')}</p></div></article>`; }
function roomCard(r) { return `<a class="room-card" href="#room/${r.roomId}"><img class="room-cover" loading="lazy" src="${esc(r.image)}" alt=""><div class="room-card-body"><div class="room-name">^${esc(r.name || 'UntitledRoom')}</div><div class="room-description">${esc(r.description || 'No description has been added yet.')}</div><div class="room-meta"><span>by ${esc(r.creatorName)}</span><span class="room-pill">${esc(r.accessibility)}</span></div><div class="room-stats"><span><i class="fa-solid fa-eye"></i> ${r.stats.visits}</span><span><i class="fa-solid fa-heart"></i> ${r.stats.cheers}</span><span><i class="fa-solid fa-user-group"></i> ${r.maxPlayers}</span></div></div></a>`; }
async function rooms(term = '') {
    app.innerHTML = `<div class="page-kicker">Discover</div><h1 class="page-title">Rooms</h1><div class="subtitle">Explore every room on this server.</div><div class="search-panel"><input class="search" placeholder="Search rooms, descriptions, or tags" value="${esc(term)}" autofocus></div><section class="room-grid"></section>`;
    const input = app.querySelector('.search'), grid = app.querySelector('.room-grid');
    let timer;
    async function load() { grid.innerHTML = '<div class="loading"><i></i>Loading rooms</div>'; const data = await get('/recnet/api/rooms?search=' + encodeURIComponent(input.value)); grid.innerHTML = data.length ? data.map(roomCard).join('') : '<div class="empty">No rooms match that search.</div>'; }
    input.addEventListener('input', () => { clearTimeout(timer); timer = setTimeout(load, 180); });
    load();
}
async function roomDetail(id) {
    app.innerHTML = '<div class="loading"><i></i>Loading room</div>';
    const r = await get('/recnet/api/rooms/' + encodeURIComponent(id));
    app.innerHTML = `<div class="room-hero"><img class="room-detail-cover" src="${esc(r.image)}" alt="${esc(r.name)}"><div class="room-hero-info"><div class="page-kicker">${r.isRRO ? 'Rec Room Original' : 'Community room'}</div><h1>^${esc(r.name || 'UntitledRoom')}</h1><a class="owner" href="#user/${r.creatorAccountId}">Created by ${esc(r.creatorName)}</a><p class="room-hero-desc">${esc(r.description || 'No description has been added yet.')}</p>${r.tags.length ? `<div class="tag-list">${r.tags.map(t => `<span class="chip">${esc(t)}</span>`).join('')}</div>` : ''}<div class="tag-list"><span class="room-pill">${esc(r.accessibility)}</span>${r.supportsJuniors ? '<span class="room-pill">Junior friendly</span>' : ''}${r.isDorm ? '<span class="room-pill">Dorm</span>' : ''}</div></div></div><div class="room-detail-stats"><div class="room-stat"><strong>${r.stats.visits}</strong><span class="muted">Visits</span></div><div class="room-stat"><strong>${r.stats.visitors}</strong><span class="muted">Visitors</span></div><div class="room-stat"><strong>${r.stats.cheers}</strong><span class="muted">Cheers</span></div><div class="room-stat"><strong>${r.stats.favorites}</strong><span class="muted">Favorites</span></div></div><section class="settings-card"><h2 class="settings-title">Room details</h2><div class="settings-grid"><div><strong>Max players</strong><div class="muted">${r.maxPlayers}</div></div><div><strong>Created</strong><div class="muted">${when(r.createdAt)}</div></div><div><strong>Status</strong><div class="muted">${esc(r.state || 'Unknown')}</div></div><div><strong>Subrooms</strong><div class="muted">${r.subRooms.length}</div></div></div></section>`;
}
function shopFallbackIcon(name) { name = String(name || '').toLowerCase(); if (/hair|hat|helmet|cap|visor/.test(name))
    return 'fa-hat-wizard'; if (/glove|gauntlet|wrist|hand/.test(name))
    return 'fa-hand'; if (/glass|goggle/.test(name))
    return 'fa-glasses'; if (/bow|tie|scarf|ribbon/.test(name))
    return 'fa-ribbon'; if (/dress|shirt|jersey|jacket|hood|harness|robe/.test(name))
    return 'fa-shirt'; return 'fa-gem'; }
async function shop() {
    app.innerHTML = '<div class="loading"><i></i>Opening Mocha Shop</div>';
    const data = await get('/recnet/api/shop');
    const rarityName = stars => ['', 'Common', 'Uncommon', 'Rare', 'Epic', 'Legendary'][stars] || 'Featured';
    app.innerHTML = `<section class="shop-hero"><div><div class="page-kicker">Daily rotation</div><h1>Mocha Shop</h1><p>Fresh legendary items every day, plus anything the staff pins into the rotation.</p></div><div class="shop-wallet"><i class="fa-solid fa-coins"></i><strong id="shopBalance">${data.balance == null ? 'Log in' : Number(data.balance).toLocaleString()}</strong><span>${data.balance == null ? 'to purchase' : 'tokens'}</span></div></section><div class="section-head shop-section-head"><div><h2>Today&rsquo;s items</h2><span class="muted">Refreshes ${when(data.nextRefresh)}</span></div><span id="shopMessage" class="shop-message"></span></div><section id="shopGrid" class="shop-grid">${data.items.length ? data.items.map(item => `<article class="shop-card rarity-${item.stars}"><div class="shop-art">${item.thumbnailUrl ? `<img class="shop-image" src="${esc(item.thumbnailUrl)}" alt="${esc(item.friendlyName)}">` : ''}<div class="shop-image-fallback"${item.thumbnailUrl ? ' hidden' : ''}><i class="fa-solid ${shopFallbackIcon(item.friendlyName)}"></i></div><span class="shop-stars" aria-label="${item.stars} stars">${'★'.repeat(item.stars)}</span></div><div class="shop-card-body"><span class="shop-rarity">${rarityName(item.stars)}</span><h3>${esc(item.friendlyName)}</h3><div class="shop-price"><i class="fa-solid fa-coins"></i>${Number(item.price).toLocaleString()}</div><button type="button" class="shop-buy-button" data-sku="${item.skuId}" ${item.owned ? 'disabled' : ''}>${item.owned ? '<i class="fa-solid fa-check"></i> Owned' : data.loggedIn ? '<i class="fa-solid fa-bag-shopping"></i> Buy item' : '<i class="fa-solid fa-right-to-bracket"></i> Log in to buy'}</button></div></article>`).join('') : '<div class="empty">The shop is restocking. Try again in a moment.</div>'}</section>`;
    app.querySelectorAll('.shop-image').forEach(image => image.addEventListener('error', () => { image.hidden = true; image.nextElementSibling.hidden = false; }));
    document.querySelector('#shopGrid').addEventListener('click', async (e) => { const button = e.target.closest('.shop-buy-button'); if (!button || button.disabled)
        return; if (!currentUser) {
        loginError.textContent = '';
        loginDialog.showModal();
        return;
    } const message = document.querySelector('#shopMessage'); try {
        button.disabled = true;
        button.innerHTML = '<i class="fa-solid fa-spinner fa-spin"></i> Purchasing';
        const response = await recnetFetch('/recnet/api/shop/purchase', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ skuId: Number(button.dataset.sku) }) }), body = await response.json().catch(() => ({}));
        if (!response.ok)
            throw new Error(body.error || 'Purchase failed.');
        button.innerHTML = '<i class="fa-solid fa-check"></i> Owned';
        document.querySelector('#shopBalance').textContent = Number(body.balance).toLocaleString();
        message.className = 'shop-message success';
        message.textContent = body.alreadyOwned ? 'You already own that item.' : `${body.itemName} purchased!`;
    }
    catch (error) {
        button.disabled = false;
        button.innerHTML = '<i class="fa-solid fa-bag-shopping"></i> Buy item';
        message.className = 'shop-message error';
        message.textContent = error.message;
    } });
}
function initCommunityBoardAdmin(adminAction) {
    const panel = document.querySelector('#adminCommunityBoard'), saveButton = document.querySelector('#communityBoardSave'), reloadButton = document.querySelector('#communityBoardReload'), status = document.querySelector('#communityBoardStatus'), featured = document.querySelector('#communityBoardFeatured'), rooms = document.querySelector('#communityBoardRooms'), photos = document.querySelector('#communityBoardPhotos'), videos = document.querySelector('#communityBoardVideos'), dialog = document.querySelector('#communityBoardDialog'), dialogTitle = document.querySelector('#communityBoardDialogTitle'), dialogBody = document.querySelector('#communityBoardDialogBody'), dialogForm = document.querySelector('#communityBoardDialogForm'), dialogDelete = document.querySelector('#communityBoardDialogDelete');
    let board = null, featuredUser = null, dirty = false, editor = { type: '', index: -1 }, previewObjectUrls = [];
    const setStatus = (message, error = false) => { status.className = 'community-board-status' + (error ? ' error' : ''); status.textContent = message; };
    const markDirty = () => { dirty = true; saveButton.disabled = false; setStatus('Unsaved changes'); };
    async function persistBoard(successMessage = 'Saved — live board updated') {
        saveButton.disabled = true;
        setStatus('Saving live board...');
        try {
            board = normalizeBoard(await adminAction('/recnet/api/admin/community-board', 'PUT', board));
            dirty = false;
            await loadFeaturedUser();
            render();
            setStatus(successMessage);
            return board;
        }
        catch (error) {
            saveButton.disabled = false;
            setStatus(error.message, true);
            throw error;
        }
    }
    const urlValue = value => String(value || '').trim();
    const imageTile = (item, index) => `<button type="button" class="community-photo-tile${item ? ' has-media' : ''}" data-board-action="photo" data-index="${index}" aria-label="${item ? 'Edit' : 'Add'} board photo ${index + 1}">${item ? `<img src="${esc(item.ImageUrl || '')}" alt=""><span><i class="fa-solid fa-pen"></i></span>` : '<i class="fa-solid fa-plus"></i>'}</button>`;
    const isDirectVideoUrl = value => { try {
        return /\.(mp4|m4v|mov|webm)$/i.test(new URL(String(value || ''), location.origin).pathname);
    }
    catch {
        return false;
    } };
    const youtubeVideoId = value => { try {
        const url = new URL(String(value || ''));
        if (url.hostname === 'youtu.be')
            return url.pathname.split('/').filter(Boolean)[0] || '';
        if (url.hostname.endsWith('youtube.com'))
            return url.searchParams.get('v') || url.pathname.match(/\/(?:embed|shorts)\/([^/?]+)/)?.[1] || '';
    }
    catch { } return ''; };
    const videoThumbnail = item => item?.ThumbnailBlobName || ((id => id ? `https://i.ytimg.com/vi/${encodeURIComponent(id)}/hqdefault.jpg` : '')(youtubeVideoId(item?.SourceUrl)));
    const videoVisual = item => isDirectVideoUrl(item?.SourceUrl) ? `<video src="${esc(item.SourceUrl)}" poster="${esc(videoThumbnail(item))}" muted playsinline preload="metadata"></video>` : videoThumbnail(item) ? `<img src="${esc(videoThumbnail(item))}" alt="">` : '<span class="community-video-fallback"><i class="fa-solid fa-film"></i></span>';
    const videoTile = (item, index) => `<button type="button" class="community-video-tile${item ? ' has-media' : ''}" data-board-action="video" data-index="${index}" aria-label="${item ? 'Edit' : 'Add'} board video ${index + 1}">${item ? `${videoVisual(item)}<span><strong>${esc(item.Title || `Video ${index + 1}`)}</strong><i class="fa-solid fa-play"></i></span>` : '<i class="fa-solid fa-plus"></i>'}</button>`;
    const releasePreviewUrls = () => { previewObjectUrls.forEach(url => URL.revokeObjectURL(url)); previewObjectUrls = []; };
    const previewUrl = file => { const url = URL.createObjectURL(file); previewObjectUrls.push(url); return url; };
    function mediaPreviewMarkup(source, thumbnail, type) {
        if (type === 'photo')
            return source ? `<img class="community-dialog-preview" src="${esc(source)}" alt="Photo preview">` : '<div class="community-media-empty"><i class="fa-solid fa-image"></i><span>Your photo preview appears here</span></div>';
        if (isDirectVideoUrl(source) || String(source || '').startsWith('blob:'))
            return `<video class="community-dialog-video" src="${esc(source)}"${thumbnail ? ` poster="${esc(thumbnail)}"` : ''} controls playsinline preload="metadata"></video>`;
        const youtubeId = youtubeVideoId(source);
        if (youtubeId)
            return `<div class="community-dialog-embed"><iframe src="https://www.youtube-nocookie.com/embed/${encodeURIComponent(youtubeId)}" title="Video preview" loading="lazy" allow="accelerometer; autoplay; encrypted-media; picture-in-picture" allowfullscreen></iframe></div>`;
        if (thumbnail)
            return `<div class="community-dialog-video-poster"><img src="${esc(thumbnail)}" alt="Video thumbnail"><i class="fa-solid fa-play"></i></div>`;
        return '<div class="community-media-empty"><i class="fa-solid fa-film"></i><span>Your video preview appears here</span></div>';
    }
    function normalizeBoard(value) {
        value = value && typeof value === 'object' ? value : {};
        value.FeaturedPlayer = value.FeaturedPlayer && typeof value.FeaturedPlayer === 'object' ? value.FeaturedPlayer : {};
        value.FeaturedRoomGroup = value.FeaturedRoomGroup && typeof value.FeaturedRoomGroup === 'object' ? value.FeaturedRoomGroup : {};
        value.FeaturedRoomGroup.Rooms = Array.isArray(value.FeaturedRoomGroup.Rooms) ? value.FeaturedRoomGroup.Rooms : [];
        value.FeaturedRoomGroup.FeaturedRooms = Array.isArray(value.FeaturedRoomGroup.FeaturedRooms) ? value.FeaturedRoomGroup.FeaturedRooms : [...value.FeaturedRoomGroup.Rooms];
        value.InstagramImages = Array.isArray(value.InstagramImages) ? value.InstagramImages.slice(0, 12) : [];
        value.Videos = Array.isArray(value.Videos) ? value.Videos.slice(0, 3) : [];
        value.CurrentAnnouncement = value.CurrentAnnouncement && typeof value.CurrentAnnouncement === 'object' ? value.CurrentAnnouncement : {};
        return value;
    }
    async function loadFeaturedUser() { featuredUser = null; const id = Number(board?.FeaturedPlayer?.Id); if (!id)
        return; try {
        featuredUser = await get('/recnet/api/users/' + id);
    }
    catch {
        featuredUser = null;
    } }
    function render() {
        if (!board)
            return;
        const player = board.FeaturedPlayer || {}, pinned = board.FeaturedRoomGroup?.Rooms || [], announcement = board.CurrentAnnouncement || {};
        featured.innerHTML = `<button type="button" class="community-featured-button" data-board-action="featured"><span class="community-card-label">Featured creator</span>${featuredUser ? `<img src="${esc(featuredUser.profileImage || '')}" alt=""><strong>${esc(featuredUser.displayName || featuredUser.username || 'Player')}</strong><small>@${esc(featuredUser.username || 'unknown')}</small>` : `<span class="community-add-orb"><i class="fa-solid fa-plus"></i></span><strong>${player.Id ? `Account #${Number(player.Id)}` : 'Choose a creator'}</strong>`}<span class="community-edit-hint"><i class="fa-solid fa-pen"></i> Change creator</span></button>`;
        rooms.innerHTML = `<div><span class="community-card-label">Pinned rooms</span><strong>${pinned.length} of 10</strong><p>Choose the rooms shown on the in-game community board.</p></div><div class="community-pinned-list">${pinned.length ? pinned.slice(0, 4).map(room => `<span>^${esc(room.RoomName || `Room ${room.RoomId}`)}</span>`).join('') : '<span class="empty-slot">No rooms pinned</span>'}${pinned.length > 4 ? `<span>+${pinned.length - 4} more</span>` : ''}</div><button type="button" class="community-inline-button" data-board-action="rooms"><i class="fa-solid fa-thumbtack"></i> Manage pinned rooms</button>`;
        photos.innerHTML = Array.from({ length: 12 }, (_, index) => imageTile(board.InstagramImages[index], index)).join('');
        videos.innerHTML = Array.from({ length: 3 }, (_, index) => videoTile(board.Videos[index], index)).join('');
        document.querySelector('#communityBoardCoachPreview').textContent = announcement.Message || 'No Coach message set';
        panel.querySelectorAll('.has-media img,.community-featured-button img').forEach(image => image.addEventListener('error', () => image.classList.add('is-broken')));
        panel.querySelectorAll('.has-media video').forEach(video => video.addEventListener('error', () => video.classList.add('is-broken')));
    }
    async function loadBoard() {
        if (!board) {
            board = normalizeBoard({});
            render();
        }
        setStatus('Loading live board...');
        saveButton.disabled = true;
        reloadButton.disabled = true;
        try {
            board = normalizeBoard(await get('/recnet/api/admin/community-board'));
            dirty = false;
            panel.classList.remove('community-board-unavailable');
            await loadFeaturedUser();
            render();
            setStatus('Live board loaded');
        }
        catch (error) {
            setStatus(error.message, true);
            panel.classList.add('community-board-unavailable');
        }
        finally {
            reloadButton.disabled = false;
        }
    }
    function wireMediaPreview(type) {
        if (type !== 'photo' && type !== 'video')
            return;
        const preview = document.querySelector('#communityMediaPreview');
        if (!preview)
            return;
        const refresh = () => { releasePreviewUrls(); if (type === 'photo') {
            const file = dialogForm.elements.imageFile?.files?.[0], source = file ? previewUrl(file) : urlValue(dialogForm.elements.imageUrl?.value);
            preview.innerHTML = mediaPreviewMarkup(source, '', 'photo');
        }
        else {
            const videoFile = dialogForm.elements.videoFile?.files?.[0], thumbnailFile = dialogForm.elements.thumbnailFile?.files?.[0], source = videoFile ? previewUrl(videoFile) : urlValue(dialogForm.elements.sourceUrl?.value), thumbnail = thumbnailFile ? previewUrl(thumbnailFile) : urlValue(dialogForm.elements.thumbnail?.value);
            preview.innerHTML = mediaPreviewMarkup(source, thumbnail, 'video');
        } };
        dialogBody.querySelectorAll('input[type="file"],input[data-media-url]').forEach(input => { input.addEventListener('change', refresh); input.addEventListener('input', refresh); });
        refresh();
    }
    async function uploadCommunityMedia(file, kind, fileName = file.name) {
        const form = new FormData();
        form.append('kind', kind);
        form.append('file', file, fileName);
        const response = await recnetFetch('/recnet/api/admin/community-board/media', { method: 'POST', body: form }), data = await response.json().catch(() => ({}));
        if (!response.ok)
            throw new Error(data.error || `${kind === 'video' ? 'Video' : 'Image'} upload failed.`);
        return data;
    }
    function captureVideoThumbnail(file) {
        return new Promise((resolve, reject) => { const video = document.createElement('video'), url = URL.createObjectURL(file); let settled = false; const finish = (error, blob) => { if (settled)
            return; settled = true; clearTimeout(timer); URL.revokeObjectURL(url); video.remove(); error ? reject(error) : resolve(new File([blob], `${file.name.replace(/\.[^.]+$/, '')}-thumbnail.jpg`, { type: 'image/jpeg' })); }; const draw = () => { try {
            const scale = Math.min(1, 1280 / video.videoWidth), canvas = document.createElement('canvas');
            canvas.width = Math.max(1, Math.round(video.videoWidth * scale));
            canvas.height = Math.max(1, Math.round(video.videoHeight * scale));
            canvas.getContext('2d').drawImage(video, 0, 0, canvas.width, canvas.height);
            canvas.toBlob(blob => blob ? finish(null, blob) : finish(new Error('Could not create a thumbnail from this video.')), 'image/jpeg', .86);
        }
        catch (error) {
            finish(error);
        } }; const timer = setTimeout(() => finish(new Error('The video preview took too long. Choose a thumbnail image manually.')), 12000); video.muted = true; video.preload = 'auto'; video.playsInline = true; video.onloadedmetadata = () => { if (!video.videoWidth || !video.videoHeight) {
            finish(new Error('This video cannot be previewed. Choose a thumbnail image manually.'));
            return;
        } const target = Number.isFinite(video.duration) && video.duration > 0 ? Math.min(.5, video.duration * .1) : 0; if (target > 0) {
            video.onseeked = draw;
            video.currentTime = target;
        }
        else
            draw(); }; video.onerror = () => finish(new Error('This browser could not read the video. Try MP4/WebM or choose a thumbnail manually.')); video.src = url; video.load(); });
    }
    function openEditor(type, index = -1) {
        if (!board)
            return;
        releasePreviewUrls();
        editor = { type, index };
        dialogDelete.hidden = true;
        dialogForm.dataset.editor = type;
        if (type === 'featured') {
            const item = board.FeaturedPlayer || {};
            dialogTitle.textContent = 'Change featured creator';
            dialogBody.innerHTML = `<label>Account ID<input name="accountId" type="number" min="1" value="${Number(item.Id) || ''}" required></label><label>Board title<input name="title" maxlength="120" value="${esc(item.TitleOverride || 'Featured Creator!')}"></label><label>Creator link<input name="url" type="url" maxlength="2048" value="${esc(item.UrlOverride || '')}" placeholder="https://localhost/recnet/#user/33"></label>`;
        }
        else if (type === 'rooms') {
            const items = board.FeaturedRoomGroup?.Rooms || [];
            dialogTitle.textContent = 'Manage pinned rooms';
            dialogBody.innerHTML = `<p class="community-dialog-help">Add up to 10 live room IDs. Names and images can be customized for the board.</p><div class="community-room-fields">${Array.from({ length: 10 }, (_, i) => { const room = items[i] || {}; return `<fieldset><legend>Slot ${i + 1}</legend><label>Room ID<input name="roomId_${i}" type="number" min="1" value="${Number(room.RoomId) || ''}" placeholder="Leave empty to hide"></label><label>Display name<input name="roomName_${i}" maxlength="50" value="${esc(room.RoomName || '')}" placeholder="DormRoom"></label><label>Image name or URL<input name="roomImage_${i}" maxlength="2048" value="${esc(room.ImageName || '')}" placeholder="room.jpg"></label></fieldset>`; }).join('')}</div>`;
        }
        else if (type === 'photo') {
            const item = board.InstagramImages[index] || {};
            dialogTitle.textContent = `${item.ImageUrl ? 'Edit' : 'Add'} photo ${index + 1}`;
            dialogDelete.hidden = !item.ImageUrl;
            dialogBody.innerHTML = `<label class="community-upload-field"><span><i class="fa-solid fa-arrow-up-from-bracket"></i><strong>Upload a photo from your PC</strong><small>PNG, JPG, WebP, GIF, or BMP · maximum 15 MB</small></span><input name="imageFile" type="file" accept="image/png,image/jpeg,image/webp,image/gif,image/bmp"></label><div class="community-upload-divider"><span>or use a URL</span></div><label>Image URL<input name="imageUrl" data-media-url inputmode="url" maxlength="2048" value="${esc(item.ImageUrl || '')}" placeholder="https://.../photo.jpg"></label><label>Image name<input name="imageName" maxlength="300" value="${esc(item.ImageName || '')}" placeholder="community-photo.jpg"></label><div id="communityMediaPreview" class="community-media-preview"></div><div id="communityMediaUploadStatus" class="form-status community-upload-status" aria-live="polite"></div>`;
        }
        else if (type === 'video') {
            const item = board.Videos[index] || {};
            dialogTitle.textContent = `${item.SourceUrl ? 'Edit' : 'Add'} video ${index + 1}`;
            dialogDelete.hidden = !item.SourceUrl;
            dialogBody.innerHTML = `<label class="community-upload-field"><span><i class="fa-solid fa-video"></i><strong>Upload a video from your PC</strong><small>MP4, M4V, MOV, or WebM · maximum 100 MB</small></span><input name="videoFile" type="file" accept="video/mp4,video/x-m4v,video/quicktime,video/webm,.mp4,.m4v,.mov,.webm"></label><label class="community-upload-field compact"><span><i class="fa-solid fa-image"></i><strong>Choose a thumbnail</strong><small>Optional — one is generated from uploaded videos when possible</small></span><input name="thumbnailFile" type="file" accept="image/png,image/jpeg,image/webp"></label><div class="community-upload-divider"><span>or use links</span></div><label>Title<input name="title" maxlength="120" value="${esc(item.Title || '')}" required></label><label>Description<textarea name="description" maxlength="1000" rows="3">${esc(item.Description || '')}</textarea></label><label>Video URL<input name="sourceUrl" data-media-url inputmode="url" maxlength="2048" value="${esc(item.SourceUrl || '')}" placeholder="https://youtube.com/watch?v=..."></label><label>Thumbnail URL<input name="thumbnail" data-media-url inputmode="url" maxlength="2048" value="${esc(item.ThumbnailBlobName || '')}"></label><label>Blob name<input name="blobName" maxlength="300" value="${esc(item.BlobName || '')}" placeholder="community-video.mp4"></label><div id="communityMediaPreview" class="community-media-preview"></div><div id="communityMediaUploadStatus" class="form-status community-upload-status" aria-live="polite"></div>`;
        }
        else if (type === 'coach') {
            const item = board.CurrentAnnouncement || {};
            dialogTitle.textContent = 'Coach message all';
            dialogBody.innerHTML = `<label>Coach message<textarea name="message" maxlength="500" rows="5" placeholder="Welcome to the server!" required>${esc(item.Message || '')}</textarea></label><label>More info link<input name="moreInfoUrl" type="url" maxlength="2048" value="${esc(item.MoreInfoUrl || '')}" placeholder="https://localhost/recnet"></label><p class="community-dialog-help">Saving the board pushes a live Community Board refresh to connected players.</p>`;
        }
        else if (type === 'maintenance') {
            dialogTitle.textContent = 'Scheduled maintenance countdown';
            dialogBody.innerHTML = `<label>Starts in minutes<input name="minutes" type="number" min="0" max="10080" value="3" required></label><p class="community-dialog-help">This immediately updates ConfigV2 and sends the in-game maintenance notice to connected players. Use 0 to clear it.</p><div id="communityMaintenanceStatus" class="form-status" aria-live="polite"></div>`;
        }
        dialog.showModal();
        wireMediaPreview(type);
    }
    dialogForm.addEventListener('submit', async (event) => {
        event.preventDefault();
        const values = Object.fromEntries(new FormData(dialogForm));
        if (editor.type === 'maintenance') {
            const minutes = Number(values.minutes), maintenanceStatus = document.querySelector('#communityMaintenanceStatus'), submit = dialogForm.querySelector('[type="submit"]');
            try {
                submit.disabled = true;
                maintenanceStatus.textContent = 'Sending notice...';
                const result = await adminAction('/recnet/api/admin/maintenance', 'POST', { minutes });
                maintenanceStatus.textContent = result.message;
                setTimeout(() => dialog.close(), 850);
            }
            catch (error) {
                maintenanceStatus.className = 'form-status admin-error';
                maintenanceStatus.textContent = error.message;
            }
            finally {
                submit.disabled = false;
            }
            return;
        }
        if (editor.type === 'featured') {
            board.FeaturedPlayer = { ...board.FeaturedPlayer, Id: Number(values.accountId), TitleOverride: String(values.title || '').trim(), UrlOverride: urlValue(values.url) };
            await loadFeaturedUser();
        }
        else if (editor.type === 'rooms') {
            const pinned = [];
            for (let i = 0; i < 10; i++) {
                const roomId = Number(values[`roomId_${i}`]);
                if (!roomId)
                    continue;
                pinned.push({ RoomName: String(values[`roomName_${i}`] || '').trim() || `Room${roomId}`, RoomId: roomId, ImageName: String(values[`roomImage_${i}`] || '').trim() });
            }
            board.FeaturedRoomGroup.Rooms = pinned;
            board.FeaturedRoomGroup.FeaturedRooms = pinned.map(item => ({ ...item }));
        }
        else if (editor.type === 'photo') {
            const imageFile = dialogForm.elements.imageFile.files[0], uploadStatus = document.querySelector('#communityMediaUploadStatus'), submit = dialogForm.querySelector('[type="submit"]');
            let imageUrl = urlValue(values.imageUrl), imageName = String(values.imageName || '').trim();
            try {
                if (imageFile) {
                    submit.disabled = true;
                    uploadStatus.className = 'form-status community-upload-status';
                    uploadStatus.textContent = 'Uploading photo...';
                    const uploaded = await uploadCommunityMedia(imageFile, 'image');
                    imageUrl = uploaded.url;
                    imageName = uploaded.imageName || imageName || uploaded.originalName || uploaded.fileName;
                }
                if (!imageUrl)
                    throw new Error('Choose a photo from your PC or enter an image URL.');
            }
            catch (error) {
                uploadStatus.className = 'form-status community-upload-status admin-error';
                uploadStatus.textContent = error.message;
                submit.disabled = false;
                return;
            }
            finally {
                submit.disabled = false;
            }
            const item = { ImageName: imageName || `community-photo-${editor.index + 1}.jpg`, ImageUrl: imageUrl };
            if (editor.index < board.InstagramImages.length)
                board.InstagramImages[editor.index] = item;
            else
                board.InstagramImages.push(item);
        }
        else if (editor.type === 'video') {
            const videoFile = dialogForm.elements.videoFile.files[0], chosenThumbnail = dialogForm.elements.thumbnailFile.files[0], uploadStatus = document.querySelector('#communityMediaUploadStatus'), submit = dialogForm.querySelector('[type="submit"]');
            let sourceUrl = urlValue(values.sourceUrl), thumbnail = urlValue(values.thumbnail), blobName = String(values.blobName || '').trim(), thumbnailFile = chosenThumbnail;
            try {
                submit.disabled = true;
                if (videoFile && !thumbnailFile) {
                    uploadStatus.textContent = 'Creating a fresh video thumbnail...';
                    thumbnailFile = await captureVideoThumbnail(videoFile);
                }
                const jobs = [];
                if (videoFile) {
                    uploadStatus.textContent = 'Uploading video...';
                    jobs.push(uploadCommunityMedia(videoFile, 'video').then(uploaded => { sourceUrl = uploaded.url; blobName = uploaded.fileName; }));
                }
                if (thumbnailFile) {
                    jobs.push(uploadCommunityMedia(thumbnailFile, 'image').then(uploaded => { thumbnail = uploaded.url; }));
                }
                await Promise.all(jobs);
                if (!sourceUrl)
                    throw new Error('Choose a video from your PC or enter a video URL.');
                thumbnail = thumbnail || videoThumbnail({ SourceUrl: sourceUrl });
                if (!thumbnail)
                    throw new Error('Choose a thumbnail image for this video.');
            }
            catch (error) {
                uploadStatus.className = 'form-status community-upload-status admin-error';
                uploadStatus.textContent = error.message;
                submit.disabled = false;
                return;
            }
            finally {
                submit.disabled = false;
            }
            const item = { BlobName: blobName || `community-video-${editor.index + 1}.mp4`, Title: String(values.title || '').trim(), Description: String(values.description || '').trim(), ThumbnailBlobName: thumbnail, SourceUrl: sourceUrl };
            if (editor.index < board.Videos.length)
                board.Videos[editor.index] = item;
            else
                board.Videos.push(item);
        }
        else if (editor.type === 'coach')
            board.CurrentAnnouncement = { ...board.CurrentAnnouncement, Message: String(values.message || '').trim(), MoreInfoUrl: urlValue(values.moreInfoUrl) };
        markDirty();
        render();
        const submit = dialogForm.querySelector('[type="submit"]'), uploadStatus = document.querySelector('#communityMediaUploadStatus');
        try {
            submit.disabled = true;
            if (uploadStatus) {
                uploadStatus.className = 'form-status community-upload-status';
                uploadStatus.textContent = 'Saving to the live community board...';
            }
            await persistBoard('Saved — the live community board was updated');
            dialog.close();
        }
        catch (error) {
            if (uploadStatus) {
                uploadStatus.className = 'form-status community-upload-status admin-error';
                uploadStatus.textContent = `The upload finished, but the board could not be saved: ${error.message}`;
            }
        }
        finally {
            submit.disabled = false;
        }
    });
    dialogDelete.addEventListener('click', async () => { if (editor.type === 'photo')
        board.InstagramImages.splice(editor.index, 1);
    else if (editor.type === 'video')
        board.Videos.splice(editor.index, 1);
    else
        return; markDirty(); render(); try {
        dialogDelete.disabled = true;
        await persistBoard('Removed — the live community board was updated');
        dialog.close();
    }
    catch { }
    finally {
        dialogDelete.disabled = false;
    } });
    dialog.querySelectorAll('[data-dialog-close]').forEach(button => button.addEventListener('click', () => dialog.close()));
    dialog.addEventListener('close', releasePreviewUrls);
    panel.addEventListener('click', event => { const button = event.target.closest('[data-board-action]'); if (!button)
        return; openEditor(button.dataset.boardAction, Number(button.dataset.index ?? -1)); });
    saveButton.addEventListener('click', async () => { try {
        await persistBoard('Saved — connected players were told to refresh');
    }
    catch { } });
    reloadButton.addEventListener('click', () => { if (dirty && !confirm('Discard your unsaved community board changes?'))
        return; loadBoard(); });
    loadBoard();
}
async function adminPanel() {
    if (!currentUser?.isAdmin) {
        app.innerHTML = '<div class="empty">You do not have permission to open the Admin panel.</div>';
        return;
    }
    app.innerHTML = `<div class="admin-migration-banner"><a href="/recnet/mocha" class="admin-migration-link">Move to the new Admin Panel &rarr;</a></div><div class="page-kicker">Developer tools</div><h1 class="page-title">Admin</h1><div class="subtitle">Create accounts, edit identities and platforms, manage roles, and moderate users.</div><div id="adminOverview" class="admin-overview"><div class="loading"><i></i>Loading server overview</div></div><div class="admin-layout"><form id="adminCreate" class="admin-panel"><h2>Create account</h2><p>Create a normal RR+ account. Every platform ID supports unlimited accounts.</p><div class="settings-grid"><label class="field">@Username<input name="username" minlength="3" maxlength="20" pattern="[A-Za-z0-9_]+" required></label><label class="field">Platform<select name="platform" required><option value="">Choose one</option><option>Steam</option><option>Oculus</option><option>PlayStation</option><option>Xbox</option><option>IOS</option><option>GooglePlay</option></select></label><label class="field wide">Platform ID<input name="platformId" inputmode="numeric" pattern="[0-9]+" required></label><label class="field">Password<input name="password" type="password" minlength="8" required></label><label class="field">Confirm password<input name="confirmPassword" type="password" minlength="8" required></label></div><div class="form-actions"><button class="primary-button">Create account</button></div><div id="adminCreateResult"></div></form><section class="admin-panel admin-users-panel"><h2>User management</h2><p>Search for a user, then open Manage to change their account.</p><input id="adminSearch" class="search" placeholder="Search accounts"><div id="adminAccounts" class="admin-account-list"></div></section></div>`;
    document.querySelector('#adminOverview').insertAdjacentHTML('afterend', `<section id="adminServerSettings" class="admin-panel admin-security-panel"><div class="admin-shop-heading"><div><span class="page-kicker">Access control</span><h2><i class="fa-solid fa-shield-halved"></i> Server security</h2><p>Control account creation, anonymous networks, and IP/CIDR bans from one place.</p></div><span id="adminSecurityState" class="security-state">Loading...</span></div><div class="admin-security-toggles"><label class="admin-check"><input id="adminAccountCreationEnabled" type="checkbox" disabled><span><strong>Allow all account creation</strong><small>Master switch for every account-creation path, including the admin form.</small></span></label><label class="admin-check"><input id="adminSignupEnabled" type="checkbox" disabled><span><strong>Allow public RecNet signup</strong><small>Controls only the website registration form and is also limited by the master switch.</small></span></label><label class="admin-check"><input id="adminVpnBlockingEnabled" type="checkbox" disabled><span><strong>Block VPNs, proxies, Tor, and hosting IPs</strong><small>Checks gameplay and account endpoints. Results are cached and provider outages fail open.</small></span></label></div><div id="adminSettingsStatus" class="form-status" aria-live="polite">Loading settings...</div><div class="admin-ip-ban-layout"><form id="adminIpBanForm" class="settings-grid"><div class="field wide"><strong>IP banning</strong><small>Enter one IP or a CIDR range such as 203.0.113.0/24.</small></div><label class="field">IP / CIDR<input name="network" maxlength="80" placeholder="203.0.113.4 or 203.0.113.0/24" required></label><label class="field">Reason<input name="reason" maxlength="500" placeholder="Ban reason"></label><div class="form-actions wide"><button class="danger-button" type="submit"><i class="fa-solid fa-network-wired"></i> Add IP ban</button><span id="adminIpBanStatus" class="form-status" aria-live="polite"></span></div></form><div id="adminIpBanList" class="admin-ip-ban-list"><div class="loading"><i></i>Loading IP bans</div></div></div></section>`);
    document.querySelector('#adminServerSettings').insertAdjacentHTML('afterend', `<section id="adminCommunityBoard" class="admin-panel community-board-admin"><div class="community-board-heading"><div><span class="page-kicker">In-game live content</span><h2><i class="fa-solid fa-table-columns"></i> Community board</h2><p>Edit the featured creator, pinned rooms, photos, videos, and Coach message.</p></div><div class="community-board-save"><span id="communityBoardStatus" class="community-board-status" aria-live="polite">Loading...</span><button id="communityBoardReload" type="button" class="secondary-button small-button"><i class="fa-solid fa-rotate"></i> Reload</button><button id="communityBoardSave" type="button" class="primary-button" disabled><i class="fa-solid fa-floppy-disk"></i> Save board</button></div></div><div class="community-board-canvas"><div class="community-board-left"><article id="communityBoardFeatured" class="community-board-card community-featured-card"></article><article id="communityBoardRooms" class="community-board-card community-rooms-card"></article></div><article class="community-board-card community-photos-card"><div class="community-card-heading"><span class="community-card-label">Board photos</span><small>Click a photo to change it</small></div><div id="communityBoardPhotos" class="community-photo-grid"></div></article><article class="community-board-card community-videos-card"><div class="community-card-heading"><span class="community-card-label">Board videos</span><small>Click a video to change it</small></div><div id="communityBoardVideos" class="community-video-grid"></div></article></div><div class="community-board-bottom"><button type="button" class="community-action-card" data-board-action="coach"><i class="fa-solid fa-comment-dots"></i><span><strong>Coach message all</strong><small id="communityBoardCoachPreview">Loading message...</small></span><i class="fa-solid fa-chevron-right"></i></button><button type="button" class="community-action-card" data-board-action="maintenance"><i class="fa-solid fa-clock"></i><span><strong>Scheduled maintenance countdown</strong><small>Send the live in-game notice</small></span><i class="fa-solid fa-chevron-right"></i></button></div><dialog id="communityBoardDialog" class="community-board-dialog"><form id="communityBoardDialogForm" method="dialog"><div class="community-dialog-heading"><div><span class="page-kicker">Community board</span><h3 id="communityBoardDialogTitle">Edit item</h3></div><button type="button" class="dialog-close" data-dialog-close aria-label="Close">&times;</button></div><div id="communityBoardDialogBody" class="community-dialog-body"></div><div class="community-dialog-actions"><button id="communityBoardDialogDelete" type="button" class="danger-button" hidden><i class="fa-solid fa-trash"></i> Remove</button><button type="button" class="secondary-button" data-dialog-close>Cancel</button><button type="submit" class="primary-button"><i class="fa-solid fa-check"></i> Apply</button></div></form></dialog></section>`);
    document.querySelector('#adminCommunityBoard').insertAdjacentHTML('afterend', `<section id="adminAnnouncementsPanel" class="admin-panel admin-announcements-panel"><div class="admin-shop-heading"><div><h2><i class="fa-solid fa-bullhorn"></i> RecNet announcements</h2><p>Create posts for the RecNet home page. Markdown supports headings, bold, italics, lists, links, quotes, and code blocks.</p></div></div><div class="admin-announcement-layout"><form id="adminAnnouncementForm" class="admin-announcement-form"><input name="announcementId" type="hidden"><div class="settings-grid"><label class="field wide">Title<input name="title" maxlength="100" placeholder="Server update" required></label><label class="field">Type<select name="kind"><option value="info">Info</option><option value="update">Update</option><option value="warning">Warning</option><option value="maintenance">Maintenance</option></select></label><label class="field wide">Markdown body<textarea name="bodyMarkdown" maxlength="12000" rows="10" placeholder="## What changed\n- Added something cool\n- Fixed another thing" required></textarea></label></div><div class="admin-announcement-options"><label class="admin-mini-check"><input name="pinned" type="checkbox"> Pin to top</label><label class="admin-mini-check"><input name="published" type="checkbox" checked> Published</label></div><div class="form-actions"><button id="adminAnnouncementSave" class="primary-button" type="submit"><i class="fa-solid fa-paper-plane"></i> Create announcement</button><button id="adminAnnouncementCancel" class="secondary-button" type="button" hidden>Cancel edit</button></div><div id="adminAnnouncementStatus" class="form-status" aria-live="polite"></div><div><h3>Live preview</h3><div id="adminAnnouncementPreview" class="admin-announcement-preview"></div></div></form><div><h3>Existing announcements</h3><div id="adminAnnouncementList" class="admin-announcement-list"><div class="loading"><i></i>Loading announcements</div></div></div></div></section>`);
    document.querySelector('#adminAnnouncementsPanel').insertAdjacentHTML('afterend', `<section id="adminSteamAccess" class="admin-panel admin-shop-panel"><div class="admin-shop-heading"><div><h2><i class="fa-brands fa-steam"></i> Steam access</h2><p>Every Steam ID is allowed by default. Add an ID here to return HTTP 403 on cached login, account creation, password login, refresh, and RecNet login.</p></div></div><form id="adminSteamBlacklistForm" class="settings-grid"><label class="field wide">Steam ID<input name="steamId" inputmode="numeric" pattern="[0-9]+" maxlength="20" placeholder="7656119..." required></label><label class="field wide">Reason<input name="reason" maxlength="500" placeholder="Why this Steam ID is blocked"></label><div class="form-actions wide"><button class="danger-button" type="submit"><i class="fa-solid fa-ban"></i> Blacklist Steam ID</button><span id="adminSteamBlacklistStatus" class="form-status" aria-live="polite"></span></div></form><div id="adminSteamBlacklist" class="admin-custom-shop-items"><div class="loading"><i></i>Loading Steam blacklist</div></div></section>`);
    document.querySelector('#adminSteamAccess').insertAdjacentHTML('afterend', `<section id="adminShopPanel" class="admin-panel admin-shop-panel"><div class="admin-shop-heading"><div><h2><i class="fa-solid fa-store"></i> Shop controls</h2><p>Reroll the random 5-star rotation or pin catalog items into the live in-game shop.</p></div><button id="adminRefreshShop" type="button" class="primary-button"><i class="fa-solid fa-rotate"></i> Refresh shop now</button></div><div class="admin-shop-grid"><div><h3>Custom shop items</h3><div id="adminCustomShopItems" class="admin-custom-shop-items"><div class="loading"><i></i>Loading custom items</div></div></div><div><h3>Add custom item</h3><input id="adminShopSearch" class="search" placeholder="Search item name, SKU, or avatar item ID" autocomplete="off"><div id="adminShopSearchResults" class="admin-shop-search-results"></div></div></div><div id="adminShopStatus" class="form-status" aria-live="polite"></div></section>`);
    document.querySelector('#adminShopPanel').insertAdjacentHTML('afterend', `<section id="adminGiftPanel" class="admin-panel admin-gift-panel"><div class="admin-shop-heading"><div><span class="page-kicker">From Coach</span><h2><i class="fa-solid fa-gift"></i> Gift center</h2><p>Send a real in-game gift box to one player or every account. Gifts are always shown as sent by Coach (#1).</p></div><span class="admin-gift-from"><i class="fa-solid fa-user-tie"></i> From: Coach (#1)</span></div><form id="adminGiftForm" class="admin-gift-layout"><div class="admin-gift-column"><h3>1. Recipient</h3><label class="field">Account ID(s)<input id="adminGiftRecipient" name="recipientAccountId" type="text" inputmode="numeric" placeholder="e.g. 2, 14, 3"></label><label class="admin-check admin-gift-all"><input id="adminGiftAll" name="sendToAll" type="checkbox"><span><strong>Send to all players</strong><small>Queues one gift for every account except Coach (#1).</small></span></label><label class="admin-check admin-gift-online-only"><input id="adminGiftOnlineOnly" name="onlineOnly" type="checkbox"><span><strong>Online players only</strong><small>Limits the recipients above to whoever is currently connected.</small></span></label><input id="adminGiftPlayerSearch" class="search" placeholder="Search username or display name" autocomplete="off"><div id="adminGiftPlayerResults" class="admin-gift-player-results"></div></div><div class="admin-gift-column"><h3>2. Gift</h3><label class="field">Gift type<select id="adminGiftType" name="giftType"><option value="avatar">Avatar item</option><option value="equipment">Equipment skin</option><option value="consumable">Consumable</option><option value="box">Level box</option><option value="tokens">Tokens</option><option value="xp">XP</option></select></label><label id="adminGiftDesignField" class="field">Box design<select id="adminGiftDesign" name="boxDesign"><option value="2">Normal box</option><option value="110000">Friendotron box</option><option value="custom">Custom design ID&hellip;</option></select><input id="adminGiftDesignCustom" type="number" step="1" min="0" placeholder="Design ID" hidden><small>Controls which box model the recipient sees when they open it — applies to any gift type.</small></label><label id="adminGiftBoxRarityField" class="field" hidden>Box rarity<select id="adminGiftBoxRarity" name="boxRarity"><option value="10">Rarity 10</option><option value="20">Rarity 20</option><option value="30">Rarity 30</option><option value="40">Rarity 40</option><option value="50">Rarity 50</option></select><small>Rolls a random consumable at this rarity for each recipient — same as the level-up reward.</small></label><label id="adminGiftAmountField" class="field" hidden>Amount<input id="adminGiftAmount" name="amount" type="number" min="-2147483648" max="2147483647" step="1" value="1"></label><div id="adminGiftCatalog"><input id="adminGiftCatalogSearch" class="search" placeholder="Search the full item catalog" autocomplete="off"><div id="adminGiftCatalogResults" class="admin-gift-catalog-results"><div class="loading"><i></i>Loading gifts</div></div></div><div id="adminGiftSelected" class="admin-gift-selected empty">Choose an item.</div></div><div class="admin-gift-column"><h3>3. Deliver</h3><label class="field">Gift message<textarea id="adminGiftMessage" name="message" maxlength="200" rows="4" placeholder="A gift from Coach!"></textarea></label><div class="admin-gift-summary"><strong id="adminGiftSummary">Choose a recipient and gift.</strong><small>The recipient opens this through the normal in-game gift flow.</small></div><button id="adminGiftSend" class="primary-button admin-gift-send" type="submit"><i class="fa-solid fa-paper-plane"></i> Send gift</button><div id="adminGiftStatus" class="form-status" aria-live="polite"></div></div></form><div class="admin-gift-clear-outgoing"><div><strong>Clear unclaimed outgoing boxes</strong><small>Pulls back every still-pending gift box sent by Coach (#1) that no one has opened yet. Already-claimed gifts are not affected.</small></div><button id="adminGiftClearOutgoing" class="danger-button" type="button"><i class="fa-solid fa-box-open"></i> Clear unclaimed boxes</button><div id="adminGiftClearStatus" class="form-status" aria-live="polite"></div></div></section>`);
    const requiredAdminIds = ['adminServerSettings', 'adminCommunityBoard', 'adminAnnouncementsPanel', 'adminSteamAccess', 'adminShopPanel', 'adminGiftPanel'];
    const missingAdminIds = requiredAdminIds.filter(id => !document.getElementById(id));
    if (missingAdminIds.length)
        throw new Error(`Admin panel markup is incomplete: ${missingAdminIds.join(', ')}`);
    const accountCreationToggle = document.querySelector('#adminAccountCreationEnabled'), signupToggle = document.querySelector('#adminSignupEnabled'), vpnBlockingToggle = document.querySelector('#adminVpnBlockingEnabled'), settingsStatus = document.querySelector('#adminSettingsStatus'), securityState = document.querySelector('#adminSecurityState'), ipBanForm = document.querySelector('#adminIpBanForm'), ipBanList = document.querySelector('#adminIpBanList'), ipBanStatus = document.querySelector('#adminIpBanStatus'), adminCreateForm = document.querySelector('#adminCreate');
    let adminSettings = { accountCreationEnabled: true, recNetSignupEnabled: true, vpnBlockingEnabled: true };
    function renderAdminSettings(settings) { adminSettings = { ...adminSettings, ...settings }; accountCreationToggle.checked = !!adminSettings.accountCreationEnabled; signupToggle.checked = !!adminSettings.recNetSignupEnabled; vpnBlockingToggle.checked = !!adminSettings.vpnBlockingEnabled; accountCreationToggle.disabled = false; signupToggle.disabled = false; vpnBlockingToggle.disabled = false; adminCreateForm.querySelectorAll('input,select,button').forEach(control => control.disabled = !adminSettings.accountCreationEnabled); securityState.className = 'security-state ' + (adminSettings.accountCreationEnabled ? 'enabled' : 'disabled'); securityState.textContent = adminSettings.accountCreationEnabled ? 'Creation open' : 'Creation locked'; const providerNote = settings.proxyCheckConfigured === false ? ' Add PROXYCHECK_API_KEY for a dedicated provider quota.' : ''; settingsStatus.className = 'form-status'; settingsStatus.textContent = `Account creation ${adminSettings.accountCreationEnabled ? 'enabled' : 'disabled'} · RecNet signup ${adminSettings.recNetSignupEnabled ? 'enabled' : 'disabled'} · VPN blocking ${adminSettings.vpnBlockingEnabled ? 'enabled' : 'disabled'}.${providerNote}`; }
    async function loadAdminSettings() { try {
        renderAdminSettings(await get('/recnet/api/admin/settings'));
    }
    catch (error) {
        settingsStatus.className = 'form-status admin-error';
        settingsStatus.textContent = error.message;
    } }
    async function saveAdminSettings() { const requested = { accountCreationEnabled: accountCreationToggle.checked, recNetSignupEnabled: signupToggle.checked, vpnBlockingEnabled: vpnBlockingToggle.checked }; try {
        accountCreationToggle.disabled = signupToggle.disabled = vpnBlockingToggle.disabled = true;
        settingsStatus.className = 'form-status';
        settingsStatus.textContent = 'Saving security settings...';
        renderAdminSettings(await adminAction('/recnet/api/admin/settings', 'PUT', requested));
        settingsStatus.textContent = 'Security settings saved.';
    }
    catch (error) {
        renderAdminSettings(adminSettings);
        settingsStatus.className = 'form-status admin-error';
        settingsStatus.textContent = error.message;
    } }
    accountCreationToggle.addEventListener('change', saveAdminSettings);
    signupToggle.addEventListener('change', saveAdminSettings);
    vpnBlockingToggle.addEventListener('change', saveAdminSettings);
    function ipBanMarkup(items) { return items.length ? items.map(item => `<article class="admin-ip-ban" data-ip-ban-id="${esc(item.id)}"><div><strong><i class="fa-solid fa-ban"></i> ${esc(item.network)}</strong><span>${esc(item.reason || 'Blocked by an administrator.')}</span><small>Added ${when(item.createdAt)} by account #${Number(item.createdByAccountId) || 0}</small></div><button type="button" class="secondary-button small-button" data-ip-ban-action="remove" data-ip-ban-id="${esc(item.id)}"><i class="fa-solid fa-unlock"></i> Remove</button></article>`).join('') : '<div class="empty admin-shop-empty">No IP addresses or ranges are banned.</div>'; }
    async function loadIpBans() { ipBanList.innerHTML = '<div class="loading"><i></i>Loading IP bans</div>'; try {
        ipBanList.innerHTML = ipBanMarkup(await get('/recnet/api/admin/ip-bans'));
    }
    catch (error) {
        ipBanList.innerHTML = `<div class="admin-error">${esc(error.message)}</div>`;
    } }
    ipBanForm.addEventListener('submit', async (event) => { event.preventDefault(); const data = Object.fromEntries(new FormData(ipBanForm)), button = ipBanForm.querySelector('button'); try {
        button.disabled = true;
        ipBanStatus.className = 'form-status';
        ipBanStatus.textContent = 'Adding IP ban...';
        await adminAction('/recnet/api/admin/ip-bans', 'POST', { network: String(data.network || '').trim(), reason: String(data.reason || '').trim() });
        ipBanForm.reset();
        ipBanStatus.textContent = 'IP ban added.';
        await loadIpBans();
    }
    catch (error) {
        ipBanStatus.className = 'form-status admin-error';
        ipBanStatus.textContent = error.message;
    }
    finally {
        button.disabled = false;
    } });
    ipBanList.addEventListener('click', async (event) => { const button = event.target.closest('[data-ip-ban-action="remove"]'); if (!button)
        return; const id = button.dataset.ipBanId, network = button.closest('.admin-ip-ban')?.querySelector('strong')?.textContent?.trim() || 'this network'; if (!confirm(`Remove the ban for ${network}?`))
        return; try {
        button.disabled = true;
        await adminAction(`/recnet/api/admin/ip-bans/${encodeURIComponent(id)}`, 'DELETE', {});
        ipBanStatus.className = 'form-status';
        ipBanStatus.textContent = 'IP ban removed.';
        await loadIpBans();
    }
    catch (error) {
        ipBanStatus.className = 'form-status admin-error';
        ipBanStatus.textContent = error.message;
    }
    finally {
        button.disabled = false;
    } });
    loadAdminSettings();
    loadIpBans();
    initCommunityBoardAdmin(adminAction);
    const announcementForm = document.querySelector('#adminAnnouncementForm'), announcementList = document.querySelector('#adminAnnouncementList'), announcementPreview = document.querySelector('#adminAnnouncementPreview'), announcementStatus = document.querySelector('#adminAnnouncementStatus'), announcementCancel = document.querySelector('#adminAnnouncementCancel'), announcementSave = document.querySelector('#adminAnnouncementSave');
    let adminAnnouncements = [];
    function announcementDraft() { const data = new FormData(announcementForm); return { id: Number(data.get('announcementId')) || 0, title: String(data.get('title') || 'Announcement preview'), bodyMarkdown: String(data.get('bodyMarkdown') || '*Start typing Markdown to preview it here.*'), kind: String(data.get('kind') || 'info'), pinned: data.get('pinned') === 'on', published: data.get('published') === 'on', updatedAt: new Date().toISOString(), authorName: currentUser?.displayName || currentUser?.username || 'Staff' }; }
    function updateAnnouncementPreview() { announcementPreview.innerHTML = announcementCard(announcementDraft()); }
    function resetAnnouncementForm() { announcementForm.reset(); announcementForm.elements.announcementId.value = ''; announcementForm.elements.published.checked = true; announcementSave.innerHTML = '<i class="fa-solid fa-paper-plane"></i> Create announcement'; announcementCancel.hidden = true; announcementStatus.className = 'form-status'; announcementStatus.textContent = ''; updateAnnouncementPreview(); }
    async function loadAnnouncements() { try {
        adminAnnouncements = await get('/recnet/api/admin/announcements');
        announcementList.innerHTML = adminAnnouncements.length ? adminAnnouncements.map(item => announcementCard(item, true)).join('') : '<div class="empty admin-shop-empty">No announcements yet.</div>';
    }
    catch (error) {
        announcementList.innerHTML = `<div class="admin-error">${esc(error.message)}</div>`;
    } }
    announcementForm.addEventListener('input', updateAnnouncementPreview);
    announcementCancel.addEventListener('click', resetAnnouncementForm);
    announcementForm.addEventListener('submit', async (event) => { event.preventDefault(); const draft = announcementDraft(), editingId = Number(announcementForm.elements.announcementId.value) || 0; try {
        announcementSave.disabled = true;
        announcementStatus.className = 'form-status';
        announcementStatus.textContent = editingId ? 'Saving changes...' : 'Publishing announcement...';
        await adminAction(editingId ? `/recnet/api/admin/announcements/${editingId}` : '/recnet/api/admin/announcements', editingId ? 'PUT' : 'POST', { title: draft.title, bodyMarkdown: draft.bodyMarkdown, kind: draft.kind, pinned: draft.pinned, published: draft.published });
        const savedMessage = editingId ? 'Announcement updated.' : 'Announcement created.';
        resetAnnouncementForm();
        announcementStatus.textContent = savedMessage;
        await loadAnnouncements();
    }
    catch (error) {
        announcementStatus.className = 'form-status admin-error';
        announcementStatus.textContent = error.message;
    }
    finally {
        announcementSave.disabled = false;
    } });
    announcementList.addEventListener('click', async (event) => { const button = event.target.closest('[data-announcement-action]'); if (!button)
        return; const id = Number(button.dataset.announcementId), item = adminAnnouncements.find(entry => Number(entry.id) === id); if (!item)
        return; if (button.dataset.announcementAction === 'edit') {
        announcementForm.elements.announcementId.value = String(id);
        announcementForm.elements.title.value = item.title || '';
        announcementForm.elements.bodyMarkdown.value = item.bodyMarkdown || '';
        announcementForm.elements.kind.value = item.kind || 'info';
        announcementForm.elements.pinned.checked = !!item.pinned;
        announcementForm.elements.published.checked = !!item.published;
        announcementSave.innerHTML = '<i class="fa-solid fa-floppy-disk"></i> Save changes';
        announcementCancel.hidden = false;
        announcementStatus.className = 'form-status';
        announcementStatus.textContent = `Editing announcement #${id}`;
        updateAnnouncementPreview();
        announcementForm.scrollIntoView({ behavior: 'smooth', block: 'start' });
        return;
    } if (!confirm(`Delete “${item.title}”?`))
        return; try {
        button.disabled = true;
        await adminAction(`/recnet/api/admin/announcements/${id}`, 'DELETE', {});
        announcementStatus.className = 'form-status';
        announcementStatus.textContent = 'Announcement deleted.';
        if (Number(announcementForm.elements.announcementId.value) === id)
            resetAnnouncementForm();
        await loadAnnouncements();
    }
    catch (error) {
        announcementStatus.className = 'form-status admin-error';
        announcementStatus.textContent = error.message;
    }
    finally {
        button.disabled = false;
    } });
    resetAnnouncementForm();
    loadAnnouncements();
    const steamBlacklistForm = document.querySelector('#adminSteamBlacklistForm'), steamBlacklistList = document.querySelector('#adminSteamBlacklist'), steamBlacklistStatus = document.querySelector('#adminSteamBlacklistStatus');
    function steamBlacklistMarkup(items) { return items.length ? items.map(item => { const actor = item.addedByDisplayName || item.addedByUsername || `Account #${item.addedByAccountId}`; return `<div class="admin-shop-item" data-steam-id="${esc(item.steamId)}"><div><strong><i class="fa-brands fa-steam"></i> ${esc(item.steamId)}</strong><span>${esc(item.reason || 'Blacklisted by an administrator.')} &middot; Added by ${esc(actor)} on ${esc(when(item.addedAt ?? item.AddedAt))}</span></div><button type="button" class="secondary-button small-button" data-steam-action="remove" data-steam-id="${esc(item.steamId)}"><i class="fa-solid fa-unlock"></i> Unblacklist</button></div>`; }).join('') : '<div class="empty admin-shop-empty">No Steam IDs are blacklisted. Every Steam player is allowed.</div>'; }
    async function loadSteamBlacklist() { steamBlacklistList.innerHTML = '<div class="loading"><i></i>Loading Steam blacklist</div>'; try {
        const items = await get('/recnet/api/admin/steam-blacklist');
        steamBlacklistList.innerHTML = steamBlacklistMarkup(items);
    }
    catch (error) {
        steamBlacklistList.innerHTML = `<div class="admin-error">${esc(error.message)}</div>`;
    } }
    steamBlacklistForm.addEventListener('submit', async (e) => { e.preventDefault(); const data = Object.fromEntries(new FormData(steamBlacklistForm)), button = steamBlacklistForm.querySelector('button'); if (!confirm(`Blacklist Steam ID ${data.steamId}? Future login and connect attempts will receive HTTP 403.`))
        return; try {
        button.disabled = true;
        steamBlacklistStatus.className = 'form-status';
        steamBlacklistStatus.textContent = 'Saving...';
        await adminAction('/recnet/api/admin/steam-blacklist', 'POST', { steamId: String(data.steamId).trim(), reason: String(data.reason || '').trim() });
        steamBlacklistForm.reset();
        steamBlacklistStatus.textContent = 'Steam ID blacklisted.';
        await loadSteamBlacklist();
    }
    catch (error) {
        steamBlacklistStatus.className = 'form-status admin-error';
        steamBlacklistStatus.textContent = error.message;
    }
    finally {
        button.disabled = false;
    } });
    steamBlacklistList.addEventListener('click', async (e) => { const button = e.target.closest('[data-steam-action="remove"]'); if (!button)
        return; const steamId = button.dataset.steamId; if (!confirm(`Remove Steam ID ${steamId} from the blacklist? It will be allowed again immediately.`))
        return; try {
        button.disabled = true;
        await adminAction(`/recnet/api/admin/steam-blacklist/${encodeURIComponent(steamId)}`, 'DELETE', {});
        steamBlacklistStatus.className = 'form-status';
        steamBlacklistStatus.textContent = 'Steam ID unblacklisted.';
        await loadSteamBlacklist();
    }
    catch (error) {
        steamBlacklistStatus.className = 'form-status admin-error';
        steamBlacklistStatus.textContent = error.message;
    }
    finally {
        button.disabled = false;
    } });
    loadSteamBlacklist();
    const shopPanel = document.querySelector('#adminShopPanel'), shopStatus = document.querySelector('#adminShopStatus'), customShopItems = document.querySelector('#adminCustomShopItems'), shopSearch = document.querySelector('#adminShopSearch'), shopSearchResults = document.querySelector('#adminShopSearchResults');
    let shopSearchTimer;
    const shopStars = rarity => ({ 0: '1 star', 10: '2 stars', 20: '3 stars', 30: '4 stars', 50: '5 stars' })[rarity] || `${rarity} rarity`;
    function customShopMarkup(items) { return items.length ? items.map(item => `<div class="admin-shop-item"><div><strong>${esc(item.friendlyName)}</strong><span>${shopStars(item.rarity)} &middot; SKU ${item.skuId} &middot; ${item.price} tokens</span></div><button type="button" class="secondary-button small-button" data-shop-action="remove" data-sku="${item.skuId}"><i class="fa-solid fa-xmark"></i> Remove</button></div>`).join('') : '<div class="empty admin-shop-empty">No custom items pinned. All 10 slots are random 5-stars.</div>'; }
    async function loadShopTools() { try {
        const shop = await get('/recnet/api/admin/shop');
        customShopItems.innerHTML = customShopMarkup(shop.customItems || []);
    }
    catch (error) {
        customShopItems.innerHTML = `<div class="admin-error">${esc(error.message)}</div>`;
    } }
    async function searchShopCatalog() { try {
        const items = await get('/recnet/api/admin/shop/catalog?take=30&search=' + encodeURIComponent(shopSearch.value.trim()));
        shopSearchResults.innerHTML = items.length ? items.map(item => `<button type="button" class="admin-shop-result" data-shop-action="add" data-sku="${item.skuId}"><span><strong>${esc(item.friendlyName)}</strong><small>${shopStars(item.rarity)} &middot; Avatar #${item.avatarItemId}</small></span><i class="fa-solid fa-plus"></i></button>`).join('') : '<div class="empty admin-shop-empty">No matching catalog items.</div>';
    }
    catch (error) {
        shopSearchResults.innerHTML = `<div class="admin-error">${esc(error.message)}</div>`;
    } }
    shopSearch.addEventListener('input', () => { clearTimeout(shopSearchTimer); shopSearchTimer = setTimeout(searchShopCatalog, 180); });
    shopPanel.addEventListener('click', async (e) => { const button = e.target.closest('[data-shop-action],#adminRefreshShop'); if (!button)
        return; const action = button.dataset.shopAction || 'refresh'; try {
        button.disabled = true;
        shopStatus.className = 'form-status';
        shopStatus.textContent = action === 'refresh' ? 'Refreshing shop...' : action === 'add' ? 'Adding item...' : 'Removing item...';
        if (action === 'refresh') {
            const result = await adminAction('/recnet/api/admin/shop/refresh', 'POST', {});
            shopStatus.textContent = result.message || 'Shop refreshed!';
        }
        else if (action === 'add') {
            await adminAction('/recnet/api/admin/shop/items', 'POST', { skuId: Number(button.dataset.sku) });
            shopStatus.textContent = 'Custom item added to the live shop.';
            await loadShopTools();
        }
        else {
            await adminAction(`/recnet/api/admin/shop/items/${button.dataset.sku}`, 'DELETE', {});
            shopStatus.textContent = 'Custom item removed from the live shop.';
            await loadShopTools();
        }
        await searchShopCatalog();
    }
    catch (error) {
        shopStatus.className = 'form-status admin-error';
        shopStatus.textContent = error.message;
    }
    finally {
        button.disabled = false;
    } });
    loadShopTools();
    searchShopCatalog();
    (function initAdminGifts() {
        const form = document.querySelector('#adminGiftForm'), recipient = document.querySelector('#adminGiftRecipient'), sendAll = document.querySelector('#adminGiftAll'), onlineOnly = document.querySelector('#adminGiftOnlineOnly'), playerSearch = document.querySelector('#adminGiftPlayerSearch'), playerResults = document.querySelector('#adminGiftPlayerResults'), giftType = document.querySelector('#adminGiftType'), amountField = document.querySelector('#adminGiftAmountField'), amount = document.querySelector('#adminGiftAmount'), boxDesign = document.querySelector('#adminGiftDesign'), boxDesignCustom = document.querySelector('#adminGiftDesignCustom'), boxRarityField = document.querySelector('#adminGiftBoxRarityField'), boxRarity = document.querySelector('#adminGiftBoxRarity'), catalog = document.querySelector('#adminGiftCatalog'), catalogSearch = document.querySelector('#adminGiftCatalogSearch'), catalogResults = document.querySelector('#adminGiftCatalogResults'), selectedBox = document.querySelector('#adminGiftSelected'), message = document.querySelector('#adminGiftMessage'), summary = document.querySelector('#adminGiftSummary'), send = document.querySelector('#adminGiftSend'), status = document.querySelector('#adminGiftStatus'), clearOutgoing = document.querySelector('#adminGiftClearOutgoing'), clearStatus = document.querySelector('#adminGiftClearStatus');
        let selectedItem = null, playerTimer, catalogTimer;
        const giftTypeLabel = () => ({ avatar: 'avatar item', equipment: 'equipment skin', consumable: 'consumable', box: 'level box', tokens: 'tokens', xp: 'XP' })[giftType.value] || 'gift';
        const giftAmountLimits = type => type === 'tokens' ? { min: -2147483648, max: 2147483647 } : type === 'consumable' ? { min: 1, max: 100000 } : { min: 1, max: 1000000 };
        function parseRecipientIds(raw) { return [...new Set((raw || '').split(/[,\s]+/).map(part => Number(part.trim())).filter(id => Number.isInteger(id) && id > 0))]; }
        function updateSummary() { const ids = parseRecipientIds(recipient.value), target = sendAll.checked ? (onlineOnly.checked ? 'every online player' : 'everyone') : ids.length === 1 ? `account #${ids[0]}` : ids.length > 1 ? `${ids.length} accounts (${ids.join(', ')})${onlineOnly.checked ? ' — online only' : ''}` : 'a recipient', item = giftType.value === 'tokens' ? `${Number(amount.value || 0).toLocaleString()} tokens` : giftType.value === 'xp' ? `${Number(amount.value || 0).toLocaleString()} XP` : giftType.value === 'box' ? `a rarity ${boxRarity.value} box` : selectedItem ? `${selectedItem.friendlyName}${giftType.value === 'consumable' ? ` x${Number(amount.value || 1).toLocaleString()}` : ''}` : `a ${giftTypeLabel()}`, designLabel = boxDesign.value === 'custom' ? `design #${resolveBoxDesign()}` : (GIFT_CONTEXTS.find(c => c.value === Number(boxDesign.value))?.name.replace(/_/g, ' ') || `design #${boxDesign.value}`); summary.textContent = `Coach will send ${item} to ${target} in a ${designLabel}.`; }
        function playerMarkup(person) { const id = accountIdOf(person), name = person.displayName ?? person.DisplayName ?? person.username ?? person.Username ?? `Player ${id}`, username = person.username ?? person.Username ?? ''; return `<button type="button" class="admin-gift-player" data-gift-player="${id}"><img src="${esc(person.profileImage ?? person.ProfileImage ?? '/imageserver/DefaultPFP.png')}" alt=""><span><strong>${esc(name)}</strong><small>${username ? '@' + esc(username) + ' &middot; ' : ''}#${id}</small></span><i class="fa-solid fa-plus"></i></button>`; }
        async function searchPlayers() { const term = playerSearch.value.trim(); if (!term) { playerResults.innerHTML = ''; return; } try { const players = await get('/recnet/api/admin/accounts?search=' + encodeURIComponent(term)); playerResults.innerHTML = players.slice(0, 12).map(playerMarkup).join('') || '<div class="empty compact-empty">No players found.</div>'; } catch (error) { playerResults.innerHTML = `<div class="admin-error">${esc(error.message)}</div>`; } }
        function catalogItemMarkup(item) { const kind = item.type === 'equipment' ? 'Skin' : item.type === 'consumable' ? 'Consumable' : 'Avatar'; return `<button type="button" class="admin-gift-catalog-item${selectedItem?.skuId === item.skuId ? ' selected' : ''}" data-gift-sku="${item.skuId}"><span class="admin-gift-item-icon"><i class="fa-solid ${item.type === 'consumable' ? 'fa-flask' : item.type === 'equipment' ? 'fa-wand-magic-sparkles' : 'fa-shirt'}"></i></span><span><strong>${esc(item.friendlyName || 'Unnamed item')}</strong><small>${kind} &middot; SKU ${item.skuId} &middot; ${shopStars(item.rarity)}</small></span><i class="fa-solid fa-check"></i></button>`; }
        async function searchGiftCatalog() { if (giftType.value === 'tokens' || giftType.value === 'xp') return; catalogResults.innerHTML = '<div class="loading"><i></i>Loading gift catalog</div>'; try { const items = await get(`/recnet/api/admin/gifts/catalog?take=50&type=${encodeURIComponent(giftType.value)}&search=${encodeURIComponent(catalogSearch.value.trim())}`); catalogResults.innerHTML = items.length ? items.map(catalogItemMarkup).join('') : '<div class="empty compact-empty">No matching gifts.</div>'; } catch (error) { catalogResults.innerHTML = `<div class="admin-error">${esc(error.message)}</div>`; } }
        function updateGiftType() { const numbered = giftType.value === 'tokens' || giftType.value === 'xp' || giftType.value === 'consumable', limits = giftAmountLimits(giftType.value), current = Number(amount.value); amountField.hidden = !numbered; amount.min = String(limits.min); amount.max = String(limits.max); if (!Number.isInteger(current) || current < limits.min || current > limits.max || (giftType.value === 'tokens' && current === 0)) amount.value = '1'; boxRarityField.hidden = giftType.value !== 'box'; catalog.hidden = giftType.value === 'tokens' || giftType.value === 'xp' || giftType.value === 'box'; selectedItem = null; selectedBox.className = 'admin-gift-selected empty'; selectedBox.textContent = giftType.value === 'box' ? 'Random consumable rolled per recipient.' : catalog.hidden ? giftType.value === 'tokens' ? 'Signed token gift — negative amounts deduct tokens.' : 'XP amount gift' : 'Choose an item.'; if (!catalog.hidden) searchGiftCatalog(); updateSummary(); }
        playerSearch.addEventListener('input', () => { clearTimeout(playerTimer); playerTimer = setTimeout(searchPlayers, 180); });
        playerResults.addEventListener('click', event => { const button = event.target.closest('[data-gift-player]'); if (!button) return; const existing = parseRecipientIds(recipient.value), id = Number(button.dataset.giftPlayer); recipient.value = existing.includes(id) ? existing.join(', ') : [...existing, id].join(', '); sendAll.checked = false; recipient.disabled = false; playerSearch.value = ''; playerResults.innerHTML = ''; updateSummary(); });
        sendAll.addEventListener('change', () => { recipient.disabled = sendAll.checked; playerSearch.disabled = sendAll.checked; if (sendAll.checked) playerResults.innerHTML = ''; updateSummary(); });
        onlineOnly.addEventListener('change', updateSummary);
        recipient.addEventListener('input', updateSummary);
        giftType.addEventListener('change', updateGiftType);
        const GIFT_CONTEXT_DEFS = [['None', -1], ['Default'], ['First_Activity'], ['Game_Drop'], ['All_Daily_Challenges_Complete'], ['All_Weekly_Challenge_Complete'], ['Daily_Challenge_Complete'], ['Weekly_Challenge_Complete'], ['Unassigned_Equipment', 10], ['Unassigned_Avatar'], ['Unassigned_Consumable'], ['Reacquisition', 20], ['Membership'], ['NUX_TokensAndDressUp', 30], ['NUX_Experiment1'], ['NUX_Experiment2'], ['NUX_Experiment3'], ['NUX_Experiment4'], ['NUX_Experiment5'], ['GameRewards', 50], ['GameRewards_Tokens'], ['LevelUp', 100], ['Purchased_Gift_A', 500], ['Purchased_Gift_B'], ['Purchased_Gift_C'], ['Purchased_Gift_D'], ['Holiday', 1000], ['Contest'], ['Promotion'], ['SubscribersOnly'], ['Deprecated', 1100], ['RecRoyale', 1200], ['DEPRECATED_Paintball_ClearCut', 2000], ['DEPRECATED_Paintball_Homestead'], ['DEPRECATED_Paintball_Quarry'], ['DEPRECATED_Paintball_River'], ['DEPRECATED_Paintball_Dam'], ['DEPRECATED_Paintball_DriveIn'], ['Paintball_ClearCut', 2010], ['Paintball_Homestead'], ['Paintball_Quarry'], ['Paintball_River'], ['Paintball_Dam'], ['Paintball_DriveIn'], ['DEPRECATED_Discgolf_Propulsion', 3000], ['DEPRECATED_Discgolf_Lake'], ['Discgolf_Propulsion', 3010], ['Discgolf_Lake'], ['Discgolf_Mode_CoopCatch', 3500], ['Quest_Goblin_A', 4000], ['Quest_Goblin_B'], ['Quest_Goblin_C'], ['Quest_Goblin_S'], ['Quest_Goblin_Consumable'], ['Quest_Cauldron_A', 4010], ['Quest_Cauldron_B'], ['Quest_Cauldron_C'], ['Quest_Cauldron_S'], ['Quest_Cauldron_Consumable'], ['Quest_Pirate1_A', 4100], ['Quest_Pirate1_B'], ['Quest_Pirate1_C'], ['Quest_Pirate1_S'], ['Quest_Pirate1_X'], ['Quest_Pirate1_Consumable'], ['Quest_Dracula1_A', 4200], ['Quest_Dracula1_B'], ['Quest_Dracula1_C'], ['Quest_Dracula1_S'], ['Quest_Dracula1_X'], ['Quest_Dracula1_Consumable'], ['Quest_Dracula1_SS'], ['Quest_SciFi_A', 4500], ['Quest_SciFi_B'], ['Quest_SciFi_C'], ['Quest_SciFi_S'], ['Quest_Scifi_Consumable'], ['DEPRECATED_Charades', 5000], ['Charades'], ['DEPRECATED_Soccer', 6000], ['Soccer'], ['DEPRECATED_Paddleball', 7000], ['Paddleball'], ['DEPRECATED_Dodgeball', 8000], ['Dodgeball'], ['DEPRECATED_Lasertag', 9000], ['Lasertag'], ['DEPRECATED_Bowling', 10000], ['Bowling'], ['StuntRunner_TheMainEvent_A', 11000], ['StuntRunner_TheMainEvent_B'], ['StuntRunner_TheMainEvent_C'], ['StuntRunner_TheMainEvent_D'], ['StuntRunner_TheMainEvent_S'], ['StuntRunner_TheMainEvent_X'], ['StuntRunner_TheMainEvent_Consumable'], ['StuntRunner_TheMainEvent_SS'], ['Store_LaserTag', 100000], ['Store_RecCenter', 100010], ['Consumable', 110000], ['Token', 110100], ['Punchcard_Challenge_Complete', 110200], ['All_Punchcard_Challenges_Complete'], ['Commerce_Purchase', 200000]];
        const GIFT_CONTEXTS = (() => { let next = 0; return GIFT_CONTEXT_DEFS.map(([name, value]) => { const v = value !== undefined ? value : next; next = v + 1; return { name, value: v }; }); })();
        (function populateGiftContexts() {
            const customOption = boxDesign.querySelector('option[value="custom"]');
            for (const { name, value } of GIFT_CONTEXTS) {
                const opt = document.createElement('option');
                opt.value = String(value);
                opt.textContent = `${name} (${value})`;
                boxDesign.insertBefore(opt, customOption);
            }
            boxDesign.querySelector('option[value="2"]').remove();
            boxDesign.querySelector('option[value="110000"]').remove();
            boxDesign.value = '2';
        })();
        boxRarity.addEventListener('change', updateSummary);
        function resolveBoxDesign() { return boxDesign.value === 'custom' ? Number(boxDesignCustom.value || 0) : Number(boxDesign.value); }
        boxDesign.addEventListener('change', () => { boxDesignCustom.hidden = boxDesign.value !== 'custom'; if (boxDesign.value === 'custom') boxDesignCustom.focus(); updateSummary(); });
        boxDesignCustom.addEventListener('input', updateSummary);
        amount.addEventListener('input', updateSummary);
        catalogSearch.addEventListener('input', () => { clearTimeout(catalogTimer); catalogTimer = setTimeout(searchGiftCatalog, 180); });
        catalogResults.addEventListener('click', event => { const button = event.target.closest('[data-gift-sku]'); if (!button) return; const rows = [...catalogResults.querySelectorAll('[data-gift-sku]')]; rows.forEach(row => row.classList.toggle('selected', row === button)); selectedItem = { skuId: Number(button.dataset.giftSku), friendlyName: button.querySelector('strong')?.textContent || 'Selected item' }; selectedBox.className = 'admin-gift-selected'; selectedBox.innerHTML = `<i class="fa-solid fa-circle-check"></i><span><strong>${esc(selectedItem.friendlyName)}</strong><small>SKU ${selectedItem.skuId}</small></span>`; updateSummary(); });
        form.addEventListener('submit', async event => { event.preventDefault(); const type = giftType.value, needsItem = !['tokens', 'xp', 'box'].includes(type), numericAmount = Number(amount.value), limits = giftAmountLimits(type), ids = parseRecipientIds(recipient.value); if (!sendAll.checked && ids.length === 0) { status.className = 'form-status admin-error'; status.textContent = 'Choose a recipient, or paste a comma-separated list of account IDs.'; return; } if (needsItem && !selectedItem) { status.className = 'form-status admin-error'; status.textContent = 'Choose an item from the catalog.'; return; } if (!Number.isInteger(numericAmount) || numericAmount < limits.min || numericAmount > limits.max || (type === 'tokens' && numericAmount === 0)) { status.className = 'form-status admin-error'; status.textContent = type === 'tokens' ? 'Enter a non-zero whole token amount from -2,147,483,648 to 2,147,483,647.' : `Enter a whole amount from ${limits.min.toLocaleString()} to ${limits.max.toLocaleString()}.`; return; } if (sendAll.checked && !confirm(`Send this gift from Coach (#1) to EVERY${onlineOnly.checked ? ' ONLINE' : ''} player account?`)) return; if (!sendAll.checked && ids.length > 1 && !confirm(`Send this gift from Coach (#1) to ${ids.length} accounts: ${ids.join(', ')}?`)) return; try { send.disabled = true; status.className = 'form-status'; status.textContent = sendAll.checked ? `Queuing gifts for every${onlineOnly.checked ? ' online' : ''} player...` : `Sending gift to ${ids.length} account${ids.length === 1 ? '' : 's'}...`; const result = await adminAction('/recnet/api/admin/gifts', 'POST', { recipientAccountId: ids[0] || 0, recipientAccountIds: ids, sendToAll: sendAll.checked, onlineOnly: onlineOnly.checked, giftType: type, skuId: selectedItem?.skuId || 0, amount: numericAmount, boxRarity: Number(boxRarity.value), boxDesign: resolveBoxDesign(), message: message.value.trim() }); const live = Number(result.livePlayers) || 0, pending = Number(result.pendingPlayers) || 0; status.textContent = `Sent ${result.gift} from Coach to ${result.queued} player${result.queued === 1 ? '' : 's'} — ${live} live now${pending ? `, ${pending} queued for fallback delivery` : ''}${result.failed ? ` (${result.failed} failed)` : ''}.`; } catch (error) { status.className = 'form-status admin-error'; status.textContent = error.message; } finally { send.disabled = false; } });
        clearOutgoing.addEventListener('click', async () => { if (!confirm('Clear every unclaimed outgoing gift box sent by Coach (#1)? Boxes already opened by players are not affected.')) return; try { clearOutgoing.disabled = true; clearStatus.className = 'form-status'; clearStatus.textContent = 'Clearing unclaimed boxes...'; const result = await adminAction('/recnet/api/admin/gifts/clear-outgoing', 'POST', { fromPlayerId: 1 }); clearStatus.textContent = `Removed ${result.removedBoxes} unclaimed box${result.removedBoxes === 1 ? '' : 'es'} across ${result.affectedPlayers} player${result.affectedPlayers === 1 ? '' : 's'}.`; } catch (error) { clearStatus.className = 'form-status admin-error'; clearStatus.textContent = error.message; } finally { clearOutgoing.disabled = false; } });
        message.addEventListener('input', updateSummary);
        updateGiftType();
    })();
    document.querySelector('#adminShopPanel').insertAdjacentHTML('afterend', `<section id="adminRoomsPanel" class="admin-panel admin-rooms-panel"><div class="admin-room-heading"><div><h2><i class="fa-solid fa-door-open"></i> Room management</h2><p>Edit room settings, stats, RoomRoles, subrooms, RoomBlob/metadata pairs, ownership, room bans, and import full room ZIP/JSON exports.</p></div><div class="admin-room-heading-actions"><input id="adminRoomImportFile" type="file" accept=".zip,.json,.txt,application/zip,application/x-zip-compressed,application/json,text/plain" hidden><button id="adminRoomImport" type="button" class="primary-button small-button"><i class="fa-solid fa-file-import"></i> Import ZIP / JSON</button><button id="adminRoomRefresh" type="button" class="secondary-button small-button"><i class="fa-solid fa-rotate"></i> Refresh</button></div></div><div id="adminRoomImportStatus" class="form-status admin-room-import-status" aria-live="polite"></div><div class="admin-room-browser"><aside class="admin-room-sidebar"><div class="admin-room-search-row"><input id="adminRoomSearch" class="search" placeholder="Search room, ID, or creator"><label class="admin-room-dorm-toggle"><input id="adminRoomIncludeDorms" type="checkbox"> Include dorms</label></div><div id="adminRoomList" class="admin-room-list"><div class="loading"><i></i>Loading rooms</div></div></aside><div id="adminRoomEditor" class="admin-room-editor"><div class="empty"><i class="fa-solid fa-arrow-left"></i> Pick a room to manage it.</div></div></div></section>`);
    (function initAdminRooms() {
        const panel = document.querySelector('#adminRoomsPanel'), search = document.querySelector('#adminRoomSearch'), includeDorms = document.querySelector('#adminRoomIncludeDorms'), list = document.querySelector('#adminRoomList'), editor = document.querySelector('#adminRoomEditor'), refresh = document.querySelector('#adminRoomRefresh'), importButton = document.querySelector('#adminRoomImport'), importFile = document.querySelector('#adminRoomImportFile'), importStatus = document.querySelector('#adminRoomImportStatus');
        let selectedRoomId = null, searchTimer;
        const assignedRoles = ['None', 'Host', 'Moderator', 'CoOwner', 'TemporaryCoOwner', 'Banned'];
        const invitedRoles = ['None', 'Host', 'Moderator', 'CoOwner', 'TemporaryCoOwner'];
        const roomStates = ['Active', 'PendingJunior', 'Moderation_PendingReview', 'Moderation_Closed', 'MarkedForDelete'];
        const accessibilities = ['Private', 'Public', 'Unlisted'];
        const flagLabels = { cloningAllowed: 'Cloning allowed', disableMicAutoMute: 'Disable mic auto-mute', disableRoomComments: 'Disable room comments', encryptVoiceChat: 'Encrypt voice chat', toxmodEnabled: 'Toxmod enabled', loadScreenLocked: 'Lock loading screen', autoLocalizeRoom: 'Auto-localize room', isDeveloperOwned: 'Developer owned', supportsLevelVoting: 'Supports level voting', isRRO: 'Rec Room Original', supportsScreens: 'Supports screens', supportsWalkVR: 'Walk VR', supportsTeleportVR: 'Teleport VR', supportsVRLow: 'Low-end VR', supportsQuest2: 'Quest 2', supportsMobile: 'Mobile', supportsJuniors: 'Junior accounts' };
        const optionMarkup = (values, current) => values.map(value => `<option value="${esc(value)}"${String(current) === value ? ' selected' : ''}>${esc(value)}</option>`).join('');
        const accountIdOf = entry => { const value = entry?.accountId ?? entry?.AccountId ?? entry?.player?.accountId ?? entry?.player?.AccountId; const accountId = Number(value); return Number.isSafeInteger(accountId) && accountId > 0 ? accountId : null; };
        const playerName = entry => entry?.player?.displayName || entry?.player?.DisplayName || entry?.player?.username || entry?.player?.Username || (accountIdOf(entry) ? `Player ${accountIdOf(entry)}` : 'Unknown player');
        const playerHandle = entry => { const username = entry?.player?.username ?? entry?.player?.Username; return username ? `@${username}` : (accountIdOf(entry) ? `#${accountIdOf(entry)}` : '#unknown'); };
        function roomSummaryMarkup(room) { return `<button type="button" class="admin-room-list-item${String(selectedRoomId) === String(room.roomId) ? ' selected' : ''}" data-room-id="${room.roomId}"><img src="${esc(room.image)}" alt=""><span class="admin-room-list-main"><strong>^${esc(room.name || 'UntitledRoom')}</strong><small>#${room.roomId} &middot; ${esc(room.creatorName)} &middot; ${esc(room.state)}</small><span><b>${Number(room.onlinePlayers || 0)}</b> online &middot; ${Number(room.roleCount || 0)} roles &middot; ${Number(room.subRoomCount || 0)} subrooms${room.isDorm ? ' &middot; Dorm' : ''}</span></span><i class="fa-solid fa-chevron-right"></i></button>`; }
        function roleRow(role, room) { const accountId = accountIdOf(role), roleName = role?.role ?? role?.Role ?? 'None', invitedRole = role?.invitedRole ?? role?.InvitedRole ?? 'None', profileImage = role?.player?.profileImage ?? role?.player?.ProfileImage ?? '', creator = accountId === Number(room.creatorAccountId) || roleName === 'Creator'; return `<div class="admin-room-member-row"${accountId ? ` data-role-account="${accountId}"` : ''}><img src="${esc(profileImage)}" alt=""><div><strong>${esc(playerName(role))}</strong><small>${esc(playerHandle(role))}${accountId ? ` &middot; #${accountId}` : ''}</small></div><span class="admin-room-role-badge">${esc(roleName)}</span>${invitedRole && invitedRole !== 'None' ? `<span class="admin-room-invite-badge">Invited: ${esc(invitedRole)}</span>` : ''}<div class="admin-room-row-actions">${!accountId ? '<span class="admin-error">Missing account ID</span>' : creator ? '<span class="muted">Owner</span>' : `<button type="button" class="secondary-button small-button" data-room-action="edit-role" data-account-id="${accountId}" data-role="${esc(roleName)}" data-invited-role="${esc(invitedRole)}"><i class="fa-solid fa-pen"></i> Edit</button><button type="button" class="danger-button small-button" data-room-action="remove-role" data-account-id="${accountId}"><i class="fa-solid fa-xmark"></i> Remove</button>`}</div></div>`; }
        function subRoomRow(sub) { const roomBlobOk = !!sub.roomBlobExists, metadataBlobOk = !!sub.metadataBlobExists; return `<article class="admin-subroom-card" data-subroom-id="${sub.subRoomId}"><div class="admin-subroom-title"><div><strong>${esc(sub.name)}</strong><small>#${sub.subRoomId} &middot; Save #${sub.currentSaveId || 0}${sub.hasData ? ' &middot; Has data' : ''}</small></div><span>${esc(sub.accessibility)}</span></div><div class="admin-subroom-grid"><label>Name<input data-subroom-field="name" maxlength="50" value="${esc(sub.name)}"></label><label>Max players<input data-subroom-field="maxPlayers" type="number" min="1" max="100" value="${Number(sub.maxPlayers) || 1}"></label><label>Accessibility<select data-subroom-field="accessibility">${optionMarkup(accessibilities, sub.accessibility)}</select></label><label>Unity scene ID<input data-subroom-field="unitySceneId" maxlength="200" value="${esc(sub.unitySceneId || '')}"></label><label class="admin-mini-check"><input data-subroom-field="isSandbox" type="checkbox"${sub.isSandbox ? ' checked' : ''}> Sandbox subroom</label></div><div class="admin-room-row-actions"><button type="button" class="primary-button small-button" data-room-action="save-subroom"><i class="fa-solid fa-floppy-disk"></i> Save subroom</button><button type="button" class="secondary-button small-button" data-room-action="clone-subroom"><i class="fa-solid fa-copy"></i> Clone</button><button type="button" class="danger-button small-button" data-room-action="delete-subroom"><i class="fa-solid fa-trash"></i> Delete</button></div><div class="admin-blob-editor"><div class="admin-blob-heading"><div><strong>Persistence blob pair</strong><small>These files must already exist in <code>CDN/room</code>. Saving creates a new active persistence save.</small></div><span class="admin-blob-health ${roomBlobOk && metadataBlobOk ? 'ok' : 'missing'}"><i class="fa-solid ${roomBlobOk && metadataBlobOk ? 'fa-circle-check' : 'fa-triangle-exclamation'}"></i> ${roomBlobOk && metadataBlobOk ? 'Files found' : 'Missing file'}</span></div><div class="admin-blob-grid"><label>RoomBlob <small>Room/object data</small><input class="admin-blob-input" data-subroom-field="roomBlob" maxlength="255" spellcheck="false" autocomplete="off" value="${esc(sub.roomBlob || '')}" placeholder="RoomBlob filename"></label><label>Metadata blob <small>Room-level metadata</small><input class="admin-blob-input" data-subroom-field="metadataBlob" maxlength="255" spellcheck="false" autocomplete="off" value="${esc(sub.metadataBlob || '')}" placeholder="Metadata blob filename"></label></div><div class="admin-blob-file-status"><span class="${roomBlobOk ? 'ok' : 'missing'}"><i class="fa-solid ${roomBlobOk ? 'fa-check' : 'fa-xmark'}"></i> RoomBlob ${roomBlobOk ? 'exists' : 'not found'}</span><span class="${metadataBlobOk ? 'ok' : 'missing'}"><i class="fa-solid ${metadataBlobOk ? 'fa-check' : 'fa-xmark'}"></i> Metadata ${metadataBlobOk ? 'exists' : 'not found'}</span></div><div class="admin-room-row-actions"><button type="button" class="primary-button small-button" data-room-action="save-blobs"><i class="fa-solid fa-database"></i> Save blob pair</button></div></div></article>`; }
        function banRow(ban) { const accountId = accountIdOf(ban), username = ban?.player?.username ?? ban?.player?.Username, profileImage = ban?.player?.profileImage ?? ban?.player?.ProfileImage ?? '', reason = ban?.reason ?? ban?.Reason ?? '', bannedAt = ban?.bannedAt ?? ban?.BannedAt; return `<div class="admin-room-member-row"><img src="${esc(profileImage)}" alt=""><div><strong>${esc(playerName(ban))}</strong><small>${username ? '@' + esc(username) + ' &middot; ' : ''}${accountId ? `#${accountId}` : '#unknown'} &middot; ${when(bannedAt)}</small><p>${esc(reason)}</p></div><div class="admin-room-row-actions">${accountId ? `<button type="button" class="secondary-button small-button" data-room-action="remove-ban" data-account-id="${accountId}"><i class="fa-solid fa-unlock"></i> Unban</button>` : '<span class="admin-error">Missing account ID</span>'}</div></div>`; }
        function roomEditorMarkup(room) { const flags = Object.entries(flagLabels).map(([key, label]) => `<label class="admin-check"><input type="checkbox" name="${key}"${room.flags?.[key] ? ' checked' : ''}><span><strong>${esc(label)}</strong></span></label>`).join(''); return `<div class="admin-room-editor-head"><img src="${esc(room.summary.image)}" alt=""><div><div class="page-kicker">Room #${room.roomId}</div><h3>^${esc(room.name)}</h3><p>Owned by ${esc(room.creator?.displayName || room.creator?.username || `Player ${room.creatorAccountId}`)} (#${room.creatorAccountId}) &middot; Version ${room.version}</p></div><a class="secondary-button small-button" href="#room/${room.roomId}"><i class="fa-solid fa-arrow-up-right-from-square"></i> Public page</a><button type="button" class="secondary-button small-button" data-room-action="export"><i class="fa-solid fa-file-arrow-down"></i> Backup</button></div><div id="adminRoomStatus" class="form-status" aria-live="polite"></div><form id="adminRoomGeneralForm" class="admin-room-section"><div class="admin-room-section-head"><div><h3>Room settings</h3><p>Core metadata, accessibility, state, tags, and compatibility flags.</p></div><button class="primary-button small-button" type="submit"><i class="fa-solid fa-floppy-disk"></i> Save room</button></div><div class="admin-room-form-grid"><label>Name<input name="name" maxlength="50" value="${esc(room.name)}" required></label><label>Image path<input name="imageName" maxlength="300" value="${esc(room.imageName || '')}"></label><label>Accessibility<select name="accessibility">${optionMarkup(accessibilities, room.accessibility)}</select></label><label>State<select name="state">${optionMarkup(roomStates, room.state)}</select></label><label>Max players<input name="maxPlayers" type="number" min="1" max="100" value="${room.maxPlayers}"></label><label>Minimum level<input name="minLevel" type="number" min="0" max="50" value="${room.minLevel}"></label><label class="wide">Tags <small>Comma-separated</small><input name="tags" value="${esc((room.tags || []).join(', '))}"></label><label class="wide">Description<textarea name="description" maxlength="2000">${esc(room.description || '')}</textarea></label></div><div class="admin-room-flags">${flags}</div></form><form id="adminRoomStatsForm" class="admin-room-section"><div class="admin-room-section-head"><div><h3>Room stats</h3><p>Directly change the counters returned by the room APIs.</p></div><button class="primary-button small-button" type="submit"><i class="fa-solid fa-chart-simple"></i> Save stats</button></div><div class="admin-room-stats-grid"><label>Cheers<input name="cheers" type="number" min="0" value="${room.stats.cheers}"></label><label>Favorites<input name="favorites" type="number" min="0" value="${room.stats.favorites}"></label><label>Visitors<input name="visitors" type="number" min="0" value="${room.stats.visitors}"></label><label>Visits<input name="visits" type="number" min="0" value="${room.stats.visits}"></label></div></form><section class="admin-room-section"><div class="admin-room-section-head"><div><h3>RoomRoles</h3><p>Add, update, invite, or remove a player&rsquo;s role.</p></div><span class="admin-room-count">${room.roles.length} entries</span></div><form id="adminRoomRoleForm" class="admin-room-inline-form"><label>Account ID<input name="accountId" type="number" min="1" required placeholder="Player account ID"></label><label>Assigned role<select name="role">${optionMarkup(assignedRoles, 'None')}</select></label><label>Invited role<select name="invitedRole">${optionMarkup(invitedRoles, 'None')}</select></label><button class="primary-button small-button" type="submit"><i class="fa-solid fa-user-shield"></i> Save role</button><button id="adminRoomRoleCancel" class="secondary-button small-button" type="button" hidden>Cancel edit</button></form><div class="admin-room-members">${room.roles.length ? room.roles.map(role => roleRow(role, room)).join('') : '<div class="empty">No RoomRoles assigned.</div>'}</div></section><section class="admin-room-section"><div class="admin-room-section-head"><div><h3>Transfer ownership</h3><p>Developer-only. The old creator becomes CoOwner.</p></div></div><form id="adminRoomOwnerForm" class="admin-room-inline-form"><label>New owner account ID<input name="accountId" type="number" min="1" required></label><button class="danger-button small-button" type="submit"><i class="fa-solid fa-crown"></i> Transfer owner</button></form></section><section class="admin-room-section"><div class="admin-room-section-head"><div><h3>Subrooms</h3><p>Create, clone, modify, or delete room subrooms.</p></div><span class="admin-room-count">${room.subRooms.length} subrooms</span></div><form id="adminSubRoomCreateForm" class="admin-room-inline-form"><label>New subroom name<input name="name" maxlength="50" required placeholder="Subroom name"></label><button class="primary-button small-button" type="submit"><i class="fa-solid fa-plus"></i> Add subroom</button></form><div class="admin-subroom-list">${room.subRooms.map(subRoomRow).join('')}</div></section><section class="admin-room-section"><div class="admin-room-section-head"><div><h3>Room bans</h3><p>Ban or unban players from this room.</p></div><span class="admin-room-count">${room.bans.length} active</span></div><form id="adminRoomBanForm" class="admin-room-inline-form"><label>Account ID<input name="accountId" type="number" min="1" required></label><label class="grow">Reason<input name="reason" maxlength="500" required placeholder="Room ban reason"></label><button class="danger-button small-button" type="submit"><i class="fa-solid fa-ban"></i> Ban from room</button></form><div class="admin-room-members">${room.bans.length ? room.bans.map(banRow).join('') : '<div class="empty">No active room bans.</div>'}</div></section>`; }
        function setStatus(message, error = false) { const status = document.querySelector('#adminRoomStatus'); if (!status)
            return; status.className = error ? 'form-status admin-error' : 'form-status'; status.textContent = message; }
        async function loadRooms() { list.innerHTML = '<div class="loading"><i></i>Loading rooms</div>'; try {
            const rooms = await get(`/recnet/api/admin/rooms?take=250&includeDorms=${includeDorms.checked ? 'true' : 'false'}&search=${encodeURIComponent(search.value.trim())}`);
            list.innerHTML = rooms.length ? rooms.map(roomSummaryMarkup).join('') : '<div class="empty">No rooms match that search.</div>';
        }
        catch (error) {
            list.innerHTML = `<div class="admin-error">${esc(error.message)}</div>`;
        } }
        async function openRoom(roomId, successMessage = '') { selectedRoomId = Number(roomId); editor.innerHTML = '<div class="loading"><i></i>Loading room editor</div>'; try {
            const room = await get(`/recnet/api/admin/rooms/${selectedRoomId}`);
            editor.innerHTML = roomEditorMarkup(room);
            if (successMessage)
                setStatus(successMessage);
            await loadRooms();
        }
        catch (error) {
            editor.innerHTML = `<div class="admin-error">${esc(error.message)}</div>`;
        } }
        async function mutate(url, method, body, message) { setStatus('Saving...'); try {
            await adminAction(url, method, body);
            await openRoom(selectedRoomId, message);
        }
        catch (error) {
            setStatus(error.message, true);
            throw error;
        } }
        function importedRoomDocuments(payload) { if (Array.isArray(payload))
            return payload; if (payload && typeof payload === 'object') {
            if (Array.isArray(payload.Rooms))
                return payload.Rooms;
            if (Array.isArray(payload.rooms))
                return payload.rooms;
            return [payload];
        } throw new Error('The file must contain a room object, an array of rooms, or an object with a Rooms array.'); }
        function setImportStatus(message, error = false) { importStatus.className = error ? 'form-status admin-room-import-status admin-error' : 'form-status admin-room-import-status'; importStatus.textContent = message; }
        async function sendRoomImport(payload, overwrite = false) { const response = await recnetFetch(`/recnet/api/admin/rooms/import?overwrite=${overwrite ? 'true' : 'false'}`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) }), data = await response.json().catch(() => ({})); if (response.status === 409 && !overwrite) {
            const conflicts = Array.isArray(data.conflictingRoomIds) && data.conflictingRoomIds.length ? `\n\nExisting IDs: ${data.conflictingRoomIds.map(id => '#' + id).join(', ')}` : '';
            if (confirm(`${data.error || 'One or more rooms already exist.'}${conflicts}\n\nOverwrite the existing room data?`))
                return sendRoomImport(payload, true);
        } if (!response.ok)
            throw new Error(data.error || 'Room import failed.'); return data; }
        async function sendRoomArchive(file, replaceExisting = true) { const form = new FormData(); form.append('file', file, file.name); form.append('replaceExisting', replaceExisting ? 'true' : 'false'); const response = await recnetFetch('/recnet/api/admin/rooms/import', { method: 'POST', body: form }), data = await response.json().catch(() => ({})); if (!response.ok)
            throw new Error(data.detail || data.error || 'Room ZIP import failed.'); return data; }
        importButton.addEventListener('click', () => importFile.click());
        importFile.addEventListener('change', async () => { const file = importFile.files?.[0]; if (!file)
            return; try {
            importButton.disabled = true;
            setImportStatus(`Reading ${file.name}...`);
            const lowerName = file.name.toLowerCase();
            const isZip = lowerName.endsWith('.zip') || file.type.includes('zip');
            if (isZip) {
                if (!confirm(`Import the full room archive ${file.name}?

This replaces an existing room with the same RoomId and installs its blobs, image, baked metadata, and asset bundles.`))
                    return;
                setImportStatus(`Uploading and importing ${file.name}...`);
                const result = await sendRoomArchive(file, true);
                const versions = Array.isArray(result.unityEngineVersions) && result.unityEngineVersions.length ? ` · Unity ${result.unityEngineVersions.join(', ')}` : '';
                const message = `Imported room #${result.roomId}: ${result.subRoomsImported || 0} subrooms, ${result.savesImported || 0} saves, ${result.bakedAssetsImported || 0} baked entries, ${result.assetBundlesCopied || 0} bundles copied${result.assetBundlesMissing ? `, ${result.assetBundlesMissing} missing` : ''}${versions}.`;
                setImportStatus(message, Number(result.assetBundlesMissing || 0) > 0);
                await loadRooms();
                if (Number.isSafeInteger(Number(result.roomId)) && Number(result.roomId) > 0)
                    await openRoom(Number(result.roomId), message);
                return;
            }
            const text = await file.text();
            let payload;
            try {
                payload = JSON.parse(text);
            }
            catch (error) {
                throw new Error(`Invalid JSON: ${error.message}`);
            }
            const documents = importedRoomDocuments(payload);
            if (!documents.length)
                throw new Error('The file does not contain any rooms.');
            const roomNames = documents.slice(0, 3).map(room => room?.Name || room?.name || `Room #${room?.RoomId ?? room?.roomId ?? 'new'}`).join(', '), more = documents.length > 3 ? ` and ${documents.length - 3} more` : '';
            if (!confirm(`Import ${documents.length} room${documents.length === 1 ? '' : 's'} from ${file.name}?\n\n${roomNames}${more}`))
                return;
            setImportStatus(`Importing ${documents.length} room${documents.length === 1 ? '' : 's'}...`);
            const result = await sendRoomImport(payload);
            const message = `Imported ${result.importedCount} room${result.importedCount === 1 ? '' : 's'}: ${result.createdCount} new, ${result.updatedCount} replaced.`;
            setImportStatus(message);
            await loadRooms();
            const firstRoomId = Number(result.rooms?.[0]?.roomId ?? result.rooms?.[0]?.RoomId);
            if (Number.isSafeInteger(firstRoomId) && firstRoomId > 0)
                await openRoom(firstRoomId, message);
        }
        catch (error) {
            setImportStatus(error.message, true);
            console.error(error);
        }
        finally {
            importButton.disabled = false;
            importFile.value = '';
        } });
        list.addEventListener('click', e => { const button = e.target.closest('[data-room-id]'); if (button)
            openRoom(button.dataset.roomId); });
        search.addEventListener('input', () => { clearTimeout(searchTimer); searchTimer = setTimeout(loadRooms, 180); });
        includeDorms.addEventListener('change', loadRooms);
        refresh.addEventListener('click', async () => { refresh.disabled = true; await loadRooms(); if (selectedRoomId)
            await openRoom(selectedRoomId); refresh.disabled = false; });
        editor.addEventListener('submit', async (e) => { e.preventDefault(); if (!selectedRoomId)
            return; const form = e.target; try {
            if (form.id === 'adminRoomGeneralForm') {
                const values = Object.fromEntries(new FormData(form));
                const body = { name: values.name, description: values.description, imageName: values.imageName, accessibility: values.accessibility, state: values.state, maxPlayers: Number(values.maxPlayers), minLevel: Number(values.minLevel), tags: String(values.tags || '').split(',').map(tag => tag.trim()).filter(Boolean) };
                Object.keys(flagLabels).forEach(key => body[key] = form.elements[key].checked);
                await mutate(`/recnet/api/admin/rooms/${selectedRoomId}`, 'PUT', body, 'Room settings saved.');
            }
            else if (form.id === 'adminRoomStatsForm') {
                const values = Object.fromEntries(new FormData(form));
                await mutate(`/recnet/api/admin/rooms/${selectedRoomId}/stats`, 'PUT', { cheers: Number(values.cheers), favorites: Number(values.favorites), visitors: Number(values.visitors), visits: Number(values.visits) }, 'Room stats saved.');
            }
            else if (form.id === 'adminRoomRoleForm') {
                const values = Object.fromEntries(new FormData(form));
                await mutate(`/recnet/api/admin/rooms/${selectedRoomId}/roles/${Number(values.accountId)}`, 'PUT', { role: values.role, invitedRole: values.invitedRole }, 'RoomRole saved.');
            }
            else if (form.id === 'adminRoomOwnerForm') {
                const accountId = Number(new FormData(form).get('accountId'));
                if (!confirm(`Transfer room #${selectedRoomId} to account #${accountId}?`))
                    return;
                await mutate(`/recnet/api/admin/rooms/${selectedRoomId}/transfer-owner`, 'POST', { accountId }, 'Room ownership transferred.');
            }
            else if (form.id === 'adminSubRoomCreateForm') {
                const name = String(new FormData(form).get('name') || '').trim();
                await mutate(`/recnet/api/admin/rooms/${selectedRoomId}/subrooms`, 'POST', { name }, 'Subroom created.');
            }
            else if (form.id === 'adminRoomBanForm') {
                const values = Object.fromEntries(new FormData(form));
                await mutate(`/recnet/api/admin/rooms/${selectedRoomId}/bans`, 'POST', { accountId: Number(values.accountId), reason: values.reason }, 'Player banned from room.');
            }
        }
        catch (error) {
            console.error(error);
        } });
        editor.addEventListener('click', async (e) => { const button = e.target.closest('[data-room-action],#adminRoomRoleCancel'); if (!button || !selectedRoomId)
            return; const action = button.dataset.roomAction; if (button.id === 'adminRoomRoleCancel') {
            const form = document.querySelector('#adminRoomRoleForm');
            form.reset();
            form.elements.accountId.readOnly = false;
            button.hidden = true;
            return;
        } try {
            button.disabled = true;
            if (action === 'export') {
                const response = await recnetFetch(`/recnet/api/admin/rooms/${selectedRoomId}/export`, { method: 'GET' });
                if (!response.ok) {
                    const data = await response.json().catch(() => ({}));
                    throw new Error(data.error || 'Room export failed.');
                }
                const blob = await response.blob();
                const disposition = response.headers.get('Content-Disposition') || '';
                const match = /filename="?([^";]+)"?/i.exec(disposition);
                const fileName = match ? match[1] : `room-${selectedRoomId}-backup.zip`;
                const url = URL.createObjectURL(blob);
                const link = document.createElement('a');
                link.href = url;
                link.download = fileName;
                document.body.appendChild(link);
                link.click();
                link.remove();
                URL.revokeObjectURL(url);
                setStatus('Backup downloaded.');
            }
            else if (action === 'edit-role') {
                const form = document.querySelector('#adminRoomRoleForm');
                form.elements.accountId.value = button.dataset.accountId;
                form.elements.accountId.readOnly = true;
                form.elements.role.value = button.dataset.role;
                form.elements.invitedRole.value = button.dataset.invitedRole;
                document.querySelector('#adminRoomRoleCancel').hidden = false;
                form.scrollIntoView({ behavior: 'smooth', block: 'center' });
                return;
            }
            else if (action === 'remove-role') {
                const accountId = Number(button.dataset.accountId);
                if (!Number.isSafeInteger(accountId) || accountId < 1)
                    throw new Error('This RoomRole is missing a valid account ID. Refresh the room and try again.');
                if (!confirm(`Remove account #${accountId}'s RoomRole?`))
                    return;
                await mutate(`/recnet/api/admin/rooms/${selectedRoomId}/roles/${accountId}`, 'DELETE', {}, 'RoomRole removed.');
            }
            else if (action === 'save-subroom') {
                const card = button.closest('[data-subroom-id]'), subRoomId = Number(card.dataset.subroomId);
                await mutate(`/recnet/api/admin/rooms/${selectedRoomId}/subrooms/${subRoomId}`, 'PUT', { name: card.querySelector('[data-subroom-field="name"]').value, maxPlayers: Number(card.querySelector('[data-subroom-field="maxPlayers"]').value), accessibility: card.querySelector('[data-subroom-field="accessibility"]').value, isSandbox: card.querySelector('[data-subroom-field="isSandbox"]').checked, unitySceneId: card.querySelector('[data-subroom-field="unitySceneId"]').value }, 'Subroom saved.');
            }
            else if (action === 'save-blobs') {
                const card = button.closest('[data-subroom-id]'), subRoomId = Number(card.dataset.subroomId), roomBlob = card.querySelector('[data-subroom-field="roomBlob"]').value.trim(), metadataBlob = card.querySelector('[data-subroom-field="metadataBlob"]').value.trim();
                if (!roomBlob || !metadataBlob)
                    throw new Error('Enter both the RoomBlob and metadata blob filenames.');
                if (roomBlob.toLowerCase() === metadataBlob.toLowerCase())
                    throw new Error('RoomBlob and metadata blob must be different files.');
                if (!confirm(`Change the active blob pair for room #${selectedRoomId}, subroom #${subRoomId}?\n\nRoomBlob: ${roomBlob}\nMetadata: ${metadataBlob}`))
                    return;
                await mutate(`/recnet/api/admin/rooms/${selectedRoomId}/subrooms/${subRoomId}/blobs`, 'PUT', { roomBlob, metadataBlob }, 'RoomBlob and metadata blob saved.');
            }
            else if (action === 'clone-subroom') {
                const subRoomId = Number(button.closest('[data-subroom-id]').dataset.subroomId);
                await mutate(`/recnet/api/admin/rooms/${selectedRoomId}/subrooms/${subRoomId}/clone`, 'POST', {}, 'Subroom cloned.');
            }
            else if (action === 'delete-subroom') {
                const subRoomId = Number(button.closest('[data-subroom-id]').dataset.subroomId);
                if (!confirm(`Delete subroom #${subRoomId} and its saved data?`))
                    return;
                await mutate(`/recnet/api/admin/rooms/${selectedRoomId}/subrooms/${subRoomId}`, 'DELETE', {}, 'Subroom deleted.');
            }
            else if (action === 'remove-ban') {
                const accountId = Number(button.dataset.accountId);
                if (!Number.isSafeInteger(accountId) || accountId < 1)
                    throw new Error('This room ban is missing a valid account ID. Refresh the room and try again.');
                if (!confirm(`Unban account #${accountId} from this room?`))
                    return;
                await mutate(`/recnet/api/admin/rooms/${selectedRoomId}/bans/${accountId}`, 'DELETE', {}, 'Room ban removed.');
            }
        }
        catch (error) {
            console.error(error);
        }
        finally {
            button.disabled = false;
        } });
        loadRooms();
    })();
    document.querySelector('#adminShopPanel').insertAdjacentHTML('afterend', `<section id="adminEventsPanel" class="admin-panel admin-events-panel"><h2><i class="fa-solid fa-calendar-days"></i> Events</h2><p>Post scheduled events. Pinned events always show first on the homepage.</p><form id="eventForm" class="settings-grid"><input type="hidden" name="eventId" value=""><label class="field wide">Title<input name="title" maxlength="80" required></label><label class="field wide">Description<textarea name="description" maxlength="1000"></textarea></label><label class="field">Starts<input name="startsAt" type="datetime-local" required></label><label class="field">Ends (optional)<input name="endsAt" type="datetime-local"></label><label class="field wide">Image path (optional)<input name="imageName" placeholder="PlayerImages/example.png"></label><label class="admin-check wide"><input name="pinned" type="checkbox"><span><strong>Pin to top</strong><small>Pinned events always show before non-pinned ones.</small></span></label></form><div class="form-actions"><button id="eventSubmit" class="primary-button" type="submit" form="eventForm">Create event</button><button id="eventCancel" class="secondary-button" type="button" hidden>Cancel edit</button><span id="eventStatus" class="form-status"></span></div><div id="eventList" class="admin-account-list"></div></section>`);
    (function initEvents() {
        const eventForm = document.querySelector('#eventForm'), eventList = document.querySelector('#eventList'), eventStatus = document.querySelector('#eventStatus'), eventCancel = document.querySelector('#eventCancel'), eventSubmit = document.querySelector('#eventSubmit');
        function toLocalInput(iso) { if (!iso)
            return ''; const d = new Date(iso); const pad = n => String(n).padStart(2, '0'); return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`; }
        function resetEventForm() { eventForm.reset(); eventForm.elements.eventId.value = ''; eventSubmit.textContent = 'Create event'; eventCancel.hidden = true; }
        function eventListItem(e) { return `<article class="admin-account"><div class="admin-account-summary"><div class="admin-account-main"><div class="admin-account-name">${esc(e.title)}${e.pinned ? ' <span class="admin-role">Pinned</span>' : ''}</div><div class="admin-platform">${when(e.startsAt)}${e.endsAt ? ' &rarr; ' + when(e.endsAt) : ''}</div></div><button type="button" class="admin-manage-button" data-action="edit-event" data-id="${e.id}">Edit</button><button type="button" class="danger-button small-button" data-action="delete-event" data-id="${e.id}"><i class="fa-solid fa-trash"></i> Delete</button></div></article>`; }
        async function loadEvents() { eventList.innerHTML = '<div class="loading"><i></i>Loading events</div>'; try {
            const events = await get('/recnet/api/admin/events');
            eventList.innerHTML = events.length ? events.map(eventListItem).join('') : '<div class="empty">No events yet.</div>';
        }
        catch (error) {
            eventList.innerHTML = `<div class="admin-error">${esc(error.message)}</div>`;
        } }
        eventForm.addEventListener('submit', async (e) => { e.preventDefault(); const values = Object.fromEntries(new FormData(eventForm)), id = values.eventId; const body = { title: values.title, description: values.description, imageName: values.imageName, startsAt: values.startsAt ? new Date(values.startsAt).toISOString() : null, endsAt: values.endsAt ? new Date(values.endsAt).toISOString() : null, pinned: eventForm.elements.pinned.checked }; eventStatus.className = 'form-status'; eventStatus.textContent = id ? 'Saving...' : 'Creating...'; try {
            eventSubmit.disabled = true;
            await adminAction(id ? `/recnet/api/admin/events/${id}` : '/recnet/api/admin/events', id ? 'PUT' : 'POST', body);
            eventStatus.textContent = id ? 'Event updated!' : 'Event created!';
            resetEventForm();
            await loadEvents();
        }
        catch (error) {
            eventStatus.className = 'form-status admin-error';
            eventStatus.textContent = error.message;
        }
        finally {
            eventSubmit.disabled = false;
        } });
        eventList.addEventListener('click', async (e) => { const button = e.target.closest('[data-action]'); if (!button)
            return; const id = button.dataset.id; if (button.dataset.action === 'delete-event') {
            if (!confirm('Delete this event?'))
                return;
            try {
                button.disabled = true;
                await adminAction(`/recnet/api/admin/events/${id}`, 'DELETE', {});
                await loadEvents();
            }
            catch (error) {
                alert(error.message);
            }
        }
        else if (button.dataset.action === 'edit-event') {
            try {
                const events = await get('/recnet/api/admin/events'), target = events.find(x => String(x.id) === String(id));
                if (!target)
                    return;
                eventForm.elements.eventId.value = target.id;
                eventForm.elements.title.value = target.title;
                eventForm.elements.description.value = target.description || '';
                eventForm.elements.imageName.value = target.image ? decodeURIComponent(target.image.replace('/imageserver/', '')) : '';
                eventForm.elements.startsAt.value = toLocalInput(target.startsAt);
                eventForm.elements.endsAt.value = target.endsAt ? toLocalInput(target.endsAt) : '';
                eventForm.elements.pinned.checked = !!target.pinned;
                eventSubmit.textContent = 'Save event';
                eventCancel.hidden = false;
                eventForm.scrollIntoView({ behavior: 'smooth', block: 'start' });
            }
            catch (error) {
                alert(error.message);
            }
        } });
        eventCancel.addEventListener('click', resetEventForm);
        loadEvents();
    })();
    const search = document.querySelector('#adminSearch'), list = document.querySelector('#adminAccounts');
    let timer;
    const roles = ['Screenshare', 'Moderator', 'Developer', 'Influencer', 'RRPlus', 'Keepsake'];
    const platforms = ['Steam', 'Oculus', 'PlayStation', 'Xbox', 'IOS', 'GooglePlay'];
    function accountCard(a) { return `<article class="admin-account${a.moderationLock ? ' is-locked' : ''}" data-account="${a.accountId}"><div class="admin-account-summary"><img class="avatar" src="${esc(a.profileImage)}" alt=""><div class="admin-account-main"><div class="admin-account-name">${esc(a.displayName || a.username)} <span class="muted">#${a.accountId}</span>${a.moderationLock ? '<span class="moderation-lock-badge"><i class="fa-solid fa-lock"></i> Moderation Lock</span>' : ''}</div><div class="admin-platform">@${esc(a.username)} &middot; ${a.platforms.map(p => `${esc(p.platform)}: ${esc(p.platformId)}`).join(' &middot; ')}</div></div><div class="admin-roles">${a.roles.map(r => `<span class="admin-role">${esc(r)}</span>`).join('')}</div><button type="button" class="admin-manage-button" data-action="manage">Manage</button></div><div class="admin-tools" hidden><section class="admin-tool-section"><h3>Profile identity</h3><div class="admin-profile-grid"><label>Display name<input class="admin-display-name" maxlength="32" value="${esc(a.displayName || '')}"></label><label>@Username<input class="admin-username" maxlength="20" value="${esc(a.username || '')}"></label><label class="wide">Bio<textarea class="admin-bio" maxlength="500">${esc(a.bio || '')}</textarea></label><label class="wide">Email<input class="admin-email" type="email" value="${esc(a.email || '')}"></label></div><button type="button" class="primary-button small-button" data-action="save-profile"><i class="fa-solid fa-floppy-disk"></i> Save profile</button></section><section class="admin-tool-section"><h3>Linked platforms</h3><div class="admin-platform-editor">${a.platforms.map(p => `<button type="button" class="admin-platform-chip" data-action="remove-platform" data-platform="${esc(p.platform)}" data-platform-id="${esc(p.platformId)}">${esc(p.platform)} ${esc(p.platformId)} <i class="fa-solid fa-xmark"></i></button>`).join('')}</div><div class="admin-tool-row"><select class="admin-platform-select">${platforms.map(p => `<option>${p}</option>`).join('')}</select><input class="admin-platform-id" inputmode="numeric" placeholder="Platform ID"><button type="button" class="primary-button small-button" data-action="add-platform"><i class="fa-solid fa-link"></i> Link</button></div></section><section class="admin-tool-section"><h3>Roles & permissions</h3><div class="admin-tool-row"><select class="admin-role-select">${roles.map(r => `<option${a.roles.includes(r) ? ' disabled' : ''}>${r}</option>`).join('')}</select><button type="button" class="primary-button small-button" data-action="add-role"><i class="fa-solid fa-plus"></i> Add role</button></div><div class="admin-role-editor">${a.roles.map(r => `<button type="button" class="admin-role removable" data-action="remove-role" data-role="${esc(r)}" title="Remove ${esc(r)}">${esc(r)} <i class="fa-solid fa-xmark"></i></button>`).join('') || '<span class="muted">No roles assigned</span>'}</div></section><section class="admin-tool-section moderation-lock-section"><h3><i class="fa-solid fa-gavel"></i> Player moderation</h3>${a.moderationLock ? `<div class="active-lock"><strong>Moderation Lock active</strong><pre>${esc(a.moderationLock.reason)}</pre><span>Issued ${when(a.moderationLock.issuedAt)}${a.moderationLock.isRelated ? ` &middot; Linked from @${esc(a.moderationLock.relatedUsername)}` : ''}</span><button type="button" class="secondary-button small-button" data-action="remove-lock"><i class="fa-solid fa-lock-open"></i> Remove lock</button></div>` : `<label class="admin-lock-reason-label">Permanent ban reason<textarea class="admin-lock-reason" maxlength="500" placeholder="Explain why this player is being locked"></textarea></label><label class="admin-check link-ban-check"><input class="admin-link-ban" type="checkbox"><span><strong>Link Ban</strong><small>Also permanently bans accounts sharing a linked platform ID. Their reason will include “Related Account ${esc(a.username)}”.</small></span></label><button type="button" class="moderation-lock-button" data-action="apply-lock"><i class="fa-solid fa-lock"></i> Apply Moderation Lock</button>`}</section><div class="admin-tool-actions"><div class="admin-coach-message"><textarea class="admin-coach-message-body" maxlength="2000" placeholder="Message this player as Coach"></textarea><button type="button" class="secondary-button small-button" data-action="send-coach-message"><i class="fa-solid fa-paper-plane"></i> Send as Coach</button></div><button type="button" class="secondary-button small-button" data-action="copy-id"><i class="fa-solid fa-copy"></i> Copy ID</button><button type="button" class="secondary-button small-button" data-action="force-join"><i class="fa-solid fa-arrows-turn-to-dots"></i> Force into my instance</button><button type="button" class="secondary-button small-button" data-action="force-me-into"><i class="fa-solid fa-right-to-bracket"></i> Force me into their instance</button><button type="button" class="secondary-button small-button" data-action="force-user-into"><i class="fa-solid fa-people-arrows"></i> Force user into their instance</button><button type="button" class="secondary-button small-button danger-button" data-action="troll-kick"><i class="fa-solid fa-plug-circle-xmark"></i> Kick + redirect (troll)</button><button type="button" class="secondary-button small-button danger-button" data-action="troll-fakebox"><i class="fa-solid fa-gift"></i> Send fake box</button><button type="button" class="secondary-button small-button danger-button" data-action="troll-fakebox-ban"><i class="fa-solid fa-gavel"></i> GRIM LABUBU ${esc(a.username)}</button><button type="button" class="secondary-button small-button" data-action="reset-password"><i class="fa-solid fa-key"></i> Reset password</button><a class="secondary-button small-button" href="#user/${a.accountId}"><i class="fa-solid fa-user"></i> View profile</a><button type="button" class="danger-button small-button" data-action="delete"><i class="fa-solid fa-trash"></i> Delete user</button></div></div></article>`; }
    function moderationAccountCard(a) { let html = accountCard(a); const start = html.indexOf('<section class="admin-tool-section moderation-lock-section">'), end = html.indexOf('<div class="admin-tool-actions">', start); let moderation; if (a.moderationLock) {
        moderation = `<section class="admin-tool-section moderation-lock-section"><h3><i class="fa-solid fa-lock"></i> Moderation Lock</h3><div class="active-lock"><strong>Moderation Lock active</strong><pre>Moderation Lock</pre><span>32-bit maximum duration &middot; Issued ${when(a.moderationLock.issuedAt)}</span><button type="button" class="secondary-button small-button" data-action="remove-lock"><i class="fa-solid fa-lock-open"></i> Remove Moderation Lock</button></div></section>`;
    }
    else if (a.ban) {
        moderation = `<section class="admin-tool-section moderation-lock-section normal-ban-section"><h3><i class="fa-solid fa-gavel"></i> Ban</h3><div class="active-lock"><strong>Player is banned</strong><pre>${esc(a.ban.reason)}</pre><span>Issued ${when(a.ban.issuedAt)}</span><button type="button" class="secondary-button small-button" data-action="remove-ban"><i class="fa-solid fa-unlock"></i> Unban player</button></div></section>`;
        html = html.replace('<article class="admin-account"', '<article class="admin-account is-banned"').replace(`<span class="muted">#${a.accountId}</span>`, `<span class="muted">#${a.accountId}</span><span class="ban-badge"><i class="fa-solid fa-gavel"></i> Banned</span>`);
    }
    else {
        moderation = `<section class="admin-tool-section moderation-lock-section normal-ban-section"><h3><i class="fa-solid fa-gavel"></i> Ban</h3><label class="admin-lock-reason-label">Ban reason<textarea class="admin-ban-reason" maxlength="500" placeholder="Enter the reason shown to the player"></textarea></label><label class="admin-check link-ban-check"><input class="admin-link-ban" type="checkbox"><span><strong>Link Ban</strong><small>Ban accounts sharing a platform ID and append a blank line plus “Related Account ${esc(a.username)}”.</small></span></label><button type="button" class="ban-button" data-action="apply-ban"><i class="fa-solid fa-gavel"></i> Ban player</button><div class="moderation-lock-option"><div><strong>Moderation Lock</strong><small>Separate from a normal ban. Reason is exactly “Moderation Lock” and duration is int.MaxValue.</small></div><button type="button" class="moderation-lock-button" data-action="apply-lock"><i class="fa-solid fa-lock"></i> Apply Moderation Lock</button></div></section>`;
    } return html.slice(0, start) + moderation + html.slice(end); }
    function durationAccountCard(a) { let html = moderationAccountCard(a); if (!a.ban && !a.moderationLock) {
        const duration = `<div class="admin-ban-duration"><label>Duration<input class="admin-ban-duration-amount" type="number" min="1" max="35791394" value="1"></label><label>Unit<select class="admin-ban-duration-unit"><option>Seconds</option><option>Minutes</option><option>Hours</option><option selected>Days</option><option>Weeks</option><option>Permanent</option></select></label></div>`;
        html = html.replace('<label class="admin-check link-ban-check">', duration + '<label class="admin-check link-ban-check">');
    }
    else if (a.ban && !a.moderationLock) {
        const detail = a.ban.duration === 0 ? 'Permanent' : `Expires ${when(new Date(new Date(a.ban.issuedAt).getTime() + a.ban.duration * 1000))}`;
        html = html.replace(`<span>Issued ${when(a.ban.issuedAt)}</span>`, `<span>Issued ${when(a.ban.issuedAt)} &middot; ${detail}</span>`);
    } return html; }
    function authenticAccountCard(a) { const username = String(a.username || a.accountId).replace(/^@/, ''); const tokenAmount = Number(a.balance || 0); const summary = `<span class="admin-balance-summary" title="Current token balance"><i class="fa-solid fa-coins"></i>${tokenAmount.toLocaleString()}</span>`; const balance = `<section class="admin-quick-balance"><div class="admin-quick-balance-title"><i class="fa-solid fa-coins"></i><strong>Tokens: ${tokenAmount.toLocaleString()}</strong></div><input class="admin-balance-amount" type="number" min="0" max="2147483647" step="1" value="1000" aria-label="Token amount"><button type="button" class="primary-button small-button" data-action="add-balance"><i class="fa-solid fa-plus"></i> Add tokens</button><button type="button" class="secondary-button small-button" data-action="set-balance"><i class="fa-solid fa-equals"></i> Set balance</button></section>`; return durationAccountCard(a).replace('<div class="admin-roles">', summary + '<div class="admin-roles">').replace('<div class="admin-tools" hidden>', balance + '<div class="admin-tools" hidden>').replace('data-action="manage">Manage</button>', 'data-action="manage">More tools</button>').replace(`Related Account ${a.username}`, `Related account: @${username}`); }
    function expandedAccountCard(a) { const details = `<section class="admin-tool-section"><h3><i class="fa-solid fa-sliders"></i> Account state & appearance</h3><div class="admin-profile-grid"><label>Level (1-50)<input class="admin-level" type="number" min="1" max="50" step="1" value="${Number(a.level) || 1}"></label><label>XP<input class="admin-xp" type="number" min="0" max="2147483647" step="1" value="${Number(a.xp) || 0}"></label><label>Username changes left<input class="admin-username-changes" type="number" min="0" max="1000000" step="1" value="${Number(a.availableUsernameChanges) || 0}"></label><label>Pronoun flags (0-63)<input class="admin-pronouns" type="number" min="0" max="63" step="1" value="${Number(a.personalPronouns) || 0}"></label><label class="wide">Display emoji<input class="admin-display-emoji" maxlength="16" value="${esc(a.displayEmoji || '')}" placeholder="Optional emoji shown with the name"></label><label class="wide">Profile image path<input class="admin-profile-image-path" maxlength="260" value="${esc(a.profileImagePath || 'DefaultPFP.png')}" placeholder="DefaultPFP.png"></label><label class="wide">Banner image path<input class="admin-banner-image-path" maxlength="260" value="${esc(a.bannerImagePath || '')}" placeholder="Leave blank for no banner"></label></div><label class="admin-mini-check"><input class="admin-is-junior" type="checkbox"${a.isJunior ? ' checked' : ''}> Junior account</label><button type="button" class="primary-button small-button" data-action="save-details"><i class="fa-solid fa-floppy-disk"></i> Save account state</button></section>`; return authenticAccountCard(a).replace('<section class="admin-tool-section"><h3>Linked platforms</h3>', details + '<section class="admin-tool-section"><h3>Linked platforms</h3>'); }
    function resettableAccountCard(a) { const resetButtons = '<button type="button" class="secondary-button small-button" data-action="reset-username"><i class="fa-solid fa-dice"></i> Reset username & display name</button><button type="button" class="secondary-button small-button" data-action="reset-pfp"><i class="fa-solid fa-user-circle"></i> Reset PFP</button><button type="button" class="secondary-button small-button" data-action="reset-banner"><i class="fa-solid fa-panorama"></i> Reset banner</button>'; return expandedAccountCard(a).replace('<button type="button" class="secondary-button small-button" data-action="reset-password">', resetButtons + '<button type="button" class="secondary-button small-button" data-action="reset-password">'); }
    function inventoryAccountCard(a) { const inventory = `<section class="admin-tool-section admin-inventory-section"><h3><i class="fa-solid fa-box-open"></i> Avatar items & consumables</h3><div class="admin-tool-row admin-inventory-search-row"><input class="admin-inventory-search" placeholder="Search AvatarItems.json / Consumables.json"><button type="button" class="secondary-button small-button" data-inventory-action="search"><i class="fa-solid fa-magnifying-glass"></i> Search</button></div><div class="admin-inventory-results"><span class="muted">Open tools to load this player's inventory.</span></div></section>`; return resettableAccountCard(a).replace('<div class="admin-tool-actions">', inventory + '<div class="admin-tool-actions">'); }
    function inventoryAvatarRow(item) { const name = item.friendlyName || item.avatarItemDesc || `Avatar item ${item.avatarItemId}`; return `<div class="admin-inventory-row"><div class="admin-inventory-item"><strong>${esc(name)}</strong><small>${esc(item.avatarItemDesc || '')}${item.avatarItemId ? ` &middot; #${item.avatarItemId}` : ''}</small></div><button type="button" class="${item.owned ? 'danger-button' : 'primary-button'} small-button" data-inventory-action="avatar-toggle" data-item-id="${Number(item.avatarItemId) || 0}" data-item-desc="${esc(item.avatarItemDesc || '')}" data-owned="${item.owned ? 'true' : 'false'}">${item.owned ? 'Remove' : 'Give'}</button></div>`; }
    function inventoryConsumableRow(item) { const name = item.friendlyName || item.consumableItemDesc || `Consumable ${item.consumableItemId}`; return `<div class="admin-inventory-row"><div class="admin-inventory-item"><strong>${esc(name)}</strong><small>${esc(item.consumableItemDesc || '')}${item.consumableItemId ? ` &middot; #${item.consumableItemId}` : ''}</small></div><div class="admin-consumable-editor"><input class="admin-consumable-quantity" type="number" min="0" max="100000" step="1" value="${Number(item.quantity) || 0}" aria-label="Quantity for ${esc(name)}"><button type="button" class="secondary-button small-button" data-inventory-action="consumable-save" data-item-id="${Number(item.consumableItemId) || 0}" data-item-desc="${esc(item.consumableItemDesc || '')}">Save</button></div></div>`; }
    async function loadAdminInventory(card, term = '') { const id = card.dataset.account, results = card.querySelector('.admin-inventory-results'); if (!results) return; results.innerHTML = '<div class="loading"><i></i>Loading inventory</div>'; try { const data = await get(`/recnet/api/admin/accounts/${id}/inventory?search=${encodeURIComponent(term)}`); const avatarItems = Array.isArray(data.avatarItems) ? data.avatarItems : [], consumables = Array.isArray(data.consumables) ? data.consumables : []; results.innerHTML = `<div class="admin-inventory-group"><div class="admin-inventory-group-title"><strong>Avatar items</strong><span>${avatarItems.filter(x => x.owned).length} owned in these results</span></div>${avatarItems.length ? avatarItems.map(inventoryAvatarRow).join('') : '<div class="empty compact-empty">No avatar items match.</div>'}</div><div class="admin-inventory-group"><div class="admin-inventory-group-title"><strong>Consumables</strong><span>Set 0 to remove</span></div>${consumables.length ? consumables.map(inventoryConsumableRow).join('') : '<div class="empty compact-empty">No consumables match.</div>'}</div>`; card.dataset.inventoryLoaded = 'true'; } catch (error) { results.innerHTML = `<div class="admin-error">${esc(error.message || 'Could not load inventory.')}</div>`; } }
    async function loadOverview() { const box = document.querySelector('#adminOverview'); try {
        const o = await get('/recnet/api/admin/overview');
        box.innerHTML = [['users', 'Accounts', o.accounts], ['user-shield', 'Admins', o.admins], ['lock', 'Mod Locks', o.moderationLocks], ['circle-check', 'Verified', o.verified], ['crown', 'RR+', o.rrPlus], ['door-open', 'Rooms', o.rooms], ['image', 'Photos', o.photos], ['heart', 'Cheers', o.cheers], ['comment', 'Comments', o.comments]].map(x => `<div class="admin-stat"><i class="fa-solid fa-${x[0]}"></i><strong>${x[2]}</strong><span>${x[1]}</span></div>`).join('');
    }
    catch {
        box.innerHTML = '<div class="admin-error">Could not load server overview.</div>';
    } }
    async function load() { list.innerHTML = '<div class="loading"><i></i>Loading accounts</div>'; const response = await recnetFetch('/recnet/api/admin/accounts?search=' + encodeURIComponent(search.value)); if (!response.ok) {
        list.innerHTML = '<div class="empty">Admin access denied.</div>';
        return;
    } const data = await response.json(); list.innerHTML = data.map(inventoryAccountCard).join('') || '<div class="empty">No accounts found.</div>'; }
    async function adminAction(url, method, body) { const response = await recnetFetch(url, { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }), data = await response.json().catch(() => ({})); if (!response.ok)
        throw new Error(data.error || 'Admin action failed.'); return data; }
    list.addEventListener('click', async (e) => { const button = e.target.closest('[data-action="add-balance"],[data-action="set-balance"]'); if (!button)
        return; e.stopImmediatePropagation(); const card = button.closest('.admin-account'), id = card.dataset.account, amount = Number.parseInt(card.querySelector('.admin-balance-amount').value, 10); if (!Number.isInteger(amount) || amount < 0) {
        alert('Enter a valid non-negative token amount.');
        return;
    } try {
        button.disabled = true;
        const result = await adminAction(`/recnet/api/admin/accounts/${id}/balance`, 'POST', { amount, add: button.dataset.action === 'add-balance' });
        alert(`Token balance is now ${Number(result.balance).toLocaleString()}.`);
        await Promise.all([load(), loadOverview()]);
    }
    catch (error) {
        alert(error.message);
    }
    finally {
        button.disabled = false;
    } });
    list.addEventListener('click', async (e) => { const button = e.target.closest('[data-action="save-details"]'); if (!button)
        return; e.stopImmediatePropagation(); const card = button.closest('.admin-account'), id = card.dataset.account, level = Number.parseInt(card.querySelector('.admin-level').value, 10), xp = Number.parseInt(card.querySelector('.admin-xp').value, 10), availableUsernameChanges = Number.parseInt(card.querySelector('.admin-username-changes').value, 10), personalPronouns = Number.parseInt(card.querySelector('.admin-pronouns').value, 10); if (!Number.isInteger(level) || level < 1 || level > 50) {
        alert('Level must be between 1 and 50.');
        return;
    } if (!Number.isInteger(xp) || xp < 0) {
        alert('XP cannot be negative.');
        return;
    } if (!Number.isInteger(availableUsernameChanges) || availableUsernameChanges < 0) {
        alert('Username changes cannot be negative.');
        return;
    } if (!Number.isInteger(personalPronouns) || personalPronouns < 0 || personalPronouns > 63) {
        alert('Pronoun flags must be between 0 and 63.');
        return;
    } try {
        button.disabled = true;
        await adminAction(`/recnet/api/admin/accounts/${id}/details`, 'PUT', { level, xp, availableUsernameChanges, personalPronouns, isJunior: card.querySelector('.admin-is-junior').checked, displayEmoji: card.querySelector('.admin-display-emoji').value, profileImage: card.querySelector('.admin-profile-image-path').value, bannerImage: card.querySelector('.admin-banner-image-path').value });
        await Promise.all([load(), loadOverview()]);
    }
    catch (error) {
        alert(error.message);
    }
    finally {
        button.disabled = false;
    } });
    list.addEventListener('keydown', async e => { if (e.key !== 'Enter' || !e.target.matches('.admin-inventory-search')) return; e.preventDefault(); const card = e.target.closest('.admin-account'); await loadAdminInventory(card, e.target.value); });
    list.addEventListener('click', async e => { const button = e.target.closest('[data-inventory-action]'); if (!button) return; e.stopImmediatePropagation(); const card = button.closest('.admin-account'), id = card.dataset.account, action = button.dataset.inventoryAction; try { button.disabled = true; if (action === 'search') { const input = card.querySelector('.admin-inventory-search'); await loadAdminInventory(card, input?.value || ''); return; } if (action === 'avatar-toggle') { const owned = button.dataset.owned === 'true'; await adminAction(`/recnet/api/admin/accounts/${id}/avatar-items`, 'POST', { avatarItemId: Number(button.dataset.itemId) || 0, avatarItemDesc: button.dataset.itemDesc || '', owned: !owned }); const input = card.querySelector('.admin-inventory-search'); await loadAdminInventory(card, input?.value || ''); return; } if (action === 'consumable-save') { const row = button.closest('.admin-inventory-row'), quantity = Number.parseInt(row.querySelector('.admin-consumable-quantity').value, 10); if (!Number.isInteger(quantity) || quantity < 0 || quantity > 100000) throw new Error('Quantity must be between 0 and 100000.'); await adminAction(`/recnet/api/admin/accounts/${id}/consumables`, 'POST', { consumableItemId: Number(button.dataset.itemId) || 0, consumableItemDesc: button.dataset.itemDesc || '', quantity }); const input = card.querySelector('.admin-inventory-search'); await loadAdminInventory(card, input?.value || ''); } } catch (error) { alert(error.message); } finally { button.disabled = false; } });
    list.addEventListener('click', async e => { const button = e.target.closest('[data-action="manage"]'); if (!button)
        return; e.stopImmediatePropagation(); const card = button.closest('.admin-account'), tools = card.querySelector('.admin-tools'); tools.hidden = !tools.hidden; button.textContent = tools.hidden ? 'Manage user & tokens' : 'Close'; if (!tools.hidden) { if (!card.dataset.inventoryLoaded) await loadAdminInventory(card); requestAnimationFrame(() => card.querySelector('.admin-token-section')?.scrollIntoView({ block: 'nearest', behavior: 'smooth' })); } });
    list.addEventListener('click', async (e) => { const button = e.target.closest('[data-action="apply-ban"]'); if (!button)
        return; e.stopImmediatePropagation(); const card = button.closest('.admin-account'), id = card.dataset.account, reason = card.querySelector('.admin-ban-reason').value.trim(), linkBan = card.querySelector('.admin-link-ban').checked, durationAmount = Number.parseInt(card.querySelector('.admin-ban-duration-amount').value, 10), durationUnit = card.querySelector('.admin-ban-duration-unit').value.toLowerCase(); if (!reason) {
        alert('Enter a ban reason first.');
        return;
    } if (durationUnit !== 'permanent' && (!Number.isInteger(durationAmount) || durationAmount < 1)) {
        alert('Choose a valid ban duration.');
        return;
    } if (!confirm(`Ban account #${id}${linkBan ? ' and its linked accounts' : ''} for ${durationUnit === 'permanent' ? 'a permanent duration' : durationAmount + ' ' + durationUnit}?`))
        return; try {
        button.disabled = true;
        const result = await adminAction(`/recnet/api/admin/accounts/${id}/ban`, 'POST', { reason, linkBan, durationAmount: durationUnit === 'permanent' ? 1 : durationAmount, durationUnit });
        alert(`Game ban applied to ${result.affectedCount} account${result.affectedCount === 1 ? '' : 's'}.`);
        await Promise.all([load(), loadOverview()]);
    }
    catch (error) {
        alert(error.message);
    }
    finally {
        button.disabled = false;
    } });
    list.addEventListener('click', async (e) => { const button = e.target.closest('[data-action]'); if (!button)
        return; const action = button.dataset.action; if (!['apply-ban', 'remove-ban', 'apply-lock', 'remove-lock'].includes(action))
        return; e.stopImmediatePropagation(); const card = button.closest('.admin-account'), id = card.dataset.account; try {
        button.disabled = true;
        if (action === 'apply-ban') {
            const reason = card.querySelector('.admin-ban-reason').value.trim(), linkBan = card.querySelector('.admin-link-ban').checked;
            if (!reason) {
                alert('Enter a ban reason first.');
                return;
            }
            if (!confirm(`Ban account #${id}${linkBan ? ' and its linked accounts' : ''}?`))
                return;
            const result = await adminAction(`/recnet/api/admin/accounts/${id}/ban`, 'POST', { reason, linkBan });
            alert(`Banned ${result.affectedCount} account${result.affectedCount === 1 ? '' : 's'}.`);
        }
        else if (action === 'remove-ban') {
            if (!confirm(`Unban account #${id}?`))
                return;
            await adminAction(`/recnet/api/admin/accounts/${id}/ban`, 'DELETE', {});
        }
        else if (action === 'apply-lock') {
            if (!confirm(`Apply a Moderation Lock to account #${id}? This uses the 32-bit maximum duration and the exact reason “Moderation Lock”.`))
                return;
            await adminAction(`/recnet/api/admin/accounts/${id}/moderation-lock`, 'POST', {});
        }
        else if (action === 'remove-lock') {
            if (!confirm(`Remove the Moderation Lock from account #${id}?`))
                return;
            await adminAction(`/recnet/api/admin/accounts/${id}/moderation-lock`, 'DELETE', { removeLinkedAccounts: false });
        }
        await Promise.all([load(), loadOverview()]);
    }
    catch (error) {
        alert(error.message);
    }
    finally {
        button.disabled = false;
    } });
    list.addEventListener('click', async (e) => { const button = e.target.closest('[data-action]'); if (!button)
        return; const card = button.closest('.admin-account'), id = card.dataset.account, action = button.dataset.action; if (action === 'manage') {
        const tools = card.querySelector('.admin-tools');
        tools.hidden = !tools.hidden;
        button.textContent = tools.hidden ? 'Manage' : 'Close';
        return;
    } try {
        button.disabled = true;
        if (action === 'save-profile') {
            await adminAction(`/recnet/api/admin/accounts/${id}/profile`, 'PUT', { displayName: card.querySelector('.admin-display-name').value, username: card.querySelector('.admin-username').value, bio: card.querySelector('.admin-bio').value, email: card.querySelector('.admin-email').value });
        }
        else if (action === 'send-coach-message') {
            const field = card.querySelector('.admin-coach-message-body'), message = field.value.trim();
            if (!message) {
                alert('Type a message first.');
                return;
            }
            await adminAction(`/recnet/api/admin/accounts/${id}/message`, 'POST', { message });
            field.value = '';
        }
        else if (action === 'add-platform') {
            await adminAction(`/recnet/api/admin/accounts/${id}/platforms`, 'POST', { platform: card.querySelector('.admin-platform-select').value, platformId: card.querySelector('.admin-platform-id').value, enabled: true });
        }
        else if (action === 'remove-platform') {
            if (!confirm(`Unlink ${button.dataset.platform} ${button.dataset.platformId} from account #${id}?`))
                return;
            await adminAction(`/recnet/api/admin/accounts/${id}/platforms`, 'POST', { platform: button.dataset.platform, platformId: button.dataset.platformId, enabled: false });
        }
        else if (action === 'add-role') {
            const role = card.querySelector('.admin-role-select').value;
            await adminAction(`/recnet/api/admin/accounts/${id}/roles`, 'POST', { role, enabled: true });
        }
        else if (action === 'remove-role') {
            if (!confirm(`Remove the ${button.dataset.role} role from account #${id}?`))
                return;
            await adminAction(`/recnet/api/admin/accounts/${id}/roles`, 'POST', { role: button.dataset.role, enabled: false });
        }
        else if (action === 'apply-lock') {
            const reason = card.querySelector('.admin-lock-reason').value.trim(), linkBan = card.querySelector('.admin-link-ban').checked;
            if (!reason) {
                alert('Enter a ban reason first.');
                return;
            }
            if (!confirm(`Apply a permanent Moderation Lock to account #${id}${linkBan ? ' and every linked account' : ''}?`))
                return;
            const result = await adminAction(`/recnet/api/admin/accounts/${id}/moderation-lock`, 'POST', { reason, linkBan });
            alert(`Moderation Lock applied to ${result.affectedCount} account${result.affectedCount === 1 ? '' : 's'}.`);
        }
        else if (action === 'remove-lock') {
            if (!confirm(`Remove the Moderation Lock from account #${id}?`))
                return;
            const removeLinked = confirm('Also unlock every account included in the same Link Ban?');
            const result = await adminAction(`/recnet/api/admin/accounts/${id}/moderation-lock`, 'DELETE', { removeLinkedAccounts: removeLinked });
            alert(`Removed ${result.affectedCount} Moderation Lock${result.affectedCount === 1 ? '' : 's'}.`);
        }
        else if (action === 'force-join') {
            const result = await adminAction(`/recnet/api/admin/accounts/${id}/force-join-instance`, 'POST', {});
            alert(result.delivered
                ? `Invite pushed live - they should see a prompt in-game to join "${result.roomName || 'your instance'}".`
                : `They're not currently connected, so nothing popped up live. The invite is saved and their server record now shows them in "${result.roomName || 'your instance'}" - it'll surface next time they connect.`);
        }
        else if (action === 'force-me-into') {
            const result = await adminAction(`/recnet/api/admin/accounts/${id}/force-me-into`, 'POST', {});
            alert(result.delivered
                ? `You've been dropped into "${result.roomName}" - it should pop up live for you.`
                : `Your server record now shows you in "${result.roomName}" - it'll surface next time your client checks in.`);
        }
        else if (action === 'force-user-into') {
            const targetInput = prompt('Account ID of the person whose instance to move this user into:', '');
            if (targetInput === null)
                return;
            const targetAccountId = Number(targetInput.trim());
            if (!Number.isInteger(targetAccountId) || targetAccountId < 1) {
                alert('Enter a valid account ID.');
                return;
            }
            const result = await adminAction(`/recnet/api/admin/accounts/${id}/force-into/${targetAccountId}`, 'POST', {});
            alert(result.delivered
                ? `Moved into "${result.roomName}" - it should pop up live for them. The other account was never notified.`
                : `Their server record now shows them in "${result.roomName}" - it'll surface next time their client checks in. The other account was never notified.`);
        }
        else if (action === 'troll-kick') {
            const roomName = prompt('Room name to send them to (leave blank to use your own current instance):', '');
            if (roomName === null)
                return;
            const result = await adminAction(`/recnet/api/admin/accounts/${id}/troll/kick-to-room`, 'POST', roomName.trim() ? { roomName: roomName.trim() } : {});
            alert(`Disconnected ${result.disconnectedSockets} live connection(s) and redirected them to "${result.roomName}".`);
        }
        else if (action === 'troll-fakebox') {
            const amountInput = prompt('Fake token amount to show them:', '5000');
            if (amountInput === null)
                return;
            const tokenAmount = Number(amountInput);
            if (!Number.isInteger(tokenAmount) || tokenAmount < 1) {
                alert('Enter a whole number of tokens.');
                return;
            }
            const result = await adminAction(`/recnet/api/admin/accounts/${id}/troll/fakebox-and-ban`, 'POST', { tokenAmount, banImmediately: false });
            alert(`Fake "Yo i levelled up!" box sent (showing level ${result.fakeLevel}, ${result.tokenAmount.toLocaleString()} tokens) - level-up effect played too.`);
        }
        else if (action === 'troll-fakebox-ban') {
            const amountInput = prompt('Fake token amount to show them before the ban:', '5000');
            if (amountInput === null)
                return;
            const tokenAmount = Number(amountInput);
            if (!Number.isInteger(tokenAmount) || tokenAmount < 1) {
                alert('Enter a whole number of tokens.');
                return;
            }
            if (!confirm(`Send the fake box, then immediately permaban #${id} for "Cheating" and auto link-ban any linked accounts?`))
                return;
            const result = await adminAction(`/recnet/api/admin/accounts/${id}/troll/fakebox-and-ban`, 'POST', { tokenAmount, banImmediately: true });
            alert(`Sent fake box (${result.tokenAmount.toLocaleString()} tokens) and permabanned ${result.affectedCount} account(s) for Cheating.`);
        }
        else if (action === 'copy-id') {
            await navigator.clipboard.writeText(id);
            button.innerHTML = '<i class="fa-solid fa-check"></i> Copied';
            return;
        }
        else if (action === 'reset-username') {
            if (!confirm(`Generate and assign a new username to account #${id}? Their display name will stay unchanged.`))
                return;
            const result = await adminAction(`/recnet/api/admin/accounts/${id}/username/reset`, 'POST', {});
            alert(`Username reset to @${result.username}.`);
        }
        else if (action === 'reset-password') {
            const password = prompt(`Enter a new password for account #${id}:`);
            if (password === null)
                return;
            await adminAction(`/recnet/api/admin/accounts/${id}/password`, 'PUT', { newPassword: password });
            alert('Password updated.');
            return;
        }
        else if (action === 'delete') {
            const confirmation = prompt(`This permanently deletes account #${id}. Type DELETE ${id} to confirm:`);
            if (confirmation === null)
                return;
            await adminAction(`/recnet/api/admin/accounts/${id}`, 'DELETE', { confirmation });
        }
        await Promise.all([load(), loadOverview()]);
    }
    catch (error) {
        alert(error.message);
    }
    finally {
        button.disabled = false;
    } });
    list.addEventListener('click', async (e) => { const button = e.target.closest('[data-action="reset-username"],[data-action="reset-pfp"],[data-action="reset-banner"]'); if (!button)
        return; e.stopImmediatePropagation(); const card = button.closest('.admin-account'), id = card.dataset.account, action = button.dataset.action; let url, message; if (action === 'reset-username') {
        if (!confirm(`Generate a new username and display name for account #${id}?`))
            return;
        url = `/recnet/api/admin/accounts/${id}/username/reset`;
        message = result => `Username and display name reset to ${result.displayName} (@${result.username}).`;
    }
    else if (action === 'reset-pfp') {
        if (!confirm(`Reset account #${id}'s profile picture to the default PFP?`))
            return;
        url = `/recnet/api/admin/accounts/${id}/images/pfp/reset`;
        message = () => `Profile picture reset to DefaultPFP.png.`;
    }
    else {
        if (!confirm(`Clear account #${id}'s profile banner?`))
            return;
        url = `/recnet/api/admin/accounts/${id}/images/banner/reset`;
        message = () => `Profile banner reset.`;
    } try {
        button.disabled = true;
        const result = await adminAction(url, 'POST', {});
        alert(message(result));
        await Promise.all([load(), loadOverview()]);
    }
    catch (error) {
        alert(error.message);
    }
    finally {
        button.disabled = false;
    } }, { capture: true });
    search.addEventListener('input', () => { clearTimeout(timer); timer = setTimeout(load, 180); });
    load();
    loadOverview();
    document.querySelector('#adminCreate').onsubmit = async (e) => { e.preventDefault(); const form = e.currentTarget, data = Object.fromEntries(new FormData(form)); const result = document.querySelector('#adminCreateResult'), button = form.querySelector('button'); button.disabled = true; button.textContent = 'Creating...'; const response = await recnetFetch('/recnet/api/admin/accounts', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(data) }), body = await response.json().catch(() => ({})); result.className = response.ok ? 'admin-result' : 'admin-result admin-error'; result.textContent = response.ok ? `Created @${body.username} as account #${body.accountId}.` : body.error || 'Account could not be created.'; button.disabled = false; button.textContent = 'Create account'; if (response.ok) {
        form.reset();
        load();
        loadOverview();
    } };
}
async function moderatorPanel() {
    if (!currentUser?.isModerator || currentUser?.isDeveloper) {
        app.innerHTML = '<div class="empty">You do not have permission to open the Moderator panel.</div>';
        return;
    }
    app.innerHTML = `<div class="page-kicker">Staff tools</div><h1 class="page-title">Moderator</h1><div class="subtitle">Moderate players and room bans. Account creation, roles, balances, shop controls, room editing, ownership, and deletion stay Developer-only.</div><div id="moderatorOverview" class="admin-overview"><div class="loading"><i></i>Loading moderation overview</div></div><div class="moderator-layout"><section class="admin-panel moderator-users-panel"><div class="moderator-panel-heading"><div><h2><i class="fa-solid fa-user-shield"></i> Player moderation</h2><p>Ban players, apply Moderation Locks, or reset inappropriate profile content.</p></div></div><input id="moderatorSearch" class="search" placeholder="Search account, username, or display name"><div id="moderatorAccounts" class="admin-account-list moderator-account-list"></div></section><section class="admin-panel moderator-rooms-panel"><div class="moderator-panel-heading"><div><h2><i class="fa-solid fa-door-closed"></i> Room bans</h2><p>Search a room and manage only its player-ban list.</p></div></div><div class="admin-room-search-row"><input id="moderatorRoomSearch" class="search" placeholder="Search room, ID, or creator"><label class="admin-room-dorm-toggle"><input id="moderatorIncludeDorms" type="checkbox"> Include dorms</label></div><div class="moderator-room-browser"><div id="moderatorRoomList" class="admin-room-list"><div class="loading"><i></i>Loading rooms</div></div><div id="moderatorRoomEditor" class="moderator-room-editor"><div class="empty"><i class="fa-solid fa-hand-pointer"></i>Select a room to manage its bans.</div></div></div></section></div>`;
    const overview = document.querySelector('#moderatorOverview'), search = document.querySelector('#moderatorSearch'), accounts = document.querySelector('#moderatorAccounts'), roomSearch = document.querySelector('#moderatorRoomSearch'), includeDorms = document.querySelector('#moderatorIncludeDorms'), roomList = document.querySelector('#moderatorRoomList'), roomEditor = document.querySelector('#moderatorRoomEditor');
    let accountTimer, roomTimer, selectedRoomId = null;
    const accountIdOf = entry => { const value = entry?.accountId ?? entry?.AccountId ?? entry?.player?.accountId ?? entry?.player?.AccountId; const id = Number(value); return Number.isSafeInteger(id) && id > 0 ? id : null; };
    const valueOf = (obj, lower, upper) => obj?.[lower] ?? obj?.[upper];
    async function moderatorAction(url, method, body = {}) { const response = await recnetFetch(url, { method, headers: { 'Content-Type': 'application/json' }, body: method === 'GET' ? undefined : JSON.stringify(body) }), data = await response.json().catch(() => ({})); if (!response.ok)
        throw new Error(data.error || data.message || 'Moderator action failed.'); return data; }
    function roleBadges(person) { const roles = person.roles ?? person.Roles ?? []; return roles.length ? roles.map(role => `<span class="admin-role">${esc(role)}</span>`).join('') : '<span class="admin-role">Player</span>'; }
    function accountMarkup(person) { const id = accountIdOf(person), roles = person.roles ?? person.Roles ?? [], developer = roles.some(role => String(role).toLowerCase() === 'developer'), ban = person.ban ?? person.Ban, lock = person.moderationLock ?? person.ModerationLock, profileImage = person.profileImage ?? person.ProfileImage ?? '/imageserver/DefaultPFP.png', displayName = person.displayName ?? person.DisplayName ?? person.username ?? person.Username ?? `Player ${id}`, username = person.username ?? person.Username ?? 'unknown'; return `<article class="admin-account${ban ? ' is-banned' : ''}${lock ? ' is-locked' : ''}" data-account="${id}"><div class="admin-account-summary"><img class="avatar" src="${esc(profileImage)}" alt=""><div class="admin-account-main"><div class="admin-account-name">${esc(displayName)} ${ban ? '<span class="ban-badge"><i class="fa-solid fa-ban"></i> Banned</span>' : ''}${lock ? '<span class="moderation-lock-badge"><i class="fa-solid fa-lock"></i> Locked</span>' : ''}</div><div class="admin-platform">@${esc(username)} &middot; #${id} &middot; Level ${Number(person.level ?? person.Level ?? 0)}</div></div><div class="admin-roles">${roleBadges(person)}</div><button type="button" class="admin-manage-button" data-mod-action="toggle">Manage</button></div><div class="admin-tools" hidden>${developer ? '<div class="moderator-protected"><i class="fa-solid fa-shield"></i><div><strong>Developer account protected</strong><small>Moderators cannot ban, lock, or reset this account.</small></div></div>' : `<section class="admin-tool-section"><h3>Profile cleanup</h3><div class="admin-tool-actions"><button type="button" class="secondary-button small-button" data-mod-action="reset-username"><i class="fa-solid fa-user-pen"></i> Reset username + display</button><button type="button" class="secondary-button small-button" data-mod-action="reset-pfp"><i class="fa-solid fa-image-portrait"></i> Reset PFP</button><button type="button" class="secondary-button small-button" data-mod-action="reset-banner"><i class="fa-solid fa-panorama"></i> Clear banner</button></div></section>${ban ? `<section class="admin-tool-section normal-ban-section"><h3>Active ban</h3><div class="active-lock"><strong>${esc(valueOf(ban, 'reason', 'Reason') || 'No reason')}</strong><span>${valueOf(ban, 'issuedAt', 'IssuedAt') ? when(valueOf(ban, 'issuedAt', 'IssuedAt')) : ''}</span><button type="button" class="secondary-button small-button" data-mod-action="unban"><i class="fa-solid fa-unlock"></i> Remove ban</button></div></section>` : `<section class="admin-tool-section normal-ban-section"><h3>Ban account</h3><textarea class="admin-ban-reason" placeholder="Reason (3-500 characters)"></textarea><div class="admin-ban-duration"><label>Amount<input class="admin-ban-amount" type="number" min="1" value="1"></label><label>Unit<select class="admin-ban-unit"><option value="seconds">Seconds</option><option value="minutes">Minutes</option><option value="hours">Hours</option><option value="days" selected>Days</option><option value="weeks">Weeks</option><option value="permanent">Permanent</option></select></label></div><label class="admin-mini-check"><input class="admin-link-ban" type="checkbox"> Ban linked accounts using the same platform ID</label><button type="button" class="ban-button" data-mod-action="ban"><i class="fa-solid fa-ban"></i> Apply ban</button></section>`}${lock ? `<section class="admin-tool-section moderation-lock-section"><h3>Moderation Lock</h3><div class="active-lock"><strong>${esc(valueOf(lock, 'reason', 'Reason') || 'Moderation Lock')}</strong><button type="button" class="secondary-button small-button" data-mod-action="remove-lock"><i class="fa-solid fa-unlock-keyhole"></i> Remove lock</button></div></section>` : `<section class="admin-tool-section moderation-lock-section"><h3>Moderation Lock</h3><p class="muted">Permanent lock for serious moderation cases.</p><button type="button" class="moderation-lock-button" data-mod-action="apply-lock"><i class="fa-solid fa-lock"></i> Apply Moderation Lock</button></section>`}`}</div></article>`; }
    async function loadOverview() { try {
        const data = await get('/recnet/api/admin/overview');
        overview.innerHTML = `<div class="admin-stat"><i class="fa-solid fa-users"></i><strong>${Number(data.accounts || 0).toLocaleString()}</strong><span>Accounts</span></div><div class="admin-stat"><i class="fa-solid fa-code"></i><strong>${Number(data.admins || 0).toLocaleString()}</strong><span>Developers</span></div><div class="admin-stat"><i class="fa-solid fa-lock"></i><strong>${Number(data.moderationLocks || 0).toLocaleString()}</strong><span>Mod locks</span></div><div class="admin-stat"><i class="fa-solid fa-door-open"></i><strong>${Number(data.rooms || 0).toLocaleString()}</strong><span>Rooms</span></div><div class="admin-stat"><i class="fa-solid fa-camera"></i><strong>${Number(data.photos || 0).toLocaleString()}</strong><span>Photos</span></div>`;
    }
    catch (error) {
        overview.innerHTML = `<div class="admin-error">${esc(error.message)}</div>`;
    } }
    async function loadAccounts() { accounts.innerHTML = '<div class="loading"><i></i>Loading accounts</div>'; try {
        const data = await get('/recnet/api/admin/accounts?search=' + encodeURIComponent(search.value.trim()));
        accounts.innerHTML = data.length ? data.map(accountMarkup).join('') : '<div class="empty">No matching accounts.</div>';
    }
    catch (error) {
        accounts.innerHTML = `<div class="admin-error">${esc(error.message)}</div>`;
    } }
    accounts.addEventListener('click', async (event) => { const button = event.target.closest('[data-mod-action]'); if (!button)
        return; const card = button.closest('.admin-account'), id = Number(card?.dataset.account), action = button.dataset.modAction; if (action === 'toggle') {
        const tools = card.querySelector('.admin-tools');
        tools.hidden = !tools.hidden;
        button.textContent = tools.hidden ? 'Manage' : 'Close';
        return;
    } if (!Number.isSafeInteger(id))
        return; try {
        button.disabled = true;
        if (action === 'reset-username') {
            if (!confirm(`Reset account #${id}'s username and display name?`))
                return;
            await moderatorAction(`/recnet/api/admin/accounts/${id}/username/reset`, 'POST');
        }
        else if (action === 'reset-pfp') {
            if (!confirm(`Reset account #${id}'s profile picture?`))
                return;
            await moderatorAction(`/recnet/api/admin/accounts/${id}/images/pfp/reset`, 'POST');
        }
        else if (action === 'reset-banner') {
            if (!confirm(`Clear account #${id}'s banner?`))
                return;
            await moderatorAction(`/recnet/api/admin/accounts/${id}/images/banner/reset`, 'POST');
        }
        else if (action === 'ban') {
            const reason = card.querySelector('.admin-ban-reason').value.trim(), unit = card.querySelector('.admin-ban-unit').value, amount = Number(card.querySelector('.admin-ban-amount').value), linkBan = card.querySelector('.admin-link-ban').checked;
            if (reason.length < 3) {
                alert('Enter a ban reason with at least 3 characters.');
                return;
            }
            if (!confirm(`Ban account #${id}${unit === 'permanent' ? ' permanently' : ` for ${amount} ${unit}`}${linkBan ? ' and linked accounts' : ''}?`))
                return;
            await moderatorAction(`/recnet/api/admin/accounts/${id}/ban`, 'POST', { reason, durationAmount: unit === 'permanent' ? 1 : amount, durationUnit: unit, linkBan });
        }
        else if (action === 'unban') {
            if (!confirm(`Unban account #${id}?`))
                return;
            await moderatorAction(`/recnet/api/admin/accounts/${id}/ban`, 'DELETE');
        }
        else if (action === 'apply-lock') {
            if (!confirm(`Apply a permanent Moderation Lock to account #${id}?`))
                return;
            await moderatorAction(`/recnet/api/admin/accounts/${id}/moderation-lock`, 'POST', {});
        }
        else if (action === 'remove-lock') {
            if (!confirm(`Remove the Moderation Lock from account #${id}?`))
                return;
            await moderatorAction(`/recnet/api/admin/accounts/${id}/moderation-lock`, 'DELETE', { removeLinkedAccounts: false });
        }
        await Promise.all([loadAccounts(), loadOverview()]);
    }
    catch (error) {
        alert(error.message);
    }
    finally {
        button.disabled = false;
    } });
    function roomListMarkup(room) { return `<button type="button" class="admin-room-list-item${String(selectedRoomId) === String(room.roomId) ? ' selected' : ''}" data-moderator-room="${room.roomId}"><img src="${esc(room.image)}" alt=""><span class="admin-room-list-main"><strong>^${esc(room.name || 'UntitledRoom')}</strong><small>#${room.roomId} &middot; ${esc(room.creatorName || 'Unknown creator')}</small><span>${Number(room.banCount || 0)} room bans &middot; ${Number(room.onlinePlayers || 0)} online${room.isDorm ? ' &middot; Dorm' : ''}</span></span><i class="fa-solid fa-chevron-right"></i></button>`; }
    function roomBanMarkup(ban) { const id = accountIdOf(ban), player = ban.player ?? ban.Player ?? {}, name = player.displayName ?? player.DisplayName ?? player.username ?? player.Username ?? `Player ${id}`, username = player.username ?? player.Username, profile = player.profileImage ?? player.ProfileImage ?? '/imageserver/DefaultPFP.png', reason = ban.reason ?? ban.Reason ?? '', date = ban.bannedAt ?? ban.BannedAt; return `<div class="admin-room-member-row"><img src="${esc(profile)}" alt=""><div><strong>${esc(name)}</strong><small>${username ? '@' + esc(username) + ' &middot; ' : ''}#${id}${date ? ' &middot; ' + when(date) : ''}</small><p>${esc(reason)}</p></div><div class="admin-room-row-actions"><button type="button" class="secondary-button small-button" data-room-ban-action="remove" data-account-id="${id}"><i class="fa-solid fa-unlock"></i> Unban</button></div></div>`; }
    function roomEditorMarkup(room) { const summary = room.summary ?? room.Summary ?? {}, bans = room.bans ?? room.Bans ?? [], creator = room.creator ?? room.Creator ?? {}; return `<div class="admin-room-editor-head"><img src="${esc(summary.image ?? summary.Image ?? '')}" alt=""><div><div class="page-kicker">Room #${room.roomId ?? room.RoomId}</div><h3>^${esc(room.name ?? room.Name ?? 'UntitledRoom')}</h3><p>Owned by ${esc(creator.displayName ?? creator.DisplayName ?? creator.username ?? creator.Username ?? `Player ${room.creatorAccountId ?? room.CreatorAccountId}`)}</p></div><a class="secondary-button small-button" href="#room/${room.roomId ?? room.RoomId}"><i class="fa-solid fa-arrow-up-right-from-square"></i> Public page</a></div><div id="moderatorRoomStatus" class="form-status"></div><section class="admin-room-section"><div class="admin-room-section-head"><div><h3>Room bans</h3><p>Moderators can add and remove room bans, but cannot edit the room itself.</p></div><span class="admin-room-count">${bans.length} active</span></div><form id="moderatorRoomBanForm" class="admin-room-inline-form moderator-room-ban-form"><label>Account ID<input name="accountId" type="number" min="1" required placeholder="Player account ID"></label><label class="grow">Reason<input name="reason" maxlength="500" required placeholder="Room-ban reason"></label><button class="primary-button small-button" type="submit"><i class="fa-solid fa-user-slash"></i> Add room ban</button></form><div class="admin-room-members">${bans.length ? bans.map(roomBanMarkup).join('') : '<div class="empty">No active room bans.</div>'}</div></section>`; }
    async function loadRooms() { roomList.innerHTML = '<div class="loading"><i></i>Loading rooms</div>'; try {
        const data = await get(`/recnet/api/admin/rooms?take=100&includeDorms=${includeDorms.checked}&search=${encodeURIComponent(roomSearch.value.trim())}`);
        roomList.innerHTML = data.length ? data.map(roomListMarkup).join('') : '<div class="empty">No matching rooms.</div>';
    }
    catch (error) {
        roomList.innerHTML = `<div class="admin-error">${esc(error.message)}</div>`;
    } }
    async function openRoom(id) { selectedRoomId = Number(id); roomEditor.innerHTML = '<div class="loading"><i></i>Loading room bans</div>'; try {
        const room = await get(`/recnet/api/admin/rooms/${selectedRoomId}`);
        roomEditor.innerHTML = roomEditorMarkup(room);
        roomList.querySelectorAll('[data-moderator-room]').forEach(item => item.classList.toggle('selected', Number(item.dataset.moderatorRoom) === selectedRoomId));
    }
    catch (error) {
        roomEditor.innerHTML = `<div class="admin-error">${esc(error.message)}</div>`;
    } }
    roomList.addEventListener('click', event => { const button = event.target.closest('[data-moderator-room]'); if (button)
        openRoom(button.dataset.moderatorRoom); });
    roomEditor.addEventListener('submit', async (event) => { if (event.target.id !== 'moderatorRoomBanForm')
        return; event.preventDefault(); const form = event.target, data = Object.fromEntries(new FormData(form)), status = document.querySelector('#moderatorRoomStatus'), button = form.querySelector('button'); try {
        button.disabled = true;
        status.className = 'form-status';
        status.textContent = 'Adding room ban...';
        await moderatorAction(`/recnet/api/admin/rooms/${selectedRoomId}/bans`, 'POST', { accountId: Number(data.accountId), reason: data.reason });
        status.textContent = 'Room ban added.';
        await Promise.all([openRoom(selectedRoomId), loadRooms()]);
    }
    catch (error) {
        status.className = 'form-status admin-error';
        status.textContent = error.message;
    }
    finally {
        button.disabled = false;
    } });
    roomEditor.addEventListener('click', async (event) => { const button = event.target.closest('[data-room-ban-action="remove"]'); if (!button)
        return; const id = Number(button.dataset.accountId); if (!confirm(`Remove player #${id}'s room ban?`))
        return; try {
        button.disabled = true;
        await moderatorAction(`/recnet/api/admin/rooms/${selectedRoomId}/bans/${id}`, 'DELETE');
        await Promise.all([openRoom(selectedRoomId), loadRooms()]);
    }
    catch (error) {
        alert(error.message);
    }
    finally {
        button.disabled = false;
    } });
    search.addEventListener('input', () => { clearTimeout(accountTimer); accountTimer = setTimeout(loadAccounts, 180); });
    roomSearch.addEventListener('input', () => { clearTimeout(roomTimer); roomTimer = setTimeout(loadRooms, 180); });
    includeDorms.addEventListener('change', loadRooms);
    await Promise.all([loadOverview(), loadAccounts(), loadRooms()]);
}
async function settings() {
    if (!currentUser) {
        app.innerHTML = '<section class="settings-card gated-settings"><i class="fa-solid fa-lock"></i><h1 class="page-title">Log in to manage your account</h1><p>Your profile, password, and account controls live here.</p><button id="settingsLogin" class="primary-button">Log in</button></section>';
        document.querySelector('#settingsLogin').onclick = () => loginDialog.showModal();
        return;
    }
    app.innerHTML = '<div class="loading"><i></i>Loading settings</div>';
    const s = await get('/recnet/api/account/settings'), badgeData = await get('/recnet/api/account/cheer-badge');
    app.innerHTML = `<div class="settings-wrap"><div class="page-kicker">Your account</div><h1 class="page-title">Settings</h1><div class="subtitle">Manage how you appear on RecNet and in game.</div><form id="profileSettings" class="settings-card"><h2 class="settings-title">Profile</h2><p>Update your public identity and profile details.</p><div class="settings-grid"><label class="field">Display name<input name="displayName" maxlength="32" value="${esc(s.displayName || '')}" required></label><label class="field">@AccountName<input name="username" maxlength="20" value="${esc(s.username || '')}" required><span class="field-hint">${s.availableUsernameChanges} username changes remaining</span></label><label class="field wide">Bio<textarea name="bio" maxlength="500">${esc(s.bio || '')}</textarea></label><label class="field wide">Email<input name="email" type="email" value="${esc(s.email || '')}"></label></div><div class="form-actions"><button class="primary-button" type="submit">Save profile</button><span class="form-status"></span></div></form><form id="imageSettings" class="settings-card"><h2 class="settings-title">Profile images</h2><p>Use an image path from the server, such as PlayerImages/photo.png.</p><div class="settings-grid"><label class="field">Profile picture<input name="profileImage" value="${esc(s.profileImage || 'DefaultPFP.png')}"></label><label class="field">Banner image<input name="bannerImage" value="${esc(s.bannerImage || '')}"></label></div><div class="form-actions"><button class="primary-button" type="submit">Save images</button><span class="form-status"></span></div></form><form id="passwordSettings" class="settings-card"><h2 class="settings-title">Password</h2><p>Choose a new password with at least eight characters.</p><div class="settings-grid"><label class="field">Current password<input name="currentPassword" type="password" autocomplete="current-password" required></label><label class="field">New password<input name="newPassword" type="password" autocomplete="new-password" minlength="8" required></label></div><div class="form-actions"><button class="primary-button" type="submit">Change password</button><span class="form-status"></span></div></form><form id="deleteAccount" class="settings-card danger-zone"><h2 class="settings-title">Danger Zone</h2><p>Deleting your account is permanent. Your server account will be removed immediately.</p><div class="settings-grid"><label class="field">Password<input name="password" type="password" required></label><label class="field">Type DELETE to confirm<input name="confirmation" required></label></div><div class="form-actions"><button class="danger-button" type="submit">Delete account permanently</button><span class="form-status"></span></div></form></div>`;
    const badgeIcon = value => ({ 0: 'fa-thumbs-up', 10: 'fa-heart', 20: 'fa-trophy', 30: 'fa-crown', 40: 'fa-palette', 9000: 'fa-code' })[value] || 'fa-star';
    document.querySelector('#passwordSettings').insertAdjacentHTML('beforebegin', `<section id="badgeSettings" class="settings-card badge-settings"><h2 class="settings-title">Profile badge</h2><p>Choose the cheer badge displayed on your nametag.${badgeData.isDeveloper ? ' The Developer badge is available here because the 2023 in-game picker only shows its five normal slots.' : ''}</p><div class="website-badge-picker">${badgeData.badges.map(badge => `<button type="button" class="website-badge${Number(badgeData.selectedBadge) === Number(badge.value) ? ' selected' : ''}" data-badge="${badge.value}" ${badge.unlocked ? '' : 'disabled'}><span class="website-badge-icon ${Number(badge.value) === 9000 ? 'developer-badge' : ''}">${Number(badge.value) === 9000 ? '<b>RR</b>' : `<i class="fa-solid ${badgeIcon(Number(badge.value))}"></i>`}</span><strong>${esc(badge.name)}</strong><small>${Number(badge.value) === 9000 ? 'Developer access' : `${Number(badge.count).toLocaleString()} cheers`}</small></button>`).join('')}</div><div class="form-actions"><span class="form-status"></span></div></section>`);
    const badgeSettings = document.querySelector('#badgeSettings');
    badgeSettings.addEventListener('click', async (e) => { const button = e.target.closest('[data-badge]'); if (!button || button.disabled)
        return; const status = badgeSettings.querySelector('.form-status'); try {
        button.disabled = true;
        status.textContent = 'Saving badge...';
        const response = await recnetFetch('/recnet/api/account/cheer-badge', { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ badge: Number(button.dataset.badge) }) }), body = await response.json().catch(() => ({}));
        if (!response.ok)
            throw new Error(body.error || 'Could not save badge.');
        badgeSettings.querySelectorAll('[data-badge]').forEach(option => option.classList.toggle('selected', Number(option.dataset.badge) === Number(body.selectedBadge)));
        status.textContent = 'Profile badge saved!';
    }
    catch (error) {
        status.textContent = error.message;
    }
    finally {
        button.disabled = false;
    } });
    const profileForm = document.querySelector('#profileSettings'), imageForm = document.querySelector('#imageSettings');
    const pfpField = imageForm.querySelector('[name="profileImage"]').closest('label');
    pfpField.classList.add('wide');
    pfpField.innerHTML = `Profile picture<div class="profile-upload-row"><img id="profileUploadPreview" class="profile-upload-preview" src="${esc('/imageserver/' + (s.profileImage || 'DefaultPFP.png'))}" alt="Current profile picture"><div class="file-picker"><input id="profileFile" type="file" accept="image/png,image/jpeg,image/webp,image/gif,image/bmp"><span class="field-hint">PNG, JPG, WebP, GIF, or BMP. Maximum 10 MB and 4096 x 4096.</span></div></div><input name="profileImage" type="hidden" value="${esc(s.profileImage || 'DefaultPFP.png')}">`;
    const profileFile = document.querySelector('#profileFile'), profilePreview = document.querySelector('#profileUploadPreview');
    profileFile.addEventListener('change', () => { const file = profileFile.files[0]; if (file)
        profilePreview.src = URL.createObjectURL(file); });
    const bannerField = imageForm.querySelector('[name="bannerImage"]').closest('label');
    bannerField.classList.add('wide');
    bannerField.innerHTML = `Banner image<div class="banner-upload-row"><img id="bannerUploadPreview" class="banner-upload-preview" src="${esc('/imageserver/' + (s.bannerImage || 'DefaultPFP.png'))}" alt="Current banner"><div class="file-picker"><input id="bannerFile" type="file" accept="image/png,image/jpeg,image/webp,image/gif,image/bmp"><span class="field-hint">Wide images look best. Maximum 10 MB and 8192 x 4096.</span></div></div><input name="bannerImage" type="hidden" value="${esc(s.bannerImage || '')}">`;
    const bannerFile = document.querySelector('#bannerFile'), bannerPreview = document.querySelector('#bannerUploadPreview');
    bannerFile.addEventListener('change', () => { const file = bannerFile.files[0]; if (file)
        bannerPreview.src = URL.createObjectURL(file); });
    async function saveAll(form) { const data = { ...Object.fromEntries(new FormData(profileForm)), ...Object.fromEntries(new FormData(imageForm)) }; const response = await recnetFetch('/recnet/api/account/settings', { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(data) }); const body = await response.json().catch(() => ({})); const status = form.querySelector('.form-status'); status.textContent = response.ok ? 'Saved!' : body.error || 'Could not save.'; if (response.ok)
        showSession(body.session); }
    profileForm.onsubmit = e => { e.preventDefault(); saveAll(profileForm); };
    imageForm.onsubmit = async (e) => { e.preventDefault(); const profile = profileFile.files[0], banner = bannerFile.files[0], status = imageForm.querySelector('.form-status'); if (profile) {
        status.textContent = 'Uploading profile picture...';
        const upload = new FormData();
        upload.append('file', profile);
        const response = await recnetFetch('/recnet/api/account/profile-image', { method: 'POST', body: upload }), body = await response.json().catch(() => ({}));
        if (!response.ok) {
            status.textContent = body.error || 'Profile upload failed.';
            return;
        }
        imageForm.querySelector('[name="profileImage"]').value = body.path;
        showSession(body.session);
    } if (banner) {
        status.textContent = 'Uploading banner...';
        const upload = new FormData();
        upload.append('file', banner);
        const response = await recnetFetch('/recnet/api/account/banner-image', { method: 'POST', body: upload }), body = await response.json().catch(() => ({}));
        if (!response.ok) {
            status.textContent = body.error || 'Banner upload failed.';
            return;
        }
        imageForm.querySelector('[name="bannerImage"]').value = body.path;
    } await saveAll(imageForm); };
    document.querySelector('#passwordSettings').onsubmit = async (e) => { e.preventDefault(); const form = e.currentTarget, response = await recnetFetch('/recnet/api/account/password', { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(Object.fromEntries(new FormData(form))) }), body = await response.json().catch(() => ({})); form.querySelector('.form-status').textContent = response.ok ? 'Password changed!' : body.error || 'Could not change password.'; if (response.ok)
        form.reset(); };
    document.querySelector('#deleteAccount').onsubmit = async (e) => { e.preventDefault(); const form = e.currentTarget, data = Object.fromEntries(new FormData(form)); if (data.confirmation !== 'DELETE' || !confirm('Permanently delete this account? This cannot be undone.'))
        return; const response = await recnetFetch('/recnet/api/account', { method: 'DELETE', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(data) }), body = await response.json().catch(() => ({})); if (response.ok) {
        showSession(null);
        location.hash = 'home';
    }
    else
        form.querySelector('.form-status').textContent = body.error || 'Could not delete account.'; };
}
function select(view) { document.querySelectorAll('.nav-link').forEach(x => x.classList.toggle('active', x.dataset.view === view)); }
async function ageVerificationPage(prefillCode) {
    if (!currentUser) {
        app.innerHTML = '<section class="settings-card gated-settings"><i class="fa-solid fa-lock"></i><h1 class="page-title">Log in to verify your age</h1><p>You need to be logged in as the account that requested a code in-game.</p><button id="ageVerificationLogin" class="primary-button">Log in</button></section>';
        document.querySelector('#ageVerificationLogin').onclick = () => loginDialog.showModal();
        return;
    }
    app.innerHTML = `<div class="settings-wrap"><div class="page-kicker">Age verification</div><h1 class="page-title">Submit your code</h1>
    <section class="settings-card">
      <p>Request a code in-game first (Settings &rsquo; Age Verification). Enter it below, choose a method, and attach a photo. A staff member will review it manually - there's no automated ID or face matching.</p>
    </section>
    <form id="ageVerificationForm" class="settings-card">
      <h2 class="settings-title">Verify</h2>
      <label class="field wide">Code from the game<input name="code" maxlength="6" required placeholder="e.g. 7K3PQR" value="${esc(prefillCode || '')}" style="text-transform:uppercase"></label>
      <label class="field wide">Method
        <select name="method" required>
          <option value="ManualId">Manual ID Verification (photo deleted after review)</option>
          <option value="FaceVerification">Face Verification (Manually)</option>
        </select>
      </label>
      <label class="field wide">Photo<input name="photo" type="file" accept="image/png,image/jpeg,image/webp" required></label>
      <div class="form-actions"><button class="primary-button" type="submit">Submit for review</button><span class="form-status"></span></div>
    </form></div>`;

    document.querySelector('#ageVerificationForm').addEventListener('submit', async (e) => {
        e.preventDefault();
        const form = e.target, button = form.querySelector('button[type=submit]'), statusEl = form.querySelector('.form-status');
        const file = form.elements.photo.files[0];
        if (!file) { statusEl.textContent = 'Attach a photo.'; return; }
        try {
            button.disabled = true;
            statusEl.textContent = 'Uploading...';
            const body = new FormData();
            body.append('code', form.elements.code.value.trim().toUpperCase());
            body.append('method', form.elements.method.value);
            body.append('file', file);
            const r = await recnetFetch('/recnet/api/ageverification/submit', { method: 'POST', body });
            const result = await r.json().catch(() => ({}));
            if (!r.ok)
                throw new Error(result.error || 'Could not submit your verification.');
            statusEl.textContent = 'Submitted - a staff member will review it soon.';
            form.querySelectorAll('input, select, button').forEach(el => el.disabled = true);
        }
        catch (error) {
            statusEl.textContent = error.message;
            button.disabled = false;
        }
    });
}

async function banAppeal() {
    const code = new URLSearchParams(location.search).get('code') || '';
    if (!currentUser) {
        app.innerHTML = '<section class="settings-card gated-settings"><i class="fa-solid fa-lock"></i><h1 class="page-title">Log in to appeal your ban</h1><p>You need to be logged in as the banned account to submit an appeal.</p><button id="appealLogin" class="primary-button">Log in</button></section>';
        document.querySelector('#appealLogin').onclick = () => loginDialog.showModal();
        return;
    }
    if (!code) {
        app.innerHTML = '<section class="settings-card gated-settings"><i class="fa-solid fa-triangle-exclamation"></i><h1 class="page-title">Missing appeal code</h1><p>This link is missing its appeal code. Use the link that was given to you when you were banned.</p></section>';
        return;
    }
    app.innerHTML = '<div class="loading"><i></i>Loading your ban details</div>';
    let status;
    try {
        status = await get(`/recnet/api/banappeal/status?code=${encodeURIComponent(code)}`);
    }
    catch (error) {
        app.innerHTML = `<section class="settings-card gated-settings"><i class="fa-solid fa-triangle-exclamation"></i><h1 class="page-title">Can't load this appeal</h1><p>${esc(error.message)}</p></section>`;
        return;
    }
    if (status.alreadySubmitted) {
        app.innerHTML = `<div class="settings-wrap"><div class="page-kicker">Ban appeal</div><h1 class="page-title">Appeal already submitted</h1><section class="settings-card"><p>You already submitted an appeal for this ban on ${new Date(status.submittedAtUtc).toLocaleString()}. Sit tight - a moderator will review it.</p></section></div>`;
        return;
    }
    app.innerHTML = `<div class="settings-wrap"><div class="page-kicker">Ban appeal</div><h1 class="page-title">Appeal your ban</h1><section class="settings-card"><h2 class="settings-title">Why you were banned</h2><p>${esc(status.banReason || 'No reason was recorded.')}</p><p class="field-hint">${status.banIsPermanent ? 'This is a permanent ban.' : `Banned ${new Date(status.bannedAtUnix * 1000).toLocaleString()}`}</p></section><form id="appealForm" class="settings-card"><h2 class="settings-title">Your appeal</h2><p>Explain why you think this ban should be reconsidered. Be honest - staff can see your account's full history.</p><label class="field wide">Appeal message<textarea name="message" minlength="10" maxlength="2000" required placeholder="Explain your side..."></textarea></label><div class="form-actions"><button class="primary-button" type="submit">Submit appeal</button><span class="form-status"></span></div></form></div>`;
    document.querySelector('#appealForm').addEventListener('submit', async (e) => {
        e.preventDefault();
        const form = e.target, button = form.querySelector('button[type=submit]'), statusEl = form.querySelector('.form-status');
        try {
            button.disabled = true;
            statusEl.textContent = 'Submitting...';
            const message = form.elements.message.value.trim();
            const r = await recnetFetch('/recnet/api/banappeal/submit', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ code, message }) });
            const body = await r.json().catch(() => ({}));
            if (!r.ok)
                throw new Error(body.error || 'Could not submit your appeal.');
            statusEl.textContent = 'Appeal submitted - a moderator will review it.';
            form.querySelector('textarea').disabled = true;
            button.remove();
        }
        catch (error) {
            statusEl.textContent = error.message;
            button.disabled = false;
        }
    });
}
async function route() { try {
    if (location.pathname === '/recnet/banappeal') {
        select('');
        await banAppeal();
        app.focus({ preventScroll: true });
        return;
    }
    const hash = location.hash.slice(1);
    if (hash.startsWith('user/')) {
        select('');
        await profile(hash.split('/')[1]);
    }
    else if (hash.startsWith('room/')) {
        select('rooms');
        await roomDetail(hash.split('/')[1]);
    }
    else if (hash === 'rooms') {
        select('rooms');
        await rooms();
    }
    else if (hash === 'shop') {
        select('shop');
        await shop();
    }
    else if (hash === 'settings') {
        select('settings');
        await settings();
    }
    else if (hash === 'admin' || hash === 'moderator') {

        location.replace('/recnet/mocha');
        return;
    }
    else if (hash.startsWith('ageverification')) {
        select('');
        await ageVerificationPage(new URLSearchParams(hash.split('?')[1] || '').get('code') || '');
    }
    else if (hash === 'users') {
        select('users');
        await users();
    }
    else {
        select('home');
        await (currentUser ? home() : landing());
    }
    app.focus({ preventScroll: true });
}
catch (e) {
    app.innerHTML = `<section class="route-error"><i class="fa-solid fa-cloud-arrow-down"></i><h2>This page didn&rsquo;t load</h2><p>${esc(e.message)}</p><button id="routeRetry" type="button">Try again</button></section>`;
    document.querySelector('#routeRetry').onclick = route;
} }
document.querySelectorAll('.nav-link[data-view]').forEach(b => b.onclick = () => location.hash = b.dataset.view);
document.addEventListener('click', e => { const button = e.target.closest('.photo-open'); if (button)
    openPhoto(button.dataset.photoPath); });
document.addEventListener('error', e => { const image = e.target; if (!(image instanceof HTMLImageElement) || image.dataset.fallbackApplied)
    return; image.dataset.fallbackApplied = 'true'; if (image.classList.contains('avatar') || image.closest('.mini-avatar'))
    image.src = '/imageserver/DefaultPFP.png';
else
    image.classList.add('image-broken'); }, true);
document.querySelector('#photoClose').addEventListener('click', () => photoDialog.close());
photoDialog.addEventListener('click', e => { if (e.target === photoDialog)
    photoDialog.close(); });
const globalSearch = document.querySelector('#globalSearch'), globalSearchResults = document.querySelector('#globalSearchResults');
let globalSearchTimer, globalSearchItems = [], globalSearchIndex = -1;
function closeGlobalSearch() { globalSearchResults.hidden = true; globalSearchIndex = -1; }
function updateGlobalSearchSelection() { globalSearchResults.querySelectorAll('.global-result').forEach((item, index) => item.classList.toggle('active', index === globalSearchIndex)); }
async function runGlobalSearch() {
    const term = globalSearch.value.trim();
    if (term.length < 2) {
        closeGlobalSearch();
        return;
    }
    globalSearchResults.hidden = false;
    globalSearchResults.innerHTML = '<div class="global-search-empty"><i class="fa-solid fa-spinner fa-spin"></i> Searching Mocha…</div>';
    try {
        const [players, roomsFound] = await Promise.all([get('/recnet/api/users?search=' + encodeURIComponent(term)), get('/recnet/api/rooms?search=' + encodeURIComponent(term))]);
        globalSearchItems = [
            ...players.slice(0, 4).map(player => ({ type: 'player', href: `#user/${player.accountId}`, image: player.profileImage, title: player.displayName || player.username || 'Player', meta: `@${player.username || 'unknown'} · Level ${player.level}` })),
            ...roomsFound.slice(0, 4).map(room => ({ type: 'room', href: `#room/${room.roomId}`, image: room.image, title: `^${room.name || 'UntitledRoom'}`, meta: `by ${room.creatorName || 'Unknown'}` }))
        ];
        if (globalSearch.value.trim() !== term)
            return;
        globalSearchResults.innerHTML = globalSearchItems.length ? `${players.length ? '<div class="search-result-label">Players and rooms</div>' : ''}${globalSearchItems.map(item => `<a class="global-result ${item.type}" href="${item.href}">${item.image ? `<img src="${esc(item.image)}" alt="">` : `<span class="global-result-icon"><i class="fa-solid ${item.type === 'room' ? 'fa-door-open' : 'fa-user'}"></i></span>`}<span><strong>${esc(item.title)}</strong><small>${esc(item.meta)}</small></span><span>${item.type}</span></a>`).join('')}` : '<div class="global-search-empty">No players or rooms match that search.</div>';
        globalSearchIndex = -1;
    }
    catch (error) {
        globalSearchResults.innerHTML = `<div class="global-search-empty">${esc(error.message)}</div>`;
    }
}
globalSearch.addEventListener('input', () => { clearTimeout(globalSearchTimer); globalSearchTimer = setTimeout(runGlobalSearch, 180); });
globalSearch.addEventListener('focus', () => { if (globalSearch.value.trim().length >= 2)
    runGlobalSearch(); });
globalSearch.addEventListener('keydown', e => { if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
    e.preventDefault();
    if (globalSearchResults.hidden)
        return runGlobalSearch();
    const count = globalSearchItems.length;
    if (!count)
        return;
    globalSearchIndex = e.key === 'ArrowDown' ? (globalSearchIndex + 1) % count : (globalSearchIndex - 1 + count) % count;
    updateGlobalSearchSelection();
}
else if (e.key === 'Enter') {
    e.preventDefault();
    if (globalSearchIndex >= 0 && globalSearchItems[globalSearchIndex])
        location.hash = globalSearchItems[globalSearchIndex].href;
    else {
        location.hash = 'users';
        setTimeout(() => { const input = document.querySelector('.search'); if (input) {
            input.value = globalSearch.value;
            input.dispatchEvent(new Event('input'));
        } }, 50);
    }
    closeGlobalSearch();
}
else if (e.key === 'Escape')
    closeGlobalSearch(); });
globalSearchResults.addEventListener('click', closeGlobalSearch);
document.addEventListener('click', e => { if (!e.target.closest('.search-shell'))
    closeGlobalSearch(); });
const settingsMenuButton = document.querySelector('#settingsMenuButton'), accountMenu = document.querySelector('#accountMenu');
settingsMenuButton.addEventListener('click', () => { if (!currentUser) {
    loginDialog.showModal();
    return;
} accountMenu.hidden = !accountMenu.hidden; });
accountMenu.addEventListener('click', e => { if (e.target.closest('.account-menu-item'))
    accountMenu.hidden = true; });
document.addEventListener('click', e => { if (!e.target.closest('.account-menu-wrap'))
    accountMenu.hidden = true; });
addEventListener('hashchange', route);
const loginDialog = document.querySelector('#loginDialog'), loginButton = document.querySelector('#loginButton'), loginForm = document.querySelector('#loginForm'), loginError = document.querySelector('#loginError'), miniAvatar = document.querySelector('#miniAvatar'), registerDialog = document.querySelector('#registerDialog'), registerForm = document.querySelector('#registerForm'), registerError = document.querySelector('#registerError');
function showSession(user) { currentUser = user; document.querySelector('#adminNav').hidden = !user?.isAdmin; document.querySelector('#moderatorNav').hidden = !user?.isModerator || !!user?.isDeveloper; document.querySelector('#developerNav').hidden = !user?.isDeveloper; if (user) {
    loginButton.textContent = 'Log out';
    miniAvatar.classList.add('has-user');
    miniAvatar.style.backgroundImage = `url('${user.profileImage}')`;
    miniAvatar.title = user.displayName || user.username;
}
else {
    loginButton.textContent = 'Log in';
    miniAvatar.classList.remove('has-user');
    miniAvatar.style.backgroundImage = '';
    miniAvatar.title = '';
} }
async function checkSession() { const r = await recnetFetch('/recnet/api/auth/me'); showSession(r.ok ? await r.json() : null); await route(); }
const serverStatus = document.querySelector('#serverStatus'), serverStatusLabel = document.querySelector('#serverStatusLabel');
async function refreshServerStatus() { try {
    const status = await get('/recnet/api/status');
    serverStatus.className = 'server-status';
    serverStatusLabel.textContent = `${Number(status.onlinePlayers).toLocaleString()} online`;
    serverStatus.title = `Mocha is online · ${Number(status.connectedSockets).toLocaleString()} live connections`;
}
catch {
    serverStatus.className = 'server-status offline';
    serverStatusLabel.textContent = 'Offline';
    serverStatus.title = 'The server status could not be reached';
} }
serverStatus.addEventListener('click', () => location.hash = 'home');
miniAvatar.addEventListener('click', () => { if (currentUser)
    location.hash = `user/${currentUser.accountId}`;
else
    loginDialog.showModal(); });
loginButton.addEventListener('click', async () => { if (currentUser) {
    await recnetFetch('/recnet/api/auth/logout', { method: 'POST' });
    showSession(null);
}
else {
    loginError.textContent = '';
    loginDialog.showModal();
} });
document.querySelector('.dialog-close').addEventListener('click', () => loginDialog.close());
loginDialog.addEventListener('click', e => { if (e.target === loginDialog)
    loginDialog.close(); });
document.querySelector('#openRegister').addEventListener('click', () => { loginDialog.close(); registerError.textContent = ''; registerDialog.showModal(); });
document.querySelector('#backToLogin').addEventListener('click', () => { registerDialog.close(); loginDialog.showModal(); });
document.querySelector('.register-close').addEventListener('click', () => registerDialog.close());
registerDialog.addEventListener('click', e => { if (e.target === registerDialog)
    registerDialog.close(); });
loginForm.addEventListener('submit', async (e) => { e.preventDefault(); const button = loginForm.querySelector('button[type=submit]'); button.disabled = true; button.textContent = 'Logging in...'; loginError.textContent = ''; const values = Object.fromEntries(new FormData(loginForm)); const r = await recnetFetch('/recnet/api/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(values) }); if (r.ok) {
    showSession(await r.json());
    loginDialog.close();
    loginForm.reset();
    await route();
}
else {
    const body = await r.json().catch(() => ({}));
    loginError.textContent = body.error || 'Login failed.';
} button.disabled = false; button.textContent = 'Log in'; });
registerForm.addEventListener('submit', async (e) => { e.preventDefault(); const button = registerForm.querySelector('button[type=submit]'), values = Object.fromEntries(new FormData(registerForm)); button.disabled = true; button.textContent = 'Creating account...'; registerError.textContent = ''; const response = await recnetFetch('/recnet/api/auth/register', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(values) }); if (response.ok) {
    showSession(await response.json());
    registerDialog.close();
    registerForm.reset();
    location.hash = 'settings';
}
else {
    const body = await response.json().catch(() => ({}));
    registerError.textContent = body.error || 'Account could not be created.';
} button.disabled = false; button.textContent = 'Create account'; });
refreshServerStatus();
setInterval(refreshServerStatus, 30000);
checkSession();