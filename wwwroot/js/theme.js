(function () {
  'use strict';
  try {
    var stored = localStorage.getItem('theme');
    var prefersDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
    var isDark = stored === 'dark' || ((!stored || stored === '') && prefersDark);
    document.documentElement.setAttribute('data-bs-theme', isDark ? 'dark' : 'light');
    document.documentElement.setAttribute('data-jr-glass', 'true');
    var meta = document.querySelector('meta[name="theme-color"]');
    if (meta) meta.setAttribute('content', isDark ? '#000000' : '#e8f0fe');
  } catch (e) {
    document.documentElement.setAttribute('data-bs-theme', 'dark');
    document.documentElement.setAttribute('data-jr-glass', 'true');
  }
})();
