/*
    SIGO OFFLINE SERVICE WORKER
    Generado para las vistas detectadas en Views.zip

    Uso:
    1) Guardar este archivo como: wwwroot/service-worker.js
    2) Registrar desde el Index/Login o layout.
    3) Abrir con red las vistas que se quieran usar offline.
    4) Después funcionarán sin red si ya quedaron cacheadas.

    Importante:
    - Las capturas/movimientos deben seguir usando IndexedDB.
    - Los endpoints de escritura no se cachean.
    - Las vistas protegidas solo se guardan si el servidor no devuelve login.
*/

const SIGO_SW_VERSION = "2026-06-27-views-v1";
const CACHE_STATIC = "sigo-static-" + SIGO_SW_VERSION;
const CACHE_PAGES = "sigo-pages-" + SIGO_SW_VERSION;
const CACHE_DATA = "sigo-data-" + SIGO_SW_VERSION;

const LOGIN_URL = "/Home/Index";
const INICIO_URL = "/Home/Inicio";
const SIDEBAR_MODULOS_URL = "/Sidebar/CargarModulos";

/*
   Vistas detectadas desde Views.zip.
   Para que una vista funcione offline, primero debe abrirse una vez con red y sesión válida.
*/
const OFFLINE_VIEWS = [
    "/",
    "/Acceso/ErrorLogin",
    "/Acceso/ModoLimitado",
    "/Acceso/NoAutorizado",
    "/Autorizaciones/ControlPrecios",
    "/Autorizaciones/aut_credito",
    "/Autorizaciones/aut_precio",
    "/Autorizaciones/aut_presupuesto",
    "/BasculaCamionera/BasculaCamionera",
    "/Chat/BandejaArea",
    "/Comercial/Balance_Master",
    "/Comercial/Cat_Articulo",
    "/Comercial/Cat_Clientes",
    "/Comercial/Cat_Precio",
    "/Comercial/Clien",
    "/Comercial/Clientes/Cliente",
    "/Comercial/Clientes/Cliente/Index",
    "/Comercial/Clientes/Cliente/Modificar",
    "/Comercial/ConfirmadoVsEmbarcado",
    "/Comercial/ControlCenter",
    "/Comercial/Inventarios",
    "/Comercial/OrdenVenta",
    "/Comercial/OrdenesPorVendedor",
    "/Comercial/Planeacion",
    "/Comercial/Planeacion/Detalle",
    "/Comercial/Planeacion/Index",
    "/Comercial/Planeacion/Index-Master",
    "/Comercial/Planeacion/Meses",
    "/Comercial/Planeacion/Planes",
    "/Comercial/Planeacion/Semanal",
    "/Comercial/Planeacion/Semanal/Detalle",
    "/Comercial/Planeacion/Semanal/Index",
    "/Comercial/Planes",
    "/Comercial/Presupuestos",
    "/Comercial/PresupuestosGenerales",
    "/Comercial/PresupuestosPorMes",
    "/Comercial/Prosp",
    "/Comercial/SolicitudMuestras",
    "/Comercial/TableroOV",
    "/Comercial/TrackingViaje",
    "/Comercial/Ventas/Chofer",
    "/Comercial/Ventas/Chofer/Index",
    "/Comercial/Ventas/Chofer/Modificar",
    "/Comercial/Ventas/Prospecto",
    "/Comercial/Ventas/Prospecto/Guardar",
    "/Comercial/Ventas/Prospecto/Index",
    "/Comercial/Ventas/Prospecto/Modificar",
    "/Comercial/admin_ventas",
    "/Comercial/mapaCarga",
    "/Embarques/Calidad",
    "/Embarques/Caseta",
    "/Embarques/ControlCenter",
    "/Embarques/Crear",
    "/Embarques/Detalle",
    "/Embarques/Documentacion",
    "/Embarques/DocumentacionCalidad",
    "/Embarques/EditarEmbarques",
    "/Embarques/Embarque",
    "/Embarques/MapaCarga",
    "/Embarques/TableroAeropuerto",
    "/Embarques/TrackingSIGO",
    "/Home",
    "/Home/Index",
    "/Home/Inicio",
    "/Home/Privacy",
    "/InventariosSistemas/ControlIPs",
    "/InventariosSistemas/InventariosSis",
    "/KPIS/Catalogo",
    "/KPIS/Kpis",
    "/KPIS/Ver",
    "/KpisAdmin/Asignar",
    "/KpisAdmin/Crear",
    "/KpisAdmin/Editar",
    "/KpisAdmin/InicioKpi",
    "/KpisPermisos/KpisPermisosConfiguracion",
    "/Mercados/MateriaPrima",
    "/Operaciones/Estudios",
    "/Operaciones/FactorCritico",
    "/Operaciones/Inyecciones",
    "/Operaciones/MapaCanales",
    "/Operaciones/Planeacion/Mensual",
    "/Operaciones/Planeacion/Semanal",
    "/Operaciones/PlaneadorMensual",
    "/Operaciones/PlaneadorProduccion",
    "/Permisos/CarouselConfiguracion",
    "/Permisos/CrearCarousel",
    "/Permisos/CrearPerfil",
    "/Permisos/CrearVista",
    "/Permisos/EditarVista",
    "/Permisos/EditarVistaForm",
    "/Permisos/EliminarPerfil",
    "/Permisos/EliminarVista",
    "/Permisos/PermisosConfiguracion",
    "/ProcesosCG/AuditoriaPesoManual",
    "/ProcesosCG/AutoArticulos",
    "/ProcesosCG/AvisosMovilizacion",
    "/ProcesosCG/AvisosMovilizacionPdf",
    "/ProcesosCG/CamarasInventario",
    "/ProcesosCG/Costeos",
    "/ProcesosCG/EmpaqueArticulos",
    "/ProcesosCG/EntregasSap",
    "/ProcesosCG/OrdenVentaPdf",
    "/ProcesosCG/Reimpresion",
    "/ProcesosCG/inventarioInicial",
    "/Reportes/Portal",
    "/Reportes/Visor",
    "/Sidebar/AdminSidebar",
    "/Sidebar/Administrar",
    "/Sidebar/Categorias",
    "/Sidebar/EditarModulo",
    "/Sidebar/PruebaSidebar",
    "/SkuConversion/SkuConversion",
    "/Transferencias/Calendario",
    "/Transferencias/OTransferencia",
    "/Transferencias/RomaneoTransferencia",
    "/Transferencias/SeleccionarParaSurtir",
    "/Transferencias/TransferenciasCedis",
    "/Transferencias/TransferenciasSucursal",
    "/Usuarios/Crear",
    "/Usuarios/Editar",
    "/Usuarios/UsuariosConfiguracion",
    "/UsuariosAD/Editar",
    "/UsuariosAD/UsuariosADConfiguracion"
];

/*
   Recursos estáticos detectados desde las vistas.
   Se cachean si existen; si alguno no existe, no detiene la instalación.
*/
const STATIC_ASSETS = [
    "/css/site.css",
    "/fonts/fonts.css",
    "/fonts/fonts.js",
    "/fonts/sap/72-Regular-full.woff2",
    "/images/Chamberete.png",
    "/images/Chuleton.png",
    "/images/Cola.png",
    "/images/Costilla Cargada.jpg",
    "/images/Costilla.jpeg",
    "/images/Cuña.jpeg",
    "/images/Diezmillo.jpg",
    "/images/Falda.jpg",
    "/images/LOGO_CARNESG.jpg",
    "/images/Muu.jpeg",
    "/images/OV/1.gif",
    "/images/OV/Guardar_2.gif",
    "/images/OV/Main.gif",
    "/images/OV/Reportes.gif",
    "/images/OV/Save.gif",
    "/images/Paleta.jpeg",
    "/images/Pecho.png",
    "/images/Pescuezo.jpg",
    "/images/Pulpa Blanca.jpeg",
    "/images/Pulpa Bola.jpg",
    "/images/Pulpa Negra.jpeg",
    "/images/ReImpresiones/etiquetas1-gif.gif",
    "/images/ReImpresiones/etiquetas2-gif.gif",
    "/images/Tapapescue.png",
    "/images/Top Sirlon.png",
    "/images/Usr.png",
    "/images/arrachera gallo.png",
    "/images/arrachera.png",
    "/images/banner-cow.jpg",
    "/images/canalres.png",
    "/images/conchita.jpeg",
    "/images/corte1.png",
    "/images/cortes/no-image.png",
    "/images/default-banner.jpg",
    "/images/distribucion.jpg",
    "/images/distribucion1.png",
    "/images/distribucion2.png",
    "/images/filete.jpg",
    "/images/giba.png",
    "/images/ico_sigo.ico",
    "/images/logoPDF.png",
    "/images/logoTIF.png",
    "/images/logo_carnesg.png",
    "/images/logo_sigo_cg2_1.png",
    "/images/logoinicio.png",
    "/images/proceso1.png",
    "/images/proceso2.png",
    "/images/produccion.png",
    "/images/ribeye.jpg",
    "/images/riñon.png",
    "/images/short-rib.png",
    "/images/sigo-anim.webp",
    "/images/sigoL.png",
    "/images/sigoNegro.png",
    "/images/sirlon.png",
    "/images/strip loin.png",
    "/images/suaderito.jpg",
    "/images/suadero.jpeg",
    "/images/tbone.jpg",
    "/images/textura.jpeg",
    "/js/embarque.js",
    "/js/inyeccion.js",
    "/js/manual.js",
    "/js/planDiario.js",
    "/js/planSemanal.js",
    "/js/planeacion-dia.js",
    "/js/planeacion-diapp.js",
    "/js/planeacion-mes.js",
    "/js/planpro.js",
    "/js/site.js",
    "/lib/bootstrap-icons/fonts/bootstrap-icons.css",
    "/lib/bootstrap/dist/css/bootstrap.css",
    "/lib/bootstrap/dist/css/bootstrap.min.css",
    "/lib/bootstrap/dist/js/bootstrap.bundle.min.js",
    "/lib/bootstrap/dist/js/bootstrap.js",
    "/lib/chartjs/chart.umd.min.js",
    "/lib/jquery-validation-unobtrusive/jquery.validate.unobtrusive.min.js",
    "/lib/jquery-validation/dist/jquery.validate.min.js",
    "/lib/jquery/dist/jquery.min.js",
    "/js/sigo-offline-inyecciones.js",
    "/js/sigo-catalogos-inyecciones.js",
     "/js/inyeccion.js"
];

/*
   Rutas que nunca deben cachearse.
   Aquí van login, logout, sincronización, acciones de escritura, exportaciones y operaciones sensibles.
*/
const NO_CACHE_EXACT = [
    "/Acceso/Login",
    "/Acceso/Logout",
    "/BasculaCamionera/Sync/Movimiento",
    "/BasculaCamionera/CatalogosOffline"
];

const NO_CACHE_KEYWORDS = [
    "/Guardar",
    "/Actualizar",
    "/Eliminar",
    "/Borrar",
    "/Cancelar",
    "/Autorizar",
    "/Rechazar",
    "/Crear",
    "/Editar",
    "/Modificar",
    "/Enviar",
    "/Cerrar",
    "/NuevaConversacion",
    "/Toggle",
    "/Set",
    "/Sync",
    "/Sincronizar",
    "/Exportar",
    "/Excel",
    "/Pdf",
    "/PDF",
    "/ReporteModal",
    "/Logout"
];

function normalizarPath(pathOrUrl) {
    try {
        const u = new URL(pathOrUrl);
        return u.pathname;
    } catch {
        return String(pathOrUrl || "").split("?")[0];
    }
}

function normalizarCacheKeyVista(pathOrUrl) {
    const path = normalizarPath(pathOrUrl);

    if (path.toLowerCase() === "/home/inicio") {
        return INICIO_URL;
    }

    if (path === "/" || path.toLowerCase() === "/home/index") {
        return LOGIN_URL;
    }

    return path;
}

function esVistaOffline(pathOrUrl) {
    const clean = normalizarCacheKeyVista(pathOrUrl).toLowerCase();

    return OFFLINE_VIEWS.some(function (view) {
        const v = normalizarCacheKeyVista(view).toLowerCase();

        return clean === v || clean.startsWith(v + "/");
    });
}

function esRecursoEstatico(request) {
    const path = normalizarPath(request.url);
    return /\.(css|js|png|jpg|jpeg|webp|gif|ico|svg|woff2|woff|ttf|eot|map)$/i.test(path);
}

function esRutaNoCacheable(pathOrUrl) {
    const p = normalizarPath(pathOrUrl);

    if (NO_CACHE_EXACT.some(function (x) { return p.toLowerCase() === x.toLowerCase(); })) {
        return true;
    }

    return NO_CACHE_KEYWORDS.some(function (k) {
        return p.toLowerCase().includes(k.toLowerCase());
    });
}

function esLoginResponse(response, requestedPath) {
    if (!response) return false;

    const finalUrl = response.url || "";
    const finalPath = finalUrl ? normalizarPath(finalUrl).toLowerCase() : "";
    const requested = normalizarPath(requestedPath).toLowerCase();

    if (requested !== "/home/index" && requested !== "/" && finalPath === "/home/index") {
        return true;
    }

    if (response.redirected && requested !== "/home/index" && requested !== "/") {
        return true;
    }

    if (finalUrl.includes("/Acceso/Login")) {
        return true;
    }

    return false;
}

function esJsonCacheable(pathOrUrl) {
    const p = normalizarPath(pathOrUrl);

    if (esRutaNoCacheable(p)) return false;

    if (p.toLowerCase() === SIDEBAR_MODULOS_URL.toLowerCase()) return true;

    const safePrefixes = [
        "/Obtener",
        "/Get",
        "/Buscar",
        "/Listar",
        "/Cargar",
        "/Areas",
        "/InventarioActual",
        "/ImpresorasLocales",
        "/LeerPeso",
        "/ProbarConexion"
    ];

    const lastSegment = "/" + p.split("/").filter(Boolean).pop();

    return safePrefixes.some(function (pref) {
        return lastSegment.toLowerCase().startsWith(pref.toLowerCase());
    });
}

async function cachearSiExiste(cacheName, url) {
    try {
        const cache = await caches.open(cacheName);
        await cache.add(url);
    } catch (err) {
        console.warn("[SIGO SW] No se pudo precachear:", url, err);
    }
}

self.addEventListener("install", function (event) {
    event.waitUntil((async function () {
        await cachearSiExiste(CACHE_PAGES, LOGIN_URL);

        for (const asset of STATIC_ASSETS) {
            await cachearSiExiste(CACHE_STATIC, asset);
        }
    })());

    self.skipWaiting();
});

self.addEventListener("activate", function (event) {
    event.waitUntil((async function () {
        const validCaches = [CACHE_STATIC, CACHE_PAGES, CACHE_DATA];
        const keys = await caches.keys();

        await Promise.all(
            keys
                .filter(function (key) { return !validCaches.includes(key); })
                .map(function (key) { return caches.delete(key); })
        );

        await self.clients.claim();
    })());
});

async function manejarStatic(request) {
    const cache = await caches.open(CACHE_STATIC);
    const cached = await cache.match(request);

    const networkPromise = fetch(request)
        .then(function (response) {
            if (response && response.status === 200) {
                cache.put(request, response.clone());
            }
            return response;
        })
        .catch(function () {
            return null;
        });

    return cached || networkPromise || new Response("", { status: 503, statusText: "Offline" });
}

async function manejarVista(request) {
    const path = normalizarPath(request.url);
    const cacheKey = normalizarCacheKeyVista(request.url);
    const cache = await caches.open(CACHE_PAGES);

    try {
        const response = await fetch(request);

        if (response && response.status === 200 && !esLoginResponse(response, path)) {
            const contentType = response.headers.get("content-type") || "";

            if (contentType.toLowerCase().includes("text/html")) {
                await cache.put(cacheKey, response.clone());
            }
        }

        return response;
    } catch (err) {
        const cachedView = await cache.match(cacheKey);
        if (cachedView) return cachedView;

        const cachedInicio = await cache.match(INICIO_URL);
        if (cachedInicio) return cachedInicio;

        const cachedLogin = await cache.match(LOGIN_URL);
        if (cachedLogin) return cachedLogin;

        return new Response("SIGO offline: esta vista aún no fue guardada. Abre la vista una vez con red y sesión válida.", {
            status: 503,
            statusText: "Offline",
            headers: { "Content-Type": "text/plain; charset=utf-8" }
        });
    }
}

async function manejarJsonCacheable(request) {
    const cache = await caches.open(CACHE_DATA);
    const cacheKey = request;

    try {
        const response = await fetch(request);

        if (response && response.status === 200) {
            const contentType = response.headers.get("content-type") || "";

            if (contentType.toLowerCase().includes("application/json")) {
                await cache.put(cacheKey, response.clone());
            }
        }

        return response;
    } catch (err) {
        const cached = await cache.match(cacheKey);
        if (cached) return cached;

        if (normalizarPath(request.url).toLowerCase() === SIDEBAR_MODULOS_URL.toLowerCase()) {
            return new Response("[]", {
                status: 200,
                headers: { "Content-Type": "application/json; charset=utf-8" }
            });
        }

        return new Response("[]", {
            status: 200,
            headers: { "Content-Type": "application/json; charset=utf-8" }
        });
    }
}

self.addEventListener("fetch", function (event) {
    const request = event.request;

    if (request.method !== "GET") {
        return;
    }

    const path = normalizarPath(request.url);

    if (esRutaNoCacheable(path)) {
        return;
    }

    if (esRecursoEstatico(request)) {
        event.respondWith(manejarStatic(request));
        return;
    }

    if (request.mode === "navigate" || esVistaOffline(path)) {
        event.respondWith(manejarVista(request));
        return;
    }

    if (esJsonCacheable(path)) {
        event.respondWith(manejarJsonCacheable(request));
        return;
    }

    event.respondWith(
        fetch(request)
            .then(function (response) {
                if (response && response.status === 200) {
                    const clone = response.clone();
                    caches.open(CACHE_DATA).then(function (cache) {
                        cache.put(request, clone);
                    });
                }
                return response;
            })
            .catch(function () {
                return caches.match(request).then(function (cached) {
                    if (cached) return cached;

                    if (request.mode === "navigate") {
                        return caches.open(CACHE_PAGES).then(function (cache) {
                            return cache.match(INICIO_URL).then(function (inicio) {
                                return inicio || cache.match(LOGIN_URL);
                            });
                        });
                    }

                    return new Response("SIGO offline: recurso no disponible.", {
                        status: 503,
                        statusText: "Offline",
                        headers: { "Content-Type": "text/plain; charset=utf-8" }
                    });
                });
            })
    );
});