const API = '/recnet/api';
const app = document.querySelector('#app');
const sidenav = document.querySelector('#sidenav');
const navBackdrop = document.querySelector('#navBackdrop');
const navToggle = document.querySelector('#navToggle');
const topbarUser = document.querySelector('#topbarUser');
const envBadge = document.querySelector('#envBadge');
let currentUser = null;

const esc = s => String(s ?? '').replace(/[&<>'"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' }[c]));

function fetchJson(url, init = {}) {
    return fetch(url, { ...init, credentials: 'same-origin', headers: { 'Content-Type': 'application/json', ...(init.headers || {}) } });
}
async function get(url) {
    const r = await fetchJson(url);
    if (!r.ok) { const body = await r.json().catch(() => ({})); throw new Error(body.error || `Request failed (${r.status})`); }
    return r.json();
}
async function send(url, method, body) {
    const r = await fetchJson(url, { method, body: body !== undefined ? JSON.stringify(body) : undefined });
    const data = await r.json().catch(() => ({}));
    if (!r.ok) throw new Error(data.error || `Request failed (${r.status})`);
    return data;
}

function openNav() { sidenav.classList.add('open'); navBackdrop.classList.add('open'); }
function closeNav() { sidenav.classList.remove('open'); navBackdrop.classList.remove('open'); }
navToggle.addEventListener('click', () => sidenav.classList.contains('open') ? closeNav() : openNav());
navBackdrop.addEventListener('click', closeNav);

function applyRoleGating() {
    const isDev = currentUser?.isDeveloper === true;

    document.querySelectorAll('[data-requires="developer"]').forEach(el => {
        el.style.display = isDev ? '' : 'none';
    });
    document.querySelectorAll('.dev-only').forEach(el => {
        el.style.display = isDev ? 'flex' : 'none';
    });
    document.querySelectorAll('.dev-block').forEach(el => {
        el.style.display = isDev ? 'block' : 'none';
    });
    topbarUser.textContent = currentUser ? `${currentUser.displayName || currentUser.username} · ${currentUser.isDeveloper ? 'Developer' : currentUser.isModerator ? 'Moderator' : 'Staff'}` : '';
    envBadge.textContent = currentUser?.isDeveloper ? 'DEV ACCESS' : 'PROD';
    envBadge.className = 'env-badge' + (currentUser?.isDeveloper ? ' dev' : '');
}

function renderLoginGate() {
    app.innerHTML = `<div class="login-gate"><i class="fa-solid fa-lock" style="font-size:26px;color:var(--text-dim)"></i><h1 class="page-title">Staff sign-in required</h1><p class="field-hint">Log in with a Moderator or Developer account through the main site, then come back here.</p><a class="btn" href="/recnet/">Go to main site</a></div>`;
}

function setActiveNav(view) {
    document.querySelectorAll('.nav-link').forEach(link => link.classList.toggle('active', link.dataset.view === view));
}

const DEVELOPER_ONLY_VIEWS = new Set([
    'community-board', 'coach-gifting', 'announcements', 'player-events',
    'room-importer', 'logs', 'scanner-logs', 'anticheat-logs', 'config', 'account-creation',
    'avatar-items', 'ip-bans', 'steam-blacklist', 'shop'
]);

function renderDeveloperOnlyPlaceholder(view) {
    const title = document.querySelector(`.nav-link[data-view="${view}"]`)?.textContent || 'This section';
    app.innerHTML = `<div class="placeholder-card"><i class="fa-solid fa-lock"></i><h1 class="page-title">${esc(title)} needs Developer access</h1><p>This section's actions all require the Developer role on the server, not just Moderator.</p></div>`;
}

async function route() {
    closeNav();
    const hash = location.hash.slice(1);
    const [view, ...rest] = hash.split('/');
    setActiveNav(view || 'players');
    if (DEVELOPER_ONLY_VIEWS.has(view) && currentUser?.isDeveloper !== true) {
        renderDeveloperOnlyPlaceholder(view);
        return;
    }
    try {
        if (view === 'players' && rest[0]) await playerDetail(rest[0]);
        else if (view === 'players' || view === '') await playersList();
        else if (view === 'player-reports') await playerReportsView();
        else if (view === 'bug-reports') await bugReportsView();
        else if (view === 'rooms' && rest[0]) await roomDetail(rest[0]);
        else if (view === 'rooms') await roomsList();
        else if (view === 'instances') await liveInstances();
        else if (view === 'age-verification') await ageVerificationView();
        else if (view === 'announcements') await announcementsView();
        else if (view === 'player-events') await playerEventsView();
        else if (view === 'config') await configView();
        else if (view === 'account-creation') await accountCreationView();
        else if (view === 'avatar-items') await avatarItemsView();
        else if (view === 'room-importer') await roomImporterView();
        else if (view === 'logs') await logsView();
        else if (view === 'scanner-logs') await scannerLogsView();
        else if (view === 'anticheat-logs') await anticheatLogsView();
        else if (view === 'ip-bans') await ipBansView();
        else if (view === 'community-board') await communityBoardView();
        else if (view === 'clubs' && rest[0]) await clubDetail(rest[0]);
        else if (view === 'clubs') await clubsView();
        else if (view === 'coach-gifting') await coachGiftingView();
        else if (view === 'steam-blacklist') await steamBlacklistView();
        else if (view === 'shop') await shopView();
        else if (view === 'preferences') await preferencesView();
        else await placeholder(view);
    } catch (error) {
        app.innerHTML = `<div class="card"><p class="form-status error">${esc(error.message)}</p></div>`;
    }
}
addEventListener('hashchange', route);

function placeholder(view) {
    const title = document.querySelector(`.nav-link[data-view="${view}"]`)?.textContent.replace(' · soon', '') || 'This section';
    app.innerHTML = `<div class="placeholder-card"><i class="fa-solid fa-hammer"></i><h1 class="page-title">${esc(title)} isn't built here yet</h1><p>This part of the new panel doesn't exist yet - it still works fine in the original admin panel for now.</p></div>`;
}

async function playersList() {
    app.innerHTML = `<h1 class="page-title">Players</h1><div id="playerOverview" class="stat-grid"><div class="loading"><i></i>Loading overview</div></div><div class="search-row"><input id="playerSearch" placeholder="Search by username, display name, or ID"><button class="btn" id="playerSearchGo">Search</button></div><div id="playerListResults" class="player-list"></div>`;
    (async () => {
        const overview = document.querySelector('#playerOverview');
        try {
            const stats = await get(`${API}/admin/overview`);
            const entries = [
                ['Accounts', stats.accounts], ['Admins', stats.admins], ['Mod Locks', stats.moderationLocks],
                ['Verified', stats.verified], ['RR+', stats.rrPlus], ['Rooms', stats.rooms],
                ['Photos', stats.photos], ['Cheers', stats.cheers], ['Comments', stats.comments]
            ];
            overview.innerHTML = entries.map(([label, value]) => `<div class="stat-pill"><strong>${value}</strong><span>${esc(label)}</span></div>`).join('');
        } catch (error) {
            overview.innerHTML = `<p class="form-status error">${esc(error.message)}</p>`;
        }
    })();
    const input = document.querySelector('#playerSearch'), results = document.querySelector('#playerListResults');
    async function runSearch() {
        results.innerHTML = '<div class="loading"><i></i>Loading players</div>';
        try {
            const players = await get(`${API}/admin/accounts?search=${encodeURIComponent(input.value.trim())}`);
            if (!players.length) { results.innerHTML = '<p class="field-hint">No players found.</p>'; return; }
            results.innerHTML = players.map(p => `<a class="player-row" href="#players/${p.accountId}"><img src="${esc(p.profileImage || '')}" class="avatar-img" alt=""><div class="meta"><div class="name">${esc(p.displayName || p.username)}</div><div class="sub">@${esc(p.username)} · ID ${p.accountId} · Level ${p.level}${p.ban ? ' · <span class="badge banned">Banned</span>' : ''}</div></div></a>`).join('');
        } catch (error) {
            results.innerHTML = `<p class="form-status error">${esc(error.message)}</p>`;
        }
    }
    const playerSearchGo = document.querySelector('#playerSearchGo');
    if (playerSearchGo) playerSearchGo.addEventListener('click', runSearch);
    if (input) input.addEventListener('keydown', e => { if (e.key === 'Enter') runSearch(); });
    await runSearch();
}

const ROLE_BADGE_CLASS = { Developer: 'dev', Moderator: 'mod' };

async function playerDetail(accountIdRaw) {
    const accountId = Number(accountIdRaw);
    app.innerHTML = '<div class="loading"><i></i>Loading player</div>';
    let player;
    try { player = await get(`${API}/admin/accounts/${accountId}`); }
    catch (error) { app.innerHTML = `<a class="back-link" href="#players">&larr; Back to Players</a><div class="card"><p class="form-status error">${esc(error.message)}</p></div>`; return; }

    const joined = player.createdAt ? new Date(player.createdAt).toLocaleDateString() : 'Unknown';
    const badges = [...player.roles.map(r => `<span class="badge ${ROLE_BADGE_CLASS[r] || ''}">${esc(r)}</span>`)];
    if (player.isJunior) badges.push('<span class="badge">Junior</span>');
    if (player.ban) badges.push('<span class="badge banned">Banned</span>');

    app.innerHTML = `
    <a class="back-link" href="#players">&larr; Back to Players</a>
    <div class="profile-header">
      <img src="${esc(player.profileImage || '')}" class="avatar-img" alt="">
      <div>
        <div class="name">@${esc(player.username)}</div>
        <div class="username-sub">${esc(player.displayName || '')}</div>
        <div class="stat-line">Level: ${player.level}</div>
        <div class="stat-line">Bio: ${esc(player.bio || '(none)')}</div>
        <div class="stat-line">ID: ${player.accountId} &middot; Joined ${joined}</div>
        <div class="profile-actions">
          <button class="btn" data-act="message">Send Coach Message</button>
          <button class="btn" data-act="force-join">Force Join</button>
          <button class="btn ghost" data-act="edit-profile">Edit Profile</button>
          <button class="btn ghost" data-act="reset-username">Reset Username</button>
          <button class="btn ghost" data-act="reset-pfp">Reset PFP</button>
          <button class="btn ghost" data-act="reset-banner">Reset Banner</button>
          <button class="btn ghost" data-act="reset-password">Reset Password</button>
          <button class="btn ghost" data-act="copy-id">Copy ID</button>
          <button class="btn danger" data-act="ban">${player.ban ? 'Unban' : 'Ban'}</button>
          <button class="btn danger" data-act="mod-lock">${player.moderationLock ? 'Remove Moderation Lock' : 'Apply Moderation Lock'}</button>
          <button class="btn danger" data-act="delete-account">Delete Account</button>
        </div>
        <div class="profile-actions" style="margin-top:10px;padding-top:10px;border-top:1px solid var(--border)">
          <select id="roleSelect" style="width:auto">
            <option value="Moderator">Moderator</option>
            <option value="Developer">Developer</option>
            <option value="RRPlus">RRPlus</option>
            <option value="Verified">Verified</option>
          </select>
          <button class="btn ghost" data-act="add-role">Add Role</button>
          <button class="btn ghost" data-act="clear-roles">Clear Roles</button>
          <button class="btn ghost" data-act="force-into-arbitrary">Force Into Another Player's Instance</button>
        </div>
        <div class="profile-badges">${badges.join('') || '<span class="field-hint">No roles</span>'}${player.moderationLock ? ' <span class="badge banned">Mod Locked</span>' : ''}</div>
      </div>
    </div>
    <div class="tabs">
      <div class="tab active" data-tab="settings">Settings</div>
      <div class="tab" data-tab="relationships">Relationships</div>
      <div class="tab" data-tab="threads">Threads</div>
      <div class="tab" data-tab="inventory">Inventory</div>
      <div class="tab" data-tab="photos-mine">My Photos</div>
      <div class="tab" data-tab="photos-of-me">Photos of Me</div>
      <div class="tab" data-tab="gifts">Gifts</div>
      <div class="tab" data-tab="reputation">Reputation</div>
      <div class="tab" data-tab="progression">Progression</div>
      <div class="tab dev-only" data-tab="troll">Troll</div>
    </div>
    <div id="tabBody"></div>`;

    document.querySelectorAll('.tab').forEach(tab => tab.addEventListener('click', () => {
        document.querySelectorAll('.tab').forEach(t => t.classList.toggle('active', t === tab));
        renderPlayerTab(tab.dataset.tab, accountId);
    }));
    renderPlayerTab('settings', accountId);

    applyRoleGating();

    const msgBtn = document.querySelector('[data-act="message"]');
    if (msgBtn) msgBtn.addEventListener('click', async () => {
        const message = prompt('Message to send as Coach:');
        if (!message) return;
        try { await send(`${API}/admin/accounts/${accountId}/message`, 'POST', { message }); alert('Sent.'); }
        catch (error) { alert(error.message); }
    });

    const forceJoinBtn = document.querySelector('[data-act="force-join"]');
    if (forceJoinBtn) forceJoinBtn.addEventListener('click', async () => {
        try { const r = await send(`${API}/admin/accounts/${accountId}/force-join-instance`, 'POST', {}); alert(r.delivered ? `Pushed live into "${r.roomName}".` : `Their record now points at "${r.roomName}" - it'll surface next time they connect.`); }
        catch (error) { alert(error.message); }
    });

    const resetUserBtn = document.querySelector('[data-act="reset-username"]');
    if (resetUserBtn) resetUserBtn.addEventListener('click', async () => {
        if (!confirm('Reset this account\'s username to a randomly generated one?')) return;
        try {
            const r = await send(`${API}/admin/accounts/${accountId}/username/reset`, 'POST', {});
            alert(`Username reset from "${r.previousUsername}" to "${r.username}".`);
            await playerDetail(accountIdRaw);
        } catch (error) { alert(error.message); }
    });

    const editProfileBtn = document.querySelector('[data-act="edit-profile"]');
    if (editProfileBtn) editProfileBtn.addEventListener('click', async () => {
        const newUsername = prompt('New username (3-20 letters, numbers, underscores):', player.username);
        if (!newUsername) return;
        const newDisplayName = prompt('New display name (1-32 characters):', player.displayName || '');
        if (newDisplayName === null) return;
        const bio = prompt('Bio (up to 500 characters):', player.bio || '');
        if (bio === null) return;
        const email = prompt('Email:', player.email || '');
        if (email === null) return;
        try {
            await send(`${API}/admin/accounts/${accountId}/profile`, 'PUT', { username: newUsername, displayName: newDisplayName, bio, email });
            alert('Profile updated successfully.');
            await playerDetail(accountIdRaw);
        } catch (error) { alert(error.message); }
    });

    const resetPfpBtn = document.querySelector('[data-act="reset-pfp"]');
    if (resetPfpBtn) resetPfpBtn.addEventListener('click', async () => {
        if (!confirm('Reset this account\'s profile picture to the default?')) return;
        try { await send(`${API}/admin/accounts/${accountId}/images/pfp/reset`, 'POST', {}); await playerDetail(accountIdRaw); }
        catch (error) { alert(error.message); }
    });

    const resetBannerBtn = document.querySelector('[data-act="reset-banner"]');
    if (resetBannerBtn) resetBannerBtn.addEventListener('click', async () => {
        if (!confirm('Reset this account\'s banner image?')) return;
        try { await send(`${API}/admin/accounts/${accountId}/images/banner/reset`, 'POST', {}); await playerDetail(accountIdRaw); }
        catch (error) { alert(error.message); }
    });

    const resetPasswordBtn = document.querySelector('[data-act="reset-password"]');
    if (resetPasswordBtn) resetPasswordBtn.addEventListener('click', async () => {
        const newPassword = prompt('New password (minimum 8 characters):');
        if (!newPassword) return;
        try { await send(`${API}/admin/accounts/${accountId}/password`, 'PUT', { newPassword }); alert('Password reset.'); }
        catch (error) { alert(error.message); }
    });

    const copyIdBtn = document.querySelector('[data-act="copy-id"]');
    if (copyIdBtn) copyIdBtn.addEventListener('click', async () => {
        try { await navigator.clipboard.writeText(String(accountId)); copyIdBtn.textContent = 'Copied!'; setTimeout(() => copyIdBtn.textContent = 'Copy ID', 1200); }
        catch (error) { alert('Could not copy to clipboard.'); }
    });

    const modLockBtn = document.querySelector('[data-act="mod-lock"]');
    if (modLockBtn) modLockBtn.addEventListener('click', async () => {
        if (player.moderationLock) {
            const removeLinked = player.moderationLock.isRelated ? confirm('Also remove the Moderation Lock from linked accounts?') : false;
            if (!confirm('Remove this account\'s Moderation Lock?')) return;
            try { await send(`${API}/admin/accounts/${accountId}/moderation-lock`, 'DELETE', { removeLinkedAccounts: removeLinked }); await playerDetail(accountIdRaw); }
            catch (error) { alert(error.message); }
            return;
        }
        if (!confirm('Apply a permanent Moderation Lock to this account?')) return;
        try { await send(`${API}/admin/accounts/${accountId}/moderation-lock`, 'POST', {}); await playerDetail(accountIdRaw); }
        catch (error) { alert(error.message); }
    });

    const deleteAccountBtn = document.querySelector('[data-act="delete-account"]');
    if (deleteAccountBtn) deleteAccountBtn.addEventListener('click', async () => {
        const confirmation = prompt(`This permanently deletes the account. Type DELETE ${accountId} to confirm:`);
        if (!confirmation) return;
        try {
            await send(`${API}/admin/accounts/${accountId}`, 'DELETE', { confirmation });
            alert('Account deleted.');
            location.hash = '#players';
        } catch (error) { alert(error.message); }
    });

    const addRoleBtn = document.querySelector('[data-act="add-role"]');
    if (addRoleBtn) addRoleBtn.addEventListener('click', async () => {
        const roleSelect = document.querySelector('#roleSelect');
        const role = roleSelect ? roleSelect.value : prompt('Role to add (e.g. Moderator, Developer, RRPlus):');
        if (!role) return;
        try { await send(`${API}/admin/accounts/${accountId}/roles`, 'POST', { role, enabled: true }); await playerDetail(accountIdRaw); }
        catch (error) { alert(error.message); }
    });

    const clearRolesBtn = document.querySelector('[data-act="clear-roles"]');
    if (clearRolesBtn) clearRolesBtn.addEventListener('click', async () => {
        if (!confirm('Clear all roles from this account?')) return;
        try { await send(`${API}/admin/accounts/${accountId}/roles`, 'DELETE'); await playerDetail(accountIdRaw); }
        catch (error) { alert(error.message); }
    });

    const forceIntoArbitraryBtn = document.querySelector('[data-act="force-into-arbitrary"]');
    if (forceIntoArbitraryBtn) forceIntoArbitraryBtn.addEventListener('click', async () => {
        const targetAccountId = prompt('Account ID of the player whose live instance to force this player into:');
        if (!targetAccountId) return;
        try {
            const r = await send(`${API}/admin/accounts/${accountId}/force-into/${Number(targetAccountId)}`, 'POST', {});
            alert(r.delivered ? `Forced into "${r.roomName}" live.` : `Record updated to "${r.roomName}" - will apply on next connection.`);
        } catch (error) { alert(error.message); }
    });

    const banBtn = document.querySelector('[data-act="ban"]');
    if (banBtn) banBtn.addEventListener('click', async () => {
        if (player.ban) {
            if (!confirm('Unban this account?')) return;
            try { await send(`${API}/admin/accounts/${accountId}/ban`, 'DELETE'); await playerDetail(accountIdRaw); }
            catch (error) { alert(error.message); }
            return;
        }
        const reason = prompt('Ban reason:');
        if (!reason) return;
        const durationAmount = Number(prompt('Duration amount (ignored if permanent):', '1') || '1');
        const durationUnit = prompt('Duration unit (seconds/minutes/hours/days/weeks/permanent):', 'days') || 'days';
        try {
            await send(`${API}/admin/accounts/${accountId}/ban`, 'POST', { reason, linkBan: false, durationAmount, durationUnit });
            await playerDetail(accountIdRaw);
        } catch (error) { alert(error.message); }
    });

}

async function renderPlayerTab(tab, accountId) {
    const body = document.querySelector('#tabBody');
    if (tab === 'settings') {
        body.innerHTML = `<h2 class="page-title" style="font-size:16px">Settings</h2>
        <div class="card">
          <h2>Token Balance</h2>
          <p class="field-hint" id="balanceCurrent">Loading...</p>
          <div class="field-row">
            <label class="field">Amount<input type="number" id="balanceAmount" min="0" value="0"></label>
            <label class="field">Mode<select id="balanceMode"><option value="set">Set to</option><option value="add">Add</option></select></label>
          </div>
          <button class="btn" id="applyBalance">Apply</button>
          <span class="form-status" id="balanceStatus"></span>
        </div>
        <div class="card">
          <h2>Linked Platforms</h2>
          <div id="platformList"><div class="loading"><i></i>Loading platforms</div></div>
          <div class="field-row" style="margin-top:10px">
            <label class="field">Platform<select id="newPlatform"><option>Steam</option><option>Oculus</option><option>PlayStation</option><option>Xbox</option><option>IOS</option><option>GooglePlay</option></select></label>
            <label class="field">Platform ID<input id="newPlatformId" inputmode="numeric" placeholder="Numeric ID"></label>
          </div>
          <button class="btn" id="addPlatform">Link Platform</button>
          <span class="form-status" id="platformStatus"></span>
        </div>
        <h2 class="page-title" style="font-size:16px">Custom Settings</h2>
        <div class="settings-add-row"><input id="newSettingKey" placeholder="New key"><input id="newSettingValue" placeholder="Value"><button class="btn" id="addSetting">Add / set</button></div><div id="settingsTable"><div class="loading"><i></i>Loading settings</div></div>`;

        (async () => {
            const balanceCurrent = document.querySelector('#balanceCurrent');
            try {
                const account = await get(`${API}/admin/accounts/${accountId}`);
                balanceCurrent.textContent = `Current balance: ${account.balance} tokens.`;
                renderPlatforms(account.platforms);
            } catch (error) {
                balanceCurrent.textContent = error.message;
                balanceCurrent.classList.add('error');
            }
        })();

        function renderPlatforms(platforms) {
            const list = document.querySelector('#platformList');
            if (!platforms || !platforms.length) { list.innerHTML = '<p class="field-hint">No linked platforms.</p>'; return; }
            list.innerHTML = platforms.map(p => `<div class="room-row"><div class="meta"><strong>${esc(p.platform)}</strong><br><small>${esc(p.platformId)}</small></div><button class="btn small danger unlink-platform" data-platform="${esc(p.platform)}" data-platform-id="${esc(p.platformId)}">Unlink</button></div>`).join('');
            list.querySelectorAll('.unlink-platform').forEach(btn => btn.addEventListener('click', async () => {
                if (!confirm(`Unlink ${btn.dataset.platform} identity ${btn.dataset.platformId}?`)) return;
                try {
                    const r = await send(`${API}/admin/accounts/${accountId}/platforms`, 'POST', { platform: btn.dataset.platform, platformId: btn.dataset.platformId, enabled: false });
                    renderPlatforms(r.platforms);
                } catch (error) { alert(error.message); }
            }));
        }

        const applyBalanceBtn = document.querySelector('#applyBalance');
        if (applyBalanceBtn) applyBalanceBtn.addEventListener('click', async () => {
            const status = document.querySelector('#balanceStatus');
            const amount = Number(document.querySelector('#balanceAmount').value);
            const add = document.querySelector('#balanceMode').value === 'add';
            if (amount < 0) { status.textContent = 'Amount cannot be negative.'; status.classList.add('error'); return; }
            try {
                const r = await send(`${API}/admin/accounts/${accountId}/balance`, 'POST', { amount, add });
                status.textContent = `Balance is now ${r.balance} tokens.`;
                status.classList.remove('error');
                document.querySelector('#balanceCurrent').textContent = `Current balance: ${r.balance} tokens.`;
            } catch (error) { status.textContent = error.message; status.classList.add('error'); }
        });

        const addPlatformBtn = document.querySelector('#addPlatform');
        if (addPlatformBtn) addPlatformBtn.addEventListener('click', async () => {
            const status = document.querySelector('#platformStatus');
            const platform = document.querySelector('#newPlatform').value;
            const platformId = document.querySelector('#newPlatformId').value.trim();
            if (!platformId) { status.textContent = 'Enter a platform ID.'; status.classList.add('error'); return; }
            try {
                const r = await send(`${API}/admin/accounts/${accountId}/platforms`, 'POST', { platform, platformId, enabled: true });
                status.textContent = 'Linked.';
                status.classList.remove('error');
                renderPlatforms(r.platforms);
            } catch (error) { status.textContent = error.message; status.classList.add('error'); }
        });

        async function loadSettings() {
            const table = document.querySelector('#settingsTable');
            try {
                const settings = await get(`${API}/admin/accounts/${accountId}/settings`);
                if (!settings.length) { table.innerHTML = '<p class="field-hint">No settings stored.</p>'; return; }
                table.innerHTML = `<table><thead><tr><th>Key</th><th>Value</th><th>Actions</th></tr></thead><tbody>${settings.map(s => `<tr data-key="${esc(s.key)}"><td data-label="Key">${esc(s.key)}</td><td data-label="Value"><input class="setting-value" value="${esc(s.value)}"></td><td data-label="Actions" class="actions-cell"><button class="btn small save-setting">Save</button><button class="btn small danger delete-setting">Delete</button></td></tr>`).join('')}</tbody></table>`;
                table.querySelectorAll('.save-setting').forEach(btn => btn.addEventListener('click', async () => {
                    const row = btn.closest('tr'), key = row.dataset.key;
                    const settingValue = row.querySelector('.setting-value');
                    if (!settingValue) return;
                    const value = settingValue.value;
                    try { await send(`${API}/admin/accounts/${accountId}/settings`, 'PUT', { key, value }); }
                    catch (error) { alert(error.message); }
                }));
                table.querySelectorAll('.delete-setting').forEach(btn => btn.addEventListener('click', async () => {
                    const row = btn.closest('tr'), key = row.dataset.key;
                    if (!confirm(`Delete "${key}"?`)) return;
                    try { await send(`${API}/admin/accounts/${accountId}/settings/${encodeURIComponent(key)}`, 'DELETE'); row.remove(); }
                    catch (error) { alert(error.message); }
                }));
            } catch (error) {
                table.innerHTML = `<p class="form-status error">${esc(error.message)}</p>`;
            }
        }
        const addSettingBtn = document.querySelector('#addSetting');
        if (addSettingBtn) addSettingBtn.addEventListener('click', async () => {
            const newSettingKey = document.querySelector('#newSettingKey');
            const newSettingValue = document.querySelector('#newSettingValue');
            if (!newSettingKey || !newSettingValue) return;
            const key = newSettingKey.value.trim(), value = newSettingValue.value;
            if (!key) return;
            try {
                await send(`${API}/admin/accounts/${accountId}/settings`, 'PUT', { key, value });
                newSettingKey.value = '';
                newSettingValue.value = '';
                await loadSettings();
            } catch (error) { alert(error.message); }
        });
        await loadSettings();
    } else if (tab === 'progression') {
        body.innerHTML = `<h2 class="page-title" style="font-size:16px">Progression &amp; Account State</h2><div class="card"><h2>Update Progression</h2><div class="field-row"><label class="field">Level (1-50)<input type="number" id="progLevel" min="1" max="50"></label><label class="field">XP<input type="number" id="progXP" min="0"></label></div><button class="btn" id="updateProgression">Update</button><span class="form-status" id="progStatus"></span></div>
        <div class="card"><h2>Account state &amp; appearance</h2>
          <div class="field-row">
            <label class="field">Available username changes<input type="number" id="stateUsernameChanges" min="0"></label>
            <label class="field">Personal pronouns (bitmask 0-63)<input type="number" id="statePronouns" min="0" max="63"></label>
          </div>
          <label class="field">Display emoji<input id="stateEmoji" maxlength="16"></label>
          <label class="field">Profile image path<input id="stateProfileImage"></label>
          <label class="field">Banner image path<input id="stateBannerImage"></label>
          <label class="field" style="display:flex;align-items:center;gap:6px;flex-direction:row"><input type="checkbox" id="stateIsJunior" style="width:auto"> Junior account</label>
          <button class="btn" id="updateAccountState">Save account state</button>
          <span class="form-status" id="stateStatus"></span>
        </div>`;
        let account;
        try {
            account = await get(`${API}/admin/accounts/${accountId}`);
            document.querySelector('#progLevel').value = account.level;
            document.querySelector('#progXP').value = account.xp;
            document.querySelector('#stateUsernameChanges').value = account.availableUsernameChanges ?? 0;
            document.querySelector('#statePronouns').value = account.personalPronouns ?? 0;
            document.querySelector('#stateEmoji').value = account.displayEmoji || '';
            document.querySelector('#stateProfileImage').value = account.profileImagePath || '';
            document.querySelector('#stateBannerImage').value = account.bannerImagePath || '';
            document.querySelector('#stateIsJunior').checked = !!account.isJunior;
        } catch (error) {
            body.innerHTML = `<p class="form-status error">${esc(error.message)}</p>`;
            return;
        }

        function detailsPayload(overrides) {
            return {
                level: account.level,
                xp: account.xp,
                isJunior: document.querySelector('#stateIsJunior').checked,
                availableUsernameChanges: parseInt(document.querySelector('#stateUsernameChanges').value) || 0,
                displayEmoji: document.querySelector('#stateEmoji').value,
                personalPronouns: parseInt(document.querySelector('#statePronouns').value) || 0,
                profileImage: document.querySelector('#stateProfileImage').value,
                bannerImage: document.querySelector('#stateBannerImage').value,
                ...overrides
            };
        }

        const updateProgBtn = document.querySelector('#updateProgression');
        if (updateProgBtn) updateProgBtn.addEventListener('click', async () => {
            const status = document.querySelector('#progStatus');
            const level = parseInt(document.querySelector('#progLevel').value);
            const xp = parseInt(document.querySelector('#progXP').value);
            if (level < 1 || level > 50) { status.textContent = 'Level must be 1-50.'; status.classList.add('error'); return; }
            if (xp < 0) { status.textContent = 'XP cannot be negative.'; status.classList.add('error'); return; }
            try {
                await send(`${API}/admin/accounts/${accountId}/details`, 'PUT', detailsPayload({ level, xp }));
                account.level = level; account.xp = xp;
                status.textContent = 'Updated.';
                status.classList.remove('error');
            } catch (error) {
                status.textContent = error.message;
                status.classList.add('error');
            }
        });

        const updateStateBtn = document.querySelector('#updateAccountState');
        if (updateStateBtn) updateStateBtn.addEventListener('click', async () => {
            const status = document.querySelector('#stateStatus');
            try {
                await send(`${API}/admin/accounts/${accountId}/details`, 'PUT', detailsPayload({}));
                status.textContent = 'Saved.';
                status.classList.remove('error');
            } catch (error) {
                status.textContent = error.message;
                status.classList.add('error');
            }
        });
    } else if (tab === 'relationships') {
        body.innerHTML = `<h2 class="page-title" style="font-size:16px">Relationships</h2>
        <div class="card dev-block">
          <h2>Force add friend</h2>
          <div class="field-row"><label class="field">Account ID<input type="number" id="forceAddFriendId" min="1"></label></div>
          <button class="btn" id="forceAddFriend">Add friendship</button>
          <span class="form-status" id="forceAddFriendStatus"></span>
        </div>
        <div id="relationshipsList"><div class="loading"><i></i>Loading relationships</div></div>`;
        applyRoleGating();

        async function loadRelationships() {
            const list = document.querySelector('#relationshipsList');
            list.innerHTML = '<div class="loading"><i></i>Loading relationships</div>';
            try {
                const relationships = await get(`${API}/admin/accounts/${accountId}/relationships`);
                if (!relationships.length) { list.innerHTML = '<p class="field-hint">No relationships found.</p>'; return; }
                const isDev = currentUser?.isDeveloper === true;
                list.innerHTML = `<table><thead><tr><th>Player ID</th><th>Username</th><th>Display Name</th><th>Type</th><th>Flags</th>${isDev ? '<th>Actions</th>' : ''}</tr></thead><tbody>${relationships.map(r => `
                    <tr><td>${r.playerId}</td><td>${esc(r.username || 'N/A')}</td><td>${esc(r.displayName || 'N/A')}</td><td>${esc(r.relationshipType)}</td><td>${r.favorited ? '⭐ ' : ''}${r.muted ? '🔇 ' : ''}${r.ignored ? '🚫' : ''}</td>${isDev ? `<td><button class="btn small danger remove-relationship" data-id="${r.playerId}">Unadd</button></td>` : ''}</tr>
                `).join('')}</tbody></table>`;
                list.querySelectorAll('.remove-relationship').forEach(btn => btn.addEventListener('click', async () => {
                    if (!confirm('Remove this relationship?')) return;
                    try {
                        await send(`${API}/admin/accounts/${accountId}/relationships/friend/${btn.dataset.id}`, 'DELETE');
                        await loadRelationships();
                    } catch (error) { alert(error.message); }
                }));
            } catch (error) {
                list.innerHTML = `<p class="form-status error">${esc(error.message)}</p>`;
            }
        }

        const forceAddFriendBtn = document.querySelector('#forceAddFriend');
        if (forceAddFriendBtn) forceAddFriendBtn.addEventListener('click', async () => {
            const status = document.querySelector('#forceAddFriendStatus');
            const targetAccountId = Number(document.querySelector('#forceAddFriendId').value);
            if (!targetAccountId) return;
            try {
                await send(`${API}/admin/accounts/${accountId}/relationships/friend`, 'POST', { targetAccountId });
                status.textContent = 'Added.';
                status.classList.remove('error');
                await loadRelationships();
            } catch (error) { status.textContent = error.message; status.classList.add('error'); }
        });

        await loadRelationships();
    } else if (tab === 'threads') {
        body.innerHTML = `<h2 class="page-title" style="font-size:16px">Threads</h2><div id="threadsList"><div class="loading"><i></i>Loading threads</div></div><div id="threadMessages"></div>`;
        const list = document.querySelector('#threadsList');
        const messagesBox = document.querySelector('#threadMessages');

        async function openThread(threadId, name) {
            messagesBox.innerHTML = `<div class="card"><h2>${esc(name)}</h2><div class="loading"><i></i>Loading messages</div></div>`;
            try {
                const messages = await get(`${API}/admin/accounts/${accountId}/chat/threads/${threadId}/messages`);
                messagesBox.innerHTML = `<div class="card"><h2>${esc(name)}</h2>${messages.length ? messages.map(m => `<div class="room-row"><div class="meta"><strong>${esc(m.senderDisplayName)}</strong><br>${esc(m.body)}<br><small class="field-hint">${new Date(m.createdAt).toLocaleString()}</small></div></div>`).join('') : '<p class="field-hint">No messages.</p>'}</div>`;
            } catch (error) {
                messagesBox.innerHTML = `<div class="card"><p class="form-status error">${esc(error.message)}</p></div>`;
            }
        }

        try {
            const threads = await get(`${API}/admin/accounts/${accountId}/chat/threads`);
            if (!threads.length) { list.innerHTML = '<p class="field-hint">No message threads.</p>'; return; }
            list.innerHTML = threads.map(t => {
                const name = t.name || t.members.map(m => m.displayName).join(', ') || `Thread ${t.threadId}`;
                return `<div class="room-row clickable" data-id="${t.threadId}" data-name="${esc(name)}"><div class="meta"><strong>${esc(name)}</strong><br><small class="field-hint">${t.lastMessage ? esc(t.lastMessage.body) : 'No messages yet'}</small></div></div>`;
            }).join('');
            list.querySelectorAll('.room-row').forEach(row => row.addEventListener('click', () => openThread(row.dataset.id, row.dataset.name)));
        } catch (error) {
            list.innerHTML = `<p class="form-status error">${esc(error.message)}</p>`;
        }
    } else if (tab === 'inventory') {
        body.innerHTML = `<h2 class="page-title" style="font-size:16px">Inventory</h2><div class="card"><input id="inventorySearch" placeholder="Search avatar items, equipment, consumables"><button class="btn" id="searchInventory">Search</button></div><div id="inventoryResults"><div class="loading"><i></i>Loading inventory</div></div>`;
        const searchInput = document.querySelector('#inventorySearch');
        const results = document.querySelector('#inventoryResults');

        async function loadInventory() {
            const search = searchInput.value.trim();
            results.innerHTML = '<div class="loading"><i></i>Loading inventory</div>';
            try {
                const data = await get(`${API}/admin/accounts/${accountId}/inventory?search=${encodeURIComponent(search)}`);
                const sections = [];

                if (data.avatarItems && data.avatarItems.length) {
                    sections.push(`<h3>Avatar Items</h3><div class="inventory-grid">${data.avatarItems.map(item => `
                        <div class="inventory-item" data-type="avatar" data-id="${item.avatarItemId}" data-desc="${esc(item.avatarItemDesc)}">
                            <strong>${esc(item.friendlyName)}</strong><br>
                            <small>ID: ${item.avatarItemId}</small><br>
                            <label><input type="checkbox" ${item.owned ? 'checked' : ''} class="inventory-check"> Owned</label>
                        </div>
                    `).join('')}</div>`);
                }

                if (data.consumables && data.consumables.length) {
                    sections.push(`<h3>Consumables</h3><div class="inventory-grid">${data.consumables.map(item => `
                        <div class="inventory-item" data-type="consumable" data-id="${item.consumableItemId}" data-desc="${esc(item.consumableItemDesc)}">
                            <strong>${esc(item.friendlyName)}</strong><br>
                            <small>ID: ${item.consumableItemId}</small><br>
                            <label>Quantity: <input type="number" class="inventory-quantity" value="${item.quantity}" min="0" max="100000" style="width:60px"></label>
                        </div>
                    `).join('')}</div>`);
                }

                if (!sections.length) { results.innerHTML = '<p class="field-hint">No items found.</p>'; return; }
                results.innerHTML = sections.join('<br><br>');

                results.querySelectorAll('.inventory-check').forEach(chk => chk.addEventListener('change', async () => {
                    const item = chk.closest('.inventory-item');
                    try {
                        await send(`${API}/admin/accounts/${accountId}/inventory/avatar`, 'POST', {
                            avatarItemId: Number(item.dataset.id),
                            avatarItemDesc: item.dataset.desc,
                            owned: chk.checked
                        });
                    } catch (error) { alert(error.message); chk.checked = !chk.checked; }
                }));

                results.querySelectorAll('.inventory-quantity').forEach(input => input.addEventListener('change', async () => {
                    const item = input.closest('.inventory-item');
                    const qty = Number(input.value);
                    if (qty < 0 || qty > 100000) { alert('Quantity must be 0-100000'); input.value = item.dataset.prevQty || 0; return; }
                    try {
                        await send(`${API}/admin/accounts/${accountId}/inventory/consumable`, 'POST', {
                            consumableItemId: Number(item.dataset.id),
                            consumableItemDesc: item.dataset.desc,
                            quantity: qty
                        });
                        item.dataset.prevQty = qty;
                    } catch (error) { alert(error.message); input.value = item.dataset.prevQty || 0; }
                }));
            } catch (error) {
                results.innerHTML = `<p class="form-status error">${esc(error.message)}</p>`;
            }
        }

    const searchInvBtn = document.querySelector('#searchInventory');
    if (searchInvBtn) searchInvBtn.addEventListener('click', loadInventory);
    if (searchInput) searchInput.addEventListener('keydown', e => { if (e.key === 'Enter') loadInventory(); });
        await loadInventory();
    } else if (tab === 'reputation') {
        body.innerHTML = `<h2 class="page-title" style="font-size:16px">Reputation / Cheer Badges</h2><div id="reputationContent"><div class="loading"><i></i>Loading reputation</div></div>`;
        const content = document.querySelector('#reputationContent');

        async function loadReputation() {
            content.innerHTML = '<div class="loading"><i></i>Loading reputation</div>';
            try {
                const data = await get(`${API}/admin/accounts/${accountId}/reputation`);
                content.innerHTML = `
                <div class="card">
                    <label><input type="checkbox" id="isCheerful" ${data.isCheerful ? 'checked' : ''}> Is Cheerful</label><br><br>
                    <label>Selected Badge<select id="selectedCheer">
                        ${data.availableBadges.map(b => `<option value="${b}" ${data.selectedCheer === b ? 'selected' : ''}>${b}</option>`).join('')}
                    </select></label><br><br>
                    <h3>Cheer Counts</h3>
                    <label>General: <input type="number" id="cheerGeneral" value="${data.cheerGeneral}" min="0"></label><br>
                    <label>Helpful: <input type="number" id="cheerHelpful" value="${data.cheerHelpful}" min="0"></label><br>
                    <label>Sportsmanship: <input type="number" id="cheerSportsman" value="${data.cheerSportsman}" min="0"></label><br>
                    <label>Great Host: <input type="number" id="cheerGreatHost" value="${data.cheerGreatHost}" min="0"></label><br>
                    <label>Creative: <input type="number" id="cheerCreative" value="${data.cheerCreative}" min="0"></label><br><br>
                    <button class="btn" id="saveReputation">Save Reputation</button>
                    <span class="form-status" id="reputationStatus"></span>
                </div>`;

                const saveRepBtn = document.querySelector('#saveReputation');
                if (saveRepBtn) saveRepBtn.addEventListener('click', async () => {
                    try {
                        const status = document.querySelector('#reputationStatus');
                        const isCheerful = document.querySelector('#isCheerful');
                        const selectedCheer = document.querySelector('#selectedCheer');
                        const cheerGeneral = document.querySelector('#cheerGeneral');
                        const cheerHelpful = document.querySelector('#cheerHelpful');
                        const cheerSportsman = document.querySelector('#cheerSportsman');
                        const cheerGreatHost = document.querySelector('#cheerGreatHost');
                        const cheerCreative = document.querySelector('#cheerCreative');

                        if (!status || !isCheerful || !selectedCheer || !cheerGeneral || !cheerHelpful || !cheerSportsman || !cheerGreatHost || !cheerCreative) return;

                        status.textContent = 'Saving...';
                        status.classList.remove('error');
                        await send(`${API}/admin/accounts/${accountId}/reputation`, 'PUT', {
                            isCheerful: isCheerful.checked,
                            selectedCheer: selectedCheer.value,
                            cheerGeneral: Number(cheerGeneral.value),
                            cheerHelpful: Number(cheerHelpful.value),
                            cheerSportsman: Number(cheerSportsman.value),
                            cheerGreatHost: Number(cheerGreatHost.value),
                            cheerCreative: Number(cheerCreative.value)
                        });
                        status.textContent = 'Saved.';
                        status.classList.remove('error');
                    } catch (error) {
                        status.textContent = error.message;
                        status.classList.add('error');
                    }
                });
            } catch (error) {
                content.innerHTML = `<p class="form-status error">${esc(error.message)}</p>`;
            }
        }

        await loadReputation();
    } else if (tab === 'photos-mine' || tab === 'photos-of-me') {
        const type = tab === 'photos-mine' ? 'mine' : 'of-me';
        body.innerHTML = `<h2 class="page-title" style="font-size:16px">${tab === 'photos-mine' ? 'My Photos' : 'Photos of Me'}</h2><div id="photosContent"><div class="loading"><i></i>Loading photos</div></div>`;
        const content = document.querySelector('#photosContent');

        async function loadPhotos() {
            content.innerHTML = '<div class="loading"><i></i>Loading photos</div>';
            try {
                const data = await get(`${API}/admin/accounts/${accountId}/photos?type=${type}`);
                if (!data.photos.length) { content.innerHTML = '<p class="field-hint">No photos found.</p>'; return; }
                content.innerHTML = `<p class="field-hint">${data.photoCount} photo(s)</p><div class="inventory-grid">${data.photos.map(p => `
                    <div class="inventory-item">
                        <img src="${esc(p.url)}" style="width:100%;height:150px;object-fit:cover;border-radius:4px;margin-bottom:5px" alt="">
                        <small>${esc(p.path)}</small><br>
                        <small>${new Date(p.takenAt).toLocaleString()}</small>
                    </div>
                `).join('')}</div>`;
            } catch (error) {
                content.innerHTML = `<p class="form-status error">${esc(error.message)}</p>`;
            }
        }

        await loadPhotos();
    } else if (tab === 'gifts') {
        const isDev = currentUser?.isDeveloper === true;
        body.innerHTML = `<h2 class="page-title" style="font-size:16px">Pending Gifts</h2>${isDev ? '<button class="btn danger small" id="clearAllGifts">Clear All</button><span class="form-status" id="giftsStatus"></span>' : ''}<div id="giftsList" style="margin-top:10px"><div class="loading"><i></i>Loading pending gifts</div></div>`;
        const list = document.querySelector('#giftsList');

        async function loadGifts() {
            list.innerHTML = '<div class="loading"><i></i>Loading pending gifts</div>';
            try {
                const gifts = await get(`${API}/admin/accounts/${accountId}/gifts`);
                list.innerHTML = gifts.length ? gifts.map(g => `<div class="room-row" data-id="${g.giftPackageId}">
                    ${g.thumbnailImage ? `<img src="${esc(g.thumbnailImage)}" alt="">` : ''}
                    <div class="meta">
                        <strong>${esc(g.friendlyName || (g.currency ? `${g.currency} tokens` : g.xp ? `${g.xp} XP` : 'Gift'))}</strong>
                        <br><small>From ${esc(g.fromDisplayName)} &middot; GiftContext ${g.giftContext}${g.consumableQuantity > 1 ? ` &middot; x${g.consumableQuantity}` : ''}</small>
                        ${g.message ? `<br><small class="field-hint">"${esc(g.message)}"</small>` : ''}
                    </div>
                    ${isDev ? `<button class="btn small danger clear-gift">Clear</button>` : ''}
                </div>`).join('') : '<p class="field-hint">No pending gifts.</p>';
                list.querySelectorAll('.clear-gift').forEach(btn => btn.addEventListener('click', async () => {
                    const row = btn.closest('[data-id]');
                    if (!confirm('Clear this pending gift? It cannot be recovered.')) return;
                    try {
                        await send(`${API}/admin/accounts/${accountId}/gifts/${row.dataset.id}`, 'DELETE');
                        await loadGifts();
                    } catch (error) { alert(error.message); }
                }));
            } catch (error) {
                list.innerHTML = `<p class="form-status error">${esc(error.message)}</p>`;
            }
        }

        const clearAllBtn = document.querySelector('#clearAllGifts');
        if (clearAllBtn) clearAllBtn.addEventListener('click', async () => {
            const status = document.querySelector('#giftsStatus');
            if (!confirm('Clear every pending gift for this player? This cannot be recovered.')) return;
            try {
                const r = await send(`${API}/admin/accounts/${accountId}/gifts`, 'DELETE');
                status.textContent = `Cleared ${r.removedBoxes} gift(s).`;
                status.classList.remove('error');
                await loadGifts();
            } catch (error) { status.textContent = error.message; status.classList.add('error'); }
        });

        await loadGifts();
    } else if (tab === 'troll') {
        body.innerHTML = `<h2 class="page-title" style="font-size:16px">Troll</h2><div class="loading"><i></i>Loading</div>`;
        let username = accountId;
        try { username = (await get(`${API}/admin/accounts/${accountId}`)).username || accountId; } catch (error) {  }

        body.innerHTML = `<h2 class="page-title" style="font-size:16px">Troll</h2>
        <div class="troll-banner">
          <img src="/recnet/troll-logo.png" alt="Grim Labubu">
          <div><strong>Troll</strong><span>Developer-only. These are joke/punitive tools - use with care.</span></div>
        </div>
        <div class="profile-actions">
          <button class="btn danger" data-act="fake-box">GRIM LABUBU ${esc(username)}</button>
          <button class="btn danger" data-act="fake-box-no-ban">Send Fake Box</button>
          <button class="btn danger" data-act="kick-to-room">Kick to Room</button>
          <button class="btn danger" data-act="force-into">Force Into My Instance</button>
        </div>`;

        const fakeBoxBtn = document.querySelector('[data-act="fake-box"]');
        if (fakeBoxBtn) fakeBoxBtn.addEventListener('click', async () => {
            if (!confirm('Send fake level-up box and ban this account? This is a developer-only troll feature.')) return;
            const tokenAmount = Number(prompt('Fake token amount (for display only):', '100000') || 100000);
            try {
                const r = await send(`${API}/admin/accounts/${accountId}/troll/fakebox-and-ban`, 'POST', { tokenAmount, banImmediately: true });
                alert(`Fake box sent with ${r.tokenAmount} tokens. Account banned.`);
                await playerDetail(accountId);
            } catch (error) { alert(error.message); }
        });

        const fakeBoxNoBanBtn = document.querySelector('[data-act="fake-box-no-ban"]');
        if (fakeBoxNoBanBtn) fakeBoxNoBanBtn.addEventListener('click', async () => {
            if (!confirm('Send a fake level-up box without banning this account? This is a developer-only troll feature.')) return;
            const tokenAmount = Number(prompt('Fake token amount (for display only):', '100000') || 100000);
            try {
                const r = await send(`${API}/admin/accounts/${accountId}/troll/fakebox-and-ban`, 'POST', { tokenAmount, banImmediately: false });
                alert(`Fake box sent with ${r.tokenAmount} tokens. Account not banned.`);
            } catch (error) { alert(error.message); }
        });

        const kickToRoomBtn = document.querySelector('[data-act="kick-to-room"]');
        if (kickToRoomBtn) kickToRoomBtn.addEventListener('click', async () => {
            const roomId = prompt('Room ID (leave empty to kick to your current instance):');
            if (roomId === null) return;
            try {
                const r = await send(`${API}/admin/accounts/${accountId}/troll/kick-to-room`, 'POST', { roomId: roomId ? Number(roomId) : null });
                alert(`Kicked to room: ${r.roomName || 'your instance'}`);
            } catch (error) { alert(error.message); }
        });

        const forceIntoBtn = document.querySelector('[data-act="force-into"]');
        if (forceIntoBtn) forceIntoBtn.addEventListener('click', async () => {
            if (!currentUser?.isDeveloper) return;
            if (!confirm('Force this player into your current room instance?')) return;
            try {
                const r = await send(`${API}/admin/accounts/${accountId}/force-into/${currentUser.accountId}`, 'POST', {});
                alert(r.delivered ? `Forced into "${r.roomName}" live.` : `Record updated to "${r.roomName}" - will apply on next connection.`);
            } catch (error) { alert(error.message); }
        });
    } else {
        body.innerHTML = `<div class="placeholder-card"><i class="fa-solid fa-hammer"></i><p>This tab isn't wired up yet.</p></div>`;
    }
}

async function ageVerificationView() {
    app.innerHTML = `<h1 class="page-title">Age Verification</h1><p class="field-hint">Players submit a photo (ID or face) against a code from the website; the photo goes to the staff Discord for manual review. Cross-check the code below against the Discord post before approving.</p><div id="ageVerificationList"><div class="loading"><i></i>Loading queue</div></div>`;
    const list = document.querySelector('#ageVerificationList');

    async function load() {
        list.innerHTML = '<div class="loading"><i></i>Loading queue</div>';
        try {
            const queue = await get(`${API}/admin/age-verification`);
            if (!queue.length) { list.innerHTML = '<p class="field-hint">Nothing awaiting review.</p>'; return; }
            list.innerHTML = queue.map(item => `<div class="room-row" data-code="${esc(item.code)}">
                <div class="meta">
                    <strong>${esc(item.displayName)}</strong> <span class="field-hint">@${esc(item.username || '')} &middot; ID ${item.accountId}</span>
                    <br><small>Code <code>${esc(item.code)}</code> &middot; ${esc(item.method === 'ManualId' ? 'Manual ID Verification' : 'Face Verification (Manually)')}${item.submittedAt ? ` &middot; ${new Date(item.submittedAt).toLocaleString()}` : ''}</small>
                </div>
                <button class="btn small ghost" data-act="approve">Approve</button>
                <button class="btn small danger" data-act="reject">Reject</button>
            </div>`).join('');
            list.querySelectorAll('[data-act="approve"]').forEach(btn => btn.addEventListener('click', async () => {
                const code = btn.closest('[data-code]').dataset.code;
                if (!confirm(`Approve age verification for code ${code}? This clears their Junior status.`)) return;
                try { await send(`${API}/admin/age-verification/${code}/approve`, 'POST', {}); await load(); }
                catch (error) { alert(error.message); }
            }));
            list.querySelectorAll('[data-act="reject"]').forEach(btn => btn.addEventListener('click', async () => {
                const code = btn.closest('[data-code]').dataset.code;
                const reason = prompt('Reason for rejection (optional):') || null;
                try { await send(`${API}/admin/age-verification/${code}/reject`, 'POST', { reason }); await load(); }
                catch (error) { alert(error.message); }
            }));
        } catch (error) {
            list.innerHTML = `<p class="form-status error">${esc(error.message)}</p>`;
        }
    }
    await load();
}

async function liveInstances() {
    app.innerHTML = `
    <h1 class="page-title">Live Room Instances</h1>
    <div class="card">
        <div id="instancesList"><div class="loading"><i></i>Loading live instances</div></div>
    </div>`;

    const list = document.querySelector('#instancesList');

    async function loadInstances() {
        list.innerHTML = '<div class="loading"><i></i>Loading live instances</div>';
        try {
            const data = await get(`${API}/admin/instances`);
            if (!data.instances.length) { list.innerHTML = '<p class="field-hint">No players currently online.</p>'; return; }
            list.innerHTML = `<p class="field-hint">${data.totalOnline} players online across ${data.instances.length} instances.</p>` + data.instances.map(instance => `
                <div class="card" style="margin-top:10px">
                    <div style="display:flex;justify-content:space-between;align-items:center">
                        <h3>${esc(instance.room)} ${instance.roomId ? `(Room #${instance.roomId})` : ''}</h3>
                        <button class="btn small danger shutdown-instance" data-instance-id="${instance.roomInstanceId}">Shutdown (Send to Dorm)</button>
                    </div>
                    <p class="field-hint">${instance.playerCount} player(s) · Instance ID: ${instance.roomInstanceId || 'N/A'}</p>
                    <div style="margin-top:10px">
                        ${instance.players.map(p => `
                            <div class="room-row">
                                <img src="${esc(p.profileImage || '')}" class="avatar-img" style="width:32px;height:32px" alt="">
                                <div class="meta">
                                    <strong>${esc(p.displayName || p.username)}</strong><br>
                                    <small>@${esc(p.username)} · ${p.device} · ${p.sockets} socket(s)</small>
                                </div>
                                <a class="btn small ghost" href="#players/${p.accountId}">View</a>
                            </div>
                        `).join('')}
                    </div>
                </div>
            `).join('');

            list.querySelectorAll('.shutdown-instance').forEach(btn => btn.addEventListener('click', async () => {
                if (!confirm('Shutdown this instance? All players will be sent to their dorm.')) return;
                try {
                    await send(`${API}/admin/instances/${btn.dataset.instanceId}/shutdown`, 'POST', {});
                    await loadInstances();
                } catch (error) { alert(error.message); }
            }));
        } catch (error) {
            list.innerHTML = `<p class="form-status error">${esc(error.message)}</p>`;
        }
    }

    await loadInstances();
}

async function roomsList() {
    app.innerHTML = `<h1 class="page-title">Rooms</h1>
    <div class="search-row">
        <input id="roomSearch" placeholder="Search by name, ID, description, or creator ID">
        <label style="display:flex;align-items:center;gap:6px;white-space:nowrap"><input type="checkbox" id="roomIncludeDorms" style="width:auto"> Include dorms</label>
        <button class="btn" id="roomSearchGo">Search</button>
    </div>
    <div id="roomListResults" class="player-list"></div>
    <div class="search-row" id="roomListPager" style="margin-top:10px;display:none">
        <button class="btn ghost small" id="roomListPrev">&larr; Prev</button>
        <span class="field-hint" id="roomListPageInfo" style="align-self:center"></span>
        <button class="btn ghost small" id="roomListNext">Next &rarr;</button>
    </div>`;
    const input = document.querySelector('#roomSearch'), results = document.querySelector('#roomListResults');
    const includeDorms = document.querySelector('#roomIncludeDorms');
    const pager = document.querySelector('#roomListPager');
    let roomListSkip = 0;
    const roomListTake = 20;
    async function runSearch() {
        results.innerHTML = '<div class="loading"><i></i>Loading rooms</div>';
        try {
            const data = await get(`${API}/admin/rooms?search=${encodeURIComponent(input.value.trim())}&includeDorms=${includeDorms.checked}&skip=${roomListSkip}&take=${roomListTake}`);
            const rooms = data.results ?? data;
            const total = data.total ?? rooms.length;
            results.innerHTML = rooms.length ? rooms.map(r => `<a class="player-row" href="#rooms/${r.roomId}"><img src="${esc(r.image || '')}" alt=""><div class="meta"><div class="name">${esc(r.name || 'Untitled room')}</div><div class="sub">ID ${r.roomId} &middot; by ${esc(r.creatorName || r.creatorAccountId)}${r.onlinePlayers ? ` &middot; ${r.onlinePlayers} online` : ''}</div></div></a>`).join('') : '<p class="field-hint">No rooms found.</p>';
            pager.style.display = total > roomListTake ? 'flex' : 'none';
            document.querySelector('#roomListPageInfo').textContent = total ? `${roomListSkip + 1}-${Math.min(roomListSkip + roomListTake, total)} of ${total}` : '';
            document.querySelector('#roomListPrev').disabled = roomListSkip <= 0;
            document.querySelector('#roomListNext').disabled = roomListSkip + roomListTake >= total;
        } catch (error) {
            results.innerHTML = `<p class="form-status error">${esc(error.message)}</p>`;
        }
    }
    const roomSearchGo = document.querySelector('#roomSearchGo');
    if (roomSearchGo) roomSearchGo.addEventListener('click', () => { roomListSkip = 0; runSearch(); });
    if (input) input.addEventListener('keydown', e => { if (e.key === 'Enter') { roomListSkip = 0; runSearch(); } });
    if (includeDorms) includeDorms.addEventListener('change', () => { roomListSkip = 0; runSearch(); });
    document.querySelector('#roomListPrev').addEventListener('click', () => { roomListSkip = Math.max(0, roomListSkip - roomListTake); runSearch(); });
    document.querySelector('#roomListNext').addEventListener('click', () => { roomListSkip += roomListTake; runSearch(); });
    await runSearch();
}

async function roomDetail(roomIdRaw) {
    const roomId = Number(roomIdRaw);
    app.innerHTML = '<div class="loading"><i></i>Loading room</div>';
    let room;
    try { room = await get(`${API}/admin/rooms/${roomId}`); }
    catch (error) { app.innerHTML = `<a class="back-link" href="#rooms">&larr; Back to Rooms</a><div class="card"><p class="form-status error">${esc(error.message)}</p></div>`; return; }

    app.innerHTML = `
    <a class="back-link" href="#rooms">&larr; Back to Rooms</a>
    <div class="profile-header">
      <img src="${esc(room.summary?.image || '')}" alt="">
      <div>
        <div class="name">${esc(room.name || 'Untitled room')}</div>
        <div class="stat-line">${esc(room.description || '(no description)')}</div>
        <div class="stat-line">ID: ${room.roomId} &middot; Creator: ${esc(room.creator?.displayName || room.creator?.username || room.creatorAccountId)}</div>
        <div class="profile-actions">
          <a class="btn ghost" href="/recnet/#room/${roomId}" target="_blank" rel="noopener">Public page</a>
          <a class="btn ghost" href="${API}/admin/rooms/${roomId}/export" target="_blank" rel="noopener">Download backup</a>
          <a class="btn ghost" href="/recnet/#admin" target="_blank" rel="noopener">Full editor (classic panel)</a>
        </div>
      </div>
    </div>
    <div class="tabs">
      <div class="tab active" data-tab="details">Details</div>
      <div class="tab" data-tab="subrooms">Subrooms</div>
      <div class="tab" data-tab="roles">Roles</div>
      <div class="tab" data-tab="bans">Bans</div>
    </div>
    <div id="tabBody"></div>`;

    document.querySelectorAll('.tab').forEach(tab => tab.addEventListener('click', () => {
        document.querySelectorAll('.tab').forEach(t => t.classList.toggle('active', t === tab));
        renderRoomTab(tab.dataset.tab, roomId);
    }));
    renderRoomTab('details', roomId);
}

const ROOM_FLAG_LABELS = {
    cloningAllowed: 'Cloning allowed', disableMicAutoMute: 'Disable mic auto-mute',
    disableRoomComments: 'Disable room comments', encryptVoiceChat: 'Encrypt voice chat',
    toxmodEnabled: 'Toxmod enabled', loadScreenLocked: 'Lock loading screen',
    autoLocalizeRoom: 'Auto-localize room', isDeveloperOwned: 'Developer owned',
    supportsLevelVoting: 'Supports level voting', isRRO: 'Rec Room Original',
    supportsScreens: 'Supports screens', supportsWalkVR: 'Walk VR',
    supportsTeleportVR: 'Teleport VR', supportsVRLow: 'Low-end VR',
    supportsQuest2: 'Quest 2', supportsMobile: 'Mobile', supportsJuniors: 'Junior accounts'
};
const ROOM_ASSIGNED_ROLES = ['None', 'Host', 'Moderator', 'CoOwner', 'TemporaryCoOwner', 'Banned'];
const ROOM_INVITED_ROLES = ['None', 'Host', 'Moderator', 'CoOwner', 'TemporaryCoOwner'];

async function renderRoomTab(tab, roomId) {
    const body = document.querySelector('#tabBody');
    if (tab === 'details') {
        body.innerHTML = `<h2 class="page-title" style="font-size:16px">Room Details</h2>
        <div class="card" id="roomSummaryCard"><h2>Summary</h2><p class="field-hint">Loading...</p></div>
        <div class="card dev-block"><h2>Edit Room</h2>
          <p class="field-hint">Developer-only.</p>
          <div class="field-row">
            <label class="field">Name<input id="roomName"></label>
            <label class="field">Image path<input id="roomImage"></label>
          </div>
          <div class="field-row">
            <label class="field">Accessibility<select id="roomAccessibility"><option>Private</option><option>Public</option><option>Unlisted</option></select></label>
            <label class="field">State<select id="roomState"><option>Active</option><option>PendingJunior</option><option>Moderation_PendingReview</option><option>Moderation_Closed</option><option>MarkedForDelete</option></select></label>
          </div>
          <div class="field-row">
            <label class="field">Max players<input type="number" id="roomMaxPlayers" min="1" max="100"></label>
            <label class="field">Minimum level<input type="number" id="roomMinLevel" min="0" max="50"></label>
          </div>
          <label class="field">Tags (comma-separated)<input id="roomTags"></label>
          <label class="field">Description<textarea id="roomDesc" rows="3"></textarea></label>
          <h3>Compatibility &amp; behavior flags</h3>
          <div class="flag-grid" id="roomFlagGrid">${Object.entries(ROOM_FLAG_LABELS).map(([key, label]) => `<label><input type="checkbox" data-flag="${key}"> ${esc(label)}</label>`).join('')}</div>
          <button class="btn" id="saveRoom">Save Changes</button><span class="form-status" id="roomStatus"></span>
        </div>
        <div class="card dev-block"><h2>Room stats</h2>
          <div class="field-row">
            <label class="field">Cheers<input type="number" id="statCheers" min="0"></label>
            <label class="field">Favorites<input type="number" id="statFavorites" min="0"></label>
            <label class="field">Visitors<input type="number" id="statVisitors" min="0"></label>
            <label class="field">Visits<input type="number" id="statVisits" min="0"></label>
          </div>
          <button class="btn" id="saveStats">Save stats</button><span class="form-status" id="statStatus"></span>
        </div>
        <div class="card dev-block"><h2>Transfer ownership</h2>
          <p class="field-hint">Developer-only. The current owner becomes CoOwner.</p>
          <div class="field-row"><label class="field">New owner account ID<input type="number" id="newOwnerId" min="1"></label></div>
          <button class="btn danger" id="transferOwner">Transfer owner</button><span class="form-status" id="transferStatus"></span>
        </div>
        <div class="card dev-block"><h2>Change room ID</h2>
          <p class="field-hint">Developer-only. Renumbers this room's ID and migrates its subrooms, saves, bans, and creator-feature data (cloud variables, inventions, room player data). Anything that only softly references the old ID (players' visited/favorited room lists) is left as-is - it'll just stop resolving.</p>
          <div class="field-row"><label class="field">New room ID<input type="number" id="newRoomId" min="1"></label></div>
          <button class="btn danger" id="changeRoomId">Change ID</button><span class="form-status" id="changeRoomIdStatus"></span>
        </div>`;

        try {
            const room = await get(`${API}/admin/rooms/${roomId}`);
            document.querySelector('#roomName').value = room.name || '';
            document.querySelector('#roomImage').value = room.imageName || '';
            document.querySelector('#roomAccessibility').value = room.accessibility || 'Public';
            document.querySelector('#roomState').value = room.state || 'Active';
            document.querySelector('#roomMaxPlayers').value = room.maxPlayers || 8;
            document.querySelector('#roomMinLevel').value = room.minLevel || 0;
            document.querySelector('#roomTags').value = (room.tags || []).join(', ');
            document.querySelector('#roomDesc').value = room.description || '';
            document.querySelectorAll('#roomFlagGrid [data-flag]').forEach(input => {
                input.checked = !!room.flags?.[input.dataset.flag];
            });
            document.querySelector('#statCheers').value = room.stats?.cheers ?? 0;
            document.querySelector('#statFavorites').value = room.stats?.favorites ?? 0;
            document.querySelector('#statVisitors').value = room.stats?.visitors ?? 0;
            document.querySelector('#statVisits').value = room.stats?.visits ?? 0;
            document.querySelector('#roomSummaryCard').innerHTML = `<h2>Summary</h2><p class="field-hint">Accessibility: ${esc(room.accessibility)} &middot; State: ${esc(room.state)} &middot; Max players: ${room.maxPlayers} &middot; Min level: ${room.minLevel}</p><p class="field-hint">Tags: ${esc((room.tags || []).join(', ') || 'none')}</p>`;
        } catch (error) {
            body.innerHTML = `<p class="form-status error">${esc(error.message)}</p>`;
            return;
        }
        applyRoleGating();

        const saveRoomBtn = document.querySelector('#saveRoom');
        if (saveRoomBtn) saveRoomBtn.addEventListener('click', async () => {
            const status = document.querySelector('#roomStatus');
            const flags = {};
            document.querySelectorAll('#roomFlagGrid [data-flag]').forEach(input => { flags[input.dataset.flag] = input.checked; });
            try {
                status.textContent = 'Saving...';
                status.classList.remove('error');
                await send(`${API}/admin/rooms/${roomId}`, 'PUT', {
                    name: document.querySelector('#roomName').value,
                    description: document.querySelector('#roomDesc').value,
                    imageName: document.querySelector('#roomImage').value,
                    accessibility: document.querySelector('#roomAccessibility').value,
                    state: document.querySelector('#roomState').value,
                    maxPlayers: parseInt(document.querySelector('#roomMaxPlayers').value) || 1,
                    minLevel: parseInt(document.querySelector('#roomMinLevel').value) || 0,
                    tags: document.querySelector('#roomTags').value.split(',').map(t => t.trim()).filter(Boolean),
                    ...flags
                });
                status.textContent = 'Saved successfully.';
                status.classList.remove('error');
            } catch (error) {
                status.textContent = error.message;
                status.classList.add('error');
            }
        });

        const saveStatsBtn = document.querySelector('#saveStats');
        if (saveStatsBtn) saveStatsBtn.addEventListener('click', async () => {
            const status = document.querySelector('#statStatus');
            try {
                await send(`${API}/admin/rooms/${roomId}/stats`, 'PUT', {
                    cheers: parseInt(document.querySelector('#statCheers').value) || 0,
                    favorites: parseInt(document.querySelector('#statFavorites').value) || 0,
                    visitors: parseInt(document.querySelector('#statVisitors').value) || 0,
                    visits: parseInt(document.querySelector('#statVisits').value) || 0
                });
                status.textContent = 'Saved.'; status.classList.remove('error');
            } catch (error) { status.textContent = error.message; status.classList.add('error'); }
        });

        const transferBtn = document.querySelector('#transferOwner');
        if (transferBtn) transferBtn.addEventListener('click', async () => {
            const status = document.querySelector('#transferStatus');
            const accountId = Number(document.querySelector('#newOwnerId').value);
            if (!accountId) return;
            if (!confirm(`Transfer ownership of this room to account ${accountId}? The current owner becomes CoOwner.`)) return;
            try {
                await send(`${API}/admin/rooms/${roomId}/transfer-owner`, 'POST', { accountId });
                status.textContent = 'Transferred.'; status.classList.remove('error');
                await roomDetail(roomId);
            } catch (error) { status.textContent = error.message; status.classList.add('error'); }
        });

        const changeIdBtn = document.querySelector('#changeRoomId');
        if (changeIdBtn) changeIdBtn.addEventListener('click', async () => {
            const status = document.querySelector('#changeRoomIdStatus');
            const newRoomId = Number(document.querySelector('#newRoomId').value);
            if (!newRoomId) return;
            if (!confirm(`Change this room's ID from ${roomId} to ${newRoomId}? Anything holding the old ID directly (bookmarks, external links) will break.`)) return;
            try {
                await send(`${API}/admin/rooms/${roomId}/change-id`, 'POST', { newRoomId });
                location.hash = `#rooms/${newRoomId}`;
            } catch (error) { status.textContent = error.message; status.classList.add('error'); }
        });
    } else if (tab === 'subrooms') {
        body.innerHTML = `<h2 class="page-title" style="font-size:16px">Subrooms</h2>
        <div class="card dev-block"><h2>Create subroom</h2><div class="field-row"><label class="field">Name<input id="newSubroomName" maxlength="50"></label></div><button class="btn" id="createSubroom">Add subroom</button><span class="form-status" id="createSubroomStatus"></span></div>
        <div id="subroomList"><div class="loading"><i></i>Loading subrooms</div></div>`;

        async function loadSubrooms() {
            const list = document.querySelector('#subroomList');
            try {
                const room = await get(`${API}/admin/rooms/${roomId}`);
                if (!room.subRooms || !room.subRooms.length) { list.innerHTML = '<p class="field-hint">No subrooms.</p>'; return; }
                list.innerHTML = room.subRooms.map(sr => `
                    <div class="card" data-id="${sr.subRoomId}">
                        <h2>${esc(sr.name || 'Untitled')} <span class="field-hint">#${sr.subRoomId} &middot; Save #${sr.currentSaveId || 0}${sr.hasData ? ' &middot; Has data' : ''} &middot; ${esc(sr.accessibility)}</span></h2>
                        <div class="dev-block">
                        <div class="field-row">
                            <label class="field">Name<input data-field="name" maxlength="50" value="${esc(sr.name || '')}"></label>
                            <label class="field">Max players<input data-field="maxPlayers" type="number" min="1" max="100" value="${sr.maxPlayers || 1}"></label>
                        </div>
                        <div class="field-row">
                            <label class="field">Accessibility<select data-field="accessibility"><option${sr.accessibility === 'Private' ? ' selected' : ''}>Private</option><option${sr.accessibility === 'Public' ? ' selected' : ''}>Public</option><option${sr.accessibility === 'Unlisted' ? ' selected' : ''}>Unlisted</option></select></label>
                            <label class="field" style="display:flex;align-items:center;gap:6px;flex-direction:row"><input type="checkbox" data-field="isSandbox" style="width:auto"${sr.isSandbox ? ' checked' : ''}> Sandbox subroom</label>
                        </div>
                        <label class="field">Unity scene ID<input data-field="unitySceneId" maxlength="200" value="${esc(sr.unitySceneId || '')}"></label>
                        <div class="actions-cell">
                            <button class="btn small save-subroom">Save</button>
                            <button class="btn small ghost clone-subroom">Clone</button>
                            <button class="btn small danger delete-subroom">Delete</button>
                        </div>
                        <h3>Persistence blob pair <span class="field-hint">(files must already exist in CDN/room)</span></h3>
                        <div class="field-row">
                            <label class="field">RoomBlob ${sr.roomBlobExists ? '<span style="color:var(--success)">&check; found</span>' : '<span style="color:var(--danger)">&times; missing</span>'}<input data-field="roomBlob" maxlength="255" value="${esc(sr.roomBlob || '')}" placeholder="RoomBlob filename"></label>
                            <label class="field">Metadata blob ${sr.metadataBlobExists ? '<span style="color:var(--success)">&check; found</span>' : '<span style="color:var(--danger)">&times; missing</span>'}<input data-field="metadataBlob" maxlength="255" value="${esc(sr.metadataBlob || '')}" placeholder="Metadata blob filename"></label>
                        </div>
                        <button class="btn small save-blobs">Save blob pair</button>
                        </div>
                    </div>
                `).join('');
                applyRoleGating();
                list.querySelectorAll('.save-subroom').forEach(btn => btn.addEventListener('click', async () => {
                    const card = btn.closest('[data-id]'), id = card.dataset.id;
                    try {
                        await send(`${API}/admin/rooms/${roomId}/subrooms/${id}`, 'PUT', {
                            name: card.querySelector('[data-field="name"]').value,
                            maxPlayers: parseInt(card.querySelector('[data-field="maxPlayers"]').value) || 1,
                            accessibility: card.querySelector('[data-field="accessibility"]').value,
                            isSandbox: card.querySelector('[data-field="isSandbox"]').checked,
                            unitySceneId: card.querySelector('[data-field="unitySceneId"]').value
                        });
                        await loadSubrooms();
                    } catch (error) { alert(error.message); }
                }));
                list.querySelectorAll('.clone-subroom').forEach(btn => btn.addEventListener('click', async () => {
                    const card = btn.closest('[data-id]'), id = card.dataset.id;
                    try { await send(`${API}/admin/rooms/${roomId}/subrooms/${id}/clone`, 'POST'); await loadSubrooms(); }
                    catch (error) { alert(error.message); }
                }));
                list.querySelectorAll('.delete-subroom').forEach(btn => btn.addEventListener('click', async () => {
                    const card = btn.closest('[data-id]'), id = card.dataset.id;
                    if (!confirm('Delete this subroom? A room must keep at least one.')) return;
                    try {
                        await send(`${API}/admin/rooms/${roomId}/subrooms/${id}`, 'DELETE');
                        await loadSubrooms();
                    } catch (error) { alert(error.message); }
                }));
                list.querySelectorAll('.save-blobs').forEach(btn => btn.addEventListener('click', async () => {
                    const card = btn.closest('[data-id]'), id = card.dataset.id;
                    try {
                        await send(`${API}/admin/rooms/${roomId}/subrooms/${id}/blobs`, 'PUT', {
                            roomBlob: card.querySelector('[data-field="roomBlob"]').value,
                            metadataBlob: card.querySelector('[data-field="metadataBlob"]').value
                        });
                        await loadSubrooms();
                    } catch (error) { alert(error.message); }
                }));
            } catch (error) {
                list.innerHTML = `<p class="form-status error">${esc(error.message)}</p>`;
            }
        }
        applyRoleGating();
        const createSubroomBtn = document.querySelector('#createSubroom');
        if (createSubroomBtn) createSubroomBtn.addEventListener('click', async () => {
            const status = document.querySelector('#createSubroomStatus');
            const nameInput = document.querySelector('#newSubroomName');
            const name = nameInput.value.trim();
            if (!name) return;
            try {
                await send(`${API}/admin/rooms/${roomId}/subrooms`, 'POST', { name });
                nameInput.value = '';
                status.textContent = ''; status.classList.remove('error');
                await loadSubrooms();
            } catch (error) { status.textContent = error.message; status.classList.add('error'); }
        });
        await loadSubrooms();
    } else if (tab === 'roles') {
        body.innerHTML = `<h2 class="page-title" style="font-size:16px">Room Roles</h2><div class="card dev-block"><h2>Add / Update Role</h2><p class="field-hint">Developer-only.</p><div class="field-row"><label class="field">Account ID<input id="roleAccountId" type="number" min="1"></label><label class="field">Assigned role<select id="roleType">${ROOM_ASSIGNED_ROLES.map(r => `<option>${r}</option>`).join('')}</select></label><label class="field">Invited role<select id="invitedRoleType">${ROOM_INVITED_ROLES.map(r => `<option>${r}</option>`).join('')}</select></label></div><button class="btn" id="addRole">Save Role</button></div><div class="card"><h2>Current Roles</h2><div id="roleList"><div class="loading"><i></i>Loading roles</div></div></div>`;
        applyRoleGating();

        async function loadRoles() {
            const list = document.querySelector('#roleList');
            try {
                const room = await get(`${API}/admin/rooms/${roomId}`);
                if (!room.roles || !room.roles.length) { list.innerHTML = '<p class="field-hint">No roles assigned.</p>'; return; }
                list.innerHTML = room.roles.map(r => `
                    <div class="room-row">
                        <div class="meta"><strong>${esc(r.player?.displayName || r.player?.username || r.accountId)}</strong><br><small>${esc(r.role)}${r.invitedRole && r.invitedRole !== 'None' ? ` &middot; Invited: ${esc(r.invitedRole)}` : ''}</small></div>
                        <button class="btn small danger delete-role dev-only" data-id="${r.accountId}">Remove</button>
                    </div>
                `).join('');
                applyRoleGating();
                list.querySelectorAll('.delete-role').forEach(btn => btn.addEventListener('click', async () => {
                    if (!confirm('Remove this role?')) return;
                    try {
                        await send(`${API}/admin/rooms/${roomId}/roles/${btn.dataset.id}`, 'DELETE');
                        await loadRoles();
                    } catch (error) { alert(error.message); }
                }));
            } catch (error) {
                list.innerHTML = `<p class="form-status error">${esc(error.message)}</p>`;
            }
        }

        const addRoleBtn = document.querySelector('#addRole');
        if (addRoleBtn) addRoleBtn.addEventListener('click', async () => {
            const accountId = document.querySelector('#roleAccountId');
            const roleType = document.querySelector('#roleType');
            const invitedRoleType = document.querySelector('#invitedRoleType');
            const accId = Number(accountId.value);
            if (!accId) return;

            try {
                await send(`${API}/admin/rooms/${roomId}/roles/${accId}`, 'PUT', { role: roleType.value, invitedRole: invitedRoleType.value });
                accountId.value = '';
                await loadRoles();
            } catch (error) { alert(error.message); }
        });

        await loadRoles();
    } else if (tab === 'bans') {
        body.innerHTML = `<h2 class="page-title" style="font-size:16px">Room Bans</h2><div class="card"><h2>Add Ban</h2><input id="banAccountId" placeholder="Account ID"><input id="banReason" placeholder="Reason"><button class="btn" id="addBan">Ban Player</button></div><div class="card"><h2>Current Bans</h2><div id="banList"><div class="loading"><i></i>Loading bans</div></div></div>`;

        async function loadBans() {
            const list = document.querySelector('#banList');
            try {
                const room = await get(`${API}/admin/rooms/${roomId}`);
                if (!room.bans || !room.bans.length) { list.innerHTML = '<p class="field-hint">No bans.</p>'; return; }
                list.innerHTML = room.bans.map(b => `
                    <div class="room-row">
                        <div style="flex:1"><strong>${esc(b.displayName || b.username || b.accountId)}</strong><br><small>${esc(b.reason || 'No reason')}</small></div>
                        <button class="btn small danger delete-ban" data-id="${b.accountId}">Unban</button>
                    </div>
                `).join('');
                list.querySelectorAll('.delete-ban').forEach(btn => btn.addEventListener('click', async () => {
                    if (!confirm('Unban this player?')) return;
                    try {
                        await send(`${API}/admin/rooms/${roomId}/bans/${btn.dataset.id}`, 'DELETE');
                        await loadBans();
                    } catch (error) { alert(error.message); }
                }));
            } catch (error) {
                list.innerHTML = `<p class="form-status error">${esc(error.message)}</p>`;
            }
        }

        const addBanBtn = document.querySelector('#addBan');
        if (addBanBtn) addBanBtn.addEventListener('click', async () => {
            const accountId = document.querySelector('#banAccountId');
            const banReason = document.querySelector('#banReason');
            if (!accountId || !banReason) return;

            const accId = Number(accountId.value);
            if (!accId) return;

            try {
                await send(`${API}/admin/rooms/${roomId}/bans`, 'POST', { accountId: accId, reason: banReason.value });
                accountId.value = '';
                banReason.value = '';
                await loadBans();
            } catch (error) { alert(error.message); }
        });

        await loadBans();
    }
}

async function playerEventsView() {
    app.innerHTML = `<h1 class="page-title">Player Events</h1>
    <div class="card">
      <h2 id="evFormTitle">New event</h2>
      <input type="hidden" id="evEditId">
      <label class="field">Title<input id="evTitle"></label>
      <label class="field">Description<textarea id="evDesc" rows="3"></textarea></label>
      <div class="field-row">
        <label class="field">Starts at<input id="evStart" type="datetime-local"></label>
        <label class="field">Ends at (optional)<input id="evEnd" type="datetime-local"></label>
      </div>
      <label class="field">Image<input type="file" id="evImageFile" accept="image/*"></label>
      <input type="hidden" id="evImageName">
      <div id="evImagePreview" class="field-hint"></div>
      <label><input type="checkbox" id="evPinned"> Pinned</label><br><br>
      <button class="btn" id="evCreate">Create event</button>
      <button class="btn ghost" id="evCancelEdit" hidden>Cancel edit</button>
      <span class="form-status" id="evStatus"></span>
    </div><div id="evList"><div class="loading"><i></i>Loading events</div></div>`;

    const evTitle = document.querySelector('#evTitle');
    const evDesc = document.querySelector('#evDesc');
    const evStart = document.querySelector('#evStart');
    const evEnd = document.querySelector('#evEnd');
    const evImageFile = document.querySelector('#evImageFile');
    const evImageName = document.querySelector('#evImageName');
    const evImagePreview = document.querySelector('#evImagePreview');
    const evPinned = document.querySelector('#evPinned');
    const evEditId = document.querySelector('#evEditId');
    const evFormTitle = document.querySelector('#evFormTitle');
    const evCreateBtn = document.querySelector('#evCreate');
    const evCancelEditBtn = document.querySelector('#evCancelEdit');

    evImageFile.addEventListener('change', async () => {
        const status = document.querySelector('#evStatus');
        const file = evImageFile.files[0];
        if (!file) return;
        const formData = new FormData();
        formData.append('file', file);
        try {
            const r = await fetch(`${API}/admin/events/image`, { method: 'POST', body: formData, credentials: 'same-origin' });
            const data = await r.json();
            if (!r.ok) throw new Error(data.error || 'Upload failed');
            evImageName.value = data.path;
            evImagePreview.innerHTML = `<img src="${esc(data.url)}" style="max-width:160px;border-radius:8px;margin-top:6px">`;
        } catch (error) { status.textContent = error.message; status.classList.add('error'); }
    });

    function resetForm() {
        evEditId.value = '';
        evFormTitle.textContent = 'New event';
        evCreateBtn.textContent = 'Create event';
        evCancelEditBtn.hidden = true;
        evTitle.value = ''; evDesc.value = ''; evStart.value = ''; evEnd.value = '';
        evImageFile.value = ''; evImageName.value = ''; evImagePreview.innerHTML = '';
        evPinned.checked = false;
    }

    function toLocalInputValue(iso) {
        if (!iso) return '';
        const d = new Date(iso);
        d.setMinutes(d.getMinutes() - d.getTimezoneOffset());
        return d.toISOString().slice(0, 16);
    }

    function editEvent(e) {
        evEditId.value = e.eventId ?? e.id;
        evFormTitle.textContent = `Editing "${e.title}"`;
        evCreateBtn.textContent = 'Save changes';
        evCancelEditBtn.hidden = false;
        evTitle.value = e.title;
        evDesc.value = e.description || '';
        evStart.value = toLocalInputValue(e.startsAt);
        evEnd.value = toLocalInputValue(e.endsAt);
        evImageName.value = e.image ? decodeURIComponent(e.image.replace('/imageserver/', '')) : '';
        evImagePreview.innerHTML = e.image ? `<img src="${esc(e.image)}" style="max-width:160px;border-radius:8px;margin-top:6px">` : '';
        evPinned.checked = !!e.pinned;
        window.scrollTo({ top: 0, behavior: 'smooth' });
    }

    async function load() {
        const list = document.querySelector('#evList');
        try {
            const items = await get(`${API}/admin/events`);
            if (!items.length) { list.innerHTML = '<p class="field-hint">No events yet.</p>'; return; }
            list.innerHTML = items.map(e => `<div class="card" data-id="${e.eventId ?? e.id}">${e.image ? `<img src="${esc(e.image)}" style="max-width:200px;border-radius:8px;margin-bottom:8px">` : ''}<h2>${esc(e.title)} ${e.pinned ? '&#128204;' : ''}</h2><p>${esc(e.description)}</p><p class="field-hint">${e.startsAt ? new Date(e.startsAt).toLocaleString() : ''}${e.endsAt ? ' - ' + new Date(e.endsAt).toLocaleString() : ''}</p><div class="actions-cell"><button class="btn small ghost edit-ev">Edit</button><button class="btn small danger delete-ev">Delete</button></div></div>`).join('');
            list.querySelectorAll('.edit-ev').forEach((btn, i) => btn.addEventListener('click', () => editEvent(items[i])));
            list.querySelectorAll('.delete-ev').forEach(btn => btn.addEventListener('click', async () => {
                const card = btn.closest('[data-id]'), id = card.dataset.id;
                if (!confirm('Delete this event?')) return;
                try { await send(`${API}/admin/events/${id}`, 'DELETE'); card.remove(); }
                catch (error) { alert(error.message); }
            }));
        } catch (error) {
            list.innerHTML = `<p class="form-status error">${esc(error.message)}</p>`;
        }
    }
    if (evCreateBtn) evCreateBtn.addEventListener('click', async () => {
        const status = document.querySelector('#evStatus');
        const title = evTitle.value.trim();
        const description = evDesc.value.trim();
        const startsAt = evStart.value;
        const endsAt = evEnd.value;
        if (!title || !startsAt) { status.textContent = 'Title and start time are required.'; status.classList.add('error'); return; }
        const payload = {
            title, description,
            imageName: evImageName.value || null,
            startsAt: new Date(startsAt).toISOString(),
            endsAt: endsAt ? new Date(endsAt).toISOString() : null,
            pinned: evPinned.checked
        };
        try {
            if (evEditId.value) {
                await send(`${API}/admin/events/${evEditId.value}`, 'PUT', payload);
                status.textContent = 'Saved.';
            } else {
                await send(`${API}/admin/events`, 'POST', payload);
                status.textContent = 'Created.';
            }
            status.classList.remove('error');
            resetForm();
            await load();
        } catch (error) {
            status.textContent = error.message;
            status.classList.add('error');
        }
    });
    if (evCancelEditBtn) evCancelEditBtn.addEventListener('click', resetForm);
    await load();
}

function simpleMarkdownPreview(markdown) {
    return esc(markdown)
        .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
        .replace(/\*(.+?)\*/g, '<em>$1</em>')
        .replace(/\n/g, '<br>');
}

const ANNOUNCEMENT_KINDS = ['info', 'update', 'warning', 'maintenance'];

async function announcementsView() {
    app.innerHTML = `<h1 class="page-title">Announcements</h1>
    <div class="card">
      <h2 id="annFormTitle">New announcement</h2>
      <input type="hidden" id="annEditId">
      <label class="field">Title<input id="annTitle"></label>
      <div class="field-row">
        <label class="field">Kind<select id="annKind">${ANNOUNCEMENT_KINDS.map(k => `<option>${k}</option>`).join('')}</select></label>
      </div>
      <label class="field">Body (markdown)<textarea id="annBody" rows="4"></textarea></label>
      <div class="card" style="background:var(--panel-alt)"><h3>Preview</h3><div id="annPreview" class="field-hint"></div></div>
      <label><input type="checkbox" id="annPinned"> Pinned</label> <label><input type="checkbox" id="annPublished" checked> Published</label><br><br>
      <button class="btn" id="annCreate">Post announcement</button>
      <button class="btn ghost" id="annCancelEdit" hidden>Cancel edit</button>
      <span class="form-status" id="annStatus"></span>
    </div><div id="annList"><div class="loading"><i></i>Loading announcements</div></div>`;

    const annTitle = document.querySelector('#annTitle');
    const annKind = document.querySelector('#annKind');
    const annBody = document.querySelector('#annBody');
    const annPreview = document.querySelector('#annPreview');
    const annPinned = document.querySelector('#annPinned');
    const annPublished = document.querySelector('#annPublished');
    const annEditId = document.querySelector('#annEditId');
    const annFormTitle = document.querySelector('#annFormTitle');
    const annCreateBtn = document.querySelector('#annCreate');
    const annCancelEditBtn = document.querySelector('#annCancelEdit');

    annBody.addEventListener('input', () => { annPreview.innerHTML = simpleMarkdownPreview(annBody.value) || '<em>Nothing to preview yet.</em>'; });

    function resetForm() {
        annEditId.value = '';
        annFormTitle.textContent = 'New announcement';
        annCreateBtn.textContent = 'Post announcement';
        annCancelEditBtn.hidden = true;
        annTitle.value = '';
        annKind.value = 'info';
        annBody.value = '';
        annPreview.innerHTML = '';
        annPinned.checked = false;
        annPublished.checked = true;
    }

    function editAnnouncement(a) {
        annEditId.value = a.id;
        annFormTitle.textContent = `Editing "${a.title}"`;
        annCreateBtn.textContent = 'Save changes';
        annCancelEditBtn.hidden = false;
        annTitle.value = a.title;
        annKind.value = a.kind;
        annBody.value = a.bodyMarkdown;
        annPreview.innerHTML = simpleMarkdownPreview(a.bodyMarkdown);
        annPinned.checked = a.pinned;
        annPublished.checked = a.published;
        window.scrollTo({ top: 0, behavior: 'smooth' });
    }

    async function load() {
        const list = document.querySelector('#annList');
        try {
            const items = await get(`${API}/admin/announcements`);
            if (!items.length) { list.innerHTML = '<p class="field-hint">No announcements yet.</p>'; return; }
            list.innerHTML = items.map(a => `<div class="card" data-id="${a.id}"><h2>${esc(a.title)} <span class="badge">${esc(a.kind)}</span> ${a.pinned ? '&#128204;' : ''} ${a.published ? '' : '<span class="badge">Draft</span>'}</h2><p>${esc(a.bodyMarkdown)}</p><div class="actions-cell"><button class="btn small ghost edit-ann">Edit</button><button class="btn small danger delete-ann">Delete</button></div></div>`).join('');
            list.querySelectorAll('.edit-ann').forEach((btn, i) => btn.addEventListener('click', () => editAnnouncement(items[i])));
            list.querySelectorAll('.delete-ann').forEach(btn => btn.addEventListener('click', async () => {
                const card = btn.closest('[data-id]'), id = card.dataset.id;
                if (!confirm('Delete this announcement?')) return;
                try { await send(`${API}/admin/announcements/${id}`, 'DELETE'); card.remove(); }
                catch (error) { alert(error.message); }
            }));
        } catch (error) {
            list.innerHTML = `<p class="form-status error">${esc(error.message)}</p>`;
        }
    }
    if (annCreateBtn) annCreateBtn.addEventListener('click', async () => {
        const status = document.querySelector('#annStatus');
        const title = annTitle.value.trim();
        const bodyMarkdown = annBody.value.trim();
        if (!title || !bodyMarkdown) { status.textContent = 'Title and body are required.'; status.classList.add('error'); return; }
        const payload = { title, bodyMarkdown, kind: annKind.value, pinned: annPinned.checked, published: annPublished.checked };
        try {
            if (annEditId.value) {
                await send(`${API}/admin/announcements/${annEditId.value}`, 'PUT', payload);
                status.textContent = 'Saved.';
            } else {
                await send(`${API}/admin/announcements`, 'POST', payload);
                status.textContent = 'Posted.';
            }
            status.classList.remove('error');
            resetForm();
            await load();
        } catch (error) {
            status.textContent = error.message;
            status.classList.add('error');
        }
    });
    if (annCancelEditBtn) annCancelEditBtn.addEventListener('click', resetForm);
    await load();
}

async function communityBoardView() {
    app.innerHTML = '<h1 class="page-title">Community Board</h1><div class="loading"><i></i>Loading board</div>';

    try {
        const board = await get(`${API}/admin/community-board`);
        renderCommunityBoard(board);
    } catch (error) {
        app.innerHTML = `<h1 class="page-title">Community Board</h1><div class="card"><p class="form-status error">${esc(error.message)}</p></div>`;
    }
}

function renderCommunityBoard(board) {
    app.innerHTML = `
    <h1 class="page-title">Community Board</h1>
    <div class="card">
        <h2>Featured Player</h2>
        <label>Player ID<input type="number" id="fpId" value="${board.featuredPlayer?.id || ''}" placeholder="Leave empty to disable"></label>
        <br><br>
        <label>Title Override<input type="text" id="fpTitle" value="${esc(board.featuredPlayer?.titleOverride || '')}" placeholder="Custom title"></label>
        <br><br>
        <label>URL Override<input type="text" id="fpUrl" value="${esc(board.featuredPlayer?.urlOverride || '')}" placeholder="Custom URL"></label>
    </div>
    <div class="card">
        <h2>Featured Rooms</h2>
        <div id="featuredRoomsList"></div>
        <button class="btn" id="addFeaturedRoom">+ Add Room</button>
    </div>
    <div class="card">
        <h2>Instagram Images</h2>
        <div id="instagramList"></div>
        <button class="btn" id="addInstagram">+ Add Image</button>
    </div>
    <div class="card">
        <h2>Videos</h2>
        <div id="videosList"></div>
        <button class="btn" id="addVideo">+ Add Video</button>
    </div>
    <div class="card">
        <h2>Current Announcement</h2>
        <label>Message<input type="text" id="annMessage" value="${esc(board.currentAnnouncement?.message || '')}" placeholder="Announcement message"></label>
        <br><br>
        <label>More Info URL<input type="text" id="annUrl" value="${esc(board.currentAnnouncement?.moreInfoUrl || '')}" placeholder="Link for more info"></label>
    </div>
    <div class="card dev-block">
        <h2>Scheduled maintenance countdown</h2>
        <p class="field-hint">Pushes a live in-game maintenance notice. Set to 0 to clear it.</p>
        <label>Minutes until maintenance<input type="number" id="maintenanceMinutes" min="0" max="10080" value="0"></label>
        <br><br>
        <button class="btn danger" id="startMaintenance">Start countdown</button>
        <span class="form-status" id="maintenanceStatus"></span>
    </div>
    <button class="btn" id="saveBoard" style="margin-top:20px">Save All Changes</button>
    <button class="btn ghost" id="resetBoard" style="margin-top:20px;margin-left:10px">Reset to Server Data</button>
    <span class="form-status" id="boardStatus"></span>`;

    const fpId = document.querySelector('#fpId');
    const fpTitle = document.querySelector('#fpTitle');
    const fpUrl = document.querySelector('#fpUrl');
    const annMessage = document.querySelector('#annMessage');
    const annUrl = document.querySelector('#annUrl');

    let featuredRooms = [...(board.featuredRoomGroup?.featuredRooms || [])];
    let instagramImages = [...(board.instagramImages || [])];
    let videos = [...(board.videos || [])];

    function renderFeaturedRooms() {
        const container = document.querySelector('#featuredRoomsList');
        container.innerHTML = featuredRooms.map((room, i) => `
            <div class="room-row">
                <label>Room ID<input type="number" class="fr-roomId" value="${room.roomId || ''}" data-index="${i}" placeholder="Room ID"></label>
                <label>Room Name<input type="text" class="fr-roomName" value="${esc(room.roomName || '')}" data-index="${i}" placeholder="Room Name"></label>
                <label>Image Name<input type="text" class="fr-imageName" value="${esc(room.imageName || '')}" data-index="${i}" placeholder="Image Name"></label>
                <button class="btn small danger remove-fr" data-index="${i}">Remove</button>
            </div>
        `).join('');
        container.querySelectorAll('.remove-fr').forEach(btn => btn.addEventListener('click', () => {
            featuredRooms.splice(btn.dataset.index, 1);
            renderFeaturedRooms();
        }));
        container.querySelectorAll('input').forEach(input => input.addEventListener('change', (e) => {
            const idx = e.target.dataset.index;
            const field = e.target.classList.contains('fr-roomId') ? 'roomId' :
                          e.target.classList.contains('fr-roomName') ? 'roomName' : 'imageName';
            featuredRooms[idx][field] = e.target.value;
        }));
    }

    function renderInstagram() {
        const container = document.querySelector('#instagramList');
        container.innerHTML = instagramImages.map((img, i) => `
            <div class="insta-row" style="margin-bottom:10px;padding:10px;background:var(--panel-alt);border-radius:var(--radius)">
                <label>Image URL<input type="text" class="insta-url" value="${esc(img.imageUrl || '')}" data-index="${i}" placeholder="https://..." style="width:400px"></label>
                <button class="btn small danger remove-insta" data-index="${i}">Remove</button>
            </div>
        `).join('');
        container.querySelectorAll('.remove-insta').forEach(btn => btn.addEventListener('click', () => {
            instagramImages.splice(btn.dataset.index, 1);
            renderInstagram();
        }));
        container.querySelectorAll('input').forEach(input => input.addEventListener('change', (e) => {
            instagramImages[e.target.dataset.index].imageUrl = e.target.value;
        }));
    }

    function renderVideos() {
        const container = document.querySelector('#videosList');
        container.innerHTML = videos.map((vid, i) => `
            <div class="video-row" style="margin-bottom:10px;padding:10px;background:var(--panel-alt);border-radius:var(--radius)">
                <label>Title<input type="text" class="vid-title" value="${esc(vid.title || '')}" data-index="${i}" placeholder="Video title"></label>
                <label>Description<input type="text" class="vid-desc" value="${esc(vid.description || '')}" data-index="${i}" placeholder="Description"></label>
                <button class="btn small danger remove-vid" data-index="${i}">Remove</button>
            </div>
        `).join('');
        container.querySelectorAll('.remove-vid').forEach(btn => btn.addEventListener('click', () => {
            videos.splice(btn.dataset.index, 1);
            renderVideos();
        }));
        container.querySelectorAll('input').forEach(input => input.addEventListener('change', (e) => {
            const idx = e.target.dataset.index;
            const field = e.target.classList.contains('vid-title') ? 'title' : 'description';
            videos[idx][field] = e.target.value;
        }));
    }

    renderFeaturedRooms();
    renderInstagram();
    renderVideos();

    const addFeaturedRoomBtn = document.querySelector('#addFeaturedRoom');
    if (addFeaturedRoomBtn) addFeaturedRoomBtn.addEventListener('click', () => {
        featuredRooms.push({ roomId: '', roomName: '', imageName: '' });
        renderFeaturedRooms();
    });

    const addInstagramBtn = document.querySelector('#addInstagram');
    if (addInstagramBtn) addInstagramBtn.addEventListener('click', () => {
        instagramImages.push({ imageUrl: '' });
        renderInstagram();
    });

    const addVideoBtn = document.querySelector('#addVideo');
    if (addVideoBtn) addVideoBtn.addEventListener('click', () => {
        videos.push({ title: '', description: '' });
        renderVideos();
    });

    applyRoleGating();

    const startMaintenanceBtn = document.querySelector('#startMaintenance');
    if (startMaintenanceBtn) startMaintenanceBtn.addEventListener('click', async () => {
        const status = document.querySelector('#maintenanceStatus');
        const minutes = parseInt(document.querySelector('#maintenanceMinutes').value) || 0;
        if (minutes > 0 && !confirm(`Push a live ${minutes}-minute maintenance countdown to every player in-game?`)) return;
        try {
            const r = await send(`${API}/admin/maintenance`, 'POST', { minutes });
            status.textContent = r.message;
            status.classList.remove('error');
        } catch (error) {
            status.textContent = error.message;
            status.classList.add('error');
        }
    });

    const saveBoardBtn = document.querySelector('#saveBoard');
    if (saveBoardBtn) saveBoardBtn.addEventListener('click', async () => {
        const status = document.querySelector('#boardStatus');
        if (!status) return;

        const newBoard = {
            featuredPlayer: fpId.value ? { id: Number(fpId.value), titleOverride: fpTitle.value, urlOverride: fpUrl.value } : null,
            featuredRoomGroup: featuredRooms.length ? { featuredRooms } : null,
            instagramImages: instagramImages,
            videos: videos,
            currentAnnouncement: annMessage.value ? { message: annMessage.value, moreInfoUrl: annUrl.value } : null
        };

        status.textContent = 'Saving...';
        status.classList.remove('error');
        try {
            await send(`${API}/admin/community-board`, 'PUT', newBoard);
            status.textContent = 'Saved successfully.';
            status.classList.remove('error');
        } catch (error) {
            status.textContent = error.message;
            status.classList.add('error');
        }
    });

    const resetBoardBtn = document.querySelector('#resetBoard');
    if (resetBoardBtn) resetBoardBtn.addEventListener('click', async () => {
        const status = document.querySelector('#boardStatus');
        if (!status) return;

        status.textContent = 'Reloading...';
        status.classList.remove('error');
        try {
            const freshBoard = await get(`${API}/admin/community-board`);
            renderCommunityBoard(freshBoard);
            status.textContent = 'Reset to server data.';
        } catch (error) {
            status.textContent = error.message;
            status.classList.add('error');
        }
    });
}

async function clubsView() {
    app.innerHTML = `<h1 class="page-title">Clubs</h1><div class="search-row"><input id="clubSearch" placeholder="Search clubs"><button class="btn" id="clubSearchGo">Search</button></div><div id="clubListResults" class="player-list"></div>`;
    const input = document.querySelector('#clubSearch'), results = document.querySelector('#clubListResults');
    async function loadClubs() {
        results.innerHTML = '<div class="loading"><i></i>Loading clubs</div>';
        try {
            const data = await get(`${API}/admin/clubs?search=${encodeURIComponent(input.value.trim())}`);
            if (!data.clubs.length) { results.innerHTML = '<p class="field-hint">No clubs found.</p>'; return; }
            results.innerHTML = data.clubs.map(c => `
                <a class="player-row" href="#clubs/${c.clubId}">
                    <div class="meta">
                        <div class="name">${esc(c.name)}</div>
                        <div class="sub">ID ${c.clubId} · ${c.memberCount} members · ${c.visibility}</div>
                    </div>
                </a>
            `).join('');
        } catch (error) {
            results.innerHTML = `<p class="form-status error">${esc(error.message)}</p>`;
        }
    }
    const clubSearchGo = document.querySelector('#clubSearchGo');
    if (clubSearchGo) clubSearchGo.addEventListener('click', loadClubs);
    if (input) input.addEventListener('keydown', e => { if (e.key === 'Enter') loadClubs(); });
    await loadClubs();
}

async function clubDetail(clubIdRaw) {
    const clubId = Number(clubIdRaw);
    app.innerHTML = '<div class="loading"><i></i>Loading club</div>';
    let club;
    try { club = await get(`${API}/admin/clubs/${clubId}`); }
    catch (error) { app.innerHTML = `<a class="back-link" href="#clubs">&larr; Back to Clubs</a><div class="card"><p class="form-status error">${esc(error.message)}</p></div>`; return; }

    const summary = club.summary;
    app.innerHTML = `
    <a class="back-link" href="#clubs">&larr; Back to Clubs</a>
    <div class="profile-header">
        <div>
            <div class="name">${esc(summary.name)}</div>
            <div class="username-sub">${esc(summary.description || '(no description)')}</div>
            <div class="stat-line">ID: ${summary.clubId} · ${summary.memberCount} members</div>
            <div class="stat-line">State: ${summary.state} · Visibility: ${summary.visibility}</div>
            <div class="stat-line">Creator: ${summary.creatorAccountId}</div>
            <div class="profile-actions">
                <button class="btn" data-act="edit">Edit Club</button>
                <a class="btn ghost" href="#clubs">Back</a>
            </div>
        </div>
    </div>
    <div class="tabs">
        <div class="tab active" data-tab="members">Members</div>
    </div>
    <div id="tabBody"></div>`;

    document.querySelectorAll('.tab').forEach(tab => tab.addEventListener('click', () => {
        document.querySelectorAll('.tab').forEach(t => t.classList.toggle('active', t === tab));
        renderClubTab(tab.dataset.tab, clubId);
    }));
    renderClubTab('members', clubId);

    const editBtn = document.querySelector('[data-act="edit"]');
    if (editBtn) editBtn.addEventListener('click', async () => {
        const name = prompt('Club name:', summary.name);
        if (name === null) return;
        const description = prompt('Description:', summary.description || '');
        if (description === null) return;
        const state = prompt('State (Active/MarkedForDelete):', summary.state);
        if (state === null) return;
        const visibility = prompt('Visibility (Public/Private):', summary.visibility);
        if (visibility === null) return;
        try {
            await send(`${API}/admin/clubs/${clubId}`, 'PUT', { name, description, state, visibility });
            await clubDetail(clubIdRaw);
        } catch (error) { alert(error.message); }
    });
}

async function renderClubTab(tab, clubId) {
    const body = document.querySelector('#tabBody');
    if (tab === 'members') {
        body.innerHTML = `<h2 class="page-title" style="font-size:16px">Members</h2><div id="membersList"><div class="loading"><i></i>Loading members</div></div>`;
        try {
            const members = await get(`${API}/admin/clubs/${clubId}/members`);
            if (!members.length) { body.innerHTML = '<p class="field-hint">No members.</p>'; return; }
            body.innerHTML = `<table><thead><tr><th>Account ID</th><th>Username</th><th>Display Name</th><th>Role</th><th>Actions</th></tr></thead><tbody>${members.map(m => `
                <tr data-account="${m.accountId}">
                    <td>${m.accountId}</td>
                    <td>${esc(m.username || 'N/A')}</td>
                    <td>${esc(m.displayName || 'N/A')}</td>
                    <td>${esc(m.membershipType)}</td>
                    <td class="actions-cell"><button class="btn small set-role">Set Role</button><button class="btn small danger remove-member">Remove</button></td>
                </tr>
            `).join('')}</tbody></table>`;
            body.querySelectorAll('.set-role').forEach(btn => btn.addEventListener('click', async () => {
                const row = btn.closest('tr'), accountId = row.dataset.account;
                const role = prompt('Membership type (Creator/Admin/Officer/Member):');
                if (!role) return;
                try { await send(`${API}/admin/clubs/${clubId}/members/${accountId}`, 'POST', { membershipType: role }); await renderClubTab('members', clubId); }
                catch (error) { alert(error.message); }
            }));
            body.querySelectorAll('.remove-member').forEach(btn => btn.addEventListener('click', async () => {
                const row = btn.closest('tr'), accountId = row.dataset.account;
                if (!confirm('Remove this member?')) return;
                try { await send(`${API}/admin/clubs/${clubId}/members/${accountId}`, 'DELETE'); await renderClubTab('members', clubId); }
                catch (error) { alert(error.message); }
            }));
        } catch (error) {
            body.innerHTML = `<p class="form-status error">${esc(error.message)}</p>`;
        }
    }
}

const GIFT_CONTEXT_DEFS = [['None', -1], ['Default'], ['First_Activity'], ['Game_Drop'], ['All_Daily_Challenges_Complete'], ['All_Weekly_Challenge_Complete'], ['Daily_Challenge_Complete'], ['Weekly_Challenge_Complete'], ['Unassigned_Equipment', 10], ['Unassigned_Avatar'], ['Unassigned_Consumable'], ['Reacquisition', 20], ['Membership'], ['NUX_TokensAndDressUp', 30], ['NUX_Experiment1'], ['NUX_Experiment2'], ['NUX_Experiment3'], ['NUX_Experiment4'], ['NUX_Experiment5'], ['GameRewards', 50], ['GameRewards_Tokens'], ['LevelUp', 100], ['Purchased_Gift_A', 500], ['Purchased_Gift_B'], ['Purchased_Gift_C'], ['Purchased_Gift_D'], ['Holiday', 1000], ['Contest'], ['Promotion'], ['SubscribersOnly'], ['Deprecated', 1100], ['RecRoyale', 1200], ['DEPRECATED_Paintball_ClearCut', 2000], ['DEPRECATED_Paintball_Homestead'], ['DEPRECATED_Paintball_Quarry'], ['DEPRECATED_Paintball_River'], ['DEPRECATED_Paintball_Dam'], ['DEPRECATED_Paintball_DriveIn'], ['Paintball_ClearCut', 2010], ['Paintball_Homestead'], ['Paintball_Quarry'], ['Paintball_River'], ['Paintball_Dam'], ['Paintball_DriveIn'], ['DEPRECATED_Discgolf_Propulsion', 3000], ['DEPRECATED_Discgolf_Lake'], ['Discgolf_Propulsion', 3010], ['Discgolf_Lake'], ['Discgolf_Mode_CoopCatch', 3500], ['Quest_Goblin_A', 4000], ['Quest_Goblin_B'], ['Quest_Goblin_C'], ['Quest_Goblin_S'], ['Quest_Goblin_Consumable'], ['Quest_Cauldron_A', 4010], ['Quest_Cauldron_B'], ['Quest_Cauldron_C'], ['Quest_Cauldron_S'], ['Quest_Cauldron_Consumable'], ['Quest_Pirate1_A', 4100], ['Quest_Pirate1_B'], ['Quest_Pirate1_C'], ['Quest_Pirate1_S'], ['Quest_Pirate1_X'], ['Quest_Pirate1_Consumable'], ['Quest_Dracula1_A', 4200], ['Quest_Dracula1_B'], ['Quest_Dracula1_C'], ['Quest_Dracula1_S'], ['Quest_Dracula1_X'], ['Quest_Dracula1_Consumable'], ['Quest_Dracula1_SS'], ['Quest_SciFi_A', 4500], ['Quest_SciFi_B'], ['Quest_SciFi_C'], ['Quest_SciFi_S'], ['Quest_Scifi_Consumable'], ['DEPRECATED_Charades', 5000], ['Charades'], ['DEPRECATED_Soccer', 6000], ['Soccer'], ['DEPRECATED_Paddleball', 7000], ['Paddleball'], ['DEPRECATED_Dodgeball', 8000], ['Dodgeball'], ['DEPRECATED_Lasertag', 9000], ['Lasertag'], ['DEPRECATED_Bowling', 10000], ['Bowling'], ['StuntRunner_TheMainEvent_A', 11000], ['StuntRunner_TheMainEvent_B'], ['StuntRunner_TheMainEvent_C'], ['StuntRunner_TheMainEvent_D'], ['StuntRunner_TheMainEvent_S'], ['StuntRunner_TheMainEvent_X'], ['StuntRunner_TheMainEvent_Consumable'], ['StuntRunner_TheMainEvent_SS'], ['Store_LaserTag', 100000], ['Store_RecCenter', 100010], ['Consumable', 110000], ['Token', 110100], ['Punchcard_Challenge_Complete', 110200], ['All_Punchcard_Challenges_Complete'], ['Commerce_Purchase', 200000]];
const GIFT_CONTEXTS = (() => { let next = 0; return GIFT_CONTEXT_DEFS.map(([name, value]) => { const v = value !== undefined ? value : next; next = v + 1; return { name, value: v }; }); })();

async function coachGiftingView() {
    app.innerHTML = `
    <h1 class="page-title">Coach Gifting</h1>
    <div class="card">
        <h2>Send Gift</h2>
        <label>Gift Type<select id="giftType">
            <option value="avatar">Avatar Item</option>
            <option value="equipment">Equipment</option>
            <option value="consumable">Consumable</option>
            <option value="tokens">Tokens</option>
            <option value="xp">XP</option>
            <option value="box">Mystery Box</option>
        </select></label>
        <br><br>
        <label>Recipient Account IDs (comma-separated, or leave empty to send to all)<input type="text" id="recipientIds" placeholder="e.g. 123,456,789"></label>
        <br><br>
        <label id="skuLabel">SKU ID<input type="number" id="skuId" placeholder="Search catalog below"></label>
        <br><br>
        <label id="amountLabel">Amount<input type="number" id="amount" value="1"></label>
        <br><br>
        <label id="boxRarityLabel">Box Rarity (10=Common, 20=Uncommon, 30=Rare, 40=Epic, 50=Legendary)<select id="boxRarity">
            <option value="10">Common (10)</option>
            <option value="20">Uncommon (20)</option>
            <option value="30">Rare (30)</option>
            <option value="40">Epic (40)</option>
            <option value="50">Legendary (50)</option>
        </select></label>
        <br><br>
        <label>Box Design<select id="boxDesign"><option value="2" selected>Normal</option><option value="110000">Friendotron</option><option value="custom">Custom...</option></select></label>
        <input type="number" id="boxDesignCustom" placeholder="Custom box design ID" hidden style="margin-top:6px">
        <br><br>
        <label>Message (optional)<input type="text" id="giftMessage" placeholder="Gift message"></label>
        <br><br>
        <label><input type="checkbox" id="sendToAll"> Send to all players</label>
        <br><br>
        <label><input type="checkbox" id="onlineOnly"> Online only</label>
        <br><br>
        <button class="btn" id="sendGift">Send Gift</button>
        <span class="form-status" id="giftStatus"></span>
    </div>
    <div class="card">
        <h2>Gift Catalog</h2>
        <div class="search-row">
            <input id="catalogSearch" placeholder="Search catalog">
            <select id="catalogType">
                <option value="">All Types</option>
                <option value="avatar">Avatar</option>
                <option value="equipment">Equipment</option>
                <option value="consumable">Consumable</option>
            </select>
            <button class="btn" id="searchCatalog">Search</button>
        </div>
        <div id="catalogResults"></div>
        <div class="search-row" style="margin-top:10px;margin-bottom:0">
            <button class="btn ghost small" id="catalogPrev">&larr; Prev</button>
            <span class="field-hint" id="catalogPageInfo" style="align-self:center"></span>
            <button class="btn ghost small" id="catalogNext">Next &rarr;</button>
        </div>
    </div>`;

    const giftType = document.querySelector('#giftType');
    const skuLabel = document.querySelector('#skuLabel');
    const amountLabel = document.querySelector('#amountLabel');
    const boxRarityLabel = document.querySelector('#boxRarityLabel');
    const skuId = document.querySelector('#skuId');
    const amount = document.querySelector('#amount');
    const boxRarity = document.querySelector('#boxRarity');
    const boxDesign = document.querySelector('#boxDesign');
    const boxDesignCustom = document.querySelector('#boxDesignCustom');

    (function populateGiftContexts() {
        const customOption = boxDesign.querySelector('option[value="custom"]');
        for (const { name, value } of GIFT_CONTEXTS) {
            if (value === 2 || value === 110000) continue;
            const opt = document.createElement('option');
            opt.value = String(value);
            opt.textContent = `${name} (${value})`;
            boxDesign.insertBefore(opt, customOption);
        }
    })();

    function resolveBoxDesign() {
        return boxDesign.value === 'custom' ? Number(boxDesignCustom.value || 0) : Number(boxDesign.value);
    }

    function updateFormVisibility() {
        const type = giftType.value;
        skuLabel.style.display = (type === 'avatar' || type === 'equipment' || type === 'consumable') ? '' : 'none';
        amountLabel.style.display = (type === 'tokens' || type === 'xp' || type === 'consumable') ? '' : 'none';
        boxRarityLabel.style.display = type === 'box' ? '' : 'none';
        boxDesign.parentElement.style.display = type === 'box' ? '' : 'none';
        boxDesignCustom.hidden = !(type === 'box' && boxDesign.value === 'custom');
    }
    if (giftType) giftType.addEventListener('change', updateFormVisibility);
    if (boxDesign) boxDesign.addEventListener('change', updateFormVisibility);
    updateFormVisibility();

    let catalogSkip = 0;
    const catalogTake = 20;
    async function loadCatalog() {
        const catalogSearch = document.querySelector('#catalogSearch');
        const catalogType = document.querySelector('#catalogType');
        const results = document.querySelector('#catalogResults');
        const pageInfo = document.querySelector('#catalogPageInfo');
        const prevBtn = document.querySelector('#catalogPrev');
        const nextBtn = document.querySelector('#catalogNext');
        if (!catalogSearch || !catalogType || !results) return;

        const search = catalogSearch.value;
        const type = catalogType.value;
        results.innerHTML = '<div class="loading"><i></i>Loading catalog</div>';
        try {
            const data = await get(`${API}/admin/gifts/catalog?search=${encodeURIComponent(search)}&type=${type}&skip=${catalogSkip}&take=${catalogTake}`);
            const items = data.results ?? data;
            const total = data.total ?? items.length;
            if (!items.length) { results.innerHTML = '<p class="field-hint">No items found.</p>'; }
            else {
                results.innerHTML = items.map(item => `
                    <div class="room-row clickable" data-sku="${item.skuId}">
                        <div class="meta"><strong>${esc(item.friendlyName)}</strong><br><small>SKU ${item.skuId} · ${item.type} ${item.avatarItemId ? `· Avatar #${item.avatarItemId}` : ''}</small></div>
                        <button class="btn small select-sku">Select</button>
                    </div>
                `).join('');
                results.querySelectorAll('.select-sku').forEach(btn => btn.addEventListener('click', () => {
                    skuId.value = btn.closest('[data-sku]').dataset.sku;
                }));
            }
            if (pageInfo) pageInfo.textContent = total ? `${catalogSkip + 1}-${Math.min(catalogSkip + catalogTake, total)} of ${total}` : '';
            if (prevBtn) prevBtn.disabled = catalogSkip <= 0;
            if (nextBtn) nextBtn.disabled = catalogSkip + catalogTake >= total;
        } catch (error) {
            results.innerHTML = `<p class="form-status error">${esc(error.message)}</p>`;
        }
    }
    const searchCatalogBtn = document.querySelector('#searchCatalog');
    if (searchCatalogBtn) searchCatalogBtn.addEventListener('click', () => { catalogSkip = 0; loadCatalog(); });
    const catalogSearch = document.querySelector('#catalogSearch');
    if (catalogSearch) catalogSearch.addEventListener('keydown', e => { if (e.key === 'Enter') { catalogSkip = 0; loadCatalog(); } });
    const catalogPrevBtn = document.querySelector('#catalogPrev');
    if (catalogPrevBtn) catalogPrevBtn.addEventListener('click', () => { catalogSkip = Math.max(0, catalogSkip - catalogTake); loadCatalog(); });
    const catalogNextBtn = document.querySelector('#catalogNext');
    if (catalogNextBtn) catalogNextBtn.addEventListener('click', () => { catalogSkip += catalogTake; loadCatalog(); });
    loadCatalog();

    const sendGiftBtn = document.querySelector('#sendGift');
    if (sendGiftBtn) sendGiftBtn.addEventListener('click', async () => {
        const status = document.querySelector('#giftStatus');
        const recipientIds = document.querySelector('#recipientIds');
        const sendToAll = document.querySelector('#sendToAll');
        const onlineOnly = document.querySelector('#onlineOnly');
        const giftMessage = document.querySelector('#giftMessage');

        if (!status || !recipientIds || !sendToAll || !onlineOnly || !giftMessage) return;

        const recipientAccountIds = recipientIds.value ? recipientIds.value.split(',').map(id => Number(id.trim())).filter(id => id > 0) : [];
        const request = {
            GiftType: giftType.value,
            RecipientAccountIds: recipientAccountIds,
            SendToAll: sendToAll.checked,
            OnlineOnly: onlineOnly.checked,
            Message: giftMessage.value
        };

        if (giftType.value === 'avatar' || giftType.value === 'equipment' || giftType.value === 'consumable') {
            request.SkuId = Number(skuId.value) || 0;
            if (giftType.value === 'consumable') request.Amount = Number(amount.value) || 1;
        } else if (giftType.value === 'tokens' || giftType.value === 'xp') {
            request.Amount = Number(amount.value) || 0;
        } else if (giftType.value === 'box') {
            request.BoxRarity = Number(boxRarity.value) || 10;
            request.BoxDesign = resolveBoxDesign() || 2;
        }

        status.textContent = 'Sending...';
        status.classList.remove('error');
        try {
            const result = await send(`${API}/admin/gifts`, 'POST', request);
            status.textContent = `Gift sent to ${result.queued} players.`;
            status.classList.remove('error');
        } catch (error) {
            status.textContent = error.message;
            status.classList.add('error');
        }
    });
}

async function configView() {
    app.innerHTML = `
    <h1 class="page-title">Server Security</h1>
    <div class="card">
        <h2>Access Control</h2>
        <label><input type="checkbox" id="accountCreationEnabled"> Allow all account creation</label>
        <p class="field-hint">Master switch for every account-creation path, including the admin form.</p>
        <br><br>
        <label><input type="checkbox" id="signupEnabled"> Allow public RecNet signup</label>
        <p class="field-hint">Controls only the website registration form and is also limited by the master switch.</p>
        <br><br>
        <label><input type="checkbox" id="vpnBlockingEnabled"> Block VPNs, proxies, Tor, and hosting IPs</label>
        <p class="field-hint">Checks gameplay and account endpoints. Results are cached and provider outages fail open.</p>
        <p class="field-hint" id="proxyCheckHint"></p>
        <br><br>
        <span class="form-status" id="configStatus"></span>
    </div>`;

    const accountCreationToggle = document.querySelector('#accountCreationEnabled');
    const signupToggle = document.querySelector('#signupEnabled');
    const vpnToggle = document.querySelector('#vpnBlockingEnabled');
    const proxyCheckHint = document.querySelector('#proxyCheckHint');
    const status = document.querySelector('#configStatus');

    async function loadSettings() {
        try {
            const settings = await get(`${API}/admin/settings`);
            accountCreationToggle.checked = !!settings.accountCreationEnabled;
            signupToggle.checked = !!settings.recNetSignupEnabled;
            vpnToggle.checked = !!settings.vpnBlockingEnabled;
            proxyCheckHint.textContent = `Proxy-check API key: ${settings.proxyCheckConfigured ? 'configured' : 'not configured (set in server config file)'}`;
            status.textContent = `Account creation ${settings.accountCreationEnabled ? 'enabled' : 'disabled'} · RecNet signup ${settings.recNetSignupEnabled ? 'enabled' : 'disabled'} · VPN blocking ${settings.vpnBlockingEnabled ? 'enabled' : 'disabled'}`;
        } catch (error) {
            status.textContent = error.message;
            status.classList.add('error');
        }
    }

    async function saveSettings() {
        try {
            accountCreationToggle.disabled = signupToggle.disabled = vpnToggle.disabled = true;
            status.textContent = 'Saving...';
            status.classList.remove('error');
            await send(`${API}/admin/settings`, 'PUT', {
                accountCreationEnabled: accountCreationToggle.checked,
                recNetSignupEnabled: signupToggle.checked,
                vpnBlockingEnabled: vpnToggle.checked
            });
            status.textContent = 'Settings saved.';
        } catch (error) {
            status.textContent = error.message;
            status.classList.add('error');
        } finally {
            accountCreationToggle.disabled = signupToggle.disabled = vpnToggle.disabled = false;
        }
    }

    accountCreationToggle.addEventListener('change', saveSettings);
    signupToggle.addEventListener('change', saveSettings);
    vpnToggle.addEventListener('change', saveSettings);
    await loadSettings();
}

async function accountCreationView() {
    await configView();
}

async function avatarItemsView() {
    app.innerHTML = `
    <h1 class="page-title">Avatar Items</h1>
    <div class="card">
        <div style="margin-bottom:15px">
            <label><input type="radio" name="avatarMode" value="simple" checked> Simple Mode</label>
            <label style="margin-left:20px"><input type="radio" name="avatarMode" value="advanced"> Advanced Mode</label>
        </div>
        <div id="simpleMode">
            <label>Upload JSON file<input type="file" id="avatarJsonFile" accept=".json"></label>
            <p class="field-hint">Upload a JSON file containing the avatar item definition.</p>
        </div>
        <div id="advancedMode" style="display:none">
            <label>Avatar Item JSON<textarea id="avatarJsonText" rows="15" placeholder='{
  "AvatarItemDesc": "05b7af56-71f2-45ba-a377-c105bf6a6f7a,4lwF00Rvb0Kr3FJz07HlOQ",
  "AvatarItemType": 0,
  "PlatformMask": -1,
  "FriendlyName": "Werewolf Wrist (Gilded)",
  "Tooltip": "",
  "Rarity": 0,
  "TagList": null,
  "AvatarItemId": 9796,
  "IsBaseAvatarItem": false,
  "CreatedAt": "2026-08-16T00:00:00.000Z",
  "ThumbnailImage": null
}'></textarea></label>
        </div>
        <br>
        <button class="btn" id="uploadAvatarItem">Upload Avatar Item</button>
        <span class="form-status" id="avatarStatus"></span>
    </div>
    <div class="card">
        <h2>Browse Avatar Items</h2>
        <div class="search-row"><input id="avatarBrowseSearch" placeholder="Search name, SKU, or avatar item ID"><button class="btn" id="avatarBrowseSearchGo">Search</button></div>
        <div id="avatarBrowseResults"><p class="field-hint">Search to browse the avatar item catalog.</p></div>
        <div class="search-row" id="avatarBrowsePager" style="margin-top:10px;margin-bottom:0;display:none">
            <button class="btn ghost small" id="avatarBrowsePrev">&larr; Prev</button>
            <span class="field-hint" id="avatarBrowsePageInfo" style="align-self:center"></span>
            <button class="btn ghost small" id="avatarBrowseNext">Next &rarr;</button>
        </div>
    </div>`;

    let avatarBrowseSkip = 0;
    const avatarBrowseTake = 20;
    async function runAvatarBrowse() {
        const searchInput = document.querySelector('#avatarBrowseSearch');
        const results = document.querySelector('#avatarBrowseResults');
        const pager = document.querySelector('#avatarBrowsePager');
        const query = searchInput.value.trim();
        results.innerHTML = '<div class="loading"><i></i>Loading</div>';
        try {
            const data = await get(`${API}/admin/gifts/catalog?search=${encodeURIComponent(query)}&type=avatar&skip=${avatarBrowseSkip}&take=${avatarBrowseTake}`);
            const items = data.results ?? data;
            const total = data.total ?? items.length;
            results.innerHTML = items.length ? items.map(item => `
                <div class="room-row">
                    ${item.thumbnailImage ? `<img src="${esc(item.thumbnailImage)}" alt="">` : ''}
                    <div class="meta"><strong>${esc(item.friendlyName)}</strong><br><small>SKU ${item.skuId} &middot; Avatar #${item.avatarItemId} &middot; Rarity ${item.rarity}</small></div>
                </div>
            `).join('') : '<p class="field-hint">No items found.</p>';
            pager.style.display = total > avatarBrowseTake ? 'flex' : 'none';
            document.querySelector('#avatarBrowsePageInfo').textContent = total ? `${avatarBrowseSkip + 1}-${Math.min(avatarBrowseSkip + avatarBrowseTake, total)} of ${total}` : '';
            document.querySelector('#avatarBrowsePrev').disabled = avatarBrowseSkip <= 0;
            document.querySelector('#avatarBrowseNext').disabled = avatarBrowseSkip + avatarBrowseTake >= total;
        } catch (error) {
            results.innerHTML = `<p class="form-status error">${esc(error.message)}</p>`;
        }
    }
    const avatarBrowseSearchGo = document.querySelector('#avatarBrowseSearchGo');
    if (avatarBrowseSearchGo) avatarBrowseSearchGo.addEventListener('click', () => { avatarBrowseSkip = 0; runAvatarBrowse(); });
    const avatarBrowseSearchInput = document.querySelector('#avatarBrowseSearch');
    if (avatarBrowseSearchInput) avatarBrowseSearchInput.addEventListener('keydown', e => { if (e.key === 'Enter') { avatarBrowseSkip = 0; runAvatarBrowse(); } });
    document.querySelector('#avatarBrowsePrev').addEventListener('click', () => { avatarBrowseSkip = Math.max(0, avatarBrowseSkip - avatarBrowseTake); runAvatarBrowse(); });
    document.querySelector('#avatarBrowseNext').addEventListener('click', () => { avatarBrowseSkip += avatarBrowseTake; runAvatarBrowse(); });

    const simpleMode = document.querySelector('#simpleMode');
    const advancedMode = document.querySelector('#advancedMode');
    const fileInput = document.querySelector('#avatarJsonFile');
    const textArea = document.querySelector('#avatarJsonText');
    const status = document.querySelector('#avatarStatus');

    document.querySelectorAll('input[name="avatarMode"]').forEach(radio => {
        radio.addEventListener('change', () => {
            simpleMode.style.display = radio.value === 'simple' ? '' : 'none';
            advancedMode.style.display = radio.value === 'advanced' ? '' : 'none';
        });
    });

    if (fileInput) fileInput.addEventListener('change', () => {
        const file = fileInput.files[0];
        if (!file) return;
        const reader = new FileReader();
        reader.onload = (e) => {
            const avatarTextArea = document.querySelector('#avatarJsonText');
            if (avatarTextArea) avatarTextArea.value = e.target.result;
        };
        reader.readAsText(file);
    });

    const uploadAvatarItemBtn = document.querySelector('#uploadAvatarItem');
    if (uploadAvatarItemBtn) uploadAvatarItemBtn.addEventListener('click', async () => {
        let json;
        try {
            const simpleMode = document.querySelector('#simpleMode');
            const advancedMode = document.querySelector('#advancedMode');
            const avatarFileInput = document.querySelector('#avatarJsonFile');
            const avatarTextArea = document.querySelector('#avatarJsonText');

            if (!simpleMode || !advancedMode || !avatarFileInput || !avatarTextArea) return;

            if (simpleMode.style.display !== 'none') {
                const file = avatarFileInput.files[0];
                if (!file) { alert('Please select a JSON file.'); return; }
                const text = await file.text();
                json = JSON.parse(text);
            } else {
                const text = avatarTextArea.value.trim();
                if (!text) { alert('Please enter JSON.'); return; }
                json = JSON.parse(text);
            }
        } catch (error) {
            alert('Invalid JSON: ' + error.message);
            return;
        }

        try {
            const status = document.querySelector('#avatarStatus');
            if (!status) return;

            status.textContent = 'Uploading...';
            status.classList.remove('error');
            const result = await send(`${API}/admin/avatar-items`, 'POST', json);
            status.textContent = result.message || 'Avatar item uploaded successfully.';
        } catch (error) {
            const status = document.querySelector('#avatarStatus');
            if (status) {
                status.textContent = error.message;
                status.classList.add('error');
            }
        }
    });
}

async function preferencesView() {
    app.innerHTML = `<h1 class="page-title">Preferences</h1><div class="card"><div class="loading"><i></i>Loading preferences</div></div>`;

    try {
        const prefs = await get(`${API}/admin/preferences`);
        app.innerHTML = `
        <h1 class="page-title">Preferences</h1>
        <div class="card">
            <h2>Appearance</h2>
            <label>Theme<select id="themeSelect">
                <option value="dark" ${prefs.theme === 'dark' ? 'selected' : ''}>Dark</option>
                <option value="light" ${prefs.theme === 'light' ? 'selected' : ''}>Light</option>
            </select></label>
            <br><br>
            <label>Accent Color<input type="color" id="accentColor" value="${prefs.accentColor || '#7c3aed'}"></label>
            <br><br>
            <button class="btn" id="savePrefs">Save Preferences</button>
            <span class="form-status" id="prefsStatus"></span>
        </div>`;

    const savePrefsBtn = document.querySelector('#savePrefs');
    if (savePrefsBtn) savePrefsBtn.addEventListener('click', async () => {
        const status = document.querySelector('#prefsStatus');
        const themeSelect = document.querySelector('#themeSelect');
        const accentColor = document.querySelector('#accentColor');

        if (!status || !themeSelect || !accentColor) return;

        status.textContent = 'Saving...';
        status.classList.remove('error');
        try {
            await send(`${API}/admin/preferences`, 'PUT', {
                theme: themeSelect.value,
                accentColor: accentColor.value
            });
            status.textContent = 'Saved successfully.';
            applyPreferences({
                theme: themeSelect.value,
                accentColor: accentColor.value
            });
        } catch (error) {
            status.textContent = error.message;
            status.classList.add('error');
        }
    });

        applyPreferences(prefs);
    } catch (error) {
        app.innerHTML = `<h1 class="page-title">Preferences</h1><div class="card"><p class="form-status error">${esc(error.message)}</p></div>`;
    }
}

function applyPreferences(prefs) {
    if (!prefs) return;
    const root = document.documentElement;
    if (prefs.theme === 'light') {
        root.style.setProperty('--bg', '#f5f5f5');
        root.style.setProperty('--panel', '#ffffff');
        root.style.setProperty('--panel-alt', '#f0f0f0');
        root.style.setProperty('--text', '#1a1a1a');
        root.style.setProperty('--text-dim', '#666666');
        root.style.setProperty('--border', '#e0e0e0');
    } else {

        root.style.removeProperty('--bg');
        root.style.removeProperty('--panel');
        root.style.removeProperty('--panel-alt');
        root.style.removeProperty('--text');
        root.style.removeProperty('--text-dim');
        root.style.removeProperty('--border');
    }
    if (prefs.accentColor) {
        root.style.setProperty('--primary', prefs.accentColor);
    }
}

async function playerReportsView() {
    app.innerHTML = `
    <h1 class="page-title">Player Reports</h1>
    <div class="card">
        <label>Status
            <select id="reportStatusFilter">
                <option value="Pending">Pending</option>
                <option value="Banned">Banned</option>
                <option value="TimedOut">Timed Out</option>
                <option value="NoAction">No Action</option>
                <option value="all">All</option>
            </select>
        </label>
    </div>
    <div class="card">
        <div id="playerReportList"><div class="loading"><i></i>Loading reports</div></div>
    </div>`;

    const statusFilter = document.querySelector('#reportStatusFilter');
    const list = document.querySelector('#playerReportList');

    async function resolveReport(id, action) {
        const body = { action };
        if (action === 'noaction') {
            body.reason = prompt('Optional note:') || '';
        } else if (action === 'ban') {
            const reason = prompt('Ban reason:');
            if (!reason) return;
            body.reason = reason;
            const durationUnit = (prompt('Duration unit (seconds/minutes/hours/days/weeks/permanent):', 'permanent') || 'permanent').trim().toLowerCase();
            body.durationUnit = durationUnit;
            if (durationUnit !== 'permanent') {
                body.durationAmount = Number(prompt('Duration amount:', '1') || '1');
            }
        } else {
            const reason = prompt('Timeout reason:');
            if (!reason) return;
            body.reason = reason;
            body.durationUnit = (prompt('Timeout unit (seconds/minutes/hours/days), capped at 30 days:', 'hours') || 'hours').trim().toLowerCase();
            body.durationAmount = Number(prompt('Timeout amount:', '24') || '24');
        }
        try {
            await send(`${API}/admin/reports/players/${encodeURIComponent(id)}/resolve`, 'POST', body);
            await loadReports();
        } catch (error) { alert(error.message); }
    }

    async function loadReports() {
        list.innerHTML = '<div class="loading"><i></i>Loading reports</div>';
        try {
            const reports = await get(`${API}/admin/reports/players?status=${encodeURIComponent(statusFilter.value)}`);
            if (!reports.length) { list.innerHTML = '<p class="field-hint">No reports here.</p>'; return; }
            list.innerHTML = reports.map(r => `
                <div class="room-row">
                    <div style="flex:1">
                        <strong>${esc(r.reportedUsername || (r.reportedPlayerId ? `Account #${r.reportedPlayerId}` : 'Unknown player'))}</strong>
                        <span class="badge">${esc(r.status)}</span>
                        <br><small>Reported by ${esc(r.reporterUsername || `Account #${r.reporterId}`)} on ${new Date(r.createdAt).toLocaleString()}</small>
                        ${r.reportCategory != null ? `<br><small>Category #${esc(r.reportCategory)}</small>` : ''}
                        ${r.details ? `<br><small>${esc(r.details)}</small>` : ''}
                        ${r.roomId ? `<br><small>Room ${esc(r.roomId)}${r.roomInstanceType ? ` (${esc(r.roomInstanceType)})` : ''}</small>` : ''}
                        ${r.resolutionNote ? `<br><small>Resolution: ${esc(r.resolutionNote)}</small>` : ''}
                    </div>
                    ${r.status === 'Pending' ? `
                    <div style="display:flex;gap:6px;flex-wrap:wrap;align-items:flex-start">
                        <button class="btn small danger act-ban" data-id="${esc(r.id)}">Ban</button>
                        <button class="btn small warn act-timeout" data-id="${esc(r.id)}">Timeout</button>
                        <button class="btn small ghost act-noaction" data-id="${esc(r.id)}">No Action</button>
                    </div>` : ''}
                </div>
            `).join('');

            list.querySelectorAll('.act-ban').forEach(btn => btn.addEventListener('click', () => resolveReport(btn.dataset.id, 'ban')));
            list.querySelectorAll('.act-timeout').forEach(btn => btn.addEventListener('click', () => resolveReport(btn.dataset.id, 'timeout')));
            list.querySelectorAll('.act-noaction').forEach(btn => btn.addEventListener('click', () => resolveReport(btn.dataset.id, 'noaction')));
        } catch (error) {
            list.innerHTML = `<p class="form-status error">${esc(error.message)}</p>`;
        }
    }

    statusFilter.addEventListener('change', loadReports);
    await loadReports();
}

async function bugReportsView() {
    app.innerHTML = `
    <h1 class="page-title">Bug Reports</h1>
    <div class="card">
        <label>Status
            <select id="bugStatusFilter">
                <option value="Open">Open</option>
                <option value="Closed">Closed</option>
                <option value="all">All</option>
            </select>
        </label>
    </div>
    <div class="card">
        <div id="bugReportList"><div class="loading"><i></i>Loading bug reports</div></div>
    </div>`;

    const statusFilter = document.querySelector('#bugStatusFilter');
    const list = document.querySelector('#bugReportList');

    async function loadBugReports() {
        list.innerHTML = '<div class="loading"><i></i>Loading bug reports</div>';
        try {
            const reports = await get(`${API}/admin/reports/bugs?status=${encodeURIComponent(statusFilter.value)}`);
            if (!reports.length) { list.innerHTML = '<p class="field-hint">No bug reports here.</p>'; return; }
            list.innerHTML = reports.map(r => `
                <div class="room-row">
                    <div style="flex:1">
                        <strong>${esc(r.category || 'Uncategorized')}</strong>
                        <span class="badge">${esc(r.status)}</span>
                        <br><small>From ${esc(r.reporterUsername || `Account #${r.reporterId}`)} on ${new Date(r.createdAt).toLocaleString()}</small>
                        <br><small>${esc(r.description || '')}</small>
                    </div>
                    <button class="btn small ${r.status === 'Open' ? 'ghost' : 'danger'} toggle-bug" data-id="${esc(r.id)}" data-next="${r.status === 'Open' ? 'Closed' : 'Open'}">${r.status === 'Open' ? 'Close' : 'Reopen'}</button>
                </div>
            `).join('');

            list.querySelectorAll('.toggle-bug').forEach(btn => btn.addEventListener('click', async () => {
                try {
                    await send(`${API}/admin/reports/bugs/${encodeURIComponent(btn.dataset.id)}/resolve`, 'POST', { status: btn.dataset.next });
                    await loadBugReports();
                } catch (error) { alert(error.message); }
            }));
        } catch (error) {
            list.innerHTML = `<p class="form-status error">${esc(error.message)}</p>`;
        }
    }

    statusFilter.addEventListener('change', loadBugReports);
    await loadBugReports();
}

async function ipBansView() {
    app.innerHTML = `
    <h1 class="page-title">IP Bans</h1>
    <div class="card">
        <h2>Add IP Ban</h2>
        <label>IP / CIDR<input id="ipBanNetwork" placeholder="203.0.113.4 or 203.0.113.0/24"></label>
        <br><br>
        <label>Reason<input id="ipBanReason" placeholder="Ban reason"></label>
        <br><br>
        <button class="btn danger" id="addIpBan">Add IP Ban</button>
        <span class="form-status" id="ipBanStatus"></span>
    </div>
    <div class="card">
        <h2>Active Bans</h2>
        <div id="ipBanList"><div class="loading"><i></i>Loading IP bans</div></div>
    </div>`;

    const networkInput = document.querySelector('#ipBanNetwork');
    const reasonInput = document.querySelector('#ipBanReason');
    const status = document.querySelector('#ipBanStatus');
    const list = document.querySelector('#ipBanList');

    async function loadIpBans() {
        list.innerHTML = '<div class="loading"><i></i>Loading IP bans</div>';
        try {
            const bans = await get(`${API}/admin/ip-bans`);
            if (!bans.length) { list.innerHTML = '<p class="field-hint">No IP addresses or ranges are banned.</p>'; return; }
            list.innerHTML = bans.map(b => `
                <div class="room-row">
                    <div style="flex:1"><strong>${esc(b.network)}</strong><br><small>${esc(b.reason || 'Blocked by an administrator.')}</small><br><small>Added ${new Date(b.createdAt).toLocaleString()} by account #${b.createdByAccountId || 0}</small></div>
                    <button class="btn small danger remove-ip" data-id="${esc(b.id)}">Remove</button>
                </div>
            `).join('');
            list.querySelectorAll('.remove-ip').forEach(btn => btn.addEventListener('click', async () => {
                if (!confirm('Remove this IP ban?')) return;
                try {
                    await send(`${API}/admin/ip-bans/${encodeURIComponent(btn.dataset.id)}`, 'DELETE', {});
                    await loadIpBans();
                } catch (error) { alert(error.message); }
            }));
        } catch (error) {
            list.innerHTML = `<p class="form-status error">${esc(error.message)}</p>`;
        }
    }

    const addIpBanBtn = document.querySelector('#addIpBan');
    if (addIpBanBtn) addIpBanBtn.addEventListener('click', async () => {
        const network = networkInput.value.trim();
        const reason = reasonInput.value.trim();
        if (!network) return;
        try {
            status.textContent = 'Adding IP ban...';
            status.classList.remove('error');
            await send(`${API}/admin/ip-bans`, 'POST', { network, reason });
            networkInput.value = '';
            reasonInput.value = '';
            status.textContent = 'IP ban added.';
            await loadIpBans();
        } catch (error) {
            status.textContent = error.message;
            status.classList.add('error');
        }
    });

    await loadIpBans();
}

async function steamBlacklistView() {
    app.innerHTML = `
    <h1 class="page-title">Steam Blacklist</h1>
    <div class="card">
        <h2>Blacklist Steam ID</h2>
        <p class="field-hint">Every Steam ID is allowed by default. Add an ID here to return HTTP 403 on cached login, account creation, password login, refresh, and RecNet login.</p>
        <label>Steam ID<input id="steamId" placeholder="7656119..." type="number"></label>
        <br><br>
        <label>Reason<input id="steamReason" placeholder="Why this Steam ID is blocked"></label>
        <br><br>
        <button class="btn danger" id="addSteam">Blacklist Steam ID</button>
        <span class="form-status" id="steamStatus"></span>
    </div>
    <div class="card">
        <h2>Blacklisted IDs</h2>
        <div id="steamList"><div class="loading"><i></i>Loading Steam blacklist</div></div>
    </div>`;

    const steamInput = document.querySelector('#steamId');
    const reasonInput = document.querySelector('#steamReason');
    const status = document.querySelector('#steamStatus');
    const list = document.querySelector('#steamList');

    async function loadSteamBlacklist() {
        list.innerHTML = '<div class="loading"><i></i>Loading Steam blacklist</div>';
        try {
            const items = await get(`${API}/admin/steam-blacklist`);
            if (!items.length) { list.innerHTML = '<p class="field-hint">No Steam IDs are blacklisted.</p>'; return; }
            list.innerHTML = items.map(item => `
                <div class="room-row">
                    <div style="flex:1"><strong>${esc(item.steamId)}</strong><br><small>${esc(item.reason || 'Blacklisted by an administrator.')}</small><br><small>Added by ${esc(item.addedByDisplayName || item.addedByByUsername || `Account #${item.addedByAccountId}`)} on ${new Date(item.addedAt || item.AddedAt).toLocaleString()}</small></div>
                    <button class="btn small danger remove-steam" data-steam="${esc(item.steamId)}">Unblacklist</button>
                </div>
            `).join('');
            list.querySelectorAll('.remove-steam').forEach(btn => btn.addEventListener('click', async () => {
                if (!confirm(`Remove Steam ID ${btn.dataset.steam} from the blacklist?`)) return;
                try {
                    await send(`${API}/admin/steam-blacklist/${encodeURIComponent(btn.dataset.steam)}`, 'DELETE', {});
                    await loadSteamBlacklist();
                } catch (error) { alert(error.message); }
            }));
        } catch (error) {
            list.innerHTML = `<p class="form-status error">${esc(error.message)}</p>`;
        }
    }

    const addSteamBtn = document.querySelector('#addSteam');
    if (addSteamBtn) addSteamBtn.addEventListener('click', async () => {
        const steamId = steamInput.value.trim();
        const reason = reasonInput.value.trim();
        if (!steamId) return;
        if (!confirm(`Blacklist Steam ID ${steamId}? Future login and connect attempts will receive HTTP 403.`)) return;
        try {
            status.textContent = 'Saving...';
            status.classList.remove('error');
            await send(`${API}/admin/steam-blacklist`, 'POST', { steamId, reason });
            steamInput.value = '';
            reasonInput.value = '';
            status.textContent = 'Steam ID blacklisted.';
            await loadSteamBlacklist();
        } catch (error) {
            status.textContent = error.message;
            status.classList.add('error');
        }
    });

    await loadSteamBlacklist();
}

async function shopView() {
    app.innerHTML = `
    <h1 class="page-title">Shop Controls</h1>
    <div class="card">
        <h2>Shop Actions</h2>
        <button class="btn" id="refreshShop">Refresh Shop Now</button>
        <span class="form-status" id="shopStatus"></span>
    </div>
    <div class="card">
        <h2>Custom Shop Items</h2>
        <div id="customShopItems"><div class="loading"><i></i>Loading custom items</div></div>
    </div>
    <div class="card">
        <h2>Add Custom Item</h2>
        <input id="shopSearch" placeholder="Search item name, SKU, or avatar item ID">
        <div id="shopSearchResults"></div>
        <div class="search-row" id="shopSearchPager" style="margin-top:10px;margin-bottom:0;display:none">
            <button class="btn ghost small" id="shopSearchPrev">&larr; Prev</button>
            <span class="field-hint" id="shopSearchPageInfo" style="align-self:center"></span>
            <button class="btn ghost small" id="shopSearchNext">Next &rarr;</button>
        </div>
    </div>`;

    const status = document.querySelector('#shopStatus');
    const customItems = document.querySelector('#customShopItems');
    const searchInput = document.querySelector('#shopSearch');
    const searchResults = document.querySelector('#shopSearchResults');

    async function loadCustomItems() {
        customItems.innerHTML = '<div class="loading"><i></i>Loading custom items</div>';
        try {
            const items = await get(`${API}/admin/shop`);
            if (!items.customItems || !items.customItems.length) { customItems.innerHTML = '<p class="field-hint">No custom items pinned. All 10 slots are random 5-stars.</p>'; return; }
            customItems.innerHTML = items.customItems.map(item => `
                <div class="room-row">
                    <div style="flex:1"><strong>${esc(item.friendlyName)}</strong><br><small>SKU ${item.skuId} · ${item.price} tokens</small></div>
                    <button class="btn small danger remove-shop" data-sku="${item.skuId}">Remove</button>
                </div>
            `).join('');
            customItems.querySelectorAll('.remove-shop').forEach(btn => btn.addEventListener('click', async () => {
                try {
                    const status = document.querySelector('#boardStatus');
                    status.textContent = 'Saving...';
                    status.classList.remove('error');
                    try {
                        await send(`${API}/admin/shop/items/${btn.dataset.sku}`, 'DELETE', {});
                        status.textContent = 'Saved successfully.';
                        status.classList.remove('error');
                    } catch (error) {
                        status.textContent = error.message;
                        status.classList.add('error');
                    }
                } catch (error) {
                    alert(error.message);
                }
            }));
        } catch (error) {
            customItems.innerHTML = `<p class="form-status error">${esc(error.message)}</p>`;
        }
    }

    const refreshShopBtn = document.querySelector('#refreshShop');
    if (refreshShopBtn) refreshShopBtn.addEventListener('click', async () => {
        const status = document.querySelector('#shopStatus');
        if (!status) return;

        try {
            status.textContent = 'Refreshing shop...';
            status.classList.remove('error');
            await send(`${API}/admin/shop/refresh`, 'POST', {});
            status.textContent = 'Shop refreshed.';
        } catch (error) {
            status.textContent = error.message;
            status.classList.add('error');
        }
    });

    let shopSearchSkip = 0;
    const shopSearchTake = 20;
    async function runShopSearch() {
        const query = searchInput.value.trim();
        const pager = document.querySelector('#shopSearchPager');
        if (!query) { searchResults.innerHTML = ''; pager.style.display = 'none'; return; }
        try {
            const data = await get(`${API}/admin/shop/catalog?search=${encodeURIComponent(query)}&skip=${shopSearchSkip}&take=${shopSearchTake}`);
            const items = data.results ?? data;
            const total = data.total ?? items.length;
            if (!items.length) { searchResults.innerHTML = '<p class="field-hint">No items found.</p>'; }
            else {
                searchResults.innerHTML = items.map(item => `
                    <div class="room-row clickable" data-sku="${item.skuId}">
                        <div class="meta"><strong>${esc(item.friendlyName)}</strong><br><small>SKU ${item.skuId} · Avatar #${item.avatarItemId || 'N/A'}</small></div>
                        <button class="btn small add-shop">Add</button>
                    </div>
                `).join('');
                searchResults.querySelectorAll('.add-shop').forEach(btn => btn.addEventListener('click', async (e) => {
                    e.stopPropagation();
                    const sku = btn.closest('[data-sku]').dataset.sku;
                    try {
                        await send(`${API}/admin/shop/custom-items`, 'POST', { skuId: Number(sku) });
                        await loadCustomItems();
                    } catch (error) { alert(error.message); }
                }));
            }
            pager.style.display = total > shopSearchTake ? 'flex' : 'none';
            document.querySelector('#shopSearchPageInfo').textContent = total ? `${shopSearchSkip + 1}-${Math.min(shopSearchSkip + shopSearchTake, total)} of ${total}` : '';
            document.querySelector('#shopSearchPrev').disabled = shopSearchSkip <= 0;
            document.querySelector('#shopSearchNext').disabled = shopSearchSkip + shopSearchTake >= total;
        } catch (error) {
            searchResults.innerHTML = `<p class="form-status error">${esc(error.message)}</p>`;
        }
    }
    let searchTimer;
    searchInput.addEventListener('input', () => {
        clearTimeout(searchTimer);
        searchTimer = setTimeout(() => { shopSearchSkip = 0; runShopSearch(); }, 300);
    });
    document.querySelector('#shopSearchPrev').addEventListener('click', () => { shopSearchSkip = Math.max(0, shopSearchSkip - shopSearchTake); runShopSearch(); });
    document.querySelector('#shopSearchNext').addEventListener('click', () => { shopSearchSkip += shopSearchTake; runShopSearch(); });

    await loadCustomItems();
}

function describeImportResult(result) {
    let text = `Imported! Room ID: ${result.roomId}, Subrooms: ${result.subRoomsImported}, Saves: ${result.savesImported}`;
    if (result.bakedAssetsImported) text += `, Baked assets: ${result.bakedAssetsImported}`;
    if (result.assetBundlesCopied) text += ` (${result.assetBundlesCopied} bundles copied${result.assetBundlesMissing ? `, ${result.assetBundlesMissing} missing` : ''})`;
    if (result.unityEngineVersions?.length) text += `. Unity versions: ${result.unityEngineVersions.join(', ')}`;
    return text;
}

async function pollMassImportJob(jobId, status, progressBox) {
    while (true) {
        let job;
        try {
            job = await get(`${API}/admin/rooms/import-batch/${jobId}`);
        } catch (error) {
            status.textContent = `Lost track of the import job: ${error.message}`;
            status.classList.add('error');
            return;
        }

        const total = job.totalFound || 0;
        const done = job.completedCount || 0;
        if (job.status === 'pending') {
            status.textContent = 'Finding rooms...';
        } else if (job.status === 'running') {
            status.textContent = `Importing ${done}/${total}${job.currentRoomName ? ` - ${job.currentRoomName}` : ''}...`;
        } else if (job.status === 'failed') {
            status.textContent = `Batch failed: ${job.fatalError || 'unknown error'}`;
            status.classList.add('error');
        } else {
            status.textContent = `Done: ${job.successCount}/${total} imported${job.failedCount ? `, ${job.failedCount} failed` : ''}.`;
        }

        if (progressBox) {
            progressBox.innerHTML = (job.results || []).map(r => {
                const detail = r.success
                    ? ` (room ${r.roomId}, ${r.subRoomsImported} subroom${r.subRoomsImported === 1 ? '' : 's'}${r.bakedAssetsImported ? `, ${r.bakedAssetsImported} baked assets` : ''})`
                    : ` — ${r.error}`;
                const color = r.success ? '#4caf50' : '#ff5555';
                return `<div style="color:${color};font-family:monospace;font-size:12px;padding:2px 0">${r.success ? '✓' : '✗'} ${r.name}${detail}</div>`;
            }).join('');
        }

        if (job.status === 'completed' || job.status === 'failed') return;
        await new Promise(resolve => setTimeout(resolve, 1500));
    }
}

async function roomImporterView() {
    app.innerHTML = `
    <h1 class="page-title">Room Importer</h1>
    <div class="card">
        <h2>Import Room</h2>
        <p class="field-hint">Upload a room export ZIP, paste Unity scene JSON metadata, or import straight from an archive URL (e.g. an <a href="https://archive.splootybean.com/Rooms/" target="_blank" rel="noopener">archive.splootybean.com</a>-style room folder). Baked asset bundles are picked up automatically in all three modes - no separate step needed.</p>
        <br>
        <label>Import Method<select id="importMethod">
            <option value="zip">ZIP File Upload</option>
            <option value="url">Import from URL</option>
            <option value="json">JSON Metadata</option>
        </select></label>
        <br><br>
        <label><input type="checkbox" id="massImportToggle"> Mass import (multiple rooms at once)</label>
        <p class="field-hint" id="massImportHint" style="display:none">ZIP: a folder of multiple room exports (e.g. a whole archived <code>Rooms/</code> folder), zipped up - every folder inside containing a room.json gets imported. URL: point at the index folder itself (e.g. <code>.../Rooms/</code>) instead of one room's folder, and every room folder listed under it gets crawled and imported. JSON: paste a JSON array of room metadata objects instead of one object. Runs as a background job so it isn't limited by how long a single request can stay open - the panel below polls for progress.</p>
        <br>
        <div id="zipSection">
            <label id="roomZipLabel">Room Export ZIP (.zip)<input type="file" id="roomZip" accept=".zip"></label>
            <br><br>
            <label>Creator Account ID (optional, defaults to you)<input type="number" id="creatorAccountId" placeholder="Account ID"></label>
            <br><br>
            <label><input type="checkbox" id="replaceExisting" checked> Replace existing room if it exists</label>
        </div>
        <div id="urlSection" style="display:none">
            <label id="roomUrlLabel">Room folder URL<input type="url" id="roomUrl" placeholder="https://archive.splootybean.com/Rooms/Showdown/"></label>
            <p class="field-hint" id="roomUrlHint">Point this at the page that directly lists room.json and the numbered subroom folders - not the parent Rooms/ index. The server crawls that listing itself: room.json, every subroom's saves, and only the specific baked asset bundles this room's save data actually references (not the whole shared AssetBundles/ folder). Capped at 1 GiB total.</p>
            <br>
            <label>Creator Account ID (optional, defaults to you)<input type="number" id="urlCreatorAccountId" placeholder="Account ID"></label>
            <br><br>
            <label><input type="checkbox" id="urlReplaceExisting" checked> Replace existing room if it exists</label>
        </div>
        <div id="jsonSection" style="display:none">
            <label id="roomJsonLabel">Unity Scene JSON Metadata<textarea id="roomJson" rows="10" placeholder="Paste Unity scene JSON metadata here..."></textarea></label>
        </div>
        <br><br>
        <button class="btn" id="importRoom">Import Room</button>
        <span class="form-status" id="importStatus"></span>
        <div id="massProgress" style="max-height:320px;overflow-y:auto;margin-top:10px"></div>
    </div>`;

    const importMethod = document.querySelector('#importMethod');
    const zipSection = document.querySelector('#zipSection');
    const urlSection = document.querySelector('#urlSection');
    const jsonSection = document.querySelector('#jsonSection');
    const massToggle = document.querySelector('#massImportToggle');
    const massHint = document.querySelector('#massImportHint');
    const importBtnLabel = document.querySelector('#importRoom');

    function applyMassLabels() {
        const isMass = massToggle && massToggle.checked;
        massHint.style.display = isMass ? '' : 'none';
        document.querySelector('#roomZipLabel').firstChild.textContent = isMass
            ? 'Rooms Export ZIP (multiple room folders)'
            : 'Room Export ZIP (.zip)';
        document.querySelector('#roomUrlLabel').firstChild.textContent = isMass
            ? 'Rooms index URL'
            : 'Room folder URL';
        document.querySelector('#roomUrl').placeholder = isMass
            ? 'https://archive.splootybean.com/Rooms/'
            : 'https://archive.splootybean.com/Rooms/Showdown/';
        document.querySelector('#roomUrlHint').textContent = isMass
            ? 'Point this at the index folder that lists every room\'s own folder (e.g. .../Rooms/) - each room folder found directly under it gets crawled and imported in turn. Same per-room 1 GiB cap, plus a 300 room-folder cap on the listing itself.'
            : 'Point this at the page that directly lists room.json and the numbered subroom folders - not the parent Rooms/ index. The server crawls that listing itself: room.json, every subroom\'s saves, and only the specific baked asset bundles this room\'s save data actually references (not the whole shared AssetBundles/ folder). Capped at 1 GiB total.';
        document.querySelector('#roomJsonLabel').firstChild.textContent = isMass
            ? 'JSON array of room metadata objects'
            : 'Unity Scene JSON Metadata';
        document.querySelector('#roomJson').placeholder = isMass
            ? 'Paste a JSON array here: [{...room 1...}, {...room 2...}]'
            : 'Paste Unity scene JSON metadata here...';
        if (importBtnLabel) importBtnLabel.textContent = isMass ? 'Import Rooms' : 'Import Room';
    }

    if (massToggle) massToggle.addEventListener('change', applyMassLabels);
    applyMassLabels();

    if (importMethod) importMethod.addEventListener('change', () => {
        zipSection.style.display = importMethod.value === 'zip' ? '' : 'none';
        urlSection.style.display = importMethod.value === 'url' ? '' : 'none';
        jsonSection.style.display = importMethod.value === 'json' ? '' : 'none';
    });

    const importBtn = document.querySelector('#importRoom');
    if (importBtn) importBtn.addEventListener('click', async () => {
        const status = document.querySelector('#importStatus');
        const progressBox = document.querySelector('#massProgress');
        if (!status) return;

        status.textContent = 'Importing...';
        status.classList.remove('error');
        if (progressBox) progressBox.innerHTML = '';

        const isMass = massToggle && massToggle.checked;

        try {
            if (isMass) {
                let startResult;
                if (importMethod.value === 'json') {
                    const roomJson = document.querySelector('#roomJson');
                    if (!roomJson || !roomJson.value.trim()) {
                        status.textContent = 'Please paste a JSON array of room metadata objects.';
                        status.classList.add('error');
                        return;
                    }
                    startResult = await send(`${API}/admin/rooms/import-batch/json`, 'POST', roomJson.value.trim());
                } else if (importMethod.value === 'url') {
                    const roomUrl = document.querySelector('#roomUrl');
                    const urlCreatorAccountId = document.querySelector('#urlCreatorAccountId');
                    const urlReplaceExisting = document.querySelector('#urlReplaceExisting');

                    if (!roomUrl || !roomUrl.value.trim()) {
                        status.textContent = 'Please enter the rooms index URL.';
                        status.classList.add('error');
                        return;
                    }

                    status.textContent = 'Finding room folders...';
                    startResult = await send(`${API}/admin/rooms/import-batch/url`, 'POST', {
                        url: roomUrl.value.trim(),
                        creatorAccountId: urlCreatorAccountId?.value ? Number(urlCreatorAccountId.value) : null,
                        replaceExisting: urlReplaceExisting ? urlReplaceExisting.checked : true
                    });
                } else {
                    const roomZip = document.querySelector('#roomZip');
                    const creatorAccountId = document.querySelector('#creatorAccountId');
                    const replaceExisting = document.querySelector('#replaceExisting');

                    if (!roomZip || !roomZip.files || !roomZip.files[0]) {
                        status.textContent = 'Please select a ZIP file.';
                        status.classList.add('error');
                        return;
                    }

                    const formData = new FormData();
                    formData.append('file', roomZip.files[0]);
                    if (creatorAccountId && creatorAccountId.value) {
                        formData.append('creatorAccountId', creatorAccountId.value);
                    }
                    if (replaceExisting) {
                        formData.append('replaceExisting', replaceExisting.checked.toString());
                    }

                    status.textContent = 'Uploading...';
                    const response = await fetch(`${API}/admin/rooms/import-batch/zip`, {
                        method: 'POST',
                        body: formData
                    });

                    if (!response.ok) {
                        const error = await response.json();
                        throw new Error(error.error || `Request failed (${response.status})`);
                    }

                    startResult = await response.json();
                }

                await pollMassImportJob(startResult.jobId, status, progressBox);
                return;
            }

            if (importMethod.value === 'json') {
                const roomJson = document.querySelector('#roomJson');
                if (!roomJson || !roomJson.value.trim()) {
                    status.textContent = 'Please paste JSON metadata.';
                    status.classList.add('error');
                    return;
                }

                const result = await send(`${API}/admin/rooms/import`, 'POST', roomJson.value.trim());
                status.textContent = describeImportResult(result);
            } else if (importMethod.value === 'url') {
                const roomUrl = document.querySelector('#roomUrl');
                const urlCreatorAccountId = document.querySelector('#urlCreatorAccountId');
                const urlReplaceExisting = document.querySelector('#urlReplaceExisting');

                if (!roomUrl || !roomUrl.value.trim()) {
                    status.textContent = 'Please enter the room folder URL.';
                    status.classList.add('error');
                    return;
                }

                status.textContent = 'Starting crawl...';
                const startResult = await send(`${API}/admin/rooms/import-url`, 'POST', {
                    url: roomUrl.value.trim(),
                    creatorAccountId: urlCreatorAccountId?.value ? Number(urlCreatorAccountId.value) : null,
                    replaceExisting: urlReplaceExisting ? urlReplaceExisting.checked : true
                });
                await pollMassImportJob(startResult.jobId, status, progressBox);
                return;
            } else {
                const roomZip = document.querySelector('#roomZip');
                const creatorAccountId = document.querySelector('#creatorAccountId');
                const replaceExisting = document.querySelector('#replaceExisting');

                if (!roomZip || !roomZip.files || !roomZip.files[0]) {
                    status.textContent = 'Please select a ZIP file.';
                    status.classList.add('error');
                    return;
                }

                const formData = new FormData();
                formData.append('file', roomZip.files[0]);
                if (creatorAccountId && creatorAccountId.value) {
                    formData.append('creatorAccountId', creatorAccountId.value);
                }
                if (replaceExisting) {
                    formData.append('replaceExisting', replaceExisting.checked.toString());
                }

                status.textContent = 'Uploading...';
                const response = await fetch(`${API}/admin/rooms/import`, {
                    method: 'POST',
                    body: formData
                });

                if (!response.ok) {
                    const error = await response.json();
                    throw new Error(error.error || `Request failed (${response.status})`);
                }

                const startResult = await response.json();
                await pollMassImportJob(startResult.jobId, status, progressBox);
                return;
            }
            status.classList.remove('error');
        } catch (error) {
            status.textContent = error.message;
            status.classList.add('error');
        }
    });
}

async function logsView() {
    app.innerHTML = `
    <h1 class="page-title">Server Logs</h1>
    <div class="card">
        <p class="field-hint">The last few thousand lines of server console output, kept in memory (cleared on restart). Not a substitute for a real log file, but enough to see what's happening right now.</p>
        <div style="display:flex;gap:10px;align-items:center;margin:10px 0">
            <label>Lines<input type="number" id="logsTake" value="500" min="50" max="5000" style="width:90px"></label>
            <button class="btn" id="logsRefresh">Refresh</button>
            <label><input type="checkbox" id="logsAutoRefresh"> Auto-refresh (5s)</label>
        </div>
        <pre id="logsOutput" style="background:#111;color:#ddd;padding:12px;border-radius:6px;max-height:600px;overflow:auto;font-size:12px;white-space:pre-wrap;word-break:break-all"></pre>
    </div>`;

    const output = document.querySelector('#logsOutput');
    const takeInput = document.querySelector('#logsTake');
    const autoRefresh = document.querySelector('#logsAutoRefresh');
    let timer = null;

    async function load() {
        const take = Math.max(50, Math.min(5000, Number(takeInput.value) || 500));
        try {
            const result = await get(`${API}/admin/logs?take=${take}`);
            output.textContent = (result.lines || []).join('\n') || '(no log lines yet)';
            output.scrollTop = output.scrollHeight;
        } catch (error) {
            output.textContent = `Error loading logs: ${error.message}`;
        }
    }

    document.querySelector('#logsRefresh').addEventListener('click', load);
    autoRefresh.addEventListener('change', () => {
        if (timer) { clearInterval(timer); timer = null; }
        if (autoRefresh.checked) timer = setInterval(load, 5000);
    });

    await load();
}

async function scannerLogsView() {
    app.innerHTML = `
    <h1 class="page-title">Loser Scanner Logs</h1>
    <div class="card">
        <p class="field-hint">Every request that hit a path nothing on this server recognizes - nobody legitimate does that by hand, so this is scanner/bot traffic going fishing for stuff like .env, aws config files, wp-login.php, .git/config, and the like. Highlighted rows matched one of those patterns specifically; everything else just hit a URL that doesn't exist here.</p>
        <div style="display:flex;gap:10px;align-items:center;margin:10px 0;flex-wrap:wrap">
            <span id="scannerTotal" class="field-hint"></span>
            <input id="scannerIpFilter" placeholder="Filter by IP" style="width:160px">
            <button class="btn" id="scannerRefresh">Refresh</button>
        </div>
        <div id="scannerResults"></div>
        <div style="margin-top:10px"><button class="btn" id="scannerLoadMore" style="display:none">Load more</button></div>
    </div>`;

    const resultsEl = document.querySelector('#scannerResults');
    const totalEl = document.querySelector('#scannerTotal');
    const ipFilter = document.querySelector('#scannerIpFilter');
    const loadMoreBtn = document.querySelector('#scannerLoadMore');
    const pageSize = 100;
    let skip = 0;
    let rows = [];

    function renderRows() {
        if (!rows.length) {
            resultsEl.innerHTML = '<p class="field-hint">No scanner attempts logged yet.</p>';
            return;
        }
        resultsEl.innerHTML = rows.map(r => `
            <div style="border-bottom:1px solid #333;padding:6px 0;font-family:monospace;font-size:12px${r.matchedPattern ? ';background:rgba(255,85,85,0.08)' : ''}">
                <div>${esc(new Date(r.timestamp).toLocaleString())} &middot; <strong>${esc(r.ipAddress)}</strong> &middot; ${esc(r.method)} ${esc(r.path)}${r.queryString ? esc(r.queryString) : ''}</div>
                ${r.matchedPattern ? `<div style="color:#ff5555">matched: ${esc(r.matchedPattern)}</div>` : ''}
                ${r.userAgent ? `<div style="color:#888">${esc(r.userAgent)}</div>` : ''}
            </div>
        `).join('');
    }

    async function load(reset) {
        if (reset) { skip = 0; rows = []; }
        try {
            const ip = ipFilter.value.trim();
            const result = await get(`${API}/admin/scanner-logs?skip=${skip}&take=${pageSize}${ip ? `&ip=${encodeURIComponent(ip)}` : ''}`);
            rows = rows.concat(result.results || []);
            skip = rows.length;
            totalEl.textContent = `${result.total} total attempt${result.total === 1 ? '' : 's'} logged`;
            loadMoreBtn.style.display = (result.results || []).length === pageSize ? '' : 'none';
            renderRows();
        } catch (error) {
            resultsEl.innerHTML = `<p class="form-status error">${esc(error.message)}</p>`;
        }
    }

    document.querySelector('#scannerRefresh').addEventListener('click', () => load(true));
    ipFilter.addEventListener('keydown', e => { if (e.key === 'Enter') load(true); });
    loadMoreBtn.addEventListener('click', () => load(false));

    await load(true);
}

async function anticheatLogsView() {
    app.innerHTML = `
    <h1 class="page-title">Anticheat Logs</h1>
    <div class="card">
        <p class="field-hint">Every time the client mod caught something bad on its own machine - a debugger attached, a proxy sitting in the traffic path, a known injector process or module, or an unrecognized MelonLoader mod riding along - it reported here and force-closed itself.</p>
        <div style="display:flex;gap:10px;align-items:center;margin:10px 0;flex-wrap:wrap">
            <span id="acTotal" class="field-hint"></span>
            <input id="acIpFilter" placeholder="Filter by IP" style="width:160px">
            <button class="btn" id="acRefresh">Refresh</button>
        </div>
        <div id="acResults"></div>
        <div style="margin-top:10px"><button class="btn" id="acLoadMore" style="display:none">Load more</button></div>
    </div>`;

    const resultsEl = document.querySelector('#acResults');
    const totalEl = document.querySelector('#acTotal');
    const ipFilter = document.querySelector('#acIpFilter');
    const loadMoreBtn = document.querySelector('#acLoadMore');
    const pageSize = 100;
    let skip = 0;
    let rows = [];

    function renderRows() {
        if (!rows.length) {
            resultsEl.innerHTML = '<p class="field-hint">No anticheat flags logged yet.</p>';
            return;
        }
        resultsEl.innerHTML = rows.map(r => `
            <div style="border-bottom:1px solid #333;padding:6px 0;font-family:monospace;font-size:12px;background:rgba(255,85,85,0.08)">
                <div>${esc(new Date(r.timestamp).toLocaleString())} &middot; <strong>${esc(r.ipAddress)}</strong>${r.accountId ? ` &middot; <a href="#players/${r.accountId}">account ${r.accountId}</a>` : ''} &middot; steam ${esc(r.steamId || '0')} &middot; build ${esc(r.build || '?')}</div>
                <div style="color:#ff5555">${esc(r.flags)}</div>
                ${r.userAgent ? `<div style="color:#888">${esc(r.userAgent)}</div>` : ''}
            </div>
        `).join('');
    }

    async function load(reset) {
        if (reset) { skip = 0; rows = []; }
        try {
            const ip = ipFilter.value.trim();
            const result = await get(`${API}/admin/anticheat-logs?skip=${skip}&take=${pageSize}${ip ? `&ip=${encodeURIComponent(ip)}` : ''}`);
            rows = rows.concat(result.results || []);
            skip = rows.length;
            totalEl.textContent = `${result.total} total flag${result.total === 1 ? '' : 's'} logged`;
            loadMoreBtn.style.display = (result.results || []).length === pageSize ? '' : 'none';
            renderRows();
        } catch (error) {
            resultsEl.innerHTML = `<p class="form-status error">${esc(error.message)}</p>`;
        }
    }

    document.querySelector('#acRefresh').addEventListener('click', () => load(true));
    ipFilter.addEventListener('keydown', e => { if (e.key === 'Enter') load(true); });
    loadMoreBtn.addEventListener('click', () => load(false));

    await load(true);
}

async function boot() {
    try {
        currentUser = await get(`${API}/auth/me`);
    } catch {
        currentUser = null;
    }
    if (!currentUser || (!currentUser.isModerator && !currentUser.isDeveloper && !currentUser.isAdmin)) {
        renderLoginGate();
        return;
    }
    applyRoleGating();

    try {
        const prefs = await get(`${API}/admin/preferences`);
        if (prefs) applyPreferences(prefs);
    } catch (e) {

    }

    await route();
    setInterval(checkSystemStatus, 10000);
}

async function checkSystemStatus() {
    try {
        const status = await get(`${API}/admin/system-status`);
        const banner = document.querySelector('#ddosBanner');
        if (status.isUnderAttack) {
            if (!banner) {
                const bannerEl = document.createElement('div');
                bannerEl.id = 'ddosBanner';
                bannerEl.style.cssText = 'position:fixed;top:60px;left:0;right:0;background:#ff4444;color:white;padding:12px;text-align:center;font-weight:bold;z-index:9999;animation:pulse 1s infinite';
                bannerEl.innerHTML = '⚠️ WE\'RE GETTING DDOSED :c ⚠️';
                document.body.appendChild(bannerEl);
            }
        } else if (banner) {
            banner.remove();
        }
    } catch {}
}
boot();
