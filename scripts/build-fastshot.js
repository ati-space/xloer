// npm run fastshot
// Builds FastShot (screenshots, recording, the file wheel) from native/fastshot,
// a copy of the FastShot for Windows sources, into native/out/FastShot.exe.
// electron-builder ships it as resources/fastshot/FastShot.exe, and Xloer runs
// it while it runs (src/main/fastshot.js). scripts/release.js runs this first.
// Needs the .NET 10 SDK. The exe is self-contained, so it runs on PCs
// without .NET installed.

import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), '..');
const project = path.join(root, 'native', 'fastshot', 'src', 'FastShot', 'FastShot.csproj');
const out = path.join(root, 'native', 'out');
const exe = path.join(out, 'FastShot.exe');

fs.rmSync(out, { recursive: true, force: true });
try {
  execFileSync(
    'dotnet',
    [
      'publish', project,
      '-c', 'Release',
      '-r', 'win-x64',
      '--self-contained', 'true',
      '-p:PublishSingleFile=true',
      '-p:IncludeNativeLibrariesForSelfExtract=true',
      '-p:EnableCompressionInSingleFile=true',
      '-p:DebugType=none',
      // Also builds on macOS.
      '-p:EnableWindowsTargeting=true',
      '-o', out,
    ],
    { cwd: root, stdio: 'inherit', env: { ...process.env, DOTNET_CLI_TELEMETRY_OPTOUT: '1', DOTNET_NOLOGO: '1' } },
  );
} catch {
  console.error('\n✗ FastShot derlenemedi. .NET 10 SDK kurulu mu? (https://dotnet.microsoft.com/download)');
  process.exit(1);
}
if (!fs.existsSync(exe)) {
  console.error(`\n✗ Derleme çıktısı eksik: ${path.relative(root, exe)}`);
  process.exit(1);
}
console.log(`FastShot hazır: ${path.relative(root, exe)} (${(fs.statSync(exe).size / 1048576).toFixed(1)} MB)`);
