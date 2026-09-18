(() => {
    if (window.__inventarioHandheldKidsLoaded) return;
    window.__inventarioHandheldKidsLoaded = true;

    const READY_DELAY = 120;

    const el = (id) => document.getElementById(id);

    const ui = {
        guide: null,
        helperMessage: null,
        helperDetail: null,
        steps: {},
        miniNote: null
    };

    function onReady(fn) {
        if (document.readyState === "loading") {
            document.addEventListener("DOMContentLoaded", fn, { once: true });
        } else {
            setTimeout(fn, READY_DELAY);
        }
    }

    function isVisible(node) {
        if (!node) return false;
        if (node.classList.contains("d-none")) return false;
        if (node.getAttribute("aria-hidden") === "true") return false;
        return node.offsetParent !== null || window.getComputedStyle(node).display !== "none";
    }

    function getText(id) {
        const node = el(id);
        return node ? (node.textContent || "").trim() : "";
    }

    function getNumberFromText(value) {
        const match = String(value || "").replace(/\./g, "").match(/-?\d+/);
        return match ? Number(match[0]) : 0;
    }

    function selectedWarehouseCount() {
        const meta = getText("warehouseSelectionHint");
        const fromHint = getNumberFromText(meta);
        if (fromHint > 0) return fromHint;

        const choices = document.querySelectorAll('#warehouseChoices input[type="checkbox"]:checked');
        return choices.length;
    }

    function scanCount() {
        return getNumberFromText(getText("scanListCount"));
    }

    function pendingCount() {
        const kQueue = getText("kQueue");
        const fromKpi = getNumberFromText(kQueue);
        if (fromKpi > 0) return fromKpi;
        return scanCount();
    }

    function isSessionActive() {
        return isVisible(el("sessionActiveBlock"));
    }

    function isSessionReadyToStart() {
        const btnStart = el("btnStartSession");
        return !!btnStart && !btnStart.disabled;
    }

    function isFlushEnabled() {
        const btn = el("btnFlush");
        return !!btn && !btn.disabled;
    }

    function getSuggestedAction() {
        const sendingBar = el("sendingBar");
        if (sendingBar && !sendingBar.classList.contains("d-none")) {
            return {
                target: null,
                message: "Guardando información. Espera un momento ⏳",
                detail: "No cierres la pantalla hasta que termine de guardar."
            };
        }

        if (!isSessionActive()) {
            if (selectedWarehouseCount() <= 0) {
                return {
                    target: el("warehouseSelectTrigger"),
                    message: "Paso 1: toca “Selecciona almacén(es)” 👆",
                    detail: "Escoge uno o varios almacenes para preparar tu inventario."
                };
            }

            if (isSessionReadyToStart()) {
                return {
                    target: el("btnStartSession"),
                    message: "Paso 2: toca “Iniciar inventario” ▶️",
                    detail: "Con eso se abre la sesión y podrás empezar a escanear."
                };
            }

            return {
                target: el("warehouseSelectTrigger"),
                message: "Selecciona tus almacenes para comenzar.",
                detail: "En cuanto el botón se habilite, presiona “Iniciar inventario”."
            };
        }

        if (scanCount() <= 0) {
            return {
                target: el("scanInput"),
                message: "Paso 3: escanea una etiqueta 📷",
                detail: "Apunta el lector y revisa abajo la última lectura en grande."
            };
        }

        if (pendingCount() > 0 && isFlushEnabled()) {
            return {
                target: el("btnFlush"),
                message: `Paso 4: ya tienes ${pendingCount()} lectura(s). Toca “Inventariar” ✅`,
                detail: "Eso guarda tus etiquetas pendientes en el inventario real."
            };
        }

        return {
            target: el("scanInput"),
            message: "Sigue escaneando o revisa tus reportes 🚜",
            detail: "Puedes leer más etiquetas, levantar incidencias o abrir reportes."
        };
    }

    function stepState() {
        const selected = selectedWarehouseCount();
        const session = isSessionActive();
        const scans = scanCount();
        const canFlush = isFlushEnabled();

        return {
            step1: selected > 0 ? "done" : "active",
            step2: session ? "done" : (selected > 0 ? "active" : "waiting"),
            step3: scans > 0 ? "done" : (session ? "active" : "waiting"),
            step4: canFlush || scans > 0 ? (canFlush ? "active" : "done") : "waiting"
        };
    }

    function stateText(kind) {
        switch (kind) {
            case "done": return "Listo";
            case "active": return "Hazlo ahora";
            default: return "Pendiente";
        }
    }

    function applyStepVisual(stepButton, state) {
        if (!stepButton) return;
        stepButton.classList.remove("is-done", "is-active", "is-waiting");
        stepButton.classList.add(`is-${state}`);
        const stateNode = stepButton.querySelector(".fork-step-state");
        if (stateNode) {
            stateNode.innerHTML = `<i class="fa-solid ${state === "done" ? "fa-circle-check" : state === "active" ? "fa-hand-pointer" : "fa-clock"}"></i> ${stateText(state)}`;
        }
    }

    function clearFocusTargets() {
        document.querySelectorAll(".fork-focus-target").forEach(node => node.classList.remove("fork-focus-target"));
    }

    function updateQuickUI() {
        if (!ui.guide) return;

        const suggestion = getSuggestedAction();
        const steps = stepState();

        Object.entries(steps).forEach(([key, value]) => applyStepVisual(ui.steps[key], value));

        if (ui.helperMessage) ui.helperMessage.textContent = suggestion.message;
        if (ui.helperDetail) ui.helperDetail.textContent = suggestion.detail;

        clearFocusTargets();
        if (suggestion.target && typeof suggestion.target.classList?.add === "function") {
            suggestion.target.classList.add("fork-focus-target");
        }

        if (ui.miniNote) {
            const scans = scanCount();
            const pending = pendingCount();
            const activeWarehouse = getText("activeWarehouseSelect") || getText("warehouseSelectText");
            ui.miniNote.textContent =
                isSessionActive()
                    ? `Sesión activa • ${activeWarehouse || "Almacén listo"} • ${scans} lectura(s) visibles • ${pending} pendiente(s)`
                    : "Guía rápida: selecciona almacenes → inicia inventario → escanea → inventaria";
        }
    }

    function stepClickHandler(step) {
        const map = {
            "1": el("warehouseSelectTrigger"),
            "2": el("btnStartSession"),
            "3": el("scanInput"),
            "4": el("btnFlush")
        };
        const target = map[step];
        if (!target || target.disabled) return;

        if (target.tagName === "INPUT" || target.tagName === "SELECT") {
            target.focus();
            try { target.select?.(); } catch (_) {}
        } else {
            target.focus();
            target.click();
        }
    }

    function injectGuide() {
        const page = document.querySelector(".handheld-page");
        const header = document.querySelector(".handheld-top");
        if (!page || !header || document.querySelector(".fork-guide")) return;

        const block = document.createElement("section");
        block.className = "fork-guide";
        block.innerHTML = `
            <div class="fork-guide-head">
                <div>
                    <div class="fork-guide-title">
                        <i class="fa-solid fa-hand-pointer"></i>
                        Guía rápida para usarlo fácil
                    </div>
                    <div class="fork-guide-sub">
                        Piensa en 4 pasos simples. Toca cualquier tarjeta para ir directo.
                    </div>
                </div>

                <div class="fork-helper">
                    <div class="fork-helper-badge">
                        <i class="fa-solid fa-bullhorn"></i>
                        ¿Qué hago ahorita?
                    </div>
                    <div id="forkHelperMessage" class="fork-helper-message">
                        Primero selecciona tus almacenes.
                    </div>
                    <div id="forkHelperDetail" class="fork-helper-detail">
                        Así el sistema sabrá dónde empezará el inventario.
                    </div>
                </div>
            </div>

            <div class="fork-steps">
                <button type="button" class="fork-step" data-step="1">
                    <span class="fork-step-num">1</span>
                    <div class="fork-step-title">Escoge almacén</div>
                    <div class="fork-step-desc">Selecciona uno o varios almacenes permitidos.</div>
                    <div class="fork-step-state"></div>
                </button>

                <button type="button" class="fork-step" data-step="2">
                    <span class="fork-step-num">2</span>
                    <div class="fork-step-title">Inicia inventario</div>
                    <div class="fork-step-desc">Abre la sesión para empezar a trabajar.</div>
                    <div class="fork-step-state"></div>
                </button>

                <button type="button" class="fork-step" data-step="3">
                    <span class="fork-step-num">3</span>
                    <div class="fork-step-title">Escanea etiqueta</div>
                    <div class="fork-step-desc">Lee el código y verifica la última lectura.</div>
                    <div class="fork-step-state"></div>
                </button>

                <button type="button" class="fork-step" data-step="4">
                    <span class="fork-step-num">4</span>
                    <div class="fork-step-title">Inventaria</div>
                    <div class="fork-step-desc">Guarda las lecturas pendientes al inventario real.</div>
                    <div class="fork-step-state"></div>
                </button>
            </div>

            <div id="forkMiniNote" class="fork-mini-note"></div>
        `;

        header.insertAdjacentElement("afterend", block);

        ui.guide = block;
        ui.helperMessage = el("forkHelperMessage");
        ui.helperDetail = el("forkHelperDetail");
        ui.miniNote = el("forkMiniNote");
        ui.steps.step1 = block.querySelector('[data-step="1"]');
        ui.steps.step2 = block.querySelector('[data-step="2"]');
        ui.steps.step3 = block.querySelector('[data-step="3"]');
        ui.steps.step4 = block.querySelector('[data-step="4"]');

        block.querySelectorAll(".fork-step").forEach(btn => {
            btn.addEventListener("click", () => stepClickHandler(btn.dataset.step));
        });
    }

    function injectScanHints() {
        const scanHead = document.querySelector(".scan-head");
        if (!scanHead || scanHead.querySelector(".scan-cheats")) return;

        const hints = document.createElement("div");
        hints.className = "scan-cheats";
        hints.innerHTML = `
            <div class="scan-cheat"><i class="fa-solid fa-1"></i><strong>Escanea</strong><br>Lee la etiqueta y revisa que salga abajo.</div>
            <div class="scan-cheat"><i class="fa-solid fa-eye"></i><strong>Valida</strong><br>Si hay error o duplicado, aquí mismo te avisa.</div>
            <div class="scan-cheat"><i class="fa-solid fa-boxes-stacked"></i><strong>Guarda</strong><br>Cuando tengas pendientes, toca “Inventariar”.</div>
        `;
        scanHead.appendChild(hints);
    }

    function improveControls() {
        const scanInput = el("scanInput");
        if (scanInput) {
            scanInput.placeholder = "Escanea aquí la etiqueta";
            scanInput.setAttribute("enterkeyhint", "done");
            scanInput.setAttribute("autocapitalize", "off");
            scanInput.setAttribute("autocorrect", "off");
            scanInput.setAttribute("spellcheck", "false");
        }

        const btnFlushText = el("btnFlushText");
        if (btnFlushText && btnFlushText.textContent.trim() === "Inventariar") {
            btnFlushText.textContent = "Inventariar";
        }
    }

    function watchChanges() {
        const ids = [
            "sessionSetupBlock",
            "sessionActiveBlock",
            "warehouseSelectionHint",
            "warehouseChoices",
            "scanListCount",
            "kQueue",
            "statusMsg",
            "btnFlush",
            "btnStartSession",
            "activeWarehouseSelect",
            "sendingBar"
        ];

        const observer = new MutationObserver(() => updateQuickUI());

        ids.forEach(id => {
            const node = el(id);
            if (!node) return;
            observer.observe(node, {
                attributes: true,
                childList: true,
                subtree: true,
                characterData: true
            });
        });

        document.addEventListener("click", () => {
            window.requestAnimationFrame(updateQuickUI);
        });

        document.addEventListener("input", () => {
            window.requestAnimationFrame(updateQuickUI);
        });

        setInterval(updateQuickUI, 1400);
    }

    function init() {
        if (!document.querySelector(".handheld-page")) return;
        injectGuide();
        injectScanHints();
        improveControls();
        watchChanges();
        updateQuickUI();
    }

    onReady(init);
})();
