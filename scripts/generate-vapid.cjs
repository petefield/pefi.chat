const { createECDH, randomBytes } = require("node:crypto");
const { spawnSync } = require("node:child_process");
const path = require("node:path");

const project = path.resolve(__dirname, "../src/PefiChat.Server/PefiChat.csproj");
const list = spawnSync("dotnet", ["user-secrets", "list", "--project", project], { encoding: "utf8" });
if (list.error || list.status !== 0) {
    console.error("Could not read user secrets.", list.error?.message || list.stderr);
    process.exit(1);
}
if (/^Push:(PublicKey|PrivateKey|SendSecret)\s*=/m.test(list.stdout)) {
    console.error("Push secrets already exist. Refusing to replace them; rotation invalidates existing subscriptions.");
    process.exit(1);
}
const ecdh = createECDH("prime256v1");
ecdh.generateKeys();
const privateKey = Buffer.alloc(32);
const scalar = ecdh.getPrivateKey();
scalar.copy(privateKey, 32 - scalar.length);
const secrets = {
    "Push:PublicKey": ecdh.getPublicKey().toString("base64url"),
    "Push:PrivateKey": privateKey.toString("base64url"),
    "Push:SendSecret": randomBytes(32).toString("base64url")
};
const save = spawnSync("dotnet", ["user-secrets", "set", "--project", project], {
    input: JSON.stringify(secrets), encoding: "utf8"
});
if (save.error || save.status !== 0) {
    console.error("Could not save push secrets.", save.error?.message || save.stderr);
    process.exit(1);
}
console.log("Generated VAPID keys and send secret in the server's .NET user secrets.");
