// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

(function () {
    const enhancedClass = "pt-dropdown-enhanced";

    function normalize(value) {
        return (value || "").toString().trim().toLowerCase();
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

        const clearButton = document.createElement("button");
        clearButton.type = "button";
        clearButton.className = "pt-search-select__clear";
        clearButton.title = "เลือกใหม่";
        clearButton.setAttribute("aria-label", "เลือกใหม่");
        clearButton.textContent = "×";
        clearButton.disabled = select.disabled;

        const dropdown = document.createElement("div");
        dropdown.className = "pt-search-select__dropdown";

        field.appendChild(input);
        field.appendChild(clearButton);
        wrapper.appendChild(field);
        wrapper.appendChild(dropdown);

        select.insertAdjacentElement("afterend", wrapper);
        select.classList.add(enhancedClass, "pt-native-select-hidden");
        select.tabIndex = -1;

        function allowClear() {
            return hasEmptyOption(select) || !wasRequired;
        }

        function validateInput() {
            if (!wasRequired) return;
            input.setCustomValidity(select.value ? "" : "กรุณาเลือกจากรายการ");
        }

        function syncInputFromSelect() {
            const selected = getSelectedOption(select);
            input.value = selected && selected.value !== "" ? selected.text : "";
            clearButton.classList.toggle("is-visible", Boolean(select.value) && allowClear());
            validateInput();
        }

        function closeDropdown() {
            wrapper.classList.remove("is-open");
        }

        function openDropdown() {
            if (select.disabled) return;
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

        function clearValue() {
            if (!allowClear()) return;
            select.value = "";
            input.value = "";
            closeDropdown();
            syncInputFromSelect();
            dispatchSelectChange(select);
        }

        function renderOptions(filter = "") {
            dropdown.innerHTML = "";

            const keyword = normalize(filter);
            const options = Array.from(select.options)
                .filter(option => !option.disabled)
                .filter(option => option.value !== "")
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

        clearButton.addEventListener("click", clearValue);

        select.addEventListener("change", syncInputFromSelect);

        document.addEventListener("click", function (event) {
            if (!wrapper.contains(event.target)) {
                closeDropdown();
                syncInputFromSelect();
            }
        });

        syncInputFromSelect();
    }

    function initProjectDropdowns(root) {
        const scope = root || document;
        scope.querySelectorAll("select").forEach(enhanceSelect);
    }

    window.ProjectTrackingDropdowns = {
        init: initProjectDropdowns
    };

    document.addEventListener("DOMContentLoaded", function () {
        setTimeout(function () {
            initProjectDropdowns(document);
        }, 0);
    });
})();
