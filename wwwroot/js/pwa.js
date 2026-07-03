(function () {
  'use strict';
  if (window.Jacred) {
    if (typeof window.Jacred.registerServiceWorker === 'function') {
      window.Jacred.registerServiceWorker();
    }
    if (typeof window.Jacred.initGlobalUi === 'function') {
      if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', () => window.Jacred.initGlobalUi());
      } else {
        window.Jacred.initGlobalUi();
      }
    }
  }
})();
