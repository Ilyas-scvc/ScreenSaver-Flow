import type { AdaptiveQualityMode } from '../config/types';
import { log } from '../core/Log';

export interface QualityLevel {
  /** Fraction of the fiber population to draw. */
  readonly particles: number;
  /** Multiplier on the configured render scale. */
  readonly renderScale: number;
}

/**
 * Degradation ladder. Population is sacrificed before resolution, because
 * dropping fibers costs the image far less than blurring it: at 60% of the
 * fibers the composition is unchanged, whereas at 60% resolution every fiber
 * loses its crisp edge.
 */
const LADDER: readonly QualityLevel[] = [
  { particles: 1.0, renderScale: 1.0 },
  { particles: 0.8, renderScale: 1.0 },
  { particles: 0.62, renderScale: 1.0 },
  { particles: 0.62, renderScale: 0.85 },
  { particles: 0.45, renderScale: 0.85 },
  { particles: 0.45, renderScale: 0.72 },
  { particles: 0.3, renderScale: 0.72 },
  { particles: 0.22, renderScale: 0.6 },
];

const MAX_STEP: Readonly<Record<AdaptiveQualityMode, number>> = {
  off: 0,
  balanced: 5,
  performance: LADDER.length - 1,
};

/** Seconds below target before stepping down. Short: stutter is intolerable. */
const DEGRADE_AFTER = 2.5;
/** Seconds comfortably above target before stepping back up. Long: recovering
 *  too eagerly produces a quality oscillation that is worse than either state. */
const RECOVER_AFTER = 9.0;

export class AdaptiveQuality {
  private mode: AdaptiveQualityMode;
  private targetFps: number;
  private step = 0;
  private belowFor = 0;
  private aboveFor = 0;

  constructor(mode: AdaptiveQualityMode, targetFps: number) {
    this.mode = mode;
    this.targetFps = targetFps;
    if (mode === 'performance') this.step = 1;
  }

  configure(mode: AdaptiveQualityMode, targetFps: number): void {
    if (mode !== this.mode) {
      this.step = mode === 'performance' ? 1 : 0;
      this.belowFor = 0;
      this.aboveFor = 0;
    }
    this.mode = mode;
    this.targetFps = targetFps;
  }

  get level(): QualityLevel {
    return LADDER[this.step];
  }

  get currentStep(): number {
    return this.step;
  }

  /** @returns true when the level changed and the caller must reapply it. */
  update(dt: number, smoothedFps: number): boolean {
    if (this.mode === 'off') {
      if (this.step === 0) return false;
      this.step = 0;
      return true;
    }

    const maxStep = MAX_STEP[this.mode];
    const degradeBelow = this.targetFps - 7;
    const recoverAbove = this.targetFps - 1;

    if (smoothedFps < degradeBelow) {
      this.belowFor += dt;
      this.aboveFor = 0;
    } else if (smoothedFps > recoverAbove) {
      this.aboveFor += dt;
      this.belowFor = 0;
    } else {
      this.belowFor = Math.max(0, this.belowFor - dt * 0.5);
      this.aboveFor = Math.max(0, this.aboveFor - dt * 0.5);
    }

    if (this.belowFor >= DEGRADE_AFTER && this.step < maxStep) {
      this.step++;
      this.belowFor = 0;
      log.info('adaptive quality: down to step', this.step, 'at', smoothedFps.toFixed(1), 'fps');
      return true;
    }

    const floor = this.mode === 'performance' ? 1 : 0;
    if (this.aboveFor >= RECOVER_AFTER && this.step > floor) {
      this.step--;
      this.aboveFor = 0;
      log.info('adaptive quality: up to step', this.step, 'at', smoothedFps.toFixed(1), 'fps');
      return true;
    }

    return false;
  }
}
