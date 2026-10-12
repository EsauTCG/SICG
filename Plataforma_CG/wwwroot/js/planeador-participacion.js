(() => {
    'use strict';
    const root=document.getElementById('partCatalogo'); if(!root)return;
    const el=id=>document.getElementById(id), form=el('partFormulario');
    const token=form.querySelector('input[name="__RequestVerificationToken"]').value;
    const editor=new bootstrap.Modal(el('partEditor')), historyModal=new bootstrap.Modal(el('partHistorial'));
    let pagina=1,paginas=1,listRequest=0,historyRequest=0,records=new Map(),editando=null;
    const node=(tag,text='',classes='')=>{const n=document.createElement(tag);n.textContent=text;n.className=classes;return n;};
    const fecha=value=>value ? new Date(value).toLocaleString('es-MX',{dateStyle:'short',timeStyle:'short'}) : '';
    const key=r=>`${r.clasificacionId}:${r.sku}`;
    const percent=r=>r.mixto ? 'Valores distintos' : `${r.porcentajeTexto} %`;
    const aviso=(id,text,tipo='danger')=>{el(id).textContent=text;el(id).className=`alert alert-${tipo}${text?'':' d-none'}`;};
    const url=(base,args)=>{const u=new URL(base,location.origin);Object.entries(args).forEach(([k,v])=>{if(v!==''&&v!==null)u.searchParams.set(k,v);});return u;};
    async function request(u,payload){
        const response=await fetch(u,{method:payload===undefined?'GET':'POST',credentials:'same-origin',cache:'no-store',
            headers:{'X-Requested-With':'XMLHttpRequest',...(payload===undefined?{}:{'Content-Type':'application/json','RequestVerificationToken':token})},
            ...(payload===undefined?{}:{body:JSON.stringify(payload)})});
        if(response.status===401||response.status===403||response.redirected)throw new Error('Tu sesión no está disponible. Recarga la página e inicia sesión.');
        const data=await response.json().catch(()=>{throw new Error('No se pudo leer la respuesta del sistema. Recarga e intenta nuevamente.');});
        if(!response.ok)throw new Error(data.mensaje||'No se pudo completar la operación.');return data;
    }
    function fila(r){
        const tr=node('tr'),product=node('td','','part-product');product.append(node('span',r.sku,'part-sku'),node('span',r.nombre||'Sin nombre en el catálogo','part-name'));
        tr.append(product,node('td',r.masters||'Sin máster','part-context'),node('td',r.clasificacion||`ID ${r.clasificacionId}`),node('td',percent(r),'part-percent'));
        const subs=node('td',r.subClasificaciones||'Sin subclasificación','part-context');subs.append(node('span',`${r.registros} registro${r.registros===1?'':'s'}`,'part-subtext'));tr.append(subs);
        const author=node('td',r.usuarioUltimoCambio||'Sin cambios registrados','part-author');if(r.fechaUltimoCambio)author.append(node('span',fecha(r.fechaUltimoCambio),'part-subtext'));tr.append(author);
        const actions=node('td'),group=node('div','','part-actions');
        [['editar','Editar porcentaje','pencil'],['historial','Historial','clock-history']].forEach(([action,label,icon])=>{
            const b=node('button','','btn btn-outline-secondary');b.type='button';b.dataset.action=action;b.dataset.key=key(r);b.title=`${label} ${r.sku} · ${r.clasificacion||r.clasificacionId}`;b.setAttribute('aria-label',b.title);
            const i=node('i','',`bi bi-${icon}`);i.setAttribute('aria-hidden','true');b.append(i);group.append(b);
        });actions.append(group);tr.append(actions);return tr;
    }
    async function cargar(){
        const current=++listRequest,tbody=el('partFilas');tbody.setAttribute('aria-busy','true');el('partAnterior').disabled=true;el('partSiguiente').disabled=true;
        try{
            const data=await request(url(root.dataset.listado,{busqueda:el('partBusqueda').value.trim(),clasificacionId:el('partClasificacion').value,pagina}));if(current!==listRequest)return;
            paginas=Math.max(1,Math.ceil(data.totalFiltrado/data.tamanoPagina));if(pagina>paginas){pagina=paginas;await cargar();return;}
            const selected=el('partClasificacion').value;el('partClasificacion').replaceChildren();const all=node('option','Todos');all.value='';el('partClasificacion').append(all);
            data.clasificaciones.forEach(c=>{const o=node('option',c.nombre);o.value=c.id;el('partClasificacion').append(o);});el('partClasificacion').value=selected;
            records=new Map(data.registros.map(r=>[key(r),r]));tbody.replaceChildren();
            if(!data.registros.length){const tr=node('tr'),td=node('td','No se encontraron SKU con estos filtros.','part-empty');td.colSpan=7;tr.append(td);tbody.append(tr);}else data.registros.forEach(r=>tbody.append(fila(r)));
            el('partSkus').textContent=data.conteos.skus;el('partGrupos').textContent=data.conteos.grupos;el('partRegistros').textContent=data.conteos.registros;
            el('partPaginaTexto').textContent=`${data.totalFiltrado} resultado${data.totalFiltrado===1?'':'s'} · Página ${pagina} de ${paginas}`;
            el('partAnterior').disabled=pagina<=1;el('partSiguiente').disabled=pagina>=paginas;
        }catch(error){if(current!==listRequest)return;records.clear();tbody.replaceChildren();const tr=node('tr'),td=node('td','El catálogo no está disponible.','part-empty');td.colSpan=7;tr.append(td);tbody.append(tr);el('partPaginaTexto').textContent='';aviso('partMensaje',error.message);}
        finally{if(current===listRequest)tbody.setAttribute('aria-busy','false');}
    }
    function abrir(r){
        editando=r;form.reset();aviso('partEditorError','');el('partEditorSku').textContent=r.sku;el('partEditorNombre').textContent=r.nombre||'Sin nombre en el catálogo';el('partEditorPlan').textContent=`Tipo de plan: ${r.clasificacion||r.clasificacionId}`;
        el('partActual').textContent=percent(r);el('partPorcentaje').value=r.porcentajeTexto??'';
        el('partAlcance').textContent=`El nuevo porcentaje se aplicará a los ${r.registros} registros de ${r.sku} en ${r.clasificacion||r.clasificacionId}, conservando sus subclasificaciones y distribución de participación.`;
        editor.show();
    }
    function busy(value){el('partEditor').setAttribute('aria-busy',String(value));el('partEditor').querySelectorAll('input,button').forEach(n=>n.disabled=value);}
    form.addEventListener('submit',async event=>{
        event.preventDefault();if(!editando||!form.reportValidity())return;busy(true);aviso('partEditorError','');
        try{const data=await request(root.dataset.guardar,{sku:editando.sku,clasificacionId:editando.clasificacionId,porcentaje:el('partPorcentaje').value,version:editando.version});editor.hide();aviso('partMensaje',data.mensaje,'success');await cargar();}
        catch(error){aviso('partEditorError',error.message);}finally{busy(false);}
    });
    async function historial(r){
        const current=++historyRequest,body=el('partHistorialContenido');el('partHistorialTitulo').textContent=`Historial · ${r.sku} · ${r.clasificacion||r.clasificacionId}`;body.replaceChildren(node('p','Cargando historial…'));historyModal.show();
        try{const changes=await request(url(root.dataset.historial,{sku:r.sku,clasificacionId:r.clasificacionId}));if(current!==historyRequest)return;body.replaceChildren();
            if(!changes.length){body.append(node('p','Este SKU todavía no tiene cambios registrados desde esta vista.','part-help'));return;}
            changes.forEach(c=>{const item=node('article','','part-history-item'),heading=node('div','','part-history-heading');heading.append(node('strong',`${c.porcentajeAntes===null?'Valores distintos':Number(c.porcentajeAntes).toFixed(2)+' %'} → ${Number(c.porcentajeDespues).toFixed(2)} %`),node('span',c.usuario),node('small',fecha(c.fecha)));item.append(heading,node('p',`${c.registros} registro${c.registros===1?'':'s'} actualizado${c.registros===1?'':'s'}.`,'part-help mb-0'));body.append(item);});
        }catch(error){if(current===historyRequest)body.replaceChildren(node('div',error.message,'alert alert-danger'));}
    }
    el('partFilas').addEventListener('click',event=>{const b=event.target.closest('button[data-action]');if(!b)return;const r=records.get(b.dataset.key);if(!r)return;if(b.dataset.action==='editar')abrir(r);else historial(r);});
    el('partEditor').addEventListener('shown.bs.modal',()=>el('partPorcentaje').focus());
    el('partHistorial').addEventListener('hidden.bs.modal',()=>historyRequest++);
    el('partFiltros').addEventListener('submit',event=>{event.preventDefault();pagina=1;aviso('partMensaje','');cargar();});
    el('partClasificacion').addEventListener('change',()=>{pagina=1;aviso('partMensaje','');cargar();});
    el('partActualizar').addEventListener('click',()=>{aviso('partMensaje','');cargar();});
    el('partAnterior').addEventListener('click',()=>{if(pagina>1){pagina--;cargar();}});
    el('partSiguiente').addEventListener('click',()=>{if(pagina<paginas){pagina++;cargar();}});
    cargar();
})();
