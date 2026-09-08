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

    const state = {
        hueso: crearEstadoFactor(),
        recorte: crearEstadoFactor(),
        grasa: crearEstadoFactor("grasa")
    };

    let activeFactor = null;
    let activeTestId = 0;
    let pendingSaveFactor = null;
    let scaleTimer = null;
    let scaleAbortController = null;
    let scaleReading = false;

    function crearEstadoFactor(selectedKey = null) {
        return {
            selectedKey,
            target: "entrada",
            weight: 0,
            automatic: false,
            readOnly: false
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
        if (display) display.textContent = parsed.toFixed(3);
    }

    function imageForBone(name) {
        const normalized = String(name ?? "").toLowerCase();
        if (normalized.includes("costilla")) return "/images/Costilla.jpeg";
        if (normalized.includes("chamberete") || normalized.includes("chamorro")) return "/images/Chamberete.png";
        if (normalized.includes("t-bone") || normalized.includes("tbone") || normalized.includes("lomo")) return "/images/tbone.jpg";
        if (normalized.includes("pecho")) return "/images/Pecho.png";
        return "/images/Hueso.png";
    }

    function createItemCard({ key, name, image, objective, entry, exit, active, onClick, disabled }) {
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
            img.src = "/images/CarneDefault.png";
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

    function renderBoneCatalog() {
        const catalog = getCatalog("hueso");
        const rows = Array.from(getTable("hueso")?.querySelectorAll("tr[data-fkhueso]") || []);
        if (!catalog || rows.length === 0) return;

        const validKeys = rows.map(row => String(row.dataset.fkhueso));
        if (!validKeys.includes(String(state.hueso.selectedKey ?? ""))) {
            state.hueso.selectedKey = validKeys[0];
        }

        catalog.replaceChildren(...rows.map(row => {
            const key = String(row.dataset.fkhueso);
            const name = row.cells[0]?.textContent?.trim() || `Hueso ${key}`;
            const objective = `${numberValue(row.dataset.porcobjetivo).toFixed(2)}%`;
            const entry = row.querySelector(".kg-entrada")?.value;
            const exit = row.querySelector(".kg-salida")?.value;

            return createItemCard({
                key,
                name,
                image: imageForBone(name),
                objective,
                entry,
                exit,
                active: key === String(state.hueso.selectedKey),
                disabled: state.hueso.readOnly,
                onClick: () => selectBone(key)
            });
        }));

        selectWeightTarget("hueso", state.hueso.target);
    }

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
        if (!catalog || !select) return;

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
        if (!catalog || !row) return;

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
            const row = Array.from(getTable("hueso")?.querySelectorAll("tr[data-fkhueso]") || [])
                .find(item => String(item.dataset.fkhueso) === String(state.hueso.selectedKey));
            if (!row) return null;
            const name = row.cells[0]?.textContent?.trim() || "Hueso";
            return {
                name,
                image: imageForBone(name),
                objective: `${numberValue(row.dataset.porcobjetivo).toFixed(2)}%`,
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
            setAll(modal, "[data-current-entry]", "0.000 kg");
            setAll(modal, "[data-current-exit]", "0.000 kg");
            if (applyButton) applyButton.disabled = true;
            if (help) help.textContent = factor === "recorte" ? "Selecciona primero una receta." : "Selecciona primero un artículo.";
            return;
        }

        if (name) name.textContent = selected.name;
        if (image) {
            image.src = selected.image;
            image.alt = selected.name;
        }
        if (objective) objective.textContent = selected.objective;
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

    function updateScaleModeUi(factor) {
        const modal = getModal(factor);
        const currentState = state[factor];
        const panel = modal?.querySelector(".fc-scale-panel");
        const button = modal?.querySelector("[data-scale-mode]");
        const input = modal?.querySelector("[data-manual-weight]");
        if (!panel || !button) return;

        panel.classList.toggle("is-auto", currentState.automatic);
        button.classList.toggle("auto", currentState.automatic);
        button.textContent = currentState.automatic ? "Báscula" : "Manual";
        button.title = currentState.automatic ? "Cambiar a captura manual" : "Cambiar a lectura de báscula";
        if (input) input.disabled = currentState.automatic || currentState.readOnly;
        setStatus(factor, currentState.automatic ? "Conectando báscula…" : "Modo manual");
    }

    window.alternarModoPesoFactor = factor => {
        if (!factorConfig[factor] || state[factor].readOnly) return;

        if (!state[factor].automatic && !scaleConfigured()) {
            activeFactor = factor;
            window.abrirConfiguracionBasculaFactor();
            return;
        }

        state[factor].automatic = !state[factor].automatic;
        updateScaleModeUi(factor);

        if (state[factor].automatic) {
            startScaleLoop(factor);
        } else {
            stopScaleLoop();
            selectWeightTarget(factor, state[factor].target);
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
            state[factor].automatic = false;
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

    window.guardarConfiguracionBasculaFactor = () => {
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
            state[activeFactor].automatic = true;
            updateScaleModeUi(activeFactor);
            startScaleLoop(activeFactor);
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

    function prepareCaptureModal(factor, readOnly) {
        activeFactor = factor;
        state[factor].readOnly = Boolean(readOnly);
        state[factor].target = "entrada";
        state[factor].weight = 0;
        state[factor].automatic = scaleConfigured() && !readOnly;

        const modal = getModal(factor);
        if (modal) {
            modal.dataset.readonly = readOnly ? "true" : "false";
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
        if (state[factor].automatic) startScaleLoop(factor);
    }

    function getConfirmationData(factor) {
        const config = factorConfig[factor];
        const lot = document.getElementById(config.lotId)?.textContent?.trim() || "—";

        if (factor === "hueso") {
            const rows = Array.from(getTable("hueso")?.querySelectorAll("tr[data-fkhueso]") || []);
            const captured = rows.filter(row => numberValue(row.querySelector(".kg-entrada")?.value) > 0 || numberValue(row.querySelector(".kg-salida")?.value) > 0);
            if (captured.length === 0) throw new Error("Captura al menos un peso de hueso antes de confirmar.");

            return {
                lot,
                factor: config.label,
                selection: `${captured.length} tipo${captured.length === 1 ? "" : "s"} de hueso con captura`,
                entry: captured.reduce((sum, row) => sum + numberValue(row.querySelector(".kg-entrada")?.value), 0),
                exit: captured.reduce((sum, row) => sum + numberValue(row.querySelector(".kg-salida")?.value), 0)
            };
        }

        const selected = getSelectedData(factor);
        if (!selected) throw new Error(factor === "recorte" ? "Selecciona una receta antes de confirmar." : "No hay información para confirmar.");
        const entry = numberValue(selected.entryInput?.value);
        const exit = numberValue(selected.exitInput?.value);
        if (entry <= 0 && exit <= 0) throw new Error("Captura al menos un peso antes de confirmar.");

        return { lot, factor: config.label, selection: selected.name, entry, exit };
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

    function installObservers() {
        Object.keys(factorConfig).forEach(factor => {
            const table = getTable(factor);
            if (!table) return;
            new MutationObserver(() => window.requestAnimationFrame(() => renderCatalog(factor)))
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
                prepareCaptureModal("hueso", readOnly);
                return originalOpenBone.call(this, idPrueba, lot, scroll, readOnly);
            };
        }

        if (typeof originalOpenTrim === "function") {
            window.abrirPruebaRecorte = function (idPrueba, lot, readOnly = false) {
                activeTestId = Number(idPrueba) || 0;
                prepareCaptureModal("recorte", readOnly);
                return originalOpenTrim.call(this, idPrueba, lot, readOnly);
            };
        }

        if (typeof originalOpenFat === "function") {
            window.abrirPruebaGrasa = function (idPrueba, lot, readOnly = false) {
                activeTestId = Number(idPrueba) || 0;
                prepareCaptureModal("grasa", readOnly);
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
                return originalClose.call(this, id);
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
