importScripts("/emoji-precache.js");
const VERSION = "lantern-web-1.2.20";
const ASSETS = [
  "/avatars.js", "/emoji.js", "/emoji-data.js", "/emoji-precache.js", ...self.LANTERN_EMOJI_FILES,
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
  "/calculator-lock.js",
  "/calculator-fit.js",
  "/camouflage.js",
  "/calculator.css",
  "/vendor/jsQR.js",
  "/icon.svg",
  "/icon-180.png", "/icon-192.png",
  "/icon-512.png",
  "/manifest.webmanifest",
];
self.addEventListener("install", (event) =>
  event.waitUntil(caches.open(VERSION).then(async (cache) => {
    // Bound concurrent requests while filling the complete offline emoji library.
    for(let start=0;start<ASSETS.length;start+=12) await cache.addAll(ASSETS.slice(start,start+12));
  })),
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
