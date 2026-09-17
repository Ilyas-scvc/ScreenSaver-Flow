import * as THREE from 'three';
import type { PaletteId } from './types';

/**
 * Palettes are six sRGB stops ordered dark -> light. They are baked into a
 * 256x1 lookup texture in *linear* space so the shader can pick a colour with a
 * single fetch, and so interpolating between stops does not desaturate the way
 * blending sRGB values does.
 */
export interface PaletteDefinition {
  readonly id: PaletteId;
  readonly label: string;
  readonly stops: readonly string[];
  /** Multiplier applied to the LUT; > 1 pushes the light end into HDR so the
   *  brightest fibers survive tone mapping as real highlights. */
  readonly intensity: number;
}

export const PALETTES: Readonly<Record<Exclude<PaletteId, 'custom'>, PaletteDefinition>> = {
  pink: {
    id: 'pink',
    label: 'Pink',
    stops: ['#1A0033', '#4C0C72', '#9A1C93', '#F04BA2', '#FF9DD2', '#FFF4FA'],
    intensity: 1.15,
  },
  purple: {
    id: 'purple',
    label: 'Purple',
    stops: ['#1E0533', '#4B1080', '#8A2BE2', '#B36BFF', '#DDB6FF', '#F6ECFF'],
    intensity: 1.12,
  },
  blue: {
    id: 'blue',
    label: 'Blue',
    stops: ['#001B3A', '#0A3D82', '#1E7BE0', '#5FB8FF', '#A8DDFF', '#EDF8FF'],
    intensity: 1.1,
  },
  cyber: {
    id: 'cyber',
    label: 'Cyber',
    stops: ['#04122B', '#0E5CA8', '#12D8E0', '#B14BFF', '#FF2ECC', '#FFF3FF'],
    intensity: 1.2,
  },
  mono: {
    id: 'mono',
    label: 'Monochrome',
    stops: ['#0A0A0C', '#2A2A30', '#5A5A66', '#9A9AA8', '#D2D2DC', '#FFFFFF'],
    intensity: 1.0,
  },
};

const LUT_SIZE = 256;

function hexToLinear(hex: string): [number, number, number] {
  const c = new THREE.Color();
  // setStyle interprets the string as sRGB, convertSRGBToLinear moves it into
  // the working space the shader actually blends in.
  c.setStyle(hex, THREE.SRGBColorSpace);
  c.convertSRGBToLinear();
  return [c.r, c.g, c.b];
}

function smoothstep(t: number): number {
  return t * t * (3 - 2 * t);
}

export function resolveStops(id: PaletteId, custom: readonly string[]): readonly string[] {
  if (id === 'custom') {
    return custom.length === 6 ? custom : PALETTES.pink.stops;
  }
  return PALETTES[id].stops;
}

export function resolveIntensity(id: PaletteId): number {
  return id === 'custom' ? 1.15 : PALETTES[id].intensity;
}

/** Builds (or refills) the 256x1 RGBA float LUT sampled by the fiber shader. */
export function buildPaletteTexture(
  stops: readonly string[],
  intensity: number,
  existing?: THREE.DataTexture,
): THREE.DataTexture {
  const data = existing ? (existing.image.data as Float32Array) : new Float32Array(LUT_SIZE * 4);
  const linear = stops.map(hexToLinear);
  const segments = linear.length - 1;

  for (let i = 0; i < LUT_SIZE; i++) {
    const u = i / (LUT_SIZE - 1);
    const scaled = u * segments;
    const idx = Math.min(segments - 1, Math.floor(scaled));
    const t = smoothstep(scaled - idx);

    const a = linear[idx];
    const b = linear[idx + 1];

    const o = i * 4;
    data[o + 0] = (a[0] + (b[0] - a[0]) * t) * intensity;
    data[o + 1] = (a[1] + (b[1] - a[1]) * t) * intensity;
    data[o + 2] = (a[2] + (b[2] - a[2]) * t) * intensity;
    data[o + 3] = 1;
  }

  if (existing) {
    existing.needsUpdate = true;
    return existing;
  }

  const tex = new THREE.DataTexture(data, LUT_SIZE, 1, THREE.RGBAFormat, THREE.FloatType);
  tex.magFilter = THREE.LinearFilter;
  tex.minFilter = THREE.LinearFilter;
  tex.wrapS = THREE.RepeatWrapping;
  tex.wrapT = THREE.ClampToEdgeWrapping;
  tex.generateMipmaps = false;
  tex.needsUpdate = true;
  return tex;
}
