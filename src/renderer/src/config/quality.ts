import type { DensityLevel } from './types';

export interface DensityPreset {
  /** Side of the square simulation texture. */
  readonly texSize: number;
  /** texSize^2 fibers. */
  readonly count: number;
  /** World-space half width of a fiber. */
  readonly fiberWidth: number;
  /** Per-fiber emission. Denser populations use dimmer, thinner fibers so the
   *  total additive energy - and therefore how much bloom the frame produces -
   *  stays in the same range across presets. */
  readonly brightness: number;
}

function preset(texSize: number, fiberWidth: number, brightness: number): DensityPreset {
  return { texSize, count: texSize * texSize, fiberWidth, brightness };
}

export const DENSITY_PRESETS: Readonly<Record<DensityLevel, DensityPreset>> = {
  low: preset(160, 0.0160, 0.50),
  medium: preset(256, 0.0126, 0.35),
  high: preset(352, 0.0100, 0.255),
  ultra: preset(480, 0.0080, 0.190),
};

export const DENSITY_ORDER: readonly DensityLevel[] = ['low', 'medium', 'high', 'ultra'];
