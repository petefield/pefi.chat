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
        url.origin !== self.location.origin || (url.pathname !== "/" && !url.pathname.startsWith("/chat/"))) {
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

self.addEventListener("push", event => {
    if (!event.data) return;
    event.waitUntil((async () => {
        const payload = event.data.json();
        await self.registration.showNotification(payload.title, {
            body: payload.body,
            icon: payload.icon,
            tag: payload.tag,
            data: { url: payload.url }
        });
    })());
});

self.addEventListener("notificationclick", event => {
    event.notification.close();
    event.waitUntil((async () => {
        const target = new URL(event.notification.data?.url || "/", self.location.origin);
        if (target.origin !== self.location.origin) return;
        const windows = await self.clients.matchAll({ type: "window", includeUncontrolled: true });
        for (const client of windows) {
            if (new URL(client.url).origin === target.origin && "navigate" in client) {
                const navigated = await client.navigate(target.href);
                if (navigated) return navigated.focus();
            }
        }
        await self.clients.openWindow(target.href);
    })());
});
