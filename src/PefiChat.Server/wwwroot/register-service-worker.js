if ("serviceWorker" in navigator) {
    window.addEventListener("load", () => {
        navigator.serviceWorker.register("/service-worker.js", { updateViaCache: "none" })
            .catch(error => console.error("Could not register the offline service worker.", error));
    });
}
