(() => {
    if (window.__transferenciaCargaMontacargasLoaded) return;
    window.__transferenciaCargaMontacargasLoaded = true;

    const $ = id => document.getElementById(id);

    function ready(fn) {
        if (document.readyState === "loading") {
            document.addEventListener("DOMContentLoaded", fn, { once: true });
        } else {
            fn();
        }
    }

    function getCards() {
        return Array.from(document.querySelectorAll(".m-item[data-sku]"));
    }

    function getProgress(card) {
        const badge = card?.querySelector(".js-badge");
        if (!badge) return 0;

        const text = (badge.textContent || "").trim().toLowerCase();
        if (text.includes("completo")) return 100;

        const n = Number(text.replace(/[^\d]/g, ""));
        return Number.isFinite(n) ? Math.max(0, Math.min(100, n)) : 0;
    }

    function summary() {
        const cards = getCards();
        const done = cards.filter(x => getProgress(x) >= 100).length;
        const started = cards.filter(x => getProgress(x) > 0).length;

        return {
            total: cards.length,
            done,
            started,
            allDone: cards.length > 0 && done === cards.length,
            anyStarted: started > 0
        };
    }

    function clearFocus() {
        document.querySelectorAll(".fork-focus-target")
            .forEach(x => x.classList.remove("fork-focus-target"));
    }

    function setStep(step, state) {
        if (!step) return;

        step.classList.remove("is-active", "is-done", "is-waiting");
        step.classList.add(`is-${state}`);

        const stateEl = step.querySelector(".fork-step-state");
        if (!stateEl) return;

        stateEl.textContent =
            state === "done"
                ? "Listo"
                : state === "active"
                    ? "Ahora"
                    : "Después";
    }

    function updateGuide() {
        const steps = Array.from(
            document.querySelectorAll(".fork-transfer-step")
        );

        if (steps.length < 3) return;

        const scan = $("scanEtiqueta");
        const transfer = $("btnTransferir");
        const next = $("forkTransferNextText");
        const s = summary();

        clearFocus();

        if (!s.anyStarted) {
            setStep(steps[0], "active");
            setStep(steps[1], "waiting");
            setStep(steps[2], "waiting");

            if (next) {
                next.textContent =
                    "Escanea la primera etiqueta o tarima.";
            }

            scan?.classList.add("fork-focus-target");
            return;
        }

        if (!s.allDone) {
            setStep(steps[0], "done");
            setStep(steps[1], "active");
            setStep(steps[2], "waiting");

            if (next) {
                next.textContent =
                    `${s.started} producto(s) con avance. Sigue escaneando y revisa los porcentajes.`;
            }

            scan?.classList.add("fork-focus-target");
            return;
        }

        setStep(steps[0], "done");
        setStep(steps[1], "done");
        setStep(steps[2], "active");

        if (next) {
            next.textContent =
                "Todo está completo. Ya puedes transferir.";
        }

        if (transfer && !transfer.disabled) {
            transfer.classList.add("fork-focus-target");
        }
    }

    function wireStepClicks() {
        const scan = $("scanEtiqueta");
        const transfer = $("btnTransferir");

        document.querySelectorAll(".fork-transfer-step")
            .forEach(step => {
                step.addEventListener("click", () => {
                    const num = step.dataset.forkStep;

                    if (num === "1") {
                        scan?.focus();
                        return;
                    }

                    if (num === "2") {
                        document.querySelector(".m-list-cards")
                            ?.scrollIntoView({
                                behavior: "smooth",
                                block: "start"
                            });
                        return;
                    }

                    if (
                        num === "3" &&
                        transfer &&
                        !transfer.disabled
                    ) {
                        transfer.focus();
                        transfer.scrollIntoView({
                            behavior: "smooth",
                            block: "center"
                        });
                    }
                });
            });
    }

    function observeChanges() {
        const observer = new MutationObserver(() => {
            window.requestAnimationFrame(updateGuide);
        });

        getCards().forEach(card => {
            observer.observe(card, {
                subtree: true,
                childList: true,
                characterData: true,
                attributes: true,
                attributeFilter: ["style", "class"]
            });
        });

        const transfer = $("btnTransferir");
        if (transfer) {
            observer.observe(transfer, {
                attributes: true,
                attributeFilter: ["disabled", "class"]
            });
        }

        const scanMsg = $("scanMsg");
        if (scanMsg) {
            observer.observe(scanMsg, {
                subtree: true,
                childList: true,
                characterData: true,
                attributes: true,
                attributeFilter: ["class"]
            });
        }

        setInterval(updateGuide, 1600);
    }

    function init() {
        const scan = $("scanEtiqueta");
        if (!scan) return;

        wireStepClicks();
        observeChanges();
        updateGuide();

        scan.addEventListener("focus", () => {
            updateGuide();
        });

        window.addEventListener("pageshow", updateGuide);
    }

    ready(init);
})();
