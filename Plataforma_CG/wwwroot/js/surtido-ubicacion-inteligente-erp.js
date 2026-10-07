(() => {
    "use strict";

    const config =
        document.getElementById("putawayConfig");

    if (!config) {
        return;
    }

    const almacen =
        (config.dataset.almacen || "")
            .trim();

    const scanInventory =
        document.getElementById("scanInventory");

    const btnBuscar =
        document.getElementById("btnBuscarInventory");

    const inventoryResult =
        document.getElementById("inventoryResult");

    const stepDestination =
        document.getElementById("stepDestination");

    const scanDestination =
        document.getElementById("scanDestination");

    const btnValidate =
        document.getElementById("btnValidateDestination");

    const suggestedLocationText =
        document.getElementById("suggestedLocationText");

    const stepConfirm =
        document.getElementById("stepConfirm");

    const confirmHelp =
        document.getElementById("confirmHelp");

    const btnConfirm =
        document.getElementById("btnConfirmLocation");

    const previewLocation =
        document.getElementById("previewLocation");

    const colorResult =
        document.getElementById("colorResult");

    const putaway3d =
        document.getElementById("putaway3d");

    const tokenInput =
        document.querySelector(
            'input[name="__RequestVerificationToken"]'
        );

    let codigoInventarioActual = "";
    let ubicacionSugeridaActual = "";
    let codigoMapa3DActual = "";
    let destinoValidadoActual = "";


    function mensaje(
        elemento,
        texto,
        ok = true)
    {
        if (!elemento) {
            return;
        }

        elemento.textContent =
            texto || "";

        elemento.style.color =
            ok
                ? "#17683f"
                : "#a1242f";
    }


    function aplicarColor(color) {
        if (!colorResult) {
            return;
        }

        const codigo =
            color?.codigo || "";

        const nombre =
            color?.nombre || "";

        if (!codigo &&
            !nombre)
        {
            colorResult.style.display =
                "none";

            return;
        }

        colorResult.style.display =
            "block";

        colorResult.textContent =
            `Zona: ${nombre || codigo}` +
            (codigo && nombre
                ? ` (${codigo})`
                : "");

        const hex =
            (color?.hex || "")
                .trim();

        if (/^#[0-9A-F]{6}$/i.test(hex)) {
            colorResult.style.borderColor =
                hex;

            colorResult.style.boxShadow =
                `inset 5px 0 0 ${hex}`;
        } else {
            colorResult.style.borderColor =
                "#d8e0e6";

            colorResult.style.boxShadow =
                "none";
        }
    }


    function actualizarMapa(
        codigoVisible,
        codigoMapa3D)
    {
        if (previewLocation) {
            previewLocation.value =
                codigoVisible || "";
        }

        if (!putaway3d) {
            return;
        }

        /*
          IMPORTANTE:
          El controller NO intenta deducir la geometría desde
          CodigoUbicacion. Ustedes administran CodigoMapa3D.

          Si CodigoMapa3D está vacío, se usa CodigoUbicacion
          como fallback.
        */
        const ubicacionMapa =
            (codigoMapa3D || codigoVisible || "")
                .trim();

        putaway3d.dataset.location =
            ubicacionMapa;

        /*
          Se emite el evento para que el script 3D pueda reaccionar
          si ya tiene soporte de actualización dinámica.
        */
        window.dispatchEvent(
            new CustomEvent(
                "sigo:ubicacion-cambio",
                {
                    detail: {
                        codigoUbicacion:
                            codigoVisible || "",

                        codigoMapa3D:
                            ubicacionMapa
                    }
                }
            )
        );

        window.dispatchEvent(
            new CustomEvent(
                "sigo:layout3d-focus",
                {
                    detail: {
                        targetId: "putaway3d",
                        location: codigoVisible || ubicacionMapa
                    }
                }
            )
        );
    }


    function bloquearDestino() {
        destinoValidadoActual = "";

        stepDestination?.classList.add(
            "disabled"
        );

        stepConfirm?.classList.add(
            "disabled"
        );

        if (scanDestination) {
            scanDestination.value = "";
            scanDestination.disabled = true;
        }

        if (btnValidate) {
            btnValidate.disabled = true;
        }

        if (btnConfirm) {
            btnConfirm.disabled = true;
        }

        if (confirmHelp) {
            confirmHelp.textContent =
                "Valida primero la ubicación.";
        }
    }


    async function leerJson(response) {
        let data;

        try {
            data =
                await response.json();
        } catch {
            data =
            {
                ok: false,
                message:
                    "Respuesta inválida del servidor."
            };
        }

        if (!response.ok) {
            throw new Error(
                data?.message ||
                "No fue posible completar la operación."
            );
        }

        return data;
    }


    async function buscarInventario() {
        const codigo =
            (scanInventory?.value || "")
                .trim();

        if (!codigo) {
            mensaje(
                inventoryResult,
                "Escanea una tarima o caja.",
                false
            );

            scanInventory?.focus();
            return;
        }

        btnBuscar.disabled = true;
        bloquearDestino();

        try {
            const qs =
                new URLSearchParams(
                    {
                        almacen,
                        codigo
                    }
                );

            const response =
                await fetch(
                    `/Surtido/Ubicar/BuscarInventario?${qs.toString()}`,
                    {
                        headers:
                        {
                            "X-Requested-With":
                                "XMLHttpRequest"
                        }
                    }
                );

            const data =
                await leerJson(
                    response
                );

            codigoInventarioActual =
                codigo;

            ubicacionSugeridaActual =
                data?.ubicacion?.codigo || "";

            codigoMapa3DActual =
                data?.ubicacion?.mapa3d || "";

            const estado =
                data.yaUbicada
                    ? "YA UBICADA"
                    : "SUGERIDA";

            mensaje(
                inventoryResult,
                `${data.articulo} · ${data.producto || ""} · ` +
                `MASTER ${data.master} · ` +
                `${data.color?.nombre || data.color?.codigo || ""} · ` +
                `${data.reglaRotacion || ""} · ` +
                `${estado}`,
                true
            );

            aplicarColor(
                data.color
            );

            if (suggestedLocationText) {
                suggestedLocationText.textContent =
                    ubicacionSugeridaActual || "—";
            }

            actualizarMapa(
                ubicacionSugeridaActual,
                codigoMapa3DActual
            );

            stepDestination?.classList.remove(
                "disabled"
            );

            if (scanDestination) {
                scanDestination.disabled =
                    false;

                scanDestination.value =
                    ubicacionSugeridaActual;

                scanDestination.focus();
                scanDestination.select();
            }

            if (btnValidate) {
                btnValidate.disabled =
                    false;
            }
        }
        catch (error) {
            codigoInventarioActual = "";
            ubicacionSugeridaActual = "";
            codigoMapa3DActual = "";

            mensaje(
                inventoryResult,
                error.message,
                false
            );

            aplicarColor(null);

            actualizarMapa(
                "",
                ""
            );
        }
        finally {
            btnBuscar.disabled = false;
        }
    }


    async function validarDestino() {
        const destino =
            (scanDestination?.value || "")
                .trim()
                .toUpperCase();

        if (!codigoInventarioActual) {
            mensaje(
                inventoryResult,
                "Primero escanea el producto.",
                false
            );
            return;
        }

        if (!destino) {
            mensaje(
                confirmHelp,
                "Escanea una ubicación.",
                false
            );

            scanDestination?.focus();
            return;
        }

        btnValidate.disabled = true;

        try {
            const qs =
                new URLSearchParams(
                    {
                        almacen,
                        codigoInventario:
                            codigoInventarioActual,
                        ubicacion:
                            destino
                    }
                );

            const response =
                await fetch(
                    `/Surtido/Ubicar/ValidarDestino?${qs.toString()}`,
                    {
                        headers:
                        {
                            "X-Requested-With":
                                "XMLHttpRequest"
                        }
                    }
                );

            const data =
                await leerJson(
                    response
                );

            destinoValidadoActual =
                data?.ubicacion?.codigo || "";

            actualizarMapa(
                destinoValidadoActual,
                data?.ubicacion?.mapa3d || ""
            );

            aplicarColor(
                data.color
            );

            stepConfirm?.classList.remove(
                "disabled"
            );

            if (btnConfirm) {
                btnConfirm.disabled =
                    false;
            }

            mensaje(
                confirmHelp,
                data.esSugerida
                    ? `Destino válido: ${destinoValidadoActual}`
                    : `Destino válido: ${destinoValidadoActual} (distinto al sugerido)`,
                true
            );
        }
        catch (error) {
            destinoValidadoActual = "";

            stepConfirm?.classList.add(
                "disabled"
            );

            if (btnConfirm) {
                btnConfirm.disabled =
                    true;
            }

            mensaje(
                confirmHelp,
                error.message,
                false
            );

            scanDestination?.focus();
            scanDestination?.select();
        }
        finally {
            btnValidate.disabled = false;
        }
    }


    async function confirmar() {
        if (!codigoInventarioActual ||
            !destinoValidadoActual)
        {
            return;
        }

        btnConfirm.disabled = true;

        try {
            const body =
                new URLSearchParams();

            body.set(
                "Almacen",
                almacen
            );

            body.set(
                "CodigoInventario",
                codigoInventarioActual
            );

            body.set(
                "CodigoUbicacion",
                destinoValidadoActual
            );

            if (tokenInput?.value) {
                body.set(
                    "__RequestVerificationToken",
                    tokenInput.value
                );
            }

            const response =
                await fetch(
                    "/Surtido/Ubicar/Confirmar",
                    {
                        method:
                            "POST",

                        headers:
                        {
                            "Content-Type":
                                "application/x-www-form-urlencoded; charset=UTF-8",

                            "X-Requested-With":
                                "XMLHttpRequest"
                        },

                        body:
                            body.toString()
                    }
                );

            const data =
                await leerJson(
                    response
                );

            mensaje(
                inventoryResult,
                `✓ ${data.message} ${data.ubicacion?.codigo || ""}`,
                true
            );

            actualizarMapa(
                data.ubicacion?.codigo || "",
                data.ubicacion?.mapa3d || ""
            );

            aplicarColor(
                data.color
            );

            codigoInventarioActual = "";
            ubicacionSugeridaActual = "";
            codigoMapa3DActual = "";
            destinoValidadoActual = "";

            if (scanInventory) {
                scanInventory.value = "";
            }

            if (scanDestination) {
                scanDestination.value = "";
                scanDestination.disabled = true;
            }

            stepDestination?.classList.add(
                "disabled"
            );

            stepConfirm?.classList.add(
                "disabled"
            );

            if (btnValidate) {
                btnValidate.disabled = true;
            }

            if (btnConfirm) {
                btnConfirm.disabled = true;
            }

            if (confirmHelp) {
                confirmHelp.textContent =
                    "Ubicación guardada.";
            }

            setTimeout(
                () =>
                    scanInventory?.focus(),
                50
            );
        }
        catch (error) {
            mensaje(
                confirmHelp,
                error.message,
                false
            );

            btnConfirm.disabled =
                false;
        }
    }


    btnBuscar?.addEventListener(
        "click",
        buscarInventario
    );

    scanInventory?.addEventListener(
        "keydown",
        event =>
        {
            if (event.key === "Enter") {
                event.preventDefault();
                buscarInventario();
            }
        }
    );

    btnValidate?.addEventListener(
        "click",
        validarDestino
    );

    scanDestination?.addEventListener(
        "keydown",
        event =>
        {
            if (event.key === "Enter") {
                event.preventDefault();
                validarDestino();
            }
        }
    );

    btnConfirm?.addEventListener(
        "click",
        confirmar
    );

    scanInventory?.focus();
})();
