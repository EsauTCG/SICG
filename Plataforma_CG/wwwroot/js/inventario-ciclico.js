(() => {
    "use strict";

    const API = "/InventarioCiclico";
    const $ = id => document.getElementById(id);

    const el = {
        button: $("btnCiclicos"),
        modal: $("cyclicModal"),
        close: $("btnCloseCyclic"),
        setupTab: $("btnCyclicSetupTab"),
        reportTab: $("btnCyclicReportTab"),
        setupView: $("cyclicSetupView"),
        reportView: $("cyclicReportView"),
        setupPanel: $("cyclicSetupPanel"),
        activePanel: $("cyclicActivePanel"),

        year: $("cycYear"),
        plant: $("cycPlant"),
        warehouse: $("cycWarehouse"),
        pendingOnly: $("cycPendingOnly"),
        load: $("btnCycLoad"),
        camera: $("cycCameraFilter"),
        rack: $("cycRackFilter"),
        search: $("cycSearch"),
        selectVisible: $("btnCycSelectVisible"),
        clearSelection: $("btnCycClearSelection"),
        catalogBody: $("cycCatalogBody"),
        selectionSummary: $("cycSelectionSummary"),
        start: $("btnCycStart"),
        setupStatus: $("cycSetupStatus"),
        catalogKpiTotal: $("cycCatalogKpiTotal"),
        catalogKpiDone: $("cycCatalogKpiDone"),
        catalogKpiPending: $("cycCatalogKpiPending"),
        catalogKpiVisible: $("cycCatalogKpiVisible"),
        catalogKpiBoxes: $("cycCatalogKpiBoxes"),
        catalogKpiKg: $("cycCatalogKpiKg"),
        locationFilterWrap: $("cycLocationFilterWrap"),

        sessionFolio: $("cycSessionFolio"),
        sessionMeta: $("cycSessionMeta"),
        progressBar: $("cycProgressBar"),
        progressText: $("cycProgressText"),
        kExpected: $("cycKExpected"),
        kFound: $("cycKFound"),
        kMissing: $("cycKMissing"),
        kExtras: $("cycKExtras"),
        kMisplaced: $("cycKMisplaced"),
        kInvalid: $("cycKInvalid"),
        activeLocationWrap: $("cycActiveLocationWrap"),
        activeLocation: $("cycActiveLocation"),
        scanInput: $("cycScanInput"),
        lastLive: $("cycLastLive"),
        lastCode: $("cycLastCode"),
        lastDetail: $("cycLastDetail"),
        scopeList: $("cycScopeList"),
        readingList: $("cycReadingList"),
        closeSession: $("btnCycCloseSession"),
        cancelSession: $("btnCycCancelSession"),
        sessionStatus: $("cycSessionStatus"),

        reportYear: $("cycReportYear"),
        reportPlant: $("cycReportPlant"),
        reportWarehouse: $("cycReportWarehouse"),
        reportLoad: $("btnCycReportLoad"),
        reportExcel: $("btnCycReportExcel"),
        reportStatus: $("cycReportStatus"),
        rTotal: $("cycRTotal"),
        rDone: $("cycRDone"),
        rPending: $("cycRPending"),
        rCoverage: $("cycRCoverage"),
        rCycles: $("cycRCycles"),
        rDifferences: $("cycRDifferences"),
        reportProgressBar: $("cycReportProgressBar"),
        reportProgressText: $("cycReportProgressText"),
        reportTableHead: $("cycReportTableHead"),
        reportTableBody: $("cycReportTableBody"),
        reportTabs: document.querySelectorAll(".cyclic-report-mini-tab")
    };

    if (!el.button || !el.modal) return;

    const state = {
        warehouses: [],
        loadedRows: [],
        selected: new Set(),
        session: null,
        scanQueue: [],
        scanBusy: false,
        scanTimer: null,
        report: null,
        reportMode: "UBICACIONES"
    };

    function esc(value) {
        return (value ?? "").toString()
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    function normalize(value) {
        return (value ?? "").toString().trim().toUpperCase();
    }

    function fmtNumber(value, decimals = 0) {
        const n = Number(value || 0);
        return new Intl.NumberFormat("es-MX", {
            minimumFractionDigits: decimals,
            maximumFractionDigits: decimals
        }).format(Number.isFinite(n) ? n : 0);
    }

    function fmtDate(value) {
        if (!value) return "--";
        const d = new Date(value);
        if (Number.isNaN(d.getTime())) return value;
        return new Intl.DateTimeFormat("es-MX", {
            day: "2-digit",
            month: "2-digit",
            year: "numeric",
            hour: "2-digit",
            minute: "2-digit"
        }).format(d);
    }

    function setStatus(target, message, kind = "info") {
        if (!target) return;
        target.textContent = message || "";
        target.className = `cyclic-status${message ? " show" : ""} ${kind}`;
    }

    async function requestJson(url, options = {}) {
        const controller = new AbortController();
        const timer = setTimeout(() => controller.abort(), options.timeout || 45000);

        try {
            const response = await fetch(url, {
                ...options,
                headers: {
                    "Accept": "application/json",
                    ...(options.body ? { "Content-Type": "application/json" } : {}),
                    ...(options.headers || {})
                },
                signal: controller.signal
            });

            const raw = await response.text();
            let data = null;
            try { data = raw ? JSON.parse(raw) : null; } catch { }

            if (!response.ok) {
                const error = new Error(
                    data?.mensaje || data?.message || raw || `HTTP ${response.status}`
                );
                error.status = response.status;
                error.data = data;
                throw error;
            }

            return data;
        } finally {
            clearTimeout(timer);
        }
    }

    function currentType() {
        return document.querySelector('input[name="cycType"]:checked')?.value || "UBICACION";
    }

    function currentYear() {
        return Number(el.year?.value || new Date().getFullYear());
    }

    function currentPlant() {
        return normalize(el.plant?.value || "P1");
    }

    function currentWarehouse() {
        return normalize(el.warehouse?.value || "");
    }

    function setModalOpen(open) {
        el.modal.classList.toggle("show", open);
        el.modal.setAttribute("aria-hidden", open ? "false" : "true");
        document.body.classList.toggle("cyclic-modal-open", open);
    }

    async function openModal() {
        setModalOpen(true);
        switchMainTab("SETUP");
        setStatus(el.setupStatus, "Preparando inventarios cíclicos…", "info");

        try {
            await ensureWarehouses();
            await restoreSession();
            if (!state.session) {
                await loadCatalog();
            }
        } catch (error) {
            setStatus(el.setupStatus, error.message || "No se pudo abrir el módulo.", "bad");
        }
    }

    function closeModal() {
        setModalOpen(false);
    }

    function switchMainTab(tab) {
        const isReport = tab === "REPORT";
        el.setupTab?.classList.toggle("active", !isReport);
        el.reportTab?.classList.toggle("active", isReport);
        el.setupView?.classList.toggle("cyclic-hidden", isReport);
        el.reportView?.classList.toggle("cyclic-hidden", !isReport);

        if (isReport) {
            syncReportFiltersFromSetup();
            if (!state.report) loadReport();
        }
    }

    async function ensureWarehouses() {
        if (state.warehouses.length > 0) {
            populateWarehouseSelects();
            return;
        }

        const data = await requestJson(`${API}/Almacenes?_=${Date.now()}`);
        state.warehouses = Array.isArray(data?.almacenes) ? data.almacenes : [];

        if (state.warehouses.length === 0) {
            throw new Error("No tienes almacenes permitidos para inventarios cíclicos.");
        }

        populatePlantOptions();
        populateWarehouseSelects();
    }

    function populatePlantOptions() {
        const plants = [...new Set(state.warehouses.map(x => normalize(x.planta || "P1")))];
        const fill = select => {
            if (!select) return;
            const previous = normalize(select.value);
            select.innerHTML = plants.map(p => `<option value="${esc(p)}">${esc(p)}</option>`).join("");
            if (plants.includes(previous)) select.value = previous;
        };
        fill(el.plant);
        fill(el.reportPlant);
    }

    function warehousesForPlant(plant) {
        const p = normalize(plant);
        return state.warehouses.filter(x => normalize(x.planta || "P1") === p);
    }

    function fillWarehouseSelect(select, plant, preferred = "") {
        if (!select) return;
        const rows = warehousesForPlant(plant);
        const old = normalize(preferred || select.value);
        select.innerHTML = rows.map(x => `
            <option value="${esc(x.id)}">${esc(x.nombreMostrar || x.nombre || x.id)}</option>
        `).join("");

        if (rows.some(x => normalize(x.id) === old)) {
            select.value = rows.find(x => normalize(x.id) === old)?.id || rows[0]?.id || "";
        }
    }

    function populateWarehouseSelects() {
        fillWarehouseSelect(el.warehouse, currentPlant());
        fillWarehouseSelect(el.reportWarehouse, normalize(el.reportPlant?.value || currentPlant()));
    }

    function resetSelection() {
        state.selected.clear();
        updateSelectionSummary();
    }

    function setTypeUi() {
        const type = currentType();
        el.locationFilterWrap?.classList.toggle("cyclic-hidden", type !== "UBICACION");
        resetSelection();
        state.loadedRows = [];
        renderCatalogRows();
    }

    async function loadCatalog() {
        if (state.session) return;

        const year = currentYear();
        const plant = currentPlant();
        const warehouse = currentWarehouse();
        const type = currentType();

        if (!warehouse) {
            setStatus(el.setupStatus, "Selecciona un almacén.", "warn");
            return;
        }

        el.load.disabled = true;
        el.load.innerHTML = '<span class="spinner-border spinner-border-sm"></span> Consultando…';
        setStatus(el.setupStatus, "Consultando recorrido e inventario real en MEAT…", "info");

        try {
            let data;
            if (type === "UBICACION") {
                const qs = new URLSearchParams({
                    anio: year,
                    planta: plant,
                    almacenId: warehouse,
                    soloPendientes: String(Boolean(el.pendingOnly?.checked))
                });
                data = await requestJson(`${API}/Catalogo?${qs}`);
                state.loadedRows = Array.isArray(data?.ubicaciones) ? data.ubicaciones : [];
                populateLocationFilters(state.loadedRows);

                el.catalogKpiTotal.textContent = fmtNumber(data?.totalCatalogo);
                el.catalogKpiDone.textContent = fmtNumber(data?.inventariadas);
                el.catalogKpiPending.textContent = fmtNumber(data?.pendientes);

                const outside = Array.isArray(data?.fueraCatalogo) ? data.fueraCatalogo : [];
                if (outside.length > 0) {
                    setStatus(
                        el.setupStatus,
                        `Listo. MEAT tiene ${outside.length} ubicación(es) con producto que no están en el catálogo anual; se muestran solo las del recorrido.`,
                        "warn"
                    );
                } else {
                    setStatus(el.setupStatus, "Recorrido cargado. Selecciona las ubicaciones a inventariar.", "ok");
                }
            } else {
                const qs = new URLSearchParams({
                    anio: year,
                    planta: plant,
                    almacenId: warehouse,
                    soloPendientes: String(Boolean(el.pendingOnly?.checked))
                });
                data = await requestJson(`${API}/Skus?${qs}`);
                state.loadedRows = Array.isArray(data?.skus) ? data.skus : [];
                el.catalogKpiTotal.textContent = fmtNumber(state.loadedRows.length);
                el.catalogKpiDone.textContent = "--";
                el.catalogKpiPending.textContent = fmtNumber(state.loadedRows.length);
                setStatus(el.setupStatus, "SKUs activos de MEAT cargados.", "ok");
            }

            resetSelection();
            renderCatalogRows();
        } catch (error) {
            state.loadedRows = [];
            renderCatalogRows();
            setStatus(el.setupStatus, error.message || "No se pudo consultar el cíclico.", "bad");
        } finally {
            el.load.disabled = false;
            el.load.innerHTML = '<i class="fa-solid fa-rotate me-1"></i> Consultar';
        }
    }

    function populateLocationFilters(rows) {
        if (!el.camera || !el.rack) return;
        const oldCamera = el.camera.value;
        const oldRack = el.rack.value;
        const cameras = [...new Set(rows.map(x => x.camara).filter(Boolean))].sort();
        const racks = [...new Set(rows.map(x => x.rack).filter(Boolean))]
            .sort((a, b) => Number(a.replace(/\D/g, "")) - Number(b.replace(/\D/g, "")));

        el.camera.innerHTML = '<option value="">Todas las cámaras</option>'
            + cameras.map(x => `<option>${esc(x)}</option>`).join("");
        el.rack.innerHTML = '<option value="">Todos los racks</option>'
            + racks.map(x => `<option>${esc(x)}</option>`).join("");

        if (cameras.includes(oldCamera)) el.camera.value = oldCamera;
        if (racks.includes(oldRack)) el.rack.value = oldRack;
    }

    function visibleRows() {
        const type = currentType();
        const search = normalize(el.search?.value);
        const camera = normalize(el.camera?.value);
        const rack = normalize(el.rack?.value);

        return state.loadedRows.filter(row => {
            if (type === "UBICACION") {
                if (camera && normalize(row.camara) !== camera) return false;
                if (rack && normalize(row.rack) !== rack) return false;
                if (search) {
                    const text = normalize(`${row.ubicacion} ${row.camara} ${row.rack} ${row.observacion || ""}`);
                    if (!text.includes(search)) return false;
                }
                return true;
            }

            if (search) {
                const text = normalize(`${row.sku} ${row.producto || ""} ${(row.ubicaciones || []).join(" ")}`);
                if (!text.includes(search)) return false;
            }
            return true;
        });
    }

    function rowKey(row) {
        return currentType() === "UBICACION" ? normalize(row.ubicacion) : normalize(row.sku);
    }

    function renderCatalogRows() {
        const rows = visibleRows();
        const type = currentType();

        if (!el.catalogBody) return;

        if (rows.length === 0) {
            el.catalogBody.innerHTML = `
                <tr><td colspan="8" class="cyclic-empty">
                    ${state.loadedRows.length === 0 ? "Consulta el recorrido para comenzar." : "No hay registros con los filtros actuales."}
                </td></tr>`;
            updateCatalogKpis(rows);
            return;
        }

        el.catalogBody.innerHTML = rows.map(row => {
            const key = rowKey(row);
            const checked = state.selected.has(key);
            if (type === "UBICACION") {
                return `
                    <tr class="${checked ? "is-selected" : ""} ${row.inventariada ? "is-done" : ""}" data-key="${esc(key)}">
                        <td data-label="Seleccionar"><input class="form-check-input js-cyc-select" type="checkbox" data-key="${esc(key)}" ${checked ? "checked" : ""}></td>
                        <td data-label="Ubicación">
                            <div class="location-main">${esc(row.ubicacion)}</div>
                            <div class="location-sub">${esc(row.camara || "")} · ${esc(row.rack || "")}</div>
                        </td>
                        <td data-label="Estatus año">${row.inventariada ? `<span class="badge text-bg-success">Inventariada</span>` : `<span class="badge text-bg-warning">Pendiente</span>`}</td>
                        <td data-label="Cajas" class="num">${fmtNumber(row.cajas)}</td>
                        <td data-label="Kg" class="num">${fmtNumber(row.kg, 3)}</td>
                        <td data-label="SKU" class="num">${fmtNumber(row.skus)}</td>
                        <td data-label="Último inventario">${row.ultimaFecha ? esc(fmtDate(row.ultimaFecha)) : "--"}</td>
                        <td data-label="Observación">${row.observacion ? `<span class="text-warning fw-bold" title="${esc(row.observacion)}"><i class="fa-solid fa-triangle-exclamation"></i></span>` : ""}</td>
                    </tr>`;
            }

            const locations = Array.isArray(row.ubicaciones) ? row.ubicaciones : [];
            return `
                <tr class="${checked ? "is-selected" : ""} ${row.inventariado ? "is-done" : ""}" data-key="${esc(key)}">
                    <td data-label="Seleccionar"><input class="form-check-input js-cyc-select" type="checkbox" data-key="${esc(key)}" ${checked ? "checked" : ""}></td>
                    <td data-label="SKU">
                        <div class="location-main">${esc(row.sku)}</div>
                        <div class="location-sub">${esc(row.producto || "Sin descripción")}</div>
                    </td>
                    <td data-label="Estatus año">${row.inventariado ? `<span class="badge text-bg-success">Inventariado</span>` : `<span class="badge text-bg-warning">Pendiente</span>`}</td>
                    <td data-label="Cajas" class="num">${fmtNumber(row.cajas)}</td>
                    <td data-label="Kg" class="num">${fmtNumber(row.kg, 3)}</td>
                    <td data-label="Ubicaciones" class="num">${fmtNumber(row.totalUbicaciones)}</td>
                    <td data-label="Ubicaciones físicas" colspan="2" title="${esc(locations.join(", "))}">${esc(locations.slice(0, 4).join(", "))}${locations.length > 4 ? ` +${locations.length - 4}` : ""}</td>
                </tr>`;
        }).join("");

        el.catalogBody.querySelectorAll(".js-cyc-select").forEach(input => {
            input.addEventListener("change", event => {
                const key = normalize(event.currentTarget.dataset.key);
                if (event.currentTarget.checked) state.selected.add(key);
                else state.selected.delete(key);
                renderCatalogRows();
                updateSelectionSummary();
            });
        });

        updateCatalogKpis(rows);
        updateSelectionSummary();
    }

    function updateCatalogKpis(rows = visibleRows()) {
        el.catalogKpiVisible.textContent = fmtNumber(rows.length);
        const boxes = rows.reduce((a, x) => a + Number(x.cajas || 0), 0);
        const kg = rows.reduce((a, x) => a + Number(x.kg || 0), 0);
        el.catalogKpiBoxes.textContent = fmtNumber(boxes);
        el.catalogKpiKg.textContent = fmtNumber(kg, 3);
    }

    function updateSelectionSummary() {
        if (!el.selectionSummary) return;
        const selectedRows = state.loadedRows.filter(x => state.selected.has(rowKey(x)));
        const boxes = selectedRows.reduce((a, x) => a + Number(x.cajas || 0), 0);
        const kg = selectedRows.reduce((a, x) => a + Number(x.kg || 0), 0);
        el.selectionSummary.textContent = `${state.selected.size} seleccionado(s) · ${fmtNumber(boxes)} cajas actuales · ${fmtNumber(kg, 3)} kg`;
        el.start.disabled = state.selected.size === 0;
    }

    function selectVisible() {
        visibleRows().forEach(row => state.selected.add(rowKey(row)));
        renderCatalogRows();
    }

    function clearSelection() {
        resetSelection();
        renderCatalogRows();
    }

    async function startCycle() {
        if (state.selected.size === 0) return;

        const payload = {
            anio: currentYear(),
            planta: currentPlant(),
            almacenId: currentWarehouse(),
            tipo: currentType(),
            claves: [...state.selected]
        };

        const label = currentType() === "UBICACION" ? "ubicaciones" : "SKU";
        if (!window.confirm(`Se congelará la fotografía MEAT de ${state.selected.size} ${label}. ¿Iniciar el cíclico?`)) {
            return;
        }

        el.start.disabled = true;
        el.start.innerHTML = '<span class="spinner-border spinner-border-sm"></span> Iniciando…';
        setStatus(el.setupStatus, "Tomando fotografía de Produccion + ProduccionReferencia en MEAT…", "info");

        try {
            const data = await requestJson(`${API}/Iniciar`, {
                method: "POST",
                body: JSON.stringify(payload),
                timeout: 120000
            });
            state.session = data?.sesion || null;
            renderSession();
        } catch (error) {
            if (error.status === 409) {
                await restoreSession();
            }
            setStatus(el.setupStatus, error.message || "No se pudo iniciar el cíclico.", "bad");
        } finally {
            el.start.innerHTML = '<i class="fa-solid fa-play me-1"></i> Iniciar cíclico';
            el.start.disabled = state.selected.size === 0;
        }
    }

    async function restoreSession() {
        const data = await requestJson(`${API}/SesionActiva?_=${Date.now()}`);
        state.session = data?.sesion || null;
        renderSession();
    }

    function renderSession() {
        const s = state.session;
        const active = Boolean(s && normalize(s.estatus) === "ABIERTO");
        el.setupPanel?.classList.toggle("cyclic-hidden", active);
        el.activePanel?.classList.toggle("cyclic-hidden", !active);

        if (!active) return;

        el.sessionFolio.textContent = s.folio || `Cíclico #${s.id}`;
        el.sessionMeta.textContent = `${s.tipo} · ${s.planta} · ${s.almacenNombre || s.almacenId} · iniciado ${fmtDate(s.fechaInicio)}`;

        const k = s.kpis || {};
        el.kExpected.textContent = fmtNumber(k.esperadas);
        el.kFound.textContent = fmtNumber(k.encontradasEsperadas);
        el.kMissing.textContent = fmtNumber(k.faltantes);
        el.kExtras.textContent = fmtNumber(k.sobrantes);
        el.kMisplaced.textContent = fmtNumber(k.malUbicadas);
        el.kInvalid.textContent = fmtNumber(k.invalidas);

        const progress = Math.max(0, Math.min(100, Number(k.avance || 0)));
        el.progressBar.style.width = `${progress}%`;
        el.progressText.textContent = `${fmtNumber(progress, 2)}% · ${fmtNumber(k.kgLeidos, 3)} kg leídos / ${fmtNumber(k.kgEsperados, 3)} kg esperados`;

        const isLocation = normalize(s.tipo) === "UBICACION";
        el.activeLocationWrap?.classList.toggle("cyclic-hidden", !isLocation);

        if (isLocation) {
            const previous = normalize(el.activeLocation?.value);
            const scopes = Array.isArray(s.alcance) ? s.alcance : [];
            el.activeLocation.innerHTML = scopes.map(x => `
                <option value="${esc(x.Clave ?? x.clave)}">${esc(x.Clave ?? x.clave)} · faltan ${fmtNumber(x.Faltantes ?? x.faltantes)}</option>
            `).join("");

            const keys = scopes.map(x => normalize(x.Clave ?? x.clave));
            if (keys.includes(previous)) {
                el.activeLocation.value = previous;
            } else {
                const next = scopes.find(x => Number(x.Faltantes ?? x.faltantes ?? 0) > 0) || scopes[0];
                if (next) el.activeLocation.value = next.Clave ?? next.clave;
            }
        }

        renderScopeList();
        renderReadings();
        setStatus(el.sessionStatus, "Cíclico abierto. Continúa escaneando; puedes cerrar la ventana y retomarlo después.", "info");

        setTimeout(() => el.scanInput?.focus(), 80);
    }

    function renderScopeList() {
        if (!el.scopeList || !state.session) return;
        const scopes = Array.isArray(state.session.alcance) ? state.session.alcance : [];
        const activeKey = normalize(el.activeLocation?.value);

        el.scopeList.innerHTML = scopes.map(x => {
            const key = x.Clave ?? x.clave;
            const expected = Number(x.CajasEsperadas ?? x.cajasEsperadas ?? 0);
            const found = Number(x.Encontradas ?? x.encontradas ?? 0);
            const missing = Number(x.Faltantes ?? x.faltantes ?? 0);
            const correct = Number(x.Correctas ?? x.correctas ?? 0);
            const misplaced = Number(x.MalUbicadas ?? x.malUbicadas ?? 0);
            const extra = Number(x.Sobrantes ?? x.sobrantes ?? 0);
            return `
                <div class="cyclic-scope-item ${normalize(key) === activeKey ? "active" : ""}" data-scope="${esc(key)}">
                    <div>
                        <div class="cyclic-scope-key">${esc(key)}</div>
                        <div class="cyclic-scope-sub">${esc(x.Camara ?? x.camara ?? "")} ${esc(x.Rack ?? x.rack ?? "")}</div>
                    </div>
                    <div class="cyclic-scope-count">
                        ${fmtNumber(found)}/${fmtNumber(expected)}<br>
                        <span class="text-muted">F ${fmtNumber(missing)} · OK ${fmtNumber(correct)} · M ${fmtNumber(misplaced)} · S ${fmtNumber(extra)}</span>
                    </div>
                </div>`;
        }).join("") || '<div class="cyclic-empty">Sin alcance.</div>';

        if (normalize(state.session.tipo) === "UBICACION") {
            el.scopeList.querySelectorAll("[data-scope]").forEach(row => {
                row.addEventListener("click", () => {
                    el.activeLocation.value = row.dataset.scope;
                    renderScopeList();
                    el.scanInput?.focus();
                });
            });
        }
    }

    function readingKind(result) {
        const r = normalize(result);
        if (r === "CORRECTA") return "ok";
        if (r === "DUPLICADA") return "dup";
        if (r === "SOBRANTE" || r === "UBICACION_DIFERENTE") return "warn";
        return "err";
    }

    function readingIcon(result) {
        const r = normalize(result);
        if (r === "CORRECTA") return "fa-check";
        if (r === "DUPLICADA") return "fa-copy";
        if (r === "SOBRANTE") return "fa-plus";
        if (r === "UBICACION_DIFERENTE") return "fa-location-dot";
        return "fa-triangle-exclamation";
    }

    function renderReadings() {
        if (!el.readingList || !state.session) return;
        const rows = Array.isArray(state.session.lecturas) ? state.session.lecturas : [];
        if (rows.length === 0) {
            el.readingList.innerHTML = '<div class="cyclic-empty">Aún no hay lecturas.</div>';
            return;
        }

        el.readingList.innerHTML = rows.map(x => {
            const result = x.resultado || "";
            const loc = x.ubicacionCaptura || x.ubicacionMeat || "";
            return `
                <div class="cyclic-reading ${readingKind(result)}">
                    <div class="cyclic-reading-icon"><i class="fa-solid ${readingIcon(result)}"></i></div>
                    <div>
                        <div class="cyclic-reading-code">${esc(x.codigoEtiqueta)}</div>
                        <div class="cyclic-reading-detail">${esc(x.sku || "")} ${x.producto ? `· ${esc(x.producto)}` : ""} ${loc ? `· ${esc(loc)}` : ""} · ${fmtNumber(x.pesoNeto, 3)} kg</div>
                    </div>
                    <div class="cyclic-reading-badge">${esc(result)}</div>
                </div>`;
        }).join("");
    }

    function showLive(code, detail, kind = "info") {
        el.lastCode.textContent = code || "--";
        el.lastDetail.textContent = detail || "";
        el.lastLive.style.borderColor = kind === "ok" ? "#75b798" : kind === "warn" ? "#ffda6a" : kind === "bad" ? "#ea868f" : "#9ec5fe";
        el.lastLive.style.background = kind === "ok" ? "#edf9f2" : kind === "warn" ? "#fff8df" : kind === "bad" ? "#fff0f1" : "#eef5ff";
    }

    function enqueueScan(raw) {
        if (!state.session || normalize(state.session.estatus) !== "ABIERTO") return;
        const code = normalize(raw);
        if (!code) return;

        const isLocation = normalize(state.session.tipo) === "UBICACION";
        const capture = isLocation ? normalize(el.activeLocation?.value) : "";
        if (isLocation && !capture) {
            setStatus(el.sessionStatus, "Selecciona la ubicación activa antes de escanear.", "warn");
            return;
        }

        state.scanQueue.push({ codigoEtiqueta: code, ubicacionCaptura: capture || null });
        showLive(code, `En cola para validar${capture ? ` en ${capture}` : ""}…`, "info");
        el.scanInput.value = "";

        if (state.scanTimer) clearTimeout(state.scanTimer);
        state.scanTimer = setTimeout(flushScans, 110);
    }

    async function flushScans() {
        if (state.scanBusy || state.scanQueue.length === 0 || !state.session) return;
        state.scanBusy = true;

        try {
            while (state.scanQueue.length > 0 && state.session) {
                const batch = state.scanQueue.splice(0, 25);
                const data = await requestJson(`${API}/RegistrarLecturas`, {
                    method: "POST",
                    body: JSON.stringify({
                        ciclicoId: state.session.id,
                        lecturas: batch
                    }),
                    timeout: 120000
                });

                const results = Array.isArray(data?.results) ? data.results : [];
                const last = results[results.length - 1];
                if (last) {
                    const kind = last.kind === "ok" ? "ok" : last.kind === "warn" ? "warn" : last.kind === "dup" ? "info" : "bad";
                    showLive(last.codigoEtiqueta, `${last.resultado}: ${last.msg || ""}`, kind);
                }

                if (data?.sesion) {
                    state.session = data.sesion;
                    renderSession();
                }
            }
        } catch (error) {
            setStatus(el.sessionStatus, error.message || "No se pudieron registrar las lecturas.", "bad");
        } finally {
            state.scanBusy = false;
            el.scanInput?.focus();
        }
    }

    async function closeCycle() {
        if (!state.session) return;
        await flushScans();
        if (state.scanQueue.length > 0 || state.scanBusy) {
            setStatus(el.sessionStatus, "Todavía hay lecturas procesándose.", "warn");
            return;
        }

        const k = state.session.kpis || {};
        const detail = `Faltantes ${fmtNumber(k.faltantes)}, sobrantes ${fmtNumber(k.sobrantes)}, mal ubicadas ${fmtNumber(k.malUbicadas)}.`;
        if (!window.confirm(`¿Cerrar ${state.session.folio}? ${detail}`)) return;

        el.closeSession.disabled = true;
        try {
            const data = await requestJson(`${API}/Cerrar`, {
                method: "POST",
                body: JSON.stringify({ ciclicoId: state.session.id }),
                timeout: 60000
            });
            setStatus(el.sessionStatus, data?.mensaje || "Cíclico cerrado.", data?.sesion?.estatus === "COMPLETADO" ? "ok" : "warn");
            state.session = null;
            resetSelection();
            renderSession();
            await loadCatalog();
        } catch (error) {
            setStatus(el.sessionStatus, error.message || "No se pudo cerrar el cíclico.", "bad");
        } finally {
            el.closeSession.disabled = false;
        }
    }

    async function cancelCycle() {
        if (!state.session) return;
        if (!window.confirm(`¿Cancelar ${state.session.folio}? Este cíclico NO contará como cobertura anual.`)) return;

        try {
            await requestJson(`${API}/Cancelar`, {
                method: "POST",
                body: JSON.stringify({ ciclicoId: state.session.id })
            });
            state.session = null;
            resetSelection();
            renderSession();
            await loadCatalog();
        } catch (error) {
            setStatus(el.sessionStatus, error.message || "No se pudo cancelar.", "bad");
        }
    }

    function syncReportFiltersFromSetup() {
        if (el.reportYear) el.reportYear.value = el.year?.value || new Date().getFullYear();
        if (el.reportPlant) el.reportPlant.value = currentPlant();
        fillWarehouseSelect(el.reportWarehouse, normalize(el.reportPlant?.value), currentWarehouse());
    }

    async function loadReport() {
        const year = Number(el.reportYear?.value || new Date().getFullYear());
        const plant = normalize(el.reportPlant?.value || "P1");
        const warehouse = normalize(el.reportWarehouse?.value || "");
        if (!warehouse) return;

        el.reportLoad.disabled = true;
        setStatus(el.reportStatus, "Consultando cobertura anual…", "info");

        try {
            const qs = new URLSearchParams({ anio: year, planta: plant, almacenId: warehouse });
            state.report = await requestJson(`${API}/Reporte?${qs}`, { timeout: 120000 });
            setStatus(el.reportStatus, "Reporte actualizado.", "ok");
            renderReport();
        } catch (error) {
            state.report = null;
            setStatus(el.reportStatus, error.message || "No se pudo consultar el reporte.", "bad");
            renderReport();
        } finally {
            el.reportLoad.disabled = false;
        }
    }

    function renderReport() {
        const r = state.report;
        if (!r) {
            el.reportTableHead.innerHTML = "";
            el.reportTableBody.innerHTML = '<tr><td class="cyclic-empty">Sin reporte cargado.</td></tr>';
            return;
        }

        el.rTotal.textContent = fmtNumber(r.catalogoTotal);
        el.rDone.textContent = fmtNumber(r.ubicacionesInventariadas);
        el.rPending.textContent = fmtNumber(r.ubicacionesPendientes);
        el.rCoverage.textContent = `${fmtNumber(r.coberturaPct, 2)}%`;
        el.rCycles.textContent = fmtNumber(r.ciclicosCompletados);
        el.rDifferences.textContent = fmtNumber(Number(r.cajasFaltantes || 0) + Number(r.cajasSobrantes || 0) + Number(r.cajasMalUbicadas || 0));

        const pct = Math.max(0, Math.min(100, Number(r.coberturaPct || 0)));
        el.reportProgressBar.style.width = `${pct}%`;
        el.reportProgressText.textContent = `${fmtNumber(r.ubicacionesInventariadas)} de ${fmtNumber(r.catalogoTotal)} ubicaciones · ${fmtNumber(pct, 2)}%`;

        renderReportTable();
    }

    function renderReportTable() {
        const r = state.report;
        if (!r) return;

        if (state.reportMode === "CICLICOS") {
            el.reportTableHead.innerHTML = `
                <tr><th>Folio</th><th>Tipo</th><th>Estatus</th><th>Usuario</th><th>Inicio</th><th>Cierre</th><th class="num">Esperadas</th><th class="num">Encontradas</th><th class="num">Sobrantes</th><th class="num">Mal ubicadas</th></tr>`;
            const rows = Array.isArray(r.ciclicos) ? r.ciclicos : [];
            el.reportTableBody.innerHTML = rows.map(x => `
                <tr>
                    <td data-label="Folio"><b>${esc(x.Folio)}</b></td><td data-label="Tipo">${esc(x.Tipo)}</td><td data-label="Estatus">${esc(x.Estatus)}</td><td data-label="Usuario">${esc(x.Usuario)}</td>
                    <td data-label="Inicio">${esc(fmtDate(x.FechaInicio))}</td><td data-label="Cierre">${esc(fmtDate(x.FechaCierre))}</td>
                    <td data-label="Esperadas" class="num">${fmtNumber(x.CajasEsperadas)}</td><td data-label="Encontradas" class="num">${fmtNumber(x.CajasEncontradasEsperadas)}</td>
                    <td data-label="Sobrantes" class="num">${fmtNumber(x.CajasSobrantes)}</td><td data-label="Mal ubicadas" class="num">${fmtNumber(x.MalUbicadas)}</td>
                </tr>`).join("") || '<tr><td colspan="10" class="cyclic-empty">Sin cíclicos.</td></tr>';
            return;
        }

        if (state.reportMode === "SKU") {
            el.reportTableHead.innerHTML = `
                <tr><th>SKU</th><th>Producto</th><th class="num">Veces inventariado</th><th>Última fecha</th></tr>`;
            const rows = Array.isArray(r.skus) ? r.skus : [];
            el.reportTableBody.innerHTML = rows.map(x => `
                <tr><td data-label="SKU"><b>${esc(x.SKU)}</b></td><td data-label="Producto">${esc(x.Producto)}</td><td data-label="Veces inventariado" class="num">${fmtNumber(x.VecesInventariado)}</td><td data-label="Última fecha">${esc(fmtDate(x.UltimaFecha))}</td></tr>`).join("") || '<tr><td colspan="4" class="cyclic-empty">Sin cobertura SKU.</td></tr>';
            return;
        }

        el.reportTableHead.innerHTML = `
            <tr><th>Cámara</th><th>Rack</th><th>Ubicación</th><th>Estatus</th><th>Último folio</th><th>Última fecha</th><th class="num">Esperadas</th><th class="num">Encontradas</th><th class="num">Diferencia</th><th class="num">Mal ubicadas</th></tr>`;
        const rows = Array.isArray(r.cobertura) ? r.cobertura : [];
        el.reportTableBody.innerHTML = rows.map(x => `
            <tr class="${normalize(x.Estatus) === "INVENTARIADA" ? "is-done" : ""}">
                <td data-label="Cámara">${esc(x.Camara)}</td><td data-label="Rack">${esc(x.Rack)}</td><td data-label="Ubicación"><b>${esc(x.Ubicacion)}</b></td>
                <td data-label="Estatus">${normalize(x.Estatus) === "INVENTARIADA" ? '<span class="badge text-bg-success">Inventariada</span>' : '<span class="badge text-bg-warning">Pendiente</span>'}</td>
                <td data-label="Último folio">${esc(x.UltimoFolio || "--")}</td><td data-label="Última fecha">${esc(fmtDate(x.UltimaFecha))}</td>
                <td data-label="Esperadas" class="num">${fmtNumber(x.CajasEsperadas)}</td><td data-label="Encontradas" class="num">${fmtNumber(x.CajasEncontradas)}</td>
                <td data-label="Diferencia" class="num">${fmtNumber(x.Diferencia)}</td><td data-label="Mal ubicadas" class="num">${fmtNumber(x.MalUbicadas)}</td>
            </tr>`).join("") || '<tr><td colspan="10" class="cyclic-empty">Sin catálogo.</td></tr>';
    }

    function downloadReportExcel() {
        const year = Number(el.reportYear?.value || new Date().getFullYear());
        const plant = normalize(el.reportPlant?.value || "P1");
        const warehouse = normalize(el.reportWarehouse?.value || "");
        if (!warehouse) return;
        const qs = new URLSearchParams({ anio: year, planta: plant, almacenId: warehouse });
        window.location.href = `${API}/ReporteExcel?${qs}`;
    }

    // ============================================================
    // Eventos
    // ============================================================

    el.button.addEventListener("click", openModal);
    el.close?.addEventListener("click", closeModal);
    el.setupTab?.addEventListener("click", () => switchMainTab("SETUP"));
    el.reportTab?.addEventListener("click", () => switchMainTab("REPORT"));

    el.plant?.addEventListener("change", () => {
        fillWarehouseSelect(el.warehouse, currentPlant());
        resetSelection();
        loadCatalog();
    });

    el.warehouse?.addEventListener("change", () => {
        resetSelection();
        loadCatalog();
    });

    document.querySelectorAll('input[name="cycType"]').forEach(input => {
        input.addEventListener("change", () => {
            setTypeUi();
            loadCatalog();
        });
    });

    el.pendingOnly?.addEventListener("change", loadCatalog);
    el.load?.addEventListener("click", loadCatalog);
    el.camera?.addEventListener("change", renderCatalogRows);
    el.rack?.addEventListener("change", renderCatalogRows);
    el.search?.addEventListener("input", renderCatalogRows);
    el.selectVisible?.addEventListener("click", selectVisible);
    el.clearSelection?.addEventListener("click", clearSelection);
    el.start?.addEventListener("click", startCycle);

    el.activeLocation?.addEventListener("change", () => {
        renderScopeList();
        el.scanInput?.focus();
    });

    el.scanInput?.addEventListener("keydown", event => {
        if (event.key !== "Enter") return;
        event.preventDefault();
        const value = event.currentTarget.value;
        enqueueScan(value);
    });

    el.closeSession?.addEventListener("click", closeCycle);
    el.cancelSession?.addEventListener("click", cancelCycle);

    el.reportPlant?.addEventListener("change", () => {
        fillWarehouseSelect(el.reportWarehouse, normalize(el.reportPlant.value));
        state.report = null;
    });
    el.reportWarehouse?.addEventListener("change", () => state.report = null);
    el.reportYear?.addEventListener("change", () => state.report = null);
    el.reportLoad?.addEventListener("click", loadReport);
    el.reportExcel?.addEventListener("click", downloadReportExcel);

    el.reportTabs?.forEach(button => {
        button.addEventListener("click", () => {
            el.reportTabs.forEach(x => x.classList.remove("active"));
            button.classList.add("active");
            state.reportMode = button.dataset.mode || "UBICACIONES";
            renderReportTable();
        });
    });

    document.addEventListener("keydown", event => {
        if (event.key !== "Escape" || !el.modal.classList.contains("show")) return;
        closeModal();
    });

    document.addEventListener("visibilitychange", () => {
        if (document.visibilityState !== "visible" || !el.modal.classList.contains("show")) return;
        restoreSession().catch(() => { });
    });

    // Defaults
    const current = new Date().getFullYear();
    if (el.year) el.year.value = current;
    if (el.reportYear) el.reportYear.value = current;
    setTypeUi();

    // Cuando se entra directamente por /InventarioCiclico,
    // la pantalla abre automáticamente el módulo cíclico.
    if (window.location.pathname.replace(/\/+$/, "").toLowerCase() === "/inventariociclico") {
        setTimeout(() => {
            openModal().catch(error => {
                setStatus(el.setupStatus, error?.message || "No se pudo abrir el módulo.", "bad");
            });
        }, 0);
    }
})();
