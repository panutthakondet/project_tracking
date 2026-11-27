(() => {
    const table = document.getElementById("sync-structure");
    if (!table?.dataset.oracleColumns) return;

    const oracleColumns = JSON.parse(table.dataset.oracleColumns)
        .map(columns => new Set(columns.map(column => column.toUpperCase())));

    for (const row of table.querySelectorAll("[data-sum-candidates]")) {
        const oracle = row.querySelector("[data-sync-oracle]");
        const value = row.querySelector("[data-sum-value]");
        const picker = row.querySelector("[data-sum-picker]");
        const options = row.querySelector("[data-sum-options]");
        const empty = row.querySelector("[data-sum-empty]");
        const count = row.querySelector("[data-sum-count]");
        const candidates = JSON.parse(row.dataset.sumCandidates);

        function updateSelection() {
            const selected = Array.from(options.querySelectorAll("input:checked"), input => input.value);
            value.value = selected.join(",");
            count.textContent = selected.length ? `(${selected.length})` : "";
        }

        function renderOptions() {
            const columns = oracleColumns[Number(oracle.value)];
            const selected = new Set(value.value.split(",").map(column => column.trim().toUpperCase()));
            const available = candidates.filter(column => columns?.has(column.toUpperCase()));
            options.replaceChildren();

            for (const column of available) {
                const label = document.createElement("label");
                label.className = "d-flex align-items-center gap-2 mb-1";
                const checkbox = document.createElement("input");
                checkbox.type = "checkbox";
                checkbox.value = column;
                checkbox.checked = selected.has(column.toUpperCase());
                label.append(checkbox, document.createTextNode(column));
                options.append(label);
            }

            picker.hidden = available.length === 0;
            empty.hidden = !picker.hidden;
            empty.textContent = columns ? "ไม่มีฟิลด์ SUM" : "เลือก Oracle ก่อน";
            updateSelection();
        }

        oracle.addEventListener("change", renderOptions);
        options.addEventListener("change", updateSelection);
        renderOptions();
    }
})();
