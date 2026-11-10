(function (root, factory) {
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    else root.PlaneadorAfectacionesMotor = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
    'use strict';
    const sku = value => String(value || '').trim().toUpperCase();
    const tipo = value => sku(value) === 'BG' ? 'VG' : sku(value);
    function numero(value) {
        if (typeof value === 'number') return value;
        if (typeof value !== 'string') return NaN;
        const text = value.trim();
        return /^[+-]?(?:\d+(?:[.,]\d*)?|[.,]\d+)$/.test(text) ? Number(text.replace(',', '.')) : NaN;
    }
    function sub(nombre) {
        const value = sku(nombre).replace(/[ .-]/g, '');
        return ({ PREMIUN:'PREMIUM', PREMIUM:'PREMIUM', LINEA:'NOV-LINEA', NOVLINEA:'NOV-LINEA', SELECTO:'SELECTO', VGBIG:'VG-BIG', BIG:'VG-BIG', VGLINEA:'VG-LINEA', REG:'VR', REGULAR:'VR', VR:'VR', TERAMA:'T.Amarilla', TAMARILLA:'T.Amarilla', TERAMARILLA:'T.Amarilla' })[value] || value;
    }
    function simular(definicion, plan, subclasificaciones, originales, cambios = [], normalizar = false, permitirPendientes = false) {
        const reglas = definicion.planes[tipo(plan)];
        if (!reglas) throw new Error('El plan de afectaciones debe ser NOV o VG.');
        const ids = new Set(subclasificaciones.map(s => s.id));
        const centavos = value => Math.round((numero(value) + Number.EPSILON) * 100);
        for (const v of [...originales, ...cambios])
        {
            if (!sku(v.sku)) throw new Error('Selecciona un SKU válido para editar su distribución.');
            if (!ids.has(v.subClasificacionId)) throw new Error(`La subclasificación de ${v.sku} no pertenece al plan. Vuelve a abrir el día.`);
            const value = numero(v.partSub);
            if (!Number.isFinite(value))
                throw new Error(`${v.sku}: la distribución debe estar entre 0.00 y 1.00; por ejemplo, 0.98 equivale al 98%.`);
        }
        for (const v of cambios)
        {
            if (numero(v.partSub) < 0 || numero(v.partSub) > 1)
                throw new Error(`${v.sku}: la distribución debe estar entre 0.00 y 1.00; por ejemplo, 0.98 equivale al 98%.`);
            if (Math.abs(numero(v.partSub) * 100 - centavos(v.partSub)) > 1e-7)
                throw new Error(`${v.sku}: captura de 0.00 a 1.00 con hasta dos decimales, por ejemplo 0.98 (98%).`);
        }
        originales = originales.map(v => ({...v, partSub:centavos(v.partSub)/100}));
        cambios = cambios.map(v => ({...v, partSub:centavos(v.partSub)/100}));
        const key = v => `${sku(v.sku)}:${v.subClasificacionId}`;
        if (new Set(originales.map(key)).size !== originales.length || new Set(cambios.map(key)).size !== cambios.length) throw new Error('Hay distribuciones repetidas para el mismo SKU y subclasificación.');
        const salida = new Map(originales.map(v => [key(v), {...v,sku:sku(v.sku)}])), resultados = [], afectados = new Set();
        for (const clas of subclasificaciones) {
            const nombre = sub(clas.nombre), before = new Map(originales.filter(v => v.subClasificacionId === clas.id).map(v => [sku(v.sku),centavos(v.partSub)]));
            const editados = new Map(cambios.filter(v => v.subClasificacionId === clas.id).map(v => [sku(v.sku),v]));
            const presentes = new Set([...before.keys(),...editados.keys()]);
            const sumas = new Map(reglas.sumas.filter(r => r.subclasificaciones.includes(nombre)).map(r => [r.destino,r]));
            let descuentos = reglas.descuentos.filter(r => r.subclasificaciones.includes(nombre)).map(r => ({origen:r.origen,destino:r.destino}));
            const modos=new Map(descuentos.map(d=>[`${d.origen}:${d.destino}`,{antes:true,despues:true}]));
            originales.filter(v => v.subClasificacionId === clas.id && !reglas.conocidos.includes(sku(v.sku))).forEach(v => {
                const anterior=sku(v.lineaCodigo)||null,siguiente=sku(editados.has(sku(v.sku))?editados.get(sku(v.sku)).lineaCodigo:v.lineaCodigo)||null;
                for(const padre of new Set([anterior,siguiente].filter(Boolean))){const d={origen:sku(v.sku),destino:padre};descuentos.push(d);modos.set(`${d.origen}:${d.destino}`,{antes:padre===anterior,despues:padre===siguiente});}
            });
            descuentos = [...new Map(descuentos.map(d => [`${d.origen}:${d.destino}`,d])).values()];
            const aristas = [...new Map([...descuentos,...[...sumas.values()].flatMap(s => [...s.fuentes,...s.restar].map(f => ({origen:f,destino:s.destino})))].map(d => [`${d.origen}:${d.destino}`,d])).values()];
            const nodos = new Set([...presentes,...aristas.flatMap(a => [a.origen,a.destino])]);
            const grados = new Map([...nodos].map(n => [n,aristas.filter(a => a.destino===n).length]));
            const cola = [...nodos].filter(n => grados.get(n)===0), orden = [];
            while(cola.length) { const n=cola.shift(); orden.push(n); aristas.filter(a=>a.origen===n).forEach(a=>{grados.set(a.destino,grados.get(a.destino)-1);if(grados.get(a.destino)===0)cola.push(a.destino);}); }
            if(orden.length!==nodos.size)throw new Error('Las afectaciones contienen un ciclo. Revisa las líneas de los productos.');
            const tocados=new Set(editados.keys()), actuales=new Map(before);
            if(normalizar)[...sumas.keys(),...reglas.bloqueados.map(b=>b.sku)].forEach(s=>tocados.add(s));
            for(const [s,v] of editados)actuales.set(s,centavos(v.partSub));
            const antes=s=>before.get(s)||0, actual=s=>actuales.get(s)||0;
            const deltaDe=d=>{const m=modos.get(`${d.origen}:${d.destino}`);return (m.despues?actual(d.origen):0)-(m.antes?antes(d.origen):0);};
            for(const s of orden) {
                if(!tocados.has(s))continue;
                const bloqueo=reglas.bloqueados.find(b=>b.sku===s), suma=sumas.get(s);
                let valor=actual(s), motivo='Captura manual';
                if(bloqueo) { if(editados.has(s)&&centavos(editados.get(s).partSub)!==0)throw new Error(`${s}: ${bloqueo.motivo} en ${tipo(plan)}.`); valor=0;motivo=bloqueo.motivo; }
                else if(suma) { valor=suma.fuentes.reduce((a,f)=>a+actual(f),0)-suma.restar.reduce((a,f)=>a+actual(f),0); motivo=`Suma de ${suma.fuentes.join(' + ')}${suma.restar.length?' menos '+suma.restar.join(' + '):''}`; }
                else { const entradas=descuentos.filter(d=>d.destino===s), delta=entradas.reduce((a,d)=>a+deltaDe(d),0); valor-=delta;
                    if(delta)motivo='Descuento/restitución por '+entradas.filter(d=>deltaDe(d)!==0).map(d=>d.origen).join(', '); }
                if(!presentes.has(s)) { if((!sumas.has(s)&&valor!==0&&descuentos.some(d=>d.destino===s&&deltaDe(d)!==0))||(sumas.has(s)&&sumas.get(s).restar.some(f=>actual(f)!==antes(f))))throw new Error(`Falta ${s} en ${clas.nombre}; agrégalo al plan antes de mover el producto que lo afecta.`); continue; }
                actuales.set(s,valor);
                const k=`${s}:${clas.id}`, prev=salida.get(k);
                afectados.add(k);
                salida.set(k,{sku:s,subClasificacionId:clas.id,partSub:valor/100,lineaCodigo:editados.has(s)?editados.get(s).lineaCodigo??null:prev?.lineaCodigo??null});
                if(valor!==antes(s))resultados.push({sku:s,subClasificacionId:clas.id,subclasificacion:clas.nombre,antes:antes(s)/100,despues:valor/100,motivo});
                aristas.filter(a=>a.origen===s).forEach(a=>tocados.add(a.destino));
            }
        }
        const pendientes=[...salida.values()].filter(v=>v.partSub<0||v.partSub>1).map(v=>{
            const clas=subclasificaciones.find(s=>s.id===v.subClasificacionId);
            const suma=reglas.sumas.find(s=>s.destino===v.sku&&s.subclasificaciones.includes(sub(clas.nombre)));
            return {sku:v.sku,subClasificacionId:clas.id,subclasificacion:clas.nombre,valor:v.partSub,fuentes:suma?.fuentes||[]};
        });
        const invalidos=pendientes.filter(p=>normalizar||afectados.has(key(p)));
        if(!permitirPendientes&&invalidos.length)
            throw new Error('No se puede guardar: '+invalidos.map(p=>`${p.sku} en ${p.valor.toFixed(2)} para ${p.subclasificacion}`).join('; ')+'. Debe quedar entre 0 y 1. Puedes seguir editando los productos de origen para corregirlo.');
        return {valores:[...salida.values()],cambios:resultados,pendientes};
    }
    return {simular,sub,tipo,numero};
});
