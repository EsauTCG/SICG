(() => {
    "use strict";

    const hosts = Array.from(document.querySelectorAll(".sigo-layout3d"));
    if (!hosts.length || typeof THREE === "undefined") return;

    const isHex = v => /^#[0-9a-f]{6}$/i.test(String(v || "").trim());
    const num = (v, d = 0) => Number.isFinite(Number(v)) ? Number(v) : d;

    // Dapper puede serializar las columnas como PascalCase.
    // El renderer acepta ambas variantes para evitar que los racks
    // terminen todos en 0,0 con el nombre genérico "RACK".
    const pick = (obj, ...names) => {
        if (!obj) return undefined;

        for (const name of names) {
            if (Object.prototype.hasOwnProperty.call(obj, name)) {
                return obj[name];
            }
        }

        return undefined;
    };

    function normalizeRack(r) {
        return {
            id: pick(r, "id", "Id"),
            layoutId: pick(r, "layoutId", "LayoutId"),
            codigoRack: pick(r, "codigoRack", "CodigoRack") ?? "",
            nombreRack: pick(r, "nombreRack", "NombreRack") ?? "",
            posX: pick(r, "posX", "PosX") ?? 0,
            posZ: pick(r, "posZ", "PosZ") ?? 0,
            ancho: pick(r, "ancho", "Ancho") ?? 10,
            alto: pick(r, "alto", "Alto") ?? 6,
            profundidad: pick(r, "profundidad", "Profundidad") ?? 2,
            rotacionY: pick(r, "rotacionY", "RotacionY") ?? 0,
            niveles: pick(r, "niveles", "Niveles") ?? 4,
            posicionesPorNivel:
                pick(r, "posicionesPorNivel", "PosicionesPorNivel") ?? 10,
            hexColor: pick(r, "hexColor", "HexColor") ?? "",
            orden: pick(r, "orden", "Orden") ?? 1
        };
    }

    function normalizeLocation(u) {
        return {
            id: pick(u, "id", "Id"),
            layoutId: pick(u, "layoutId", "LayoutId"),
            rackId: pick(u, "rackId", "RackId"),
            codigoRack: pick(u, "codigoRack", "CodigoRack") ?? "",
            codigoUbicacion:
                pick(u, "codigoUbicacion", "CodigoUbicacion") ?? "",
            codigoMapa3D:
                pick(u, "codigoMapa3D", "CodigoMapa3D") ?? "",
            codigoColor:
                pick(u, "codigoColor", "CodigoColor") ?? "",
            nombreColor:
                pick(u, "nombreColor", "NombreColor") ?? "",
            hexColor:
                pick(u, "hexColor", "HexColor") ?? "",
            posicion:
                pick(u, "posicion", "Posicion") ?? "",
            altura:
                pick(u, "altura", "Altura") ?? "",
            ordenFisico:
                pick(u, "ordenFisico", "OrdenFisico") ?? 1,
            activo:
                pick(u, "activo", "Activo") ?? true,
            ocupada:
                pick(u, "ocupada", "Ocupada") ?? false,
            unidadCodigo:
                pick(u, "unidadCodigo", "UnidadCodigo") ?? ""
        };
    }

    function normalizeLayout(l) {
        if (!l) return null;

        return {
            id: pick(l, "id", "Id"),
            codigoAlmacen:
                pick(l, "codigoAlmacen", "CodigoAlmacen") ?? "",
            codigoLayout:
                pick(l, "codigoLayout", "CodigoLayout") ?? "",
            nombreLayout:
                pick(l, "nombreLayout", "NombreLayout") ?? "",
            descripcion:
                pick(l, "descripcion", "Descripcion") ?? "",
            esOperativo:
                pick(l, "esOperativo", "EsOperativo") ?? false,
            activo:
                pick(l, "activo", "Activo") ?? true
        };
    }

    function normalizeData(data) {
        const almacenRaw =
            data?.almacen ||
            data?.Almacen ||
            {};

        return {
            ...data,

            almacen: {
                codigo:
                    pick(almacenRaw, "codigo", "Codigo") ?? "",
                nombre:
                    pick(almacenRaw, "nombre", "Nombre") ?? "",
                planta:
                    pick(almacenRaw, "planta", "Planta") ?? ""
            },

            layout:
                normalizeLayout(
                    data?.layout ||
                    data?.Layout
                ),

            racks:
                (
                    data?.racks ||
                    data?.Racks ||
                    []
                ).map(normalizeRack),

            ubicaciones:
                (
                    data?.ubicaciones ||
                    data?.Ubicaciones ||
                    []
                ).map(normalizeLocation)
        };
    }

    function levelIndex(value, fallback = 0) {
        const raw = String(value || "").trim().toUpperCase();
        if (!raw) return fallback;
        if (/^\d+$/.test(raw)) return Math.max(0, Number(raw) - 1);
        const c = raw.charCodeAt(0);
        if (c >= 65 && c <= 90) return c - 65;
        return fallback;
    }

    function positionIndex(value, fallback = 0) {
        const m = String(value || "").match(/\d+/);
        return m ? Math.max(0, Number(m[0]) - 1) : fallback;
    }

    // Propuesta1 usa 3 zonas dentro del mismo rack y repite
    // posiciones lógicas 01..10. CodigoMapa3D guarda el SLOT físico
    // único como R18-S01A ... R18-S23A.
    function physicalPositionIndex(location, fallback = 0) {
        const mapCode =
            String(location?.codigoMapa3D || "")
                .trim()
                .toUpperCase();

        const slot =
            mapCode.match(/-S(\d{1,2})[A-Z]?$/);

        if (slot) {
            return Math.max(
                0,
                Number(slot[1]) - 1
            );
        }

        return positionIndex(
            location?.posicion,
            fallback
        );
    }

    function labelSprite(text, background = "rgba(255,255,255,.95)", color = "#263844") {
        const canvas = document.createElement("canvas");
        canvas.width = 512;
        canvas.height = 120;
        const ctx = canvas.getContext("2d");
        ctx.fillStyle = background;
        ctx.fillRect(0, 0, canvas.width, canvas.height);
        ctx.strokeStyle = "rgba(45,60,70,.30)";
        ctx.lineWidth = 4;
        ctx.strokeRect(2, 2, canvas.width - 4, canvas.height - 4);
        ctx.fillStyle = color;
        ctx.font = "700 52px Segoe UI, Arial";
        ctx.textAlign = "center";
        ctx.textBaseline = "middle";
        ctx.fillText(text || "RACK", canvas.width / 2, canvas.height / 2);
        const texture = new THREE.CanvasTexture(canvas);
        const material = new THREE.SpriteMaterial({ map: texture, transparent: true, depthTest: false });
        const sprite = new THREE.Sprite(material);
        sprite.scale.set(5.2, 1.2, 1);
        sprite.renderOrder = 20;
        return sprite;
    }

    async function readJson(url) {
        const r = await fetch(url, { headers: { "X-Requested-With": "XMLHttpRequest" } });
        const data = await r.json().catch(() => ({ ok: false, message: "Respuesta inválida." }));
        if (!r.ok || data?.ok === false) throw new Error(data?.message || "No fue posible cargar el layout.");
        return data;
    }

    for (const host of hosts) {
        const endpoint = (host.dataset.endpoint || "").trim();
        if (!endpoint) continue;

        const infoTarget = host.dataset.infoTarget ? document.querySelector(host.dataset.infoTarget) : null;
        const selectedTarget = host.dataset.selectedTarget ? document.querySelector(host.dataset.selectedTarget) : null;
        const searchInput = host.dataset.searchInput ? document.querySelector(host.dataset.searchInput) : null;
        const searchButton = host.dataset.searchButton ? document.querySelector(host.dataset.searchButton) : null;
        let initialLocation = (host.dataset.location || "").trim().toUpperCase();

        const scene = new THREE.Scene();
        scene.background = new THREE.Color(0xeef2f4);

        const camera = new THREE.PerspectiveCamera(45, 1, 0.1, 5000);
        const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false });
        renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
        renderer.shadowMap.enabled = true;
        renderer.shadowMap.type = THREE.PCFSoftShadowMap;
        host.innerHTML = "";
        host.appendChild(renderer.domElement);

        const controls = new THREE.OrbitControls(camera, renderer.domElement);
        controls.enableDamping = true;
        controls.dampingFactor = 0.08;
        controls.screenSpacePanning = true;
        controls.minDistance = 4;
        controls.maxDistance = 500;

        scene.add(new THREE.HemisphereLight(0xffffff, 0x71808a, 1.25));
        const sun = new THREE.DirectionalLight(0xffffff, 0.95);
        sun.position.set(35, 60, 35);
        sun.castShadow = true;
        scene.add(sun);

        const world = new THREE.Group();
        scene.add(world);

        const raycaster = new THREE.Raycaster();
        const pointer = new THREE.Vector2();
        const pickables = [];
        const locationMeshes = new Map();
        const rackGroups = new Map();
        let currentData = null;
        let selectedLocationCode = "";

        function resize() {
            const w = Math.max(1, host.clientWidth);
            const h = Math.max(320, host.clientHeight || 520);
            renderer.setSize(w, h, false);
            camera.aspect = w / h;
            camera.updateProjectionMatrix();
        }

        function clearWorld() {
            while (world.children.length) {
                const obj = world.children.pop();
                obj?.traverse?.(child => {
                    if (child.geometry) child.geometry.dispose?.();
                    if (child.material) {
                        if (Array.isArray(child.material)) child.material.forEach(m => m.dispose?.());
                        else child.material.dispose?.();
                    }
                });
            }
            pickables.length = 0;
            locationMeshes.clear();
            rackGroups.clear();
        }

        function box(w, h, d, color, opacity = 1) {
            const material = new THREE.MeshStandardMaterial({
                color: isHex(color) ? color : "#8c98a2",
                roughness: 0.68,
                metalness: 0.08,
                transparent: opacity < 1,
                opacity
            });
            const mesh = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), material);
            mesh.castShadow = true;
            mesh.receiveShadow = true;
            return mesh;
        }

        function setInfo(location, rack) {
            if (selectedTarget) {
                if (!location && !rack) {
                    selectedTarget.textContent = "Haz clic en un rack o una ubicación.";
                } else if (location) {
                    selectedTarget.innerHTML =
                        `<strong>${location.codigoUbicacion || "UBICACIÓN"}</strong><br>` +
                        `${location.codigoColor || ""} · ${location.nombreColor || ""}<br>` +
                        `Rack: ${rack?.codigoRack || location.codigoRack || "—"} · Posición: ${location.posicion || "—"} · Altura: ${location.altura || "—"}` +
                        (location.ocupada ? `<br><b>OCUPADA</b> · ${location.unidadCodigo || ""}` : `<br><b>LIBRE</b>`);
                } else {
                    const locs = (currentData?.ubicaciones || []).filter(x => String(x.rackId) === String(rack.id));
                    const occ = locs.filter(x => x.ocupada).length;
                    selectedTarget.innerHTML = `<strong>${rack.codigoRack || "RACK"}</strong><br>${rack.nombreRack || ""}<br>Ubicaciones: ${locs.length} · Libres: ${locs.length - occ} · Ocupadas: ${occ}`;
                }
            }

            if (infoTarget && currentData?.layout) {
                const l = currentData.layout;
                infoTarget.innerHTML = `<strong>${l.nombreLayout || l.codigoLayout || "Layout"}</strong> · ${currentData.almacen?.codigo || ""}`;
            }
        }

        function focusLocation(code) {
            const key = String(code || "").trim().toUpperCase();
            if (!key) return false;

            let entry = locationMeshes.get(key);
            if (!entry) {
                for (const [k, v] of locationMeshes.entries()) {
                    if (String(v.location.codigoMapa3D || "").trim().toUpperCase() === key) {
                        entry = v;
                        break;
                    }
                }
            }
            if (!entry) return false;

            for (const [, v] of locationMeshes) {
                if (v.mesh?.material?.emissive) v.mesh.material.emissive.setHex(0x000000);
            }

            if (entry.mesh?.material?.emissive) entry.mesh.material.emissive.setHex(0x5b4800);
            selectedLocationCode = entry.location.codigoUbicacion || key;

            const p = new THREE.Vector3();
            entry.mesh.getWorldPosition(p);
            controls.target.copy(p);
            const d = 8;
            camera.position.set(p.x + d, p.y + d * 0.7, p.z + d);
            controls.update();
            setInfo(entry.location, entry.rack);
            return true;
        }

        function build(data) {
            currentData = data;
            clearWorld();

            const racks = Array.isArray(data?.racks) ? data.racks : [];
            const locations = Array.isArray(data?.ubicaciones) ? data.ubicaciones : [];

            if (!racks.length) {
                host.insertAdjacentHTML("beforeend", '<div style="position:absolute;inset:0;display:grid;place-items:center;color:#66747c;font:700 12px Segoe UI">No hay racks configurados en este layout.</div>');
                camera.position.set(15, 15, 15);
                controls.target.set(0, 0, 0);
                resize();
                return;
            }

            let minX = Infinity, maxX = -Infinity, minZ = Infinity, maxZ = -Infinity, maxH = 1;

            for (const rack of racks) {
                const width = Math.max(1, num(rack.ancho, 10));
                const height = Math.max(1, num(rack.alto, 6));
                const depth = Math.max(.5, num(rack.profundidad, 2));
                const x = num(rack.posX, 0);
                const z = num(rack.posZ, 0);
                const rot = THREE.MathUtils.degToRad(num(rack.rotacionY, 0));
                const levels = Math.max(1, Math.trunc(num(rack.niveles, 4)));
                const posCount = Math.max(1, Math.trunc(num(rack.posicionesPorNivel, 10)));

                const group = new THREE.Group();
                group.position.set(x, 0, z);
                group.rotation.y = rot;
                group.userData.rack = rack;
                world.add(group);
                rackGroups.set(String(rack.id), group);

                const frame = box(width, height, depth, isHex(rack.hexColor) ? rack.hexColor : "#91a0aa", .18);
                frame.position.y = height / 2;
                frame.userData = { type: "rack", rack };
                group.add(frame);
                pickables.push(frame);

                const edges = new THREE.LineSegments(
                    new THREE.EdgesGeometry(frame.geometry),
                    new THREE.LineBasicMaterial({ color: 0x40515d })
                );
                edges.position.copy(frame.position);
                group.add(edges);

                const label = labelSprite(rack.codigoRack || "RACK");
                label.position.set(0, height + .85, 0);
                group.add(label);

                const rackLocs = locations.filter(u => String(u.rackId) === String(rack.id));
                const levelH = height / levels;
                const cellW = width / posCount;
                const slotW = Math.max(.12, cellW * .74);
                const slotH = Math.max(.12, levelH * .58);
                const slotD = Math.max(.08, depth * .09);

                rackLocs.forEach((loc, i) => {
                    const pi = Math.min(
                        posCount - 1,
                        physicalPositionIndex(
                            loc,
                            i % posCount
                        )
                    );
                    const li = Math.min(levels - 1, levelIndex(loc.altura, Math.floor(i / posCount) % levels));
                    const lx = -width / 2 + cellW * (pi + .5);
                    const ly = levelH * (li + .5);
                    const lz = depth / 2 + slotD / 2 + .02;
                    const c = isHex(loc.hexColor) ? loc.hexColor : "#aeb8be";
                    const mesh = box(slotW, slotH, slotD, c, loc.activo === false ? .25 : .92);
                    mesh.position.set(lx, ly, lz);
                    mesh.userData = { type: "location", location: loc, rack };
                    if (loc.ocupada && mesh.material?.emissive) mesh.material.emissive.setHex(0x222222);
                    group.add(mesh);
                    pickables.push(mesh);
                    const code = String(loc.codigoUbicacion || "").trim().toUpperCase();
                    if (code) locationMeshes.set(code, { mesh, location: loc, rack });
                });

                minX = Math.min(minX, x - width / 2 - depth);
                maxX = Math.max(maxX, x + width / 2 + depth);
                minZ = Math.min(minZ, z - width / 2 - depth);
                maxZ = Math.max(maxZ, z + width / 2 + depth);
                maxH = Math.max(maxH, height);
            }

            const cx = (minX + maxX) / 2;
            const cz = (minZ + maxZ) / 2;
            const sx = Math.max(10, maxX - minX);
            const sz = Math.max(10, maxZ - minZ);
            const floor = new THREE.Mesh(
                new THREE.PlaneGeometry(sx + 16, sz + 16),
                new THREE.MeshStandardMaterial({ color: 0xf8fafb, roughness: 1 })
            );
            floor.rotation.x = -Math.PI / 2;
            floor.position.set(cx, -.03, cz);
            floor.receiveShadow = true;
            world.add(floor);

            const grid = new THREE.GridHelper(Math.max(sx, sz) + 16, 30, 0x92a2ad, 0xd1d9de);
            grid.position.set(cx, 0, cz);
            world.add(grid);

            const distance = Math.max(18, Math.max(sx, sz, maxH) * 1.35);
            camera.position.set(cx + distance, Math.max(14, distance * .75), cz + distance);
            controls.target.set(cx, maxH * .3, cz);
            controls.update();
            resize();

            const wanted = initialLocation || (searchInput?.value || "");
            if (wanted) setTimeout(() => focusLocation(wanted), 50);
            setInfo(null, null);
        }

        renderer.domElement.addEventListener("pointerdown", e => {
            const rect = renderer.domElement.getBoundingClientRect();
            pointer.x = ((e.clientX - rect.left) / rect.width) * 2 - 1;
            pointer.y = -((e.clientY - rect.top) / rect.height) * 2 + 1;
            raycaster.setFromCamera(pointer, camera);
            const hits = raycaster.intersectObjects(pickables, false);
            if (!hits.length) return;
            const data = hits[0].object.userData || {};
            if (data.type === "location") {
                focusLocation(data.location.codigoUbicacion || data.location.codigoMapa3D);
            } else if (data.type === "rack") {
                const rack = data.rack;
                const g = rackGroups.get(String(rack.id));
                const p = new THREE.Vector3();
                g?.getWorldPosition(p);
                controls.target.copy(p);
                camera.position.set(p.x + 10, Math.max(8, num(rack.alto, 6) * 1.3), p.z + 10);
                controls.update();
                setInfo(null, rack);
            }
        });

        const doSearch = () => {
            const value = (searchInput?.value || "").trim().toUpperCase();
            if (!value) return;
            if (!focusLocation(value) && selectedTarget) selectedTarget.textContent = `No se encontró ${value} dentro de este layout.`;
        };
        searchButton?.addEventListener("click", doSearch);
        searchInput?.addEventListener("keydown", e => {
            if (e.key === "Enter") { e.preventDefault(); doSearch(); }
        });

        window.addEventListener("sigo:layout3d-focus", e => {
            const targetId = e.detail?.targetId;
            if (targetId && targetId !== host.id) return;
            focusLocation(e.detail?.location || "");
        });

        window.addEventListener("resize", resize);

        function animate() {
            requestAnimationFrame(animate);
            controls.update();
            renderer.render(scene, camera);
        }
        animate();

        resize();
        readJson(endpoint)
            .then(normalizeData)
            .then(build)
            .catch(err => {
                host.innerHTML = `<div style="position:absolute;inset:0;display:grid;place-items:center;padding:20px;color:#9a1d2a;text-align:center;font:700 12px Segoe UI">${err.message}</div>`;
            });
    }
})();