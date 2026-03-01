window.sectionObserver = {
    _observer: null,
    _dotnetRef: null,

    init: function (dotnetRef) {
        this.dispose();
        this._dotnetRef = dotnetRef;

        const targets = document.querySelectorAll('.nav-target');
        if (targets.length === 0) return;

        this._observer = new IntersectionObserver(
            (entries) => {
                // Find the topmost visible section
                let best = null;
                for (const entry of entries) {
                    if (entry.isIntersecting) {
                        if (!best || entry.boundingClientRect.top < best.boundingClientRect.top) {
                            best = entry;
                        }
                    }
                }
                if (best) {
                    this._dotnetRef.invokeMethodAsync('OnSectionVisible', best.target.id);
                }
            },
            { rootMargin: '-48px 0px -60% 0px', threshold: 0 }
        );

        targets.forEach(t => this._observer.observe(t));
    },

    dispose: function () {
        if (this._observer) {
            this._observer.disconnect();
            this._observer = null;
        }
        this._dotnetRef = null;
    }
};
