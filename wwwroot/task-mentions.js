window.lumaTaskMentions = {
    getContext(textarea) {
        if (!textarea || textarea.selectionStart !== textarea.selectionEnd) return null;
        const end = textarea.selectionStart;
        const before = textarea.value.slice(0, end);
        const match = before.match(/(?:^|[\s([{])@([\p{L}\p{N}._ -]{0,50})$/u);
        if (!match) return null;
        return { start: end - match[1].length - 1, end, query: match[1] };
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
