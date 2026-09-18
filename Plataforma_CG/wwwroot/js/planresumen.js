async function abrirResumenPlaneacion() {

    const modalElement =
        document.getElementById("modalResumenPlaneacion");

    const contenedor =
        document.getElementById("contenedorResumenPlaneacion");

    contenedor.innerHTML = `
        <div class="text-center p-4">
            <div class="spinner-border"></div>
            <div class="mt-2">
                Cargando Resumen de Planeación...
            </div>
        </div>
    `;

    const modal =
        bootstrap.Modal.getOrCreateInstance(modalElement);

    modal.show();

    try {

        const response = await fetch(
            "/Operaciones/ResumenPlaneacion",
            {
                method: "GET",
                headers: {
                    "X-Requested-With": "XMLHttpRequest"
                }
            }
        );

        const textoRespuesta = await response.text();

        console.log("Status:", response.status);
        console.log("Respuesta servidor:", textoRespuesta);

        if (!response.ok) {

            throw new Error(
                `HTTP ${response.status}: ${textoRespuesta}`
            );
        }

        contenedor.innerHTML = textoRespuesta;

        inicializarResumenPlaneacion();

    }
    catch (error) {

        console.error(
            "Error cargando ResumenPlaneacion:",
            error
        );

        contenedor.innerHTML = `
            <div class="alert alert-danger m-2">
                <strong>Error al cargar Resumen de Planeación</strong>
                <hr>
                <pre class="mb-0"
                     style="white-space:pre-wrap;">${error.message}</pre>
            </div>
        `;
    }
}
function inicializarResumenPlaneacion() {

    const contenedor =
        document.getElementById(
            "contenedorResumenPlaneacion"
        );

    if (!contenedor)
        return;

    console.log(
        "ResumenPlaneacion inicializado."
    );
}
async function refrescarResumenPlaneacion() {

    const contenedor =
        document.querySelector(".resumen-planeacion");

    if (!contenedor)
        return;

    const fechaInicial =
        document.getElementById(
            "fechaInicialResumenPlaneacion"
        ).value;

    const fechaFinal =
        document.getElementById(
            "fechaFinalResumenPlaneacion"
        ).value;

    const clasificacion =
        document.getElementById(
            "clasificacionResumenPlaneacion"
        ).value;

    const tbody =
        document.getElementById(
            "tbodyResumenPlaneacion"
        );

    if (!fechaInicial || !fechaFinal) {
        alert("Selecciona la fecha inicial y final.");
        return;
    }

    if (!clasificacion) {
        alert("Selecciona una clasificación.");
        return;
    }

    tbody.innerHTML = `
        <tr>
            <td colspan="9" class="text-center p-4">
                <div class="spinner-border"></div>
                <div class="mt-2">
                    Cargando información...
                </div>
            </td>
        </tr>
    `;

    try {

        const params = new URLSearchParams({
            fechaInicial: fechaInicial,
            fechaFinal: fechaFinal,
            clasificacionId: clasificacion
        });

        const response = await fetch(
            `/Operaciones/ObtenerResumenPlaneacion?${params.toString()}`
        );

        if (!response.ok) {

            const error = await response.text();

            throw new Error(
                `HTTP ${response.status}: ${error}`
            );
        }

        const resultado = await response.json();

        console.log(
            "Datos ResumenPlaneacion:",
            resultado
        );

        // =====================================================
        // EL ENDPOINT DEVUELVE DIRECTAMENTE LA LISTA
        // =====================================================

        if (!Array.isArray(resultado)) {

            throw new Error(
                "La respuesta del servidor no tiene el formato esperado."
            );
        }

        tbody.innerHTML = "";

        construirTablaResumenPlaneacion(
            tbody,
            resultado
        );

        // =====================================================
        // LA TABLA YA ESTÁ CONSTRUIDA
        // AHORA CONSULTAMOS PRODUCCIÓN
        // =====================================================

        await cargarPorcentajeInyeccionProduccionResumen();

        await cargarKgEmpaqueProduccionResumen();
        recalcularCumplimientoResumenPlaneacion();

    }
    catch (error) {

        console.error(
            "Error cargando ResumenPlaneacion:",
            error
        );

        tbody.innerHTML = `
            <tr>
                <td colspan="9"
                    class="text-center text-danger p-4">

                    Error al cargar la información.

                    <div class="small mt-2">
                        ${error.message}
                    </div>

                </td>
            </tr>
        `;
    }
}
function construirTablaResumenPlaneacion(tbody, datos) {

    if (!datos || !datos.length) {

        tbody.innerHTML = `
            <tr>
                <td colspan="9"
                    class="text-center p-4">
                    No se encontraron registros.
                </td>
            </tr>
        `;

        return;
    }

    /*
     * Orden:
     *
     * 1. Master
     * 2. SKU Desh.
     * 3. SKU Empacado
     */

    datos.sort((a, b) => {

        const masterA =
            (a.master || "SIN MASTER").toUpperCase();

        const masterB =
            (b.master || "SIN MASTER").toUpperCase();

        const masterComparacion =
            masterA.localeCompare(
                masterB,
                undefined,
                {
                    numeric: true,
                    sensitivity: "base"
                }
            );

        if (masterComparacion !== 0)
            return masterComparacion;

        const skuA =
            (a.productoCodigo || "").toUpperCase();

        const skuB =
            (b.productoCodigo || "").toUpperCase();

        const skuComparacion =
            skuA.localeCompare(
                skuB,
                undefined,
                {
                    numeric: true,
                    sensitivity: "base"
                }
            );

        if (skuComparacion !== 0)
            return skuComparacion;

        const convertidoA =
            (a.productoCodigoConvertido || "").toUpperCase();

        const convertidoB =
            (b.productoCodigoConvertido || "").toUpperCase();

        return convertidoA.localeCompare(
            convertidoB,
            undefined,
            {
                numeric: true,
                sensitivity: "base"
            }
        );
    });


    let masterActual = null;

    datos.forEach(item => {

        const master =
            item.master || "SIN MASTER";

        /*
         * Header del Master
         */

        if (masterActual !== master) {

            masterActual = master;

            const trMaster =
                document.createElement("tr");

            trMaster.className =
                "master-header-resumen";

            trMaster.dataset.master =
                master;

            trMaster.innerHTML = `
                <td colspan="9"
                    class="text-start fw-bold">

                    ${master}

                </td>
            `;

            tbody.appendChild(trMaster);
        }


        /*
         * Valores
         */

        const skuDesh =
            item.productoCodigo || "";

        const skuEmpacado =
            item.productoCodigoConvertido ||
            skuDesh;

        const kgDesh =
            Number(item.kgNatural || 0);

        const porcentajeInyeccion =
            Number(item.porcentajeInyeccion || 0);

        const kgEmpaque =
            Number(item.kgInyeccion || 0);

        const porcentaje =
            Number(item.porcentaje || 0);


        /*
         * Fila
         */

        const tr =
            document.createElement("tr");

        tr.className =
            "fila-resumen-planeacion";

        /*
         * Estos atributos serán utilizados
         * posteriormente por las funciones
         * de producción.
         */

        tr.dataset.skuDesh =
            skuDesh;

        tr.dataset.skuEmpacado =
            skuEmpacado;


        tr.innerHTML = `

            <td class="td-master">
                ${master}
            </td>

            <td class="td-sku-desh">
                ${skuDesh}
            </td>

            <td class="td-sku-empacado">
                ${skuEmpacado}
            </td>

            <td class="td-kg-desh-plan">
                ${kgDesh.toFixed(2)}
            </td>

            <td class="td-porcentaje-inyeccion-esperado">
                ${porcentajeInyeccion.toFixed(2)} %
            </td>

            <!--
                Producción:
                inicialmente vacío
            -->

            <td class="td-porcentaje-inyeccion-produccion">
                -
            </td>

            <td class="td-kg-empaque-plan">
                ${kgEmpaque.toFixed(2)}
            </td>

            <!--
                Producción:
                inicialmente vacío
            -->

            <td class="td-kg-empaque-produccion">
                -
            </td>

            <td class="td-porcentaje">
                -
            </td>

        `;

        tbody.appendChild(tr);
    });
}

function formatearNumeroResumen(valor) {

    const numero =
        Number(valor) || 0;


    return numero.toLocaleString(
        "es-MX",
        {
            minimumFractionDigits: 2,
            maximumFractionDigits: 2
        }
    );

}
function escapeHtml(valor) {

    if (
        valor === null ||
        valor === undefined
    )
        return "";


    return String(valor)
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#039;");

}
function actualizarContadoresMastersResumen() {

    const masters =
        document.querySelectorAll(
            "#tbodyResumenPlaneacion .master-header-resumen"
        );


    masters.forEach(header => {

        const master =
            header.dataset.master;


        const filas =
            document.querySelectorAll(
                `#tbodyResumenPlaneacion
                .fila-resumen-planeacion[data-master="${CSS.escape(master)}"]`
            );


        const contador =
            header.querySelector(
                ".contador-master-resumen"
            );


        if (contador) {

            contador.textContent =
                filas.length;

        }

    });

}
async function cargarPorcentajeInyeccionProduccionResumen() {

    const filas =
        document.querySelectorAll(
            ".resumen-planeacion .fila-resumen-planeacion"
        );

    if (!filas.length)
        return;

    // Obtener filtros actuales
    const fechaInicio =
        document.getElementById(
            "fechaInicialResumenPlaneacion"
        ).value;

    const fechaFin =
        document.getElementById(
            "fechaFinalResumenPlaneacion"
        ).value;

    const clasId =
        document.getElementById(
            "clasificacionResumenPlaneacion"
        ).value;

    for (const fila of filas) {

        const skuEmpacado =
            fila.dataset.skuEmpacado;

        const celda =
            fila.querySelector(
                ".td-porcentaje-inyeccion-produccion"
            );

        if (!skuEmpacado || !celda)
            continue;

        try {

            const params = new URLSearchParams({
                FechaInicio: fechaInicio,
                FechaFin: fechaFin,
                SKU: skuEmpacado,
                ClasId: clasId
            });

            const response = await fetch(
                `/Operaciones/ObtenerPorcentajeInyeccionProduccion?${params.toString()}`
            );

            if (!response.ok)
                throw new Error(
                    `HTTP ${response.status}`
                );

            const resultado =
                await response.json();

            console.log(
                "Porcentaje producción:",
                {
                    FechaInicio: fechaInicio,
                    FechaFin: fechaFin,
                    SKU: skuEmpacado,
                    ClasId: clasId,
                    resultado: resultado
                }
            );

            const porcentaje =
                resultado.porcentaje ??
                resultado.data?.porcentaje ??
                0;

            celda.textContent =
                `${Number(porcentaje).toFixed(2)} %`;

        }
        catch (error) {

            console.error(
                `Error obteniendo % inyección producción para ${skuEmpacado}:`,
                error
            );

            celda.textContent = "-";
        }
    }
}
async function cargarKgEmpaqueProduccionResumen() {

    const filas =
        document.querySelectorAll(
            ".resumen-planeacion .fila-resumen-planeacion"
        );

    if (!filas.length)
        return;

    // Obtener filtros actuales
    const fechaInicio =
        document.getElementById(
            "fechaInicialResumenPlaneacion"
        ).value;

    const fechaFin =
        document.getElementById(
            "fechaFinalResumenPlaneacion"
        ).value;

    const clasId =
        document.getElementById(
            "clasificacionResumenPlaneacion"
        ).value;

    for (const fila of filas) {

        const skuEmpacado =
            fila.dataset.skuEmpacado;

        const celda =
            fila.querySelector(
                ".td-kg-empaque-produccion"
            );

        if (!skuEmpacado || !celda)
            continue;

        try {

            const params = new URLSearchParams({
                FechaIn: fechaInicio,
                FechaFin: fechaFin,
                SKU: skuEmpacado,
                ClasId: clasId
            });

            const response = await fetch(
                `/Operaciones/ObtenerKgEmpaqueProduccion?${params.toString()}`
            );

            if (!response.ok)
                throw new Error(
                    `HTTP ${response.status}`
                );

            const resultado =
                await response.json();

            console.log(
                "Kg empaque producción:",
                {
                    FechaInicio: fechaInicio,
                    FechaFin: fechaFin,
                    SKU: skuEmpacado,
                    ClasId: clasId,
                    resultado: resultado
                }
            );

            const kg =
                resultado.kg ??
                resultado.kgEmpaque ??
                resultado.data?.kg ??
                0;

            celda.textContent =
                `${Number(kg).toFixed(2)}`;

        }
        catch (error) {

            console.error(
                `Error obteniendo Kg Empaque Producción para ${skuEmpacado}:`,
                error
            );

            celda.textContent = "-";
        }
    }
}
function recalcularCumplimientoResumenPlaneacion() {

    const filas = document.querySelectorAll(
        ".resumen-planeacion .fila-resumen-planeacion"
    );

    filas.forEach(fila => {

        const celdaPlan =
            fila.querySelector(".td-kg-empaque-plan");

        const celdaProduccion =
            fila.querySelector(".td-kg-empaque-produccion");

        const celdaCumplimiento =
            fila.querySelector(".td-porcentaje");

        if (!celdaPlan ||
            !celdaProduccion ||
            !celdaCumplimiento) {

            return;
        }

        // ================================
        // KG EMPAQUE PLAN
        // ================================

        const textoPlan =
            celdaPlan.textContent
                .replace(/Kg/gi, "")
                .replace(/,/g, "")
                .trim();

        const kgPlan =
            parseFloat(textoPlan) || 0;


        // ================================
        // KG EMPAQUE PRODUCCIÓN
        // ================================

        const textoProduccion =
            celdaProduccion.textContent
                .replace(/Kg/gi, "")
                .replace(/,/g, "")
                .trim();

        const kgProduccion =
            parseFloat(textoProduccion) || 0;


        // ================================
        // CÁLCULO
        // ================================

        if (kgPlan <= 0) {

            celdaCumplimiento.textContent =
                "0.00 %";

            return;
        }

        const cumplimiento =
            (kgProduccion / kgPlan) * 100;


        // ================================
        // RESULTADO
        // ================================

        celdaCumplimiento.textContent =
            `${cumplimiento.toFixed(2)} %`;
    });
}