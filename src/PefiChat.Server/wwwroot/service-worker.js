const cacheName = "pefi-chat-offline-v1";
const offlinePage = "/offline.html";

self.addEventListener("install", event => {
    event.waitUntil((async () => {
        const cache = await caches.open(cacheName);
        await cache.add(new Request(offlinePage, { cache: "reload" }));
        await self.skipWaiting();
    })());
});

self.addEventListener("activate", event => {
    event.waitUntil((async () => {
        const names = await caches.keys();
        await Promise.all(names
            .filter(name => name.startsWith("pefi-chat-offline-") && name !== cacheName)
            .map(name => caches.delete(name)));
        await self.clients.claim();
    })());
});

self.addEventListener("fetch", event => {
    const url = new URL(event.request.url);
    if (event.request.method !== "GET" || event.request.mode !== "navigate" ||
        url.origin !== self.location.origin || url.pathname !== "/") {
        return;
    }

    event.respondWith((async () => {
        try {
            return await fetch(event.request);
        } catch (error) {
            const cache = await caches.open(cacheName);
            const fallback = await cache.match(offlinePage);
            if (fallback) return fallback;
            throw error;
        }
    })());
});
