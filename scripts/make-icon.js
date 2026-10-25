// Builds the app icons from build/logo.png (white logo on transparent):
//   build/icon.png            512px, electron-builder turns it into the .ico
//   src/renderer/img/logo.png 128px, shown in the main window and the widget
// The white mark sits on a dark rounded tile so it reads on light and dark
// backgrounds alike. Run with: npm run icon
import { app, BrowserWindow } from 'electron';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), '..');
const source = path.join(root, 'build', 'logo.png');
const outputs = [
  [path.join(root, 'build', 'icon.png'), 512],
  [path.join(root, 'src', 'renderer', 'img', 'logo.png'), 128],
];

// Runs in the page: draws one tile and returns it as a PNG data URL.
function drawTile(logoUrl, size) {
  return new Promise((resolve) => {
    const img = new Image();
    img.onload = () => {
      const c = document.createElement('canvas');
      c.width = c.height = size;
      const x = c.getContext('2d');
      const m = size * 0.04;
      const s = size - 2 * m;
      const r = s * 0.23;
      x.beginPath();
      x.roundRect(m, m, s, s, r);
      const g = x.createLinearGradient(m, m, m + s, m + s);
      g.addColorStop(0, '#2c2c30');
      g.addColorStop(1, '#0c0c0e');
      x.fillStyle = g;
      x.fill();
      if (size >= 128) {
        x.lineWidth = size / 256;
        x.strokeStyle = 'rgba(255,255,255,0.10)';
        x.stroke();
      }
      // Crop to the mark itself, then centre it at 70% of the tile width.
      const src = [190, 292, 1620, 1416].map((v) => (v * img.width) / 2000);
      const w = s * 0.7;
      const h = (w * src[3]) / src[2];
      x.imageSmoothingQuality = 'high';
      x.drawImage(img, ...src, (size - w) / 2, (size - h) / 2, w, h);
      resolve(c.toDataURL('image/png'));
    };
    img.src = logoUrl;
  });
}

app.whenReady().then(async () => {
  const win = new BrowserWindow({ show: false, webPreferences: { offscreen: true } });
  await win.loadURL('data:text/html,<body></body>');
  const logoUrl = `data:image/png;base64,${fs.readFileSync(source).toString('base64')}`;
  for (const [file, size] of outputs) {
    const dataUrl = await win.webContents.executeJavaScript(`(${drawTile})(${JSON.stringify(logoUrl)}, ${size})`);
    fs.mkdirSync(path.dirname(file), { recursive: true });
    fs.writeFileSync(file, Buffer.from(dataUrl.split(',')[1], 'base64'));
    console.log(`icon: ${path.relative(root, file)} ${size}px`);
  }
  app.quit();
});
