(() => {
    'use strict';
    const root = document.getElementById('activacionSkus');
    if (!root) return;
    const el = id => document.getElementById(`activacion${id}`);
    const puedeEscribir = root.dataset.escribir === 'true';
    const puedeEliminar = root.dataset.puedeEliminar === 'true';
    const maximoLote = Number(root.dataset.maximoLote) || 500;
    const seleccionados = new Map(), altasSeleccionadas = new Map();
    const llaveRegistro = registro => JSON.stringify([registro.origen, registro.aliasExterno, registro.articuloId]);
    // Las descripciones solo pertenecen a la presentación; el valor de cada opción sigue siendo la letra.
    const descripcionesTipo = new Map(Array.from(root.querySelectorAll('[data-tipo][data-descripcion]'),
        nodo => [nodo.dataset.tipo, nodo.dataset.descripcion]));
    const descripcionTipo = tipo => descripcionesTipo.get(tipo) || (tipo ? 'Sin descripción' : 'Sin tipo');
    const etiquetaTipo = tipo => tipo ? `${tipo} · ${descripcionTipo(tipo)}` : 'Sin tipo';
    const modal = new bootstrap.Modal(el('Modal'));
    const altaModal = new bootstrap.Modal(el('AltaModal'));
    const eliminarModal = new bootstrap.Modal(el('EliminarModal'));
    const masivoModal = new bootstrap.Modal(el('MasivoModal'));
    const subModal = new bootstrap.Modal(el('SubModal'));
    const modales = [modal, altaModal, eliminarModal, masivoModal, subModal];
    const subCambios = new Map();
    let subContexto = null, subController = null, subCargando = false, subPagina = 1, subTotalPaginas = 1;
    let subRegistros = [], subFiltros = { busqueda: '', estado: '' };
    let tipos = [], seleccionado = null, pagina = 1, totalPaginas = 1;
    let programaciones = [], altaContexto = null, eliminarSeleccionado = null, masivoContexto = null;
    let registrosVisibles = [], catalogoRegistros = [];
    let catalogoController = null, cargandoCatalogo = false, catalogoPagina = 1, catalogoTotalPaginas = 1, catalogoBusqueda = '';
    let planta = el('Planta').value, versionPlanta = 0;
    let filtros = { origen: '', busqueda: '', tipo: '' };
    let cargando = false, guardando = false, inicializando = false;
    let listadoController = null, programacionesController = null;

    const nombrePlanta = clave => Array.from(el('Planta').options).find(opcion => opcion.value === clave)?.textContent || clave;

    function mensaje(nodo, texto, nivel = 'danger') {
        nodo.textContent = texto || '';
        nodo.className = texto ? `alert alert-${nivel}` : 'alert d-none';
    }
    async function solicitar(url, opciones = {}) {
        const response = await fetch(url, { credentials: 'same-origin', ...opciones,
            headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest', ...opciones.headers } });
        const data = await response.json().catch(() => ({}));
        if (!response.ok) {
            if (response.status === 401 && data.redirectUrl) window.location.assign(data.redirectUrl);
            throw new Error(data.mensaje || data.message || 'No se pudo completar la operación. Intenta nuevamente.');
        }
        return data;
    }
    function opcionesTipo(select, primera) {
        select.replaceChildren(new Option(primera, ''));
        tipos.forEach(tipo => select.add(new Option(etiquetaTipo(tipo), tipo)));
    }
    function opcionesProgramacion(select, primera) {
        const previo = select.value;
        select.replaceChildren(new Option(primera, ''));
        programaciones.forEach(p => select.add(new Option(`${p.nombre.trim()} · ${p.proveedorId} (${p.registros})`, p.proveedorId)));
        select.value = programaciones.some(p => p.proveedorId === previo) ? previo : '';
    }
    function cambioPendiente() {
        return seleccionado && seleccionado.planta === planta && tipos.includes(el('TipoNuevo').value)
            && el('OrigenNuevo').value && (el('TipoNuevo').value !== seleccionado.tipo || el('OrigenNuevo').value !== seleccionado.origen);
    }
    function mostrarTipo(nodo, tipo) {
        const detalle = document.createElement('span'), insignia = document.createElement('span');
        detalle.className = 'activation-type-info'; insignia.className = 'activation-type';
        if (descripcionesTipo.has(tipo)) insignia.classList.add(`activation-type-${tipo}`);
        insignia.textContent = tipo || '—';
        const descripcion = document.createElement('span');
        descripcion.className = 'activation-type-description'; descripcion.textContent = descripcionTipo(tipo);
        detalle.append(insignia, descripcion); nodo.replaceChildren(detalle);
    }
    function actualizarDescripcionSeleccion() {
        const tipo = el('TipoNuevo').value;
        el('TipoNuevoDescripcion').textContent = tipo ? descripcionTipo(tipo) : '';
        el('TipoNuevoDescripcion').classList.toggle('d-none', !tipo);
    }
    function actualizarBotones() {
        const ocupado = inicializando || cargando || guardando;
        el('Anterior').disabled = ocupado || !tipos.length || pagina <= 1;
        el('Siguiente').disabled = ocupado || !tipos.length || pagina >= totalPaginas;
        el('Consultar').disabled = ocupado;
        el('Planta').disabled = guardando;
        el('Origen').disabled = inicializando || !tipos.length;
        el('Tipo').disabled = inicializando || !tipos.length;
        el('Busqueda').disabled = inicializando;
        el('Agregar').disabled = ocupado || !puedeEscribir || !programaciones.length;
        el('SubAbrir').classList.toggle('d-none', planta !== 'TIF');
        el('SubAbrir').disabled = guardando || planta !== 'TIF';
        el('Guardar').disabled = guardando || !cambioPendiente();
        el('OrigenNuevo').disabled = guardando; el('TipoNuevo').disabled = guardando;
        el('Guardar').textContent = guardando ? 'Guardando…' : 'Confirmar cambio';
        el('EliminarGuardar').disabled = guardando || !eliminarSeleccionado;
        el('EliminarGuardar').textContent = guardando ? 'Eliminando…' : 'Eliminar vínculo';
        root.querySelectorAll('.activation-row-actions button').forEach(boton => { boton.disabled = ocupado; });
        actualizarSeleccion(); actualizarMasivoBotones();
        actualizarAltaBotones();
        actualizarSubBotones();
    }
    function actualizarSeleccion() {
        const ocupado = inicializando || cargando || guardando;
        const validos = registrosVisibles.filter(r => r.articuloId?.trim());
        const marcados = validos.filter(r => seleccionados.has(llaveRegistro(r))).length;
        el('SeleccionResumen').textContent = `${seleccionados.size} ${seleccionados.size === 1 ? 'vínculo seleccionado' : 'vínculos seleccionados'} · máximo ${maximoLote}`;
        el('SeleccionPagina').checked = validos.length > 0 && marcados === validos.length;
        el('SeleccionPagina').indeterminate = marcados > 0 && marcados < validos.length;
        el('SeleccionPagina').disabled = ocupado || !puedeEscribir || !validos.length;
        el('SeleccionLimpiar').disabled = ocupado || !seleccionados.size;
        el('MasivoAbrir').disabled = ocupado || !puedeEscribir || !seleccionados.size;
        root.querySelectorAll('.activation-record-select input').forEach(check => {
            check.checked = seleccionados.has(check.dataset.llave);
            check.disabled = ocupado || (!check.checked && seleccionados.size >= maximoLote);
            check.closest('tr').dataset.selected = String(check.checked);
        });
    }
    el('SeleccionPagina').addEventListener('change', () => {
        if (!puedeEscribir || inicializando || cargando || guardando) return;
        const validos = registrosVisibles.filter(r => r.articuloId?.trim());
        const nuevos = validos.filter(r => !seleccionados.has(llaveRegistro(r)));
        if (el('SeleccionPagina').checked && seleccionados.size + nuevos.length > maximoLote)
            mensaje(el('Mensaje'), `Puedes seleccionar hasta ${maximoLote} vínculos por lote.`, 'warning');
        else validos.forEach(r => {
            if (el('SeleccionPagina').checked) {
                if (!seleccionados.has(llaveRegistro(r))) seleccionados.set(llaveRegistro(r), { ...r });
            } else seleccionados.delete(llaveRegistro(r));
        });
        actualizarSeleccion();
    });
    el('SeleccionLimpiar').addEventListener('click', () => {
        if (guardando || cargando || inicializando) return;
        seleccionados.clear(); actualizarSeleccion();
    });
    function actualizarMasivoBotones() {
        const tipo = el('MasivoTipo').value;
        const valido = masivoContexto?.planta === planta && masivoContexto.version === versionPlanta;
        const pendientes = masivoContexto?.registros.filter(r => r.tipo !== tipo).length || 0;
        el('MasivoTipo').disabled = guardando;
        el('MasivoGuardar').disabled = guardando || !valido || !tipos.includes(tipo) || !pendientes;
        el('MasivoGuardar').textContent = guardando ? 'Guardando…' : 'Confirmar cambio masivo';
        const sinCambios = masivoContexto ? masivoContexto.registros.length - pendientes : 0;
        el('MasivoTipoDescripcion').textContent = tipo ? `${descripcionTipo(tipo)}. ${pendientes} ${pendientes === 1 ? 'vínculo cambiará' : 'vínculos cambiarán'} de tipo; ${sinCambios} ya lo ${sinCambios === 1 ? 'tiene' : 'tienen'}.` : '';
        el('MasivoTipoDescripcion').classList.toggle('d-none', !tipo);
    }
    el('MasivoAbrir').addEventListener('click', () => {
        if (!puedeEscribir || inicializando || cargando || guardando || !seleccionados.size) return;
        masivoContexto = { planta, version: versionPlanta, registros: Array.from(seleccionados.values()) };
        mensaje(el('MasivoMensaje'), ''); el('MasivoPlanta').textContent = nombrePlanta(planta);
        const numeroProgramaciones = new Set(masivoContexto.registros.map(r => r.origen)).size;
        el('MasivoResumen').textContent = `${seleccionados.size} ${seleccionados.size === 1 ? 'vínculo' : 'vínculos'} en ${numeroProgramaciones} ${numeroProgramaciones === 1 ? 'programación' : 'programaciones'}. Revisa la selección y elige el tipo común.`;
        el('MasivoFilas').replaceChildren();
        masivoContexto.registros.forEach(r => {
            const item = document.createElement('li');
            item.textContent = `${r.articuloId} · ${r.programacion.trim()} (${r.origen}) · Alias: ${r.aliasExterno || '—'} · Tipo: ${r.tipo || 'Sin tipo'}`;
            el('MasivoFilas').append(item);
        });
        opcionesTipo(el('MasivoTipo'), 'Selecciona el tipo para todos');
        actualizarMasivoBotones(); masivoModal.show();
    });
    el('MasivoTipo').addEventListener('change', actualizarMasivoBotones);
    el('MasivoModal').addEventListener('hidden.bs.modal', () => { masivoContexto = null; });
    el('MasivoGuardar').addEventListener('click', async () => {
        if (!puedeEscribir || guardando || !masivoContexto || masivoContexto.planta !== planta
            || masivoContexto.version !== versionPlanta || !tipos.includes(el('MasivoTipo').value)) return;
        await mutar(root.dataset.cambiarMasivo, { planta: masivoContexto.planta, tipoNuevo: el('MasivoTipo').value,
            registros: masivoContexto.registros.map(r => ({ origen: r.origen, aliasExterno: r.aliasExterno,
                articuloId: r.articuloId, tipoAnterior: r.tipo })) }, masivoModal, el('MasivoMensaje'));
    });
    function filaEstado(texto) {
        registrosVisibles = [];
        const fila = document.createElement('tr'), celda = document.createElement('td');
        fila.setAttribute('role', 'row'); celda.setAttribute('role', 'cell');
        celda.colSpan = 6; celda.className = 'activation-empty'; celda.textContent = texto;
        fila.append(celda); el('Filas').replaceChildren(fila);
    }
    function celdaListado(columna, valor) {
        const celda = document.createElement('td'), etiqueta = document.createElement('span');
        const encabezado = el(`Col${columna}`);
        celda.className = `activation-cell-${columna.toLowerCase()}`;
        celda.setAttribute('role', 'cell'); celda.setAttribute('headers', encabezado.id);
        // La misma fila se presenta como tarjeta en espacios pequeños; conserva sus encabezados accesibles.
        etiqueta.className = 'activation-cell-label'; etiqueta.setAttribute('aria-hidden', 'true');
        etiqueta.textContent = encabezado.textContent;
        const contenido = document.createElement('span'); contenido.className = 'activation-cell-value';
        if (valor !== undefined) contenido.textContent = valor;
        celda.append(etiqueta, contenido);
        return { celda, contenido };
    }
    function dibujar(registros) {
        registrosVisibles = registros;
        const plantaRegistros = planta, version = versionPlanta;
        el('Filas').replaceChildren();
        if (!registros.length) { filaEstado('No se encontraron SKU con estos filtros.'); return; }
        registros.forEach(registro => {
            const fila = document.createElement('tr');
            fila.className = 'activation-record'; fila.setAttribute('role', 'row');
            const tieneSku = !!registro.articuloId?.trim();
            [['Sku', tieneSku ? registro.articuloId : 'Sin código'], ['Alias', registro.aliasExterno?.trim() ? registro.aliasExterno : '—'],
                ['Programacion', registro.programacion], ['Origen', registro.origen]].forEach(([columna, valor]) => {
                fila.append(celdaListado(columna, valor).celda);
            });
            if (puedeEscribir && tieneSku) {
                const contenido = fila.querySelector('.activation-cell-sku .activation-cell-value');
                const texto = document.createElement('span'); texto.textContent = contenido.textContent;
                const etiqueta = document.createElement('label'), check = document.createElement('input');
                etiqueta.className = 'activation-record-select'; check.type = 'checkbox';
                check.className = 'activation-selection-input'; check.dataset.llave = llaveRegistro(registro);
                check.setAttribute('aria-label', `Seleccionar SKU ${registro.articuloId} de ${registro.programacion}, alias ${registro.aliasExterno || 'vacío'}`);
                check.addEventListener('change', () => {
                    if (inicializando || cargando || guardando || plantaRegistros !== planta || version !== versionPlanta) return;
                    if (check.checked && seleccionados.size < maximoLote) seleccionados.set(llaveRegistro(registro), { ...registro });
                    else if (!check.checked) seleccionados.delete(llaveRegistro(registro));
                    actualizarSeleccion();
                });
                etiqueta.append(check); contenido.classList.add('activation-sku-line'); contenido.replaceChildren(etiqueta, texto);
            }
            const tipo = celdaListado('Tipo');
            mostrarTipo(tipo.contenido, registro.tipo); fila.append(tipo.celda);
            const accion = celdaListado('Acciones'), acciones = document.createElement('span');
            acciones.className = 'activation-row-actions';
            if (puedeEscribir && tieneSku) {
                const boton = document.createElement('button');
                boton.type = 'button'; boton.className = 'btn btn-outline-primary btn-sm'; boton.textContent = 'Editar';
                boton.setAttribute('aria-label', `Editar SKU ${registro.articuloId} en ${registro.programacion}`);
                boton.addEventListener('click', () => abrirCambio(registro, plantaRegistros, version)); acciones.append(boton);
            }
            if (puedeEliminar && tieneSku) {
                const boton = document.createElement('button');
                boton.type = 'button'; boton.className = 'btn btn-outline-danger btn-sm'; boton.textContent = 'Eliminar';
                boton.setAttribute('aria-label', `Eliminar SKU ${registro.articuloId} de ${registro.programacion}`);
                boton.addEventListener('click', () => abrirEliminacion(registro, plantaRegistros, version)); acciones.append(boton);
            }
            if (acciones.childElementCount) accion.contenido.append(acciones);
            else {
                accion.contenido.textContent = tieneSku ? 'Solo lectura' : 'Sin SKU'; accion.celda.classList.add('activation-readonly');
                if (!tieneSku) accion.celda.title = 'Este registro no tiene un ArticuloId válido para editar.';
            }
            fila.append(accion.celda); el('Filas').append(fila);
        });
    }
    async function cargarListado(nuevaPagina = 1) {
        const plantaListado = planta, version = versionPlanta;
        listadoController?.abort();
        const controller = new AbortController(); listadoController = controller;
        cargando = true; actualizarBotones(); filaEstado('Consultando SKU…');
        el('Tabla').setAttribute('aria-busy', 'true');
        try {
            const url = new URL(root.dataset.listado, window.location.origin);
            Object.entries({ ...filtros, planta: plantaListado, pagina: nuevaPagina }).forEach(([clave, valor]) => url.searchParams.set(clave, valor));
            const data = await solicitar(url, { signal: controller.signal });
            if (version !== versionPlanta || listadoController !== controller) return;
            totalPaginas = Math.max(1, Math.ceil(data.total / data.tamanoPagina));
            if (nuevaPagina > totalPaginas) { await cargarListado(totalPaginas); return; }
            pagina = data.pagina;
            tipos.forEach(tipo => {
                const conteo = data.conteos.find(item => item.tipo === tipo);
                el(`Conteo${tipo}`).textContent = (conteo?.registros || 0).toLocaleString('es-MX');
            });
            el('Total').textContent = `${data.total.toLocaleString('es-MX')} registros`;
            el('Pagina').textContent = `Página ${pagina} de ${totalPaginas}`;
            dibujar(data.registros);
        } catch (error) {
            if (error.name === 'AbortError' || version !== versionPlanta || listadoController !== controller) return;
            filaEstado('No fue posible consultar los SKU. Usa Consultar para intentar de nuevo.');
            el('Total').textContent = 'Consulta no disponible'; el('Pagina').textContent = 'Página —';
            tipos.forEach(tipo => { el(`Conteo${tipo}`).textContent = '—'; });
            pagina = 1; totalPaginas = 1; mensaje(el('Mensaje'), error.message);
        } finally {
            if (version === versionPlanta && listadoController === controller) {
                cargando = false; actualizarBotones(); el('Tabla').setAttribute('aria-busy', 'false');
            }
        }
    }
    function abrirCambio(registro, plantaRegistro, version) {
        if (!puedeEscribir || inicializando || cargando || guardando || plantaRegistro !== planta || version !== versionPlanta) return;
        seleccionado = { ...registro, planta: plantaRegistro }; mensaje(el('ModalMensaje'), '');
        el('DetalleSku').textContent = registro.articuloId;
        el('DetallePlanta').textContent = nombrePlanta(plantaRegistro);
        el('DetalleAlias').textContent = registro.aliasExterno || '—';
        el('DetalleProgramacion').textContent = registro.programacion;
        el('DetalleOrigen').textContent = registro.origen;
        mostrarTipo(el('DetalleTipo'), registro.tipo);
        opcionesProgramacion(el('OrigenNuevo'), 'Selecciona la programación');
        el('OrigenNuevo').value = registro.origen;
        opcionesTipo(el('TipoNuevo'), 'Selecciona el nuevo tipo');
        if (tipos.includes(registro.tipo)) el('TipoNuevo').value = registro.tipo;
        actualizarDescripcionSeleccion();
        el('Guardar').disabled = true; modal.show();
    }
    el('TipoNuevo').addEventListener('change', () => {
        actualizarDescripcionSeleccion();
        actualizarBotones();
    });
    el('OrigenNuevo').addEventListener('change', actualizarBotones);
    ['Modal', 'AltaModal', 'EliminarModal', 'MasivoModal', 'SubModal'].forEach(id => {
        el(id).addEventListener('hide.bs.modal', event => { if (guardando) event.preventDefault(); });
    });
    el('Modal').addEventListener('hidden.bs.modal', () => { seleccionado = null; });
    el('Guardar').addEventListener('click', async () => {
        if (!puedeEscribir || guardando || !cambioPendiente()) return;
        const registro = seleccionado;
        await mutar(root.dataset.cambiar, { planta: registro.planta, origen: registro.origen,
            origenNuevo: el('OrigenNuevo').value, aliasExterno: registro.aliasExterno,
            articuloId: registro.articuloId, tipoAnterior: registro.tipo, tipoNuevo: el('TipoNuevo').value },
            modal, el('ModalMensaje'));
    });
    el('Filtros').addEventListener('submit', async event => {
        event.preventDefault(); mensaje(el('Mensaje'), '');
        if (inicializando || cargando || guardando) return;
        if (!tipos.length) { await iniciar(); return; }
        seleccionados.clear();
        filtros = { origen: el('Origen').value, busqueda: el('Busqueda').value.trim(), tipo: el('Tipo').value };
        await cargarListado();
    });
    el('Anterior').addEventListener('click', () => cargarListado(pagina - 1));
    el('Siguiente').addEventListener('click', () => cargarListado(pagina + 1));
    el('Planta').addEventListener('change', async () => {
        if (guardando) { el('Planta').value = planta; return; }
        planta = el('Planta').value;
        await iniciar();
    });
    async function iniciar() {
        const version = ++versionPlanta, plantaConsulta = planta;
        programacionesController?.abort(); listadoController?.abort(); listadoController = null;
        const controller = new AbortController(); programacionesController = controller;
        inicializando = true; cargando = false; tipos = []; programaciones = []; pagina = 1; totalPaginas = 1;
        seleccionado = null; eliminarSeleccionado = null; altaContexto = null; masivoContexto = null;
        seleccionados.clear(); limpiarSeleccionAlta();
        limpiarSubContexto();
        catalogoController?.abort(); catalogoController = null; cargandoCatalogo = false;
        modales.forEach(ventana => ventana.hide()); el('Guardar').disabled = true;
        filtros = { origen: '', busqueda: '', tipo: '' };
        el('Busqueda').value = ''; opcionesTipo(el('Tipo'), 'Todos');
        el('Origen').replaceChildren(new Option('Cargando programaciones…', ''));
        el('PlantaActual').textContent = nombrePlanta(plantaConsulta);
        el('PlantaEncabezado').textContent = nombrePlanta(plantaConsulta);
        root.dataset.planta = plantaConsulta;
        ['Modal', 'AltaModal', 'EliminarModal', 'MasivoModal', 'SubModal'].forEach(id => { el(id).dataset.planta = plantaConsulta; });
        el('Total').textContent = 'Cargando…'; el('Pagina').textContent = 'Página —';
        ['P', 'F', 'C', 'S'].forEach(tipo => { el(`Conteo${tipo}`).textContent = '—'; });
        mensaje(el('Mensaje'), ''); filaEstado('Cargando programaciones y SKU…');
        el('Tabla').setAttribute('aria-busy', 'true'); actualizarBotones();
        try {
            const url = new URL(root.dataset.programaciones, window.location.origin);
            url.searchParams.set('planta', plantaConsulta);
            const data = await solicitar(url, { signal: controller.signal });
            if (version !== versionPlanta) return;
            tipos = data.tipos; opcionesTipo(el('Tipo'), 'Todos');
            programaciones = data.programaciones; opcionesProgramacion(el('Origen'), 'Todas las programaciones');
            await cargarListado();
        } catch (error) {
            if (error.name === 'AbortError' || version !== versionPlanta) return;
            mensaje(el('Mensaje'), error.message);
            filaEstado('No fue posible cargar las programaciones. Usa Reintentar.');
            el('Total').textContent = 'Consulta no disponible';
        } finally {
            if (version === versionPlanta) {
                inicializando = false; actualizarBotones();
                el('ConsultarTexto').textContent = tipos.length ? 'Consultar' : 'Reintentar';
                if (!cargando) el('Tabla').setAttribute('aria-busy', 'false');
            }
        }
    }
    async function mutar(url, datos, ventana, nodoMensaje, origenPreferido = null) {
        if (guardando || datos.planta !== planta) return;
        guardando = true; actualizarBotones(); mensaje(nodoMensaje, '');
        try {
            const data = await solicitar(url, { method: 'POST',
                headers: { 'Content-Type': 'application/json', RequestVerificationToken: el('Modal').querySelector('[name="__RequestVerificationToken"]').value },
                body: JSON.stringify(datos) });
            guardando = false; ventana.hide(); guardando = true;
            const registro = data.registro;
            seleccionados.clear();
            const detalle = data.resultado
                ? `${data.resultado.modificados} ${data.resultado.modificados === 1 ? 'vínculo guardado' : 'vínculos guardados'}${data.resultado.sinCambios ? `; ${data.resultado.sinCambios} ya ${data.resultado.sinCambios === 1 ? 'tenía' : 'tenían'} el tipo elegido` : ''}.`
                : `SKU ${registro.articuloId}, ${registro.programacion.trim()} (${registro.origen}).`;
            mensaje(el('Mensaje'), `${nombrePlanta(datos.planta)}: ${data.mensaje} ${detalle}`, 'success');
            let errorProgramaciones = false;
            try {
                const programasUrl = new URL(root.dataset.programaciones, window.location.origin);
                programasUrl.searchParams.set('planta', datos.planta);
                const programas = await solicitar(programasUrl);
                programaciones = programas.programaciones;
                opcionesProgramacion(el('Origen'), 'Todas las programaciones');
            } catch { errorProgramaciones = true; }
            if (origenPreferido) {
                filtros = { origen: origenPreferido, busqueda: '', tipo: '' };
                el('Origen').value = origenPreferido; el('Busqueda').value = ''; el('Tipo').value = '';
            }
            await cargarListado(origenPreferido ? 1 : pagina);
            if (errorProgramaciones)
                mensaje(el('Mensaje'), 'El cambio quedó guardado. Recarga la página para actualizar las programaciones y sus conteos.', 'warning');
        } catch (error) { mensaje(nodoMensaje, error.message); }
        finally { guardando = false; actualizarBotones(); }
    }

    function abrirEliminacion(registro, plantaRegistro, version) {
        if (!puedeEliminar || inicializando || cargando || guardando || plantaRegistro !== planta || version !== versionPlanta) return;
        eliminarSeleccionado = { ...registro, planta: plantaRegistro };
        mensaje(el('EliminarMensaje'), '');
        el('EliminarSku').textContent = registro.articuloId;
        el('EliminarPlanta').textContent = nombrePlanta(plantaRegistro);
        el('EliminarProgramacion').textContent = registro.programacion;
        el('EliminarOrigen').textContent = registro.origen;
        el('EliminarAlias').textContent = registro.aliasExterno || '—';
        mostrarTipo(el('EliminarTipo'), registro.tipo);
        actualizarBotones(); eliminarModal.show();
    }
    el('EliminarModal').addEventListener('hidden.bs.modal', () => { eliminarSeleccionado = null; });
    el('EliminarGuardar').addEventListener('click', async () => {
        if (!puedeEliminar || guardando || !eliminarSeleccionado || eliminarSeleccionado.planta !== planta) return;
        const registro = eliminarSeleccionado;
        await mutar(root.dataset.eliminar, { planta: registro.planta, origen: registro.origen,
            aliasExterno: registro.aliasExterno, articuloId: registro.articuloId, tipoAnterior: registro.tipo },
            eliminarModal, el('EliminarMensaje'));
    });

    function actualizarAltaBotones() {
        const valido = altaContexto && altaContexto.planta === planta && altaContexto.version === versionPlanta;
        const origen = el('AltaOrigen').value;
        el('AltaOrigen').disabled = guardando; el('AltaTipo').disabled = guardando;
        el('CatalogoBusqueda').disabled = guardando || !origen;
        el('CatalogoConsultar').disabled = guardando || cargandoCatalogo || !valido || !origen;
        el('CatalogoAnterior').disabled = guardando || cargandoCatalogo || !valido || catalogoPagina <= 1;
        el('CatalogoSiguiente').disabled = guardando || cargandoCatalogo || !valido || catalogoPagina >= catalogoTotalPaginas;
        el('AltaGuardar').disabled = guardando || cargandoCatalogo || !valido || !origen || !altasSeleccionadas.size
            || Array.from(altasSeleccionadas.values()).some(r => r.origen !== origen) || !tipos.includes(el('AltaTipo').value);
        el('AltaGuardar').textContent = guardando ? 'Agregando…' : altasSeleccionadas.size ? `Agregar ${altasSeleccionadas.size} SKU` : 'Agregar a programación';
        const marcados = catalogoRegistros.filter(r => altasSeleccionadas.has(r.articuloId)).length;
        el('CatalogoSeleccionPagina').checked = catalogoRegistros.length > 0 && marcados === catalogoRegistros.length;
        el('CatalogoSeleccionPagina').indeterminate = marcados > 0 && marcados < catalogoRegistros.length;
        el('CatalogoSeleccionPagina').disabled = guardando || cargandoCatalogo || !valido || !catalogoRegistros.length;
        el('CatalogoLimpiar').disabled = guardando || cargandoCatalogo || !altasSeleccionadas.size;
        el('CatalogoFilas').querySelectorAll('input[type="checkbox"]').forEach(check => {
            check.checked = altasSeleccionadas.has(check.dataset.articuloId);
            check.disabled = guardando || cargandoCatalogo || (!check.checked && altasSeleccionadas.size >= maximoLote);
            check.closest('tr').dataset.selected = String(check.checked);
        });
        el('AltaSeleccionFilas').querySelectorAll('button').forEach(boton => { boton.disabled = guardando || cargandoCatalogo; });
    }
    function estadoCatalogo(texto) {
        catalogoRegistros = [];
        const fila = document.createElement('tr'), celda = document.createElement('td');
        celda.colSpan = 3; celda.className = 'activation-catalog-empty'; celda.textContent = texto;
        fila.append(celda); el('CatalogoFilas').replaceChildren(fila);
    }
    function limpiarSeleccionAlta() {
        altasSeleccionadas.clear(); actualizarSeleccionAlta();
    }
    function actualizarSeleccionAlta() {
        el('AltaSeleccionResumen').textContent = `${altasSeleccionadas.size} SKU seleccionados · máximo ${maximoLote}`;
        el('AltaSeleccionFilas').replaceChildren();
        altasSeleccionadas.forEach(articulo => {
            const item = document.createElement('li'), texto = document.createElement('span'), quitar = document.createElement('button');
            texto.textContent = `${articulo.articuloId} · ${articulo.descripcion || 'Sin descripción'}`;
            quitar.type = 'button'; quitar.className = 'btn btn-outline-secondary btn-sm'; quitar.textContent = 'Quitar';
            quitar.setAttribute('aria-label', `Quitar SKU ${articulo.articuloId} de la selección`);
            quitar.addEventListener('click', () => {
                if (guardando || cargandoCatalogo) return;
                altasSeleccionadas.delete(articulo.articuloId); actualizarSeleccionAlta(); actualizarAltaBotones();
            });
            item.append(texto, quitar); el('AltaSeleccionFilas').append(item);
        });
    }
    function dibujarCatalogo(registros, origen) {
        catalogoRegistros = registros;
        el('CatalogoFilas').replaceChildren();
        if (!registros.length) { estadoCatalogo('No hay SKU disponibles con esta búsqueda para la programación seleccionada.'); return; }
        registros.forEach(articulo => {
            const fila = document.createElement('tr');
            [articulo.articuloId, articulo.descripcion || 'Sin descripción'].forEach(valor => {
                const celda = document.createElement('td'); celda.textContent = valor; fila.append(celda);
            });
            const accion = document.createElement('td'), etiqueta = document.createElement('label'), check = document.createElement('input');
            etiqueta.className = 'activation-record-select'; check.type = 'checkbox';
            check.className = 'activation-selection-input'; check.dataset.articuloId = articulo.articuloId;
            check.setAttribute('aria-label', `Seleccionar SKU ${articulo.articuloId} del catálogo`);
            check.addEventListener('change', () => {
                if (guardando || cargandoCatalogo || !altaContexto || altaContexto.planta !== planta || origen !== el('AltaOrigen').value) return;
                if (check.checked && altasSeleccionadas.size < maximoLote) altasSeleccionadas.set(articulo.articuloId, { ...articulo, origen });
                else if (!check.checked) altasSeleccionadas.delete(articulo.articuloId);
                actualizarSeleccionAlta(); actualizarAltaBotones();
            });
            etiqueta.append(check); accion.append(etiqueta); fila.append(accion); el('CatalogoFilas').append(fila);
        });
    }
    el('CatalogoSeleccionPagina').addEventListener('change', () => {
        if (guardando || cargandoCatalogo || !altaContexto || altaContexto.planta !== planta) return;
        const nuevos = catalogoRegistros.filter(r => !altasSeleccionadas.has(r.articuloId));
        if (el('CatalogoSeleccionPagina').checked && altasSeleccionadas.size + nuevos.length > maximoLote)
            mensaje(el('AltaMensaje'), `Puedes seleccionar hasta ${maximoLote} SKU por lote.`, 'warning');
        else catalogoRegistros.forEach(r => {
            if (el('CatalogoSeleccionPagina').checked) altasSeleccionadas.set(r.articuloId, { ...r, origen: el('AltaOrigen').value });
            else altasSeleccionadas.delete(r.articuloId);
        });
        actualizarSeleccionAlta(); actualizarAltaBotones();
    });
    el('CatalogoLimpiar').addEventListener('click', () => {
        if (guardando || cargandoCatalogo) return;
        limpiarSeleccionAlta(); actualizarAltaBotones();
    });
    async function cargarCatalogo(nuevaPagina = 1) {
        catalogoController?.abort();
        const contexto = altaContexto, origen = el('AltaOrigen').value;
        if (!contexto || !origen) {
            catalogoController = null; cargandoCatalogo = false;
            estadoCatalogo('Selecciona una programación para consultar el catálogo.'); actualizarAltaBotones(); return;
        }
        const controller = new AbortController(); catalogoController = controller;
        cargandoCatalogo = true; mensaje(el('AltaMensaje'), ''); estadoCatalogo('Buscando SKU disponibles…'); actualizarAltaBotones();
        try {
            const url = new URL(root.dataset.disponibles, window.location.origin);
            Object.entries({ planta: contexto.planta, origen, busqueda: catalogoBusqueda, pagina: nuevaPagina })
                .forEach(([clave, valor]) => url.searchParams.set(clave, valor));
            const data = await solicitar(url, { signal: controller.signal });
            if (catalogoController !== controller || altaContexto !== contexto || contexto.version !== versionPlanta || origen !== el('AltaOrigen').value) return;
            catalogoPagina = data.pagina; catalogoTotalPaginas = Math.max(1, Math.ceil(data.total / data.tamanoPagina));
            el('CatalogoPagina').textContent = `${data.total.toLocaleString('es-MX')} disponibles · Página ${catalogoPagina} de ${catalogoTotalPaginas}`;
            dibujarCatalogo(data.registros, origen);
        } catch (error) {
            if (error.name === 'AbortError' || catalogoController !== controller || altaContexto !== contexto) return;
            estadoCatalogo('No fue posible consultar el catálogo. Usa Buscar para reintentar.');
            catalogoPagina = 1; catalogoTotalPaginas = 1; el('CatalogoPagina').textContent = '';
            mensaje(el('AltaMensaje'), error.message);
        } finally {
            if (catalogoController === controller) { cargandoCatalogo = false; actualizarAltaBotones(); }
        }
    }
    el('Agregar').addEventListener('click', async () => {
        if (!puedeEscribir || guardando || inicializando || cargando || !programaciones.length) return;
        altaContexto = { planta, version: versionPlanta }; limpiarSeleccionAlta();
        mensaje(el('AltaMensaje'), ''); el('AltaPlanta').textContent = nombrePlanta(planta);
        opcionesProgramacion(el('AltaOrigen'), 'Selecciona la programación');
        el('AltaOrigen').value = el('Origen').value;
        opcionesTipo(el('AltaTipo'), 'Selecciona el tipo inicial');
        el('AltaTipoDescripcion').textContent = ''; el('AltaTipoDescripcion').classList.add('d-none');
        el('CatalogoBusqueda').value = ''; catalogoBusqueda = ''; catalogoPagina = 1; catalogoTotalPaginas = 1;
        el('CatalogoPagina').textContent = ''; estadoCatalogo('Selecciona una programación para consultar el catálogo.');
        actualizarAltaBotones(); altaModal.show();
        if (el('AltaOrigen').value) await cargarCatalogo();
    });
    el('AltaModal').addEventListener('hidden.bs.modal', () => {
        altaContexto = null; limpiarSeleccionAlta(); catalogoController?.abort(); catalogoController = null; cargandoCatalogo = false;
    });
    el('AltaOrigen').addEventListener('change', async () => {
        limpiarSeleccionAlta(); catalogoBusqueda = ''; el('CatalogoBusqueda').value = '';
        catalogoPagina = 1; catalogoTotalPaginas = 1; el('CatalogoPagina').textContent = '';
        await cargarCatalogo(); actualizarAltaBotones();
    });
    el('AltaTipo').addEventListener('change', () => {
        const tipo = el('AltaTipo').value;
        el('AltaTipoDescripcion').textContent = tipo ? descripcionTipo(tipo) : '';
        el('AltaTipoDescripcion').classList.toggle('d-none', !tipo); actualizarAltaBotones();
    });
    el('CatalogoFiltros').addEventListener('submit', async event => {
        event.preventDefault(); if (guardando) return;
        catalogoBusqueda = el('CatalogoBusqueda').value.trim(); await cargarCatalogo();
    });
    el('CatalogoAnterior').addEventListener('click', () => cargarCatalogo(catalogoPagina - 1));
    el('CatalogoSiguiente').addEventListener('click', () => cargarCatalogo(catalogoPagina + 1));
    el('AltaGuardar').addEventListener('click', async () => {
        if (!puedeEscribir || guardando || cargandoCatalogo || !altaContexto || altaContexto.planta !== planta
            || altaContexto.version !== versionPlanta || !altasSeleccionadas.size
            || Array.from(altasSeleccionadas.values()).some(r => r.origen !== el('AltaOrigen').value)
            || !tipos.includes(el('AltaTipo').value)) return;
        await mutar(root.dataset.agregar, { planta: altaContexto.planta, origen: el('AltaOrigen').value,
            articulos: Array.from(altasSeleccionadas.keys()), tipo: el('AltaTipo').value }, altaModal, el('AltaMensaje'), el('AltaOrigen').value);
    });
    function limpiarSubContexto() {
        subController?.abort(); subController = null; subContexto = null; subCargando = false;
        subCambios.clear(); subRegistros = []; subPagina = 1; subTotalPaginas = 1;
        subFiltros = { busqueda: '', estado: '' };
        actualizarCambiosSub();
    }
    function actualizarSubBotones() {
        const valido = subContexto?.planta === planta && subContexto.version === versionPlanta;
        const ocupado = guardando || subCargando || !valido;
        const escribir = puedeEscribirSub();
        el('SubGuardar').classList.toggle('d-none', !escribir);
        el('SubDescartar').classList.toggle('d-none', !escribir);
        el('SubLectura').classList.toggle('d-none', !subContexto || escribir);
        el('SubLectura').textContent = 'Tu acceso a Subclasificación es de solo lectura.';
        el('SubNotaCambios').classList.toggle('d-none', !escribir);
        el('SubAyuda').textContent = `Consulta todos los artículos de esta planta. ${escribir ? 'Marca para agregar N o desmarca para quitarla. ' : ''}Aplica al SKU en todas sus programaciones de esta planta. Los demás valores de subclasificación se conservan.`;
        ['SubBusqueda', 'SubEstado', 'SubConsultar'].forEach(id => { el(id).disabled = ocupado; });
        el('SubAnterior').disabled = ocupado || subPagina <= 1;
        el('SubSiguiente').disabled = ocupado || subPagina >= subTotalPaginas;
        el('SubDescartar').disabled = ocupado || !subCambios.size;
        el('SubGuardar').disabled = ocupado || !escribir || !subCambios.size;
        el('SubGuardar').textContent = guardando ? 'Guardando…' : subCambios.size
            ? `Guardar ${subCambios.size} ${subCambios.size === 1 ? 'cambio' : 'cambios'}` : 'Guardar cambios';
        el('SubFilas').querySelectorAll('input[type="checkbox"]').forEach(check => {
            const registro = subRegistros.find(r => r.articuloId === check.dataset.articuloId);
            check.disabled = ocupado || !escribir || !registro?.puedeCambiar
                || (!subCambios.has(check.dataset.articuloId) && subCambios.size >= maximoLote);
        });
        el('SubCambios').querySelectorAll('button').forEach(boton => { boton.disabled = ocupado; });
    }
    function puedeEscribirSub() { return puedeEscribir && subContexto?.planta === 'TIF' && !subContexto.soloConsulta; }
    function actualizarCambiosSub() {
        const agregar = Array.from(subCambios.values()).filter(c => c.asignarN).length;
        el('SubPendientes').textContent = subCambios.size
            ? `${subCambios.size} ${subCambios.size === 1 ? 'pendiente' : 'pendientes'} · ${agregar} agregar N · ${subCambios.size - agregar} quitar N` : puedeEscribirSub() ? 'Sin cambios pendientes' : 'Solo consulta';
        el('SubRevision').classList.toggle('d-none', !subCambios.size);
        el('SubCambios').replaceChildren();
        subCambios.forEach(cambio => {
            const item = document.createElement('li'), texto = document.createElement('span'), deshacer = document.createElement('button');
            texto.textContent = `${cambio.articuloId} · ${cambio.nombre || 'Sin nombre'} · ${cambio.asignarN ? 'Agregar N' : 'Quitar N'}`;
            deshacer.type = 'button'; deshacer.className = 'btn btn-outline-secondary btn-sm'; deshacer.textContent = 'Deshacer';
            deshacer.setAttribute('aria-label', `Deshacer cambio del SKU ${cambio.articuloId}`);
            deshacer.addEventListener('click', () => {
                if (guardando || subCargando) return;
                subCambios.delete(cambio.articuloId); actualizarCambiosSub(); actualizarSubBotones();
            });
            item.append(texto, deshacer); el('SubCambios').append(item);
        });
        el('SubFilas').querySelectorAll('input[type="checkbox"]').forEach(check => {
            const registro = subRegistros.find(r => r.articuloId === check.dataset.articuloId);
            const pendiente = subCambios.get(check.dataset.articuloId);
            check.checked = pendiente?.asignarN ?? registro?.tieneN ?? false;
            const fila = check.closest('li'); fila.dataset.pending = String(!!pendiente);
            fila.querySelector('[data-sub-status]').textContent = pendiente ? `${pendiente.asignarN ? 'Agregar N' : 'Quitar N'} pendiente` : '';
        });
    }
    function estadoSub(texto) {
        subRegistros = [];
        const item = document.createElement('li'); item.className = 'activation-sub-empty'; item.textContent = texto;
        el('SubFilas').replaceChildren(item);
    }
    function dibujarSub(registros, contexto) {
        subRegistros = registros; el('SubFilas').replaceChildren();
        if (!registros.length) { estadoSub('No se encontraron artículos con estos filtros.'); return; }
        registros.forEach(registro => {
            const fila = document.createElement('li'), producto = document.createElement('div');
            producto.className = 'activation-sub-product';
            const codigo = document.createElement('strong'), nombre = document.createElement('span');
            codigo.textContent = registro.articuloId?.trim() ? registro.articuloId : 'Sin código';
            nombre.textContent = registro.nombre || 'Sin nombre'; producto.append(codigo, nombre);
            const actual = document.createElement('div'), estado = document.createElement('small');
            actual.className = 'activation-sub-current'; estado.dataset.subStatus = '';
            if (registro.tieneN) {
                const letra = document.createElement('span'), texto = document.createElement('span');
                letra.className = 'activation-type activation-sub-n'; letra.textContent = 'N';
                texto.textContent = 'Solo aparece en Empaque'; actual.append(letra, texto);
            } else if (registro.clasifica2?.replace(/ /g, '')) {
                actual.textContent = `Otro valor: ${registro.clasifica2} · Se conserva`;
            } else actual.textContent = 'Sin subclasificación';
            actual.append(estado);
            const etiqueta = document.createElement('label'), check = document.createElement('input'), texto = document.createElement('span');
            etiqueta.className = 'activation-sub-option'; check.type = 'checkbox'; check.className = 'activation-selection-input';
            check.dataset.articuloId = registro.articuloId; texto.textContent = 'Solo Empaque (N)';
            check.setAttribute('aria-label', `Solo Empaque (N) para SKU ${registro.articuloId}`);
            if (!registro.puedeCambiar) etiqueta.title = 'Este valor se conserva y no puede cambiarse desde Subclasificación.';
            check.addEventListener('change', () => {
                if (guardando || subCargando || !puedeEscribirSub() || !registro.puedeCambiar || subContexto !== contexto
                    || contexto.planta !== planta || contexto.version !== versionPlanta) return;
                if (check.checked === registro.tieneN) subCambios.delete(registro.articuloId);
                else if (subCambios.has(registro.articuloId) || subCambios.size < maximoLote)
                    subCambios.set(registro.articuloId, { articuloId: registro.articuloId, nombre: registro.nombre,
                        clasifica2Anterior: registro.clasifica2, asignarN: check.checked });
                else mensaje(el('SubMensaje'), `Puedes guardar hasta ${maximoLote} cambios por lote.`, 'warning');
                actualizarCambiosSub(); actualizarSubBotones();
            });
            etiqueta.append(check, texto); fila.append(producto, actual, etiqueta); el('SubFilas').append(fila);
        });
        actualizarCambiosSub();
    }
    async function cargarSubListado(nuevaPagina = 1, despuesDeGuardar = false) {
        const contexto = subContexto;
        if (!contexto || contexto.planta !== planta || contexto.version !== versionPlanta) return;
        subController?.abort(); const controller = new AbortController(); subController = controller;
        subCargando = true; estadoSub('Consultando artículos…'); actualizarSubBotones();
        el('SubFilas').setAttribute('aria-busy', 'true');
        try {
            const url = new URL(root.dataset.subclasificaciones, window.location.origin);
            Object.entries({ planta: contexto.planta, ...subFiltros, pagina: nuevaPagina }).forEach(([k, v]) => url.searchParams.set(k, v));
            const data = await solicitar(url, { signal: controller.signal });
            if (subContexto !== contexto || subController !== controller || contexto.version !== versionPlanta) return;
            contexto.soloConsulta = data.soloConsulta;
            subTotalPaginas = Math.max(1, Math.ceil(data.total / data.tamanoPagina));
            if (nuevaPagina > subTotalPaginas) { await cargarSubListado(subTotalPaginas, despuesDeGuardar); return; }
            subPagina = data.pagina;
            el('SubPagina').textContent = `${data.total.toLocaleString('es-MX')} SKU · Página ${subPagina} de ${subTotalPaginas}`;
            dibujarSub(data.registros, contexto);
        } catch (error) {
            if (error.name === 'AbortError' || subContexto !== contexto || subController !== controller) return;
            estadoSub('No fue posible consultar los artículos. Usa Buscar para reintentar.');
            subPagina = 1; subTotalPaginas = 1; el('SubPagina').textContent = 'Consulta no disponible';
            mensaje(el('SubMensaje'), `${despuesDeGuardar ? 'Los cambios se guardaron, pero no se pudo recargar el listado. ' : ''}${error.message}`,
                despuesDeGuardar ? 'warning' : 'danger');
        } finally {
            if (subContexto === contexto && subController === controller) {
                subCargando = false; actualizarSubBotones(); el('SubFilas').setAttribute('aria-busy', 'false');
            }
        }
    }
    el('SubAbrir').addEventListener('click', async () => {
        if (guardando || planta !== 'TIF') return;
        limpiarSubContexto(); subContexto = { planta, version: versionPlanta };
        el('SubModal').dataset.planta = planta; el('SubPlanta').textContent = nombrePlanta(planta);
        el('SubBusqueda').value = ''; el('SubEstado').value = ''; el('SubPagina').textContent = 'Cargando…';
        el('SubRevision').open = false; mensaje(el('SubMensaje'), '');
        subModal.show(); await cargarSubListado();
    });
    el('SubModal').addEventListener('hidden.bs.modal', () => { limpiarSubContexto(); actualizarSubBotones(); });
    el('SubFiltros').addEventListener('submit', async event => {
        event.preventDefault();
        if (guardando || subCargando || !subContexto) return;
        subFiltros = { busqueda: el('SubBusqueda').value.trim(), estado: el('SubEstado').value };
        mensaje(el('SubMensaje'), ''); await cargarSubListado();
    });
    el('SubAnterior').addEventListener('click', () => cargarSubListado(subPagina - 1));
    el('SubSiguiente').addEventListener('click', () => cargarSubListado(subPagina + 1));
    el('SubDescartar').addEventListener('click', async () => {
        if (guardando || subCargando || !subContexto) return;
        subCambios.clear(); actualizarCambiosSub(); mensaje(el('SubMensaje'), ''); await cargarSubListado(subPagina);
    });
    el('SubGuardar').addEventListener('click', async () => {
        const contexto = subContexto;
        if (!puedeEscribirSub() || guardando || subCargando || !contexto || contexto.planta !== planta
            || contexto.version !== versionPlanta || !subCambios.size) return;
        const cambios = Array.from(subCambios.values(), ({ articuloId, clasifica2Anterior, asignarN }) => ({ articuloId, clasifica2Anterior, asignarN }));
        guardando = true; actualizarBotones(); mensaje(el('SubMensaje'), '');
        try {
            const data = await solicitar(root.dataset.guardarSubclasificacion, { method: 'POST',
                headers: { 'Content-Type': 'application/json', RequestVerificationToken: el('Modal').querySelector('[name="__RequestVerificationToken"]').value },
                body: JSON.stringify({ planta: contexto.planta, cambios }) });
            subCambios.clear(); actualizarCambiosSub();
            mensaje(el('SubMensaje'), `${nombrePlanta(contexto.planta)}: ${data.mensaje} ${data.resultado.modificados} SKU actualizados.`, 'success');
            await cargarSubListado(subPagina, true);
        } catch (error) { mensaje(el('SubMensaje'), error.message); }
        finally { guardando = false; actualizarBotones(); }
    });
    iniciar();
})();
