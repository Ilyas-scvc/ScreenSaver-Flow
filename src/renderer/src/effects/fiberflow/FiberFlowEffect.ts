import * as THREE from 'three';
import type { Effect, EffectStats, FrameContext } from '../Effect';
import type { EffectId, FlowScreenSettings, ViewContext } from '../../config/types';
import { DENSITY_PRESETS } from '../../config/quality';
import { buildPaletteTexture, resolveIntensity, resolveStops } from '../../config/palettes';
import { defaultViewContext } from '../../config/defaults';
import type { FullscreenQuad } from '../../core/FullscreenQuad';
import { log } from '../../core/Log';
import { CameraRig } from './CameraRig';
import { FiberRenderer } from './FiberRenderer';
import { FiberSimulation } from './FiberSimulation';
import { FlowFieldController } from './FlowFieldController';

const DEG2RAD = Math.PI / 180;

/**
 * FiberFlow: tens of thousands of short glowing capsules advected through a
 * layered curl-noise field.
 *
 * Per frame the effect issues exactly two draw calls, one to integrate the
 * simulation and one to draw every fiber, and touches no per-particle data on
 * the CPU at all.
 */
export class FiberFlowEffect implements Effect {
  readonly id: EffectId = 'fiberFlow';
  readonly scene = new THREE.Scene();

  private readonly quad: FullscreenQuad;
  private readonly rig: CameraRig;
  private readonly field: FlowFieldController;
  private readonly palette: THREE.DataTexture;
  private readonly seed: number;

  private simulation: FiberSimulation;
  private fibers: FiberRenderer;

  private settings: FlowScreenSettings;
  private view: ViewContext;
  private width = 1920;
  private height = 1080;

  private simTime = 0;
  private qualityScale = 1;
  private pendingDensityRebuild = false;

  constructor(
    renderer: THREE.WebGLRenderer,
    quad: FullscreenQuad,
    settings: FlowScreenSettings,
    view: ViewContext,
  ) {
    this.quad = quad;
    this.settings = settings;
    this.view = view;
    this.seed = settings.seed;

    this.field = new FlowFieldController(this.seed);
    this.rig = new CameraRig(this.seed);

    this.palette = buildPaletteTexture(
      resolveStops(settings.palette, settings.customPalette),
      resolveIntensity(settings.palette),
    );

    const preset = DENSITY_PRESETS[settings.density];
    this.simulation = new FiberSimulation(renderer, quad, this.field, {
      texSize: preset.texSize,
      seed: this.seed,
    });
    this.fibers = new FiberRenderer(preset.texSize, this.palette);
    this.scene.add(this.fibers.mesh);

    this.rig.resize(this.width, this.height, this.view);
    this.applyTunables();
  }

  get camera(): THREE.PerspectiveCamera {
    return this.rig.camera;
  }

  get stats(): EffectStats {
    return { activeCount: this.fibers.activeCount, capacity: this.fibers.maxCount };
  }

  applySettings(settings: FlowScreenSettings): void {
    const densityChanged = settings.density !== this.settings.density;
    this.settings = settings;
    if (densityChanged) this.pendingDensityRebuild = true;

    buildPaletteTexture(
      resolveStops(settings.palette, settings.customPalette),
      resolveIntensity(settings.palette),
      this.palette,
    );
    this.applyTunables();
  }

  resize(width: number, height: number, view: ViewContext): void {
    this.width = Math.max(1, width);
    this.height = Math.max(1, height);
    this.view = view;
    this.rig.resize(this.width, this.height, view);
    this.applyTunables();
  }

  setQualityScale(scale: number): void {
    this.qualityScale = THREE.MathUtils.clamp(scale, 0.15, 1);
    this.fibers.setActiveFraction(this.qualityScale);
  }

  /** Pushes every settings-derived value into the two materials. */
  private applyTunables(): void {
    const s = this.settings;
    const preset = DENSITY_PRESETS[s.density];

    // Temporal persistence accumulates energy: at trail 0.85 a static fiber ends
    // up ~6x brighter than a single frame. Compensating here keeps the trail
    // slider a look control instead of a second exposure control.
    const trailGain = THREE.MathUtils.clamp((1 - s.trail) / 0.45, 0.28, 2.0);

    const f = this.fibers.material.uniforms;
    f.uFiberLength.value = 0.27 * s.fiberLength;
    f.uFiberWidth.value = preset.fiberWidth;
    f.uBrightness.value = preset.brightness * trailGain;

    this.fibers.setActiveFraction(this.qualityScale);
    this.updatePixelScale();
  }

  /**
   * World units covered by one pixel, per unit of view depth. The fiber shader
   * uses this to guarantee a minimum on-screen width, which is what keeps the
   * far field from shimmering as fibers cross pixel centres.
   */
  private updatePixelScale(): void {
    const fovRad = this.rig.camera.fov * DEG2RAD;
    const pixels = Math.max(1, this.rig.fullFovPixelHeight());
    this.fibers.material.uniforms.uPixelScale.value = (2 * Math.tan(fovRad * 0.5)) / pixels;
  }

  /**
   * Drifts the visibility field. Slow and on incommensurate periods, so the
   * luminous body keeps reforming without ever repeating a shape.
   */
  private updateMask(time: number): void {
    const offset = this.fibers.material.uniforms.uMaskOffset.value as THREE.Vector3;
    offset.set(
      Math.sin(time / 137) * 9 + time * 0.012,
      Math.sin(time / 211 + 1.7) * 7,
      Math.sin(time / 173 + 3.1) * 6 - time * 0.008,
    );
  }

  update(renderer: THREE.WebGLRenderer, ctx: FrameContext): void {
    if (this.pendingDensityRebuild) {
      this.rebuildDensity(renderer);
      this.pendingDensityRebuild = false;
    }

    // The user-facing "speed" scales simulation time, not the frame rate, so the
    // result is identical at 30, 60 or 144 Hz.
    const dt = Math.min(ctx.dt, 1 / 15) * this.settings.speed;
    this.simTime += dt;

    this.field.update(this.simTime);
    this.rig.update(this.simTime);
    this.updateMask(this.simTime);
    this.updatePixelScale();

    const audio = ctx.audio;
    const audioVec = this.fibers.material.uniforms.uAudio.value as THREE.Vector4;
    audioVec.set(audio.bass, audio.mid, audio.treble, audio.volume);
    (this.simulation.uniforms.uAudio.value as THREE.Vector4).copy(audioVec);

    this.simulation.step(renderer, this.simTime, dt);
    this.fibers.setSimulationTextures(
      this.simulation.positionTexture,
      this.simulation.velocityTexture,
    );
  }

  private rebuildDensity(renderer: THREE.WebGLRenderer): void {
    const preset = DENSITY_PRESETS[this.settings.density];
    if (preset.texSize === this.simulation.texSize) return;

    log.info('rebuilding at density', this.settings.density, preset.count, 'fibers');

    this.scene.remove(this.fibers.mesh);
    this.fibers.dispose();
    this.simulation.dispose();

    this.simulation = new FiberSimulation(renderer, this.quad, this.field, {
      texSize: preset.texSize,
      seed: this.seed,
    });
    this.fibers = new FiberRenderer(preset.texSize, this.palette);
    this.scene.add(this.fibers.mesh);
    this.applyTunables();
  }

  dispose(): void {
    this.scene.remove(this.fibers.mesh);
    this.fibers.dispose();
    this.simulation.dispose();
    this.palette.dispose();
  }
}

export function createFiberFlowEffect(
  renderer: THREE.WebGLRenderer,
  quad: FullscreenQuad,
  settings: FlowScreenSettings,
  view?: ViewContext,
): FiberFlowEffect {
  return new FiberFlowEffect(renderer, quad, settings, view ?? defaultViewContext(1920, 1080));
}
