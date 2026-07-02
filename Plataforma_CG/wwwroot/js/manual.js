// ========================================
// MANUAL DE USUARIO INTERACTIVO - ESTILO LIBRO
// ========================================
(function initLayoutUserManual() {
    const btn = document.getElementById("layout-manual-fab");
    const modalEl = document.getElementById("layoutManualModal");
    if (!btn || !modalEl) return;

    const modal = new bootstrap.Modal(modalEl);
    const titleEl = document.getElementById("layoutManualTitle");

    btn.addEventListener("click", () => {
        if (!window.manualConfig) {
            titleEl.textContent = "Manual no disponible";
            document.getElementById("manualSections").innerHTML = `
                        <div class="alert alert-warning m-4">
                           Por el momento este modulo aun no cuenta con manual de usuario disponible.
                        </div>`;
            modal.show();
            return;
        }

        titleEl.textContent = window.manualConfig.title || "Manual de Usuario - SIGO";
        generarManualLibro();
        modal.show();

        // Inicializar listeners después de abrir
        setTimeout(() => {
            initManualListeners();
        }, 300);
    });
})();

function generarManualLibro() {
    const tocNav = document.getElementById('manualToc');
    const sectionsContainer = document.getElementById('manualSections');

    if (!window.manualConfig || !window.manualConfig.sections) return;

    tocNav.innerHTML = '';
    sectionsContainer.innerHTML = '';

    window.manualConfig.sections.forEach((section, index) => {
        // Crear item en índice
        const tocItem = document.createElement('div');
        tocItem.className = 'manual-toc-item';
        tocItem.setAttribute('data-section', section.id);
        tocItem.innerHTML = `
                    <i class="fas ${section.icon}"></i>
                    <span>${section.title}</span>
                `;
        tocItem.onclick = () => scrollToManualSection(section.id);
        tocNav.appendChild(tocItem);

        // Crear sección de contenido
        const sectionDiv = document.createElement('section');
        sectionDiv.className = 'manual-section';
        sectionDiv.id = section.id;

        let stepsHTML = '';
        section.steps.forEach(step => {
            stepsHTML += `
                        <div class="manual-step">
                            <h4 class="manual-step-title">
                                <i class="fas fa-chevron-right"></i>
                                ${step.title}
                            </h4>
                            <p class="manual-step-text">${step.text}</p>
                            ${renderManualMedia(step)}
                        </div>
                    `;
        });

        sectionDiv.innerHTML = `
                    <div class="manual-section-header">
                        <div class="manual-section-number">${index + 1}</div>
                        <h2 class="manual-section-title">${section.title}</h2>
                    </div>
                    <div class="manual-section-content">
                        ${stepsHTML}
                    </div>
                `;

        sectionsContainer.appendChild(sectionDiv);
    });
}

function initManualListeners() {
    const contentArea = document.getElementById('manualContent');
    const scrollTopBtn = document.getElementById('manualScrollTopBtn');
    const progressBar = document.getElementById('manualProgressBar');

    if (!contentArea) return;

    // Scroll tracking
    contentArea.addEventListener('scroll', () => {
        // Actualizar item activo en índice
        updateActiveManualTocItem();

        // Mostrar/ocultar botón scroll to top
        if (contentArea.scrollTop > 300) {
            scrollTopBtn.classList.add('show');
        } else {
            scrollTopBtn.classList.remove('show');
        }

        // Actualizar barra de progreso
        const scrollPercent = (contentArea.scrollTop / (contentArea.scrollHeight - contentArea.clientHeight)) * 100;
        progressBar.style.width = scrollPercent + '%';
    });
}

function updateActiveManualTocItem() {
    const sections = document.querySelectorAll('.manual-section');
    const tocItems = document.querySelectorAll('.manual-toc-item');

    let currentSection = null;

    sections.forEach(section => {
        const rect = section.getBoundingClientRect();
        if (rect.top <= 150) {
            currentSection = section.id;
        }
    });

    tocItems.forEach(item => {
        if (item.getAttribute('data-section') === currentSection) {
            item.classList.add('active');
        } else {
            item.classList.remove('active');
        }
    });
}

function scrollToManualSection(sectionId) {
    const section = document.getElementById(sectionId);
    const contentArea = document.getElementById('manualContent');

    if (section && contentArea) {
        const offset = section.offsetTop - 20;
        contentArea.scrollTo({
            top: offset,
            behavior: 'smooth'
        });

        // Cerrar sidebar en mobile
        if (window.innerWidth < 992) {
            toggleManualSidebar();
        }
    }
}

function scrollManualToTop() {
    const contentArea = document.getElementById('manualContent');
    contentArea.scrollTo({
        top: 0,
        behavior: 'smooth'
    });
}

function toggleManualSidebar() {
    const sidebar = document.getElementById('manualSidebar');
    const overlay = document.querySelector('.manual-overlay');

    if (sidebar && overlay) {
        sidebar.classList.toggle('show');
        overlay.classList.toggle('show');
    }
}

function renderManualMedia(step) {

    // VIDEO
    if (step.video) {
        return `
                    <video class="manual-step-video" controls preload="metadata">
                        <source src="${step.video}" type="video/mp4">
                        Tu navegador no soporta video HTML5.
                    </video>
                `;
    }

    // IMAGEN
    if (step.image) {
        return `
                    <img
                        src="${step.image}"
                        alt="${step.title || ''}"
                        class="manual-step-image"
                    >
                `;
    }

    return "";
}