function exportarExcelSemanal() {

    const tabla = document.getElementById("tablaPlanSemanal");
    if (!tabla) return;

    const tituloBase = document.querySelector("h3")?.innerText || "Plan Semanal";
    const tipoPlan = document.getElementById("comboTipoPlan")?.value || "PLAN";

    const nombreArchivo = `${tituloBase} - ${tipoPlan}`;

    // 🔥 clonar tabla
    const tablaClon = tabla.cloneNode(true);

    // 🔥 inputs → texto
    tablaClon.querySelectorAll("input").forEach(input => {
        const td = input.closest("td");
        if (td) td.innerText = input.value;
    });

    const wb = XLSX.utils.book_new();
    const ws = XLSX.utils.table_to_sheet(tablaClon);

    // 🔥 insertar título
    XLSX.utils.sheet_add_aoa(ws, [[nombreArchivo]], { origin: "A1" });

    // 🔥 mover tabla 2 filas abajo
    const rango = XLSX.utils.decode_range(ws["!ref"]);

    for (let R = rango.e.r; R >= 0; --R) {
        for (let C = rango.e.c; C >= 0; --C) {
            const oldCell = XLSX.utils.encode_cell({ r: R, c: C });
            const newCell = XLSX.utils.encode_cell({ r: R + 2, c: C });
            ws[newCell] = ws[oldCell];
        }
    }

    ws["!ref"] = XLSX.utils.encode_range({
        s: { r: 0, c: 0 },
        e: { r: rango.e.r + 2, c: rango.e.c }
    });

    // ================= ESTILOS BODY =================
    const filas = tabla.querySelectorAll("tbody tr");

    filas.forEach((fila, i) => {

        const celdas = fila.querySelectorAll("td");

        celdas.forEach((td, j) => {

            const r = i + 3;
            const c = j;

            const ref = XLSX.utils.encode_cell({ r, c });

            if (!ws[ref]) return;

            const valor = ws[ref].v;

            // 🔢 formato numérico
            if (!isNaN(valor) && valor !== "") {
                ws[ref].t = "n";
                ws[ref].z = "0.00";
            }

            // 🎨 colores (compatibles con semanal)
            let color = null;

            if (td.classList.contains("td-0")) color = "F8B26A";
            else if (td.classList.contains("td-1")) color = "58D68D";
            else if (td.classList.contains("td-parcial")) color = "5DADE2";
            else if (td.classList.contains("td-codigo")) color = "FDF2D0";

            ws[ref].s = {
                fill: color ? { fgColor: { rgb: color } } : undefined,
                border: {
                    top: { style: "thin", color: { rgb: "000000" } },
                    bottom: { style: "thin", color: { rgb: "000000" } },
                    left: { style: "thin", color: { rgb: "000000" } },
                    right: { style: "thin", color: { rgb: "000000" } }
                },
                alignment: { horizontal: "center", vertical: "middle" }
            };
        });
    });

    // ================= HEADER =================
    const headerRange = XLSX.utils.decode_range(ws["!ref"]);

    for (let C = 0; C <= headerRange.e.c; C++) {

        const ref = XLSX.utils.encode_cell({ r: 2, c: C });

        if (!ws[ref]) continue;

        ws[ref].s = {
            fill: { fgColor: { rgb: "D5D8DC" } },
            font: { bold: true },
            alignment: { horizontal: "center" },
            border: {
                top: { style: "thin" },
                bottom: { style: "thin" },
                left: { style: "thin" },
                right: { style: "thin" }
            }
        };
    }

    // ❄️ freeze (header semanal)
    ws["!freeze"] = { xSplit: 0, ySplit: 3 };

    // 📏 ancho columnas
    const cols = [];
    for (let i = 0; i <= rango.e.c; i++) {
        cols.push({ wch: 18 });
    }
    ws["!cols"] = cols;

    XLSX.utils.book_append_sheet(wb, ws, "Plan Semanal");

    XLSX.writeFile(wb, `${nombreArchivo}.xlsx`);
}


function aplicarColorSemanal(input) {

    let valor = parseFloat(input.value) || 0;
    const td = input.closest("td");

    td.classList.remove("td-0", "td-parcial", "td-1");

    if (valor === 0) td.classList.add("td-0");
    else if (valor === 1) td.classList.add("td-1");
    else if (valor > 0 && valor < 1) td.classList.add("td-parcial");
}

document.addEventListener("input", function (e) {

    if (!e.target.classList.contains("input-participacion-semanal"))
        return;

    const input = e.target;
    const fila = input.closest(".fila-producto");

    const productoPadre = fila.dataset.linea;

    const valorAnterior = parseFloat(input.dataset.prev) || 0;
    const valorNuevo = parseFloat(input.value) || 0;

    const diferencia = valorNuevo - valorAnterior;

    input.dataset.prev = valorNuevo;

    // 🔥 SI ES DERIVADO → RESTAR AL PADRE
    if (productoPadre && productoPadre !== "") {

        const subclas = input.dataset.subclas;

        const filaPadre = document.querySelector(
            `#tablaPlanSemanal .fila-producto[data-sku='${productoPadre}']`
        );

        if (filaPadre) {

            const inputPadre = filaPadre.querySelector(
                `.input-participacion-semanal[data-subclas='${subclas}']`
            );

            if (inputPadre) {

                let valorPadre = parseFloat(inputPadre.value) || 0;

                valorPadre -= diferencia;

                if (valorPadre < 0) valorPadre = 0;

                inputPadre.value = valorPadre.toFixed(2);

                aplicarColorSemanal(inputPadre);
                recalcularFilaSemanal(filaPadre);
            }
        }
    }

    recalcularFilaSemanal(fila);
});






















// ================= VARIABLES =================
let resultadosBusqueda = [];
let indiceActual = -1;
let ultimoTexto = "";

// ================= EVENTOS =================
document.addEventListener("keydown", function (e) {

    const input = document.getElementById("buscadorProductos");
    if (!input) return;

    const activo = document.activeElement === input;

    // ENTER → siguiente resultado
    if (activo && e.key === "Enter") {
        e.preventDefault();
        buscarYScroll();
    }

    // F3 → siguiente resultado
    if (e.key === "F3") {
        e.preventDefault();
        buscarYScroll();
    }

    // ↑ ↓ navegación
    if (activo && (e.key === "ArrowDown" || e.key === "ArrowUp")) {

        e.preventDefault();

        if (!resultadosBusqueda.length) return;

        if (e.key === "ArrowDown") {
            indiceActual++;
        } else {
            indiceActual--;
        }

        if (indiceActual >= resultadosBusqueda.length) indiceActual = 0;
        if (indiceActual < 0) indiceActual = resultadosBusqueda.length - 1;

        hacerScrollYResaltar(resultadosBusqueda[indiceActual]);
        actualizarContador();
    }
});

// RESET cuando cambias texto
document.addEventListener("input", function (e) {

    if (e.target.id === "buscadorProductos") {
        indiceActual = -1;
        resultadosBusqueda = [];
        ultimoTexto = "";
    }
});

// ================= BUSCAR =================
function buscarYScroll() {

    const input = document.getElementById("buscadorProductos");
    if (!input) return;

    const texto = input.value.trim().toLowerCase();
    if (!texto) return;

    const filas = document.querySelectorAll("#tablaPlanSemanal .fila-producto");
    if (!filas.length) return;

    // 🔥 reconstruir si cambió texto
    if (texto !== ultimoTexto) {

        resultadosBusqueda = [];
        indiceActual = -1;

        filas.forEach(fila => {

            const codigo = fila.querySelector(".td-codigo")?.innerText.toLowerCase() || "";
            const nombre = fila.children[1]?.innerText.toLowerCase() || "";

            if (codigo.includes(texto) || nombre.includes(texto)) {
                resultadosBusqueda.push(fila);
            }
        });

        ultimoTexto = texto;

        if (!resultadosBusqueda.length) {
            input.classList.add("is-invalid");
            setTimeout(() => input.classList.remove("is-invalid"), 1000);
            return;
        }
    }

    // avanzar índice
    indiceActual++;

    if (indiceActual >= resultadosBusqueda.length) {
        indiceActual = 0;
    }

    const fila = resultadosBusqueda[indiceActual];

    hacerScrollYResaltar(fila);
    actualizarContador();
}

// ================= SCROLL + HIGHLIGHT =================
function hacerScrollYResaltar(fila) {

    if (!fila) return;

    fila.scrollIntoView({
        behavior: "smooth",
        block: "center"
    });

    // limpiar anteriores
    document.querySelectorAll(".fila-producto")
        .forEach(f => f.classList.remove("table-warning"));

    // resaltar actual
    setTimeout(() => {
        fila.classList.add("table-warning");
    }, 200);
}

// ================= CONTADOR =================
function actualizarContador() {

    const input = document.getElementById("buscadorProductos");
    if (!input) return;

    const total = resultadosBusqueda.length;
    const actual = indiceActual + 1;

    input.title = total > 0
        ? `${actual} de ${total}`
        : "Sin resultados";
}










async function actualizarPlaneacionSemanal(btn) {

    const contenedor = btn.closest("#contenidoPlantillaSemanal");

    const fila = contenedor.querySelector(".fila-producto");

    const fechaInicio = fila.dataset.fechaInicio;
    const fechaFin = fila.dataset.fechaFin;

    const combo = document.getElementById("comboTipoPlan");
    const tipoPlan = combo.value;

    try {

        const response = await fetch(
            `/Operaciones/PlantillaSemanal?plan=${tipoPlan}&fechain=${fechaInicio}&fechafin=${fechaFin}`
        );

        const html = await response.text();

        document.getElementById("contenidoPlantillaSemanal").innerHTML = html;

        // 🔥 MUY IMPORTANTE
        inicializarPlanSemanal();

    } catch (error) {
        console.error("Error:", error);
    }
}


// ================= GUARDAR =================
function guardarPlanSemanal() {

    const filas = document.querySelectorAll("#tablaPlanSemanal tbody .fila-producto");

    const lista = [];

    filas.forEach(fila => {

        const sku = fila.dataset.sku;
        const fechaInicio = fila.dataset.fechaInicio;
        const fechaFin = fila.dataset.fechaFin;
        const porcentaje = parseFloat(fila.dataset.porcentaje) || 0;

        const inputs = fila.querySelectorAll(".input-participacion-semanal");

        inputs.forEach(input => {

            const partSub = parseFloat(input.value) || 0;
            const subClas = parseInt(input.dataset.subclas) || 0;
            const peso = parseFloat(input.dataset.peso) || 0;

            // 👉 solo guardar si tiene valor (opcional)
            if (partSub > 0) {

                lista.push({
                    ProductoCodigo: sku,
                    Porcentaje: porcentaje,
                    Peso: peso,
                    FechaInicio: fechaInicio,
                    FechaFin: fechaFin,
                    SubClas: subClas,
                    PartSub: partSub
                });

            }

        });

    });

    fetch("/Operaciones/GuardarPlanSemanal", {
        method: "POST",
        headers: {
            "Content-Type": "application/json"
        },
        body: JSON.stringify(lista)
    })
        .then(r => r.json())
        .then(data => {

            if (data.ok) {
                alert("Plan semanal guardado correctamente");
            }

        })
        .catch(err => {
            console.error(err);
            alert("Error al guardar");
        });
}


// ================= COLORES =================
function aplicarColorSemanal(input) {

    let valor = parseFloat(input.value) || 0;
    const td = input.closest("td");

    td.classList.remove("td-0", "td-parcial", "td-1");

    if (valor === 0) td.classList.add("td-0");
    else if (valor === 1) td.classList.add("td-1");
    else td.classList.add("td-parcial");
}


// ================= CALCULO =================
//function recalcularFilaSemanal(fila) {

//    let totalKg = 0;

//    const inputs = fila.querySelectorAll(".input-participacion-semanal");

//    inputs.forEach(input => {

//        aplicarColorSemanal(input);

//        let participacion = parseFloat(input.value);
//        let canales = parseFloat(input.dataset.total);
//        let peso = parseFloat(input.dataset.peso);
//        let porc = parseFloat(input.dataset.porcentaje);

//        if (isNaN(participacion)) participacion = 0;
//        if (isNaN(canales)) canales = 0;
//        if (isNaN(peso)) peso = 0;
//        if (isNaN(porc)) porc = 0;

//        totalKg += (participacion * canales * peso * porc) / 100;

//    });

//    fila.querySelector(".total-calculado").textContent =
//        totalKg.toFixed(2);
//}

function recalcularFilaSemanal(fila) {

    const porcentaje = parseFloat(fila.dataset.porcentaje) || 0;

    let totalCalculado = 0;
    let totalSemanal = 0;

    const inputs = fila.querySelectorAll(".input-participacion-semanal");

    inputs.forEach(input => {

        aplicarColorSemanal(input);

        let part = parseFloat(input.value) || 0;
        let total = parseFloat(input.dataset.total) || 0;
        let peso = parseFloat(input.dataset.peso) || 0;

        totalCalculado += (porcentaje / 100) * part * total * peso;
        totalSemanal += part * total;
    });

    fila.querySelector(".total-calculado").textContent =
        totalCalculado.toFixed(2);

    fila.querySelector(".total-semanal").textContent =
        totalSemanal.toFixed(2);
}




// ================= FETCH TOTAL SEMANAL =================
async function cargarTotalSemanalFila(fila) {

    const sku = fila.dataset.sku;
    const fechaInicio = fila.dataset.fechaInicio;
    const fechaFin = fila.dataset.fechaFin;

    try {

        const response = await fetch(`/Operaciones/SkuSem?sku=${sku}&fechaIn=${fechaInicio}&fechaFin=${fechaFin}`);

        if (!response.ok) throw new Error("Error");

        const total = await response.json();

        fila.querySelector(".total-semanal").textContent =
            parseFloat(total || 0).toFixed(2);

    } catch (err) {

        console.error("Error cargando total semanal", err);

        fila.querySelector(".total-semanal").textContent = "0.00";
    }
}


// ================= EVENTO =================
document.addEventListener("input", function (e) {

    if (!e.target.classList.contains("input-participacion-semanal"))
        return;

    const fila = e.target.closest(".fila-producto");

    recalcularFilaSemanal(fila);

});


// ================= INIT =================
function recalcularTodoSemanal() {

    const filas = document.querySelectorAll("#tablaPlanSemanal .fila-producto");

    filas.forEach(fila => {

        const inputs = fila.querySelectorAll(".input-participacion-semanal");

        inputs.forEach(input => {
            aplicarColorSemanal(input);
        });

        recalcularFilaSemanal(fila);

        // 🔥 NUEVO: cargar total guardado
        cargarTotalSemanalFila(fila);
    });

}


// 🔥 ESTA ES LA QUE TE ESTABA FALLANDO
function inicializarPlanSemanal() {

    console.log("Inicializando plan semanal...");

    recalcularTodoSemanal();

}