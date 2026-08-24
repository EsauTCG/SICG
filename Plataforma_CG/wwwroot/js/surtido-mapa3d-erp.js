(() => {
    'use strict';

    function initMapa3DSearch() {
        const input = document.getElementById('map3dSearch');
        const button = document.getElementById('map3dSearchBtn');
        const title = document.getElementById('map3dTitle');

        if (!input || !button || !title) {
            return;
        }

        function buscar() {
            const value = (input.value || '')
                .trim()
                .toUpperCase();

            const canonical =
                window.SigoWms3D
                    ? window.SigoWms3D.canonicalLocation(value)
                    : null;

            if (!canonical) {
                title.textContent = 'Ubicación inválida';
                return;
            }

            input.value = canonical;
            title.textContent = canonical;

            window.SigoWms3D.focus(
                'warehouseRack3d',
                canonical
            );
        }

        button.addEventListener('click', buscar);

        input.addEventListener('keydown', e => {
            if (e.key === 'Enter') {
                e.preventDefault();
                buscar();
            }
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener(
            'DOMContentLoaded',
            initMapa3DSearch,
            { once: true }
        );
    } else {
        initMapa3DSearch();
    }
})();
