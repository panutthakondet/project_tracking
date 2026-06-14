// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

(function () {
    const enhancedClass = "pt-dropdown-enhanced";
    const coopFilterClass = "pt-project-coop-filter";

    function normalize(value) {
        return (value || "").toString().trim().toLowerCase();
    }

    function extractCoopName(option) {
        if (!option || option.value === "") return "";

        const explicitName = (option.dataset.coopName || "").trim();
        if (explicitName) return explicitName;

        const text = (option.textContent || option.text || "").trim();
        const separator = " - ";
        const separatorIndex = text.indexOf(separator);
        if (separatorIndex <= 0) return "";

        const possibleCoop = text.substring(0, separatorIndex).trim();
        return possibleCoop.startsWith("สหกรณ์") || possibleCoop.startsWith("สอ.")
            ? possibleCoop
            : "";
    }

    function extractProjectName(option) {
        if (!option || option.value === "") return "";

        const explicitName = (option.dataset.projectName || "").trim();
        if (explicitName) return explicitName;

        const text = (option.textContent || option.text || "").trim();
        const coopName = extractCoopName(option);
        const separator = " - ";
        if (coopName && text.startsWith(`${coopName}${separator}`)) {
            return text.substring(`${coopName}${separator}`.length).trim();
        }

        return text;
    }

    function prepareProjectOption(option) {
        if (!option || option.value === "") return;

        const coopName = extractCoopName(option);
        if (coopName) {
            option.dataset.coopName = coopName;
        }

        const projectName = extractProjectName(option);
        if (projectName) {
            option.dataset.projectName = projectName;
            option.textContent = projectName;
        }
    }

    function shouldEnhance(select) {
        if (!select || select.dataset.ptDropdown === "off") return false;
        if (select.classList.contains(enhancedClass)) return false;
        if (select.multiple) return false;
        if (Number(select.getAttribute("size") || "1") > 1) return false;
        if (select.closest(".pt-search-select")) return false;

        const style = window.getComputedStyle(select);
        if (style.display === "none" || select.hidden) return false;

        return true;
    }

    function destroySelect2(select) {
        if (!window.jQuery) return;

        const $select = window.jQuery(select);
        if (!$select.data("select2")) return;

        try {
            $select.select2("destroy");
        } catch (_) {
            const container = select.nextElementSibling;
            if (container && container.classList.contains("select2-container")) {
                container.remove();
            }
        }
    }

    function getSelectedOption(select) {
        return select.options[select.selectedIndex] || null;
    }

    function hasEmptyOption(select) {
        return Array.from(select.options).some(option => option.value === "");
    }

    function dispatchSelectChange(select) {
        select.dispatchEvent(new Event("input", { bubbles: true }));
        select.dispatchEvent(new Event("change", { bubbles: true }));
    }

    function restoreNativeSelect(select) {
        if (!select) return;

        select.classList.remove(enhancedClass, "pt-native-select-hidden");
        select.removeAttribute("tabindex");

        if (select.dataset.ptWasRequired === "true") {
            select.required = true;
            delete select.dataset.ptWasRequired;
        }

        destroySelect2(select);
    }

    function restoreNativeSearchSelects(root) {
        const scope = root || document;

        scope.querySelectorAll(".pt-search-select").forEach(wrapper => {
            const select = wrapper.previousElementSibling;
            if (select && select.matches("select")) {
                restoreNativeSelect(select);
            }
            wrapper.remove();
        });

        scope.querySelectorAll("select.pt-native-select-hidden, select.pt-dropdown-enhanced").forEach(restoreNativeSelect);
    }

    function enhanceSelect(select) {
        if (!shouldEnhance(select)) return;

        destroySelect2(select);

        const wrapper = document.createElement("div");
        wrapper.className = "pt-search-select";
        if (select.disabled) wrapper.classList.add("is-disabled");

        const wasRequired = select.required;
        if (wasRequired) {
            select.dataset.ptWasRequired = "true";
            select.required = false;
        }

        const field = document.createElement("div");
        field.className = "pt-search-select__field";

        const input = document.createElement("input");
        input.type = "text";
        input.className = "form-control form-control-lg pt-search-select__input";
        input.autocomplete = "off";
        input.disabled = select.disabled;
        input.required = wasRequired;
        input.placeholder =
            select.dataset.placeholder ||
            select.getAttribute("placeholder") ||
            getSelectedOption(select)?.text ||
            "🔍 พิมพ์ค้นหา...";

        const dropdown = document.createElement("div");
        dropdown.className = "pt-search-select__dropdown";

        field.appendChild(input);
        wrapper.appendChild(field);
        wrapper.appendChild(dropdown);

        select.insertAdjacentElement("afterend", wrapper);
        select.classList.add(enhancedClass, "pt-native-select-hidden");
        select.tabIndex = -1;

        function validateInput() {
            if (!wasRequired) return;
            input.setCustomValidity(select.value ? "" : "กรุณาเลือกจากรายการ");
        }

        function syncInputFromSelect() {
            const selected = getSelectedOption(select);
            const showEmptyOptionText = select.dataset.showEmptyOptionText === "true";
            input.value = selected && (selected.value !== "" || showEmptyOptionText) ? selected.text : "";
            validateInput();
        }

        function closeDropdown() {
            wrapper.classList.remove("is-open");
        }

        function openDropdown() {
            if (select.disabled) return;
            input.value = "";
            renderOptions(input.value);
            wrapper.classList.add("is-open");
        }

        function selectValue(value, text) {
            select.value = value;
            input.value = value ? text : "";
            closeDropdown();
            syncInputFromSelect();
            dispatchSelectChange(select);
        }

        function renderOptions(filter = "") {
            dropdown.innerHTML = "";

            const keyword = normalize(filter);
            const options = Array.from(select.options)
                .filter(option => !option.disabled && !option.hidden && option.dataset.ptFilterHidden !== "true")
                .filter(option => {
                    const haystack = normalize(`${option.text} ${option.value}`);
                    return !keyword || haystack.includes(keyword);
                });

            if (options.length === 0) {
                const empty = document.createElement("div");
                empty.className = "pt-search-select__empty";
                empty.textContent = "ไม่พบข้อมูล";
                dropdown.appendChild(empty);
                return;
            }

            options.forEach(option => {
                const item = document.createElement("button");
                item.type = "button";
                item.className = "pt-search-select__option";
                if (option.value === select.value) item.classList.add("is-selected");
                item.textContent = option.text;

                item.addEventListener("click", function () {
                    selectValue(option.value, option.text);
                });

                dropdown.appendChild(item);
            });
        }

        input.addEventListener("focus", openDropdown);
        input.addEventListener("click", openDropdown);
        input.addEventListener("input", function () {
            renderOptions(input.value);
            wrapper.classList.add("is-open");
            validateInput();
        });
        input.addEventListener("keydown", function (event) {
            if (event.key === "Escape") {
                closeDropdown();
                syncInputFromSelect();
            }
        });

        select.addEventListener("change", syncInputFromSelect);
        select.addEventListener("pt-dropdown-refresh", function () {
            renderOptions(input.value);
            syncInputFromSelect();
        });

        document.addEventListener("click", function (event) {
            if (!wrapper.contains(event.target)) {
                closeDropdown();
                syncInputFromSelect();
            }
        });

        syncInputFromSelect();
    }

    function findProjectField(select) {
        return select.closest(".field, [class*='col-'], .form-group, .mb-3") || select.parentElement;
    }

    function buildCoopField(projectField) {
        const field = document.createElement("div");
        const isGridColumn = projectField && /\bcol(?:-\d+|-sm-\d+|-md-\d+|-lg-\d+|-xl-\d+|-xxl-\d+)?\b/.test(projectField.className || "");
        field.className = isGridColumn
            ? `col-12 ${coopFilterClass}`
            : projectField && projectField.className
            ? `${projectField.className} ${coopFilterClass}`
            : `field ${coopFilterClass}`;

        const label = document.createElement("label");
        label.className = "form-label fw-semibold";
        label.textContent = "🏢 สหกรณ์";

        const select = document.createElement("select");
        select.className = "form-select";
        select.dataset.placeholder = "🔍 พิมพ์ค้นหาสหกรณ์...";

        field.appendChild(label);
        field.appendChild(select);
        return { field, select };
    }

    function normalizeProjectLabel(projectField) {
        const label = projectField?.querySelector("label");
        if (!label) return;

        const text = (label.textContent || "").trim().toLowerCase();
        if (text.includes("project") || text.includes("โครงการ")) {
            label.textContent = "📁 โครงการ";
        }
    }

    function updateProjectOptionVisibility(projectSelect, coopName) {
        const normalizedCoop = normalize(coopName);
        let selectedStillVisible = true;

        Array.from(projectSelect.options).forEach(option => {
            if (option.value === "") {
                option.hidden = false;
                option.disabled = false;
                option.dataset.ptFilterHidden = "false";
                return;
            }

            const optionCoop = extractCoopName(option);
            const visible = !normalizedCoop || normalize(optionCoop) === normalizedCoop;
            option.hidden = !visible;
            option.disabled = !visible;
            option.dataset.ptFilterHidden = visible ? "false" : "true";

            if (option.selected && !visible) {
                selectedStillVisible = false;
            }
        });

        if (!selectedStillVisible) {
            projectSelect.value = "";
        }

        projectSelect.dispatchEvent(new Event("pt-dropdown-refresh"));
    }

    function initProjectCoopFilters(root) {
        const scope = root || document;
        const projectSelects = Array.from(scope.querySelectorAll("select[name='projectId'], select[name='ProjectId'], select[name='projectName'], select[name='ProjectName'], select[id='projectSelect'], select[id='projectId'], select[id='projectName'], select[id='ProjectName']"))
            .filter(select => select.dataset.projectCoopFilter !== "off")
            .filter(select => select.dataset.projectCoopFilterReady !== "true")
            .filter(select => {
                const style = window.getComputedStyle(select);
                return style.display !== "none" && !select.hidden;
            });

        projectSelects.forEach(projectSelect => {
            const options = Array.from(projectSelect.options);
            options.forEach(prepareProjectOption);

            const coopNames = Array.from(new Set(options.map(extractCoopName).filter(Boolean)))
                .sort((a, b) => a.localeCompare(b, "th"));

            if (coopNames.length === 0) return;

            const projectField = findProjectField(projectSelect);
            if (!projectField || !projectField.parentNode) return;
            projectField.classList.add("pt-project-field-with-coop");
            normalizeProjectLabel(projectField);

            const existingField = projectField.previousElementSibling;
            if (existingField && existingField.classList.contains(coopFilterClass)) return;

            const { field, select: coopSelect } = buildCoopField(projectField);
            coopSelect.innerHTML = "";

            const allOption = document.createElement("option");
            allOption.value = "";
            allOption.textContent = "-- ทุกสหกรณ์ --";
            coopSelect.appendChild(allOption);

            coopNames.forEach(name => {
                const option = document.createElement("option");
                option.value = name;
                option.textContent = name;
                coopSelect.appendChild(option);
            });

            const selectedProjectCoop = extractCoopName(projectSelect.options[projectSelect.selectedIndex]);
            if (selectedProjectCoop) {
                coopSelect.value = selectedProjectCoop;
            }

            projectField.parentNode.insertBefore(field, projectField);
            projectSelect.dataset.projectCoopFilterReady = "true";
            updateProjectOptionVisibility(projectSelect, coopSelect.value);

            coopSelect.addEventListener("change", function () {
                updateProjectOptionVisibility(projectSelect, coopSelect.value);
            });
        });
    }

    function initProjectDropdowns(root) {
        const scope = root || document;
        initProjectCoopFilters(scope);
        restoreNativeSearchSelects(scope);
        scope.querySelectorAll("select").forEach(enhanceSelect);
    }

    window.ProjectTrackingDropdowns = {
        init: initProjectDropdowns,
        initProjectCoopFilters
    };

    document.addEventListener("DOMContentLoaded", function () {
        setTimeout(function () {
            initProjectDropdowns(document);
        }, 0);
    });
})();

(function () {
    const notePositionStorageKey = "projectTracking.requirementCardNote.position";

    function clamp(value, min, max) {
        return Math.min(Math.max(value, min), max);
    }

    function setText(id, value) {
        const element = document.getElementById(id);
        if (element) element.textContent = value || "-";
        return element;
    }

    function clearElement(element) {
        if (!element) return;
        while (element.firstChild) {
            element.removeChild(element.firstChild);
        }
    }

    function escapeHtml(value) {
        return (value || "")
            .toString()
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    function sanitizeRequirementDetailHtml(value) {
        const text = (value || "").toString().trim();
        if (!text) return "";

        if (!/<\/?[a-z][\s\S]*>/i.test(text)) {
            return escapeHtml(text).replace(/\r\n|\r|\n/g, "<br>");
        }

        const template = document.createElement("template");
        template.innerHTML = text;
        const allowedTags = new Set(["A", "B", "BR", "DIV", "EM", "I", "LI", "OL", "P", "SPAN", "STRONG", "U", "UL"]);

        template.content.querySelectorAll("*").forEach(node => {
            if (!allowedTags.has(node.tagName)) {
                node.replaceWith(document.createTextNode(node.textContent || ""));
                return;
            }

            Array.from(node.attributes).forEach(attribute => {
                const name = attribute.name.toLowerCase();
                if (node.tagName === "A" && name === "href") {
                    const href = attribute.value.trim();
                    if (/^(https?:|mailto:|tel:|\/)/i.test(href)) return;
                }
                node.removeAttribute(attribute.name);
            });
        });

        return template.innerHTML;
    }

    function createActionLink(label, href, variant) {
        const link = document.createElement("a");
        link.className = `btn btn-outline-${variant || "secondary"}`;
        link.href = href || "#";
        link.target = "_blank";
        link.rel = "noopener";
        link.textContent = label;
        return link;
    }

    function renderAttachment(file) {
        const row = document.createElement("div");
        row.className = "requirement-card-popup__file";

        const thumb = document.createElement("a");
        thumb.className = "requirement-card-popup__thumb";
        thumb.href = file.previewUrl || "#";
        thumb.target = "_blank";
        thumb.rel = "noopener";

        if (file.isImage && file.filePath) {
            const img = document.createElement("img");
            img.src = file.filePath;
            img.alt = file.fileName || "Attachment";
            thumb.appendChild(img);
        } else {
            thumb.textContent = "FILE";
        }

        const info = document.createElement("div");
        const name = document.createElement("div");
        name.className = "requirement-card-popup__file-name";
        name.textContent = file.fileName || "-";

        const meta = document.createElement("div");
        meta.className = "requirement-card-popup__file-info";
        meta.textContent = `${file.fileSize || "-"} · ${file.uploadedAt || "-"}`;

        info.appendChild(name);
        info.appendChild(meta);

        const actions = document.createElement("div");
        actions.className = "requirement-card-popup__file-actions";
        actions.appendChild(createActionLink("Preview", file.previewUrl, "primary"));
        actions.appendChild(createActionLink("ดาวน์โหลด", file.downloadUrl, "secondary"));

        row.appendChild(thumb);
        row.appendChild(info);
        row.appendChild(actions);
        return row;
    }

    function renderPhaseItem(item) {
        const row = document.createElement("div");
        row.className = "requirement-card-popup__phase";

        const head = document.createElement("div");
        head.className = "requirement-card-popup__phase-head";

        const title = document.createElement("div");
        title.className = "requirement-card-popup__phase-title";
        title.textContent = item.phaseName || "-";

        const label = document.createElement("div");
        label.className = "requirement-card-popup__phase-pill";
        label.textContent = item.phasePeriodLabel || `ส่วนที่ ${item.phaseOrder || "-"} งวดที่ ${item.periodOrder || "-"}`;

        head.appendChild(title);
        head.appendChild(label);

        const meta = document.createElement("div");
        meta.className = "requirement-card-popup__phase-meta";
        meta.textContent = `Plan ${item.planDate || "-"} · Period ${item.periodDate || "-"}`;

        row.appendChild(head);
        row.appendChild(meta);
        return row;
    }

    function renderRequirementCardPopup(card) {
        const header = document.getElementById("RequirementCardDetailHeader");
        if (header) {
            header.classList.toggle("has-cover", Boolean(card.coverImagePath));
            header.style.backgroundImage = card.coverImagePath
                ? `linear-gradient(180deg, rgba(8, 22, 41, .22), rgba(8, 22, 41, .5)), url("${card.coverImagePath}")`
                : "";
        }

        setText("RequirementCardDetailEyebrow", `Project Card #${card.cardId || "-"}`);
        setText("RequirementCardDetailTitle", card.title || "-");
        setText(
            "RequirementCardDetailMeta",
            `List: ${card.columnName || "-"} · สร้างโดย ${card.createdBy || "-"} · อัปเดต ${card.updatedAt || "-"}`
        );

        const detail = document.getElementById("RequirementCardDetailText");
        if (detail) {
            detail.innerHTML = sanitizeRequirementDetailHtml(card.detail) || "ไม่มีรายละเอียด";
            detail.classList.toggle("requirement-card-popup__empty", !card.detail);
        }

        const phaseItems = Array.isArray(card.phaseItems) ? card.phaseItems : [];
        setText("RequirementCardPhaseCount", phaseItems.length.toString());

        const phaseList = document.getElementById("RequirementCardPhaseList");
        clearElement(phaseList);
        if (phaseList) {
            if (phaseItems.length === 0) {
                const empty = document.createElement("div");
                empty.className = "requirement-card-popup__detail requirement-card-popup__empty";
                empty.textContent = "ยังไม่มีร่างส่วนงาน/งวดงาน";
                phaseList.appendChild(empty);
            } else {
                phaseItems.forEach(item => {
                    phaseList.appendChild(renderPhaseItem(item));
                });
            }
        }

        const attachments = Array.isArray(card.attachments) ? card.attachments : [];
        setText("RequirementCardAttachmentCount", attachments.length.toString());

        const list = document.getElementById("RequirementCardAttachmentList");
        clearElement(list);
        if (!list) return;

        if (attachments.length === 0) {
            const empty = document.createElement("div");
            empty.className = "requirement-card-popup__detail requirement-card-popup__empty";
            empty.textContent = "ยังไม่มีไฟล์แนบ";
            list.appendChild(empty);
            return;
        }

        attachments.forEach(file => {
            list.appendChild(renderAttachment(file));
        });
    }

    function cleanupBootstrapModalState() {
        document.querySelectorAll(".modal-backdrop").forEach(backdrop => backdrop.remove());
        document.body.classList.remove("modal-open");
        document.body.style.removeProperty("overflow");
        document.body.style.removeProperty("padding-right");
    }

    function openRequirementCardNote() {
        const note = document.getElementById("RequirementCardDetailModal");
        if (!note) return;

        if (note.parentElement !== document.body) {
            document.body.appendChild(note);
        }

        applyStoredRequirementCardNotePosition(note);
        cleanupBootstrapModalState();
        note.classList.add("is-open");
        note.setAttribute("aria-hidden", "false");
    }

    function closeRequirementCardNote() {
        const note = document.getElementById("RequirementCardDetailModal");
        if (!note) return;

        note.classList.remove("is-open");
        note.setAttribute("aria-hidden", "true");
    }

    function getRequirementCardNotePositionBounds(note) {
        const rect = note.getBoundingClientRect();
        const margin = 8;
        return {
            maxLeft: Math.max(margin, window.innerWidth - rect.width - margin),
            maxTop: Math.max(margin, window.innerHeight - rect.height - margin),
            margin
        };
    }

    function setRequirementCardNotePosition(note, left, top) {
        const bounds = getRequirementCardNotePositionBounds(note);
        note.style.setProperty("--requirement-card-note-left", `${clamp(left, bounds.margin, bounds.maxLeft)}px`);
        note.style.setProperty("--requirement-card-note-top", `${clamp(top, bounds.margin, bounds.maxTop)}px`);
    }

    function applyStoredRequirementCardNotePosition(note) {
        if (window.matchMedia?.("(max-width: 575.98px)").matches) {
            note.style.removeProperty("--requirement-card-note-left");
            note.style.removeProperty("--requirement-card-note-top");
            return;
        }

        let savedPosition = null;
        try {
            savedPosition = JSON.parse(window.localStorage?.getItem(notePositionStorageKey) || "null");
        } catch {
            savedPosition = null;
        }

        if (!savedPosition || !Number.isFinite(savedPosition.left) || !Number.isFinite(savedPosition.top)) return;
        setRequirementCardNotePosition(note, savedPosition.left, savedPosition.top);
    }

    function initRequirementCardNoteDrag() {
        const note = document.getElementById("RequirementCardDetailModal");
        const header = document.getElementById("RequirementCardDetailHeader");
        if (!note || !header || header.dataset.dragReady === "true") return;
        header.dataset.dragReady = "true";

        let startX = 0;
        let startY = 0;
        let startLeft = 0;
        let startTop = 0;

        function stopDrag() {
            note.classList.remove("is-dragging");
            document.removeEventListener("pointermove", drag);
            document.removeEventListener("pointerup", stopDrag);
            document.removeEventListener("pointercancel", stopDrag);

            const rect = note.getBoundingClientRect();
            window.localStorage?.setItem(notePositionStorageKey, JSON.stringify({
                left: Math.round(rect.left),
                top: Math.round(rect.top)
            }));
        }

        function drag(event) {
            setRequirementCardNotePosition(
                note,
                startLeft + event.clientX - startX,
                startTop + event.clientY - startY
            );
        }

        header.addEventListener("pointerdown", function (event) {
            if (window.matchMedia?.("(max-width: 575.98px)").matches) return;
            if (event.target.closest("button, a, input, textarea, select")) return;

            event.preventDefault();
            startX = event.clientX;
            startY = event.clientY;
            const rect = note.getBoundingClientRect();
            startLeft = rect.left;
            startTop = rect.top;
            note.classList.add("is-dragging");
            header.setPointerCapture?.(event.pointerId);
            document.addEventListener("pointermove", drag);
            document.addEventListener("pointerup", stopDrag);
            document.addEventListener("pointercancel", stopDrag);
        });

        window.addEventListener("resize", function () {
            if (!note.classList.contains("is-open")) return;
            applyStoredRequirementCardNotePosition(note);
        });
    }

    function initRequirementCardDetailButton(root) {
        const scope = root || document;
        const buttons = Array.from(scope.querySelectorAll
            ? scope.querySelectorAll(".project-board-card-detail, #RequirementCardDetailButton")
            : document.querySelectorAll(".project-board-card-detail, #RequirementCardDetailButton"));

        buttons.forEach(button => {
            const selectId = button.dataset.cardFieldId || "RequirementCardId";
            const select = scope.getElementById
                ? scope.getElementById(selectId)
                : document.getElementById(selectId);
            const projectFieldId = button.dataset.projectFieldId || "ProjectId";
            const projectField = scope.getElementById
                ? scope.getElementById(projectFieldId)
                : document.getElementById(projectFieldId);

            if ((!select && !projectField) || button.dataset.popupReady === "true") return;
            button.dataset.popupReady = "true";

            const cardTemplate = button.dataset.detailUrlTemplate || "";
            const projectTemplate = button.dataset.detailProjectUrlTemplate || "";

            function getDetailUrl() {
                const cardId = select?.value || "";
                if (cardId && cardTemplate) {
                    return cardTemplate.replace("__CARD_ID__", encodeURIComponent(cardId));
                }

                const projectId = projectField?.value || "";
                if (projectId && projectTemplate) {
                    return projectTemplate.replace("__PROJECT_ID__", encodeURIComponent(projectId));
                }

                return "";
            }

            function syncButton() {
                const hasUrl = Boolean(getDetailUrl());
                button.classList.toggle("is-hidden", !hasUrl);
                button.disabled = !hasUrl;
            }

            async function openDetailPopup() {
                const url = getDetailUrl();
                if (!url) return;

                const originalText = button.dataset.originalText || button.textContent.trim() || "🔎 ดูรายละเอียด";
                button.dataset.originalText = originalText;
                button.disabled = true;
                button.textContent = "กำลังโหลด...";

                try {
                    const response = await fetch(url, { headers: { Accept: "application/json" } });
                    if (!response.ok) throw new Error("โหลดข้อมูลการ์ดไม่สำเร็จ");

                    const card = await response.json();
                    renderRequirementCardPopup(card);
                    openRequirementCardNote();
                } catch (error) {
                    window.alert(error.message || "โหลดข้อมูลการ์ดไม่สำเร็จ");
                } finally {
                    button.textContent = originalText;
                    syncButton();
                }
            }

            select?.addEventListener("change", syncButton);
            projectField?.addEventListener("change", syncButton);
            button.addEventListener("click", openDetailPopup);
            syncButton();
        });
    }

    window.ProjectTrackingRequirementCardPopup = {
        init: initRequirementCardDetailButton
    };

    document.addEventListener("DOMContentLoaded", function () {
        initRequirementCardDetailButton(document);
        initRequirementCardNoteDrag();
    });

    document.addEventListener("click", function (event) {
        if (event.target.closest("[data-requirement-card-close]")) {
            closeRequirementCardNote();
        }
    });
})();

(function () {
    const enhancedFlag = "appDateEnhanced";
    const textDateSelector = "input.thai-date, input[data-app-date], input[id='MeetingDateDisplay']";

    function formatTypedThaiDate(value) {
        let digits = (value || "").replace(/\D/g, "");
        if (digits.length > 8) digits = digits.substring(0, 8);

        if (digits.length >= 5) {
            return `${digits.substring(0, 2)}/${digits.substring(2, 4)}/${digits.substring(4)}`;
        }

        if (digits.length >= 3) {
            return `${digits.substring(0, 2)}/${digits.substring(2)}`;
        }

        return digits;
    }

    function thaiDateToIso(value) {
        const match = (value || "").trim().match(/^(\d{1,2})\/(\d{1,2})\/(\d{2,4})$/);
        if (!match) return "";

        const day = Number(match[1]);
        const month = Number(match[2]);
        let year = Number(match[3]);
        if (year < 100) year += 2500;
        if (year > 2400) year -= 543;

        if (!day || !month || !year || month < 1 || month > 12 || day < 1 || day > 31) return "";

        const date = new Date(year, month - 1, day);
        if (date.getFullYear() !== year || date.getMonth() !== month - 1 || date.getDate() !== day) return "";

        return `${String(year).padStart(4, "0")}-${String(month).padStart(2, "0")}-${String(day).padStart(2, "0")}`;
    }

    function isoDateToThai(value) {
        const match = (value || "").match(/^(\d{4})-(\d{2})-(\d{2})$/);
        if (!match) return "";

        return `${match[3]}/${match[2]}/${Number(match[1]) + 543}`;
    }

    function dispatchDateEvents(input) {
        input.dispatchEvent(new Event("input", { bubbles: true }));
        input.dispatchEvent(new Event("change", { bubbles: true }));
    }

    function wrapInput(input) {
        if (input.closest(".app-date-input-wrap")) return input.closest(".app-date-input-wrap");

        const wrapper = document.createElement("div");
        wrapper.className = "app-date-input-wrap";
        input.parentNode.insertBefore(wrapper, input);
        wrapper.appendChild(input);
        return wrapper;
    }

    function openNativePicker(input) {
        input.focus({ preventScroll: true });

        if (typeof input.showPicker === "function") {
            input.showPicker();
            return;
        }

        input.click();
    }

    function enhanceNativeDate(input) {
        if (!input || input.dataset[enhancedFlag] === "true") return;
        if (input.hidden || input.type !== "date") return;
        if (input.closest(".rb-date-input-wrap") || input.classList.contains("rb-date-native")) return;
        if (input.classList.contains("app-date-native")) return;

        input.dataset[enhancedFlag] = "true";
        input.classList.add("app-date-input");

        const wrapper = wrapInput(input);
        const button = document.createElement("button");
        button.type = "button";
        button.className = "app-date-button";
        button.setAttribute("aria-label", "เลือกวันที่");
        button.textContent = "📅";

        wrapper.appendChild(button);
        button.addEventListener("click", () => openNativePicker(input));
    }

    function enhanceTextDate(input) {
        if (!input || input.dataset[enhancedFlag] === "true") return;
        if (input.hidden || input.type === "hidden") return;
        if (input.closest(".rb-date-input-wrap")) return;

        input.dataset[enhancedFlag] = "true";
        input.classList.add("app-date-input");
        input.setAttribute("inputmode", "numeric");
        input.setAttribute("autocomplete", input.getAttribute("autocomplete") || "off");

        const wrapper = wrapInput(input);

        const nativeInput = document.createElement("input");
        nativeInput.type = "date";
        nativeInput.className = "app-date-native";
        nativeInput.tabIndex = -1;
        nativeInput.setAttribute("aria-hidden", "true");
        nativeInput.value = thaiDateToIso(input.value);

        const button = document.createElement("button");
        button.type = "button";
        button.className = "app-date-button";
        button.setAttribute("aria-label", "เลือกวันที่");
        button.textContent = "📅";

        wrapper.appendChild(button);
        wrapper.appendChild(nativeInput);

        input.addEventListener("input", function () {
            const cursorAtEnd = input.selectionStart === input.value.length;
            input.value = formatTypedThaiDate(input.value);
            nativeInput.value = thaiDateToIso(input.value);
            if (cursorAtEnd) input.setSelectionRange(input.value.length, input.value.length);
        });

        input.addEventListener("change", function () {
            nativeInput.value = thaiDateToIso(input.value);
        });

        button.addEventListener("click", function () {
            nativeInput.value = thaiDateToIso(input.value);
            openNativePicker(nativeInput);
        });

        nativeInput.addEventListener("change", function () {
            if (!nativeInput.value) return;
            input.value = isoDateToThai(nativeInput.value);
            dispatchDateEvents(input);
            input.focus({ preventScroll: true });
        });
    }

    function enhanceDateInputs(root) {
        const scope = root || document;

        scope.querySelectorAll(textDateSelector).forEach(enhanceTextDate);
        scope.querySelectorAll("input[type='date']").forEach(enhanceNativeDate);
    }

    window.ProjectTrackingDateInputs = {
        init: enhanceDateInputs,
        thaiDateToIso,
        isoDateToThai
    };

    document.addEventListener("DOMContentLoaded", function () {
        enhanceDateInputs(document);
    });
})();
