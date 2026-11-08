const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const root = path.resolve(__dirname, "../../src/PefiChat.Server/wwwroot");
const manifest = JSON.parse(fs.readFileSync(path.join(root, "manifest.webmanifest"), "utf8"));
assert.equal(manifest.start_url, "/");
assert.equal(manifest.scope, "/");
assert.equal(manifest.display, "standalone");
for (const size of [192, 512]) {
    const icon = manifest.icons.find(item => item.sizes === `${size}x${size}` && item.purpose === "any");
    assert.ok(icon, `missing ${size}px icon`);
}
assert.ok(manifest.icons.some(item => item.sizes === "512x512" && item.purpose === "maskable"));
for (const icon of manifest.icons) {
    const data = fs.readFileSync(path.join(root, icon.src));
    const [width, height] = icon.sizes.split("x").map(Number);
    assert.equal(data.subarray(0, 8).toString("hex"), "89504e470d0a1a0a");
    assert.equal(data.readUInt32BE(16), width);
    assert.equal(data.readUInt32BE(20), height);
}

const listeners = {};
const stored = new Map();
const deleted = [];
let online = true;
let claimed = false;
let skipped = false;
const cache = {
    async add(request) { stored.set(request.url, "offline-page"); },
    async match(key) { return stored.get(key); }
};
const self = {
    location: { origin: "https://chat.example" },
    addEventListener(name, callback) { listeners[name] = callback; },
    async skipWaiting() { skipped = true; },
    clients: { async claim() { claimed = true; } }
};
const caches = {
    async open(name) { assert.equal(name, "pefi-chat-offline-v1"); return cache; },
    async keys() { return ["pefi-chat-offline-v0", "pefi-chat-offline-v1", "another-app"]; },
    async delete(name) { deleted.push(name); }
};
vm.runInNewContext(fs.readFileSync(path.join(root, "service-worker.js"), "utf8"), {
    self, caches, URL, Request: class { constructor(url) { this.url = url; } },
    fetch: async () => { if (!online) throw new Error("offline"); return "online-page"; }
});

async function dispatchLifecycle(name) {
    let task;
    listeners[name]({ waitUntil(value) { task = value; } });
    await task;
}

function dispatchFetch(url, mode = "navigate", method = "GET") {
    let response;
    listeners.fetch({
        request: { url, mode, method },
        respondWith(value) { response = value; }
    });
    return response;
}

(async () => {
    await dispatchLifecycle("install");
    assert.equal(stored.get("/offline.html"), "offline-page");
    assert.equal(stored.size, 1);
    assert.ok(skipped);
    await dispatchLifecycle("activate");
    assert.deepEqual(deleted, ["pefi-chat-offline-v0"]);
    assert.ok(claimed);
    assert.equal(await dispatchFetch("https://chat.example/"), "online-page");
    online = false;
    assert.equal(await dispatchFetch("https://chat.example/"), "offline-page");
    for (const [url, mode, method] of [
        ["https://chat.example/chat-hub", "cors", "GET"],
        ["https://chat.example/profile-pictures/123", "no-cors", "GET"],
        ["https://chat.example/login/microsoft", "navigate", "GET"],
        ["https://chat.example/", "navigate", "POST"],
        ["https://other.example/", "navigate", "GET"]
    ]) assert.equal(dispatchFetch(url, mode, method), undefined);
    assert.equal(stored.size, 1, "private responses must not be cached");
    console.log("PASS: PWA manifest, icon sizes, offline fallback and network-only private requests");
})().catch(error => { console.error(error); process.exitCode = 1; });
