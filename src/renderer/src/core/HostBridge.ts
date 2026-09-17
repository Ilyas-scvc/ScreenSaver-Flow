import { normaliseSettings, defaultViewContext } from '../config/defaults';
import type { FlowScreenSettings, RendererPayload, ViewContext } from '../config/types';
import { log } from './Log';

interface WebView2Bridge {
  postMessage(message: unknown): void;
  addEventListener(type: 'message', listener: (event: { data: unknown }) => void): void;
  removeEventListener(type: 'message', listener: (event: { data: unknown }) => void): void;
}

declare global {
  interface Window {
    chrome?: { webview?: WebView2Bridge };
  }
}

/** Messages the page sends to the Windows host. */
export type RendererMessage =
  | { type: 'ready'; monitorIndex: number }
  | { type: 'input'; kind: 'key' | 'mouse-button' | 'mouse-move'; distance: number }
  | { type: 'error'; message: string }
  | { type: 'contextLost' };

/** Messages the host sends to the page. */
interface HostMessage {
  type?: string;
  settings?: unknown;
  view?: unknown;
}

function decodeBase64Utf8(value: string): string {
  const normalised = value.replace(/-/g, '+').replace(/_/g, '/');
  const padded = normalised + '='.repeat((4 - (normalised.length % 4)) % 4);
  const binary = atob(padded);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return new TextDecoder().decode(bytes);
}

function normaliseView(input: unknown, fallback: ViewContext): ViewContext {
  if (typeof input !== 'object' || input === null) return fallback;
  const v = input as Partial<ViewContext>;
  const rect = (r: unknown, def: { x: number; y: number; w: number; h: number }) => {
    if (typeof r !== 'object' || r === null) return def;
    const o = r as Record<string, unknown>;
    const num = (k: string, d: number) => (Number.isFinite(Number(o[k])) ? Number(o[k]) : d);
    return { x: num('x', def.x), y: num('y', def.y), w: num('w', def.w), h: num('h', def.h) };
  };
  return {
    monitorIndex: Number.isFinite(Number(v.monitorIndex)) ? Number(v.monitorIndex) : 0,
    monitorCount: Math.max(1, Number(v.monitorCount) || 1),
    synchronized: v.synchronized === true,
    epochMs: Number.isFinite(Number(v.epochMs)) ? Number(v.epochMs) : fallback.epochMs,
    bounds: rect(v.bounds, fallback.bounds),
    virtual: rect(v.virtual, fallback.virtual),
  };
}

/**
 * Reads the startup payload out of the URL.
 *
 * The host base64-encodes the settings into `?cfg=`; the loose query parameters
 * exist purely so the renderer can be driven straight from a browser address bar
 * during development.
 */
export function readPayload(width: number, height: number): RendererPayload {
  const params = new URLSearchParams(window.location.search);
  const fallbackView = defaultViewContext(width, height);

  let settings: FlowScreenSettings;
  let view = fallbackView;

  const cfg = params.get('cfg');
  if (cfg) {
    try {
      const parsed = JSON.parse(decodeBase64Utf8(cfg)) as { settings?: unknown; view?: unknown };
      settings = normaliseSettings(parsed.settings);
      view = normaliseView(parsed.view, fallbackView);
    } catch (error) {
      log.warn('failed to decode host payload, using defaults', error);
      settings = normaliseSettings({});
    }
  } else {
    settings = normaliseSettings({});
  }

  // Development overrides.
  const density = params.get('density');
  if (density) settings = normaliseSettings({ ...settings, density });
  const palette = params.get('palette');
  if (palette) settings = normaliseSettings({ ...settings, palette });
  const seed = params.get('seed');
  if (seed) settings = normaliseSettings({ ...settings, seed: Number(seed), randomSeedOnLaunch: false });
  if (params.get('debug') === '1') settings = { ...settings, showDebugOverlay: true };

  if (settings.randomSeedOnLaunch && !seed && !cfg) {
    settings = { ...settings, seed: (Math.random() * 2147483646) | 0 };
  }

  return { settings, view };
}

/**
 * Thin wrapper over the WebView2 message channel. Everything degrades to a
 * no-op in a plain browser so `npm run dev` behaves identically minus the host.
 */
export class HostBridge {
  readonly isHosted: boolean;
  private readonly channel: WebView2Bridge | undefined;
  private settingsListener: ((settings: FlowScreenSettings) => void) | null = null;
  private readonly handler = (event: { data: unknown }): void => this.onMessage(event.data);

  constructor() {
    this.channel = window.chrome?.webview;
    this.isHosted = this.channel !== undefined;
    this.channel?.addEventListener('message', this.handler);
  }

  onSettings(listener: (settings: FlowScreenSettings) => void): void {
    this.settingsListener = listener;
  }

  private onMessage(data: unknown): void {
    let message: HostMessage | null = null;
    if (typeof data === 'string') {
      try {
        message = JSON.parse(data) as HostMessage;
      } catch {
        log.warn('host sent a non-JSON string message');
        return;
      }
    } else if (typeof data === 'object' && data !== null) {
      message = data as HostMessage;
    }

    if (!message) return;
    if (message.type === 'settings' && this.settingsListener) {
      this.settingsListener(normaliseSettings(message.settings));
    }
  }

  post(message: RendererMessage): void {
    // JSON string rather than a structured clone: the host receives it through
    // WebMessageAsJson either way, and a string round-trips predictably.
    this.channel?.postMessage(JSON.stringify(message));
  }

  dispose(): void {
    this.channel?.removeEventListener('message', this.handler);
    this.settingsListener = null;
  }
}
