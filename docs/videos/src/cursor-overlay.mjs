/** Injected on every mock page — cursor, ripples, fade, and content motion. */
export const DEMO_INIT_SCRIPT = `
(() => {
  if (window.__apDemo) return;

  const easeInOutCubic = (t) =>
    t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2;

  const state = {
    cursor: { x: 640, y: 380, pressed: 0 },
    fade: 0,
    content: { opacity: 1, translateY: 0 },
    ripples: [],
    ripple: null,
    captionHtml: "",
  };

  function ensureDom() {
    if (document.getElementById("ap-demo-root")) return;
    const root = document.createElement("div");
    root.id = "ap-demo-root";
    root.innerHTML = \`
      <style id="ap-demo-style">
        #ap-demo-root { pointer-events: none; position: fixed; inset: 0; z-index: 99999; }
        #ap-fade {
          position: absolute; inset: 0; background: #0d0e12;
          opacity: 0; transition: none;
        }
        #ap-content-shift {
          position: fixed; inset: 0; z-index: 0;
          transition: none;
        }
        #ap-cursor {
          position: absolute; left: 0; top: 0;
          width: 28px; height: 28px;
          margin-left: -2px; margin-top: -2px;
          filter: drop-shadow(0 1px 2px rgba(0,0,0,0.65));
          transform-origin: 2px 2px;
          will-change: transform;
        }
        #ap-cursor svg { display: block; }
        .ap-ripple {
          position: absolute; width: 12px; height: 12px;
          margin: -6px 0 0 -6px; border-radius: 50%;
          border: 2px solid rgba(140, 209, 255, 0.85);
          background: rgba(140, 209, 255, 0.15);
          transform: scale(0.35); opacity: 0.9;
        }
        #ap-caption {
          position: fixed; left: 0; right: 0; bottom: 0; z-index: 100000;
          pointer-events: none;
          font-family: "Segoe UI", system-ui, sans-serif;
        }
        #ap-caption-inner {
          margin: 0 auto 20px; max-width: 90%; text-align: center;
          background: rgba(8, 10, 14, 0.88); border: 1px solid #454652;
          border-radius: 6px; padding: 10px 20px; font-size: 17px; color: #e8e8f0;
          box-shadow: 0 4px 24px rgba(0, 0, 0, 0.45);
          opacity: 0; transform: translateY(8px);
          transition: opacity 0.35s ease, transform 0.35s ease;
        }
        #ap-caption-inner.visible { opacity: 1; transform: translateY(0); }
        #ap-caption-inner strong { color: #8cd1ff; font-weight: 600; }
        .ap-click-target { transition: transform 0.12s ease, filter 0.12s ease; }
        .ap-click-target.ap-pressed { transform: scale(0.985); filter: brightness(1.08); }
      </style>
      <div id="ap-fade"></div>
      <div id="ap-cursor" aria-hidden="true">
        <svg width="28" height="28" viewBox="0 0 28 28" fill="none">
          <path d="M3 2L3 22L9 16L13 24L16 22L12 14L20 14L3 2Z"
            fill="#fff" stroke="#1a1a22" stroke-width="1.2" stroke-linejoin="round"/>
        </svg>
      </div>
      <div id="ap-caption"></div>
    \`;
    document.body.appendChild(root);
    const shift = document.createElement("div");
    shift.id = "ap-content-shift";
    while (document.body.firstChild && document.body.firstChild.id !== "ap-demo-root") {
      shift.appendChild(document.body.firstChild);
    }
    document.body.insertBefore(shift, root);
  }

  function paint() {
    ensureDom();
    const fadeEl = document.getElementById("ap-fade");
    const cursorEl = document.getElementById("ap-cursor");
    const shiftEl = document.getElementById("ap-content-shift");
    fadeEl.style.opacity = String(state.fade);
    shiftEl.style.opacity = String(state.content.opacity);
    shiftEl.style.transform = \`translateY(\${state.content.translateY}px)\`;
    const p = state.cursor.pressed;
    const scale = 1 - p * 0.08;
    cursorEl.style.transform =
      \`translate(\${state.cursor.x}px, \${state.cursor.y}px) scale(\${scale})\`;
    const host = document.getElementById("ap-demo-root");
    host.querySelectorAll(".ap-ripple").forEach((n) => n.remove());
    if (state.ripple) {
      const age = state.ripple.t / 14;
      if (age < 1) {
        const el = document.createElement("div");
        el.className = "ap-ripple";
        el.style.left = state.ripple.x + "px";
        el.style.top = state.ripple.y + "px";
        const s = 0.35 + age * 2.2;
        el.style.transform = \`scale(\${s})\`;
        el.style.opacity = String(0.85 * (1 - age));
        host.appendChild(el);
      }
    }
    const cap = document.getElementById("ap-caption");
    if (cap) cap.innerHTML = state.captionHtml;
  }

  window.__apDemo = {
    easeInOutCubic,
    paint,
    setCursor(x, y, pressed = 0) {
      state.cursor.x = x;
      state.cursor.y = y;
      state.cursor.pressed = pressed;
      paint();
    },
    setFade(opacity) {
      state.fade = opacity;
      paint();
    },
    setContentMotion(opacity, translateY) {
      state.content.opacity = opacity;
      state.content.translateY = translateY;
      paint();
    },
    setCaption(title, subtitle = "", visible = true) {
      const cls = visible ? " visible" : "";
      state.captionHtml = subtitle
        ? \`<div id="ap-caption-inner" class="\${cls}"><strong>\${title}</strong> — \${subtitle}</div>\`
        : \`<div id="ap-caption-inner" class="\${cls}"><strong>\${title}</strong></div>\`;
      paint();
      requestAnimationFrame(() => {
        const inner = document.getElementById("ap-caption-inner");
        if (inner) inner.classList.add("visible");
      });
    },
    ripple(x, y) {
      state.ripple = { x, y, t: 0 };
      paint();
    },
    onFrame() {
      if (state.ripple) {
        state.ripple.t += 1;
        if (state.ripple.t > 14) state.ripple = null;
      }
      paint();
    },
    centerOf(selector) {
      const el = document.querySelector(selector);
      if (!el) return null;
      const r = el.getBoundingClientRect();
      return { x: r.left + r.width / 2, y: r.top + r.height / 2 };
    },
    pressTarget(selector, on) {
      const el = document.querySelector(selector);
      if (!el) return;
      el.classList.toggle("ap-click-target", true);
      el.classList.toggle("ap-pressed", on);
    },
    resetMotion() {
      state.content.opacity = 1;
      state.content.translateY = 0;
      state.fade = 0;
      state.cursor.pressed = 0;
      paint();
    },
  };
})();
`;

export async function demo(page, fn, ...args) {
  return page.evaluate(
    ({ fn, args }) => {
      const d = window.__apDemo;
      if (!d) throw new Error("demo overlay missing");
      return d[fn](...args);
    },
    { fn, args },
  );
}

export async function demoEval(page, expression) {
  return page.evaluate(expression);
}

export function easeInOutCubic(t) {
  return t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2;
}

/** Lerp cursor from (x0,y0) to element over `frames` captures; returns async tick(i). */
export async function lerpCursorTo(page, x0, y0, selector, frames) {
  const end = await demo(page, "centerOf", selector);
  if (!end) return async () => {};
  const x1 = end.x;
  const y1 = end.y;
  return async (i) => {
    const t = easeInOutCubic(Math.min(1, i / Math.max(1, frames - 1)));
    await demo(page, "setCursor", x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, 0);
  };
}

export async function clickRipple(page, selector, action) {
  const hit = await demo(page, "centerOf", selector);
  if (!hit) {
    if (action) await action(page);
    return;
  }
  await demo(page, "setCursor", hit.x, hit.y, 1);
  await demo(page, "pressTarget", selector, true);
  await demo(page, "ripple", hit.x, hit.y);
  if (action) await action(page);
  await demo(page, "setCursor", hit.x, hit.y, 0);
  await demo(page, "pressTarget", selector, false);
}
