(() => {
    "use strict";

    const factorConfig = {
        hueso: {
            modalId: "modalHueso",
            tableId: "tabla-hueso",
            catalogId: "catalogoHueso",
            lotId: "lote-hueso",
            label: "Hueso",
            defaultImage: "/images/Hueso.png"
        },
        recorte: {
            modalId: "modalRecorte",
            tableId: "tabla-Recorte",
            catalogId: "catalogoRecorte",
            lotId: "lote-Recorte",
            label: "Recorte",
            defaultImage: "/images/Recorte-80-20.png"
        },
        grasa: {
            modalId: "modalGrasa",
            tableId: "tabla-Grasa",
            catalogId: "catalogoGrasa",
            lotId: "lote-Grasa",
            label: "Grasa",
            defaultImage: "/images/Rib con Grasa.jpeg"
        }
    };

    const BONE_TOTAL_OBJECTIVE = 5.5;

    const boneImageRules = [
        { terms: ["cabeza", "filete"], image: "/images/FactorCritico/HuesoCabezaFilete.jpg" },
        { terms: ["cadena", "new", "york"], image: "/images/FactorCritico/HuesoCadenaNewYork.png" },
        { terms: ["tablillas", "new", "york"], image: "/images/FactorCritico/Tablillas New York.jpeg" },
        { terms: ["cadena", "rib", "eye"], image: "/images/FactorCritico/HuesoCadenaRibEye.jpg" },
        { terms: ["cadera"], image: "/images/FactorCritico/Cadera.jpeg" },
        { terms: ["cartilago"], image: "/images/FactorCritico/Tamalilla.jpeg" },
        { terms: ["choco"], image: "/images/FactorCritico/Choco.jpeg" },
        { terms: ["codo"], image: "/images/FactorCritico/Codo.jpeg" },
        { terms: ["escapula"], image: "/images/FactorCritico/HuesoEscapula.jpeg" },
        { terms: ["espina", "diezmillo"], image: "/images/FactorCritico/HuesoEspinasdeDiezmillo.jpg" },
        { terms: ["espina", "rib", "eye"], image: "/images/FactorCritico/Espinas Rib Eye.jpeg" },
        { terms: ["esternon"], image: "/images/FactorCritico/HuesoEsternon.jpg" },
        { terms: ["femur"], image: "/images/FactorCritico/Puntas Femur.jpeg" },
        { terms: ["humero"], image: "/images/FactorCritico/Hueso.png" },
        { terms: ["munon"], image: "/images/FactorCritico/Muñon.jpeg" },
        { terms: ["perico"], image: "/images/FactorCritico/Perico.jpeg" },
        { terms: ["radio"], image: "/images/FactorCritico/HuesoRadio.jpg" },
        { terms: ["punta", "retazo"], image: "/images/FactorCritico/HuesosPuntasRetazo.jpg" },
        { terms: ["tibia"], image: "/images/FactorCritico/HuesoTibia.jpg" }
    ];

    const state = {
        hueso: crearEstadoFactor(),
        recorte: crearEstadoFactor(),
        grasa: crearEstadoFactor("grasa")
    };

    let activeFactor = null;
    let activeTestId = 0;
    let pendingSaveFactor = null;
    let pendingManualFactor = null;
    let activeManualAuthorization = null;
    let scaleTimer = null;
    let scaleAbortController = null;
    let scaleReading = false;

    function crearEstadoFactor(selectedKey = null) {
        return {
            selectedKey,
            target: "entrada",
            weight: 0,
            automatic: false,
            readOnly: false,
            manualAuthorizedBy: null,
            initialModeLogKey: null,
            search: ""
        };
    }

    function getModal(factor) {
        return document.getElementById(factorConfig[factor]?.modalId || "");
    }

    function getTable(factor) {
        return document.getElementById(factorConfig[factor]?.tableId || "");
    }

    function getCatalog(factor) {
        return document.getElementById(factorConfig[factor]?.catalogId || "");
    }

    function numberValue(value) {
        const parsed = Number.parseFloat(String(value ?? "").replace(",", "."));
        return Number.isFinite(parsed) ? parsed : 0;
    }

    function kg(value) {
        return `${numberValue(value).toFixed(3)} kg`;
    }

    function percentage(value, signed = false) {
        const parsed = numberValue(value);
        const prefix = signed && parsed > 0 ? "+" : "";
        return `${prefix}${parsed.toFixed(2)}%`;
    }

    function differenceStatus(value) {
        const distance = Math.abs(numberValue(value));
        if (distance <= 1) return { key: "success", label: "En objetivo" };
        if (distance <= 3) return { key: "warning", label: "En rango" };
        return { key: "danger", label: "Fuera de rango" };
    }

    function historicalNumber(value) {
        if (value === null || value === undefined || String(value).trim() === "") return null;
        const parsed = Number(String(value).replace(",", "."));
        return Number.isFinite(parsed) ? parsed : null;
    }

    function historicalMetric(kind, label, value, detail) {
        const metric = document.createElement("div");
        metric.className = `fc-confirm-metric ${kind}`;
        if (kind === "difference" && value !== null) {
            metric.dataset.status = differenceStatus(value).key;
        }

        const caption = document.createElement("span");
        caption.textContent = label;
        const amount = document.createElement("strong");
        amount.textContent = value === null ? "—" : percentage(value, kind === "difference");
        const note = document.createElement("small");
        note.textContent = detail;
        metric.append(caption, amount, note);
        return metric;
    }

    function historicalResultCard(title, real, objective, difference) {
        const card = document.createElement("div");
        card.className = "fc-confirm-result";
        const heading = document.createElement("div");
        heading.className = "fc-confirm-result-head";
        const headingText = document.createElement("strong");
        headingText.textContent = title;
        heading.appendChild(headingText);

        const metrics = document.createElement("div");
        metrics.className = "fc-confirm-metrics";
        metrics.append(
            historicalMetric("real", "% real", real, "Salida / entrada"),
            historicalMetric("objective", "% objetivo", objective, "Meta de la prueba"),
            historicalMetric("difference", "% diferencia", difference,
                difference === null ? "Sin dato" : differenceStatus(difference).label)
        );
        card.append(heading, metrics);
        return card;
    }

    window.mostrarResultadosHistoricosFactor = (factor, registros = null, error = null) => {
        const container = getModal(factor)?.querySelector("[data-history-results]");
        if (!container) return;
        container.replaceChildren();

        if (error || !Array.isArray(registros) || registros.length === 0) {
            const message = document.createElement("p");
            message.className = "fc-history-message";
            message.textContent = error || (registros === null
                ? "Cargando resultados…"
                : `Esta prueba no tiene registros de ${factor}.`);
            container.appendChild(message);
            return;
        }

        if (factor === "hueso") {
            const entry = registros.reduce((sum, row) => sum + (historicalNumber(row.kgEntrada) || 0), 0);
            const exit = registros.reduce((sum, row) => sum + (historicalNumber(row.kgSalida) || 0), 0);
            const real = entry > 0 ? exit / entry * 100 : null;
            container.appendChild(historicalResultCard("Total de hueso", real,
                BONE_TOTAL_OBJECTIVE, real === null ? null : real - BONE_TOTAL_OBJECTIVE));
            return;
        }

        registros.forEach((row, index) => {
            const objective = historicalNumber(row.porcObjetivo);
            const entry = historicalNumber(row.kgEntrada);
            const exit = historicalNumber(row.kgSalida);
            const real = historicalNumber(row.porcPartic) ??
                (entry > 0 && exit !== null ? exit / entry * 100 : null);
            const difference = historicalNumber(row.difPorc) ??
                (real === null || objective === null ? null : real - objective);
            const title = registros.length === 1 ? "Resultado de la prueba" : `Registro #${row.id ?? index + 1}`;
            container.appendChild(historicalResultCard(title, real, objective, difference));
        });
    };

    function setAll(modal, selector, text) {
        modal?.querySelectorAll(selector).forEach(element => {
            element.textContent = text;
        });
    }

    function setStatus(factor, text, isError = false) {
        const modal = getModal(factor);
        const label = modal?.querySelector("[data-scale-status]");
        const status = modal?.querySelector(".fc-scale-status > span");

        if (label) label.textContent = text;
        if (status) status.classList.toggle("error", isError);
    }

    function setDisplayedWeight(factor, value) {
        const parsed = Math.max(0, numberValue(value));
        state[factor].weight = parsed;

        const display = getModal(factor)?.querySelector("[data-scale-value]");
        if (display) display.textContent = parsed.toFixed(2);
    }

    function imageForBone(name) {
        const normalized = normalizeSearch(name);
        const rule = boneImageRules.find(item => item.terms.every(term => normalized.includes(term)));
        return rule?.image || factorConfig.hueso.defaultImage;
    }

    function normalizeSearch(value) {
        return String(value ?? "")
            .normalize("NFD")
            .replace(/[\u0300-\u036f]/g, "")
            .toLocaleLowerCase("es-MX")
            .trim();
    }

    function updateBoneSearchStatus(visible, total) {
        const search = document.getElementById("buscarHueso");
        const clearButton = document.getElementById("limpiarBusquedaHueso");
        const result = document.getElementById("resultadoBusquedaHueso");
        const hasSearch = normalizeSearch(state.hueso.search).length > 0;

        if (search && search.value !== state.hueso.search) search.value = state.hueso.search;
        if (clearButton) clearButton.hidden = !hasSearch;
        if (!result) return;

        if (total === 0) result.textContent = "Cargando tipos de hueso…";
        else if (!hasSearch) result.textContent = `${total} ${total === 1 ? "hueso disponible" : "huesos disponibles"}`;
        else if (visible === 0) result.textContent = "Sin coincidencias";
        else result.textContent = `${visible} ${visible === 1 ? "coincidencia" : "coincidencias"}`;
    }

    function createItemCard({ key, name, image, objective, entry, exit, active, onClick, disabled, fallbackImage = "/images/CarneDefault.png" }) {
        const button = document.createElement("button");
        button.type = "button";
        button.className = `fc-item-card${active ? " active" : ""}`;
        button.dataset.key = String(key);
        button.disabled = Boolean(disabled);
        button.setAttribute("aria-pressed", active ? "true" : "false");

        const img = document.createElement("img");
        img.src = image;
        img.alt = name;
        img.loading = "lazy";
        img.onerror = () => {
            img.onerror = null;
            img.src = fallbackImage;
        };

        const content = document.createElement("span");
        const title = document.createElement("strong");
        title.textContent = name;
        title.title = name;

        const meta = document.createElement("small");
        const objectiveText = document.createElement("b");
        objectiveText.textContent = `Obj. ${objective || "0%"}`;
        const progressText = document.createElement("em");
        progressText.textContent = numberValue(entry) > 0 || numberValue(exit) > 0 ? "Con captura" : "Pendiente";
        meta.append(objectiveText, progressText);
        content.append(title, meta);
        button.append(img, content);
        button.addEventListener("click", onClick);
        return button;
    }

    function lockLegacyInputs(factor) {
        const modal = getModal(factor);
        modal?.querySelectorAll(".fc-summary-table input").forEach(input => {
            input.readOnly = true;
            input.tabIndex = -1;
        });
        modal?.querySelectorAll(".fc-summary-table select").forEach(select => {
            select.tabIndex = -1;
        });
    }

    function renderCatalog(factor) {
        if (!factorConfig[factor]) return;
        lockLegacyInputs(factor);

        if (factor === "hueso") renderBoneCatalog();
        if (factor === "recorte") renderTrimCatalog();
        if (factor === "grasa") renderFatCatalog();
    }

    function boneSelectionKey(row) {
        return String(state.hueso.readOnly ? row.dataset.id : row.dataset.fkhueso);
    }

    function boneDisplayName(row, rows) {
        const name = row.cells[0]?.textContent?.trim() || `Hueso ${row.dataset.fkhueso}`;
        const repeated = state.hueso.readOnly && rows.some(other =>
            other !== row && other.dataset.fkhueso === row.dataset.fkhueso);
        return repeated ? `${name} · Registro #${row.dataset.id}` : name;
    }

    function renderBoneCatalog() {
        const catalog = getCatalog("hueso");
        const rows = Array.from(getTable("hueso")?.querySelectorAll("tr[data-fkhueso]") || []);
        if (!catalog) return;

        if (rows.length === 0) {
            if (state.hueso.readOnly) {
                catalog.innerHTML = '<div class="fc-catalog-loading">No hay registros de hueso en esta prueba.</div>';
                state.hueso.selectedKey = null;
                selectWeightTarget("hueso", state.hueso.target);
            }
            updateBoneSearchStatus(0, 0);
            return;
        }

        const term = normalizeSearch(state.hueso.search);
        const visibleRows = term
            ? rows.filter(row => normalizeSearch(row.cells[0]?.textContent).includes(term))
            : rows;

        const validKeys = (state.hueso.readOnly ? visibleRows : rows).map(boneSelectionKey);
        if (!validKeys.includes(String(state.hueso.selectedKey ?? ""))) {
            state.hueso.selectedKey = validKeys[0] ?? null;
        }

        if (visibleRows.length === 0) {
            const empty = document.createElement("div");
            empty.className = "fc-catalog-loading fc-catalog-empty";
            const icon = document.createElement("i");
            icon.className = "bi bi-search";
            icon.setAttribute("aria-hidden", "true");
            const message = document.createElement("strong");
            message.textContent = "No encontramos ese hueso";
            const hint = document.createElement("span");
            hint.textContent = "Prueba con otro nombre o limpia la búsqueda.";
            empty.append(icon, message, hint);
            catalog.replaceChildren(empty);
            updateBoneSearchStatus(0, rows.length);
            selectWeightTarget("hueso", state.hueso.target);
            return;
        }

        catalog.replaceChildren(...visibleRows.map(row => {
            const key = boneSelectionKey(row);
            const name = row.cells[0]?.textContent?.trim() || `Hueso ${key}`;
            const objective = state.hueso.readOnly && row.dataset.porcobjetivo === ""
                ? "—" : `${numberValue(row.dataset.porcobjetivo).toFixed(2)}%`;
            const entry = row.querySelector(".kg-entrada")?.value;
            const exit = row.querySelector(".kg-salida")?.value;

            return createItemCard({
                key,
                name: boneDisplayName(row, rows),
                image: imageForBone(name),
                objective,
                entry,
                exit,
                active: key === String(state.hueso.selectedKey),
                disabled: false,
                fallbackImage: factorConfig.hueso.defaultImage,
                onClick: () => selectBone(key)
            });
        }));

        updateBoneSearchStatus(visibleRows.length, rows.length);
        selectWeightTarget("hueso", state.hueso.target);
    }

    window.filtrarCatalogoHuesos = value => {
        state.hueso.search = String(value ?? "");
        renderBoneCatalog();
    };

    window.limpiarBusquedaHuesos = () => {
        state.hueso.search = "";
        renderBoneCatalog();
        document.getElementById("buscarHueso")?.focus();
    };

    function selectBone(key) {
        state.hueso.selectedKey = String(key);
        getCatalog("hueso")?.querySelectorAll(".fc-item-card").forEach(card => {
            const active = card.dataset.key === String(key);
            card.classList.toggle("active", active);
            card.setAttribute("aria-pressed", active ? "true" : "false");
        });
        selectWeightTarget("hueso", state.hueso.target);
    }

    function renderTrimCatalog() {
        const catalog = getCatalog("recorte");
        const select = getTable("recorte")?.querySelector(".receta-select");
        if (!catalog) return;
        if (!select) {
            if (state.recorte.readOnly) {
                catalog.innerHTML = '<div class="fc-catalog-loading">No hay registros de recorte en esta prueba.</div>';
                state.recorte.selectedKey = null;
                selectWeightTarget("recorte", state.recorte.target);
            }
            return;
        }

        const options = Array.from(select.options).filter(option => numberValue(option.value) > 0);
        const selectedValue = String(select.value || "0");
        state.recorte.selectedKey = selectedValue !== "0" ? selectedValue : null;

        if (options.length === 0) {
            catalog.innerHTML = '<div class="fc-catalog-loading">No hay recetas disponibles.</div>';
            selectWeightTarget("recorte", state.recorte.target);
            return;
        }

        const entry = document.getElementById("txtRecorteEnt")?.value;
        const exit = document.getElementById("txtRecorteSal")?.value;

        catalog.replaceChildren(...options.map(option => {
            const objectiveMatch = option.textContent.match(/\(([-+]?\d+(?:[.,]\d+)?)%\)\s*$/);
            const objective = objectiveMatch ? `${objectiveMatch[1]}%` : "0%";
            const name = option.textContent.replace(/\s*\(([-+]?\d+(?:[.,]\d+)?)%\)\s*$/, "").trim();

            return createItemCard({
                key: option.value,
                name,
                image: factorConfig.recorte.defaultImage,
                objective,
                entry,
                exit,
                active: String(option.value) === selectedValue,
                disabled: state.recorte.readOnly,
                onClick: () => selectTrim(option.value)
            });
        }));

        selectWeightTarget("recorte", state.recorte.target);
    }

    function selectTrim(key) {
        const select = getTable("recorte")?.querySelector(".receta-select");
        if (!select) return;

        select.value = String(key);
        select.dispatchEvent(new Event("change", { bubbles: true }));
        state.recorte.selectedKey = String(key);
        getCatalog("recorte")?.querySelectorAll(".fc-item-card").forEach(card => {
            const active = card.dataset.key === String(key);
            card.classList.toggle("active", active);
            card.setAttribute("aria-pressed", active ? "true" : "false");
        });
        selectWeightTarget("recorte", state.recorte.target);
    }

    function renderFatCatalog() {
        const catalog = getCatalog("grasa");
        const row = getTable("grasa")?.querySelector("tr[data-id]");
        if (!catalog) return;
        if (!row) {
            if (state.grasa.readOnly) {
                catalog.innerHTML = '<div class="fc-catalog-loading">No hay registros de grasa en esta prueba.</div>';
                state.grasa.selectedKey = null;
                selectWeightTarget("grasa", state.grasa.target);
            }
            return;
        }

        state.grasa.selectedKey = "grasa";
        const objective = row.querySelector(".badge-sap")?.textContent?.trim() || "6.5%";
        const entry = document.getElementById("txtGrasaEnt")?.value;
        const exit = document.getElementById("txtGrasaSal")?.value;

        catalog.replaceChildren(createItemCard({
            key: "grasa",
            name: "Grasa",
            image: factorConfig.grasa.defaultImage,
            objective,
            entry,
            exit,
            active: true,
            disabled: state.grasa.readOnly,
            onClick: () => updateSelection("grasa")
        }));

        selectWeightTarget("grasa", state.grasa.target);
    }

    function getSelectedData(factor) {
        if (factor === "hueso") {
            const rows = Array.from(getTable("hueso")?.querySelectorAll("tr[data-fkhueso]") || []);
            const row = rows.find(item => boneSelectionKey(item) === String(state.hueso.selectedKey));
            if (!row) return null;
            const name = row.cells[0]?.textContent?.trim() || "Hueso";
            return {
                name: boneDisplayName(row, rows),
                image: imageForBone(name),
                objective: state.hueso.readOnly && row.dataset.porcobjetivo === ""
                    ? "—" : `${numberValue(row.dataset.porcobjetivo).toFixed(2)}%`,
                participation: row.querySelector(".td-partic")?.textContent?.trim() || "—",
                difference: row.querySelector(".td-dif")?.textContent?.trim() || "—",
                entryInput: row.querySelector(".kg-entrada"),
                exitInput: row.querySelector(".kg-salida")
            };
        }

        if (factor === "recorte") {
            const select = getTable("recorte")?.querySelector(".receta-select");
            const option = select?.options?.[select.selectedIndex];
            if (!select || !option || numberValue(select.value) <= 0) return null;
            return {
                name: option.textContent.replace(/\s*\(([-+]?\d+(?:[.,]\d+)?)%\)\s*$/, "").trim(),
                image: factorConfig.recorte.defaultImage,
                objective: getTable("recorte")?.querySelector(".badge-sap")?.textContent?.trim() || "0%",
                entryInput: document.getElementById("txtRecorteEnt"),
                exitInput: document.getElementById("txtRecorteSal")
            };
        }

        const row = getTable("grasa")?.querySelector("tr[data-id]");
        if (!row) return null;
        return {
            name: "Grasa",
            image: factorConfig.grasa.defaultImage,
            objective: row.querySelector(".badge-sap")?.textContent?.trim() || "6.5%",
            entryInput: document.getElementById("txtGrasaEnt"),
            exitInput: document.getElementById("txtGrasaSal")
        };
    }

    function updateSelection(factor) {
        const modal = getModal(factor);
        const selected = getSelectedData(factor);
        if (!modal) return;

        const name = modal.querySelector("[data-selected-name]");
        const image = modal.querySelector("[data-selected-image]");
        const objective = modal.querySelector("[data-selected-objective]");
        const applyButton = modal.querySelector("[data-apply-weight]");
        const help = modal.querySelector("[data-scale-help]");

        if (!selected) {
            if (name) name.textContent = factor === "recorte" ? "Ninguna receta seleccionada" : "Ningún artículo seleccionado";
            if (objective) objective.textContent = "—";
            if (factor === "hueso") {
                modal.querySelector("[data-selected-participation]")?.replaceChildren("—");
                modal.querySelector("[data-selected-difference]")?.replaceChildren("—");
            }
            setAll(modal, "[data-current-entry]", "0.000 kg");
            setAll(modal, "[data-current-exit]", "0.000 kg");
            if (applyButton) applyButton.disabled = true;
            if (help) help.textContent = factor === "recorte" ? "Selecciona primero una receta." : "Selecciona primero un artículo.";
            return;
        }

        if (name) name.textContent = selected.name;
        if (image) {
            image.onerror = () => {
                image.onerror = null;
                image.src = factorConfig[factor].defaultImage;
            };
            image.src = selected.image;
            image.alt = selected.name;
        }
        if (objective) objective.textContent = selected.objective;
        if (factor === "hueso") {
            modal.querySelector("[data-selected-participation]")?.replaceChildren(selected.participation);
            modal.querySelector("[data-selected-difference]")?.replaceChildren(selected.difference);
        }
        setAll(modal, "[data-current-entry]", kg(selected.entryInput?.value));
        setAll(modal, "[data-current-exit]", kg(selected.exitInput?.value));
        const targetInput = state[factor].target === "entrada" ? selected.entryInput : selected.exitInput;
        const targetLocked = Boolean(targetInput?.disabled);
        if (applyButton) applyButton.disabled = state[factor].readOnly || targetLocked;
        if (help) {
            if (state[factor].readOnly) help.textContent = "Consulta en modo lectura.";
            else if (targetLocked) help.textContent = "La entrada ya fue confirmada y se conserva sin cambios.";
            else help.textContent = `Listo para capturar el peso de ${selected.name}.`;
        }
    }

    function selectWeightTarget(factor, target) {
        const modal = getModal(factor);
        const selected = getSelectedData(factor);
        if (!modal || !["entrada", "salida"].includes(target)) return;

        state[factor].target = target;
        modal.querySelectorAll("[data-weight-target]").forEach(button => {
            button.classList.toggle("active", button.dataset.weightTarget === target);
            button.setAttribute("aria-pressed", String(button.dataset.weightTarget === target));
        });

        if (!state[factor].automatic) {
            const current = target === "entrada" ? selected?.entryInput?.value : selected?.exitInput?.value;
            const input = modal.querySelector("[data-manual-weight]");
            if (input) input.value = numberValue(current) > 0 ? numberValue(current).toFixed(3) : "";
            setDisplayedWeight(factor, current);
        }

        updateSelection(factor);
    }

    window.seleccionarTipoPesoFactor = selectWeightTarget;

    window.actualizarPesoManualFactor = (factor, value) => {
        if (!factorConfig[factor] || state[factor].automatic) return;
        setDisplayedWeight(factor, value);
    };

    window.aplicarPesoFactor = factor => {
        const selected = getSelectedData(factor);
        const currentState = state[factor];
        if (!selected || currentState.readOnly) return;

        if (!Number.isFinite(currentState.weight) || currentState.weight <= 0) {
            const help = getModal(factor)?.querySelector("[data-scale-help]");
            if (help) help.textContent = "El peso debe ser mayor a cero para aplicarlo.";
            return;
        }

        const input = currentState.target === "entrada" ? selected.entryInput : selected.exitInput;
        if (!input || input.disabled) {
            const help = getModal(factor)?.querySelector("[data-scale-help]");
            if (help) help.textContent = currentState.target === "entrada"
                ? "La entrada ya fue confirmada y no puede reemplazarse."
                : "Este peso no está disponible para edición.";
            return;
        }

        input.value = currentState.weight.toFixed(3);
        input.dispatchEvent(new Event("input", { bubbles: true }));
        input.dispatchEvent(new Event("change", { bubbles: true }));
        updateSelection(factor);
        updateCardProgress(factor);

        const help = getModal(factor)?.querySelector("[data-scale-help]");
        if (help) help.textContent = `${currentState.target === "entrada" ? "Entrada" : "Salida"} aplicada a ${selected.name}.`;
    };

    function updateCardProgress(factor) {
        const selected = getSelectedData(factor);
        const card = getCatalog(factor)?.querySelector(`.fc-item-card[data-key="${CSS.escape(String(state[factor].selectedKey))}"]`);
        const progress = card?.querySelector("small em");
        if (progress && selected) {
            progress.textContent = numberValue(selected.entryInput?.value) > 0 || numberValue(selected.exitInput?.value) > 0
                ? "Con captura"
                : "Pendiente";
        }
    }

    function scaleConfigured() {
        return Boolean(localStorage.getItem("ipBascula")?.trim() && localStorage.getItem("comandoBascula")?.trim());
    }

    function csrfToken() {
        return document.querySelector('#csrf-form input[name="__RequestVerificationToken"]')?.value || "";
    }

    async function registrarCambioModoFactor(factor, modoDestino, credenciales = {}, modoOrigen, loteOverride) {
        const config = factorConfig[factor];
        if (!config) throw new Error("Factor inválido.");

        const response = await fetch("/tif/modo-peso/cambiar", {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "Accept": "application/json",
                "X-Requested-With": "XMLHttpRequest",
                "RequestVerificationToken": csrfToken()
            },
            body: JSON.stringify({
                pruebaId: activeTestId > 0 ? activeTestId : null,
                lote: loteOverride ?? document.getElementById(config.lotId)?.textContent?.trim() ?? "",
                factor,
                modoOrigen: modoOrigen === undefined
                    ? (state[factor].automatic ? "AUTOMATICO" : "MANUAL")
                    : modoOrigen,
                modoDestino,
                usuarioAutorizador: credenciales.usuario || null,
                clave: credenciales.clave || null
            })
        });

        let data = null;
        try {
            data = await response.json();
        } catch {
            data = null;
        }

        if (!response.ok || !data?.success) {
            const error = new Error(data?.message || "No fue posible registrar el cambio de modo.");
            error.status = response.status;
            throw error;
        }

        return data;
    }

    function actualizarAuditoriaModo(factor) {
        const audit = getModal(factor)?.querySelector("[data-mode-authorization] span");
        if (!audit) return;

        if (state[factor].readOnly) {
            audit.textContent = "Consulta de captura registrada";
        } else if (state[factor].automatic) {
            audit.textContent = "Captura automática · no requiere autorización";
        } else if (state[factor].manualAuthorizedBy) {
            audit.textContent = `${state[factor].manualAuthorizedBy} autorizó el peso manual`;
        } else {
            audit.textContent = "El modo manual requiere autorización";
        }
    }

    function updateScaleModeUi(factor) {
        const modal = getModal(factor);
        const currentState = state[factor];
        const panel = modal?.querySelector(".fc-scale-panel");
        const button = modal?.querySelector("[data-scale-mode]");
        const input = modal?.querySelector("[data-manual-weight]");
        if (!panel || !button) return;

        panel.classList.toggle("is-auto", currentState.automatic);
        panel.classList.toggle("is-manual-authorized", !currentState.automatic && Boolean(currentState.manualAuthorizedBy));
        button.classList.toggle("auto", currentState.automatic);
        button.disabled = currentState.readOnly;
        button.textContent = currentState.readOnly ? "Consulta" : (currentState.automatic ? "Báscula" : "Manual");
        button.title = currentState.readOnly
            ? "Consulta en modo lectura"
            : (currentState.automatic ? "Solicitar captura manual" : "Cambiar a lectura de báscula");
        if (input) input.disabled = currentState.automatic || currentState.readOnly;
        if (currentState.readOnly) {
            setStatus(factor, "Modo lectura");
        } else if (currentState.automatic) {
            setStatus(factor, scaleConfigured() ? "Conectando báscula…" : "Báscula sin configurar", !scaleConfigured());
        } else {
            setStatus(factor, "Modo manual autorizado");
        }
        actualizarAuditoriaModo(factor);
    }

    function abrirAutorizacionManual(factor) {
        pendingManualFactor = factor;
        const config = factorConfig[factor];
        const modal = document.getElementById("modalAutorizarManualFactor");
        const usuario = document.getElementById("fcAuthUsuario");
        const clave = document.getElementById("fcAuthClave");
        const error = document.getElementById("fcAuthError");

        document.getElementById("fcAuthFactor").textContent = config.label;
        document.getElementById("fcAuthLote").textContent = document.getElementById(config.lotId)?.textContent?.trim() || "—";
        if (usuario) usuario.value = "";
        if (clave) clave.value = "";
        if (error) error.textContent = "";
        modal.style.display = "flex";
        window.setTimeout(() => usuario?.focus(), 0);
    }

    window.cancelarAutorizacionManualFactor = () => {
        pendingManualFactor = null;
        const clave = document.getElementById("fcAuthClave");
        const error = document.getElementById("fcAuthError");
        if (clave) clave.value = "";
        if (error) error.textContent = "";
        document.getElementById("modalAutorizarManualFactor").style.display = "none";
    };

    window.confirmarModoManualFactor = async event => {
        event?.preventDefault();
        const factor = pendingManualFactor;
        if (!factor || !factorConfig[factor]) return;

        const usuario = document.getElementById("fcAuthUsuario")?.value?.trim() || "";
        const claveInput = document.getElementById("fcAuthClave");
        const clave = claveInput?.value || "";
        const error = document.getElementById("fcAuthError");
        const button = document.getElementById("fcBtnAutorizarManual");

        if (!usuario || !clave) {
            if (error) error.textContent = "Captura el usuario y la contraseña de autorización.";
            return;
        }

        if (button) {
            button.disabled = true;
            button.innerHTML = '<span class="spinner-border spinner-border-sm" aria-hidden="true"></span> Validando…';
        }
        if (error) error.textContent = "";

        try {
            const result = await registrarCambioModoFactor(factor, "MANUAL", { usuario, clave });
            activeManualAuthorization = result.autorizadoPor || "Autorización registrada";
            state[factor].automatic = false;
            state[factor].manualAuthorizedBy = activeManualAuthorization;
            pendingManualFactor = null;
            if (claveInput) claveInput.value = "";
            document.getElementById("modalAutorizarManualFactor").style.display = "none";
            stopScaleLoop();
            updateScaleModeUi(factor);
            selectWeightTarget(factor, state[factor].target);

            const help = getModal(factor)?.querySelector("[data-scale-help]");
            if (help) help.textContent = result.message;
        } catch (requestError) {
            if (claveInput) {
                claveInput.value = "";
                claveInput.focus();
            }
            if (error) error.textContent = requestError.message || "No fue posible validar la autorización.";
        } finally {
            if (button) {
                button.disabled = false;
                button.innerHTML = '<i class="bi bi-shield-check"></i> Autorizar modo manual';
            }
        }
    };

    window.alternarModoPesoFactor = async factor => {
        if (!factorConfig[factor] || state[factor].readOnly) return;

        if (state[factor].automatic) {
            abrirAutorizacionManual(factor);
            return;
        }

        activeManualAuthorization = null;
        state[factor].automatic = true;
        state[factor].manualAuthorizedBy = null;
        updateScaleModeUi(factor);
        stopScaleLoop();

        try {
            await registrarCambioModoFactor(factor, "AUTOMATICO");
        } catch (requestError) {
            console.warn("El modo automático se activó, pero no fue posible registrar el cambio.", requestError);
            window.alert("El modo automático quedó activo, pero no fue posible registrar el cambio en la bitácora.");
        }

        if (scaleConfigured()) {
            startScaleLoop(factor);
        } else {
            window.abrirConfiguracionBasculaFactor();
        }
    };

    function cleanScaleValue(value) {
        const text = String(value ?? "")
            .replace(/["']/g, "")
            .replace(/kg/gi, "")
            .replace(/[\r\n\t\s]/g, "")
            .replace(",", ".");
        const match = text.match(/[-+]?\d+(?:\.\d+)?/);
        return match ? numberValue(match[0]) : Number.NaN;
    }

    function stopScaleLoop() {
        if (scaleTimer) {
            window.clearTimeout(scaleTimer);
            scaleTimer = null;
        }
        if (scaleAbortController) {
            scaleAbortController.abort();
            scaleAbortController = null;
        }
        scaleReading = false;
    }

    function startScaleLoop(factor) {
        stopScaleLoop();
        activeFactor = factor;
        if (!state[factor].automatic || state[factor].readOnly) return;
        readScale(factor);
    }

    async function readScale(factor) {
        if (scaleReading || activeFactor !== factor || !state[factor].automatic) return;

        const ip = localStorage.getItem("ipBascula")?.trim() || "";
        const command = localStorage.getItem("comandoBascula")?.trim() || "";
        if (!ip || !command) {
            updateScaleModeUi(factor);
            return;
        }

        scaleReading = true;
        let succeeded = false;
        scaleAbortController = new AbortController();
        const requestTimeout = window.setTimeout(() => scaleAbortController?.abort(), 1600);

        try {
            const response = await fetch(`/api/Inyeccion/ObtenerPeso?ip=${encodeURIComponent(ip)}&comando=${encodeURIComponent(command)}`, {
                cache: "no-store",
                signal: scaleAbortController.signal
            });
            if (!response.ok) throw new Error(`HTTP ${response.status}`);

            const value = cleanScaleValue(await response.text());
            if (!Number.isFinite(value)) throw new Error("Lectura inválida");

            setDisplayedWeight(factor, value);
            setStatus(factor, "Báscula conectada");
            succeeded = true;
        } catch (error) {
            if (error.name !== "AbortError") console.warn("No fue posible leer la báscula de Factor Crítico.", error);
            setStatus(factor, "Sin respuesta · reintentando", true);
        } finally {
            window.clearTimeout(requestTimeout);
            scaleAbortController = null;
            scaleReading = false;

            if (activeFactor === factor && state[factor].automatic) {
                scaleTimer = window.setTimeout(() => readScale(factor), succeeded ? 650 : 1800);
            }
        }
    }

    window.abrirConfiguracionBasculaFactor = () => {
        const ip = document.getElementById("fcIpBascula");
        const command = document.getElementById("fcComandoBascula");
        if (ip) ip.value = localStorage.getItem("ipBascula") || "";
        if (command) command.value = localStorage.getItem("comandoBascula") || "";
        document.getElementById("modalBasculaFactor").style.display = "flex";
    };

    window.guardarConfiguracionBasculaFactor = async () => {
        const ip = document.getElementById("fcIpBascula")?.value?.trim() || "";
        const command = document.getElementById("fcComandoBascula")?.value?.trim() || "";
        if (!ip || !command) {
            window.alert("Captura la IP y el comando de la báscula.");
            return;
        }

        localStorage.setItem("ipBascula", ip);
        localStorage.setItem("comandoBascula", command);
        document.getElementById("modalBasculaFactor").style.display = "none";

        if (activeFactor && factorConfig[activeFactor] && !state[activeFactor].readOnly) {
            const factor = activeFactor;
            const veniaDeManual = !state[factor].automatic;
            activeManualAuthorization = null;
            state[factor].automatic = true;
            state[factor].manualAuthorizedBy = null;
            updateScaleModeUi(factor);

            if (veniaDeManual) {
                try {
                    await registrarCambioModoFactor(factor, "AUTOMATICO");
                } catch (requestError) {
                    console.warn("No fue posible registrar el regreso al modo automático.", requestError);
                    window.alert("La báscula quedó configurada, pero no fue posible registrar el cambio a automático.");
                }
            }

            startScaleLoop(factor);
        }
    };

    function currentFactorFromVisibleModal() {
        return Object.keys(factorConfig).find(factor => getModal(factor)?.style.display === "flex") || activeFactor;
    }

    window.cambiarFactorCaptura = factor => {
        if (!factorConfig[factor]) return;
        const current = currentFactorFromVisibleModal();
        const lot = current ? document.getElementById(factorConfig[current].lotId)?.textContent?.trim() : "";
        const readOnly = current ? state[current].readOnly : false;

        if (current) document.getElementById(factorConfig[current].modalId).style.display = "none";
        stopScaleLoop();

        if (factor === "hueso") window.abrirPruebaHueso(activeTestId, lot, 0, readOnly);
        if (factor === "recorte") window.abrirPruebaRecorte(activeTestId, lot, readOnly);
        if (factor === "grasa") window.abrirPruebaGrasa(activeTestId, lot, readOnly);
    };

    function prepareCaptureModal(factor, readOnly, loteActual = "") {
        const veniaDeManual = !state[factor].automatic && Boolean(state[factor].manualAuthorizedBy);
        const conservarModoManual = !readOnly && Boolean(activeManualAuthorization);

        activeFactor = factor;
        state[factor].readOnly = Boolean(readOnly);
        state[factor].target = "entrada";
        state[factor].weight = 0;
        state[factor].automatic = readOnly ? false : !conservarModoManual;
        state[factor].manualAuthorizedBy = conservarModoManual
            ? activeManualAuthorization
            : null;

        if (factor === "hueso") {
            state.hueso.search = "";
            updateBoneSearchStatus(0, 0);
        }

        const modal = getModal(factor);
        if (modal) {
            modal.dataset.readonly = readOnly ? "true" : "false";
            if (readOnly) window.mostrarResultadosHistoricosFactor(factor);
            modal.querySelectorAll("[data-weight-target]").forEach(button => {
                button.classList.toggle("active", button.dataset.weightTarget === "entrada");
                button.setAttribute("aria-pressed", String(button.dataset.weightTarget === "entrada"));
                button.disabled = Boolean(readOnly);
            });
            const input = modal.querySelector("[data-manual-weight]");
            if (input) input.value = "";
        }

        setDisplayedWeight(factor, 0);
        updateScaleModeUi(factor);
        if (state[factor].automatic && scaleConfigured()) startScaleLoop(factor);

        if (state[factor].automatic) {
            const lote = String(loteActual || document.getElementById(factorConfig[factor].lotId)?.textContent || "").trim();
            const logKey = `${activeTestId}|${lote}|AUTOMATICO`;
            if (veniaDeManual || state[factor].initialModeLogKey !== logKey) {
                state[factor].initialModeLogKey = logKey;
                registrarCambioModoFactor(factor, "AUTOMATICO", {}, veniaDeManual ? "MANUAL" : null, lote).catch(error => {
                    console.warn("No fue posible registrar el modo automático inicial.", error);
                    state[factor].initialModeLogKey = null;
                });
            }
        }
    }

    function getConfirmationData(factor) {
        const config = factorConfig[factor];
        const lot = document.getElementById(config.lotId)?.textContent?.trim() || "—";

        if (factor === "hueso") {
            const rows = Array.from(getTable("hueso")?.querySelectorAll("tr[data-fkhueso]") || []);
            const captured = rows.filter(row => numberValue(row.querySelector(".kg-entrada")?.value) > 0 || numberValue(row.querySelector(".kg-salida")?.value) > 0);
            if (captured.length === 0) throw new Error("Captura al menos un peso de hueso antes de confirmar.");

            const entry = captured.reduce((sum, row) => sum + numberValue(row.querySelector(".kg-entrada")?.value), 0);
            const exit = captured.reduce((sum, row) => sum + numberValue(row.querySelector(".kg-salida")?.value), 0);
            const real = entry > 0 ? (exit / entry) * 100 : 0;

            return {
                lot,
                factor: config.label,
                selection: `${captured.length} tipo${captured.length === 1 ? "" : "s"} de hueso con captura`,
                entry,
                exit,
                real,
                objective: BONE_TOTAL_OBJECTIVE,
                difference: real - BONE_TOTAL_OBJECTIVE,
                mode: state[factor].automatic ? "Automático" : "Manual",
                authorizedBy: state[factor].automatic ? "No requiere" : (state[factor].manualAuthorizedBy || "—")
            };
        }

        const selected = getSelectedData(factor);
        if (!selected) throw new Error(factor === "recorte" ? "Selecciona una receta antes de confirmar." : "No hay información para confirmar.");
        const entry = numberValue(selected.entryInput?.value);
        const exit = numberValue(selected.exitInput?.value);
        if (entry <= 0 && exit <= 0) throw new Error("Captura al menos un peso antes de confirmar.");

        const objective = numberValue(selected.objective.replace("%", ""));
        const real = entry > 0 ? (exit / entry) * 100 : 0;

        return {
            lot,
            factor: config.label,
            selection: selected.name,
            entry,
            exit,
            real,
            objective,
            difference: real - objective,
            mode: state[factor].automatic ? "Automático" : "Manual",
            authorizedBy: state[factor].automatic ? "No requiere" : (state[factor].manualAuthorizedBy || "—")
        };
    }

    window.prepararConfirmacionFactor = factor => {
        try {
            const data = getConfirmationData(factor);
            pendingSaveFactor = factor;
            document.getElementById("fcConfirmLote").textContent = data.lot;
            document.getElementById("fcConfirmFactor").textContent = data.factor;
            document.getElementById("fcConfirmSeleccion").textContent = data.selection;
            document.getElementById("fcConfirmEntrada").textContent = kg(data.entry);
            document.getElementById("fcConfirmSalida").textContent = kg(data.exit);
            document.getElementById("fcConfirmModo").textContent = data.mode;
            document.getElementById("fcConfirmAutorizo").textContent = data.authorizedBy;
            document.getElementById("fcConfirmReal").textContent = percentage(data.real);
            document.getElementById("fcConfirmObjetivo").textContent = percentage(data.objective);
            document.getElementById("fcConfirmDiferencia").textContent = percentage(data.difference, true);

            const status = differenceStatus(data.difference);
            document.getElementById("fcConfirmDiferenciaCard").dataset.status = status.key;
            document.getElementById("fcConfirmDiferenciaEstado").textContent = status.label;
            document.getElementById("modalConfirmarFactor").style.display = "flex";
        } catch (error) {
            window.alert(error.message || "No fue posible preparar la confirmación.");
        }
    };

    window.confirmarGuardadoFactor = () => {
        const factor = pendingSaveFactor;
        pendingSaveFactor = null;
        document.getElementById("modalConfirmarFactor").style.display = "none";

        if (factor === "hueso") window.guardarTodosHueso();
        if (factor === "recorte") window.guardarRecorte();
        if (factor === "grasa") window.guardarGrasa();
    };

    function enhanceLotActions() {
        document.querySelectorAll("#lista-registros-body tr, #lista-log-body tr").forEach(row => {
            const buttons = Array.from(row.querySelectorAll("td:last-child .tbtn"));
            const items = [
                { name: "Hueso", image: "/images/Hueso.png" },
                { name: "Recorte", image: "/images/Recorte-80-20.png" },
                { name: "Grasa", image: "/images/Rib con Grasa.jpeg" }
            ];

            buttons.slice(0, 3).forEach((button, index) => {
                if (button.classList.contains("fc-row-factor")) return;
                const item = items[index];
                button.classList.add("fc-row-factor");
                button.innerHTML = `<img src="${item.image}" alt=""><span>${item.name}</span>`;
            });
        });
    }

    function renderExpandedBoneSummary() {
        const source = getTable("hueso")?.closest("table");
        const content = document.getElementById("fcResumenHuesosContenido");
        if (!source || !content) return;

        const table = source.cloneNode(true);
        table.querySelectorAll("[id]").forEach(element => element.removeAttribute("id"));
        table.querySelectorAll("input").forEach(input => {
            const weight = document.createElement("span");
            weight.textContent = input.value === "" ? "—" : kg(input.value);
            input.replaceWith(weight);
        });
        table.querySelector("thead th:last-child").textContent = "Estado";
        content.replaceChildren(table);
    }

    window.abrirResumenHuesosAmpliado = () => {
        if (!state.hueso.readOnly || getModal("hueso")?.style.display !== "flex") return;
        const dialog = document.getElementById("modalResumenHuesos");
        if (!dialog) return;

        document.getElementById("fcResumenHuesosLote").textContent = document.getElementById("lote-hueso").textContent;
        document.getElementById("fcResumenHuesosPrueba").textContent = document.getElementById("badge-prueba-hueso").textContent;
        renderExpandedBoneSummary();
        dialog.style.display = "flex";
        const content = document.getElementById("fcResumenHuesosContenido");
        content.scrollTop = 0;
        content.scrollLeft = 0;
    };

    function installObservers() {
        Object.keys(factorConfig).forEach(factor => {
            const table = getTable(factor);
            if (!table) return;
            new MutationObserver(() => window.requestAnimationFrame(() => {
                renderCatalog(factor);
                if (factor === "hueso" && document.getElementById("modalResumenHuesos")?.style.display === "flex") {
                    renderExpandedBoneSummary();
                }
            }))
                .observe(table, { childList: true, subtree: true });
        });

        document.querySelectorAll("#lista-registros-body, #lista-log-body").forEach(lotBody => {
            new MutationObserver(() => window.requestAnimationFrame(enhanceLotActions))
                .observe(lotBody, { childList: true, subtree: true });
        });
    }

    function wrapExistingFunctions() {
        const originalOpenBone = window.abrirPruebaHueso;
        const originalOpenTrim = window.abrirPruebaRecorte;
        const originalOpenFat = window.abrirPruebaGrasa;
        const originalClose = window.cerrarModal;

        if (typeof originalOpenBone === "function") {
            window.abrirPruebaHueso = function (idPrueba, lot, scroll = 0, readOnly = false) {
                activeTestId = Number(idPrueba) || 0;
                prepareCaptureModal("hueso", readOnly, lot);
                return originalOpenBone.call(this, idPrueba, lot, scroll, readOnly);
            };
        }

        if (typeof originalOpenTrim === "function") {
            window.abrirPruebaRecorte = function (idPrueba, lot, readOnly = false) {
                activeTestId = Number(idPrueba) || 0;
                prepareCaptureModal("recorte", readOnly, lot);
                return originalOpenTrim.call(this, idPrueba, lot, readOnly);
            };
        }

        if (typeof originalOpenFat === "function") {
            window.abrirPruebaGrasa = function (idPrueba, lot, readOnly = false) {
                activeTestId = Number(idPrueba) || 0;
                prepareCaptureModal("grasa", readOnly, lot);
                return originalOpenFat.call(this, idPrueba, lot, readOnly);
            };
        }

        if (typeof originalClose === "function") {
            window.cerrarModal = function (id) {
                const closingFactor = Object.keys(factorConfig).find(factor => factorConfig[factor].modalId === id);
                if (closingFactor && activeFactor === closingFactor) {
                    stopScaleLoop();
                    activeFactor = null;
                }
                if (id === "modalConfirmarFactor") pendingSaveFactor = null;
                if (id === "modalAutorizarManualFactor") {
                    pendingManualFactor = null;
                    const usuario = document.getElementById("fcAuthUsuario");
                    const clave = document.getElementById("fcAuthClave");
                    const error = document.getElementById("fcAuthError");
                    if (usuario) usuario.value = "";
                    if (clave) clave.value = "";
                    if (error) error.textContent = "";
                }
                const result = originalClose.call(this, id);
                if (id === "modalHueso" && getModal("hueso")?.style.display === "none") {
                    document.getElementById("modalResumenHuesos").style.display = "none";
                }
                return result;
            };
        }
    }

    function installDialogNavigation() {
        const overlays = Array.from(document.querySelectorAll(".factor-tif-app .modal-overlay"));
        const openDialogs = [];
        const returnFocus = new WeakMap();
        const focusable = dialog => Array.from(dialog.querySelectorAll(
            'button:not(:disabled), input:not(:disabled):not([readonly]), select:not(:disabled), [tabindex="0"], summary'
        )).filter(element => element.getClientRects().length > 0);

        const syncDialogs = () => {
            overlays.forEach(dialog => {
                const isOpen = dialog.style.display === "flex";
                const index = openDialogs.indexOf(dialog);
                if (isOpen && index < 0) {
                    returnFocus.set(dialog, document.activeElement);
                    openDialogs.push(dialog);
                    dialog.tabIndex = -1;
                    (focusable(dialog)[0] || dialog).focus({ preventScroll: true });
                } else if (!isOpen && index >= 0) {
                    openDialogs.splice(index, 1);
                    const previous = returnFocus.get(dialog);
                    if (previous?.isConnected && previous.getClientRects().length) previous.focus({ preventScroll: true });
                }
            });
            document.body.classList.toggle("fc-modal-open", openDialogs.length > 0);
        };
        overlays.forEach(dialog => new MutationObserver(syncDialogs).observe(dialog, {
            attributes: true, attributeFilter: ["style"]
        }));
        document.addEventListener("keydown", event => {
            const dialog = openDialogs[openDialogs.length - 1];
            if (!dialog || document.getElementById("loaderGlobal")?.style.display === "flex") return;
            if (event.key === "Escape") {
                event.preventDefault();
                window.cerrarModal(dialog.id);
            } else if (event.key === "Tab") {
                const items = focusable(dialog);
                const first = items[0] || dialog;
                const last = items[items.length - 1] || dialog;
                if (event.shiftKey && (document.activeElement === first || !dialog.contains(document.activeElement))) {
                    event.preventDefault();
                    last.focus();
                } else if (!event.shiftKey && (document.activeElement === last || !dialog.contains(document.activeElement))) {
                    event.preventDefault();
                    first.focus();
                }
            }
        });
        syncDialogs();
    }

    function initialize() {
        installObservers();
        wrapExistingFunctions();
        installDialogNavigation();
        enhanceLotActions();
        document.addEventListener("visibilitychange", () => {
            if (document.hidden) {
                stopScaleLoop();
            } else if (activeFactor && state[activeFactor].automatic) {
                startScaleLoop(activeFactor);
            }
        });
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initialize, { once: true });
    } else {
        initialize();
    }
})();
