window.lumaTaskAttachments = (() => {
    const bindings = new Map();
    const markdownBindings = new Map();
    const viewerBindings = new Map();

    function initialize(zoneId, inputId) {
        const zone = document.getElementById(zoneId);
        const input = document.getElementById(inputId);
        if (!zone || !input || bindings.has(zoneId)) return;

        const setFiles = files => {
            if (!files || files.length === 0) return;
            const transfer = new DataTransfer();
            for (const file of files) transfer.items.add(file);
            input.files = transfer.files;
            input.dispatchEvent(new Event('change', { bubbles: true }));
        };
        const dragEnter = event => {
            event.preventDefault();
            zone.classList.add('dragging');
        };
        const dragLeave = event => {
            if (!zone.contains(event.relatedTarget)) zone.classList.remove('dragging');
        };
        const drop = event => {
            event.preventDefault();
            zone.classList.remove('dragging');
            setFiles(event.dataTransfer?.files);
        };
        const paste = event => setFiles(event.clipboardData?.files);

        zone.addEventListener('dragenter', dragEnter);
        zone.addEventListener('dragover', dragEnter);
        zone.addEventListener('dragleave', dragLeave);
        zone.addEventListener('drop', drop);
        zone.addEventListener('paste', paste);
        bindings.set(zoneId, { zone, dragEnter, dragLeave, drop, paste });
    }

    function dispose(zoneId) {
        const binding = bindings.get(zoneId);
        if (!binding) return;
        binding.zone.removeEventListener('dragenter', binding.dragEnter);
        binding.zone.removeEventListener('dragover', binding.dragEnter);
        binding.zone.removeEventListener('dragleave', binding.dragLeave);
        binding.zone.removeEventListener('drop', binding.drop);
        binding.zone.removeEventListener('paste', binding.paste);
        bindings.delete(zoneId);
    }

    function initializeMarkdownEditor(editorId, inputId) {
        const editor = document.getElementById(editorId);
        const input = document.getElementById(inputId);
        if (!editor || !input || markdownBindings.has(editorId)) return;

        const binding = { editor, input, insertionStart: editor.selectionStart ?? editor.value.length };
        const rememberInsertion = () => {
            binding.insertionStart = editor.selectionStart ?? editor.value.length;
        };
        const sendFiles = files => {
            const images = Array.from(files ?? []).filter(file => file.type?.startsWith("image/"));
            if (images.length === 0) return false;
            rememberInsertion();
            const transfer = new DataTransfer();
            images.forEach(file => transfer.items.add(file));
            input.files = transfer.files;
            input.dispatchEvent(new Event("change", { bubbles: true }));
            return true;
        };
        const paste = event => {
            if (sendFiles(event.clipboardData?.files)) event.preventDefault();
        };
        const dragOver = event => {
            if (Array.from(event.dataTransfer?.items ?? []).some(item => item.kind === "file"))
                event.preventDefault();
        };
        const drop = event => {
            if (sendFiles(event.dataTransfer?.files)) event.preventDefault();
        };

        editor.addEventListener("click", rememberInsertion);
        editor.addEventListener("keyup", rememberInsertion);
        editor.addEventListener("select", rememberInsertion);
        editor.addEventListener("paste", paste);
        editor.addEventListener("dragover", dragOver);
        editor.addEventListener("drop", drop);
        Object.assign(binding, { rememberInsertion, paste, dragOver, drop });
        markdownBindings.set(editorId, binding);
    }

    function chooseMarkdownImage(editorId, inputId) {
        const binding = markdownBindings.get(editorId);
        const input = document.getElementById(inputId);
        if (!binding || !input) return;
        binding.insertionStart = binding.editor.selectionStart ?? binding.editor.value.length;
        input.click();
    }

    function insertMarkdownImages(editorId, markdown) {
        const binding = markdownBindings.get(editorId);
        if (!binding || !markdown) return;
        const editor = binding.editor;
        const position = Math.min(binding.insertionStart ?? editor.value.length, editor.value.length);
        const before = editor.value.slice(0, position);
        const after = editor.value.slice(position);
        const leading = before.length > 0 && !before.endsWith("\n") ? "\n\n" : "";
        const trailing = after.length > 0 && !after.startsWith("\n") ? "\n\n" : "\n";
        const inserted = `${leading}${markdown}${trailing}`;
        editor.value = `${before}${inserted}${after}`;
        const caret = position + inserted.length;
        editor.focus();
        editor.setSelectionRange(caret, caret);
        binding.insertionStart = caret;
        editor.dispatchEvent(new Event("input", { bubbles: true }));
    }

    function disposeMarkdownEditor(editorId) {
        const binding = markdownBindings.get(editorId);
        if (!binding) return;
        binding.editor.removeEventListener("click", binding.rememberInsertion);
        binding.editor.removeEventListener("keyup", binding.rememberInsertion);
        binding.editor.removeEventListener("select", binding.rememberInsertion);
        binding.editor.removeEventListener("paste", binding.paste);
        binding.editor.removeEventListener("dragover", binding.dragOver);
        binding.editor.removeEventListener("drop", binding.drop);
        markdownBindings.delete(editorId);
    }

    function initializeMarkdownViewer(containerId, dotNetReference) {
        const container = document.getElementById(containerId);
        if (!container) return;
        disposeMarkdownViewer(containerId);

        const open = event => {
            const image = event.target.closest?.("img");
            if (!image || !container.contains(image)) return;
            event.preventDefault();
            dotNetReference.invokeMethodAsync(
                "OpenMarkdownImage",
                image.currentSrc || image.src,
                image.alt || "Task image");
        };
        const keydown = event => {
            if (event.key !== "Enter" && event.key !== " ") return;
            const image = event.target.closest?.("img");
            if (!image || !container.contains(image)) return;
            open(event);
        };
        container.querySelectorAll("img").forEach(image => {
            image.tabIndex = 0;
            image.setAttribute("role", "button");
            image.setAttribute("aria-label", `Preview ${image.alt || "task image"}`);
        });
        container.addEventListener("click", open);
        container.addEventListener("keydown", keydown);
        viewerBindings.set(containerId, { container, open, keydown, dotNetReference });
    }

    function disposeMarkdownViewer(containerId) {
        const binding = viewerBindings.get(containerId);
        if (!binding) return;
        binding.container.removeEventListener("click", binding.open);
        binding.container.removeEventListener("keydown", binding.keydown);
        viewerBindings.delete(containerId);
    }

    return {
        initialize,
        dispose,
        initializeMarkdownEditor,
        chooseMarkdownImage,
        insertMarkdownImages,
        disposeMarkdownEditor,
        initializeMarkdownViewer,
        disposeMarkdownViewer
    };
})();
