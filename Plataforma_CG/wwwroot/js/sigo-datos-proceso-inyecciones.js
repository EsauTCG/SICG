(function () {
    "use strict";

    const DB_NAME = "SIGO_OFFLINE_DB";
    const DB_VERSION = 4;
    const STORE_CACHE = "responseCache";

    const ENDPOINT = "/Operaciones/ObtenerDatosProcesoInyeccion";
    const CACHE_PREFIX = "DATOS_PROCESO_INYECCION|";

    let ultimoSku = "";
    let cargando = false;

    function limpiar(value) {
        if (value === null || value === undefined) return "";

        const txt = String(value).trim();

        if (!txt) return "";
        if (txt.toLowerCase() === "undefined") return "";
        if (txt.toLowerCase() === "null") return "";
        if (txt === "—") return "";

        return txt;
    }

    function getValue(id) {
        const el = document.getElementById(id);
        if (!el) return "";

        if ("value" in el) {
            return limpiar(el.value);
        }

        return limpiar(el.textContent);
    }

    function setValue(id, value) {
        const el = document.getElementById(id);
        if (!el) return;

        const txt = limpiar(value) || "—";

        if ("value" in el) {
            el.value = txt;
        } else {
            el.textContent = txt;
        }
    }

    function abrirDB() {
        return new Promise(function (resolve, reject) {
            const req = indexedDB.open(DB_NAME, DB_VERSION);

            req.onupgradeneeded = function (event) {
                const db = event.target.result;

                if (!db.objectStoreNames.contains(STORE_CACHE)) {
                    const store = db.createObjectStore(STORE_CACHE, {
                        keyPath: "cacheKey"
                    });

                    store.createIndex("endpoint", "endpoint", { unique: false });
                    store.createIndex("fechaCache", "fechaCache", { unique: false });
                }
            };

            req.onsuccess = function () {
                resolve(req.result);
            };

            req.onerror = function () {
                reject(req.error);
            };
        });
    }

    async function guardarCache(sku, data) {
        const db = await abrirDB();

        return new Promise(function (resolve, reject) {
            const tx = db.transaction(STORE_CACHE, "readwrite");

            tx.objectStore(STORE_CACHE).put({
                cacheKey: CACHE_PREFIX + sku,
                endpoint: ENDPOINT,
                method: "GET",
                body: sku,
                responseText: JSON.stringify(data),
                contentType: "application/json; charset=utf-8",
                fechaCache: new Date().toISOString()
            });

            tx.oncomplete = function () {
                resolve();
            };

            tx.onerror = function () {
                reject(tx.error);
            };
        });
    }

    async function leerCache(sku) {
        try {
            const db = await abrirDB();

            return new Promise(function (resolve, reject) {
                const tx = db.transaction(STORE_CACHE, "readonly");
                const req = tx.objectStore(STORE_CACHE).get(CACHE_PREFIX + sku);

                req.onsuccess = function () {
                    const row = req.result;

                    if (!row || !row.responseText) {
                        resolve(null);
                        return;
                    }

                    try {
                        resolve(JSON.parse(row.responseText));
                    } catch {
                        resolve(null);
                    }
                };

                req.onerror = function () {
                    reject(req.error);
                };
            });
        } catch {
            return null;
        }
    }

    function obtenerSkuActual() {
        const sku = getValue("sku");

        if (sku) return sku;

        const productoSeleccionado = getValue("productoSeleccionado");

        if (productoSeleccionado && productoSeleccionado.includes("|")) {
            return limpiar(productoSeleccionado.split("|")[0]);
        }

        return "";
    }

    async function obtenerDatosProceso(sku) {
        sku = limpiar(sku);

        if (!sku) return null;

        try {
            const res = await fetch(ENDPOINT + "?sku=" + encodeURIComponent(sku), {
                method: "GET",
                headers: {
                    "Accept": "application/json"
                },
                cache: "no-store"
            });

            const data = await res.json().catch(function () {
                return null;
            });

            if (res.ok && data && data.ok !== false) {
                await guardarCache(sku, data);
                return data;
            }
        } catch {
            // Si no hay red, usa memoria local.
        }

        return await leerCache(sku);
    }

function pintarDatos(data) {
    if (!data) return;

    setValueSoloSiTieneDato("sku", data.sku);
    setValueSoloSiTieneDato("producto", data.producto);
    setValueSoloSiTieneDato("porcentaje", data.porcentaje);
    setValueSoloSiTieneDato("velocidad", data.velocidad);
    setValueSoloSiTieneDato("modo", data.modo);
    setValueSoloSiTieneDato("presion", data.presion);
    setValueSoloSiTieneDato("altura", data.altura);
    setValueSoloSiTieneDato("avance", data.avance);
    setValueSoloSiTieneDato("tara", data.tara);
}

function setValueSoloSiTieneDato(id, value) {
    const el = document.getElementById(id);
    if (!el) return;

    const nuevoValor = limpiar(value);

    /*
        Si el servidor viene vacío, undefined, null o "—",
        NO borres lo que ya cargó inyeccion.js.
    */
    if (!nuevoValor) {
        return;
    }

    if ("value" in el) {
        el.value = nuevoValor;
    } else {
        el.textContent = nuevoValor;
    }
}

  function limpiarCampos() {
    const ids = [
        "sku",
        "producto",
        "porcentaje",
        "velocidad",
        "modo",
        "presion",
        "altura",
        "avance",
        "tara"
    ];

    ids.forEach(function (id) {
        const el = document.getElementById(id);
        if (!el) return;

        const raw = "value" in el ? el.value : el.textContent;
        const value = limpiar(raw);

        /*
            Sólo limpia undefined/null.
            No pisa datos válidos como 0, 2, 15, 50.
        */
        if (String(raw || "").toLowerCase().includes("undefined") ||
            String(raw || "").toLowerCase().includes("null")) {

            if ("value" in el) {
                el.value = "—";
            } else {
                el.textContent = "—";
            }
        }
    });
}

    async function hidratarDatosProceso(forzar) {
        if (cargando) return;

        const sku = obtenerSkuActual();

        if (!sku) {
            limpiarCampos();
            return;
        }

        if (!forzar && sku === ultimoSku) {
            limpiarCampos();
            return;
        }

        cargando = true;
        ultimoSku = sku;

        try {
            const data = await obtenerDatosProceso(sku);

            if (data) {
                pintarDatos(data);
            }

            limpiarCampos();

            console.log("[SIGO] Datos proceso cargados:", data);
        } catch (err) {
            console.warn("[SIGO] Error cargando datos proceso:", err);
            limpiarCampos();
        } finally {
            cargando = false;
        }
    }

    function instalarEventos() {
        document.addEventListener("click", function (e) {
            if (
                e.target.closest(".product-card") ||
                e.target.closest(".product-card-large") ||
                e.target.closest("[data-sku]") ||
                e.target.closest("[data-producto]") ||
                e.target.closest("#contenedorProductos")
            ) {
                setTimeout(function () {
                    hidratarDatosProceso(true);
                }, 300);

                setTimeout(function () {
                    hidratarDatosProceso(true);
                }, 900);

                setTimeout(function () {
                    hidratarDatosProceso(true);
                }, 1600);
            }
        });

        document.addEventListener("change", function (e) {
            if (
                e.target &&
                (
                    e.target.id === "sku" ||
                    e.target.id === "producto" ||
                    e.target.id === "loteSelect"
                )
            ) {
                setTimeout(function () {
                    hidratarDatosProceso(true);
                }, 300);
            }
        });

        setInterval(function () {
            hidratarDatosProceso(false);
        }, 2500);
    }

    window.SIGO_DATOS_PROCESO_INY = {
        hidratar: function () {
            return hidratarDatosProceso(true);
        },
        cache: leerCache
    };

    document.addEventListener("DOMContentLoaded", function () {
        instalarEventos();

        setTimeout(function () {
            hidratarDatosProceso(true);
        }, 800);

        setTimeout(function () {
            hidratarDatosProceso(true);
        }, 2000);
    });

})();