(() => {
    let dotNetReference;
    let draggedElement;
    let activeDropTarget;
    let timeSelection;
    let selectionPreview;
    let suppressSlotClickUntil = 0;
    const minutesPerSlot = 30;
    const pixelsPerSlot = 36;

    function closestElement(event, selector) {
        return event.target instanceof Element ? event.target.closest(selector) : null;
    }

    function clearVisualState() {
        draggedElement?.classList.remove("dragging");
        activeDropTarget?.classList.remove("drop-target");
        draggedElement = undefined;
        activeDropTarget = undefined;
    }

    function parseLocalDateTime(value) {
        const parts = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})$/.exec(value);
        return parts
            ? new Date(Number(parts[1]), Number(parts[2]) - 1, Number(parts[3]), Number(parts[4]), Number(parts[5]), Number(parts[6]))
            : undefined;
    }

    function formatLocalDateTime(value) {
        const pad = part => String(part).padStart(2, "0");
        return `${value.getFullYear()}-${pad(value.getMonth() + 1)}-${pad(value.getDate())}T${pad(value.getHours())}:${pad(value.getMinutes())}:00`;
    }

    function formatTime(value) {
        return value.toLocaleTimeString([], { hour: "numeric", minute: "2-digit" });
    }

    function selectedBoundary(event, slot, allowDayEnd) {
        const slotIndex = Number(slot.dataset.slotIndex);
        if (!Number.isInteger(slotIndex)) return undefined;
        const bounds = slot.getBoundingClientRect();
        const nearestIndex = slotIndex + (event.clientY >= bounds.top + bounds.height / 2 ? 1 : 0);
        return Math.max(0, Math.min(allowDayEnd ? 48 : 47, nearestIndex));
    }

    function selectionRange() {
        const anchor = timeSelection.anchorIndex;
        const current = timeSelection.currentIndex;
        if (!timeSelection.moved || anchor === current)
            return { startIndex: anchor, endIndex: anchor + 2 };
        return { startIndex: Math.min(anchor, current), endIndex: Math.max(anchor, current) };
    }

    function selectionDate(slotIndex) {
        const value = new Date(timeSelection.day);
        value.setMinutes(value.getMinutes() + slotIndex * minutesPerSlot);
        return value;
    }

    function updateSelectionPreview() {
        if (!timeSelection || !selectionPreview) return;
        const range = selectionRange();
        const visibleEnd = Math.min(48, range.endIndex);
        selectionPreview.style.top = `${range.startIndex * pixelsPerSlot}px`;
        selectionPreview.style.height = `${Math.max(pixelsPerSlot, (visibleEnd - range.startIndex) * pixelsPerSlot)}px`;
        selectionPreview.querySelector("span").textContent = `${formatTime(selectionDate(range.startIndex))} – ${formatTime(selectionDate(range.endIndex))}`;
    }

    function clearTimeSelection() {
        selectionPreview?.remove();
        selectionPreview = undefined;
        timeSelection = undefined;
    }

    document.addEventListener("pointerdown", event => {
        if ((event.pointerType !== "mouse" && event.pointerType !== "pen") || event.button !== 0) return;
        const slot = closestElement(event, ".hour-slot");
        const column = slot?.closest(".day-column[data-calendar-day]");
        if (!slot || !column || closestElement(event, "[data-calendar-event]")) return;

        const anchorIndex = selectedBoundary(event, slot, false);
        const day = parseLocalDateTime(column.dataset.calendarDay);
        if (anchorIndex === undefined || !day) return;

        event.preventDefault();
        clearTimeSelection();
        timeSelection = {
            pointerId: event.pointerId,
            column,
            day,
            anchorIndex,
            currentIndex: anchorIndex,
            startX: event.clientX,
            startY: event.clientY,
            moved: false
        };
        selectionPreview = document.createElement("div");
        selectionPreview.className = "time-selection-preview";
        selectionPreview.setAttribute("aria-hidden", "true");
        const label = document.createElement("span");
        const hint = document.createElement("small");
        hint.textContent = "Release to create";
        selectionPreview.append(label, hint);
        column.append(selectionPreview);
        updateSelectionPreview();
    }, { passive: false });

    document.addEventListener("pointermove", event => {
        if (!timeSelection || event.pointerId !== timeSelection.pointerId) return;
        const pointedElement = document.elementFromPoint(event.clientX, event.clientY);
        const slot = pointedElement instanceof Element ? pointedElement.closest(".hour-slot") : null;
        if (!slot || slot.closest(".day-column") !== timeSelection.column) return;

        const currentIndex = selectedBoundary(event, slot, true);
        if (currentIndex === undefined) return;
        timeSelection.currentIndex = currentIndex;
        timeSelection.moved ||= Math.hypot(event.clientX - timeSelection.startX, event.clientY - timeSelection.startY) > 4;
        updateSelectionPreview();
    });

    document.addEventListener("pointerup", event => {
        if (!timeSelection || event.pointerId !== timeSelection.pointerId) return;
        event.preventDefault();
        const range = selectionRange();
        const start = formatLocalDateTime(selectionDate(range.startIndex));
        const end = formatLocalDateTime(selectionDate(range.endIndex));
        clearTimeSelection();
        suppressSlotClickUntil = performance.now() + 500;

        if (!dotNetReference) {
            console.error("Luma calendar time selection was missing its .NET callback.");
            return;
        }
        dotNetReference.invokeMethodAsync("HandleCalendarTimeSelection", start, end)
            .catch(error => console.error("Luma calendar time selection failed before opening the event editor.", error));
    }, { passive: false });

    document.addEventListener("pointercancel", clearTimeSelection);
    document.addEventListener("click", event => {
        if (event.detail > 0 && performance.now() < suppressSlotClickUntil && closestElement(event, ".hour-slot")) {
            event.preventDefault();
            event.stopImmediatePropagation();
        }
    }, true);

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
        },
        scrollToTime(element, top) {
            if (!(element instanceof HTMLElement)) return;
            element.scrollTo({ top: Math.max(0, Number(top) || 0), behavior: "auto" });
        }
    };
})();
