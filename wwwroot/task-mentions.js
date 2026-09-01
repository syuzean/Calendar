window.lumaTaskMentions = {
    getContext(textarea) {
        if (!textarea || textarea.selectionStart !== textarea.selectionEnd) return null;
        const end = textarea.selectionStart;
        const before = textarea.value.slice(0, end);
        const match = before.match(/(?:^|[\s([{])@([\p{L}\p{N}._ -]{0,50})$/u);
        if (!match) return null;

        const gap = 6;
        const edge = 12;
        const desiredWidth = 286;
        const desiredHeight = 168;
        const rect = textarea.getBoundingClientRect();
        const modal = textarea.closest(".task-modal")?.getBoundingClientRect();
        const leftBoundary = Math.max(edge, modal?.left ?? edge);
        const rightBoundary = Math.min(window.innerWidth - edge, modal?.right ?? window.innerWidth - edge);
        const topBoundary = Math.max(edge, modal?.top ?? edge);
        const bottomBoundary = Math.min(window.innerHeight - edge, modal?.bottom ?? window.innerHeight - edge);
        const width = Math.min(desiredWidth, Math.max(0, rightBoundary - leftBoundary));
        const left = Math.min(Math.max(rect.left, leftBoundary), rightBoundary - width);
        const roomBelow = Math.max(0, bottomBoundary - rect.bottom - gap);
        const roomAbove = Math.max(0, rect.top - topBoundary - gap);
        const placeAbove = roomBelow < 96 && roomAbove > roomBelow;
        const maxHeight = Math.min(desiredHeight, placeAbove ? roomAbove : roomBelow);

        return {
            start: end - match[1].length - 1,
            end,
            query: match[1],
            left,
            top: placeAbove ? rect.top - gap : rect.bottom + gap,
            width,
            maxHeight,
            placeAbove
        };
    },

    insert(textarea, start, end, token) {
        if (!textarea) return "";
        const next = `${textarea.value.slice(0, start)}${token} ${textarea.value.slice(end)}`;
        const caret = start + token.length + 1;
        textarea.value = next;
        textarea.focus();
        textarea.setSelectionRange(caret, caret);
        return next;
    }
};
