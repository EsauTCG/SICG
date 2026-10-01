(function () {
    "use strict";

    const desde = document.getElementById("logDesde");
    const hasta = document.getElementById("logHasta");
    const sku = document.getElementById("logSku");
    const producto = document.getElementById("logProducto");
    const estado = document.getElementById("logEstado");
    const boton = document.getElementById("btnConsultarBitacora");
    const filas = document.getElementById("logFilas");
    const resumen = document.getElementById("logResumen");
    const dialogo = document.getElementById("logPayloadDialog");
    const payloadTitulo = document.getElementById("logPayloadTitulo");
    const payloadMeta = document.getElementById("logPayloadMeta");
    const payloadMensaje = document.getElementById("logPayloadMensaje");
    const payloadContenido = document.getElementById("logPayloadContenido");
    const dialogoReimpresion = document.getElementById("logReprintDialog");
    const formularioReimpresion = document.getElementById("logReprintForm");
    const reimpresionMeta = document.getElementById("logReprintMeta");
    const reimpresionIp = document.getElementById("logReprintIp");
    const reimpresionEstado = document.getElementById("logReprintEstado");
    const impresoras = document.getElementById("logPrinterIps");
    const botonReimpresion = document.getElementById("btnConfirmarReimpresion");
    const botonCerrarReimpresion = document.getElementById("btnCerrarReimpresion");
    const botonCancelarReimpresion = document.getElementById("btnCancelarReimpresion");
    let registroReimpresion = null;
    let solicitudReimpresionGuid = null;
    let reimpresionEnCurso = false;

    function valor(objeto, camel, pascal) {
        return objeto?.[camel] ?? objeto?.[pascal];
    }

    function fechaInputLocal(fecha) {
        const anio = fecha.getFullYear();
        const mes = String(fecha.getMonth() + 1).padStart(2, "0");
        const dia = String(fecha.getDate()).padStart(2, "0");
        return `${anio}-${mes}-${dia}`;
    }

    function configurarFechas() {
        const hoy = new Date();
        const inicio = new Date(hoy);
        inicio.setDate(inicio.getDate() - 7);
        desde.value = fechaInputLocal(inicio);
        hasta.value = fechaInputLocal(hoy);
    }

    function crearCelda(texto, clase, etiqueta) {
        const td = document.createElement("td");
        td.textContent = texto ?? "";
        if (clase) td.className = clase;
        if (etiqueta) td.dataset.label = etiqueta;
        return td;
    }

    function formatearFecha(valorFecha) {
        if (!valorFecha) return "—";
        const fecha = new Date(valorFecha);
        if (Number.isNaN(fecha.getTime())) return String(valorFecha);
        return fecha.toLocaleString("es-MX", {
            year: "numeric",
            month: "2-digit",
            day: "2-digit",
            hour: "2-digit",
            minute: "2-digit",
            second: "2-digit"
        });
    }

    function formatearNumero(valorNumero, decimales) {
        const numero = Number(valorNumero ?? 0);
        return Number.isFinite(numero) ? numero.toFixed(decimales) : "0.000";
    }

    function mostrarVacio(mensaje) {
        filas.replaceChildren();
        const tr = document.createElement("tr");
        tr.className = "iny-log-empty-row";
        const td = crearCelda(mensaje, "iny-log-empty");
        td.colSpan = 12;
        tr.appendChild(td);
        filas.appendChild(tr);
    }

    function renderEstado(valorEstado) {
        const span = document.createElement("span");
        const texto = String(valorEstado || "PENDIENTE").toUpperCase();
        span.textContent = texto;
        span.className = `iny-log-status ${texto.toLowerCase()}`;
        return span;
    }

    function renderFilas(datos) {
        filas.replaceChildren();
        actualizarImpresoras(datos);

        for (const item of datos) {
            const tr = document.createElement("tr");
            const id = valor(item, "id", "Id");
            const entradaId = valor(item, "entradaId", "EntradaId");
            const folio = valor(item, "folio", "Folio") || "Sin folio";
            const hash = valor(item, "payloadSha256", "PayloadSha256") || "";
            const estadoValor = valor(item, "estado", "Estado") || "PENDIENTE";

            tr.appendChild(crearCelda(
                formatearFecha(valor(item, "fechaSolicitudUtc", "FechaSolicitudUtc")),
                "",
                "Fecha"));

            const tdEstado = document.createElement("td");
            tdEstado.dataset.label = "Estado";
            tdEstado.appendChild(renderEstado(estadoValor));
            tr.appendChild(tdEstado);

            tr.appendChild(crearCelda(`#${entradaId} · ${folio}`, "", "Entrada / folio"));
            tr.appendChild(crearCelda(valor(item, "sku", "SKU") || "", "", "SKU"));
            tr.appendChild(crearCelda(valor(item, "producto", "Producto") || "", "", "Producto"));
            tr.appendChild(crearCelda(valor(item, "lote", "Lote") || "", "", "Lote"));
            tr.appendChild(crearCelda(
                `${formatearNumero(valor(item, "peso", "Peso"), 3)} / ${formatearNumero(valor(item, "tara", "Tara"), 3)} kg`,
                "",
                "Peso / tara"));
            tr.appendChild(crearCelda(valor(item, "impresoraIp", "ImpresoraIp") || "", "", "Impresora"));
            tr.appendChild(crearCelda(valor(item, "usuario", "Usuario") || "", "", "Usuario"));
            tr.appendChild(crearCelda(
                valor(item, "esReimpresion", "EsReimpresion") ? "Reimpresión" : "Primera",
                "",
                "Tipo"));

            const tdHash = document.createElement("td");
            tdHash.dataset.label = "Huella";
            const hashSpan = document.createElement("span");
            hashSpan.className = "iny-log-hash";
            hashSpan.textContent = hash;
            hashSpan.title = hash;
            tdHash.appendChild(hashSpan);
            tr.appendChild(tdHash);

            const tdDetalle = document.createElement("td");
            tdDetalle.className = "iny-log-actions";
            tdDetalle.dataset.label = "Acciones";
            const btnDetalle = document.createElement("button");
            btnDetalle.type = "button";
            btnDetalle.className = "iny-log-detail-btn";
            btnDetalle.textContent = "Ver ZPL";
            btnDetalle.addEventListener("click", () => abrirDetalle(id));
            tdDetalle.appendChild(btnDetalle);

            const btnReimprimir = document.createElement("button");
            btnReimprimir.type = "button";
            btnReimprimir.className = "iny-log-reprint-btn";
            btnReimprimir.textContent = "Reimprimir";
            btnReimprimir.addEventListener("click", () => abrirReimpresion(item));
            tdDetalle.appendChild(btnReimprimir);
            tr.appendChild(tdDetalle);

            filas.appendChild(tr);
        }
    }

    function actualizarImpresoras(datos) {
        const direcciones = [...new Set(datos
            .map(item => String(valor(item, "impresoraIp", "ImpresoraIp") || "").trim())
            .filter(Boolean))]
            .sort((a, b) => a.localeCompare(b, undefined, { numeric: true }));

        impresoras.replaceChildren();
        for (const direccion of direcciones) {
            const opcion = document.createElement("option");
            opcion.value = direccion;
            impresoras.appendChild(opcion);
        }
    }

    function crearGuidOperacion() {
        if (window.crypto?.randomUUID) return window.crypto.randomUUID();

        const bytes = new Uint8Array(16);
        window.crypto.getRandomValues(bytes);
        bytes[6] = (bytes[6] & 0x0f) | 0x40;
        bytes[8] = (bytes[8] & 0x3f) | 0x80;
        const hex = [...bytes].map(byte => byte.toString(16).padStart(2, "0"));
        return `${hex.slice(0, 4).join("")}-${hex.slice(4, 6).join("")}-${hex.slice(6, 8).join("")}-${hex.slice(8, 10).join("")}-${hex.slice(10).join("")}`;
    }

    function abrirReimpresion(item) {
        registroReimpresion = item;
        solicitudReimpresionGuid = crearGuidOperacion();
        reimpresionEnCurso = false;

        const id = valor(item, "id", "Id");
        const skuRegistro = valor(item, "sku", "SKU") || "Sin SKU";
        const productoRegistro = valor(item, "producto", "Producto") || "Sin producto";
        reimpresionMeta.textContent = `Registro ${id} · ${skuRegistro} · ${productoRegistro}`;
        reimpresionIp.value = valor(item, "impresoraIp", "ImpresoraIp") || "";
        reimpresionEstado.textContent = "";
        reimpresionEstado.className = "iny-log-reprint-status";
        botonReimpresion.disabled = false;
        botonCerrarReimpresion.disabled = false;
        botonCancelarReimpresion.disabled = false;
        reimpresionIp.disabled = false;
        botonReimpresion.textContent = "Enviar a impresora";
        dialogoReimpresion.showModal();
        reimpresionIp.focus();
        reimpresionIp.select();
    }

    async function confirmarReimpresion(evento) {
        evento.preventDefault();
        if (reimpresionEnCurso || !registroReimpresion || !solicitudReimpresionGuid) return;

        const ip = reimpresionIp.value.trim();
        if (!ip) {
            reimpresionIp.focus();
            return;
        }

        const id = valor(registroReimpresion, "id", "Id");
        reimpresionEnCurso = true;
        botonReimpresion.disabled = true;
        botonCerrarReimpresion.disabled = true;
        botonCancelarReimpresion.disabled = true;
        reimpresionIp.disabled = true;
        botonReimpresion.textContent = "Enviando…";
        reimpresionEstado.textContent = "Validando el registro histórico y enviando la etiqueta…";
        reimpresionEstado.className = "iny-log-reprint-status";

        try {
            const respuesta = await fetch(
                `/api/Inyeccion/BitacoraImpresiones/${encodeURIComponent(id)}/Reimprimir`,
                {
                    method: "POST",
                    cache: "no-store",
                    headers: {
                        "Accept": "application/json",
                        "Content-Type": "application/json"
                    },
                    body: JSON.stringify({
                        solicitudGuid: solicitudReimpresionGuid,
                        ipImpresora: ip
                    })
                });

            const dato = await respuesta.json().catch(() => null);
            if (!respuesta.ok) {
                // Si el servidor confirmó un log con ERROR, un nuevo clic sí es un
                // intento intencional. Ante una respuesta ambigua se conserva el
                // mismo GUID para que SQL bloquee cualquier duplicado accidental.
                if (dato?.logId) solicitudReimpresionGuid = crearGuidOperacion();
                throw new Error(dato?.message || dato?.mensaje || `Error HTTP ${respuesta.status}`);
            }

            reimpresionEstado.textContent = `${dato?.message || "Etiqueta enviada correctamente."} Registro nuevo: ${dato?.logId}.`;
            reimpresionEstado.className = "iny-log-reprint-status success";
            botonReimpresion.textContent = "Enviada";
            await consultar();
            setTimeout(() => {
                reimpresionEnCurso = false;
                botonCerrarReimpresion.disabled = false;
                botonCancelarReimpresion.disabled = false;
                reimpresionIp.disabled = false;
                if (dialogoReimpresion.open) dialogoReimpresion.close();
            }, 1400);
        } catch (error) {
            console.error("Error reimprimiendo desde la bitácora:", error);
            reimpresionEstado.textContent = error.message || "No fue posible completar la reimpresión.";
            reimpresionEstado.className = "iny-log-reprint-status error";
            reimpresionEnCurso = false;
            botonReimpresion.disabled = false;
            botonCerrarReimpresion.disabled = false;
            botonCancelarReimpresion.disabled = false;
            reimpresionIp.disabled = false;
            botonReimpresion.textContent = "Volver a intentar";
            await consultar();
        }
    }

    async function consultar() {
        if (!desde.value || !hasta.value) {
            resumen.textContent = "Seleccione ambas fechas.";
            return;
        }

        const inicioLocal = new Date(`${desde.value}T00:00:00`);
        const finExclusivoLocal = new Date(`${hasta.value}T00:00:00`);
        finExclusivoLocal.setDate(finExclusivoLocal.getDate() + 1);

        const params = new URLSearchParams({
            desdeUtc: inicioLocal.toISOString(),
            hastaUtc: finExclusivoLocal.toISOString(),
            sku: sku.value.trim(),
            producto: producto.value.trim(),
            estado: estado.value,
            limite: "1000"
        });

        boton.disabled = true;
        boton.textContent = "Consultando…";
        resumen.textContent = "Consultando bitácora…";
        mostrarVacio("Cargando…");

        try {
            const respuesta = await fetch(`/api/Inyeccion/BitacoraImpresiones?${params}`, {
                cache: "no-store",
                headers: { "Accept": "application/json" }
            });

            const datos = await respuesta.json().catch(() => null);
            if (!respuesta.ok) {
                throw new Error(datos?.message || datos?.mensaje || `Error HTTP ${respuesta.status}`);
            }

            const registros = Array.isArray(datos) ? datos : [];
            if (!registros.length) {
                actualizarImpresoras([]);
                mostrarVacio("No hay envíos de impresión con estos filtros.");
                resumen.textContent = "0 registros";
                return;
            }

            renderFilas(registros);
            const enviados = registros.filter(x => String(valor(x, "estado", "Estado")).toUpperCase() === "ENVIADO").length;
            const errores = registros.filter(x => String(valor(x, "estado", "Estado")).toUpperCase() === "ERROR").length;
            resumen.textContent = `${registros.length} registros · ${enviados} enviados · ${errores} con error`;
        } catch (error) {
            console.error("Error consultando bitácora de impresión:", error);
            mostrarVacio(error.message || "No fue posible consultar la bitácora.");
            resumen.textContent = "Consulta fallida";
        } finally {
            boton.disabled = false;
            boton.textContent = "Consultar";
        }
    }

    async function abrirDetalle(id) {
        payloadTitulo.textContent = `Payload ZPL · registro ${id}`;
        payloadMeta.textContent = "Cargando…";
        payloadMensaje.textContent = "";
        payloadContenido.textContent = "";
        dialogo.showModal();

        try {
            const respuesta = await fetch(`/api/Inyeccion/BitacoraImpresiones/${encodeURIComponent(id)}`, {
                cache: "no-store",
                headers: { "Accept": "application/json" }
            });
            const dato = await respuesta.json().catch(() => null);
            if (!respuesta.ok) {
                throw new Error(dato?.message || dato?.mensaje || `Error HTTP ${respuesta.status}`);
            }

            const hash = valor(dato, "payloadSha256", "PayloadSha256") || "";
            const mensaje = valor(dato, "mensaje", "Mensaje") || "Sin mensaje de impresora.";
            payloadMeta.textContent = `${valor(dato, "sku", "SKU")} · ${valor(dato, "producto", "Producto")} · SHA-256 ${hash}`;
            payloadMensaje.textContent = mensaje;
            payloadContenido.textContent = valor(dato, "payloadZpl", "PayloadZpl") || "";
        } catch (error) {
            payloadMeta.textContent = "No disponible";
            payloadMensaje.textContent = error.message || "No fue posible cargar el payload.";
            payloadContenido.textContent = "";
        }
    }

    async function copiarPayload() {
        const texto = payloadContenido.textContent || "";
        if (!texto) return;

        await navigator.clipboard.writeText(texto);
        const botonCopiar = document.getElementById("btnCopiarPayload");
        const anterior = botonCopiar.textContent;
        botonCopiar.textContent = "Copiado";
        setTimeout(() => { botonCopiar.textContent = anterior; }, 1200);
    }

    boton.addEventListener("click", consultar);
    document.getElementById("btnCerrarPayload").addEventListener("click", () => dialogo.close());
    document.getElementById("btnCerrarPayloadFooter").addEventListener("click", () => dialogo.close());
    document.getElementById("btnCopiarPayload").addEventListener("click", copiarPayload);
    botonCerrarReimpresion.addEventListener("click", () => {
        if (!reimpresionEnCurso) dialogoReimpresion.close();
    });
    botonCancelarReimpresion.addEventListener("click", () => {
        if (!reimpresionEnCurso) dialogoReimpresion.close();
    });
    dialogoReimpresion.addEventListener("cancel", evento => {
        if (reimpresionEnCurso) evento.preventDefault();
    });
    formularioReimpresion.addEventListener("submit", confirmarReimpresion);
    [sku, producto].forEach(control => control.addEventListener("keydown", evento => {
        if (evento.key === "Enter") consultar();
    }));

    configurarFechas();
    consultar();
})();
