(() => {
    "use strict";

    const root = document.getElementById("dashboardVentasApp");
    const cfg = window.dashboardVentasConfig || {};
    if (!root) return;

    const today = new Date();
    const months = [
        "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio",
        "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"
    ];

    const state = {
        year: today.getFullYear(),
        month: today.getMonth() + 1,
        day: today.getDate(),
        compareMode: "budget",

        // Filtros múltiples
        masterVendor: [],
        vendorMaster: [],
        vendorSku: [],
        priceMaster: [],
        priceSku: [],
        trendMaster: [],
        trendSku: []
    };

    let catalogs = { anios: [], masters: [], skus: [], vendedores: [], ultimaFechaVenta: null };
    let refreshToken = 0;
    let refreshTimer = null;

    const $ = id => document.getElementById(id);
    const nf = new Intl.NumberFormat("es-MX", { maximumFractionDigits: 0 });
    const nf2 = new Intl.NumberFormat("es-MX", { minimumFractionDigits: 0, maximumFractionDigits: 2 });
    const money = new Intl.NumberFormat("es-MX", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

    document.addEventListener("DOMContentLoaded", init, { once: true });
    if (document.readyState !== "loading") init();

    let initialized = false;
    async function init() {
        if (initialized) return;
        initialized = true;

        try {
            setLoading(true);
            catalogs = await getJson(cfg.catalogosUrl);

            const ultima = parseDateOnly(catalogs.ultimaFechaVenta);
            const years = Array.from(new Set(catalogs.anios || [])).sort((a, b) => b - a);

            // Abrir SIEMPRE en la última fecha que realmente tenga surtido validado.
            // Así no mostramos 0 solo porque el mes actual todavía no tiene movimientos.
            if (ultima) {
                state.year = ultima.year;
                state.month = ultima.month;
                state.day = ultima.day;
            } else if (!years.includes(state.year)) {
                state.year = years[0] || today.getFullYear();
            }

            buildCatalogs();
            bindEvents();
            renderActiveFilters();
            await refreshAll();
        } catch (err) {
            showError(err);
        } finally {
            setLoading(false);
        }
    }

    function buildCatalogs() {
        fillSelect(
            $("monthSelect"),
            months.map((m, i) => ({
                value: i + 1,
                label: `${m} ${state.year}`
            })),
            state.month
        );

        rebuildDays();

        const vendorOptions = (catalogs.vendedores || [])
            .map(x => ({
                value: String(x.id ?? ""),
                label: x.nombre || `VENDEDOR ${x.id}`
            }))
            .filter(x => x.value);

        const masterOptions = (catalogs.masters || [])
            .map(x => ({
                value: String(x),
                label: String(x)
            }))
            .filter(x => x.value);

        // Unificamos los tres filtros de MASTER.
        state.vendorMaster = normalizeMultiSelection(
            state.vendorMaster,
            masterOptions
        );
        state.priceMaster = [...state.vendorMaster];
        state.trendMaster = [...state.vendorMaster];

        state.masterVendor = normalizeMultiSelection(
            state.masterVendor,
            vendorOptions
        );

        renderMultiSelect(
            "masterVendorFilter",
            vendorOptions,
            state.masterVendor,
            values => {
                state.masterVendor = values;
                renderActiveFilters();
                scheduleRefreshAll();
            },
            "Todos"
        );

        const onMasterChange = (sourceId, values) => {
            state.vendorMaster = [...values];
            state.priceMaster = [...values];
            state.trendMaster = [...values];

            syncMultiSelectGroup(
                [
                    "vendorMasterFilter",
                    "priceMasterFilter",
                    "trendMasterFilter"
                ],
                values,
                sourceId
            );

            rebuildAllSkuFilters();
            renderActiveFilters();
            scheduleRefreshAll();
        };

        renderMultiSelect(
            "vendorMasterFilter",
            masterOptions,
            state.vendorMaster,
            values => onMasterChange("vendorMasterFilter", values),
            "Todos"
        );

        renderMultiSelect(
            "priceMasterFilter",
            masterOptions,
            state.priceMaster,
            values => onMasterChange("priceMasterFilter", values),
            "Todos"
        );

        renderMultiSelect(
            "trendMasterFilter",
            masterOptions,
            state.trendMaster,
            values => onMasterChange("trendMasterFilter", values),
            "Todos"
        );

        rebuildAllSkuFilters();
    }

    function rebuildAllSkuFilters() {
        const selectedMasters = state.vendorMaster || [];

        // Mantener una sola selección global de SKU.
        const seedSkus = state.vendorSku || [];

        const onSkuChange = (sourceId, values) => {
            state.vendorSku = [...values];
            state.priceSku = [...values];
            state.trendSku = [...values];

            syncMultiSelectGroup(
                [
                    "vendorSkuFilter",
                    "priceSkuFilter",
                    "trendSkuFilter"
                ],
                values,
                sourceId
            );

            renderActiveFilters();
            scheduleRefreshAll();
        };

        state.vendorSku = rebuildSkuMultiSelect(
            "vendorSkuFilter",
            selectedMasters,
            seedSkus,
            values => onSkuChange("vendorSkuFilter", values)
        );

        state.priceSku = [...state.vendorSku];
        state.trendSku = [...state.vendorSku];

        rebuildSkuMultiSelect(
            "priceSkuFilter",
            selectedMasters,
            state.priceSku,
            values => onSkuChange("priceSkuFilter", values)
        );

        rebuildSkuMultiSelect(
            "trendSkuFilter",
            selectedMasters,
            state.trendSku,
            values => onSkuChange("trendSkuFilter", values)
        );
    }

    function rebuildDays() {
        const max = new Date(state.year, state.month, 0).getDate();

        if (state.day > max) state.day = max;
        if (state.day < 1) state.day = 1;

        fillSelect(
            $("daySelect"),
            Array.from(
                { length: max },
                (_, i) => ({
                    value: i + 1,
                    label: i + 1
                })
            ),
            state.day
        );
    }

    function rebuildSkuMultiSelect(
        id,
        selectedMasters,
        selectedSkus,
        onChange
    ) {
        const masterSet = new Set(
            (selectedMasters || [])
                .map(x => norm(x))
                .filter(Boolean)
        );

        const skuMap = new Map();

        (catalogs.skus || [])
            .filter(x =>
                masterSet.size === 0 ||
                masterSet.has(norm(x.master))
            )
            .forEach(x => {
                const sku = String(x.sku ?? "").trim();
                if (!sku) return;

                const nombre = String(x.nombre ?? "").trim();

                const label = nombre
                    ? `${sku} - ${nombre}`
                    : sku;

                if (!skuMap.has(sku)) {
                    skuMap.set(sku, label);
                }
            });

        const options = Array
            .from(skuMap.entries())
            .map(([value, label]) => ({
                value,
                label
            }))
            .sort((a, b) =>
                a.value.localeCompare(
                    b.value,
                    "es",
                    { numeric: true }
                )
            );

        const normalized = normalizeMultiSelection(
            selectedSkus,
            options
        );

        renderMultiSelect(
            id,
            options,
            normalized,
            onChange,
            "Todos"
        );

        return normalized;
    }

    function fillSelect(el, options, selected) {
        if (!el) return;

        el.innerHTML = options
            .map(o =>
                `<option value="${esc(o.value)}" ${
                    String(o.value) === String(selected)
                        ? "selected"
                        : ""
                }>${esc(o.label)}</option>`
            )
            .join("");
    }

    function normalizeMultiSelection(selected, options) {
        const allowed = new Set(
            (options || []).map(x => String(x.value))
        );

        return Array.from(
            new Set(
                (Array.isArray(selected) ? selected : [])
                    .map(x => String(x))
                    .filter(x => allowed.has(x))
            )
        );
    }

    function renderMultiSelect(
        id,
        options,
        selected,
        onChange,
        placeholder = "Todos"
    ) {
        const host = $(id);
        if (!host) return;

        const safeOptions = (options || [])
            .map(x => ({
                value: String(x.value ?? ""),
                label: String(x.label ?? x.value ?? "")
            }))
            .filter(x => x.value !== "");

        let selectedValues = normalizeMultiSelection(
            selected,
            safeOptions
        );

        const selectedSet = new Set(selectedValues);

        host.innerHTML = `
            <div class="dvx-ms">
                <button type="button"
                        class="dvx-ms-toggle"
                        aria-haspopup="true"
                        aria-expanded="false">
                    <span class="dvx-ms-label"></span>
                    <span class="dvx-ms-arrow">▼</span>
                </button>

                <div class="dvx-ms-menu" hidden>
                    <div class="dvx-ms-search-wrap">
                        <input type="text"
                               class="dvx-ms-search"
                               placeholder="Buscar..."
                               autocomplete="off" />
                    </div>

                    <label class="dvx-ms-all">
                        <input type="checkbox"
                               class="dvx-ms-check-all" />
                        <span>Seleccionar todo</span>
                    </label>

                    <div class="dvx-ms-options">
                        ${
                            safeOptions.length
                                ? safeOptions.map(o => `
                                    <label class="dvx-ms-option"
                                           data-search="${esc(norm(`${o.value} ${o.label}`))}">
                                        <input type="checkbox"
                                               class="dvx-ms-check"
                                               value="${esc(o.value)}"
                                               data-label="${esc(o.label)}"
                                               ${selectedSet.has(o.value) ? "checked" : ""} />
                                        <span class="dvx-ms-option-text">
                                            ${esc(o.label)}
                                        </span>
                                    </label>
                                `).join("")
                                : `<div class="dvx-ms-empty">
                                       Sin opciones disponibles
                                   </div>`
                        }
                    </div>
                </div>
            </div>
        `;

        const toggle = host.querySelector(".dvx-ms-toggle");
        const menu = host.querySelector(".dvx-ms-menu");
        const search = host.querySelector(".dvx-ms-search");
        const allCheck = host.querySelector(".dvx-ms-check-all");

        const getChecks = () =>
            Array.from(host.querySelectorAll(".dvx-ms-check"));

        const refreshVisualState = () => {
            const checks = getChecks();

            selectedValues = checks
                .filter(x => x.checked)
                .map(x => x.value);

            const label = host.querySelector(".dvx-ms-label");

            if (label) {
                const selectedOptions = safeOptions.filter(x =>
                    selectedValues.includes(x.value)
                );

                if (!selectedOptions.length) {
                    label.textContent = placeholder;
                }
                else if (selectedOptions.length <= 2) {
                    label.textContent = selectedOptions
                        .map(x => x.label)
                        .join(", ");
                }
                else {
                    label.textContent =
                        `${selectedOptions.length} seleccionados`;
                }

                label.title = selectedOptions
                    .map(x => x.label)
                    .join(", ");
            }

            if (allCheck) {
                allCheck.checked =
                    checks.length > 0 &&
                    checks.every(x => x.checked);

                allCheck.indeterminate =
                    checks.some(x => x.checked) &&
                    !checks.every(x => x.checked);
            }
        };

        const emitChange = () => {
            refreshVisualState();

            Promise
                .resolve(onChange([...selectedValues]))
                .catch(showError);
        };

        toggle?.addEventListener("click", e => {
            e.preventDefault();
            e.stopPropagation();

            const willOpen = !!menu?.hidden;

            closeAllMultiSelectMenus(host);

            if (!menu) return;

            menu.hidden = !willOpen;

            toggle.setAttribute(
                "aria-expanded",
                willOpen ? "true" : "false"
            );

            if (willOpen) {
                positionMultiMenu(toggle, menu);

                window.setTimeout(
                    () => search?.focus(),
                    0
                );
            }
        });

        search?.addEventListener("input", () => {
            const q = norm(search.value);

            host.querySelectorAll(".dvx-ms-option")
                .forEach(row => {
                    const text = row.dataset.search || "";

                    row.style.display =
                        !q || text.includes(q)
                            ? ""
                            : "none";
                });
        });

        search?.addEventListener(
            "click",
            e => e.stopPropagation()
        );

        search?.addEventListener(
            "keydown",
            e => e.stopPropagation()
        );

        allCheck?.addEventListener("change", () => {
            getChecks().forEach(check => {
                check.checked = allCheck.checked;
            });

            emitChange();
        });

        getChecks().forEach(check => {
            check.addEventListener(
                "change",
                emitChange
            );
        });

        menu?.addEventListener(
            "click",
            e => e.stopPropagation()
        );

        menu?.addEventListener(
            "wheel",
            e => e.stopPropagation(),
            { passive: true }
        );

        refreshVisualState();
    }

    function updateMultiSelectVisualGlobal(host) {
        if (!host) return;

        const checks = Array.from(
            host.querySelectorAll(".dvx-ms-check")
        );

        const selected = checks.filter(x => x.checked);

        const label = host.querySelector(".dvx-ms-label");
        const allCheck = host.querySelector(".dvx-ms-check-all");

        if (label) {
            const selectedLabels = selected.map(
                x => x.dataset.label || x.value
            );

            if (!selected.length) {
                label.textContent = "Todos";
                label.title = "";
            }
            else if (selected.length <= 2) {
                label.textContent = selectedLabels.join(", ");
                label.title = selectedLabels.join(", ");
            }
            else {
                label.textContent = `${selected.length} seleccionados`;
                label.title = selectedLabels.join(", ");
            }
        }

        if (allCheck) {
            allCheck.checked =
                checks.length > 0 &&
                checks.every(x => x.checked);

            allCheck.indeterminate =
                checks.some(x => x.checked) &&
                !checks.every(x => x.checked);
        }
    }

    function setMultiSelectSelection(id, values) {
        const host = $(id);
        if (!host) return;

        const selected = new Set(
            (values || []).map(x => String(x))
        );

        host.querySelectorAll(".dvx-ms-check")
            .forEach(check => {
                check.checked = selected.has(check.value);
            });

        updateMultiSelectVisualGlobal(host);
    }

    function syncMultiSelectGroup(ids, values, exceptId = "") {
        (ids || []).forEach(id => {
            if (id === exceptId) return;
            setMultiSelectSelection(id, values);
        });
    }

    function closeAllMultiSelectMenus(exceptHost = null) {
        root.querySelectorAll(".dvx-multi-host")
            .forEach(host => {
                if (exceptHost && host === exceptHost) return;

                const menu = host.querySelector(".dvx-ms-menu");
                const toggle = host.querySelector(".dvx-ms-toggle");

                if (menu) menu.hidden = true;

                toggle?.setAttribute(
                    "aria-expanded",
                    "false"
                );
            });
    }

    function positionMultiMenu(toggle, menu) {
        const rect = toggle.getBoundingClientRect();

        const width = Math.min(
            300,
            window.innerWidth - 16
        );

        menu.style.width = `${width}px`;

        let left = rect.right - width;

        left = Math.max(
            8,
            Math.min(
                left,
                window.innerWidth - width - 8
            )
        );

        const desiredHeight = Math.min(
            340,
            window.innerHeight - 16
        );

        let top = rect.bottom + 5;

        if (top + desiredHeight > window.innerHeight - 8) {
            top = Math.max(
                8,
                rect.top - desiredHeight - 5
            );
        }

        menu.style.left = `${left}px`;
        menu.style.top = `${top}px`;
    }

    function repositionOpenMultiSelectMenus() {
        root.querySelectorAll(".dvx-multi-host")
            .forEach(host => {
                const menu =
                    host.querySelector(".dvx-ms-menu");

                const toggle =
                    host.querySelector(".dvx-ms-toggle");

                if (
                    !menu ||
                    !toggle ||
                    menu.hidden
                ) {
                    return;
                }

                positionMultiMenu(
                    toggle,
                    menu
                );
            });
    }

    function csv(values) {
        return (Array.isArray(values) ? values : [])
            .join(",");
    }

    function norm(value) {
        return String(value ?? "")
            .trim()
            .toUpperCase();
    }

    function bindEvents() {
        $("monthSelect").addEventListener("change", async e => {
            state.month = +e.target.value;
            rebuildDays();
            await refreshAll();
        });

        $("daySelect").addEventListener("change", async e => {
            state.day = +e.target.value;
            await refreshAll();
        });

        root.querySelectorAll(".dvx-segment").forEach(btn => {
            btn.addEventListener("click", async () => {
                state.compareMode = btn.dataset.mode || "budget";

                root.querySelectorAll(".dvx-segment")
                    .forEach(b =>
                        b.classList.toggle(
                            "dvx-active",
                            b === btn
                        )
                    );

                await Promise.all([
                    refreshSummary(),
                    refreshMaster(),
                    refreshVendor()
                ]);
            });
        });

        $("resetFilters").addEventListener("click", async () => {
            resetAllFilters();
            await refreshAll();
        });

        $("clearCrossFilters")?.addEventListener("click", async () => {
            clearDimensionFilters();
            await refreshAll();
        });

        $("activeFilterChips")?.addEventListener("click", async e => {
            const button = e.target.closest("[data-remove-filter]");
            if (!button) return;

            const type = button.dataset.type;
            const value = button.dataset.value;

            if (type === "vendor") {
                state.masterVendor = state.masterVendor.filter(x => x !== value);
                setMultiSelectSelection(
                    "masterVendorFilter",
                    state.masterVendor
                );
            }

            if (type === "master") {
                state.vendorMaster = state.vendorMaster.filter(x => x !== value);
                state.priceMaster = [...state.vendorMaster];
                state.trendMaster = [...state.vendorMaster];

                syncMultiSelectGroup(
                    [
                        "vendorMasterFilter",
                        "priceMasterFilter",
                        "trendMasterFilter"
                    ],
                    state.vendorMaster
                );

                rebuildAllSkuFilters();
            }

            if (type === "sku") {
                state.vendorSku = state.vendorSku.filter(x => x !== value);
                state.priceSku = [...state.vendorSku];
                state.trendSku = [...state.vendorSku];

                syncMultiSelectGroup(
                    [
                        "vendorSkuFilter",
                        "priceSkuFilter",
                        "trendSkuFilter"
                    ],
                    state.vendorSku
                );
            }

            renderActiveFilters();
            await refreshAll();
        });

        // Cerrar solo al hacer clic fuera de los filtros.
        document.addEventListener(
            "click",
            e => {
                const dentroDeMulti =
                    e.target instanceof Element &&
                    e.target.closest(".dvx-multi-host");

                if (!dentroDeMulti) {
                    closeAllMultiSelectMenus();
                }
            }
        );

        // Scroll y resize no cierran el menú.
        window.addEventListener(
            "resize",
            () => repositionOpenMultiSelectMenus()
        );

        window.addEventListener(
            "scroll",
            () => repositionOpenMultiSelectMenus(),
            true
        );
    }

    function resetAllFilters() {
        const years = Array
            .from(new Set(catalogs.anios || []))
            .sort((a, b) => b - a);

        const ultima = parseDateOnly(
            catalogs.ultimaFechaVenta
        );

        if (ultima) {
            state.year = ultima.year;
            state.month = ultima.month;
            state.day = ultima.day;
        }
        else {
            state.year = years.includes(today.getFullYear())
                ? today.getFullYear()
                : (years[0] || today.getFullYear());

            state.month = today.getMonth() + 1;
            state.day = today.getDate();
        }

        state.compareMode = "budget";

        state.masterVendor = [];
        state.vendorMaster = [];
        state.vendorSku = [];
        state.priceMaster = [];
        state.priceSku = [];
        state.trendMaster = [];
        state.trendSku = [];

        buildCatalogs();
        renderActiveFilters();

        root.querySelectorAll(".dvx-segment")
            .forEach(b =>
                b.classList.toggle(
                    "dvx-active",
                    b.dataset.mode === "budget"
                )
            );
    }

    function clearDimensionFilters() {
        state.masterVendor = [];
        state.vendorMaster = [];
        state.vendorSku = [];
        state.priceMaster = [];
        state.priceSku = [];
        state.trendMaster = [];
        state.trendSku = [];

        setMultiSelectSelection("masterVendorFilter", []);

        syncMultiSelectGroup(
            [
                "vendorMasterFilter",
                "priceMasterFilter",
                "trendMasterFilter"
            ],
            []
        );

        rebuildAllSkuFilters();
        renderActiveFilters();
    }

    function scheduleRefreshAll(delay = 140) {
        if (refreshTimer) {
            clearTimeout(refreshTimer);
        }

        refreshTimer = window.setTimeout(
            () => refreshAll(),
            delay
        );
    }

    async function refreshAll() {
        const token = ++refreshToken;

        hideError();
        setLoading(true);

        try {
            const params = baseParams();

            const [summary, masters, vendors, prices, trend] =
                await Promise.all([
                    getJson(url(cfg.resumenUrl, params)),
                    getJson(url(cfg.masterUrl, params)),
                    getJson(url(cfg.vendedorUrl, params)),
                    getJson(url(cfg.preciosUrl, params)),
                    getJson(url(cfg.tendenciaUrl, params))
                ]);

            if (token !== refreshToken) return;

            renderKPIs(summary);
            renderMasterChart(masters);
            renderVendorChart(vendors);
            renderPriceChart(prices);
            renderTrendChart(trend);
            renderActiveFilters();
        }
        catch (err) {
            if (token === refreshToken) {
                showError(err);
            }
        }
        finally {
            if (token === refreshToken) {
                setLoading(false);
            }
        }
    }

    async function refreshSummary() {
        try {
            renderKPIs(
                await getJson(
                    url(cfg.resumenUrl, baseParams())
                )
            );
        }
        catch (e) {
            showError(e);
        }
    }

    async function refreshMaster() {
        try {
            renderMasterChart(
                await getJson(
                    url(cfg.masterUrl, baseParams())
                )
            );
        }
        catch (e) {
            showError(e);
        }
    }

    async function refreshVendor() {
        try {
            renderVendorChart(
                await getJson(
                    url(cfg.vendedorUrl, baseParams())
                )
            );
        }
        catch (e) {
            showError(e);
        }
    }

    async function refreshPrices() {
        try {
            renderPriceChart(
                await getJson(
                    url(cfg.preciosUrl, baseParams())
                )
            );
        }
        catch (e) {
            showError(e);
        }
    }

    async function refreshTrend() {
        try {
            renderTrendChart(
                await getJson(
                    url(cfg.tendenciaUrl, baseParams())
                )
            );
        }
        catch (e) {
            showError(e);
        }
    }

    function dateParams(extra = {}) {
        return {
            anio: state.year,
            mes: state.month,
            dia: state.day,
            ...extra
        };
    }

    function baseParams() {
        return {
            ...dateParams(),

            compararContra:
                state.compareMode === "reach"
                    ? "alcance"
                    : "presupuesto",

            master:
                csv(state.vendorMaster),

            sku:
                csv(state.vendorSku),

            vendedorId:
                csv(state.masterVendor)
        };
    }

    function selectCrossValue(type, value, event) {
        value = String(value ?? "").trim();
        if (!value) return;

        const additive = !!(
            event?.ctrlKey ||
            event?.metaKey
        );

        let current = [];

        if (type === "vendor")
            current = [...state.masterVendor];

        if (type === "master")
            current = [...state.vendorMaster];

        if (type === "sku")
            current = [...state.vendorSku];

        let next;

        if (additive) {
            const set = new Set(current);

            if (set.has(value))
                set.delete(value);
            else
                set.add(value);

            next = Array.from(set);
        }
        else {
            next =
                current.length === 1 &&
                current[0] === value
                    ? []
                    : [value];
        }

        if (type === "vendor") {
            state.masterVendor = next;

            setMultiSelectSelection(
                "masterVendorFilter",
                state.masterVendor
            );
        }

        if (type === "master") {
            state.vendorMaster = next;
            state.priceMaster = [...next];
            state.trendMaster = [...next];

            syncMultiSelectGroup(
                [
                    "vendorMasterFilter",
                    "priceMasterFilter",
                    "trendMasterFilter"
                ],
                next
            );

            rebuildAllSkuFilters();
        }

        if (type === "sku") {
            state.vendorSku = next;
            state.priceSku = [...next];
            state.trendSku = [...next];

            syncMultiSelectGroup(
                [
                    "vendorSkuFilter",
                    "priceSkuFilter",
                    "trendSkuFilter"
                ],
                next
            );
        }

        renderActiveFilters();
        scheduleRefreshAll(0);
    }

    function selectCrossDate(value) {
        const p = parseDateOnly(value);
        if (!p) return;

        state.year = p.year;
        state.month = p.month;
        state.day = p.day;

        fillSelect(
            $("monthSelect"),
            months.map((m, i) => ({
                value: i + 1,
                label: `${m} ${state.year}`
            })),
            state.month
        );

        rebuildDays();
        scheduleRefreshAll(0);
    }

    function vendorFilterKey(item) {
        const vendedorId = Number(item?.vendedorId || 0);

        if (vendedorId > 0) {
            return `VENDEDOR|${vendedorId}`;
        }

        const label = norm(item?.vendedor);

        if (label.startsWith("CEDIS")) {
            return `CEDIS|${label}`;
        }

        return "";
    }

    function vendorLabelFromKey(key) {
        const found = (catalogs.vendedores || [])
            .find(x => String(x.id) === String(key));

        if (found) {
            return found.nombre;
        }

        if (
            String(key)
                .toUpperCase()
                .startsWith("CEDIS|")
        ) {
            return String(key).substring("CEDIS|".length);
        }

        return String(key)
            .replace(/^VENDEDOR\|/i, "Vendedor ");
    }

    function renderActiveFilters() {
        const bar = $("activeFiltersBar");
        const host = $("activeFilterChips");

        if (!bar || !host) return;

        const chips = [];

        state.masterVendor.forEach(value => {
            chips.push({
                type: "vendor",
                value,
                label: vendorLabelFromKey(value)
            });
        });

        state.vendorMaster.forEach(value => {
            chips.push({
                type: "master",
                value,
                label: `Máster: ${value}`
            });
        });

        state.vendorSku.forEach(value => {
            chips.push({
                type: "sku",
                value,
                label: `SKU: ${value}`
            });
        });

        bar.hidden = chips.length === 0;

        host.innerHTML = chips
            .map(x => `
                <button type="button"
                        class="dvx-filter-chip"
                        data-remove-filter="1"
                        data-type="${esc(x.type)}"
                        data-value="${esc(x.value)}"
                        title="Quitar filtro">
                    <span>${esc(x.label)}</span>
                    <span class="dvx-filter-chip-x">×</span>
                </button>
            `)
            .join("");
    }

    function renderKPIs(d) {
        $("kpiWorkday").innerHTML = `${nf.format(d.diaLaboral || 0)} <small>de ${nf.format(d.diasLaborablesMes || 0)}</small>`;
        $("kpiSales").textContent = nf.format(d.ventaReal || 0);
        $("kpiBudget").textContent = nf.format(d.presupuestoMensual || 0);
        $("kpiReach").textContent = nf.format(d.alcance || 0);
        $("kpiCompliance").textContent = `${nf.format(d.cumplimientoPct || 0)}%`;
        $("kpiCompliance").className = (d.cumplimientoPct || 0) >= 100 ? "dvx-positive" : "";

        $("kpiComplianceLabel").innerHTML =
            `% Cumplimiento<br /><small>vs ${state.compareMode === "reach" ? "Alcance" : "Presupuesto"}</small>`;

        const gapKg = +(d.brechaAlcanceKg || 0);
        const gapPct = +(d.brechaAlcancePct || 0);
        $("kpiGap").textContent = `${gapPct > 0 ? "+" : ""}${nf.format(gapPct)}%`;
        $("kpiGapKg").textContent = `(${gapKg > 0 ? "+" : ""}${nf.format(gapKg)} KGS)`;
        $("kpiGap").className = gapKg >= 0 ? "dvx-positive" : "dvx-negative";
        $("kpiGapKg").className = `dvx-kpi-delta ${gapKg >= 0 ? "dvx-positive" : "dvx-negative"}`;

        $("lastUpdate").textContent = d.ultimaFechaVenta
            ? formatDate(d.ultimaFechaVenta)
            : "Sin ventas en el período";
    }

    function renderMasterChart(items) {
        const container = $("masterChart");

        if (!items || !items.length) {
            return empty(
                container,
                "Sin información para los filtros seleccionados."
            );
        }

        const list = items.slice(0, 14);
        const advances = list.map(x => +x.avancePct || 0);
        const count = list.length;

        const selectedSet = new Set(
            state.vendorMaster.map(norm)
        );

        container.innerHTML = `
            <svg class="dvx-master-svg"
                 viewBox="0 0 700 220"
                 preserveAspectRatio="none"
                 aria-hidden="true">
                <polyline id="masterPolyline"
                          fill="none"
                          stroke="#082e73"
                          stroke-width="3"
                          points="" />
            </svg>

            <div class="dvx-master-row"
                 style="grid-template-columns:repeat(${count},1fr)">
                ${list.map(x => {
                    const share = +x.participacionPct || 0;
                    const pct = +x.avancePct || 0;

                    const colorClass =
                        pct >= 100
                            ? "dvx-positive"
                            : pct < 80
                                ? "dvx-negative"
                                : "";

                    const key = String(
                        x.master || "SIN_MASTER"
                    );

                    const selected =
                        selectedSet.has(norm(key));

                    return `
                        <div class="dvx-master-item dvx-clickable ${
                                selected
                                    ? "dvx-cross-selected"
                                    : ""
                             }"
                             data-cross-master="${esc(key)}"
                             title="Clic para filtrar todo el dashboard. Clic otra vez para quitar el filtro.">

                            <div class="dvx-master-bar"
                                 style="height:${
                                     Math.max(
                                         8,
                                         Math.min(
                                             92,
                                             share * 2.25
                                         )
                                     )
                                 }%"
                                 data-tip="<strong>${esc(key)}</strong><br>Participación: ${nf2.format(share)}%<br>Venta real: ${nf.format(x.ventaReal)} KGS<br>Referencia: ${nf.format(x.referencia)} KGS<br>Avance: ${nf2.format(pct)}%">
                                <span class="dvx-master-bar-value">
                                    ${nf.format(share)}%
                                </span>
                            </div>

                            <span class="dvx-master-point"
                                  style="bottom:${
                                      Math.min(
                                          95,
                                          pct / 1.8
                                      )
                                  }%">
                            </span>

                            <span class="dvx-master-percent ${colorClass}"
                                  style="bottom:calc(${
                                      Math.min(
                                          95,
                                          pct / 1.8
                                      )
                                  }% + 10px)">
                                ${nf.format(pct)}%
                            </span>

                            <span class="dvx-master-x"
                                  title="${esc(key)}">
                                ${esc(key)}
                            </span>
                        </div>`;
                }).join("")}
            </div>`;

        container.onclick = e => {
            const hit = e.target.closest(
                "[data-cross-master]"
            );

            if (!hit) return;

            selectCrossValue(
                "master",
                hit.dataset.crossMaster,
                e
            );
        };

        requestAnimationFrame(() => {
            const poly =
                container.querySelector(
                    "#masterPolyline"
                );

            const points = advances
                .map((pct, i) => {
                    const x =
                        ((i + .5) / advances.length) * 700;

                    const y =
                        220 -
                        Math.min(
                            210,
                            pct / 180 * 205
                        );

                    return `${x},${y}`;
                })
                .join(" ");

            if (poly) {
                poly.setAttribute(
                    "points",
                    points
                );
            }

            bindTooltips(container);
        });
    }

    function renderVendorChart(items) {
        const container = $("vendorChart");

        if (!items || !items.length) {
            return empty(
                container,
                "Sin información para los filtros seleccionados."
            );
        }

        const list = items.slice(0, 12);

        const maxRef =
            Math.max(
                1,
                ...list.map(x =>
                    Math.max(
                        +x.referencia || 0,
                        +x.ventaReal || 0
                    )
                )
            ) * 1.12;

        const selectedSet = new Set(
            state.masterVendor
        );

        container.innerHTML = `
            <div class="dvx-vendor-table">
                <div></div>
                <div></div>

                <div class="dvx-vendor-header">
                    Referencia<br>(KGS)
                </div>

                <div class="dvx-vendor-header dvx-sales-col">
                    Ventas<br>(KGS)
                </div>

                ${list.map(x => {
                    const ref = +x.referencia || 0;
                    const sales = +x.ventaReal || 0;
                    const pct = +x.cumplimientoPct || 0;

                    const refWidth =
                        ref / maxRef * 100;

                    const salesWidth =
                        sales / maxRef * 100;

                    const pctClass =
                        pct >= 100
                            ? "dvx-positive"
                            : "dvx-negative";

                    const skuLabel =
                        state.vendorSku.length
                            ? state.vendorSku.join(", ")
                            : "Todos";

                    const key =
                        vendorFilterKey(x);

                    const selected =
                        key &&
                        selectedSet.has(key);

                    const selectedClass =
                        selected
                            ? "dvx-cross-selected"
                            : "";

                    const attr =
                        key
                            ? `data-cross-vendor="${esc(key)}"`
                            : "";

                    const title =
                        key
                            ? `title="Clic para filtrar todo el dashboard. Clic otra vez para quitar el filtro."`
                            : "";

                    return `
                        <div class="dvx-vendor-name dvx-clickable ${selectedClass}"
                             ${attr}
                             ${title}>
                            ${esc(x.vendedor)}
                        </div>

                        <div class="dvx-vendor-track dvx-clickable ${selectedClass}"
                             ${attr}
                             ${title}
                             style="width:${Math.max(12, refWidth)}%"
                             data-tip="<strong>${esc(x.vendedor)}</strong><br>SKU: ${esc(skuLabel)}<br>Referencia: ${nf.format(ref)} KGS<br>Ventas: ${nf.format(sales)} KGS<br>Cumplimiento: ${nf2.format(pct)}%">

                            <div class="dvx-vendor-fill"
                                 style="width:${
                                     salesWidth /
                                     Math.max(refWidth, 1) *
                                     100
                                 }%">
                            </div>

                            <span class="dvx-vendor-pct ${pctClass}"
                                  style="left:${
                                      Math.min(
                                          125,
                                          salesWidth /
                                          Math.max(refWidth, 1) *
                                          100
                                      )
                                  }%">
                                ${nf.format(pct)}%
                            </span>
                        </div>

                        <div class="dvx-vendor-num dvx-clickable ${selectedClass}"
                             ${attr}
                             ${title}>
                            ${nf.format(ref)}
                        </div>

                        <div class="dvx-vendor-num dvx-sales-col dvx-clickable ${selectedClass}"
                             ${attr}
                             ${title}>
                            ${nf.format(sales)}
                        </div>`;
                }).join("")}
            </div>`;

        container.onclick = e => {
            const hit = e.target.closest(
                "[data-cross-vendor]"
            );

            if (!hit) return;

            selectCrossValue(
                "vendor",
                hit.dataset.crossVendor,
                e
            );
        };

        bindTooltips(container);
    }

    function renderPriceChart(data) {
        const container = $("priceChart");

        const items =
            data &&
            Array.isArray(data.items)
                ? data.items
                : [];

        const avg =
            +(data?.precioPromedioPonderado || 0);

        $("weightedAverage").textContent =
            avg > 0
                ? `Precio Promedio Ponderado ($${money.format(avg)} /KG)`
                : "Precio Promedio Ponderado (-)";

        if ($("priceFootnote")) {
            $("priceFootnote").textContent =
                "* Precio ponderado calculado con base en el volumen vendido.";

            if (data?.nota) {
                $("priceFootnote").title = data.nota;
            }
        }

        if (!items.length) {
            return empty(
                container,
                "Sin información de precios para los filtros seleccionados."
            );
        }

        const list = items.slice(0, 12);

        const values = list
            .map(x => +x.precioPonderado || 0)
            .filter(x => x > 0);

        if (!values.length) {
            return empty(
                container,
                "Sin precios válidos para los filtros seleccionados."
            );
        }

        let minY =
            Math.floor(
                Math.min(
                    ...values,
                    avg || Infinity
                ) / 10
            ) * 10 - 10;

        let maxY =
            Math.ceil(
                Math.max(...values, avg) / 10
            ) * 10 + 10;

        minY = Math.max(0, minY);

        if (maxY <= minY) {
            maxY = minY + 20;
        }

        const range = maxY - minY;

        const avgPosition =
            ((avg - minY) / range) * 100;

        const selectedSet =
            new Set(state.masterVendor);

        container.innerHTML = `
            <div class="dvx-avg-line"
                 style="bottom:${
                     30 +
                     Math.max(
                         0,
                         Math.min(
                             100,
                             avgPosition
                         )
                     ) * 2.04
                 }px">
            </div>

            <div class="dvx-price-row"
                 style="grid-template-columns:repeat(${list.length},1fr)">
                ${list.map(x => {
                    const value =
                        +x.precioPonderado || 0;

                    const h =
                        Math.max(
                            7,
                            ((value - minY) / range) * 100
                        );

                    const cls =
                        value >= avg
                            ? "dvx-good"
                            : "dvx-bad";

                    const key =
                        vendorFilterKey(x);

                    const selected =
                        key &&
                        selectedSet.has(key);

                    return `
                        <div class="dvx-price-item dvx-clickable ${
                                selected
                                    ? "dvx-cross-selected"
                                    : ""
                             }"
                             ${
                                 key
                                     ? `data-cross-vendor="${esc(key)}"`
                                     : ""
                             }
                             ${
                                 key
                                     ? `title="Clic para filtrar todo el dashboard. Clic otra vez para quitar el filtro."`
                                     : ""
                             }>

                            <div class="dvx-price-bar ${cls}"
                                 style="height:${
                                     Math.max(
                                         7,
                                         Math.min(
                                             100,
                                             h
                                         )
                                     )
                                 }%"
                                 data-tip="<strong>${esc(x.vendedor)}</strong><br>Volumen: ${nf.format(x.kilos || 0)} KGS<br>Precio ponderado: $${money.format(value)} /KG<br>Promedio general: $${money.format(avg)} /KG">

                                <span class="dvx-price-value">
                                    $${money.format(value)}
                                </span>
                            </div>

                            <span class="dvx-price-x"
                                  title="${esc(x.vendedor)}">
                                ${esc(x.vendedor)}
                            </span>
                        </div>`;
                }).join("")}
            </div>`;

        container.onclick = e => {
            const hit = e.target.closest(
                "[data-cross-vendor]"
            );

            if (!hit) return;

            selectCrossValue(
                "vendor",
                hit.dataset.crossVendor,
                e
            );
        };

        bindTooltips(container);
    }

    function renderTrendChart(data) {
        const container = $("trendChart");
        const items = data && Array.isArray(data.items) ? data.items : [];
        if (!items.length) return empty(container, "Sin información de tendencia para los filtros seleccionados.");

        const W = 900, H = 304;
        const pad = { l: 55, r: 62, t: 28, b: 40 };
        const chartW = W - pad.l - pad.r;
        const chartH = H - pad.t - pad.b;
        const count = Math.max(1, items.length);

        const reach = items.map(x => +x.alcanceAcumulado || 0);
        const actual = items.map(x => x.ventaAcumulada == null ? null : +x.ventaAcumulada);
        const gap = items.map(x => x.brecha == null ? null : +x.brecha);

        const maxLeftValue = Math.max(
            +(data.presupuestoMensual || 0),
            ...reach,
            ...actual.filter(v => v != null),
            1
        );
        const leftMax = Math.max(5000, roundUp(maxLeftValue * 1.08, 5000));
        const gapPeak = Math.max(5000, ...gap.filter(v => v != null).map(v => Math.abs(v)));
        const rightLimit = Math.max(10000, roundUp(gapPeak * 1.18, 5000));

        const x = i => count === 1 ? pad.l + chartW / 2 : pad.l + i * (chartW / (count - 1));
        const yLeft = v => pad.t + chartH - (v / leftMax) * chartH;
        const yRight = v => pad.t + chartH / 2 - (v / rightLimit) * (chartH / 2);

        const leftStep = leftMax <= 35000 ? 5000 : leftMax / 7;
        const leftTicks = [];
        for (let v = 0; v <= leftMax + .01; v += leftStep) leftTicks.push(v);

        const gridLines = leftTicks.map(v => {
            const yy = yLeft(v);
            return `
                <line x1="${pad.l}" x2="${W - pad.r}" y1="${yy}" y2="${yy}" stroke="#e7ebf1" stroke-width="1" />
                <text x="${pad.l - 9}" y="${yy + 3.5}" text-anchor="end" class="dvx-trend-axis-label">${nf.format(v)}</text>`;
        }).join("");

        const rightTicks = [-rightLimit, -rightLimit / 2, 0, rightLimit / 2, rightLimit];
        const rightLabels = rightTicks.map(v => {
            const yy = yRight(v);
            const cls = v > 0 ? "dvx-positive-axis" : v < 0 ? "dvx-negative-axis" : "dvx-neutral-axis";
            return `<text x="${W - pad.r + 12}" y="${yy + 3.5}" text-anchor="start" class="dvx-trend-axis-label ${cls}">${nf.format(v)}</text>`;
        }).join("");

        const reachPts = reach.map((v, i) => `${x(i)},${yLeft(v)}`).join(" ");
        const validGapPoints = gap.map((v, i) => v == null ? null : `${x(i)},${yRight(v)}`).filter(Boolean).join(" ");
        const xLabels = items.map((r, i) =>
            `<text x="${x(i)}"
                   y="${H - 17}"
                   text-anchor="middle"
                   class="dvx-trend-axis-label dvx-trend-day-label"
                   data-cross-date="${esc(r.fecha)}">${r.diaLaboral}</text>`
        ).join("");

        const actualBars = actual.map((v, i) => {
            if (v == null) return "";
            const barW = Math.max(6, chartW / Math.max(25, count) * .56);
            const yy = yLeft(v);
            return `
                <rect x="${x(i) - barW / 2}" y="${yy}" width="${barW}" height="${pad.t + chartH - yy}" rx="1.2" fill="#082e73"
                      data-cross-date="${esc(items[i].fecha)}"
                      data-tip="<strong>Día ${items[i].diaLaboral}</strong><br>Ventas Reales Acumuladas: ${nf.format(v)} KGS<br>Alcance: ${nf.format(reach[i])} KGS<br>Brecha: ${nf.format(gap[i] || 0)} KGS" />
                <text x="${x(i)}" y="${Math.max(pad.t + 8, yy - 6)}" text-anchor="middle" class="dvx-trend-value dvx-actual-value"
                      data-cross-date="${esc(items[i].fecha)}">${nf.format(v)}</text>`;
        }).join("");

        const reachDots = reach.map((v, i) => `
            <circle cx="${x(i)}" cy="${yLeft(v)}" r="3.25" fill="#e99a00"
                    data-cross-date="${esc(items[i].fecha)}"
                    data-tip="<strong>Día ${items[i].diaLaboral}</strong><br>Alcance (Ventas Esperadas): ${nf.format(v)} KGS" />
            <text x="${x(i)}" y="${Math.max(pad.t + 8, yLeft(v) - 8)}" text-anchor="middle" class="dvx-trend-value dvx-reach-value"
                  data-cross-date="${esc(items[i].fecha)}">${nf.format(v)}</text>`
        ).join("");

        const gapDots = gap.map((v, i) => {
            if (v == null) return "";
            return `
                <circle cx="${x(i)}" cy="${yRight(v)}" r="2.7" fill="#fff" stroke="#7a818e" stroke-width="1.7"
                        data-cross-date="${esc(items[i].fecha)}"
                        data-tip="<strong>Día ${items[i].diaLaboral}</strong><br>Brecha: ${nf.format(v)} KGS" />
                <text x="${x(i)}" y="${Math.min(pad.t + chartH - 4, yRight(v) + 15)}" text-anchor="middle" class="dvx-trend-value ${v < 0 ? "dvx-gap-negative" : "dvx-gap-positive"}"
                      data-cross-date="${esc(items[i].fecha)}">${v > 0 ? "+" : ""}${nf.format(v)}</text>`;
        }).join("");

        const selectedIndex = Math.max(0, actual.map(v => v != null).lastIndexOf(true));
        const actualIndexes = actual.map((v, i) => v != null ? i : -1).filter(i => i >= 0);
        const markerIndex = actualIndexes.length ? actualIndexes[actualIndexes.length - 1] : 0;
        const markerX = x(markerIndex);

        container.innerHTML = `
            <svg class="dvx-trend-svg" viewBox="0 0 ${W} ${H}" preserveAspectRatio="none" aria-label="Tendencia acumulada de ventas reales contra alcance">
                ${gridLines}
                <text x="${pad.l - 42}" y="${pad.t - 11}" class="dvx-trend-axis-title">KGS</text>
                <text x="${W - pad.r + 4}" y="${pad.t - 11}" class="dvx-trend-axis-title dvx-right-title">Brecha (KGS)</text>
                <line x1="${pad.l}" x2="${W - pad.r}" y1="${pad.t + chartH}" y2="${pad.t + chartH}" stroke="#c7cfda" stroke-width="1" />
                <line x1="${markerX}" x2="${markerX}" y1="${pad.t}" y2="${pad.t + chartH}" stroke="#9bc1ff" stroke-width="2" stroke-dasharray="5 5" opacity=".9" />
                ${actualBars}
                <polyline points="${reachPts}" fill="none" stroke="#e99a00" stroke-width="2.7" stroke-linejoin="round" stroke-linecap="round" />
                ${reachDots}
                ${validGapPoints ? `<polyline points="${validGapPoints}" fill="none" stroke="#7a818e" stroke-width="2" stroke-linejoin="round" stroke-linecap="round" />` : ""}
                ${gapDots}
                ${rightLabels}
                ${xLabels}
                <text x="${W / 2}" y="${H - 2}" text-anchor="middle" class="dvx-trend-axis-title dvx-x-title">Días laborales del mes</text>
            </svg>`;

        container.onclick = e => {
            const hit = e.target.closest(
                "[data-cross-date]"
            );

            if (!hit) return;

            selectCrossDate(
                hit.dataset.crossDate
            );
        };

        bindTooltips(container);
    }

    function bindTooltips(scope) {
        const tip = $("tooltip");
        if (!tip || !scope) return;
        scope.querySelectorAll("[data-tip]").forEach(el => {
            el.addEventListener("mouseenter", e => {
                tip.innerHTML = el.dataset.tip || "";
                tip.classList.add("dvx-show");
                moveTooltip(e);
            });
            el.addEventListener("mousemove", moveTooltip);
            el.addEventListener("mouseleave", () => tip.classList.remove("dvx-show"));
        });
    }

    function moveTooltip(e) {
        const tip = $("tooltip");
        if (!tip) return;
        const pad = 14;
        let left = e.clientX + pad;
        let top = e.clientY + pad;
        const rect = tip.getBoundingClientRect();
        if (left + rect.width > window.innerWidth - 8) left = e.clientX - rect.width - pad;
        if (top + rect.height > window.innerHeight - 8) top = e.clientY - rect.height - pad;
        tip.style.left = `${left}px`;
        tip.style.top = `${top}px`;
    }

    async function getJson(endpoint) {
        if (!endpoint) throw new Error("No se configuró uno de los endpoints del dashboard.");
        const response = await fetch(endpoint, {
            headers: { "X-Requested-With": "XMLHttpRequest" },
            credentials: "same-origin"
        });
        const text = await response.text();
        let body = null;
        try { body = text ? JSON.parse(text) : null; } catch { body = null; }
        if (!response.ok) {
            const msg = body?.error || body?.detalle || body?.message || text || `HTTP ${response.status}`;
            throw new Error(msg);
        }
        return body;
    }

    function url(base, params) {
        const u = new URL(base, window.location.origin);
        Object.entries(params || {}).forEach(([k, v]) => {
            if (v !== undefined && v !== null && String(v) !== "") u.searchParams.set(k, v);
        });
        return u.toString();
    }

    function empty(el, message) {
        if (!el) return;
        el.innerHTML = `<div style="height:100%;min-height:250px;display:flex;align-items:center;justify-content:center;color:#6d7890;font-size:11px;font-weight:700;text-align:center;padding:20px;">${esc(message)}</div>`;
    }

    function roundUp(value, step) {
        return Math.ceil(value / step) * step;
    }

    function parseDateOnly(value) {
        if (!value) return null;
        const m = String(value).match(/^(\d{4})-(\d{2})-(\d{2})/);
        if (!m) return null;
        const year = Number(m[1]);
        const month = Number(m[2]);
        const day = Number(m[3]);
        if (!year || month < 1 || month > 12 || day < 1 || day > 31) return null;
        return { year, month, day };
    }

    function formatDate(value) {
        const p = parseDateOnly(value);
        if (p) {
            return `${String(p.day).padStart(2, "0")}/${String(p.month).padStart(2, "0")}/${p.year}`;
        }
        const d = new Date(value);
        if (Number.isNaN(d.getTime())) return String(value || "");
        return new Intl.DateTimeFormat("es-MX", {
            day: "2-digit", month: "2-digit", year: "numeric"
        }).format(d);
    }

    function esc(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#39;");
    }

    function setLoading(value) {
        root.classList.toggle("dvx-loading", !!value);
    }

    function showError(err) {
        const el = $("dashboardError");
        if (!el) return;
        el.hidden = false;
        el.textContent = `No se pudo cargar el dashboard: ${err?.message || err || "Error desconocido"}`;
    }

    function hideError() {
        const el = $("dashboardError");
        if (!el) return;
        el.hidden = true;
        el.textContent = "";
    }
})();