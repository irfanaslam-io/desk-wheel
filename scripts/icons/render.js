// Renders build/*.svg to PNGs at every size the icon files need (Chromium via Playwright).
const { chromium } = require('playwright');
const fs = require('fs'), path = require('path');
const dir = path.join(__dirname, 'build');
const jobs = [];
for (const s of [16, 32, 64, 128, 256, 512, 1024]) jobs.push(['mac' + (s <= 32 ? '-small' : ''), s, `mac-${s}.png`]);
for (const s of [16, 20, 24, 32, 40, 48, 64, 256]) jobs.push(['win' + (s <= 32 ? '-small' : ''), s, `win-${s}.png`]);
for (const s of [16, 32, 44]) jobs.push(['menubar', s, `menubar-${s}.png`]);
jobs.push(['win', 256, 'app-256.png']);
jobs.push(['win', 1024, 'win-1024.png']);
(async () => {
  const b = await chromium.launch();
  const p = await b.newPage();
  for (const [name, size, out] of jobs) {
    const svg = fs.readFileSync(path.join(dir, name + '.svg'), 'utf8');
    await p.setViewportSize({ width: size, height: size });
    await p.setContent(`<html><body style="margin:0;background:transparent">${svg.replace('<svg ', `<svg width="${size}" height="${size}" `)}</body></html>`);
    await p.screenshot({ path: path.join(dir, out), omitBackground: true, clip: { x: 0, y: 0, width: size, height: size } });
  }
  await b.close();
  console.log('png ok', jobs.length);
})();
