(() => {
    'use strict';
    const root = document.getElementById('pdCatalogo');
    if (!root) return;
    const el = id => document.getElementById(id);
    const node = (tag, text = '', classes = '') => { const n = document.createElement(tag); n.textContent = text; n.className = classes; return n; };
    const form = el('pdFormulario'), token = form.querySelector('input[name="__RequestVerificationToken"]').value;
    const editor = new bootstrap.Modal(el('pdEditor')), deletion = new bootstrap.Modal(el('pdEliminarModal')), history = new bootstrap.Modal(el('pdHistorialModal'));
    let pagina = 1, paginas = 1, listRequest = 0, records = new Map(), editando = null, eliminando = null;
    let clasificaciones = [], subs = [], historyPage = 1, historyRequest = 0, versionCatalogo = '', versionEdicion = '', reglas = null, confirmacion = null;
    const key = r => `${r.clasificacionId}:${r.sku}`;
    const fecha = value => value ? new Date(value).toLocaleString('es-MX', { dateStyle: 'short', timeStyle: 'short' }) : '';
    const numero = value => value === null || value === undefined ? 'Sin valor' : Number(value).toLocaleString('es-MX', { maximumFractionDigits: 2 });
    const aviso = (id, text, tipo = 'danger') => { el(id).textContent = text; el(id).className = `alert alert-${tipo}${text ? '' : ' d-none'}`; };
    const busy = (id, value) => { el(id).setAttribute('aria-busy', String(value)); el(id).querySelectorAll('input,select,button').forEach(n => n.disabled = value); };
    const url = (base, args) => { const u = new URL(base, location.origin); Object.entries(args).forEach(([k, v]) => { if (v !== '' && v !== null) u.searchParams.set(k, v); }); return u; };
    async function request(u, payload, signal) {
        const response = await fetch(u, {
            method: payload === undefined ? 'GET' : 'POST', credentials: 'same-origin', cache: 'no-store', signal,
            headers: { 'X-Requested-With': 'XMLHttpRequest', ...(payload === undefined ? {} : { 'Content-Type': 'application/json', 'RequestVerificationToken': token }) },
            ...(payload === undefined ? {} : { body: JSON.stringify(payload) })
        });
        if (response.status === 401 || response.status === 403 || response.redirected) throw new Error('Tu sesión no está disponible. Recarga la página e inicia sesión.');
        const data = await response.json().catch(() => { throw new Error('No se pudo leer la respuesta del sistema. Intenta nuevamente.'); });
        if (!response.ok) throw new Error(data.mensaje || 'No se pudo completar la operación.');
        return data;
    }
    function opciones(id, values, todos = false) {
        const select = el(id), actual = select.value;
        select.replaceChildren();
        if (todos) { const option = node('option', 'Todos'); option.value = ''; select.append(option); }
        values.forEach(v => { const option = node('option', v.nombre); option.value = v.id; select.append(option); });
        if ([...select.options].some(o => o.value === actual)) select.value = actual;
    }
    function fila(r) {
        const tr = node('tr'), product = node('td', '', 'part-product');
        product.append(node('span', r.sku, 'part-sku'), node('span', r.nombre || 'Sin nombre en ArticuloSAP', 'part-name'));
        const tipo=clasificaciones.find(c=>c.id===r.clasificacionId)?.tipo,bloqueo=reglas?.planes[tipo]?.bloqueados.find(b=>b.sku===r.sku);
        if(bloqueo)product.append(node('span',bloqueo.excluir?'Excluido de esta plantilla':bloqueo.motivo,'part-subtext'));
        const porcentajes = [...new Set(r.filas.map(f => f.porcentaje))];
        tr.append(product, node('td', r.master || 'SIN MASTER', 'part-context'), node('td', r.clasificacion), node('td', porcentajes.length === 1 ? `${numero(porcentajes[0])} %` : 'Valores distintos', 'part-percent'));
        tr.append(node('td', [...new Set(r.filas.map(f => f.lineaCodigo || 'Principal'))].join(', ')));
        const dist = node('td', '', 'part-context');
        r.filas.forEach(f => dist.append(node('span', `${subs.find(s => s.id === f.subClasificacionId)?.nombre || 'Sin subclasificación'}: ${numero(f.partSub)}`, 'part-subtext')));
        tr.append(dist);
        const author = node('td', r.usuarioUltimoCambio || 'Sin cambios registrados', 'part-author');
        if (r.fechaUltimoCambio) author.append(node('span', fecha(r.fechaUltimoCambio), 'part-subtext'));
        tr.append(author);
        const actions = node('td'), group = node('div', '', 'part-actions');
        [['editar', 'Editar', 'pencil'], ['eliminar', 'Eliminar', 'trash'], ['historial', 'Historial', 'clock-history']].forEach(([action, label, icon]) => {
            const b = node('button', '', `btn btn-outline-${action === 'eliminar' ? 'danger' : 'secondary'}`);
            b.type = 'button'; b.dataset.action = action; b.dataset.key = key(r); b.title = `${label} ${r.sku} · ${r.clasificacion}`; b.setAttribute('aria-label', b.title);
            const i = node('i', '', `bi bi-${icon}`); i.setAttribute('aria-hidden', 'true'); b.append(i); group.append(b);
        });
        actions.append(group); tr.append(actions); return tr;
    }
    async function cargar() {
        const current = ++listRequest, body = el('pdFilas'); body.setAttribute('aria-busy', 'true');
        el('pdAnterior').disabled = el('pdSiguiente').disabled = true;
        try {
            const data = await request(url(root.dataset.listado, { busqueda: el('pdBusqueda').value.trim(), clasificacionId: el('pdClasificacionFiltro').value, pagina }));
            if (current !== listRequest) return;
            if(!data.versionCatalogo||!data.reglas)throw new Error('El catálogo requiere la nueva versión de afectaciones. Recompila/reinicia la aplicación y recarga la página.');
            paginas = Math.max(1, Math.ceil(data.totalFiltrado / data.tamanoPagina));
            if (pagina > paginas) { pagina = paginas; await cargar(); return; }
            clasificaciones = data.clasificaciones; subs = data.subclasificaciones; versionCatalogo = data.versionCatalogo; reglas = data.reglas;
            opciones('pdClasificacionFiltro', clasificaciones, true); opciones('pdClasificacion', clasificaciones); opciones('pdHistorialClasificacion', clasificaciones, true);
            records = new Map(data.registros.map(r => [key(r), r])); body.replaceChildren();
            if (!records.size) { const tr = node('tr'), td = node('td', 'No hay productos con estos filtros. Puedes agregar un SKU del catálogo de SIGO.', 'part-empty'); td.colSpan = 8; tr.append(td); body.append(tr); }
            else data.registros.forEach(r => body.append(fila(r)));
            el('pdTotal').textContent = data.totalFiltrado;
            el('pdPaginaTexto').textContent = `${data.totalFiltrado} resultados · Página ${pagina} de ${paginas}`;
            el('pdAnterior').disabled = pagina <= 1; el('pdSiguiente').disabled = pagina >= paginas; el('pdNuevo').disabled = !clasificaciones.length;
        } catch (error) {
            if (current !== listRequest) return;
            records.clear(); body.replaceChildren(); const tr = node('tr'), td = node('td', 'El catálogo no está disponible.', 'part-empty'); td.colSpan = 8; tr.append(td); body.append(tr);
            el('pdNuevo').disabled = true; el('pdPaginaTexto').textContent = ''; aviso('pdMensaje', error.message);
        } finally { if (current === listRequest) body.setAttribute('aria-busy', 'false'); }
    }
    function distribuciones(filas = []) {
        const body = el('pdDistribuciones'); body.replaceChildren();
        const clasificacionId = Number(el('pdClasificacion').value);
        const visibles = subs.filter(s => s.clasificacionId === clasificacionId);
        if (clasificaciones.find(c => c.id === clasificacionId)?.tipo === 'NOV') {
            const orden = { PREMIUM: 0, PREMIUN: 0, NOVLINEA: 1, LINEA: 1, SELECTO: 2 };
            const prioridad = s => orden[s.nombre.trim().toUpperCase().replace(/[ .-]/g, '')] ?? 3;
            visibles.sort((a, b) => prioridad(a) - prioridad(b));
        }
        visibles.forEach(s => {
            const row = node('div', '', 'pd-distribucion'), check = node('input'), label = node('label', s.nombre), value = node('input');
            const prev = filas.find(f => f.subClasificacionId === s.id);
            check.type = 'checkbox'; check.id = `pdSub${s.id}`; check.className = 'form-check-input'; check.checked = !!prev; label.htmlFor = check.id;
            value.type = 'number'; value.className = 'form-control'; value.min = '0'; value.max = '1'; value.step = '0.01'; value.value = prev?.partSub ?? '0'; value.dataset.sub = s.id;
            value.disabled = !check.checked; value.required = check.checked; value.setAttribute('aria-label', `Distribución de ${s.nombre}`);
            check.addEventListener('change', () => { value.disabled = !check.checked; value.required = check.checked; });
            row.append(check, label, value); body.append(row);
        });
        marcarCalculados();
    }
    function marcarCalculados() {
        const tipo=clasificaciones.find(c=>c.id===Number(el('pdClasificacion').value))?.tipo;
        const p=reglas?.planes[tipo], codigo=el('pdSku').value.trim().toUpperCase();
        for(const input of el('pdDistribuciones').querySelectorAll('input[data-sub]')) {
            const nombre=subs.find(s=>s.id===Number(input.dataset.sub))?.nombre||'';
            const sub=nombre.toUpperCase().replace(/[ .-]/g,'');
            const aliases={PREMIUN:'PREMIUM',PREMIUM:'PREMIUM',NOVLINEA:'NOV-LINEA',LINEA:'NOV-LINEA',SELECTO:'SELECTO',VGBIG:'VG-BIG',VGLINEA:'VG-LINEA',VR:'VR',REG:'VR',TAMARILLA:'T.Amarilla',TERAMA:'T.Amarilla'};
            const suma=p?.sumas.find(s=>s.destino===codigo&&s.subclasificaciones.includes(aliases[sub]||sub)),bloqueo=p?.bloqueados.find(b=>b.sku===codigo);
            input.readOnly=!!suma||!!bloqueo;input.title=bloqueo?bloqueo.motivo:suma?'Automático: '+suma.fuentes.join(' + ')+(suma.restar.length?' menos '+suma.restar.join(' + '):''):'';
            if(bloqueo)input.value='0';
        }
    }
    function invalidarPreview() { confirmacion=null;el('pdAfectacionesResumen').replaceChildren();form.querySelector('button[type=submit]').textContent='Revisar cambio global'; }
    function resumen(id,cambios) {
        const body=el(id);body.replaceChildren(node('p',cambios.length?'Se aplicarán estos ajustes a todas las fechas del tipo de plan:':'No hay ajustes adicionales de distribución. Se guardarán los demás datos capturados.','part-help'));
        if(!cambios.length)return;
        const wrap=node('div','','table-responsive'),table=node('table','','table table-bordered table-sm pd-history-table'),thead=node('thead'),tr=node('tr'),tbody=node('tbody');
        ['SKU','Subclasificación','Antes','Después','Afectación'].forEach(t=>tr.append(node('th',t)));thead.append(tr);
        cambios.forEach(c=>{const row=node('tr');[c.sku,c.subclasificacion,numero(c.antes),numero(c.despues),c.motivo].forEach(t=>row.append(node('td',t)));tbody.append(row);});
        table.append(thead,tbody);wrap.append(table);body.append(wrap);
    }
    function abrir(r = null) {
        editando = r; form.reset(); aviso('pdEditorError', ''); versionEdicion=versionCatalogo; invalidarPreview();
        el('pdEditorTitulo').textContent = r ? `Editar ${r.sku} globalmente` : 'Agregar SKU global';
        el('pdSku').value = r?.sku || ''; el('pdMaster').value = r?.master || ''; el('pdSkuNombre').textContent = r?.nombre || 'Busca por código o nombre en ArticuloSAP de SIGO.';
        el('pdArticulos').replaceChildren(); el('pdLineas').replaceChildren();
        el('pdClasificacion').value = r?.clasificacionId || el('pdClasificacionFiltro').value || clasificaciones[0]?.id || '';
        const porcentajes = [...new Set((r?.filas || []).map(f => f.porcentaje))], lineas = [...new Set((r?.filas || []).map(f => f.lineaCodigo || ''))];
        el('pdPorcentaje').value = porcentajes.length === 1 ? porcentajes[0] : '';
        el('pdLinea').value = lineas.length === 1 ? lineas[0] : '';
        distribuciones(r?.filas);
        if (r && (porcentajes.length > 1 || lineas.length > 1)) aviso('pdEditorError', 'Este SKU tiene valores distintos por subclasificación. Captura el porcentaje y la línea que se aplicarán a todas.', 'warning');
        editor.show();
    }
    // Cancelar solicitudes obsoletas evita mostrar el master de otro SKU.
    function buscador(inputId, listId, mostrarMaster) {
        let timer, controller, sequence = 0;
        const cancelar = () => { clearTimeout(timer); controller?.abort(); sequence++; };
        el(inputId).addEventListener('input', () => {
            cancelar(); const current = sequence, q = el(inputId).value.trim(); el(listId).replaceChildren();
            if (mostrarMaster) { el('pdMaster').value = ''; el('pdSkuNombre').textContent = q.length < 2 ? 'Escribe al menos 2 caracteres para buscar.' : 'Buscando en ArticuloSAP…'; }
            if (q.length < 2) return;
            timer = setTimeout(async () => {
                controller = new AbortController();
                try {
                    const data = await request(url(root.dataset.articulos, { busqueda: q }), undefined, controller.signal);
                    if (current !== sequence) return;
                    data.forEach(a => { const o = node('option', `${a.nombre || ''} · ${a.master || 'SIN MASTER'}`); o.value = a.sku; el(listId).append(o); });
                    if (mostrarMaster) {
                        const exact = data.find(a => a.sku.toUpperCase() === q.toUpperCase());
                        el('pdMaster').value = exact?.master || '';
                        el('pdSkuNombre').textContent = exact ? exact.nombre || 'Artículo sin nombre' : `${data.length} coincidencias. Selecciona el código de un artículo.`;
                    }
                } catch (error) { if (error.name !== 'AbortError' && current === sequence) aviso('pdEditorError', error.message); }
            }, 250);
        });
        el('pdEditor').addEventListener('hidden.bs.modal', cancelar);
    }
    buscador('pdSku', 'pdArticulos', true); buscador('pdLinea', 'pdLineas', false);
    el('pdClasificacion').addEventListener('change', () => distribuciones());
    form.addEventListener('input',()=>{invalidarPreview();marcarCalculados();});
    form.addEventListener('change',invalidarPreview);
    form.addEventListener('submit', async event => {
        event.preventDefault(); if (!form.reportValidity()) return;
        const dist = [...el('pdDistribuciones').querySelectorAll('input[data-sub]')].filter(n => !n.disabled).map(n => ({ subClasificacionId: Number(n.dataset.sub), partSub: n.value })).sort((a,b)=>a.subClasificacionId-b.subClasificacionId);
        if (!dist.length) { aviso('pdEditorError', 'Selecciona al menos una subclasificación y captura su distribución.'); return; }
        const payload = { versionCatalogo:versionEdicion, skuOriginal: editando?.sku || null, clasificacionOriginalId: editando?.clasificacionId ?? null, version: editando?.version || null,
            sku: el('pdSku').value.trim(), clasificacionId: Number(el('pdClasificacion').value), porcentaje: el('pdPorcentaje').value, lineaCodigo: el('pdLinea').value.trim() || null, distribuciones: dist };
        busy('pdEditor', true); aviso('pdEditorError', '');
        try {
            if(confirmacion!==JSON.stringify(payload)) {
                const data=await request(root.dataset.previsualizar,payload);
                for(const d of data.distribuciones||[]) {const input=el('pdDistribuciones').querySelector(`input[data-sub="${d.subClasificacionId}"]`);if(input){input.value=d.partSub;input.closest('.pd-distribucion').querySelector('input[type=checkbox]').checked=true;input.required=true;}const original=payload.distribuciones.find(v=>v.subClasificacionId===d.subClasificacionId);if(original)original.partSub=String(d.partSub);else payload.distribuciones.push({subClasificacionId:d.subClasificacionId,partSub:String(d.partSub)});}
                payload.distribuciones.sort((a,b)=>a.subClasificacionId-b.subClasificacionId);
                resumen('pdAfectacionesResumen',data.cambios);confirmacion=JSON.stringify(payload);form.querySelector('button[type=submit]').textContent='Confirmar cambio global';return;
            }
            const data = await request(root.dataset.guardar, payload); editor.hide(); aviso('pdMensaje', `${data.mensaje} ${data.planesAfectados} planes actualizados. Incluye los SKU relacionados.`, 'success'); await cargar();
        }
        catch (error) { aviso('pdEditorError', error.message); }
        finally { busy('pdEditor', false); el('pdDistribuciones').querySelectorAll('.pd-distribucion').forEach(row => { const check = row.querySelector('input[type=checkbox]'); row.querySelector('input[type=number]').disabled = !check.checked; }); }
    });
    async function eliminar(r) {
        eliminando=null;aviso('pdEliminarError','');el('pdEliminarAfectaciones').textContent='Calculando afectaciones…';el('pdEliminarTexto').textContent=`Eliminar ${r.sku} · ${r.nombre||''} de todos los planes ${r.clasificacion}.`;deletion.show();el('pdConfirmarEliminar').disabled=true;
        try {const payload={sku:r.sku,clasificacionId:r.clasificacionId,version:r.version,versionCatalogo};const data=await request(root.dataset.previsualizarEliminar,payload);resumen('pdEliminarAfectaciones',data.cambios);eliminando=payload;el('pdConfirmarEliminar').disabled=false;}
        catch(error){el('pdEliminarAfectaciones').replaceChildren();aviso('pdEliminarError',error.message);}
    }
    el('pdConfirmarEliminar').addEventListener('click', async () => {
        if (!eliminando) return; const r = eliminando; busy('pdEliminarModal', true); aviso('pdEliminarError', '');
        try { const data = await request(root.dataset.eliminar, r); deletion.hide(); aviso('pdMensaje', `${data.mensaje} ${data.planesAfectados} planes actualizados.`, 'success'); await cargar(); }
        catch (error) { aviso('pdEliminarError', error.message); } finally { busy('pdEliminarModal', false); }
    });
    function estado(text) { try { return text ? JSON.parse(text) : null; } catch { return null; } }
    function tablaEstado(antes, despues) {
        const details = node('details', '', 'pd-history-detail'); details.append(node('summary', 'Ver valores anteriores y nuevos'));
        const origen=despues?.origenCambio||antes?.origenCambio;if(origen)details.append(node('p',`Afectación automática originada por ${origen}.`,'part-help'));
        const identity = e => e ? `${e.sku} · ${clasificaciones.find(c => c.id === e.clasificacionId)?.nombre || e.clasificacionId} · Master: ${e.master || 'SIN MASTER'}` : 'Sin registro';
        details.append(node('p', `${identity(antes)} → ${identity(despues)}`, 'pd-history-identity'));
        const wrap = node('div', '', 'table-responsive'), table = node('table', '', 'table table-bordered table-sm pd-history-table'), head = node('thead'), heading = node('tr'), body = node('tbody');
        ['Subclasificación', '% antes', '% después', 'Línea antes', 'Línea después', 'Distribución antes', 'Distribución después'].forEach(t => heading.append(node('th', t)));
        head.append(heading);
        const before = antes?.filas || [], after = despues?.filas || [], ids = [...new Set([...before, ...after].map(f => f.subClasificacionId))];
        ids.forEach(id => { const a = before.find(f => f.subClasificacionId === id), d = after.find(f => f.subClasificacionId === id), tr = node('tr');
            [subs.find(s => s.id === id)?.nombre || `ID ${id ?? 'sin asignar'}`, a ? numero(a.porcentaje) : '—', d ? numero(d.porcentaje) : '—', a ? a.lineaCodigo || 'Principal' : '—', d ? d.lineaCodigo || 'Principal' : '—', a ? numero(a.partSub) : '—', d ? numero(d.partSub) : '—'].forEach(t => tr.append(node('td', t))); body.append(tr); });
        table.append(head, body); wrap.append(table); details.append(wrap); return details;
    }
    async function cargarHistorial() {
        const current = ++historyRequest, body = el('pdHistorialContenido'); body.replaceChildren(node('p', 'Cargando historial…'));
        el('pdHistorialAnterior').disabled = el('pdHistorialSiguiente').disabled = true;
        try {
            const data = await request(url(root.dataset.historial, { sku: el('pdHistorialSku').value.trim(), clasificacionId: el('pdHistorialClasificacion').value, pagina: historyPage }));
            if (current !== historyRequest) return; body.replaceChildren();
            if (!data.length) body.append(node('p', 'No hay cambios registrados con estos filtros.', 'part-help'));
            data.forEach(c => { const item = node('article', '', 'part-history-item'), heading = node('div', '', 'part-history-heading');
                heading.append(node('strong', `${c.accion} · ${c.sku}`), node('span', c.usuario), node('small', fecha(c.fecha)));
                item.append(heading, node('p', `${c.planesAfectados} planes afectados.`, 'part-help mb-0'), tablaEstado(estado(c.datosAntes), estado(c.datosDespues))); body.append(item); });
            el('pdHistorialPagina').textContent = `Página ${historyPage}`; el('pdHistorialAnterior').disabled = historyPage <= 1; el('pdHistorialSiguiente').disabled = data.length < 25;
        } catch (error) { if (current === historyRequest) body.replaceChildren(node('div', error.message, 'alert alert-danger')); }
    }
    function abrirHistorial(r = null) { el('pdHistorialSku').value = r?.sku || ''; el('pdHistorialClasificacion').value = r?.clasificacionId || ''; historyPage = 1; history.show(); cargarHistorial(); }
    el('pdFilas').addEventListener('click', event => { const b = event.target.closest('button[data-action]'); if (!b) return; const r = records.get(b.dataset.key); if (!r) return; if (b.dataset.action === 'editar') abrir(r); else if (b.dataset.action === 'eliminar') eliminar(r); else abrirHistorial(r); });
    el('pdNuevo').addEventListener('click', () => abrir());
    el('pdHistorialGlobal').addEventListener('click', () => abrirHistorial());
    el('pdHistorialFiltros').addEventListener('submit', event => { event.preventDefault(); historyPage = 1; cargarHistorial(); });
    el('pdHistorialAnterior').addEventListener('click', () => { if (historyPage > 1) { historyPage--; cargarHistorial(); } });
    el('pdHistorialSiguiente').addEventListener('click', () => { historyPage++; cargarHistorial(); });
    el('pdHistorialModal').addEventListener('hidden.bs.modal', () => historyRequest++);
    el('pdEditor').addEventListener('shown.bs.modal', () => el('pdSku').focus());
    el('pdFiltros').addEventListener('submit', event => { event.preventDefault(); pagina = 1; aviso('pdMensaje', ''); cargar(); });
    el('pdClasificacionFiltro').addEventListener('change', () => { pagina = 1; aviso('pdMensaje', ''); cargar(); });
    el('pdActualizar').addEventListener('click', () => { aviso('pdMensaje', ''); cargar(); });
    el('pdAnterior').addEventListener('click', () => { if (pagina > 1) { pagina--; cargar(); } });
    el('pdSiguiente').addEventListener('click', () => { if (pagina < paginas) { pagina++; cargar(); } });
    cargar();
})();
