(() => {
    "use strict";

    const root = document.getElementById("manualAuthAdmin");
    if (!root) return;

    const urls = {
        list: root.dataset.listUrl,
        create: root.dataset.createUrl,
        base: root.dataset.baseUrl
    };
    const token = document.querySelector("#manual-auth-csrf input[name='__RequestVerificationToken']")?.value || "";
    const rows = document.getElementById("manualAuthRows");
    const empty = document.getElementById("manualAuthEmpty");
    const alertBox = document.getElementById("manualAuthAlert");
    const search = document.getElementById("manualAuthSearch");
    const status = document.getElementById("manualAuthStatus");
    const form = document.getElementById("manualAuthForm");
    const editorElement = document.getElementById("manualAuthEditor");
    const deleteElement = document.getElementById("manualAuthDelete");
    const editorModal = bootstrap.Modal.getOrCreateInstance(editorElement);
    const deleteModal = bootstrap.Modal.getOrCreateInstance(deleteElement);
    let users = [];
    let deleteTarget = null;
    let alertTimer = null;

    const $ = id => document.getElementById(id);

    function escapeHtml(value) {
        const element = document.createElement("div");
        element.textContent = String(value ?? "");
        return element.innerHTML;
    }

    function parseUtc(value) {
        if (!value) return null;
        const text = String(value);
        const hasZone = /(?:Z|[+-]\d{2}:?\d{2})$/i.test(text);
        const date = new Date(hasZone ? text : `${text}Z`);
        return Number.isNaN(date.getTime()) ? null : date;
    }

    function formatDate(value) {
        const date = parseUtc(value);
        if (!date) return "Nunca";
        return new Intl.DateTimeFormat("es-MX", {
            dateStyle: "medium",
            timeStyle: "short"
        }).format(date);
    }

    function isBlocked(user) {
        const until = parseUtc(user.bloqueadoHastaUtc);
        return Boolean(until && until.getTime() > Date.now());
    }

    function initials(name) {
        const parts = String(name || "U").trim().split(/\s+/).filter(Boolean);
        return parts.slice(0, 2).map(x => x.charAt(0).toUpperCase()).join("") || "U";
    }

    function showAlert(message, type = "success") {
        window.clearTimeout(alertTimer);
        alertBox.textContent = message;
        alertBox.className = `manual-auth-alert ${type}`;
        alertBox.hidden = false;
        alertTimer = window.setTimeout(() => { alertBox.hidden = true; }, 6500);
    }

    function showFormError(message) {
        const error = $("manualAuthFormError");
        error.textContent = message;
        error.hidden = !message;
    }

    function setBusy(button, busy, busyText) {
        if (!button) return;
        if (busy) {
            button.dataset.originalHtml = button.innerHTML;
            button.disabled = true;
            button.innerHTML = `<span class="spinner-border spinner-border-sm"></span> ${escapeHtml(busyText)}`;
        } else {
            button.disabled = false;
            button.innerHTML = button.dataset.originalHtml || button.innerHTML;
        }
    }

    async function api(url, options = {}) {
        const headers = {
            Accept: "application/json",
            "X-Requested-With": "XMLHttpRequest",
            ...(options.headers || {})
        };

        if (options.body !== undefined) headers["Content-Type"] = "application/json";
        if (options.method && options.method !== "GET") headers.RequestVerificationToken = token;

        const response = await fetch(url, { ...options, headers });
        let data = {};
        try { data = await response.json(); } catch { /* respuesta sin JSON */ }

        if (response.status === 401 && data.redirectUrl) {
            window.location.assign(data.redirectUrl);
            throw new Error("La sesión expiró.");
        }

        if (!response.ok || data.success === false)
            throw new Error(data.message || "No fue posible completar la operación.");

        return data;
    }

    function updateStats() {
        $("manualAuthTotal").textContent = users.length;
        $("manualAuthActive").textContent = users.filter(x => x.activo).length;
        $("manualAuthBlocked").textContent = users.filter(isBlocked).length;
        $("manualAuthInactive").textContent = users.filter(x => !x.activo).length;
    }

    function filteredUsers() {
        const term = search.value.trim().toLocaleLowerCase("es-MX");
        const selected = status.value;

        return users.filter(user => {
            const matchesText = !term || `${user.nombre} ${user.usuario}`.toLocaleLowerCase("es-MX").includes(term);
            const blocked = isBlocked(user);
            const matchesStatus = selected === "all"
                || (selected === "blocked" && blocked)
                || (selected === "active" && user.activo && !blocked)
                || (selected === "inactive" && !user.activo);
            return matchesText && matchesStatus;
        });
    }

    function render() {
        updateStats();
        const visible = filteredUsers();
        empty.hidden = visible.length > 0;
        rows.closest("table").hidden = visible.length === 0;

        rows.innerHTML = visible.map(user => {
            const blocked = isBlocked(user);
            const badge = blocked
                ? '<span class="manual-auth-badge blocked">Bloqueado</span>'
                : user.activo
                    ? '<span class="manual-auth-badge active">Activo</span>'
                    : '<span class="manual-auth-badge inactive">Inactivo</span>';
            const attempts = user.intentosFallidos > 0
                ? `<small>${user.intentosFallidos} intento(s) fallido(s)</small>`
                : "";

            return `<tr>
                <td>
                    <div class="manual-auth-person">
                        <span class="manual-auth-avatar">${escapeHtml(initials(user.nombre))}</span>
                        <div><strong>${escapeHtml(user.nombre)}</strong><small>ID ${user.id}</small></div>
                    </div>
                </td>
                <td><span class="manual-auth-username">${escapeHtml(user.usuario)}</span></td>
                <td>${badge}${attempts}</td>
                <td>${escapeHtml(formatDate(user.ultimoAccesoUtc))}</td>
                <td>${escapeHtml(formatDate(user.fechaCreacionUtc))}</td>
                <td>
                    <div class="manual-auth-actions">
                        ${blocked || user.intentosFallidos > 0 ? `<button class="manual-auth-action unlock" type="button" data-action="unlock" data-id="${user.id}" title="Desbloquear"><i class="bi bi-unlock"></i></button>` : ""}
                        <button class="manual-auth-action" type="button" data-action="edit" data-id="${user.id}" title="Editar"><i class="bi bi-pencil"></i></button>
                        <button class="manual-auth-action delete" type="button" data-action="delete" data-id="${user.id}" title="Eliminar"><i class="bi bi-trash3"></i></button>
                    </div>
                </td>
            </tr>`;
        }).join("");
    }

    async function loadUsers(showLoading = true) {
        if (showLoading) {
            empty.hidden = true;
            rows.closest("table").hidden = false;
            rows.innerHTML = '<tr><td colspan="6" class="manual-auth-loading"><span class="spinner-border spinner-border-sm"></span> Consultando usuarios...</td></tr>';
        }

        try {
            const data = await api(urls.list);
            users = Array.isArray(data.autorizadores) ? data.autorizadores : [];
            render();
        } catch (error) {
            users = [];
            rows.innerHTML = `<tr><td colspan="6" class="manual-auth-loading text-danger"><i class="bi bi-exclamation-triangle"></i> ${escapeHtml(error.message)}</td></tr>`;
            updateStats();
            showAlert(error.message, "error");
        }
    }

    function openCreate() {
        form.reset();
        $("manualAuthId").value = "";
        $("manualAuthEditorTitle").textContent = "Nuevo usuario autorizador";
        $("manualAuthPasswordLabel").textContent = "Contraseña";
        $("manualAuthPasswordHelp").textContent = "Mínimo 8 caracteres.";
        $("manualAuthPassword").required = true;
        $("manualAuthConfirm").required = true;
        $("manualAuthActiveField").hidden = true;
        $("manualAuthEnabled").checked = true;
        showFormError("");
        editorModal.show();
    }

    function openEdit(user) {
        form.reset();
        $("manualAuthId").value = user.id;
        $("manualAuthName").value = user.nombre;
        $("manualAuthUser").value = user.usuario;
        $("manualAuthEnabled").checked = user.activo;
        $("manualAuthEditorTitle").textContent = "Editar usuario autorizador";
        $("manualAuthPasswordLabel").textContent = "Nueva contraseña (opcional)";
        $("manualAuthPasswordHelp").textContent = "Déjala vacía para conservar la contraseña actual.";
        $("manualAuthPassword").required = false;
        $("manualAuthConfirm").required = false;
        $("manualAuthActiveField").hidden = false;
        showFormError("");
        editorModal.show();
    }

    async function save(event) {
        event.preventDefault();
        showFormError("");

        const id = Number($("manualAuthId").value || 0);
        const name = $("manualAuthName").value.trim();
        const user = $("manualAuthUser").value.trim();
        const password = $("manualAuthPassword").value;
        const confirm = $("manualAuthConfirm").value;

        if (!form.checkValidity()) {
            form.reportValidity();
            return;
        }
        if (/\s/.test(user)) {
            showFormError("El usuario no puede contener espacios.");
            return;
        }
        if (!/^[\p{L}\p{N}._-]+$/u.test(user)) {
            showFormError("El usuario solo puede contener letras, números, punto, guion o guion bajo.");
            return;
        }
        if (password !== confirm) {
            showFormError("Las contraseñas no coinciden.");
            return;
        }

        const button = $("manualAuthSave");
        setBusy(button, true, "Guardando");
        try {
            const data = id
                ? await api(`${urls.base}/${id}`, {
                    method: "PUT",
                    body: JSON.stringify({ nombre: name, usuario: user, nuevaClave: password || null, activo: $("manualAuthEnabled").checked })
                })
                : await api(urls.create, {
                    method: "POST",
                    body: JSON.stringify({ nombre: name, usuario: user, clave: password })
                });

            editorModal.hide();
            await loadUsers(false);
            showAlert(data.message);
        } catch (error) {
            showFormError(error.message);
        } finally {
            setBusy(button, false);
        }
    }

    async function unlock(user, button) {
        setBusy(button, true, "");
        try {
            const data = await api(`${urls.base}/${user.id}/desbloquear`, { method: "POST" });
            await loadUsers(false);
            showAlert(data.message);
        } catch (error) {
            showAlert(error.message, "error");
        } finally {
            setBusy(button, false);
        }
    }

    function openDelete(user) {
        deleteTarget = user;
        $("manualAuthDeleteName").textContent = `${user.nombre} (${user.usuario})`;
        deleteModal.show();
    }

    async function confirmDelete() {
        if (!deleteTarget) return;
        const button = $("manualAuthDeleteConfirm");
        setBusy(button, true, "Eliminando");
        try {
            const data = await api(`${urls.base}/${deleteTarget.id}`, { method: "DELETE" });
            deleteModal.hide();
            deleteTarget = null;
            await loadUsers(false);
            showAlert(data.message);
        } catch (error) {
            deleteModal.hide();
            showAlert(error.message, "error");
        } finally {
            setBusy(button, false);
        }
    }

    $("manualAuthNew").addEventListener("click", openCreate);
    $("manualAuthRefresh").addEventListener("click", () => loadUsers());
    $("manualAuthDeleteConfirm").addEventListener("click", confirmDelete);
    form.addEventListener("submit", save);
    search.addEventListener("input", render);
    status.addEventListener("change", render);

    rows.addEventListener("click", event => {
        const button = event.target.closest("[data-action]");
        if (!button) return;
        const user = users.find(x => x.id === Number(button.dataset.id));
        if (!user) return;

        if (button.dataset.action === "edit") openEdit(user);
        if (button.dataset.action === "delete") openDelete(user);
        if (button.dataset.action === "unlock") unlock(user, button);
    });

    document.querySelectorAll("[data-password-target]").forEach(button => {
        button.addEventListener("click", () => {
            const input = $(button.dataset.passwordTarget);
            const visible = input.type === "text";
            input.type = visible ? "password" : "text";
            button.innerHTML = `<i class="bi bi-${visible ? "eye" : "eye-slash"}"></i>`;
            button.setAttribute("aria-label", visible ? "Mostrar contraseña" : "Ocultar contraseña");
        });
    });

    editorElement.addEventListener("hidden.bs.modal", () => {
        form.reset();
        showFormError("");
        document.querySelectorAll("#manualAuthForm input[type='text']").forEach(input => {
            if (input.id === "manualAuthName" || input.id === "manualAuthUser") return;
            input.type = "password";
        });
        document.querySelectorAll("[data-password-target]").forEach(button => {
            button.innerHTML = '<i class="bi bi-eye"></i>';
        });
    });

    loadUsers();
})();
