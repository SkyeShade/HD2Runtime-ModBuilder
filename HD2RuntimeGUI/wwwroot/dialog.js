window.builderDialog = {
    focus(element) {
        if (!this.previousFocus) this.previousFocus = document.activeElement;
        const selector = 'button:not(:disabled),input:not(:disabled),select:not(:disabled),textarea:not(:disabled),[tabindex="0"]';
        (element.querySelector(selector) || element).focus();
        element.onkeydown = event => {
            if (event.key !== 'Tab') return;
            const items = [...element.querySelectorAll(selector)].filter(item => item.offsetParent !== null);
            if (!items.length) { event.preventDefault(); element.focus(); return; }
            const first = items[0], last = items[items.length - 1];
            if (event.shiftKey && (document.activeElement === first || document.activeElement === element)) { event.preventDefault(); last.focus(); }
            else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
        };
    },
    restore() {
        if (this.previousFocus?.isConnected) this.previousFocus.focus();
        this.previousFocus = null;
    }
};
// Brings an element into view after a page change (the weapon attack a custom projectile was created for, for example).
window.builderView = {
    reveal(selector) { document.querySelector(selector)?.scrollIntoView({ block: 'center' }); },
    // A section of a long editor: opened when it is a collapsed <details>, then scrolled to its top.
    jump(selector) {
        const el = document.querySelector(selector); if (!el) return;
        if (el.tagName === 'DETAILS') el.open = true;
        el.scrollIntoView({ block: 'start', behavior: 'smooth' });
    }
};
