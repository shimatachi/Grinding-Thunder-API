(() => {
    const svgNamespace = "http://www.w3.org/2000/svg";
    const columnStep = 240;
    const rowStep = 176;

    document.querySelectorAll("[data-visual-tree]").forEach(tree => {
        const canEdit = tree.dataset.canEdit === "true";
        const editToggle = tree.querySelector("[data-tree-edit-toggle]");
        const help = tree.querySelector("[data-tree-help]");
        const inspectorForm = tree.querySelector("[data-tree-inspector-form]");
        const removeForm = tree.querySelector("[data-tree-remove-form]");
        const selectionEmpty = tree.querySelector("[data-tree-selection-empty]");
        const inspector = {
            entryId: tree.querySelector("[data-inspector-entry-id]"),
            removeEntryId: tree.querySelector("[data-remove-entry-id]"),
            name: tree.querySelector("[data-inspector-name]"),
            image: tree.querySelector("[data-inspector-image]"),
            rank: tree.querySelector("[data-inspector-rank]"),
            rp: tree.querySelector("[data-inspector-rp]"),
            sl: tree.querySelector("[data-inspector-sl]"),
            column: tree.querySelector("[data-inspector-column]"),
            row: tree.querySelector("[data-inspector-row]"),
            acquisition: tree.querySelector("[data-inspector-acquisition]"),
            folder: tree.querySelector("[data-inspector-folder]")
        };

        const viewport = tree.querySelector("[data-tree-viewport]");
        const surface = tree.querySelector("[data-tree-surface]");
        const stage = tree.querySelector("[data-tree-stage]");
        const svg = tree.querySelector("[data-tree-connectors]");
        const paths = tree.querySelector("[data-tree-paths]");
        const zoomLabel = tree.querySelector("[data-tree-zoom-label]");
        let scale = 1;
        let panStart = null;
        let dragState = null;
        let selectedNode = null;

        const isEditing = () => canEdit && editToggle?.checked === true;
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
            if (!stage || !svg || !paths) return;
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
            if (!viewport || !surface || !stage) return;
            scale = clampScale(nextScale);
            stage.style.transform = `scale(${scale})`;
            surface.style.width = `${Math.max(viewport.clientWidth, stage.offsetWidth * scale)}px`;
            surface.style.height = `${Math.max(viewport.clientHeight, stage.offsetHeight * scale)}px`;
            if (zoomLabel) zoomLabel.textContent = `${Math.round(scale * 100)}%`;
            drawConnectors();
        };

        const layoutRank = rank => {
            if (!rank) return;
            const nodes = [...rank.querySelectorAll("[data-tree-node]")];
            if (nodes.length === 0) return;
            const rows = nodes.map(node => Number.parseInt(node.dataset.treeRow || "0", 10));
            const minimumRow = Math.min(...rows);
            const maximumRow = Math.max(...rows);
            rank.dataset.treeMinRow = minimumRow.toString();
            nodes.forEach(node => {
                const row = Number.parseInt(node.dataset.treeRow || "0", 10);
                node.style.setProperty("--tree-row", row - minimumRow);
            });
            rank.style.minHeight = `${((maximumRow - minimumRow + 1) * rowStep) + 36}px`;
        };

        const ensureStageBounds = (column) => {
            if (!stage) return;
            const requiredWidth = ((column + 1) * columnStep) + 220;
            if (stage.offsetWidth < requiredWidth) stage.style.width = `${requiredWidth}px`;
        };

        const setCoordinates = (column, row) => {
            if (!selectedNode || !inspector.column || !inspector.row) return;
            column = Math.max(0, Math.round(column));
            row = Math.max(0, Math.round(row));
            const rank = selectedNode.closest("[data-tree-rank]");
            selectedNode.dataset.treeColumn = column.toString();
            selectedNode.dataset.treeRow = row.toString();
            selectedNode.style.setProperty("--tree-column", column);
            selectedNode.style.setProperty("--tree-stack", 0);
            inspector.column.value = column.toString();
            inspector.row.value = row.toString();
            layoutRank(rank);
            ensureStageBounds(column);
            applyScale(scale);
        };

        const selectNode = node => {
            if (!isEditing() || !node) return;
            selectedNode?.classList.remove("is-selected");
            selectedNode = node;
            selectedNode.classList.add("is-selected");

            if (inspector.entryId) inspector.entryId.value = node.dataset.treeNode || "";
            if (inspector.removeEntryId) inspector.removeEntryId.value = node.dataset.treeNode || "";
            if (inspector.name) inspector.name.textContent = node.dataset.vehicleName || "Unknown vehicle";
            if (inspector.image) {
                const imageUrl = node.dataset.imageUrl || "";
                inspector.image.toggleAttribute("hidden", !imageUrl);
                if (imageUrl) inspector.image.src = imageUrl;
                else inspector.image.removeAttribute("src");
            }
            if (inspector.rank) inspector.rank.value = node.dataset.treeRankId || "";
            if (inspector.rp) inspector.rp.value = node.dataset.rpCost || "0";
            if (inspector.sl) inspector.sl.value = node.dataset.slCost || "0";
            if (inspector.column) inspector.column.value = node.dataset.treeColumn || "0";
            if (inspector.row) inspector.row.value = node.dataset.treeRow || "0";
            if (inspector.acquisition) inspector.acquisition.value = node.dataset.acquisitionType || "0";
            if (inspector.folder) {
                inspector.folder.querySelectorAll("option").forEach(option => {
                    option.disabled = option.value === node.dataset.treeNode;
                });
                inspector.folder.value = node.dataset.folderParentId || "";
            }
            selectionEmpty?.classList.add("d-none");
            inspectorForm?.classList.remove("d-none");
            removeForm?.classList.remove("d-none");
        };

        editToggle?.addEventListener("change", () => {
            tree.classList.toggle("is-editing", isEditing());
            if (help) help.textContent = isEditing()
                ? "Select a node, then drag it or use the inspector arrows"
                : "Drag empty space to pan · enable Edit Mode to select nodes";
            if (!isEditing()) {
                selectedNode?.classList.remove("is-selected");
                selectedNode = null;
                selectionEmpty?.classList.remove("d-none");
                inspectorForm?.classList.add("d-none");
                removeForm?.classList.add("d-none");
            }
            requestAnimationFrame(() => applyScale(scale));
        });

        tree.querySelectorAll("[data-tree-node]").forEach(node => {
            node.addEventListener("pointerdown", event => {
                if (!isEditing() || event.button !== 0) return;
                event.preventDefault();
                selectNode(node);
                dragState = {
                    node,
                    pointerId: event.pointerId,
                    startX: event.clientX,
                    startY: event.clientY,
                    column: Number.parseInt(node.dataset.treeColumn || "0", 10),
                    row: Number.parseInt(node.dataset.treeRow || "0", 10)
                };
                node.classList.add("is-dragging");
                node.setPointerCapture(event.pointerId);
            });
            node.addEventListener("pointermove", event => {
                if (!dragState || dragState.node !== node) return;
                const columnDelta = Math.round((event.clientX - dragState.startX) / (columnStep * scale));
                const rowDelta = Math.round((event.clientY - dragState.startY) / (rowStep * scale));
                setCoordinates(dragState.column + columnDelta, dragState.row + rowDelta);
            });
            const stopDragging = event => {
                if (!dragState || dragState.node !== node) return;
                dragState = null;
                node.classList.remove("is-dragging");
                if (node.hasPointerCapture(event.pointerId)) node.releasePointerCapture(event.pointerId);
            };
            node.addEventListener("pointerup", stopDragging);
            node.addEventListener("pointercancel", stopDragging);
            node.addEventListener("keydown", event => {
                if (!isEditing()) return;
                if (event.key === "Enter" || event.key === " ") {
                    event.preventDefault();
                    selectNode(node);
                }
            });
        });

        tree.querySelectorAll("[data-tree-nudge]").forEach(button => {
            button.addEventListener("click", () => {
                if (!selectedNode) return;
                let column = Number.parseInt(inspector.column?.value || "0", 10);
                let row = Number.parseInt(inspector.row?.value || "0", 10);
                if (button.dataset.treeNudge === "left") column--;
                if (button.dataset.treeNudge === "right") column++;
                if (button.dataset.treeNudge === "up") row--;
                if (button.dataset.treeNudge === "down") row++;
                setCoordinates(column, row);
            });
        });
        inspector.column?.addEventListener("input", () => setCoordinates(Number(inspector.column.value), Number(inspector.row?.value || 0)));
        inspector.row?.addEventListener("input", () => setCoordinates(Number(inspector.column?.value || 0), Number(inspector.row.value)));

        tree.querySelector("[data-tree-zoom-in]")?.addEventListener("click", () => applyScale(scale + .1));
        tree.querySelector("[data-tree-zoom-out]")?.addEventListener("click", () => applyScale(scale - .1));
        tree.querySelector("[data-tree-reset]")?.addEventListener("click", () => {
            applyScale(1);
            viewport?.scrollTo({ left: 0, top: 0, behavior: "smooth" });
        });
        tree.querySelector("[data-tree-fit]")?.addEventListener("click", () => {
            if (!viewport || !stage) return;
            const horizontal = (viewport.clientWidth - 24) / stage.offsetWidth;
            const vertical = (viewport.clientHeight - 24) / stage.offsetHeight;
            applyScale(Math.min(horizontal, vertical, 1));
            viewport.scrollTo({ left: 0, top: 0, behavior: "smooth" });
        });

        viewport?.addEventListener("wheel", event => {
            if (!event.ctrlKey) return;
            event.preventDefault();
            applyScale(scale + (event.deltaY < 0 ? .08 : -.08));
        }, { passive: false });

        viewport?.addEventListener("pointerdown", event => {
            if (event.button !== 0 || event.target.closest(".visual-tree-node")) return;
            panStart = { x: event.clientX, y: event.clientY, left: viewport.scrollLeft, top: viewport.scrollTop };
            viewport.classList.add("is-panning");
            viewport.setPointerCapture(event.pointerId);
        });
        viewport?.addEventListener("pointermove", event => {
            if (!panStart) return;
            viewport.scrollLeft = panStart.left - (event.clientX - panStart.x);
            viewport.scrollTop = panStart.top - (event.clientY - panStart.y);
        });
        const stopPanning = event => {
            if (!panStart || !viewport) return;
            panStart = null;
            viewport.classList.remove("is-panning");
            if (viewport.hasPointerCapture(event.pointerId)) viewport.releasePointerCapture(event.pointerId);
        };
        viewport?.addEventListener("pointerup", stopPanning);
        viewport?.addEventListener("pointercancel", stopPanning);

        if (stage && typeof ResizeObserver !== "undefined") {
            new ResizeObserver(() => applyScale(scale)).observe(stage);
        }
        document.addEventListener("shown.bs.tab", drawConnectors);
        applyScale(1);
    });
})();
