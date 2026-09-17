import type { FlowScreenSettings, Rect, ViewContext } from './types';
import { SETTINGS_VERSION } from './types';

export const DEFAULT_CUSTOM_PALETTE: readonly string[] = [
  '#1A0033',
  '#4C0C72',
  '#9A1C93',
  '#F04BA2',
  '#FF9DD2',
  '#FFF4FA',
];

export const DEFAULT_SETTINGS: FlowScreenSettings = {
  version: SETTINGS_VERSION,
  effect: 'fiberFlow',
  density: 'high',
  speed: 1.0,
  fiberLength: 1.0,
  glow: 1.0,
  trail: 0.85,
  palette: 'pink',
  customPalette: [...DEFAULT_CUSTOM_PALETTE],
  fpsLimit: 60,
  vsync: true,
  renderScale: 1.0,
  adaptiveQuality: 'balanced',
  multiMonitorMode: 'synchronized',
  randomSeedOnLaunch: true,
  seed: 12345,
  showDebugOverlay: false,
};

const clamp = (v: number, lo: number, hi: number): number =>
  Number.isFinite(v) ? Math.min(hi, Math.max(lo, v)) : lo;

const clampInt = (v: unknown, lo: number, hi: number, fallback: number): number => {
  const n = Math.trunc(Number(v));
  return Number.isFinite(n) && n >= lo && n <= hi ? n : fallback;
};

const oneOf = <T extends string>(v: unknown, allowed: readonly T[], fallback: T): T =>
  allowed.includes(v as T) ? (v as T) : fallback;

const HEX_RE = /^#[0-9a-fA-F]{6}$/;

/**
 * Coerces anything (a parsed JSON blob, a partial object from the host) into a
 * valid settings object. Never throws: a screensaver that refuses to start
 * because of one bad field is worse than one that starts with defaults.
 */
export function normaliseSettings(input: unknown): FlowScreenSettings {
  const raw = (typeof input === 'object' && input !== null ? input : {}) as Partial<FlowScreenSettings>;

  const custom = Array.isArray(raw.customPalette)
    ? raw.customPalette.filter((c): c is string => typeof c === 'string' && HEX_RE.test(c))
    : [];

  return {
    version: SETTINGS_VERSION,
    effect: 'fiberFlow',
    density: oneOf(raw.density, ['low', 'medium', 'high', 'ultra'] as const, DEFAULT_SETTINGS.density),
    speed: clamp(Number(raw.speed ?? DEFAULT_SETTINGS.speed), 0.1, 3),
    fiberLength: clamp(Number(raw.fiberLength ?? DEFAULT_SETTINGS.fiberLength), 0.2, 3),
    glow: clamp(Number(raw.glow ?? DEFAULT_SETTINGS.glow), 0, 2),
    trail: clamp(Number(raw.trail ?? DEFAULT_SETTINGS.trail), 0, 0.95),
    palette: oneOf(raw.palette, ['pink', 'purple', 'blue', 'cyber', 'mono', 'custom'] as const, DEFAULT_SETTINGS.palette),
    customPalette: custom.length === 6 ? custom : [...DEFAULT_CUSTOM_PALETTE],
    fpsLimit: clampInt(raw.fpsLimit, 0, 480, DEFAULT_SETTINGS.fpsLimit),
    vsync: raw.vsync ?? DEFAULT_SETTINGS.vsync,
    renderScale: [0.5, 0.75, 1].includes(Number(raw.renderScale)) ? Number(raw.renderScale) : 1,
    adaptiveQuality: oneOf(raw.adaptiveQuality, ['off', 'balanced', 'performance'] as const, DEFAULT_SETTINGS.adaptiveQuality),
    multiMonitorMode: oneOf(raw.multiMonitorMode, ['independent', 'synchronized'] as const, DEFAULT_SETTINGS.multiMonitorMode),
    randomSeedOnLaunch: raw.randomSeedOnLaunch ?? DEFAULT_SETTINGS.randomSeedOnLaunch,
    seed: Number.isFinite(Number(raw.seed)) ? Math.abs(Math.trunc(Number(raw.seed))) % 2147483647 : DEFAULT_SETTINGS.seed,
    showDebugOverlay: raw.showDebugOverlay ?? DEFAULT_SETTINGS.showDebugOverlay,
  };
}

export function defaultViewContext(width: number, height: number): ViewContext {
  const rect: Rect = { x: 0, y: 0, w: width, h: height };
  return {
    monitorIndex: 0,
    monitorCount: 1,
    synchronized: false,
    epochMs: Date.now(),
    bounds: rect,
    virtual: { ...rect },
  };
}
