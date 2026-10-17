(() => {
    const svgNamespace = "http://www.w3.org/2000/svg";
    const defaultColumnStep = 280;
    const defaultRowStep = 204;

    document.querySelectorAll("[data-visual-tree]").forEach(tree => {
        const canEdit = tree.dataset.canEdit === "true";
        const columnStep = Number.parseInt(tree.dataset.treeColumnStep || defaultColumnStep.toString(), 10);
        const rowStep = Number.parseInt(tree.dataset.treeRowStep || defaultRowStep.toString(), 10);
        const editToggle = tree.querySelector("[data-tree-edit-toggle]");
        const edgeToggle = tree.querySelector("[data-tree-edge-toggle]");
        const edgeModeIndicator = tree.querySelector("[data-tree-edge-mode-indicator]");
        const help = tree.querySelector("[data-tree-help]");
        const inspectorCard = tree.querySelector("[data-tree-inspector]");
        const inspectorForm = tree.querySelector("[data-tree-inspector-form]");
        const removeForm = tree.querySelector("[data-tree-remove-form]");
        const selectionEmpty = tree.querySelector("[data-tree-selection-empty]");
        const previousButton = tree.querySelector("[data-tree-previous]");
        const nextButton = tree.querySelector("[data-tree-next]");
        const inspector = {
            entryId: tree.querySelector("[data-inspector-entry-id]"),
            removeEntryId: tree.querySelector("[data-remove-entry-id]"),
            name: tree.querySelector("[data-inspector-name]"),
            vehicleId: tree.querySelector("[data-inspector-vehicle-id]"),
            image: tree.querySelector("[data-inspector-image]"),
            rank: tree.querySelector("[data-inspector-rank]"),
            rp: tree.querySelector("[data-inspector-rp]"),
            sl: tree.querySelector("[data-inspector-sl]"),
            column: tree.querySelector("[data-inspector-column]"),
            row: tree.querySelector("[data-inspector-row]"),
            coordinate: tree.querySelector("[data-inspector-coordinate]"),
            acquisition: tree.querySelector("[data-inspector-acquisition]"),
            folder: tree.querySelector("[data-inspector-folder]"),
            dirty: tree.querySelector("[data-inspector-dirty]")
        };
        const edgeUi = {
            panel: tree.querySelector("[data-tree-edge-panel]"),
            empty: tree.querySelector("[data-tree-edge-empty]"),
            pending: tree.querySelector("[data-tree-edge-pending]"),
            selected: tree.querySelector("[data-tree-edge-selected]"),
            createForm: tree.querySelector("[data-tree-edge-create-form]"),
            removeForm: tree.querySelector("[data-tree-edge-remove-form]"),
            dependentName: tree.querySelector("[data-tree-edge-dependent-name]"),
            prerequisiteName: tree.querySelector("[data-tree-edge-prerequisite-name]"),
            dependentInput: tree.querySelector("[data-tree-edge-dependent-input]"),
            prerequisiteInput: tree.querySelector("[data-tree-edge-prerequisite-input]"),
            selectedDependentName: tree.querySelector("[data-tree-selected-dependent-name]"),
            selectedPrerequisiteName: tree.querySelector("[data-tree-selected-prerequisite-name]"),
            removeDependent: tree.querySelector("[data-tree-edge-remove-dependent]"),
            removePrerequisite: tree.querySelector("[data-tree-edge-remove-prerequisite]"),
            selectedDependentInput: tree.querySelector("[data-tree-edge-selected-dependent-input]"),
            selectedPrerequisiteInput: tree.querySelector("[data-tree-edge-selected-prerequisite-input]")
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
        let edgeMode = false;
        let pendingDependent = null;
        let pendingPrerequisite = null;
        let selectedEdge = null;
        let originalEntry = null;
        let isDirty = false;
        let isSubmitting = false;

        const isEditing = () => canEdit && editToggle?.checked === true;
        const isEdgeMode = () => canEdit && edgeMode;
        const clampScale = value => Math.min(1.5, Math.max(.35, value));
        const nodeById = id => id ? tree.querySelector(`[data-tree-node="${id}"]`) : null;
        const nodeName = node => node?.dataset.vehicleName || "Unknown vehicle";
        const entryStateFromNode = node => ({
            treeRankId: node?.dataset.treeRankId || "",
            rpCost: node?.dataset.rpCost || "0",
            slCost: node?.dataset.slCost || "0",
            treeColumn: node?.dataset.treeColumn || "0",
            treeRow: node?.dataset.treeRow || "0",
            acquisitionType: node?.dataset.acquisitionType || "0",
            folderParentId: node?.dataset.folderParentId || ""
        });
        const entryStateFromForm = () => ({
            treeRankId: inspector.rank?.value || "",
            rpCost: inspector.rp?.value || "",
            slCost: inspector.sl?.value || "",
            treeColumn: inspector.column?.value || "",
            treeRow: inspector.row?.value || "",
            acquisitionType: inspector.acquisition?.value || "",
            folderParentId: inspector.folder?.value || ""
        });
        const stateMatches = (left, right) => left && right &&
            Object.keys(left).every(key => left[key] === right[key]);
        const setDirty = value => {
            isDirty = Boolean(value);
            inspector.dirty?.classList.toggle("d-none", !isDirty);
            inspectorCard?.classList.toggle("is-dirty", isDirty);
            if (inspectorForm) inspectorForm.dataset.dirty = isDirty.toString();
        };
        const updateDirty = () => setDirty(Boolean(selectedNode && originalEntry) && !stateMatches(entryStateFromForm(), originalEntry));
        const confirmDiscardChanges = () => !isDirty || window.confirm("Discard unsaved changes to this vehicle entry?");
        const formatCost = value => {
            const parsed = Number.parseInt(value || "0", 10);
            return Number.isFinite(parsed) ? parsed.toLocaleString() : value;
        };
        const acquisitionClasses = {
            "0": "bg-green-lt",
            "1": "bg-yellow-lt",
            "2": "bg-purple-lt",
            "3": "bg-orange-lt",
            "4": "bg-blue-lt",
            "5": "bg-secondary-lt"
        };

        const nodeGeometry = node => {
            const nodeRect = node.getBoundingClientRect();
            const stageRect = stage.getBoundingClientRect();
            return {
                left: (nodeRect.left - stageRect.left) / scale,
                top: (nodeRect.top - stageRect.top) / scale,
                width: nodeRect.width / scale,
                height: nodeRect.height / scale
            };
        };

        const connectorPath = (from, to, kind) => {
            const source = nodeGeometry(from);
            const target = nodeGeometry(to);
            const sourceCenter = { x: source.left + (source.width / 2), y: source.top + (source.height / 2) };
            const targetCenter = { x: target.left + (target.width / 2), y: target.top + (target.height / 2) };
            const dx = targetCenter.x - sourceCenter.x;
            const dy = targetCenter.y - sourceCenter.y;
            const gutter = 30;

            if (kind === "folder" || Math.abs(dy) < rowStep * .55) {
                const direction = dx >= 0 ? 1 : -1;
                const start = { x: direction > 0 ? source.left + source.width : source.left, y: sourceCenter.y };
                const end = { x: direction > 0 ? target.left : target.left + target.width, y: targetCenter.y };
                const exit = { x: start.x + (direction * gutter), y: start.y };
                const enter = { x: end.x - (direction * gutter), y: end.y };
                const middleX = exit.x + ((enter.x - exit.x) / 2);
                return `M ${start.x} ${start.y} L ${exit.x} ${exit.y} C ${middleX} ${exit.y}, ${middleX} ${enter.y}, ${enter.x} ${enter.y} L ${end.x} ${end.y}`;
            }

            const direction = dy >= 0 ? 1 : -1;
            const anchorShift = Math.max(-source.width * .28, Math.min(source.width * .28, dx * .12));
            const start = { x: sourceCenter.x + anchorShift, y: direction > 0 ? source.top + source.height : source.top };
            const end = { x: targetCenter.x - anchorShift, y: direction > 0 ? target.top : target.top + target.height };
            const exit = { x: start.x, y: start.y + (direction * gutter) };
            const enter = { x: end.x, y: end.y - (direction * gutter) };
            const middleY = exit.y + ((enter.y - exit.y) / 2);
            return `M ${start.x} ${start.y} L ${exit.x} ${exit.y} C ${exit.x} ${middleY}, ${enter.x} ${middleY}, ${enter.x} ${enter.y} L ${end.x} ${end.y}`;
        };

        const appendVisiblePath = (from, to, kind, extraClass = "") => {
            const path = document.createElementNS(svgNamespace, "path");
            path.setAttribute("d", connectorPath(from, to, kind));
            path.setAttribute("class", `visual-tree-edge visual-tree-edge-${kind} ${extraClass}`.trim());
            path.dataset.edgeFrom = from.dataset.treeNode || "";
            path.dataset.edgeTo = to.dataset.treeNode || "";
            paths.appendChild(path);
        };

        const appendEdgeHitTarget = (from, to) => {
            const hitTarget = document.createElementNS(svgNamespace, "path");
            hitTarget.setAttribute("d", connectorPath(from, to, "prerequisite"));
            hitTarget.setAttribute("class", "visual-tree-edge-hit");
            hitTarget.setAttribute("role", "button");
            hitTarget.setAttribute("tabindex", isEdgeMode() ? "0" : "-1");
            hitTarget.setAttribute("aria-label", `${nodeName(to)} requires ${nodeName(from)}`);
            hitTarget.dataset.treeEdgeHit = "";
            hitTarget.dataset.edgeFrom = from.dataset.treeNode || "";
            hitTarget.dataset.edgeTo = to.dataset.treeNode || "";
            paths.appendChild(hitTarget);
        };

        const drawConnectors = () => {
            if (!stage || !svg || !paths) return;
            const width = stage.offsetWidth;
            const height = stage.offsetHeight;
            svg.setAttribute("viewBox", `0 0 ${width} ${height}`);
            svg.setAttribute("width", width);
            svg.setAttribute("height", height);
            paths.replaceChildren();
            const renderedFolderTargets = new Set();

            tree.querySelectorAll("[data-tree-edge]").forEach(edgeData => {
                const from = nodeById(edgeData.dataset.fromNode);
                const to = nodeById(edgeData.dataset.toNode);
                if (!from || !to) return;

                const kind = edgeData.dataset.edgeKind;
                if (kind === "folder" && to === selectedNode &&
                    edgeData.dataset.fromNode !== selectedNode.dataset.folderParentId) return;
                const isSelected = kind === "prerequisite" &&
                    selectedEdge?.from === edgeData.dataset.fromNode &&
                    selectedEdge?.to === edgeData.dataset.toNode;
                appendVisiblePath(from, to, kind, isSelected ? "is-selected" : "");
                if (kind === "folder") renderedFolderTargets.add(edgeData.dataset.toNode);
                if (kind === "prerequisite" && canEdit) appendEdgeHitTarget(from, to);
            });

            if (selectedNode?.dataset.folderParentId &&
                !renderedFolderTargets.has(selectedNode.dataset.treeNode || "")) {
                const folderParent = nodeById(selectedNode.dataset.folderParentId);
                if (folderParent) appendVisiblePath(folderParent, selectedNode, "folder", "is-pending");
            }

            if (pendingDependent && pendingPrerequisite) {
                appendVisiblePath(pendingPrerequisite, pendingDependent, "prerequisite", "is-pending");
            }
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
            rank.style.minHeight = `${((maximumRow - minimumRow + 1) * rowStep) + 48}px`;
        };

        const ensureStageBounds = column => {
            if (!stage) return;
            const requiredWidth = ((column + 1) * columnStep) + 240;
            if (stage.offsetWidth < requiredWidth) stage.style.width = `${requiredWidth}px`;
        };

        const setCoordinates = (column, row) => {
            if (!selectedNode || !inspector.column || !inspector.row) return;
            if (!Number.isFinite(column) || !Number.isFinite(row)) return;
            column = Math.max(0, Math.round(column));
            row = Math.max(0, Math.round(row));
            const rank = selectedNode.closest("[data-tree-rank]");
            selectedNode.dataset.treeColumn = column.toString();
            selectedNode.dataset.treeRow = row.toString();
            selectedNode.style.setProperty("--tree-column", column);
            selectedNode.style.setProperty("--tree-stack", 0);
            inspector.column.value = column.toString();
            inspector.row.value = row.toString();
            const coordinateText = `Column ${column} · Row ${row}`;
            if (inspector.coordinate) inspector.coordinate.textContent = coordinateText;
            const nodeCoordinate = selectedNode.querySelector("[data-tree-coordinate]");
            if (nodeCoordinate) {
                nodeCoordinate.textContent = coordinateText;
                nodeCoordinate.setAttribute("aria-label", `Tree position: column ${column}, row ${row}`);
            }
            layoutRank(rank);
            ensureStageBounds(column);
            applyScale(scale);
            updateDirty();
        };

        const updateNavigation = () => {
            const rank = selectedNode?.closest("[data-tree-rank]");
            const nodes = rank ? [...rank.querySelectorAll("[data-tree-node]")]
                .sort((left, right) =>
                    Number.parseInt(left.dataset.treeColumn || "0", 10) - Number.parseInt(right.dataset.treeColumn || "0", 10) ||
                    Number.parseInt(left.dataset.treeRow || "0", 10) - Number.parseInt(right.dataset.treeRow || "0", 10) ||
                    nodeName(left).localeCompare(nodeName(right))) : [];
            const index = selectedNode ? nodes.indexOf(selectedNode) : -1;
            if (previousButton) {
                previousButton.disabled = index <= 0;
                previousButton.dataset.targetNode = index > 0 ? nodes[index - 1].dataset.treeNode || "" : "";
            }
            if (nextButton) {
                nextButton.disabled = index < 0 || index >= nodes.length - 1;
                nextButton.dataset.targetNode = index >= 0 && index < nodes.length - 1 ? nodes[index + 1].dataset.treeNode || "" : "";
            }
        };

        const focusInspector = () => {
            if (!selectedNode || !inspectorForm) return;
            inspectorCard?.scrollIntoView({ behavior: "smooth", block: "nearest" });
            requestAnimationFrame(() => inspector.rank?.focus({ preventScroll: true }));
        };

        const moveSelectedNodeToRank = rankId => {
            if (!selectedNode || !rankId) return;
            const oldRank = selectedNode.closest("[data-tree-rank]");
            const newRank = [...tree.querySelectorAll("[data-tree-rank]")]
                .find(rank => rank.dataset.treeRankId === rankId);
            if (!newRank || newRank === oldRank) return;
            newRank.querySelector(".visual-tree-rank-grid")?.appendChild(selectedNode);
            selectedNode.dataset.treeRankId = rankId;
            selectedNode.dataset.treeRankNumber = newRank.dataset.treeRank || "";
            layoutRank(oldRank);
            layoutRank(newRank);
        };

        const previewInspectorState = () => {
            if (!selectedNode) return;
            const state = entryStateFromForm();
            moveSelectedNodeToRank(state.treeRankId);
            selectedNode.dataset.rpCost = state.rpCost;
            selectedNode.dataset.slCost = state.slCost;
            selectedNode.dataset.acquisitionType = state.acquisitionType;
            selectedNode.dataset.folderParentId = state.folderParentId;

            const rpPreview = selectedNode.querySelector("[data-tree-rp-preview]");
            const slPreview = selectedNode.querySelector("[data-tree-sl-preview]");
            const acquisitionPreview = selectedNode.querySelector("[data-tree-acquisition-preview]");
            if (rpPreview) rpPreview.textContent = `${formatCost(state.rpCost)} RP`;
            if (slPreview) slPreview.textContent = `${formatCost(state.slCost)} SL`;
            if (acquisitionPreview) {
                Object.values(acquisitionClasses).forEach(className => acquisitionPreview.classList.remove(className));
                acquisitionPreview.classList.add(acquisitionClasses[state.acquisitionType] || "bg-secondary-lt");
                acquisitionPreview.textContent = inspector.acquisition?.selectedOptions[0]?.textContent || "Unknown";
            }
            selectedNode.classList.toggle("visual-tree-node-folder-child", Boolean(state.folderParentId));
            selectedNode.classList.toggle("visual-tree-node-folder-parent", selectedNode.dataset.isFolderParent === "true");

            const column = state.treeColumn === "" ? Number.NaN : Number(state.treeColumn);
            const row = state.treeRow === "" ? Number.NaN : Number(state.treeRow);
            if (Number.isFinite(column) && Number.isFinite(row)) setCoordinates(column, row);
            else updateDirty();
            updateNavigation();
            drawConnectors();
        };

        const setInspectorState = state => {
            if (inspector.rank) inspector.rank.value = state.treeRankId;
            if (inspector.rp) inspector.rp.value = state.rpCost;
            if (inspector.sl) inspector.sl.value = state.slCost;
            if (inspector.column) inspector.column.value = state.treeColumn;
            if (inspector.row) inspector.row.value = state.treeRow;
            if (inspector.acquisition) inspector.acquisition.value = state.acquisitionType;
            if (inspector.folder) inspector.folder.value = state.folderParentId;
        };

        const restoreSelectedPreview = () => {
            if (!selectedNode || !originalEntry) return;
            setInspectorState(originalEntry);
            previewInspectorState();
            setDirty(false);
        };

        const clearEntrySelection = () => {
            selectedNode?.classList.remove("is-selected");
            selectedNode = null;
            originalEntry = null;
            setDirty(false);
            selectionEmpty?.classList.remove("d-none");
            inspectorForm?.classList.add("d-none");
            removeForm?.classList.add("d-none");
            updateNavigation();
        };

        const selectNode = (node, shouldFocus = true) => {
            if (!isEditing() || !node) return false;
            if (node === selectedNode) {
                if (shouldFocus) focusInspector();
                return true;
            }
            if (!confirmDiscardChanges()) return false;
            restoreSelectedPreview();
            selectedNode?.classList.remove("is-selected");
            selectedNode = node;
            selectedNode.classList.add("is-selected");
            originalEntry = entryStateFromNode(node);

            if (inspector.entryId) inspector.entryId.value = node.dataset.treeNode || "";
            if (inspector.removeEntryId) inspector.removeEntryId.value = node.dataset.treeNode || "";
            if (inspector.name) inspector.name.textContent = nodeName(node);
            if (inspector.vehicleId) inspector.vehicleId.textContent = node.dataset.vehicleId || "";
            if (inspector.image) {
                const imageUrl = node.dataset.imageUrl || "";
                inspector.image.toggleAttribute("hidden", !imageUrl);
                if (imageUrl) inspector.image.src = imageUrl;
                else inspector.image.removeAttribute("src");
            }
            setInspectorState(originalEntry);
            if (inspector.coordinate) inspector.coordinate.textContent = `Column ${node.dataset.treeColumn || "0"} · Row ${node.dataset.treeRow || "0"}`;
            if (inspector.folder) {
                inspector.folder.querySelectorAll("option").forEach(option => {
                    option.disabled = option.value === node.dataset.treeNode;
                });
            }
            selectionEmpty?.classList.add("d-none");
            inspectorForm?.classList.remove("d-none");
            removeForm?.classList.remove("d-none");
            setDirty(false);
            updateNavigation();
            if (shouldFocus) focusInspector();
            return true;
        };

        const updateEdgePanel = () => {
            edgeUi.empty?.classList.toggle("d-none", Boolean(pendingDependent || selectedEdge));
            edgeUi.pending?.classList.toggle("d-none", !pendingDependent || Boolean(selectedEdge));
            edgeUi.createForm?.classList.toggle("d-none", !pendingDependent || !pendingPrerequisite || Boolean(selectedEdge));
            edgeUi.selected?.classList.toggle("d-none", !selectedEdge);
            edgeUi.removeForm?.classList.toggle("d-none", !selectedEdge);

            if (pendingDependent) {
                if (edgeUi.dependentName) edgeUi.dependentName.textContent = nodeName(pendingDependent);
                if (edgeUi.prerequisiteName) edgeUi.prerequisiteName.textContent = pendingPrerequisite
                    ? nodeName(pendingPrerequisite)
                    : "select a prerequisite";
                if (edgeUi.dependentInput) edgeUi.dependentInput.value = pendingDependent.dataset.treeNode || "";
                if (edgeUi.prerequisiteInput) edgeUi.prerequisiteInput.value = pendingPrerequisite?.dataset.treeNode || "";
            }

            if (selectedEdge) {
                const prerequisite = nodeById(selectedEdge.from);
                const dependent = nodeById(selectedEdge.to);
                if (edgeUi.selectedDependentName) edgeUi.selectedDependentName.textContent = nodeName(dependent);
                if (edgeUi.selectedPrerequisiteName) edgeUi.selectedPrerequisiteName.textContent = nodeName(prerequisite);
                if (edgeUi.removeDependent) edgeUi.removeDependent.value = selectedEdge.to;
                if (edgeUi.removePrerequisite) edgeUi.removePrerequisite.value = selectedEdge.from;
                if (edgeUi.selectedDependentInput) edgeUi.selectedDependentInput.value = selectedEdge.to;
                if (edgeUi.selectedPrerequisiteInput) edgeUi.selectedPrerequisiteInput.value = selectedEdge.from;
            }
        };

        const clearEdgeState = (redraw = true) => {
            pendingDependent?.classList.remove("is-edge-dependent");
            pendingPrerequisite?.classList.remove("is-edge-prerequisite");
            pendingDependent = null;
            pendingPrerequisite = null;
            selectedEdge = null;
            updateEdgePanel();
            if (redraw) drawConnectors();
        };

        const activateEdgeMode = (resetState = true) => {
            if (!canEdit) return;
            edgeMode = true;
            if (editToggle) editToggle.checked = false;
            tree.classList.remove("is-editing");
            tree.classList.add("is-edge-creating");
            clearEntrySelection();
            if (resetState) clearEdgeState(false);
            edgeUi.panel?.classList.remove("d-none");
            edgeToggle?.classList.add("active");
            edgeToggle?.setAttribute("aria-pressed", "true");
            edgeModeIndicator?.classList.remove("d-none");
            if (help) help.textContent = "Select the dependent, then its prerequisite · select a blue edge to remove it";
            updateEdgePanel();
            requestAnimationFrame(() => applyScale(scale));
        };

        const deactivateEdgeMode = () => {
            edgeMode = false;
            tree.classList.remove("is-edge-creating");
            edgeUi.panel?.classList.add("d-none");
            edgeToggle?.classList.remove("active");
            edgeToggle?.setAttribute("aria-pressed", "false");
            edgeModeIndicator?.classList.add("d-none");
            clearEdgeState(false);
            if (help) help.textContent = "Drag empty space to pan · enable Move entries to reposition nodes";
            requestAnimationFrame(() => applyScale(scale));
        };

        const selectNodeForEdge = node => {
            if (!isEdgeMode() || !node) return;
            if (selectedEdge || (pendingDependent && pendingPrerequisite)) clearEdgeState(false);

            if (!pendingDependent) {
                pendingDependent = node;
                pendingDependent.classList.add("is-edge-dependent");
            } else {
                pendingPrerequisite = node;
                pendingPrerequisite.classList.add("is-edge-prerequisite");
            }
            updateEdgePanel();
            drawConnectors();
        };

        const selectExistingEdge = (from, to) => {
            if (!isEdgeMode()) return;
            clearEdgeState(false);
            selectedEdge = { from, to };
            updateEdgePanel();
            drawConnectors();
        };

        editToggle?.addEventListener("change", () => {
            if (!editToggle.checked && isDirty && !confirmDiscardChanges()) {
                editToggle.checked = true;
                return;
            }
            if (!editToggle.checked) restoreSelectedPreview();
            if (isEditing()) deactivateEdgeMode();
            tree.classList.toggle("is-editing", isEditing());
            if (help) help.textContent = isEditing()
                ? "Select a node, then drag it or use the arrow keys or inspector buttons"
                : "Drag empty space to pan · enable Move entries to reposition nodes";
            if (!isEditing()) clearEntrySelection();
            requestAnimationFrame(() => applyScale(scale));
        });

        edgeToggle?.addEventListener("click", () => {
            if (isEdgeMode()) deactivateEdgeMode();
            else {
                if (!confirmDiscardChanges()) return;
                restoreSelectedPreview();
                activateEdgeMode();
            }
        });

        tree.querySelectorAll("[data-tree-node]").forEach(node => {
            node.addEventListener("pointerdown", event => {
                if (!isEditing() || event.button !== 0 || selectedNode !== node) return;
                event.preventDefault();
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
            node.addEventListener("click", event => {
                if (isEdgeMode()) {
                    event.preventDefault();
                    selectNodeForEdge(node);
                } else if (isEditing()) {
                    event.preventDefault();
                    selectNode(node, true);
                }
            });
            node.addEventListener("dblclick", event => {
                if (!isEditing()) return;
                event.preventDefault();
                selectNode(node, true);
            });
            node.addEventListener("keydown", event => {
                if (event.key !== "Enter" && event.key !== " ") return;
                if (isEdgeMode()) {
                    event.preventDefault();
                    selectNodeForEdge(node);
                } else if (isEditing()) {
                    event.preventDefault();
                    selectNode(node);
                }
            });
        });

        paths?.addEventListener("pointerdown", event => {
            if (isEdgeMode() && event.target.closest("[data-tree-edge-hit]")) event.stopPropagation();
        });
        paths?.addEventListener("click", event => {
            const hitTarget = event.target.closest("[data-tree-edge-hit]");
            if (!isEdgeMode() || !hitTarget) return;
            event.preventDefault();
            event.stopPropagation();
            selectExistingEdge(hitTarget.dataset.edgeFrom, hitTarget.dataset.edgeTo);
        });
        paths?.addEventListener("keydown", event => {
            const hitTarget = event.target.closest("[data-tree-edge-hit]");
            if (!isEdgeMode() || !hitTarget || (event.key !== "Enter" && event.key !== " ")) return;
            event.preventDefault();
            selectExistingEdge(hitTarget.dataset.edgeFrom, hitTarget.dataset.edgeTo);
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
        inspectorForm?.querySelectorAll("[data-dirty-field]").forEach(field => {
            field.addEventListener("input", previewInspectorState);
            field.addEventListener("change", previewInspectorState);
        });
        inspectorForm?.addEventListener("submit", () => {
            isSubmitting = true;
            setDirty(false);
        });
        removeForm?.addEventListener("submit", () => { isSubmitting = true; });

        const navigateToInspectorNode = button => {
            const target = nodeById(button?.dataset.targetNode);
            if (target) selectNode(target, true);
        };
        previousButton?.addEventListener("click", () => navigateToInspectorNode(previousButton));
        nextButton?.addEventListener("click", () => navigateToInspectorNode(nextButton));

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
            if (event.button !== 0 || event.target.closest(".visual-tree-node, [data-tree-edge-hit]")) return;
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

        document.addEventListener("keydown", event => {
            if (event.key === "Escape" && isEdgeMode()) {
                deactivateEdgeMode();
                return;
            }
            if (event.key === "Escape" && isEditing() && selectedNode) {
                if (!confirmDiscardChanges()) return;
                restoreSelectedPreview();
                clearEntrySelection();
                return;
            }
            if (!isEditing() || !selectedNode || event.altKey || event.ctrlKey || event.metaKey || !["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown"].includes(event.key)) return;
            const target = event.target;
            if (target instanceof HTMLElement && (target.isContentEditable || target.matches("input, select, textarea, button"))) return;
            event.preventDefault();
            let column = Number.parseInt(selectedNode.dataset.treeColumn || "0", 10);
            let row = Number.parseInt(selectedNode.dataset.treeRow || "0", 10);
            if (event.key === "ArrowLeft") column--;
            if (event.key === "ArrowRight") column++;
            if (event.key === "ArrowUp") row--;
            if (event.key === "ArrowDown") row++;
            setCoordinates(column, row);
        });

        window.addEventListener("beforeunload", event => {
            if (!isDirty || isSubmitting) return;
            event.preventDefault();
            event.returnValue = "";
        });

        if (stage && typeof ResizeObserver !== "undefined") {
            new ResizeObserver(() => applyScale(scale)).observe(stage);
        }
        document.addEventListener("shown.bs.tab", drawConnectors);
        applyScale(1);

        if (tree.dataset.startEdgeMode === "true") {
            activateEdgeMode(false);
            const dependent = nodeById(tree.dataset.pendingDependent);
            const prerequisite = nodeById(tree.dataset.pendingPrerequisite);
            if (tree.dataset.startSelectedEdge === "true" && dependent && prerequisite) {
                selectExistingEdge(prerequisite.dataset.treeNode, dependent.dataset.treeNode);
            } else {
                pendingDependent = dependent;
                pendingPrerequisite = prerequisite;
                pendingDependent?.classList.add("is-edge-dependent");
                pendingPrerequisite?.classList.add("is-edge-prerequisite");
                updateEdgePanel();
                drawConnectors();
            }
        }
    });
})();
