/* ============================================================
   SIGO WMS - MAPA / RACK 3D
   ------------------------------------------------------------
   Fuente del destino:
       ProduccionReferencia.TipoReferenciaId = 16
       Ejemplo: R4-04A

   Layout completo:
       únicamente cuando data-layout-known="true".

   Para almacenes sin layout confirmado:
       se dibuja solamente el rack objetivo para no inventar
       la planta completa.
   ============================================================ */

(function (global) {
    "use strict";

    const contexts = new Map();

    const FACILITY = {
        widthM: 42.62,
        lengthM: 70.34,
        rackSpanM: 31.0,
        positionsPerRack: 13,
        heights: ["A", "B", "C", "D"],
        centerPassagePosition: 7
    };

    const RACK_LAYOUT = [
        { rack: 1,  z: 4.0,  bank: "NORTE" },
        { rack: 2,  z: 6.1,  bank: "NORTE" },

        { rack: 3,  z: 14.0, bank: "B1" },
        { rack: 4,  z: 16.0, bank: "B1" },
        { rack: 5,  z: 18.0, bank: "B1" },
        { rack: 6,  z: 20.0, bank: "B1" },

        { rack: 7,  z: 28.0, bank: "B2" },
        { rack: 8,  z: 30.0, bank: "B2" },
        { rack: 9,  z: 32.0, bank: "B2" },
        { rack: 10, z: 34.0, bank: "B2" },

        { rack: 11, z: 42.0, bank: "B3" },
        { rack: 12, z: 44.0, bank: "B3" },
        { rack: 13, z: 46.0, bank: "B3" },
        { rack: 14, z: 48.0, bank: "B3" },

        { rack: 15, z: 56.0, bank: "B4" },
        { rack: 16, z: 58.0, bank: "B4" },
        { rack: 17, z: 60.0, bank: "B4" },
        { rack: 18, z: 62.0, bank: "B4" },

        { rack: 19, z: 67.0, bank: "SUR" },
        { rack: 20, z: 69.0, bank: "SUR" }
    ];

    const HEIGHTS = FACILITY.heights;
    const POSITIONS = FACILITY.positionsPerRack;

    const X_START =
        (FACILITY.widthM - FACILITY.rackSpanM) / 2;

    const POS_GAP =
        FACILITY.rackSpanM / POSITIONS;

    const CENTER_X =
        X_START
        + (FACILITY.centerPassagePosition - 0.5) * POS_GAP;

    const ENTRY_Z =
        FACILITY.lengthM + 1.3;

    const LEVEL_GAP = 1.72;

    const COLORS = {
        target: 0xc65cff,
        route: 0x25d695,
        post: 0x004a96,
        beam: 0xd31f2b,
        floor: 0xbfc4c8,
        rack: 0x7b5328
    };

    function canonicalLocation(value) {
        const txt =
            String(value || "")
                .trim()
                .toUpperCase()
                .replace(/\s+/g, "");

        const m =
            txt.match(/^[RW]?(\d+)-(\d+)-?([A-D])$/);

        if (!m) {
            return null;
        }

        return `R${Number(m[1])}-${String(Number(m[2])).padStart(2, "0")}${m[3]}`;
    }

    function parseLocation(value) {
        const id =
            canonicalLocation(value);

        if (!id) {
            return null;
        }

        const m =
            id.match(/^R(\d+)-(\d+)([A-D])$/);

        if (!m) {
            return null;
        }

        return {
            id,
            rack: Number(m[1]),
            pos: Number(m[2]),
            height: m[3]
        };
    }

    function rackRow(rack) {
        return RACK_LAYOUT.find(
            x => x.rack === Number(rack)
        ) || null;
    }

    function isTunnelSlot(rack, pos) {
        return (
            Number(rack) >= 3
            &&
            Number(rack) <= 20
            &&
            Number(pos) === FACILITY.centerPassagePosition
        );
    }

    function zoneForRack(rack) {
        rack = Number(rack);

        if (rack <= 2) return "PASILLO NORTE";
        if (rack <= 6) return "PASILLO 1";
        if (rack <= 10) return "PASILLO 2";
        if (rack <= 14) return "PASILLO 3";
        if (rack <= 18) return "PASILLO 4";

        return "PASILLO SUR";
    }

    function coordForLocation(value) {
        const p =
            parseLocation(value);

        if (!p) {
            return null;
        }

        const row =
            rackRow(p.rack);

        if (!row) {
            return null;
        }

        return {
            x:
                X_START
                + (p.pos - 0.5) * POS_GAP,

            y:
                HEIGHTS.indexOf(p.height)
                * LEVEL_GAP
                + 0.48,

            z:
                row.z,

            p
        };
    }

    function dispose(containerId) {
        const ctx =
            contexts.get(containerId);

        if (!ctx) {
            return;
        }

        if (ctx.anim) {
            cancelAnimationFrame(ctx.anim);
        }

        if (ctx.resizeObserver) {
            ctx.resizeObserver.disconnect();
        }

        if (ctx.renderer) {
            ctx.renderer.dispose();

            if (ctx.renderer.domElement) {
                ctx.renderer.domElement.remove();
            }
        }

        contexts.delete(containerId);
    }

    function labelSprite(
        parent,
        text,
        x,
        y,
        z,
        sx,
        sy,
        border = "#174b82",
        font = 22
    ) {
        const cv =
            document.createElement("canvas");

        cv.width = 360;
        cv.height = 90;

        const ctx =
            cv.getContext("2d");

        ctx.fillStyle =
            "rgba(255,255,255,.95)";

        ctx.fillRect(
            0,
            0,
            cv.width,
            cv.height
        );

        ctx.strokeStyle =
            border;

        ctx.lineWidth = 5;

        ctx.strokeRect(
            3,
            3,
            cv.width - 6,
            cv.height - 6
        );

        ctx.fillStyle = "#111";

        ctx.font =
            `bold ${font}px Arial`;

        ctx.textAlign =
            "center";

        ctx.textBaseline =
            "middle";

        ctx.fillText(
            String(text || "-"),
            cv.width / 2,
            cv.height / 2
        );

        const texture =
            new THREE.CanvasTexture(cv);

        const material =
            new THREE.SpriteMaterial({
                map: texture,
                transparent: true,
                depthTest: false
            });

        const sprite =
            new THREE.Sprite(material);

        sprite.position.set(
            x,
            y,
            z
        );

        sprite.scale.set(
            sx,
            sy,
            1
        );

        sprite.renderOrder = 999;

        parent.add(sprite);

        return sprite;
    }

    function addFloorStrip(
        scene,
        x,
        z,
        w,
        d,
        color,
        opacity = 0.15
    ) {
        const mesh =
            new THREE.Mesh(
                new THREE.BoxGeometry(
                    w,
                    0.035,
                    d
                ),
                new THREE.MeshStandardMaterial({
                    color,
                    transparent: true,
                    opacity,
                    roughness: 0.85
                })
            );

        mesh.position.set(
            x,
            0.02,
            z
        );

        scene.add(mesh);

        return mesh;
    }

    function addRouteSegment(
        scene,
        a,
        b,
        width = 0.42
    ) {
        const dx =
            b.x - a.x;

        const dz =
            b.z - a.z;

        const len =
            Math.sqrt(
                dx * dx + dz * dz
            );

        const mesh =
            new THREE.Mesh(
                new THREE.BoxGeometry(
                    len,
                    0.055,
                    width
                ),
                new THREE.MeshStandardMaterial({
                    color: COLORS.route,
                    emissive: 0x0a6a47,
                    emissiveIntensity: 0.3
                })
            );

        mesh.position.set(
            (a.x + b.x) / 2,
            0.08,
            (a.z + b.z) / 2
        );

        mesh.rotation.y =
            -Math.atan2(
                dz,
                dx
            );

        scene.add(mesh);
    }

    function addRoute(
        scene,
        targetId
    ) {
        const c =
            coordForLocation(targetId);

        if (!c) {
            return;
        }

        const a = {
            x: CENTER_X,
            z: ENTRY_Z
        };

        const b = {
            x: CENTER_X,
            z: c.z
        };

        const d = {
            x: c.x,
            z: c.z
        };

        addRouteSegment(
            scene,
            a,
            b,
            0.52
        );

        addRouteSegment(
            scene,
            b,
            d,
            0.52
        );

        labelSprite(
            scene,
            "INICIO",
            CENTER_X,
            0.38,
            FACILITY.lengthM - 0.3,
            1.7,
            0.42,
            "#14794f",
            19
        );
    }

    function targetBeacon(
        scene,
        targetId
    ) {
        const c =
            coordForLocation(targetId);

        if (!c) {
            return null;
        }

        const group =
            new THREE.Group();

        const ring =
            new THREE.Mesh(
                new THREE.RingGeometry(
                    0.75,
                    1.02,
                    48
                ),
                new THREE.MeshBasicMaterial({
                    color: 0xd900ff,
                    transparent: true,
                    opacity: 0.9,
                    side: THREE.DoubleSide,
                    depthTest: false
                })
            );

        ring.rotation.x =
            -Math.PI / 2;

        ring.position.set(
            c.x,
            0.13,
            c.z
        );

        ring.renderOrder =
            2000;

        group.add(ring);

        const poleHeight =
            Math.max(
                2.7,
                c.y + 2.3
            );

        const pole =
            new THREE.Mesh(
                new THREE.CylinderGeometry(
                    0.035,
                    0.035,
                    poleHeight,
                    12
                ),
                new THREE.MeshBasicMaterial({
                    color: 0xd900ff,
                    transparent: true,
                    opacity: 0.68,
                    depthTest: false
                })
            );

        pole.position.set(
            c.x,
            poleHeight / 2,
            c.z
        );

        group.add(pole);

        labelSprite(
            group,
            `AQUÍ · ${targetId}`,
            c.x,
            c.y + 2.25,
            c.z,
            2.5,
            0.55,
            "#a400c7",
            24
        );

        scene.add(group);

        return {
            group,
            ring
        };
    }

    function addRackStructure(
        scene,
        rows,
        targetParsed,
        compact
    ) {
        const postHeight =
            HEIGHTS.length
            * LEVEL_GAP
            + 0.65;

        const postGeo =
            new THREE.BoxGeometry(
                0.10,
                postHeight,
                0.10
            );

        const postMat =
            new THREE.MeshStandardMaterial({
                color: COLORS.post,
                metalness: 0.22,
                roughness: 0.45
            });

        const dummy =
            new THREE.Object3D();

        const postCount =
            rows.length
            * (POSITIONS + 1)
            * 2;

        const posts =
            new THREE.InstancedMesh(
                postGeo,
                postMat,
                postCount
            );

        let pi = 0;

        for (const row of rows) {
            for (
                let boundary = 0;
                boundary <= POSITIONS;
                boundary++
            ) {
                const x =
                    X_START
                    + boundary * POS_GAP;

                for (const dz of [-0.58, 0.58]) {
                    dummy.position.set(
                        x,
                        postHeight / 2,
                        row.z + dz
                    );

                    dummy.updateMatrix();

                    posts.setMatrixAt(
                        pi++,
                        dummy.matrix
                    );
                }
            }
        }

        scene.add(posts);

        const beamGeo =
            new THREE.BoxGeometry(
                POS_GAP,
                0.10,
                0.10
            );

        const beamMat =
            new THREE.MeshStandardMaterial({
                color: COLORS.beam,
                metalness: 0.18,
                roughness: 0.45
            });

        let beamCount = 0;

        for (const row of rows) {
            for (
                let h = 0;
                h <= HEIGHTS.length;
                h++
            ) {
                for (
                    let pos = 1;
                    pos <= POSITIONS;
                    pos++
                ) {
                    if (
                        isTunnelSlot(
                            row.rack,
                            pos
                        )
                    ) {
                        continue;
                    }

                    beamCount += 2;
                }
            }
        }

        const beams =
            new THREE.InstancedMesh(
                beamGeo,
                beamMat,
                beamCount
            );

        let bi = 0;

        for (const row of rows) {
            for (
                let h = 0;
                h <= HEIGHTS.length;
                h++
            ) {
                for (
                    let pos = 1;
                    pos <= POSITIONS;
                    pos++
                ) {
                    if (
                        isTunnelSlot(
                            row.rack,
                            pos
                        )
                    ) {
                        continue;
                    }

                    const x =
                        X_START
                        + (pos - 0.5) * POS_GAP;

                    for (const dz of [-0.58, 0.58]) {
                        dummy.position.set(
                            x,
                            h * LEVEL_GAP,
                            row.z + dz
                        );

                        dummy.updateMatrix();

                        beams.setMatrixAt(
                            bi++,
                            dummy.matrix
                        );
                    }
                }
            }
        }

        scene.add(beams);

        for (const row of rows) {
            labelSprite(
                scene,
                `R${row.rack}`,
                X_START - 1.5,
                HEIGHTS.length * LEVEL_GAP + 0.55,
                row.z,
                1.25,
                0.48,
                "#004a96",
                20
            );
        }

        if (
            targetParsed
            &&
            rackRow(targetParsed.rack)
        ) {
            const row =
                rackRow(targetParsed.rack);

            for (
                let pos = 1;
                pos <= POSITIONS;
                pos++
            ) {
                if (
                    isTunnelSlot(
                        targetParsed.rack,
                        pos
                    )
                ) {
                    continue;
                }

                labelSprite(
                    scene,
                    String(pos).padStart(2, "0"),
                    X_START
                    + (pos - 0.5) * POS_GAP,
                    -0.32,
                    row.z,
                    0.62,
                    0.28,
                    "#333",
                    18
                );
            }

            HEIGHTS.forEach(
                (height, hi) => {
                    labelSprite(
                        scene,
                        height,
                        X_START - 0.55,
                        hi * LEVEL_GAP + 0.5,
                        row.z,
                        0.52,
                        0.31,
                        "#004a96",
                        20
                    );
                }
            );
        }
    }

    function addTargetBox(
        scene,
        targetId
    ) {
        const c =
            coordForLocation(targetId);

        if (!c) {
            return;
        }

        const pallet =
            new THREE.Mesh(
                new THREE.BoxGeometry(
                    POS_GAP * 0.72,
                    0.14,
                    0.9
                ),
                new THREE.MeshStandardMaterial({
                    color: 0x7b5328,
                    roughness: 0.62
                })
            );

        pallet.position.set(
            c.x,
            c.y - 0.22,
            c.z
        );

        scene.add(pallet);

        const material =
            new THREE.MeshStandardMaterial({
                color: COLORS.target,
                emissive: 0x5a006b,
                emissiveIntensity: 0.9,
                roughness: 0.52
            });

        for (
            let i = 0;
            i < 4;
            i++
        ) {
            const box =
                new THREE.Mesh(
                    new THREE.BoxGeometry(
                        0.62,
                        0.43,
                        0.42
                    ),
                    material
                );

            box.position.set(
                c.x
                + ((i % 2) - 0.5) * 0.66,

                c.y
                + Math.floor(i / 2) * 0.45,

                c.z
            );

            scene.add(box);
        }
    }

    function focusTarget(
        camera,
        controls,
        targetId,
        compact
    ) {
        const c =
            coordForLocation(targetId);

        if (!c) {
            return;
        }

        controls.target.set(
            c.x,
            c.y + 0.18,
            c.z
        );

        camera.position.set(
            c.x + (compact ? 2.0 : 3.2),
            c.y + (compact ? 2.0 : 2.9),
            c.z + (compact ? 4.0 : 5.4)
        );

        controls.update();
    }

    function render(
        containerId,
        options
    ) {
        const el =
            document.getElementById(containerId);

        if (!el) {
            return;
        }

        if (
            typeof THREE === "undefined"
            ||
            typeof THREE.OrbitControls === "undefined"
        ) {
            el.innerHTML =
                '<div class="wms-3d-error">No cargó Three.js. Verifica conexión o los scripts del mapa.</div>';

            return;
        }

        dispose(containerId);

        const opt =
            Object.assign(
                {
                    target: "",
                    fullLayout: false,
                    compact: false,
                    route: true
                },
                options || {}
            );

        const target =
            canonicalLocation(opt.target);

        const parsed =
            parseLocation(target);

        el.innerHTML = "";

        const scene =
            new THREE.Scene();

        scene.background =
            new THREE.Color(0xd8dadd);

        const width =
            Math.max(
                el.clientWidth,
                1
            );

        const height =
            Math.max(
                el.clientHeight,
                1
            );

        const camera =
            new THREE.PerspectiveCamera(
                opt.compact ? 34 : 43,
                width / height,
                0.1,
                1200
            );

        const renderer =
            new THREE.WebGLRenderer({
                antialias: true
            });

        renderer.setPixelRatio(
            Math.min(
                window.devicePixelRatio || 1,
                1.7
            )
        );

        renderer.setSize(
            width,
            height
        );

        renderer.shadowMap.enabled =
            !opt.compact;

        el.appendChild(
            renderer.domElement
        );

        const controls =
            new THREE.OrbitControls(
                camera,
                renderer.domElement
            );

        controls.enableDamping = true;
        controls.dampingFactor = 0.08;

        scene.add(
            new THREE.AmbientLight(
                0xffffff,
                0.82
            )
        );

        const sun =
            new THREE.DirectionalLight(
                0xffffff,
                0.92
            );

        sun.position.set(
            35,
            45,
            72
        );

        scene.add(sun);

        let rows = [];

        if (
            opt.fullLayout
            &&
            parsed
        ) {
            rows =
                RACK_LAYOUT.slice();

            const floor =
                new THREE.Mesh(
                    new THREE.BoxGeometry(
                        FACILITY.widthM,
                        0.16,
                        FACILITY.lengthM
                    ),
                    new THREE.MeshStandardMaterial({
                        color: COLORS.floor,
                        roughness: 0.93
                    })
                );

            floor.position.set(
                FACILITY.widthM / 2,
                -0.12,
                FACILITY.lengthM / 2
            );

            scene.add(floor);

            addFloorStrip(
                scene,
                CENTER_X,
                38,
                POS_GAP * 0.92,
                64,
                0x35b779,
                0.19
            );

            [10, 24, 38, 52, 65]
                .forEach(
                    (z, i) => {
                        addFloorStrip(
                            scene,
                            FACILITY.widthM / 2,
                            z,
                            FACILITY.widthM - 2,
                            2.5,
                            0x6aaed6,
                            0.12
                        );

                        labelSprite(
                            scene,
                            i === 4
                                ? "ACCESO / PREPROCESO"
                                : `PASILLO ${i + 1}`,
                            2.8,
                            0.25,
                            z,
                            3.2,
                            0.45,
                            "#276b93",
                            18
                        );
                    }
                );

            labelSprite(
                scene,
                "CÁMARA DE CONGELACIÓN  -22°C",
                FACILITY.widthM / 2,
                9.6,
                1.2,
                8.0,
                0.7,
                "#8b0000",
                20
            );

            const outlinePoints = [
                new THREE.Vector3(0, 0.05, 0),
                new THREE.Vector3(FACILITY.widthM, 0.05, 0),
                new THREE.Vector3(FACILITY.widthM, 0.05, FACILITY.lengthM),
                new THREE.Vector3(0, 0.05, FACILITY.lengthM),
                new THREE.Vector3(0, 0.05, 0)
            ];

            scene.add(
                new THREE.Line(
                    new THREE.BufferGeometry()
                        .setFromPoints(
                            outlinePoints
                        ),
                    new THREE.LineBasicMaterial({
                        color: 0x3d6d55
                    })
                )
            );

            camera.position.set(
                FACILITY.widthM + 22,
                38,
                FACILITY.lengthM + 28
            );

            controls.target.set(
                FACILITY.widthM / 2,
                0,
                FACILITY.lengthM / 2
            );
        }
        else if (parsed) {
            const row =
                rackRow(parsed.rack);

            rows =
                row
                    ? [row]
                    : [];

            // Piso local para el rack enfocado.
            const floor =
                new THREE.Mesh(
                    new THREE.BoxGeometry(
                        FACILITY.rackSpanM + 5,
                        0.12,
                        8
                    ),
                    new THREE.MeshStandardMaterial({
                        color: COLORS.floor,
                        roughness: 0.93
                    })
                );

            floor.position.set(
                FACILITY.widthM / 2,
                -0.12,
                row ? row.z : 0
            );

            scene.add(floor);
        }
        else {
            el.innerHTML =
                '<div class="wms-3d-empty">Escribe o selecciona una ubicación con formato R4-04A para mostrar el mapa.</div>';

            return;
        }

        addRackStructure(
            scene,
            rows,
            parsed,
            opt.compact
        );

        if (target) {
            addTargetBox(
                scene,
                target
            );
        }

        if (
            opt.fullLayout
            &&
            opt.route
            &&
            target
        ) {
            addRoute(
                scene,
                target
            );
        }

        const beacon =
            target
                ? targetBeacon(
                    scene,
                    target
                )
                : null;

        if (target) {
            focusTarget(
                camera,
                controls,
                target,
                opt.compact
            );
        }

        const ctx = {
            scene,
            camera,
            renderer,
            controls,
            beacon,
            anim: null,
            resizeObserver: null,
            target,
            options: opt
        };

        function animate() {
            ctx.anim =
                requestAnimationFrame(
                    animate
                );

            if (
                beacon
                &&
                beacon.ring
            ) {
                const pulse =
                    1
                    + Math.sin(
                        Date.now() / 260
                    ) * 0.12;

                beacon.ring.scale.set(
                    pulse,
                    pulse,
                    pulse
                );

                beacon.ring.material.opacity =
                    0.62
                    + Math.sin(
                        Date.now() / 260
                    ) * 0.22;
            }

            controls.update();

            renderer.render(
                scene,
                camera
            );
        }

        animate();

        if (
            typeof ResizeObserver !== "undefined"
        ) {
            ctx.resizeObserver =
                new ResizeObserver(
                    () => {
                        const w =
                            Math.max(
                                el.clientWidth,
                                1
                            );

                        const h =
                            Math.max(
                                el.clientHeight,
                                1
                            );

                        camera.aspect =
                            w / h;

                        camera.updateProjectionMatrix();

                        renderer.setSize(
                            w,
                            h
                        );
                    }
                );

            ctx.resizeObserver.observe(el);
        }

        contexts.set(
            containerId,
            ctx
        );

        updateInfo(
            el,
            target,
            opt.fullLayout
        );
    }

    function updateInfo(
        el,
        target,
        fullLayout
    ) {
        const infoSelector =
            el.dataset.infoTarget;

        if (!infoSelector) {
            return;
        }

        const info =
            document.querySelector(
                infoSelector
            );

        if (!info) {
            return;
        }

        const p =
            parseLocation(target);

        if (!p) {
            info.innerHTML =
                "Ubicación no válida.";

            return;
        }

        info.innerHTML = `
            <strong>${p.id}</strong>
            <span>${zoneForRack(p.rack)}</span>
            <span>Rack ${p.rack}</span>
            <span>Posición ${String(p.pos).padStart(2, "0")}</span>
            <span>Altura ${p.height}</span>
            <span>${fullLayout ? "Layout completo TIF 776" : "Vista enfocada del rack"}</span>
        `;
    }

    function initElement(el) {
        const target =
            el.dataset.location || "";

        const fullLayout =
            String(
                el.dataset.layoutKnown || ""
            ).toLowerCase() === "true";

        const compact =
            String(
                el.dataset.compact || ""
            ).toLowerCase() === "true";

        render(
            el.id,
            {
                target,
                fullLayout,
                compact,
                route: true
            }
        );
    }

    function initAll() {
        document
            .querySelectorAll(
                ".wms-rack3d[id]"
            )
            .forEach(
                initElement
            );
    }

    function focus(
        containerId,
        location
    ) {
        const el =
            document.getElementById(
                containerId
            );

        if (!el) {
            return false;
        }

        el.dataset.location =
            canonicalLocation(location)
            || String(location || "");

        initElement(el);

        return true;
    }

    global.SigoWms3D = {
        render,
        focus,
        canonicalLocation,
        parseLocation,
        zoneForRack,
        initAll
    };

    if (
        document.readyState === "loading"
    ) {
        document.addEventListener(
            "DOMContentLoaded",
            initAll
        );
    }
    else {
        initAll();
    }

})(window);
