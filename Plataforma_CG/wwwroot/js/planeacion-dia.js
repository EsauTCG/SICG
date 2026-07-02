// ===============================
// Planeación diaria (Partial View)
// ===============================

(function () {

    let fechaActual = null;

    // ---- Inicializador público ----
    window.initPlaneacionDia = function () {
        console.log("[Planeación Día] inicializado");

        // Fecha inicial desde input hidden o date
        const inputFecha = document.getElementById("fecha");
        if (inputFecha) {
            fechaActual = inputFecha.value;
        }

        registrarEventos();
        recalcularTotales();
    };

    // -------------------------------
    // Registro de eventos
    // -------------------------------
    function registrarEventos() {

        // Evita duplicados
        document.querySelectorAll("[data-planeado]").forEach(el => {
            el.removeEventListener("input", onInputPlaneado);
            el.addEventListener("input", onInputPlaneado);
        });

        document.querySelectorAll(".btn-mas").forEach(btn => {
            btn.removeEventListener("click", onMas);
            btn.addEventListener("click", onMas);
        });

        document.querySelectorAll(".btn-menos").forEach(btn => {
            btn.removeEventListener("click", onMenos);
            btn.addEventListener("click", onMenos);
        });
    }

    // -------------------------------
    // Eventos
    // -------------------------------
    function onInputPlaneado(e) {
        const input = e.target;
        const fila = input.closest("tr");

        let valor = parseFloat(input.value) || 0;
        if (valor < 0) {
            input.value = 0;
            valor = 0;
        }

        fila.querySelector(".total").innerText = valor.toFixed(2);
        recalcularTotales();
    }

    function onMas(e) {
        const input = e.target.closest("tr").querySelector("[data-planeado]");
        input.value = (parseFloat(input.value) || 0) + 1;
        input.dispatchEvent(new Event("input"));
    }

    function onMenos(e) {
        const input = e.target.closest("tr").querySelector("[data-planeado]");
        let v = (parseFloat(input.value) || 0) - 1;
        input.value = v < 0 ? 0 : v;
        input.dispatchEvent(new Event("input"));
    }

    // -------------------------------
    // Cálculos
    // -------------------------------
    function recalcularTotales() {
        let total = 0;

        document.querySelectorAll("[data-planeado]").forEach(input => {
            total += parseFloat(input.value) || 0;
        });

        const lblTotal = document.getElementById("totalGeneral");
        if (lblTotal) {
            lblTotal.innerText = total.toFixed(2);
        }
    }

})();
