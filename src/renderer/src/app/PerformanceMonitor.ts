/**
 * Rolling frame-time statistics.
 *
 * Everything is kept in preallocated typed arrays and updated in place: a
 * screensaver runs for hours, and a per-frame allocation here is a guaranteed
 * GC hitch every few minutes.
 */
export class PerformanceMonitor {
  private readonly samples: Float32Array;
  private cursor = 0;
  private filled = 0;
  private sum = 0;

  private lastTimestamp = 0;
  private smoothedFps = 60;

  constructor(windowSize = 120) {
    this.samples = new Float32Array(windowSize);
  }

  /**
   * @param timestamp high-resolution timestamp, milliseconds
   * @returns delta time in seconds, clamped to a sane range
   */
  beginFrame(timestamp: number): number {
    if (this.lastTimestamp === 0) {
      this.lastTimestamp = timestamp;
      return 1 / 60;
    }

    const rawMs = timestamp - this.lastTimestamp;
    this.lastTimestamp = timestamp;

    // A stall (minimised window, GPU reset, resume from sleep) must not be
    // integrated into the simulation as a single enormous step.
    const ms = Math.min(Math.max(rawMs, 0.1), 250);

    const previous = this.samples[this.cursor];
    this.sum += ms - previous;
    this.samples[this.cursor] = ms;
    this.cursor = (this.cursor + 1) % this.samples.length;
    if (this.filled < this.samples.length) this.filled++;

    const instant = 1000 / ms;
    // Heavy smoothing: adaptive quality must react to a sustained drop, not to
    // one slow frame caused by something else on the machine.
    this.smoothedFps += (instant - this.smoothedFps) * 0.06;

    return ms / 1000;
  }

  /** Mean frame time over the window, in milliseconds. */
  get frameTimeMs(): number {
    return this.filled === 0 ? 0 : this.sum / this.filled;
  }

  get fps(): number {
    const ft = this.frameTimeMs;
    return ft > 0 ? 1000 / ft : 0;
  }

  /** Exponentially smoothed FPS, used for quality decisions. */
  get smoothed(): number {
    return this.smoothedFps;
  }

  reset(): void {
    this.samples.fill(0);
    this.cursor = 0;
    this.filled = 0;
    this.sum = 0;
    this.lastTimestamp = 0;
    this.smoothedFps = 60;
  }
}
