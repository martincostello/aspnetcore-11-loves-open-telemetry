(function () {
    const key = 'grots.theme';
    const query = window.matchMedia('(prefers-color-scheme: dark)');

    function get() {
        try {
            const saved = localStorage.getItem(key);
            return saved === 'light' || saved === 'dark' ? saved : 'system';
        } catch {
            return 'system';
        }
    }

    function apply(preference) {
        const resolved = preference === 'system' ? (query.matches ? 'dark' : 'light') : preference;
        document.documentElement.dataset.theme = resolved;
    }

    function set(preference) {
        try {
            if (preference === 'system') {
                localStorage.removeItem(key);
            } else {
                localStorage.setItem(key, preference);
            }
        } catch {
            // Storage can be unavailable (private mode); the theme still applies for this session.
        }

        apply(preference);
    }

    query.addEventListener('change', () => apply(get()));
    apply(get());

    window.grotsTheme = { get, set };
})();
