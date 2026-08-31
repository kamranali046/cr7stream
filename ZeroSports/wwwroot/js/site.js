// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

(function () {
    var btn = document.getElementById('themeToggle');
    if (!btn) return;

    var SUN = '☀'; // ☀
    var MOON = '☾'; // ☾

    function apply(theme) {
        if (theme === 'light') {
            document.documentElement.setAttribute('data-theme', 'light');
        } else {
            document.documentElement.removeAttribute('data-theme');
        }
    }

    var icon = document.getElementById('themeIcon');

    var saved = localStorage.getItem('theme');
    apply(saved);
    if (icon) icon.textContent = saved === 'light' ? MOON : SUN;

    btn.addEventListener('click', function () {
        var isLight = document.documentElement.getAttribute('data-theme') === 'light';
        var next = isLight ? 'dark' : 'light';
        apply(next);
        localStorage.setItem('theme', next);
        if (icon) icon.textContent = next === 'light' ? MOON : SUN;
    });
})();
