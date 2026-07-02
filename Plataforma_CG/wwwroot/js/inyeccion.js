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
let ultimaEntradaParaImprimir = null;
let toastImpresionTimeout = null;
let toastTimeout = null;
let pesoTaraActual = 0; // Peso de la tara actual
let taraDescripcion = ""; // Descripción de la tara seleccionada
let pesoBrutoSinTara = 0; // Para almacenar el peso bruto real
let ultimaCapturaPayload = null; // 🆕 Guardar el payload de la última captura para reimpresión
let ultimoPesoValido = "0.00";
let loadingActivo = false;
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

    mostrarLoading("Cargando productos del lote...");

    try {
        aplicarSeleccionLoteDesdeCombo(this);

        await cargarProductosPorPlantilla(plantillaSeleccionada);

        if (intervaloRendimiento) {
            clearInterval(intervaloRendimiento);
        }

        await cargarRendimientoTiempoReal();

    } catch (err) {
        console.error(err);
    } finally {
        ocultarLoading();
    }
});

document.getElementById("loteSelect").addEventListener("click", async function () {
    await cargarLotes(this.value || loteSeleccionado);
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

    mostrarLoading("Cargando productos...");

    try {
        await cargarProductosPorPlantilla(plantillaSeleccionada);
    } catch (error) {
        console.error("❌ Error al refrescar productos:", error);
    }
    finally {
        ocultarLoading();
    }
}

async function cargarImagenProducto(nombre, sku) {
    const resp = await fetch(`/api/Inyeccion/ObtenerImagen?nombre=${encodeURIComponent(nombre)}&sku=${encodeURIComponent(sku)}`);
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

async function cargarProductosPorPlantilla(plantilla) {
    try {
        const resp = await fetch(`/api/Inyeccion/ListarProductos?plan=${plantilla}`);
        if (!resp.ok) throw new Error("Error al cargar productos");

        const productos = await resp.json();

        const cont = document.getElementById("contenedorProductos");
        cont.innerHTML = "";

        for (const p of productos) {
            // Obtener imagen de API
            const imgUrl = await cargarImagenProducto(p.nombre, p.sku);

            cont.innerHTML += `
                <div class="product-card" 
                     data-sku="${p.sku.toLowerCase()}" 
                     data-nombre="${p.nombre.toLowerCase()}"
                     onclick="seleccionarProducto('${p.sku}', '${p.nombre}')">
                    <img src="${imgUrl}"
                         style="width:70px;height:70px;border-radius:6px;object-fit:cover;margin-bottom:8px;">
                    
                    <span style="font-weight:bold;">${p.sku}</span><br>
                    <small>${p.nombre}</small>
                    <br>
                    <b>${p.porcentaje}%</b>
                </div>
            `;
        }

        abrirModal('modalProducto');

        // Limpiar búsqueda al abrir
        document.getElementById('searchProducto').value = '';
        document.getElementById('resultadosBusqueda').textContent = '';

    } catch (err) {
        console.error("❌ Error al cargar productos:", err);
    }
}
async function seleccionarProducto(sku, nombre) {

    mostrarLoading("Cargando receta...");

    try {

        skuSeleccionado = sku;
        document.getElementById("productoSeleccionado").textContent = nombre;
        NombreSeleccionado = nombre;

        cerrarModal('modalProducto');

        await cargarReceta(sku, nombre);

    } catch (err) {
        console.error(err);
    }
    finally {
        ocultarLoading();
    }
}

async function cargarReceta(sku, nombre) {
    try {
        const resp = await fetch(`/api/Inyeccion/ObtenerReceta?sku=${sku}`);
        if (!resp.ok) throw new Error("No se encontró la receta");

        const receta = await resp.json(); // RecetaModel

        // Llenar campo por campo en la vista de receta
        document.getElementById("sku").value = receta.sku;
        document.getElementById("porcentaje").value = receta.porcentaje;
        document.getElementById("velocidad").value = receta.velocidad;
        document.getElementById("producto").value = nombre;
        document.getElementById("modo").value = receta.modoInyeccion;
        document.getElementById("presion").value = receta.presion;
        document.getElementById("altura").value = receta.altura;
        document.getElementById("avance").value = receta.avance;

        console.log("✔ Receta cargada:", receta);
        const ruta = await cargarImagenProducto(nombre, sku);
        mostrarImagenProducto(ruta);

    } catch (err) {
        console.error("❌ Error al cargar receta:", err);
    }
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

    etiquetaPeso.textContent = pesoConvertido.toFixed(3);

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

function abrirConfirmacionCaptura() {
    const comboLote = document.getElementById("loteSelect");
    const sku = document.getElementById("sku")?.value?.trim() || "";
    const producto = NombreSeleccionado || document.getElementById("productoSeleccionado")?.textContent?.trim() || "";
    const peso = obtenerPesoActual();
    const taraActual = taraSeleccionada ? taraSeleccionada.peso : 0;


    if (!comboLote.value || !loteSeleccionado || !plantillaSeleccionada) {
        alert("Debe seleccionar un lote antes de capturar.");
        return;
    }

    if (!sku || producto === "—") {
        alert("Debe seleccionar un producto antes de capturar.");
        return;
    }

    document.getElementById("confirmLote").textContent = nombreLoteGlobal || comboLote.options[comboLote.selectedIndex].textContent;
    document.getElementById("confirmProducto").textContent = producto;
    document.getElementById("confirmSku").textContent = sku;
    document.getElementById("confirmPeso").textContent = `${Number(peso || 0).toFixed(3)} kg`;
    document.getElementById("confirmTara").textContent = `${Number(taraActual || 0).toFixed(3)} kg`;

    abrirModal("modalConfirmarCaptura");

    iniciarPesoVivoConfirmacion();
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
    cerrarModal("modalConfirmarCaptura");
}

async function confirmarCapturaEntrada() {
    const btnConfirmar = document.getElementById("btnConfirmarCaptura");

    btnConfirmar.disabled = true;
    btnConfirmar.innerHTML = "Capturando...";

    try {
        detenerPesoVivoConfirmacion();
        cerrarModal("modalConfirmarCaptura");

        await capturarEntrada();

    } finally {
        btnConfirmar.disabled = false;
        btnConfirmar.innerHTML = "Confirmar captura";
    }
}

async function capturarEntrada() {

    const comboLote = document.getElementById("loteSelect");

    if (!comboLote.value) {
        restaurarUltimoLote();
    }

    if (!comboLote.value || !loteSeleccionado) {
        alert("Debe seleccionar un lote válido antes de capturar");
        return;
    }

    if (!plantillaSeleccionada) {
        alert("No hay una plantilla válida asociada al lote seleccionado");
        return;
    }

    const sku = document.getElementById("sku").value;
    const porcentaje = Number(document.getElementById("porcentaje").value || 0);
    const velocidad = Number(document.getElementById("velocidad").value || 0);
    const modo = Number(document.getElementById("modo").value || 0);
    const presion = Number(document.getElementById("presion").value || 0);
    const altura = Number(document.getElementById("altura").value || 0);
    const avance = document.getElementById("avance").value;
    const taraNum = taraSeleccionada ? taraSeleccionada.peso : 0;
    const pesoActual = obtenerPesoActual();

    const entrada = {
        Id: 0,
        Folio: "",
        SKU: sku,
        fk_Inyectora: 0,
        Porcentaje: porcentaje,
        ModoInyeccion: modo,
        Presion: presion,
        Velocidad: velocidad,
        Altura: altura,
        Avance: avance,
        Bascula: ipBasculaGlobal,
        FechaHora: new Date().toISOString(),
        TipoPeso: modoManualActivo ? "Man" : "Aut",
        Autoriza: usuarioAutorizaId,
        Peso: pesoActual,
        Tara: taraNum,
        fk_Lote: loteSeleccionado,
        Plantilla: plantillaSeleccionada,
        UsSIGO: correo
    };

    console.log("➡ Objeto Capturado", entrada);

    if (entrada.SKU !== "") {
        try {
            const resp = await fetch("/api/Inyeccion/CapturarEntrada", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify(entrada)
            });

            if (!resp.ok) throw new Error("Error al guardar la entrada");

            const resultado = await resp.json();
            console.log("✔ Respuesta backend:", resultado);

            const idGenerado =
                typeof resultado === "object" && resultado !== null
                    ? (resultado.id ?? resultado.Id ?? 0)
                    : resultado;

            entrada.Id = idGenerado;

            /*
                El folio NO viene en la respuesta de CapturarEntrada.
                Lo consultamos igual que en Detallado usando /api/Inyeccion/ConsultarEntrada.
            */
            const folioReal = await obtenerFolioEntrada(entrada.Id);
            entrada.Folio = folioReal || "";

            console.log("✔ Entrada registrada", {
                id: entrada.Id,
                folio: entrada.Folio
            });

            ultimaCapturaPayload = {
                ...entrada,
                Producto: NombreSeleccionado
            };

            console.log(" Payload guardado:", ultimaCapturaPayload);
            console.log(" Producto guardado:", ultimaCapturaPayload.Producto);

            mostrarToastEntrada(entrada.Id);
            await cargarRendimientoTiempoReal();

            try {
                await imprimirEtiquetaSalida(ultimaCapturaPayload);
                console.log(" Primera impresión exitosa");
            } catch (errImpr) {
                console.warn(" La entrada se guardó pero falló la impresión", errImpr);
            }

        } catch (err) {
            console.error("❌ Error capturando entrada", err);
            alert("Ocurrió un error al registrar la entrada");
        }
    } else {
        alert("Debe asegurarse de seleccionar producto");
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
    if (!ipImpresoraGlobal) {
        alert("Debe configurar la impresora antes de imprimir.");
        return;
    }
    if (!nombreLoteGlobal) {
        console.warn("⚠ No existe nombre de lote para impresión");
    }
    if (!entradaObj || !entradaObj.SKU) {
        console.warn("⚠ Entrada inválida. No se puede imprimir.");
        return;
    }

    console.log("🖨️ Enviando impresión a IP:", ipImpresoraGlobal);

    if (!entradaObj.Folio) {
        console.warn("⚠ La entrada no trae Folio para imprimir:", entradaObj);
    }

    const productoParaImprimir =
        entradaObj.Producto ||
        ultimaCapturaPayload?.Producto ||
        NombreSeleccionado ||
        "";

    const url = `/api/Inyeccion/Imprimir` +
        `?ip=${encodeURIComponent(ipImpresoraGlobal)}` +
        `&lote=${encodeURIComponent(nombreLoteGlobal || "")}` +
        `&prod=${encodeURIComponent(productoParaImprimir)}`;

    try {
        // Crear un timeout de 15 segundos
        const controller = new AbortController();
        const timeoutId = setTimeout(() => controller.abort(), 15000);

        const resp = await fetch(url, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(entradaObj),
            signal: controller.signal
        });

        clearTimeout(timeoutId);

        console.log("📡 Respuesta recibida - Status:", resp.status);

        // Verificar si la respuesta es exitosa
        if (!resp.ok) {
            console.error("❌ Error HTTP al imprimir:", resp.status, resp.statusText);
            mostrarToastImpresionError(entradaObj);
            throw new Error(`Error HTTP: ${resp.status} - ${resp.statusText}`);
        }

        // Leer la respuesta del servidor
        const resultado = await resp.json();
        console.log("📄 Respuesta del servidor:", resultado);

        // Verificar si el backend reporta éxito
        if (resultado.success === false) {
            console.error("❌ El servidor reportó error:", resultado.message);
            mostrarToastImpresionError(entradaObj);
            throw new Error(resultado.message || "Error en la impresión");
        }

        console.log("✔ Impresión enviada correctamente");
        return resultado;

    } catch (err) {
        if (err.name === 'AbortError') {
            console.error("❌ Timeout: La impresora no respondió en 15 segundos");
            mostrarToastImpresionError(entradaObj);
            throw new Error("Timeout: La impresora no respondió");
        }

        console.error("❌ Error al imprimir:", err.message);
        mostrarToastImpresionError(entradaObj);
        throw err;
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

    const ini = repFechaInicio.value;
    const fin = repFechaFin.value;
    const tbody = document.getElementById("tbodyRendimientoModal");

    tbody.innerHTML = `<tr><td colspan="8">Cargando...</td></tr>`;

    const resp = await fetch(`/api/Reportes/RendimientoFecha?fechain=${ini}&fechafin=${fin}`);
    const data = await resp.json();

    tbody.innerHTML = "";

    data.forEach(item => {

        const esperado = Number(item["Porcentaje Esperado"]);
        const rendimiento = Number(item.Rendimiento);
        const tolerancia = 2;

        let claseFila = "";

        if (rendimiento <= esperado - tolerancia) {
            claseFila = "fila-roja";
        }
        else if (rendimiento >= esperado + tolerancia) {
            claseFila = "fila-amarilla";
        }
        // si no entra en ninguna → queda normal

        tbody.innerHTML += `
        <tr class="${claseFila}">
            <td data-label="Fecha Produccion">${new Date(item.FechaProduccion).toLocaleDateString("es-MX")}</td>
            <td data-label="Lote">${item.Lote}</td>
            <td data-label="SKU">${item.SKU}</td>
            <td data-label="Peso Entrada">${item["Peso Entrada"].toFixed(2)}</td>
            <td data-label="Peso Salida">${item["Peso Salida"].toFixed(2)}</td>
            <td data-label="Esperado %">${esperado}%</td>
            <td data-label="Variacion">${item.Variacion.toFixed(2)}</td>
            <td data-label="Rendimiento %">${rendimiento.toFixed(2)}%</td>
        </tr>
    `;
    });

}

async function obtenerFolioEntrada(id) {
    try {
        if (!id) return "";

        const resp = await fetch(`/api/Inyeccion/ConsultarEntrada?id=${encodeURIComponent(id)}`);
        if (!resp.ok) return "";

        const entrada = await resp.json();

        return entrada?.folio ?? entrada?.Folio ?? "";
    } catch (err) {
        console.warn("⚠️ No se pudo obtener folio para id:", id, err);
        return "";
    }
}

async function cargarReporteDetallado() {

    const ini = repFechaInicio.value;
    const fin = repFechaFin.value;
    const tbody = document.getElementById("tbodyDetallado");

    tbody.innerHTML = `<tr><td colspan="11">Cargando...</td></tr>`;

    try {
        const resp = await fetch(`/api/Reportes/Detallado?fechain=${ini}&fechafin=${fin}`);
        if (!resp.ok) throw new Error("Error API detallado");

        const data = await resp.json();

        console.log("📊 Data detallado:", data);

        tbody.innerHTML = "";

        for (const item of data) {
            console.log("Fila detallado:", item);

            // Intentar tomar folio directo del API primero
            let folio =
                item.Folio ??
                item.folio ??
                item.FOLIO ??
                "";

            // Si no viene, intentamos obtenerlo con la referencia/id
            if (!folio) {
                const idEntrada =
                    item.Referencia ??
                    item.referencia ??
                    item.Id ??
                    item.id ??
                    0;

                folio = await obtenerFolioEntrada(idEntrada);
            }

            const peso = Number(item.Peso ?? 0);
            const tara = Number(item.Tara ?? 0);

            tbody.innerHTML += `
                <tr>
                    <td data-label="Referencia">${item.Referencia ?? item.referencia ?? ""}</td>
                    <td data-label="Folio">${folio}</td>
                    <td data-label="Fecha / Hora">${item.FechaHora ?? item.fechaHora ?? ""}</td>
                    <td data-label="Lote">${item.Lote ?? item.lote ?? ""}</td>
                    <td data-label="SKU">${item.SKU ?? item.sku ?? ""}</td>
                    <td data-label="Producto">${item.Producto ?? item.producto ?? ""}</td>
                    <td data-label="Peso">${peso.toFixed(2)}</td>
                    <td data-label="Tara">${tara.toFixed(2)}</td>
                    <td data-label="Modo">${item.TipoPeso ?? item.tipoPeso ?? ""}</td>
                    <td data-label="Usuario">${item.InyUsuario ?? item.inyUsuario ?? ""}</td>
                    <td data-label="Autorización">${item.Autorizacion ?? item.autorizacion ?? ""}</td>
                </tr>
            `;
        }

        if (!data.length) {
            tbody.innerHTML = `<tr><td colspan="11">Sin registros</td></tr>`;
        }

    } catch (err) {
        console.error("❌ Error cargando reporte detallado:", err);
        tbody.innerHTML = `<tr><td colspan="11">Error consultando reporte detallado</td></tr>`;
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


async function cargarDetalladoTiempoRealHoy() {

    const tbody = document.getElementById("tbodyDetalladoTiempoReal");

    if (!tbody) {
        console.warn("No existe tbodyDetalladoTiempoReal en la vista");
        return;
    }

    const hoy = new Date().toISOString().split("T")[0];

    tbody.innerHTML = `<tr><td colspan="10">Cargando últimas capturas...</td></tr>`;

    try {
        const resp = await fetch(`/api/Reportes/Detallado?fechain=${hoy}&fechafin=${hoy}`);

        if (!resp.ok) {
            throw new Error("Error API detallado");
        }

        let data = await resp.json();

        

        if (!Array.isArray(data)) {
            data = [data];
        }


        data.sort((a, b) => {
            const refA = numSeguro(a.Referencia ?? a.referencia ?? a.Id ?? a.id);
            const refB = numSeguro(b.Referencia ?? b.referencia ?? b.Id ?? b.id);

            return refB - refA;
        });

        data = data.slice(0, 50);

        tbody.innerHTML = "";

        if (!data.length) {
            tbody.innerHTML = `<tr><td colspan="10">Sin capturas registradas hoy</td></tr>`;
            return;
        }

        data.forEach(item => {

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

            tbody.innerHTML += `
                <tr>
                    <td data-label="Referencia">${item.Referencia ?? item.referencia ?? item.Id ?? item.id ?? ""}</td>
                    <td data-label="Fecha / Hora">${fechaTexto}</td>
                    <td data-label="Lote">${item.Lote ?? item.lote ?? ""}</td>
                    <td data-label="SKU">${item.SKU ?? item.sku ?? ""}</td>
                    <td data-label="Producto">${item.Producto ?? item.producto ?? ""}</td>
                    <td data-label="Peso">${peso.toFixed(2)}</td>
                    <td data-label="Tara">${tara.toFixed(2)}</td>
                    <td data-label="Modo">${item.TipoPeso ?? item.tipoPeso ?? ""}</td>
                    <td data-label="Usuario">${item.InyUsuario ?? item.inyUsuario ?? ""}</td>
                    <td data-label="Autorización">${item.Autorizacion ?? item.autorizacion ?? ""}</td>
                </tr>
            `;
        });

    } catch (err) {
        console.error("❌ Error cargando detallado tiempo real ligero:", err);
        tbody.innerHTML = `<tr><td colspan="10">Error consultando últimas capturas</td></tr>`;
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


function filtrarProductos() {
    const searchTerm = document.getElementById('searchProducto').value.toLowerCase().trim();
    const cards = document.querySelectorAll('.product-card');
    const resultadosDiv = document.getElementById('resultadosBusqueda');
    const sinResultados = document.getElementById('sinResultados');

    let contador = 0;

    cards.forEach(card => {
        const sku = card.dataset.sku || '';
        const nombre = card.dataset.nombre || '';

        // Buscar coincidencias
        const coincide = sku.includes(searchTerm) || nombre.includes(searchTerm);

        if (coincide || searchTerm === '') {
            card.style.display = '';
            contador++;
        } else {
            card.style.display = 'none';
        }
    });

    // Actualizar mensaje de resultados
    if (searchTerm === '') {
        resultadosDiv.textContent = '';
        if (sinResultados) sinResultados.style.display = 'none';
    } else if (contador === 0) {
        resultadosDiv.textContent = '❌ No se encontraron productos';
        resultadosDiv.style.color = '#dc3545';
        if (sinResultados) sinResultados.style.display = 'block';
    } else {
        resultadosDiv.textContent = `✅ ${contador} producto${contador !== 1 ? 's' : ''} encontrado${contador !== 1 ? 's' : ''}`;
        resultadosDiv.style.color = '#28a745';
        if (sinResultados) sinResultados.style.display = 'none';
    }
}

function mostrarToastImpresionError(entradaObj) {
    const toast = document.getElementById("toastImpresionError");

    // Guardamos la última entrada para reintento
    ultimaEntradaParaImprimir = entradaObj;

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
    🏷️  SKU: ${ultimaCapturaPayload.SKU}`))
    {
        try {
            await imprimirEtiquetaSalida(ultimaCapturaPayload);
            alert('✅ Reimpresión exitosa');
        } catch (error) {
            console.error('❌ Error:', error);
        }
    }
}

function mostrarLoading(texto = "Cargando información...") {
    if (loadingActivo) return; // evita múltiples activaciones

    loadingActivo = true;

    const overlay = document.getElementById("loadingOverlay");
    overlay.style.display = "flex";

    document.querySelector(".loading-text").textContent = texto;

    // Bloquea clicks
    document.body.style.pointerEvents = "none";
    overlay.style.pointerEvents = "all";
}

function ocultarLoading() {
    loadingActivo = false;

    const overlay = document.getElementById("loadingOverlay");
    overlay.style.display = "none";

    document.body.style.pointerEvents = "auto";
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
