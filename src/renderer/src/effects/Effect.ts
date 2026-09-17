import type * as THREE from 'three';
import type { AudioLevels, EffectId, FlowScreenSettings, ViewContext } from '../config/types';

export interface FrameContext {
  /** Seconds since the shared scene epoch. Never wraps. */
  readonly time: number;
  /** Seconds since the previous frame, already clamped to a sane range. */
  readonly dt: number;
  readonly audio: AudioLevels;
}

export interface EffectStats {
  /** Elements actually submitted this frame (adaptive quality may reduce it). */
  readonly activeCount: number;
  /** Elements the effect is capable of drawing at the current density. */
  readonly capacity: number;
}

/**
 * A self-contained visual. `FiberFlowEffect` is the only implementation today;
 * the interface exists so Liquid / Galaxy / AudioReactive can be dropped in
 * without touching the app shell or the post pipeline.
 */
export interface Effect {
  readonly id: EffectId;
  readonly scene: THREE.Scene;
  readonly camera: THREE.PerspectiveCamera;
  readonly stats: EffectStats;

  applySettings(settings: FlowScreenSettings): void;
  resize(width: number, height: number, view: ViewContext): void;
  /** 0..1 fraction of the population to draw. Adaptive quality's main lever. */
  setQualityScale(scale: number): void;
  update(renderer: THREE.WebGLRenderer, ctx: FrameContext): void;
  dispose(): void;
}
