window.lumaTaskAttachments = (() => {
    const bindings = new Map();

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

    return { initialize, dispose };
})();
