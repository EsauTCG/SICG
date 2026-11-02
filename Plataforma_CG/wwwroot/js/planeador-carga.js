(function (root) {
    "use strict";
    if (root.PlaneadorCarga) return;

    const doc = root.document;
    const overlay = doc.getElementById("planeadorCarga");
    const contenido = doc.getElementById("planeadorContenido");
    if (!overlay || !contenido) return;

    const titulo = doc.getElementById("planeadorCargaTitulo");
    const tareas = new Map();
    const bloqueados = new Map();
    let focoAnterior = null;
    let estadoAnterior = null;

    // Sacar la capa de los contenedores del layout para cubrir también el menú y los modales.
    doc.body.appendChild(overlay);

    function esEnlaceDeDescarga(elemento) {
        return elemento?.tagName === "A" && elemento.ownerDocument === doc &&
            elemento.hasAttribute("download") && elemento.href.startsWith("blob:");
    }

    function bloquear(elemento) {
        if (elemento === overlay || esEnlaceDeDescarga(elemento) || ["SCRIPT", "STYLE", "LINK"].includes(elemento.tagName)) return;
        if (!bloqueados.has(elemento)) bloqueados.set(elemento, elemento.inert);
        elemento.inert = true;
    }

    const observador = typeof root.MutationObserver === "function" ? new root.MutationObserver(() => {
        if (tareas.size) Array.from(doc.body.children).forEach(bloquear);
    }) : null;

    function comenzar(mensaje) {
        const token = Symbol();
        const primera = tareas.size === 0;
        tareas.set(token, mensaje);
        if (primera) {
            focoAnterior = doc.activeElement;
            estadoAnterior = contenido.getAttribute("aria-busy");
            overlay.hidden = false;
            doc.body.classList.add("pp-loading-lock");
            contenido.setAttribute("aria-busy", "true");
            Array.from(doc.body.children).forEach(bloquear);
            observador?.observe(doc.body, { childList: true });
            overlay.focus({ preventScroll: true });
        }
        titulo.textContent = tareas.values().next().value;
        return token;
    }

    function terminar(token) {
        tareas.delete(token);
        if (tareas.size) {
            titulo.textContent = tareas.values().next().value;
            return;
        }
        observador?.disconnect();
        bloqueados.forEach((estado, elemento) => { elemento.inert = estado; });
        bloqueados.clear();
        doc.body.classList.remove("pp-loading-lock");
        if (estadoAnterior === null) contenido.removeAttribute("aria-busy");
        else contenido.setAttribute("aria-busy", estadoAnterior);
        overlay.hidden = true;

        const modales = doc.querySelectorAll(".modal.show");
        const modal = modales[modales.length - 1];
        const destino = focoAnterior?.isConnected && focoAnterior !== doc.body && (!modal || modal.contains(focoAnterior))
            ? focoAnterior : modal || contenido;
        destino?.focus({ preventScroll: true });
        focoAnterior = null;
    }

    async function ejecutar(accion, mensaje = "Cargando información…") {
        const token = comenzar(mensaje);
        try {
            return await accion();
        } finally {
            terminar(token);
        }
    }

    // Inert bloquea teclado y puntero; la captura también frena un segundo clic ya encolado.
    function impedirInteraccion(evento) {
        if (!tareas.size) return;
        // El exportador activa un enlace Blob temporal por código; no es un clic del usuario.
        if (evento.type === "click" && evento.isTrusted === false && esEnlaceDeDescarga(evento.target)) return;
        if (evento.type === "keydown" && (evento.ctrlKey || evento.metaKey || evento.key === "F5")) return;
        evento.preventDefault();
        evento.stopImmediatePropagation();
    }
    for (const tipo of ["click", "dblclick", "pointerdown", "keydown", "submit", "touchmove", "wheel"]) {
        doc.addEventListener(tipo, impedirInteraccion, { capture: true, passive: false });
    }
    doc.addEventListener("focusin", evento => {
        if (!tareas.size) return;
        // Evitar que las trampas de foco de Bootstrap devuelvan el foco detrás de la capa.
        evento.stopImmediatePropagation();
        if (!overlay.contains(evento.target)) overlay.focus({ preventScroll: true });
    }, true);

    const operaciones = {
        mostrarMes: "Cargando el mes…",
        cargarResumenMensual: "Cargando el mes…",
        cargarPlaneacionMensual: "Cargando el plan mensual…",
        inicializarPlantillaMensual: "Cargando el plan mensual…",
        cargarDetallesClasificacionMensual: "Cargando el plan mensual…",
        cargarInyeccionFilaMensual: "Cargando información del producto…",
        seleccionarDia: "Cargando el plan diario…",
        actualizarPlaneacionDia: "Actualizando el plan diario…",
        recalcularTodoDia: "Cargando el plan diario…",
        recalcularConversionFila: "Cargando información del producto…",
        abrirPlantillaSemanal: "Cargando el plan semanal…",
        actualizarPlaneacionSemanal: "Actualizando el plan semanal…",
        inicializarPlanSemanal: "Cargando el plan semanal…",
        abrirModalDistribucion: "Cargando la distribución diaria…",
        guardarDistribucion: "Guardando la distribución…",
        guardarSemana: "Guardando la semana…",
        guardarPlanDiario: "Guardando el plan diario…",
        guardarPlantillaMensual: "Guardando el plan mensual…",
        guardarPlanMensual: "Guardando el plan mensual…",
        guardarPlanSemanal: "Guardando el plan semanal…",
        exportarExcel: "Preparando el Excel…",
        exportarExcelSemanal: "Preparando el Excel…",
        abrirResumenPlaneacion: "Cargando el resumen…",
        refrescarResumenPlaneacion: "Actualizando el resumen…",
        abrirCatalogoExtra: "Cargando el catálogo de productos…",
        agregarSkuExtra: "Agregando el producto…",
        abrirModalSolicitud: "Cargando la solicitud…",
        abrirSolicitudes: "Cargando las solicitudes…",
        cargarSolicitudes: "Cargando las solicitudes…",
        verSolicitud: "Cargando el detalle de la solicitud…",
        guardarSolicitud: "Guardando la solicitud…",
        activarPlantilla: "Activando la plantilla…"
    };
    for (const [nombre, mensaje] of Object.entries(operaciones)) {
        const original = root[nombre];
        if (typeof original !== "function") continue;
        root[nombre] = function (...argumentos) {
            return ejecutar(() => original.apply(this, argumentos), mensaje);
        };
    }

    root.PlaneadorCarga = { ejecutar, get activa() { return tareas.size > 0; } };
})(window);
