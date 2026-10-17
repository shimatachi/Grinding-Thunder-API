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
        const focusToggle = tree.querySelector("[data-tree-focus-toggle]");
        const help = tree.querySelector("[data-tree-help]");
        const inspectorCard = tree.querySelector("[data-tree-inspector]");
        const inspectorForm = tree.querySelector("[data-tree-inspector-form]");
        const inspectorSave = inspectorForm?.querySelector("button[type='submit']");
        const removeForm = tree.querySelector("[data-tree-remove-form]");
        const selectionEmpty = tree.querySelector("[data-tree-selection-empty]");
        const previousButton = tree.querySelector("[data-tree-previous]");
        const nextButton = tree.querySelector("[data-tree-next]");
        const undoButton = tree.querySelector("[data-tree-undo]");
        const redoButton = tree.querySelector("[data-tree-redo]");
        const snapToggle = tree.querySelector("[data-tree-grid-snap]");
        const selectionCount = tree.querySelector("[data-tree-selection-count]");
        const layoutForm = tree.querySelector("[data-tree-layout-form]");
        const layoutFields = tree.querySelector("[data-tree-layout-fields]");
        const layoutSave = tree.querySelector("[data-tree-layout-save]");
        const layoutDiscard = tree.querySelector("[data-tree-layout-discard]");
        const layoutStatus = tree.querySelector("[data-tree-layout-status]");
        const collisionWarning = tree.querySelector("[data-tree-collision-warning]");
        const collisionList = tree.querySelector("[data-tree-collision-list]");
        const undoStack = [];
        const redoStack = [];
        const maxHistorySize = 50;
        const readonlyPanel = tree.querySelector("[data-tree-readonly-inspector]");
        const readonlyEmpty = tree.querySelector("[data-tree-readonly-empty]");
        const readonlyDetails = tree.querySelector("[data-tree-readonly-details]");
        const readonlyInspector = {
            name: tree.querySelector("[data-readonly-name]"),
            vehicleId: tree.querySelector("[data-readonly-vehicle-id]"),
            image: tree.querySelector("[data-readonly-image]"),
            wikiContainer: tree.querySelector("[data-readonly-wiki-container]"),
            wikiLink: tree.querySelector("[data-readonly-wiki-link]"),
            rank: tree.querySelector("[data-readonly-rank]"),
            rp: tree.querySelector("[data-readonly-rp]"),
            sl: tree.querySelector("[data-readonly-sl]"),
            acquisition: tree.querySelector("[data-readonly-acquisition]"),
            coordinate: tree.querySelector("[data-readonly-coordinate]"),
            folder: tree.querySelector("[data-readonly-folder]"),
            progressionBadge: tree.querySelector("[data-readonly-progression-badge]"),
            toggleOwnedBtn: tree.querySelector("[data-readonly-toggle-owned]"),
            setTargetBtn: tree.querySelector("[data-readonly-set-target]")
        };
        const activeRouteEdges = new Set();
        let targetVehicleId = null;
        let calculationResult = null;
        const allUnlockedVehicleIds = new Set();
        const unlockedVehicleIds = new Set();
        const treeVehicleIds = new Set();
        let versionId = null;
        const playerState = { isAuthenticated: false, username: null, preferences: null };

        const hud = document.querySelector("[data-progression-hud]");
        const hudTargetName = hud?.querySelector("[data-hud-target-name]");
        const hudTotalRp = hud?.querySelector("[data-hud-total-rp]");
        const hudTotalSl = hud?.querySelector("[data-hud-total-sl]");
        const hudMatches = hud?.querySelector("[data-hud-matches]");
        const hudOptMode = hud?.querySelector("[data-hud-opt-mode]");
        const hudRpInput = hud?.querySelector("[data-hud-rp-match]");
        const hudSlInput = hud?.querySelector("[data-hud-sl-match]");
        const hudClearTarget = hud?.querySelector("[data-hud-clear-target]");
        const hudDeficitsContainer = hud?.querySelector("[data-hud-deficits-container]");
        const hudDeficitsList = hud?.querySelector("[data-hud-deficits-list]");

        const playerWidget = document.querySelector("[data-player-widget]");
        const playerModalEl = document.querySelector("[data-player-modal]");
        const loginForm = document.querySelector("[data-player-login-form]");
        const registerForm = document.querySelector("[data-player-register-form]");
        const loginError = document.querySelector("[data-player-login-error]");
        const registerError = document.querySelector("[data-player-register-error]");
        const logoutBtn = document.querySelector("[data-player-logout-btn]");
        const inspector = {
            entryId: tree.querySelector("[data-inspector-entry-id]"),
            removeEntryId: tree.querySelector("[data-remove-entry-id]"),
            name: tree.querySelector("[data-inspector-name]"),
            vehicleId: tree.querySelector("[data-inspector-vehicle-id]"),
            image: tree.querySelector("[data-inspector-image]"),
            wikiContainer: tree.querySelector("[data-inspector-wiki-container]"),
            wikiLink: tree.querySelector("[data-inspector-wiki-link]"),
            rank: tree.querySelector("[data-inspector-rank]"),
            rp: tree.querySelector("[data-inspector-rp]"),
            sl: tree.querySelector("[data-inspector-sl]"),
            column: tree.querySelector("[data-inspector-column]"),
            row: tree.querySelector("[data-inspector-row]"),
            coordinate: tree.querySelector("[data-inspector-coordinate]"),
            acquisition: tree.querySelector("[data-inspector-acquisition]"),
            folder: tree.querySelector("[data-inspector-folder]"),
            dirty: tree.querySelector("[data-inspector-dirty]"),
            status: tree.querySelector("[data-inspector-save-status]")
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
            selectedPrerequisiteInput: tree.querySelector("[data-tree-edge-selected-prerequisite-input]"),
            status: tree.querySelector("[data-tree-edge-status]")
        };
        // Keep inline confirmation for the no-JavaScript fallback, but let the
        // asynchronous handlers own confirmation once this state machine loads.
        removeForm?.removeAttribute("onsubmit");
        edgeUi.removeForm?.removeAttribute("onsubmit");

        const viewport = tree.querySelector("[data-tree-viewport]");
        const surface = tree.querySelector("[data-tree-surface]");
        const stage = tree.querySelector("[data-tree-stage]");
        const svg = tree.querySelector("[data-tree-connectors]");
        const paths = tree.querySelector("[data-tree-paths]");
        const zoomLabel = tree.querySelector("[data-tree-zoom-label]");
        let scale = 1;
        let panStart = null;
        let dragState = null;
        let suppressNodeClick = false;
        let selectedNode = null;
        const selectedNodes = new Set();
        const layoutOriginalPositions = new Map();
        let edgeMode = false;
        let pendingDependent = null;
        let pendingPrerequisite = null;
        let selectedEdge = null;
        let originalEntry = null;
        let isDirty = false;
        let isLayoutDirty = false;
        let isSubmitting = false;
        let inspectorStatus = "none";

        const isEditing = () => canEdit && editToggle?.checked === true;
        const isEdgeMode = () => canEdit && edgeMode;
        const clampScale = value => Math.min(1.5, Math.max(.35, value));
        const nodeById = id => id ? tree.querySelector(`[data-tree-node="${id}"]`) : null;
        const nodeName = node => node?.dataset.vehicleName || "Unknown vehicle";
        const entryStateFromNode = node => ({
            treeRankId: node?.dataset.treeRankId || "",
            rpCost: node?.dataset.rpCost || "0",
            slCost: node?.dataset.slCost || "0",
            treeColumn: (Number.parseInt(node?.dataset.treeColumn || "0", 10) + 1).toString(),
            treeRow: (Number.parseInt(node?.dataset.treeRow || "0", 10) + 1).toString(),
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
        const setInspectorStatus = (status, message = "") => {
            inspectorStatus = status;
            if (!inspector.status) return;
            inspector.status.classList.remove("alert-warning", "alert-info", "alert-success", "alert-danger");
            const states = {
                unsaved: ["alert-warning", "Unsaved"],
                saving: ["alert-info", "Saving\u2026"],
                saved: ["alert-success", "Saved"],
                failed: ["alert-danger", `Save failed \u2014 ${message || "Unknown error"}`]
            };
            const state = states[status];
            inspector.status.classList.toggle("d-none", !state);
            inspector.status.textContent = state?.[1] || "";
        };
        const setDirty = value => {
            isDirty = Boolean(value);
            inspectorCard?.classList.toggle("is-dirty", isDirty);
            if (inspectorForm) inspectorForm.dataset.dirty = isDirty.toString();
            if (isDirty) setInspectorStatus("unsaved");
            else if (inspectorStatus === "unsaved") setInspectorStatus("none");
        };
        const updateDirty = () => setDirty(Boolean(selectedNode && originalEntry) && !stateMatches(entryStateFromForm(), originalEntry));
        const confirmDiscardChanges = () => (!isDirty && !isLayoutDirty) ||
            window.confirm(isLayoutDirty
                ? "Discard unsaved layout changes to the selected entries?"
                : "Discard unsaved changes to this vehicle entry?");
        const formatCost = value => {
            const parsed = Number.parseInt(value || "0", 10);
            return Number.isFinite(parsed) ? parsed.toLocaleString() : value;
        };
        const acquisitionClasses = {
            "0": "visual-tree-acquisition-researchable",
            "1": "visual-tree-acquisition-premium",
            "2": "visual-tree-acquisition-squadron",
            "3": "visual-tree-acquisition-event",
            "4": "visual-tree-acquisition-pack",
            "5": "visual-tree-acquisition-hidden"
        };
        const acquisitionNodeClasses = Object.values(acquisitionClasses)
            .map(className => className.replace("visual-tree-acquisition-", "visual-tree-node-acquisition-"));
        const acquisitionLabels = {
            "0": "Researchable",
            "1": "Premium",
            "2": "Squadron",
            "3": "Event",
            "4": "Pack",
            "5": "Hidden"
        };

        const jsonAction = (form, handler) => {
            const url = new URL(form.action, window.location.href);
            url.searchParams.set("handler", handler);
            return url;
        };

        const postFormJson = async (form, handler) => {
            const response = await fetch(jsonAction(form, handler), {
                method: "POST",
                body: new FormData(form),
                credentials: "same-origin",
                headers: {
                    "Accept": "application/json",
                    "X-Requested-With": "XMLHttpRequest"
                }
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || payload?.success !== true) {
                const fallback = response.status === 403
                    ? "Your session does not have permission to edit this draft."
                    : `Request failed (HTTP ${response.status}).`;
                throw new Error(payload?.error || fallback);
            }
            return payload;
        };

        const setTransientStatus = (element, message, kind = "info") => {
            if (!element) return;
            element.classList.remove("d-none", "text-secondary", "text-success", "text-danger", "alert-info", "alert-success", "alert-danger");
            if (element.classList.contains("alert")) element.classList.add(`alert-${kind}`);
            else element.classList.add(kind === "success" ? "text-success" : kind === "danger" ? "text-danger" : "text-secondary");
            element.textContent = message;
        };

        let activeStageRect = null;
        let activeGeometryCache = null;

        const nodeGeometry = node => {
            if (activeGeometryCache) {
                const cached = activeGeometryCache.get(node);
                if (cached) return cached;
            }
            const nodeRect = node.getBoundingClientRect();
            const stageRect = activeStageRect || stage.getBoundingClientRect();
            const geom = {
                left: (nodeRect.left - stageRect.left) / scale,
                top: (nodeRect.top - stageRect.top) / scale,
                width: nodeRect.width / scale,
                height: nodeRect.height / scale
            };
            if (activeGeometryCache) {
                activeGeometryCache.set(node, geom);
            }
            return geom;
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
            activeStageRect = stage.getBoundingClientRect();
            activeGeometryCache = new Map();
            try {
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
                    const isRoute = kind === "prerequisite" &&
                        activeRouteEdges.has(`${edgeData.dataset.fromNode}->${edgeData.dataset.toNode}`);
                    const fromRank = Number.parseInt(from.dataset.treeRankNumber || "0", 10);
                    const toRank = Number.parseInt(to.dataset.treeRankNumber || "0", 10);
                    const isRankInversion = kind === "prerequisite" && fromRank > toRank && toRank > 0;
                    appendVisiblePath(from, to, kind, `${isSelected ? "is-selected" : ""} ${isRoute ? "is-progression-route" : ""} ${isRankInversion ? "is-validation-warning" : ""}`.trim());
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
            } finally {
                activeStageRect = null;
                activeGeometryCache = null;
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
            rank.style.minHeight = `${((maximumRow - minimumRow + 1) * rowStep) + 96}px`;
        };

        const ensureStageBounds = column => {
            if (!stage) return;
            const requiredWidth = ((column + 1) * columnStep) + 240;
            if (stage.offsetWidth < requiredWidth) stage.style.width = `${requiredWidth}px`;
        };

        const selectedNodeList = () => [...selectedNodes];
        const nodePosition = node => ({
            column: Number.parseInt(node.dataset.treeColumn || "0", 10),
            row: Number.parseInt(node.dataset.treeRow || "0", 10)
        });
        const cellKey = node => `${node.dataset.treeRankId}:${node.dataset.treeColumn}:${node.dataset.treeRow}`;

        const setNodeCoordinates = (node, column, row) => {
            column = Math.max(0, Math.round(column));
            row = Math.max(0, Math.round(row));
            node.dataset.treeColumn = column.toString();
            node.dataset.treeRow = row.toString();
            node.style.setProperty("--tree-column", column);
            node.style.setProperty("--tree-stack", 0);
            const nodeCoordinate = node.querySelector("[data-tree-coordinate]");
            if (nodeCoordinate) {
                const displayCol = column + 1;
                const displayRow = row + 1;
                nodeCoordinate.textContent = `Column ${displayCol} · Row ${displayRow}`;
                nodeCoordinate.setAttribute("aria-label", `Tree position: column ${displayCol}, row ${displayRow}`);
            }
            ensureStageBounds(column);
        };

        const refreshOccupiedCells = () => {
            tree.querySelectorAll(".visual-tree-cell").forEach(cell => cell.remove());
            tree.querySelectorAll("[data-tree-rank]").forEach(rank => {
                const grid = rank.querySelector(".visual-tree-rank-grid");
                if (!grid) return;
                const minimumRow = Number.parseInt(rank.dataset.treeMinRow || "0", 10);
                const groups = new Map();
                rank.querySelectorAll("[data-tree-node]").forEach(node => {
                    const key = `${node.dataset.treeColumn}:${node.dataset.treeRow}`;
                    if (!groups.has(key)) groups.set(key, []);
                    groups.get(key).push(node);
                });
                groups.forEach(nodes => {
                    const marker = document.createElement("div");
                    marker.className = "visual-tree-cell";
                    const col = Number.parseInt(nodes[0].dataset.treeColumn || "0", 10);
                    const row = Number.parseInt(nodes[0].dataset.treeRow || "0", 10);
                    marker.style.setProperty("--tree-column", col);
                    marker.style.setProperty("--tree-row", Math.max(0, row - minimumRow));
                    marker.classList.toggle("is-folder-cell", nodes.some(node => Boolean(node.dataset.folderParentId)));
                    marker.classList.toggle("is-collision", nodes.filter(node => !node.dataset.folderParentId).length > 1);
                    marker.setAttribute("aria-hidden", "true");
                    marker.title = `Occupied: Col ${col + 1}, Row ${row + 1}`;

                    const cellLabel = document.createElement("span");
                    cellLabel.className = "visual-tree-cell-label";
                    cellLabel.textContent = `${col + 1},${row + 1}`;
                    marker.appendChild(cellLabel);

                    grid.appendChild(marker);
                });
            });
        };

        const refreshGridLabels = () => {
            tree.querySelectorAll(".visual-tree-grid-labels").forEach(el => el.remove());
            if (!isEditing()) return;
            tree.querySelectorAll("[data-tree-rank]").forEach(rank => {
                const grid = rank.querySelector(".visual-tree-rank-grid");
                if (!grid) return;
                const container = document.createElement("div");
                container.className = "visual-tree-grid-labels";
                container.setAttribute("aria-hidden", "true");
                const nodes = rank.querySelectorAll("[data-tree-node]");
                let maxCol = 3;
                nodes.forEach(n => {
                    const col = Number.parseInt(n.dataset.treeColumn || "0", 10);
                    if (col > maxCol) maxCol = col;
                });
                for (let c = 0; c <= maxCol + 1; c++) {
                    const colLabel = document.createElement("span");
                    colLabel.className = "visual-tree-grid-col-label";
                    colLabel.style.left = `${c * columnStep}px`;
                    colLabel.textContent = `Col ${c + 1}`;
                    container.appendChild(colLabel);
                }
                grid.appendChild(container);
            });
        };

        const refreshCollisionState = () => {
            tree.querySelectorAll("[data-tree-node]").forEach(node => {
                node.classList.remove("is-coordinate-collision", "is-out-of-bounds");
            });
            const rootGroups = new Map();
            const outOfBoundsNodes = new Set();
            tree.querySelectorAll("[data-tree-node]").forEach(node => {
                const col = Number.parseInt(node.dataset.treeColumn || "0", 10);
                const row = Number.parseInt(node.dataset.treeRow || "0", 10);
                if (col < 0 || row < 0 || Number.isNaN(col) || Number.isNaN(row)) {
                    node.classList.add("is-out-of-bounds");
                    outOfBoundsNodes.add(node);
                }
                if (node.dataset.folderParentId) return;
                const key = cellKey(node);
                if (!rootGroups.has(key)) rootGroups.set(key, []);
                rootGroups.get(key).push(node);
            });
            const collidingNodes = new Set();
            rootGroups.forEach(nodes => {
                if (nodes.length < 2) return;
                nodes.forEach(node => {
                    node.classList.add("is-coordinate-collision");
                    collidingNodes.add(node);
                });
            });
            const hasIssue = collidingNodes.size > 0 || outOfBoundsNodes.size > 0;
            const selectedCollision = selectedNodeList().some(node => collidingNodes.has(node) || outOfBoundsNodes.has(node));
            collisionWarning?.classList.toggle("d-none", !hasIssue);
            if (collisionList) {
                collisionList.replaceChildren();
                collidingNodes.forEach(node => {
                    const btn = document.createElement("button");
                    btn.type = "button";
                    btn.className = "btn btn-xs btn-outline-danger py-0 px-2";
                    btn.dataset.jumpToNode = node.dataset.treeNode || "";
                    const col = Number.parseInt(node.dataset.treeColumn || "0", 10) + 1;
                    const row = Number.parseInt(node.dataset.treeRow || "0", 10) + 1;
                    btn.textContent = `${nodeName(node)} (Overlap: Col ${col}, Row ${row})`;
                    btn.title = `Focus ${nodeName(node)} on canvas`;
                    collisionList.appendChild(btn);
                });
                outOfBoundsNodes.forEach(node => {
                    const btn = document.createElement("button");
                    btn.type = "button";
                    btn.className = "btn btn-xs btn-outline-danger py-0 px-2";
                    btn.dataset.jumpToNode = node.dataset.treeNode || "";
                    const col = Number.parseInt(node.dataset.treeColumn || "0", 10) + 1;
                    const row = Number.parseInt(node.dataset.treeRow || "0", 10) + 1;
                    btn.textContent = `${nodeName(node)} (Out-of-bounds: Col ${col}, Row ${row})`;
                    btn.title = `Focus ${nodeName(node)} on canvas`;
                    collisionList.appendChild(btn);
                });
            }
            return selectedCollision;
        };

        const refreshFolderFeedback = () => {
            tree.querySelectorAll(".is-folder-related").forEach(node => node.classList.remove("is-folder-related"));
            selectedNodeList().forEach(node => {
                if (node.dataset.folderParentId) nodeById(node.dataset.folderParentId)?.classList.add("is-folder-related");
                tree.querySelectorAll(`[data-folder-parent-id="${node.dataset.treeNode}"]`)
                    .forEach(child => child.classList.add("is-folder-related"));
            });
        };

        const rebuildLayoutFields = () => {
            if (!layoutFields) return;
            layoutFields.replaceChildren();
            selectedNodeList().forEach((node, index) => {
                const col = Number.parseInt(node.dataset.treeColumn || "0", 10) + 1;
                const row = Number.parseInt(node.dataset.treeRow || "0", 10) + 1;
                const values = {
                    EntryId: node.dataset.treeNode || "",
                    TreeColumn: col.toString(),
                    TreeRow: row.toString()
                };
                Object.entries(values).forEach(([name, value]) => {
                    const input = document.createElement("input");
                    input.type = "hidden";
                    input.name = `LayoutPositions[${index}].${name}`;
                    input.value = value;
                    layoutFields.appendChild(input);
                });
            });
        };

        const refreshLayoutTools = () => {
            const count = selectedNodes.size;
            if (selectionCount) selectionCount.textContent = `${count} selected`;
            tree.querySelectorAll("[data-tree-layout-nudge]").forEach(button => { button.disabled = count < 2; });
            tree.querySelectorAll("[data-tree-layout-action]").forEach(button => {
                const required = button.dataset.treeLayoutAction === "distribute-vertical" ? 3 : 2;
                button.disabled = count < required;
            });
            const selectedCollision = refreshCollisionState();
            if (layoutSave) layoutSave.disabled = !isLayoutDirty || count < 2 || selectedCollision;
            if (inspectorSave) inspectorSave.disabled = count === 1 && selectedCollision;
            if (layoutDiscard) layoutDiscard.disabled = !isLayoutDirty;
            rebuildLayoutFields();
            refreshFolderFeedback();
        };

        const refreshLayoutPresentation = () => {
            tree.querySelectorAll("[data-tree-rank]").forEach(layoutRank);
            refreshOccupiedCells();
            refreshGridLabels();
            refreshLayoutTools();
            applyScale(scale);
        };

        const rememberLayoutPositions = () => {
            layoutOriginalPositions.clear();
            selectedNodeList().forEach(node => layoutOriginalPositions.set(node, nodePosition(node)));
        };

        const restoreLayoutPreview = () => {
            layoutOriginalPositions.forEach((position, node) => setNodeCoordinates(node, position.column, position.row));
            isLayoutDirty = false;
            refreshLayoutPresentation();
        };

        const moveSelectedNodes = positions => {
            positions.forEach(({ node, column, row }) => setNodeCoordinates(node, column, row));
            isLayoutDirty = selectedNodeList().some(node => {
                const original = layoutOriginalPositions.get(node);
                const current = nodePosition(node);
                return original && (original.column !== current.column || original.row !== current.row);
            });
            refreshLayoutPresentation();
        };

        const setCoordinates = (column, row) => {
            if (!selectedNode || !inspector.column || !inspector.row) return;
            if (!Number.isFinite(column) || !Number.isFinite(row)) return;
            column = Math.max(0, Math.round(column));
            row = Math.max(0, Math.round(row));
            const rank = selectedNode.closest("[data-tree-rank]");
            setNodeCoordinates(selectedNode, column, row);
            const displayCol = column + 1;
            const displayRow = row + 1;
            inspector.column.value = displayCol.toString();
            inspector.row.value = displayRow.toString();
            const coordinateText = `Column ${displayCol} · Row ${displayRow}`;
            if (inspector.coordinate) inspector.coordinate.textContent = coordinateText;
            layoutRank(rank);
            refreshOccupiedCells();
            refreshLayoutTools();
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

        const moveNodeToRank = (node, rankId) => {
            if (!node || !rankId) return;
            const oldRank = node.closest("[data-tree-rank]");
            const newRank = [...tree.querySelectorAll("[data-tree-rank]")]
                .find(rank => rank.dataset.treeRankId === rankId);
            if (!newRank || newRank === oldRank) return;
            newRank.querySelector(".visual-tree-rank-grid")?.appendChild(node);
            node.dataset.treeRankId = rankId;
            node.dataset.treeRankNumber = newRank.dataset.treeRank || "";
            layoutRank(oldRank);
            layoutRank(newRank);
        };

        const moveSelectedNodeToRank = rankId => {
            if (!selectedNode || !rankId) return;
            moveNodeToRank(selectedNode, rankId);
        };

        const updateUndoRedoControls = () => {
            if (undoButton) {
                undoButton.disabled = !isEditing() || undoStack.length === 0;
                undoButton.setAttribute("title", undoStack.length > 0
                    ? `Undo ${undoStack[undoStack.length - 1].action} (Ctrl+Z)`
                    : "Undo (Ctrl+Z)");
            }
            if (redoButton) {
                redoButton.disabled = !isEditing() || redoStack.length === 0;
                redoButton.setAttribute("title", redoStack.length > 0
                    ? `Redo ${redoStack[redoStack.length - 1].action} (Ctrl+Y)`
                    : "Redo (Ctrl+Y)");
            }
        };

        const pushHistory = (actionName, mutations) => {
            if (!isEditing()) return;
            const actualChanges = mutations.filter(m =>
                m.entryState
                    ? !stateMatches(m.from, m.to)
                    : m.from.column !== m.to.column ||
                      m.from.row !== m.to.row ||
                      (m.from.rankId && m.to.rankId && m.from.rankId !== m.to.rankId)
            );
            if (actualChanges.length === 0) return;

            undoStack.push({
                action: actionName,
                mutations: actualChanges
            });
            if (undoStack.length > maxHistorySize) {
                undoStack.shift();
            }
            redoStack.length = 0;
            updateUndoRedoControls();
        };

        const undo = () => {
            if (!isEditing() || undoStack.length === 0) return;
            const entry = undoStack.pop();
            const isEntryStateHistory = entry.mutations.some(mutation => mutation.entryState);
            entry.mutations.forEach(m => {
                if (m.entryState) {
                    applyEntryStateToNode(m.node, m.from);
                    if (m.node === selectedNode) {
                        setInspectorState(m.from);
                        if (inspector.coordinate) inspector.coordinate.textContent = `Column ${m.from.treeColumn} · Row ${m.from.treeRow}`;
                    }
                    return;
                }
                if (m.from.rankId && m.to.rankId && m.from.rankId !== m.to.rankId) {
                    moveNodeToRank(m.node, m.from.rankId);
                }
                setNodeCoordinates(m.node, m.from.column, m.from.row);
                if (m.node === selectedNode && inspector.column && inspector.row) {
                    const displayCol = m.from.column + 1;
                    const displayRow = m.from.row + 1;
                    inspector.column.value = displayCol.toString();
                    inspector.row.value = displayRow.toString();
                    if (inspector.coordinate) {
                        inspector.coordinate.textContent = `Column ${displayCol} · Row ${displayRow}`;
                    }
                }
            });
            redoStack.push(entry);

            rebuildFolderState();
            updateDirty();
            isLayoutDirty = !isEntryStateHistory && selectedNodeList().some(node => {
                const original = layoutOriginalPositions.get(node);
                const current = nodePosition(node);
                return original && (original.column !== current.column || original.row !== current.row);
            });

            refreshLayoutPresentation();
            drawConnectors();
            updateUndoRedoControls();
        };

        const redo = () => {
            if (!isEditing() || redoStack.length === 0) return;
            const entry = redoStack.pop();
            const isEntryStateHistory = entry.mutations.some(mutation => mutation.entryState);
            entry.mutations.forEach(m => {
                if (m.entryState) {
                    applyEntryStateToNode(m.node, m.to);
                    if (m.node === selectedNode) {
                        setInspectorState(m.to);
                        if (inspector.coordinate) inspector.coordinate.textContent = `Column ${m.to.treeColumn} · Row ${m.to.treeRow}`;
                    }
                    return;
                }
                if (m.from.rankId && m.to.rankId && m.from.rankId !== m.to.rankId) {
                    moveNodeToRank(m.node, m.to.rankId);
                }
                setNodeCoordinates(m.node, m.to.column, m.to.row);
                if (m.node === selectedNode && inspector.column && inspector.row) {
                    const displayCol = m.to.column + 1;
                    const displayRow = m.to.row + 1;
                    inspector.column.value = displayCol.toString();
                    inspector.row.value = displayRow.toString();
                    if (inspector.coordinate) {
                        inspector.coordinate.textContent = `Column ${displayCol} · Row ${displayRow}`;
                    }
                }
            });
            undoStack.push(entry);

            rebuildFolderState();
            updateDirty();
            isLayoutDirty = !isEntryStateHistory && selectedNodeList().some(node => {
                const original = layoutOriginalPositions.get(node);
                const current = nodePosition(node);
                return original && (original.column !== current.column || original.row !== current.row);
            });

            refreshLayoutPresentation();
            drawConnectors();
            updateUndoRedoControls();
        };

        const jumpToNode = nodeId => {
            const node = nodeById(nodeId);
            if (!node) return;

            const visualTreeTab = document.getElementById("visual-tree-tab");
            if (visualTreeTab && window.bootstrap?.Tab) {
                const tabInstance = window.bootstrap.Tab.getOrCreateInstance(visualTreeTab);
                tabInstance.show();
            } else if (visualTreeTab && !visualTreeTab.classList.contains("active")) {
                visualTreeTab.click();
            }

            if (isEditing()) {
                selectNode(node, true);
            } else if (!canEdit) {
                selectReadonlyNode(node);
            }

            node.scrollIntoView({ behavior: "smooth", block: "center", inline: "center" });

            node.classList.remove("is-jump-highlighted");
            void node.offsetWidth;
            node.classList.add("is-jump-highlighted");
            setTimeout(() => node.classList.remove("is-jump-highlighted"), 1500);
        };

        const entryStateFromPayload = entry => ({
            treeRankId: entry?.treeRankId?.toString() || "",
            rpCost: entry?.rpCost?.toString() || "0",
            slCost: entry?.slCost?.toString() || "0",
            treeColumn: entry?.treeColumn?.toString() || "1",
            treeRow: entry?.treeRow?.toString() || "1",
            acquisitionType: entry?.acquisitionType?.toString() || "0",
            folderParentId: entry?.folderParentEntryId?.toString() || ""
        });

        const applyEntryStateToNode = (node, state) => {
            if (!node || !state) return;
            moveNodeToRank(node, state.treeRankId);
            node.dataset.rpCost = state.rpCost;
            node.dataset.slCost = state.slCost;
            node.dataset.acquisitionType = state.acquisitionType;
            node.dataset.folderParentId = state.folderParentId;
            const column = state.treeColumn === "" ? Number.NaN : Number(state.treeColumn);
            const row = state.treeRow === "" ? Number.NaN : Number(state.treeRow);
            if (Number.isFinite(column) && Number.isFinite(row) && column >= 1 && row >= 1) {
                setNodeCoordinates(node, column - 1, row - 1);
            }

            const rpPreview = node.querySelector("[data-tree-rp-preview]");
            const slPreview = node.querySelector("[data-tree-sl-preview]");
            const acquisitionPreview = node.querySelector("[data-tree-acquisition-preview]");
            if (rpPreview) rpPreview.textContent = `${formatCost(state.rpCost)} RP`;
            if (slPreview) slPreview.textContent = `${formatCost(state.slCost)} SL`;
            if (acquisitionPreview) {
                Object.values(acquisitionClasses).forEach(className => acquisitionPreview.classList.remove(className));
                acquisitionPreview.classList.add(acquisitionClasses[state.acquisitionType] || "visual-tree-acquisition-hidden");
                acquisitionPreview.textContent = acquisitionLabels[state.acquisitionType] || "Unknown";
            }
            acquisitionNodeClasses.forEach(className => node.classList.remove(className));
            node.classList.add((acquisitionClasses[state.acquisitionType] || "visual-tree-acquisition-hidden")
                .replace("visual-tree-acquisition-", "visual-tree-node-acquisition-"));

            const rankPreview = node.querySelector("[data-tree-rank-preview]");
            const currentRank = node.closest("[data-tree-rank]");
            if (rankPreview) rankPreview.textContent = `Rank ${currentRank?.dataset.treeRankLabel || node.dataset.treeRankNumber}`;
            node.classList.toggle("visual-tree-node-folder-child", Boolean(state.folderParentId));
        };

        const rebuildFolderState = () => {
            const edgeData = tree.querySelector("[data-tree-edge-data]");
            edgeData?.querySelectorAll('[data-tree-edge][data-edge-kind="folder"]').forEach(edge => edge.remove());
            const parentIds = new Set();
            tree.querySelectorAll("[data-tree-node]").forEach(node => {
                const parentId = node.dataset.folderParentId;
                if (!parentId) return;
                parentIds.add(parentId);
                if (!edgeData) return;
                const edge = document.createElement("span");
                edge.dataset.treeEdge = "";
                edge.dataset.fromNode = parentId;
                edge.dataset.toNode = node.dataset.treeNode || "";
                edge.dataset.edgeKind = "folder";
                edgeData.appendChild(edge);
            });
            tree.querySelectorAll("[data-tree-node]").forEach(node => {
                const isParent = parentIds.has(node.dataset.treeNode || "");
                node.dataset.isFolderParent = isParent.toString();
                node.classList.toggle("visual-tree-node-folder-parent", isParent);
            });
        };

        const addPrerequisiteEdge = (prerequisiteId, dependentId) => {
            const edgeData = tree.querySelector("[data-tree-edge-data]");
            if (!edgeData || edgeData.querySelector(
                `[data-tree-edge][data-edge-kind="prerequisite"][data-from-node="${prerequisiteId}"][data-to-node="${dependentId}"]`)) return;
            const edge = document.createElement("span");
            edge.dataset.treeEdge = "";
            edge.dataset.fromNode = prerequisiteId;
            edge.dataset.toNode = dependentId;
            edge.dataset.edgeKind = "prerequisite";
            edgeData.appendChild(edge);
        };

        const removePrerequisiteEdge = (prerequisiteId, dependentId) => {
            tree.querySelector(
                `[data-tree-edge][data-edge-kind="prerequisite"][data-from-node="${prerequisiteId}"][data-to-node="${dependentId}"]`
            )?.remove();
        };

        const previewInspectorState = () => {
            if (!selectedNode) return;
            const state = entryStateFromForm();
            const previousRank = selectedNode.closest("[data-tree-rank]");
            applyEntryStateToNode(selectedNode, state);
            layoutRank(previousRank);
            layoutRank(selectedNode.closest("[data-tree-rank]"));

            const column = state.treeColumn === "" ? Number.NaN : Number(state.treeColumn);
            const row = state.treeRow === "" ? Number.NaN : Number(state.treeRow);
            if (Number.isFinite(column) && Number.isFinite(row) && column >= 1 && row >= 1) {
                setCoordinates(column - 1, row - 1);
            } else {
                updateDirty();
            }
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
            selectedNodeList().forEach(node => node.classList.remove("is-selected", "is-multi-selected"));
            selectedNodes.clear();
            selectedNode = null;
            originalEntry = null;
            layoutOriginalPositions.clear();
            isLayoutDirty = false;
            setDirty(false);
            setInspectorStatus("none");
            selectionEmpty?.classList.remove("d-none");
            if (selectionEmpty) selectionEmpty.textContent = "Select a vehicle node to inspect and edit it.";
            inspectorForm?.classList.add("d-none");
            removeForm?.classList.add("d-none");
            updateNavigation();
            refreshLayoutTools();
        };

        const clearReadonlySelection = () => {
            selectedNode?.classList.remove("is-selected");
            selectedNode = null;
            tree.classList.remove("is-inspecting");
            readonlyEmpty?.classList.remove("d-none");
            readonlyDetails?.classList.add("d-none");
            requestAnimationFrame(() => applyScale(scale));
        };

        const selectReadonlyNode = node => {
            if (canEdit || !node || !readonlyPanel) return;
            selectedNode?.classList.remove("is-selected");
            selectedNode = node;
            selectedNode.classList.add("is-selected");
            tree.classList.add("is-inspecting");
            readonlyEmpty?.classList.add("d-none");
            readonlyDetails?.classList.remove("d-none");

            if (readonlyInspector.name) readonlyInspector.name.textContent = nodeName(node);
            if (readonlyInspector.vehicleId) readonlyInspector.vehicleId.textContent = node.dataset.vehicleId || "";
            if (readonlyInspector.rank) readonlyInspector.rank.textContent = `Rank ${node.dataset.treeRankNumber || ""}`;
            if (readonlyInspector.rp) readonlyInspector.rp.textContent = `${formatCost(node.dataset.rpCost)} RP`;
            if (readonlyInspector.sl) readonlyInspector.sl.textContent = `${formatCost(node.dataset.slCost)} SL`;
            if (readonlyInspector.acquisition) {
                readonlyInspector.acquisition.textContent = node.querySelector("[data-tree-acquisition-preview]")?.textContent || "Unknown";
            }
            if (readonlyInspector.coordinate) {
                const col = Number.parseInt(node.dataset.treeColumn || "0", 10) + 1;
                const r = Number.parseInt(node.dataset.treeRow || "0", 10) + 1;
                readonlyInspector.coordinate.textContent = `Column ${col} · Row ${r}`;
            }
            if (readonlyInspector.folder) {
                const parent = nodeById(node.dataset.folderParentId);
                readonlyInspector.folder.textContent = parent ? `Member of ${nodeName(parent)}` :
                    node.dataset.isFolderParent === "true" ? "Folder parent" : "None";
            }
            if (readonlyInspector.image) {
                const imageUrl = node.dataset.imageUrl || "";
                readonlyInspector.image.toggleAttribute("hidden", !imageUrl);
                if (imageUrl) readonlyInspector.image.src = imageUrl;
                else readonlyInspector.image.removeAttribute("src");
            }
            if (readonlyInspector.wikiContainer && readonlyInspector.wikiLink) {
                const wikiUrl = (node.dataset.wikiUrl || "").trim();
                if (wikiUrl) {
                    readonlyInspector.wikiLink.href = wikiUrl;
                    readonlyInspector.wikiContainer.classList.remove("d-none");
                } else {
                    readonlyInspector.wikiContainer.classList.add("d-none");
                    readonlyInspector.wikiLink.removeAttribute("href");
                }
            }
            updateReadonlyProgressionControls(node);
            requestAnimationFrame(() => applyScale(scale));
        };

        const updateReadonlyProgressionControls = node => {
            if (!node || !readonlyPanel) return;
            const vId = node.dataset.vehicleId?.toLowerCase();
            const isOwned = vId && unlockedVehicleIds.has(vId);
            const isTarget = vId && targetVehicleId && vId === targetVehicleId.toLowerCase();

            let requiredSummary = null;
            if (calculationResult?.requiredVehicles && vId) {
                requiredSummary = calculationResult.requiredVehicles.find(
                    rv => rv.vehicleId.toLowerCase() === vId
                );
            }

            if (readonlyInspector.progressionBadge) {
                if (isTarget) {
                    readonlyInspector.progressionBadge.className = "badge bg-orange text-white";
                    readonlyInspector.progressionBadge.textContent = "Target";
                } else if (requiredSummary) {
                    if (requiredSummary.isRankGateFiller) {
                        readonlyInspector.progressionBadge.className = "badge bg-purple text-white";
                        readonlyInspector.progressionBadge.textContent = "Rank Filler";
                    } else {
                        readonlyInspector.progressionBadge.className = "badge bg-blue text-white";
                        readonlyInspector.progressionBadge.textContent = "Required Route";
                    }
                } else if (isOwned) {
                    readonlyInspector.progressionBadge.className = "badge bg-green-lt";
                    readonlyInspector.progressionBadge.textContent = "Owned";
                } else {
                    readonlyInspector.progressionBadge.className = "badge bg-secondary-lt";
                    readonlyInspector.progressionBadge.textContent = "Unresearched";
                }
            }

            if (readonlyInspector.toggleOwnedBtn) {
                if (isOwned) {
                    readonlyInspector.toggleOwnedBtn.textContent = "Mark as Unowned";
                    readonlyInspector.toggleOwnedBtn.className = "btn btn-sm btn-outline-danger w-100";
                } else {
                    readonlyInspector.toggleOwnedBtn.textContent = "Mark as Owned";
                    readonlyInspector.toggleOwnedBtn.className = "btn btn-sm btn-outline-success w-100";
                }
            }

            if (readonlyInspector.setTargetBtn) {
                if (isTarget) {
                    readonlyInspector.setTargetBtn.textContent = "Clear Target";
                    readonlyInspector.setTargetBtn.className = "btn btn-sm btn-outline-secondary w-100";
                } else {
                    readonlyInspector.setTargetBtn.textContent = "Set as Target";
                    readonlyInspector.setTargetBtn.className = "btn btn-sm btn-primary w-100";
                }
            }
        };

        const updateProgressionUI = () => {
            const requiredMap = new Map();
            if (calculationResult?.requiredVehicles) {
                calculationResult.requiredVehicles.forEach(rv => {
                    requiredMap.set(rv.vehicleId.toLowerCase(), rv);
                });
            }

            tree.querySelectorAll("[data-tree-node]").forEach(node => {
                const vId = node.dataset.vehicleId?.toLowerCase();
                const badge = node.querySelector("[data-progression-badge]");
                node.classList.remove("is-progression-owned", "is-progression-target", "is-progression-required", "is-progression-filler");

                if (!vId) {
                    if (badge) badge.classList.add("d-none");
                    return;
                }

                if (targetVehicleId && vId === targetVehicleId.toLowerCase()) {
                    node.classList.add("is-progression-target");
                    if (badge) {
                        badge.className = "badge bg-orange text-white visual-tree-progression-badge";
                        badge.textContent = "Target";
                        badge.classList.remove("d-none");
                    }
                } else if (requiredMap.has(vId)) {
                    const summary = requiredMap.get(vId);
                    if (summary.isRankGateFiller) {
                        node.classList.add("is-progression-filler");
                        if (badge) {
                            badge.className = "badge bg-purple text-white visual-tree-progression-badge";
                            badge.textContent = "Filler";
                            badge.classList.remove("d-none");
                        }
                    } else {
                        node.classList.add("is-progression-required");
                        if (badge) {
                            badge.className = "badge bg-blue text-white visual-tree-progression-badge";
                            badge.textContent = "Required";
                            badge.classList.remove("d-none");
                        }
                    }
                } else if (unlockedVehicleIds.has(vId)) {
                    node.classList.add("is-progression-owned");
                    if (badge) {
                        badge.className = "badge bg-green-lt visual-tree-progression-badge";
                        badge.textContent = "Owned";
                        badge.classList.remove("d-none");
                    }
                } else {
                    if (badge) badge.classList.add("d-none");
                }
            });

            if (selectedNode) {
                updateReadonlyProgressionControls(selectedNode);
            }

            drawConnectors();
        };

        const computeActiveRouteEdges = () => {
            activeRouteEdges.clear();
            if (!calculationResult || !targetVehicleId) return;

            const requiredMap = new Map();
            calculationResult.requiredVehicles.forEach(rv => {
                requiredMap.set(rv.vehicleId.toLowerCase(), rv);
            });

            tree.querySelectorAll("[data-tree-edge][data-edge-kind='prerequisite']").forEach(edge => {
                const fromNode = nodeById(edge.dataset.fromNode);
                const toNode = nodeById(edge.dataset.toNode);
                if (!fromNode || !toNode) return;

                const toVehicleId = toNode.dataset.vehicleId?.toLowerCase();
                const fromVehicleId = fromNode.dataset.vehicleId?.toLowerCase();
                if (!toVehicleId || !fromVehicleId) return;

                const toIsTarget = toVehicleId === targetVehicleId.toLowerCase();
                const toSummary = requiredMap.get(toVehicleId);

                if (toIsTarget || (toSummary && !toSummary.isRankGateFiller)) {
                    const fromSummary = requiredMap.get(fromVehicleId);
                    const fromIsOwned = unlockedVehicleIds.has(fromVehicleId);
                    if ((fromSummary && !fromSummary.isRankGateFiller) || fromIsOwned) {
                        activeRouteEdges.add(`${edge.dataset.fromNode}->${edge.dataset.toNode}`);
                    }
                }
            });
        };

        const calculateRoute = async () => {
            if (!targetVehicleId || !versionId) {
                calculationResult = null;
                activeRouteEdges.clear();
                hud?.classList.add("d-none");
                updateProgressionUI();
                return;
            }

            const optMode = hudOptMode?.value || "LeastRp";
            const avgRp = Number.parseInt(hudRpInput?.value || "5000", 10) || 5000;
            const avgSl = Number.parseInt(hudSlInput?.value || "20000", 10) || 20000;

            const targetNode = Array.from(tree.querySelectorAll("[data-tree-node]"))
                .find(n => n.dataset.vehicleId?.toLowerCase() === targetVehicleId.toLowerCase());
            const targetName = targetNode ? nodeName(targetNode) : "Selected vehicle";

            try {
                const response = await fetch("/Tree?handler=Calculate", {
                    method: "POST",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify({
                        researchTreeVersionId: versionId,
                        targetVehicleId: targetVehicleId,
                        unlockedVehicleIds: Array.from(unlockedVehicleIds),
                        fillerTargetIds: [],
                        averageRpPerMatch: avgRp,
                        averageNetSlPerMatch: avgSl,
                        optimizationMode: optMode
                    })
                });

                if (!response.ok) {
                    console.error("Calculation failed:", await response.text());
                    return;
                }

                const data = await response.json();
                calculationResult = data;

                if (hud) {
                    hud.classList.remove("d-none");
                    if (hudTargetName) hudTargetName.textContent = targetName;
                    if (hudTotalRp) hudTotalRp.textContent = `${formatCost(data.totalRpRequired)} RP`;
                    if (hudTotalSl) hudTotalSl.textContent = `${formatCost(data.totalSlRequired)} SL`;
                    if (hudMatches) {
                        hudMatches.textContent = `${data.estimatedMatches} matches`;
                        hudMatches.title = `RP estimate: ${data.rpEstimatedMatches} matches · SL estimate: ${data.slEstimatedMatches} matches`;
                    }

                    if (hudDeficitsContainer && hudDeficitsList) {
                        if (data.rankDeficits && data.rankDeficits.length > 0) {
                            hudDeficitsContainer.classList.remove("d-none");
                            hudDeficitsList.innerHTML = "<strong>Rank Gates:</strong> " +
                                data.rankDeficits.map(d => `<span class="badge bg-warning text-dark">Rank ${d.rankNumber}: need ${d.shortfall} more (requires ${d.required})</span>`).join(" ");
                        } else {
                            hudDeficitsContainer.classList.add("d-none");
                            hudDeficitsList.innerHTML = "";
                        }
                    }
                }

                computeActiveRouteEdges();
                updateProgressionUI();
            } catch (err) {
                console.error("Error calculating research:", err);
            }
        };

        const toggleVehicleOwnership = async vehicleId => {
            if (!vehicleId) return;
            const normId = vehicleId.toLowerCase();
            if (unlockedVehicleIds.has(normId)) {
                unlockedVehicleIds.delete(normId);
                allUnlockedVehicleIds.delete(normId);
            } else {
                unlockedVehicleIds.add(normId);
                allUnlockedVehicleIds.add(normId);
                if (targetVehicleId && targetVehicleId.toLowerCase() === normId) {
                    targetVehicleId = null;
                }
            }

            updateProgressionUI();

            if (playerState.isAuthenticated) {
                try {
                    const optMode = hudOptMode?.value || playerState.preferences?.optimizationMode || "LeastRp";
                    const avgRp = Number.parseInt(hudRpInput?.value || "5000", 10) || 5000;
                    const avgSl = Number.parseInt(hudSlInput?.value || "20000", 10) || 20000;

                    const response = await fetch("/Tree?handler=UpdateProgress", {
                        method: "POST",
                        headers: { "Content-Type": "application/json" },
                        body: JSON.stringify({
                            unlockedVehicleIds: Array.from(allUnlockedVehicleIds),
                            preferences: {
                                optimizationMode: optMode,
                                averageRpPerMatch: avgRp,
                                averageNetSlPerMatch: avgSl
                            }
                        })
                    });

                    if (response.status === 409) {
                        // Concurrency conflict: refetch player progress
                        const refreshResponse = await fetch("/Tree?handler=PlayerProgress");
                        if (refreshResponse.ok) {
                            const freshData = await refreshResponse.json();
                            allUnlockedVehicleIds.clear();
                            (freshData.unlockedVehicleIds || []).forEach(id => allUnlockedVehicleIds.add(id.toLowerCase()));
                            unlockedVehicleIds.clear();
                            allUnlockedVehicleIds.forEach(id => {
                                if (treeVehicleIds.has(id)) unlockedVehicleIds.add(id);
                            });
                            updateProgressionUI();
                        }
                    }
                } catch (err) {
                    console.error("Failed to update progress:", err);
                }
            }

            if (targetVehicleId) {
                await calculateRoute();
            }
        };

        const setTargetVehicle = async vehicleId => {
            if (!vehicleId) return;
            const normId = vehicleId.toLowerCase();
            if (targetVehicleId && targetVehicleId.toLowerCase() === normId) {
                targetVehicleId = null;
            } else {
                targetVehicleId = vehicleId;
            }
            await calculateRoute();
        };

        const hidePlayerModal = () => {
            if (!playerModalEl) return;
            if (window.bootstrap?.Modal) {
                const modal = window.bootstrap.Modal.getInstance(playerModalEl);
                if (modal) modal.hide();
                else {
                    playerModalEl.classList.remove("show");
                    playerModalEl.style.display = "none";
                    document.body.classList.remove("modal-open");
                    document.querySelectorAll(".modal-backdrop").forEach(b => b.remove());
                }
            } else {
                playerModalEl.classList.remove("show");
                playerModalEl.style.display = "none";
                document.body.classList.remove("modal-open");
                document.querySelectorAll(".modal-backdrop").forEach(b => b.remove());
            }
        };

        const updatePlayerWidget = username => {
            if (!playerWidget) return;
            if (username) {
                playerWidget.innerHTML = `
                    <span class="badge bg-green-lt d-flex align-items-center gap-1" data-player-user-display>
                        <span class="status-dot status-dot-animated bg-green me-1"></span>
                        <strong data-player-username-text>${username}</strong>
                    </span>
                    <button type="button" class="btn btn-outline-danger btn-sm" data-player-logout-btn>Sign out</button>
                `;
                playerWidget.querySelector("[data-player-logout-btn]")?.addEventListener("click", async () => {
                    try {
                        await fetch("/Tree?handler=PlayerLogout", { method: "POST" });
                    } catch { }
                    playerState.isAuthenticated = false;
                    playerState.username = null;
                    updatePlayerWidget(null);
                    updateProgressionUI();
                });
            } else {
                playerWidget.innerHTML = `
                    <button type="button" class="btn btn-primary btn-sm" data-player-login-btn data-bs-toggle="modal" data-bs-target="#playerAuthModal">
                        Player sign in
                    </button>
                `;
            }
        };

        const initPlayerProgression = () => {
            const initialDataEl = document.getElementById("initial-player-state");
            let initialData = null;
            if (initialDataEl) {
                try {
                    initialData = JSON.parse(initialDataEl.textContent);
                } catch { }
            }

            if (initialData) {
                playerState.isAuthenticated = Boolean(initialData.isAuthenticated);
                playerState.username = initialData.username || null;
                playerState.preferences = initialData.preferences || null;
                versionId = initialData.versionId || null;

                allUnlockedVehicleIds.clear();
                (initialData.unlockedVehicleIds || []).forEach(id => {
                    if (id) allUnlockedVehicleIds.add(id.toLowerCase());
                });

                treeVehicleIds.clear();
                (initialData.treeVehicleIds || []).forEach(id => {
                    if (id) treeVehicleIds.add(id.toLowerCase());
                });

                unlockedVehicleIds.clear();
                allUnlockedVehicleIds.forEach(id => {
                    if (treeVehicleIds.has(id)) unlockedVehicleIds.add(id);
                });

                if (hudOptMode && playerState.preferences?.optimizationMode) {
                    hudOptMode.value = playerState.preferences.optimizationMode;
                }
                if (hudRpInput && playerState.preferences?.averageRpPerMatch) {
                    hudRpInput.value = playerState.preferences.averageRpPerMatch;
                } else if (hudRpInput) {
                    hudRpInput.value = "5000";
                }
                if (hudSlInput && playerState.preferences?.averageNetSlPerMatch) {
                    hudSlInput.value = playerState.preferences.averageNetSlPerMatch;
                } else if (hudSlInput) {
                    hudSlInput.value = "20000";
                }
            } else {
                tree.querySelectorAll("[data-tree-node]").forEach(n => {
                    const vId = n.dataset.vehicleId?.toLowerCase();
                    if (vId) treeVehicleIds.add(vId);
                });
            }

            hudOptMode?.addEventListener("change", () => { if (targetVehicleId) calculateRoute(); });
            hudRpInput?.addEventListener("change", () => { if (targetVehicleId) calculateRoute(); });
            hudSlInput?.addEventListener("change", () => { if (targetVehicleId) calculateRoute(); });
            hudClearTarget?.addEventListener("click", () => {
                targetVehicleId = null;
                calculateRoute();
            });

            loginForm?.addEventListener("submit", async event => {
                event.preventDefault();
                loginError?.classList.add("d-none");
                const usernameOrEmail = loginForm.querySelector("[data-login-user]")?.value;
                const password = loginForm.querySelector("[data-login-pass]")?.value;
                try {
                    const res = await fetch("/Tree?handler=PlayerLogin", {
                        method: "POST",
                        headers: { "Content-Type": "application/json" },
                        body: JSON.stringify({ usernameOrEmail, password })
                    });
                    if (res.ok) {
                        const data = await res.json();
                        playerState.isAuthenticated = true;
                        playerState.username = data.username;
                        updatePlayerWidget(data.username);
                        hidePlayerModal();

                        const progRes = await fetch("/Tree?handler=PlayerProgress");
                        if (progRes.ok) {
                            const progData = await progRes.json();
                            allUnlockedVehicleIds.clear();
                            (progData.unlockedVehicleIds || []).forEach(id => allUnlockedVehicleIds.add(id.toLowerCase()));
                            unlockedVehicleIds.clear();
                            allUnlockedVehicleIds.forEach(id => {
                                if (treeVehicleIds.has(id)) unlockedVehicleIds.add(id);
                            });
                            if (progData.preferences) {
                                playerState.preferences = progData.preferences;
                                if (hudOptMode && progData.preferences.optimizationMode) hudOptMode.value = progData.preferences.optimizationMode;
                                if (hudRpInput && progData.preferences.averageRpPerMatch) hudRpInput.value = progData.preferences.averageRpPerMatch;
                                if (hudSlInput && progData.preferences.averageNetSlPerMatch) hudSlInput.value = progData.preferences.averageNetSlPerMatch;
                            }
                            updateProgressionUI();
                            if (targetVehicleId) calculateRoute();
                        }
                    } else {
                        const err = await res.json().catch(() => ({ message: "Login failed." }));
                        if (loginError) {
                            loginError.textContent = err.message || "Invalid credentials.";
                            loginError.classList.remove("d-none");
                        }
                    }
                } catch (ex) {
                    if (loginError) {
                        loginError.textContent = "Unable to connect: " + ex.message;
                        loginError.classList.remove("d-none");
                    }
                }
            });

            registerForm?.addEventListener("submit", async event => {
                event.preventDefault();
                registerError?.classList.add("d-none");
                const username = registerForm.querySelector("[data-reg-user]")?.value;
                const email = registerForm.querySelector("[data-reg-email]")?.value;
                const password = registerForm.querySelector("[data-reg-pass]")?.value;
                try {
                    const res = await fetch("/Tree?handler=PlayerRegister", {
                        method: "POST",
                        headers: { "Content-Type": "application/json" },
                        body: JSON.stringify({ username, email, password })
                    });
                    if (res.ok) {
                        const data = await res.json();
                        playerState.isAuthenticated = true;
                        playerState.username = data.username;
                        updatePlayerWidget(data.username);
                        hidePlayerModal();
                        updateProgressionUI();
                    } else {
                        const err = await res.json().catch(() => ({ message: "Registration failed." }));
                        if (registerError) {
                            registerError.textContent = err.message || "Registration failed.";
                            registerError.classList.remove("d-none");
                        }
                    }
                } catch (ex) {
                    if (registerError) {
                        registerError.textContent = "Unable to connect: " + ex.message;
                        registerError.classList.remove("d-none");
                    }
                }
            });

            logoutBtn?.addEventListener("click", async () => {
                try {
                    await fetch("/Tree?handler=PlayerLogout", { method: "POST" });
                } catch { }
                playerState.isAuthenticated = false;
                playerState.username = null;
                updatePlayerWidget(null);
                updateProgressionUI();
            });

            updateProgressionUI();
        };

        const selectNode = (node, shouldFocus = true) => {
            if (!isEditing() || !node) return false;
            if (node === selectedNode && selectedNodes.size === 1) {
                if (shouldFocus) focusInspector();
                return true;
            }
            if (!confirmDiscardChanges()) return false;
            if (isLayoutDirty) restoreLayoutPreview();
            restoreSelectedPreview();
            selectedNodeList().forEach(selected => selected.classList.remove("is-selected", "is-multi-selected"));
            selectedNodes.clear();
            selectedNode = node;
            selectedNodes.add(node);
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
            if (inspector.wikiContainer && inspector.wikiLink) {
                const wikiUrl = (node.dataset.wikiUrl || "").trim();
                if (wikiUrl) {
                    inspector.wikiLink.href = wikiUrl;
                    inspector.wikiContainer.classList.remove("d-none");
                } else {
                    inspector.wikiContainer.classList.add("d-none");
                    inspector.wikiLink.removeAttribute("href");
                }
            }
            setInspectorState(originalEntry);
            const col = Number.parseInt(node.dataset.treeColumn || "0", 10) + 1;
            const r = Number.parseInt(node.dataset.treeRow || "0", 10) + 1;
            if (inspector.coordinate) inspector.coordinate.textContent = `Column ${col} · Row ${r}`;
            if (inspector.folder) {
                inspector.folder.querySelectorAll("option").forEach(option => {
                    option.disabled = option.value === node.dataset.treeNode;
                });
            }
            selectionEmpty?.classList.add("d-none");
            inspectorForm?.classList.remove("d-none");
            removeForm?.classList.remove("d-none");
            setDirty(false);
            setInspectorStatus("none");
            updateNavigation();
            rememberLayoutPositions();
            refreshLayoutTools();
            if (shouldFocus) focusInspector();
            return true;
        };

        const toggleNodeSelection = node => {
            if (!isEditing() || !node) return;
            if (isDirty || isLayoutDirty) {
                if (!confirmDiscardChanges()) return;
                if (isLayoutDirty) restoreLayoutPreview();
                restoreSelectedPreview();
            }

            if (selectedNodes.size === 0) {
                selectNode(node, false);
                return;
            }

            const rankId = selectedNodeList()[0]?.dataset.treeRankId;
            if (!selectedNodes.has(node) && node.dataset.treeRankId !== rankId) {
                window.alert("Multi-selection is limited to entries in the same rank.");
                return;
            }
            if (!selectedNodes.has(node) && selectedNodes.size >= 12) {
                window.alert("Select at most 12 entries for one layout operation.");
                return;
            }

            if (selectedNodes.has(node)) selectedNodes.delete(node);
            else selectedNodes.add(node);

            tree.querySelectorAll("[data-tree-node]").forEach(candidate =>
                candidate.classList.remove("is-selected", "is-multi-selected"));

            if (selectedNodes.size === 0) {
                clearEntrySelection();
                return;
            }
            if (selectedNodes.size === 1) {
                const remaining = selectedNodeList()[0];
                selectedNodes.clear();
                selectedNode = null;
                selectNode(remaining, false);
                return;
            }

            selectedNode = selectedNodes.has(selectedNode) ? selectedNode : selectedNodeList()[0];
            selectedNodeList().forEach(selected => selected.classList.add("is-multi-selected"));
            selectedNode.classList.add("is-selected");
            originalEntry = null;
            setDirty(false);
            setInspectorStatus("none");
            selectionEmpty?.classList.remove("d-none");
            if (selectionEmpty) selectionEmpty.textContent = "Multiple entries selected. Use the layout toolbar to align, distribute, move, save, or discard their positions.";
            inspectorForm?.classList.add("d-none");
            removeForm?.classList.add("d-none");
            rememberLayoutPositions();
            refreshLayoutTools();
        };

        const showPlacementPreview = (rank, column, row) => {
            tree.querySelectorAll(".visual-tree-placement-preview").forEach(preview => preview.remove());
            const grid = rank?.querySelector(".visual-tree-rank-grid");
            if (!grid) return;
            const preview = document.createElement("div");
            preview.className = "visual-tree-placement-preview";
            preview.style.left = `${Math.max(0, column) * columnStep}px`;
            const minimumRow = Number.parseInt(rank.dataset.treeMinRow || "0", 10);
            preview.style.top = `${Math.max(0, row - minimumRow) * rowStep}px`;
            preview.setAttribute("aria-hidden", "true");

            const displayCol = Math.max(0, column) + 1;
            const displayRow = Math.max(0, row) + 1;
            const badge = document.createElement("span");
            badge.className = "visual-tree-placement-badge badge bg-dark text-white";
            badge.textContent = `Col ${displayCol}, Row ${displayRow}`;
            preview.appendChild(badge);
            preview.title = `Position: Col ${displayCol}, Row ${displayRow}`;

            grid.appendChild(preview);
        };

        const applyDragDelta = (columnDelta, rowDelta) => {
            if (!dragState) return;
            if (selectedNodes.size === 1) {
                const start = dragState.positions.get(selectedNode);
                setCoordinates(start.column + columnDelta, start.row + rowDelta);
                return;
            }
            moveSelectedNodes(selectedNodeList().map(node => {
                const start = dragState.positions.get(node);
                return { node, column: start.column + columnDelta, row: start.row + rowDelta };
            }));
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
            if (!editToggle.checked && (isDirty || isLayoutDirty) && !confirmDiscardChanges()) {
                editToggle.checked = true;
                return;
            }
            if (!editToggle.checked && isLayoutDirty) restoreLayoutPreview();
            if (!editToggle.checked) restoreSelectedPreview();
            if (isEditing()) deactivateEdgeMode();
            tree.classList.toggle("is-editing", isEditing());
            if (help) help.textContent = isEditing()
                ? "Select a node, then drag it or use the arrow keys or inspector buttons"
                : "Drag empty space to pan · enable Move entries to reposition nodes";
            if (!isEditing()) {
                clearEntrySelection();
                undoStack.length = 0;
                redoStack.length = 0;
            }
            updateUndoRedoControls();
            requestAnimationFrame(() => applyScale(scale));
        });

        undoButton?.addEventListener("click", undo);
        redoButton?.addEventListener("click", redo);

        edgeToggle?.addEventListener("click", () => {
            if (isEdgeMode()) deactivateEdgeMode();
            else {
                if (!confirmDiscardChanges()) return;
                if (isLayoutDirty) restoreLayoutPreview();
                restoreSelectedPreview();
                activateEdgeMode();
            }
        });

        tree.querySelectorAll("[data-tree-node]").forEach(node => {
            node.addEventListener("pointerdown", event => {
                if (!isEditing() || event.button !== 0 || !selectedNodes.has(node) || event.ctrlKey || event.metaKey || event.shiftKey) return;
                event.preventDefault();
                dragState = {
                    node,
                    pointerId: event.pointerId,
                    startX: event.clientX,
                    startY: event.clientY,
                    positions: new Map(selectedNodeList().map(selected => [selected, nodePosition(selected)])),
                    columnDelta: 0,
                    rowDelta: 0,
                    moved: false
                };
                selectedNodeList().forEach(selected => selected.classList.add("is-dragging"));
                node.setPointerCapture(event.pointerId);
            });
            node.addEventListener("pointermove", event => {
                if (!dragState || dragState.node !== node) return;
                const columnDelta = Math.round((event.clientX - dragState.startX) / (columnStep * scale));
                const rowDelta = Math.round((event.clientY - dragState.startY) / (rowStep * scale));
                dragState.columnDelta = columnDelta;
                dragState.rowDelta = rowDelta;
                dragState.moved ||= Math.abs(event.clientX - dragState.startX) > 4 || Math.abs(event.clientY - dragState.startY) > 4;
                const primaryStart = dragState.positions.get(selectedNode);
                showPlacementPreview(selectedNode.closest("[data-tree-rank]"), primaryStart.column + columnDelta, primaryStart.row + rowDelta);
                if (snapToggle?.checked !== false) {
                    applyDragDelta(columnDelta, rowDelta);
                } else {
                    const x = (event.clientX - dragState.startX) / scale;
                    const y = (event.clientY - dragState.startY) / scale;
                    selectedNodeList().forEach(selected => { selected.style.transform = `translate(${x}px, ${y}px)`; });
                }
            });
            const stopDragging = event => {
                if (!dragState || dragState.node !== node) return;
                const completedDrag = dragState;
                selectedNodeList().forEach(selected => {
                    selected.classList.remove("is-dragging");
                    selected.style.removeProperty("transform");
                });
                tree.querySelectorAll(".visual-tree-placement-preview").forEach(preview => preview.remove());
                dragState = null;
                if (completedDrag.moved && snapToggle?.checked === false) {
                    dragState = completedDrag;
                    applyDragDelta(completedDrag.columnDelta, completedDrag.rowDelta);
                    dragState = null;
                }
                suppressNodeClick = completedDrag.moved;
                if (node.hasPointerCapture(event.pointerId)) node.releasePointerCapture(event.pointerId);

                if (completedDrag.moved) {
                    const mutations = [];
                    completedDrag.positions.forEach((startPos, n) => {
                        const endPos = nodePosition(n);
                        if (startPos.column !== endPos.column || startPos.row !== endPos.row) {
                            mutations.push({ node: n, from: startPos, to: endPos });
                        }
                    });
                    if (mutations.length > 0) {
                        pushHistory("Move vehicle", mutations);
                    }
                }
            };
            node.addEventListener("pointerup", stopDragging);
            node.addEventListener("pointercancel", stopDragging);
            node.addEventListener("click", event => {
                if (suppressNodeClick) {
                    suppressNodeClick = false;
                    event.preventDefault();
                    return;
                }
                const ownedToggle = event.target.closest("[data-node-owned-toggle]");
                if (ownedToggle) {
                    event.preventDefault();
                    event.stopPropagation();
                    toggleVehicleOwnership(node.dataset.vehicleId);
                    return;
                }
                const targetToggle = event.target.closest("[data-node-target-toggle]");
                if (targetToggle) {
                    event.preventDefault();
                    event.stopPropagation();
                    setTargetVehicle(node.dataset.vehicleId);
                    return;
                }
                if (isEdgeMode()) {
                    event.preventDefault();
                    selectNodeForEdge(node);
                } else if (isEditing()) {
                    event.preventDefault();
                    if (event.ctrlKey || event.metaKey || event.shiftKey) toggleNodeSelection(node);
                    else selectNode(node, true);
                } else if (!canEdit) {
                    event.preventDefault();
                    selectReadonlyNode(node);
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
                } else if (!canEdit) {
                    event.preventDefault();
                    selectReadonlyNode(node);
                }
            });
        });

        tree.querySelector("[data-tree-readonly-close]")?.addEventListener("click", clearReadonlySelection);

        readonlyInspector.toggleOwnedBtn?.addEventListener("click", () => {
            if (selectedNode?.dataset.vehicleId) {
                toggleVehicleOwnership(selectedNode.dataset.vehicleId);
            }
        });

        readonlyInspector.setTargetBtn?.addEventListener("click", () => {
            if (selectedNode?.dataset.vehicleId) {
                setTargetVehicle(selectedNode.dataset.vehicleId);
            }
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
                let column = Number.parseInt(inspector.column?.value || "1", 10);
                let row = Number.parseInt(inspector.row?.value || "1", 10);
                if (button.dataset.treeNudge === "left") column--;
                if (button.dataset.treeNudge === "right") column++;
                if (button.dataset.treeNudge === "up") row--;
                if (button.dataset.treeNudge === "down") row++;
                if (column < 1 || row < 1) return;
                const from = nodePosition(selectedNode);
                setCoordinates(column - 1, row - 1);
                pushHistory("Nudge", [{ node: selectedNode, from, to: { column: column - 1, row: row - 1 } }]);
            });
        });
        tree.querySelectorAll("[data-tree-layout-nudge]").forEach(button => {
            button.addEventListener("click", () => {
                if (selectedNodes.size === 0) return;
                const delta = { column: 0, row: 0 };
                if (button.dataset.treeLayoutNudge === "left") delta.column--;
                if (button.dataset.treeLayoutNudge === "right") delta.column++;
                if (button.dataset.treeLayoutNudge === "up") delta.row--;
                if (button.dataset.treeLayoutNudge === "down") delta.row++;
                const nodes = selectedNodeList();
                if (!nodes.every(n => {
                    const pos = nodePosition(n);
                    return pos.column + delta.column >= 0 && pos.row + delta.row >= 0;
                })) return;
                const fromSnapshots = nodes.map(n => ({ node: n, from: nodePosition(n) }));
                moveSelectedNodes(nodes.map(node => {
                    const current = nodePosition(node);
                    return { node, column: current.column + delta.column, row: current.row + delta.row };
                }));
                pushHistory("Nudge selection", fromSnapshots.map(s => ({ node: s.node, from: s.from, to: nodePosition(s.node) })));
            });
        });
        tree.querySelectorAll("[data-tree-layout-action]").forEach(button => {
            button.addEventListener("click", () => {
                const nodes = selectedNodeList();
                if (nodes.length < 2) return;
                const primary = nodePosition(selectedNode || nodes[0]);
                const fromSnapshots = nodes.map(n => ({ node: n, from: nodePosition(n) }));
                let actionName = "Align";
                if (button.dataset.treeLayoutAction === "align-column") {
                    actionName = "Align column";
                    moveSelectedNodes(nodes.map(node => ({ node, column: primary.column, row: nodePosition(node).row })));
                }
                if (button.dataset.treeLayoutAction === "align-row") {
                    actionName = "Align row";
                    moveSelectedNodes(nodes.map(node => ({ node, column: nodePosition(node).column, row: primary.row })));
                }
                if (button.dataset.treeLayoutAction === "distribute-vertical" && nodes.length >= 3) {
                    actionName = "Distribute vertically";
                    const ordered = [...nodes].sort((left, right) => nodePosition(left).row - nodePosition(right).row || nodeName(left).localeCompare(nodeName(right)));
                    const firstRow = nodePosition(ordered[0]).row;
                    const lastRow = Math.max(nodePosition(ordered[ordered.length - 1]).row, firstRow + ordered.length - 1);
                    moveSelectedNodes(ordered.map((node, index) => ({
                        node,
                        column: nodePosition(node).column,
                        row: Math.round(firstRow + ((lastRow - firstRow) * index / (ordered.length - 1)))
                    })));
                }
                pushHistory(actionName, fromSnapshots.map(s => ({ node: s.node, from: s.from, to: nodePosition(s.node) })));
            });
        });
        layoutDiscard?.addEventListener("click", () => {
            restoreLayoutPreview();
            undoStack.length = 0;
            redoStack.length = 0;
            updateUndoRedoControls();
        });
        layoutForm?.addEventListener("submit", event => {
            if (!isLayoutDirty || refreshCollisionState()) {
                event.preventDefault();
                return;
            }
            event.preventDefault();
            rebuildLayoutFields();
            const save = async () => {
                const originalLabel = layoutSave?.textContent || "Save layout";
                if (layoutSave) {
                    layoutSave.disabled = true;
                    layoutSave.textContent = "Saving\u2026";
                }
                setTransientStatus(layoutStatus, "Saving\u2026");
                try {
                    const payload = await postFormJson(layoutForm, "UpdateLayoutJson");
                    (payload.positions || []).forEach(position => {
                        const node = nodeById(position.entryId);
                        if (node) setNodeCoordinates(node, position.treeColumn - 1, position.treeRow - 1);
                    });
                    isLayoutDirty = false;
                    rememberLayoutPositions();
                    setTransientStatus(layoutStatus, "Saved", "success");
                    refreshLayoutPresentation();
                } catch (error) {
                    setTransientStatus(layoutStatus, `Save failed \u2014 ${error.message}`, "danger");
                    refreshLayoutTools();
                } finally {
                    if (layoutSave) layoutSave.textContent = originalLabel;
                }
            };
            save();
        });
        let inspectorCoordFocus = null;
        [inspector.column, inspector.row].forEach(input => {
            if (!input) return;
            input.addEventListener("focus", () => {
                if (selectedNode) {
                    inspectorCoordFocus = nodePosition(selectedNode);
                }
            });
            input.addEventListener("change", () => {
                if (selectedNode && inspectorCoordFocus) {
                    const current = nodePosition(selectedNode);
                    if (inspectorCoordFocus.column !== current.column || inspectorCoordFocus.row !== current.row) {
                        pushHistory("Change coordinates", [{
                            node: selectedNode,
                            from: inspectorCoordFocus,
                            to: current
                        }]);
                        inspectorCoordFocus = { ...current };
                    }
                }
            });
        });
        inspectorForm?.querySelectorAll("[data-dirty-field]").forEach(field => {
            field.addEventListener("input", previewInspectorState);
            field.addEventListener("change", previewInspectorState);
        });
        inspectorForm?.addEventListener("submit", event => {
            event.preventDefault();
            if (!selectedNode || !originalEntry || !isDirty) return;
            const node = selectedNode;
            const preSaveState = { ...originalEntry };
            const save = async () => {
                setInspectorStatus("saving");
                if (inspectorSave) inspectorSave.disabled = true;
                try {
                    const payload = await postFormJson(inspectorForm, "UpdateVisualEntryJson");
                    const authoritative = entryStateFromPayload(payload.entry);
                    applyEntryStateToNode(node, authoritative);
                    setInspectorState(authoritative);
                    rebuildFolderState();
                    originalEntry = authoritative;
                    undoStack.length = 0;
                    redoStack.length = 0;
                    pushHistory("saved entry change", [{
                        node,
                        from: preSaveState,
                        to: authoritative,
                        entryState: true
                    }]);
                    setDirty(false);
                    setInspectorStatus("saved");
                    rememberLayoutPositions();
                    refreshLayoutPresentation();
                    updateNavigation();
                } catch (error) {
                    applyEntryStateToNode(node, originalEntry);
                    rebuildFolderState();
                    isDirty = true;
                    inspectorCard?.classList.add("is-dirty");
                    inspectorForm.dataset.dirty = "true";
                    setInspectorStatus("failed", error.message);
                    refreshLayoutPresentation();
                    updateNavigation();
                } finally {
                    if (inspectorSave) inspectorSave.disabled = refreshCollisionState();
                }
            };
            save();
        });
        removeForm?.addEventListener("submit", event => {
            event.preventDefault();
            if (!selectedNode || !window.confirm("Remove this vehicle from the draft?")) return;
            const node = selectedNode;
            const remove = async () => {
                setInspectorStatus("saving");
                try {
                    await postFormJson(removeForm, "DeleteEntryJson");
                    const rank = node.closest("[data-tree-rank]");
                    const entryId = node.dataset.treeNode || "";
                    tree.querySelectorAll(`[data-tree-edge][data-from-node="${entryId}"], [data-tree-edge][data-to-node="${entryId}"]`)
                        .forEach(edge => edge.remove());
                    tree.querySelectorAll(`option[value="${entryId}"]`).forEach(option => option.remove());
                    node.remove();
                    clearEntrySelection();
                    rebuildFolderState();
                    layoutRank(rank);
                    refreshLayoutPresentation();
                } catch (error) {
                    setInspectorStatus("failed", error.message);
                }
            };
            remove();
        });

        edgeUi.createForm?.addEventListener("submit", event => {
            event.preventDefault();
            if (!pendingDependent || !pendingPrerequisite) return;
            const dependentId = pendingDependent.dataset.treeNode || "";
            const prerequisiteId = pendingPrerequisite.dataset.treeNode || "";
            const save = async () => {
                setTransientStatus(edgeUi.status, "Saving\u2026");
                try {
                    await postFormJson(edgeUi.createForm, "AddPrerequisiteJson");
                    addPrerequisiteEdge(prerequisiteId, dependentId);
                    clearEdgeState(false);
                    setTransientStatus(edgeUi.status, "Saved", "success");
                    drawConnectors();
                } catch (error) {
                    setTransientStatus(edgeUi.status, `Save failed \u2014 ${error.message}`, "danger");
                }
            };
            save();
        });

        edgeUi.removeForm?.addEventListener("submit", event => {
            event.preventDefault();
            if (!selectedEdge || !window.confirm("Remove this prerequisite edge?")) return;
            const { from: prerequisiteId, to: dependentId } = selectedEdge;
            const remove = async () => {
                setTransientStatus(edgeUi.status, "Saving\u2026");
                try {
                    await postFormJson(edgeUi.removeForm, "RemovePrerequisiteJson");
                    removePrerequisiteEdge(prerequisiteId, dependentId);
                    clearEdgeState(false);
                    setTransientStatus(edgeUi.status, "Saved", "success");
                    drawConnectors();
                } catch (error) {
                    setTransientStatus(edgeUi.status, `Save failed \u2014 ${error.message}`, "danger");
                }
            };
            remove();
        });

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
        focusToggle?.addEventListener("click", () => {
            const active = tree.classList.toggle("is-focus-mode");
            document.body.classList.toggle("visual-tree-focus-active", active);
            focusToggle.setAttribute("aria-pressed", active.toString());
            focusToggle.textContent = active ? "Exit focus" : "Focus mode";
            requestAnimationFrame(() => applyScale(scale));
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
            if (event.key === "Escape") {
                if (dragState) {
                    event.preventDefault();
                    dragState.positions.forEach((pos, n) => setNodeCoordinates(n, pos.column, pos.row));
                    selectedNodeList().forEach(selected => {
                        selected.classList.remove("is-dragging");
                        selected.style.removeProperty("transform");
                    });
                    tree.querySelectorAll(".visual-tree-placement-preview").forEach(preview => preview.remove());
                    if (dragState.node && dragState.pointerId && dragState.node.hasPointerCapture(dragState.pointerId)) {
                        dragState.node.releasePointerCapture(dragState.pointerId);
                    }
                    dragState = null;
                    suppressNodeClick = true;
                    refreshLayoutPresentation();
                    drawConnectors();
                    return;
                }
                if (isEdgeMode()) {
                    deactivateEdgeMode();
                    return;
                }
                if (isEditing() && selectedNode) {
                    if (!confirmDiscardChanges()) return;
                    if (isLayoutDirty) restoreLayoutPreview();
                    restoreSelectedPreview();
                    clearEntrySelection();
                    return;
                }
            }

            if (!isEditing()) return;

            const isInputTarget = event.target instanceof HTMLElement &&
                (event.target.isContentEditable || event.target.matches("input, select, textarea"));

            if (event.ctrlKey || event.metaKey) {
                if (!isInputTarget) {
                    if ((event.key === "z" || event.key === "Z") && !event.shiftKey) {
                        event.preventDefault();
                        undo();
                        return;
                    }
                    if ((event.key === "y" || event.key === "Y") || ((event.key === "z" || event.key === "Z") && event.shiftKey)) {
                        event.preventDefault();
                        redo();
                        return;
                    }
                }
            }

            if ((event.key === "Delete" || event.key === "Backspace") && !isInputTarget && selectedNode) {
                event.preventDefault();
                const removeBtn = removeForm?.querySelector("button[type='submit']");
                if (removeBtn && !removeForm.classList.contains("d-none")) {
                    removeBtn.click();
                }
                return;
            }

            if (event.altKey || event.ctrlKey || event.metaKey || !["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown"].includes(event.key)) return;
            const target = event.target;
            if (target instanceof HTMLElement && (target.isContentEditable || target.matches("input, select, textarea, button"))) return;
            if (!selectedNode) return;

            event.preventDefault();
            const delta = { column: 0, row: 0 };
            if (event.key === "ArrowLeft") delta.column--;
            if (event.key === "ArrowRight") delta.column++;
            if (event.key === "ArrowUp") delta.row--;
            if (event.key === "ArrowDown") delta.row++;

            if (selectedNodes.size > 1) {
                const nodes = selectedNodeList();
                if (!nodes.every(n => {
                    const pos = nodePosition(n);
                    return pos.column + delta.column >= 0 && pos.row + delta.row >= 0;
                })) return;
                const fromSnapshots = nodes.map(n => ({ node: n, from: nodePosition(n) }));
                moveSelectedNodes(nodes.map(node => {
                    const current = nodePosition(node);
                    return { node, column: current.column + delta.column, row: current.row + delta.row };
                }));
                pushHistory("Arrow nudge", fromSnapshots.map(s => ({ node: s.node, from: s.from, to: nodePosition(s.node) })));
            } else {
                const current = nodePosition(selectedNode);
                const targetCol = current.column + delta.column;
                const targetRow = current.row + delta.row;
                if (targetCol < 0 || targetRow < 0) return;
                const from = { ...current };
                setCoordinates(targetCol, targetRow);
                pushHistory("Arrow nudge", [{ node: selectedNode, from, to: { column: targetCol, row: targetRow } }]);
            }
        });

        const initValidationJumpLinks = () => {
            const messages = document.querySelectorAll("[data-validation-message]");
            if (messages.length === 0) return;
            const nodes = Array.from(tree.querySelectorAll("[data-tree-node]"));
            messages.forEach(msgEl => {
                if (msgEl.dataset.jumpInitialized === "true") return;
                msgEl.dataset.jumpInitialized = "true";
                const text = msgEl.textContent || "";
                nodes.forEach(node => {
                    const name = node.dataset.vehicleName;
                    if (name && text.includes(name)) {
                        const jumpBtn = document.createElement("button");
                        jumpBtn.type = "button";
                        jumpBtn.className = "btn btn-xs btn-outline-danger ms-2 py-0 px-2";
                        jumpBtn.dataset.jumpToNode = node.dataset.treeNode || "";
                        jumpBtn.textContent = `Jump to ${name}`;
                        jumpBtn.title = `Focus ${name} on visual tree`;
                        msgEl.appendChild(jumpBtn);
                    }
                });
            });
        };

        document.addEventListener("click", event => {
            const jumpBtn = event.target.closest("[data-jump-to-node]");
            if (jumpBtn) {
                event.preventDefault();
                jumpToNode(jumpBtn.dataset.jumpToNode);
            }
        });

        window.addEventListener("beforeunload", event => {
            if ((!isDirty && !isLayoutDirty) || isSubmitting) return;
            event.preventDefault();
            event.returnValue = "";
        });

        if (stage && typeof ResizeObserver !== "undefined") {
            new ResizeObserver(() => applyScale(scale)).observe(stage);
        }
        document.addEventListener("shown.bs.tab", drawConnectors);
        refreshOccupiedCells();
        refreshLayoutTools();
        updateUndoRedoControls();
        initValidationJumpLinks();
        applyScale(1);

        if (!canEdit) {
            initPlayerProgression();
        }

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
