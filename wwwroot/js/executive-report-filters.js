(function () {
            function normalize(value) {
                return (value || "").toString().trim().toLowerCase();
            }

            function getSelectedOption(select) {
                return select.options[select.selectedIndex] || null;
            }

            function dispatchSelectChange(select) {
                select.dispatchEvent(new Event("input", { bubbles: true }));
                select.dispatchEvent(new Event("change", { bubbles: true }));
            }

            function enhanceSelect(select) {
                if (!select || select.classList.contains("pt-dropdown-enhanced")) return;

                const wrapper = document.createElement("div");
                wrapper.className = "pt-search-select";

                const field = document.createElement("div");
                field.className = "pt-search-select__field";

                const input = document.createElement("input");
                input.type = "text";
                input.id = select.id + "-search";
                input.className = "pt-search-select__input";
                input.autocomplete = "off";
                input.setAttribute("aria-label", document.querySelector(`label[for="${select.id}"]`)?.textContent || input.placeholder);
                const label = document.querySelector(`label[for="${select.id}"]`);
                if (label) label.htmlFor = input.id;
                select.tabIndex = -1;
                input.placeholder = select.dataset.placeholder || getSelectedOption(select)?.text || "🔍 พิมพ์ค้นหา...";
                const dropdown = document.createElement("div");
                dropdown.className = "pt-search-select__dropdown";

                field.appendChild(input);
                wrapper.appendChild(field);
                wrapper.appendChild(dropdown);
                select.parentNode.insertBefore(wrapper, select);
                wrapper.appendChild(select);
                select.classList.add("pt-native-select-hidden", "pt-dropdown-enhanced");

                const options = Array.from(select.options).map(option => ({
                    value: option.value,
                    text: option.text.trim(),
                    option
                }));

                function syncInput() {
                    const selected = getSelectedOption(select);
                    input.value = selected ? selected.text.trim() : "";                }

                function renderOptions() {
                    const term = normalize(input.value);
                    dropdown.innerHTML = "";

                    const matches = options.filter(item => !item.option.disabled && (!term || normalize(item.text).includes(term)));
                    if (!matches.length) {
                        const empty = document.createElement("div");
                        empty.className = "pt-search-select__empty";
                        empty.textContent = "ไม่พบข้อมูล";
                        dropdown.appendChild(empty);
                        return;
                    }

                    matches.forEach(item => {
                        const button = document.createElement("button");
                        button.type = "button";
                        button.className = "pt-search-select__option";
                        if (item.option.selected) button.classList.add("is-selected");
                        button.textContent = item.text || "--";
                        button.addEventListener("click", () => {
                            select.value = item.value;
                            syncInput();
                            wrapper.classList.remove("is-open");
                            dispatchSelectChange(select);
                        });
                        dropdown.appendChild(button);
                    });
                }

                input.addEventListener("focus", () => {
                    input.value = "";
                    renderOptions();
                    wrapper.classList.add("is-open");
                });

                input.addEventListener("input", () => {
                    renderOptions();
                    wrapper.classList.add("is-open");
                });
                document.addEventListener("click", event => {
                    if (!wrapper.contains(event.target)) { wrapper.classList.remove("is-open"); syncInput(); }
                });

                input.addEventListener("keydown", event => {
                    if (event.key === "Escape") { wrapper.classList.remove("is-open"); syncInput(); }
                    if (event.key === "ArrowDown") { event.preventDefault(); dropdown.querySelector("button")?.focus(); }
                    if (event.key === "Enter") { event.preventDefault(); dropdown.querySelector("button")?.click(); }
                });
                wrapper.addEventListener("focusout", () => {
                    setTimeout(() => { if (!wrapper.contains(document.activeElement)) { wrapper.classList.remove("is-open"); syncInput(); } }, 0);
                });
                syncInput();
                renderOptions();
            }

            document.querySelectorAll("select[data-placeholder]").forEach(enhanceSelect);

})();
