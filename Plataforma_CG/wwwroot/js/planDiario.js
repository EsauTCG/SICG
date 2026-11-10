let resultadosBusquedaDia = [];
let indiceActualDia = -1;
let ultimoTextoDia = "";
async function recalcularConversionFila(fila) {
    await Promise.all([cargarInyeccionFila(fila), cargarEtiquetacionFila(fila)]);
}
const claveCatalogoDiario = sku => (sku || "").trim().toUpperCase();
const skuConversionDiaria = select => select?.dataset.catalogoDiarioCargado === "true"
    ? select.value : (select?.dataset.skuConvertido || select?.value);

async function consultarCatalogosDiarios(skus) {
    const productos = [...new Set(skus.filter(Boolean))];
    if (!productos.length) return new Map();
    const response = await fetch("/Operaciones/ObtenerCatalogosPlanDiario", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(productos),
        cache: "no-store"
    });
    if (!response.ok) throw new Error("No se pudieron cargar los catálogos del plan diario. Vuelve a abrir el día.");
    return new Map((await response.json()).map(item => [claveCatalogoDiario(item.productoCodigo), item]));
}

function aplicarInyeccionFila(fila, item) {
    fila.querySelector(".td-iny-porcentaje").innerText = item?.porcentaje ?? 0;
    fila.querySelector(".td-iny-tipo").innerText = item?.tipo ?? "";
    fila.querySelector(".td-iny-nombre").innerText = item?.nombre ?? "";
}

function aplicarEtiquetacionFila(fila, item) {
    fila.querySelector(".td-etiq").innerText = item?.nombre ?? "";
    fila.querySelector(".td-caduc").innerText = item?.diasCaducidad ?? "";
}
async function cargarInyeccionFila(fila) {

    
    try {

        const select =
            fila.querySelector(".select-conversion");

        const sku =
            select?.value || fila.dataset.sku;

        const response = await fetch(
            `/Operaciones/ObtenerInyeccion?sku=${encodeURIComponent(sku)}`
        );

        if (!response.ok)
            throw new Error("Error cargando inyección");

        const item =
            await response.json();

        fila.querySelector(".td-iny-porcentaje")
            .innerText = item.iny?.porcentaje ?? 0;

        fila.querySelector(".td-iny-tipo")
            .innerText = item.iny?.tipo ?? "";

        fila.querySelector(".td-iny-nombre")
            .innerText = item.iny?.nombre ?? "";

        recalcularInyeccion();

    }
    catch (err) {

        console.error(
            "Error inyección individual:",
            err
        );

    }

}
async function cargarEtiquetacionFila(fila) {

    const select =
        fila.querySelector(".select-conversion");

    const sku =
        select?.value || fila.dataset.sku;

    try {

        const response = await fetch(
            `/Operaciones/ObtenerEtiquetacionSku?sku=${encodeURIComponent(sku)}`
        );

        if (!response.ok)
            throw new Error("Error cargando etiquetación");

        const item = await response.json();

        fila.querySelector(".td-etiq")
            .innerText = item.etiq?.nombre ?? "";

        fila.querySelector(".td-caduc")
            .innerText = item.etiq?.diasCaducidad ?? "";

    }
    catch (err) {

        console.error(
            "Error etiquetación:",
            err
        );

    }
}
async function cargarCatalogoConversiones(catalogos) {

    const selects =
        document.querySelectorAll(".select-conversion");

    catalogos ??= await consultarCatalogosDiarios([...selects].map(select => select.dataset.skuOriginal));

    for (const select of selects) {

        const skuOriginal =
            select.dataset.skuOriginal;

        const skuConvertidoGuardado =
            skuConversionDiaria(select);

        const conversiones = catalogos.get(claveCatalogoDiario(skuOriginal))?.conversiones ?? [];

        select.replaceChildren();
        select.removeAttribute("title");
        const opciones = new Set();
        const agregarOpcion = (sku, texto = sku) => {
            const option = document.createElement("option");
            option.value = sku;
            option.textContent = texto;
            select.append(option);
            opciones.add(sku);
            return option;
        };
        // El SKU original representa dejar el producto sin convertir.
        agregarOpcion(skuOriginal);
        conversiones.forEach(c => {
            if (!opciones.has(c.skuDestino)) agregarOpcion(c.skuDestino);
        });

        // Seleccionar el SKU guardado en PlanDiario
        if (skuConvertidoGuardado) {

            if (!opciones.has(skuConvertidoGuardado)) {
                // Conservar el valor histórico sin ofrecerlo para nuevas selecciones.
                const historica = agregarOpcion(skuConvertidoGuardado, `${skuConvertidoGuardado} (inactiva)`);
                historica.disabled = true;
                historica.dataset.conversionInactiva = "true";
                select.title = "La conversión guardada está inactiva o ya no está disponible. Se conserva hasta que elijas otra opción.";
            }

            select.value =
                skuConvertidoGuardado;

            const fila =
                select.closest(".fila-producto");

            if (fila) {
                fila.dataset.skuConversion =
                    skuConvertidoGuardado;
            }
        }
        select.dataset.catalogoDiarioCargado = "true";
    }
}
document.addEventListener(
    "change",
    async function (e) {
        if (!e.target.classList.contains("select-conversion"))
            return;
        const select = e.target;
        const historica = select.querySelector('option[data-conversion-inactiva="true"]');
        if (historica && select.value !== historica.value) {
            historica.remove();
            select.removeAttribute("title");
        }
        const fila =
            select.closest(".fila-producto");
        fila.dataset.skuConversion =
            select.value;
        await recalcularConversionFila(fila);
    }
);

function buscarYScrollDiario() {

    const input = document.getElementById("buscadorProductos");
    if (!input) return;

    const texto = input.value.trim().toLowerCase();
    if (!texto) return;

    const filas = document.querySelectorAll("#tablaPlan .fila-producto");
    if (!filas.length) return;

    if (texto !== ultimoTextoDia) {

        resultadosBusquedaDia = [];
        indiceActualDia = -1;

        //filas.forEach(fila => {
        //    const master = fila.children[0]?.innerText.toLowerCase() || "";
        //    const codigo = fila.children[1]?.innerText.toLowerCase() || "";
        //    const nombre = fila.children[2]?.innerText.toLowerCase() || "";

        //    if (
        //        master.includes(texto) ||
        //        codigo.includes(texto) ||
        //        nombre.includes(texto)
        //    ) {
        //        resultadosBusquedaDia.push(fila);
        //    }
        //});
        filas.forEach(fila => {

            const master =
                (fila.dataset.master || "")
                    .toLowerCase();

            const codigo =
                fila.children[1]
                    ?.innerText
                    .toLowerCase() || "";

            const nombre =
                fila.children[2]
                    ?.innerText
                    .toLowerCase() || "";

            if (
                master.includes(texto) ||
                codigo.includes(texto) ||
                nombre.includes(texto)
            ) {
                resultadosBusquedaDia.push(fila);
            }

        });


        ultimoTextoDia = texto;

        if (!resultadosBusquedaDia.length) {
            input.classList.add("is-invalid");
            setTimeout(() => input.classList.remove("is-invalid"), 1000);
            return;
        }
    }
    indiceActualDia++;

    if (indiceActualDia >= resultadosBusquedaDia.length) {
        indiceActualDia = 0;
    }

    const fila = resultadosBusquedaDia[indiceActualDia];

    hacerScrollYResaltarDiario(fila);
    actualizarContador();
}

function hacerScrollYResaltarDiario(fila) {

    if (!fila) return;

    const master = fila.dataset.master;

    const headerBtn = document.querySelector(
        `.btn-toggle-master[data-master='${master}']`
    );

    // 🔵 Expandir master si está cerrado
    if (headerBtn && headerBtn.dataset.closed === "true") {

        const filas = document.querySelectorAll(
            `.fila-producto[data-master='${master}']`
        );

        filas.forEach(f => f.style.display = "");

        headerBtn.dataset.closed = "false";
    }

    // 🔥 esperar a que el DOM termine de aplicar cambios (clave para virtual rendering)
    requestAnimationFrame(() => {
        requestAnimationFrame(() => {

            // limpiar resaltado
            document.querySelectorAll("#tablaPlan .fila-producto")
                .forEach(f => f.classList.remove("table-warning"));

            fila.classList.add("table-warning");

            // =========================
            // 🔍 DETECTAR SCROLLABLE REAL
            // =========================
            const scrollable = getScrollParent(fila) || document.documentElement;

            const isWindowScroll =
                scrollable === document.documentElement ||
                scrollable === document.body ||
                scrollable === document.scrollingElement;

            // =========================
            // 📌 CALCULAR POSICIÓN REAL
            // =========================
            const rect = fila.getBoundingClientRect();

            if (isWindowScroll) {

                const target =
                    rect.top +
                    window.scrollY -
                    (window.innerHeight / 2) +
                    (rect.height / 2);

                window.scrollTo({
                    top: target,
                    behavior: "smooth"
                });

            } else {

                const containerRect = scrollable.getBoundingClientRect();

                const target =
                    scrollable.scrollTop +
                    (rect.top - containerRect.top) -
                    (scrollable.clientHeight / 2) +
                    (rect.height / 2);

                scrollable.scrollTo({
                    top: target,
                    behavior: "smooth"
                });
            }
        });
    });
}

function getScrollParent(element) {

    let parent = element.parentElement;

    while (parent) {

        const style = getComputedStyle(parent);

        const overflowY = style.overflowY;
        const canScroll =
            (overflowY === "auto" || overflowY === "scroll") &&
            parent.scrollHeight > parent.clientHeight;

        if (canScroll) return parent;

        parent = parent.parentElement;
    }

    return null;
}

function actualizarContador() {

    const input = document.getElementById("buscadorProductos");
    if (!input) return;

    const total = resultadosBusquedaDia.length;
    const actual = indiceActualDia + 1;

    input.title = total > 0
        ? `${actual} de ${total}`
        : "Sin resultados";
}

async function exportarExcel() {
    try {
        await PlanDiarioExcel.exportar();
    } catch (error) {
        console.error(error);
        alert(error.message || "No se pudo exportar el plan diario.");
    }
}

async function recalcularTodoDia() {
    window.PlanDiarioAfectaciones?.iniciar();
    const filas =
        [...document.querySelectorAll(".fila-producto")];
    if (!filas.length) return;
    // Una lectura por apertura. No se reutilizan catálogos entre días o actualizaciones.
    const catalogos = await consultarCatalogosDiarios(filas.flatMap(fila => {
        const select = fila.querySelector(".select-conversion");
        return [fila.dataset.sku, select?.dataset.skuOriginal,
            skuConversionDiaria(select)];
    }));
    await cargarCatalogoConversiones(catalogos);
    filas.forEach(fila => {
        recalcularFilaDia(fila, false);
        const sku = fila.querySelector(".select-conversion")?.value || fila.dataset.sku;
        const item = catalogos.get(claveCatalogoDiario(sku));
        aplicarInyeccionFila(fila, item?.iny);
        aplicarEtiquetacionFila(fila, item?.etiq);
    });
    recalcularInyeccion();
    recalcularSubtotalesMaster();
}
function recalcularInyeccion() {
    const filas = document.querySelectorAll(
        "#tablaPlan tbody .fila-producto"
    );
    if (!filas.length) return;
    filas.forEach(fila => {
        const kgBase = parseFloat(
            fila.querySelector(".total-calculado")?.innerText
        ) || 0;
        const tdPorcentaje =
            fila.querySelector(".td-iny-porcentaje");
        const tdKg =
            fila.querySelector(".td-iny-kg");
        if (!tdPorcentaje || !tdKg)
            return;
        const porcentajeIny =
            parseFloat(tdPorcentaje.innerText) || 0;
        const kgFinal =
            kgBase + (kgBase * porcentajeIny / 100);
        tdKg.innerText =
            `${kgFinal.toFixed(2)} Kg`;
    });
}
function recalcularSubtotalesMaster() {
    const grupos = {};
    document.querySelectorAll(".fila-producto").forEach(fila => {
        const master = fila.dataset.master || "SIN MASTER";
        const valor = parseFloat(
            fila.querySelector(".total-calculado")?.innerText
        ) || 0;
        if (!grupos[master])
            grupos[master] = 0;
        grupos[master] += valor;
    });
    document.querySelectorAll(".subtotal-master").forEach(el => {
        const master = el.dataset.master;
        el.innerText = (grupos[master] || 0).toFixed(2);
    });
}
document.addEventListener("click", function (e) {
    if (!e.target.classList.contains("btn-agregar-participacion"))
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
        "form-control form-control-sm input-participacion";
    input.value = "0";
    input.dataset.prev = "0";
    input.dataset.inicial = "true";
    input.min = "0";
    input.max = "1";
    input.step = "0.01";
    input.dataset.kg = boton.dataset.kg;
    input.dataset.canales = boton.dataset.canales;
    input.dataset.subclas = boton.dataset.subclas;
    input.dataset.subclasNombre = boton.dataset.subclasNombre || '';
    boton.replaceWith(input);
    aplicarColor(input);
    const fila = input.closest(".fila-producto");
    recalcularFilaDia(fila);
    input.focus();
});
function recalcularSubtotalesMaster() {
    const grupos = {};
    document.querySelectorAll(".fila-producto")
        .forEach(fila => {
            const master =
                fila.dataset.master || "SIN MASTER";
            const total =
                parseFloat(
                    fila.querySelector(".total-calculado")
                        ?.innerText
                ) || 0;
            grupos[master] =
                (grupos[master] || 0) + total;
        });
    document.querySelectorAll(".subtotal-master")
        .forEach(el => {
            const master = el.dataset.master;
            el.innerText =
                (grupos[master] || 0).toFixed(2);
        });
}


async function cargarInyecciones() {
    const filas = document.querySelectorAll(".fila-producto");
    if (!filas.length)
        return;
    //const skus = [...filas]
    //    .map(f => f.dataset.sku)
    const skus = [...filas]
        .map(f => {
            const select =
                f.querySelector(".select-conversion");
            return select?.value || f.dataset.sku;
        })
        .filter(Boolean);
    try {
        const response = await fetch(
            "/Operaciones/ObtenerInyecciones",
            {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify(skus)
            });
        if (!response.ok)
            throw new Error("Error cargando inyecciones");
        const data = await response.json();
        filas.forEach(fila => {

            const select =
                fila.querySelector(".select-conversion");

            const skuConsulta =
                select?.value || fila.dataset.sku;

            const item =
                data.find(x =>
                    x.productoCodigo == skuConsulta
                );

            if (!item)
                return;

            fila.querySelector(".td-iny-porcentaje")
                .innerText = item.iny?.porcentaje ?? 0;

            fila.querySelector(".td-iny-tipo")
                .innerText = item.iny?.tipo ?? "";
        });
        recalcularInyeccion();
    }
    catch (err) {
        console.error(err);
    }
}
async function cargarEtiquetacion() {
    const filas = document.querySelectorAll(
        ".fila-producto"
    );
    if (!filas.length)
        return;
    //const skus = [...filas]
    //    .map(f => f.dataset.sku)
    const skus = [...filas]
        .map(f => {
            const select =
                f.querySelector(".select-conversion");
            return select?.value || f.dataset.sku;
        })
        .filter(Boolean);
    try {
        const response = await fetch(
            "/Operaciones/ObtenerEtiquetacion",
            {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify(skus)
            });
        if (!response.ok)
            throw new Error(
                "Error cargando etiquetación"
            );
        const data = await response.json();
        console.log(data);
        filas.forEach(fila => {

            const select =
                fila.querySelector(".select-conversion");

            const skuConsulta =
                select?.value || fila.dataset.sku;

            const item =
                data.find(x =>
                    x.productoCodigo == skuConsulta
                );

            if (!item)
                return;

            fila.querySelector(".td-etiq")
                .innerText = item.etiq?.nombre ?? "";

            fila.querySelector(".td-caduc")
                .innerText = item.etiq?.diasCaducidad ?? "";
        });
    }
    catch (err) {
        console.error(
            "Error etiquetación:",
            err
        );
    }
}
async function abrirCatalogoExtra() {
    const tbody =
        document.querySelector("#tablaExtra tbody");
    tbody.innerHTML = "";
    const response =
        await fetch("/Operaciones/ObtenerCatalogoExtra");
    let data =
        await response.json();
    console.log(data);
    const skusExistentes = new Set(
        [...document.querySelectorAll(
            "#tablaPlan tbody tr.fila-producto"
        )]
            .map(x => x.dataset.sku?.trim())
            .filter(Boolean)
    );
    data = data.filter(p =>
        !skusExistentes.has(
            (p.productoCodigo ?? "").trim()
        )
    );
    data.forEach(p => {
        const sku =
            p.productoCodigo ?? "";
        const nombre =
            p.nombre ?? "";
        const porcentaje =
            p.porcentaje ?? 0;
        tbody.insertAdjacentHTML(
            "beforeend",
            `
            <tr>
                <td>${sku}</td>
                <td>${nombre}</td>
                <td>${porcentaje}</td>
                <td>
                    <button class="btn btn-success btn-sm"
                        onclick='agregarSkuExtra(
                            "${sku}",
                            "${nombre.replace(/'/g, "\\'")}",
                            ${porcentaje}
                        )'>
                        Agregar
                    </button>
                </td>
            </tr>
            `
        );
    });
    new bootstrap.Modal(
        document.getElementById("modalExtra")
    ).show();
}


//Creación de Solicitud
async function abrirModalSolicitud() {

    try {
        const modalActual =
            bootstrap.Modal.getInstance(
                document.getElementById(
                    "modalSolicitudes"
                )
            );

        modalActual?.hide();


        const modal =
            new bootstrap.Modal(
                document.getElementById("modalSolicitud")
            );

        // ==========================
        // COMBO
        // ==========================

        const combo =
            document.getElementById("comboSolicitud");

        combo.innerHTML = "";

        const respCombo =
            await fetch(
                "/Operaciones/ObtenerTiposSolicitud"
            );

        const tipos =
            await respCombo.json();

        tipos.forEach(x => {

            combo.insertAdjacentHTML(
                "beforeend",
                `
                <option value="${x.id}">
                    ${x.nombre}
                </option>
                `
            );

        });

        // ==========================
        // TABLA
        // ==========================

        const tbody =
            document.querySelector(
                "#tablaSolicitud tbody"
            );

        tbody.innerHTML = "";

        const respTabla =
            await fetch(
                "/Operaciones/ObtenerSolicitudSkus"
            );

        const data =
            await respTabla.json();

        data.forEach(x => {

            tbody.insertAdjacentHTML(
                "beforeend",
                `
                <tr>

                    <td>
                        ${x.sku}
                    </td>

                    <td>
                        ${x.nombre}
                    </td>

                    <td>

                        <input type="number"
                               class="form-control form-control-sm input-cantidad-solicitud"
                               data-sku="${x.sku}"
                               min="0"
                               step="0.01"
                               value="0">

                    </td>

                </tr>
                `
            );

        });

        modal.show();

    }
    catch (err) {

        console.error(err);

        alert(
            "Error cargando solicitud"
        );

    }

}
async function activarPlantilla(btn) {

    const fecha = btn.dataset.fecha;
    console.log(btn.dataset.fecha);

    try {

        const response = await fetch("/Operaciones/ActivarPlantilla", {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify(
                 fecha
            )
        });

        if (!response.ok) {
            throw new Error("Error al activar la plantilla.");
        }

        const resultado = await response.json();

        alert(resultado.mensaje);

    } catch (error) {

        console.error(error);
        alert("Ocurrió un error al activar la plantilla.");

    }
}
async function guardarSolicitud() {

    try {

        const combo =
            document.getElementById(
                "comboSolicitud"
            );

        const comentarios =
            document.getElementById(
                "txtSolicitudComentarios"
            ).value;

        // FECHA
        const fecha =
            document.getElementById(
                "planeacionData"
            )?.dataset.fecha || "";

        const lista = [];

        const inputs =
            document.querySelectorAll(
                ".input-cantidad-solicitud"
            );

        console.log(
            "Inputs encontrados:",
            inputs.length
        );

        inputs.forEach(input => {

            const cantidad =
                parseFloat(input.value) || 0;

            if (cantidad <= 0)
                return;

            const sku =
                input.dataset.sku;

            console.log(
                "SKU:",
                sku,
                "Cantidad:",
                cantidad
            );

            lista.push({

                SKU:
                    sku,

                Cantidad:
                    cantidad

            });

        });

        console.log(
            "Lista final:",
            lista
        );

        if (lista.length <= 0) {

            alert(
                "Debes capturar al menos un producto"
            );

            return;

        }

        const tipoId =
            combo.value;

        const tipoNombre =
            combo.options[combo.selectedIndex]
                ?.text || "";

        const payload = {

            Fecha:
                fecha,

            TipoId:
                tipoId,

            TipoNombre:
                tipoNombre,

            Comentarios:
                comentarios,

            Productos:
                lista

        };

        console.log(
            "Payload:",
            payload
        );

        const response =
            await fetch(
                "/Operaciones/GuardarSolicitud",
                {
                    method: "POST",

                    headers: {
                        "Content-Type":
                            "application/json"
                    },

                    body:
                        JSON.stringify(payload)
                }
            );

        if (!response.ok) {

            const err =
                await response.text();

            console.error(err);

            throw new Error(err);

        }

        alert(
            "Solicitud guardada"
        );

        bootstrap.Modal.getInstance(
            document.getElementById(
                "modalSolicitud"
            )
        )?.hide();

    }
    catch (err) {

        console.error(err);

        alert(
            "Error guardando solicitud"
        );

    }

}

//Solicitudes
async function abrirSolicitudes() {

    const modal =
        new bootstrap.Modal(
            document.getElementById(
                "modalSolicitudes"
            )
        );

    modal.show();

    await cargarSolicitudes();

}
async function cargarSolicitudes() {

    const contenedor =
        document.getElementById(
            "contenedorSolicitudes"
        );
    const fecha =
        document.getElementById(
            "planeacionData"
        )?.dataset.fecha || "";
    contenedor.innerHTML =
        "<div>Cargando...</div>";

    const response =
        await fetch(
            `/Operaciones/ObtenerSolicitudes?fecha=${fecha}`
        );

    const solicitudes =
        await response.json();

    contenedor.innerHTML = "";

    solicitudes.forEach(s => {

        contenedor.insertAdjacentHTML(
            "beforeend",
            `
            <div class="col-md-4">

                <div class="card shadow-sm h-100 solicitud-card"
                     style="cursor:pointer;"
                     onclick="verSolicitud(${s.solicitudId})">

                    <div class="card-body">

                        <h5 class="card-title">
                            Solicitud #${s.solicitudId}
                        </h5>

                        <p class="mb-1">
                            ${s.nombre}
                        </p>

                        <p class="mb-1">
                            ${s.nombre}
                        </p>

                        <span class="badge bg-primary">
                            ${s.estatus}
                        </span>

                    </div>

                </div>

            </div>
            `
        );

    });

}

async function verSolicitud(id) {

    const [responseSolicitud, responseEstatus] =
        await Promise.all([
            fetch(`/Operaciones/ObtenerSolicitudDetalle?id=${id}`),
            fetch(`/Operaciones/ObtenerEstatusSolicitud`)
        ]);

    const solicitud = await responseSolicitud.json();
    const estatuses = await responseEstatus.json();

    const opcionesEstatus =
        estatuses.map(e => `
            <option value="${e.estatusId}"
                ${String(e.estatusId) === String(solicitud.estatusId) ? "selected" : ""}
            >
                ${e.nombre}
            </option>
        `).join("");
    console.log("ESTATUS LIST:", estatuses);
    console.log("SOLICITUD:", solicitud);
    console.log("ESTATUS ID:", solicitud.estatusId);

    document.getElementById("detalleSolicitud").innerHTML = `

        <!-- ID oculto -->
        <input type="hidden" id="solicitudIdEditar" value="${solicitud.id}" />

        <!-- TIPO OCULTO (ya no se muestra) -->
        <input type="hidden" id="tipoSolicitud" value="${solicitud.tipoNombre || ""}" />

        <div class="mb-3">
            <strong>Solicitud:</strong> ${solicitud.id}
        </div>

        <!-- ESTATUS EDITABLE -->
        <div class="mb-3">
            <label class="form-label">Estatus</label>

            <select id="comboEstatusSolicitud" class="form-select">
                ${opcionesEstatus}
            </select>
        </div>

        <!-- COMENTARIOS EDITABLES -->
        <div class="mb-3">
            <label class="form-label">Comentarios</label>

            <textarea
                id="comentariosSolicitud"
                class="form-control"
                rows="3"
            >${solicitud.comentarios || ""}</textarea>
        </div>

        <hr>

        <table class="table table-sm">

            <thead>
                <tr>
                    <th>SKU</th>
                    <th>Cantidad</th>
                </tr>
            </thead>

            <tbody>
                ${(solicitud.productos || []).map(x => `
                    <tr>

                        <td>${x.articulo}</td>

                        <td>
                            <input
                                type="number"
                                class="form-control input-cantidad-detalle"
                                data-articulo="${x.articulo}"
                                value="${x.cantidad}"
                                min="0"
                                step="1"
                            >
                        </td>

                    </tr>
                `).join("")}
            </tbody>

        </table>

        <div class="text-end">

            <button
                class="btn btn-success"
                onclick="guardarCambiosSolicitud()">

                Guardar cambios
            </button>

        </div>
    `;

    new bootstrap.Modal(
        document.getElementById("modalDetalleSolicitud")
    ).show();
}

function abrirNuevaSolicitud() {

    const modalActual =
        bootstrap.Modal.getInstance(
            document.getElementById(
                "modalSolicitudes"
            )
        );

    modalActual?.hide();

    new bootstrap.Modal(
        document.getElementById(
            "modalSolicitud"
        )
    ).show();

}
document.addEventListener("DOMContentLoaded", async function () {
    await recalcularTodoDia();
    
});
async function guardarPlanDiario() {
    if (!window.PlanDiarioAfectaciones) { alert('No se cargaron las afectaciones. Recarga la página antes de guardar.'); return; }
    if (window.PlanDiarioAfectaciones && !window.PlanDiarioAfectaciones.validar()) return;

    const filas =
        document.querySelectorAll(
            "#tablaPlan tbody .fila-producto"
        );

    const lista = [];

    filas.forEach(fila => {

        const planeacionId =
            parseInt(fila.dataset.planeacion) || 0;

        const sku =
            fila.dataset.sku;

        const porcentaje =
            parseFloat(
                fila.dataset.porcentaje
            ) || 0;

        const kg =
            parseFloat(
                fila.querySelector(".total-calculado")
                    .innerText
            ) || 0;

        const canales =
            parseInt(
                fila.querySelector(".total-canales")
                    .innerText
            ) || 0;

        const select =
            fila.querySelector(".select-conversion");

        const skuConvertido =
            select?.value || sku;

        const porcentajeInyeccion =
            parseFloat(
                fila.querySelector(".td-iny-porcentaje")
                    ?.innerText
            ) || 0;

        const kgInyeccion =
            parseFloat(
                (
                    fila.querySelector(".td-iny-kg")
                        ?.innerText || "0"
                )
                    .replace("Kg", "")
                    .trim()
            ) || 0;

        const participaciones = [];

        fila.querySelectorAll(".input-participacion")
            .forEach(input => {

                const partSub =
                    parseFloat(input.value) || 0;

                const subclas =
                    parseInt(input.dataset.subclas) || 0;

                participaciones.push({
                    PlanId: planeacionId,
                    fk_SubClas: subclas,
                    ProductoCodigo: sku,
                    PartSub: partSub
                });

            });

        lista.push({

            PlaneacionId: planeacionId,
            ProductoCodigo: sku,
            ProductoCodigoConvertido: skuConvertido,
            PorcentajeInyeccion: porcentajeInyeccion,
            KgInyeccion: kgInyeccion,
            Porcentaje: porcentaje,
            KgLote: kg,
            Canales: canales,

            Participaciones: participaciones

        });

    });

    try {

        const response =
            await fetch(
                "/Operaciones/GuardarPlanDiario",
                {
                    method: "POST",
                    headers: {
                        "Content-Type":
                            "application/json",
                        "X-PlanDiario-Catalogo": document.getElementById("planeacionData")?.dataset.catalogoVersion || "",
                        "RequestVerificationToken": document.querySelector('#planeacionData input[name="__RequestVerificationToken"]')?.value || ""
                    },
                    body:
                        JSON.stringify(lista)
                }
            );

        if (!response.ok) {
            const error = await response.json().catch(() => ({}));
            throw new Error(
                error.mensaje || "Error al guardar"
            );
        }

        const resultado =
            await response.json();

        alert(
            "Plan diario guardado correctamente"
        );

    }
    catch (error) {

        console.error(error);

        alert(
            error.message || "Error al guardar el plan diario"
        );

    }

}
function aplicarColor(input) {
    let valor = parseFloat(input.value) || 0;
    const td = input.closest("td");
    td.classList.remove("td-0", "td-parcial", "td-1");
    if (valor === 0) {
        td.classList.add("td-0");
    }
    else if (valor === 1) {
        td.classList.add("td-1");
    }
    else if (valor > 0 && valor < 1) {
        td.classList.add("td-parcial");
    }
}
function recalcularFilaDia(fila, actualizarTotales = true) {
    const porcentaje = parseFloat(fila.dataset.porcentaje) || 0;
    let totalCalculado = 0;
    let totalCanales = 0;
    const inputs = fila.querySelectorAll(".input-participacion");
    inputs.forEach(input => {
        aplicarColor(input);
        let participacion = parseFloat(input.value) || 0;
        let kg = parseFloat(input.dataset.kg) || 0;
        let canales = parseInt(input.dataset.canales) || 0;
        totalCalculado += porcentaje / 100 * participacion * kg * canales;
        totalCanales += participacion * canales;
    });
    fila.querySelector(".total-calculado").textContent =
        totalCalculado.toFixed(2);
    fila.querySelector(".total-canales").textContent =
        totalCanales.toFixed(0);
    if (actualizarTotales) {
        recalcularInyeccion();
        recalcularSubtotalesMaster();
    }
}
document.addEventListener("keydown", function (e) {

    const input = document.getElementById("buscadorProductos");
    if (!input) return;

    const activo = document.activeElement === input;

    if (activo && e.key === "Enter") {
        e.preventDefault();
        buscarYScrollDiario();
    }
    if (e.key === "F3") {
        e.preventDefault();
        buscarYScrollDiario();
    }
    if (activo && (e.key === "ArrowDown" || e.key === "ArrowUp")) {

        e.preventDefault();

        if (!resultadosBusquedaDia.length) return;

        if (e.key === "ArrowDown") indiceActualDia++;
        else indiceActualDia--;

        if (indiceActualDia >= resultadosBusquedaDia.length) indiceActualDia = 0;
        if (indiceActualDia < 0) indiceActualDia = resultadosBusquedaDia.length - 1;

        hacerScrollYResaltarDiario(resultadosBusquedaDia[indiceActualDia]);
        actualizarContador();
    }
});
document.addEventListener("input", function (e) {

    if (e.target.id === "buscadorProductos") {
        indiceActualDia = -1;
        resultadosBusquedaDia = [];
        ultimoTextoDia = "";
    }
});
document.addEventListener("change", function (e) {
    if (!e.target.classList.contains("input-participacion"))
        return;
    window.PlanDiarioAfectaciones?.cambiar(e.target);
});
function toggleAllMasters(expandir) {
    const botones = document.querySelectorAll(".btn-toggle-master");
    botones.forEach(btn => {
        const master = btn.dataset.master;
        const filas = document.querySelectorAll(
            `.fila-producto[data-master='${master}']`
        );
        filas.forEach(f => {
            f.style.display = expandir ? "" : "none";
        });
        btn.dataset.closed = (!expandir).toString();
        const subtotal =
            document.querySelector(
                `.subtotal-master[data-master='${master}']`
            )?.innerText || "0.00";
        btn.innerHTML = `
            ${expandir ? "▼" : "▶"} ${master}
            <span class="float-end">
                Subtotal:
                <span class="subtotal-master" data-master="${master}">
                    ${parseFloat(subtotal).toFixed(2)}
                </span>
            </span>
        `;
    });
}
function asegurarMasterExtra() {
    let masterRow = document.querySelector(
        ".master-header[data-master='EXTRA']"
    );
    if (masterRow)
        return;
    const tbody =
        document.querySelector("#tablaPlan tbody");
    tbody.insertAdjacentHTML(
        "beforeend",
        `
        <tr class="master-header"
            data-master="EXTRA">
            <td colspan="999">
                <button class="btn-toggle-master"
                        type="button"
                        data-master="EXTRA">
                    ▼ EXTRA
                    <span class="float-end">
                        Subtotal:
                        <span class="subtotal-master"
                            data-master="EXTRA">
                            0.00
                        </span>
                    </span>
                </button>
            </td>
        </tr> 
        `
    );
}
async function agregarSkuExtra(
    sku,
    nombre,
    porcentaje
) {
    asegurarMasterExtra();
    const yaExiste = document.querySelector(
        `.fila-producto[data-sku='${sku}']`
    );
    if (yaExiste) {
        alert("Ese SKU ya existe");
        return;
    }
    const tbody =
        document.querySelector("#tablaPlan tbody");
    const canales =
        document.querySelectorAll(
            "#tablaPlan thead tr:nth-child(3) th"
        );
    let htmlParticipaciones = "";
    document.querySelectorAll(
        ".input-participacion[data-subclas]"
    );
    const primeraFila =
        document.querySelector(".fila-producto");
    const inputsBase =
        primeraFila.querySelectorAll(
            ".input-participacion"
        );
    inputsBase.forEach(input => {
        htmlParticipaciones += `
            <td>
                <input type="number"
                    class="form-control form-control-sm input-participacion"
                    value="0"
                    data-prev="0"
                    min="0"
                    max="1"
                    step="0.01"
                    data-kg="${input.dataset.kg}"
                    data-canales="${input.dataset.canales}"
                    data-subclas="${input.dataset.subclas}" data-subclas-nombre="${input.dataset.subclasNombre || ''}" />
            </td>
        `;
    });
    const filaHtml = `
        <tr class="fila-producto"
            data-master="EXTRA"
            data-porcentaje="${porcentaje}"
            data-sku="${sku}"
            data-linea=""
            data-extra="true"
            data-planeacion="0">
            <td>
                <button class="btn btn-danger btn-sm btn-eliminar-extra">
                    X
                </button>
            </td>
            <td class="td-codigo">
                ${sku}
            </td>
            <td>
                ${nombre}
            </td>
            ${htmlParticipaciones}
            <td class="porcentaje col-gris">
                ${porcentaje}
            </td>
            <td class="total-calculado col-gris">
                0.00
            </td>
            <td class="total-canales col-gris">
                0.00
            </td>
            <td class="col-iny">
                ${sku}
            </td>
            <td class="col-iny td-iny-nombre">
                ${nombre}
            </td>
            <td class="col-iny td-iny-porcentaje"
                data-sku="${sku}">
                ...
            </td>
            <td class="col-iny td-iny-tipo"
                data-sku="${sku}">
                ...
            </td>
            <td class="col-iny td-iny-kg"
                data-sku="${sku}">
                0.00Kg
            </td>
            <td class="td-etiq"
                data-sku="${sku}">
                ...
            </td>
            <td class="td-caduc"
                data-sku="${sku}">
                ...
            </td>
        </tr>
    `;
    const masterExtra =
        document.querySelector(
            ".master-header[data-master='EXTRA']"
        );
    masterExtra.insertAdjacentHTML(
        "afterend",
        filaHtml
    );
    const filaInsertada =
        masterExtra.nextElementSibling;
    filaInsertada?.scrollIntoView({
        behavior: "smooth",
        block: "center"
    });
    await recalcularTodoDia();
    const modalEl =
        document.getElementById("modalExtra");
    const modal =
        bootstrap.Modal.getOrCreateInstance(modalEl);
    modal.hide();
    recalcularSubtotalesMaster();   
}
document.addEventListener("click", function (e) {
    const btn =
        e.target.closest(".btn-toggle-master");
    if (!btn)
        return;
    const master =
        btn.dataset.master;
    const filas =
        document.querySelectorAll(
            `.fila-producto[data-master='${master}']`
        );
    const cerrado =
        btn.dataset.closed === "true";
    filas.forEach(f => {

        f.style.display =
            cerrado ? "" : "none";
    });
    btn.dataset.closed =
        (!cerrado).toString();
    btn.innerHTML =
        `${cerrado ? "▼" : "▶"} ${master}
    <span class="subtotal-master-wrapper">
        Subtotal:
        <span class="subtotal-master"
            data-master="${master}">
            ${(parseFloat(
            document.querySelector(
                `.subtotal-master[data-master='${master}']`
            )?.innerText
        ) || 0).toFixed(2)}
        </span>
    </span>`;
});
document.addEventListener(
    "click",
    function (e) {
        const btn =
            e.target.closest(
                ".btn-eliminar-extra"
            );
        if (!btn)
            return;
        const fila =
            btn.closest(".fila-producto");
        fila.remove();
        recalcularSubtotalesMaster();
        recalcularInyeccion();
    });
