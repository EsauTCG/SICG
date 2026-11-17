(() => {
    'use strict';
    const root=document.getElementById('pdReglas'),catalogo=document.getElementById('pdCatalogo');
    if(!root||!catalogo)return;
    const el=id=>document.getElementById(id), crear=(tag,text='',clase='')=>{const n=document.createElement(tag);n.textContent=text;n.className=clase;return n;};
    const form=el('paFormulario'),modal=new bootstrap.Modal(el('paEditor')),historial=new bootstrap.Modal(el('paHistorialModal'));
    const token=document.querySelector('#pdFormulario input[name="__RequestVerificationToken"]').value;
    const tipos={Descuento:'Descuento',Reparto:'Reparto de 1.00',Calculado:'Cálculo automático',Cero:'Fijo en cero'};
    let datos={reglas:[],subclasificaciones:{},version:'',instalada:false},idActual='',tipoOriginal='',versionEdicion='',confirmacion=null,selecciones={},peticion=0,paginaHistorial=1;
    let cancelarBusquedas=[];
    const mensaje=(id,text,tipo='danger')=>{el(id).textContent=text;el(id).className=`alert alert-${tipo}${text?'':' d-none'}`;};
    async function request(url,payload){
        const response=await fetch(url,{method:payload===undefined?'GET':'POST',credentials:'same-origin',cache:'no-store',headers:{'X-Requested-With':'XMLHttpRequest',...(payload===undefined?{}:{'Content-Type':'application/json',RequestVerificationToken:token})},...(payload===undefined?{}:{body:JSON.stringify(payload)})});
        if(response.redirected||[401,403].includes(response.status))throw new Error('Tu sesión no está disponible. Recarga la página.');
        const data=await response.json().catch(()=>{throw new Error('No se pudo leer la respuesta.');});
        if(!response.ok)throw new Error(data.mensaje||'No se pudo completar la operación.');return data;
    }
    function descripcion(r){
        const origen=r.origenes.join(' + '),destino=r.destinos.join(', ');
        if(r.tipoRegla==='Reparto')return `${destino} = 1.00 − (${origen}). El restante es automático y de solo lectura.`;
        if(r.tipoRegla==='Calculado')return `${destino} = ${origen}${r.restar.length?' − ('+r.restar.join(' + ')+')':''}. Destinos de solo lectura.`;
        if(r.tipoRegla==='Cero')return `${destino} = 0.00 · Solo lectura${r.excluir?' · Ocultos del plan diario':''}.`;
        return `Al aumentar ${r.origenes.join(', ')}, se descuenta el mismo incremento a cada uno de: ${destino}. Al reducirlo se devuelve la diferencia.`;
    }
    function pintar(){
        const q=el('paBusqueda').value.trim().toUpperCase(),tipo=el('paFiltroTipo').value,estado=el('paFiltroEstado').value;
        const filas=datos.reglas.filter(r=>(!tipo||r.tipoPlan===tipo)&&(!estado||r.activa===(estado==='1'))&&`${r.nombre} ${r.origenes} ${r.destinos} ${r.restar}`.toUpperCase().includes(q));
        const body=el('paFilas');body.replaceChildren();
        for(const r of filas){
            const tr=crear('tr'),nombre=crear('td');nombre.append(crear('strong',r.nombre),crear('div',`${r.tipoPlan} · ${tipos[r.tipoRegla]}`,'part-help'));
            tr.append(nombre,crear('td',descripcion(r)),crear('td',r.subclasificaciones.join(', ')),crear('td',r.activa?'Activa':'Desactivada'));
            const td=crear('td'),boton=crear('button','Editar','btn btn-outline-secondary btn-sm');boton.type='button';boton.disabled=!datos.instalada;boton.setAttribute('aria-label',`Editar afectación ${r.nombre}`);boton.addEventListener('click',()=>abrir(r));td.append(boton);tr.append(td);body.append(tr);
        }
        if(!filas.length){const tr=crear('tr'),td=crear('td','No hay afectaciones con estos filtros.');td.colSpan=5;tr.append(td);body.append(tr);}
        el('paTotal').textContent=`${filas.length} afectaciones · ${datos.reglas.filter(r=>r.activa).length} activas en total`;
    }
    async function cargar(){
        const actual=++peticion;el('paNueva').disabled=true;el('paFilas').setAttribute('aria-busy','true');
        try{const d=await request(root.dataset.listado);if(actual!==peticion)return;datos=d;el('paNueva').disabled=!d.instalada;el('paHistorial').disabled=!d.instalada;pintar();mensaje('paMensaje',d.instalada?'':'La administración se habilita al instalar manualmente PlanDiarioAfectaciones-Instalacion.sql en pruebas. Mientras tanto se usan las reglas actuales.','warning');}
        catch(error){if(actual===peticion){mensaje('paMensaje',error.message);el('paFilas').replaceChildren();el('paHistorial').disabled=true;}}
        finally{if(actual===peticion)el('paFilas').setAttribute('aria-busy','false');}
    }
    function invalidar(){confirmacion=null;el('paRevision').replaceChildren();el('paRevision').classList.add('d-none');el('paConfirmar').textContent='Revisar regla';}
    function subclasificaciones(seleccionadas=[]){
        const body=el('paSubs');body.replaceChildren();
        for(const sub of datos.subclasificaciones[el('paTipoPlan').value]||[]){const label=crear('label','','form-check'),check=crear('input');check.type='checkbox';check.className='form-check-input';check.value=sub;check.checked=seleccionadas.includes(sub);label.append(check,document.createTextNode(' '+sub));body.append(label);}
    }
    function selector(nombre,titulo){
        const box=crear('div','','col-md-6'),fieldset=crear('fieldset','','border rounded p-3 h-100'),legend=crear('legend',titulo,'pd-legend');
        const grupo=crear('div','','input-group'),input=crear('input'),lista=crear('datalist'),agregar=crear('button','Agregar SKU','btn btn-outline-secondary'),tags=crear('div','','d-flex flex-wrap gap-2 mt-2');
        input.className='form-control';input.maxLength=50;input.autocomplete='off';input.placeholder='Busca por SKU o nombre';input.setAttribute('aria-label',titulo);lista.id=`paLista${nombre}`;input.setAttribute('list',lista.id);agregar.type='button';agregar.setAttribute('aria-label',`Agregar a ${titulo}`);
        let timer,controller,secuencia=0;
        const cancelar=()=>{clearTimeout(timer);controller?.abort();secuencia++;};cancelarBusquedas.push(cancelar);
        function pintarTags(){tags.replaceChildren();for(const s of selecciones[nombre]){const tag=crear('span','','badge bg-light text-dark border p-2'),quitar=crear('button','×','btn-close ms-2');quitar.type='button';quitar.setAttribute('aria-label',`Quitar ${s} de ${titulo}`);quitar.addEventListener('click',()=>{selecciones[nombre]=selecciones[nombre].filter(x=>x!==s);pintarTags();invalidar();});tag.append(document.createTextNode(s+' '),quitar);tags.append(tag);}}
        agregar.addEventListener('click',()=>{const s=input.value.trim().toUpperCase();if(!s)return;if(selecciones[nombre].includes(s)){mensaje('paError','Ese SKU ya está en el grupo.');return;}if(nombre==='destinos'&&el('paTipoRegla').value==='Reparto'&&selecciones[nombre].length){mensaje('paError','Elige un solo SKU para recibir el restante.');return;}selecciones[nombre].push(s);input.value='';lista.replaceChildren();pintarTags();invalidar();mensaje('paError','');});
        input.addEventListener('keydown',e=>{if(e.key==='Enter'){e.preventDefault();agregar.click();}});
        input.addEventListener('input',()=>{cancelar();const q=input.value.trim(),actual=secuencia;lista.replaceChildren();if(q.length<2)return;timer=setTimeout(async()=>{controller=new AbortController();try{const url=new URL(catalogo.dataset.articulos,location.origin);url.searchParams.set('busqueda',q);const response=await fetch(url,{signal:controller.signal,credentials:'same-origin',cache:'no-store'});if(!response.ok||response.redirected)throw new Error('No se pudo buscar en ArticuloSAP.');const rows=await response.json();if(actual!==secuencia)return;rows.forEach(a=>{const o=crear('option',a.nombre||a.sku);o.value=a.sku;lista.append(o);});}catch(error){if(error.name!=='AbortError'&&actual===secuencia)mensaje('paError',error.message);}},250);});
        grupo.append(input,agregar);fieldset.append(legend,grupo,lista,tags);box.append(fieldset);el('paGrupos').append(box);pintarTags();
    }
    function grupos(){
        cancelarBusquedas.forEach(f=>f());cancelarBusquedas=[];el('paGrupos').replaceChildren();const tipo=el('paTipoRegla').value;
        const ayuda={Descuento:'Selecciona los SKU editables y uno o varios productos de los que se descontará el incremento. Se devuelve la proporción al reducir la captura.',Reparto:'Elige los participantes editables y un SKU que reciba el restante: 1.00 menos la suma de los participantes. Entre todos sumarán 1.00; el SKU del restante será de solo lectura.',Calculado:'Selecciona uno o varios SKU que se suman, los que se restan y uno o varios destinos. Los destinos se calculan automáticamente y no se editan manualmente.',Cero:'Los SKU seleccionados quedan en 0.00 y de solo lectura en las subclasificaciones elegidas.'};
        el('paAyuda').textContent=ayuda[tipo];el('paExcluirGrupo').hidden=tipo!=='Cero';
        if(tipo!=='Cero')selector('origenes',tipo==='Reparto'?'Participantes editables':'SKU de origen (se suman o editan)');
        selector('destinos',tipo==='Reparto'?'SKU que recibe el restante':tipo==='Cero'?'SKU que quedan en cero':tipo==='Calculado'?'SKU calculados (solo lectura)':'SKU que reciben el descuento');
        if(tipo==='Calculado')selector('restar','SKU que se restan (opcional)');
    }
    function abrir(r){
        form.reset();idActual=r?.id||'';tipoOriginal=r?.tipoPlan||'';versionEdicion=datos.version;invalidar();mensaje('paError','');
        el('paTitulo').textContent=r?'Editar afectación':'Agregar afectación';el('paNombre').value=r?.nombre||'';el('paTipoPlan').value=r?.tipoPlan||el('paFiltroTipo').value||'NOV';el('paTipoPlan').disabled=!!r;
        el('paTipoRegla').value=r?.tipoRegla||'Descuento';el('paActiva').checked=r?.activa??true;el('paExcluir').checked=!!r?.excluir;
        selecciones={origenes:[...r?.origenes||[]],destinos:[...r?.destinos||[]],restar:[...r?.restar||[]]};subclasificaciones(r?.subclasificaciones||[]);grupos();modal.show();
    }
    function payload(){
        const tipo=el('paTipoRegla').value;
        return {version:versionEdicion,regla:{id:idActual||'00000000-0000-0000-0000-000000000000',tipoPlan:el('paTipoPlan').value,nombre:el('paNombre').value.trim(),tipoRegla:tipo,subclasificaciones:[...el('paSubs').querySelectorAll('input:checked')].map(c=>c.value),origenes:tipo==='Cero'?[]:[...selecciones.origenes],destinos:[...selecciones.destinos],restar:tipo==='Calculado'?[...selecciones.restar]:[],excluir:tipo==='Cero'&&el('paExcluir').checked,activa:el('paActiva').checked}};
    }
    function resumen(regla,body){body.append(crear('p',`${regla.nombre} · ${regla.tipoPlan} · ${regla.activa?'Activa':'Desactivada'}`,'fw-bold'),crear('p',descripcion(regla)),crear('p','Subclasificaciones: '+regla.subclasificaciones.join(', '),'part-help'));}
    form.addEventListener('submit',async event=>{
        event.preventDefault();if(!form.reportValidity())return;const p=payload();
        if(!p.regla.subclasificaciones.length){mensaje('paError','Selecciona al menos una subclasificación.');return;}
        form.querySelectorAll('input,select,button').forEach(n=>n.disabled=true);mensaje('paError','');
        try{
            if(confirmacion!==JSON.stringify(p)){
                const revision=await request(root.dataset.revisar,p);idActual=revision.regla.id;p.regla=revision.regla;
                const body=el('paRevision');body.replaceChildren();resumen(revision.regla,body);body.append(crear('p',revision.alcance,'part-help mb-0'));body.classList.remove('d-none');confirmacion=JSON.stringify(p);el('paConfirmar').textContent='Confirmar regla';return;
            }
            const resultado=await request(root.dataset.guardar,p);modal.hide();await cargar();mensaje('paMensaje',resultado.mensaje,'success');window.dispatchEvent(new CustomEvent('plan-diario-reglas-actualizadas'));
        }catch(error){mensaje('paError',error.message);}
        finally{form.querySelectorAll('input,select,button').forEach(n=>n.disabled=false);el('paTipoPlan').disabled=!!tipoOriginal;}
    });
    form.addEventListener('input',invalidar);form.addEventListener('change',invalidar);
    el('paTipoPlan').addEventListener('change',()=>subclasificaciones());
    el('paTipoRegla').addEventListener('change',()=>{if(el('paTipoRegla').value!=='Cero')el('paExcluir').checked=false;grupos();});
    el('paEditor').addEventListener('hidden.bs.modal',()=>cancelarBusquedas.forEach(f=>f()));
    async function cargarHistorial(){
        el('paHistorialContenido').textContent='Cargando historial…';el('paHistorialAnterior').disabled=true;el('paHistorialSiguiente').disabled=true;
        try{
            const url=new URL(root.dataset.historial,location.origin);url.searchParams.set('pagina',paginaHistorial);url.searchParams.set('tipoPlan',el('paFiltroTipo').value);const rows=await request(url),body=el('paHistorialContenido');body.replaceChildren();
            rows.forEach(raw=>{const r=Object.fromEntries(Object.entries(raw).map(([k,v])=>[k[0].toLowerCase()+k.slice(1),v]));const article=crear('article','','border rounded p-3 mb-3');article.append(crear('h6',`${r.accion} · ${r.tipoPlan} · ${r.usuario}`),crear('p',new Date(r.fecha).toLocaleString('es-MX'),'part-help'));for(const [titulo,json] of [['Antes',r.datosAntes],['Después',r.datosDespues]]){article.append(crear('strong',titulo));if(json)resumen(JSON.parse(json),article);else article.append(crear('p','Sin registro anterior.'));}body.append(article);});
            if(!rows.length)body.textContent='No hay cambios de reglas registrados.';el('paHistorialPagina').textContent=`Página ${paginaHistorial}`;el('paHistorialAnterior').disabled=paginaHistorial===1;el('paHistorialSiguiente').disabled=rows.length<25;
        }catch(error){el('paHistorialContenido').textContent=error.message;}
    }
    el('paNueva').addEventListener('click',()=>abrir(null));el('paActualizar').addEventListener('click',cargar);
    ['paFiltroTipo','paFiltroEstado'].forEach(id=>el(id).addEventListener('change',pintar));el('paBusqueda').addEventListener('input',pintar);
    el('paHistorial').addEventListener('click',()=>{paginaHistorial=1;historial.show();cargarHistorial();});
    el('paHistorialAnterior').addEventListener('click',()=>{paginaHistorial=Math.max(1,paginaHistorial-1);cargarHistorial();});el('paHistorialSiguiente').addEventListener('click',()=>{paginaHistorial++;cargarHistorial();});
    cargar();
})();
