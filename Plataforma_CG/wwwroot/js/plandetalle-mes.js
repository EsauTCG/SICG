async function cargarInyeccionFilaMensual(fila) {

    try {

        const sku = fila.dataset.sku;

        const response = await fetch(
            `/Operaciones/ObtenerInyeccion?sku=${encodeURIComponent(sku)}`
        );

        if (!response.ok)
            throw new Error();

        const item = await response.json();

        fila.querySelector(".td-iny-porcentaje-mensual").innerText =
            item.iny?.porcentaje ?? 0;

        fila.querySelector(".td-iny-tipo-mensual").innerText =
            item.iny?.tipo ?? "";

        fila.querySelector(".td-iny-nombre-mensual").innerText =
            item.iny?.nombre ?? "";
        

    }
    catch (err) {

        console.error(
            "Error cargando inyección:",
            err
        );

    }

}
async function cargarInyeccionesMensuales(wrapper) {

    const filas = wrapper.querySelectorAll(".fila-producto-mensual");

    for (const fila of filas) {
        await cargarInyeccionFilaMensual(fila);
        
    }

}
async function cargarCatalogoConversionesMes() {

    const selects =
        document.querySelectorAll(".select-conversion-mes");

    console.log(
        "Selects encontrados:",
        selects.length
    );

    console.log(
        "Tabla existe:",
        !!document.querySelector("#tablaPlan")
    );

    for (const select of selects) {

        const skuOriginal =
            select.dataset.skuOriginal;

        const skuConvertidoGuardado =
            select.dataset.skuConvertido;

        console.log(
            "Consultando SKU:",
            skuOriginal
        );

        const resp = await fetch(
            `/Operaciones/ObtenerConversiones?sku=${skuOriginal}`
        );

        console.log(
            "Status:",
            resp.status
        );

        const conversiones =
            await resp.json();

        console.log(
            "Resultado:",
            conversiones
        );

        select.innerHTML = `
            <option value="${skuOriginal}">
                ${skuOriginal}
            </option>
        `;

        conversiones.forEach(c => {

            select.insertAdjacentHTML(
                "beforeend",
                `
                <option value="${c.skuDestino}">
                    ${c.skuDestino}
                </option>
                `
            );

        });

        // Seleccionar el SKU guardado en PlanDiario

    }
}
function recalcularInyeccionMensual(wrapper) {

    const filas = wrapper.querySelectorAll(".fila-producto-mensual");

    filas.forEach(fila => {

        const kgBase =
            parseFloat(
                fila.querySelector(".total-calculado-mensual")?.textContent
            ) || 0;

        const porcentajeIny =
            parseFloat(
                fila.querySelector(".td-iny-porcentaje-mensual")?.textContent
            ) || 0;

        const kgFinal =
            kgBase + (kgBase * porcentajeIny / 100);

        const tdKg =
            fila.querySelector(".td-iny-kg-mensual");

        if (tdKg) {
            tdKg.textContent =
                `${kgFinal.toFixed(2)} Kg`;
        }

    });

}
function recalcularSubtotalesMasterMensual(wrapper) {

    const grupos = {};

    wrapper.querySelectorAll(".fila-producto-mensual")
        .forEach(fila => {

            const master =
                fila.dataset.master || "SIN MASTER";

            const valor =
                parseFloat(
                    fila.querySelector(".total-calculado-mensual")?.textContent
                ) || 0;

            grupos[master] =
                (grupos[master] || 0) + valor;

        });

    wrapper.querySelectorAll(".subtotal-master")
        .forEach(span => {

            const master =
                span.dataset.master;

            span.textContent =
                (grupos[master] || 0).toFixed(2);

        });

}
function recalcularPlantillaMensual(wrapper) {

    if (!wrapper)
        return;

    const filas = wrapper.querySelectorAll(".fila-producto-mensual");

    filas.forEach(fila => {
        recalcularFilaMes(fila);
    });

}
function recalcularFilaMes(fila) {
    const porcentaje = parseFloat(fila.dataset.porcentaje) || 0;
    let totalCalculado = 0;
    let totalCanales = 0;
    const inputs = fila.querySelectorAll(".inputmes-participacion");
    inputs.forEach(input => {
        aplicarColor(input);
        let participacion = parseFloat(input.value) || 0;
        let kg = parseFloat(input.dataset.kg) || 0;
        let canales = parseInt(input.dataset.canales) || 0;
        totalCalculado += porcentaje / 100 * participacion * kg * canales;
        totalCanales += participacion * canales;
    });
    const wrapper = fila.closest(".js-planeacion-mensual");
    fila.querySelector(".total-calculado-mensual").textContent =
        totalCalculado.toFixed(2);
    fila.querySelector(".total-canales-mensual").textContent =
        totalCanales.toFixed(0);
    recalcularInyeccionMensual(wrapper);
    recalcularSubtotalesMasterMensual(wrapper);
}
document.addEventListener("click", function (e) {
    if (!e.target.classList.contains("btn-agregar-participacionmes"))
        return;
    const boton = e.target;
    const confirmar = confirm(
        "¿Deseas habilitar esta participación?"
    );
    if (!confirmar)
        return;
    const input = document.createElement("input");
    input.type = "number";
    input.className =
        "form-control form-control-sm inputmes-participacion";
    input.value = "0";
    input.dataset.prev = "0";
    input.dataset.inicial = "true";
    input.min = "0";
    input.max = "1";
    input.step = "0.01";
    input.dataset.kg = boton.dataset.kg;
    input.dataset.canales = boton.dataset.canales;
    input.dataset.subclas = boton.dataset.subclas;
    boton.replaceWith(input);
    aplicarColor(input);
    const fila = input.closest(".fila-producto-mensual");
    recalcularFilaMes(fila);
    input.focus();
});
document.addEventListener("input", function (e) {
    if (!e.target.classList.contains("inputmes-participacion"))
        return;
    const input = e.target;
    const fila = input.closest(".fila-producto-mensual");
    const productoPadre = fila.dataset.linea;
    const valorAnterior = parseFloat(input.dataset.prev) || 0;
    const valorNuevo = parseFloat(input.value) || 0;
    const diferencia = valorNuevo - valorAnterior;
    input.dataset.prev = valorNuevo;
    if (productoPadre && productoPadre !== "") {
        const subclas = input.dataset.subclas;
        const filaPadre = document.querySelector(
            `.fila-producto[data-sku='${productoPadre}']`
        );
        if (filaPadre) {
            const inputPadre = filaPadre.querySelector(
                `.input-participacion[data-subclas='${subclas}']`
            );
            if (inputPadre) {
                let valorPadre = parseFloat(inputPadre.value) || 0;
                valorPadre -= diferencia;
                if (valorPadre < 0)
                    valorPadre = 0;
                inputPadre.value = valorPadre.toFixed(2);
                aplicarColor(inputPadre);
                /*recalcularFilaDia(filaPadre);*/
            }
        }
    }
    recalcularFilaMes(fila);
});
document.addEventListener("change", async function (e) {

    if (!e.target.classList.contains("select-conversion-mes"))
        return;

    const select = e.target;

    const skuNuevo = select.value;
    const skuOriginal = select.dataset.skuOriginal;

    if (skuNuevo === skuOriginal)
        return;

    const filaPadre = select.closest(".fila-producto-mensual");

    if (!filaPadre)
        return;

    // Evitar duplicados
    const existe = filaPadre.parentElement.querySelector(
        `.fila-producto-mensual[data-linea="${skuOriginal}"][data-sku="${skuNuevo}"]`
    );

    if (existe) {

        select.value = skuOriginal;
        return;

    }

    const porcentaje =
        filaPadre.dataset.porcentaje;

    const master =
        filaPadre.dataset.master;

    const celdasParticipacion =
        [...filaPadre.querySelectorAll(".inputmes-participacion, .btn-agregar-participacionmes")]
            .map(c => {

                if (c.tagName === "INPUT") {

                    return `
                <td>
                    <input
                        type="number"
                        class="form-control form-control-sm inputmes-participacion"
                        value="0"
                        data-prev="0"
                        data-inicial="true"
                        min="0"
                        max="1"
                        step="0.01"
                        data-kg="${c.dataset.kg}"
                        data-canales="${c.dataset.canales}"
                        data-subclas="${c.dataset.subclas}">
                </td>`;

                }

                return `
            <td>
                <button
                    type="button"
                    class="btn btn-outline-secondary btn-sm btn-agregar-participacionmes"
                    data-kg="${c.dataset.kg}"
                    data-canales="${c.dataset.canales}"
                    data-subclas="${c.dataset.subclas}">
                    +
                </button>
            </td>`;

            }).join("");

    const nuevaFila = document.createElement("tr");

    nuevaFila.className =
        "fila-producto-mensual fila-derivado";

    nuevaFila.dataset.master = master;
    nuevaFila.dataset.linea = skuOriginal;
    nuevaFila.dataset.sku = skuNuevo;
    nuevaFila.dataset.porcentaje = porcentaje;

    nuevaFila.innerHTML = `

        <td></td>

        <td class="td-codigo">
            ${skuNuevo}
        </td>

        <td>...</td>

        ${celdasParticipacion}

        <td class="porcentaje-mensual col-gris">
            ${porcentaje}
        </td>

        <td class="total-calculado-mensual col-gris">
            0.00
        </td>

        <td class="total-canales-mensual col-gris">
            0
        </td>

        <td class="col-iny">
            ${skuNuevo}
        </td>

        <td class="td-iny-nombre-mensual">
            ...
        </td>

        <td class="td-iny-porcentaje-mensual">
            ...
        </td>

        <td class="td-iny-tipo-mensual">
            ...
        </td>

        <td class="td-iny-kg-mensual">
            0.00Kg
        </td>
    `;

    filaPadre.insertAdjacentElement(
        "afterend",
        nuevaFila
    );

    // Eliminar del catálogo
    select.querySelector(`option[value="${skuNuevo}"]`)?.remove();

    // Regresar el select al SKU original
    select.value = skuOriginal;

    await cargarInyeccionFilaMensual(nuevaFila);

    recalcularFilaMes(nuevaFila);

});

async function guardarPlanMensual(btn) {

    const wrapper = btn.closest(".js-planeacion-mensual");

    if (!wrapper)
        return;

    const modelo = {

        fecha: wrapper.dataset.fecha,
        clasif: wrapper.dataset.clasif,
        productos: []

    };

    const filas =
        wrapper.querySelectorAll(".fila-producto-mensual");

    filas.forEach(fila => {

        const producto = {

            productoCodigo: fila.dataset.sku,

            kgInyeccion:
                parseFloat(
                    fila.querySelector(".td-iny-kg-mensual")
                        ?.textContent
                        ?.replace("Kg", "")
                        ?.trim()
                ) || 0,

            subClasificaciones: []

        };

        fila.querySelectorAll(".inputmes-participacion")
            .forEach(input => {

                producto.subClasificaciones.push({

                    subClasificacionId:
                        parseInt(input.dataset.subclas),
                        
                    participacion:
                        parseFloat(input.value) || 0

                });

            });

        modelo.productos.push(producto);

    });

    console.log(modelo);
    console.log(JSON.stringify(modelo));

    const response = await fetch(
        "/Planeacion/GuardarPlanMensual",
        {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify(modelo)
        });

    if (!response.ok) {

        alert("Error al guardar");
        return;

    }
    else {
        alert("Guardado con éxito");
        return;
    }

}