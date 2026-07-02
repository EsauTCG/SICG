/*
    SIGO CATÁLOGOS OFFLINE - OPERACIONES / INYECCIONES

    Objetivo:
    - Precargar catálogos cuando hay red.
    - Guardar información en IndexedDB.
    - Rellenar datos desde memoria local si se va la red.
    - Evitar que el usuario vea "undefined".
    - Debe cargarse DESPUÉS de inyeccion.js.
*/

(function () {
    "use strict";

    const DB_NAME = "SIGO_OFFLINE_DB";
    const DB_VERSION = 4;
    const STORE_CACHE = "responseCache";

    const URL_CATALOGO_EXTRA = "/Operaciones/ObtenerCatalogoExtra";
    const URL_SOLICITUD_SKUS = "/Operaciones/ObtenerSolicitudSkus";
    const URL_TIPOS_SOLICITUD = "/Operaciones/ObtenerTiposSolicitud";
    const URL_ESTATUS_SOLICITUD = "/Operaciones/ObtenerEstatusSolicitud";

    const URL_INYECCION = "/Operaciones/ObtenerInyeccion";
    const URL_ETIQUETACION = "/Operaciones/ObtenerEtiquetacionSku";
    const URL_CONVERSIONES = "/Operaciones/ObtenerConversiones";

    const CACHE_PREFIX = "CATALOGO_INYECCIONES|";

    function abrirDB() {
        return new Promise(function (resolve, reject) {
            const req = indexedDB.open(DB_NAME, DB_VERSION);

            req.onsuccess = function () {
                resolve(req.result);
            };

            req.onerror = function () {
                reject(req.error);
            };

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
        });
    }

    async function cachePut(key, data) {
        try {
            const db = await abrirDB();

            return new Promise(function (resolve, reject) {
                const tx = db.transaction(STORE_CACHE, "readwrite");

                tx.objectStore(STORE_CACHE).put({
                    cacheKey: CACHE_PREFIX + key,
                    endpoint: key,
                    method: "CATALOGO",
                    body: "",
                    responseText: JSON.stringify(data == null ? null : data),
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
        } catch (err) {
            console.warn("[SIGO Catálogos] No se pudo guardar cache:", key, err);
        }
    }

    async function cacheGet(key) {
        try {
            const db = await abrirDB();

            return new Promise(function (resolve, reject) {
                const tx = db.transaction(STORE_CACHE, "readonly");
                const req = tx.objectStore(STORE_CACHE).get(CACHE_PREFIX + key);

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
        } catch (err) {
            console.warn("[SIGO Catálogos] No se pudo leer cache:", key, err);
            return null;
        }
    }

    async function getJsonConCache(url, cacheKey) {
        try {
            const res = await fetch(url, {
                method: "GET",
                headers: {
                    "Accept": "application/json"
                },
                cache: "no-store"
            });

            const data = await res.json().catch(function () {
                return null;
            });

            if (res.ok && data !== null && data !== undefined) {
                await cachePut(cacheKey, data);
                return data;
            }
        } catch (err) {
            // Si no hay red, intenta leer cache local.
        }

        return await cacheGet(cacheKey);
    }

    function normalizarTexto(value) {
        return String(value == null ? "" : value)
            .normalize("NFD")
            .replace(/[\u0300-\u036f]/g, "")
            .trim()
            .toUpperCase();
    }

    function limpiarUndefined(value) {
        if (value === null || value === undefined) return "";

        const txt = String(value).trim();

        if (!txt) return "";
        if (txt.toLowerCase() === "undefined") return "";
        if (txt.toLowerCase() === "null") return "";

        return txt;
    }

    function setInput(id, value) {
        const el = document.getElementById(id);
        if (!el) return;

        const limpio = limpiarUndefined(value);
        el.value = limpio || "—";
    }

    function getInput(id) {
        const el = document.getElementById(id);
        if (!el) return "";

        return limpiarUndefined(el.value);
    }

    function setText(id, value) {
        const el = document.getElementById(id);
        if (!el) return;

        const limpio = limpiarUndefined(value);
        el.textContent = limpio || "—";
    }

    function getText(id) {
        const el = document.getElementById(id);
        if (!el) return "";

        return limpiarUndefined(el.textContent);
    }

    function pick(obj, names) {
        if (!obj) return "";

        for (const name of names) {
            if (obj[name] !== null && obj[name] !== undefined && String(obj[name]).trim() !== "") {
                return obj[name];
            }

            const lowerName = name.toLowerCase();

            for (const key of Object.keys(obj)) {
                if (key.toLowerCase() === lowerName) {
                    const value = obj[key];

                    if (value !== null && value !== undefined && String(value).trim() !== "") {
                        return value;
                    }
                }
            }
        }

        return "";
    }

    function toArray(data) {
        if (!data) return [];

        if (Array.isArray(data)) return data;

        if (Array.isArray(data.data)) return data.data;
        if (Array.isArray(data.result)) return data.result;
        if (Array.isArray(data.resultado)) return data.resultado;
        if (Array.isArray(data.productos)) return data.productos;
        if (Array.isArray(data.lista)) return data.lista;
        if (Array.isArray(data.items)) return data.items;

        return [data];
    }

    function extraerSku(item) {
        return limpiarUndefined(
            pick(item, [
                "SKU",
                "Sku",
                "sku",
                "ProductoCodigo",
                "productoCodigo",
                "Codigo",
                "codigo",
                "Articulo",
                "articulo",
                "ItemCode",
                "itemCode"
            ])
        );
    }

    function extraerNombreProducto(item) {
        return limpiarUndefined(
            pick(item, [
                "Producto",
                "producto",
                "Nombre",
                "nombre",
                "Descripcion",
                "descripcion",
                "Descripción",
                "ItemName",
                "itemName",
                "ProductoNombre",
                "productoNombre"
            ])
        );
    }

    async function obtenerCatalogoProductosLocal() {
        const extra = await cacheGet("BASE|ObtenerCatalogoExtra");
        const skus = await cacheGet("BASE|ObtenerSolicitudSkus");

        const arr = []
            .concat(toArray(extra))
            .concat(toArray(skus));

        const map = new Map();

        for (const item of arr) {
            const sku = extraerSku(item);
            const nombre = extraerNombreProducto(item);

            if (!sku && !nombre) continue;

            const key = sku || nombre;

            if (!map.has(key)) {
                map.set(key, item);
            }
        }

        return Array.from(map.values());
    }

    async function buscarProductoActualEnCatalogo() {
        const skuActual = getInput("sku");
        const productoInput = getInput("producto");
        const productoSeleccionado = getText("productoSeleccionado");

        const productos = await obtenerCatalogoProductosLocal();

        if (skuActual && skuActual !== "—") {
            const encontradoSku = productos.find(function (p) {
                return normalizarTexto(extraerSku(p)) === normalizarTexto(skuActual);
            });

            if (encontradoSku) return encontradoSku;
        }

        const nombreActual = productoInput && productoInput !== "—"
            ? productoInput
            : productoSeleccionado;

        if (!nombreActual || nombreActual === "—") {
            return null;
        }

        const nombreNorm = normalizarTexto(nombreActual);

        let encontrado = productos.find(function (p) {
            return normalizarTexto(extraerNombreProducto(p)) === nombreNorm;
        });

        if (encontrado) return encontrado;

        encontrado = productos.find(function (p) {
            const nombre = normalizarTexto(extraerNombreProducto(p));
            return nombre.includes(nombreNorm) || nombreNorm.includes(nombre);
        });

        return encontrado || null;
    }

    async function precargarCatalogosBase() {
        const catalogoExtra = await getJsonConCache(
            URL_CATALOGO_EXTRA,
            "BASE|ObtenerCatalogoExtra"
        );

        const solicitudSkus = await getJsonConCache(
            URL_SOLICITUD_SKUS,
            "BASE|ObtenerSolicitudSkus"
        );

        await getJsonConCache(
            URL_TIPOS_SOLICITUD,
            "BASE|ObtenerTiposSolicitud"
        );

        await getJsonConCache(
            URL_ESTATUS_SOLICITUD,
            "BASE|ObtenerEstatusSolicitud"
        );

        const productos = []
            .concat(toArray(catalogoExtra))
            .concat(toArray(solicitudSkus));

        const skus = Array.from(new Set(
            productos
                .map(extraerSku)
                .filter(Boolean)
        ));

        /*
            Para no saturar el servidor, precarga detalle de los primeros 150 SKUs.
            Puedes subir este número si necesitas más.
        */
        for (const sku of skus.slice(0, 150)) {
            await precargarDetalleSku(sku);
        }
    }

    async function precargarDetalleSku(sku) {
        sku = limpiarUndefined(sku);
        if (!sku || sku === "—") return;

        const safeSku = encodeURIComponent(sku);

        await getJsonConCache(
            URL_INYECCION + "?sku=" + safeSku,
            "DETALLE|" + sku + "|INYECCION"
        );

        await getJsonConCache(
            URL_ETIQUETACION + "?sku=" + safeSku,
            "DETALLE|" + sku + "|ETIQUETACION"
        );

        await getJsonConCache(
            URL_CONVERSIONES + "?sku=" + safeSku,
            "DETALLE|" + sku + "|CONVERSIONES"
        );
    }

    function normalizarDetalleInyeccion(data) {
        if (!data) return {};

        const root = data.Iny || data.iny || data.INY || data;
        const arr = toArray(root);

        return arr.length ? arr[0] : {};
    }

    function normalizarDetalleEtiquetacion(data) {
        if (!data) return {};

        const root = data.Etiq || data.etiq || data.ETIQ || data;
        const arr = toArray(root);

        return arr.length ? arr[0] : {};
    }

    function normalizarDetalleConversion(data) {
        if (!data) return {};

        const arr = toArray(data);

        return arr.length ? arr[0] : {};
    }

    function valorPorcentaje(iny, etiq, conv) {
        return pick(iny, [
            "Porcentaje",
            "porcentaje",
            "PorcentajeInyeccion",
            "porcentajeInyeccion",
            "Pct",
            "pct",
            "RendPct",
            "rendPct",
            "Inyeccion",
            "inyeccion",
            "Iny",
            "iny"
        ]) || pick(etiq, [
            "Porcentaje",
            "porcentaje",
            "Pct",
            "pct"
        ]) || pick(conv, [
            "Porcentaje",
            "porcentaje",
            "Pct",
            "pct"
        ]);
    }

    function valorVelocidad(iny) {
        return pick(iny, [
            "Velocidad",
            "velocidad",
            "Speed",
            "speed",
            "Vel",
            "vel"
        ]);
    }

    function valorModo(iny) {
        return pick(iny, [
            "Modo",
            "modo",
            "ModoInyeccion",
            "modoInyeccion",
            "Programa",
            "programa"
        ]);
    }

    function valorPresion(iny) {
        return pick(iny, [
            "Presion",
            "presion",
            "Presión",
            "presión",
            "Pressure",
            "pressure"
        ]);
    }

    function valorAltura(iny) {
        return pick(iny, [
            "Altura",
            "altura",
            "Height",
            "height"
        ]);
    }

    function valorAvance(iny) {
        return pick(iny, [
            "Avance",
            "avance",
            "Advance",
            "advance"
        ]);
    }

    function valorTara(conv, iny) {
        return pick(conv, [
            "Tara",
            "tara",
            "PesoTara",
            "pesoTara",
            "Tarima",
            "tarima"
        ]) || pick(iny, [
            "Tara",
            "tara",
            "PesoTara",
            "pesoTara"
        ]);
    }

    function valorImagen(producto, iny, etiq, conv) {
        return pick(producto, [
            "Imagen",
            "imagen",
            "ImagenUrl",
            "imagenUrl",
            "UrlImagen",
            "urlImagen",
            "Foto",
            "foto"
        ]) || pick(iny, [
            "Imagen",
            "imagen",
            "ImagenUrl",
            "imagenUrl"
        ]) || pick(etiq, [
            "Imagen",
            "imagen",
            "ImagenUrl",
            "imagenUrl"
        ]) || pick(conv, [
            "Imagen",
            "imagen",
            "ImagenUrl",
            "imagenUrl"
        ]);
    }

    async function obtenerDetalleCompletoSku(sku) {
        sku = limpiarUndefined(sku);
        if (!sku || sku === "—") return null;

        const safeSku = encodeURIComponent(sku);

        const dataIny = await getJsonConCache(
            URL_INYECCION + "?sku=" + safeSku,
            "DETALLE|" + sku + "|INYECCION"
        );

        const dataEtiq = await getJsonConCache(
            URL_ETIQUETACION + "?sku=" + safeSku,
            "DETALLE|" + sku + "|ETIQUETACION"
        );

        const dataConv = await getJsonConCache(
            URL_CONVERSIONES + "?sku=" + safeSku,
            "DETALLE|" + sku + "|CONVERSIONES"
        );

        return {
            iny: normalizarDetalleInyeccion(dataIny),
            etiq: normalizarDetalleEtiquetacion(dataEtiq),
            conv: normalizarDetalleConversion(dataConv)
        };
    }

    async function hidratarDatosProducto() {
        try {
            let sku = getInput("sku");
            let productoNombre = getInput("producto");

            const productoCatalogo = await buscarProductoActualEnCatalogo();

            if ((!sku || sku === "—") && productoCatalogo) {
                sku = extraerSku(productoCatalogo);
            }

            if ((!productoNombre || productoNombre === "—") && productoCatalogo) {
                productoNombre = extraerNombreProducto(productoCatalogo);
            }

            if (!sku || sku === "—") {
                limpiarCamposUndefined();
                return;
            }

            await precargarDetalleSku(sku);

            const detalle = await obtenerDetalleCompletoSku(sku);

            if (!detalle) {
                limpiarCamposUndefined();
                return;
            }

            const iny = detalle.iny || {};
            const etiq = detalle.etiq || {};
            const conv = detalle.conv || {};

            setInput("sku", sku);
            setInput("producto", productoNombre || getText("productoSeleccionado"));

            setInput("porcentaje", valorPorcentaje(iny, etiq, conv));
            setInput("velocidad", valorVelocidad(iny));
            setInput("modo", valorModo(iny));
            setInput("presion", valorPresion(iny));
            setInput("altura", valorAltura(iny));
            setInput("avance", valorAvance(iny));
            setInput("tara", valorTara(conv, iny));

            const img = document.getElementById("imagenProducto");
            const imgUrl = valorImagen(productoCatalogo || {}, iny, etiq, conv);

            if (img && limpiarUndefined(imgUrl)) {
                img.src = imgUrl;
            }

            limpiarCamposUndefined();
        } catch (err) {
            console.warn("[SIGO Catálogos] No se pudo hidratar datos del producto:", err);
            limpiarCamposUndefined();
        }
    }

    function limpiarCamposUndefined() {
        const ids = [
            "sku",
            "porcentaje",
            "velocidad",
            "producto",
            "modo",
            "presion",
            "altura",
            "avance",
            "tara"
        ];

        for (const id of ids) {
            const el = document.getElementById(id);
            if (!el) continue;

            const value = limpiarUndefined(el.value);

            if (!value) {
                el.value = "—";
            } else {
                el.value = value;
            }
        }

        const productoSeleccionado = document.getElementById("productoSeleccionado");

        if (productoSeleccionado) {
            const value = limpiarUndefined(productoSeleccionado.textContent);
            productoSeleccionado.textContent = value || "—";
        }
    }

    function contieneUndefinedVisible() {
        const ids = [
            "sku",
            "porcentaje",
            "velocidad",
            "producto",
            "modo",
            "presion",
            "altura",
            "avance",
            "tara"
        ];

        return ids.some(function (id) {
            const el = document.getElementById(id);
            if (!el) return false;

            return String(el.value || "")
                .toLowerCase()
                .includes("undefined");
        });
    }

    function instalarEventos() {
        document.addEventListener("click", function (e) {
            const productCard = e.target.closest(
                ".product-card, .product-card-large, [data-sku], [data-producto], #contenedorProductos *"
            );

            if (productCard) {
                setTimeout(hidratarDatosProducto, 150);
                setTimeout(hidratarDatosProducto, 500);
                setTimeout(hidratarDatosProducto, 1200);
            }
        });

        document.addEventListener("change", function (e) {
            if (
                e.target &&
                (
                    e.target.id === "loteSelect" ||
                    e.target.id === "sku" ||
                    e.target.id === "producto"
                )
            ) {
                setTimeout(hidratarDatosProducto, 250);
            }
        });

        /*
            Corrige casos donde inyeccion.js escriba "undefined" después.
        */
        setInterval(function () {
            const productoActual = getInput("producto") || getText("productoSeleccionado");

            if (contieneUndefinedVisible() || productoActual) {
                hidratarDatosProducto();
            }
        }, 2500);
    }

    async function inicializarCatalogosOffline() {
        limpiarCamposUndefined();

        /*
            Precarga silenciosa.
            No bloquea al usuario.
        */
        precargarCatalogosBase()
            .then(function () {
                return hidratarDatosProducto();
            })
            .catch(function (err) {
                console.warn("[SIGO Catálogos] Precarga incompleta:", err);
            });

        setTimeout(hidratarDatosProducto, 800);
        setTimeout(hidratarDatosProducto, 1800);
    }

    window.SIGO_INYECCIONES_CATALOGOS = {
        precargar: precargarCatalogosBase,
        hidratar: hidratarDatosProducto,
        limpiarUndefined: limpiarCamposUndefined,
        precargarSku: precargarDetalleSku
    };

    document.addEventListener("DOMContentLoaded", function () {
        instalarEventos();
        inicializarCatalogosOffline();
    });

})();