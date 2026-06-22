(function () {
    const inputStates = new WeakMap();
    let pasteImageCounter = 1;

    function uniqueName(file) {
        const lastDot = file.name.lastIndexOf(".");
        const extension = lastDot >= 0 ? file.name.substring(lastDot).toLowerCase() : "";
        return `${file.name}|${file.size}|${file.type}|${extension}`;
    }

    function pastedFileName(blob) {
        const now = new Date();
        const pad = value => String(value).padStart(2, "0");
        const extension = blob.type === "image/jpeg"
            ? "jpg"
            : blob.type === "image/webp"
                ? "webp"
                : "png";

        const stamp = [
            now.getFullYear(),
            pad(now.getMonth() + 1),
            pad(now.getDate()),
            "-",
            pad(now.getHours()),
            pad(now.getMinutes()),
            pad(now.getSeconds())
        ].join("");

        return `pasted-image-${stamp}-${pasteImageCounter++}.${extension}`;
    }

    function ensureStyles() {
        if (document.getElementById("paste-image-upload-style")) return;

        const style = document.createElement("style");
        style.id = "paste-image-upload-style";
        style.textContent = `
            .paste-image-upload-panel {
                margin-top: 10px;
                border: 1px dashed #9db4cf;
                border-radius: 8px;
                background: #f8fbff;
                padding: 12px;
                outline: none;
            }
            .paste-image-upload-panel:focus-within,
            .paste-image-upload-panel.is-active {
                border-color: #14b8a6;
                box-shadow: 0 0 0 3px rgba(20, 184, 166, .14);
            }
            .paste-image-upload-help {
                color: #475569;
                font-size: 13px;
                font-weight: 700;
                line-height: 1.4;
            }
            .paste-image-upload-help small {
                display: block;
                color: #64748b;
                font-weight: 600;
                margin-top: 2px;
            }
            .paste-image-upload-list {
                display: grid;
                grid-template-columns: repeat(auto-fill, minmax(118px, 1fr));
                gap: 10px;
                margin-top: 10px;
            }
            .paste-image-upload-item {
                position: relative;
                border: 1px solid #d8e3ef;
                border-radius: 8px;
                background: #ffffff;
                padding: 6px;
                min-width: 0;
            }
            .paste-image-upload-item img {
                width: 100%;
                aspect-ratio: 1 / 1;
                border-radius: 6px;
                object-fit: cover;
                display: block;
                background: #e2e8f0;
            }
            .paste-image-upload-name {
                color: #334155;
                font-size: 11px;
                font-weight: 700;
                line-height: 1.25;
                margin-top: 6px;
                overflow: hidden;
                text-overflow: ellipsis;
                white-space: nowrap;
            }
            .paste-image-upload-remove {
                position: absolute;
                right: 4px;
                top: 4px;
                width: 24px;
                height: 24px;
                border: 0;
                border-radius: 50%;
                color: #ffffff;
                background: rgba(190, 18, 60, .9);
                font-size: 16px;
                font-weight: 900;
                line-height: 1;
                display: inline-flex;
                align-items: center;
                justify-content: center;
                cursor: pointer;
            }
        `;
        document.head.appendChild(style);
    }

    function createPanel(input) {
        const panel = document.createElement("div");
        panel.className = "paste-image-upload-panel";
        panel.tabIndex = 0;
        panel.dataset.pasteImagePanel = "";
        panel.innerHTML = `
            <div class="paste-image-upload-help">
                คลิกบริเวณนี้แล้วกด Ctrl+V / Cmd+V เพื่อวางรูปจาก clipboard
                <small>รองรับรูปภาพจากการ Copy หรือ Screenshot และยังเลือกไฟล์ได้ตามปกติ</small>
            </div>
            <div class="paste-image-upload-list" data-paste-image-list hidden></div>
        `;
        input.insertAdjacentElement("afterend", panel);
        return panel;
    }

    function fileArrayToDataTransfer(files) {
        const dataTransfer = new DataTransfer();
        files.forEach(file => dataTransfer.items.add(file));
        return dataTransfer;
    }

    function render(input) {
        const state = inputStates.get(input);
        if (!state) return;

        state.list.innerHTML = "";
        state.list.hidden = state.files.length === 0;

        state.files.forEach((file, index) => {
            const item = document.createElement("div");
            item.className = "paste-image-upload-item";

            const image = document.createElement("img");
            image.alt = file.name;
            image.src = URL.createObjectURL(file);
            image.onload = function () {
                URL.revokeObjectURL(image.src);
            };

            const name = document.createElement("div");
            name.className = "paste-image-upload-name";
            name.textContent = file.name;
            name.title = file.name;

            const remove = document.createElement("button");
            remove.type = "button";
            remove.className = "paste-image-upload-remove";
            remove.setAttribute("aria-label", "เอารูปออก");
            remove.textContent = "×";
            remove.addEventListener("click", function () {
                state.files.splice(index, 1);
                syncInput(input);
            });

            item.append(image, name, remove);
            state.list.appendChild(item);
        });
    }

    function syncInput(input) {
        const state = inputStates.get(input);
        if (!state) return;

        state.applying = true;
        input.files = fileArrayToDataTransfer(state.files).files;
        state.applying = false;
        render(input);
    }

    function addFiles(input, files) {
        const state = inputStates.get(input);
        if (!state) return 0;

        const existing = new Set(state.files.map(uniqueName));
        let added = 0;

        files.forEach(file => {
            if (!file.type.startsWith("image/")) return;

            const key = uniqueName(file);
            if (existing.has(key)) return;

            state.files.push(file);
            existing.add(key);
            added++;
        });

        if (added > 0) {
            syncInput(input);
            state.panel.classList.add("is-active");
            window.setTimeout(() => state.panel.classList.remove("is-active"), 900);
        }

        return added;
    }

    function extractImageFiles(event) {
        const files = [];
        const clipboard = event.clipboardData;
        if (!clipboard) return files;

        Array.from(clipboard.items || []).forEach(item => {
            if (!item.type || !item.type.startsWith("image/")) return;

            const blob = item.getAsFile();
            if (!blob) return;

            files.push(new File([blob], pastedFileName(blob), {
                type: blob.type || "image/png",
                lastModified: Date.now()
            }));
        });

        if (files.length === 0) {
            Array.from(clipboard.files || []).forEach(file => {
                if (file.type && file.type.startsWith("image/")) {
                    files.push(file);
                }
            });
        }

        return files;
    }

    function initInput(input) {
        if (inputStates.has(input)) return;

        const panel = createPanel(input);
        const list = panel.querySelector("[data-paste-image-list]");
        inputStates.set(input, {
            files: Array.from(input.files || []),
            panel,
            list,
            applying: false
        });

        input.addEventListener("change", function () {
            const state = inputStates.get(input);
            if (!state || state.applying) return;
            addFiles(input, Array.from(input.files || []));
        });

        panel.addEventListener("paste", function (event) {
            const files = extractImageFiles(event);
            if (files.length === 0) return;

            event.preventDefault();
            addFiles(input, files);
        });

        panel.addEventListener("click", function () {
            panel.focus();
        });

        render(input);
    }

    function targetInputForPaste() {
        const inputs = Array.from(document.querySelectorAll("input[type='file'][data-paste-image-upload]"));
        if (inputs.length === 1) return inputs[0];

        const activePanel = document.activeElement?.closest?.("[data-paste-image-panel]");
        if (activePanel) {
            return inputs.find(input => inputStates.get(input)?.panel === activePanel) || null;
        }

        return null;
    }

    document.addEventListener("DOMContentLoaded", function () {
        ensureStyles();
        document.querySelectorAll("input[type='file'][data-paste-image-upload]").forEach(initInput);
    });

    document.addEventListener("paste", function (event) {
        if (event.defaultPrevented) return;

        const input = targetInputForPaste();
        if (!input) return;

        const files = extractImageFiles(event);
        if (files.length === 0) return;

        event.preventDefault();
        addFiles(input, files);
    });
})();
