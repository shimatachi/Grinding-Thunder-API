(() => {
    const svgNamespace = "http://www.w3.org/2000/svg";

    document.querySelectorAll("[data-visual-tree]").forEach(tree => {
        const viewport = tree.querySelector("[data-tree-viewport]");
        const surface = tree.querySelector("[data-tree-surface]");
        const stage = tree.querySelector("[data-tree-stage]");
        const svg = tree.querySelector("[data-tree-connectors]");
        const paths = tree.querySelector("[data-tree-paths]");
        const zoomLabel = tree.querySelector("[data-tree-zoom-label]");
        if (!viewport || !surface || !stage || !svg || !paths) return;

        let scale = 1;
        let panStart = null;

        const clampScale = value => Math.min(1.5, Math.max(.35, value));

        const nodePoint = (node, edge, isFrom) => {
            const nodeRect = node.getBoundingClientRect();
            const stageRect = stage.getBoundingClientRect();
            const left = (nodeRect.left - stageRect.left) / scale;
            const top = (nodeRect.top - stageRect.top) / scale;
            const width = nodeRect.width / scale;
            const height = nodeRect.height / scale;

            if (edge === "folder") {
                return isFrom
                    ? { x: left + width, y: top + (height / 2) }
                    : { x: left, y: top + (height / 2) };
            }

            return isFrom
                ? { x: left + (width / 2), y: top + height }
                : { x: left + (width / 2), y: top };
        };

        const drawConnectors = () => {
            const width = stage.offsetWidth;
            const height = stage.offsetHeight;
            svg.setAttribute("viewBox", `0 0 ${width} ${height}`);
            svg.setAttribute("width", width);
            svg.setAttribute("height", height);
            paths.replaceChildren();

            tree.querySelectorAll("[data-tree-edge]").forEach(edgeData => {
                const from = tree.querySelector(`[data-tree-node="${edgeData.dataset.fromNode}"]`);
                const to = tree.querySelector(`[data-tree-node="${edgeData.dataset.toNode}"]`);
                if (!from || !to) return;

                const kind = edgeData.dataset.edgeKind;
                const start = nodePoint(from, kind, true);
                const end = nodePoint(to, kind, false);
                let pathData;
                if (kind === "folder" || Math.abs(end.y - start.y) < 48) {
                    const middleX = start.x + ((end.x - start.x) / 2);
                    pathData = `M ${start.x} ${start.y} C ${middleX} ${start.y}, ${middleX} ${end.y}, ${end.x} ${end.y}`;
                } else {
                    const middleY = start.y + ((end.y - start.y) / 2);
                    pathData = `M ${start.x} ${start.y} C ${start.x} ${middleY}, ${end.x} ${middleY}, ${end.x} ${end.y}`;
                }

                const path = document.createElementNS(svgNamespace, "path");
                path.setAttribute("d", pathData);
                path.setAttribute("class", `visual-tree-edge visual-tree-edge-${kind}`);
                paths.appendChild(path);
            });
        };

        const applyScale = nextScale => {
            scale = clampScale(nextScale);
            stage.style.transform = `scale(${scale})`;
            surface.style.width = `${Math.max(viewport.clientWidth, stage.offsetWidth * scale)}px`;
            surface.style.height = `${Math.max(viewport.clientHeight, stage.offsetHeight * scale)}px`;
            if (zoomLabel) zoomLabel.textContent = `${Math.round(scale * 100)}%`;
            drawConnectors();
        };

        tree.querySelector("[data-tree-zoom-in]")?.addEventListener("click", () => applyScale(scale + .1));
        tree.querySelector("[data-tree-zoom-out]")?.addEventListener("click", () => applyScale(scale - .1));
        tree.querySelector("[data-tree-reset]")?.addEventListener("click", () => {
            applyScale(1);
            viewport.scrollTo({ left: 0, top: 0, behavior: "smooth" });
        });
        tree.querySelector("[data-tree-fit]")?.addEventListener("click", () => {
            const horizontal = (viewport.clientWidth - 24) / stage.offsetWidth;
            const vertical = (viewport.clientHeight - 24) / stage.offsetHeight;
            applyScale(Math.min(horizontal, vertical, 1));
            viewport.scrollTo({ left: 0, top: 0, behavior: "smooth" });
        });

        viewport.addEventListener("wheel", event => {
            if (!event.ctrlKey) return;
            event.preventDefault();
            applyScale(scale + (event.deltaY < 0 ? .08 : -.08));
        }, { passive: false });

        viewport.addEventListener("pointerdown", event => {
            if (event.button !== 0 || event.target.closest(".visual-tree-node")) return;
            panStart = { x: event.clientX, y: event.clientY, left: viewport.scrollLeft, top: viewport.scrollTop };
            viewport.classList.add("is-panning");
            viewport.setPointerCapture(event.pointerId);
        });
        viewport.addEventListener("pointermove", event => {
            if (!panStart) return;
            viewport.scrollLeft = panStart.left - (event.clientX - panStart.x);
            viewport.scrollTop = panStart.top - (event.clientY - panStart.y);
        });
        const stopPanning = event => {
            if (!panStart) return;
            panStart = null;
            viewport.classList.remove("is-panning");
            if (viewport.hasPointerCapture(event.pointerId)) viewport.releasePointerCapture(event.pointerId);
        };
        viewport.addEventListener("pointerup", stopPanning);
        viewport.addEventListener("pointercancel", stopPanning);

        new ResizeObserver(() => applyScale(scale)).observe(stage);
        document.addEventListener("shown.bs.tab", drawConnectors);
        applyScale(1);
    });
})();
