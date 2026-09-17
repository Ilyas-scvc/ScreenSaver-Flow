/**
 * Deterministic PRNG. `mulberry32` is used because it is 4 lines, has a period
 * long enough for anything we seed, and produces identical streams in the
 * renderer and (if ever needed) in a port - which is what makes a seed
 * reproducible across launches.
 */
export class Rng {
  private state: number;

  constructor(seed: number) {
    // Force to a well-distributed 32-bit state; small integer seeds like 1, 2, 3
    // would otherwise produce visibly similar first draws.
    this.state = (Math.imul(seed ^ 0x9e3779b9, 0x85ebca6b) >>> 0) || 0x1a2b3c4d;
  }

  next(): number {
    this.state = (this.state + 0x6d2b79f5) >>> 0;
    let t = this.state;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  }

  /** Uniform in [min, max). */
  range(min: number, max: number): number {
    return min + (max - min) * this.next();
  }

  /** Uniform in [-1, 1). */
  signed(): number {
    return this.next() * 2 - 1;
  }

  /** Approximately normal, mean 0, sd ~0.4. Cheap sum-of-uniforms. */
  gaussian(): number {
    return (this.next() + this.next() + this.next() - 1.5) * 0.8;
  }
}

/** 32-bit string hash, used to derive per-monitor seeds. */
export function hashString(s: string): number {
  let h = 2166136261;
  for (let i = 0; i < s.length; i++) {
    h ^= s.charCodeAt(i);
    h = Math.imul(h, 16777619);
  }
  return h >>> 0;
}
