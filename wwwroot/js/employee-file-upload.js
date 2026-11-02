(() => {
    document.querySelectorAll('form[data-employee-file-upload]').forEach(form => {
        let busy = false;
        let cancelRequested = false;
        const panel = document.createElement('div');
        panel.className = 'mt-3';
        panel.hidden = true;
        panel.innerHTML = '<div class="progress mb-2"><div class="progress-bar" role="progressbar" aria-label="ความคืบหน้าอัปโหลด" aria-valuemin="0" aria-valuemax="100" aria-valuenow="0" style="width:0%">0%</div></div><div class="d-flex align-items-center gap-2"><span role="status" aria-live="polite"></span><button type="button" class="btn btn-sm btn-outline-secondary">ยกเลิกอัปโหลด</button></div><div class="alert alert-danger mt-2" role="alert" hidden></div>';
        form.append(panel);
        const progress = panel.querySelector('[role="progressbar"]');
        const status = panel.querySelector('[role="status"]');
        const cancel = panel.querySelector('button');
        const error = panel.querySelector('[role="alert"]');
        cancel.addEventListener('click', () => {
            cancelRequested = true;
            cancel.disabled = true;
            status.textContent = 'กำลังยกเลิก รอส่งชิ้นส่วนปัจจุบันให้เสร็จ...';
        });
        window.addEventListener('beforeunload', event => {
            if (!busy) return;
            event.preventDefault();
            event.returnValue = '';
        });
        form.addEventListener('submit', async event => {
            if (event.defaultPrevented) return;
            event.preventDefault();
            if (busy || !form.reportValidity()) return;
            const file = form.querySelector('input[type="file"]').files[0];
            if (!file) return;
            if (file.size === 0) {
                panel.hidden = false;
                cancel.hidden = true;
                error.hidden = false;
                error.textContent = 'กรุณาเลือกไฟล์ที่มีข้อมูล';
                return;
            }
            const fields = new FormData(form);
            const token = fields.get('__RequestVerificationToken');
            const controls = [...form.querySelectorAll('input, select, textarea, button')]
                .filter(control => !panel.contains(control)).map(control => [control, control.disabled]);
            const post = async (url, values) => {
                const body = new FormData();
                body.append('__RequestVerificationToken', token);
                Object.entries(values).forEach(([key, value]) => body.append(key, value));
                const response = await fetch(url, { method: 'POST', body, credentials: 'same-origin' });
                let result;
                try { result = await response.json(); } catch { /* Server may return an HTML error page. */ }
                if (!response.ok || response.redirected || !result) {
                    throw new Error(result?.message || (response.status === 413
                        ? 'เว็บเซิร์ฟเวอร์ไม่รับขนาดชิ้นส่วนไฟล์ กรุณาติดต่อผู้ดูแลระบบ'
                        : 'อัปโหลดไม่สำเร็จ กรุณาตรวจการเชื่อมต่อและสถานะล็อกอิน แล้วลองใหม่'));
                }
                return result;
            };
            const checkCancelled = () => {
                if (cancelRequested) throw new Error('ยกเลิกการอัปโหลดแล้ว');
            };
            busy = true;
            cancelRequested = false;
            cancel.disabled = false;
            cancel.hidden = false;
            error.hidden = true;
            panel.hidden = false;
            progress.style.width = '0%';
            progress.textContent = '0%';
            progress.setAttribute('aria-valuenow', '0');
            status.textContent = 'เตรียมอัปโหลด...';
            controls.forEach(([control]) => control.disabled = true);
            let uploadId;
            try {
                const started = await post(form.dataset.uploadBegin, {
                    empId: fields.get('empId'), documentType: fields.get('documentType'),
                    description: fields.get('description') || '', fileName: file.name, fileSize: file.size
                });
                uploadId = started.uploadId;
                if (!uploadId || !Number.isInteger(started.chunkSize) || started.chunkSize <= 0) throw new Error('ข้อมูลอัปโหลดไม่ถูกต้อง');
                let offset = 0;
                while (offset < file.size) {
                    checkCancelled();
                    const end = Math.min(offset + started.chunkSize, file.size);
                    const result = await post(form.dataset.uploadChunk, {
                        uploadId, offset, chunk: file.slice(offset, end)
                    });
                    if (result.nextOffset !== end) throw new Error('ชิ้นส่วนไฟล์ส่งมาไม่ครบ กรุณาลองใหม่');
                    offset = end;
                    const percent = Math.floor(offset / file.size * 100);
                    progress.style.width = `${percent}%`;
                    progress.textContent = `${percent}%`;
                    progress.setAttribute('aria-valuenow', percent);
                    status.textContent = `อัปโหลดแล้ว ${(offset / 1048576).toLocaleString('th-TH', { maximumFractionDigits: 1 })} / ${(file.size / 1048576).toLocaleString('th-TH', { maximumFractionDigits: 1 })} MB`;
                }
                checkCancelled();
                cancel.disabled = true;
                status.textContent = 'กำลังบันทึกไฟล์...';
                const completed = await post(form.dataset.uploadComplete, { uploadId });
                busy = false;
                window.location.assign(completed.redirectUrl);
            } catch (failure) {
                if (uploadId) {
                    const body = new FormData();
                    body.append('__RequestVerificationToken', token);
                    body.append('uploadId', uploadId);
                    try { await fetch(form.dataset.uploadCancel, { method: 'POST', body, credentials: 'same-origin' }); } catch { /* Stale uploads are cleaned up on a later upload. */ }
                }
                error.textContent = failure.message || 'อัปโหลดไม่สำเร็จ กรุณาลองใหม่';
                error.hidden = false;
                status.textContent = '';
            } finally {
                busy = false;
                cancel.hidden = true;
                controls.forEach(([control, disabled]) => control.disabled = disabled);
            }
        });
    });
})();
