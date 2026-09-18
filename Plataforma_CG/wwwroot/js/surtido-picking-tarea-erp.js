(() => {
    'use strict';

    function initPickingTask() {
        const form = document.getElementById('frmBajada');
        const input = document.getElementById('CodigoEscaneado');
        const validar = document.getElementById('btnValidar');
        const confirmar = document.getElementById('btnConfirmar');
        const msg = document.getElementById('scanMessage');

        if (!form || !input || !validar || !confirmar || !msg) {
            return;
        }

        const etiqueta = (form.dataset.etiqueta || '').trim().toUpperCase();
        const tarima = (form.dataset.tarima || '').trim().toUpperCase();

        const validarCodigo = () => {
            const scan = (input.value || '').trim().toUpperCase();

            const ok =
                scan !== '' &&
                (
                    scan === etiqueta ||
                    (
                        tarima !== '' &&
                        scan === tarima
                    )
                );

            if (ok) {
                msg.innerHTML =
                    '<span class="scan-status-ok">✓ Producto correcto. Listo para confirmar bajada.</span>';

                confirmar.disabled = false;
                input.readOnly = true;
                input.classList.add('is-valid-scan');
            } else {
                msg.innerHTML =
                    '<span class="scan-status-error">× Código incorrecto. No corresponde a la caja/tarima PEPS indicada.</span>';

                confirmar.disabled = true;
                input.classList.remove('is-valid-scan');
            }
        };

        validar.addEventListener('click', validarCodigo);

        input.addEventListener('keydown', e => {
            if (e.key === 'Enter') {
                e.preventDefault();
                validarCodigo();
            }
        });

        setTimeout(() => {
            if (!input.disabled) {
                input.focus();
            }
        }, 150);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initPickingTask, { once: true });
    } else {
        initPickingTask();
    }
})();
