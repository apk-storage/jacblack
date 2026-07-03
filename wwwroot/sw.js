/* JacRed — PWA cache (static + offline shell) */
'use strict';

/* Inter, Bootstrap, Icons, GSAP — CDN (jsDelivr); не в precache.
   Offline: shell (offline.html) без CDN; index/stats требуют сеть для vendor.
   opensearch.xml is served by the backend and is not in static precache. */

const CACHE_NAME = 'jacred-static-v17';
const OFFLINE_URL = './offline.html';

const PRECACHE = [
  './',
  './stats',
  OFFLINE_URL,
  './css/styles.css',
  './js/common.js',
  './js/theme.js',
  './js/pwa.js',
  './js/animations.js',
  './js/app.js',
  './js/stats.js',
  './manifest.json',
  './img/jacred.png',
  './img/favicon.ico',
  './img/icon-192.png',
  './img/icon-512.png'
];

const isStaticAsset = (url) => {
  const p = url.pathname;
  return p.startsWith('/css/') || p.startsWith('/js/') || p.startsWith('/img/') ||
    /\.(css|js|png|ico|json|woff2?)$/i.test(p);
};

const isNavigation = (request) => request.mode === 'navigate';

const precacheEntries = async (cache) => {
  const results = await Promise.allSettled(PRECACHE.map((url) => cache.add(url)));
  const failed = [];
  results.forEach((result, index) => {
    if (result.status === 'rejected') {
      console.error('[SW] precache failed:', PRECACHE[index], result.reason);
      failed.push(PRECACHE[index]);
    }
  });
  return failed;
};

self.addEventListener('install', (event) => {
  event.waitUntil(
    (async () => {
      const cache = await caches.open(CACHE_NAME);
      const failed = await precacheEntries(cache);
      if (failed.length === PRECACHE.length) {
        console.error('[SW] install aborted: all precache entries failed');
        return;
      }
      if (failed.length) {
        console.warn('[SW] partial precache failure:', failed.length, 'of', PRECACHE.length);
      }
      await self.skipWaiting();
    })()
  );
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys()
      .then((keys) => Promise.all(keys.filter((k) => k !== CACHE_NAME).map((k) => caches.delete(k))))
      .then(() => self.clients.claim())
  );
});

const serveOfflineFallback = async () => {
  const offline = await caches.match(OFFLINE_URL);
  if (offline) return offline;
  return new Response('Offline', { status: 503, statusText: 'Offline' });
};

self.addEventListener('fetch', (event) => {
  if (event.request.method !== 'GET') return;
  const url = new URL(event.request.url);
  if (url.origin !== self.location.origin) return;
  if (url.pathname.startsWith('/api/')) return;

  if (isNavigation(event.request)) {
    event.respondWith(
      fetch(event.request)
        .then((res) => {
          if (res.ok) {
            const clone = res.clone();
            caches.open(CACHE_NAME).then((c) => c.put(event.request, clone));
          }
          return res;
        })
        .catch(async () => {
          const cached = await caches.match(event.request);
          if (cached) return cached;
          return serveOfflineFallback();
        })
    );
    return;
  }

  if (isStaticAsset(url)) {
    event.respondWith(
      caches.match(event.request).then((cached) => {
        const network = fetch(event.request).then((res) => {
          if (res.ok) {
            const clone = res.clone();
            caches.open(CACHE_NAME).then((c) => c.put(event.request, clone));
          }
          return res;
        }).catch(() => cached);
        return cached || network;
      })
    );
    return;
  }

  event.respondWith(
    fetch(event.request).catch(() => caches.match(event.request))
  );
});
