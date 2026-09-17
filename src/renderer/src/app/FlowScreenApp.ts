import * as THREE from 'three';
import type { FlowScreenSettings, RendererPayload, ViewContext } from '../config/types';
import type { Effect } from '../effects/Effect';
import { FiberFlowEffect } from '../effects/fiberflow/FiberFlowEffect';
import { PostPipeline } from '../post/PostPipeline';
import { FullscreenQuad } from '../core/FullscreenQuad';
import type { HostBridge } from '../core/HostBridge';
import { log } from '../core/Log';
import { NullAudioSource, type AudioSource } from '../audio/AudioLevels';
import { AdaptiveQuality } from './AdaptiveQuality';
import { PerformanceMonitor } from './PerformanceMonitor';
import { DebugOverlay, describeGpu } from './DebugOverlay';

const MAX_DEVICE_PIXEL_RATIO = 2;

export class WebGL2UnavailableError extends Error {
  constructor() {
    super('WebGL2 is not available. FlowScreen needs a GPU with WebGL2 support.');
    this.name = 'WebGL2UnavailableError';
  }
}

/**
 * Owns the canvas, the render loop and everything hanging off it.
 *
 * Deliberately the only place that knows about wall-clock time, frame pacing and
 * the host: the effect and the post pipeline are both pure "given dt, draw".
 */
export class FlowScreenApp {
  private readonly container: HTMLElement;
  private readonly canvas: HTMLCanvasElement;
  private readonly renderer: THREE.WebGLRenderer;
  private readonly quad: FullscreenQuad;
  private readonly post: PostPipeline;
  private readonly effect: Effect;
  private readonly overlay: DebugOverlay;
  private readonly perf = new PerformanceMonitor();
  private readonly adaptive: AdaptiveQuality;
  private readonly bridge: HostBridge;
  private readonly audio: AudioSource = new NullAudioSource();

  private settings: FlowScreenSettings;
  private view: ViewContext;

  private rafId = 0;
  private running = false;
  private lastRenderMs = 0;
  private uptime = 0;
  private appliedRenderScale = 1;
  private bufferWidth = 1;
  private bufferHeight = 1;
  private cssWidth = 1;
  private cssHeight = 1;
  private resizePending = true;

  private readonly resizeObserver: ResizeObserver;

  constructor(container: HTMLElement, payload: RendererPayload, bridge: HostBridge) {
    this.container = container;
    this.settings = payload.settings;
    this.view = payload.view;
    this.bridge = bridge;

    this.canvas = document.createElement('canvas');
    this.canvas.className = 'fs-canvas';
    container.appendChild(this.canvas);

    const context = this.canvas.getContext('webgl2', {
      alpha: false,
      antialias: false,
      depth: false,
      stencil: false,
      powerPreference: 'high-performance',
      preserveDrawingBuffer: false,
      desynchronized: true,
    });
    if (!context) throw new WebGL2UnavailableError();

    this.renderer = new THREE.WebGLRenderer({
      canvas: this.canvas,
      context,
      antialias: false,
      alpha: false,
      powerPreference: 'high-performance',
    });
    // Every pass targets an explicit buffer and either overwrites or accumulates
    // into it; letting three clear behind our back would wipe the trail history.
    this.renderer.autoClear = false;
    this.renderer.info.autoReset = false;
    this.renderer.setClearColor(0x000000, 1);
    this.renderer.outputColorSpace = THREE.LinearSRGBColorSpace;

    this.quad = new FullscreenQuad();
    this.effect = new FiberFlowEffect(this.renderer, this.quad, this.settings, this.view);
    this.post = new PostPipeline(this.quad, 1, 1);
    this.post.applySettings(this.settings);

    this.adaptive = new AdaptiveQuality(this.settings.adaptiveQuality, this.targetFps());

    this.overlay = new DebugOverlay(container, describeGpu(this.renderer));
    this.overlay.setVisible(this.settings.showDebugOverlay);

    this.resizeObserver = new ResizeObserver(() => {
      this.resizePending = true;
    });
    this.resizeObserver.observe(container);

    this.canvas.addEventListener('webglcontextlost', this.onContextLost, false);

    this.applyResize();
    log.info('renderer ready', {
      density: this.settings.density,
      seed: this.settings.seed,
      monitor: this.view.monitorIndex,
      synchronized: this.view.synchronized,
    });
  }

  private readonly onContextLost = (event: Event): void => {
    event.preventDefault();
    log.error('WebGL context lost');
    this.stop();
    // A frozen screensaver is worse than none: tell the host so it can close.
    this.bridge.post({ type: 'contextLost' });
  };

  private targetFps(): number {
    if (this.settings.fpsLimit > 0) return this.settings.fpsLimit;
    // Unlimited: aim just under a typical refresh rate rather than chasing
    // whatever the display happens to be capable of.
    return 58;
  }

  applySettings(settings: FlowScreenSettings): void {
    const scaleChanged = settings.renderScale !== this.settings.renderScale;
    this.settings = settings;

    this.effect.applySettings(settings);
    this.post.applySettings(settings);
    this.adaptive.configure(settings.adaptiveQuality, this.targetFps());
    this.overlay.setVisible(settings.showDebugOverlay);
    if (scaleChanged) this.resizePending = true;
  }

  private applyResize(): void {
    this.resizePending = false;

    const dpr = Math.min(window.devicePixelRatio || 1, MAX_DEVICE_PIXEL_RATIO);
    const rect = this.container.getBoundingClientRect();
    this.cssWidth = Math.max(1, Math.round(rect.width));
    this.cssHeight = Math.max(1, Math.round(rect.height));

    const scale = this.settings.renderScale * this.appliedRenderScale;
    const width = Math.max(64, Math.round(this.cssWidth * dpr * scale));
    const height = Math.max(64, Math.round(this.cssHeight * dpr * scale));

    if (width === this.bufferWidth && height === this.bufferHeight) return;

    this.bufferWidth = width;
    this.bufferHeight = height;

    this.renderer.setSize(width, height, false);
    this.canvas.style.width = '100%';
    this.canvas.style.height = '100%';

    this.post.resize(width, height);
    this.effect.resize(width, height, this.view);
  }

  setViewContext(view: ViewContext): void {
    this.view = view;
    this.effect.resize(this.bufferWidth, this.bufferHeight, view);
  }

  start(): void {
    if (this.running) return;
    this.running = true;
    this.perf.reset();
    this.lastRenderMs = 0;
    this.rafId = requestAnimationFrame(this.frame);
  }

  stop(): void {
    if (!this.running) return;
    this.running = false;
    cancelAnimationFrame(this.rafId);
    this.rafId = 0;
  }

  private readonly frame = (timestamp: number): void => {
    if (!this.running) return;
    this.rafId = requestAnimationFrame(this.frame);

    // Frame limiter. `vsync` cannot be switched off from inside the page - the
    // host passes --disable-gpu-vsync to the WebView for that - so all this does
    // is skip presents we do not want.
    const limit = this.settings.fpsLimit;
    if (limit > 0) {
      const minInterval = 1000 / limit - 0.75;
      if (this.lastRenderMs !== 0 && timestamp - this.lastRenderMs < minInterval) return;
      this.lastRenderMs = timestamp;
    }

    const dt = this.perf.beginFrame(timestamp);
    this.uptime += dt;

    if (this.resizePending) this.applyResize();

    if (this.adaptive.update(dt, this.perf.smoothed)) {
      const level = this.adaptive.level;
      this.effect.setQualityScale(level.particles);
      if (level.renderScale !== this.appliedRenderScale) {
        this.appliedRenderScale = level.renderScale;
        this.applyResize();
      }
    }

    const time = (performance.timeOrigin + timestamp - this.view.epochMs) / 1000;

    this.renderer.info.reset();
    this.effect.update(this.renderer, { time, dt, audio: this.audio.sample(dt) });
    this.post.render(this.renderer, this.effect.scene, this.effect.camera, time, dt);

    if (this.overlay.isVisible) {
      const stats = this.effect.stats;
      this.overlay.update(dt, {
        fps: this.perf.fps,
        frameTimeMs: this.perf.frameTimeMs,
        activeCount: stats.activeCount,
        capacity: stats.capacity,
        bufferWidth: this.bufferWidth,
        bufferHeight: this.bufferHeight,
        cssWidth: this.cssWidth,
        cssHeight: this.cssHeight,
        renderScale: this.settings.renderScale * this.appliedRenderScale,
        qualityStep: this.adaptive.currentStep,
        drawCalls: this.renderer.info.render.calls,
        uptimeSeconds: this.uptime,
      });
    }
  };

  toggleOverlay(): void {
    this.overlay.setVisible(!this.overlay.isVisible);
  }

  dispose(): void {
    this.stop();
    this.canvas.removeEventListener('webglcontextlost', this.onContextLost);
    this.resizeObserver.disconnect();
    this.overlay.dispose();
    this.post.dispose();
    this.effect.dispose();
    this.quad.dispose();
    this.audio.dispose();
    this.renderer.dispose();
    this.canvas.remove();
  }
}
