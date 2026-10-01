/* BASCULA CAMIONERA — JavaScript original externalizado sin cambios funcionales. */

(function () {
    'use strict';

    function prepararContenedorBascula() {
        const root = document.getElementById('basculaApp');
        if (!root) return;

        let parent = root.parentElement;

        while (parent && parent !== document.body) {
            if (
                parent.matches('.container, .container-fluid, .pb-3') ||
                parent.matches('main[role="main"]')
            ) {
                parent.classList.add(
                    'bascula-sidebar-host',
                    'bascula-sidebar-host-clean'
                );
            }

            parent = parent.parentElement;
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener(
            'DOMContentLoaded',
            prepararContenedorBascula,
            { once: true }
        );
    } else {
        prepararContenedorBascula();
    }
})();

/* ===== SCRIPT ORIGINAL 2 ===== */

(function () {
    'use strict';

    const ROOT_SELECTOR = '#basculaApp';
    const SIDEBAR_SELECTORS = [
        '#sidebar',
        '.sidebar',
        '.main-sidebar',
        '.app-sidebar',
        '.sidebar-wrapper',
        '.left-sidebar',
        'aside',
        '[id*="sidebar" i]',
        '[class*="sidebar" i]',
        '[data-sidebar]'
    ].join(',');

    let root = null;
    let parent = null;
    let sidebar = null;

    let intervalId = 0;
    let finalTimerId = 0;
    let lastWidth = -1;
    let lastLeft = -1;

    function esSidebarValido(element) {
        if (!element || !document.documentElement.contains(element)) {
            return false;
        }

        /*
         * Excluir paneles laterales internos de topología y cualquier
         * elemento perteneciente al propio módulo.
         */
        if (root && (element === root || root.contains(element))) {
            return false;
        }

        const style = window.getComputedStyle(element);
        const rect = element.getBoundingClientRect();
        const viewportWidth = document.documentElement.clientWidth;
        const viewportHeight = window.innerHeight;

        return (
            style.display !== 'none' &&
            style.visibility !== 'hidden' &&
            Number.parseFloat(style.opacity || '1') > 0.05 &&
            rect.left <= 10 &&
            rect.width >= 150 &&
            rect.width <= Math.min(470, viewportWidth * 0.60) &&
            rect.height >= Math.max(400, viewportHeight * 0.52)
        );
    }

    function buscarSidebar() {
        let mejor = null;
        let mejorPuntaje = -1;

        document.querySelectorAll(SIDEBAR_SELECTORS).forEach(function (element) {
            if (!esSidebarValido(element)) return;

            const rect = element.getBoundingClientRect();
            const puntaje = rect.height + (rect.width * 2);

            if (puntaje > mejorPuntaje) {
                mejor = element;
                mejorPuntaje = puntaje;
            }
        });

        if (mejor) return mejor;

        /*
         * Respaldo geométrico para layouts cuyo menú no usa una clase
         * o identificador relacionado con sidebar.
         */
        const puntosY = [
            140,
            Math.round(window.innerHeight * 0.40),
            Math.round(window.innerHeight * 0.72)
        ];

        puntosY.some(function (y) {
            return [5, 24, 70].some(function (x) {
                const elementos = document.elementsFromPoint(x, y);

                for (const element of elementos) {
                    let actual = element;

                    while (actual && actual !== document.body) {
                        if (esSidebarValido(actual)) {
                            mejor = actual;
                            return true;
                        }

                        actual = actual.parentElement;
                    }
                }

                return false;
            });
        });

        return mejor;
    }

    function bordeDerechoSidebar() {
        if (!sidebar || !esSidebarValido(sidebar)) {
            sidebar = buscarSidebar();
        }

        if (!sidebar) return 0;

        return Math.max(
            0,
            Math.round(sidebar.getBoundingClientRect().right)
        );
    }

    function aplicarAjuste() {
        root ??= document.querySelector(ROOT_SELECTOR);
        if (!root) return;

        parent = root.parentElement;
        if (!parent) return;

        const parentRect = parent.getBoundingClientRect();
        const viewportRight = document.documentElement.clientWidth;
        const sidebarRight = bordeDerechoSidebar();

        const containerRight = Math.min(
            viewportRight,
            parentRect.right
        );

        /*
         * Si _Layout2 ya desplazó el contenido se respeta esa posición.
         * Si el menú se superpone, el módulo comienza en su borde derecho.
         */
        const targetLeft = Math.max(
            parentRect.left,
            sidebarRight
        );

        const leftOffset = Math.max(
            0,
            Math.round(targetLeft - parentRect.left)
        );

        const availableWidth = Math.max(
            320,
            Math.floor(containerRight - targetLeft)
        );

        if (
            Math.abs(availableWidth - lastWidth) < 2 &&
            Math.abs(leftOffset - lastLeft) < 2
        ) {
            return;
        }

        lastWidth = availableWidth;
        lastLeft = leftOffset;

        root.style.setProperty('position', 'relative', 'important');
        root.style.setProperty('left', leftOffset + 'px', 'important');
        root.style.setProperty('width', availableWidth + 'px', 'important');
        root.style.setProperty('max-width', availableWidth + 'px', 'important');
        root.style.setProperty('margin-left', '0', 'important');
        root.style.setProperty('margin-right', '0', 'important');
    }

    function detenerSecuencia() {
        if (intervalId) {
            window.clearInterval(intervalId);
            intervalId = 0;
        }

        if (finalTimerId) {
            window.clearTimeout(finalTimerId);
            finalTimerId = 0;
        }
    }

    function ajustarDuranteTransicion() {
        detenerSecuencia();
        aplicarAjuste();

        /*
         * Frecuencia moderada para mantener fluida la animación sin
         * recalcular continuamente tablas, Select2 y topología.
         */
        intervalId = window.setInterval(aplicarAjuste, 70);

        finalTimerId = window.setTimeout(function () {
            detenerSecuencia();
            aplicarAjuste();

            /*
             * Actualiza una sola vez los componentes originales que
             * escuchan el evento resize.
             */
            window.dispatchEvent(new Event('resize'));
        }, 490);
    }

    function clickRelacionadoConSidebar(event) {
        const target = event.target instanceof Element
            ? event.target
            : null;

        if (!target) return false;

        const control = target.closest(
            'button, a, [role="button"], [aria-expanded], [aria-controls]'
        );

        if (!control) return false;

        const descriptor = [
            control.id,
            control.className,
            control.getAttribute('aria-controls'),
            control.getAttribute('title'),
            control.getAttribute('data-target'),
            control.getAttribute('data-bs-target')
        ]
            .filter(Boolean)
            .join(' ')
            .toLowerCase();

        const rect = control.getBoundingClientRect();

        return (
            descriptor.includes('sidebar') ||
            descriptor.includes('menu') ||
            descriptor.includes('toggle') ||
            descriptor.includes('collapse') ||
            rect.left < 330
        );
    }

    function iniciarSidebarRapido() {
        root = document.querySelector(ROOT_SELECTOR);
        if (!root) return;

        parent = root.parentElement;
        sidebar = buscarSidebar();

        aplicarAjuste();

        window.addEventListener(
            'resize',
            function (event) {
                /*
                 * El resize sintético actualiza la lógica original, pero
                 * no reinicia esta misma secuencia.
                 */
                if (event.isTrusted) {
                    ajustarDuranteTransicion();
                }
            },
            { passive: true }
        );

        window.addEventListener(
            'orientationchange',
            ajustarDuranteTransicion,
            { passive: true }
        );

        document.addEventListener('click', function (event) {
            if (!clickRelacionadoConSidebar(event)) return;

            window.setTimeout(function () {
                sidebar = buscarSidebar() || sidebar;
                ajustarDuranteTransicion();
            }, 0);
        }, true);

        /*
         * Sólo se observan el padre inmediato y el sidebar.
         * No se instala MutationObserver sobre toda la página.
         */
        if ('ResizeObserver' in window) {
            const resizeObserver = new ResizeObserver(
                ajustarDuranteTransicion
            );

            if (parent) {
                resizeObserver.observe(parent);
            }

            if (sidebar) {
                resizeObserver.observe(sidebar);
            }
        }

        if (sidebar) {
            const mutationObserver = new MutationObserver(
                ajustarDuranteTransicion
            );

            mutationObserver.observe(sidebar, {
                attributes: true,
                attributeFilter: [
                    'class',
                    'style',
                    'aria-expanded',
                    'aria-hidden',
                    'data-state'
                ]
            });
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener(
            'DOMContentLoaded',
            iniciarSidebarRapido,
            { once: true }
        );
    } else {
        iniciarSidebarRapido();
    }
})();

/* ===== SCRIPT ORIGINAL 2 ===== */

    (function () {
        /*
         * Versión de JavaScript para impresión directa.
         * No usamos solamente el booleano anterior porque, durante Hot Reload
         * o navegación parcial, puede quedar cargado el script viejo que llamaba
         * a window.print(). Una versión nueva siempre reemplaza ese comportamiento.
         */
        var BASCULA_BUILD = "2026.07.20-DIRECT-PRINT-V3";

        if (window.__BASCULA_CAMIONERA_BUILD__ === BASCULA_BUILD) {
            return;
        }

        window.__BASCULA_CAMIONERA_BUILD__ = BASCULA_BUILD;
        window.__BASCULA_CAMIONERA_JS_CARGADO__ = true;

        /*
         * Seguridad adicional: dentro de esta pantalla queda bloqueada cualquier
         * llamada JavaScript antigua a window.print(). La impresión válida se hace
         * exclusivamente mediante POST /BasculaCamionera/ImprimirTicket.
         */
        if (!window.__BASCULA_PRINT_NATIVO__) {
            window.__BASCULA_PRINT_NATIVO__ = window.print ? window.print.bind(window) : null;
        }

        window.print = function () {
            console.warn("Se bloqueó window.print(): el ticket debe enviarse por impresión directa ESC/POS.");
            return false;
        };

        var STORAGE_REGISTROS = "sigoBasculaRegistros";
        var STORAGE_BITACORA = "sigoBasculaBitacora";
        var STORAGE_TERMINAL_ID = "sigoBasculaTerminalId";
        var STORAGE_BASCULA_CONFIG = "sigoBasculaConfiguracionActiva";
        var SYNC_ENDPOINT = "/BasculaCamionera/Sync/Movimiento";
        var PRINT_ENDPOINT = "/BasculaCamionera/ImprimirTicket";
        var LIST_ENDPOINT = "/BasculaCamionera/Listar";
        var PRE_REGISTRO_ENDPOINT = "/BasculaCamionera/PreRegistroPorToken";
        var CATALOGOS_OFFLINE_ENDPOINT = "/BasculaCamionera/CatalogosOffline";
        var IDB_NAME = "SIGO_BASCULA_OFFLINE";
        var IDB_VERSION = 1;
        var IDB_STORE_CATALOGOS = "catalogos";
        var CATALOGOS_OFFLINE_META = "sigoBasculaCatalogosOfflineMeta";
        var PASSWORD_CAPTURA_MANUAL = "BASCULA2026";
        var DEFAULT_STABLE_TOLERANCE_KG = 20;
        var DEFAULT_STABLE_SAMPLES = 4;

        var state = {
            registros: [],
            bitacora: [],
            selectedFolio: null,
            preRegistro: null,
            preRegistroBloqueado: false,
            liveWeight: 0,
            connected: false,
            manualAutorizado: false,
            catalogoTipoActual: "",
            terceroCatalogoCache: [],
            productoCatalogoCache: [],
            catalogModalMode: "tercero",
            stableReadings: [],
            isStable: false,
            stableWeight: 0,
            simBaseWeight: 0,
            syncInProgress: false,
            lastSyncError: "",
            catalogosOfflineInProgress: false,
            catalogosOfflineReady: false,
            catalogosOfflineMeta: null,
            scalePolling: false
        };

        function byId(id) {
            return document.getElementById(id);
        }

        function safeText(id, value) {
            var el = byId(id);
            if (el) el.textContent = value == null || value === "" ? "---" : String(value);
        }

        function safeValue(id, value) {
            var el = byId(id);
            if (el) el.value = value == null ? "" : value;
        }

        function getValue(id) {
            var el = byId(id);
            return el ? el.value : "";
        }

        function on(id, eventName, fn) {
            var el = byId(id);
            if (el) el.addEventListener(eventName, fn);
        }

        function toArray(nodes) {
            return Array.prototype.slice.call(nodes || []);
        }

        function now() {
            return new Date().toLocaleString("es-MX");
        }

        function today() {
            return new Date().toLocaleDateString("es-MX");
        }

        function num(v) {
            var n = Number(v || 0);
            return isNaN(n) ? 0 : n;
        }

        function fmt0(v) {
            return Number(v || 0).toLocaleString("es-MX", {
                maximumFractionDigits: 0
            });
        }

        function fmt2(v) {
            return Number(v || 0).toLocaleString("es-MX", {
                minimumFractionDigits: 2,
                maximumFractionDigits: 2
            });
        }

        function kg(v) {
            return fmt2(v) + " kg";
        }

        function usuario() {
            return "Supervisor Báscula";
        }


        function escapeHtml(value) {
            return String(value == null ? "" : value)
                .replace(/&/g, "&amp;")
                .replace(/</g, "&lt;")
                .replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;")
                .replace(/'/g, "&#39;");
        }

        function pad2(n) {
            n = Number(n || 0);
            return n < 10 ? "0" + n : String(n);
        }

        function fechaInputDesdeDate(d) {
            if (!(d instanceof Date) || isNaN(d.getTime())) return "";
            return d.getFullYear() + "-" + pad2(d.getMonth() + 1) + "-" + pad2(d.getDate());
        }

        function parseFechaMx(value) {
            var txt = String(value || "").trim();
            if (!txt) return null;

            var mIso = txt.match(/^(\d{4})-(\d{2})-(\d{2})/);
            if (mIso) {
                var iso = new Date(Number(mIso[1]), Number(mIso[2]) - 1, Number(mIso[3]));
                return isNaN(iso.getTime()) ? null : iso;
            }

            var m = txt.match(/(\d{1,2})[\/\-](\d{1,2})[\/\-](\d{4})/);
            if (!m) return null;

            var d = new Date(Number(m[3]), Number(m[2]) - 1, Number(m[1]));
            return isNaN(d.getTime()) ? null : d;
        }

        function fechaRegistroDentroRango(r, desdeTxt, hastaTxt) {
            var desde = parseFechaMx(desdeTxt);
            var hasta = parseFechaMx(hastaTxt);

            if (hasta) {
                hasta.setHours(23, 59, 59, 999);
            }

            var fechas = [parseFechaMx(r.fechaEntrada), parseFechaMx(r.fechaSalida)].filter(Boolean);

            if (!fechas.length) {
                return !desde && !hasta;
            }

            for (var i = 0; i < fechas.length; i++) {
                var f = fechas[i];
                if (desde && f < desde) continue;
                if (hasta && f > hasta) continue;
                return true;
            }

            return false;
        }

        function getHistorialFiltrado() {
            var q = getValue("searchHistory").toLowerCase();
            var est = getValue("filterEstatus");
            var desde = getValue("filterFechaDesde");
            var hasta = getValue("filterFechaHasta");
            var rows = [];

            for (var i = 0; i < state.registros.length; i++) {
                var r = state.registros[i];

                if (est && r.estatus !== est) continue;

                var json = JSON.stringify(r).toLowerCase();
                if (q && json.indexOf(q) < 0) continue;

                if (!fechaRegistroDentroRango(r, desde, hasta)) continue;

                rows.push(r);
            }

            return rows;
        }

        function cargarStorage() {
            try {
                state.registros = JSON.parse(localStorage.getItem(STORAGE_REGISTROS) || "[]");
            } catch (e) {
                state.registros = [];
            }

            try {
                state.bitacora = JSON.parse(localStorage.getItem(STORAGE_BITACORA) || "[]");
            } catch (e) {
                state.bitacora = [];
            }
        }

        function guardarStorage() {
            localStorage.setItem(STORAGE_REGISTROS, JSON.stringify(state.registros));
            localStorage.setItem(STORAGE_BITACORA, JSON.stringify(state.bitacora));
        }

        function obtenerConfigBasculaDesdeFormulario(scaleActive) {
            var host = getValue("scaleHost").trim();
            var port = Number(getValue("scalePort") || 0);

            return {
                scaleHost: host,
                scalePort: port > 0 ? port : "",
                scaleCommand: getValue("scaleCommand") || "",
                printerName: getValue("printerName").trim(),
                stableTolerance: getValue("stableTolerance") || DEFAULT_STABLE_TOLERANCE_KG,
                stableSamples: getValue("stableSamples") || DEFAULT_STABLE_SAMPLES,
                scaleActive: !!scaleActive && !!host && port > 0,
                updatedAt: new Date().toISOString()
            };
        }

        function persistirConfigBascula(scaleActive) {
            try {
                var cfg = obtenerConfigBasculaDesdeFormulario(scaleActive);
                localStorage.setItem(STORAGE_BASCULA_CONFIG, JSON.stringify(cfg));
                return cfg;
            } catch (e) {
                console.warn("No se pudo guardar configuración de báscula/impresora.", e);
                return null;
            }
        }

        function leerConfigBasculaPersistida() {
            try {
                return JSON.parse(localStorage.getItem(STORAGE_BASCULA_CONFIG) || "null");
            } catch (e) {
                return null;
            }
        }

        function aplicarConfigBasculaPersistida(cfg) {
            if (!cfg) return false;

            safeValue("scaleHost", cfg.scaleHost || "");
            safeValue("scalePort", cfg.scalePort || "");
            safeValue("scaleCommand", cfg.scaleCommand || "");
            safeValue("printerName", cfg.printerName || "");

            if (cfg.stableTolerance !== undefined && cfg.stableTolerance !== null && cfg.stableTolerance !== "") {
                safeValue("stableTolerance", cfg.stableTolerance);
            }

            if (cfg.stableSamples !== undefined && cfg.stableSamples !== null && cfg.stableSamples !== "") {
                safeValue("stableSamples", cfg.stableSamples);
            }

            var activa = !!cfg.scaleActive && !!cfg.scaleHost && Number(cfg.scalePort || 0) > 0;
            state.connected = activa;

            var dot = byId("connectionDot");
            if (dot) dot.classList.toggle("ok", activa);

            if (activa) {
                safeText("connectionText", "Báscula activa: " + cfg.scaleHost + ":" + cfg.scalePort);
                var raw = byId("rawScale");
                if (raw) {
                    raw.textContent = "Configuración activa cargada.\nBáscula: " + cfg.scaleHost + ":" + cfg.scalePort + "\nImpresora: " + (cfg.printerName || "Sin impresora") + "\n" + now();
                }
            } else if (cfg.printerName || cfg.scaleHost || cfg.scalePort) {
                safeText("connectionText", "Configuración cargada / báscula inactiva");
            }

            return activa;
        }

        function cargarConfigBascula() {
            var cfg = leerConfigBasculaPersistida();
            return aplicarConfigBasculaPersistida(cfg);
        }

        function desactivarConexionBascula() {
            if (!confirm("¿Desactivar la conexión activa de la báscula?\n\nLa IP, puerto e impresora se conservarán, pero la báscula quedará inactiva hasta volver a guardar o probar TCP.")) {
                return;
            }

            state.connected = false;
            persistirConfigBascula(false);

            safeText("connectionText", "Báscula desactivada manualmente");
            resetEstabilizador();

            var dot = byId("connectionDot");
            if (dot) dot.classList.remove("ok");

            var raw = byId("rawScale");
            if (raw) {
                raw.textContent = "Conexión de báscula desactivada manualmente.\nLa configuración permanece guardada.\n" + now();
            }

            addLog("Desactivó conexión de báscula", state.selectedFolio || "CONFIGURACIÓN");
        }

        function abrirDbOffline() {
            return new Promise(function (resolve, reject) {
                if (!window.indexedDB) {
                    reject(new Error("IndexedDB no disponible en este navegador."));
                    return;
                }

                var req = indexedDB.open(IDB_NAME, IDB_VERSION);

                req.onupgradeneeded = function (ev) {
                    var db = ev.target.result;
                    if (!db.objectStoreNames.contains(IDB_STORE_CATALOGOS)) {
                        db.createObjectStore(IDB_STORE_CATALOGOS, { keyPath: "key" });
                    }
                };

                req.onsuccess = function () {
                    resolve(req.result);
                };

                req.onerror = function () {
                    reject(req.error || new Error("No se pudo abrir IndexedDB."));
                };
            });
        }

        function idbGet(storeName, key) {
            return abrirDbOffline().then(function (db) {
                return new Promise(function (resolve, reject) {
                    var tx = db.transaction(storeName, "readonly");
                    var store = tx.objectStore(storeName);
                    var req = store.get(key);

                    req.onsuccess = function () {
                        resolve(req.result || null);
                    };

                    req.onerror = function () {
                        reject(req.error || new Error("No se pudo leer cache local."));
                    };

                    tx.oncomplete = function () {
                        db.close();
                    };
                });
            });
        }

        function idbPut(storeName, value) {
            return abrirDbOffline().then(function (db) {
                return new Promise(function (resolve, reject) {
                    var tx = db.transaction(storeName, "readwrite");
                    var store = tx.objectStore(storeName);
                    var req = store.put(value);

                    req.onsuccess = function () {
                        resolve(true);
                    };

                    req.onerror = function () {
                        reject(req.error || new Error("No se pudo guardar cache local."));
                    };

                    tx.oncomplete = function () {
                        db.close();
                    };
                });
            });
        }

        function textoCatalogo(value) {
            return String(value == null ? "" : value).toLowerCase()
                .normalize("NFD")
                .replace(/[\u0300-\u036f]/g, "");
        }

        function filtrarCatalogoOffline(rows, q, campos, take) {
            rows = Array.isArray(rows) ? rows : [];
            take = take || 80;
            var query = textoCatalogo(q).trim();
            var salida = [];

            for (var i = 0; i < rows.length; i++) {
                var r = rows[i] || {};
                var ok = !query;

                if (query) {
                    for (var c = 0; c < campos.length; c++) {
                        if (textoCatalogo(r[campos[c]]).indexOf(query) >= 0) {
                            ok = true;
                            break;
                        }
                    }
                }

                if (ok) {
                    salida.push(r);
                    if (salida.length >= take) break;
                }
            }

            return salida;
        }

        async function leerCatalogoOffline(tipo) {
            try {
                var item = await idbGet(IDB_STORE_CATALOGOS, tipo);
                return item && Array.isArray(item.rows) ? item.rows : [];
            } catch (err) {
                console.warn("No se pudo leer catálogo offline", tipo, err);
                return [];
            }
        }

        async function guardarCatalogoOffline(tipo, rows) {
            rows = Array.isArray(rows) ? rows : [];
            try {
                await idbPut(IDB_STORE_CATALOGOS, {
                    key: tipo,
                    rows: rows,
                    total: rows.length,
                    fecha: new Date().toISOString()
                });
                return true;
            } catch (err) {
                console.warn("No se pudo guardar catálogo offline", tipo, err);
                return false;
            }
        }

        function obtenerRowsRespuestaCatalogo(data) {
            if (Array.isArray(data)) return data;
            if (data && Array.isArray(data.rows)) return data.rows;
            if (data && Array.isArray(data.data)) return data.data;
            if (data && Array.isArray(data.items)) return data.items;
            return [];
        }

        function normalizarCatalogosOfflinePayload(data) {
            data = data || {};
            return {
                clientes: obtenerRowsRespuestaCatalogo(data.clientes || data.Clientes || []),
                proveedores: obtenerRowsRespuestaCatalogo(data.proveedores || data.Proveedores || []),
                productos: obtenerRowsRespuestaCatalogo(data.productos || data.Productos || data.articulos || data.Articulos || []),
                fechaServidor: data.fechaServidor || data.FechaServidor || null
            };
        }

        async function actualizarCatalogosOfflineDesdeServidor(forzar) {
            if (state.catalogosOfflineInProgress) return false;
            if (!forzar && !window.navigator.onLine) return false;

            state.catalogosOfflineInProgress = true;

            try {
                var response = await fetch(CATALOGOS_OFFLINE_ENDPOINT + "?take=20000", {
                    method: "GET",
                    headers: { "Accept": "application/json" },
                    cache: "no-store"
                });

                if (!response.ok) throw new Error("HTTP " + response.status);

                var data = await response.json();
                if (data && data.ok === false) throw new Error(data.msg || "No se pudieron cargar catálogos offline.");

                var payload = normalizarCatalogosOfflinePayload(data);

                await guardarCatalogoOffline("clientes", payload.clientes.map(normalizarTerceroCatalogo).filter(function (r) { return r.codigo || r.nombre; }));
                await guardarCatalogoOffline("proveedores", payload.proveedores.map(normalizarTerceroCatalogo).filter(function (r) { return r.codigo || r.nombre; }));
                await guardarCatalogoOffline("productos", payload.productos.map(normalizarProductoCatalogo).filter(function (r) { return r.codigo || r.nombre; }));

                state.catalogosOfflineReady = true;
                state.catalogosOfflineMeta = {
                    fecha: new Date().toISOString(),
                    fechaServidor: payload.fechaServidor,
                    clientes: payload.clientes.length,
                    proveedores: payload.proveedores.length,
                    productos: payload.productos.length
                };
                localStorage.setItem(CATALOGOS_OFFLINE_META, JSON.stringify(state.catalogosOfflineMeta));

                if (payload.clientes.length || payload.proveedores.length || payload.productos.length) {
                    console.log("Catálogos offline actualizados", state.catalogosOfflineMeta);
                }

                return true;
            } catch (err) {
                console.warn("No se pudieron actualizar catálogos offline. Se usará cache local.", err);
                return false;
            } finally {
                state.catalogosOfflineInProgress = false;
            }
        }

        async function inicializarCatalogosOffline() {
            try {
                state.catalogosOfflineMeta = JSON.parse(localStorage.getItem(CATALOGOS_OFFLINE_META) || "null");
                state.catalogosOfflineReady = !!state.catalogosOfflineMeta;
            } catch (e) {
                state.catalogosOfflineMeta = null;
                state.catalogosOfflineReady = false;
            }

            actualizarCatalogosOfflineDesdeServidor(false);
        }

        function crearGuidLocal() {
            if (window.crypto && typeof window.crypto.randomUUID === "function") {
                return window.crypto.randomUUID();
            }

            return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, function (c) {
                var r = Math.random() * 16 | 0;
                var v = c === "x" ? r : (r & 0x3 | 0x8);
                return v.toString(16);
            });
        }

        function obtenerTerminalId() {
            var terminal = localStorage.getItem(STORAGE_TERMINAL_ID);

            if (!terminal) {
                terminal = "CASETA-01";
                localStorage.setItem(STORAGE_TERMINAL_ID, terminal);
            }

            return terminal;
        }

        function asegurarDatosOffline(r) {
            if (!r) return r;

            if (!r.movimientoGuid) r.movimientoGuid = crearGuidLocal();
            if (!r.terminalId) r.terminalId = obtenerTerminalId();
            if (!r.syncStatus) r.syncStatus = "PENDIENTE_SYNC";
            if (r.syncAttempts == null) r.syncAttempts = 0;
            if (!r.fechaCreacionLocal) r.fechaCreacionLocal = new Date().toISOString();

            var cantidad = Number(r.cantidad || 0);
            if (!Number.isFinite(cantidad) || cantidad <= 0) r.cantidad = 1;
            else r.cantidad = Math.max(1, Math.trunc(cantidad));

            if (!String(r.origen || "").trim()) r.origen = "PLANTA 1";
            if (!String(r.destino || "").trim()) r.destino = "TIF 776";

            return r;
        }

        function normalizarRegistrosOffline() {
            var cambio = false;

            for (var i = 0; i < state.registros.length; i++) {
                var r = state.registros[i];
                var antesGuid = r.movimientoGuid;
                var folioAnterior = r.folio;

                asegurarDatosOffline(r);

                /*
                   Migración desde la versión anterior, que utilizaba BAS-AAAA-00001
                   como folio local. El nuevo folio local debe ser único; el oficial
                   lo asigna SQL Server al sincronizar.
                */
                if (!r.folioServidor && /^BAS-\d{4}-\d+$/i.test(String(r.folio || ""))) {
                    r.folioLocalAnterior = r.folio;
                    r.folio = nuevoFolio();

                    if (state.selectedFolio === folioAnterior) {
                        state.selectedFolio = r.folio;
                    }

                    cambio = true;
                }

                /* Fuerza una nueva sincronización para obtener FolioServidor. */
                if (!r.folioServidor && r.syncStatus === "SINCRONIZADO") {
                    r.syncStatus = "PENDIENTE_SYNC";
                    cambio = true;
                }

                if (!antesGuid) cambio = true;
            }

            if (cambio) guardarStorage();
        }

        function fechaParaServidor(valor) {
            if (!valor) return null;

            if (valor instanceof Date && !isNaN(valor.getTime())) {
                return valor.toISOString();
            }

            var txt = String(valor || "").trim();
            if (!txt) return null;

            var d = new Date(txt);
            if (!isNaN(d.getTime())) return d.toISOString();

            return new Date().toISOString();
        }

        function crearPayloadSync(r) {
            asegurarDatosOffline(r);

            var payload = Object.assign({}, r);
            payload.movimientoGuid = r.movimientoGuid;
            payload.terminalId = r.terminalId || obtenerTerminalId();
            payload.fechaEntrada = fechaParaServidor(r.fechaEntrada) || new Date().toISOString();
            payload.fechaSalida = r.fechaSalida ? fechaParaServidor(r.fechaSalida) : null;
            payload.fechaCreacionLocal = r.fechaCreacionLocal || new Date().toISOString();
            payload.usuarioEntrada = r.usuarioEntrada || r.usuario || usuario();
            payload.usuarioSalida = r.usuarioSalida || (r.estatus === "CERRADO" ? usuario() : "");
            payload.pesoEntradaEstable = !!(r.pesoEntrada && Number(r.pesoEntrada) > 0);
            payload.pesoSalidaEstable = !!(r.pesoSalida && Number(r.pesoSalida) > 0);
            payload.creadoOffline = true;

            return payload;
        }

        function actualizarRegistroSincronizado(folio, movimientoGuid, datos) {
            for (var i = 0; i < state.registros.length; i++) {
                var r = state.registros[i];
                if ((movimientoGuid && r.movimientoGuid === movimientoGuid) || (folio && r.folio === folio)) {
                    r.syncStatus = "SINCRONIZADO";
                    r.syncAttempts = Number(r.syncAttempts || 0);
                    r.syncError = "";
                    r.fechaSyncServidor = datos && datos.fechaSyncServidor
                        ? datos.fechaSyncServidor
                        : new Date().toISOString();

                    if (datos && datos.folioServidor) r.folioServidor = datos.folioServidor;
                    if (datos && datos.movimientoId) r.movimientoId = datos.movimientoId;
                    return true;
                }
            }

            return false;
        }

        async function sincronizarMovimientoServidor(r) {
            asegurarDatosOffline(r);
            r.syncAttempts = Number(r.syncAttempts || 0) + 1;
            r.ultimoIntentoSync = new Date().toISOString();
            r.syncStatus = "ENVIANDO_SYNC";
            guardarStorage();

            var resp = await fetch(SYNC_ENDPOINT, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "Accept": "application/json"
                },
                body: JSON.stringify(crearPayloadSync(r))
            });

            var data = null;
            try {
                data = await resp.json();
            } catch (e) {
                data = null;
            }

            if (!resp.ok || !data || data.ok !== true) {
                var msg = data && data.msg ? data.msg : ("HTTP " + resp.status);
                throw new Error(msg);
            }

            actualizarRegistroSincronizado(r.folio, r.movimientoGuid, data);
            guardarStorage();
            return data;
        }

        async function sincronizarPendientes() {
            if (state.syncInProgress) return;
            if (!window.navigator.onLine) return;

            state.syncInProgress = true;

            try {
                normalizarRegistrosOffline();

                for (var i = 0; i < state.registros.length; i++) {
                    var r = state.registros[i];
                    if (!r || r.syncStatus === "SINCRONIZADO") continue;

                    try {
                        await sincronizarMovimientoServidor(r);
                        safeText("connectionText", "Movimiento sincronizado con servidor SQL");
                    } catch (err) {
                        r.syncStatus = "ERROR_SYNC";
                        r.syncError = err && err.message ? err.message : String(err);
                        state.lastSyncError = r.syncError;
                        guardarStorage();
                        console.warn("Registro pendiente de sincronizar:", r.folio, r.syncError);
                        break;
                    }
                }

                renderAll();
            } finally {
                state.syncInProgress = false;
            }
        }

        function fechaVistaServidor(value) {
            if (!value) return "";
            var d = new Date(value);
            return isNaN(d.getTime()) ? String(value) : d.toLocaleString("es-MX");
        }

        async function cargarMovimientosServidor() {
            if (!window.navigator.onLine) return false;

            try {
                var resp = await fetch(LIST_ENDPOINT + "?take=1000", {
                    headers: { "Accept": "application/json" },
                    cache: "no-store"
                });

                var data = await resp.json();
                if (!resp.ok || !data || data.ok !== true) {
                    throw new Error(data && data.msg ? data.msg : ("HTTP " + resp.status));
                }

                var rows = Array.isArray(data.rows) ? data.rows : [];

                for (var i = 0; i < rows.length; i++) {
                    var s = rows[i] || {};
                    s.folio = s.folio || s.folioLocal || "";
                    s.folioServidor = s.folioServidor || "";
                    s.fechaEntrada = fechaVistaServidor(s.fechaEntrada);
                    s.fechaSalida = fechaVistaServidor(s.fechaSalida);
                    s.cantidad = Math.max(1, Math.trunc(Number(s.cantidad || 1)));
                    s.origen = String(s.origen || "").trim() || "PLANTA 1";
                    s.destino = String(s.destino || "").trim() || "TIF 776";
                    s.syncStatus = s.folioServidor ? "SINCRONIZADO" : "PENDIENTE_SYNC";
                    s.syncError = "";

                    var index = -1;
                    for (var j = 0; j < state.registros.length; j++) {
                        var local = state.registros[j];
                        if ((s.movimientoGuid && local.movimientoGuid === s.movimientoGuid) ||
                            (s.folio && local.folio === s.folio)) {
                            index = j;
                            break;
                        }
                    }

                    if (index >= 0) {
                        var anterior = state.registros[index] || {};
                        var combinado = Object.assign({}, anterior, s);

                        /* Datos de relación con pre-registro que no viven en la tabla principal. */
                        combinado.preRegistroId = anterior.preRegistroId || combinado.preRegistroId || null;
                        combinado.preRegistroGuid = anterior.preRegistroGuid || combinado.preRegistroGuid || "";
                        combinado.tokenPreRegistro = anterior.tokenPreRegistro || combinado.tokenPreRegistro || "";
                        combinado.folioPreRegistro = anterior.folioPreRegistro || combinado.folioPreRegistro || "";
                        combinado.areaOrigenPreRegistro = anterior.areaOrigenPreRegistro || combinado.areaOrigenPreRegistro || "";
                        combinado.usuarioCapturaPreRegistro = anterior.usuarioCapturaPreRegistro || combinado.usuarioCapturaPreRegistro || "";
                        combinado.origenCaptura = anterior.origenCaptura || combinado.origenCaptura || "CAPTURA_CASETA";

                        state.registros[index] = combinado;
                    } else {
                        state.registros.push(s);
                    }
                }

                state.registros.sort(function (a, b) {
                    var aId = Number(a.movimientoId || 0);
                    var bId = Number(b.movimientoId || 0);
                    if (aId !== bId) return bId - aId;
                    return String(b.fechaEntrada || "").localeCompare(String(a.fechaEntrada || ""));
                });

                guardarStorage();
                renderAll();
                return true;
            } catch (err) {
                console.warn("No se pudo cargar historial desde SQL:", err);
                state.lastSyncError = err && err.message ? err.message : String(err);
                return false;
            }
        }

        function encolarSincronizacion(r) {
            asegurarDatosOffline(r);
            r.syncStatus = "PENDIENTE_SYNC";
            guardarStorage();

            setTimeout(function () {
                sincronizarPendientes();
            }, 300);
        }

        function nuevoFolio() {
            /*
               Este folio es únicamente provisional para trabajar sin internet.
               El folio oficial BAS-AAAA-000001 lo genera SQL Server y regresa
               en la propiedad folioServidor.
            */
            var terminal = String(obtenerTerminalId() || "CASETA-01")
                .toUpperCase()
                .replace(/[^A-Z0-9]/g, "")
                .substring(0, 16) || "CASETA01";

            var d = new Date();
            var marca =
                d.getFullYear() +
                pad2(d.getMonth() + 1) +
                pad2(d.getDate()) +
                pad2(d.getHours()) +
                pad2(d.getMinutes()) +
                pad2(d.getSeconds()) +
                String(d.getMilliseconds()).padStart(3, "0");

            var aleatorio = Math.random().toString(36).substring(2, 8).toUpperCase();
            return ("LOCAL-" + terminal + "-" + marca + "-" + aleatorio).substring(0, 60);
        }

        function folioVisible(r) {
            if (!r) return "---";

            /*
               El folio mostrado al operador debe ser el oficial de SQL Server.
               El folio local se conserva internamente para modo offline, pero
               no se presenta como si fuera el consecutivo oficial.
            */
            return r.folioServidor || "PENDIENTE SQL";
        }

        function addLog(accion, folio) {
            state.bitacora.unshift({
                fecha: now(),
                usuario: usuario(),
                accion: accion,
                folio: folio || "---"
            });

            if (state.bitacora.length > 300) {
                state.bitacora = state.bitacora.slice(0, 300);
            }

            guardarStorage();
        }

        function buscarRegistroActual() {
            if (!state.selectedFolio) return null;

            for (var i = 0; i < state.registros.length; i++) {
                if (state.registros[i].folio === state.selectedFolio) {
                    return state.registros[i];
                }
            }

            return null;
        }


        function toggleButton(id, visible) {
            var el = byId(id);
            if (!el) return;

            el.classList.toggle("op-hidden", !visible);
            el.disabled = !visible;
            el.setAttribute("aria-hidden", visible ? "false" : "true");
        }

        function actualizarBotonesOperacion() {
            var registroActual = buscarRegistroActual();
            var esPendiente = !!(registroActual && registroActual.estatus === "PENDIENTE" && num(registroActual.pesoEntrada) > 0);
            var esCerrado = !!(registroActual && registroActual.estatus === "CERRADO");
            var mostrarEntrada = !esPendiente && !esCerrado;
            var mostrarSalida = esPendiente;

            toggleButton("btnGuardarEntrada", mostrarEntrada);
            toggleButton("btnGuardarEntrada2", mostrarEntrada);
            toggleButton("btnCerrarSalida", mostrarSalida);
            toggleButton("btnCerrarSalida2", mostrarSalida);

            var btnCapturar = byId("btnCapturarPeso");
            if (btnCapturar) {
                btnCapturar.textContent = mostrarSalida ? "Capturar salida" : "Capturar entrada";
                btnCapturar.title = mostrarSalida
                    ? "Captura el peso actual de la báscula como peso de salida."
                    : "Captura el peso actual de la báscula como peso de entrada.";
            }
        }

        function setReadonly(id, readonly) {
            var el = byId(id);
            if (!el) return;

            el.readOnly = !!readonly;
            el.classList.toggle("manual-enabled", !readonly);
        }

        function aplicarModoCapturaManual() {
            var solicitado = getValue("capturaManual") === "Sí";
            var habilitado = solicitado && state.manualAutorizado;

            setReadonly("pesoEntrada", !habilitado);
            setReadonly("pesoSalida", !habilitado);
            setReadonly("motivoManual", !habilitado);

            var motivo = byId("motivoManual");
            if (motivo) {
                motivo.placeholder = habilitado
                    ? "Capture motivo obligatorio de edición manual"
                    : "Solo si hubo captura manual autorizada";
            }
        }

        function bloquearCamposCatalogo() {
            var codigo = byId("codigoSap");
            var sku = byId("sku");

            if (codigo) {
                codigo.readOnly = true;
                codigo.setAttribute("tabindex", "-1");
                codigo.title = "Campo bloqueado. Se llena automáticamente desde el catálogo de proveedor/cliente.";
            }

            if (sku) {
                sku.readOnly = true;
                sku.setAttribute("tabindex", "-1");
                sku.title = "Campo bloqueado. Se llena automáticamente desde el catálogo de producto.";
            }
        }

        function cambiarCapturaManual() {
            if (getValue("capturaManual") === "Sí") {
                var pass = window.prompt("Contraseña para habilitar captura manual de peso:");

                if (pass === PASSWORD_CAPTURA_MANUAL) {
                    state.manualAutorizado = true;
                    aplicarModoCapturaManual();
                    addLog("Habilitó captura manual de peso", state.selectedFolio || "SIN FOLIO");
                    alert("Captura manual habilitada. Capture el motivo de autorización.");
                } else {
                    state.manualAutorizado = false;
                    safeValue("capturaManual", "No");
                    aplicarModoCapturaManual();
                    alert("Contraseña incorrecta. Los campos de peso siguen bloqueados y solo se llenan desde la báscula.");
                }
            } else {
                state.manualAutorizado = false;
                aplicarModoCapturaManual();
            }

            renderResumen();
        }

        function usarPesoBasculaAutomatico() {
            return !(getValue("capturaManual") === "Sí" && state.manualAutorizado);
        }

        function obtenerCampoDestinoPeso(destinoSolicitado) {
            if (destinoSolicitado === "entrada") return "pesoEntrada";
            if (destinoSolicitado === "salida") return "pesoSalida";

            var registroActual = buscarRegistroActual();

            if (registroActual && registroActual.estatus === "PENDIENTE") {
                return "pesoSalida";
            }

            return "pesoEntrada";
        }


        function setPreRegistroStatus(texto, tipo) {
            var el = byId("estatusPreRegistro");
            if (!el) return;

            el.textContent = texto || "Sin escaneo";
            el.classList.remove("ok", "warn", "error");
            el.classList.add(tipo || "warn");
        }

        function extraerTokenPreRegistro(valor) {
            var txt = String(valor || "").trim();

            if (!txt) return "";

            if (txt.toUpperCase().indexOf("BASPRE|") === 0) {
                return (txt.split("|")[1] || "").trim();
            }

            try {
                var url = new URL(txt, window.location.origin);
                var token = url.searchParams.get("token");
                if (token) return token.trim();
            } catch (e) {
                // No era URL; se usa como token directo.
            }

            return txt;
        }

        async function cargarPreRegistroDesdeToken(valor) {
            var token = extraerTokenPreRegistro(valor);

            if (!token) {
                setPreRegistroStatus("Código inválido", "error");
                alert("Escanee o capture un QR/código válido.");
                return;
            }

            setPreRegistroStatus("Buscando...", "warn");
            safeText("connectionText", "Consultando pre-registro de báscula...");

            try {
                var resp = await fetch(PRE_REGISTRO_ENDPOINT + "?token=" + encodeURIComponent(token), {
                    method: "GET",
                    headers: { "Accept": "application/json" }
                });

                var data = await resp.json();

                if (!resp.ok || !data || data.ok !== true) {
                    setPreRegistroStatus("No encontrado", "error");
                    alert(data && data.msg ? data.msg : "No se encontró el pre-registro.");
                    return;
                }

                aplicarPreRegistroEnFormulario(data.preRegistro);
                setPreRegistroStatus("Cargado", "ok");
                safeText("connectionText", "Pre-registro cargado. Caseta solo debe capturar peso y guardar.");

            } catch (err) {
                setPreRegistroStatus("Error", "error");
                alert("Error al consultar el pre-registro: " + err.message);
            }
        }

        function aplicarPreRegistroEnFormulario(p) {
            if (!p) return;

            setForm(null);

            state.preRegistro = {
                preRegistroId: p.preRegistroId || null,
                preRegistroGuid: p.preRegistroGuid || "",
                token: p.token || "",
                folioPreRegistro: p.folioPreRegistro || "",
                areaOrigen: p.areaOrigen || "",
                usuarioCaptura: p.usuarioCaptura || ""
            };

            state.preRegistroBloqueado = true;

            safeValue("tipoMovimiento", p.tipoMovimiento || "Entrada proveedor");
            safeValue("clasificacion", p.clasificacion || "Ganado en pie");
            safeValue("cantidad", Math.max(1, Math.trunc(Number(p.cantidad || 1))));
            safeValue("tercero", p.tercero || "");
            safeValue("codigoSap", p.codigoSap || "");
            safeValue("placas", (p.placas || "").toUpperCase());
            safeValue("producto", p.producto || "");
            safeValue("sku", p.sku || "");
            safeValue("documento", p.documento || "");
            safeValue("chofer", p.chofer || "");
            safeValue("origen", p.origen || "PLANTA 1");
            safeValue("destino", p.destino || "TIF 776");
            safeValue("condicion", p.condicion || "");
            safeValue("observaciones", p.observaciones || "");
            safeValue("folioPreRegistroVista", p.folioPreRegistro || "");
            safeValue("areaPreRegistroVista", p.areaOrigen || "");

            actualizarCampoCantidad();
            bloquearCamposPreRegistro(true);
            renderResumen();
        }

        function bloquearCamposPreRegistro(bloquear) {
            state.preRegistroBloqueado = !!bloquear;

            var form = byId("frmBascula");
            if (form) {
                form.classList.toggle("pre-registro-lock", !!bloquear);
            }

            var camposClave = [
                "tipoMovimiento",
                "clasificacion",
                "cantidad",
                "tercero",
                "codigoSap",
                "producto",
                "sku",
                "documento",
                "origen",
                "destino"
            ];

            camposClave.forEach(function (id) {
                var el = byId(id);
                if (el) {
                    el.disabled = !!bloquear;
                    if (bloquear) el.setAttribute("data-lock-preregistro", "1");
                    else el.removeAttribute("data-lock-preregistro");
                }
            });

            // Caseta puede corregir estos datos operativos si la unidad llegó diferente.
            ["placas", "chofer", "condicion", "observaciones"].forEach(function (id) {
                var el = byId(id);
                if (el) el.disabled = false;
            });
        }

        function limpiarPreRegistro() {
            state.preRegistro = null;
            state.preRegistroBloqueado = false;
            safeValue("scanPreRegistro", "");
            safeValue("folioPreRegistroVista", "");
            safeValue("areaPreRegistroVista", "");
            setPreRegistroStatus("Sin escaneo", "warn");
            bloquearCamposPreRegistro(false);
        }

        function liberarCapturaManualDesdePreRegistro() {
            if (!state.preRegistro) {
                limpiarPreRegistro();
                setForm(null);
                return;
            }

            var ok = confirm("Esto liberará la captura manual y el movimiento quedará marcado como CAPTURA_CASETA. ¿Desea continuar?");

            if (!ok) return;

            limpiarPreRegistro();
            safeText("connectionText", "Captura manual habilitada. Revise todos los datos antes de pesar.");
        }


        function unidadCantidad(clasificacion, cantidad) {
            var tipo = String(clasificacion || "").toLowerCase();
            var plural = Number(cantidad || 0) !== 1;

            if (tipo.indexOf("ganado") >= 0) return plural ? "animales" : "animal";
            if (tipo.indexOf("caja") >= 0) return plural ? "cajas" : "caja";
            if (tipo.indexOf("canal") >= 0) return plural ? "canales" : "canal";
            if (tipo.indexOf("subproducto") >= 0) return plural ? "piezas" : "pieza";
            return plural ? "unidades" : "unidad";
        }

        function textoCantidad(registro) {
            var cantidad = Math.max(1, Math.trunc(Number(registro && registro.cantidad ? registro.cantidad : 1)));
            return cantidad.toLocaleString("es-MX") + " " + unidadCantidad(registro ? registro.clasificacion : "", cantidad);
        }

        function actualizarCampoCantidad() {
            var clasificacion = getValue("clasificacion") || "Ganado en pie";
            var cantidadEl = byId("cantidad");
            var label = byId("cantidadLabel");
            var hint = byId("cantidadHint");
            var unidad = unidadCantidad(clasificacion, 2);

            if (label) {
                if (unidad === "animales") label.textContent = "Cantidad de animales";
                else if (unidad === "cajas") label.textContent = "Cantidad de cajas";
                else if (unidad === "canales") label.textContent = "Cantidad de canales";
                else if (unidad === "piezas") label.textContent = "Cantidad de piezas";
                else label.textContent = "Cantidad de unidades";
            }

            if (cantidadEl) {
                cantidadEl.min = "1";
                cantidadEl.step = "1";
                cantidadEl.placeholder = "Número de " + unidad;
                if (!Number.isFinite(Number(cantidadEl.value)) || Number(cantidadEl.value) <= 0) {
                    cantidadEl.value = "1";
                }
            }

            if (hint) hint.textContent = "Capture cuántos " + unidad + " ingresan.";
        }

        function getForm() {
            var entrada = num(getValue("pesoEntrada"));
            var salida = num(getValue("pesoSalida"));
            var actual = buscarRegistroActual();

            return {
                folio: state.selectedFolio || nuevoFolio(),
                folioServidor: actual ? (actual.folioServidor || "") : "",
                movimientoId: actual ? (actual.movimientoId || null) : null,
                tipoMovimiento: getValue("tipoMovimiento"),
                clasificacion: getValue("clasificacion"),
                cantidad: Math.max(1, Math.trunc(Number(getValue("cantidad") || 1))),
                tercero: getValue("tercero").trim(),
                codigoSap: getValue("codigoSap").trim(),
                placas: getValue("placas").trim().toUpperCase(),
                producto: getValue("producto").trim(),
                sku: getValue("sku").trim(),
                documento: getValue("documento").trim(),
                chofer: getValue("chofer").trim(),
                origen: getValue("origen").trim(),
                destino: getValue("destino").trim(),
                condicion: getValue("condicion").trim(),
                pesoEntrada: entrada,
                pesoSalida: salida,
                pesoNeto: Math.abs(entrada - salida),
                capturaManual: getValue("capturaManual"),
                motivoManual: getValue("motivoManual").trim(),
                observaciones: getValue("observaciones").trim(),
                estatus: salida > 0 ? "CERRADO" : "PENDIENTE",
                fechaEntrada: "",
                fechaSalida: "",
                usuario: usuario(),
                usuarioEntrada: usuario(),
                usuarioSalida: salida > 0 ? usuario() : "",
                movimientoGuid: actual ? (actual.movimientoGuid || "") : "",
                terminalId: actual ? (actual.terminalId || obtenerTerminalId()) : obtenerTerminalId(),
                preRegistroId: state.preRegistro ? state.preRegistro.preRegistroId : null,
                preRegistroGuid: state.preRegistro ? state.preRegistro.preRegistroGuid : null,
                tokenPreRegistro: state.preRegistro ? state.preRegistro.token : "",
                folioPreRegistro: state.preRegistro ? state.preRegistro.folioPreRegistro : "",
                origenCaptura: state.preRegistro ? "PRE_REGISTRO_QR" : "CAPTURA_CASETA",
                areaOrigenPreRegistro: state.preRegistro ? state.preRegistro.areaOrigen : "",
                usuarioCapturaPreRegistro: state.preRegistro ? state.preRegistro.usuarioCaptura : "",
                syncStatus: "PENDIENTE_SYNC",
                syncAttempts: 0,
                fechaCreacionLocal: new Date().toISOString()
            };
        }

        function setForm(r) {
            state.selectedFolio = r ? r.folio : null;
            state.manualAutorizado = false;

            if (r && (r.tokenPreRegistro || r.folioPreRegistro || r.preRegistroGuid || r.preRegistroId)) {
                state.preRegistro = {
                    preRegistroId: r.preRegistroId || null,
                    preRegistroGuid: r.preRegistroGuid || "",
                    token: r.tokenPreRegistro || "",
                    folioPreRegistro: r.folioPreRegistro || "",
                    areaOrigen: r.areaOrigenPreRegistro || "",
                    usuarioCaptura: r.usuarioCapturaPreRegistro || ""
                };
                state.preRegistroBloqueado = true;
                safeValue("folioPreRegistroVista", state.preRegistro.folioPreRegistro);
                safeValue("areaPreRegistroVista", state.preRegistro.areaOrigen);
                setPreRegistroStatus("Cargado", "ok");
            } else if (!r) {
                limpiarPreRegistro();
            }

            safeValue("tipoMovimiento", r ? r.tipoMovimiento : "Entrada proveedor");
            safeValue("clasificacion", r ? r.clasificacion : "Ganado en pie");
            safeValue("cantidad", r ? Math.max(1, Math.trunc(Number(r.cantidad || 1))) : 1);
            safeValue("tercero", r ? r.tercero : "");
            safeValue("codigoSap", r ? r.codigoSap : "");
            safeValue("placas", r ? r.placas : "");
            safeValue("producto", r ? r.producto : "");
            safeValue("sku", r ? r.sku : "");
            safeValue("documento", r ? r.documento : "");
            safeValue("chofer", r ? r.chofer : "");
            safeValue("origen", r && String(r.origen || "").trim() ? r.origen : "PLANTA 1");
            safeValue("destino", r && String(r.destino || "").trim() ? r.destino : "TIF 776");
            safeValue("condicion", r ? r.condicion : "");
            safeValue("pesoEntrada", r ? r.pesoEntrada : "");
            safeValue("pesoSalida", r ? r.pesoSalida : "");
            safeValue("capturaManual", r ? r.capturaManual : "No");
            safeValue("motivoManual", r ? r.motivoManual : "");
            safeValue("observaciones", r ? r.observaciones : "");

            actualizarCampoCantidad();
            bloquearCamposPreRegistro(!!state.preRegistro);
            aplicarModoCapturaManual();
            renderResumen();
        }

        function validarBasico() {
            if (!getValue("tercero").trim()) {
                alert("Capture Proveedor / Cliente.");
                return false;
            }

            if (!getValue("producto").trim()) {
                alert("Capture Producto / Descripción.");
                return false;
            }

            if (!getValue("placas").trim()) {
                alert("Capture Placas.");
                return false;
            }

            var cantidad = Number(getValue("cantidad") || 0);
            if (!Number.isFinite(cantidad) || cantidad <= 0 || Math.trunc(cantidad) !== cantidad) {
                alert("Capture una cantidad entera mayor a cero.");
                var cantidadEl = byId("cantidad");
                if (cantidadEl) cantidadEl.focus();
                return false;
            }

            if (!getValue("origen").trim()) {
                alert("Capture el origen.");
                var origenEl = byId("origen");
                if (origenEl) origenEl.focus();
                return false;
            }

            if (!getValue("destino").trim()) {
                alert("Capture el destino.");
                var destinoEl = byId("destino");
                if (destinoEl) destinoEl.focus();
                return false;
            }

            return true;
        }


        function getStableTolerance() {
            var n = Number(getValue("stableTolerance") || DEFAULT_STABLE_TOLERANCE_KG);
            return isNaN(n) || n < 0 ? DEFAULT_STABLE_TOLERANCE_KG : n;
        }

        function getStableSamples() {
            var n = Math.round(Number(getValue("stableSamples") || DEFAULT_STABLE_SAMPLES));
            if (isNaN(n) || n < 2) n = DEFAULT_STABLE_SAMPLES;
            if (n > 10) n = 10;
            return n;
        }

        function resetEstabilizador() {
            state.stableReadings = [];
            state.isStable = false;
            state.stableWeight = 0;
            actualizarBadgeEstable();
        }

        function actualizarBadgeEstable() {
            var badge = byId("stableBadge");
            var panel = document.querySelector(".peso-panel");
            var display = document.querySelector(".peso-display");
            var scene = byId("truckScene");

            if (state.isStable) {
                if (badge) {
                    badge.textContent = "Estable";
                    badge.className = "stable-badge ok";
                }
                if (panel) panel.classList.add("scale-stable");
                if (display) display.classList.add("scale-stable");
                if (scene) scene.classList.add("scale-stable");
            } else {
                if (badge) {
                    badge.textContent = "Estabilizando";
                    badge.className = "stable-badge warn";
                }
                if (panel) panel.classList.remove("scale-stable");
                if (display) display.classList.remove("scale-stable");
                if (scene) scene.classList.remove("scale-stable");
            }
        }

        function procesarEstabilizador(peso) {
            peso = Number(peso || 0);

            if (peso <= 0 || isNaN(peso)) {
                state.isStable = false;
                state.stableWeight = 0;
                state.stableReadings = [];
                actualizarBadgeEstable();
                return false;
            }

            var muestras = getStableSamples();
            var tolerancia = getStableTolerance();

            state.stableReadings.push(peso);
            if (state.stableReadings.length > muestras) {
                state.stableReadings = state.stableReadings.slice(state.stableReadings.length - muestras);
            }

            if (state.stableReadings.length < muestras) {
                state.isStable = false;
                state.stableWeight = 0;
                actualizarBadgeEstable();
                return false;
            }

            var min = Math.min.apply(null, state.stableReadings);
            var max = Math.max.apply(null, state.stableReadings);
            var promedio = state.stableReadings.reduce(function (a, b) { return a + b; }, 0) / state.stableReadings.length;

            state.isStable = (max - min) <= tolerancia;
            state.stableWeight = state.isStable ? promedio : 0;
            actualizarBadgeEstable();
            return state.isStable;
        }

        function actualizarTrailerEscena(pesoMostrar) {
            var scene = byId("truckScene");
            if (!scene) return;

            var peso = Number(pesoMostrar || 0);
            if (peso > 0) {
                scene.classList.remove("empty");
                scene.classList.remove("leaving");
                scene.classList.add("active");
            } else {
                if (scene.classList.contains("active")) {
                    scene.classList.remove("active");
                    scene.classList.add("leaving");
                    window.clearTimeout(scene._leaveTimer);
                    scene._leaveTimer = window.setTimeout(function () {
                        scene.classList.remove("leaving");
                        scene.classList.add("empty");
                    }, 900);
                } else {
                    scene.classList.add("empty");
                }
            }
        }

        function actualizarPesoBascula(peso, rawText) {
            state.liveWeight = Number(peso || 0);
            var estable = procesarEstabilizador(state.liveWeight);
            var pesoMostrar = estable && state.stableWeight > 0 ? state.stableWeight : state.liveWeight;

            safeText("liveWeight", fmt2(pesoMostrar));
            actualizarCampoPesoEnVivo(pesoMostrar);
            actualizarTrailerEscena(pesoMostrar);

            if (estable) {
                safeText("connectionText", "Peso estable listo para capturar");
            } else {
                safeText("connectionText", "Estabilizando peso de báscula");
            }

            var raw = byId("rawScale");
            if (raw) {
                var muestras = state.stableReadings.map(function (x) { return x.toFixed(2); }).join(" / ");
                raw.textContent = (rawText || "LECTURA BÁSCULA") + "\nMuestras: " + muestras + "\nEstado: " + (estable ? "ESTABLE" : "EN ESTABILIZACIÓN") + "\n" + now();
            }

            return estable;
        }

        function simularPeso() {
            if (!state.simBaseWeight || Math.random() < .08) {
                state.simBaseWeight = Math.round((Math.random() * 34000 + 6000) * 100) / 100;
                resetEstabilizador();
            }

            var muestras = getStableSamples();
            var tolerancia = Math.max(0.2, getStableTolerance() / 4);
            var peso = state.simBaseWeight;

            for (var i = 0; i < muestras; i++) {
                peso = state.simBaseWeight + ((Math.random() * tolerancia) - (tolerancia / 2));
                actualizarPesoBascula(peso, "SIM TCP STREAM > ST,GS,+" + peso.toFixed(2) + "kg");
            }

            var dot = byId("connectionDot");
            if (dot) dot.classList.add("ok");
        }

        function probarPesoCero() {
            state.simBaseWeight = 0;
            resetEstabilizador();
            actualizarPesoBascula(0, "PRUEBA DE CERO > 0.00kg");
            safeText("connectionText", "Báscula en cero / sin unidad sobre plataforma");

            var dot = byId("connectionDot");
            if (dot) dot.classList.add("ok");
        }

        function normalizarNumeroBascula(valor) {
            var txt = String(valor == null ? "" : valor).trim();

            if (!txt) return 0;

            txt = txt.replace(/kg/gi, "").replace(/[^0-9,.-]/g, "");

            if (txt.indexOf(",") >= 0 && txt.indexOf(".") >= 0) {
                txt = txt.replace(/,/g, "");
            } else if (txt.indexOf(",") >= 0 && txt.indexOf(".") < 0) {
                var partes = txt.split(",");
                if (partes.length === 2 && partes[1].length <= 2) {
                    txt = partes[0].replace(/,/g, "") + "." + partes[1];
                } else {
                    txt = txt.replace(/,/g, "");
                }
            }

            var n = Number(txt);
            return isNaN(n) ? 0 : n;
        }

        function obtenerPesoActualBascula() {
            if (state.isStable && state.stableWeight > 0) {
                return num(state.stableWeight);
            }

            var peso = num(state.liveWeight);

            if (peso > 0) return peso;

            var indicador = byId("liveWeight");
            if (!indicador) return 0;

            return normalizarNumeroBascula(indicador.textContent || indicador.innerText || "");
        }

        function setPesoCampo(id, peso) {
            var el = byId(id);
            if (!el) return false;

            var estabaReadonly = el.readOnly;
            el.readOnly = false;
            el.value = Number(peso || 0).toFixed(2);
            el.dispatchEvent(new Event("input", { bubbles: true }));
            el.dispatchEvent(new Event("change", { bubbles: true }));
            el.readOnly = estabaReadonly;

            return true;
        }

        function debeActualizarPesoEnVivo() {
            if (!usarPesoBasculaAutomatico()) return false;

            var registroActual = buscarRegistroActual();

            if (registroActual && registroActual.estatus === "CERRADO") {
                return false;
            }

            return true;
        }

        function actualizarCampoPesoEnVivo(pesoIndicador) {
            if (!debeActualizarPesoEnVivo()) return;

            var target = obtenerCampoDestinoPeso("");
            var el = byId(target);

            if (!el) return;

            var peso = Number(pesoIndicador || 0);
            if (isNaN(peso)) peso = 0;

            var nuevoValor = peso.toFixed(2);

            if (el.value === nuevoValor) return;

            var estabaReadonly = el.readOnly;
            el.readOnly = false;
            el.value = nuevoValor;
            el.dispatchEvent(new Event("input", { bubbles: true }));
            el.dispatchEvent(new Event("change", { bubbles: true }));
            el.readOnly = estabaReadonly;
        }

        function capturarPeso(destinoSolicitado, silencioso) {
            if (typeof destinoSolicitado !== "string") {
                destinoSolicitado = "";
            }

            if (usarPesoBasculaAutomatico() && !state.isStable) {
                if (!silencioso) {
                    alert("El peso todavía no está estable. Espere a que el indicador marque ESTABLE o use captura manual autorizada.");
                }
                return false;
            }

            var peso = obtenerPesoActualBascula();

            if (peso <= 0) {
                alert("No hay peso válido de báscula para capturar.");
                return false;
            }

            var target = obtenerCampoDestinoPeso(destinoSolicitado);
            setPesoCampo(target, peso);

            var raw = byId("rawScale");
            if (raw) {
                raw.textContent = "PESO CAPTURADO > " + (target === "pesoEntrada" ? "ENTRADA" : "SALIDA") + " = " + peso.toFixed(2) + " kg\nEstado: " + (state.isStable ? "ESTABLE" : "MANUAL AUTORIZADO") + "\n" + now();
            }

            renderResumen();

            if (!silencioso) {
                var etiqueta = target === "pesoEntrada" ? "entrada" : "salida";
                console.log("Peso de " + etiqueta + " capturado desde báscula: " + peso.toFixed(2) + " kg");
            }

            return true;
        }

        async function guardarEntrada() {
            if (usarPesoBasculaAutomatico()) {
                var pesoCapturado = capturarPeso("entrada", true);

                if (!pesoCapturado) {
                    alert("No se puede guardar la entrada en 0 kg. Espere peso estable mayor a 0 kg o use captura manual autorizada.");
                    return;
                }
            }

            if (!validarBasico()) return;

            var r = getForm();

            if (r.pesoEntrada <= 0) {
                alert("No se puede guardar la entrada en 0 kg. Capture un peso de entrada válido mayor a 0 kg.");
                return;
            }

            if (r.capturaManual === "Sí" && !r.motivoManual) {
                alert("Capture el motivo de autorización manual.");
                return;
            }

            r.estatus = "PENDIENTE";
            r.fechaEntrada = now();
            r.fechaSalida = "";
            r.pesoSalida = 0;
            r.pesoNeto = 0;
            r.usuarioEntrada = usuario();
            r.usuarioSalida = "";

            var idx = -1;
            for (var i = 0; i < state.registros.length; i++) {
                if (state.registros[i].folio === r.folio) {
                    idx = i;
                    break;
                }
            }

            if (idx >= 0) {
                var anterior = state.registros[idx];
                r.movimientoGuid = anterior.movimientoGuid || r.movimientoGuid;
                r.movimientoId = anterior.movimientoId || r.movimientoId;
                r.folioServidor = anterior.folioServidor || r.folioServidor;
                r.terminalId = anterior.terminalId || r.terminalId;
                r.fechaCreacionLocal = anterior.fechaCreacionLocal || r.fechaCreacionLocal;
                asegurarDatosOffline(r);
                state.registros[idx] = r;
            } else {
                asegurarDatosOffline(r);
                state.registros.unshift(r);
            }

            r.syncStatus = "PENDIENTE_SYNC";
            guardarStorage();
            safeText("connectionText", "Guardando entrada en SQL Server...");

            var sincronizado = false;
            var errorSync = "";

            if (window.navigator.onLine) {
                try {
                    await sincronizarMovimientoServidor(r);
                    sincronizado = true;
                } catch (err) {
                    r.syncStatus = "ERROR_SYNC";
                    r.syncError = err && err.message ? err.message : String(err);
                    errorSync = r.syncError;
                    guardarStorage();
                }
            }

            var folioGuardado = folioVisible(r);
            addLog("Guardó entrada de báscula", folioGuardado);
            guardarStorage();

            setForm(null);
            renderAll();
            cambiarVista("pendientes");

            if (sincronizado) {
                safeText("connectionText", "Entrada guardada en SQL con folio oficial " + folioGuardado + ".");
                alert("Entrada guardada correctamente.\n\nFolio oficial: " + folioGuardado + "\n\nPara pesar la salida, seleccione este folio en Pendientes de salida.");
            } else {
                safeText("connectionText", "Entrada guardada localmente; queda pendiente de sincronizar con SQL.");
                alert("Entrada guardada temporalmente sin conexión.\n\nFolio provisional: " + r.folio +
                    "\nEl folio oficial se mostrará cuando SQL Server confirme la sincronización." +
                    (errorSync ? "\n\nDetalle: " + errorSync : ""));
                encolarSincronizacion(r);
            }
        }

        async function cerrarSalida() {
            if (!state.selectedFolio || !buscarRegistroActual()) {
                alert("Seleccione primero un folio pendiente desde Pendientes de salida.");
                cambiarVista("pendientes");
                return;
            }

            if (usarPesoBasculaAutomatico()) {
                var pesoCapturado = capturarPeso("salida", true);

                if (!pesoCapturado) {
                    alert("No se puede cerrar la salida en 0 kg. Espere peso estable mayor a 0 kg o use captura manual autorizada.");
                    return;
                }
            }

            if (!validarBasico()) return;

            var r = getForm();

            if (r.pesoEntrada <= 0) {
                alert("El folio seleccionado no tiene peso de entrada válido. Vuelva a seleccionar el pendiente o registre una entrada nueva.");
                return;
            }

            if (r.pesoSalida <= 0) {
                alert("No se puede cerrar la salida en 0 kg. Capture un peso de salida válido mayor a 0 kg.");
                return;
            }

            if (r.capturaManual === "Sí" && !r.motivoManual) {
                alert("Capture el motivo de autorización manual.");
                return;
            }

            var existente = buscarRegistroActual();
            if (!existente || !existente.movimientoGuid) {
                alert("El movimiento no tiene MovimientoGuid. Sin este identificador no se puede actualizar la misma fila de SQL.");
                return;
            }

            r.fechaEntrada = existente.fechaEntrada || now();
            r.fechaSalida = now();
            r.estatus = "CERRADO";
            r.pesoNeto = Math.abs(r.pesoEntrada - r.pesoSalida);
            r.usuarioEntrada = existente.usuarioEntrada || usuario();
            r.usuarioSalida = usuario();
            r.movimientoGuid = existente.movimientoGuid;
            r.movimientoId = existente.movimientoId || null;
            r.folioServidor = existente.folioServidor || "";
            r.terminalId = existente.terminalId || r.terminalId;
            r.fechaCreacionLocal = existente.fechaCreacionLocal || r.fechaCreacionLocal;
            asegurarDatosOffline(r);

            for (var i = 0; i < state.registros.length; i++) {
                if (state.registros[i].folio === r.folio) {
                    state.registros[i] = r;
                    break;
                }
            }

            state.selectedFolio = r.folio;
            r.syncStatus = "PENDIENTE_SYNC";
            guardarStorage();
            safeText("connectionText", "Cerrando salida en SQL Server...");

            try {
                if (!window.navigator.onLine) {
                    throw new Error("Sin conexión al servidor.");
                }

                await sincronizarMovimientoServidor(r);
                var folioOficial = folioVisible(r);

                addLog("Cerró salida y calculó peso neto", folioOficial);
                guardarStorage();
                renderAll();

                safeText("connectionText", "Salida cerrada en SQL con folio oficial " + folioOficial + ". Enviando ticket a la impresora...");

                await imprimirActual(r.folio);
            } catch (err) {
                r.syncStatus = "ERROR_SYNC";
                r.syncError = err && err.message ? err.message : String(err);
                guardarStorage();
                renderAll();
                encolarSincronizacion(r);

                safeText("connectionText", "Salida cerrada localmente; pendiente de sincronizar con SQL.");
                alert("La salida quedó guardada localmente, pero SQL Server todavía no la confirmó.\n\n" + r.syncError);
            }
        }

        function renderResumen() {
            var r = getForm();
            var estatusTexto = state.selectedFolio ? (r.estatus || "PENDIENTE") : "SIN GUARDAR";

            var registroActual = buscarRegistroActual();
            var folioActual = registroActual ? folioVisible(registroActual) : "Folio nuevo";
            safeText("folioPreview", folioActual);
            safeText("sumFolio", registroActual ? folioVisible(registroActual) : "Pendiente de guardar");
            safeText("sumEstatus", estatusTexto);
            safeText("sumEntrada", kg(r.pesoEntrada));
            safeText("sumSalida", kg(r.pesoSalida));
            safeText("sumNeto", kg(r.pesoNeto));
            safeText("sumCantidad", textoCantidad(r));
            safeText("sumTercero", r.tercero || "---");
            safeText("sumProducto", r.producto || "---");
            safeText("sumPlacas", r.placas || "---");

            var estatusEl = byId("sumEstatus");
            if (estatusEl) {
                var clase = "nuevo";
                if (r.estatus === "CERRADO") clase = "cerrado";
                else if (r.pesoEntrada > 0 || state.selectedFolio) clase = "pendiente";
                estatusEl.className = "estado-operativo " + clase;
            }

            actualizarBotonesOperacion();
        }

        function renderKpis() {
            var hoy = today();
            var movimientosHoy = 0;
            var pendientes = 0;
            var kgCerrados = 0;
            var ultimoOficial = null;

            for (var i = 0; i < state.registros.length; i++) {
                var r = state.registros[i];

                /*
                   Los indicadores principales muestran únicamente movimientos
                   confirmados por SQL Server. Los registros locales pendientes
                   continúan disponibles para sincronización, pero no alteran
                   los totales oficiales.
                */
                if (!r || !r.folioServidor) continue;

                if (!ultimoOficial) ultimoOficial = r;
                if ((r.fechaEntrada || "").indexOf(hoy) === 0) movimientosHoy++;
                if (r.estatus === "PENDIENTE") pendientes++;
                if (r.estatus === "CERRADO") kgCerrados += num(r.pesoNeto);
            }

            safeText("kpiMovimientos", movimientosHoy);
            safeText("kpiPendientes", pendientes);
            safeText("kpiKg", fmt0(kgCerrados));
            safeText("kpiFolio", ultimoOficial ? ultimoOficial.folioServidor : "---");
        }

        function clearTable(tbodyId) {
            var tbody = byId(tbodyId);
            if (!tbody) return null;

            while (tbody.firstChild) {
                tbody.removeChild(tbody.firstChild);
            }

            return tbody;
        }

        function addCell(row, text, className) {
            var td = document.createElement("td");
            td.textContent = text || "";
            if (className) td.className = className;
            row.appendChild(td);
            return td;
        }

        function badge(estatus) {
            var span = document.createElement("span");
            span.className = "badge-est " + (estatus === "CERRADO" ? "est-cerrado" : "est-pendiente");
            span.textContent = estatus || "PENDIENTE";
            return span;
        }


        var clientesSapCache = [];
        var articulosSapCache = [];
        var clientesSapTimer = null;
        var articulosSapTimer = null;

        function optionClienteSap(c) {
            return (c.codigoSap || "") + " - " + (c.nombre || "");
        }

        function optionArticuloSap(a) {
            return (a.productoCodigo || "") + " - " + (a.productoNombre || "");
        }


        function limpiarLookupPanel(id) {
            var panel = byId(id);
            if (panel) {
                panel.classList.remove("active");
                panel.innerHTML = "";
            }
        }

        function seleccionarClienteObj(c) {
            if (!c) return;

            safeValue("tercero", c.nombre || "");
            safeValue("codigoSap", c.codigoSap || "");
            limpiarLookupPanel("clientesSapPanel");

            renderResumen();
        }

    function seleccionarArticuloObj(a) {
        if (!a) return;

        safeValue("producto", a.productoNombre || "");
        safeValue("sku", a.productoCodigo || "");

        limpiarLookupPanel("articulosSapPanel");
        renderResumen();
    }

        function renderClientesSapPanel() {
            var panel = byId("clientesSapPanel");
            if (!panel) return;

            panel.innerHTML = "";

            if (!clientesSapCache.length) {
                panel.innerHTML = '<div class="lookup-empty">Sin clientes encontrados.</div>';
                panel.classList.add("active");
                return;
            }

            var table = document.createElement("table");

            var thead = document.createElement("thead");
            var trh = document.createElement("tr");

            ["Código", "Cliente", "Canal"].forEach(function (h) {
                var th = document.createElement("th");
                th.textContent = h;
                trh.appendChild(th);
            });

            thead.appendChild(trh);
            table.appendChild(thead);

            var tbody = document.createElement("tbody");

            for (var i = 0; i < clientesSapCache.length; i++) {
                var c = clientesSapCache[i];

                var tr = document.createElement("tr");
                tr.setAttribute("data-index", i);

                var td1 = document.createElement("td");
                td1.textContent = c.codigoSap || "";

                var td2 = document.createElement("td");
                td2.textContent = c.nombre || "";

                var td3 = document.createElement("td");
                td3.textContent = c.canal || "";

                tr.appendChild(td1);
                tr.appendChild(td2);
                tr.appendChild(td3);

                tr.addEventListener("click", function () {
                    var idx = Number(this.getAttribute("data-index"));
                    seleccionarClienteObj(clientesSapCache[idx]);
                });

                tbody.appendChild(tr);
            }

            table.appendChild(tbody);
            panel.appendChild(table);
            panel.classList.add("active");
        }

       function renderArticulosSapPanel() {
        var panel = byId("articulosSapPanel");
        if (!panel) return;

        panel.innerHTML = "";

        if (!articulosSapCache.length) {
            panel.innerHTML = '<div class="lookup-empty">Sin productos encontrados.</div>';
            panel.classList.add("active");
            return;
        }

        var table = document.createElement("table");

        var thead = document.createElement("thead");
        var trh = document.createElement("tr");

        ["Código", "Producto", "Acción"].forEach(function (h) {
            var th = document.createElement("th");
            th.textContent = h;
            trh.appendChild(th);
        });

        thead.appendChild(trh);
        table.appendChild(thead);

        var tbody = document.createElement("tbody");

        for (var i = 0; i < articulosSapCache.length; i++) {
            var a = articulosSapCache[i];

            var tr = document.createElement("tr");

            addCell(tr, a.productoCodigo || "");
            addCell(tr, a.productoNombre || "");

            var tdBtn = document.createElement("td");
            tdBtn.className = "center";

            var btn = document.createElement("button");
            btn.type = "button";
            btn.className = "small-btn";
            btn.textContent = "Usar";
            btn.setAttribute("data-index", i);

            btn.addEventListener("click", function () {
                var idx = Number(this.getAttribute("data-index"));
                seleccionarArticuloObj(articulosSapCache[idx]);
            });

            tdBtn.appendChild(btn);
            tr.appendChild(tdBtn);

            tbody.appendChild(tr);
        }

        table.appendChild(tbody);
        panel.appendChild(table);
        panel.classList.add("active");
    }

        async function buscarClientesSap() {
            var q = getValue("tercero").trim();

            if (q.length < 2) {
                limpiarLookupPanel("clientesSapPanel");
                return;
            }

            try {
                var response = await fetch("/BasculaCamionera/BuscarClientes?q=" + encodeURIComponent(q) + "&take=50", {
                    method: "GET",
                    headers: {
                        "Accept": "application/json"
                    }
                });

                var data = await response.json();

                if (!data.ok) {
                    console.warn(data.msg || "No se pudieron cargar clientes.");
                    return;
                }

                clientesSapCache = data.rows || [];
                renderClientesSapPanel();
            } catch (err) {
                console.warn("Error buscando clientes SAP", err);
            }
        }

        function seleccionarClienteSap() {
            var value = getValue("tercero").trim().toUpperCase();

            if (!value) return;

            var found = null;

            for (var i = 0; i < clientesSapCache.length; i++) {
                var c = clientesSapCache[i];

                if (
                    optionClienteSap(c).toUpperCase() === value ||
                    String(c.codigoSap || "").toUpperCase() === value ||
                    String(c.nombre || "").toUpperCase() === value
                ) {
                    found = c;
                    break;
                }
            }

            if (!found) return;

            safeValue("tercero", found.nombre || "");
            safeValue("codigoSap", found.codigoSap || "");

            renderResumen();
        }

        async function buscarArticulosSap() {
            abrirCatalogoProducto();
        }

        function seleccionarArticuloSap() {
            var value = getValue("producto").trim().toUpperCase();

            if (!value) return;

            var found = null;

            for (var i = 0; i < articulosSapCache.length; i++) {
                var a = articulosSapCache[i];

                if (
                    optionArticuloSap(a).toUpperCase() === value ||
                    String(a.productoCodigo || "").toUpperCase() === value ||
                    String(a.productoNombre || "").toUpperCase() === value
                ) {
                    found = a;
                    break;
                }
            }

            if (!found) return;

            safeValue("producto", found.productoNombre || "");
            safeValue("sku", found.productoCodigo || "");

            renderResumen();
        }


        function obtenerModoCatalogoTercero() {
            var tipo = (getValue("tipoMovimiento") || "").toLowerCase();

            if (tipo.indexOf("entrada") >= 0 && tipo.indexOf("proveedor") >= 0) {
                return {
                    tipo: "proveedor",
                    titulo: "Catálogo de proveedores",
                    label: "Proveedor",
                    placeholder: "Buscar proveedor por código o nombre",
                    urls: [
                        "/BasculaCamionera/BuscarProveedores",
                        "/BasculaCamionera/BuscarClientes?tipo=Proveedor"
                    ]
                };
            }

            if (tipo.indexOf("salida") >= 0 && tipo.indexOf("cliente") >= 0) {
                return {
                    tipo: "cliente",
                    titulo: "Catálogo de clientes",
                    label: "Cliente",
                    placeholder: "Buscar cliente por código o nombre",
                    urls: [
                        "/BasculaCamionera/BuscarClientes",
                        "/BasculaCamionera/BuscarClientes?tipo=Cliente"
                    ]
                };
            }

            return {
                tipo: "tercero",
                titulo: "Catálogo de terceros",
                label: "Proveedor / Cliente",
                placeholder: "Buscar proveedor o cliente por código o nombre",
                urls: [
                    "/BasculaCamionera/BuscarClientes",
                    "/BasculaCamionera/BuscarProveedores"
                ]
            };
        }

        function actualizarTerceroPorMovimiento(limpiarSiCambio) {
            var modo = obtenerModoCatalogoTercero();
            var anterior = state.catalogoTipoActual;
            state.catalogoTipoActual = modo.tipo;

            safeText("terceroLabel", modo.label);

            var tercero = byId("tercero");
            if (tercero) tercero.placeholder = modo.placeholder;

            var btn = byId("btnBuscarClientes");
            if (btn) btn.textContent = "Catálogo";

            if (limpiarSiCambio && anterior && anterior !== modo.tipo) {
                safeValue("tercero", "");
                safeValue("codigoSap", "");
                state.terceroCatalogoCache = [];
                limpiarLookupPanel("clientesSapPanel");
            }
        }

        function normalizarTerceroCatalogo(x) {
            x = x || {};

            return {
                codigo: x.codigoSap || x.CodigoSap || x.codigo || x.Codigo || x.cardCode || x.CardCode || x.id || x.Id || "",
                nombre: x.nombre || x.Nombre || x.razonSocial || x.RazonSocial || x.cardName || x.CardName || x.name || x.Name || "",
                canal: x.canal || x.Canal || x.tipo || x.Tipo || x.grupo || x.Grupo || x.tipoTercero || x.TipoTercero || "",
                raw: x
            };
        }

        function appendQueryToUrl(url, q) {
            var sep = url.indexOf("?") >= 0 ? "&" : "?";
            return url + sep + "q=" + encodeURIComponent(q || "") + "&take=80";
        }

        function obtenerTextoFechaCatalogoOffline() {
            var meta = state.catalogosOfflineMeta;
            if (!meta) {
                try {
                    meta = JSON.parse(localStorage.getItem(CATALOGOS_OFFLINE_META) || "null");
                } catch (e) {
                    meta = null;
                }
            }

            if (!meta || !meta.fecha) return "sin fecha";

            try {
                return new Date(meta.fecha).toLocaleString("es-MX");
            } catch (e) {
                return "sin fecha";
            }
        }

        async function consultarCatalogoTercero(q) {
            var modo = obtenerModoCatalogoTercero();
            var errores = [];
            var tipoOffline = modo.tipo === "proveedor" ? "proveedores" : "clientes";

            if (window.navigator.onLine) {
                for (var i = 0; i < modo.urls.length; i++) {
                    try {
                        var response = await fetch(appendQueryToUrl(modo.urls[i], q), {
                            method: "GET",
                            headers: { "Accept": "application/json" },
                            cache: "no-store"
                        });

                        if (!response.ok) {
                            errores.push("HTTP " + response.status);
                            continue;
                        }

                        var data = await response.json();
                        if (data && data.ok === false) {
                            errores.push(data.msg || "Catálogo no disponible");
                            continue;
                        }

                        var rowsOnline = obtenerRowsRespuestaCatalogo(data).map(normalizarTerceroCatalogo).filter(function (r) {
                            return r.codigo || r.nombre;
                        });

                        if (rowsOnline.length) {
                            return rowsOnline;
                        }
                    } catch (err) {
                        errores.push(err.message);
                    }
                }
            }

            var rowsOffline = await leerCatalogoOffline(tipoOffline);

            if (!rowsOffline.length && tipoOffline === "proveedores") {
                rowsOffline = await leerCatalogoOffline("clientes");
            }

            rowsOffline = filtrarCatalogoOffline(rowsOffline, q, ["codigo", "nombre", "canal"], 80);

            if (rowsOffline.length) {
                safeText("catalogModalSubtitle", "Catálogo disponible. Última actualización: " + obtenerTextoFechaCatalogoOffline());
                return rowsOffline;
            }

            console.warn("No se pudo consultar catálogo de tercero", errores);
            return [];
        }

        function configurarEncabezadoCatalogoModal(tipo) {
            var tbody = byId("catalogModalBody");
            if (!tbody) return;

            var table = tbody.closest("table");
            if (!table) return;

            var thead = table.querySelector("thead");
            if (!thead) return;

            if (tipo === "producto") {
                thead.innerHTML =
                    '<tr>' +
                    '<th style="width:150px;">Código</th>' +
                    '<th>Producto</th>' +
                    '<th style="width:120px;" class="center">Acción</th>' +
                    '</tr>';
            } else {
                thead.innerHTML =
                    '<tr>' +
                    '<th style="width:150px;">Código</th>' +
                    '<th>Nombre / Razón social</th>' +
                    '<th style="width:150px;">Tipo / Canal</th>' +
                    '<th style="width:120px;" class="center">Acción</th>' +
                    '</tr>';
            }
        }

        function abrirModalCatalogo() {
            var modal = byId("catalogModalBackdrop");
            var search = byId("catalogSearch");

            if (modal) {
                modal.classList.add("active");
                modal.setAttribute("aria-hidden", "false");
            }

            setTimeout(function () {
                if (search) {
                    search.focus();
                    search.select();
                }
            }, 80);
        }

        function abrirCatalogoTercero() {
            actualizarTerceroPorMovimiento(false);
            state.catalogModalMode = "tercero";
            configurarEncabezadoCatalogoModal("tercero");

            var modo = obtenerModoCatalogoTercero();
            safeText("catalogModalTitle", modo.titulo);
            safeText("catalogModalSubtitle", "Seleccione " + modo.label.toLowerCase() + " para este movimiento.");

            var search = byId("catalogSearch");
            if (search) {
                search.value = getValue("tercero");
                search.placeholder = modo.placeholder;
            }

            abrirModalCatalogo();
            buscarCatalogoTerceroModal();
        }

        function abrirCatalogoProducto() {
            state.catalogModalMode = "producto";
            configurarEncabezadoCatalogoModal("producto");

            safeText("catalogModalTitle", "Catálogo de productos");
            safeText("catalogModalSubtitle", "Seleccione el producto para este movimiento.");

            var search = byId("catalogSearch");
            if (search) {
                search.value = getValue("producto");
                search.placeholder = "Buscar producto por código o nombre";
            }

            abrirModalCatalogo();
            buscarCatalogoProductoModal();
        }

        function cerrarCatalogoTercero() {
            var modal = byId("catalogModalBackdrop");
            if (!modal) return;

            modal.classList.remove("active");
            modal.setAttribute("aria-hidden", "true");
        }

        function renderCatalogoTerceroModal(rows) {
            var tbody = byId("catalogModalBody");
            if (!tbody) return;

            configurarEncabezadoCatalogoModal("tercero");
            tbody.innerHTML = "";

            if (!rows || !rows.length) {
                tbody.innerHTML = '<tr><td colspan="4" class="catalog-empty">Sin resultados en catálogo local/servidor. Intente con código, nombre o razón social.</td></tr>';
                return;
            }

            for (var i = 0; i < rows.length; i++) {
                var r = rows[i];
                var tr = document.createElement("tr");
                tr.innerHTML =
                    '<td>' + escapeHtml(r.codigo) + '</td>' +
                    '<td><strong>' + escapeHtml(r.nombre) + '</strong></td>' +
                    '<td>' + escapeHtml(r.canal) + '</td>' +
                    '<td class="center"><button type="button" class="catalog-select-btn" data-index="' + i + '">Seleccionar</button></td>';
                tbody.appendChild(tr);
            }
        }

        function normalizarProductoCatalogo(x) {
            x = x || {};

            return {
                codigo: x.productoCodigo || x.ProductoCodigo || x.codigo || x.Codigo || x.itemCode || x.ItemCode || "",
                nombre: x.productoNombre || x.ProductoNombre || x.nombre || x.Nombre || x.itemName || x.ItemName || "",
                raw: x
            };
        }

        async function consultarCatalogoProducto(q) {
            var errores = [];
            var urls = [
                "/BasculaCamionera/BuscarArticulos",
                "/BasculaCamionera/BuscarProductos"
            ];

            if (window.navigator.onLine) {
                for (var i = 0; i < urls.length; i++) {
                    try {
                        var response = await fetch(appendQueryToUrl(urls[i], q), {
                            method: "GET",
                            headers: { "Accept": "application/json" },
                            cache: "no-store"
                        });

                        if (!response.ok) {
                            errores.push("HTTP " + response.status);
                            continue;
                        }

                        var data = await response.json();
                        if (data && data.ok === false) {
                            errores.push(data.msg || "Catálogo de productos no disponible");
                            continue;
                        }

                        var rowsOnline = obtenerRowsRespuestaCatalogo(data).map(normalizarProductoCatalogo).filter(function (r) {
                            return r.codigo || r.nombre;
                        });

                        if (rowsOnline.length) {
                            return rowsOnline;
                        }
                    } catch (err) {
                        errores.push(err.message);
                    }
                }
            }

            var rowsOffline = await leerCatalogoOffline("productos");
            rowsOffline = filtrarCatalogoOffline(rowsOffline, q, ["codigo", "nombre"], 80);

            if (rowsOffline.length) {
                safeText("catalogModalSubtitle", "Catálogo disponible. Última actualización: " + obtenerTextoFechaCatalogoOffline());
                return rowsOffline;
            }

            console.warn("No se pudo consultar catálogo de productos", errores);
            return [];
        }

        function renderCatalogoProductoModal(rows) {
            var tbody = byId("catalogModalBody");
            if (!tbody) return;

            configurarEncabezadoCatalogoModal("producto");
            tbody.innerHTML = "";

            if (!rows || !rows.length) {
                tbody.innerHTML = '<tr><td colspan="3" class="catalog-empty">Sin productos en catálogo local/servidor. Intente con código o nombre.</td></tr>';
                return;
            }

            for (var i = 0; i < rows.length; i++) {
                var r = rows[i];
                var tr = document.createElement("tr");
                tr.innerHTML =
                    '<td>' + escapeHtml(r.codigo) + '</td>' +
                    '<td><strong>' + escapeHtml(r.nombre) + '</strong></td>' +
                    '<td class="center"><button type="button" class="catalog-select-btn" data-index="' + i + '">Seleccionar</button></td>';
                tbody.appendChild(tr);
            }
        }

        async function buscarCatalogoTerceroModal() {
            var q = getValue("catalogSearch").trim();
            var tbody = byId("catalogModalBody");

            if (tbody) {
                tbody.innerHTML = '<tr><td colspan="4" class="catalog-empty">Consultando catálogo...</td></tr>';
            }

            if (q.length < 2 && state.catalogosOfflineReady) {
                if (tbody) {
                    tbody.innerHTML = '<tr><td colspan="4" class="catalog-empty">Mostrando catálogo disponible...</td></tr>';
                }
            } else if (q.length < 2) {
                if (tbody) {
                    tbody.innerHTML = '<tr><td colspan="4" class="catalog-empty">Cargando primeros registros del catálogo...</td></tr>';
                }
            }

            state.terceroCatalogoCache = await consultarCatalogoTercero(q);
            renderCatalogoTerceroModal(state.terceroCatalogoCache);
        }

        async function buscarCatalogoProductoModal() {
            var q = getValue("catalogSearch").trim();
            var tbody = byId("catalogModalBody");

            if (tbody) {
                tbody.innerHTML = '<tr><td colspan="3" class="catalog-empty">Consultando catálogo de productos...</td></tr>';
            }

            if (q.length < 2 && state.catalogosOfflineReady) {
                if (tbody) {
                    tbody.innerHTML = '<tr><td colspan="3" class="catalog-empty">Mostrando catálogo disponible...</td></tr>';
                }
            } else if (q.length < 2) {
                if (tbody) {
                    tbody.innerHTML = '<tr><td colspan="3" class="catalog-empty">Cargando primeros productos del catálogo...</td></tr>';
                }
            }

            state.productoCatalogoCache = await consultarCatalogoProducto(q);
            renderCatalogoProductoModal(state.productoCatalogoCache);
        }

        function buscarCatalogoModal() {
            if (state.catalogModalMode === "producto") {
                buscarCatalogoProductoModal();
            } else {
                buscarCatalogoTerceroModal();
            }
        }

        function seleccionarTerceroCatalogo(index) {
            var item = state.terceroCatalogoCache[Number(index)];
            if (!item) return;

            safeValue("tercero", item.nombre || "");
            safeValue("codigoSap", item.codigo || "");
            cerrarCatalogoTercero();
            limpiarLookupPanel("clientesSapPanel");
            renderResumen();
        }

        function seleccionarProductoCatalogo(index) {
            var item = state.productoCatalogoCache[Number(index)];
            if (!item) return;

            safeValue("producto", item.nombre || "");
            safeValue("sku", item.codigo || "");
            cerrarCatalogoTercero();
            limpiarLookupPanel("articulosSapPanel");
            renderResumen();
        }

        function seleccionarCatalogoModal(index) {
            if (state.catalogModalMode === "producto") {
                seleccionarProductoCatalogo(index);
            } else {
                seleccionarTerceroCatalogo(index);
            }
        }

        async function buscarClientesSap() {
            abrirCatalogoTercero();
        }

        function seleccionarClienteSap() {
            renderResumen();
        }

        async function probarConexionTcp() {
            var host = getValue("scaleHost").trim();
            var port = Number(getValue("scalePort") || 0);

            if (!host) {
                alert("Capture la IP de la báscula.");
                return;
            }

            if (port <= 0) {
                alert("Capture el puerto TCP de la báscula.");
                return;
            }

            safeText("connectionText", "Probando conexión TCP...");

            var dot = byId("connectionDot");
            if (dot) dot.classList.remove("ok");

            try {
                var response = await fetch("/BasculaCamionera/Tcp/ProbarConexion", {
                    method: "POST",
                    headers: {
                        "Content-Type": "application/json",
                        "Accept": "application/json"
                    },
                    body: JSON.stringify({
                        host: host,
                        port: port,
                        timeoutMs: 3000
                    })
                });

                var data = await response.json();

                if (data.ok && data.connected) {
                    state.connected = true;
                    persistirConfigBascula(true);

                    safeText("connectionText", data.msg || ("Báscula activa: " + host + ":" + port));

                    if (dot) dot.classList.add("ok");

                    var raw = byId("rawScale");
                    if (raw) {
                        raw.textContent = "Conexión TCP exitosa.\nConfiguración guardada como ACTIVA.\nBáscula: " + host + ":" + port + "\nImpresora: " + (getValue("printerName") || "Sin impresora") + "\n" + now();
                    }

                    addLog("Activó conexión TCP de báscula", "CONFIGURACIÓN");
                    alert("Conexión exitosa. La báscula quedó activa y guardada en esta terminal.");
                    pollPesoBasculaActivo();
                } else {
                    safeText("connectionText", data.msg || "No se pudo conectar. La configuración no se borró.");

                    if (dot) dot.classList.remove("ok");

                    alert((data.msg || "No se pudo conectar a la báscula.") + "\n\nLa configuración guardada no se eliminó. Solo se desactiva si presionas Desconectar.");
                }
            } catch (err) {
                safeText("connectionText", "Error probando TCP. La configuración no se borró.");

                if (dot) dot.classList.remove("ok");

                alert("Error probando conexión TCP: " + err.message + "\n\nLa configuración guardada no se eliminó. Solo se desactiva si presionas Desconectar.");
            }
        }

        async function leerPesoTcpUnaVez() {
            var host = getValue("scaleHost").trim();
            var port = Number(getValue("scalePort") || 0);
            var command = getValue("scaleCommand") || "";

            if (!host || port <= 0) {
                simularPeso();
                return { ok: true, pesoKg: obtenerPesoActualBascula(), raw: "SIMULADO" };
            }

            var response = await fetch("/BasculaCamionera/Tcp/LeerPeso", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "Accept": "application/json"
                },
                body: JSON.stringify({
                    host: host,
                    port: port,
                    command: command,
                    timeoutMs: 3000
                })
            });

            return await response.json();
        }

        function sleep(ms) {
            return new Promise(function (resolve) { setTimeout(resolve, ms); });
        }

        async function leerPesoTcp() {
            var host = getValue("scaleHost").trim();
            var port = Number(getValue("scalePort") || 0);
            var intentos = Math.max(getStableSamples(), 6);

            resetEstabilizador();
            safeText("connectionText", "Leyendo y estabilizando peso...");

            try {
                for (var i = 0; i < intentos; i++) {
                    var data = await leerPesoTcpUnaVez();

                    if (data.ok && data.pesoKg !== null && data.pesoKg !== undefined) {
                        var pesoLeido = Number(data.pesoKg || 0);
                        var estable = actualizarPesoBascula(pesoLeido, (data.raw || "") + "\nIntento " + (i + 1) + " de " + intentos);

                        var dot = byId("connectionDot");
                        if (dot) dot.classList.add("ok");

                        if (estable) {
                            safeText("connectionText", "Peso estable leído correctamente");
                            capturarPeso("", true);
                            return;
                        }

                        await sleep(host && port > 0 ? 450 : 80);
                    } else {
                        safeText("connectionText", data.msg || "Sin peso real recibido");
                        alert(data.msg || "No se recibió peso real.");
                        return;
                    }
                }

                safeText("connectionText", "Peso no estable, intente leer de nuevo");
                alert("La báscula todavía no está estable. Verifique que la unidad esté detenida y vuelva a leer el peso.");
            } catch (err) {
                safeText("connectionText", "Error leyendo peso real");
                alert("Error leyendo peso real: " + err.message);
            }
        }


        async function pollPesoBasculaActivo() {
            if (!state.connected || state.scalePolling) return;

            var host = getValue("scaleHost").trim();
            var port = Number(getValue("scalePort") || 0);

            if (!host || port <= 0) {
                state.connected = false;
                persistirConfigBascula(false);
                safeText("connectionText", "Báscula sin IP/puerto configurado");
                return;
            }

            state.scalePolling = true;

            try {
                var data = await leerPesoTcpUnaVez();

                if (data && data.ok && data.pesoKg !== null && data.pesoKg !== undefined) {
                    var pesoLeido = Number(data.pesoKg || 0);
                    actualizarPesoBascula(pesoLeido, data.raw || "LECTURA AUTOMÁTICA TCP");

                    var dot = byId("connectionDot");
                    if (dot) dot.classList.add("ok");
                } else {
                    safeText("connectionText", (data && data.msg) ? data.msg : "Sin lectura TCP. Configuración sigue activa.");

                    var dotFail = byId("connectionDot");
                    if (dotFail) dotFail.classList.remove("ok");
                }
            } catch (err) {
                safeText("connectionText", "Sin respuesta TCP. Configuración sigue activa.");

                var dotErr = byId("connectionDot");
                if (dotErr) dotErr.classList.remove("ok");

                var raw = byId("rawScale");
                if (raw) {
                    raw.textContent = "Error de lectura automática TCP.\n" + err.message + "\nLa configuración sigue guardada y activa hasta presionar Desconectar.\n" + now();
                }
            } finally {
                state.scalePolling = false;
            }
        }

        async function detectarImpresorasLocales() {
        var cont = byId("printerList");

        if (!cont) return;

        cont.innerHTML = "Buscando impresoras locales...";

        try {
            var response = await fetch("/BasculaCamionera/ImpresorasLocales", {
                method: "GET",
                headers: {
                    "Accept": "application/json"
                }
            });

            var data = await response.json();

            if (!data.ok) {
                cont.innerHTML = data.msg || "No se pudieron detectar impresoras.";
                alert(data.msg || "No se pudieron detectar impresoras.");
                return;
            }

            cont.innerHTML = "";

            if (!data.rows || data.rows.length === 0) {
                cont.innerHTML = "No se encontraron impresoras instaladas.";
                return;
            }

            for (var i = 0; i < data.rows.length; i++) {
                var p = data.rows[i];

                var row = document.createElement("div");
                row.style.display = "flex";
                row.style.justifyContent = "space-between";
                row.style.alignItems = "center";
                row.style.gap = "8px";
                row.style.padding = "7px";
                row.style.borderBottom = "1px solid #d8bcbc";

                var name = document.createElement("span");
                name.textContent = p.name + (p.isDefault ? " · Predeterminada" : "");

                var btn = document.createElement("button");
                btn.type = "button";
                btn.className = "small-btn";
                btn.textContent = "Usar";
                btn.setAttribute("data-printer", p.name);

                btn.addEventListener("click", function () {
                    var selected = this.getAttribute("data-printer");

                    safeValue("printerName", selected);
                    persistirConfigBascula(state.connected);

                    alert("Impresora seleccionada y guardada: " + selected);
                });

                row.appendChild(name);
                row.appendChild(btn);

                cont.appendChild(row);
            }

            if (data.defaultPrinter && !getValue("printerName")) {
                safeValue("printerName", data.defaultPrinter);
                persistirConfigBascula(state.connected);
            }
        } catch (err) {
            cont.innerHTML = "Error detectando impresoras: " + err.message;
            alert("Error detectando impresoras: " + err.message);
        }
    }

        function renderPendientes() {
            var tbody = clearTable("pendingTable");
            if (!tbody) return;

            var count = 0;

            for (var i = 0; i < state.registros.length; i++) {
                var r = state.registros[i];

                if (r.estatus !== "PENDIENTE") continue;

                count++;

                var tr = document.createElement("tr");

                var tdFolio = document.createElement("td");
                var a = document.createElement("a");
                a.href = "#";
                a.className = "ver-pesaje";
                a.setAttribute("data-folio", r.folio);
                a.textContent = folioVisible(r);
                tdFolio.appendChild(a);
                tr.appendChild(tdFolio);

                addCell(tr, r.fechaEntrada);
                addCell(tr, r.tipoMovimiento);
                addCell(tr, r.tercero);
                addCell(tr, r.producto);
                addCell(tr, textoCantidad(r));
                addCell(tr, r.placas);
                addCell(tr, kg(r.pesoEntrada), "num");

                var tdAccion = document.createElement("td");
                tdAccion.className = "center";

                var btn = document.createElement("button");
                btn.type = "button";
                btn.className = "small-btn btn-cargar";
                btn.setAttribute("data-folio", r.folio);
                btn.textContent = "Cerrar salida";

                tdAccion.appendChild(btn);
                tr.appendChild(tdAccion);

                tbody.appendChild(tr);
            }

            if (count === 0) {
                var trEmpty = document.createElement("tr");
                var td = addCell(trEmpty, "Sin pendientes de salida.", "center text-muted");
                td.colSpan = 9;
                tbody.appendChild(trEmpty);
            }
        }

        function renderHistorial() {
            var tbody = clearTable("historyTable");
            if (!tbody) return;

            var rows = getHistorialFiltrado();

            for (var i = 0; i < rows.length; i++) {
                var r = rows[i];

                var tr = document.createElement("tr");

                var tdFolio = document.createElement("td");
                var a = document.createElement("a");
                a.href = "#";
                a.className = "ver-pesaje";
                a.setAttribute("data-folio", r.folio);
                a.textContent = folioVisible(r);
                tdFolio.appendChild(a);
                tr.appendChild(tdFolio);

                var tdEst = document.createElement("td");
                tdEst.appendChild(badge(r.estatus));
                tr.appendChild(tdEst);

                addCell(tr, r.fechaEntrada);
                addCell(tr, r.fechaSalida || "Pendiente");
                addCell(tr, r.tipoMovimiento);
                addCell(tr, r.clasificacion);
                addCell(tr, textoCantidad(r));
                addCell(tr, r.tercero);
                addCell(tr, r.sku);
                addCell(tr, kg(r.pesoEntrada), "num");
                addCell(tr, kg(r.pesoSalida), "num");
                addCell(tr, kg(r.pesoNeto), "num");
                addCell(tr, r.usuario);

                var tdTicket = document.createElement("td");
                tdTicket.className = "center";

                var btnTicket = document.createElement("button");
                btnTicket.type = "button";
                btnTicket.className = "small-btn btn-ticket";
                btnTicket.setAttribute("data-folio", r.folio);
                btnTicket.textContent = "Imprimir";

                tdTicket.appendChild(btnTicket);
                tr.appendChild(tdTicket);

                tbody.appendChild(tr);
            }

            if (rows.length === 0) {
                var trEmpty = document.createElement("tr");
                var td = addCell(trEmpty, "Sin registros para los filtros seleccionados.", "center text-muted");
                td.colSpan = 14;
                tbody.appendChild(trEmpty);
            }
        }

        function renderBitacora() {
            var tbody = clearTable("logTable");
            if (!tbody) return;

            if (state.bitacora.length === 0) {
                var trEmpty = document.createElement("tr");
                var td = addCell(trEmpty, "Sin bitácora todavía.", "center text-muted");
                td.colSpan = 4;
                tbody.appendChild(trEmpty);
                return;
            }

            for (var i = 0; i < state.bitacora.length; i++) {
                var l = state.bitacora[i];
                var tr = document.createElement("tr");

                addCell(tr, l.fecha);
                addCell(tr, l.usuario);
                addCell(tr, l.accion);
                addCell(tr, l.folio);

                tbody.appendChild(tr);
            }
        }

        function renderAll() {
            renderResumen();
            renderKpis();
            renderPendientes();
            renderHistorial();
            renderBitacora();
        }

        function cargarRegistro(folio) {
            var r = null;

            for (var i = 0; i < state.registros.length; i++) {
                if (state.registros[i].folio === folio) {
                    r = state.registros[i];
                    break;
                }
            }

            if (!r) return;

            setForm(r);
            cambiarVista("registro");
        }

        function cambiarVista(view) {
            var panels = document.querySelectorAll(".view-panel, .view");
            toArray(panels).forEach(function (x) {
                x.classList.remove("active");
            });

            var panel = byId("view-" + view);
            if (panel) panel.classList.add("active");

            var tabs = document.querySelectorAll(".tab, .nav-item");
            toArray(tabs).forEach(function (x) {
                x.classList.toggle("active", x.getAttribute("data-view") === view);
            });
        }

        function setPrintText(id, value) {
            var el = byId(id);

            if (!el) return;

            if (value === null || value === undefined || value === "") {
                el.textContent = "";
            } else {
                el.textContent = String(value);
            }
        }

        function llenarTicketImpresion(r) {
            var folio = folioVisible(r) || "SIN-GUARDAR";
            var estatus = state.selectedFolio ? (r.estatus || "EDITANDO") : "Vista previa";

            setPrintText("pFolio", folio);
            setPrintText("pEstatus", estatus);
            setPrintText("pMovimiento", r.tipoMovimiento || "Entrada proveedor");
            setPrintText("pClasificacion", r.clasificacion || "Ganado en pie");
            setPrintText("pCantidad", textoCantidad(r));

            setPrintText("pFechaEntrada", r.fechaEntrada || now());
            setPrintText("pFechaSalida", r.fechaSalida || "Pendiente");

            setPrintText("pTercero", r.tercero || "");
            setPrintText("pCodigoSap", r.codigoSap || "");
            setPrintText("pProducto", r.producto || "");
            setPrintText("pSku", r.sku || "");
            setPrintText("pDocumento", r.documento || "");

            setPrintText("pPlacas", r.placas || "");
            setPrintText("pChofer", r.chofer || "");
            setPrintText("pOrigen", r.origen || "");
            setPrintText("pDestino", r.destino || "");
            setPrintText("pCondicion", r.condicion || "");

            setPrintText("pEntrada", kg(r.pesoEntrada || 0));
            setPrintText("pSalida", r.pesoSalida ? kg(r.pesoSalida) : "Pendiente");
            setPrintText("pNeto", kg(r.pesoNeto || 0));

            setPrintText("pCapturaManual", r.capturaManual || "No");
            setPrintText("pMotivoManual", r.motivoManual || "");

            setPrintText("pUsuarioEntrada", r.usuarioEntrada || r.usuario || "Supervisor Báscula");
            setPrintText("pUsuarioSalida", r.usuarioSalida || r.usuario || "Supervisor Báscula");

            setPrintText("pObservaciones", r.observaciones || "");
            setPrintText("pFechaImpresion", now());
        }

        function obtenerRegistroParaImprimir(folioSolicitado) {
            var folio = typeof folioSolicitado === "string" ? folioSolicitado : "";

            if (!folio && state.selectedFolio) {
                folio = state.selectedFolio;
            }

            if (folio) {
                for (var i = 0; i < state.registros.length; i++) {
                    if (state.registros[i].folio === folio) {
                        return state.registros[i];
                    }
                }
            }

            var actual = getForm();
            actual.folio = actual.folio || "SIN-GUARDAR";
            actual.estatus = actual.estatus || (actual.pesoSalida > 0 ? "CERRADO" : "PENDIENTE");
            actual.fechaEntrada = actual.fechaEntrada || now();
            actual.fechaSalida = actual.fechaSalida || (actual.pesoSalida > 0 ? now() : null);
            actual.usuarioEntrada = actual.usuarioEntrada || usuario();
            actual.usuarioSalida = actual.usuarioSalida || (actual.pesoSalida > 0 ? usuario() : "");

            return actual;
        }

        async function imprimirActual(folioSolicitado) {
            var r = obtenerRegistroParaImprimir(folioSolicitado);
            var printerName = getValue("printerName").trim();

            if (!printerName) {
                alert("Seleccione y guarde primero la impresora en Configuración. Ejemplo: EPSON TM-T20IV Receipt.");
                cambiarVista("configuracion");
                return false;
            }

            llenarTicketImpresion(r);
            safeText("connectionText", "Enviando ticket directamente a " + printerName + "...");

            try {
                var response = await fetch(PRINT_ENDPOINT, {
                    method: "POST",
                    headers: {
                        "Content-Type": "application/json",
                        "Accept": "application/json"
                    },
                    body: JSON.stringify({
                        printerName: printerName,
                        copias: 2,
                        movimiento: crearPayloadSync(r)
                    })
                });

                var data = null;

                try {
                    data = await response.json();
                } catch (jsonError) {
                    data = null;
                }

                if (!response.ok || !data || data.ok !== true) {
                    throw new Error(data && data.msg ? data.msg : ("HTTP " + response.status));
                }

                var folio = folioVisible(r);
                addLog("Imprimió ticket directo en " + printerName, folio);
                guardarStorage();
                renderBitacora();

                safeText("connectionText", data.msg || ("Ticket enviado a " + printerName + "."));
                return true;
            } catch (err) {
                var detalle = err && err.message ? err.message : String(err);
                safeText("connectionText", "No se pudo imprimir el ticket: " + detalle);
                alert("El pesaje quedó guardado, pero el ticket no pudo imprimirse.\n\n" + detalle);
                return false;
            }
        }

        /*
         * Intercepta los controles de impresión en fase de captura. Esto evita que
         * un manejador antiguo, todavía residente por Hot Reload o navegación
         * parcial, alcance a ejecutar window.print() y abra la vista previa.
         */
        function capturarClickImpresionDirecta(ev) {
            var control = ev.target && ev.target.closest
                ? ev.target.closest("#btnImprimirActual, #btnImprimirActual2, #btnTestPrint, .btn-ticket")
                : null;

            if (!control) return;

            ev.preventDefault();
            ev.stopPropagation();
            ev.stopImmediatePropagation();

            var folio = control.classList.contains("btn-ticket")
                ? (control.getAttribute("data-folio") || "")
                : "";

            imprimirActual(folio).catch(function (err) {
                var detalle = err && err.message ? err.message : String(err);
                safeText("connectionText", "Error de impresión directa: " + detalle);
                alert("No se pudo enviar el ticket a la impresora.\n\n" + detalle);
            });
        }

        if (window.__BASCULA_PRINT_CAPTURE_HANDLER__) {
            document.removeEventListener(
                "click",
                window.__BASCULA_PRINT_CAPTURE_HANDLER__,
                true
            );
        }

        window.__BASCULA_PRINT_CAPTURE_HANDLER__ = capturarClickImpresionDirecta;
        document.addEventListener("click", capturarClickImpresionDirecta, true);

        function exportCsv() {
            exportExcelHistorial();
        }

        function exportExcelHistorial() {
            var rows = getHistorialFiltrado();
            var cols = [
                ["folioServidor", "Folio oficial"],
                ["folio", "Folio local"],
                ["estatus", "Estatus"],
                ["tipoMovimiento", "Tipo movimiento"],
                ["clasificacion", "Clasificación"],
                ["cantidad", "Cantidad"],
                ["tercero", "Proveedor / Cliente"],
                ["codigoSap", "Código SAP"],
                ["placas", "Placas"],
                ["producto", "Producto"],
                ["sku", "SKU / Lote"],
                ["documento", "Documento"],
                ["chofer", "Chofer"],
                ["origen", "Origen"],
                ["destino", "Destino"],
                ["condicion", "Condición"],
                ["pesoEntrada", "Peso entrada kg"],
                ["pesoSalida", "Peso salida kg"],
                ["pesoNeto", "Peso neto kg"],
                ["fechaEntrada", "Fecha entrada"],
                ["fechaSalida", "Fecha salida"],
                ["capturaManual", "Captura manual"],
                ["motivoManual", "Motivo manual"],
                ["usuario", "Usuario"]
            ];

            /*
               IMPORTANTE:
               No se genera markup completo dentro del bloque de JavaScript,
               porque Razor/navegador puede interpretar ciertos cierres
               como markup real y terminar mostrando el JavaScript en pantalla.
               Este export genera un archivo .xls compatible con Excel usando texto tabulado UTF-8.
            */
            function excelCell(value) {
                if (value === null || value === undefined) return "";

                var text = String(value)
                    .replace(/\r\n/g, " ")
                    .replace(/\n/g, " ")
                    .replace(/\r/g, " ")
                    .replace(/\t/g, " ")
                    .trim();

                var firstChar = text.charAt(0);
                if (firstChar === "=" || firstChar === "+" || firstChar === "-" || firstChar === String.fromCharCode(64)) {
                    text = "'" + text;
                }

                return text;
            }

            var lines = [];
            var headers = [];

            for (var c = 0; c < cols.length; c++) {
                headers.push(excelCell(cols[c][1]));
            }

            lines.push(headers.join("\t"));

            for (var i = 0; i < rows.length; i++) {
                var r = rows[i];
                var row = [];

                for (var j = 0; j < cols.length; j++) {
                    var key = cols[j][0];
                    var value = key === "folioServidor" ? folioVisible(r) : r[key];
                    row.push(excelCell(value));
                }

                lines.push(row.join("\t"));
            }

            var blob = new Blob(["\ufeff" + lines.join("\r\n")], {
                type: "application/vnd.ms-excel;charset=utf-8"
            });

            var a = document.createElement("a");
            var desde = getValue("filterFechaDesde") || "inicio";
            var hasta = getValue("filterFechaHasta") || fechaInputDesdeDate(new Date());
            var url = URL.createObjectURL(blob);

            a.href = url;
            a.download = "historial_bascula_" + desde + "_a_" + hasta + ".xls";
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);

            setTimeout(function () {
                URL.revokeObjectURL(url);
            }, 1000);
        }

        async function limpiarDemo() {
            if (!confirm("Esto limpia únicamente la caché local de esta computadora. NO borra movimientos de SQL ni reinicia el folio oficial. ¿Continuar?")) return;

            state.registros = [];
            state.bitacora = [];
            state.selectedFolio = null;

            guardarStorage();
            setForm(null);
            renderAll();
            await cargarMovimientosServidor();
        }

        function guardarConfig() {
            var host = getValue("scaleHost").trim();
            var port = Number(getValue("scalePort") || 0);
            var printer = getValue("printerName").trim();

            var activarBascula = !!host && port > 0;
            state.connected = activarBascula;

            var cfg = persistirConfigBascula(activarBascula);

            var dot = byId("connectionDot");
            if (dot) dot.classList.toggle("ok", activarBascula);

            if (activarBascula) {
                safeText("connectionText", "Báscula configurada activa: " + host + ":" + port);
            } else {
                safeText("connectionText", "Configuración guardada / báscula inactiva");
            }

            var raw = byId("rawScale");
            if (raw && cfg) {
                raw.textContent =
                    "Configuración guardada en esta terminal.\n" +
                    "Báscula activa: " + (cfg.scaleActive ? "SÍ" : "NO") + "\n" +
                    "IP: " + (cfg.scaleHost || "Sin IP") + "\n" +
                    "Puerto: " + (cfg.scalePort || "Sin puerto") + "\n" +
                    "Impresora: " + (cfg.printerName || "Sin impresora") + "\n" +
                    now();
            }

            addLog("Guardó configuración de báscula/impresora", "CONFIGURACIÓN");

            alert(
                "Configuración guardada.\n\n" +
                (activarBascula ? "La báscula quedó ACTIVA y se restaurará al abrir la vista.\n" : "La impresora/configuración quedó guardada. Capture IP y puerto para activar la báscula.\n") +
                (printer ? "Impresora: " + printer : "Impresora: sin capturar") +
                "\n\nSolo se desactiva con el botón Desconectar."
            );

            if (activarBascula) {
                pollPesoBasculaActivo();
            }
        }

        document.addEventListener("click", function (ev) {
            var tab = ev.target.closest(".tab, .nav-item");

            if (tab) {
                cambiarVista(tab.getAttribute("data-view"));
                return;
            }

            var cargar = ev.target.closest(".btn-cargar, .ver-pesaje");

            if (cargar) {
                ev.preventDefault();
                cargarRegistro(cargar.getAttribute("data-folio"));
                return;
            }

        });

        async function init() {
            cargarStorage();
            cargarConfigBascula();
            normalizarRegistrosOffline();
            inicializarCatalogosOffline();

            on("btnDetectarImpresoras", "click", detectarImpresorasLocales);
            on("btnBuscarClientes", "click", buscarClientesSap);
            on("btnBuscarArticulos", "click", abrirCatalogoProducto);

            on("btnNuevo", "click", function () { setForm(null); });
            on("btnNuevo2", "click", function () { setForm(null); });

            on("btnBuscarPreRegistro", "click", function () {
                cargarPreRegistroDesdeToken(getValue("scanPreRegistro"));
            });

            on("scanPreRegistro", "keydown", function (ev) {
                if (ev.key === "Enter") {
                    ev.preventDefault();
                    cargarPreRegistroDesdeToken(getValue("scanPreRegistro"));
                }
            });

            on("btnLiberarCapturaManual", "click", liberarCapturaManualDesdePreRegistro);

            on("btnGuardarEntrada", "click", guardarEntrada);
            on("btnGuardarEntrada2", "click", guardarEntrada);

            on("btnCerrarSalida", "click", cerrarSalida);
            on("btnCerrarSalida2", "click", cerrarSalida);


            on("btnExportCsv", "click", exportCsv);
            on("btnLimpiarDemo", "click", limpiarDemo);

            on("btnCapturarPeso", "click", capturarPeso);
            on("btnCaptureWeight", "click", capturarPeso);

            on("btnSimularPeso", "click", simularPeso);
            on("btnSimulate", "click", simularPeso);
            on("btnProbarCero", "click", probarPesoCero);

            on("btnConnectScale", "click", probarConexionTcp);

            on("btnDisconnectScale", "click", desactivarConexionBascula);

            on("btnReadCommand", "click", leerPesoTcp);
            on("btnSaveConfig", "click", guardarConfig);
            on("stableTolerance", "change", resetEstabilizador);
            on("stableSamples", "change", resetEstabilizador);

            on("capturaManual", "change", cambiarCapturaManual);
            on("pesoEntrada", "input", renderResumen);
            on("pesoSalida", "input", renderResumen);
            on("tercero", "input", renderResumen);
            on("tercero", "dblclick", abrirCatalogoTercero);
            on("tercero", "change", seleccionarClienteSap);
            on("tipoMovimiento", "change", function () {
                actualizarTerceroPorMovimiento(true);
                renderResumen();
            });

            on("clasificacion", "change", function () {
                actualizarCampoCantidad();
                renderResumen();
            });
            on("cantidad", "input", function () {
                actualizarCampoCantidad();
                renderResumen();
            });
            on("origen", "input", renderResumen);
            on("destino", "input", renderResumen);

            on("producto", "input", renderResumen);
            on("producto", "dblclick", abrirCatalogoProducto);
            on("producto", "change", seleccionarArticuloSap);
            on("placas", "input", renderResumen);

            on("searchHistory", "input", renderHistorial);
            on("filterEstatus", "change", renderHistorial);
            on("filterFechaDesde", "change", renderHistorial);
            on("filterFechaHasta", "change", renderHistorial);
            on("btnExportExcelHistory", "click", exportExcelHistorial);

            on("btnCloseCatalogModal", "click", cerrarCatalogoTercero);
            on("btnCatalogSearch", "click", buscarCatalogoModal);
            on("catalogSearch", "keydown", function (ev) {
                if (ev.key === "Enter") {
                    ev.preventDefault();
                    buscarCatalogoModal();
                }
                if (ev.key === "Escape") {
                    cerrarCatalogoTercero();
                }
            });
            on("catalogModalBody", "click", function (ev) {
                var btn = ev.target.closest(".catalog-select-btn");
                if (btn) seleccionarCatalogoModal(btn.getAttribute("data-index"));
            });
            on("catalogModalBackdrop", "click", function (ev) {
                if (ev.target && ev.target.id === "catalogModalBackdrop") cerrarCatalogoTercero();
            });

            actualizarTerceroPorMovimiento(false);
            aplicarModoCapturaManual();
            bloquearCamposCatalogo();
            resetEstabilizador();

            if (state.connected) {
                pollPesoBasculaActivo();
            } else {
                simularPeso();
            }

            renderAll();
            await sincronizarPendientes();
            await cargarMovimientosServidor();
            renderAll();

            window.addEventListener("online", function () {
                sincronizarPendientes()
                    .then(cargarMovimientosServidor)
                    .then(renderAll);
                actualizarCatalogosOfflineDesdeServidor(false);
            });

            setInterval(function () {
                sincronizarPendientes();
            }, 15000);

            setInterval(function () {
                actualizarCatalogosOfflineDesdeServidor(false);
            }, 600000);

            setInterval(function () {
                if (state.connected) {
                    pollPesoBasculaActivo();
                }
            }, 3500);

            console.log("BASCULA CAMIONERA JS OK");
        }

        if (document.readyState === "loading") {
            document.addEventListener("DOMContentLoaded", init);
        } else {
            init();
        }
    })();


    /* Refuerzo visual del efecto mecánico en mouse, touch y teclado. */
    (function inicializarBotonesPanico() {
        function activar(btn) {
            if (btn && !btn.disabled) btn.classList.add('is-pressed');
        }

        function liberar(btn) {
            if (btn) btn.classList.remove('is-pressed');
        }

        ['btnGuardarEntrada', 'btnCerrarSalida'].forEach(function (id) {
            var btn = document.getElementById(id);
            if (!btn || btn.dataset.panicBound === '1') return;

            btn.dataset.panicBound = '1';
            btn.addEventListener('pointerdown', function () { activar(btn); });
            btn.addEventListener('pointerup', function () { liberar(btn); });
            btn.addEventListener('pointercancel', function () { liberar(btn); });
            btn.addEventListener('pointerleave', function () { liberar(btn); });
            btn.addEventListener('blur', function () { liberar(btn); });
            btn.addEventListener('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ') activar(btn);
            });
            btn.addEventListener('keyup', function () { liberar(btn); });
        });
    })();
