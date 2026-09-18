let loteSeleccionado = null;       // ID del lote (long)
let plantillaSeleccionada = null;  // Plantilla (string)
let ultimoLoteSeleccionado = null;
let skuSeleccionado = null;        // SKU de producto seleccionado
let NombreSeleccionado = "";        // SKU de producto seleccionado
let basculaActiva = false;
let intervaloBascula = null;
let ipBasculaGlobal = "";
let comandoBasculaGlobal = "";
let taraSeleccionada = null;
let ipImpresoraGlobal = "";
let nombreLoteGlobal = "";
let correo = correoUsuario || "";
let usuarioAutorizaId = 0;
let intervaloRendimiento = null;
let productosCache = [];

// Paginación de "Últimas capturas"
let capturasTiempoRealData = [];
let capturasTiempoRealPagina = 1;
const capturasTiempoRealPorPagina = 8;
let ultimaEntradaParaImprimir = null;
let impresionEnProceso = false;
let toastImpresionTimeout = null;
let toastTimeout = null;
let pesoTaraActual = 0; // Peso de la tara actual
let taraDescripcion = ""; // Descripción de la tara seleccionada
let pesoBrutoSinTara = 0; // Para almacenar el peso bruto real
let ultimaCapturaPayload = null; // 🆕 Guardar el payload de la última captura para reimpresión
let ultimoPesoValido = "0.00";
let loadingActivo = false;
let secuenciaLoading = 0;
const operacionesLoading = new Map();
let erroresBasculaConsecutivos = 0;
let basculaEnCooldown = false;
let timerBascula = null;
let tiempoCooldownBascula = 3000;   // más descanso cuando falla
let maxErroresBascula = 20;          // no esperes 30 errores para reaccionar
let tiempoConsultaNormal = 900;       // lectura normal cuando el operador está usando la pantalla
let tiempoConsultaError = 1800;       // lectura más lenta si hay errores
let tiempoConsultaOculta = 10000;     // lectura mucho más lenta si cambian de pestaña/ventana
let debugBascula = false;
let leyendoBascula = false;
let modoManualActivo = false;
let pausaPorPestanaOculta = true;

// Identidad inmutable del producto que realmente terminó de cargar su receta.
let productoSeleccionadoActual = null;

// Fotografía de lote/producto/receta usada por la confirmación y la etiqueta.
let capturaPendiente = null;
let capturaEnProceso = false;

// Evita que una respuesta vieja de receta sobrescriba una selección más reciente.
let secuenciaSeleccionProducto = 0;

// Evita que una lista de productos atrasada se abra para un lote que ya cambió.
let secuenciaCargaProductos = 0;

// ======================================================
// CONTROL AFK REAL DEL OPERADOR
// La báscula NO debe contar como actividad del usuario.
// ======================================================

// Para pruebas:
//const TIEMPO_AFK_INYECCIONES_MS = 30 * 1000;

// Para producción, después cámbialo por algo así:
const TIEMPO_AFK_INYECCIONES_MS = 2 * 60 * 60 * 1000; // 2 horas

let timerAfkInyecciones = null;
let sesionCerrandosePorAfk = false;


document.addEventListener("DOMContentLoaded", async () => {
    iniciarControlAfkInyecciones();

    // UI de productos: tarjetas operativas e interactivas.
    // Se inyecta desde este mismo archivo para no depender de cambios adicionales en la vista.
    inyectarEstilosTarjetasProducto();
    configurarBusquedaProductos();

    await cargarLotes();
    await cargarTaras();

    cargarConfiguracionesGuardadas();
    cargarConfiguracionImpresora();

    // Si ya hay báscula guardada, iniciar lectura automáticamente.
    // Esto evita que guardarBascula() muestre alert cuando no haya configuración.
    if (ipBasculaGlobal && comandoBasculaGlobal) {
        iniciarLoopBascula();
    }
});

document.getElementById("SeleccionarTara").addEventListener("click", async () => {
    await cargarTaras();  // ← este llena el modal
    abrirModal("modalTara");

});
document.getElementById("loteSelect").addEventListener("change", async function () {
    const value = this.value;

    // Si intentaron dejarlo vacío, regresar al último válido
    if (!value) {
        restaurarUltimoLote();
        return;
    }

    const loadingId = mostrarLoading("Cargando productos del lote...");

    try {
        aplicarSeleccionLoteDesdeCombo(this);

        // Un producto del lote anterior nunca debe permanecer activo.
        limpiarProductoSeleccionado();

        await cargarProductosPorPlantilla(
            plantillaSeleccionada,
            String(loteSeleccionado)
        );

        if (intervaloRendimiento) {
            clearInterval(intervaloRendimiento);
        }

        await cargarRendimientoTiempoReal();

    } catch (err) {
        console.error(err);
    } finally {
        ocultarLoading(loadingId);
    }
});

document.getElementById("loteSelect").addEventListener("blur", function () {
    // Si quedó en vacío por abrir/cerrar sin seleccionar, restaurar
    if (!this.value && ultimoLoteSeleccionado) {
        restaurarUltimoLote();
    }
});


document.getElementById("loteSelect").addEventListener("focus", async function () {
    await cargarLotes();
});

async function refrescarYAbrirModalProducto() {

    if (!plantillaSeleccionada) {
        alert("Debe seleccionar un lote primero");
        return;
    }

    const loadingId = mostrarLoading("Cargando productos...");

    try {
        await cargarProductosPorPlantilla(
            plantillaSeleccionada,
            String(loteSeleccionado)
        );
    } catch (error) {
        console.error("❌ Error al refrescar productos:", error);
    }
    finally {
        ocultarLoading(loadingId);
    }
}

async function cargarImagenProducto(nombre, sku) {
    const resp = await fetch(`/api/Inyeccion/ObtenerImagen?nombre=${encodeURIComponent(nombre)}&sku=${encodeURIComponent(sku)}`);
    if (!resp.ok) throw new Error(`No se pudo cargar la imagen del SKU ${sku}.`);
    const blob = await resp.blob();
    return URL.createObjectURL(blob);
}
async function cargarLotes(loteAConservar = null) {
    try {
        const resp = await fetch("/api/Inyeccion/ObtenerLotes");
        if (!resp.ok) throw new Error("Error al cargar lotes");

        const lotes = await resp.json();

        const combo = document.getElementById("loteSelect");
        const valorActual = loteAConservar || combo.value || loteSeleccionado || "";

        combo.innerHTML = `<option value="">Seleccione…</option>`;

        lotes.forEach(l => {
            const opt = document.createElement("option");
            opt.value = String(l.loteId);
            opt.dataset.plantilla = l.plantilla;
            opt.dataset.nombre = l.nombre;
            opt.dataset.lote = l.lote;
            opt.textContent = `${l.lote} — ${l.nombre}`;
            combo.appendChild(opt);
        });

        // Restaurar selección previa si aún existe
        if (valorActual && [...combo.options].some(o => o.value === String(valorActual))) {
            combo.value = String(valorActual);
        } else if (ultimoLoteSeleccionado &&
            [...combo.options].some(o => o.value === String(ultimoLoteSeleccionado))) {
            combo.value = String(ultimoLoteSeleccionado);
        }

    } catch (err) {
        console.error("❌ Error al cargar lotes:", err);
    }
}

function aplicarSeleccionLoteDesdeCombo(combo) {
    const opt = combo.options[combo.selectedIndex];
    if (!opt || !opt.value) return;

    loteSeleccionado = combo.value;
    ultimoLoteSeleccionado = combo.value;

    plantillaSeleccionada = opt.dataset.plantilla || null;
    nombrePlantilla = opt.dataset.nombre || "";
    nombreLoteGlobal = opt.dataset.lote || "";

    document.getElementById("programacionActual").textContent = nombrePlantilla || "—";
}

function restaurarUltimoLote() {
    const combo = document.getElementById("loteSelect");

    if (!ultimoLoteSeleccionado) {
        combo.value = "";
        return;
    }

    const existe = [...combo.options].some(o => o.value === String(ultimoLoteSeleccionado));
    if (!existe) return;

    combo.value = String(ultimoLoteSeleccionado);
    aplicarSeleccionLoteDesdeCombo(combo);
}


// ======================================================
// UI PRODUCTOS - TARJETAS INTERACTIVAS
// Todo el estilo vive aquí para no requerir cambios en la vista .cshtml.
// ======================================================
function inyectarEstilosTarjetasProducto() {
    if (document.getElementById("iny-product-card-styles")) return;

    const style = document.createElement("style");
    style.id = "iny-product-card-styles";

    style.textContent = `
        /* Modal de productos */
        #modalProducto {
            width: min(96vw, 1180px) !important;
        }

        #modalProducto .modal-header {
            font-weight: 800 !important;
            color: #111820 !important;
            background: #ffffff !important;
            border-bottom: 1px solid #d9e1e8 !important;
        }

        #modalProducto .op-search-bar {
            position: sticky;
            top: 0;
            z-index: 6;
            padding: 0 0 12px !important;
            background: #ffffff;
        }

        #modalProducto #searchProducto {
            width: 100% !important;
            min-height: 48px !important;
            padding: 0 14px !important;
            border: 1px solid #cfd9e3 !important;
            border-radius: 12px !important;
            background: #ffffff !important;
            color: #1d2d3d !important;
            font-size: 13px !important;
            outline: none !important;
            box-shadow: none !important;
        }

        #modalProducto #searchProducto:focus {
            border-color: #7f9bb3 !important;
            box-shadow: 0 0 0 3px rgba(70, 100, 125, .10) !important;
        }

        #modalProducto #resultadosBusqueda {
            min-height: 15px;
            margin-top: 5px !important;
            font-size: 11px !important;
            font-weight: 700;
            color: #60758a !important;
        }

        #modalProducto .modal-body {
            max-height: 72vh !important;
            overflow-y: auto !important;
            padding: 14px !important;
            background: #f3f6f9 !important;
            -webkit-overflow-scrolling: touch;
        }

        /* Scroll discreto */
        #modalProducto .modal-body::-webkit-scrollbar {
            width: 7px;
        }

        #modalProducto .modal-body::-webkit-scrollbar-track {
            background: transparent;
        }

        #modalProducto .modal-body::-webkit-scrollbar-thumb {
            background: #b7c2cc;
            border-radius: 999px;
        }

        #modalProducto .modal-body::-webkit-scrollbar-thumb:hover {
            background: #96a5b2;
        }

        /* Grid */
        #contenedorProductos.product-grid,
        #contenedorProductos {
            width: 100% !important;
            display: grid !important;
            grid-template-columns: repeat(auto-fill, minmax(180px, 1fr)) !important;
            gap: 10px !important;
            align-items: stretch !important;
        }

        /* Tarjeta completa */
        #contenedorProductos .product-card {
            appearance: none !important;
            -webkit-appearance: none !important;
            position: relative !important;
            width: 100% !important;
            min-width: 0 !important;
            min-height: 204px !important;
            margin: 0 !important;
            padding: 9px !important;

            display: flex !important;
            flex-direction: column !important;
            gap: 7px !important;

            border: 1px solid #d6e0e8 !important;
            border-radius: 14px !important;
            background: #ffffff !important;
            color: #182b3d !important;
            text-align: left !important;

            box-shadow: 0 3px 10px rgba(30, 49, 65, .07) !important;
            cursor: pointer !important;
            overflow: hidden !important;

            transition:
                transform .15s ease,
                border-color .15s ease,
                box-shadow .15s ease,
                background .15s ease !important;
        }

        #contenedorProductos .product-card:hover {
            transform: translateY(-3px) !important;
            border-color: #8da9bd !important;
            background: #fbfdff !important;
            box-shadow: 0 10px 22px rgba(30, 49, 65, .14) !important;
        }

        #contenedorProductos .product-card:focus-visible {
            outline: 3px solid rgba(46, 108, 153, .20) !important;
            outline-offset: 2px !important;
            border-color: #5f88a7 !important;
        }

        #contenedorProductos .product-card:active {
            transform: translateY(-1px) scale(.99) !important;
        }

        #contenedorProductos .product-card.is-selected {
            border-color: #487a9e !important;
            box-shadow:
                0 0 0 3px rgba(72, 122, 158, .12),
                0 10px 22px rgba(30, 49, 65, .13) !important;
        }

        /* Imagen */
        #contenedorProductos .product-card-image {
            width: 100% !important;
            height: 88px !important;
            display: grid !important;
            place-items: center !important;
            overflow: hidden !important;

            border: 1px solid #e1e8ee !important;
            border-radius: 11px !important;
            background: linear-gradient(180deg, #ffffff 0%, #f2f5f8 100%) !important;
        }

        #contenedorProductos .product-card-image img {
            display: block !important;
            width: 100% !important;
            height: 100% !important;
            padding: 5px !important;
            object-fit: contain !important;
        }

        #contenedorProductos .product-card-image-empty {
            width: 100% !important;
            height: 100% !important;
            display: grid !important;
            place-items: center !important;
            color: #8191a0 !important;
            font-size: 31px !important;
            background: #f2f5f8 !important;
        }

        /* Fila SKU + porcentaje */
        #contenedorProductos .product-card-meta {
            width: 100% !important;
            display: grid !important;
            grid-template-columns: minmax(0, 1fr) auto !important;
            gap: 7px !important;
            align-items: center !important;
        }

        /* SKU en su propio recuadro */
        #contenedorProductos .product-card-sku {
            min-width: 0 !important;
            min-height: 27px !important;
            padding: 5px 8px !important;

            display: flex !important;
            align-items: center !important;

            border: 1px solid #ccd9e4 !important;
            border-radius: 8px !important;
            background: #edf3f7 !important;
            color: #18364f !important;

            font-size: 13px !important;
            line-height: 1 !important;
            font-weight: 900 !important;
            letter-spacing: .15px !important;

            overflow: hidden !important;
            text-overflow: ellipsis !important;
            white-space: nowrap !important;
        }

        /* Porcentaje destacado, pero discreto */
        #contenedorProductos .product-card-percent {
            min-width: 48px !important;
            min-height: 27px !important;
            padding: 5px 7px !important;

            display: flex !important;
            align-items: center !important;
            justify-content: center !important;

            border: 1px solid #e2c882 !important;
            border-radius: 8px !important;
            background: #fff8e7 !important;
            color: #765100 !important;

            font-size: 13px !important;
            line-height: 1 !important;
            font-weight: 900 !important;
            white-space: nowrap !important;
        }

        /* Nombre separado del SKU y del porcentaje */
        #contenedorProductos .product-card-name-box {
            min-height: 46px !important;
            padding: 7px 8px !important;

            display: flex !important;
            align-items: flex-start !important;

            border: 1px solid #e0e6eb !important;
            border-radius: 9px !important;
            background: #fafcfd !important;
        }

        #contenedorProductos .product-card-name {
            width: 100% !important;
            margin: 0 !important;
            color: #21364a !important;

            font-size: 12px !important;
            line-height: 1.28 !important;
            font-weight: 750 !important;

            display: -webkit-box !important;
            -webkit-line-clamp: 3 !important;
            -webkit-box-orient: vertical !important;
            overflow: hidden !important;
        }

        /* Acción inferior */
        #contenedorProductos .product-card-action {
            min-height: 28px !important;
            margin-top: auto !important;
            padding: 5px 8px !important;

            display: flex !important;
            align-items: center !important;
            justify-content: space-between !important;
            gap: 7px !important;

            border: 1px solid #17344b !important;
            border-radius: 8px !important;
            background: #173a55 !important;
            color: #ffffff !important;

            font-size: 10px !important;
            line-height: 1 !important;
            font-weight: 800 !important;
        }

        #contenedorProductos .product-card-action strong {
            color: #ffffff !important;
            font-size: 10px !important;
        }

        #contenedorProductos .product-card-action-icon {
            width: 24px !important;
            height: 24px !important;
            flex: 0 0 24px !important;

            display: grid !important;
            place-items: center !important;

            border-radius: 999px !important;
            background: #ffffff !important;
            color: #173a55 !important;
            font-size: 12px !important;
        }

        /* El buscador usa esta clase.
           Debe ganar al display:flex !important de la tarjeta. */
        #contenedorProductos .product-card.product-filter-hidden {
            display: none !important;
        }

        /* Paginador compacto de Últimas capturas */
        #paginadorCapturasTiempoReal {
            width: 100%;
            margin-top: 5px;
            padding: 4px 2px 0;
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: 5px;
            color: #63788b;
            font-size: 8px;
            font-weight: 800;
        }

        #paginadorCapturasTiempoReal .iny-cap-page-info {
            flex: 1;
            text-align: center;
            white-space: nowrap;
        }

        #paginadorCapturasTiempoReal button {
            min-width: 27px;
            height: 24px;
            padding: 0 7px;
            border: 1px solid #cbd6df;
            border-radius: 7px;
            background: #f7fafc;
            color: #29465f;
            font-size: 10px;
            font-weight: 900;
            cursor: pointer;
        }

        #paginadorCapturasTiempoReal button:hover:not(:disabled) {
            background: #eaf1f6;
        }

        #paginadorCapturasTiempoReal button:disabled {
            opacity: .38;
            cursor: default;
        }

        /* Tablet */
        @media (max-width: 1000px) {
            #contenedorProductos.product-grid,
            #contenedorProductos {
                grid-template-columns: repeat(4, minmax(0, 1fr)) !important;
            }

            #contenedorProductos .product-card {
                min-height: 198px !important;
            }
        }

        @media (max-width: 760px) {
            #modalProducto {
                width: 96vw !important;
            }

            #contenedorProductos.product-grid,
            #contenedorProductos {
                grid-template-columns: repeat(2, minmax(0, 1fr)) !important;
            }
        }

        @media (max-width: 480px) {
            #contenedorProductos.product-grid,
            #contenedorProductos {
                grid-template-columns: 1fr !important;
            }
        }
    `;

    document.head.appendChild(style);
}

async function cargarProductosPorPlantilla(plantilla, loteIdEsperado = String(loteSeleccionado ?? "")) {
    const cargaId = ++secuenciaCargaProductos;
    const plantillaEsperada = String(plantilla ?? "");

    try {
        const resp = await fetch(
            `/api/Inyeccion/ListarProductos?plan=${encodeURIComponent(plantillaEsperada)}`,
            { cache: "no-store" }
        );

        if (!resp.ok) throw new Error("Error al cargar productos");

        const productos = await resp.json();

        if (
            cargaId !== secuenciaCargaProductos ||
            String(loteSeleccionado ?? "") !== loteIdEsperado ||
            String(plantillaSeleccionada ?? "") !== plantillaEsperada
        ) {
            console.warn("⚠ Se descartó una lista de productos atrasada.");
            return false;
        }

        const productosConImagen = await Promise.all(productos.map(async p => {
            try {
                return {
                    ...p,
                    imgUrl: await cargarImagenProducto(p.nombre, p.sku)
                };
            } catch (error) {
                console.warn("⚠ No se pudo cargar imagen de producto:", p.sku, error);
                return { ...p, imgUrl: "" };
            }
        }));

        if (
            cargaId !== secuenciaCargaProductos ||
            String(loteSeleccionado ?? "") !== loteIdEsperado ||
            String(plantillaSeleccionada ?? "") !== plantillaEsperada
        ) {
            console.warn("⚠ Se descartaron productos de un lote que ya no está activo.");
            return false;
        }

        const cont = document.getElementById("contenedorProductos");
        cont.innerHTML = "";

        for (const p of productosConImagen) {
            const sku = String(p.sku ?? "").trim();
            const nombre = String(p.nombre ?? "").trim();

            const porcentajeRaw = p.porcentaje ?? p.Porcentaje;
            const porcentajeNumero = Number(porcentajeRaw);
            const porcentajeTexto = Number.isFinite(porcentajeNumero)
                ? `${porcentajeNumero}%`
                : "—";

            // La tarjeta es un botón real: funciona con mouse, touch y teclado.
            const card = document.createElement("button");
            card.type = "button";
            card.className = "product-card";
            card.dataset.sku = sku.toLowerCase();
            card.dataset.nombre = nombre.toLowerCase();
            card.setAttribute("aria-label", `Seleccionar ${sku} ${nombre}`);

            // Imagen en recuadro independiente.
            const imageBox = document.createElement("div");
            imageBox.className = "product-card-image";

            if (p.imgUrl) {
                const img = document.createElement("img");
                img.src = p.imgUrl;
                img.alt = nombre || sku;
                img.loading = "lazy";
                imageBox.appendChild(img);
            } else {
                const empty = document.createElement("div");
                empty.className = "product-card-image-empty";
                empty.textContent = "📦";
                empty.setAttribute("aria-hidden", "true");
                imageBox.appendChild(empty);
            }

            // SKU y porcentaje quedan separados en la misma fila.
            const meta = document.createElement("div");
            meta.className = "product-card-meta";

            const skuElement = document.createElement("span");
            skuElement.className = "product-card-sku";
            skuElement.textContent = sku || "SIN SKU";
            skuElement.title = sku || "Sin SKU";

            const porcentajeElement = document.createElement("span");
            porcentajeElement.className = "product-card-percent";
            porcentajeElement.textContent = porcentajeTexto;
            porcentajeElement.title = "Porcentaje";

            meta.append(skuElement, porcentajeElement);

            // Nombre en un recuadro separado.
            const nameBox = document.createElement("div");
            nameBox.className = "product-card-name-box";

            const nombreElement = document.createElement("div");
            nombreElement.className = "product-card-name";
            nombreElement.textContent = nombre || "Producto sin nombre";
            nombreElement.title = nombre || "Producto sin nombre";
            nameBox.appendChild(nombreElement);

            // Indicador inferior para dejar claro que la tarjeta es seleccionable.
            const action = document.createElement("div");
            action.className = "product-card-action";

            const actionText = document.createElement("strong");
            actionText.textContent = "Seleccionar producto";

            const actionIcon = document.createElement("span");
            actionIcon.className = "product-card-action-icon";
            actionIcon.textContent = "›";
            actionIcon.setAttribute("aria-hidden", "true");

            action.append(actionText, actionIcon);

            card.append(imageBox, meta, nameBox, action);

            card.addEventListener("click", async () => {
                document.querySelectorAll("#contenedorProductos .product-card")
                    .forEach(x => x.classList.remove("is-selected"));

                card.classList.add("is-selected");

                await seleccionarProducto(sku, nombre);
            });

            cont.appendChild(card);
        }

        abrirModal('modalProducto');

        // Limpiar búsqueda al abrir
        document.getElementById('searchProducto').value = '';
        document.getElementById('resultadosBusqueda').textContent = '';

        return true;
    } catch (err) {
        console.error("❌ Error al cargar productos:", err);
        return false;
    }
}
function normalizarSku(valor) {
    return String(valor ?? "").trim().toUpperCase();
}

function limpiarProductoSeleccionado(invalidarSolicitudPendiente = true) {
    if (invalidarSolicitudPendiente) {
        secuenciaSeleccionProducto++;
    }

    productoSeleccionadoActual = null;
    skuSeleccionado = null;
    NombreSeleccionado = "";
    capturaPendiente = null;

    const productoSeleccionado = document.getElementById("productoSeleccionado");
    if (productoSeleccionado) productoSeleccionado.textContent = "—";

    const ids = ["sku", "porcentaje", "velocidad", "producto", "modo", "presion", "altura", "avance"];
    ids.forEach(id => {
        const elemento = document.getElementById(id);
        if (elemento) elemento.value = "";
    });
}

async function seleccionarProducto(sku, nombre) {
    const seleccionId = ++secuenciaSeleccionProducto;
    const skuSolicitado = String(sku ?? "").trim();
    const nombreSolicitado = String(nombre ?? "").trim();
    const loteIdSeleccion = String(loteSeleccionado ?? "");
    const plantillaSeleccion = String(plantillaSeleccionada ?? "");

    const loadingId = mostrarLoading("Cargando receta...");

    try {
        const receta = await cargarReceta(skuSolicitado, nombreSolicitado, seleccionId);

        // Si otra selección empezó después, esta respuesta ya no tiene permiso de cambiar el estado.
        if (!receta || seleccionId !== secuenciaSeleccionProducto) return;

        if (
            String(loteSeleccionado ?? "") !== loteIdSeleccion ||
            String(plantillaSeleccionada ?? "") !== plantillaSeleccion
        ) {
            throw new Error("El lote cambió mientras se cargaba el producto. Selecciónelo nuevamente.");
        }

        const skuReceta = String(receta.sku ?? receta.SKU ?? skuSolicitado).trim();

        if (normalizarSku(skuReceta) !== normalizarSku(skuSolicitado)) {
            throw new Error(`La receta recibida pertenece al SKU ${skuReceta}, no al SKU ${skuSolicitado}.`);
        }

        productoSeleccionadoActual = Object.freeze({
            sku: skuReceta,
            nombre: nombreSolicitado,
            loteId: loteIdSeleccion,
            plantilla: plantillaSeleccion,
            receta: Object.freeze({
                Porcentaje: Number(receta.porcentaje ?? 0),
                Velocidad: Number(receta.velocidad ?? 0),
                ModoInyeccion: Number(receta.modoInyeccion ?? 0),
                Presion: Number(receta.presion ?? 0),
                Altura: Number(receta.altura ?? 0),
                Avance: String(receta.avance ?? "")
            })
        });

        skuSeleccionado = productoSeleccionadoActual.sku;
        NombreSeleccionado = productoSeleccionadoActual.nombre; // Se conserva solo por compatibilidad visual.

        document.getElementById("productoSeleccionado").textContent = productoSeleccionadoActual.nombre;
        cerrarModal("modalProducto");

        console.log("✔ Producto confirmado:", productoSeleccionadoActual);
    } catch (err) {
        // Un error de una solicitud vieja tampoco puede limpiar una selección nueva.
        if (seleccionId !== secuenciaSeleccionProducto) return;

        console.error("❌ Error seleccionando producto:", err);
        limpiarProductoSeleccionado(false);
        alert(err.message || "No fue posible cargar el producto seleccionado.");
    } finally {
        ocultarLoading(loadingId);
    }
}

async function cargarReceta(sku, nombre, seleccionId) {
    const resp = await fetch(`/api/Inyeccion/ObtenerReceta?sku=${encodeURIComponent(sku)}`, {
        cache: "no-store"
    });

    if (!resp.ok) throw new Error("No se encontró la receta");

    const receta = await resp.json();

    // Una respuesta atrasada no debe escribir en la pantalla.
    if (seleccionId !== secuenciaSeleccionProducto) return null;

    const skuReceta = String(receta.sku ?? receta.SKU ?? "").trim();
    if (!skuReceta || normalizarSku(skuReceta) !== normalizarSku(sku)) {
        throw new Error("La receta recibida no coincide con el SKU seleccionado.");
    }

    document.getElementById("sku").value = skuReceta;
    document.getElementById("porcentaje").value = receta.porcentaje;
    document.getElementById("velocidad").value = receta.velocidad;
    document.getElementById("producto").value = nombre;
    document.getElementById("modo").value = receta.modoInyeccion;
    document.getElementById("presion").value = receta.presion;
    document.getElementById("altura").value = receta.altura;
    document.getElementById("avance").value = receta.avance;

    console.log("✔ Receta cargada:", receta);

    try {
        const ruta = await cargarImagenProducto(nombre, skuReceta);
        if (seleccionId === secuenciaSeleccionProducto) {
            mostrarImagenProducto(ruta);
        }
    } catch (errorImagen) {
        // La imagen es informativa; su ausencia no invalida una receta correcta.
        console.warn("⚠ No se pudo cargar la imagen del producto:", skuReceta, errorImagen);
    }

    return receta;
}
function mostrarImagenProducto(ruta) {

    const imgDetalle = document.getElementById("imagenProducto");

    imgDetalle.style.opacity = "0";
    imgDetalle.style.transform = "scale(.7)";

    setTimeout(() => {
        imgDetalle.src = ruta;
        imgDetalle.style.opacity = "1";
        imgDetalle.style.transform = "scale(1)";
    }, 150);
}
function guardarBascula() {
    // Leer valores escritos en pantalla
    ipBasculaGlobal = document.getElementById("ipBascula").value.trim();
    comandoBasculaGlobal = document.getElementById("comandoBascula").value.trim();

    if (!ipBasculaGlobal || !comandoBasculaGlobal) {
        mostrarToast("Debe configurar IP y comando para la báscula", "error");
        return;
    }

    localStorage.setItem("ipBascula", ipBasculaGlobal);
    localStorage.setItem("comandoBascula", comandoBasculaGlobal);

    cerrarModal('modalBascula');

    iniciarLoopBascula();

    console.log("✔ Báscula activada con:", ipBasculaGlobal, comandoBasculaGlobal);
}
function cargarConfiguracionesGuardadas() {
    const ipGuardada = localStorage.getItem("ipBascula");
    const comandoGuardado = localStorage.getItem("comandoBascula");

    if (ipGuardada) {
        document.getElementById("ipBascula").value = ipGuardada;
        document.getElementById("metaBascula").textContent =
            `IP: ${ipGuardada}${comandoGuardado ? " | Cmd: " + comandoGuardado : ""}`;
        ipBasculaGlobal = ipGuardada;
    }

    if (comandoGuardado) {
        document.getElementById("comandoBascula").value = comandoGuardado;
        comandoBasculaGlobal = comandoGuardado;
    }

    console.log("⚙ Configuración restaurada:", {
        ip: ipBasculaGlobal,
        comando: comandoBasculaGlobal
    });
}

// ======================================================
// CONTROL REAL DE INACTIVIDAD DEL OPERADOR - INYECCIONES
// ======================================================

function reiniciarTimerAfkInyecciones() {
    if (sesionCerrandosePorAfk) return;

    if (timerAfkInyecciones) {
        clearTimeout(timerAfkInyecciones);
    }

    timerAfkInyecciones = setTimeout(() => {
        cerrarSesionPorAfkInyecciones();
    }, TIEMPO_AFK_INYECCIONES_MS);
}

function iniciarControlAfkInyecciones() {
    const eventosUsuario = [
        "click",
        "keydown",
        "mousedown",
        "mouseup",
        "mousemove",
        "touchstart",
        "touchmove",
        "touchend",
        "pointerdown",
        "pointermove",
        "pointerup",
        "scroll",
        "change",
        "input",
        "focusin"
    ];

    eventosUsuario.forEach(evento => {
        document.addEventListener(evento, reiniciarTimerAfkInyecciones, true);
    });

    reiniciarTimerAfkInyecciones();
}

async function cerrarSesionPorAfkInyecciones() {
    if (sesionCerrandosePorAfk) return;

    sesionCerrandosePorAfk = true;

    try {
        // 1. Detener la báscula para que ya no siga pegándole al backend
        if (typeof detenerLoopBascula === "function") {
            detenerLoopBascula();
        }

        // 2. Detener peso vivo del modal de confirmación si estaba abierto
        if (typeof detenerPesoVivoConfirmacion === "function") {
            detenerPesoVivoConfirmacion();
        }

        // 3. Detener rendimiento en tiempo real si está corriendo
        if (typeof intervaloRendimiento !== "undefined" && intervaloRendimiento) {
            clearInterval(intervaloRendimiento);
            intervaloRendimiento = null;
        }

        // 4. Redirigir cerrando sesión real
        const returnUrl = window.location.pathname + window.location.search;

        window.location.href =
            "/Acceso/Logout?expirada=1&returnUrl=" + encodeURIComponent(returnUrl);

    } catch (error) {
        console.error("Error cerrando sesión por AFK:", error);

        const returnUrl = window.location.pathname + window.location.search;

        window.location.href =
            "/Home/Index?expirada=1&returnUrl=" + encodeURIComponent(returnUrl);
    }
}

function limpiarPesoBasculaFrontend(valor) {
    if (valor === null || valor === undefined) return "";

    let texto = String(valor)
        .replace(/"/g, "")
        .replace(/'/g, "")
        .replace(/kg/gi, "")
        .replace(/\r/g, "")
        .replace(/\n/g, "")
        .replace(/\t/g, "")
        .trim();

    texto = texto.replace(/\s+/g, "");
    texto = texto.replace(",", ".");

    const match = texto.match(/[-+]?\d+(\.\d+)?/);

    if (!match) return "";

    return match[0];
}

async function consultarBascula() {
    if (modoManualActivo) return false;
    if (basculaEnCooldown) {
        logBascula("⏸ En cooldown...");
        return false;
    }
    if (leyendoBascula) return false;

    leyendoBascula = true;

    const ipBascula = ipBasculaGlobal || document.getElementById("ipBascula").value.trim();
    const comando = comandoBasculaGlobal || document.getElementById("comandoBascula").value.trim();

    if (!ipBascula || !comando) {
        leyendoBascula = false;
        return false;
    }

    try {
        const controller = new AbortController();
        const timeoutId = setTimeout(() => controller.abort(), 1200);

        const resp = await fetch(
            `/api/Inyeccion/ObtenerPeso?ip=${encodeURIComponent(ipBascula)}&comando=${encodeURIComponent(comando)}`,
            {
                signal: controller.signal,
                cache: "no-store"
            }
        );

        clearTimeout(timeoutId);

        if (!resp.ok) throw new Error(`HTTP ${resp.status}`);

        const respuestaRaw = (await resp.text()).trim();

        const peso = limpiarPesoBasculaFrontend(respuestaRaw);

        if (!isNaN(parseFloat(peso)) && peso !== "" && respuestaRaw !== "Error") {
            if (peso !== ultimoPesoValido) {
                ultimoPesoValido = peso;
                actualizarPesoP(peso);
            }

            erroresBasculaConsecutivos = 0;
            logBascula("✅ Peso recibido:", {
                raw: respuestaRaw,
                limpio: peso
            });
            return true;
        }

        erroresBasculaConsecutivos++;
        logBascula("⚠ Valor inválido:", {
            raw: respuestaRaw,
            limpio: peso
        });

        actualizarPesoP(ultimoPesoValido);

        if (erroresBasculaConsecutivos >= maxErroresBascula) {
            activarCooldownBascula();
        }

        return false;
    } catch (err) {
        erroresBasculaConsecutivos++;
        logBascula("❌ Error conexión:", err?.name || err?.message);

        actualizarPesoP(ultimoPesoValido);

        if (erroresBasculaConsecutivos >= maxErroresBascula) {
            activarCooldownBascula();
        }

        return false;
    } finally {
        leyendoBascula = false;
    }
}
//////////////////////////////////////////////////////////////////////////
function actualizarPesoP(peso) {
    let pesoConvertido = parseFloat(peso || "0");

    // restar tara si existe
    try {
        if (taraSeleccionada) {
            pesoConvertido = pesoConvertido - taraSeleccionada.peso;
        }

        if (pesoConvertido < 0) pesoConvertido = 0;
    } catch (e) {

    }

    const etiquetaPeso = document.getElementById("pesoActual");

    etiquetaPeso.textContent = pesoConvertido.toFixed(2);

    //etiquetaPeso.style.transform = "scale(1.25)";
    //etiquetaPeso.style.transition = ".15s ease-out";

    setTimeout(() => {
        //  etiquetaPeso.style.transform = "scale(1)";
    }, 100);
}
async function cargarTaras() {
    try {
        const resp = await fetch("/api/Inyeccion/ObtenerTaras");
        if (!resp.ok) throw new Error("Error al cargar taras");

        const taras = await resp.json();

        const cont = document.getElementById("contenedorTaras");
        cont.innerHTML = "";

        taras.forEach(t => {
            cont.innerHTML += `
                <div class="tara-item" onclick="seleccionarTara(${t.id}, '${t.descripcion}', ${t.peso})">
                    <div class="tara-title">${t.descripcion}</div>
                    <div class="tara-weight">${t.peso.toFixed(3)} kg</div>
                </div>
            `;
        });

    } catch (e) {
        console.error("❌ Error cargando taras:", e);
    }
}
function seleccionarTara(id, descripcion, peso) {

    taraSeleccionada = { id, descripcion, peso };


    document.getElementById("tara").value = peso.toFixed(3);

    actualizarPesoP(ultimoPesoValido);

    cerrarModal("modalTara");

    console.log("✔ Tara aplicada:", taraSeleccionada);
}
function abrirModal(id) {
    document.getElementById(id).style.display = "block";
    document.getElementById("overlay").style.display = "block";
}
function cerrarModal(id) {
    document.getElementById(id).style.display = "none";
    document.getElementById("overlay").style.display = "none";
}

function crearSnapshotCaptura() {
    const comboLote = document.getElementById("loteSelect");
    const opcionLote = comboLote?.options?.[comboLote.selectedIndex];
    const skuPantalla = String(document.getElementById("sku")?.value ?? "").trim();
    const loteIdActual = String(loteSeleccionado ?? "");
    const plantillaActual = String(plantillaSeleccionada ?? "");

    if (!comboLote?.value || !loteSeleccionado || !plantillaSeleccionada) {
        throw new Error("Debe seleccionar un lote antes de capturar.");
    }

    if (!productoSeleccionadoActual) {
        throw new Error("Debe seleccionar un producto antes de capturar.");
    }

    if (normalizarSku(productoSeleccionadoActual.sku) !== normalizarSku(skuPantalla)) {
        throw new Error("El nombre del producto y el SKU en pantalla no corresponden. Seleccione nuevamente el producto.");
    }

    if (
        String(comboLote.value) !== loteIdActual ||
        String(opcionLote?.dataset?.plantilla ?? "") !== plantillaActual ||
        productoSeleccionadoActual.loteId !== loteIdActual ||
        productoSeleccionadoActual.plantilla !== plantillaActual
    ) {
        throw new Error("El producto ya no corresponde al lote activo. Selecciónelo nuevamente.");
    }

    const loteImpresion = String(
        opcionLote?.dataset?.lote ||
        nombreLoteGlobal ||
        opcionLote?.textContent ||
        ""
    ).trim();

    if (!loteImpresion) {
        throw new Error("No se pudo determinar el lote para la etiqueta.");
    }

    const receta = productoSeleccionadoActual.receta;
    const valoresNumericos = [
        receta.Porcentaje,
        receta.Velocidad,
        receta.ModoInyeccion,
        receta.Presion,
        receta.Altura
    ];

    if (!valoresNumericos.every(Number.isFinite)) {
        throw new Error("La receta contiene valores inválidos. Seleccione nuevamente el producto.");
    }

    return Object.freeze({
        SKU: skuPantalla,
        ProductoSKU: skuPantalla,
        Producto: productoSeleccionadoActual.nombre,
        LoteId: loteIdActual,
        Plantilla: plantillaActual,
        LoteImpresion: loteImpresion,
        Porcentaje: receta.Porcentaje,
        Velocidad: receta.Velocidad,
        ModoInyeccion: receta.ModoInyeccion,
        Presion: receta.Presion,
        Altura: receta.Altura,
        Avance: receta.Avance,
        Tara: taraSeleccionada ? Number(taraSeleccionada.peso || 0) : 0,
        TipoPeso: modoManualActivo ? "Man" : "Aut",
        Autoriza: usuarioAutorizaId,
        Bascula: String(ipBasculaGlobal ?? ""),
        UsSIGO: String(correo ?? "")
    });
}

function abrirConfirmacionCaptura() {
    try {
        capturaPendiente = crearSnapshotCaptura();

        const peso = obtenerPesoActual();

        document.getElementById("confirmLote").textContent = capturaPendiente.LoteImpresion;
        document.getElementById("confirmProducto").textContent = capturaPendiente.Producto;
        document.getElementById("confirmSku").textContent = capturaPendiente.SKU;
        document.getElementById("confirmPeso").textContent = `${Number(peso || 0).toFixed(2)} kg`;
        document.getElementById("confirmTara").textContent = `${capturaPendiente.Tara.toFixed(3)} kg`;

        abrirModal("modalConfirmarCaptura");
        iniciarPesoVivoConfirmacion();
    } catch (error) {
        capturaPendiente = null;
        alert(error.message || "No fue posible preparar la captura.");
    }
}

let intervaloPesoConfirmacion = null;

function actualizarPesoConfirmacion() {
    const lblPeso = document.getElementById("confirmPeso");

    if (!lblPeso) return;

    const pesoActual = obtenerPesoActual();

    lblPeso.textContent = `${Number(pesoActual || 0).toFixed(3)} kg`;
}

function iniciarPesoVivoConfirmacion() {
    detenerPesoVivoConfirmacion();

    actualizarPesoConfirmacion();

    intervaloPesoConfirmacion = setInterval(() => {
        actualizarPesoConfirmacion();
    }, 300);
}

function detenerPesoVivoConfirmacion() {
    if (intervaloPesoConfirmacion) {
        clearInterval(intervaloPesoConfirmacion);
        intervaloPesoConfirmacion = null;
    }
}

function cerrarConfirmacionCaptura() {
    detenerPesoVivoConfirmacion();
    capturaPendiente = null;
    cerrarModal("modalConfirmarCaptura");
}

async function confirmarCapturaEntrada() {
    const btnConfirmar = document.getElementById("btnConfirmarCaptura");

    if (capturaEnProceso) return;

    if (!capturaPendiente) {
        alert("La captura perdió su contexto. Abra nuevamente la confirmación.");
        return;
    }

    const pesoConfirmado = Number(obtenerPesoActual());

    if (!Number.isFinite(pesoConfirmado) || pesoConfirmado <= 0) {
        alert("El peso neto debe ser mayor a cero. No se realizó la captura.");
        return;
    }

    // Desde este punto todos los datos enviados pertenecen a una sola captura.
    // Ningún cambio posterior de pantalla puede modificar este objeto.
    const snapshot = Object.freeze({
        ...capturaPendiente,
        Peso: pesoConfirmado,
        FechaHora: new Date().toISOString()
    });
    const loadingId = mostrarLoading("Guardando captura...");

    btnConfirmar.disabled = true;
    btnConfirmar.innerHTML = "Capturando...";

    try {
        detenerPesoVivoConfirmacion();
        cerrarModal("modalConfirmarCaptura");
        capturaPendiente = null;

        await capturarEntrada(snapshot, loadingId);
    } finally {
        btnConfirmar.disabled = false;
        btnConfirmar.innerHTML = "Confirmar captura";
        ocultarLoading(loadingId);
    }
}

async function capturarEntrada(snapshot, loadingId = null) {
    if (capturaEnProceso) {
        console.warn("⚠ Ya existe una captura en proceso.");
        return;
    }

    capturaEnProceso = true;

    try {
        if (!snapshot || !snapshot.SKU || !snapshot.Producto) {
            throw new Error("La captura no contiene una identidad válida de producto.");
        }

        if (normalizarSku(snapshot.SKU) !== normalizarSku(snapshot.ProductoSKU)) {
            throw new Error("El SKU de la etiqueta no coincide con el SKU capturado.");
        }

        const pesoActual = Number(snapshot.Peso);

        if (!Number.isFinite(pesoActual) || pesoActual <= 0) {
            throw new Error("El peso neto debe ser mayor a cero.");
        }

        const entrada = {
            Id: 0,
            Folio: "",
            SKU: snapshot.SKU,
            fk_Inyectora: 0,
            Porcentaje: snapshot.Porcentaje,
            ModoInyeccion: snapshot.ModoInyeccion,
            Presion: snapshot.Presion,
            Velocidad: snapshot.Velocidad,
            Altura: snapshot.Altura,
            Avance: snapshot.Avance,
            Bascula: snapshot.Bascula,
            FechaHora: snapshot.FechaHora,
            TipoPeso: snapshot.TipoPeso,
            Autoriza: snapshot.Autoriza,
            Peso: pesoActual,
            Tara: snapshot.Tara,
            fk_Lote: snapshot.LoteId,
            Plantilla: snapshot.Plantilla,
            UsSIGO: snapshot.UsSIGO
        };

        console.log("➡ Objeto capturado (snapshot):", {
            entrada,
            producto: snapshot.Producto,
            lote: snapshot.LoteImpresion
        });

        const resp = await fetch("/api/Inyeccion/CapturarEntrada", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(entrada)
        });

        const resultado = await resp.json().catch(() => null);

        if (!resp.ok) {
            throw new Error(resultado?.message || "Error al guardar la entrada directamente en SQL Server.");
        }
        const idGenerado =
            typeof resultado === "object" && resultado !== null
                ? (resultado.id ?? resultado.Id ?? 0)
                : resultado;

        const idEntrada = Number(idGenerado);
        if (!Number.isInteger(idEntrada) || idEntrada <= 0) {
            throw new Error("La captura se guardó, pero el servidor no devolvió un Id válido para imprimir.");
        }

        entrada.Id = idEntrada;
        entrada.Folio = resultado?.folio ?? resultado?.Folio ?? "";
        if (!entrada.Folio) {
            entrada.Folio = (await obtenerFolioEntrada(entrada.Id)) || "";
        }

        // La etiqueta se construye exclusivamente con la fotografía de esta captura.
        // Ya no consulta NombreSeleccionado, nombreLoteGlobal ni otra captura global.
        const etiquetaCapturada = Object.freeze({
            ...entrada,
            ProductoSKU: snapshot.ProductoSKU,
            Producto: snapshot.Producto,
            LoteImpresion: snapshot.LoteImpresion
        });

        ultimaCapturaPayload = { ...etiquetaCapturada };
        mostrarToastEntrada(entrada.Id);

        try {
            // Imprimir antes de refrescar reportes reduce todavía más la ventana de concurrencia.
            actualizarLoading(loadingId, "Enviando etiqueta a la impresora...");
            await imprimirEtiquetaSalida(etiquetaCapturada);
            console.log("✔ Primera impresión exitosa", etiquetaCapturada);
        } catch (errImpr) {
            console.warn("⚠ La entrada se guardó pero falló la impresión", errImpr);
        }

        await cargarRendimientoTiempoReal();
    } catch (err) {
        console.error("❌ Error capturando entrada", err);
        alert(err.message || "Ocurrió un error al registrar la entrada");
    } finally {
        capturaEnProceso = false;
    }
}
function guardarImpresora() {
    const ipIngresada = document.getElementById("ipImpresora").value.trim();

    if (!ipIngresada) {
        alert("Debe ingresar la IP de impresora");
        return;
    }

    ipImpresoraGlobal = ipIngresada;

    localStorage.setItem("IpImpresora", ipImpresoraGlobal);

    document.getElementById("metaImpresora").textContent = ipIngresada;

    cerrarModal("modalImpresora");

    console.log("✔ Impresora configurada: " + ipImpresoraGlobal);
}
function cargarConfiguracionImpresora() {
    const guardada = localStorage.getItem("IpImpresora");
    const loteActivoNombre = localStorage.getItem("NombreLote");

    if (guardada) {
        ipImpresoraGlobal = guardada;
        document.getElementById("ipImpresora").value = guardada;
        document.getElementById("metaImpresora").textContent = guardada;
    }

    if (loteActivoNombre) {
        nombreLoteGlobal = loteActivoNombre;
        document.getElementById("programacionActual").textContent = loteActivoNombre;
    }

    console.log("⚙ Impresora restaurada:", ipImpresoraGlobal);
}
async function imprimirEtiquetaSalida(entradaObj) {
    const etiqueta = entradaObj ? Object.freeze({ ...entradaObj }) : null;
    // La impresora sí se toma de la configuración vigente para permitir corregirla
    // antes de un reintento; los datos de negocio permanecen congelados.
    const impresoraParaImprimir = String(ipImpresoraGlobal || "").trim();

    if (!impresoraParaImprimir) {
        throw new Error("Debe configurar la impresora antes de imprimir.");
    }

    if (!etiqueta?.Id || Number(etiqueta.Id) <= 0) {
        throw new Error("La entrada no contiene un Id válido para imprimir.");
    }

    if (!etiqueta?.SKU) {
        throw new Error("La entrada no contiene SKU para imprimir.");
    }

    const productoParaImprimir = String(etiqueta.Producto ?? "").trim();
    const loteParaImprimir = String(etiqueta.LoteImpresion ?? "").trim();
    const skuProducto = normalizarSku(etiqueta.ProductoSKU ?? etiqueta.SKU);

    if (!productoParaImprimir) {
        throw new Error("La etiqueta no contiene el nombre congelado del producto.");
    }

    if (!loteParaImprimir) {
        throw new Error("La etiqueta no contiene el lote congelado de la captura.");
    }

    if (skuProducto !== normalizarSku(etiqueta.SKU)) {
        throw new Error("Se bloqueó la impresión porque el nombre y el SKU no pertenecen a la misma captura.");
    }

    if (impresionEnProceso) {
        throw new Error("Ya existe una impresión en proceso. Espere a que termine.");
    }

    impresionEnProceso = true;

    console.log("🖨️ Imprimiendo snapshot:", {
        id: etiqueta.Id,
        sku: etiqueta.SKU,
        producto: productoParaImprimir,
        lote: loteParaImprimir,
        tipoPeso: etiqueta.TipoPeso,
        impresora: impresoraParaImprimir
    });

    // Todos los valores de la etiqueta viajan juntos en esta petición.
    // El backend imprime este snapshot sin reconsultar la entrada ni el catálogo.
    const url = `/api/Inyeccion/Imprimir` +
        `?ip=${encodeURIComponent(impresoraParaImprimir)}` +
        `&lote=${encodeURIComponent(loteParaImprimir)}` +
        `&prod=${encodeURIComponent(productoParaImprimir)}`;

    const controller = new AbortController();
    // El backend puede realizar dos intentos de conexión de hasta 8 segundos.
    const timeoutId = setTimeout(() => controller.abort(), 25000);

    try {
        const resp = await fetch(url, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(etiqueta),
            signal: controller.signal,
            cache: "no-store"
        });

        if (!resp.ok) {
            const detalle = await resp.text().catch(() => "");
            throw new Error(`Error HTTP ${resp.status}${detalle ? `: ${detalle}` : ""}`);
        }

        const resultado = await resp.json();

        if (resultado.success === false) {
            throw new Error(resultado.message || "Error en la impresión");
        }

        console.log("✔ Impresión enviada correctamente");
        return resultado;
    } catch (err) {
        const errorFinal = err.name === "AbortError"
            ? new Error("Timeout: la impresora no respondió en 25 segundos")
            : err;

        mostrarToastImpresionError(etiqueta);
        console.error("❌ Error al imprimir:", errorFinal.message);
        throw errorFinal;
    } finally {
        clearTimeout(timeoutId);
        impresionEnProceso = false;
    }
}

// ============================================
// MODO MANUAL - VALIDACIÓN CON PERMISOS
// ============================================


// Evento del botón de cambio de modo
document.getElementById("btnTogglePeso").addEventListener("click", function () {
    if (!modoManualActivo) {
        // Intentar activar modo manual → pedir validación
        abrirModal('modalModoManual');
    } else {
        // Desactivar modo manual
        desactivarModoManual();
    }
});

// Función de validación
async function validarYActivarModoManual() {
    const usuarioId = document.getElementById("usuarioIdManual").value.trim();
    const nip = document.getElementById("nipManual").value.trim();

    if (!usuarioId || !nip) {
        alert("Debes ingresar usuario y NIP");
        return;
    }

    try {
        const resp = await fetch(`/api/Inyeccion/ValidarModoManual?usrid=${usuarioId}&nip=${encodeURIComponent(nip)}`);

        if (!resp.ok) throw new Error("Error en la validación");

        const resultado = await resp.json();

        if (resultado.success) {
            // ✅ Validación exitosa
            usuarioAutorizaId = parseInt(usuarioId); // ← GUARDAR EL ID DEL USUARIO
            activarModoManual(resultado.usuario);
            cerrarModal('modalModoManual');

            // Limpiar campos
            document.getElementById("usuarioIdManual").value = "";
            document.getElementById("nipManual").value = "";

            console.log(`✔ Usuario autorizado ID: ${usuarioAutorizaId}`); // ← LOG PARA DEBUG
        } else {
            // ❌ Sin permisos
            alert(resultado.message || "No tienes permisos para modo manual");
        }

    } catch (err) {
        console.error("❌ Error validando:", err);
        alert("Error al validar permisos");
    }
}

// Activar modo manual
function activarModoManual(nombreUsuario) {
    modoManualActivo = true;

    // Detener lectura automática
    detenerLoopBascula();


    // Cambiar UI
    const btn = document.getElementById("btnTogglePeso");
    btn.textContent = "Automático";
    btn.style.backgroundColor = "#28a745";
    btn.title = "Cambiar a modo automático";

    // Mostrar input manual y ocultar peso automático
    document.getElementById("pesoActual").style.display = "none";
    document.getElementById("inputPesoManual").style.display = "block";
    document.getElementById("inputPesoManual").focus();

    // Cambiar color del KPI
    document.getElementById("kpiPeso").classList.remove("ok");
    document.getElementById("kpiPeso").classList.add("manual");

    console.log(`✔ Modo manual activado por: ${nombreUsuario}`);
    alert(`Modo manual activado. Usuario: ${nombreUsuario}`);
}

// Desactivar modo manual
function desactivarModoManual() {

    modoManualActivo = false;
    usuarioAutorizaId = 0; // Reset autorización

    // Limpiar input manual
    document.getElementById("inputPesoManual").value = "";

    // 🔥 Reiniciar lectura automática correctamente
    if (ipBasculaGlobal && comandoBasculaGlobal) {
        iniciarLoopBascula();
    }

    // Cambiar UI
    const btn = document.getElementById("btnTogglePeso");
    btn.textContent = "Manual";
    btn.style.backgroundColor = "";
    btn.title = "Cambiar a modo manual";

    // Mostrar peso automático
    document.getElementById("inputPesoManual").style.display = "none";
    document.getElementById("pesoActual").style.display = "block";

    // Restaurar KPI
    document.getElementById("kpiPeso").classList.remove("manual");
    document.getElementById("kpiPeso").classList.add("ok");

    console.log("✔ Modo automático activado - Autoriza reseteado a 0");
}


// Función para obtener el peso correcto según el modo
function obtenerPesoActual() {
    if (modoManualActivo) {
        const pesoManual = document.getElementById("inputPesoManual").value;
        return parseFloat(pesoManual || 0);
    } else {
        return parseFloat(document.getElementById("pesoActual").textContent || 0);
    }
}

window.manualConfig = {
    title: "Manual de Operación",
    sections: [
        {
            id: "Vista General de la Interfaz",
            title: "Vista General de la Interfaz",
            icon: "fa-home",
            steps: [
                {
                    title: "Inicio",
                    text: "El menu de inyeccion permite al operador configurar el lote y realizar el pesaje. Navegue hacia abajo o mediante la barra superior para ver el flujo completo de operación.",
                    image: "/images/Inyecciones/Main-INY.gif"
                }
            ]
        },
        {
            id: " Operacion",
            title: " Operacion",
            icon: "fa-file-lines",
            steps: [
                {
                    title: "2.1 Configuración Inicial",
                    text: "Antes de pesar, configure la sesión en la barra superior:Selector de Lote: Despliegue la lista y elija la orden activa.Selector de Producto: Seleccione el SKU.Esto cargará las tolerancias de peso.",
                    image: "/images/Inyecciones/2.jpeg"
                },
                {
                    title: "2.2 Proceso de Captura",
                    text: "Una vez configurado el lote, proceda al pesaje en el área central,Seleccione el producto.Verifique el indicador numérico central (ej. 0.00).Cuando el peso sea estable presione el botón rojo 'Capturar' el sistema guardará el dato y actualizará los valores en la interfaz.",
                    video: "/images/Inyecciones/Captura_INY.mp4"
                }
            ]
        },
        {
            id: " Configuración Rápida",
            title: " Configuración Rápida",
            icon: "fa-file-lines",
            steps: [
                {
                    title: "3.1 Configurar Bascula",
                    text: "Presionamos el boton de Configurar en el apartado de Bascula para poner la IP y el comando necesario",
                    image: "/images/Inyecciones/bascula-gif.gif"
                },
                {
                    title: "3.2 Configurar Impresora",
                    text: "Presionamos el boton de Configurar en el apartado de Impresora para poner la IP",
                    image: "/images/Inyecciones/impresora-gif.gif"
                },
                {
                    title: "3.3 Modo Manual",
                    text: "Para cambiar el modo de operación:Presione el botón Seleccionar Modo.El sistema solicitará ID de Usuario y NIP.Solo personal autorizado puede activar el modo manual.",
                    image: "/images/Inyecciones/modomanual-gif.gif",


                }
            ]
        },
        {
            id: "Reporteadores",
            title: "Reporteadores",
            icon: "fa-file-lines",
            steps: [
                {
                    title: "4.1 Ver Reporte Detallado/Rendimiento",
                    text: "Aqui es donde podemos ver el detallado de los reportes de Rendimiento/Detallado por fechas",
                    video: "/images/Inyecciones/Reportes_Iny.mp4"
                },
                {
                    title: "4.2 Ver Reporte en Tiempo Real",
                    text: "Reporte en tiempo real que se actualiza cada 3 segundos y cambia segun el lote seleccionado",
                    video: "/images/Inyecciones/Reportes_Iny2.mp4"
                }
            ]
        }
    ]
};

let tabActiva = "rendimiento";

function cambiarTab(tab, el) {

    tabActiva = tab;

    document.querySelectorAll(".tab-btn").forEach(b => b.classList.remove("active"));
    document.querySelectorAll(".tab-content").forEach(c => c.classList.remove("active"));

    document.getElementById(`tab-${tab}`).classList.add("active");

    // Si viene desde click en tab
    if (el) {
        el.classList.add("active");
    }
    // Si viene desde el botón único (por código)
    else {
        document.querySelectorAll(".tab-btn").forEach(btn => {
            if (btn.dataset.tab === tab) {
                btn.classList.add("active");
            }
        });
    }
}

function consultarReporteActivo() {
    if (tabActiva === "rendimiento") {
        cargarRendimiento();
    } else if (tabActiva === "detallado") {
        cargarReporteDetallado();
    }
}

async function cargarRendimiento() {

    const ini = document.getElementById("repFechaInicio")?.value || "";
    const fin = document.getElementById("repFechaFin")?.value || "";
    const tbody = document.getElementById("tbodyRendimientoModal");

    if (!tbody) return;

    if (!ini || !fin) {
        tbody.innerHTML = `<tr><td colspan="8">Seleccione fecha inicio y fecha fin.</td></tr>`;
        return;
    }

    tbody.innerHTML = `<tr><td colspan="8">Cargando...</td></tr>`;

    try {
        const resp = await fetch(
            `/api/Reportes/RendimientoFecha?fechain=${encodeURIComponent(ini)}&fechafin=${encodeURIComponent(fin)}`,
            { cache: "no-store" }
        );

        if (!resp.ok) {
            throw new Error(`Error API rendimiento HTTP ${resp.status}`);
        }

        let data = await resp.json();

        if (!Array.isArray(data)) {
            data = data ? [data] : [];
        }

        tbody.innerHTML = "";

        if (!data.length) {
            tbody.innerHTML = `<tr><td colspan="8">Sin registros</td></tr>`;
            return;
        }

        data.forEach(item => {

            const esperado = Number(
                item["Porcentaje Esperado"] ??
                item.PorcentajeEsperado ??
                item.porcentajeEsperado ??
                0
            );

            const rendimiento = Number(
                item.Rendimiento ??
                item.rendimiento ??
                0
            );

            const pesoEntrada = Number(
                item["Peso Entrada"] ??
                item.PesoEntrada ??
                item.pesoEntrada ??
                0
            );

            const pesoSalida = Number(
                item["Peso Salida"] ??
                item.PesoSalida ??
                item.pesoSalida ??
                0
            );

            const variacion = Number(
                item.Variacion ??
                item.variacion ??
                0
            );

            // ±2 puntos contra el rendimiento esperado.
            const tolerancia = 2;

            let estiloRendimiento = "";
            let tituloRendimiento = "Rendimiento dentro del rango esperado";

            // Negativo = alerta fuerte.
            if (rendimiento < 0) {
                estiloRendimiento =
                    "background:#f8d7da !important;" +
                    "color:#991b1b !important;" +
                    "font-weight:900 !important;" +
                    "box-shadow:inset 4px 0 0 #c62828 !important;";

                tituloRendimiento = "ALERTA: rendimiento negativo";
            }
            // Bajo contra el esperado.
            else if (rendimiento <= esperado - tolerancia) {
                estiloRendimiento =
                    "background:#fdebec !important;" +
                    "color:#b4232f !important;" +
                    "font-weight:900 !important;" +
                    "box-shadow:inset 4px 0 0 #dc3545 !important;";

                tituloRendimiento =
                    `Rendimiento bajo. Esperado: ${esperado.toFixed(2)}%`;
            }
            // Muy alto contra el esperado.
            else if (rendimiento >= esperado + tolerancia) {
                estiloRendimiento =
                    "background:#fff3cd !important;" +
                    "color:#8a5700 !important;" +
                    "font-weight:900 !important;" +
                    "box-shadow:inset 4px 0 0 #e0a000 !important;";

                tituloRendimiento =
                    `Rendimiento alto. Esperado: ${esperado.toFixed(2)}%`;
            }

            const fechaRaw =
                item.FechaProduccion ??
                item.fechaProduccion ??
                "";

            let fechaTexto = "";

            if (fechaRaw) {
                const fecha = new Date(fechaRaw);
                fechaTexto = Number.isNaN(fecha.getTime())
                    ? String(fechaRaw)
                    : fecha.toLocaleDateString("es-MX");
            }

            tbody.insertAdjacentHTML("beforeend", `
                <tr>
                    <td data-label="Fecha Produccion">${fechaTexto}</td>
                    <td data-label="Lote">${item.Lote ?? item.lote ?? ""}</td>
                    <td data-label="SKU">${item.SKU ?? item.sku ?? ""}</td>
                    <td data-label="Peso Entrada">${pesoEntrada.toFixed(2)}</td>
                    <td data-label="Peso Salida">${pesoSalida.toFixed(2)}</td>
                    <td data-label="Esperado %">${esperado.toFixed(2)}%</td>
                    <td data-label="Variación">${variacion.toFixed(2)}</td>
                    <td data-label="Rendimiento %"
                        title="${tituloRendimiento}"
                        style="${estiloRendimiento}">
                        ${rendimiento.toFixed(2)}%
                    </td>
                </tr>
            `);
        });

    } catch (err) {
        console.error("❌ Error cargando rendimiento:", err);
        tbody.innerHTML =
            `<tr><td colspan="8">Error consultando rendimiento</td></tr>`;
    }
}

async function obtenerFolioEntrada(id) {
    try {
        if (!id) return "";

        const resp = await fetch(`/api/Inyeccion/ConsultarEntrada?id=${encodeURIComponent(id)}`);
        if (!resp.ok) return "";

        const entrada = await resp.json();

        return entrada?.folio ?? entrada?.Folio ?? "";
    } catch (err) {
        // El folio es complementario: una API separada no debe bloquear la etiqueta.
        console.warn("⚠ No se pudo obtener el folio de la entrada:", id, err);
        return "";
    }
}

async function cargarReporteDetallado() {

    const ini = document.getElementById("repFechaInicio")?.value || "";
    const fin = document.getElementById("repFechaFin")?.value || "";
    const tbody = document.getElementById("tbodyDetallado");

    if (!tbody) return;

    if (!ini || !fin) {
        tbody.innerHTML = `<tr><td colspan="11">Seleccione fecha inicio y fecha fin.</td></tr>`;
        return;
    }

    tbody.innerHTML = `<tr><td colspan="11">Cargando...</td></tr>`;

    try {
        const resp = await fetch(
            `/api/Reportes/Detallado?fechain=${encodeURIComponent(ini)}&fechafin=${encodeURIComponent(fin)}`,
            { cache: "no-store" }
        );

        if (!resp.ok) throw new Error("Error API detallado");

        let data = await resp.json();

        if (!Array.isArray(data)) {
            data = data ? [data] : [];
        }

        console.log("📊 Data detallado:", data);

        tbody.innerHTML = "";

        if (!data.length) {
            tbody.innerHTML = `<tr><td colspan="11">Sin registros</td></tr>`;
            return;
        }

        for (const item of data) {

            let folio =
                item.Folio ??
                item.folio ??
                item.FOLIO ??
                "";

            if (!folio) {
                const idEntrada =
                    item.Referencia ??
                    item.referencia ??
                    item.Id ??
                    item.id ??
                    0;

                folio = await obtenerFolioEntrada(idEntrada);
            }

            const peso = Number(item.Peso ?? item.peso ?? 0);
            const tara = Number(item.Tara ?? item.tara ?? 0);

            const modoTexto = String(
                item.TipoPeso ??
                item.tipoPeso ??
                item.Modo ??
                item.modo ??
                ""
            ).trim();

            const modoNormalizado = modoTexto.toLowerCase();

            const esManual =
                modoNormalizado === "man" ||
                modoNormalizado === "m" ||
                modoNormalizado === "manual" ||
                modoNormalizado.includes("manual");

            // Se usa estilo inline con !important para que ningún CSS posterior
            // pueda quitar el rojo de la alerta.
            const estiloModo = esManual
                ? "background:#dc3545 !important;" +
                  "color:#ffffff !important;" +
                  "font-weight:900 !important;" +
                  "text-transform:uppercase !important;"
                : "";

            const tituloModo = esManual
                ? "ALERTA: captura realizada en modo manual"
                : "Captura automática";

            tbody.insertAdjacentHTML("beforeend", `
                <tr>
                    <td data-label="Referencia">${item.Referencia ?? item.referencia ?? ""}</td>
                    <td data-label="Folio">${folio}</td>
                    <td data-label="Fecha / Hora">${item.FechaHora ?? item.fechaHora ?? ""}</td>
                    <td data-label="Lote">${item.Lote ?? item.lote ?? ""}</td>
                    <td data-label="SKU">${item.SKU ?? item.sku ?? ""}</td>
                    <td data-label="Producto">${item.Producto ?? item.producto ?? ""}</td>
                    <td data-label="Peso">${peso.toFixed(2)}</td>
                    <td data-label="Tara">${tara.toFixed(2)}</td>
                    <td data-label="Modo"
                        title="${tituloModo}"
                        style="${estiloModo}">
                        ${modoTexto || "-"}
                    </td>
                    <td data-label="Usuario">${item.InyUsuario ?? item.inyUsuario ?? ""}</td>
                    <td data-label="Autorización">${item.Autorizacion ?? item.autorizacion ?? ""}</td>
                </tr>
            `);
        }

    } catch (err) {
        console.error("❌ Error cargando reporte detallado:", err);
        tbody.innerHTML =
            `<tr><td colspan="11">Error consultando reporte detallado</td></tr>`;
    }
}
function abrirModalReportes() {
    // Auto-llenar fechas con la fecha actual
    const hoy = new Date().toISOString().split('T')[0];
    document.getElementById('repFechaInicio').value = hoy;
    document.getElementById('repFechaFin').value = hoy;

    abrirModal("modalReportes");
    cambiarTab("rendimiento"); // tab por defecto
}

function numSeguro(valor) {
    const n = Number(valor);
    return isNaN(n) ? 0 : n;
}


function asegurarPaginadorCapturasTiempoReal() {
    const tbody = document.getElementById("tbodyDetalladoTiempoReal");
    const tabla = tbody?.closest("table");

    if (!tbody || !tabla) return null;

    let pager = document.getElementById("paginadorCapturasTiempoReal");
    if (pager) return pager;

    pager = document.createElement("div");
    pager.id = "paginadorCapturasTiempoReal";

    const btnPrev = document.createElement("button");
    btnPrev.type = "button";
    btnPrev.id = "btnCapturasPrev";
    btnPrev.title = "Página anterior";
    btnPrev.textContent = "‹";

    const info = document.createElement("span");
    info.className = "iny-cap-page-info";
    info.id = "infoCapturasPagina";
    info.textContent = "0 de 0";

    const btnNext = document.createElement("button");
    btnNext.type = "button";
    btnNext.id = "btnCapturasNext";
    btnNext.title = "Página siguiente";
    btnNext.textContent = "›";

    btnPrev.addEventListener("click", () => {
        if (capturasTiempoRealPagina <= 1) return;
        capturasTiempoRealPagina--;
        renderCapturasTiempoReal();
    });

    btnNext.addEventListener("click", () => {
        const totalPaginas = Math.max(
            1,
            Math.ceil(capturasTiempoRealData.length / capturasTiempoRealPorPagina)
        );

        if (capturasTiempoRealPagina >= totalPaginas) return;

        capturasTiempoRealPagina++;
        renderCapturasTiempoReal();
    });

    pager.append(btnPrev, info, btnNext);

    // Preferimos poner el paginador fuera del scroll de la tabla.
    const scroll = tabla.closest(".inj-table-scroll");
    if (scroll) {
        scroll.insertAdjacentElement("afterend", pager);
    } else {
        tabla.insertAdjacentElement("afterend", pager);
    }

    return pager;
}

function renderCapturasTiempoReal() {
    const tbody = document.getElementById("tbodyDetalladoTiempoReal");
    if (!tbody) return;

    const total = capturasTiempoRealData.length;

    if (!total) {
        tbody.innerHTML = `<tr><td colspan="10">Sin capturas registradas hoy</td></tr>`;

        const pagerVacio = asegurarPaginadorCapturasTiempoReal();
        if (pagerVacio) pagerVacio.style.display = "none";
        return;
    }

    const totalPaginas = Math.max(
        1,
        Math.ceil(total / capturasTiempoRealPorPagina)
    );

    if (capturasTiempoRealPagina > totalPaginas) {
        capturasTiempoRealPagina = totalPaginas;
    }

    if (capturasTiempoRealPagina < 1) {
        capturasTiempoRealPagina = 1;
    }

    const inicio = (capturasTiempoRealPagina - 1) * capturasTiempoRealPorPagina;
    const fin = Math.min(inicio + capturasTiempoRealPorPagina, total);
    const pagina = capturasTiempoRealData.slice(inicio, fin);

    tbody.innerHTML = "";

    pagina.forEach(item => {
        const peso = numSeguro(item.Peso ?? item.peso);
        const tara = numSeguro(item.Tara ?? item.tara);

        const fechaRaw =
            item.FechaHora ??
            item.fechaHora ??
            item.FechaProduccion ??
            item.fechaProduccion ??
            "";

        const fechaTexto = fechaRaw
            ? new Date(fechaRaw).toLocaleString("es-MX")
            : "";

        tbody.insertAdjacentHTML("beforeend", `
            <tr>
                <td data-label="Referencia">${item.Referencia ?? item.referencia ?? item.Id ?? item.id ?? ""}</td>
                <td data-label="Fecha / Hora">${fechaTexto}</td>
                <td data-label="Lote">${item.Lote ?? item.lote ?? ""}</td>
                <td data-label="SKU">${item.SKU ?? item.sku ?? ""}</td>
                <td data-label="Producto">${item.Producto ?? item.producto ?? ""}</td>
                <td data-label="Peso">${peso.toFixed(2)}</td>
                <td data-label="Tara">${tara.toFixed(2)}</td>
                <td data-label="Modo"
                    style="${
                        (() => {
                            const modo = String(item.TipoPeso ?? item.tipoPeso ?? "").trim().toLowerCase();
                            const manual =
                                modo === "man" ||
                                modo === "m" ||
                                modo === "manual" ||
                                modo.includes("manual");

                            return manual
                                ? "background:#dc3545 !important;color:#fff !important;font-weight:900 !important;"
                                : "";
                        })()
                    }">
                    ${item.TipoPeso ?? item.tipoPeso ?? ""}
                </td>
                <td data-label="Usuario">${item.InyUsuario ?? item.inyUsuario ?? ""}</td>
                <td data-label="Autorización">${item.Autorizacion ?? item.autorizacion ?? ""}</td>
            </tr>
        `);
    });

    const pager = asegurarPaginadorCapturasTiempoReal();

    if (pager) {
        pager.style.display = "flex";

        const info = document.getElementById("infoCapturasPagina");
        const prev = document.getElementById("btnCapturasPrev");
        const next = document.getElementById("btnCapturasNext");

        if (info) {
            info.textContent =
                `${inicio + 1}-${fin} de ${total} · Pág. ${capturasTiempoRealPagina}/${totalPaginas}`;
        }

        if (prev) prev.disabled = capturasTiempoRealPagina <= 1;
        if (next) next.disabled = capturasTiempoRealPagina >= totalPaginas;
    }
}

async function cargarDetalladoTiempoRealHoy() {
    const tbody = document.getElementById("tbodyDetalladoTiempoReal");

    if (!tbody) {
        console.warn("No existe tbodyDetalladoTiempoReal en la vista");
        return;
    }

    const hoy = new Date().toISOString().split("T")[0];

    tbody.innerHTML = `<tr><td colspan="10">Cargando últimas capturas...</td></tr>`;

    try {
        const resp = await fetch(
            `/api/Reportes/Detallado?fechain=${hoy}&fechafin=${hoy}`,
            { cache: "no-store" }
        );

        if (!resp.ok) {
            throw new Error("Error API detallado");
        }

        let data = await resp.json();

        if (!Array.isArray(data)) {
            data = data ? [data] : [];
        }

        data.sort((a, b) => {
            const refA = numSeguro(a.Referencia ?? a.referencia ?? a.Id ?? a.id);
            const refB = numSeguro(b.Referencia ?? b.referencia ?? b.Id ?? b.id);
            return refB - refA;
        });

        // Conservamos las 50 más recientes, pero sólo mostramos 8 por página.
        capturasTiempoRealData = data.slice(0, 50);
        capturasTiempoRealPagina = 1;

        renderCapturasTiempoReal();

    } catch (err) {
        console.error("❌ Error cargando detallado tiempo real:", err);

        capturasTiempoRealData = [];
        capturasTiempoRealPagina = 1;

        tbody.innerHTML =
            `<tr><td colspan="10">Error consultando últimas capturas</td></tr>`;

        const pager = document.getElementById("paginadorCapturasTiempoReal");
        if (pager) pager.style.display = "none";
    }
}

async function cargarRendimientoTiempoReal() {
    await cargarDetalladoTiempoRealHoy();
}

function obtenerFechaDetallado(item) {

    const fechaRaw =
        item.FechaHora ??
        item.fechaHora ??
        item.FechaProduccion ??
        item.fechaProduccion ??
        null;

    const fecha = fechaRaw ? new Date(fechaRaw) : new Date(0);

    return isNaN(fecha.getTime()) ? new Date(0) : fecha;
}

function exportarReporteExcel() {

    let tabla;
    let nombreArchivo;

    if (tabActiva === "rendimiento") {
        tabla = document.querySelector("#tab-rendimiento table");
        nombreArchivo = "Reporte_Rendimiento";
    }
    else if (tabActiva === "detallado") {
        tabla = document.querySelector("#tab-detallado table");
        nombreArchivo = "Reporte_Detallado";
    }

    if (!tabla) {
        alert("No hay datos para exportar");
        return;
    }

    // Clonar tabla para evitar modificar la original
    const tablaClon = tabla.cloneNode(true);

    // Crear HTML compatible con Excel
    const html = `
        <html xmlns:o="urn:schemas-microsoft-com:office:office"
              xmlns:x="urn:schemas-microsoft-com:office:excel"
              xmlns="http://www.w3.org/TR/REC-html40">
        <head>
            <meta charset="UTF-8">
        </head>
        <body>
            ${tablaClon.outerHTML}
        </body>
        </html>
    `;

    const blob = new Blob([html], {
        type: "application/vnd.ms-excel;charset=utf-8;"
    });

    const url = URL.createObjectURL(blob);

    const a = document.createElement("a");
    a.href = url;
    a.download = `${nombreArchivo}_${new Date().toISOString().slice(0, 10)}.xls`;
    document.body.appendChild(a);
    a.click();

    document.body.removeChild(a);
    URL.revokeObjectURL(url);
}



function configurarBusquedaProductos() {
    const input = document.getElementById("searchProducto");
    if (!input || input.dataset.filtroProductoBound === "1") return;

    input.addEventListener("input", filtrarProductos);
    input.dataset.filtroProductoBound = "1";
}

function normalizarBusquedaProducto(valor) {
    return String(valor ?? "")
        .normalize("NFD")
        .replace(/[\u0300-\u036f]/g, "")
        .toLowerCase()
        .trim();
}

function filtrarProductos() {
    const input = document.getElementById("searchProducto");
    const resultadosDiv = document.getElementById("resultadosBusqueda");
    const sinResultados = document.getElementById("sinResultados");

    const searchTerm = normalizarBusquedaProducto(input?.value || "");
    const cards = document.querySelectorAll("#contenedorProductos .product-card");

    let contador = 0;

    cards.forEach(card => {
        const sku = normalizarBusquedaProducto(card.dataset.sku || "");
        const nombre = normalizarBusquedaProducto(card.dataset.nombre || "");

        const coincide =
            searchTerm === "" ||
            sku.includes(searchTerm) ||
            nombre.includes(searchTerm);

        // No usamos style.display porque .product-card tiene display:flex !important.
        // Una clase con display:none !important sí puede ocultarla correctamente.
        card.classList.toggle("product-filter-hidden", !coincide);

        if (coincide) contador++;
    });

    if (!resultadosDiv) return;

    if (searchTerm === "") {
        resultadosDiv.textContent = "";
        resultadosDiv.style.color = "#60758a";
        if (sinResultados) sinResultados.style.display = "none";
    } else if (contador === 0) {
        resultadosDiv.textContent = "No se encontraron productos";
        resultadosDiv.style.color = "#6b7280";
        if (sinResultados) sinResultados.style.display = "block";
    } else {
        resultadosDiv.textContent =
            `✓ ${contador} producto${contador !== 1 ? "s" : ""} encontrado${contador !== 1 ? "s" : ""}`;

        resultadosDiv.style.color = "#4f6f86";
        if (sinResultados) sinResultados.style.display = "none";
    }
}

function mostrarToastImpresionError(entradaObj) {
    const toast = document.getElementById("toastImpresionError");

    // Guardamos una copia independiente: el reintento jamás usa el estado actual de pantalla.
    ultimaEntradaParaImprimir = entradaObj ? Object.freeze({ ...entradaObj }) : null;

    // Asegurarse de que esté visible
    toast.style.display = "flex";
    toast.classList.add("show");

    console.log("Toast de error de impresión mostrado");
}

function ocultarToastImpresion() {
    const toast = document.getElementById("toastImpresionError");
    toast.classList.remove("show");

    // Esperar a que termine la animación antes de ocultar
    setTimeout(() => {
        toast.style.display = "none";
    }, 300);

    console.log("Toast de error de impresión ocultado");
}

async function reintentarImpresion() {
    if (!ultimaEntradaParaImprimir) {
        console.warn("⚠️ No hay entrada para reimprimir");
        ocultarToastImpresion();
        return;
    }

    console.log("🔄 Reintentando impresión...");
    ocultarToastImpresion();

    try {
        await imprimirEtiquetaSalida(ultimaEntradaParaImprimir);

        // Si llegó aquí sin error, fue exitoso
        console.log("✅ Reimpresión exitosa");
        mostrarToastEntrada(ultimaEntradaParaImprimir.Id);

    } catch (e) {
        console.error("❌ Reimpresión falló:", e);
        // mostrarToastImpresionError ya se llamó dentro de imprimirEtiquetaSalida
    }
}

function mostrarToastEntrada(idEntrada) {
    const toast = document.getElementById("toastEntrada");
    const spanId = document.getElementById("toastEntradaId");

    spanId.textContent = idEntrada;

    toast.classList.add("show");

    // Limpiar timeout previo si existe
    if (toastTimeout) {
        clearTimeout(toastTimeout);
    }

    // Ocultar automáticamente
    toastTimeout = setTimeout(() => {
        toast.classList.remove("show");
    }, 4000); // 4 segundos
}


/**
 * Reimprimir la última captura
 */
async function reimprimirUltimaCaptura() {
    if (!ultimaCapturaPayload) {
        alert('❌ No hay captura para reimprimir');
        return;
    }

    if (confirm(`¿Reimprimir esta captura?

    📦 Producto: ${ultimaCapturaPayload.Producto}
    ⚖️  Peso: ${ultimaCapturaPayload.Peso} kg
    🏷️  SKU: ${ultimaCapturaPayload.SKU}`)) {
        try {
            await imprimirEtiquetaSalida(ultimaCapturaPayload);
            alert('✅ Reimpresión exitosa');
        } catch (error) {
            console.error('❌ Error:', error);
        }
    }
}

function mostrarLoading(texto = "Cargando información...") {
    const loadingId = ++secuenciaLoading;
    operacionesLoading.set(loadingId, texto);
    renderizarLoading();
    return loadingId;
}

function actualizarLoading(loadingId, texto) {
    if (!loadingId || !operacionesLoading.has(loadingId)) return;

    operacionesLoading.set(loadingId, texto);
    renderizarLoading();
}

function ocultarLoading(loadingId) {
    if (loadingId) {
        operacionesLoading.delete(loadingId);
    } else {
        // Compatibilidad defensiva con llamadas antiguas sin identificador.
        operacionesLoading.clear();
    }

    renderizarLoading();
}

function renderizarLoading() {
    loadingActivo = operacionesLoading.size > 0;

    const overlay = document.getElementById("loadingOverlay");
    if (!overlay) return;

    const textosActivos = [...operacionesLoading.values()];
    const textoActivo = textosActivos[textosActivos.length - 1] || "Cargando información...";
    const texto = overlay.querySelector(".loading-text");

    if (texto) texto.textContent = textoActivo;

    overlay.style.display = loadingActivo ? "flex" : "none";
    overlay.style.pointerEvents = loadingActivo ? "all" : "none";
    overlay.setAttribute("aria-hidden", loadingActivo ? "false" : "true");
    document.body.setAttribute("aria-busy", loadingActivo ? "true" : "false");

    // Bloquea mouse, teclado táctil y cambios de controles mientras haya
    // al menos una operación crítica pendiente.
    document.body.style.pointerEvents = loadingActivo ? "none" : "";
}

function detenerLoopBascula() {
    basculaActiva = false;

    if (timerBascula) {
        clearTimeout(timerBascula);
        timerBascula = null;
    }

    leyendoBascula = false;
}

function obtenerTiempoSiguienteLecturaBascula(exito) {
    // Si está en modo manual, no consultamos báscula
    if (modoManualActivo) {
        return null;
    }

    // Si la pestaña/ventana no está visible, bajamos bastante la frecuencia
    if (pausaPorPestanaOculta && document.hidden) {
        return tiempoConsultaOculta;
    }

    // Si hubo error, usamos frecuencia de error
    if (exito === false || erroresBasculaConsecutivos > 0) {
        return tiempoConsultaError;
    }

    // Caso normal
    return tiempoConsultaNormal;
}

function programarSiguienteLectura(ms = null) {
    if (!basculaActiva || modoManualActivo) return;

    if (timerBascula) {
        clearTimeout(timerBascula);
        timerBascula = null;
    }

    const tiempo = ms ?? obtenerTiempoSiguienteLecturaBascula(true);

    if (tiempo === null) return;

    timerBascula = setTimeout(async () => {
        await cicloBascula();
    }, tiempo);
}

async function cicloBascula() {
    if (!basculaActiva || modoManualActivo) return;

    let exito = true;

    // Si la ventana está oculta, sí dejamos leer, pero mucho más lento.
    // Si prefieres que NO consulte nada cuando está oculta, aquí se puede cambiar.
    exito = await consultarBascula();

    const siguienteTiempo = obtenerTiempoSiguienteLecturaBascula(exito);

    if (siguienteTiempo !== null) {
        programarSiguienteLectura(siguienteTiempo);
    }
}

function iniciarLoopBascula() {
    detenerLoopBascula(); // asegura que nunca haya dos loops activos

    if (!ipBasculaGlobal || !comandoBasculaGlobal) {
        console.warn("⚠ No se inicia báscula porque falta IP o comando.");
        return;
    }

    basculaActiva = true;
    programarSiguienteLectura(300);
}

document.addEventListener("visibilitychange", () => {
    if (!basculaActiva || modoManualActivo) return;

    if (document.hidden) {
        logBascula("📴 Pestaña/ventana oculta. Bajando frecuencia de báscula.");
        programarSiguienteLectura(tiempoConsultaOculta);
    } else {
        logBascula("👀 Pestaña/ventana visible. Reanudando lectura normal.");
        programarSiguienteLectura(300);
    }
});

function activarCooldownBascula() {

    basculaEnCooldown = true;

    logBascula("🧊 Entrando en cooldown 2s...");

    setTimeout(() => {

        erroresBasculaConsecutivos = 0;
        basculaEnCooldown = false;

        logBascula("🔄 Fin cooldown. Reintentando...");

    }, tiempoCooldownBascula);
}


function logBascula(...args) {
    if (debugBascula) {
        console.log("[BÁSCULA]", ...args);
    }
}

function mostrarToast(mensaje, tipo = "info") {
    let toast = document.getElementById("toastFlotante");

    if (!toast) {
        toast = document.createElement("div");
        toast.id = "toastFlotante";
        document.body.appendChild(toast);
    }

    toast.className = "toast-flotante toast-" + tipo;
    toast.textContent = mensaje;
    toast.classList.add("show");

    setTimeout(() => {
        toast.classList.remove("show");
    }, 3500);
}
