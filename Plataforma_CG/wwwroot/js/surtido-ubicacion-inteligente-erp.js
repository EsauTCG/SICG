(() => {

        const inventory =
            document.getElementById('scanInventory');

        const btnInventory =
            document.getElementById('btnBuscarInventory');

        const result =
            document.getElementById('inventoryResult');

        const stepDestination =
            document.getElementById('stepDestination');

        const destination =
            document.getElementById('scanDestination');

        const btnDestination =
            document.getElementById('btnValidateDestination');

        const stepConfirm =
            document.getElementById('stepConfirm');

        const confirmHelp =
            document.getElementById('confirmHelp');

        const btnConfirm =
            document.getElementById('btnConfirmLocation');

        const suggestedText =
            document.getElementById('suggestedLocationText');

        const previewLocation =
            document.getElementById('previewLocation');

        const btnFocus =
            document.getElementById('btnFocus3d');


        let suggestedLocation = '';
        let validatedLocation = '';


        function canonical(raw) {

            if (!window.SigoWms3D) {
                return null;
            }

            return window.SigoWms3D
                .canonicalLocation(raw);
        }


        function focus3d(location) {

            const loc =
                canonical(location);

            if (!loc) {
                return;
            }

            previewLocation.value =
                loc;

            window.SigoWms3D.focus(
                'putaway3d',
                loc
            );
        }


        function enableDestination(location) {

            suggestedLocation =
                canonical(location)
                || '';

            if (!suggestedLocation) {
                return;
            }

            stepDestination.classList
                .remove('disabled');

            destination.disabled =
                false;

            btnDestination.disabled =
                false;

            suggestedText.textContent =
                `Sugerida: ${suggestedLocation}`;

            destination.placeholder =
                `Ej. ${suggestedLocation}`;

            focus3d(
                suggestedLocation
            );

            setTimeout(
                () => destination.focus(),
                100
            );
        }


        function resetConfirm() {

            validatedLocation =
                '';

            stepConfirm.classList
                .add('disabled');

            btnConfirm.disabled =
                true;

            btnConfirm.classList
                .remove('ready');

            confirmHelp.textContent =
                'Primero valida una ubicación.';
        }


        /*
         * ============================================================
         * PASO 1
         *
         * IMPORTANTE:
         * Esta vista ya queda con la estructura visual de V7.
         *
         * El endpoint real de slotting debe regresar después:
         *
         * {
         *   ok: true,
         *   articulo: "...",
         *   producto: "...",
         *   diasInventario: 5,
         *   rotacion: "ALTA",
         *   zona: "FRENTE",
         *   ubicacionSugerida: "R7-11D"
         * }
         *
         * Mientras no exista ese endpoint, NO inventamos la
         * ubicación. Para probar el flujo visual se deja una
         * recomendación temporal R7-11D.
         * ============================================================
         */
        function buscarInventario() {

            const scan =
                (inventory.value || '')
                .trim()
                .toUpperCase();

            if (!scan) {

                result.innerHTML =
                    '<span class="status-error">Escanea una tarima o caja.</span>';

                return;
            }


            result.innerHTML =
                '<span class="status-warn">Inventario leído: '
                + scan
                + '. Falta conectar el endpoint real de slotting para obtener SKU, días y ubicación recomendada.</span>';


            /*
             * TEMPORAL ÚNICAMENTE PARA QUE LA VISTA PUEDA PROBARSE.
             * Cuando conectemos la consulta real, esta línea se elimina
             * y usamos respuesta.ubicacionSugerida.
             */
            enableDestination(
                'R7-11D'
            );

            resetConfirm();
        }


        function validarDestino() {

            const raw =
                (destination.value || '')
                .trim()
                .toUpperCase();

            const loc =
                canonical(raw);

            if (!loc) {

                suggestedText.innerHTML =
                    '<span class="status-error">Ubicación inválida.</span>';

                resetConfirm();

                return;
            }


            validatedLocation =
                loc;

            destination.value =
                loc;

            suggestedText.innerHTML =
                `<span class="status-ok">Ubicación válida: ${loc}</span>`;

            focus3d(
                loc
            );

            stepConfirm.classList
                .remove('disabled');

            btnConfirm.disabled =
                false;

            btnConfirm.classList
                .add('ready');

            confirmHelp.textContent =
                `Destino validado: ${loc}.`;
        }


        function confirmar() {

            if (!validatedLocation) {
                return;
            }

            /*
             * Aquí conectaremos el POST real de putaway.
             * No se modifica inventario todavía para no inventar
             * una escritura sin conocer el proceso actual.
             */
            confirmHelp.innerHTML =
                `<span class="status-warn">
                    ${validatedLocation} validada.
                    Falta conectar el POST real para mover la tarima/caja.
                 </span>`;
        }


        btnInventory.addEventListener(
            'click',
            buscarInventario
        );


        inventory.addEventListener(
            'keydown',
            e => {

                if (e.key === 'Enter') {

                    e.preventDefault();

                    buscarInventario();
                }

            }
        );


        btnDestination.addEventListener(
            'click',
            validarDestino
        );


        destination.addEventListener(
            'keydown',
            e => {

                if (e.key === 'Enter') {

                    e.preventDefault();

                    validarDestino();
                }

            }
        );


        btnConfirm.addEventListener(
            'click',
            confirmar
        );


        btnFocus.addEventListener(
            'click',
            () => {

                focus3d(
                    validatedLocation
                    || suggestedLocation
                    || previewLocation.value
                );

            }
        );


        setTimeout(
            () => inventory.focus(),
            180
        );

    })();
