(() => {
    'use strict';
    const motor = () => window.PlaneadorAfectacionesMotor;
    function contexto() {
        const root=document.getElementById('planeacionData'), json=document.getElementById('reglasAfectacionesDia');
        if(!root||!json||!motor())return null;
        return {root,definicion:JSON.parse(json.textContent),tipo:root.dataset.tipoPlan};
    }
    const filas = () => [...document.querySelectorAll('#tablaPlan tbody .fila-producto')];
    function aviso(texto, cambios=[], pendientes=[], guardando=false) {
        const body=document.getElementById('afectacionesDiaAviso'); if(!body)return;
        body.replaceChildren(); body.className=`alert ${texto||guardando&&pendientes.length?'alert-danger':pendientes.length?'alert-warning':cambios.length?'alert-info':'d-none'}`;
        const p=document.createElement('p');p.className='mb-0';p.textContent=texto||(pendientes.length?
            `${guardando?'No se puede guardar todavía. ':''}Hay distribuciones fuera de rango. Puedes seguir editando; corrige los pendientes antes de guardar.`:
            'Afectaciones aplicadas solo a este día. Pulsa Guardar para conservarlas.');body.append(p);
        if(pendientes.length) {
            const list=document.createElement('ul');list.className='mb-0 mt-2';
            const c=contexto(),reglas=c.definicion.planes[motor().tipo(c.tipo)];
            pendientes.forEach(p=>{
                const li=document.createElement('li');
                li.textContent=`${p.sku} · ${p.subclasificacion}: ${p.valor.toFixed(2)} (${p.valor>1?'excedente':'faltante'} de ${(p.valor>1?p.valor-1:-p.valor).toFixed(2)}). Debe quedar entre 0.00 y 1.00.`;
                if(p.fuentes.length)li.append(document.createTextNode(` Revisa ${p.fuentes.join(', ')}.`));
                const bases=[...new Set(reglas.descuentos.filter(d=>p.fuentes.includes(d.origen)&&p.fuentes.includes(d.destino)&&d.subclasificaciones.includes(motor().sub(p.subclasificacion))).map(d=>d.destino))];
                if(bases.length===1)li.append(document.createTextNode(` Para corregir el total ajusta la base ${bases[0]}; mover sus variantes redistribuye la proporción y conserva el total.`));
                const corregir=bases.length===1?bases[0]:p.fuentes[0]||p.sku;
                const fila=filas().find(f=>f.dataset.sku.trim().toUpperCase()===corregir);
                const input=fila?.querySelector(`.input-participacion[data-subclas="${p.subClasificacionId}"]`);
                if(input&&!input.readOnly) {
                    const boton=document.createElement('button');boton.type='button';boton.className='btn btn-sm btn-outline-dark ms-2';boton.textContent=`Editar ${corregir}`;
                    boton.setAttribute('aria-label',`Corregir ${p.sku} editando ${corregir}`);
                    boton.addEventListener('click',()=>{input.scrollIntoView({block:'center'});input.focus();input.select();});li.append(boton);
                }
                list.append(li);
            });body.append(list);
        }
        if(!texto&&cambios.length) {
            const list=document.createElement('ul');list.className='mb-0 mt-2';
            cambios.forEach(c=>{const li=document.createElement('li');li.textContent=`${c.sku} · ${c.subclasificacion}: ${c.antes.toFixed(2)} → ${c.despues.toFixed(2)} (${c.motivo})`;list.append(li);});body.append(list);
        }
    }
    function estado(inputEditado) {
        const subs=new Map(),valores=[];
        for(const fila of filas()) {
            for(const cell of fila.querySelectorAll('.input-participacion,.btn-agregar-participacion')) {
                const id=Number(cell.dataset.subclas); if(!id)continue;
                subs.set(id,{id,nombre:cell.dataset.subclasNombre||document.querySelector(`.input-participacion[data-subclas="${id}"],.btn-agregar-participacion[data-subclas="${id}"]`)?.dataset.subclasNombre||String(id)});
                const esInput=cell.classList.contains('input-participacion');
                valores.push({sku:fila.dataset.sku,subClasificacionId:id,partSub:esInput?motor().numero(cell===inputEditado?cell.dataset.prev||'0':cell.value||'0'):0,lineaCodigo:fila.dataset.linea||null});
            }
        }
        return {subs:[...subs.values()],valores};
    }
    function aplicar(result) {
        const rows=new Map(filas().map(f=>[f.dataset.sku.trim().toUpperCase(),f])),tocadas=new Set();
        const pendientes=new Set(result.pendientes.map(p=>`${p.sku}:${p.subClasificacionId}`));
        for(const v of result.valores) {
            const fila=rows.get(v.sku);if(!fila)continue;
            let input=fila.querySelector(`.input-participacion[data-subclas="${v.subClasificacionId}"]`);
            if(!input&&v.partSub!==0) {
                const boton=fila.querySelector(`.btn-agregar-participacion[data-subclas="${v.subClasificacionId}"]`);if(!boton)continue;
                input=document.createElement('input');input.type='number';input.className='form-control form-control-sm input-participacion';input.min='0';input.max='1';input.step='0.01';
                ['kg','canales','subclas','subclasNombre'].forEach(k=>input.dataset[k]=boton.dataset[k]||'');boton.replaceWith(input);
            }
            if(input) {
                input.value=v.partSub.toFixed(2);input.dataset.prev=input.value;
                const pendiente=pendientes.has(`${v.sku}:${v.subClasificacionId}`);
                input.classList.toggle('is-invalid',pendiente);input.setAttribute('aria-invalid',String(pendiente));tocadas.add(fila);
            }
        }
        for(const fila of tocadas)recalcularFilaDia(fila,false);
        recalcularInyeccion();recalcularSubtotalesMaster();
        marcar();
    }
    function marcar() {
        const c=contexto();if(!c)return;
        const reglas=c.definicion.planes[motor().tipo(c.tipo)];
        for(const fila of filas()) {
            const sku=fila.dataset.sku.trim().toUpperCase(), bloqueo=reglas.bloqueados.find(b=>b.sku===sku);
            for(const input of fila.querySelectorAll('.input-participacion')) {
                const nombre=motor().sub(input.dataset.subclasNombre||'');
                const suma=reglas.sumas.find(s=>s.destino===sku&&s.subclasificaciones.includes(nombre));
                input.readOnly=!!bloqueo||!!suma;
                input.title=bloqueo?bloqueo.motivo:suma?`Automático: ${suma.fuentes.join(' + ')}${suma.restar.length?' menos '+suma.restar.join(' + '):''}`:'';
                input.setAttribute('aria-label',`${sku} · ${input.dataset.subclasNombre||''}${input.readOnly?' · automático':''}`);
                if(bloqueo){input.value='0.00';input.dataset.prev='0.00';}
            }
        }
    }
    function recalcular(normalizar, input, guardando=false) {
        const c=contexto();if(!c){if(document.getElementById('planeacionData')){alert('Faltan los componentes de afectaciones. Recompila/reinicia la aplicación y vuelve a abrir el día antes de guardar.');return false;}return true;}
        const data=estado(input), fila=input?.closest('.fila-producto');
        try {
            const valor = input?.validity?.badInput ? NaN : motor().numero(input?.value || '0');
            const cambios=input?[{sku:fila.dataset.sku,subClasificacionId:Number(input.dataset.subclas),partSub:valor,lineaCodigo:fila.dataset.linea||null}]:[];
            // Un borrador con pendientes debe seguir siendo editable. Guardar exige resolverlos todos.
            const result=motor().simular(c.definicion,c.tipo,data.subs,data.valores,cambios,normalizar,true);
            aplicar(result);aviso('',input?result.cambios:[],result.pendientes,guardando);
            return !guardando||result.pendientes.length===0;
        }catch(error) {
            if(input){input.value=motor().numero(input.dataset.prev||'0').toFixed(2);recalcularFilaDia(fila);}
            aviso(error.message);marcar();return false;
        }
    }
    function iniciar() {
        // Recuperar capturas renderizadas con separador local antes de que un input number
        // vacío las convierta inadvertidamente en cero. El motor normaliza después a centésimas.
        for(const fila of filas())for(const input of fila.querySelectorAll('.input-participacion')) {
            if(input.dataset.prev === undefined || input.value === '') {
                const raw=input.dataset.prev ?? input.getAttribute('value');
                if(raw!==null && raw!==undefined && raw!=='') {
                    const value=motor().numero(raw);
                    if(Number.isFinite(value)){input.value=String(value);input.dataset.prev=String(value);}
                }
            }
        }
        return recalcular(true);
    }
    window.PlanDiarioAfectaciones={iniciar,cambiar:input=>recalcular(true,input),validar:()=>recalcular(true,undefined,true)};
})();
