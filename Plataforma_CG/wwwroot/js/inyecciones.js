// =====================
// CONFIGURACIÓN Y CONSTANTES
// =====================

const imagenesProductos = {
    "ARRACHERA": "/images/arrachera.png",
    "PALETA": "/images/Paleta.jpeg",
    "CHULETON": "/images/Chuleton.png",
    "CHULETÓN": "/images/Chuleton.png",
    "PECHO": "/images/Pecho.png",
    "PESCUEZO": "/images/Pescuezo.jpg",
    "PLATANILLO": "/images/Platanillo.png",
    "RECORTE": "/images/Recorte-80-20.png",
    "AGUJA REGIA": "/images/Aguja Regia.jpeg",
    "BRISKET": "/images/Brisket.png",
    "CHAMBERETE": "/images/Chamberete.png",
    "LOMO": "/images/Lomo.png",
    "CLOD": "/images/Clod.jpeg",
    "COSTILLA CARGADA": "/images/Costilla Cargada.jpg",
    "COSTILLA": "/images/Costilla.jpeg",
    "CUÑA": "/images/Cuña.jpeg",
    "DESHEBRADA": "/images/Deshebrada.png",
    "DIEZMILLO": "/images/Diezmillo.jpg",
    "NEW YORK": "/images/new york.png",
    "PULPA BLANCA": "/images/Pulpa Blanca.jpeg",
    "PULPA SELECTA BLANCA": "/images/Pulpa Blanca.jpeg",
    "PULPA SELECTA BOLA": "/images/Pulpa Bola.jpg",
    "PULPA BOLA": "/images/Pulpa Bola.jpg",
    "PULPA NEGRA": "/images/Pulpa Negra.jpeg",
    "PULPA SELECTA NEGRA": "/images/Pulpa Negra.jpeg",
    "RIB EYE": "/images/ribeye.jpg",
    "SHORT RIB": "/images/short rib.jpg",
    "RIB": "/images/Rib con Grasa.jpeg",
    "SIRLOIN": "/images/Sirloin.jpeg",
    "T BONE": "/images/tbone.jpg",
    "T-BONE": "/images/tbone.jpg",
    "TOMAHAWK": "/images/tomahawk.png"
};

// =====================
// VARIABLES GLOBALES
// =====================

let productosGlobal = [];
let timer = null;
let modalDestino = null;
let loteSelect, productoSeleccionado, programacionActual, overlay;
let pesoActual, porcentajeActual, velocidadActual;
let fields = {};
let loteIdGlobal = null; // Nueva variable global para almacenar el loteId
let ipImpresoraGlobal = "";
let modoManual = false;
let serverPassword = null; // Contraseña que llega del backend
let pesoTaraActual = 0; // Peso de la tara actual
let taraDescripcion = ""; // Descripción de la tara seleccionada
let pesoBrutoSinTara = 0; // Para almacenar el peso bruto real

// Claves para localStorage
const STORAGE_KEYS = {
    IMPRESORA_IP: 'inyecciones_impresora_ip',
    BASCULA_CONFIG: 'inyecciones_bascula_config'
};


// =====================
// UTILIDADES Y HELPERS
// =====================

const $ = (id) => {
    const element = document.getElementById(id);
    if (!element) {
        console.warn(`⚠️ Elemento con ID '${id}' no encontrado`);
    }
    return element;
};

const showError = (message, duration = 5000) => {
    console.error('❌', message);
    // Opcional: mostrar en UI
    const errorDiv = document.createElement('div');
    errorDiv.className = 'error-toast';
    errorDiv.textContent = message;
    errorDiv.style.cssText = `
        position: fixed; top: 20px; right: 20px; z-index: 10000;
        background: #ff4757; color: white; padding: 12px 20px;
        border-radius: 6px; box-shadow: 0 4px 12px rgba(0,0,0,0.3);
    `;
    document.body.appendChild(errorDiv);
    setTimeout(() => errorDiv.remove(), duration);
};

const showSuccess = (message, duration = 3000) => {
    console.log('✅', message);
    const successDiv = document.createElement('div');
    successDiv.className = 'success-toast';
    successDiv.textContent = message;
    successDiv.style.cssText = `
        position: fixed; top: 20px; right: 20px; z-index: 10000;
        background: #2ed573; color: white; padding: 12px 20px;
        border-radius: 6px; box-shadow: 0 4px 12px rgba(0,0,0,0.3);
    `;
    document.body.appendChild(successDiv);
    setTimeout(() => successDiv.remove(), duration);
};

// =====================
// API CALLS - MEJORADAS
// =====================
async function obtenerReceta(sku) {
    try {
        if (!sku || sku.trim() === '') {
            console.warn('⚠️ SKU vacío proporcionado a obtenerReceta');
            return null;
        }

        console.log(`🔄 Consultando receta para SKU ${sku}...`);

        // ✅ RUTA CORREGIDA: Coincide con el controller
        const response = await fetch(`/api/Recetas/ConsultarReceta?sku=${encodeURIComponent(sku)}`, {
            method: "GET",
            headers: {
                "Accept": "application/json",
                "Content-Type": "application/json"
            }
        });

        if (!response.ok) {
            if (response.status === 404) {
                console.warn(`⚠️ No se encontró receta para SKU ${sku}`);
                return null;
            }

            let errorMessage = `HTTP ${response.status}: ${response.statusText}`;
            try {
                const errorData = await response.json();
                errorMessage = errorData.error || errorData.message || errorMessage;
            } catch {
                // Si no se puede parsear, usar mensaje por defecto
            }

            throw new Error(errorMessage);
        }

        const receta = await response.json();
        console.log("✅ Receta obtenida para SKU:", sku);
        return receta;

    } catch (error) {
        console.error(`❌ Error al obtener receta para SKU ${sku}:`, error);
        showError(`Error al cargar receta: ${error.message}`);
        return null;
    }
}

async function obtenerRecetas() {
    try {
        console.log('🔄 Cargando todas las recetas...');

        // ✅ RUTA CORREGIDA: Coincide con el controller  
        const response = await fetch("/api/Recetas/Listar", {
            method: "GET",
            headers: {
                "Accept": "application/json",
                "Content-Type": "application/json"
            }
        });

        if (!response.ok) {
            if (response.status === 404) {
                console.warn("⚠️ Endpoint de recetas no encontrado");
                return [];
            }

            let errorMessage = `HTTP ${response.status}: ${response.statusText}`;
            try {
                const errorData = await response.json();
                errorMessage = errorData.error || errorData.message || errorMessage;
            } catch {
                // Si no se puede parsear, usar mensaje por defecto
            }

            throw new Error(errorMessage);
        }

        const recetas = await response.json();
        console.log("✅ Recetas cargadas:", Array.isArray(recetas) ? recetas.length : 'N/A');

        return Array.isArray(recetas) ? recetas : [];

    } catch (error) {
        console.error("❌ Error al cargar recetas:", error);
        showError(`Error al cargar recetas: ${error.message}`);
        return [];
    }
}

async function obtenerProductos(planId) {
    try {
        // Validar entrada
        if (!planId || planId === 0) {
            console.warn("⚠️ PlanId inválido:", planId);
            return [];
        }

        console.log(`🔄 Obteniendo productos para plan ${planId}...`);

        // ✅ RUTA CORREGIDA: Coincide con el controller
        const resp = await fetch(`/api/Recetas/productos/${planId}`, {
            method: 'GET',
            headers: {
                'Accept': 'application/json',
                'Content-Type': 'application/json'
            }
        });

        if (!resp.ok) {
            // Manejo específico de errores
            if (resp.status === 404) {
                console.warn(`⚠️ No se encontraron productos para el plan ${planId}`);
                return [];
            }

            // Intentar obtener mensaje de error del servidor
            let errorMessage = `HTTP ${resp.status}: ${resp.statusText}`;
            try {
                const errorData = await resp.json();
                errorMessage = errorData.error || errorData.message || errorMessage;
            } catch {
                // Si no se puede parsear el error, usar el mensaje por defecto
            }

            throw new Error(errorMessage);
        }

        const productos = await resp.json();
        console.log(`✅ ${Array.isArray(productos) ? productos.length : 'N/A'} productos obtenidos para plan ${planId}`);

        // Asegurar que siempre devolvemos un array
        return Array.isArray(productos) ? productos : [];

    } catch (err) {
        console.error("❌ Error cargando productos:", err);
        showError(`Error al cargar productos: ${err.message}`);
        return [];
    }
}

async function probarConectividad(planId = 1) {
    try {
        console.log('🧪 Probando conectividad...');

        const response = await fetch(`/api/Recetas/test/${planId}`, {
            method: 'GET',
            headers: {
                'Accept': 'application/json'
            }
        });

        if (!response.ok) {
            throw new Error(`Test falló: ${response.status}`);
        }

        const result = await response.json();
        console.log('✅ Conectividad OK:', result);
        showSuccess('Conexión con el servidor establecida');

        return true;
    } catch (error) {
        console.error('❌ Test de conectividad falló:', error);
        showError(`Error de conectividad: ${error.message}`);
        return false;
    }
}


// =====================
// LÓGICA DE NEGOCIO
// =====================
function obtenerImagenProducto(nombre, sku) {
    if (!nombre) return "/images/default.png";

    // Busca por palabra clave en el nombre
    const nombreUpper = nombre.toUpperCase();
    for (const [clave, imagen] of Object.entries(imagenesProductos)) {
        if (nombreUpper.includes(clave)) {
            return imagen;
        }
    }

    // Fallback por SKU o imagen default
    return `/images/${sku || 'default'}.png`;
}

async function obtenerProductosCompletos() {
    try {
        const [recetas, productos] = await Promise.all([
            obtenerRecetas(),
            obtenerProductos()
        ]);

        const productosCompletos = recetas.map(receta => {
            // Buscar producto correspondiente por SKU
            const producto = productos.find(p => p.sku === receta.sku || p.SKU === receta.sku);
            const nombre = producto?.nombre || producto?.Nombre || "Producto Desconocido";

            return {
                // IDs y referencias
                id: receta.id || receta.Id,
                sku: receta.sku || receta.SKU,

                // Info del producto
                nombre: nombre,
                imagen: obtenerImagenProducto(nombre, receta.sku || receta.SKU),

                // Parámetros de proceso
                porcentaje: producto?.porcentaje || receta.porcentaje || receta.Porcentaje || 0,
                velocidad: receta.velocidad || receta.Velocidad || 0,
                modo: receta.modoInyeccion || receta.ModoInyeccion || 0,
                presion: receta.presion || receta.Presion || 0,
                altura: receta.altura || receta.Altura || 0,
                avance: receta.avance || receta.Avance || "",
                tara: receta.tara || receta.Tara || 0
            };
        });

        console.log('✅ Productos completos procesados:', productosCompletos.length);
        return productosCompletos;

    } catch (error) {
        showError(`Error al procesar productos: ${error.message}`);
        return [];
    }
}

// =====================
// INICIALIZACIÓN DEL DOM
// =====================
function initializeDOMElements() {
    // Elementos principales
    loteSelect = $("loteSelect");
    productoSeleccionado = $("productoSeleccionado");
    programacionActual = $("programacionActual");
    overlay = $("overlay");

    // Displays de estado
    pesoActual = $("pesoActual");
    porcentajeActual = $("porcentajeActual");
    velocidadActual = $("velocidadActual");

    // Campos del formulario
    fields = {
        sku: $("sku"),
        porcentaje: $("porcentaje"),
        velocidad: $("velocidad"),
        producto: $("producto"),
        modo: $("modo"),
        presion: $("presion"),
        altura: $("altura"),
        avance: $("avance"),
        tara: $("tara")
    };

    // Validación de elementos críticos
    if (!loteSelect) {
        console.error('❌ loteSelect no encontrado - la aplicación no funcionará correctamente');
        return false;
    }

    console.log('✅ Elementos DOM inicializados');
    return true;
}

// =====================
// GESTIÓN DE LOTES
// =====================
function initLotes() {
    if (!loteSelect) {
        console.error("❌ loteSelect no disponible");
        return;
    }

    enableSelect(loteSelect, Object.keys(lotesConfig));
    console.log('✅ Lotes inicializados');
}

function enableSelect(selectEl, items) {
    if (!selectEl) {
        console.warn("⚠️ selectEl no definido en enableSelect");
        return;
    }

    selectEl.innerHTML = items.map(value =>
        `<option value="${value}">${value}</option>`
    ).join('');

    selectEl.disabled = items.length === 0;

    if (items.length === 0) {
        console.warn('⚠️ No hay items para el select');
    }
}

// =====================
// CAPTURA DE PRODUCTOS
// =====================
async function capturarProducto(e) {

    let pesoFinal;

    e.preventDefault();

    // Función helper para convertir a string
    const toString = (value) => {
        if (value === null || value === undefined) return "";
        return String(value);
    };

    // Función helper para convertir a número
    const toNumber = (value) => {
        if (value === null || value === undefined) return null;
        const num = Number(value);
        return isNaN(num) ? null : num;
    };

    // Función helper para obtener texto de elemento
    const getElementText = (id) => {
        const el = document.getElementById(id);
        return el ? el.innerText.trim() : "";
    };

    // Función helper para obtener valor de input
    const getInputValue = (id) => {
        const el = document.getElementById(id);
        return el ? el.value.trim() : "";
    };

    if (modoManual) {
        const inputPeso = document.getElementById("inputPesoManual");
        pesoFinal = inputPeso && inputPeso.value.trim() !== "" ? inputPeso.value.trim() : "0.00";
    } else {
        pesoFinal = document.getElementById('pesoActual')?.innerText.trim() || "0.00";
    }

    // Recolecta valores del DOM con tipos apropiados
    const payload = {
        // Campos de texto (strings)
        lote: toString(getInputValue('loteSelect')),
        producto: toString(getInputValue('producto')),
        productoSeleccionado: toString(getElementText('productoSeleccionado')),
        programacion: toString(getElementText('programacionActual')),
        sku: toString(getInputValue('sku')),
        avance: toString(getInputValue('avance')),
        ipBascula: toString(getElementText('metaBascula')),
        comandoBascula: toString(getInputValue('comandoBascula') || ""),
        ipImpresora: toString(getElementText('metaImpresora')),
        usuarioCorreo: toString(""), // Agregar si tienes este campo

        // Campos numéricos (números o null)
        loteId: toString(loteIdGlobal),
        porcentaje: toString(getInputValue('porcentaje')),
        velocidad: toString(getInputValue('velocidad')),
        modo: toString(getInputValue('modo')),
        presion: toString(getInputValue('presion')),
        altura: toString(getInputValue('altura')),
        tara: toString(getInputValue('tara')),
        pesoActual: toString(pesoFinal),
        porcentajeActual: toString(getElementText('porcentajeActual')),
        velocidadActual: toString(getElementText('velocidadActual')),
        modoCaptura: modoManual ? "Manual" : "Automático"
    };

    console.log('📤 Payload a enviar:', payload);

    // Validación mejorada
    if (!payload.productoSeleccionado || payload.productoSeleccionado === '—' || payload.productoSeleccionado === '') {
        alert('Selecciona un producto antes de capturar.');
        return;
    }

    if (!payload.lote || payload.lote === 'Seleccionar' || payload.lote === '') {
        alert('Selecciona un lote antes de capturar.');
        return;
    }

    if (!payload.loteId || payload.loteId === null) {
        alert('No se pudo obtener el ID del lote. Intenta seleccionar el lote nuevamente.');
        return;
    }

    // Mostrar estado
    const btn = document.getElementById('btnStart');
    if (!btn) {
        console.error('❌ Botón btnStart no encontrado');
        return;
    }

    const prevText = btn.innerText;
    btn.disabled = true;
    btn.innerText = 'Guardando...';

    try {
        console.log('🔄 Enviando captura al servidor...');

        const res = await fetch('/api/Capturas', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(payload)
        });

        if (!res.ok) {
            const errorText = await res.text();
            let errorObj;

            try {
                errorObj = JSON.parse(errorText);
            } catch {
                errorObj = { error: 'Error desconocido', message: errorText };
            }

            console.error('❌ Error del servidor:', errorObj);
            showError(`Error al guardar: ${errorObj.error || errorObj.message || 'Error desconocido'}`);
            return;
        }

        const data = await res.json();
        console.log('✅ Captura guardada exitosamente:', data);

        showSuccess(`Captura guardada correctamente (ID: ${data.id})`);

        await cargarReporteHoy();

        imprimirEtiqueta(payload);

    } catch (error) {
        console.error('❌ Error de conexión:', error);
        showError('No se pudo conectar con el servidor. Revisa tu conexión.');
    } finally {
        btn.disabled = false;
        btn.innerText = prevText;
    }
}

async function guardarCaptura() {

    if (LoteId !== "" && sku !== "" && pesoActual !== "" && pesoActual !== "Error") {

        let peso = parseFloat(pesoActual);

        // Si el usuario está en modo manual, entonces restar la tara
        if (modoCaptura === "manual") {
            peso = peso - taraSeleccionada;
        }

        const model = {
            usIny: usuarioId,
            sku: sku,
            fk_Inyectora: inyectoraId,
            porcentaje: porcentajeSeleccionado,
            modoInyeccion: modoSeleccionado,
            presion: presionActual,
            velocidad: velocidadActual,
            avance: avanceActual,
            bascula: basculaSeleccionada,
            tipoPeso: tipoPeso,
            altura: alturaActual,
            fechaHora: new Date().toISOString(),
            autoriza: autorizacion ?? 0,
            peso: peso,
            fk_Lote: parseInt(LoteId),
            tara: taraSeleccionada,
            plantilla: plantillaSeleccionada
        };
/*
        try {
            const response = await fetch('/api/Inyecciones/Ingresar', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(model)
            });

            const entry = await response.text();

            if (entry !== "") {

                imprimirEtiqueta(model);

                if (confirm(`Se generó la referencia "${entry}"
Peso: ${peso}
Producto: ${sku}
¿Desea reimprimir?`)) {
                    imprimirEtiqueta(model);
                }

                limpiarCampos();
                cargarUltimosRegistros();
            } else {
                alert("Fallo al guardar");
            }
        }
        catch (err) {
            console.error("Error al guardar:", err);
            alert("Error al guardar");
        }*/

    } else {
        alert("Compruebe los datos");
    }
}


// =====================
// EVENTOS
// =====================
function setupEventListeners() {
    // Cambio de lote
    if (loteSelect) {
        loteSelect.addEventListener('change', handleLoteChange);
    }

    // Botón de captura
    const btnStart = document.getElementById('btnStart');
    if (btnStart) {
        //btnStart.addEventListener('click', capturarProducto);
        btnStart.addEventListener('click', guardarCaptura);
    } else {
        console.warn('⚠️ Botón btnStart no encontrado');
    }

    // Overlay para cerrar modales
    if (overlay) {
        overlay.addEventListener('click', handleOverlayClick);
    }

    // Event listener para el modal de Tara
    const seleccionarTara = document.getElementById("SeleccionarTara");
    if (seleccionarTara) {
        seleccionarTara.addEventListener("click", () => {
            abrirModal("modalTara");
            cargarTaras();
        });
    }

    const btnToggle = document.getElementById('btnTogglePeso');
    if (btnToggle) {
        btnToggle.addEventListener('click', solicitarCambioModo);
        console.log('✅ Event listener de modo configurado');
    }

    console.log('✅ Event listeners configurados');
}

function handleOverlayClick() {
    document.querySelectorAll('.modal').forEach(modal => {
        modal.style.display = 'none';
    });
    if (overlay) overlay.style.display = 'none';
}

// =====================
// GESTIÓN DE DATOS
// =====================
function limpiarCampos() {
    Object.values(fields).forEach(field => {
        if (field && field.id !== 'tara') { // ✅ NO limpiar el campo tara
            field.value = '';
        }
    });

    if (pesoActual) pesoActual.textContent = '0.00';
    if (porcentajeActual) porcentajeActual.textContent = '—';
    if (velocidadActual) velocidadActual.textContent = '—';
    if (productoSeleccionado) productoSeleccionado.textContent = '—';

    // ✅ NO resetear la tara - se mantiene activa
    console.log('🧹 Campos limpiados (Tara se mantiene activa)');
    if (pesoTaraActual > 0) {
        console.log(`⚖️ Tara activa: ${pesoTaraActual} kg`);
    }
}

function setDatosProceso(producto) {
    if (!producto) {
        console.warn('⚠️ Producto no definido en setDatosProceso');
        return;
    }

    console.log("🔧 Actualizando datos de proceso:", producto);

    // Actualizar campos del formulario
    const campos = {
        sku: producto.sku || '',
        porcentaje: producto.porcentaje || '',
        velocidad: producto.velocidad || '',
        producto: producto.nombre || '',
        modo: producto.modo || '',
        presion: producto.presion || '',
        altura: producto.altura || '',
        avance: producto.avance || '',
        
    };

    Object.entries(campos).forEach(([key, value]) => {
        const field = document.getElementById(key);
        if (field) {
            field.value = value;
        } else {
            console.warn(`⚠️ Campo ${key} no encontrado en el DOM`);
        }
    });

    // Actualizar displays de KPIs
    if (porcentajeActual) porcentajeActual.textContent = producto.porcentaje || '—';
    if (velocidadActual) velocidadActual.textContent = producto.velocidad || '—';

    console.log('✅ Datos del proceso actualizados para:', producto.nombre);
    if (pesoTaraActual > 0) {
        console.log(`⚖️ Tara se mantiene: ${taraDescripcion} (${pesoTaraActual} kg)`);
    }
}

function setStatus(online) {
    const kpiPeso = $("kpiPeso");
    if (kpiPeso) {
        kpiPeso.className = online ? "kpi ok" : "kpi";
    }

    // Opcional: actualizar otros indicadores visuales
    const statusElements = document.querySelectorAll('.status-indicator');
    statusElements.forEach(el => {
        el.classList.toggle('online', online);
        el.classList.toggle('offline', !online);
    });
}

// =====================
// SIMULACIÓN DE LECTURAS (Fallback)
// =====================
function simularLecturas(force = false) {
    // Limpiar timer anterior
    if (timer) {
        clearInterval(timer);
        timer = null;
    }

    // No iniciar simulación si está en modo manual
    if (modoManual) {
        console.log('⸏ Simulación pausada - Modo Manual activo');
        return;
    }

    console.log('🔄 Iniciando simulación de peso (modo fallback)');
    if (pesoTaraActual > 0) {
        console.log(`⚖️ Tara activa: ${pesoTaraActual} kg - Restando automáticamente`);
    }

    // Iniciar simulación continua
    timer = setInterval(() => {
        const pesoBruto = (Math.random() * 300 + 1);
        pesoBrutoSinTara = pesoBruto; // Guardar peso bruto
        const pesoNeto = Math.max(0, pesoBruto - pesoTaraActual);
        if (pesoActual) {
            pesoActual.textContent = pesoNeto.toFixed(2);
        }
    }, 2000);

    // Si es forzado, actualizar inmediatamente
    if (force) {
        const pesoBruto = (Math.random() * 300 + 1);
        pesoBrutoSinTara = pesoBruto;
        const pesoNeto = Math.max(0, pesoBruto - pesoTaraActual);
        if (pesoActual) {
            pesoActual.textContent = pesoNeto.toFixed(2);
        }
    }

    setStatus(true);
}

function solicitarCambioModo() {
    if (modoManual) {
        // Está en manual, quiere volver a automático (sin contraseña)
        alternarModoEntrada();
    } else {
        // Está en automático, quiere pasar a manual (CON contraseña)
        abrirModalContra("modo");
    }
}

function alternarModoEntrada() {
    modoManual = !modoManual;

    const btnToggle = document.getElementById('btnTogglePeso');
    const inputPeso = document.getElementById('inputPesoManual');
    const kpiPeso = document.getElementById('kpiPeso');

    if (!btnToggle || !inputPeso || !pesoActual) {
        console.error('❌ Elementos de peso no encontrados');
        return;
    }

    if (modoManual) {
        // ========== ACTIVAR MODO MANUAL ==========
        clearInterval(timer);
        timer = null;

        // Obtener el peso actual (ya con tara restada)
        const pesoActualValor = pesoActual.textContent || '0.00';
        inputPeso.value = pesoActualValor;

        pesoActual.style.display = 'none';
        inputPeso.style.display = 'block';
        inputPeso.style.fontSize = '75px';
        inputPeso.style.border = 'none';
        inputPeso.style.background = 'transparent';
        inputPeso.style.padding = '10px';
        inputPeso.style.color = 'red';
        inputPeso.style.width = '375px';
        inputPeso.style.height = '140px';
        inputPeso.focus();
        inputPeso.select();

        // ✅ NUEVO: Agregar evento para restar tara en tiempo real
        inputPeso.addEventListener('input', function () {
            // Este evento ya está manejando la entrada del usuario
            // El peso que ingrese será el peso neto (ya considerando la tara)
        });

        if (kpiPeso) {
            kpiPeso.classList.remove('ok', 'error');
            kpiPeso.classList.add('manual-mode');
        }

        btnToggle.innerHTML = 'Automático';
        btnToggle.classList.add('manual-active');
        btnToggle.title = 'Volver a modo automático';

        setStatus(false);
        console.log('✏️ Modo Manual activado');
        if (pesoTaraActual > 0) {
            showSuccess(`Modo Manual: Ingresa peso neto (Tara: ${pesoTaraActual} kg activa)`);
        } else {
            showSuccess('Modo Manual: Ingresa el peso manualmente');
        }

    } else {
        // ========== VOLVER A MODO AUTOMÁTICO ==========
        const pesoManual = inputPeso.value.trim();

        if (pesoManual && !isNaN(pesoManual) && parseFloat(pesoManual) > 0) {
            pesoActual.textContent = parseFloat(pesoManual).toFixed(2);
        }

        inputPeso.style.display = 'none';
        pesoActual.style.display = 'block';

        if (kpiPeso) {
            kpiPeso.classList.remove('manual-mode', 'error');
            kpiPeso.classList.add('ok');
        }

        btnToggle.innerHTML = 'Manual';
        btnToggle.classList.remove('manual-active');
        btnToggle.title = 'Cambiar a modo manual (requiere contraseña)';

        // Reiniciar lectura de báscula o simulación
        const configBascula = cargarConfigBascula();
        if (configBascula && configBascula.ip) {
            console.log('📡 Reconectando con báscula...');
            iniciarLecturaBascula();
        } else {
            console.log('🔄 Iniciando simulación (sin báscula configurada)');
            simularLecturas();
        }

        setStatus(true);
        console.log('🔄 Modo Automático activado');
        if (pesoTaraActual > 0) {
            showSuccess(`Modo Automático: Lectura activa (Tara: ${pesoTaraActual} kg)`);
        } else {
            showSuccess('Modo Automático: Lectura activa');
        }
    }
}

// =====================
// MODALES - VERSIÓN CORREGIDA
// =====================
function abrirModal(idModal) {
    document.getElementById("overlay").style.display = "block";
    const modal = document.getElementById(idModal);
    modal.style.display = "block";

    if (idModal === "modalProducto") {
        cargarProductosModal(); // 🔹 Llama la versión nueva
    }
}


function cerrarModal(modalId) {
    const overlayEl = document.getElementById("overlay");
    const modal = document.getElementById(modalId);

    if (modal) {
        modal.style.display = "none";
        modal.classList.remove("active");
    }
    if (overlayEl) overlayEl.style.display = "none";
    console.log('❌ Modal cerrado:', modalId);
}

function abrirModalContra(destino) {
    console.log('🔐 Abriendo modal con contraseña para:', destino);
    modalDestino = destino;
    abrirModal("modalContra");
}

function Comparar() {
    const input = document.getElementById("Password").value;
    if (input === serverPassword) {
        cerrarModal("modalContra");
        document.getElementById("Password").value = ""; // limpiar campo

        if (modalDestino === "modo") {
            alternarModoEntrada(); // Ejecutar cambio a modo manual
        } else {
            abrirModal(modalDestino); // Abrir otros modales
        }
    } else {
        alert("❌ Contraseña incorrecta");
    }
}


// =====================
// Cargar productos en el modal
// =====================
function cargarProductosModal() {
    const contenedor = document.getElementById("contenedorProductos");

    if (!contenedor) {
        console.error("❌ Contenedor de productos no encontrado");
        return;
    }

    contenedor.innerHTML = "";

    console.log("📋 Cargando productos en modal:", productosGlobal.length);

    if (!productosGlobal || productosGlobal.length === 0) {
        contenedor.innerHTML = `
            <div class="no-products">
                <p>No hay productos disponibles para este lote.</p>
                <p>Selecciona un lote primero.</p>
            </div>
        `;
        return;
    }

    productosGlobal.forEach((producto, index) => {
        const div = document.createElement("div");
        div.classList.add("product-card");

        div.innerHTML = `
            <div class="product-info">
                <img src="${producto.imagen}" alt="${producto.nombre}" onerror="this.src='/images/default.png'" />
                <div class="product-details">
                    <h4>${producto.sku}</h4>
                    <p>${producto.nombre}</p>
                    <small>Porcentaje: ${producto.porcentaje}% | Velocidad: ${producto.velocidad}</small>
                </div>
            </div>
        `;

        div.onclick = async () => {
            console.log("🎯 Producto seleccionado:", producto);

            // Actualizar UI
            productoSeleccionado.textContent = producto.nombre;
            setDatosProceso(producto);

            cerrarModal("modalProducto");
            showSuccess(`Producto seleccionado: ${producto.nombre}`);
        };

        contenedor.appendChild(div);
    });

    console.log(`✅ ${productosGlobal.length} productos cargados en el modal`);
}



function seleccionarProductoModal(productoId) {
    const producto = productosGlobal.find(p => p.id === productoId);
    if (!producto) {
        showError("Producto no encontrado");
        return;
    }

    setDatosProceso(producto);
    if (productoSeleccionado) {
        productoSeleccionado.textContent = producto.nombre;
    }

    cerrarModal("modalProducto");
    showSuccess(`Producto seleccionado: ${producto.nombre}`);
}

let dataReporte = []; // cache de datos originales
/*
document.getElementById("btnreporte").addEventListener("click", async () => {
    await cargarReporte();
    abrirModal("modalreporte");
});
*/

async function cargarReporte() {
    const tbody = document.getElementById("tablaReporteBody");
    tbody.innerHTML = "<tr><td colspan='21'>Cargando...</td></tr>";

    try {
        const resp = await fetch("/api/Capturas");
        if (!resp.ok) throw new Error("Error al obtener datos");

        dataReporte = await resp.json(); // guardamos copia

        pintarTabla(dataReporte);
    } catch (err) {
        console.error(err);
        tbody.innerHTML = "<tr><td colspan='21'>Error cargando el reporte</td></tr>";
    }
}

// =====================
// FILTRADO
// =====================
document.getElementById("btnFiltrar").addEventListener("click", () => {
    const lote = document.getElementById("filtroLote").value.toLowerCase();
    const usuario = document.getElementById("filtroUsuario").value.toLowerCase();
    const fecha = document.getElementById("filtroFecha").value;

    const filtrados = dataReporte.filter(item => {
        let ok = true;
        if (lote && !(item.lote ?? "").toLowerCase().includes(lote)) ok = false;
        if (usuario && !(item.usuarioCorreo ?? "").toLowerCase().includes(usuario)) ok = false;
        if (fecha) {
            const fechaItem = item.fechaCaptura ? new Date(item.fechaCaptura).toISOString().split("T")[0] : "";
            if (fechaItem !== fecha) ok = false;
        }
        return ok;
    });

    pintarTabla(filtrados);
});

document.getElementById("btnLimpiarFiltros").addEventListener("click", () => {
    document.getElementById("filtroLote").value = "";
    document.getElementById("filtroUsuario").value = "";
    document.getElementById("filtroFecha").value = "";
    pintarTabla(dataReporte);
});

// =====================
// PINTAR TABLA
// =====================
function pintarTabla(data) {
    const tbody = document.getElementById("tablaReporteBody");
    tbody.innerHTML = "";

    if (!data || data.length === 0) {
        tbody.innerHTML = "<tr><td colspan='21'>No hay registros</td></tr>";
        return;
    }

    data.forEach(item => {
        const fila = `
            <tr>
                <td>${item.idCaptura ?? ""}</td>
                <td>${item.lote ?? ""}</td>
                <td>${item.producto ?? ""}</td>
                <td>${item.programacion ?? ""}</td>
                <td>${item.sku ?? ""}</td>
                <td>${item.porcentaje ?? ""}</td>
                <td>${item.velocidad ?? ""}</td>
                <td>${item.modo ?? ""}</td>
                <td>${item.presion ?? ""}</td>
                <td>${item.altura ?? ""}</td>
                <td>${item.avance ?? ""}</td>
                <td>${item.tara ?? ""}</td>
                <td>${item.pesoActual ?? ""}</td>
                <td>${item.porcentajeActual ?? ""}</td>
                <td>${item.velocidadActual ?? ""}</td>
                <td>${item.fechaCaptura ? new Date(item.fechaCaptura).toLocaleString() : ""}</td>
                <td>${item.usuarioCorreo ?? ""}</td>
            </tr>
        `;
        tbody.insertAdjacentHTML("beforeend", fila);
    });
}


// =====================
// CONFIGURACIÓN DE DISPOSITIVOS
// =====================
function guardarBascula() {
    const ipBascula = document.getElementById("ipBascula");
    const comandoBascula = document.getElementById("comandoBascula");
    const metaBascula = document.getElementById("metaBascula");

    if (!ipBascula || !comandoBascula || !metaBascula) {
        showError("Elementos de configuración de báscula no encontrados");
        return;
    }

    const ip = ipBascula.value.trim();
    const comando = comandoBascula.value;

    if (!ip) {
        showError("Ingrese una IP válida para la báscula");
        return;
    }

    const ipRegex = /^(\d{1,3}\.){3}\d{1,3}$/;
    if (!ipRegex.test(ip)) {
        showError("Formato de IP inválido. Use formato: 192.168.1.100");
        return;
    }

    if (guardarConfigBascula(ip, comando)) {
        metaBascula.textContent = `IP: ${ip} • ${comando}`;
        cerrarModal('modalBascula');
        showSuccess('Configuración de báscula guardada');

        ipBascula.value = '';
        comandoBascula.selectedIndex = 0;

        // Reiniciar lectura con nueva configuración
        console.log('🔄 Reconectando con nueva configuración de báscula...');
        if (!modoManual) {
            clearInterval(timer);
            timer = null;
            setTimeout(() => {
                iniciarLecturaBascula();
            }, 1000);
        }
    }
}

function guardarImpresora() {
    const ipImpresora = document.getElementById("ipImpresora");
    const metaImpresora = document.getElementById("metaImpresora");

    if (!ipImpresora || !metaImpresora) {
        showError("Elementos de configuración de impresora no encontrados");
        return;
    }

    const ip = ipImpresora.value.trim();
    if (!ip) {
        showError("Ingrese una IP válida para la impresora");
        return;
    }

    // Validación básica de formato IP
    const ipRegex = /^(\d{1,3}\.){3}\d{1,3}$/;
    if (!ipRegex.test(ip)) {
        showError("Formato de IP inválido. Use formato: 192.168.1.100");
        return;
    }

    // Guardar en variable global Y en localStorage
    ipImpresoraGlobal = ip;

    if (guardarConfigImpresora(ip)) {
        // Actualizar UI
        metaImpresora.textContent = `IP: ${ip}`;
        cerrarModal('modalImpresora');
        showSuccess('Configuración de impresora guardada y persistida');

        // Limpiar campo del modal
        ipImpresora.value = '';
    }
}


// Función para cargar taras
async function cargarTaras() {
    try {
        const response = await fetch("/api/Tara/Listar");
        const taras = await response.json();

        const contenedor = document.getElementById("contenedorTaras");
        contenedor.innerHTML = "";

        // Agregar opción para quitar tara
        const divSinTara = document.createElement("div");
        divSinTara.classList.add("tara-item");
        divSinTara.style.backgroundColor = "#f0f0f0";
        divSinTara.innerHTML = `
            <div><strong>❌ SIN TARA</strong></div>
            <div>Peso: 0 kg</div>
        `;
        divSinTara.addEventListener("click", () => {
            pesoTaraActual = 0;
            taraDescripcion = "";
            document.getElementById("tara").value = "";
            cerrarModal("modalTara");
            showSuccess(`Tara removida - mostrando peso bruto`);
            console.log('🗑️ Tara removida');
        });
        contenedor.appendChild(divSinTara);

        // Agregar todas las taras disponibles
        taras.forEach(tara => {
            const div = document.createElement("div");
            div.classList.add("tara-item");
            div.innerHTML = `
                <div><strong>${tara.descripcion}</strong></div>
                <div>Peso: ${tara.peso} kg</div>
            `;
            div.addEventListener("click", () => {
                // Guardar peso y descripción de la tara
                pesoTaraActual = parseFloat(tara.peso) || 0;
                taraDescripcion = tara.descripcion;

                // Actualizar campo visual
                document.getElementById("tara").value = `${tara.descripcion} (${tara.peso} kg)`;

                cerrarModal("modalTara");
                showSuccess(`Tara aplicada: ${tara.descripcion} (-${tara.peso} kg) - Se mantiene activa`);
                console.log('✅ Tara activa:', pesoTaraActual, 'kg');
            });
            contenedor.appendChild(div);
        });
    } catch (err) {
        console.error("Error cargando taras:", err);
    }
}

function aplicarTaraAPeso() {
    if (modoManual) {
        // En modo manual, actualizar el input
        const inputPeso = document.getElementById("inputPesoManual");
        if (inputPeso && inputPeso.value.trim() !== "") {
            const pesoActualValor = parseFloat(inputPeso.value) || 0;
            const pesoConTara = Math.max(0, pesoActualValor - pesoTaraActual);
            inputPeso.value = pesoConTara.toFixed(2);
        }
    }
    // En modo automático la resta se aplica en leerYActualizarPeso() y simularLecturas()
}

function actualizarIndicadorTara() {
    const taraField = document.getElementById("tara");
    if (!taraField) return;

    if (pesoTaraActual > 0) {
        taraField.style.backgroundColor = '#fff3cd'; // Amarillo suave
        taraField.style.borderColor = '#ffc107';
        taraField.title = `Tara activa: ${pesoTaraActual} kg - Restando automáticamente`;
    } else {
        taraField.style.backgroundColor = '';
        taraField.style.borderColor = '';
        taraField.title = 'Sin tara aplicada';
    }
}


async function seleccionarProducto(sku, nombre) {
    productoSeleccionado.textContent = nombre.toUpperCase();

    const receta = await obtenerReceta(sku);

    if (receta) {
        // aquí actualizas tus campos en pantalla
        porcentajeActual = receta.porcentaje;
        velocidadActual = receta.velocidad;
        pesoActual = receta.presion; // ejemplo, ajusta según tu UI
    }

    cerrarModal(); // si usas modal
}

async function cargarReporteHoy() {
    const tbody = document.getElementById("tablaReporteHoyBody");
    tbody.innerHTML = "<tr><td colspan='14'>Cargando...</td></tr>";

    try {
        const resp = await fetch("/api/Capturas");
        if (!resp.ok) throw new Error("Error al obtener datos");

        dataReporte = await resp.json();
        console.log("📦 Datos API:", dataReporte); // 👈 revisa aquí

        // 🔹 Filtrar solo capturas del día de hoy
        const hoy = new Date().toISOString().split("T")[0];
        const capturasHoy = dataReporte.filter(item => {
            if (!item.fechaCaptura) return false;
            const fechaItem = new Date(item.fechaCaptura).toISOString().split("T")[0];
            return fechaItem === hoy;
        });

        // 🔹 Pintar tabla
        if (capturasHoy.length === 0) {
            tbody.innerHTML = "<tr><td colspan='14'>No hay capturas registradas hoy</td></tr>";
        } else {
            tbody.innerHTML = capturasHoy.map((item, i) => `
                <tr>
                    <td>${i + 1}</td>
                    <td>${item.lote ?? ""}</td>
                    <td>${item.sku ?? ""}</td>
                    <td>${item.pesoActual ?? ""}</td>
                    <td>${item.producto ?? ""}</td>
                    <td>${item.programacion ?? ""}</td>
                    <td>${item.fechaCaptura ? new Date(item.fechaCaptura).toLocaleDateString() : ""}</td>
                    <td>${item.fechaCaptura ? new Date(item.fechaCaptura).toLocaleTimeString() : ""}</td>
                </tr>
            `).join("");
        }

    } catch (err) {
        console.error(err);
        tbody.innerHTML = "<tr><td colspan='14'>Error cargando el reporte</td></tr>";
    }
}

// 🔹 Ejecutar cuando cargue la página
document.addEventListener("DOMContentLoaded", cargarReporteHoy);


// =====================
// API: Obtener lotes desde backend MVC
// =====================
async function obtenerLotes() {
    try {
        console.log('🔄 Cargando lotes...');
        const response = await fetch("/api/Lotes/Listar", {
            method: "GET",
            headers: { "Accept": "application/json" }
        });

        if (!response.ok) throw new Error(`HTTP ${response.status}: ${response.statusText}`);

        const lotes = await response.json();
        console.log("✅ Lotes cargados:", lotes.length);
        return lotes;
    } catch (error) {
        showError(`Error al cargar lotes: ${error.message}`);
        return [];
    }
}

// =====================
// Inicializar select de lotes
// =====================
async function initLotes() {
    loteSelect = document.getElementById("loteSelect");
    programacionActual = document.getElementById("programacionActual");
    productoSeleccionado = document.getElementById("productoSeleccionado");

    if (!loteSelect) {
        console.error("❌ No se encontró #loteSelect en el DOM");
        return;
    }

    const lotes = await obtenerLotes();
    window.lotesGlobal = lotes;

    // Cargar opciones
    loteSelect.innerHTML = `<option>Seleccionar</option>`;
    lotes.forEach(l => {
        const opt = document.createElement("option");
        opt.value = l.lote;
        opt.textContent = l.lote;
        loteSelect.appendChild(opt);
    });

    // Evento de cambio
    loteSelect.addEventListener("change", handleLoteChange);

    console.log("✅ Lotes inicializados en el select");
}

// =====================
// Manejo cuando se selecciona un lote
// =====================


async function obtenerProductosPorPlantilla(plantilla) {
    try {
        if (!plantilla || plantilla === "0" || plantilla === 0) {
            console.warn("⚠️ Plantilla inválida:", plantilla);
            return [];
        }

        console.log(`🔄 Obteniendo productos para plantilla ${plantilla}...`);

        // Usar la ruta corregida del controller
        const resp = await fetch(`/api/Recetas/productos/${plantilla}`, {
            method: 'GET',
            headers: {
                'Accept': 'application/json',
                'Content-Type': 'application/json'
            }
        });

        if (!resp.ok) {
            if (resp.status === 404) {
                console.warn(`⚠️ No se encontraron productos para la plantilla ${plantilla}`);
                return [];
            }
            throw new Error(`HTTP ${resp.status}: ${resp.statusText}`);
        }

        const productos = await resp.json();
        console.log(`✅ ${Array.isArray(productos) ? productos.length : 'N/A'} productos obtenidos para plantilla ${plantilla}`);
        console.log("📋 Productos obtenidos:", productos);

        return Array.isArray(productos) ? productos : [];

    } catch (err) {
        console.error("❌ Error cargando productos por plantilla:", err);
        showError(`Error al cargar productos: ${err.message}`);
        return [];
    }
}

// PASO 2: Función para enriquecer productos con datos de receta
async function enriquecerProductosConRecetas(productos) {
    if (!productos || productos.length === 0) {
        console.warn("⚠️ No hay productos para enriquecer");
        return [];
    }

    console.log(`🔄 Enriqueciendo ${productos.length} productos con recetas...`);

    const productosEnriquecidos = [];

    for (const producto of productos) {
        try {
            const sku = producto.sku;
            if (!sku) {
                console.warn("⚠️ Producto sin SKU:", producto);
                continue;
            }

            console.log(`🔄 Consultando receta para SKU: ${sku}`);
            const receta = await obtenerReceta(sku);

            const productoCompleto = {
                // Datos del producto base
                id: producto.id || sku,
                sku: sku,
                nombre: producto.nombre || `Producto ${sku}`,
                porcentajeBase: producto.porcentaje || 0, // Del listado de plantilla

                // Datos de la receta (más detallados)
                porcentaje: receta?.porcentaje || producto.porcentaje || 0,
                velocidad: receta?.velocidad || 0,
                modo: receta?.modoInyeccion || 0,
                presion: receta?.presion || 0,
                altura: receta?.altura || 0,
                avance: receta?.avance || "",
                tara: receta?.tara || 0,
                fk_Inyectora: receta?.fk_Inyectora || 0,

                // Imagen del producto
                imagen: obtenerImagenProducto(producto.nombre || `Producto ${sku}`, sku)
            };

            productosEnriquecidos.push(productoCompleto);
            console.log(`✅ Producto enriquecido: ${sku} - ${producto.nombre}`);

        } catch (error) {
            console.error(`❌ Error enriqueciendo producto ${producto.sku}:`, error);
            // Agregar producto sin enriquecer en caso de error
            productosEnriquecidos.push({
                id: producto.sku,
                sku: producto.sku,
                nombre: producto.nombre || `Producto ${producto.sku}`,
                porcentaje: producto.porcentaje || 0,
                velocidad: 0,
                modo: 0,
                presion: 0,
                altura: 0,
                avance: "",
                tara: 0,
                imagen: obtenerImagenProducto(producto.nombre, producto.sku)
            });
        }
    }

    console.log(`✅ ${productosEnriquecidos.length} productos enriquecidos completamente`);
    return productosEnriquecidos;
}

// PASO 3: Función principal del flujo completo
async function procesarLoteCompleto(loteSeleccionado) {
    try {
        console.log("🚀 Iniciando procesamiento completo del lote:", loteSeleccionado);

        // 1. Buscar información del lote
        const loteInfo = (window.lotesGlobal || []).find(l => l.lote === loteSeleccionado);

        if (!loteInfo) {
            throw new Error(`No se encontró información para el lote ${loteSeleccionado}`);
        }

        console.log("📋 Información del lote:", loteInfo);

        // Nueva: almacenar loteId en la variable global
        loteIdGlobal = loteInfo.loteId;
        console.log("🔑 LoteId almacenado:", loteIdGlobal);

        // 2. Extraer la plantilla (CORREGIDO: era 'plan', ahora es 'plantilla')
        const plantilla = loteInfo.plantilla;

        if (!plantilla || plantilla === "0" || plantilla === 0) {
            throw new Error(`El lote ${loteSeleccionado} no tiene una plantilla válida (plantilla: ${plantilla})`);
        }

        // 3. Obtener productos de la plantilla
        console.log(`🔄 Consultando productos para plantilla: ${plantilla}`);
        const productosBase = await obtenerProductosPorPlantilla(plantilla);

        if (productosBase.length === 0) {
            throw new Error(`No se encontraron productos para la plantilla ${plantilla}`);
        }

        // 4. Enriquecer productos con datos de recetas
        console.log("🔄 Enriqueciendo productos con recetas...");
        const productosCompletos = await enriquecerProductosConRecetas(productosBase);

        // 5. Actualizar estado global
        productosGlobal = productosCompletos;

        console.log("✅ Procesamiento completo finalizado:", {
            lote: loteSeleccionado,
            plantilla: plantilla,
            productosEncontrados: productosCompletos.length
        });

        return {
            success: true,
            lote: loteSeleccionado,
            plantilla: plantilla,
            productos: productosCompletos,
            programacion: loteInfo.nombre
        };

    } catch (error) {
        console.error("❌ Error en procesamiento completo:", error);
        return {
            success: false,
            error: error.message,
            lote: loteSeleccionado
        };
    }
}



async function handleLoteChange() {
    limpiarCampos();

    const lote = loteSelect.value;
    if (!lote || lote === "Seleccionar" || lote === "—") {
        setStatus(false);
        programacionActual.textContent = "—";
        productoSeleccionado.textContent = "—";
        productosGlobal = [];
        loteIdGlobal = null;

        // Detener lectura de báscula
        if (timer) {
            clearInterval(timer);
            timer = null;
        }
        return;
    }

    console.log("🔄 Cambiando a lote:", lote);
    programacionActual.textContent = "Cargando...";
    setStatus(false);

    try {
        const resultado = await procesarLoteCompleto(lote);

        if (resultado.success) {
            programacionActual.textContent = resultado.programacion?.toUpperCase() || "—";
            setStatus(true);

            // Iniciar lectura de báscula o simulación
            const configBascula = cargarConfigBascula();
            if (configBascula && configBascula.ip) {
                console.log('📡 Iniciando lectura de báscula configurada');
                iniciarLecturaBascula();
            } else {
                console.log('🔄 No hay báscula configurada, usando simulación');
                simularLecturas();
            }

            showSuccess(`Lote ${lote} cargado con ${resultado.productos.length} productos`);
            console.log("📊 Productos cargados en productosGlobal:", productosGlobal);
        } else {
            programacionActual.textContent = "Error";
            setStatus(false);
            productosGlobal = [];
            loteIdGlobal = null;
            showError(resultado.error);
        }

    } catch (error) {
        console.error("❌ Error inesperado:", error);
        programacionActual.textContent = "Error";
        setStatus(false);
        productosGlobal = [];
        loteIdGlobal = null;
        showError(`Error inesperado: ${error.message}`);
    }
}

const modal = document.getElementById("modalComparacion");
const btn = document.getElementById("btnAbrirComparacion");
const span = document.getElementsByClassName("close")[0];

// Abrir modal
btn.onclick = function () {
    modal.style.display = "block";
    setFechaHoy();
    cargarComparacion(); // carga datos de hoy automáticamente
}

// Cerrar si clickea fuera
window.onclick = function (event) {
    if (event.target == modal) {
        modal.style.display = "none";
    }
}

// Poner fecha de hoy en el input
function setFechaHoy() {
    const hoy = new Date().toISOString().split("T")[0];
    document.getElementById("fechaComparacion").value = hoy;
}

// Cargar datos desde la API Comparacion
async function cargarComparacion() {
    const fecha = document.getElementById("fechaComparacion").value;

    try {
        const res = await fetch(`/api/comparacion?fechaIn=${fecha}&fechaFin=${fecha}`);
        const data = await res.json();

        const tbody = document.querySelector("#tablaComparacion tbody");
        tbody.innerHTML = "";

        if (data.length === 0) {
            tbody.innerHTML = `<tr><td colspan="9">No hay datos</td></tr>`;
            return;
        }

        data.forEach(item => {
            const row = document.createElement("tr");

            // Convertimos diferencia "2%" a número decimal
            const diff = parseFloat(item.diferencia.replace("%", "").replace(",", "."));

            // Regla de colores por fila
            if (diff > 2) {
                row.classList.add("amarillo");
            } else if (diff < -2) {
                row.classList.add("rojo");
            } else {
                row.classList.add("verde");
            }

            row.innerHTML = `
                <td>${item.lote}</td>
                <td>${item.sku}</td>
                <td>${item.producto}</td>
                <td>${item.pesoAntes.toFixed(2)}</td>
                <td>${item.esperado.toFixed(2)}</td>
                <td>${item.pesoDespues.toFixed(2)}</td>
                <td>${item.diferencia}</td>
                 <!-- <td class="${item.estado === 'OK' ? 'estado-ok' : 'estado-fail'}">
                  ${item.estado === 'OK' ? '✅ OK' : '❌ FAIL'}
                </td> -->
                
            `;
            tbody.appendChild(row);
        });
    } catch (err) {
        console.error("Error cargando comparación:", err);
        alert("Error al obtener los datos");
    }
}

async function imprimirEtiqueta(datos) {
    if (!ipImpresoraGlobal) {
        showError("No hay IP de impresora configurada");
        return;
    }

    // Generar el contenido en ZPL
    const zpl = `
        ^XA

        ^FX Top section with logo, name and address.
        ^CF0,70
        ^FO260,150^FDCarnes G^FS
        ^CF0,42
        ^FO260,220^FDCalidad Garantizada^FS
        ^FO60,280^GB700,6,5^FS

        ^CFA,35
        ^FO60,330^FDLote: ${datos.lote}^FS
        ^FO60,380^FDProducto: ${datos.producto}^FS
        ^FO60,430^FDSKU: ${datos.sku}^FS
        ^FO60,480^FDPeso: ${datos.pesoActual} kg^FS
        ^FO60,530^FDTara: ${datos.tara} ^FS
        ^FO60,580^FDFecha: ${new Date().toLocaleString()}^FS
        ^FO140,650^FDETIQUETA DE PRUEBA^FS
        ^CFA,15
        ^FO60,700^GB700,6,5^FS
        
        ^XZ
    `;

    console.log('Enviando etiqueta a impresora:', ipImpresoraGlobal);

    try {
        const res = await fetch(`/api/Impresora/imprimir?ip=${encodeURIComponent(ipImpresoraGlobal)}`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(zpl)
        });

        if (!res.ok) throw new Error(await res.text());

        showSuccess("Etiqueta enviada a la impresora Zebra");
    } catch (err) {
        showError(`Error al imprimir: ${err.message}`);
    }
}

/**
 * Guardar configuración de impresora en localStorage
 */
function guardarConfigImpresora(ip) {
    try {
        if (!ip || ip.trim() === '') {
            console.warn('⚠️ IP de impresora vacía, no se guardará');
            return false;
        }

        localStorage.setItem(STORAGE_KEYS.IMPRESORA_IP, ip.trim());
        console.log('✅ IP de impresora guardada:', ip);
        return true;
    } catch (error) {
        console.error('❌ Error guardando IP de impresora:', error);
        showError('Error al guardar configuración de impresora');
        return false;
    }
}

/**
 * Cargar configuración de impresora desde localStorage
 */
function cargarConfigImpresora() {
    try {
        const ip = localStorage.getItem(STORAGE_KEYS.IMPRESORA_IP);
        if (ip && ip.trim() !== '') {
            console.log('✅ IP de impresora cargada desde localStorage:', ip);
            return ip.trim();
        }
        return null;
    } catch (error) {
        console.error('❌ Error cargando IP de impresora:', error);
        return null;
    }
}

/**
 * Limpiar configuración de impresora
 */
function limpiarConfigImpresora() {
    try {
        localStorage.removeItem(STORAGE_KEYS.IMPRESORA_IP);
        console.log('🗑️ Configuración de impresora eliminada');
        return true;
    } catch (error) {
        console.error('❌ Error eliminando configuración:', error);
        return false;
    }
}

/**
 * Guardar configuración de báscula en localStorage
 */
function guardarConfigBascula(ip, comando) {
    try {
        if (!ip || ip.trim() === '') {
            console.warn('⚠️ IP de báscula vacía, no se guardará');
            return false;
        }

        const config = {
            ip: ip.trim(),
            comando: comando || 'Leer Peso'
        };

        localStorage.setItem(STORAGE_KEYS.BASCULA_CONFIG, JSON.stringify(config));
        console.log('✅ Configuración de báscula guardada:', config);
        return true;
    } catch (error) {
        console.error('❌ Error guardando configuración de báscula:', error);
        showError('Error al guardar configuración de báscula');
        return false;
    }
}

/**
 * Cargar configuración de báscula desde localStorage
 */
function cargarConfigBascula() {
    try {
        const configStr = localStorage.getItem(STORAGE_KEYS.BASCULA_CONFIG);
        if (configStr) {
            const config = JSON.parse(configStr);
            console.log('✅ Configuración de báscula cargada:', config);
            return config;
        }
        return null;
    } catch (error) {
        console.error('❌ Error cargando configuración de báscula:', error);
        return null;
    }
}

function inicializarConfiguracionesGuardadas() {
    console.log('🔄 Cargando configuraciones guardadas...');

    // Cargar configuración de impresora
    const ipImpresora = cargarConfigImpresora();
    if (ipImpresora) {
        ipImpresoraGlobal = ipImpresora;

        const metaImpresora = document.getElementById("metaImpresora");
        if (metaImpresora) {
            metaImpresora.textContent = `IP: ${ipImpresora}`;
        }

        console.log('✅ Configuración de impresora restaurada:', ipImpresora);
        showSuccess(`Impresora configurada: ${ipImpresora}`, 2000);
    }

    // Cargar configuración de báscula
    const configBascula = cargarConfigBascula();
    if (configBascula) {
        const metaBascula = document.getElementById("metaBascula");
        if (metaBascula) {
            metaBascula.textContent = `IP: ${configBascula.ip} • ${configBascula.comando}`;
        }

        console.log('✅ Configuración de báscula restaurada:', configBascula);
        showSuccess(`Báscula configurada: ${configBascula.ip}`, 2000);
    }

    if (!ipImpresora && !configBascula) {
        console.log('ℹ️ No se encontraron configuraciones guardadas');
    }
}

/**
 * Función para pre-llenar los modales con configuración actual
 */
function preLlenarModalImpresora() {
    const ipImpresoraInput = document.getElementById("ipImpresora");
    if (ipImpresoraInput && ipImpresoraGlobal) {
        ipImpresoraInput.value = ipImpresoraGlobal;
        console.log('🔄 Modal de impresora pre-llenado con:', ipImpresoraGlobal);
    }
}

function preLlenarModalBascula() {
    const configBascula = cargarConfigBascula();
    if (configBascula) {
        const ipBascuaInput = document.getElementById("ipBascula");
        const comandoBascuaSelect = document.getElementById("comandoBascula");

        if (ipBascuaInput) {
            ipBascuaInput.value = configBascula.ip;
        }

        if (comandoBascuaSelect) {
            comandoBascuaSelect.value = configBascula.comando;
        }

        console.log('🔄 Modal de báscula pre-llenado con:', configBascula);
    }
}

/**
 * Función mejorada para abrir modales con pre-llenado
 */
function abrirModalMejorado(idModal) {
    // Llamar a la función original
    abrirModal(idModal);

    // Pre-llenar según el tipo de modal
    if (idModal === "modalImpresora") {
        setTimeout(preLlenarModalImpresora, 100); // Pequeño delay para asegurar que el modal esté visible
    } else if (idModal === "modalBascula") {
        setTimeout(preLlenarModalBascula, 100);
    }
}

/**
 * Panel de gestión de configuraciones (opcional)
 */
function mostrarConfiguracionesActuales() {
    const ipImpresora = cargarConfigImpresora();
    const configBascula = cargarConfigBascula();

    const info = [];
    info.push('📋 CONFIGURACIONES ACTUALES:');
    info.push('─────────────────────────────');

    if (ipImpresora) {
        info.push(`🖨️  Impresora: ${ipImpresora}`);
    } else {
        info.push('🖨️  Impresora: No configurada');
    }

    if (configBascula) {
        info.push(`⚖️   Báscula: ${configBascula.ip} (${configBascula.comando})`);
    } else {
        info.push('⚖️   Báscula: No configurada');
    }

    console.log(info.join('\n'));

    // Opcional: mostrar en UI
    const infoText = info.join('\n');
    alert(infoText); // Cambiar por un modal más elegante si prefieres
}

/**
 * Función para resetear todas las configuraciones
 */
function resetearConfiguraciones() {
    if (confirm('¿Estás seguro de que quieres eliminar todas las configuraciones guardadas?')) {
        try {
            // Limpiar localStorage
            localStorage.removeItem(STORAGE_KEYS.IMPRESORA_IP);
            localStorage.removeItem(STORAGE_KEYS.BASCULA_CONFIG);

            // Limpiar variables globales
            ipImpresoraGlobal = "";

            // Limpiar UI
            const metaImpresora = document.getElementById("metaImpresora");
            const metaBascula = document.getElementById("metaBascula");

            if (metaImpresora) metaImpresora.textContent = "";
            if (metaBascula) metaBascula.textContent = "";

            showSuccess('Todas las configuraciones han sido eliminadas');
            console.log('🗑️ Configuraciones reseteadas');

        } catch (error) {
            console.error('❌ Error reseteando configuraciones:', error);
            showError('Error al resetear configuraciones');
        }
    }
}

async function cargarPassword() {
    try {
        const res = await fetch("/api/config/password");
        const data = await res.json();
        serverPassword = data.password;
    } catch (error) {
        console.error("Error al cargar la contraseña:", error);
    }
}
cargarPassword();


// =====================
// LECTURA REAL DE BÁSCULA
// =====================
let estadoBascula = {
    conectada: false,
    ultimaLectura: null,
    erroresConsecutivos: 0
};

async function leerPesoBascula() {
    const configBascula = cargarConfigBascula();

    if (!configBascula || !configBascula.ip) {
        console.warn('⚠️ No hay configuración de báscula');
        return null;
    }

    try {
        console.log('🔄 Solicitando peso a báscula:', configBascula.ip);

        const response = await fetch(`/api/Bascular/LeerPeso?ip=${encodeURIComponent(configBascula.ip)}`, {
            method: 'GET',
            headers: {
                'Accept': 'application/json'
            },
            signal: AbortSignal.timeout(5000) // Timeout de 5 segundos
        });

        if (!response.ok) {
            throw new Error(`HTTP ${response.status}: ${response.statusText}`);
        }

        const data = await response.json();

        // Validar que el peso sea un número válido
        const peso = parseFloat(data.peso);
        if (isNaN(peso) || peso < 0) {
            throw new Error('Peso inválido recibido de la báscula');
        }

        // Actualizar estado
        estadoBascula.conectada = true;
        estadoBascula.ultimaLectura = new Date();
        estadoBascula.erroresConsecutivos = 0;

        console.log('✅ Peso recibido:', peso);
        return peso.toFixed(2);

    } catch (error) {
        estadoBascula.erroresConsecutivos++;

        if (estadoBascula.erroresConsecutivos >= 3) {
            estadoBascula.conectada = false;
            console.error('❌ Báscula desconectada después de múltiples intentos');
        }

        console.error('❌ Error leyendo báscula:', error.message);
        return null;
    }
}

function iniciarLecturaBascula() {
    // Limpiar timer anterior
    if (timer) {
        clearInterval(timer);
        timer = null;
    }

    // No iniciar si está en modo manual
    if (modoManual) {
        console.log('⏸️ Lectura de báscula pausada - Modo Manual activo');
        return;
    }

    const configBascula = cargarConfigBascula();
    if (!configBascula || !configBascula.ip) {
        console.warn('⚠️ No hay báscula configurada, usando simulación');
        simularLecturas();
        return;
    }

    console.log('📡 Iniciando lectura continua de báscula:', configBascula.ip);

    // Primera lectura inmediata
    leerYActualizarPeso();

    // Lecturas continuas cada 2 segundos
    timer = setInterval(leerYActualizarPeso, 2000);

    setStatus(true);
}

async function leerYActualizarPeso() {
    const peso = await leerPesoBascula();

    if (peso !== null) {
        // ✅ Se recibió un peso válido
        const pesoBruto = parseFloat(peso);
        pesoBrutoSinTara = pesoBruto; // Guardar peso bruto
        const pesoNeto = Math.max(0, pesoBruto - pesoTaraActual);

        if (pesoActual) {
            pesoActual.textContent = pesoNeto.toFixed(2);
        }

        // Actualizar indicador visual
        const kpiPeso = document.getElementById("kpiPeso");
        if (kpiPeso) {
            kpiPeso.classList.add('ok');
            kpiPeso.classList.remove('error');
        }

    } else {
        // ❌ Error al leer peso
        const kpiPeso = document.getElementById("kpiPeso");
        if (kpiPeso) {
            kpiPeso.classList.remove('ok');
            kpiPeso.classList.add('error');
        }

        if (estadoBascula.erroresConsecutivos >= 5) {
            console.warn('⚠️ Demasiados errores, deteniendo temporalmente lecturas');

            clearInterval(timer);
            timer = null;

            if (!modoManual) {
                showError('Error conectando con báscula. Reintentando en 10s...');

                setTimeout(() => {
                    if (!modoManual) {
                        console.log('🔄 Reintentando conexión con báscula...');
                        iniciarLecturaBascula();
                    } else {
                        console.log('⸏ En modo manual, no se reintenta conexión.');
                    }
                }, 10000);
            } else {
                console.log('⸏ En modo manual, reconexión detenida.');
            }
        }
    }
}



/**
 * Panel de diagnóstico de báscula
 */
function diagnosticoBascula() {
    const config = cargarConfigBascula();

    console.log('🔍 DIAGNÓSTICO DE BÁSCULA');
    console.log('═══════════════════════════════');
    console.log('📡 IP Configurada:', config?.ip || 'No configurada');
    console.log('⚙️  Comando:', config?.comando || 'No configurado');
    console.log('🔌 Estado:', estadoBascula.conectada ? '✅ Conectada' : '❌ Desconectada');
    console.log('📊 Última lectura:', estadoBascula.ultimaLectura || 'Nunca');
    console.log('❌ Errores consecutivos:', estadoBascula.erroresConsecutivos);
    console.log('🔄 Timer activo:', timer ? 'Sí' : 'No');
    console.log('✏️  Modo manual:', modoManual ? 'Sí' : 'No');

    if (pesoActual) {
        console.log('⚖️  Peso actual mostrado:', pesoActual.textContent);
    }

    return {
        configurada: config !== null,
        conectada: estadoBascula.conectada,
        ultimaLectura: estadoBascula.ultimaLectura,
        errores: estadoBascula.erroresConsecutivos
    };
}

/**
 * Probar conexión con báscula manualmente
 */
async function probarBascula() {
    console.log('🧪 Probando conexión con báscula...');

    const config = cargarConfigBascula();
    if (!config) {
        showError('No hay báscula configurada');
        return false;
    }

    showSuccess('Probando conexión...');

    const peso = await leerPesoBascula();

    if (peso !== null) {
        showSuccess(`✅ Báscula conectada. Peso: ${peso} kg`);
        return true;
    } else {
        showError('❌ No se pudo conectar con la báscula');
        return false;
    }
}

// Exponer funciones de diagnóstico
window.diagnosticoBascula = diagnosticoBascula;
window.probarBascula = probarBascula;


window.leerPesoBascula = leerPesoBascula;
window.iniciarLecturaBascula = iniciarLecturaBascula;

console.log('📡 Funciones de báscula disponibles: diagnosticoBascula(), probarBascula()');


window.testEndpoints = async function () {
    console.log('🧪 Iniciando pruebas de endpoints...');

    // Test 1: Conectividad básica
    await probarConectividad(1);

    // Test 2: Obtener productos (con un planId de ejemplo)
    await obtenerProductos(1);

    // Test 3: Obtener recetas
    await obtenerRecetas();

    // Test 4: Obtener receta específica (con SKU de ejemplo)
    await obtenerReceta('TEST001');

    console.log('🧪 Pruebas completadas');
};

// Función para debug del estado actual
window.debugState = function () {
    console.log('🔍 Estado actual:', {
        loteSeleccionado: loteSelect?.value,
        lotesGlobal: window.lotesGlobal?.length || 0,
        productosGlobal: productosGlobal?.length || 0,
        loteIdGlobal: loteIdGlobal,
        programacion: programacionActual?.textContent,
        productoSeleccionado: productoSeleccionado?.textContent
    });
};

console.log('✅ Funciones de API actualizadas. Usa testEndpoints() para probar conectividad.');



// =====================
// Inicializar al cargar página
// =====================
document.addEventListener("DOMContentLoaded", () => {
    initLotes();
});


const initOriginal = window.init;
window.init = async function () {
    // Llamar inicialización original
    if (initOriginal) {
        await initOriginal();
    }

    // Inicializar configuraciones guardadas
    setTimeout(inicializarConfiguracionesGuardadas, 500);

    console.log('✅ Sistema de persistencia inicializado');
};

// =====================
// INICIALIZACIÓN PRINCIPAL
// =====================
async function init() {
    // Verificar estado del DOM
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
        return;
    }

    console.log('🚀 Inicializando aplicación Inyecciones...');

    try {
        // 1. Inicializar elementos del DOM
        if (!initializeDOMElements()) {
            throw new Error("Error crítico: No se pudieron inicializar elementos del DOM");
        }

        // 2. Configurar event listeners
        setupEventListeners();

        // 3. Inicializar lotes
        initLotes();

        // 4. EXPONER FUNCIONES INMEDIATAMENTE 👈 AGREGAR ESTO
        console.log('🔗 Exponiendo funciones globales desde init...');

        window.abrirModal = abrirModal;
        window.cerrarModal = cerrarModal;
        window.abrirModalContra = abrirModalContra;
        window.Comparar = Comparar;
        window.seleccionarProductoModal = seleccionarProductoModal;
        window.guardarBascula = guardarBascula;
        window.guardarImpresora = guardarImpresora;
        window.mostrarConfiguracionesActuales = mostrarConfiguracionesActuales;
        window.resetearConfiguraciones = resetearConfiguraciones;
        window.abrirModalMejorado = abrirModalMejorado;

        // Verificar inmediatamente
        console.log('✅ Funciones expuestas desde init:', {
            abrirModal: typeof window.abrirModal,
            abrirModalContra: typeof window.abrirModalContra,
            Comparar: typeof window.Comparar
        });

        console.log('✅ Aplicación inicializada correctamente');

    } catch (error) {
        console.error('❌ Error durante la inicialización:', error);
        showError(`Error de inicialización: ${error.message}`);
    }
}

// INICIAR APLICACIÓN
// =====================
init();

        /* ALTER TABLE Capturas
ADD ModoCaptura NVARCHAR(20) NULL; */
