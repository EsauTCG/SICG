let productosEtiquetacion = [];

let etiquetasResumen = [];
let etiquetasCargadas = false;
let autocompleteEtiquetasInicializado = false;


/* =========================================================
   INICIALIZACIÓN
========================================================= */

document.addEventListener("DOMContentLoaded", () => {

    inicializarEtiquetacion();

});


/* =========================================================
   PRODUCTOS
========================================================= */

function inicializarEtiquetacion() {

    const buscador =
        document.getElementById(
            "buscadorArticulos"
        );

    if (!buscador)
        return;


    /*
     * Primera carga
     */
    cargarProductosEtiquetacion("");


    /*
     * Nueva búsqueda cada vez
     * que cambia el texto.
     */
    buscador.addEventListener(
        "input",
        function () {

            cargarProductosEtiquetacion(
                this.value.trim()
            );

        }
    );

}


/* =========================================================
   BUSCAR PRODUCTOS
========================================================= */

async function cargarProductosEtiquetacion(busqueda) {

    const tbody =
        document.getElementById(
            "tbodyArticulos"
        );

    if (!tbody)
        return;


    tbody.innerHTML = `
        <tr>
            <td colspan="3"
                class="text-center p-4">

                <div class="spinner-border"
                     role="status">
                </div>

                <div class="mt-2">
                    Buscando...
                </div>

            </td>
        </tr>
    `;


    try {

        const params =
            new URLSearchParams();

        params.append(
            "busq",
            busqueda || ""
        );


        const response =
            await fetch(
                `/Operaciones/BuscarProductos?${params.toString()}`
            );


        if (!response.ok) {

            const error =
                await response.text();

            throw new Error(
                `HTTP ${response.status}: ${error}`
            );

        }


        const resultado =
            await response.json();


        console.log(
            "Respuesta BuscarProductos:",
            resultado
        );


        if (Array.isArray(resultado)) {

            productosEtiquetacion =
                resultado;

        }
        else {

            productosEtiquetacion =
                resultado.datos ||
                resultado.data ||
                resultado.resultado ||
                [];

        }


        renderizarProductosEtiquetacion(
            productosEtiquetacion
        );

    }
    catch (error) {

        console.error(
            "Error cargando productos:",
            error
        );


        tbody.innerHTML = `
            <tr>
                <td colspan="3"
                    class="text-center text-danger p-4">

                    Error al cargar los productos.

                    <div class="small mt-2">
                        ${escapeHtml(error.message)}
                    </div>

                </td>
            </tr>
        `;

    }

}


/* =========================================================
   CONSTRUIR TABLA
========================================================= */

function renderizarProductosEtiquetacion(productos) {

    const tbody =
        document.getElementById(
            "tbodyArticulos"
        );

    if (!tbody)
        return;


    tbody.innerHTML = "";


    if (
        !Array.isArray(productos) ||
        productos.length === 0
    ) {

        tbody.innerHTML = `
            <tr>
                <td colspan="3"
                    class="text-center text-muted p-4">

                    No se encontraron productos.

                </td>
            </tr>
        `;

        return;
    }


    productos.forEach(producto => {

        console.log(
            "Producto recibido:",
            producto
        );


        const sku =
            producto.sku ?? "";

        const productoNombre =
            producto.producto ?? "";

        const etiquetacion =
            producto.etiquetacion ?? 0;

        const nombre =
            producto.nombre ?? "";

        const diasCaducidad =
            producto.diasCaducidad ?? "";


        const tr =
            document.createElement("tr");


        /*
         * Datos internos
         */
        tr.dataset.sku =
            sku;

        tr.dataset.producto =
            productoNombre;

        tr.dataset.etiquetacion =
            etiquetacion;

        tr.dataset.nombre =
            nombre;

        tr.dataset.diasCaducidad =
            diasCaducidad;


        tr.classList.add(
            "fila-producto-etiquetacion"
        );


        /*
         * Datos visibles
         */
        tr.innerHTML = `

            <td class="td-articulo-id">
                ${escapeHtml(sku)}
            </td>

            <td class="td-producto">
                ${escapeHtml(productoNombre)}
            </td>

            <td class="td-etiqueta">
                ${escapeHtml(nombre)}
            </td>

        `;


        /*
         * Toda la fila abre el modal.
         */
        tr.addEventListener(
            "click",
            function () {

                abrirModalProducto(
                    this
                );

            }
        );


        tbody.appendChild(tr);

    });

}


/* =========================================================
   ABRIR MODAL
========================================================= */

async function abrirModalProducto(fila) {

    const modalElement =
        document.getElementById("modalDetalleArticulo");

    if (!modalElement)
        return;

    /*
     * =========================================
     * OBTENER DATOS DE LA FILA
     * =========================================
     */

    const sku =
        fila.dataset.sku || "";

    const producto =
        fila.dataset.producto || "";

    const etiquetacion =
        fila.dataset.etiquetacion || "";

    const nombre =
        fila.dataset.nombre || "";

    const diasCaducidad =
        fila.dataset.diasCaducidad || "";


    /*
     * =========================================
     * CARGAR CATÁLOGO DE ETIQUETAS
     * =========================================
     */

    await cargarEtiquetas();


    /*
     * =========================================
     * LLENAR DATOS DEL ARTÍCULO
     * =========================================
     */

    const inputArticulo =
        document.getElementById(
            "detalleArticuloId"
        );

    const inputProducto =
        document.getElementById(
            "detalleProducto"
        );

    const inputEtiqueta =
        document.getElementById(
            "detalleEtiqueta"
        );

    const inputNombre =
        document.getElementById(
            "detalleNombre"
        );

    const inputCaducidad =
        document.getElementById(
            "detalleDiasCaducidad"
        );

    const inputColector =
        document.getElementById(
            "detalleColectorId"
        );


    if (inputArticulo)
        inputArticulo.value = sku;

    if (inputProducto)
        inputProducto.value = producto;


    /*
     * =========================================
     * BUSCAR ETIQUETA ACTUAL
     * =========================================
     */

    const etiquetaActual =
        etiquetasResumen.find(etiqueta =>
            String(etiqueta.colectorId) ===
            String(etiquetacion)
        );


    if (etiquetaActual) {

        /*
         * Usamos el objeto completo del catálogo
         */
        seleccionarEtiqueta(
            etiquetaActual
        );

    }
    else {

        /*
         * El artículo no tiene una etiqueta
         * que coincida con el catálogo.
         */

        if (inputEtiqueta)
            inputEtiqueta.value = "";

        if (inputColector)
            inputColector.value = "";

        /*
         * Como no existe una etiqueta seleccionada,
         * podemos conservar los datos que regresó
         * BuscarProductos.
         */

        if (inputNombre)
            inputNombre.value = nombre;

        if (inputCaducidad)
            inputCaducidad.value =
                diasCaducidad;

    }


    /*
     * =========================================
     * ABRIR MODAL
     * =========================================
     */

    const modal =
        bootstrap.Modal.getOrCreateInstance(
            modalElement
        );

    modal.show();
}


/* =========================================================
   CARGAR ETIQUETAS
========================================================= */

async function cargarEtiquetas() {

    /*
     * Si ya fueron cargadas,
     * solamente nos aseguramos de que
     * el autocomplete esté inicializado.
     */
    if (etiquetasCargadas) {

        inicializarAutocompleteEtiquetas();

        return;
    }


    try {

        const response =
            await fetch(
                "/Operaciones/ListarEtiquetas"
            );


        if (!response.ok) {

            throw new Error(
                `HTTP ${response.status}`
            );

        }


        const resultado =
            await response.json();


        console.log(
            "Etiquetas recibidas:",
            resultado
        );


        if (Array.isArray(resultado)) {

            etiquetasResumen =
                resultado;

        }
        else {

            etiquetasResumen =
                resultado.datos ||
                resultado.data ||
                resultado.resultado ||
                [];

        }


        etiquetasCargadas = true;


        inicializarAutocompleteEtiquetas();

    }
    catch (error) {

        console.error(
            "Error cargando etiquetas:",
            error
        );

    }

}


/* =========================================================
   AUTOCOMPLETE
========================================================= */

function inicializarAutocompleteEtiquetas() {

    if (autocompleteEtiquetasInicializado)
        return;

    const input =
        document.getElementById(
            "detalleEtiqueta"
        );

    const lista =
        document.getElementById(
            "listaEtiquetasAutocomplete"
        );

    if (!input || !lista)
        return;

    autocompleteEtiquetasInicializado = true;



    /*
     * Buscar mientras escribe.
     */
    input.addEventListener(
        "input",
        function () {

            const texto =
                this.value
                    .trim()
                    .toLowerCase();


            /*
             * Al modificar manualmente
             * la etiqueta, eliminamos
             * la selección anterior.
             */
            document.getElementById(
                "detalleColectorId"
            ).value = "";


            document.getElementById(
                "detalleNombre"
            ).value = "";


            document.getElementById(
                "detalleDiasCaducidad"
            ).value = "";


            if (!texto) {

                ocultarAutocomplete();

                return;

            }


            const coincidencias =
                etiquetasResumen.filter(
                    etiqueta => {

                        const nombre =
                            String(
                                etiqueta.nombre ?? ""
                            ).toLowerCase();


                        return nombre.includes(
                            texto
                        );

                    }
                );


            mostrarResultadosAutocomplete(
                coincidencias
            );

        }
    );


    /*
     * Click fuera del autocomplete.
     */
    document.addEventListener(
        "click",
        function (event) {

            if (
                event.target !== input &&
                !lista.contains(
                    event.target
                )
            ) {

                ocultarAutocomplete();

            }

        }
    );

}


/* =========================================================
   MOSTRAR RESULTADOS
========================================================= */

function mostrarResultadosAutocomplete(
    coincidencias
) {

    const lista =
        document.getElementById(
            "listaEtiquetasAutocomplete"
        );


    if (!lista)
        return;


    lista.innerHTML = "";


    if (
        !coincidencias ||
        coincidencias.length === 0
    ) {

        const elemento =
            document.createElement("div");


        elemento.className =
            "list-group-item text-muted";


        elemento.textContent =
            "No se encontraron etiquetas.";


        lista.appendChild(
            elemento
        );


        lista.style.display =
            "block";


        return;

    }


    coincidencias.forEach(
        etiqueta => {

            const elemento =
                document.createElement(
                    "button"
                );


            elemento.type =
                "button";


            elemento.className =
                "list-group-item list-group-item-action";


            elemento.innerHTML = `
                <div>
                    <strong>
                        ${escapeHtml(
                etiqueta.nombre
            )}
                    </strong>
                </div>

                <small class="text-muted">
                    Caducidad:
                    ${escapeHtml(
                etiqueta.caducidad
            )}
                    días
                </small>
            `;


            elemento.addEventListener(
                "click",
                function (event) {

                    /*
                     * Evitamos que el click
                     * llegue al document.
                     */
                    event.stopPropagation();


                    seleccionarEtiqueta(
                        etiqueta
                    );

                }
            );


            lista.appendChild(
                elemento
            );

        }
    );


    lista.style.display =
        "block";

}


/* =========================================================
   SELECCIONAR ETIQUETA
========================================================= */

function seleccionarEtiqueta(
    etiqueta
) {

    if (!etiqueta)
        return;


    const input =
        document.getElementById(
            "detalleEtiqueta"
        );

    const colectorId =
        document.getElementById(
            "detalleColectorId"
        );

    const nombre =
        document.getElementById(
            "detalleNombre"
        );

    const caducidad =
        document.getElementById(
            "detalleDiasCaducidad"
        );


    if (input) {

        input.value =
            etiqueta.nombre ?? "";

    }


    if (colectorId) {

        colectorId.value =
            etiqueta.colectorId ?? "";

    }


    if (nombre) {

        nombre.value =
            etiqueta.nombre ?? "";

    }


    if (caducidad) {

        caducidad.value =
            etiqueta.caducidad ?? "";

    }


    ocultarAutocomplete();

}


/* =========================================================
   LIMPIAR ETIQUETA
========================================================= */

function limpiarDatosEtiqueta() {

    const input =
        document.getElementById(
            "detalleEtiqueta"
        );

    const colectorId =
        document.getElementById(
            "detalleColectorId"
        );

    const nombre =
        document.getElementById(
            "detalleNombre"
        );

    const caducidad =
        document.getElementById(
            "detalleDiasCaducidad"
        );


    if (input)
        input.value = "";


    if (colectorId)
        colectorId.value = "";


    if (nombre)
        nombre.value = "";


    if (caducidad)
        caducidad.value = "";


    ocultarAutocomplete();

}


/* =========================================================
   OCULTAR AUTOCOMPLETE
========================================================= */

function ocultarAutocomplete() {

    const lista =
        document.getElementById(
            "listaEtiquetasAutocomplete"
        );


    if (!lista)
        return;


    lista.innerHTML = "";

    lista.style.display =
        "none";

}


/* =========================================================
   ESCAPE HTML
========================================================= */

async function guardarEtiquetaArticulo() {

    const sku =
        document.getElementById(
            "detalleArticuloId"
        )?.value?.trim();

    const etiqueta =
        document.getElementById(
            "detalleColectorId"
        )?.value;


    /*
     * =========================================
     * VALIDACIONES
     * =========================================
     */

    if (!sku) {

        alert(
            "No se encontró el SKU del artículo."
        );

        return;
    }


    if (!etiqueta) {

        alert(
            "Selecciona una etiqueta."
        );

        return;
    }


    const boton =
        document.getElementById(
            "btnGuardarEtiqueta"
        );


    try {

        /*
         * Deshabilitar botón mientras guarda
         */

        if (boton) {

            boton.disabled = true;

            boton.innerHTML = `
                <span class="spinner-border spinner-border-sm me-1"></span>
                Guardando...
            `;
        }


        /*
         * =========================================
         * ENVIAR AL ENDPOINT
         * =========================================
         */

        const params =
            new URLSearchParams();

        params.append(
            "sku",
            sku
        );

        params.append(
            "etiqueta",
            etiqueta
        );


        const response =
            await fetch(
                "/Operaciones/ModificarEtiqueta",
                {
                    method: "POST",

                    headers: {
                        "Content-Type":
                            "application/x-www-form-urlencoded; charset=UTF-8"
                    },

                    body:
                        params.toString()
                }
            );


        if (!response.ok) {

            const error =
                await response.text();

            throw new Error(
                `HTTP ${response.status}: ${error}`
            );
        }


        const resultado =
            await response.json();


        console.log(
            "Resultado ModificarEtiqueta:",
            resultado
        );


        /*
         * =========================================
         * RESPUESTA DEL ENDPOINT
         * =========================================
         */

        /*
         * Si tu método retorna algún objeto
         * indicando éxito, puedes validar aquí.
         */

        if (
            resultado &&
            resultado.ok === false
        ) {

            throw new Error(
                resultado.mensaje ||
                "No fue posible modificar la etiqueta."
            );
        }


        /*
         * =========================================
         * ACTUALIZAR LA FILA
         * =========================================
         */

        const fila =
            document.querySelector(
                `.fila-producto-etiquetacion[data-sku="${CSS.escape(sku)}"]`
            );


        if (fila) {

            const celdaEtiqueta =
                fila.querySelector(
                    ".td-etiqueta"
                );

            const inputEtiqueta =
                document.getElementById(
                    "detalleEtiqueta"
                );


            if (
                celdaEtiqueta &&
                inputEtiqueta
            ) {

                celdaEtiqueta.textContent =
                    inputEtiqueta.value;
            }


            /*
             * También actualizamos los datos
             * almacenados en la fila.
             */

            fila.dataset.etiquetacion =
                etiqueta;

            fila.dataset.nombre =
                inputEtiqueta?.value || "";

            const caducidad =
                document.getElementById(
                    "detalleDiasCaducidad"
                )?.value || "";

            fila.dataset.diasCaducidad =
                caducidad;
        }


        /*
         * =========================================
         * CERRAR MODAL
         * =========================================
         */

        const modalElement =
            document.getElementById(
                "modalDetalleArticulo"
            );

        if (modalElement) {

            const modal =
                bootstrap.Modal.getInstance(
                    modalElement
                );

            if (modal)
                modal.hide();
        }


        /*
         * Mensaje
         */

        alert(
            "Etiqueta modificada correctamente."
        );

    }
    catch (error) {

        console.error(
            "Error modificando etiqueta:",
            error
        );

        alert(
            "No fue posible guardar la etiqueta.\n\n" +
            error.message
        );

    }
    finally {

        /*
         * Reactivar botón
         */

        if (boton) {

            boton.disabled = false;

            boton.textContent =
                "Guardar";
        }

    }

}
function escapeHtml(valor) {

    return String(valor ?? "")
        .replace(
            /&/g,
            "&amp;"
        )
        .replace(
            /</g,
            "&lt;"
        )
        .replace(
            />/g,
            "&gt;"
        )
        .replace(
            /"/g,
            "&quot;"
        )
        .replace(
            /'/g,
            "&#039;"
        );

}