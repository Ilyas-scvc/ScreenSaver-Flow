/**
 * The settings contract shared with the Windows host.
 *
 * These names must stay in sync with `src/windows/FlowScreen/Settings/FlowScreenSettings.cs`.
 * The host serialises exactly this shape into the URL and into
 * `%LOCALAPPDATA%\FlowScreen\settings.json`.
 */

export const SETTINGS_VERSION = 1;

export type EffectId = 'fiberFlow';
export type DensityLevel = 'low' | 'medium' | 'high' | 'ultra';
export type PaletteId = 'pink' | 'purple' | 'blue' | 'cyber' | 'mono' | 'custom';
export type AdaptiveQualityMode = 'off' | 'balanced' | 'performance';
export type MultiMonitorMode = 'independent' | 'synchronized';

export interface FlowScreenSettings {
  version: number;
  effect: EffectId;
  /** Fiber population bucket. */
  density: DensityLevel;
  /** Global time multiplier, 0.1 - 3.0. */
  speed: number;
  /** Fiber length multiplier, 0.2 - 3.0. */
  fiberLength: number;
  /** Bloom strength multiplier, 0.0 - 2.0. */
  glow: number;
  /** Temporal persistence, 0.0 - 0.95. */
  trail: number;
  palette: PaletteId;
  /** Six sRGB hex stops, dark to light. Only used when palette === 'custom'. */
  customPalette: string[];
  /** 0 means unlimited. */
  fpsLimit: number;
  vsync: boolean;
  /** 0.5 | 0.75 | 1.0 */
  renderScale: number;
  adaptiveQuality: AdaptiveQualityMode;
  multiMonitorMode: MultiMonitorMode;
  randomSeedOnLaunch: boolean;
  seed: number;
  showDebugOverlay: boolean;
}

export interface Rect {
  x: number;
  y: number;
  w: number;
  h: number;
}

/**
 * Per-window context supplied by the host. In `synchronized` mode every window
 * shares `seed` and `epochMs` and receives its own slice of `virtual`, which the
 * camera turns into a view offset so the scene is genuinely continuous across
 * physical displays.
 */
export interface ViewContext {
  monitorIndex: number;
  monitorCount: number;
  synchronized: boolean;
  /** Shared wall-clock origin (ms since epoch) so all windows agree on t=0. */
  epochMs: number;
  /** This monitor, in virtual-desktop pixels. */
  bounds: Rect;
  /** The whole virtual desktop, in virtual-desktop pixels. */
  virtual: Rect;
}

export interface RendererPayload {
  settings: FlowScreenSettings;
  view: ViewContext;
}

/** Audio analysis levels. Reserved: always zero until audio reactivity lands. */
export interface AudioLevels {
  bass: number;
  mid: number;
  treble: number;
  volume: number;
}
