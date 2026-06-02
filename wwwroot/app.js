(function () {
    'use strict';

    const API_BASE = '/api/files';
    const BROWSE_PREFIX = '/browse';

    // --- App State ---
    let currentPath = '';
    let entries = [];
    let filterText = '';
    let currentPage = 1;
    let totalPages = 1;
    let totalCount = 0;
    let filteredCount = 0;
    let folderCount = 0;
    let fileCount = 0;
    let totalSize = 0;
    let pageSize = 50;
    let sortOrder = '';
    let searchTimer = null;

    // --- DOM handles ---
    const breadcrumbEl = document.getElementById('breadcrumb');
    const fileListEl = document.getElementById('file-list');
    const searchInput = document.getElementById('search-input');
    const uploadBtn = document.getElementById('upload-btn');
    const uploadInput = document.getElementById('upload-input');
    const dropzone = document.getElementById('dropzone');
    const toastEl = document.getElementById('toast');
    const apiKeyInput = document.getElementById('api-key-input');
    const apiKeySaveBtn = document.getElementById('api-key-save');
    const dirStatsEl = document.getElementById('dir-stats');
    const sortSelect = document.getElementById('sort-select');

    // --- API Key ---
    function getApiKey() {
        return localStorage.getItem('apiKey') || '';
    }

    function setApiKey(key) {
        localStorage.setItem('apiKey', key);
    }

    apiKeyInput.value = getApiKey();
    apiKeySaveBtn.addEventListener('click', () => {
        setApiKey(apiKeyInput.value.trim());
        toast('API key saved');
        navigate(currentPath);
    });

    // --- Fetch wrapper ---
    async function apiFetch(url, options = {}) {
        const headers = options.headers || {};
        headers['X-Api-Key'] = getApiKey();
        options.headers = headers;
        const res = await fetch(url, options);
        if (!res.ok) {
            let message = `HTTP ${res.status}`;
            try {
                const body = await res.json();
                if (body && body.message) message = body.message;
            } catch {
                const text = await res.text().catch(() => '');
                if (text) message = text;
            }
            const err = new Error(message);
            err.status = res.status;
            throw err;
        }
        return res;
    }

    // --- Toast Alert ---
    let toastTimer;
    function toast(msg) {
        toastEl.textContent = msg;
        toastEl.classList.add('show');
        clearTimeout(toastTimer);
        toastTimer = setTimeout(() => toastEl.classList.remove('show'), 2500);
    }

    // --- Router ---
    function getPathFromUrl() {
        const path = window.location.pathname;
        if (path.startsWith(BROWSE_PREFIX + '/')) {
            return decodeURIComponent(path.slice(BROWSE_PREFIX.length + 1));
        }
        if (path === BROWSE_PREFIX) return '';
        return '';
    }

    function pushUrl(relativePath) {
        const url = relativePath ? `${BROWSE_PREFIX}/${encodeURIComponent(relativePath).replace(/%2F/g, '/')}` : BROWSE_PREFIX;
        history.pushState(null, '', url);
    }

    window.addEventListener('popstate', () => navigate(getPathFromUrl(), false));

    // --- Navigate ---
    async function navigate(path, push = true) {
        currentPath = path || '';
        currentPage = 1;
        if (push) pushUrl(currentPath);
        filterText = '';
        searchInput.value = '';
        await loadDirectory();
    }

    async function loadDirectory() {
        fileListEl.innerHTML = '<div class="loading">Loading...</div>';
        try {
            const params = new URLSearchParams();
            if (currentPath) params.set('path', currentPath);
            if (filterText) params.set('$search', filterText);
            if (sortOrder) params.set('$orderby', sortOrder);
            params.set('page', currentPage);
            params.set('pageSize', pageSize);
            const res = await apiFetch(`${API_BASE}/browse?${params}`);
            const data = await res.json();
            currentPath = data.path === '.' ? '' : data.path;
            entries = data.entries;
            currentPage = data.page;
            totalPages = data.totalPages;
            totalCount = data.totalCount;
            filteredCount = data.filteredCount;
            folderCount = data.folderCount;
            fileCount = data.fileCount;
            totalSize = data.totalSize;
            render();
        } catch (err) {
            fileListEl.innerHTML = `<div class="empty">Error: ${escapeHtml(err.message)}</div>`;
        }
    }

    // --- Render ---
    function render() {
        renderBreadcrumb();
        renderDirStats();
        renderFileList(entries);
        renderPager();
    }

    // --- Current view/path info ---
    function renderDirStats() {
        const parts = [];
        if (fileCount > 0) parts.push(`${fileCount} file${fileCount !== 1 ? 's' : ''}`);
        if (folderCount > 0) parts.push(`${folderCount} folder${folderCount !== 1 ? 's' : ''}`);
        if (totalSize > 0) parts.push(formatSize(totalSize));
        dirStatsEl.textContent = parts.length > 0 ? parts.join(' · ') : 'Empty';
    }

    function renderBreadcrumb() {
        const parts = currentPath ? currentPath.split('/') : [];
        let html = `<a data-path="">root</a>`;
        let accumulated = '';
        for (const part of parts) {
            accumulated += (accumulated ? '/' : '') + part;
            html += `<span class="sep">/</span><a data-path="${escapeAttr(accumulated)}">${escapeHtml(part)}</a>`;
        }
        breadcrumbEl.innerHTML = html;
        breadcrumbEl.querySelectorAll('a').forEach(a => {
            a.addEventListener('click', (e) => {
                e.preventDefault();
                navigate(a.dataset.path);
            });
        });
    }

    function renderFileList(items) {
        if (items.length === 0) {
            fileListEl.innerHTML = '<div class="empty">No items found</div>';
            return;
        }

        let html = '';
        for (const entry of items) {
            const e = { name: entry.name, isDirectory: entry.isDirectory, size: entry.size, lastModified: entry.lastModified };
            const icon = e.isDirectory ? '📁' : '📄';
            const size = e.isDirectory ? '' : formatSize(e.size);
            const modified = formatDate(e.lastModified);
            const entryPath = currentPath ? `${currentPath}/${e.name}` : e.name;

            html += `
                <div class="file-item" data-path="${escapeAttr(entryPath)}" data-is-dir="${e.isDirectory}" data-name="${escapeAttr(e.name)}">
                    <div class="name">
                        <span class="icon">${icon}</span>
                        <span>${escapeHtml(e.name)}</span>
                    </div>
                    <div class="meta">${size ? size + ' · ' : ''}${modified}</div>
                    <div class="actions">
                        <button class="share-btn" title="Share">🔗</button>
                        ${!e.isDirectory ? `<button class="dl-btn" title="Download">\u2193</button>` : ''}
                        <button class="rename-btn" title="Rename">✎</button>                        <button class="move-btn" title="Move">✂</button>
                        ${!e.isDirectory ? `<button class="copy-btn" title="Copy">📋</button>` : ''}
                        <button class="delete-btn" title="Delete">🗑</button>                    </div>
                </div>`;
        }

        fileListEl.innerHTML = html;

        // Event listeners
        fileListEl.querySelectorAll('.file-item').forEach(el => {
            el.addEventListener('click', (e) => {
                if (e.target.closest('.actions')) return;
                const isDir = el.dataset.isDir === 'true';
                if (isDir) {
                    navigate(el.dataset.path);
                } else {
                    downloadFile(el.dataset.path);
                }
            });

            const dlBtn = el.querySelector('.dl-btn');
            if (dlBtn) {
                dlBtn.addEventListener('click', (e) => {
                    e.stopPropagation();
                    downloadFile(el.dataset.path);
                });
            }

            const renameBtn = el.querySelector('.rename-btn');
            renameBtn.addEventListener('click', (e) => {
                e.stopPropagation();
                startRename(el);
            });

            const shareBtn = el.querySelector('.share-btn');
            shareBtn.addEventListener('click', (e) => {
                e.stopPropagation();
                openShareDialog(el.dataset.path, el.dataset.isDir === 'true');
            });

            const deleteBtn = el.querySelector('.delete-btn');
            deleteBtn.addEventListener('click', (e) => {
                e.stopPropagation();
                deleteItem(el.dataset.path, el.dataset.name);
            });

            const moveBtn = el.querySelector('.move-btn');
            moveBtn.addEventListener('click', (e) => {
                e.stopPropagation();
                moveItem(el.dataset.path, el.dataset.name);
            });

            const copyBtn = el.querySelector('.copy-btn');
            if (copyBtn) {
                copyBtn.addEventListener('click', (e) => {
                    e.stopPropagation();
                    copyItem(el.dataset.path, el.dataset.name);
                });
            }
        });
    }

    // --- Download ---
    function downloadFile(path) {
        const url = `${API_BASE}/download?path=${encodeURIComponent(path)}&apikey=${encodeURIComponent(getApiKey())}`;
        const a = document.createElement('a');
        a.href = url;
        a.download = '';
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
    }

    // --- Delete ---
    async function deleteItem(path, name) {
        if (!confirm(`Delete "${name}"? This cannot be undone.`)) return;
        try {
            const res = await fetch(`${API_BASE}/delete?path=${encodeURIComponent(path)}`, {
                method: 'DELETE',
                headers: { 'X-Api-Key': getApiKey() }
            });
            if (!res.ok) {
                const msg = await res.text();
                throw new Error(msg || res.statusText);
            }
            showToast(`Deleted "${name}"`);
            loadDirectory();
        } catch (err) {
            showToast(`Delete failed: ${err.message}`, true);
        }
    }

    // --- Move ---
    async function moveItem(path, name) {
        const dest = prompt(`Move "${name}" to directory (relative path):`, currentPath);
        if (dest === null) return;
        try {
            const res = await fetch(`${API_BASE}/move`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'X-Api-Key': getApiKey() },
                body: JSON.stringify({ path, destination: dest })
            });
            if (!res.ok) {
                const msg = await res.text();
                throw new Error(msg || res.statusText);
            }
            const data = await res.json();
            showToast(`Moved "${name}" → ${data.newPath}`);
            loadDirectory();
        } catch (err) {
            showToast(`Move failed: ${err.message}`, true);
        }
    }

    // --- Copy ---
    async function copyItem(path, name) {
        const dest = prompt(`Copy "${name}" to directory (relative path):`, currentPath);
        if (dest === null) return;
        try {
            const res = await fetch(`${API_BASE}/copy`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'X-Api-Key': getApiKey() },
                body: JSON.stringify({ path, destination: dest })
            });
            if (!res.ok) {
                const msg = await res.text();
                throw new Error(msg || res.statusText);
            }
            const data = await res.json();
            showToast(`Copied "${name}" → ${data.newPath}`);
            loadDirectory();
        } catch (err) {
            showToast(`Copy failed: ${err.message}`, true);
        }
    }

    // --- Rename ---
    function startRename(el) {
        const nameSpan = el.querySelector('.name span:last-child');
        const oldName = el.dataset.name;
        const input = document.createElement('input');
        input.type = 'text';
        input.className = 'rename-input';
        input.value = oldName;
        nameSpan.replaceWith(input);
        input.focus();
        input.select();

        const commit = async () => {
            const newName = input.value.trim();
            if (!newName || newName === oldName) {
                await loadDirectory();
                return;
            }
            try {
                await apiFetch(`${API_BASE}/rename`, {
                    method: 'PATCH',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ path: el.dataset.path, newName })
                });
                toast(`Renamed to "${newName}"`);
                await loadDirectory();
            } catch (err) {
                toast(`Rename failed: ${err.message}`);
                await loadDirectory();
            }
        };

        input.addEventListener('blur', commit);
        input.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') { e.preventDefault(); input.blur(); }
            if (e.key === 'Escape') { loadDirectory(); }
        });
    }

    // --- Share Dialog ---
    async function openShareDialog(path, isDir) {
        const modal = document.getElementById('share-modal');
        const directLinkEl = document.getElementById('share-direct-link');
        const expiringSection = document.getElementById('share-expiring-section');
        const expiringLinkEl = document.getElementById('share-expiring-link');
        const expiresAtEl = document.getElementById('share-expires-at');
        const expirySelect = document.getElementById('share-expiry-select');
        const generateBtn = document.getElementById('share-generate-btn');

        directLinkEl.value = 'Loading...';
        expiringLinkEl.value = '';
        expiresAtEl.textContent = '';
        expiringSection.style.display = isDir ? 'none' : 'block';
        modal.classList.add('show');

        async function generateLinks(minutes) {
            try {
                const res = await apiFetch(`${API_BASE}/share`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ path, expiresInMinutes: minutes })
                });
                const data = await res.json();
                directLinkEl.value = data.directLink;
                if (data.expiringLink) {
                    expiringLinkEl.value = data.expiringLink;
                    expiresAtEl.textContent = `Expires: ${new Date(data.expiresAt).toLocaleString()}`;
                }
            } catch (err) {
                directLinkEl.value = 'Error generating link';
                toast(`Share failed: ${err.message}`);
            }
        }

        await generateLinks(parseInt(expirySelect.value));

        // Re-generate when expiry changes
        const onExpiryChange = () => generateLinks(parseInt(expirySelect.value));
        expirySelect.addEventListener('change', onExpiryChange);
        generateBtn.addEventListener('click', onExpiryChange);

        // Copy buttons
        const copyDirect = document.getElementById('copy-direct-link');
        const copyExpiring = document.getElementById('copy-expiring-link');
        const onCopyDirect = () => { navigator.clipboard.writeText(directLinkEl.value); toast('Direct link copied'); };
        const onCopyExpiring = () => { navigator.clipboard.writeText(expiringLinkEl.value); toast('Expiring link copied'); };
        copyDirect.addEventListener('click', onCopyDirect);
        copyExpiring.addEventListener('click', onCopyExpiring);

        // Close
        const closeBtn = document.getElementById('share-close');
        const backdrop = modal;
        const close = () => {
            modal.classList.remove('show');
            expirySelect.removeEventListener('change', onExpiryChange);
            generateBtn.removeEventListener('click', onExpiryChange);
            copyDirect.removeEventListener('click', onCopyDirect);
            copyExpiring.removeEventListener('click', onCopyExpiring);
            closeBtn.removeEventListener('click', close);
            backdrop.removeEventListener('click', onBackdrop);
        };
        const onBackdrop = (e) => { if (e.target === modal) close(); };
        closeBtn.addEventListener('click', close);
        backdrop.addEventListener('click', onBackdrop);
    }

    // --- Upload ---
    uploadBtn.addEventListener('click', () => uploadInput.click());
    uploadInput.addEventListener('change', () => {
        if (uploadInput.files.length > 0) uploadFiles(uploadInput.files);
        uploadInput.value = '';
    });

    dropzone.addEventListener('dragover', (e) => { e.preventDefault(); dropzone.classList.add('active'); });
    dropzone.addEventListener('dragleave', () => dropzone.classList.remove('active'));
    dropzone.addEventListener('drop', (e) => {
        e.preventDefault();
        dropzone.classList.remove('active');
        if (e.dataTransfer.files.length > 0) uploadFiles(e.dataTransfer.files);
    });

    async function uploadFiles(files) {
        for (const file of files) {
            const formData = new FormData();
            formData.append('file', file);
            try {
                const params = currentPath ? `?path=${encodeURIComponent(currentPath)}` : '';
                await apiFetch(`${API_BASE}/upload${params}`, { method: 'POST', body: formData });
                toast(`Uploaded "${file.name}"`);
            } catch (err) {
                toast(`Upload failed: ${err.message}`);
            }
        }
        await loadDirectory();
    }

    // --- Search/filter ---
    searchInput.addEventListener('input', () => {
        filterText = searchInput.value;
        currentPage = 1;
        clearTimeout(searchTimer);
        searchTimer = setTimeout(() => loadDirectory(), 250);
    });

    // --- Sort ---
    sortSelect.addEventListener('change', () => {
        sortOrder = sortSelect.value;
        currentPage = 1;
        loadDirectory();
    });

    // --- Helpers ---
    function formatSize(bytes) {
        if (bytes == null) return '';
        if (bytes < 1024) return bytes + ' B';
        if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
        if (bytes < 1024 * 1024 * 1024) return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
        return (bytes / (1024 * 1024 * 1024)).toFixed(1) + ' GB';
    }

    function formatDate(iso) {
        if (!iso) return '';
        const d = new Date(iso);
        return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' });
    }

    function escapeHtml(str) {
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }

    function escapeAttr(str) {
        return str.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

    function showToast(message, isError) {
        const toast = document.createElement('div');
        toast.className = 'toast' + (isError ? ' toast-error' : '');
        toast.textContent = message;
        document.body.appendChild(toast);
        requestAnimationFrame(() => toast.classList.add('show'));
        setTimeout(() => {
            toast.classList.remove('show');
            setTimeout(() => toast.remove(), 300);
        }, 3000);
    }

    // --- Init ---
    pageSize = parseInt(localStorage.getItem('pageSize')) || 50;

    function renderPager() {
        const pagerEl = document.getElementById('pager');
        if (totalPages <= 1) {
            pagerEl.innerHTML = filteredCount > 0 && filteredCount < totalCount
                ? `<span class="pager-info">${filteredCount} of ${totalCount} result${totalCount !== 1 ? 's' : ''}</span>` : '';
            return;
        }

        let html = `<span class="pager-info">${filteredCount} result${filteredCount !== 1 ? 's' : ''} \u00b7 page ${currentPage} of ${totalPages}</span>`;
        html += `\n<button class="btn" data-page="1" ${currentPage === 1 ? 'disabled' : ''}>&laquo;</button>`;

        html += `<button class="btn" data-page="${currentPage - 1}" ${currentPage === 1 ? 'disabled' : ''}>&lsaquo;</button>`;

        const range = getPagerRange(currentPage, totalPages, 5);
        for (let i = range.start; i <= range.end; i++) {
            html += `<button class="btn ${i === currentPage ? 'btn-primary' : ''}" data-page="${i}">${i}</button>`;
        }

        html += `<button class="btn" data-page="${currentPage + 1}" ${currentPage === totalPages ? 'disabled' : ''}>&rsaquo;</button>`;
        html += `<button class="btn" data-page="${totalPages}" ${currentPage === totalPages ? 'disabled' : ''}>&raquo;</button>`;

        html += `<select id="page-size-select" class="btn">`;
        for (const s of [25, 50, 100, 200]) {
            html += `<option value="${s}" ${s === pageSize ? 'selected' : ''}>${s} / page</option>`;
        }
        html += `</select>`;

        pagerEl.innerHTML = html;

        pagerEl.querySelectorAll('button[data-page]').forEach(btn => {
            btn.addEventListener('click', () => {
                const p = parseInt(btn.dataset.page);
                if (p >= 1 && p <= totalPages && p !== currentPage) {
                    currentPage = p;
                    loadDirectory();
                }
            });
        });

        document.getElementById('page-size-select').addEventListener('change', (e) => {
            pageSize = parseInt(e.target.value);
            localStorage.setItem('pageSize', pageSize);
            currentPage = 1;
            loadDirectory();
        });
    }

    function getPagerRange(current, total, maxButtons) {
        let start = Math.max(1, current - Math.floor(maxButtons / 2));
        let end = start + maxButtons - 1;
        if (end > total) { end = total; start = Math.max(1, end - maxButtons + 1); }
        return { start, end };
    }

    navigate(getPathFromUrl(), false);
})();
