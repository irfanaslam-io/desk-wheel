// Renders docs/assets/desk-wheel-demo.gif from demo.html (the wheel's real geometry, colours, labels and messages).
//   cd docs/demo && node render.js        (needs Playwright with Chromium, and ffmpeg)
const { chromium } = require('playwright');
const { execFileSync } = require('child_process');
const fs = require('fs'), path = require('path');
(async () => {
  const frames = fs.mkdtempSync(path.join(require('os').tmpdir(), 'dw-demo-'));
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 900, height: 540 } });
  await page.goto('file://' + path.join(__dirname, 'demo.html'));
  await page.evaluate(() => document.fonts.ready);
  const fps = 15, duration = await page.evaluate(() => DURATION);
  let i = 0;
  for (let t = 0; t < duration; t += 1 / fps) {
    await page.evaluate(t => render(t), t);
    await page.screenshot({ path: path.join(frames, `f${String(i++).padStart(4, '0')}.png`) });
  }
  await browser.close();
  const input = path.join(frames, 'f%04d.png'), palette = path.join(frames, 'palette.png');
  const out = path.join(__dirname, '..', 'assets', 'desk-wheel-demo.gif');
  execFileSync('ffmpeg', ['-v', 'error', '-y', '-framerate', String(fps), '-i', input, '-vf', 'palettegen=max_colors=160:stats_mode=diff', palette]);
  execFileSync('ffmpeg', ['-v', 'error', '-y', '-framerate', String(fps), '-i', input, '-i', palette, '-lavfi', 'paletteuse=dither=sierra2_4a:diff_mode=rectangle', '-loop', '0', out]);
  fs.rmSync(frames, { recursive: true });
  console.log(`${i} frames → ${out}`);
})();
