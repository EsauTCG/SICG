(() => {
    "use strict";

    const host =
        document.getElementById("layoutAdmin3d");

    if (!host ||
        typeof THREE === "undefined")
    {
        return;
    }

    const almacen =
        (host.dataset.almacen || "")
            .trim();

    const selectedBox =
        document.getElementById("layoutSelectedRack");

    let renderer;
    let camera;
    let scene;
    let controls;
    let raycaster;
    let pointer;

    const rackMeshes = [];
    const dataByRack = new Map();


    function isHex(value) {
        return /^#[0-9A-F]{6}$/i.test(
            String(value || "").trim()
        );
    }


    function number(value, fallback = 0) {
        const n = Number(value);
        return Number.isFinite(n)
            ? n
            : fallback;
    }


    function createLabel(text) {
        const canvas =
            document.createElement("canvas");

        canvas.width = 512;
        canvas.height = 128;

        const ctx =
            canvas.getContext("2d");

        ctx.clearRect(
            0,
            0,
            canvas.width,
            canvas.height
        );

        ctx.fillStyle =
            "rgba(255,255,255,.92)";

        ctx.fillRect(
            0,
            0,
            canvas.width,
            canvas.height
        );

        ctx.strokeStyle =
            "rgba(30,45,55,.30)";

        ctx.lineWidth = 4;

        ctx.strokeRect(
            2,
            2,
            canvas.width - 4,
            canvas.height - 4
        );

        ctx.fillStyle =
            "#263844";

        ctx.font =
            "bold 58px Segoe UI, Arial";

        ctx.textAlign =
            "center";

        ctx.textBaseline =
            "middle";

        ctx.fillText(
            text || "RACK",
            canvas.width / 2,
            canvas.height / 2
        );

        const texture =
            new THREE.CanvasTexture(
                canvas
            );

        const material =
            new THREE.SpriteMaterial(
                {
                    map: texture,
                    transparent: true
                }
            );

        const sprite =
            new THREE.Sprite(
                material
            );

        sprite.scale.set(
            5.2,
            1.3,
            1
        );

        return sprite;
    }


    function setSelected(rack) {
        if (!selectedBox) {
            return;
        }

        if (!rack) {
            selectedBox.textContent =
                "Haz clic en un rack del mapa.";

            return;
        }

        const locations =
            dataByRack.get(
                String(rack.id)
            )
            ?.locations
            || [];

        const occupied =
            locations.filter(
                x => Boolean(x.ocupada)
            );

        const free =
            locations.length -
            occupied.length;

        const locationText =
            locations
                .slice(0, 6)
                .map(x =>
                    x.codigoUbicacion
                )
                .filter(Boolean)
                .join(", ");

        selectedBox.innerHTML =
            `<strong>${rack.codigoRack || "RACK"}</strong><br>` +
            `${rack.nombreRack || ""}<br>` +
            `Ubicaciones: ${locations.length} · ` +
            `Libres: ${free} · Ocupadas: ${occupied.length}` +
            (
                locationText
                    ? `<br><span>${locationText}${locations.length > 6 ? "…" : ""}</span>`
                    : ""
            );
    }


    function initScene() {
        scene =
            new THREE.Scene();

        scene.background =
            new THREE.Color(
                0xf0f3f5
            );

        camera =
            new THREE.PerspectiveCamera(
                48,
                Math.max(
                    1,
                    host.clientWidth
                ) /
                Math.max(
                    1,
                    host.clientHeight
                ),
                0.1,
                5000
            );

        renderer =
            new THREE.WebGLRenderer(
                {
                    antialias: true
                }
            );

        renderer.setPixelRatio(
            Math.min(
                window.devicePixelRatio || 1,
                2
            )
        );

        renderer.setSize(
            Math.max(
                1,
                host.clientWidth
            ),
            Math.max(
                1,
                host.clientHeight
            )
        );

        renderer.shadowMap.enabled =
            true;

        host.innerHTML = "";
        host.appendChild(
            renderer.domElement
        );

        const ambient =
            new THREE.HemisphereLight(
                0xffffff,
                0x7a8791,
                1.25
            );

        scene.add(
            ambient
        );

        const directional =
            new THREE.DirectionalLight(
                0xffffff,
                .9
            );

        directional.position.set(
            30,
            60,
            35
        );

        directional.castShadow =
            true;

        scene.add(
            directional
        );

        controls =
            new THREE.OrbitControls(
                camera,
                renderer.domElement
            );

        controls.enableDamping =
            true;

        controls.dampingFactor =
            .08;

        controls.screenSpacePanning =
            true;

        raycaster =
            new THREE.Raycaster();

        pointer =
            new THREE.Vector2();

        renderer.domElement.addEventListener(
            "pointerdown",
            onPointerDown
        );

        window.addEventListener(
            "resize",
            onResize
        );
    }


    function buildLayout(data) {
        const racks =
            Array.isArray(data?.racks)
                ? data.racks
                : [];

        const locations =
            Array.isArray(data?.ubicaciones)
                ? data.ubicaciones
                : [];

        dataByRack.clear();

        for (const rack of racks) {
            dataByRack.set(
                String(rack.id),
                {
                    rack,
                    locations: []
                }
            );
        }

        for (const location of locations) {
            if (location.rackId == null) {
                continue;
            }

            const key =
                String(location.rackId);

            if (!dataByRack.has(key)) {
                dataByRack.set(
                    key,
                    {
                        rack: null,
                        locations: []
                    }
                );
            }

            dataByRack
                .get(key)
                .locations
                .push(location);
        }

        if (racks.length === 0) {
            if (selectedBox) {
                selectedBox.textContent =
                    "No hay racks activos configurados para este almacén.";
            }

            camera.position.set(
                18,
                18,
                18
            );

            controls.target.set(
                0,
                0,
                0
            );

            return;
        }

        let minX =
            Number.POSITIVE_INFINITY;

        let maxX =
            Number.NEGATIVE_INFINITY;

        let minZ =
            Number.POSITIVE_INFINITY;

        let maxZ =
            Number.NEGATIVE_INFINITY;

        let maxH = 1;

        for (const rack of racks) {
            const width =
                Math.max(
                    .3,
                    number(
                        rack.ancho,
                        10
                    )
                );

            const height =
                Math.max(
                    .3,
                    number(
                        rack.alto,
                        6
                    )
                );

            const depth =
                Math.max(
                    .3,
                    number(
                        rack.profundidad,
                        2
                    )
                );

            const x =
                number(
                    rack.posX,
                    0
                );

            const z =
                number(
                    rack.posZ,
                    0
                );

            const rotation =
                THREE.MathUtils.degToRad(
                    number(
                        rack.rotacionY,
                        0
                    )
                );

            const geometry =
                new THREE.BoxGeometry(
                    width,
                    height,
                    depth
                );

            const color =
                isHex(
                    rack.hexColor
                )
                    ? rack.hexColor
                    : "#8C98A2";

            const material =
                new THREE.MeshStandardMaterial(
                    {
                        color,
                        roughness: .68,
                        metalness: .08
                    }
                );

            const mesh =
                new THREE.Mesh(
                    geometry,
                    material
                );

            mesh.position.set(
                x,
                height / 2,
                z
            );

            mesh.rotation.y =
                rotation;

            mesh.castShadow =
                true;

            mesh.receiveShadow =
                true;

            mesh.userData.rack =
                rack;

            scene.add(
                mesh
            );

            rackMeshes.push(
                mesh
            );

            const edges =
                new THREE.LineSegments(
                    new THREE.EdgesGeometry(
                        geometry
                    ),
                    new THREE.LineBasicMaterial(
                        {
                            color: 0x485762
                        }
                    )
                );

            edges.position.copy(
                mesh.position
            );

            edges.rotation.copy(
                mesh.rotation
            );

            scene.add(
                edges
            );

            const label =
                createLabel(
                    rack.codigoRack
                );

            label.position.set(
                x,
                height + 1.1,
                z
            );

            scene.add(
                label
            );

            minX =
                Math.min(
                    minX,
                    x - width / 2
                );

            maxX =
                Math.max(
                    maxX,
                    x + width / 2
                );

            minZ =
                Math.min(
                    minZ,
                    z - depth / 2
                );

            maxZ =
                Math.max(
                    maxZ,
                    z + depth / 2
                );

            maxH =
                Math.max(
                    maxH,
                    height
                );
        }

        const centerX =
            (minX + maxX) / 2;

        const centerZ =
            (minZ + maxZ) / 2;

        const sizeX =
            Math.max(
                10,
                maxX - minX
            );

        const sizeZ =
            Math.max(
                10,
                maxZ - minZ
            );

        const floorMargin =
            8;

        const floor =
            new THREE.Mesh(
                new THREE.PlaneGeometry(
                    sizeX + floorMargin * 2,
                    sizeZ + floorMargin * 2
                ),
                new THREE.MeshStandardMaterial(
                    {
                        color: 0xdfe5e9,
                        roughness: 1
                    }
                )
            );

        floor.rotation.x =
            -Math.PI / 2;

        floor.position.set(
            centerX,
            0,
            centerZ
        );

        floor.receiveShadow =
            true;

        scene.add(
            floor
        );

        const grid =
            new THREE.GridHelper(
                Math.max(
                    sizeX,
                    sizeZ
                ) + floorMargin * 2,
                30,
                0x9aa8b3,
                0xc9d1d7
            );

        grid.position.set(
            centerX,
            .01,
            centerZ
        );

        scene.add(
            grid
        );

        const distance =
            Math.max(
                sizeX,
                sizeZ,
                maxH
            ) * 1.35;

        camera.position.set(
            centerX + distance,
            Math.max(
                18,
                distance * .85
            ),
            centerZ + distance
        );

        controls.target.set(
            centerX,
            Math.max(
                1,
                maxH * .28
            ),
            centerZ
        );

        controls.update();
    }


    function onPointerDown(event) {
        if (!renderer ||
            !camera)
        {
            return;
        }

        const rect =
            renderer.domElement
                .getBoundingClientRect();

        pointer.x =
            (
                (
                    event.clientX -
                    rect.left
                ) /
                rect.width
            ) * 2 - 1;

        pointer.y =
            -(
                (
                    event.clientY -
                    rect.top
                ) /
                rect.height
            ) * 2 + 1;

        raycaster.setFromCamera(
            pointer,
            camera
        );

        const hits =
            raycaster.intersectObjects(
                rackMeshes,
                false
            );

        for (const rackMesh of rackMeshes) {
            rackMesh.material.emissive?.setHex(
                0x000000
            );
        }

        if (hits.length === 0) {
            setSelected(null);
            return;
        }

        const mesh =
            hits[0].object;

        mesh.material.emissive?.setHex(
            0x263844
        );

        setSelected(
            mesh.userData.rack
        );
    }


    function onResize() {
        if (!renderer ||
            !camera)
        {
            return;
        }

        const width =
            Math.max(
                1,
                host.clientWidth
            );

        const height =
            Math.max(
                1,
                host.clientHeight
            );

        renderer.setSize(
            width,
            height
        );

        camera.aspect =
            width / height;

        camera.updateProjectionMatrix();
    }


    function animate() {
        requestAnimationFrame(
            animate
        );

        controls?.update();

        renderer?.render(
            scene,
            camera
        );
    }


    async function load() {
        initScene();

        try {
            const qs =
                new URLSearchParams(
                    {
                        almacen
                    }
                );

            const response =
                await fetch(
                    `/Surtido/GestionLayouts/Datos3D?${qs.toString()}`,
                    {
                        headers:
                        {
                            "X-Requested-With":
                                "XMLHttpRequest"
                        }
                    }
                );

            const data =
                await response.json();

            if (!response.ok ||
                !data?.ok)
            {
                throw new Error(
                    data?.message ||
                    "No fue posible cargar el layout."
                );
            }

            buildLayout(
                data
            );
        }
        catch (error) {
            if (selectedBox) {
                selectedBox.textContent =
                    error.message;
            }
        }

        animate();
    }

    load();
})();
