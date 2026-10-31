import { chromium } from "playwright";
import { fileURLToPath } from "url";
import path from "path";
import fs from "fs";

const dir = path.dirname(fileURLToPath(import.meta.url));
const src = path.join(dir, "src");

const pages = [
  { html: "01-main-route-comparison.html", png: "01-main-route-comparison.png" },
  { html: "02-main-destination-picker.html", png: "02-main-destination-picker.png" },
  { html: "03-main-diagnostics-slot-value.html", png: "03-main-diagnostics-slot-value.png" },
  { html: "04-settings-travel-slots.html", png: "04-settings-travel-slots.png" },
  { html: "05-settings-display-override.html", png: "05-settings-display-override.png", fullViewport: true },
];

const browser = await chromium.launch();
const context = await browser.newContext({ deviceScaleFactor: 2 });

for (const p of pages) {
  const page = await context.newPage();
  await page.setViewportSize({ width: 1280, height: 720 });
  await page.goto("file://" + path.join(src, p.html), { waitUntil: "networkidle" });

  if (p.fullViewport) {
    await page.screenshot({ path: path.join(dir, p.png), type: "png" });
  } else {
    await page.locator(".window").first().screenshot({ path: path.join(dir, p.png), type: "png" });
  }
  await page.close();
}

await browser.close();

let total = 0;
for (const p of pages) {
  const stat = fs.statSync(path.join(dir, p.png));
  total += stat.size;
  console.log(`${p.png}: ${(stat.size / 1024).toFixed(1)} KiB`);
}
console.log(`Total: ${(total / 1024 / 1024).toFixed(2)} MiB`);
