(() => {
    "use strict";

    const BASE_URL = "/InventarioTiempoReal";
    const SITE_NAME = "Carnes G";
    const LOGO_URL = "/images/logoCubo.png";
    const PAGE_SIZE = 200;
    const MAX_PAGES = 250;

    const button = document.getElementById("btnPdfReport");
    const fromInput = document.getElementById("repDesde");
    const toInput = document.getElementById("repHasta");
    const warehouseSelect = document.getElementById("repAlmacen");

    if (!button) {
        return;
    }

    function number(value) {
        if (typeof value === "number") {
            return Number.isFinite(value) ? value : 0;
        }

        const parsed = Number(
            String(value ?? "")
                .replace(/,/g, "")
                .trim()
        );

        return Number.isFinite(parsed) ? parsed : 0;
    }

    function formatNumber(value, decimals = 0) {
        return number(value).toLocaleString("es-MX", {
            minimumFractionDigits: decimals,
            maximumFractionDigits: decimals
        });
    }

    function formatKg(value) {
        return formatNumber(value, 3);
    }

    function escapeHtml(value) {
        return String(value ?? "")
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#039;");
    }

    function normalize(value) {
        return String(value ?? "")
            .normalize("NFD")
            .replace(/[\u0300-\u036f]/g, "")
            .trim()
            .toUpperCase();
    }

    function parseDate(value) {
        if (!value) return null;

        const parsed = new Date(value);
        return Number.isNaN(parsed.getTime()) ? null : parsed;
    }

    function formatDateTime(value) {
        const parsed = value instanceof Date ? value : parseDate(value);
        if (!parsed) return "N/D";

        return parsed.toLocaleString("es-MX", {
            year: "numeric",
            month: "2-digit",
            day: "2-digit",
            hour: "2-digit",
            minute: "2-digit"
        });
    }

    function formatDuration(milliseconds) {
        const minutes = Math.max(
            0,
            Math.round(number(milliseconds) / 60000)
        );

        const hours = Math.floor(minutes / 60);
        const remaining = minutes % 60;

        if (hours <= 0) {
            return `${remaining} min`;
        }

        return `${hours} h ${String(remaining).padStart(2, "0")} min`;
    }

    function todayInputValue() {
        const now = new Date();
        const local = new Date(
            now.getTime() - now.getTimezoneOffset() * 60000
        );

        return local.toISOString().slice(0, 10);
    }

    function currentFilter() {
        const desde = fromInput?.value || todayInputValue();
        const hasta = toInput?.value || desde;

        return {
            almacen: warehouseSelect?.value || "ALL",
            desde,
            hasta
        };
    }

    function selectedWarehouseText() {
        if (!warehouseSelect || warehouseSelect.value === "ALL") {
            return "Todos los almacenes permitidos";
        }

        return (
            warehouseSelect.selectedOptions?.[0]?.textContent?.trim() ||
            warehouseSelect.value
        );
    }

    async function postJson(endpoint, payload) {
        const response = await fetch(`${BASE_URL}/${endpoint}`, {
            method: "POST",
            headers: {
                "Content-Type": "application/json; charset=utf-8",
                "Accept": "application/json"
            },
            body: JSON.stringify(payload)
        });

        const contentType =
            response.headers.get("content-type") || "";

        const data = contentType.includes("application/json")
            ? await response.json()
            : await response.text();

        if (!response.ok) {
            const message =
                typeof data === "object"
                    ? data?.mensaje || data?.message
                    : data;

            throw new Error(
                message ||
                `Error HTTP ${response.status} al consultar ${endpoint}.`
            );
        }

        return data;
    }

    async function fetchAllPages(endpoint, filter, extra, onProgress) {
        const rows = [];
        let page = 1;
        let pages = 1;
        let total = 0;
        let truncated = false;

        do {
            const result = await postJson(endpoint, {
                ...filter,
                ...(extra || {}),
                pagina: page,
                tamanoPagina: PAGE_SIZE
            });

            const batch = Array.isArray(result?.rows)
                ? result.rows
                : [];

            rows.push(...batch);
            total = number(result?.total);

            pages = Math.max(
                1,
                number(result?.totalPaginas) || 1
            );

            onProgress?.({
                page,
                pages,
                loaded: rows.length,
                total
            });

            if (!batch.length || page >= pages) {
                break;
            }

            page++;

            if (page > MAX_PAGES) {
                truncated = true;
                break;
            }
        } while (true);

        return {
            rows,
            total,
            truncated
        };
    }

    function correctReading(status) {
        const normalized = normalize(status);

        return [
            "CORRECTO",
            "CORRECTA",
            "VALIDO",
            "VALIDA",
            "OK",
            "GUARDADO",
            "GUARDADA"
        ].includes(normalized);
    }

    function userName(value) {
        const text = String(value ?? "").trim();
        return text || "Sin usuario registrado";
    }

    function warehouseName(item) {
        return (
            item?.almacenMostrar ||
            item?.almacen ||
            item?.almacenId ||
            "N/D"
        );
    }

    function buildOperators(readings, incidents) {
        const map = new Map();

        function getOperator(value) {
            const name = userName(value);

            if (!map.has(name)) {
                map.set(name, {
                    name,
                    total: 0,
                    correct: 0,
                    observed: 0,
                    kg: 0,
                    incidents: 0,
                    incidentKg: 0,
                    first: null,
                    last: null
                });
            }

            return map.get(name);
        }

        readings.forEach(item => {
            const operator = getOperator(item?.usuarioRegistro);
            const date = parseDate(item?.fechaRegistro);

            operator.total++;
            operator.kg += number(
                item?.pesoNeto ?? item?.pesoKg ?? item?.kg
            );

            if (correctReading(item?.estado)) {
                operator.correct++;
            } else {
                operator.observed++;
            }

            if (date) {
                if (!operator.first || date < operator.first) {
                    operator.first = date;
                }

                if (!operator.last || date > operator.last) {
                    operator.last = date;
                }
            }
        });

        incidents.forEach(item => {
            const operator = getOperator(item?.usuarioRegistro);
            operator.incidents++;
            operator.incidentKg += number(item?.pesoKg);
        });

        return Array.from(map.values())
            .map(item => {
                const activeMilliseconds =
                    item.first && item.last
                        ? item.last.getTime() - item.first.getTime()
                        : 0;

                const activeHours =
                    activeMilliseconds > 0
                        ? activeMilliseconds / 3600000
                        : 0;

                return {
                    ...item,
                    activeMilliseconds,
                    rate:
                        activeHours > 0
                            ? item.correct / activeHours
                            : item.correct
                };
            })
            .sort((a, b) =>
                b.correct - a.correct ||
                b.kg - a.kg ||
                a.name.localeCompare(b.name)
            );
    }

    function overallVerdict(summary) {
        const expected = number(summary?.totalEsperado);
        const counted = number(summary?.totalContadas);
        const pending = number(summary?.totalPendiente);
        const incidents = number(summary?.totalIncidencias);
        const readings = number(summary?.totalLecturas);

        const progress =
            expected > 0
                ? counted * 100 / expected
                : 0;

        const incidentRate =
            readings > 0
                ? incidents * 100 / readings
                : 0;

        if (
            progress >= 99.5 &&
            pending <= 0 &&
            incidents <= 0
        ) {
            return {
                label: "AUDITORÍA CONFORME",
                css: "good",
                progress,
                incidentRate
            };
        }

        if (
            progress >= 98 &&
            incidentRate <= 2
        ) {
            return {
                label: "CON OBSERVACIONES",
                css: "warn",
                progress,
                incidentRate
            };
        }

        return {
            label: "REQUIERE REVISIÓN",
            css: "bad",
            progress,
            incidentRate
        };
    }

    function warehouseVerdict(item) {
        const progress = number(item?.avance);
        const pending = number(item?.pendientes);
        const incidents =
            number(item?.sobrantes) +
            number(item?.mezcladas) +
            number(item?.incidenciasManuales);

        if (
            progress >= 99 &&
            pending <= 0 &&
            incidents <= 1
        ) {
            return {
                label: "Conforme",
                css: "good"
            };
        }

        if (
            progress >= 97 &&
            pending <= 5
        ) {
            return {
                label: "Con observaciones",
                css: "warn"
            };
        }

        return {
            label: "Requiere revisión",
            css: "bad"
        };
    }

    function topBy(rows, selector) {
        if (!rows.length) return null;

        return [...rows].sort(
            (a, b) => selector(b) - selector(a)
        )[0];
    }

    function tableRows(rows, emptyMessage, builder, colspan) {
        if (!rows.length) {
            return `
                <tr>
                    <td colspan="${colspan}" class="empty">
                        ${escapeHtml(emptyMessage)}
                    </td>
                </tr>`;
        }

        return rows.map(builder).join("");
    }

    function buildReportHtml(context) {
        const {
            summary,
            readings,
            incidents,
            filter,
            warehouseText,
            readingsTruncated,
            incidentsTruncated
        } = context;

        const warehouses = Array.isArray(summary?.resumenAlmacenes)
            ? summary.resumenAlmacenes
            : [];

        const sessions = Array.isArray(summary?.sesiones)
            ? summary.sesiones
            : [];

        const stock = Array.isArray(summary?.stockPorSku)
            ? summary.stockPorSku
            : [];

        const incidentTypes =
            Array.isArray(summary?.incidenciasResumen)
                ? summary.incidenciasResumen
                : [];

        const operators = buildOperators(readings, incidents);
        const verdict = overallVerdict(summary);

        const expected = number(summary?.totalEsperado);
        const counted = number(summary?.totalContadas);
        const pending = number(summary?.totalPendiente);
        const totalReadings = number(summary?.totalLecturas);
        const totalKg = number(summary?.totalKgContados);
        const totalIncidents = number(summary?.totalIncidencias);

        const topScanner = operators[0] || null;
        const topKg = topBy(operators, item => item.kg);
        const topIncident = topBy(
            operators,
            item => item.incidents
        );

        const topStock = [...stock]
            .sort((a, b) =>
                number(b?.kg) - number(a?.kg) ||
                number(b?.cantidad) - number(a?.cantidad)
            )
            .slice(0, 10);

        const remainingStock = Math.max(
            0,
            stock.length - topStock.length
        );

        const criticalIncidents = [...incidents]
            .sort((a, b) =>
                number(b?.pesoKg) - number(a?.pesoKg) ||
                (
                    parseDate(b?.fecha)?.getTime() || 0
                ) -
                (
                    parseDate(a?.fecha)?.getTime() || 0
                )
            )
            .slice(0, 10);

        const allDates = [
            ...readings.map(item => parseDate(item?.fechaRegistro)),
            ...sessions.flatMap(item => [
                parseDate(item?.fechaInicio),
                parseDate(item?.fechaCierre)
            ])
        ].filter(Boolean);

        const operationStart = allDates.length
            ? new Date(
                Math.min(...allDates.map(date => date.getTime()))
            )
            : null;

        const operationEnd = allDates.length
            ? new Date(
                Math.max(...allDates.map(date => date.getTime()))
            )
            : null;

        const conclusion = [
            `Se contabilizaron ${formatNumber(counted)} de ` +
            `${formatNumber(expected)} cajas esperadas ` +
            `(${verdict.progress.toFixed(2)}% de avance).`,

            pending > 0
                ? `Permanecen ${formatNumber(pending)} cajas pendientes.`
                : "No se reportan cajas pendientes.",

            totalIncidents > 0
                ? `Se registraron ${formatNumber(totalIncidents)} incidencias ` +
                  `(${verdict.incidentRate.toFixed(2)}% de las lecturas).`
                : "No se registraron incidencias.",

            topScanner
                ? `${topScanner.name} obtuvo el mayor número de lecturas ` +
                  `correctas: ${formatNumber(topScanner.correct)}.`
                : "No hay información suficiente para determinar productividad."
        ].join(" ");

        const logo = new URL(
            LOGO_URL,
            window.location.origin
        ).href;

        const warnings = [
            readingsTruncated
                ? "La consulta de lecturas alcanzó el límite técnico de páginas."
                : "",
            incidentsTruncated
                ? "La consulta de incidencias alcanzó el límite técnico de páginas."
                : ""
        ].filter(Boolean);

        return `<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Auditoría de inventario ${escapeHtml(filter.desde)} - ${escapeHtml(filter.hasta)}</title>
<style>
:root{--brand:#8b0000;--ink:#20252b;--muted:#65707a;--line:#d9dee3;--soft:#f4f6f8;--good:#146c43;--goodbg:#e8f7ef;--warn:#795700;--warnbg:#fff3cd;--bad:#a31623;--badbg:#fdebed}
*{box-sizing:border-box}
body{margin:0;background:#e9ecef;color:var(--ink);font-family:Arial,Helvetica,sans-serif;font-size:8.2px;line-height:1.25}
.toolbar{position:sticky;top:0;z-index:20;display:flex;justify-content:center;gap:8px;padding:8px;background:#20252b}
.toolbar button{border:0;border-radius:6px;padding:8px 13px;cursor:pointer;font-weight:700}
.toolbar .print{background:#198754;color:#fff}.toolbar .close{background:#fff;color:#222}
.sheet{width:297mm;min-height:210mm;margin:10px auto;padding:8mm;background:#fff;box-shadow:0 8px 26px rgba(0,0,0,.18)}
.header{display:grid;grid-template-columns:48px minmax(0,1fr) 145px;align-items:center;gap:9px;padding-bottom:7px;border-bottom:2px solid var(--brand)}
.header img{width:43px;max-height:43px;object-fit:contain}
h1{margin:0;color:var(--brand);font-size:16px;line-height:1.05}
.subtitle{margin-top:3px;color:var(--muted);font-size:8px}
.verdict{padding:8px;border-radius:7px;text-align:center;font-size:9px;font-weight:800}
.good{color:var(--good);background:var(--goodbg);border:1px solid #9bcdb5}.warn{color:var(--warn);background:var(--warnbg);border:1px solid #e2c65f}.bad{color:var(--bad);background:var(--badbg);border:1px solid #e4a3aa}
.grid4{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:5px;margin-top:7px}
.grid8{display:grid;grid-template-columns:repeat(8,minmax(0,1fr));gap:5px;margin-top:7px}
.box{min-width:0;padding:6px;border:1px solid var(--line);border-radius:6px;background:#fff}
.box span{display:block;color:var(--muted);font-size:6.5px;font-weight:700;text-transform:uppercase}
.box strong{display:block;margin-top:2px;font-size:10.5px;overflow-wrap:anywhere}
.executive{margin-top:7px;padding:7px 9px;border-left:4px solid var(--brand);background:#faf5f5}
.executive h2,.section h2{margin:0 0 4px;color:var(--brand);font-size:10px;text-transform:uppercase}
.section{margin-top:8px;break-inside:avoid}.section.allow-break{break-inside:auto}
table{width:100%;border-collapse:collapse;table-layout:fixed}
th,td{padding:3.5px 4px;border:1px solid var(--line);vertical-align:top;overflow-wrap:anywhere}
th{background:#edf0f2;color:#4f5962;font-size:6.5px;text-align:left;text-transform:uppercase}
td.num,th.num{text-align:right;font-variant-numeric:tabular-nums}
tbody tr:nth-child(even){background:#fafafa}
.small{color:var(--muted);font-size:6.5px}.empty{padding:10px;text-align:center;color:var(--muted)}
.notes{margin-top:8px;padding:6px;border:1px dashed #bdc5cc;color:var(--muted);font-size:6.5px}
.signatures{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:25px;margin-top:20px}
.signature{padding-top:18px;border-top:1px solid #777;text-align:center;font-size:6.5px}
.footer{margin-top:9px;padding-top:4px;border-top:1px solid var(--line);color:var(--muted);font-size:6px;text-align:center}
@page{size:A4 landscape;margin:6mm}
@media print{body{background:#fff;print-color-adjust:exact;-webkit-print-color-adjust:exact}.toolbar{display:none!important}.sheet{width:auto;min-height:auto;margin:0;padding:0;box-shadow:none}.section{page-break-inside:avoid}.section.allow-break{page-break-inside:auto}thead{display:table-header-group}tr{page-break-inside:avoid}}
</style>
</head>
<body>
<div class="toolbar">
<button class="print" onclick="window.print()">Imprimir / guardar como PDF</button>
<button class="close" onclick="window.close()">Cerrar</button>
</div>
<main class="sheet">
<header class="header">
<img src="${escapeHtml(logo)}" alt="Carnes G">
<div><h1>Informe ejecutivo de auditoría de inventario</h1><div class="subtitle">${escapeHtml(SITE_NAME)} · ${escapeHtml(warehouseText)} · ${escapeHtml(filter.desde)} al ${escapeHtml(filter.hasta)}</div></div>
<div class="verdict ${verdict.css}">${escapeHtml(verdict.label)}</div>
</header>

<section class="grid4">
<div class="box"><span>Ventana operativa</span><strong>${escapeHtml(formatDateTime(operationStart))} — ${escapeHtml(formatDateTime(operationEnd))}</strong></div>
<div class="box"><span>Almacenes</span><strong>${formatNumber(warehouses.length)}</strong></div>
<div class="box"><span>Sesiones</span><strong>${formatNumber(sessions.length)}</strong></div>
<div class="box"><span>Personal participante</span><strong>${formatNumber(operators.length)}</strong></div>
</section>

<section class="grid8">
<div class="box"><span>Cajas esperadas</span><strong>${formatNumber(expected)}</strong></div>
<div class="box"><span>Cajas contadas</span><strong>${formatNumber(counted)}</strong></div>
<div class="box"><span>Pendientes</span><strong>${formatNumber(pending)}</strong></div>
<div class="box"><span>Avance</span><strong>${verdict.progress.toFixed(2)}%</strong></div>
<div class="box"><span>Lecturas</span><strong>${formatNumber(totalReadings)}</strong></div>
<div class="box"><span>Kg</span><strong>${formatKg(totalKg)}</strong></div>
<div class="box"><span>Incidencias</span><strong>${formatNumber(totalIncidents)}</strong></div>
<div class="box"><span>Tasa incidencias</span><strong>${verdict.incidentRate.toFixed(2)}%</strong></div>
</section>

<section class="executive"><h2>Conclusión ejecutiva</h2>${escapeHtml(conclusion)}</section>

<section class="grid4">
<div class="box"><span>Más lecturas correctas</span><strong>${topScanner ? `${escapeHtml(topScanner.name)} · ${formatNumber(topScanner.correct)}` : "N/D"}</strong></div>
<div class="box"><span>Mayor volumen</span><strong>${topKg ? `${escapeHtml(topKg.name)} · ${formatKg(topKg.kg)} kg` : "N/D"}</strong></div>
<div class="box"><span>Más incidencias registradas</span><strong>${topIncident && topIncident.incidents ? `${escapeHtml(topIncident.name)} · ${formatNumber(topIncident.incidents)}` : "Sin incidencias"}</strong></div>
<div class="box"><span>SKU incluidos en resumen</span><strong>${formatNumber(topStock.length)} de ${formatNumber(stock.length)}</strong></div>
</section>

<section class="section allow-break">
<h2>Productividad y trazabilidad por operador</h2>
<table>
<thead><tr><th style="width:18%">Operador</th><th>Primera lectura</th><th>Última lectura</th><th>Tiempo activo</th><th class="num">Correctas</th><th class="num">Observadas</th><th class="num">Kg</th><th class="num">Lect./hora</th><th class="num">Incidencias registradas</th></tr></thead>
<tbody>${tableRows(operators.slice(0,25),"No hay datos por operador.",item=>`<tr><td><b>${escapeHtml(item.name)}</b></td><td>${escapeHtml(formatDateTime(item.first))}</td><td>${escapeHtml(formatDateTime(item.last))}</td><td>${escapeHtml(formatDuration(item.activeMilliseconds))}</td><td class="num">${formatNumber(item.correct)}</td><td class="num">${formatNumber(item.observed)}</td><td class="num">${formatKg(item.kg)}</td><td class="num">${item.rate.toFixed(1)}</td><td class="num">${formatNumber(item.incidents)}</td></tr>`,9)}</tbody>
</table>
<div class="small">Las incidencias se atribuyen a quien las registró o detectó, no necesariamente a quien las originó.</div>
</section>

<section class="section allow-break">
<h2>Resultado por almacén</h2>
<table>
<thead><tr><th style="width:23%">Almacén</th><th class="num">Sesiones</th><th class="num">Inicial</th><th class="num">Contadas</th><th class="num">Pendientes</th><th class="num">Avance</th><th class="num">Incidencias</th><th>Dictamen</th></tr></thead>
<tbody>${tableRows(warehouses,"No hay almacenes en el periodo.",item=>{const result=warehouseVerdict(item);const inc=number(item?.sobrantes)+number(item?.mezcladas)+number(item?.incidenciasManuales);return `<tr><td><b>${escapeHtml(warehouseName(item))}</b><div class="small">${escapeHtml(item?.planta||"")}${item?.sucursal?` · ${escapeHtml(item.sucursal)}`:""}</div></td><td class="num">${formatNumber(item?.sesiones)}</td><td class="num">${formatNumber(item?.cajasIniciales)}</td><td class="num">${formatNumber(item?.contadas)}</td><td class="num">${formatNumber(item?.pendientes)}</td><td class="num">${number(item?.avance).toFixed(2)}%</td><td class="num">${formatNumber(inc)}</td><td><span class="${result.css}">${escapeHtml(result.label)}</span></td></tr>`},8)}</tbody>
</table>
</section>

<section class="section">
<h2>Resumen de incidencias</h2>
<table>
<thead><tr><th>Tipo</th><th class="num">Cantidad</th><th class="num">% del total</th></tr></thead>
<tbody>${tableRows(incidentTypes,"No se registraron incidencias.",item=>{const qty=number(item?.cantidad);const pct=totalIncidents>0?qty*100/totalIncidents:0;return `<tr><td><b>${escapeHtml(item?.tipo||"Sin tipo")}</b></td><td class="num">${formatNumber(qty)}</td><td class="num">${pct.toFixed(2)}%</td></tr>`},3)}</tbody>
</table>
</section>

<section class="section allow-break">
<h2>Principales hallazgos</h2>
<table>
<thead><tr><th>Fecha</th><th>Tipo</th><th>Almacén</th><th>SKU / producto</th><th class="num">Kg</th><th>Registró</th><th>Comentario</th></tr></thead>
<tbody>${tableRows(criticalIncidents,"No existen incidencias para mostrar.",item=>`<tr><td>${escapeHtml(formatDateTime(item?.fecha))}</td><td><b>${escapeHtml(item?.tipo||"N/D")}</b></td><td>${escapeHtml(warehouseName(item))}</td><td><b>${escapeHtml(item?.sku||item?.codigoEtiqueta||"N/D")}</b><div class="small">${escapeHtml(item?.producto||"")}</div></td><td class="num">${formatKg(item?.pesoKg)}</td><td>${escapeHtml(userName(item?.usuarioRegistro))}</td><td>${escapeHtml(item?.comentario||item?.ubicacion||"Sin comentario")}</td></tr>`,7)}</tbody>
</table>
<div class="small">Se muestran hasta 10 hallazgos, priorizados por kilogramos involucrados.</div>
</section>

<section class="section allow-break">
<h2>SKU de mayor impacto</h2>
<table>
<thead><tr><th style="width:22%">Almacén</th><th>SKU / producto</th><th class="num">Cajas</th><th class="num">Kg</th><th class="num">% lecturas</th></tr></thead>
<tbody>${tableRows(topStock,"No hay stock por SKU.",item=>{const pct=totalReadings>0?number(item?.cantidad)*100/totalReadings:0;return `<tr><td>${escapeHtml(warehouseName(item))}</td><td><b>${escapeHtml(item?.sku||"SIN SKU")}</b><div class="small">${escapeHtml(item?.producto||"")}</div></td><td class="num">${formatNumber(item?.cantidad)}</td><td class="num">${formatKg(item?.kg)}</td><td class="num">${pct.toFixed(2)}%</td></tr>`},5)}</tbody>
</table>
<div class="small">Se muestran los 10 SKU con mayor volumen. ${remainingStock ? `Otros ${formatNumber(remainingStock)} SKU permanecen disponibles en la vista operativa.` : ""}</div>
</section>

<section class="section allow-break">
<h2>Sesiones y responsables</h2>
<table>
<thead><tr><th>Folio</th><th>Inicio</th><th>Cierre</th><th>Duración</th><th>Inició</th><th>Cerró</th><th>Almacenes</th><th>Estatus</th></tr></thead>
<tbody>${tableRows(sessions.slice(0,20),"No hay sesiones en el periodo.",item=>{const start=parseDate(item?.fechaInicio);const end=parseDate(item?.fechaCierre);const duration=start&&end?end.getTime()-start.getTime():0;return `<tr><td><b>${escapeHtml(item?.folio||`Sesión ${item?.sesionId||""}`)}</b></td><td>${escapeHtml(formatDateTime(start))}</td><td>${end?escapeHtml(formatDateTime(end)):"Abierta"}</td><td>${end?escapeHtml(formatDuration(duration)):"En curso"}</td><td>${escapeHtml(item?.usuarioInicio||"N/D")}</td><td>${escapeHtml(item?.usuarioCierre||"N/D")}</td><td>${escapeHtml(item?.almacenes||"N/D")}</td><td>${escapeHtml(item?.estatus||"N/D")}</td></tr>`},8)}</tbody>
</table>
${sessions.length>20?`<div class="small">Se muestran 20 de ${formatNumber(sessions.length)} sesiones.</div>`:""}
</section>

${warnings.length?`<section class="notes"><b>Advertencias técnicas:</b>${warnings.map(item=>`<div>• ${escapeHtml(item)}</div>`).join("")}</section>`:""}

<section class="notes"><b>Metodología:</b> La productividad se calcula entre la primera y la última lectura registrada por operador. Las lecturas observadas no incrementan el indicador de lecturas correctas. El informe resume la información relevante para auditoría, Dirección General y gerencias; el detalle completo permanece disponible en SIGO.</section>

<section class="signatures"><div class="signature">Responsable de inventario</div><div class="signature">Auditor / supervisor</div><div class="signature">Dirección / gerencia</div></section>
<footer class="footer">Documento generado automáticamente por SIGO el ${escapeHtml(formatDateTime(new Date()))}.</footer>
</main>
</body>
</html>`;
    }

    function writeLoading(target) {
        target.document.open();
        target.document.write(`<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8">
<title>Generando informe</title>
<style>
body{margin:0;min-height:100vh;display:grid;place-items:center;background:#f1f3f5;font-family:Arial,sans-serif;color:#343a40}
.card{width:min(430px,calc(100% - 30px));padding:28px;border-radius:14px;background:#fff;box-shadow:0 18px 50px rgba(0,0,0,.16);text-align:center}
.spinner{width:36px;height:36px;margin:0 auto 15px;border:4px solid #e5e7eb;border-top-color:#8b0000;border-radius:50%;animation:spin .8s linear infinite}
h2{margin:0 0 8px;color:#8b0000}p{color:#667079}@keyframes spin{to{transform:rotate(360deg)}}
</style>
</head>
<body>
<div class="card"><div class="spinner"></div><h2>Generando informe de auditoría</h2><p id="audit-progress">Consultando información…</p></div>
</body>
</html>`);
        target.document.close();
    }

    function updateProgress(target, message) {
        if (!target || target.closed) return;

        const element =
            target.document.getElementById("audit-progress");

        if (element) {
            element.textContent = message;
        }
    }

    function writeError(target, error) {
        if (!target || target.closed) return;

        target.document.open();
        target.document.write(`<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8">
<title>Error</title>
<style>
body{margin:0;min-height:100vh;display:grid;place-items:center;background:#f1f3f5;font-family:Arial,sans-serif}
.card{width:min(520px,calc(100% - 30px));padding:25px;border:1px solid #e4a3aa;border-radius:12px;background:#fff;color:#842029}
button{margin-top:12px;padding:8px 13px;border:0;border-radius:6px;background:#333;color:#fff;cursor:pointer}
</style>
</head>
<body>
<div class="card"><h2>No fue posible generar el informe</h2><p>${escapeHtml(error?.message || "Error desconocido.")}</p><button onclick="window.close()">Cerrar</button></div>
</body>
</html>`);
        target.document.close();
    }

    button.addEventListener("click", async event => {
        event.preventDefault();
        event.stopImmediatePropagation();

        const filter = currentFilter();

        if (filter.desde > filter.hasta) {
            alert(
                "Rango inválido: la fecha Desde no puede ser mayor que Hasta."
            );
            return;
        }

        const target = window.open("", "_blank");

        if (!target) {
            alert(
                "El navegador bloqueó la ventana del informe. " +
                "Permite ventanas emergentes para este sitio."
            );
            return;
        }

        writeLoading(target);

        const originalHtml = button.innerHTML;
        button.disabled = true;
        button.innerHTML =
            '<span class="spinner-border spinner-border-sm me-1"></span>Preparando auditoría';

        try {
            updateProgress(
                target,
                "Consultando resumen de inventario…"
            );

            const summary = await postJson(
                "ReporteHistorico",
                filter
            );

            updateProgress(
                target,
                "Analizando lecturas e incidencias…"
            );

            const [readingResult, incidentResult] =
                await Promise.all([
                    fetchAllPages(
                        "ReporteHistoricoLecturas",
                        filter,
                        {},
                        info => updateProgress(
                            target,
                            `Lecturas analizadas: ${formatNumber(info.loaded)} ` +
                            `de ${formatNumber(info.total || info.loaded)}`
                        )
                    ),
                    fetchAllPages(
                        "ReporteHistoricoIncidencias",
                        filter,
                        { tipo: "ALL" },
                        info => updateProgress(
                            target,
                            `Incidencias analizadas: ${formatNumber(info.loaded)} ` +
                            `de ${formatNumber(info.total || info.loaded)}`
                        )
                    )
                ]);

            updateProgress(
                target,
                "Construyendo informe ejecutivo…"
            );

            const html = buildReportHtml({
                summary,
                readings: readingResult.rows,
                incidents: incidentResult.rows,
                filter,
                warehouseText: selectedWarehouseText(),
                readingsTruncated: readingResult.truncated,
                incidentsTruncated: incidentResult.truncated
            });

            const blob = new Blob([html], {
                type: "text/html;charset=utf-8"
            });

            const blobUrl = URL.createObjectURL(blob);
            target.location.replace(blobUrl);
            target.focus();

            window.setTimeout(() => {
                if (!target.closed) {
                    target.print();
                }
            }, 1100);

            window.setTimeout(() => {
                URL.revokeObjectURL(blobUrl);
            }, 60000);
        } catch (error) {
            console.error(error);
            writeError(target, error);
        } finally {
            button.disabled = false;
            button.innerHTML = originalHtml;
        }
    }, true);
})();
