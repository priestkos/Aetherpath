import { chromium } from "playwright";
import { fileURLToPath } from "url";
import path from "path";
import fs from "fs";
import { spawnSync } from "child_process";

const dir = path.dirname(fileURLToPath(import.meta.url));
const videosDir = path.join(dir, "..");
const screenshotsSrc = path.join(dir, "..", "..", "screenshots", "src");
const outMp4 = path.join(videosDir, "mockup-walkthrough.mp4");
const outGif = path.join(videosDir, "mockup-walkthrough.gif");
const tmpDir = path.join(videosDir, ".render-tmp");

const W = 1280;
const H = 720;
const FPS = 30;

/** Fixed delays (ms) — deterministic, no wall-clock drift beyond Playwright. */
const sleep = (page, ms) => page.waitForTimeout(ms);

const captionCss = `
  #ap-caption {
    position: fixed;
    left: 0;
    right: 0;
    bottom: 0;
    z-index: 10000;
    pointer-events: none;
    font-family: "Segoe UI", system-ui, sans-serif;
  }
  #ap-caption-inner {
    margin: 0 auto 20px;
    max-width: 90%;
    text-align: center;
    background: rgba(8, 10, 14, 0.88);
    border: 1px solid #454652;
    border-radius: 6px;
    padding: 10px 20px;
    font-size: 17px;
    color: #e8e8f0;
    box-shadow: 0 4px 24px rgba(0, 0, 0, 0.45);
  }
  #ap-caption-inner strong { color: #8cd1ff; font-weight: 600; }
`;

async function injectCaption(page, title, subtitle = "") {
  await page.evaluate(
    ({ css, title, subtitle }) => {
      if (!document.getElementById("ap-caption-style")) {
        const s = document.createElement("style");
        s.id = "ap-caption-style";
        s.textContent = css;
        document.head.appendChild(s);
      }
      let el = document.getElementById("ap-caption");
      if (!el) {
        el = document.createElement("div");
        el.id = "ap-caption";
        document.body.appendChild(el);
      }
      el.innerHTML = subtitle
        ? `<div id="ap-caption-inner"><strong>${title}</strong> — ${subtitle}</div>`
        : `<div id="ap-caption-inner"><strong>${title}</strong></div>`;
    },
    { css: captionCss, title, subtitle },
  );
}

async function openMock(page, htmlName, fullPage = false) {
  const url = "file://" + path.join(screenshotsSrc, htmlName);
  await page.goto(url, { waitUntil: "networkidle" });
  if (!fullPage) {
    await page.evaluate(() => {
      document.body.style.alignItems = "center";
      document.body.style.justifyContent = "center";
      document.body.style.minHeight = "100vh";
      document.body.style.padding = "16px";
    });
  }
}

function runFfmpeg(args) {
  const r = spawnSync("ffmpeg", args, { encoding: "utf-8" });
  if (r.status !== 0) {
    console.error(r.stderr || r.stdout);
    throw new Error(`ffmpeg failed: ${args.join(" ")}`);
  }
}

const scenes = [
  {
    name: "intro",
    async run(page) {
      await page.goto("file://" + path.join(dir, "intro.html"), { waitUntil: "networkidle" });
      await sleep(page, 3500);
    },
  },
  {
    name: "route-comparison",
    async run(page) {
      await openMock(page, "01-main-route-comparison.html");
      await injectCaption(page, "Route comparison", "/aetherpath — ranked routes & slot advice");
      await sleep(page, 2200);
      await page.evaluate(() => {
        const rows = document.querySelectorAll("table.data tbody tr");
        rows.forEach((r, i) => {
          r.style.transition = "background 0.35s";
          r.style.background = i === 0 ? "rgba(140, 230, 153, 0.12)" : "";
        });
      });
      await sleep(page, 2800);
    },
  },
  {
    name: "destination-picker",
    async run(page) {
      await openMock(page, "02-main-destination-picker.html");
      await injectCaption(page, "Destination picker", "search, Zones list, live & projected costs");
      await sleep(page, 1800);
      await page.evaluate(() => {
        const zones = document.querySelector(".fake-check.checked");
        if (zones) zones.classList.remove("checked");
      });
      await sleep(page, 900);
      await page.evaluate(() => {
        const zones = document.querySelector(".row-tools .fake-check");
        if (zones) zones.classList.add("checked");
      });
      await sleep(page, 900);
      await page.evaluate(() => {
        const items = [...document.querySelectorAll(".scroll-panel .selectable")];
        items.forEach((el) => el.classList.remove("selected"));
        const pick = items.find((el) => el.textContent.includes("Costa del Sol"));
        if (pick) pick.classList.add("selected");
      });
      await sleep(page, 2200);
    },
  },
  {
    name: "diagnostics",
    async run(page) {
      await openMock(page, "03-main-diagnostics-slot-value.html");
      await injectCaption(page, "Diagnostics & slot value", "missing free transfer hint & worth table");
      await sleep(page, 3500);
    },
  },
  {
    name: "settings-travel",
    async run(page) {
      await openMock(page, "04-settings-travel-slots.html");
      await injectCaption(page, "Settings — travel slots", "/aetherpath cfg — overrides & travel modes");
      await sleep(page, 1600);
      await page.evaluate(() => {
        const read = document.querySelector(".collapse-body .fake-check.checked");
        if (read) read.classList.remove("checked");
      });
      await sleep(page, 700);
      await page.evaluate(() => {
        const read = document.querySelector(".collapse-body .fake-check");
        if (read) read.classList.add("checked");
      });
      await sleep(page, 700);
      await page.evaluate(() => {
        const ticket = document.querySelectorAll(".int-field");
        const field = ticket[ticket.length - 1];
        if (field) {
          field.textContent = "3";
          field.style.outline = "1px solid #8cd1ff";
        }
      });
      await sleep(page, 2200);
    },
  },
  {
    name: "display-override",
    async run(page) {
      await openMock(page, "05-settings-display-override.html", true);
      await injectCaption(page, "Display & override banner", "/ap alias · manual override projections");
      await sleep(page, 1800);
      await page.evaluate(() => {
        const hide = [...document.querySelectorAll(".fake-check")].find((el) =>
          el.textContent.includes("Hide routes costing 1000"),
        );
        if (hide) hide.classList.add("checked");
      });
      await sleep(page, 800);
      await page.evaluate(() => {
        const fill = document.querySelector(".slider-fill");
        const label = document.querySelector(".slider-row .muted");
        if (fill) fill.style.width = "42%";
        if (label) label.textContent = "12";
      });
      await sleep(page, 2800);
    },
  },
  {
    name: "outro",
    async run(page) {
      await page.goto("file://" + path.join(dir, "intro.html"), { waitUntil: "networkidle" });
      await injectCaption(page, "Read-only by design", "mock-ups — see README for real usage");
      await sleep(page, 2500);
    },
  },
];

fs.rmSync(tmpDir, { recursive: true, force: true });
fs.mkdirSync(tmpDir, { recursive: true });

const browser = await chromium.launch();
const context = await browser.newContext({
  viewport: { width: W, height: H },
  deviceScaleFactor: 1,
  recordVideo: { dir: tmpDir, size: { width: W, height: H } },
});

const page = await context.newPage();

for (const scene of scenes) {
  console.log(`Scene: ${scene.name}`);
  await scene.run(page);
}

const video = page.video();
await page.close();
await context.close();
await browser.close();

const webmPath = await video.path();
console.log(`Recorded: ${webmPath}`);

runFfmpeg([
  "-y",
  "-i",
  webmPath,
  "-c:v",
  "libx264",
  "-pix_fmt",
  "yuv420p",
  "-crf",
  "26",
  "-preset",
  "medium",
  "-movflags",
  "+faststart",
  outMp4,
]);

const palettePath = path.join(tmpDir, "palette.png");
runFfmpeg([
  "-y",
  "-i",
  outMp4,
  "-vf",
  "fps=6,scale=800:-1:flags=lanczos,palettegen=max_colors=128",
  palettePath,
]);
runFfmpeg([
  "-y",
  "-i",
  outMp4,
  "-i",
  palettePath,
  "-lavfi",
  "fps=6,scale=800:-1:flags=lanczos[x];[x][1:v]paletteuse=dither=bayer:bayer_scale=3",
  "-loop",
  "0",
  outGif,
]);

fs.rmSync(tmpDir, { recursive: true, force: true });

for (const f of [outMp4, outGif]) {
  const stat = fs.statSync(f);
  console.log(`${path.basename(f)}: ${(stat.size / 1024 / 1024).toFixed(2)} MiB`);
}
