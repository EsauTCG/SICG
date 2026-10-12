(() => {
    'use strict';
    const root = document.getElementById('skuCatalogo');
    if (!root) return;
    const el = id => document.getElementById(id);
    const form = el('skuFormulario');
    const token = form.querySelector('input[name="__RequestVerificationToken"]').value;
    const editor = new bootstrap.Modal(el('skuEditor'));
    const estadoModal = new bootstrap.Modal(el('skuEstadoModal'));
    const historialModal = new bootstrap.Modal(el('skuHistorialModal'));
    let pagina = 1, paginas = 1, listadoRequest = 0, registros = new Map(), editando = null, cambiando = null, historialRequest = 0;
    const fecha = value => value ? new Date(value).toLocaleString('es-MX', {dateStyle:'short', timeStyle:'short'}) : '—';
    const element = (tag, text = '', className = '') => { const node = document.createElement(tag); node.textContent = text; node.className = className; return node; };
    const aviso = (id, texto, tipo = 'danger') => { const node = el(id); node.textContent = texto; node.className = `alert alert-${tipo}${texto ? '' : ' d-none'}`; };
    const busy = (node, value) => { node.setAttribute('aria-busy', String(value)); node.querySelectorAll('input,select,textarea,button').forEach(input => input.disabled = value); };
    const queryUrl = (url, args) => { const result = new URL(url, location.origin); for (const [k,v] of Object.entries(args)) result.searchParams.set(k, v); return result; };

    async function request(url, payload, signal) {
        const response = await fetch(url, { method:payload === undefined ? 'GET' : 'POST', credentials:'same-origin', signal,
            headers:{ 'X-Requested-With':'XMLHttpRequest', ...(payload === undefined ? {} : {'Content-Type':'application/json', 'RequestVerificationToken':token}) },
            ...(payload === undefined ? {} : {body:JSON.stringify(payload)}) });
        if (response.status === 401 || response.status === 403 || response.redirected) throw new Error('Tu sesión no está disponible. Recarga la página e inicia sesión.');
        const data = await response.json().catch(() => { throw new Error('No se pudo leer la respuesta del sistema. Recarga e intenta nuevamente.'); });
        if (!response.ok) throw new Error(data.mensaje || 'No se pudo completar la operación.');
        return data;
    }

    function decimal(value) {
        if (value === null || value === undefined || value === '') return '—';
        const [integer, fraction] = String(value).split('.');
        return integer.replace(/\B(?=(\d{3})+(?!\d))/g, ',') + (fraction ? '.' + fraction : '');
    }

    function row(record) {
        const tr = document.createElement('tr');
        for (const [sku, nombre] of [[record.skuOrigen, record.nombreOrigen], [record.skuDestino, record.nombreDestino]]) {
            const cell = element('td', '', 'sku-product'); cell.append(element('span', sku, 'sku-code'), element('span', nombre || 'Sin nombre en el catálogo', 'sku-name')); tr.append(cell);
        }
        tr.append(element('td', decimal(record.factorTexto), 'sku-number'), element('td', record.prioridad === null ? 'Base' : String(record.prioridad), 'sku-number'));
        const state = element('td'); state.append(element('span', record.activo ? 'Activa' : 'Inactiva', 'sku-status ' + (record.activo ? 'sku-active' : 'sku-inactive'))); tr.append(state);
        const alta = element('td', '', 'sku-author'); alta.append(element('span', record.usuarioAlta || 'Sin usuario registrado'), element('span', fecha(record.fechaAlta), 'sku-subtext')); tr.append(alta);
        const cambio = element('td', '', 'sku-author'); cambio.append(element('span', record.usuarioUltimoCambio || 'Sin cambios registrados'), element('span', fecha(record.updatedAt), 'sku-subtext')); tr.append(cambio);
        const actions = element('td'), group = element('div', '', 'sku-row-actions');
        for (const [action, label, icon] of [['editar','Editar','pencil'], ['estado', record.activo ? 'Desactivar' : 'Activar', record.activo ? 'pause-circle' : 'play-circle'], ['historial','Historial','clock-history']]) {
            const button = element('button', '', 'btn btn-outline-secondary'); button.type='button'; button.dataset.action=action; button.dataset.id=record.id;
            button.title=`${label} ${record.skuOrigen} → ${record.skuDestino}`; button.setAttribute('aria-label', button.title);
            const i = element('i', '', `bi bi-${icon}`); i.setAttribute('aria-hidden','true'); button.append(i); group.append(button);
        }
        actions.append(group); tr.append(actions); return tr;
    }

    async function cargar() {
        const current = ++listadoRequest, tbody = el('skuRegistros');
        tbody.setAttribute('aria-busy', 'true'); el('skuAnterior').disabled = true; el('skuSiguiente').disabled = true;
        try {
            const data = await request(queryUrl(root.dataset.listado, {busqueda:el('skuBusqueda').value.trim(),estado:el('skuFiltroEstado').value,pagina}));
            if (current !== listadoRequest) return;
            paginas = Math.max(1, Math.ceil(data.totalFiltrado / data.tamanoPagina));
            if (pagina > paginas) { pagina=paginas; await cargar(); return; }
            registros = new Map(data.registros.map(r => [String(r.id), r])); tbody.replaceChildren();
            if (!data.registros.length) { const tr=element('tr'), td=element('td','No se encontraron conversiones con estos filtros.','sku-empty'); td.colSpan=8; tr.append(td); tbody.append(tr); }
            else data.registros.forEach(r => tbody.append(row(r)));
            el('skuTotal').textContent=data.conteos.total; el('skuActivas').textContent=data.conteos.activas; el('skuInactivas').textContent=data.conteos.inactivas;
            el('skuPaginaTexto').textContent=`${data.totalFiltrado} resultado${data.totalFiltrado === 1 ? '' : 's'} · Página ${pagina} de ${paginas}`;
            el('skuAnterior').disabled=pagina <= 1; el('skuSiguiente').disabled=pagina >= paginas;
        } catch (error) {
            if (current !== listadoRequest) return;
            registros.clear(); tbody.replaceChildren(); const tr=element('tr'), td=element('td','El catálogo no está disponible.','sku-empty'); td.colSpan=8; tr.append(td); tbody.append(tr);
            el('skuPaginaTexto').textContent=''; aviso('skuMensaje', error.message);
        } finally { if (current === listadoRequest) tbody.setAttribute('aria-busy','false'); }
    }

    function abrirEditor(record) {
        editando=record; form.reset(); aviso('skuEditorError','');
        el('skuEditorTitulo').textContent=record ? 'Editar conversión' : 'Nueva conversión';
        el('skuOrigen').value=record?.skuOrigen || ''; el('skuDestino').value=record?.skuDestino || '';
        el('skuOrigenNombre').textContent=record?.nombreOrigen || 'Escribe al menos 2 caracteres para buscar por SKU o nombre.';
        el('skuDestinoNombre').textContent=record?.nombreDestino || 'Selecciona un artículo del catálogo de SIGO.';
        el('skuOpcionesOrigen').replaceChildren(); el('skuOpcionesDestino').replaceChildren();
        el('skuFactor').value=record?.factorTexto ?? ''; el('skuPrioridad').value=record ? record.prioridad ?? '' : '1';
        el('skuFactor').readOnly=!record; el('skuPrioridad').readOnly=!record;
        el('skuFactor').placeholder=record ? '' : 'NULL';
        el('skuFactorAyuda').textContent=record ? 'Opcional. Hasta 6 decimales; vacío conserva la conversión sin factor.' : 'Las nuevas conversiones se guardan sin factor (NULL).';
        el('skuPrioridadAyuda').textContent=record ? 'Vacía = Base. Los números menores aparecen primero.' : 'La prioridad de las nuevas conversiones es siempre 1.';
        el('skuMotivo').value=record?.motivo || ''; el('skuActivo').checked=record?.activo ?? true;
        el('skuUsuarioAlta').textContent=record ? (record.usuarioAlta || 'Sin usuario registrado') : root.dataset.usuario;
        el('skuFechaAlta').textContent=record ? fecha(record.fechaAlta) : 'Se asignará al guardar';
        editor.show();
    }

    form.addEventListener('submit', async event => {
        event.preventDefault(); if (!form.reportValidity()) return;
        const payload={id:editando?.id || 0,skuOrigen:el('skuOrigen').value.trim(),skuDestino:el('skuDestino').value.trim(),
            factor:editando ? el('skuFactor').value || null : null,prioridad:editando ? (el('skuPrioridad').value === '' ? null : Number(el('skuPrioridad').value)) : 1,
            motivo:el('skuMotivo').value.trim() || null,activo:el('skuActivo').checked,version:editando?.version || null};
        busy(el('skuEditor'),true); aviso('skuEditorError','');
        try { const data=await request(root.dataset.guardar,payload); editor.hide(); aviso('skuMensaje',data.mensaje,'success'); await cargar(); }
        catch(error) { aviso('skuEditorError',error.message); }
        finally { busy(el('skuEditor'),false); }
    });

    el('skuRegistros').addEventListener('click', event => {
        const button=event.target.closest('button[data-action]'); if (!button) return;
        const record=registros.get(button.dataset.id); if (!record) return;
        if (button.dataset.action === 'editar') abrirEditor(record);
        if (button.dataset.action === 'estado') {
            cambiando=record; aviso('skuEstadoError','');
            el('skuEstadoTitulo').textContent=record.activo ? 'Desactivar conversión' : 'Activar conversión';
            el('skuEstadoTexto').textContent=`${record.skuOrigen} → ${record.skuDestino}. ${record.activo ? 'La conversión dejará de estar activa.' : 'La conversión volverá a estar activa.'}`;
            el('skuConfirmarEstado').textContent=record.activo ? 'Desactivar' : 'Activar'; estadoModal.show();
        }
        if (button.dataset.action === 'historial') mostrarHistorial(record);
    });

    el('skuConfirmarEstado').addEventListener('click', async () => {
        if (!cambiando) return;
        busy(el('skuEstadoModal'),true); aviso('skuEstadoError','');
        try { const data=await request(root.dataset.estado,{id:cambiando.id,activo:!cambiando.activo,version:cambiando.version}); estadoModal.hide(); aviso('skuMensaje',data.mensaje,'success'); await cargar(); }
        catch(error) { aviso('skuEstadoError',error.message); }
        finally { busy(el('skuEstadoModal'),false); }
    });

    async function mostrarHistorial(record) {
        const current=++historialRequest, content=el('skuHistorialContenido');
        el('skuHistorialTitulo').textContent=`Historial · ${record.skuOrigen} → ${record.skuDestino}`; content.replaceChildren(element('p','Cargando historial…')); historialModal.show();
        try {
            const changes=await request(queryUrl(root.dataset.historial,{id:record.id})); if (current !== historialRequest) return;
            content.replaceChildren();
            if (!changes.length) {content.append(element('p',`Alta registrada por ${record.usuarioAlta || 'usuario no registrado'} el ${fecha(record.fechaAlta)}.`), element('p','Los cambios anteriores a la incorporación de este catálogo no tienen historial. Los nuevos cambios quedarán registrados.','sku-help')); return;}
            const fields={skuOrigen:'SKU origen',skuDestino:'SKU destino',factor:'Factor',prioridad:'Prioridad',motivo:'Motivo',activo:'Estado'};
            const value=(key, v) => key === 'activo' ? (v ? 'Activa' : 'Inactiva') : v === null || v === undefined || v === '' ? (key === 'prioridad' ? 'Base' : 'Vacío') : String(v);
            changes.forEach(change => {
                const before=change.datosAntes ? JSON.parse(change.datosAntes) : null, after=JSON.parse(change.datosDespues);
                const item=element('article','','sku-history-item'), heading=element('div','','sku-history-heading');
                heading.append(element('strong',change.accion),element('span',change.usuario),element('small',fecha(change.fecha))); item.append(heading);
                const details=element('ul','','sku-history-details');
                Object.entries(fields).forEach(([key,label]) => { if (before && before[key] === after[key]) return; details.append(element('li',`${label}: ${before ? value(key,before[key]) + ' → ' : ''}${value(key,after[key])}`)); });
                if (!details.childElementCount) details.append(element('li','Se guardó la conversión sin cambios en sus campos.'));
                item.append(details); content.append(item);
            });
        } catch(error) { if (current === historialRequest) content.replaceChildren(element('div',error.message,'alert alert-danger')); }
    }

    ['Origen','Destino'].forEach(side => {
        const input=el(`sku${side}`), options=el(`skuOpciones${side}`), help=el(`sku${side}Nombre`);
        let timer, controller, items=[];
        input.addEventListener('input', () => {
            clearTimeout(timer); controller?.abort();
            const q=input.value.trim(), selected=items.find(a => a.sku.toUpperCase() === q.toUpperCase());
            if (selected) {help.textContent=selected.nombre || 'Sin nombre en el catálogo';return;}
            if (q.length < 2) {options.replaceChildren();help.textContent='Escribe al menos 2 caracteres para buscar.';return;}
            help.textContent='Buscando artículos…';
            timer=setTimeout(async () => {
                controller=new AbortController();
                try {items=await request(queryUrl(root.dataset.articulos,{busqueda:q}),undefined,controller.signal);options.replaceChildren();items.forEach(a => {const option=element('option');option.value=a.sku;option.label=`${a.sku} · ${a.nombre || ''}`;options.append(option);}); const exact=items.find(a => a.sku.toUpperCase() === input.value.trim().toUpperCase());help.textContent=exact ? exact.nombre || 'Sin nombre en el catálogo' : items.length ? 'Selecciona el SKU de las sugerencias.' : 'No se encontraron artículos.';}
                catch(error) {if(error.name !== 'AbortError')help.textContent=error.message;}
            },250);
        });
        el('skuEditor').addEventListener('hidden.bs.modal', () => {clearTimeout(timer);controller?.abort();items=[];});
    });

    el('skuEditor').addEventListener('shown.bs.modal', () => el('skuOrigen').focus());
    el('skuHistorialModal').addEventListener('hidden.bs.modal', () => historialRequest++);
    el('skuNueva').addEventListener('click', () => abrirEditor(null));
    el('skuFiltros').addEventListener('submit', event => {event.preventDefault();pagina=1;aviso('skuMensaje','');cargar();});
    el('skuFiltroEstado').addEventListener('change', () => {pagina=1;aviso('skuMensaje','');cargar();});
    el('skuActualizar').addEventListener('click', () => {aviso('skuMensaje','');cargar();});
    el('skuAnterior').addEventListener('click', () => {if(pagina>1){pagina--;cargar();}});
    el('skuSiguiente').addEventListener('click', () => {if(pagina<paginas){pagina++;cargar();}});
    cargar();
})();
