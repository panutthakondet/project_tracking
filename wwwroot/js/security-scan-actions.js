(() => {
    const links = document.querySelectorAll('[data-scan-download]');
    let downloading = false;
    links.forEach(link => link.addEventListener('click', async event => {
        if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        if (!window.ProjectTrackingLoading) return;
        event.preventDefault();
        if (downloading) return;
        downloading = true;
        const end = window.ProjectTrackingLoading.show('กำลังเตรียมไฟล์ดาวน์โหลด…', { delay: 0 });
        const previous = link.getAttribute('aria-disabled');
        link.setAttribute('aria-disabled', 'true');
        const controller = new AbortController();
        const timeout = setTimeout(() => controller.abort(), 180000);
        let errorBox = document.getElementById('securityDownloadError');
        if (errorBox) errorBox.remove();
        try {
            const response = await fetch(link.href, { credentials: 'same-origin', signal: controller.signal });
            const disposition = response.headers.get('Content-Disposition') || '';
            if (!response.ok || response.redirected || !/attachment/i.test(disposition))
                throw new Error('download failed');
            const blob = await response.blob();
            const url = URL.createObjectURL(blob);
            const anchor = document.createElement('a');
            const format = new URL(link.href).searchParams.get('format');
            const serverName = disposition.match(/filename="?(security-scan-\d+\.(?:pdf|md|json))"?(?:;|$)/i)?.[1];
            anchor.download = serverName || 'security-scan-report.' + (['pdf', 'md', 'json'].includes(format) ? format : 'bin');
            anchor.href = url;
            document.body.append(anchor);
            anchor.click();
            anchor.remove();
            setTimeout(() => URL.revokeObjectURL(url), 60000);
        } catch (error) {
            errorBox = document.createElement('div');
            errorBox.id = 'securityDownloadError';
            errorBox.className = 'alert alert-danger mt-3';
            errorBox.setAttribute('role', 'alert');
            errorBox.textContent = error.name === 'AbortError'
                ? 'เตรียมไฟล์นานเกินไป กรุณาลองดาวน์โหลดอีกครั้ง'
                : 'ดาวน์โหลดไม่สำเร็จ กรุณาตรวจสอบการเข้าสู่ระบบและลองอีกครั้ง';
            link.closest('.security-inline').after(errorBox);
        } finally {
            clearTimeout(timeout);
            downloading = false;
            if (previous === null) link.removeAttribute('aria-disabled');
            else link.setAttribute('aria-disabled', previous);
            end();
        }
    }));
})();
