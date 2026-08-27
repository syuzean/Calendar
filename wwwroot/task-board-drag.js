(() => {
    let dotNetReference;
    let draggedCard;
    let activeColumn;
    let invalidColumn;
    let suppressClickUntil = 0;

    const closestElement = (event, selector) =>
        event.target instanceof Element ? event.target.closest(selector) : null;

    const isForwardTransition = (source, target) =>
        (source === "ToDo" && target === "InProgress") ||
        (source === "InProgress" && target === "Done");

    const clearVisualState = () => {
        draggedCard?.classList.remove("dragging");
        activeColumn?.classList.remove("drop-target");
        invalidColumn?.classList.remove("invalid-drop-target");
        draggedCard = undefined;
        activeColumn = undefined;
        invalidColumn = undefined;
    };

    document.addEventListener("dragstart", event => {
        const card = closestElement(event, "[data-task-drag][draggable='true']");
        if (!card || !event.dataTransfer) return;

        draggedCard = card;
        card.classList.add("dragging");
        event.dataTransfer.setData("text/plain", card.dataset.taskDrag || "");
        event.dataTransfer.effectAllowed = "move";
    });

    document.addEventListener("dragover", event => {
        const column = closestElement(event, "[data-task-drop]");
        if (!draggedCard || !column || !event.dataTransfer) return;

        event.preventDefault();
        const valid = isForwardTransition(draggedCard.dataset.workStatus, column.dataset.taskDrop);
        event.dataTransfer.dropEffect = valid ? "move" : "none";

        if (valid) {
            invalidColumn?.classList.remove("invalid-drop-target");
            invalidColumn = undefined;
            if (activeColumn !== column) {
                activeColumn?.classList.remove("drop-target");
                activeColumn = column;
                column.classList.add("drop-target");
            }
        } else {
            activeColumn?.classList.remove("drop-target");
            activeColumn = undefined;
            if (invalidColumn !== column) {
                invalidColumn?.classList.remove("invalid-drop-target");
                invalidColumn = column;
            }
        }
    });

    document.addEventListener("drop", event => {
        const column = closestElement(event, "[data-task-drop]");
        if (!draggedCard || !column) return;

        event.preventDefault();
        event.stopPropagation();
        const taskId = draggedCard.dataset.taskDrag;
        const version = draggedCard.dataset.taskVersion;
        const source = draggedCard.dataset.workStatus;
        const target = column.dataset.taskDrop;
        const valid = isForwardTransition(source, target);
        suppressClickUntil = performance.now() + 350;
        clearVisualState();

        if (!dotNetReference) return;
        const method = valid ? "HandleTaskBoardDrop" : "HandleInvalidTaskBoardDrop";
        const args = valid ? [taskId, target, version] : [];
        dotNetReference.invokeMethodAsync(method, ...args)
            .catch(error => console.error("Luma task Board drop failed.", error));
    });

    document.addEventListener("dragend", () => {
        if (draggedCard) suppressClickUntil = performance.now() + 350;
        clearVisualState();
    });

    document.addEventListener("click", event => {
        if (performance.now() >= suppressClickUntil || !closestElement(event, "[data-task-board-card]")) return;
        event.preventDefault();
        event.stopImmediatePropagation();
    }, true);

    window.lumaTaskBoardDrag = {
        initialize(reference) {
            dotNetReference = reference;
        },
        dispose() {
            dotNetReference = undefined;
            clearVisualState();
        }
    };
})();
