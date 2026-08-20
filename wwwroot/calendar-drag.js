(() => {
    let dotNetReference;
    let draggedElement;
    let activeDropTarget;

    function closestElement(event, selector) {
        return event.target instanceof Element ? event.target.closest(selector) : null;
    }

    function clearVisualState() {
        draggedElement?.classList.remove("dragging");
        activeDropTarget?.classList.remove("drop-target");
        draggedElement = undefined;
        activeDropTarget = undefined;
    }

    document.addEventListener("dragstart", event => {
        const source = closestElement(event, "[data-calendar-event][draggable='true']");
        if (!source || !event.dataTransfer) return;

        draggedElement = source;
        source.classList.add("dragging");
        event.dataTransfer.setData("text/plain", source.dataset.calendarEvent);
        event.dataTransfer.effectAllowed = "copyMove";
    });

    document.addEventListener("dragover", event => {
        const target = closestElement(event, "[data-calendar-drop]");
        if (!draggedElement || !target || !event.dataTransfer) return;

        event.preventDefault();
        event.dataTransfer.dropEffect = event.ctrlKey ? "copy" : "move";
        if (activeDropTarget !== target) {
            activeDropTarget?.classList.remove("drop-target");
            activeDropTarget = target;
            target.classList.add("drop-target");
        }
    });

    document.addEventListener("drop", event => {
        const target = closestElement(event, "[data-calendar-drop]");
        if (!target || !event.dataTransfer) return;

        event.preventDefault();
        event.stopPropagation();
        const eventId = event.dataTransfer.getData("text/plain") || draggedElement?.dataset.calendarEvent;
        const targetValue = target.dataset.dropValue;
        const targetKind = target.dataset.calendarDrop;
        const copy = event.ctrlKey;
        clearVisualState();

        if (!dotNetReference || !eventId || !targetValue || !targetKind) {
            console.error("Luma calendar drop was missing its event, destination, or .NET callback.");
            return;
        }

        dotNetReference.invokeMethodAsync("HandleCalendarDrop", eventId, targetValue, targetKind, copy)
            .catch(error => console.error("Luma calendar drop failed before reaching the event operation.", error));
    });

    document.addEventListener("dragend", clearVisualState);

    window.lumaCalendarDrag = {
        initialize(reference) {
            dotNetReference = reference;
        }
    };
})();
