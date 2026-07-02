// ============================================
// Planeador de Producción - Módulo Global
// ============================================
console.log("🟢 planeador-produccion.js inició");

(function () {

    if (window.PlaneadorPP) return;

    let isDirty = false;
    let suppress = false;

    function markDirty() { isDirty = true; }
    function clearDirty() { isDirty = false; }

    const toNum = v => {
        const n = parseFloat((v ?? "").toString().replace(",", "."));
        return isNaN(n) ? 0 : n;
    };

    const toTenths = v => Math.round(Math.max(0, Math.min(1, toNum(v))) * 10);
    const tenthsToStr = t => (Math.max(0, Math.min(10, t)) / 10).toFixed(1);

    function format2(n) {
        return (n || n === 0)
            ? n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })
            : "";
    }

    function recalcRow(tr) {
        if (!tr) return;

        const topc1 = toNum(tr.dataset.topc1);
        const topc2 = toNum(tr.dataset.topc2);
        const topc3 = toNum(tr.dataset.topc3);

        const topkg1 = toNum(tr.dataset.topkg1);
        const topkg2 = toNum(tr.dataset.topkg2);
        const topkg3 = toNum(tr.dataset.topkg3);

        const rend = toNum(tr.dataset.rend);

        const vg1 = toNum(tr.querySelector(".js-vg1")?.value);
        const vg2 = toNum(tr.querySelector(".js-vg2")?.value);
        const vr = toNum(tr.querySelector(".js-vr")?.value);

        const canales = (topc1 * vg1) + (topc2 * vg2) + (topc3 * vr);
        const elCan = tr.querySelector(".js-canales");
        if (elCan) elCan.textContent = Math.round(canales);
        const elPz = tr.querySelector(".js-piezas");
        if (elPz) elPz.textContent = Math.round(canales);

        const kgBase =
            (topkg1 * topc1 * vg1) +
            (topkg2 * topc2 * vg2) +
            (topkg3 * topc3 * vr);

        const kgLote = kgBase * rend;
        const elKg = tr.querySelector(".js-kglote");
        if (elKg) elKg.textContent = format2(kgLote);
    }

    function syncGroupFromMaster(group, key) {
        const rows = [...document.querySelectorAll(`.js-row[data-group="${group}"]`)];
        if (!rows.length) return;

        const master = rows.find(r => r.dataset.nivel === "0") || rows[0];
        const masterInp = master.querySelector("." + key);
        if (!masterInp) return;

        const mT = toTenths(masterInp.value);
        const compT = 10 - mT;

        suppress = true;
        rows.forEach(r => {
            const inp = r.querySelector("." + key);
            if (inp && r !== master) inp.value = tenthsToStr(compT);
        });
        suppress = false;

        rows.forEach(recalcRow);
    }

    function initTableEvents(table) {
        table.addEventListener("input", e => {
            const t = e.target;
            if (!t || suppress) return;

            if (t.matches(".js-vg1,.js-vg2,.js-vr")) {
                markDirty();
                syncGroupFromMaster(
                    t.closest(".js-row")?.dataset.group,
                    t.classList.contains("js-vg1") ? "js-vg1" :
                        t.classList.contains("js-vg2") ? "js-vg2" : "js-vr"
                );
            }

            if (t.classList.contains("js-obs")) markDirty();
        });
    }

    // ============================================
    // INIT GLOBAL
    // ============================================
    window.initPlaneadorProduccion = function () {
        console.log("🟢 initPlaneadorProduccion()");

        const table = document.getElementById("ppTable");
        if (!table) {
            console.warn("⚠️ #ppTable no existe");
            return;
        }

        initTableEvents(table);
        initProgramaciones();
    };
    function initProgramaciones() {

        const sel = document.getElementById("ppProgramacionId");

        // 🔁 AÚN NO EXISTE → reintentar en el siguiente frame
        if (!sel) {
            requestAnimationFrame(initProgramaciones);
            return;
        }

        // Evitar doble binding
        if (sel.dataset.ppInit === "1") return;
        sel.dataset.ppInit = "1";

        console.log("[PlaneadorPP] initProgramaciones OK");

        sel.addEventListener("change", async () => {

            const programacionId = parseInt(sel.value || "0", 10);
            if (!programacionId) return;

            if (isDirty) {
                const ok = confirm("Hay cambios sin guardar. ¿Deseas continuar?");
                if (!ok) {
                    sel.value = "";
                    return;
                }
            }

            try {
                const res = await fetch(
                    `/Operaciones/PlaneadorProduccionCargar?programacionId=${programacionId}`,
                    { headers: { "X-Requested-With": "XMLHttpRequest" } }
                );

                if (!res.ok) {
                    alert("No se pudo cargar la programación");
                    return;
                }

                const html = await res.text();

                const cont = document.getElementById("ppContainer");
                if (!cont) {
                    console.error("#ppContainer no existe");
                    return;
                }

                cont.innerHTML = html;

                clearDirty();

                // 🔁 Reenganchar eventos
                requestAnimationFrame(() => {
                    if (typeof window.initPlaneadorProduccion === "function") {
                        window.initPlaneadorProduccion();
                    }
                });

            } catch (err) {
                console.error(err);
                alert("Error cargando la programación");
            }
        });
    }


    // ============================================
    // GUARDAR (GLOBAL)
    // ============================================
    window.ppSavePlan = async function () {
        console.log("💾 ppSavePlan()");

        const rows = [...document.querySelectorAll(".js-row")];
        if (!rows.length) {
            alert("No hay datos para guardar");
            return;
        }

        const payload = {
            fechaPlan: document.getElementById("ppFechaPlan")?.value,
            tipoPlan: document.getElementById("ppTipoPlan")?.value || "VG",
            programacionId: Number(
                document.getElementById("ppProgramacionId")?.value || 0
            ),

            planTexto: document.getElementById("ppPlanTexto")?.value || "",

            rows: rows.map((tr, idx) => ({
                groupKey: tr.dataset.group || null,
                nivel: Number(tr.dataset.nivel || 0),
                orden: idx + 1,

                desSku: tr.dataset.desSku || "",
                desProducto: tr.dataset.desProducto || "",

                col1: tr.querySelector(".js-vg1")?.value || "0",
                col2: tr.querySelector(".js-vg2")?.value || "0",
                col3: tr.querySelector(".js-vr")?.value || "0",

                rendPct: tr.dataset.rend || "0",

                kgLote: tr.querySelector(".js-kglote")?.textContent || "0",
                canales: tr.querySelector(".js-canales")?.textContent || "0",
                subtotal: tr.querySelector(".js-subtotal")?.textContent || "0",
                piezas: tr.querySelector(".js-piezas")?.textContent || "0", 

                inySku: tr.dataset.inySku || "",
                inyProducto: tr.dataset.inyProducto || "",
                inyPct: tr.dataset.inyPct || "",
                inyModo: tr.dataset.inyModo || "",

                almacen: tr.dataset.almacen || "",
                manejo: tr.dataset.manejo || "",
                etiquetado: tr.dataset.etiquetado || "",

                observaciones: tr.querySelector(".js-obs")?.value || ""
            }))
        };


        const res = await fetch("/Operaciones/PlaneadorProduccionGuardar", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload)
        });

        if (!res.ok) {
            alert("Error al guardar");
            return;
        }

        alert("Guardado correctamente ✅");
        clearDirty();
    };

    console.log("🟢 planeador-produccion.js terminó");
    // ============================================
    // 📄 PDF / Vista previa
    // ============================================

})();
