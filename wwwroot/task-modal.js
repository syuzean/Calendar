window.lumaTaskModal = (() => {
    const entries = [];
    const inertStates = new Map();
    let lockedStyles;

    const focusableSelector = [
        'a[href]',
        'button:not([disabled])',
        'input:not([disabled]):not([type="hidden"])',
        'select:not([disabled])',
        'textarea:not([disabled])',
        '[tabindex]:not([tabindex="-1"])'
    ].join(',');

    function activeEntry() {
        return entries.length === 0 ? null : entries[entries.length - 1];
    }

    function visibleFocusableElements(modal) {
        return [...modal.querySelectorAll(focusableSelector)].filter(element => {
            const style = getComputedStyle(element);
            return !element.hidden && style.display !== 'none' && style.visibility !== 'hidden' &&
                element.getClientRects().length > 0;
        });
    }

    function focusInside(entry, preferLast = false) {
        const elements = visibleFocusableElements(entry.modal);
        const target = preferLast ? elements[elements.length - 1] :
            entry.modal.querySelector('[autofocus]') ?? elements[0] ?? entry.modal;
        target.focus({ preventScroll: true });
    }

    function rememberAndSetInert(element) {
        if (!inertStates.has(element)) inertStates.set(element, element.inert);
        element.inert = true;
    }

    function restoreInertStates() {
        for (const [element, wasInert] of inertStates) {
            if (element.isConnected) element.inert = wasInert;
        }
        inertStates.clear();
    }

    function isolateActiveModal() {
        restoreInertStates();
        const active = activeEntry();
        if (!active) return;

        let activeBranch = active.backdrop;
        while (activeBranch.parentElement) {
            const parent = activeBranch.parentElement;
            for (const sibling of parent.children) {
                if (sibling !== activeBranch && sibling.tagName !== 'SCRIPT')
                    rememberAndSetInert(sibling);
            }
            if (parent === document.body) break;
            activeBranch = parent;
        }
    }

    function lockPage() {
        if (lockedStyles) return;
        lockedStyles = {
            bodyOverflow: document.body.style.overflow,
            bodyOverscroll: document.body.style.overscrollBehavior,
            htmlOverflow: document.documentElement.style.overflow,
            htmlOverscroll: document.documentElement.style.overscrollBehavior
        };
        document.body.style.overflow = 'hidden';
        document.body.style.overscrollBehavior = 'none';
        document.documentElement.style.overflow = 'hidden';
        document.documentElement.style.overscrollBehavior = 'none';
        document.body.classList.add('task-modal-open');
    }

    function unlockPage() {
        if (!lockedStyles) return;
        document.body.style.overflow = lockedStyles.bodyOverflow;
        document.body.style.overscrollBehavior = lockedStyles.bodyOverscroll;
        document.documentElement.style.overflow = lockedStyles.htmlOverflow;
        document.documentElement.style.overscrollBehavior = lockedStyles.htmlOverscroll;
        document.body.classList.remove('task-modal-open');
        lockedStyles = undefined;
    }

    function onDocumentKeyDown(event) {
        if (event.key !== 'Tab') return;
        const active = activeEntry();
        if (!active) return;

        const elements = visibleFocusableElements(active.modal);
        if (elements.length === 0) {
            event.preventDefault();
            active.modal.focus({ preventScroll: true });
            return;
        }

        const currentIndex = elements.indexOf(document.activeElement);
        if (event.shiftKey && currentIndex <= 0) {
            event.preventDefault();
            elements[elements.length - 1].focus({ preventScroll: true });
        } else if (!event.shiftKey && (currentIndex < 0 || currentIndex === elements.length - 1)) {
            event.preventDefault();
            elements[0].focus({ preventScroll: true });
        }
    }

    function onDocumentFocusIn(event) {
        const active = activeEntry();
        if (!active || active.modal.contains(event.target)) return;
        focusInside(active);
    }

    function scrollTarget(entry, origin, deltaY) {
        let candidate = origin instanceof Element ? origin : null;
        while (candidate && candidate !== entry.modal) {
            const style = getComputedStyle(candidate);
            const scrollable = /(auto|scroll)/.test(style.overflowY) &&
                candidate.scrollHeight > candidate.clientHeight;
            const canMove = deltaY < 0 ? candidate.scrollTop > 0 :
                candidate.scrollTop + candidate.clientHeight < candidate.scrollHeight;
            if (scrollable && canMove) return candidate;
            candidate = candidate.parentElement;
        }
        return entry.scrollRegion;
    }

    function scrollModal(entry, origin, deltaY) {
        if (!deltaY) return;
        scrollTarget(entry, origin, deltaY).scrollTop += deltaY;
    }

    function initialize(backdropId, modalId, scrollRegionId) {
        if (entries.some(entry => entry.backdrop.id === backdropId)) return;
        const backdrop = document.getElementById(backdropId);
        const modal = document.getElementById(modalId);
        const scrollRegion = document.getElementById(scrollRegionId);
        if (!backdrop || !modal || !scrollRegion) return;

        const entry = {
            backdrop,
            modal,
            scrollRegion,
            previouslyFocused: document.activeElement,
            touchY: null
        };
        entry.wheel = event => {
            if (activeEntry() !== entry) return;
            event.preventDefault();
            const scale = event.deltaMode === WheelEvent.DOM_DELTA_LINE ? 16 :
                event.deltaMode === WheelEvent.DOM_DELTA_PAGE ? entry.scrollRegion.clientHeight : 1;
            scrollModal(entry, event.target, event.deltaY * scale);
        };
        entry.touchStart = event => {
            if (activeEntry() === entry && event.touches.length === 1)
                entry.touchY = event.touches[0].clientY;
        };
        entry.touchMove = event => {
            if (activeEntry() !== entry || entry.touchY === null || event.touches.length !== 1) return;
            event.preventDefault();
            const nextY = event.touches[0].clientY;
            scrollModal(entry, event.target, entry.touchY - nextY);
            entry.touchY = nextY;
        };
        entry.touchEnd = () => { entry.touchY = null; };

        backdrop.addEventListener('wheel', entry.wheel, { passive: false });
        backdrop.addEventListener('touchstart', entry.touchStart, { passive: true });
        backdrop.addEventListener('touchmove', entry.touchMove, { passive: false });
        backdrop.addEventListener('touchend', entry.touchEnd);
        backdrop.addEventListener('touchcancel', entry.touchEnd);

        if (entries.length === 0) {
            document.addEventListener('keydown', onDocumentKeyDown, true);
            document.addEventListener('focusin', onDocumentFocusIn, true);
            lockPage();
        }
        entries.push(entry);
        isolateActiveModal();
        requestAnimationFrame(() => {
            if (activeEntry() === entry) focusInside(entry);
        });
    }

    function dispose(backdropId) {
        const index = entries.findIndex(entry => entry.backdrop.id === backdropId);
        if (index < 0) return;
        const [entry] = entries.splice(index, 1);
        entry.backdrop.removeEventListener('wheel', entry.wheel);
        entry.backdrop.removeEventListener('touchstart', entry.touchStart);
        entry.backdrop.removeEventListener('touchmove', entry.touchMove);
        entry.backdrop.removeEventListener('touchend', entry.touchEnd);
        entry.backdrop.removeEventListener('touchcancel', entry.touchEnd);

        isolateActiveModal();
        const active = activeEntry();
        if (active) {
            requestAnimationFrame(() => focusInside(active));
            return;
        }

        document.removeEventListener('keydown', onDocumentKeyDown, true);
        document.removeEventListener('focusin', onDocumentFocusIn, true);
        restoreInertStates();
        unlockPage();
        if (entry.previouslyFocused?.isConnected)
            entry.previouslyFocused.focus({ preventScroll: true });
    }

    return { initialize, dispose };
})();
