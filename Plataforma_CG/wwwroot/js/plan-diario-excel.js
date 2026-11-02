(function (root) {
    "use strict";

    function numero(value, etiqueta) {
        const text = String(value ?? "").trim().replace(/%$/, "").replace(",", ".");
        const parsed = text === "" ? NaN : Number(text);
        if (!Number.isFinite(parsed)) throw new Error(`No se pudo leer ${etiqueta}. Espera a que termine de cargar el plan o actualízalo.`);
        return parsed;
    }

    function crearSolicitud(doc) {
        const data = doc.getElementById("planeacionData");
        const tabla = doc.getElementById("tablaPlan");
        const canales = doc.getElementById("canalesPlanDiarioExcel");
        if (!data || !tabla || !canales) throw new Error("Abre un plan diario antes de exportar.");
        const texto = (fila, selector) => (fila.querySelector(selector)?.textContent || "").trim();
        const productos = [...tabla.querySelectorAll("tbody .fila-producto")].map(fila => {
            const nombreConversion = texto(fila, ".td-iny-nombre");
            const etiqueta = texto(fila, ".td-etiq");
            const dias = texto(fila, ".td-caduc");
            if ([nombreConversion, etiqueta, dias].some(value => value === "..."))
                throw new Error("Espera a que termine de cargar la información de inyección y etiquetación antes de exportar.");
            return {
                Sku: (fila.dataset.sku || "").trim(),
                Nombre: texto(fila, ".td-nombre") || fila.children[2]?.textContent.trim() || "",
                Master: fila.dataset.master || "SIN MASTER",
                Porcentaje: numero(fila.dataset.porcentaje, "el rendimiento del producto"),
                SkuConversion: fila.querySelector(".select-conversion")?.value || fila.dataset.sku,
                NombreConversion: nombreConversion,
                PorcentajeInyeccion: numero(texto(fila, ".td-iny-porcentaje"), "el porcentaje de inyección"),
                Etiqueta: etiqueta,
                DiasCaducidad: dias,
                Participaciones: [...fila.querySelectorAll(".input-participacion, .btn-agregar-participacion")].map(control => ({
                    SubClasificacionId: numero(control.dataset.subclas, "la subclasificación"),
                    Proporcion: control.classList.contains("input-participacion") ? numero(control.value, "la participación") : 0
                }))
            };
        });
        if (!productos.length) throw new Error("El plan no contiene productos para exportar.");
        return { Fecha: data.dataset.fecha, TipoPlan: data.dataset.tipoPlan, Canales: JSON.parse(canales.textContent), Productos: productos };
    }

    async function exportar(doc = root.document) {
        const button = doc.getElementById("btnExportarPlanDiario");
        if (button?.disabled) return;
        try {
            if (button) button.disabled = true;
            const plan = crearSolicitud(doc);
            const response = await root.fetch("/Operaciones/ExportarPlanDiarioExcel", {
                method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(plan)
            });
            if (!response.ok) {
                const error = await response.json().catch(() => ({}));
                throw new Error(error.mensaje || "No se pudo generar el Excel del plan diario.");
            }
            const contentType = response.headers.get("Content-Type") || "";
            if (!contentType.includes("spreadsheetml")) throw new Error("No se recibió un archivo Excel. Verifica tu sesión y vuelve a intentarlo.");
            const blob = await response.blob();
            const url = root.URL.createObjectURL(blob);
            const link = doc.createElement("a");
            link.href = url;
            const [year, month, day] = plan.Fecha.split("-");
            link.download = `PLAN ${plan.TipoPlan} ${day}-${month}-${year}.xlsx`;
            doc.body.appendChild(link);
            link.click();
            link.remove();
            root.setTimeout(() => root.URL.revokeObjectURL(url), 1000);
        } finally {
            if (button) button.disabled = false;
        }
    }

    const api = { crearSolicitud, exportar };
    root.PlanDiarioExcel = api;
    if (typeof module !== "undefined" && module.exports) module.exports = api;
})(typeof window !== "undefined" ? window : globalThis);
