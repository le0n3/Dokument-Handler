// Theme Management - Ultra-Simple Version
(function() {
    'use strict';

    const STORAGE_KEY = 'dokument-handler-theme';

    // Get theme from storage or default to light
    function getTheme() {
        try {
            return localStorage.getItem(STORAGE_KEY) || 'light';
        } catch {
            return 'light';
        }
    }

    // Set theme in DOM and storage
    function setTheme(theme) {
        try {
            if (theme !== 'light' && theme !== 'dark') {
                theme = 'light';
            }
            document.documentElement.setAttribute('data-theme', theme);
            localStorage.setItem(STORAGE_KEY, theme);
            console.log('Theme set to:', theme);
            return theme;
        } catch (e) {
            console.error('Failed to set theme:', e);
            return theme;
        }
    }

    // Toggle between themes
    function toggleTheme() {
        const current = getTheme();
        const newTheme = current === 'dark' ? 'light' : 'dark';
        return setTheme(newTheme);
    }

    // Initialize theme immediately
    function initTheme() {
        const theme = getTheme();
        setTheme(theme);
        console.log('Theme initialized:', theme);
        return theme;
    }

    // Export functions globally
    window.getTheme = getTheme;
    window.setTheme = setTheme;
    window.toggleTheme = toggleTheme;
    window.initTheme = initTheme;

    // Auto-initialize immediately
    initTheme();

    // MutationObserver: reagiert sofort wenn Blazor data-theme beim
    // DOM-Morphing (Enhanced Navigation) vom <html>-Tag entfernt.
    var observer = new MutationObserver(function (mutations) {
        mutations.forEach(function (mutation) {
            if (mutation.attributeName === 'data-theme') {
                var current = document.documentElement.getAttribute('data-theme');
                var stored = getTheme();
                if (current !== stored) {
                    document.documentElement.setAttribute('data-theme', stored);
                }
            }
        });
    });
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });

    // Fallback: Theme nach jeder Blazor Enhanced Navigation sicherstellen
    document.addEventListener('enhancedload', function () {
        initTheme();
    });

    console.log('Theme.js loaded successfully');
})();

// Smooth Scroll
document.addEventListener('DOMContentLoaded', function() {
    // Add smooth scrolling to all links
    document.querySelectorAll('a[href^="#"]').forEach(anchor => {
        anchor.addEventListener('click', function (e) {
            e.preventDefault();
            const target = document.querySelector(this.getAttribute('href'));
            if (target) {
                target.scrollIntoView({
                    behavior: 'smooth',
                    block: 'start'
                });
            }
        });
    });
});

// Add intersection observer for fade-in animations
const observerOptions = {
    threshold: 0.1,
    rootMargin: '0px 0px -50px 0px'
};

const observer = new IntersectionObserver(function(entries) {
    entries.forEach(entry => {
        if (entry.isIntersecting) {
            entry.target.style.opacity = '1';
            entry.target.style.transform = 'translateY(0)';
        }
    });
}, observerOptions);

document.addEventListener('DOMContentLoaded', function() {
    // Observe elements with fade-in class
    document.querySelectorAll('.fade-in').forEach(el => {
        el.style.opacity = '0';
        el.style.transform = 'translateY(20px)';
        el.style.transition = 'opacity 0.6s ease, transform 0.6s ease';
        observer.observe(el);
    });
});
