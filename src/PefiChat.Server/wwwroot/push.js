window.pefiPush = {
    async subscribe(token, prompt) {
        if (!("serviceWorker" in navigator) || !("PushManager" in window) || !("Notification" in window))
            return prompt ? "Push notifications are not supported by this browser." : "unsupported";
        if (Notification.permission === "default" && !prompt) return "not-enabled";
        const permission = Notification.permission === "default"
            ? await Notification.requestPermission() : Notification.permission;
        if (permission !== "granted")
            return prompt ? "Notification permission was not granted." : "not-enabled";

        const response = await fetch("/api/push/public-key", { credentials: "same-origin" });
        if (!response.ok) throw new Error(`Could not load push key: ${response.status}`);
        const { publicKey } = await response.json();
        const decoded = atob(publicKey.replace(/-/g, "+").replace(/_/g, "/") + "=".repeat((4 - publicKey.length % 4) % 4));
        const key = Uint8Array.from(decoded, char => char.charCodeAt(0));
        let timer;
        let registration;
        try {
            registration = await Promise.race([
                navigator.serviceWorker.ready,
                new Promise((_, reject) => { timer = setTimeout(() => reject(new Error("Service worker registration timed out.")), 15000); })
            ]);
        } finally {
            clearTimeout(timer);
        }
        let subscription = await registration.pushManager.getSubscription();
        if (subscription && subscription.options.applicationServerKey) {
            const existing = new Uint8Array(subscription.options.applicationServerKey);
            if (existing.length !== key.length || existing.some((value, index) => value !== key[index])) {
                if (!await subscription.unsubscribe()) throw new Error("Could not replace the old push subscription.");
                subscription = null;
            }
        }
        subscription ??= await registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: key });
        const saved = await fetch("/api/push/subscribe", {
            method: "POST",
            credentials: "same-origin",
            headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token },
            body: JSON.stringify(subscription)
        });
        if (!saved.ok) throw new Error(`Could not save push subscription: ${saved.status}`);
        return "subscribed";
    }
};
