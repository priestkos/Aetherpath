import { chromium } from "playwright";
import { fileURLToPath } from "url";
import path from "path";
import fs from "fs";
import { spawnSync } from "child_process";
import {
  DEMO_INIT_SCRIPT,
  demo,
  easeInOutCubic,
  clickRipple,
} from "./cursor-overlay.mjs";

const dir = path.dirname(fileURLToPath(import.meta.url));
const videosDir = path.join(dir, "..");
const screenshotsSrc = path.join(dir, "..", "..", "screenshots", "src");
const outMp4 = path.join(videosDir, "mockup-walkthrough.mp4");
const outGif = path.join(videosDir, "mockup-walkthrough.gif");
const tmpDir = path.join(videosDir, ".render-tmp");
const framesDir = path.join(tmpDir, "frames");

const W = 1280;
const H = 720;
const FPS = 30;

function runFfmpeg(args) {
  const r = spawnSync("ffmpeg", args, { encoding: "utf-8" });
  if (r.status !== 0) {
    console.error(r.stderr || r.stdout);
    throw new Error(`ffmpeg failed: ${args.join(" ")}`);
  }
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
  await demo(page, "resetMotion");
}

async function openIntro(page) {
  await page.goto("file://" + path.join(dir, "intro.html"), { waitUntil: "networkidle" });
  await demo(page, "resetMotion");
}

async function captureFrame(page, index) {
  await demo(page, "onFrame");
  const name = `frame_${String(index).padStart(6, "0")}.png`;
  await page.screenshot({
    path: path.join(framesDir, name),
    type: "png",
  });
  return index + 1;
}

/** @typedef {{ frames: number, run: (page: import('playwright').Page, emit: () => Promise<number>, cursor: {x:number,y:number}) => Promise<{x:number,y:number}> }} Scene */

/** @type {Scene[]} */
const scenes = [
  {
    frames: 93,
    async run(page, emit, cursor) {
      await openIntro(page);
      await demo(page, "setFade", 1);
      await demo(page, "setContentMotion", 0.4, 14);
      await emit();
      for (let i = 1; i < 18; i++) {
        const t = easeInOutCubic(i / 17);
        await demo(page, "setFade", 1 - t);
        await demo(page, "setContentMotion", 0.4 + 0.6 * t, 14 * (1 - t));
        await emit();
      }
      for (let i = 0; i < 60; i++) await emit();
      for (let i = 1; i <= 15; i++) {
        await demo(page, "setFade", i / 15);
        await emit();
      }
      cursor.x = 640;
      cursor.y = 380;
    },
  },
  {
    frames: 150,
    async run(page, emit, cursor) {
      await openMock(page, "01-main-route-comparison.html");
      await demo(page, "setFade", 1);
      await demo(page, "setContentMotion", 0.55, 10);
      await demo(page, "setCaption", "Route comparison", "ranked routes & slot advice", false);
      await emit();
      for (let i = 1; i < 14; i++) {
        const t = easeInOutCubic(i / 13);
        await demo(page, "setFade", 1 - t);
        await demo(page, "setContentMotion", 0.55 + 0.45 * t, 10 * (1 - t));
        await emit();
      }
      await demo(page, "setCaption", "Route comparison", "ranked routes & slot advice");
      for (let i = 0; i < 20; i++) await emit();

      const rowSel = "table.data tbody tr:nth-child(2)";
      const end = await demo(page, "centerOf", rowSel);
      const x0 = cursor.x;
      const y0 = cursor.y;
      const frames = 38;
      for (let i = 0; i < frames; i++) {
        const t = easeInOutCubic(i / Math.max(1, frames - 1));
        const x = end ? x0 + (end.x - x0) * t : x0;
        const y = end ? y0 + (end.y - y0) * t : y0;
        await demo(page, "setCursor", x, y, 0);
        await emit();
      }
      if (end) {
        cursor.x = end.x;
        cursor.y = end.y;
      }
      await clickRipple(page, rowSel, async (p) => {
        await p.evaluate(() => {
          const rows = document.querySelectorAll("table.data tbody tr");
          rows.forEach((r, i) => {
            r.style.transition = "background 0.35s ease";
            r.style.background = i === 1 ? "rgba(140, 209, 255, 0.1)" : "";
          });
        });
      });
      for (let i = 0; i < 8; i++) await emit();
      for (let i = 0; i < 58; i++) await emit();
      for (let i = 1; i <= 12; i++) {
        await demo(page, "setFade", i / 12);
        await emit();
      }
    },
  },
  {
    frames: 181,
    async run(page, emit, cursor) {
      await openMock(page, "02-main-destination-picker.html");
      await demo(page, "setFade", 1);
      await demo(page, "setCaption", "Destination picker", "search, Zones list, live & projected costs", false);
      await emit();
      for (let i = 1; i < 14; i++) {
        const t = easeInOutCubic(i / 13);
        await demo(page, "setFade", 1 - t);
        await emit();
      }
      await demo(page, "setCaption", "Destination picker", "search, Zones list, live & projected costs");
      for (let i = 0; i < 15; i++) await emit();

      const zonesSel = ".row-tools .fake-check";
      let end = await demo(page, "centerOf", zonesSel);
      const x0 = cursor.x;
      const y0 = cursor.y;
      for (let i = 0; i < 32; i++) {
        const t = easeInOutCubic(i / 31);
        const x = end ? x0 + (end.x - x0) * t : x0;
        const y = end ? y0 + (end.y - y0) * t : y0;
        await demo(page, "setCursor", x, y, 0);
        await emit();
      }
      await clickRipple(page, zonesSel, async (p) => {
        await p.evaluate(() => {
          const zones = document.querySelector(".fake-check.checked");
          if (zones) zones.classList.remove("checked");
        });
      });
      for (let i = 0; i < 10; i++) await emit();
      await clickRipple(page, zonesSel, async (p) => {
        await p.evaluate(() => {
          const zones = document.querySelector(".row-tools .fake-check");
          if (zones) zones.classList.add("checked");
        });
      });
      for (let i = 0; i < 12; i++) await emit();

      const pickCenter = await page.evaluate(() => {
        const pick = [...document.querySelectorAll(".scroll-panel .selectable")].find((el) =>
          el.textContent.includes("Costa del Sol"),
        );
        if (!pick) return null;
        pick.dataset.apTarget = "pick";
        const r = pick.getBoundingClientRect();
        return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
      });
      end = pickCenter;
      const pickSel = '[data-ap-target="pick"]';
      const x1 = cursor.x;
      const y1 = cursor.y;
      for (let i = 0; i < 36; i++) {
        const t = easeInOutCubic(i / 35);
        const x = end ? x1 + (end.x - x1) * t : x1;
        const y = end ? y1 + (end.y - y1) * t : y1;
        await demo(page, "setCursor", x, y, 0);
        await emit();
      }
      await clickRipple(page, pickSel, async (p) => {
        await p.evaluate(() => {
          const items = [...document.querySelectorAll(".scroll-panel .selectable")];
          items.forEach((el) => el.classList.remove("selected"));
          const pick = items.find((el) => el.textContent.includes("Costa del Sol"));
          if (pick) pick.classList.add("selected");
        });
      });
      if (end) {
        cursor.x = end.x;
        cursor.y = end.y;
      }
      for (let i = 0; i < 10; i++) await emit();
      for (let i = 0; i < 40; i++) await emit();
      for (let i = 1; i <= 12; i++) {
        await demo(page, "setFade", i / 12);
        await emit();
      }
    },
  },
  {
    frames: 121,
    async run(page, emit, cursor) {
      await openMock(page, "03-main-diagnostics-slot-value.html");
      await demo(page, "setFade", 1);
      await demo(page, "setCaption", "Diagnostics & slot value", "missing free transfer hint & worth table", false);
      await emit();
      for (let i = 1; i < 14; i++) {
        const t = easeInOutCubic(i / 13);
        await demo(page, "setFade", 1 - t);
        await emit();
      }
      await demo(page, "setCaption", "Diagnostics & slot value", "missing free transfer hint & worth table");
      cursor.x = 520;
      cursor.y = 340;
      await demo(page, "setCursor", cursor.x, cursor.y, 0);
      for (let i = 0; i < 95; i++) await emit();
      for (let i = 1; i <= 12; i++) {
        await demo(page, "setFade", i / 12);
        await emit();
      }
    },
  },
  {
    frames: 168,
    async run(page, emit, cursor) {
      await openMock(page, "04-settings-travel-slots.html");
      await page.evaluate(() => {
        const fields = document.querySelectorAll(".int-field");
        const field = fields[fields.length - 1];
        if (field) field.id = "ap-ticket-field";
      });
      await demo(page, "setFade", 1);
      await demo(page, "setCaption", "Settings — travel slots", "overrides & travel modes", false);
      await emit();
      for (let i = 1; i < 14; i++) {
        const t = easeInOutCubic(i / 13);
        await demo(page, "setFade", 1 - t);
        await emit();
      }
      await demo(page, "setCaption", "Settings — travel slots", "overrides & travel modes");
      for (let i = 0; i < 12; i++) await emit();

      const readSel = ".collapse-body .fake-check";
      let end = await demo(page, "centerOf", readSel);
      const x0 = cursor.x;
      const y0 = cursor.y;
      for (let i = 0; i < 30; i++) {
        const t = easeInOutCubic(i / 29);
        await demo(page, "setCursor", end ? x0 + (end.x - x0) * t : x0, end ? y0 + (end.y - y0) * t : y0, 0);
        await emit();
      }
      await clickRipple(page, readSel, async (p) => {
        await p.evaluate(() => {
          const read = document.querySelector(".collapse-body .fake-check.checked");
          if (read) read.classList.remove("checked");
        });
      });
      for (let i = 0; i < 8; i++) await emit();
      await clickRipple(page, readSel, async (p) => {
        await p.evaluate(() => {
          const read = document.querySelector(".collapse-body .fake-check");
          if (read) read.classList.add("checked");
        });
      });
      for (let i = 0; i < 10; i++) await emit();

      const ticketSel = "#ap-ticket-field";
      end = await demo(page, "centerOf", ticketSel);
      const x1 = cursor.x;
      const y1 = cursor.y;
      for (let i = 0; i < 34; i++) {
        const t = easeInOutCubic(i / 33);
        await demo(page, "setCursor", end ? x1 + (end.x - x1) * t : x1, end ? y1 + (end.y - y1) * t : y1, 0);
        await emit();
      }
      await clickRipple(page, ticketSel, async (p) => {
        await p.evaluate(() => {
          const ticket = document.querySelectorAll(".int-field");
          const field = ticket[ticket.length - 1];
          if (field) {
            field.textContent = "3";
            field.style.outline = "1px solid #8cd1ff";
            field.style.transition = "outline 0.25s ease";
          }
        });
      });
      if (end) {
        cursor.x = end.x;
        cursor.y = end.y;
      }
      for (let i = 0; i < 10; i++) await emit();
      for (let i = 0; i < 38; i++) await emit();
      for (let i = 1; i <= 12; i++) {
        await demo(page, "setFade", i / 12);
        await emit();
      }
    },
  },
  {
    frames: 167,
    async run(page, emit, cursor) {
      await openMock(page, "05-settings-display-override.html", true);
      await page.evaluate(() => {
        const hide = [...document.querySelectorAll(".fake-check")].find((el) =>
          el.textContent.includes("Hide routes costing 1000"),
        );
        if (hide) hide.dataset.apTarget = "hide-routes";
      });
      await demo(page, "setFade", 1);
      await demo(page, "setCaption", "Display & override banner", "/ap alias · manual override projections", false);
      await emit();
      for (let i = 1; i < 14; i++) {
        const t = easeInOutCubic(i / 13);
        await demo(page, "setFade", 1 - t);
        await emit();
      }
      await demo(page, "setCaption", "Display & override banner", "/ap alias · manual override projections");
      for (let i = 0; i < 12; i++) await emit();

      const hideSel = '[data-ap-target="hide-routes"]';
      let end = await demo(page, "centerOf", hideSel);
      const x0 = cursor.x;
      const y0 = cursor.y;
      for (let i = 0; i < 32; i++) {
        const t = easeInOutCubic(i / 31);
        await demo(page, "setCursor", end ? x0 + (end.x - x0) * t : x0, end ? y0 + (end.y - y0) * t : y0, 0);
        await emit();
      }
      await clickRipple(page, hideSel, async (p) => {
        await p.evaluate(() => {
          const hide = [...document.querySelectorAll(".fake-check")].find((el) =>
            el.textContent.includes("Hide routes costing 1000"),
          );
          if (hide) hide.classList.add("checked");
        });
      });
      for (let i = 0; i < 12; i++) await emit();

      const sliderSel = ".slider-track";
      end = await demo(page, "centerOf", sliderSel);
      const track = await page.evaluate(() => {
        const tr = document.querySelector(".slider-track");
        if (!tr) return null;
        const r = tr.getBoundingClientRect();
        return { left: r.left, right: r.right, y: r.top + r.height / 2 };
      });
      if (track) {
        const startX = cursor.x;
        const startY = cursor.y;
        const targetX = track.left + (track.right - track.left) * 0.42;
        for (let i = 0; i < 40; i++) {
          const t = easeInOutCubic(i / 39);
          const x = startX + (targetX - startX) * t;
          const y = startY + (track.y - startY) * t;
          await demo(page, "setCursor", x, y, i > 34 ? 1 : 0);
          if (i === 35) {
            await demo(page, "ripple", x, y);
            await page.evaluate(() => {
              const fill = document.querySelector(".slider-fill");
              const label = document.querySelector(".slider-row .muted");
              if (fill) {
                fill.style.transition = "width 0.45s ease";
                fill.style.width = "42%";
              }
              if (label) label.textContent = "12";
            });
          }
          await emit();
        }
        cursor.x = targetX;
        cursor.y = track.y;
        await demo(page, "setCursor", cursor.x, cursor.y, 0);
      }
      for (let i = 0; i < 45; i++) await emit();
      for (let i = 1; i <= 12; i++) {
        await demo(page, "setFade", i / 12);
        await emit();
      }
    },
  },
  {
    frames: 81,
    async run(page, emit, cursor) {
      await openIntro(page);
      await demo(page, "setFade", 1);
      await demo(page, "setCaption", "Read-only by design", "mock-ups — see README for real usage", false);
      await emit();
      for (let i = 1; i < 14; i++) {
        const t = easeInOutCubic(i / 13);
        await demo(page, "setFade", 1 - t);
        await emit();
      }
      await demo(page, "setCaption", "Read-only by design", "mock-ups — see README for real usage");
      await demo(page, "setCursor", 640, 400, 0);
      for (let i = 0; i < 55; i++) await emit();
      for (let i = 1; i <= 12; i++) {
        await demo(page, "setFade", i / 12);
        await emit();
      }
    },
  },
];

fs.rmSync(tmpDir, { recursive: true, force: true });
fs.mkdirSync(framesDir, { recursive: true });

const browser = await chromium.launch();
const context = await browser.newContext({
  viewport: { width: W, height: H },
  deviceScaleFactor: 1,
});
await context.addInitScript(DEMO_INIT_SCRIPT);
const page = await context.newPage();

let frameIndex = 0;
const cursor = { x: 640, y: 380 };

for (const scene of scenes) {
  console.log(`Scene (${scene.frames} frames)`);
  let emitted = 0;
  const emit = async () => {
    frameIndex = await captureFrame(page, frameIndex);
    emitted++;
    return frameIndex;
  };
  await scene.run(page, emit, cursor);
  if (emitted !== scene.frames) {
    console.warn(`  expected ${scene.frames} frames, got ${emitted}`);
  }
}

await page.close();
await context.close();
await browser.close();

console.log(`Encoded ${frameIndex} frames`);

runFfmpeg([
  "-y",
  "-framerate",
  String(FPS),
  "-i",
  path.join(framesDir, "frame_%06d.png"),
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
