// In Electron the preload script provides window.xloer. Opened as a plain web
// page (for a quick look in a browser) the app runs on a localStorage copy.
import { createPreviewApi } from './preview-api.js';

export const api = globalThis.xloer ?? createPreviewApi();
