// ===============================
// CARGAR SEGUNDA VISTA PARCIAL
// ===============================
// ===============================
// CARGAR DETALLES POR CLASIFICACIÓN
// ===============================
async function cargarDetallesClasificacionMensual() {

    const contenedores = document.querySelectorAll(".detalle-clasificacion-container");

    for (const fila of contenedores) {

        const clasificacionId = fila.dataset.clasificacionId;
        const fecha = fila.dataset.fecha;

        const contenedor = fila.querySelector(".contenedor-detalle-mensual");

        try {

            const response = await fetch(
                `/Planeacion/CargarDetalleClasificacionMensual?clasificacionId=${clasificacionId}&fecha=${encodeURIComponent(fecha)}`
            );

            if (!response.ok)
                throw new Error();

            contenedor.innerHTML = await response.text();

            // Inicializar únicamente esta clasificación
            const wrapper = contenedor.querySelector(".js-planeacion-mensual");

            if (wrapper) {
                await inicializarDetalleMensual(wrapper);
            }

        }
        catch {

            contenedor.innerHTML = `
                <div class="alert alert-danger m-2">
                    Error al cargar el detalle.
                </div>`;
        }
    }
}


// ===============================
// INICIALIZAR UNA CLASIFICACIÓN
// ===============================
async function inicializarDetalleMensual(wrapper) {

    recalcularPlantillaMensual(wrapper);
    await cargarInyeccionesMensuales(wrapper);
    await cargarCatalogoConversionesMes();

    // Aquí irán agregándose todas las funciones propias
    // de la vista mensual.
    //
    // await cargarCatalogoMensual(wrapper);
    // inicializarBuscadorMensual(wrapper);
    // inicializarMastersMensual(wrapper);
    // recalcularTotalesMensuales(wrapper);
}


// ===============================
// INICIALIZAR TODA LA PLANTILLA
// ===============================
async function inicializarPlantillaMensual() {

    await cargarDetallesClasificacionMensual();

}






// Ejecutar automáticamente cuando la parcial ya exista en el DOM
queueMicrotask(() => {
    inicializarPlantillaMensual();
});


// ===============================
// RECÁLCULO AUTOMÁTICO
// ===============================
document.addEventListener("input", function (e) {

    if (
        e.target.classList.contains("cantidad") ||
        e.target.classList.contains("peso-promedio")
    ) {
        recalcularFila(e.target.closest("tr"));
    }
});

function recalcularFila(fila) {

    if (!fila) return;

    const cantidad = parseInt(
        fila.querySelector(".cantidad").value
    ) || 0;

    const pesoProm = parseFloat(
        fila.querySelector(".peso-promedio").value
    ) || 0;

    const pesoTotal = cantidad * pesoProm;

    fila.querySelector(".peso-total").value =
        pesoTotal.toFixed(2);

    recalcularSubclasificacion(fila);
}

function recalcularSubclasificacion(filaDetalle) {

    let subclasRow = filaDetalle.previousElementSibling;

    while (subclasRow && !subclasRow.classList.contains("subclas-row")) {
        subclasRow = subclasRow.previousElementSibling;
    }

    if (!subclasRow) return;

    let totalCanales = 0;
    let totalPeso = 0;

    let detalles = [];
    let fila = subclasRow.nextElementSibling;

    while (fila && fila.classList.contains("detalle-row")) {

        const cant = parseInt(
            fila.querySelector(".cantidad")?.value
        ) || 0;

        const peso = parseFloat(
            fila.querySelector(".peso-total")?.value
        ) || 0;

        totalCanales += cant;
        totalPeso += peso;

        detalles.push(fila);

        fila = fila.nextElementSibling;
    }

    const pesoProm = totalCanales > 0
        ? totalPeso / totalCanales
        : 0;

    subclasRow.querySelector(".cantidad").value =
        totalCanales;

    subclasRow.querySelector(".peso-total").value =
        totalPeso.toFixed(2);

    subclasRow.querySelector(".peso-promedio").value =
        pesoProm.toFixed(2);

    detalles.forEach(d => {

        const canalesDetalle = parseFloat(
            d.querySelector(".cantidad")?.value
        ) || 0;

        const porcentaje = totalCanales > 0
            ? (canalesDetalle / totalCanales) * 100
            : 0;

        const inputPorc = d.querySelector(".porcentaje");

        if (inputPorc)
            inputPorc.value = porcentaje.toFixed(2);
    });
}


// ===============================
// GUARDAR PLANTILLA MENSUAL
// ===============================
function guardarPlantillaMensual() {

    const filas = document.querySelectorAll("tr.detalle-row");
    const lista = [];

    filas.forEach(fila => {

        const tdNombre = fila.querySelector("td");

        lista.push({
            Id: parseInt(fila.dataset.id) || 0,
            Fecha: fila.dataset.fecha,
            SkuClasificacion: fila.dataset.sku,
            NombreClasificacion: tdNombre ? tdNombre.innerText.replace("└", "").trim() : "",
            Canales: parseInt(fila.querySelector(".cantidad")?.value) || 0,
            PesoPromedio: parseFloat(fila.querySelector(".peso-promedio")?.value) || 0,
            PesoTotal: parseFloat(fila.querySelector(".peso-total")?.value) || 0,
            Porcentaje: parseFloat(fila.querySelector(".porcentaje")?.value) || 0,
        });

    });

    console.log("📦 Enviando planeación mensual:", lista);

    fetch("/Operaciones/GuardarPlanMensual", {
        method: "POST",
        headers: {
            "Content-Type": "application/json"
        },
        body: JSON.stringify(lista)
    })
        .then(r => {
            if (!r.ok) throw new Error("Error al guardar");
            return r.json();
        })
        .then(() => {
            volverASemanalDesdePlaneador();
        })
        .catch(err => {
            alert("Error al guardar la planeación mensual");
            console.error(err);
        });
}


// ===============================
// REGRESAR A SEMANAL
// ===============================
function volverASemanalDesdePlaneador() {

    document.getElementById("modoSemanal").checked = true;

    if (typeof mostrarSemanal === "function") {
        mostrarSemanal();
    }

    const contenedor = document.getElementById("contenedor-planeador");

    if (contenedor)
        contenedor.innerHTML = "";
}