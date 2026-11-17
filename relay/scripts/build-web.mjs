import { readFile, writeFile, mkdir } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";
import { createHash } from "node:crypto";
const root = resolve(dirname(fileURLToPath(import.meta.url)), "../..");
const names = [
  "index.html",
  "style.css",
  "app.js",
  "crypto.js",
  "channel.js",
  "vault.js",
  "model.js",
  "qr-scanner.js",
  "calculator.js",
  "camouflage.js",
  "calculator.css",
  "sw.js",
  "manifest.webmanifest",
  "icon.svg",
  "icon-192.png",
  "icon-512.png",
  "licenses/jsqr.txt",
];
const types = {
  html: "text/html; charset=utf-8",
  css: "text/css; charset=utf-8",
  js: "text/javascript; charset=utf-8",
  webmanifest: "application/manifest+json",
  svg: "image/svg+xml",
  png: "image/png",
  txt: "text/plain; charset=utf-8",
};
const assets = {};
for (const file of names) {
  const bytes = await readFile(resolve(root, "pwa", file));
  assets["/" + file] = {
    type: types[file.split(".").at(-1)],
    data: bytes.toString("base64"),
  };
}
assets["/"] = assets["/index.html"];
assets["/vendor/jsQR.js"] = {
  type: types.js,
  data: (
    await readFile(resolve(root, "pwa/node_modules/jsqr/dist/jsQR.js"))
  ).toString("base64"),
};
// Any application asset change creates a new offline cache, including rebuilds of one release.
const fingerprint = createHash("sha256").update(JSON.stringify(assets)).digest("hex").slice(0,16);
const serviceWorker = Buffer.from(assets["/sw.js"].data,"base64").toString("utf8").replace(/const VERSION\s*=\s*["'][^"']+["'];/,`const VERSION = "lantern-web-${fingerprint}";`);
assets["/sw.js"].data = Buffer.from(serviceWorker).toString("base64");
const code = (
  await readFile(resolve(root, "relay/worker/index.js"), "utf8")
).replace(
  "import {webAssets} from './web-assets.js';",
  "const webAssets=" + JSON.stringify(assets) + ";",
);
const output = resolve(root, "relay/dist/server");
await mkdir(output, { recursive: true });
await writeFile(resolve(output, "index.js"), code);
console.log(
  "Built mobile web + encrypted relay: " +
    Object.keys(assets).length +
    " assets",
);
