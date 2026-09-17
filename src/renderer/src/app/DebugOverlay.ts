import type * as THREE from 'three';

export interface OverlayData {
  fps: number;
  frameTimeMs: number;
  activeCount: number;
  capacity: number;
  bufferWidth: number;
  bufferHeight: number;
  cssWidth: number;
  cssHeight: number;
  renderScale: number;
  qualityStep: number;
  drawCalls: number;
  uptimeSeconds: number;
}

const ROWS = [
  'fps',
  'frame',
  'fibers',
  'buffer',
  'display',
  'scale',
  'quality',
  'draws',
  'uptime',
  'gpu',
] as const;

type RowKey = (typeof ROWS)[number];

/**
 * Development-only statistics panel.
 *
 * Text nodes are created once and only their `nodeValue` is written, so the
 * overlay never triggers layout or allocates during a frame.
 */
export class DebugOverlay {
  private readonly root: HTMLDivElement;
  private readonly values = new Map<RowKey, Text>();
  private accumulator = 0;
  private visible = false;

  constructor(parent: HTMLElement, gpuDescription: string) {
    this.root = document.createElement('div');
    this.root.className = 'fs-debug';

    for (const key of ROWS) {
      const row = document.createElement('div');
      row.className = 'fs-debug-row';

      const label = document.createElement('span');
      label.className = 'fs-debug-key';
      label.textContent = key;

      const value = document.createElement('span');
      value.className = 'fs-debug-value';
      const text = document.createTextNode('-');
      value.appendChild(text);
      this.values.set(key, text);

      row.append(label, value);
      this.root.appendChild(row);
    }

    const gpu = this.values.get('gpu');
    if (gpu) gpu.nodeValue = gpuDescription;

    this.root.style.display = 'none';
    parent.appendChild(this.root);
  }

  setVisible(visible: boolean): void {
    if (visible === this.visible) return;
    this.visible = visible;
    this.root.style.display = visible ? '' : 'none';
  }

  get isVisible(): boolean {
    return this.visible;
  }

  /** Throttled to ~5 Hz; a per-frame readout is unreadable anyway. */
  update(dt: number, data: OverlayData): void {
    if (!this.visible) return;
    this.accumulator += dt;
    if (this.accumulator < 0.2) return;
    this.accumulator = 0;

    this.set('fps', data.fps.toFixed(1));
    this.set('frame', `${data.frameTimeMs.toFixed(2)} ms`);
    this.set('fibers', `${data.activeCount.toLocaleString('en-US')} / ${data.capacity.toLocaleString('en-US')}`);
    this.set('buffer', `${data.bufferWidth} x ${data.bufferHeight}`);
    this.set('display', `${data.cssWidth} x ${data.cssHeight}`);
    this.set('scale', data.renderScale.toFixed(2));
    this.set('quality', `step ${data.qualityStep}`);
    this.set('draws', String(data.drawCalls));
    this.set('uptime', DebugOverlay.formatDuration(data.uptimeSeconds));
  }

  private set(key: RowKey, value: string): void {
    const node = this.values.get(key);
    if (node && node.nodeValue !== value) node.nodeValue = value;
  }

  private static formatDuration(seconds: number): string {
    const s = Math.floor(seconds);
    const h = Math.floor(s / 3600);
    const m = Math.floor((s % 3600) / 60);
    const sec = s % 60;
    return h > 0
      ? `${h}h ${String(m).padStart(2, '0')}m`
      : `${m}m ${String(sec).padStart(2, '0')}s`;
  }

  dispose(): void {
    this.root.remove();
  }
}

/** Best-effort GPU name via WEBGL_debug_renderer_info, which some browsers mask. */
export function describeGpu(renderer: THREE.WebGLRenderer): string {
  try {
    const gl = renderer.getContext();
    const ext = gl.getExtension('WEBGL_debug_renderer_info');
    if (ext) {
      const name = gl.getParameter(ext.UNMASKED_RENDERER_WEBGL) as unknown;
      if (typeof name === 'string' && name.length > 0) return name;
    }
    const fallback = gl.getParameter(gl.RENDERER) as unknown;
    return typeof fallback === 'string' ? fallback : 'unknown';
  } catch {
    return 'unknown';
  }
}
