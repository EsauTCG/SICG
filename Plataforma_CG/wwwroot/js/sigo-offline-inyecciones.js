/*
    SIGO OFFLINE CORE - OPERACIONES / INYECCIONES

    Objetivo:
    - Si hay red, trabaja normal.
    - Si se cae la red, las capturas/guardados se guardan en IndexedDB.
    - Aunque el usuario cierre sesión o cierre navegador, la información queda en la PC.
    - Cuando vuelve la red y hay sesión válida, sincroniza automáticamente.

    Este archivo debe cargarse ANTES de inyeccion.js.
*/

(function () {
    "use strict";

    const DB_NAME = "SIGO_OFFLINE_DB";
    const DB_VERSION = 4;

    const MODULO = "OPERACIONES_INYECCIONES";

    const STORE_SYNC = "syncQueue";
    const STORE_CACHE = "responseCache";
    const STORE_BITACORA = "bitacoraOffline";

    const STATUS_PENDIENTE = "PENDIENTE_SYNC";
    const STATUS_SINCRONIZANDO = "SINCRONIZANDO";
    const STATUS_SINCRONIZADO = "SINCRONIZADO";
    const STATUS_ERROR = "ERROR_SYNC";
    const STATUS_SESION = "SESION_REQUERIDA";

    const originalFetch = window.fetch.bind(window);

    /*
        Endpoints de ESCRITURA.
        Estos primero se guardan en la PC.
    */
    const WRITE_ENDPOINTS = [
        "/Operaciones/GuardarSolicitud",
        "/Operaciones/GuardarPlanDiario",
        "/Operaciones/GuardarPlanSemanal",
        "/Operaciones/GuardarPlanMensual",
        "/Operaciones/GuardarDistribucion",
        "/Operaciones/GuardarDistribucionSemanal",
        "/Operaciones/PlaneadorProduccionGuardar"
    ];

    /*
        Endpoints de consulta.
        Estos se cachean para poder responder si se va la red.
    */
    const READ_ENDPOINTS = [
        "/Operaciones/ObtenerInyeccion",
        "/Operaciones/ObtenerInyecciones",
        "/Operaciones/ObtenerEtiquetacion",
        "/Operaciones/ObtenerEtiquetacionSku",
        "/Operaciones/ObtenerConversiones",
        "/Operaciones/ObtenerSolicitudes",
        "/Operaciones/ObtenerSolicitudDetalle",
        "/Operaciones/ObtenerTiposSolicitud",
        "/Operaciones/ObtenerSolicitudSkus",
        "/Operaciones/ObtenerCatalogoExtra",
        "/Operaciones/ObtenerEstatusSolicitud",
        "/Operaciones/ObtenerSemanal",
        "/Operaciones/ObtenerResumenMensual",
        "/Operaciones/ObtenerDistribucionDia",
        "/Operaciones/SkuSem"
    ];

    /*
        Endpoints que NO deben interceptarse.
    */
    const NO_INTERCEPTAR = [
        "/Acceso/Login",
        "/Acceso/Logout",
        "/Operaciones/PlaneadorProduccionPdf",
        "/Operaciones/ImpresorasLocales",
        "/Operaciones/Tcp/ProbarConexion"
    ];

    function normalizarPath(url) {
        try {
            return new URL(url, window.location.origin).pathname;
        } catch {
            return String(url || "").split("?")[0];
        }
    }

    function normalizarUrlCompleta(url) {
        try {
            const u = new URL(url, window.location.origin);
            return u.pathname + u.search;
        } catch {
            return String(url || "");
        }
    }

    function coincideEndpoint(path, lista) {
        const p = normalizarPath(path).toLowerCase();

        return lista.some(function (x) {
            const endpoint = x.toLowerCase();
            return p === endpoint || p.startsWith(endpoint + "/");
        });
    }

    function esEndpointEscritura(path) {
        return coincideEndpoint(path, WRITE_ENDPOINTS);
    }

    function esEndpointLectura(path) {
        return coincideEndpoint(path, READ_ENDPOINTS);
    }

    function esNoInterceptar(path) {
        return coincideEndpoint(path, NO_INTERCEPTAR);
    }

    function crearGuid() {
        if (window.crypto && crypto.randomUUID) {
            return crypto.randomUUID();
        }

        return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, function (c) {
            const r = Math.random() * 16 | 0;
            const v = c === "x" ? r : (r & 0x3 | 0x8);
            return v.toString(16);
        });
    }

    function ahoraIso() {
        return new Date().toISOString();
    }

    function getUsuarioLocal() {
        return sessionStorage.getItem("SIGO_USUARIO_OFFLINE") ||
            sessionStorage.getItem("SIGO_USUARIO") ||
            window.correoUsuario ||
            "Usuario SIGO";
    }

    function getTerminalId() {
        let terminal = localStorage.getItem("sigoTerminalId") ||
            localStorage.getItem("basculaTerminalId");

        if (!terminal) {
            terminal = "TERMINAL-01";
            localStorage.setItem("sigoTerminalId", terminal);
        }

        return terminal;
    }

    function abrirDB() {
        return new Promise(function (resolve, reject) {
            const request = indexedDB.open(DB_NAME, DB_VERSION);

            request.onupgradeneeded = function (event) {
                const db = event.target.result;

                if (!db.objectStoreNames.contains(STORE_SYNC)) {
                    const store = db.createObjectStore(STORE_SYNC, {
                        keyPath: "syncGuid"
                    });

                    store.createIndex("syncStatus", "syncStatus", { unique: false });
                    store.createIndex("modulo", "modulo", { unique: false });
                    store.createIndex("endpoint", "endpoint", { unique: false });
                    store.createIndex("fechaCreacionLocal", "fechaCreacionLocal", { unique: false });
                }

                if (!db.objectStoreNames.contains(STORE_CACHE)) {
                    const storeCache = db.createObjectStore(STORE_CACHE, {
                        keyPath: "cacheKey"
                    });

                    storeCache.createIndex("endpoint", "endpoint", { unique: false });
                    storeCache.createIndex("fechaCache", "fechaCache", { unique: false });
                }

                if (!db.objectStoreNames.contains(STORE_BITACORA)) {
                    db.createObjectStore(STORE_BITACORA, {
                        keyPath: "bitacoraGuid"
                    });
                }
            };

            request.onsuccess = function () {
                resolve(request.result);
            };

            request.onerror = function () {
                reject(request.error);
            };
        });
    }

    async function idbPut(storeName, value) {
        const db = await abrirDB();

        return new Promise(function (resolve, reject) {
            const tx = db.transaction(storeName, "readwrite");
            tx.objectStore(storeName).put(value);

            tx.oncomplete = function () {
                resolve(value);
            };

            tx.onerror = function () {
                reject(tx.error);
            };
        });
    }

    async function idbGet(storeName, key) {
        const db = await abrirDB();

        return new Promise(function (resolve, reject) {
            const tx = db.transaction(storeName, "readonly");
            const req = tx.objectStore(storeName).get(key);

            req.onsuccess = function () {
                resolve(req.result || null);
            };

            req.onerror = function () {
                reject(req.error);
            };
        });
    }

    async function idbGetAll(storeName) {
        const db = await abrirDB();

        return new Promise(function (resolve, reject) {
            const tx = db.transaction(storeName, "readonly");
            const req = tx.objectStore(storeName).getAll();

            req.onsuccess = function () {
                resolve(req.result || []);
            };

            req.onerror = function () {
                reject(req.error);
            };
        });
    }

    async function guardarBitacora(accion, detalle, extra) {
        try {
            await idbPut(STORE_BITACORA, {
                bitacoraGuid: crearGuid(),
                modulo: MODULO,
                accion: accion,
                detalle: detalle || "",
                extra: extra || null,
                usuarioLocal: getUsuarioLocal(),
                terminalId: getTerminalId(),
                fechaLocal: ahoraIso()
            });
        } catch (err) {
            console.warn("[SIGO Offline] No se pudo guardar bitácora:", err);
        }
    }

    function leerBodyComoTexto(input, init) {
        if (init && init.body !== undefined && init.body !== null) {
            if (typeof init.body === "string") {
                return init.body;
            }

            try {
                return JSON.stringify(init.body);
            } catch {
                return String(init.body);
            }
        }

        if (input instanceof Request) {
            return "";
        }

        return "";
    }

    function parseJsonSeguro(texto) {
        if (!texto) return null;

        try {
            return JSON.parse(texto);
        } catch {
            return null;
        }
    }

    function crearInitConBodyNuevo(init, nuevoBody) {
        const nuevoInit = Object.assign({}, init || {});
        nuevoInit.body = nuevoBody;

        const headers = new Headers(nuevoInit.headers || {});
        if (!headers.has("Content-Type")) {
            headers.set("Content-Type", "application/json");
        }

        nuevoInit.headers = headers;
        return nuevoInit;
    }

    function crearCacheKey(input, init) {
        const method = ((init && init.method) || (input instanceof Request ? input.method : "GET") || "GET").toUpperCase();
        const url = input instanceof Request ? input.url : String(input || "");
        const body = leerBodyComoTexto(input, init);

        return [
            method,
            normalizarUrlCompleta(url),
            body || ""
        ].join("|");
    }

    async function guardarRespuestaCache(input, init, response) {
        try {
            if (!response || !response.ok) return;

            const contentType = response.headers.get("content-type") || "";

            if (!contentType.toLowerCase().includes("application/json")) {
                return;
            }

            const clone = response.clone();
            const texto = await clone.text();

            const cacheItem = {
                cacheKey: crearCacheKey(input, init),
                endpoint: normalizarPath(input instanceof Request ? input.url : String(input || "")),
                method: ((init && init.method) || (input instanceof Request ? input.method : "GET") || "GET").toUpperCase(),
                body: leerBodyComoTexto(input, init),
                responseText: texto,
                contentType: contentType,
                fechaCache: ahoraIso()
            };

            await idbPut(STORE_CACHE, cacheItem);
        } catch (err) {
            console.warn("[SIGO Offline] No se pudo cachear respuesta:", err);
        }
    }

    async function obtenerRespuestaCache(input, init) {
        const cacheKey = crearCacheKey(input, init);
        const item = await idbGet(STORE_CACHE, cacheKey);

        if (!item) return null;

        return new Response(item.responseText || "[]", {
            status: 200,
            headers: {
                "Content-Type": item.contentType || "application/json; charset=utf-8",
                "X-SIGO-OFFLINE-CACHE": "1"
            }
        });
    }

    function respuestaJson(obj, status) {
        return new Response(JSON.stringify(obj), {
            status: status || 200,
            headers: {
                "Content-Type": "application/json; charset=utf-8",
                "X-SIGO-OFFLINE": "1"
            }
        });
    }

    function responsePareceLogin(response) {
        if (!response) return false;

        const finalUrl = response.url || "";
        const contentType = response.headers.get("content-type") || "";

        if (response.status === 401 || response.status === 403) return true;

        if (response.redirected && finalUrl.toLowerCase().includes("/home/index")) {
            return true;
        }

        if (response.redirected && finalUrl.toLowerCase().includes("/acceso/login")) {
            return true;
        }

        if (contentType.toLowerCase().includes("text/html") && finalUrl.toLowerCase().includes("/home/index")) {
            return true;
        }

        return false;
    }

    async function guardarPendienteSync(input, init, motivo) {
        const url = input instanceof Request ? input.url : String(input || "");
        const endpoint = normalizarPath(url);
        const method = ((init && init.method) || (input instanceof Request ? input.method : "GET") || "GET").toUpperCase();
        const bodyTextOriginal = leerBodyComoTexto(input, init);
        let payload = parseJsonSeguro(bodyTextOriginal);

        if (!payload) {
            payload = {
                rawBody: bodyTextOriginal || "",
                contentType: "text/plain"
            };
        }

        const syncGuid = payload.syncGuid ||
            payload.movimientoGuid ||
            payload.idLocal ||
            crearGuid();

        payload.syncGuid = syncGuid;
        payload.movimientoGuid = payload.movimientoGuid || syncGuid;
        payload.moduloOffline = payload.moduloOffline || MODULO;
        payload.usuarioLocal = payload.usuarioLocal || getUsuarioLocal();
        payload.terminalId = payload.terminalId || getTerminalId();
        payload.fechaLocal = payload.fechaLocal || ahoraIso();

        const item = {
            syncGuid: syncGuid,
            modulo: MODULO,
            endpoint: endpoint,
            urlCompleta: normalizarUrlCompleta(url),
            metodo: method,
            payload: payload,
            bodyOriginal: bodyTextOriginal,
            syncStatus: STATUS_PENDIENTE,
            syncAttempts: 0,
            fechaCreacionLocal: ahoraIso(),
            fechaUltimoIntento: null,
            fechaSyncServidor: null,
            errorSync: null,
            motivoLocal: motivo || "Guardado local"
        };

        await idbPut(STORE_SYNC, item);
        await guardarBitacora("GUARDADO_LOCAL", "Registro guardado localmente para sincronización.", item);

        actualizarIndicadorOffline();

        return item;
    }

    async function marcarSync(item, status, error) {
        item.syncStatus = status;
        item.errorSync = error || null;

        if (status === STATUS_SINCRONIZADO) {
            item.fechaSyncServidor = ahoraIso();
        }

        await idbPut(STORE_SYNC, item);
        actualizarIndicadorOffline();
    }

    async function obtenerPendientes() {
        const all = await idbGetAll(STORE_SYNC);

        return all.filter(function (x) {
            return x.modulo === MODULO &&
                (
                    x.syncStatus === STATUS_PENDIENTE ||
                    x.syncStatus === STATUS_ERROR ||
                    x.syncStatus === STATUS_SESION
                );
        });
    }

    async function sincronizarPendientes() {
        const pendientes = await obtenerPendientes();

        if (!pendientes.length) {
            actualizarIndicadorOffline();
            return;
        }

        if (!navigator.onLine) {
            actualizarIndicadorOffline();
            return;
        }

        for (const item of pendientes) {
            try {
                item.syncStatus = STATUS_SINCRONIZANDO;
                item.syncAttempts = (item.syncAttempts || 0) + 1;
                item.fechaUltimoIntento = ahoraIso();
                await idbPut(STORE_SYNC, item);
                actualizarIndicadorOffline();

                const response = await originalFetch(item.endpoint, {
                    method: item.metodo || "POST",
                    headers: {
                        "Content-Type": "application/json"
                    },
                    body: JSON.stringify(item.payload)
                });

                if (responsePareceLogin(response)) {
                    await marcarSync(item, STATUS_SESION, "Sesión requerida para sincronizar.");
                    break;
                }

                const contentType = response.headers.get("content-type") || "";
                let json = null;

                if (contentType.toLowerCase().includes("application/json")) {
                    json = await response.clone().json().catch(function () {
                        return null;
                    });
                }

                const servidorAcepto =
                    response.ok &&
                    (!json || json.success !== false && json.ok !== false);

                if (servidorAcepto) {
                    await marcarSync(item, STATUS_SINCRONIZADO, null);
                    await guardarBitacora("SINCRONIZADO", "Registro sincronizado correctamente.", item);
                } else {
                    const msg = json?.message ||
                        json?.mensaje ||
                        json?.msg ||
                        response.statusText ||
                        "El servidor rechazó la sincronización.";

                    await marcarSync(item, STATUS_ERROR, msg);
                }
            } catch (err) {
                await marcarSync(item, STATUS_ERROR, err.message || "Sin comunicación con servidor.");
                break;
            }
        }

        actualizarIndicadorOffline();
    }

    async function manejarEscritura(input, init) {
        const url = input instanceof Request ? input.url : String(input || "");
        const method = ((init && init.method) || (input instanceof Request ? input.method : "GET") || "GET").toUpperCase();

        let bodyText = leerBodyComoTexto(input, init);
        let payload = parseJsonSeguro(bodyText);

        if (payload) {
            const syncGuid = payload.syncGuid ||
                payload.movimientoGuid ||
                payload.idLocal ||
                crearGuid();

            payload.syncGuid = syncGuid;
            payload.movimientoGuid = payload.movimientoGuid || syncGuid;
            payload.moduloOffline = payload.moduloOffline || MODULO;
            payload.usuarioLocal = payload.usuarioLocal || getUsuarioLocal();
            payload.terminalId = payload.terminalId || getTerminalId();
            payload.fechaLocal = payload.fechaLocal || ahoraIso();

            bodyText = JSON.stringify(payload);
            init = crearInitConBodyNuevo(init, bodyText);
        }

        /*
            Primero se guarda local.
            Si luego el servidor responde bien, se marca como sincronizado.
            Si falla, ya quedó en la PC.
        */
        const item = await guardarPendienteSync(input, init, "Guardado antes de enviar al servidor.");

        if (!navigator.onLine) {
            return respuestaJson({
                success: true,
                ok: true,
                offline: true,
                pendingSync: true,
                syncGuid: item.syncGuid,
                message: "Guardado localmente. Se sincronizará cuando vuelva la red."
            }, 200);
        }

        try {
            const response = await originalFetch(input, init);

            if (responsePareceLogin(response)) {
                await marcarSync(item, STATUS_SESION, "Sesión requerida para sincronizar.");
                return respuestaJson({
                    success: true,
                    ok: true,
                    offline: true,
                    pendingSync: true,
                    sessionRequired: true,
                    syncGuid: item.syncGuid,
                    message: "Guardado localmente. Se sincronizará cuando vuelva a iniciar sesión."
                }, 200);
            }

            if (response.ok) {
                let json = null;
                const contentType = response.headers.get("content-type") || "";

                if (contentType.toLowerCase().includes("application/json")) {
                    json = await response.clone().json().catch(function () {
                        return null;
                    });
                }

                if (!json || json.success !== false && json.ok !== false) {
                    await marcarSync(item, STATUS_SINCRONIZADO, null);
                } else {
                    await marcarSync(item, STATUS_ERROR, json.message || json.mensaje || "Servidor rechazó el registro.");
                }

                return response;
            }

            await marcarSync(item, STATUS_ERROR, response.statusText || "Error del servidor.");

            /*
                Aunque el servidor falle, regresamos éxito al front porque ya está guardado local.
                Así el usuario no pierde operación.
            */
            return respuestaJson({
                success: true,
                ok: true,
                offline: true,
                pendingSync: true,
                syncGuid: item.syncGuid,
                message: "Guardado localmente. El servidor no respondió correctamente y se reintentará."
            }, 200);
        } catch (err) {
            await marcarSync(item, STATUS_ERROR, err.message || "Sin red.");

            return respuestaJson({
                success: true,
                ok: true,
                offline: true,
                pendingSync: true,
                syncGuid: item.syncGuid,
                message: "Guardado localmente. Se sincronizará cuando vuelva la red."
            }, 200);
        }
    }

    async function manejarLectura(input, init) {
        try {
            const response = await originalFetch(input, init);

            if (response && response.ok) {
                await guardarRespuestaCache(input, init, response.clone());
            }

            return response;
        } catch (err) {
            const cached = await obtenerRespuestaCache(input, init);

            if (cached) {
                return cached;
            }

            return respuestaJson([], 200);
        }
    }

    async function fetchOffline(input, init) {
        const url = input instanceof Request ? input.url : String(input || "");
        const path = normalizarPath(url);
        const method = ((init && init.method) || (input instanceof Request ? input.method : "GET") || "GET").toUpperCase();

        if (esNoInterceptar(path)) {
            return originalFetch(input, init);
        }

        if (method !== "GET" && method !== "POST") {
            return originalFetch(input, init);
        }

        if (method === "POST" && esEndpointEscritura(path)) {
            return manejarEscritura(input, init || {});
        }

        if (esEndpointLectura(path)) {
            return manejarLectura(input, init || {});
        }

        return originalFetch(input, init);
    }

    function crearIndicadorOffline() {
        if (document.getElementById("sigoOfflineStatus")) return;

        const div = document.createElement("div");
        div.id = "sigoOfflineStatus";
        div.style.position = "fixed";
        div.style.left = "16px";
        div.style.bottom = "16px";
        div.style.zIndex = "30000";
        div.style.padding = "10px 14px";
        div.style.borderRadius = "999px";
        div.style.fontSize = "13px";
        div.style.fontWeight = "800";
        div.style.boxShadow = "0 12px 28px rgba(0,0,0,.22)";
        div.style.display = "none";
        div.style.alignItems = "center";
        div.style.gap = "8px";
        div.style.maxWidth = "calc(100vw - 32px)";
        div.style.cursor = "pointer";
        div.title = "Haz clic para intentar sincronizar pendientes.";
        div.onclick = function () {
            sincronizarPendientes();
        };

        document.body.appendChild(div);
    }

    async function contarPendientes() {
        const all = await idbGetAll(STORE_SYNC);

        const pendientes = all.filter(function (x) {
            return x.modulo === MODULO &&
                (
                    x.syncStatus === STATUS_PENDIENTE ||
                    x.syncStatus === STATUS_ERROR ||
                    x.syncStatus === STATUS_SESION ||
                    x.syncStatus === STATUS_SINCRONIZANDO
                );
        });

        return pendientes.length;
    }

    async function actualizarIndicadorOffline() {
        try {
            crearIndicadorOffline();

            const div = document.getElementById("sigoOfflineStatus");
            if (!div) return;

            const pendientes = await contarPendientes();
            const online = navigator.onLine;

            if (online && pendientes === 0) {
                div.style.display = "none";
                return;
            }

            div.style.display = "flex";

            if (!online) {
                div.style.background = "linear-gradient(90deg,#f0a202,#b36b00)";
                div.style.color = "#fff";
                div.textContent = pendientes > 0
                    ? "Modo local · " + pendientes + " pendiente(s)"
                    : "Modo local activo";
                return;
            }

            if (pendientes > 0) {
                div.style.background = "linear-gradient(90deg,#0d6efd,#084298)";
                div.style.color = "#fff";
                div.textContent = "Sincronizando · " + pendientes + " pendiente(s)";
                return;
            }

            div.style.display = "none";
        } catch (err) {
            console.warn("[SIGO Offline] Error actualizando indicador:", err);
        }
    }

    async function mostrarResumenOfflineEnConsola() {
        const all = await idbGetAll(STORE_SYNC);
        const rows = all
            .filter(x => x.modulo === MODULO)
            .map(x => ({
                syncGuid: x.syncGuid,
                endpoint: x.endpoint,
                status: x.syncStatus,
                intentos: x.syncAttempts,
                fecha: x.fechaCreacionLocal,
                error: x.errorSync
            }));

        console.table(rows);
        return rows;
    }

    /*
        API global por si quieres usarla manualmente desde consola o desde otros botones.
    */
    window.SIGO_INYECCIONES_OFFLINE = {
        dbName: DB_NAME,
        modulo: MODULO,
        sincronizar: sincronizarPendientes,
        pendientes: obtenerPendientes,
        resumen: mostrarResumenOfflineEnConsola,
        actualizarIndicador: actualizarIndicadorOffline
    };

    /*
        Activar interceptor.
    */
    window.fetch = fetchOffline;

    window.addEventListener("online", function () {
        actualizarIndicadorOffline();
        sincronizarPendientes();
    });

    window.addEventListener("offline", function () {
        actualizarIndicadorOffline();
    });

    document.addEventListener("DOMContentLoaded", function () {
        crearIndicadorOffline();
        actualizarIndicadorOffline();

        setTimeout(function () {
            sincronizarPendientes();
        }, 1500);
    });

    setInterval(function () {
        sincronizarPendientes();
    }, 15000);

})();