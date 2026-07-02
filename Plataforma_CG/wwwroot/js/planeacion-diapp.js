// planeacion-diapp.js
// ===============================
// Planeador Producción (Partial-safe)
// ===============================

(function (window, document) {

    console.log("[Planeador] script cargado ✅");

    // =====================================================
    // 🔁 API pública para reinicializar desde la vista padre
    // =====================================================
    window.initPlaneadorProduccion = function () {
        console.log("[Planeador] initPlaneadorProduccion()");

        const table = document.getElementById("ppTable");
        if (!table) {
            console.warn("[Planeador] #ppTable no existe aún");
            return;
        }

        initDirtyCheck();
        initTableEvents(table);
        initProgramaciones();
        syncAllGroups();
    };

    // =====================================================
    // Dirty-check
    // =====================================================
    let isDirty = false;
    let suppress = false;

    function markDirty() { isDirty = true; }
    function clearDirty() { isDirty = false; }

    function initDirtyCheck() {

        if (window.__ppDirtyInit) return;
        window.__ppDirtyInit = true;

        window.addEventListener("beforeunload", function (e) {
            if (!isDirty) return;
            e.preventDefault();
            e.returnValue = "";
        });

        document.addEventListener("click", function (e) {
            const a = e.target.closest("a.js-planLink");
            if (!a) return;

            if (isDirty) {
                const ok = confirm("Tienes cambios sin guardar. ¿Seguro que quieres continuar?");
                if (!ok) {
                    e.preventDefault();
                    e.stopPropagation();
                }
            }
        });
    }

    // =====================================================
    // Helpers
    // =====================================================
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

    // =====================================================
    // Cálculos por fila
    // =====================================================
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
        tr.querySelector(".js-canales")?.textContent = Math.round(canales);
        tr.querySelector(".js-piezas")?.textContent = Math.round(canales);

        const kgBase =
            (topkg1 * topc1 * vg1) +
            (topkg2 * topc2 * vg2) +
            (topkg3 * topc3 * vr);

        const kgLote = kgBase * rend;
        tr.querySelector(".js-kglote")?.textContent = format2(kgLote);
    }

    // =====================================================
    // Grupos
    // =====================================================
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
            if (!inp || r === master) return;
            inp.value = tenthsToStr(compT);
        });
        suppress = false;

        rows.forEach(recalcRow);
    }

    function syncAllGroups() {
        const rows = [...document.querySelectorAll(".js-row")];
        const groups = [...new Set(rows.map(r => r.dataset.group).filter(Boolean))];
        groups.forEach(g =>
            ["js-vg1", "js-vg2", "js-vr"].forEach(k => syncGroupFromMaster(g, k))
        );
    }

    // =====================================================
    // Eventos de tabla
    // =====================================================
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

        table.addEventListener("change", e => {
            const t = e.target;
            if (!t || suppress) return;

            if (t.matches(".js-inyPick,.js-desPick")) {
                markDirty();
                recalcRow(t.closest(".js-row"));
            }
        });
    }

    // =====================================================
    // Programaciones
    // =====================================================
    function initProgramaciones() {

        const sel = document.getElementById("ppProgramacionId");
        if (!sel) {
            console.warn("[PlaneadorPP] No existe #ppProgramacionId");
            return;
        }

        // Evitar doble binding en parciales
        if (sel.dataset.ppInit === "1") return;
        sel.dataset.ppInit = "1";

        console.log("[PlaneadorPP] initProgramaciones OK");

        sel.addEventListener("change", () => {
            markDirty();

            const val = parseInt(sel.value || "0", 10);
            if (!val || val <= 0) {
                console.warn("[PlaneadorPP] Programación inválida");
            }
        });
    }

    // =====================================================
    // 💾 GUARDAR (GLOBAL)
    // =====================================================
    window.ppSavePlan = async function () {

        const rows = [...document.querySelectorAll(".js-row")];
        if (!rows.length) {
            alert("No hay datos para guardar");
            return;
        }

        const payload = {
            fechaPlan: document.getElementById("ppFechaPlan")?.value,
            rows: rows.map(tr => ({
                canales: tr.querySelector(".js-canales")?.textContent,
                piezas: tr.querySelector(".js-piezas")?.textContent,
                observaciones: tr.querySelector(".js-obs")?.value
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

})(window, document);
