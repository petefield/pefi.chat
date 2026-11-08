const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const publicKey = Buffer.from([4, ...new Array(64).fill(1)]).toString("base64url");
let permissionRequests = 0;
let subscribed = 0;
let posted;
let current;
const Notification = {
    permission: "default",
    async requestPermission() { permissionRequests++; this.permission = "granted"; return "granted"; }
};
const pushManager = {
    async getSubscription() { return current; },
    async subscribe(options) {
        subscribed++;
        assert.equal(options.userVisibleOnly, true);
        assert.equal(Buffer.from(options.applicationServerKey).toString("base64url"), publicKey);
        current = { options, toJSON: () => ({ endpoint: "https://fcm.googleapis.com/test", keys: { p256dh: "key", auth: "auth" } }) };
        return current;
    }
};
const window = { PushManager: {}, Notification };
const navigator = { serviceWorker: { ready: Promise.resolve({ pushManager }) } };
const fetch = async (url, options) => {
    if (url === "/api/push/public-key") return { ok: true, json: async () => ({ publicKey }) };
    assert.equal(url, "/api/push/subscribe");
    posted = options;
    return { ok: true };
};
vm.runInNewContext(fs.readFileSync(path.resolve(__dirname, "../../src/PefiChat.Server/wwwroot/push.js"), "utf8"),
    { window, navigator, Notification, fetch, Uint8Array, setTimeout, clearTimeout, atob: value => Buffer.from(value, "base64").toString("binary") });

(async () => {
    assert.equal(await window.pefiPush.subscribe("token", false), "not-enabled");
    assert.equal(permissionRequests, 0);
    assert.equal(await window.pefiPush.subscribe("token", true), "subscribed");
    assert.equal(permissionRequests, 1);
    assert.equal(subscribed, 1);
    assert.equal(posted.headers["X-CSRF-TOKEN"], "token");
    assert.equal(JSON.parse(posted.body).endpoint, "https://fcm.googleapis.com/test");
    await window.pefiPush.subscribe("token", false);
    assert.equal(subscribed, 1, "an existing subscription is reused");
    let unsubscribed = false;
    current = { options: { applicationServerKey: Uint8Array.of(1) }, async unsubscribe() { unsubscribed = true; return true; } };
    await window.pefiPush.subscribe("token", false);
    assert.ok(unsubscribed);
    assert.equal(subscribed, 2, "a subscription with an old VAPID key is replaced");
    console.log("PASS: push permission, subscription, ownership refresh and key rotation");
})().catch(error => { console.error(error); process.exitCode = 1; });
