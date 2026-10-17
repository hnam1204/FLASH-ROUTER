window.flashRouter = {
    copyToClipboard: async function (text) {
        if (navigator.clipboard && navigator.clipboard.writeText) {
            try {
                await navigator.clipboard.writeText(text);
                return true;
            } catch (e) {
                console.warn('Clipboard write failed:', e);
            }
        }
        // Fallback
        const textarea = document.createElement('textarea');
        textarea.value = text;
        textarea.style.position = 'fixed';
        textarea.style.opacity = '0';
        document.body.appendChild(textarea);
        textarea.focus();
        textarea.select();
        try {
            document.execCommand('copy');
            document.body.removeChild(textarea);
            return true;
        } catch (e) {
            document.body.removeChild(textarea);
            return false;
        }
    },
    getTheme: function () {
        try {
            return localStorage.getItem('flashrouter_theme') || document.documentElement.getAttribute('data-theme') || 'dark';
        } catch (e) {
            return document.documentElement.getAttribute('data-theme') || 'dark';
        }
    },
    setTheme: function (theme) {
        var applied = theme === 'light' ? 'light' : 'dark';
        document.documentElement.setAttribute('data-theme', applied);
        try {
            localStorage.setItem('flashrouter_theme', applied);
        } catch (e) { }
        return applied;
    },
    toggleTheme: function () {
        var current = document.documentElement.getAttribute('data-theme') || 'dark';
        var next = current === 'dark' ? 'light' : 'dark';
        document.documentElement.setAttribute('data-theme', next);
        try {
            localStorage.setItem('flashrouter_theme', next);
        } catch (e) { }
        return next;
    },
    scrollConsole: function (elementId) {
        const element = document.getElementById(elementId);
        if (element) {
            element.scrollTop = element.scrollHeight;
        }
    },
    setupKeyboardShortcuts: function (dotNetHelper) {
        window.addEventListener('keydown', function (e) {
            if ((e.ctrlKey || e.metaKey) && (e.key === 'k' || e.key === 'K')) {
                e.preventDefault();
                const searchInput = document.querySelector('.search-input');
                if (searchInput) {
                    searchInput.focus();
                    searchInput.select();
                }
            } else if (e.key === 'Escape') {
                dotNetHelper.invokeMethodAsync('OnEscapePressed');
            }
        });
    }
};
