(() => {
    let pending;
    let revision = 0;
    const pagePath = location.pathname.toLowerCase().replace(/\/$/, '');

    async function loadTab(url, addHistory, focusTab) {
        const current = document.querySelector('[data-document-list]');
        if (!current) return;
        pending?.abort();
        pending = new AbortController();
        const version = ++revision;
        current.setAttribute('aria-busy', 'true');
        try {
            const response = await fetch(url, { credentials: 'same-origin', signal: pending.signal });
            if (!response.ok || response.redirected) throw new Error('Cannot load documents');
            const html = new DOMParser().parseFromString(await response.text(), 'text/html');
            const next = html.querySelector('[data-document-list]');
            if (!next) throw new Error('Missing document list');
            if (version !== revision) return;
            const x = window.scrollX;
            const y = window.scrollY;
            // คงพื้นที่ถึงขอบจอล่าง แม้แท็บใหม่มีไฟล์น้อยลง เพื่อไม่ให้หน้ากระโดด
            next.style.minHeight = `${Math.max(0, window.innerHeight - current.getBoundingClientRect().top)}px`;
            current.replaceWith(next);
            const type = new URL(url, location.href).searchParams.get('type') || '';
            document.querySelectorAll('input[type="hidden"][name="type"]').forEach(input => input.value = type);
            if (addHistory) history.pushState(null, '', url);
            if (focusTab) next.querySelector('.document-type-tab.is-active')?.focus({ preventScroll: true });
            window.scrollTo({ left: x, top: y, behavior: 'instant' });
        } catch (error) {
            if (error.name !== 'AbortError' && version === revision) location.assign(url);
        } finally {
            if (version === revision) {
                pending = null;
                document.querySelector('[data-document-list]')?.removeAttribute('aria-busy');
            }
        }
    }

    document.addEventListener('click', event => {
        const link = event.target.closest('.document-type-tabs a.document-type-tab');
        if (!link || event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        event.preventDefault();
        if (link.classList.contains('is-active') && !pending) return;
        loadTab(link.href, true, true);
    });
    window.addEventListener('popstate', () => {
        if (location.pathname.toLowerCase().replace(/\/$/, '') === pagePath) loadTab(location.href, false, false);
    });
})();
