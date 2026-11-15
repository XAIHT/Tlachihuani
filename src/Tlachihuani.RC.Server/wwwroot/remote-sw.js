/* Shell only: never cache API responses, credentials or conversation content. */
const CACHE='tlachihuani-rc-shell-v8';
const SHELL=['/remote.html','/remote.js','/remote.css','/remote-response.js','/mobile-download.js','/remote-response.css','/vendor/purify.min.js','/remote.webmanifest','/icons/xt-192.png','/icons/xt-512.png'];
self.addEventListener('install',event=>event.waitUntil(caches.open(CACHE).then(cache=>cache.addAll(SHELL))));
self.addEventListener('activate',event=>event.waitUntil(caches.keys().then(keys=>Promise.all(keys.filter(key=>key.startsWith('tlachihuani-rc-shell-')&&key!==CACHE).map(key=>caches.delete(key))))));
self.addEventListener('fetch',event=>{const url=new URL(event.request.url);if(event.request.method!=='GET'||url.origin!==location.origin||url.search||!SHELL.includes(url.pathname))return;event.respondWith(fetch(event.request).catch(()=>caches.match(event.request)));});
