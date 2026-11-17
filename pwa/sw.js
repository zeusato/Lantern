const VERSION = "lantern-web-pc-v1";
const ASSETS = [
  "/",
  "/index.html",
  "/style.css",
  "/app.js",
  "/pc.js",
  "/crypto.js",
  "/channel.js",
  "/vault.js",
  "/model.js",
  "/qr-scanner.js",
  "/calculator.js",
  "/camouflage.js",
  "/calculator.css",
  "/vendor/jsQR.js",
  "/icon.svg",
  "/icon-180.png", "/icon-192.png",
  "/icon-512.png",
  "/manifest.webmanifest",
];
self.addEventListener("install", (event) =>
  event.waitUntil(caches.open(VERSION).then((cache) => cache.addAll(ASSETS))),
);
self.addEventListener("activate", (event) =>
  event.waitUntil(
    caches
      .keys()
      .then((keys) =>
        Promise.all(
          keys
            .filter((k) => k.startsWith("lantern-web-") && k !== VERSION)
            .map((k) => caches.delete(k)),
        ),
      )
      .then(() => self.clients.claim()),
  ),
);
self.addEventListener("message", (event) => {
  if (event.data === "activate") self.skipWaiting();
});
if (location.hostname === "localhost")
  self.addEventListener("install", () => self.skipWaiting());
self.addEventListener("fetch", (event) => {
  const url = new URL(event.request.url);
  if (
    location.hostname === "localhost" ||
    event.request.method !== "GET" ||
    url.origin !== location.origin ||
    !ASSETS.includes(url.pathname)
  )
    return;
  event.respondWith(
    caches
      .open(VERSION)
      .then(
        async (cache) =>
          (await cache.match(url.pathname)) || fetch(event.request),
      ),
  );
});
