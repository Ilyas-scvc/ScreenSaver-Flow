import * as THREE from 'three';
import { Rng } from '../../core/Rng';

export const VORTEX_COUNT = 4;

/** World-space half-extents of the volume the fibers live in. */
export const FIELD_BOUNDS = new THREE.Vector3(16, 10, 12);
/** Where new fibers are seeded. Smaller than the bounds so nothing spawns on
 *  top of the soft wall. */
export const SPAWN_EXTENT = new THREE.Vector3(15, 9, 6.0);

export interface FlowFieldUniforms {
  uSeedOffset: { value: THREE.Vector3 };
  uBounds: { value: THREE.Vector3 };
  uFlowScale: { value: number };
  uWarpAmount: { value: number };
  uTurbulence: { value: number };
  uMacroAmp: { value: number };
  uSheetAmp: { value: number };
  uSheetFreq: { value: number };
  uSheetPull: { value: number };
  uContain: { value: number };
  uDrift: { value: THREE.Vector3 };
  uVortexPos: { value: THREE.Vector4[] };
  uVortexAxis: { value: THREE.Vector4[] };
}

interface VortexPlan {
  readonly centreAmp: THREE.Vector3;
  readonly centrePeriod: THREE.Vector3;
  readonly centrePhase: THREE.Vector3;
  readonly axis: THREE.Vector3;
  readonly axisPeriod: number;
  readonly strength: number;
  readonly strengthPeriod: number;
  readonly strengthPhase: number;
  readonly radius: number;
  readonly radiusPeriod: number;
}

/**
 * Owns everything about the field that is cheaper to compute once per frame on
 * the CPU than per texel on the GPU: the vortex placement and the very slow
 * drift of the global parameters.
 *
 * All periods are long (60-900 s) and mutually incommensurate, so the composite
 * state never revisits itself in any practical viewing session. That is the
 * whole answer to "must not look cyclic after ten seconds".
 */
export class FlowFieldController {
  readonly uniforms: FlowFieldUniforms;

  private readonly vortices: VortexPlan[] = [];
  private readonly base: {
    warp: number;
    turbulence: number;
    macro: number;
    sheetAmp: number;
    sheetFreq: number;
    sheetPull: number;
    driftPeriod: THREE.Vector3;
    driftPhase: THREE.Vector3;
  };

  constructor(seed: number) {
    const rng = new Rng(seed);

    this.uniforms = {
      uSeedOffset: { value: new THREE.Vector3(rng.range(-400, 400), rng.range(-400, 400), rng.range(-400, 400)) },
      uBounds: { value: FIELD_BOUNDS.clone() },
      uFlowScale: { value: 0.19 },
      uWarpAmount: { value: 0.9 },
      uTurbulence: { value: 1.0 },
      uMacroAmp: { value: 1.15 },
      uSheetAmp: { value: 3.2 },
      uSheetFreq: { value: 0.055 },
      uSheetPull: { value: 0.55 },
      uContain: { value: 0.55 },
      uDrift: { value: new THREE.Vector3() },
      uVortexPos: { value: Array.from({ length: VORTEX_COUNT }, () => new THREE.Vector4()) },
      uVortexAxis: { value: Array.from({ length: VORTEX_COUNT }, () => new THREE.Vector4()) },
    };

    this.base = {
      warp: 0.9,
      turbulence: 1.0,
      macro: 1.15,
      sheetAmp: 3.2,
      sheetFreq: 0.055,
      sheetPull: 0.55,
      driftPeriod: new THREE.Vector3(rng.range(180, 320), rng.range(210, 380), rng.range(240, 420)),
      driftPhase: new THREE.Vector3(rng.range(0, 6.28), rng.range(0, 6.28), rng.range(0, 6.28)),
    };

    for (let i = 0; i < VORTEX_COUNT; i++) {
      const axis = new THREE.Vector3(rng.signed() * 0.55, rng.signed() * 0.55, rng.range(0.55, 1))
        .normalize();
      this.vortices.push({
        centreAmp: new THREE.Vector3(
          FIELD_BOUNDS.x * rng.range(0.25, 0.62),
          FIELD_BOUNDS.y * rng.range(0.2, 0.55),
          FIELD_BOUNDS.z * rng.range(0.15, 0.42),
        ),
        centrePeriod: new THREE.Vector3(rng.range(97, 233), rng.range(113, 281), rng.range(151, 337)),
        centrePhase: new THREE.Vector3(rng.range(0, 6.28), rng.range(0, 6.28), rng.range(0, 6.28)),
        axis,
        axisPeriod: rng.range(240, 620),
        strength: rng.range(0.45, 1.05) * (rng.next() < 0.5 ? -1 : 1),
        strengthPeriod: rng.range(140, 330),
        strengthPhase: rng.range(0, 6.28),
        radius: rng.range(3.4, 7.2),
        radiusPeriod: rng.range(190, 430),
      });
    }
  }

  /** `time` is unbounded seconds since the scene epoch. */
  update(time: number): void {
    const u = this.uniforms;
    const b = this.base;

    // Slow breathing of the global character of the field. Ranges are narrow on
    // purpose: the goal is "it is never quite the same", not "it changes mood".
    const osc = (period: number, phase: number): number => Math.sin((time / period) * Math.PI * 2 + phase);

    u.uWarpAmount.value = b.warp * (1 + 0.30 * osc(287, 0.7));
    u.uTurbulence.value = b.turbulence * (1 + 0.28 * osc(199, 2.1));
    u.uMacroAmp.value = b.macro * (1 + 0.18 * osc(419, 4.3));
    u.uSheetAmp.value = b.sheetAmp * (1 + 0.35 * osc(347, 1.4));
    u.uSheetFreq.value = b.sheetFreq * (1 + 0.22 * osc(521, 3.2));
    u.uSheetPull.value = b.sheetPull * (1 + 0.40 * osc(263, 5.1));

    u.uDrift.value.set(
      0.045 * osc(b.driftPeriod.x, b.driftPhase.x),
      0.030 * osc(b.driftPeriod.y, b.driftPhase.y),
      0.022 * osc(b.driftPeriod.z, b.driftPhase.z),
    );

    for (let i = 0; i < VORTEX_COUNT; i++) {
      const v = this.vortices[i];
      const pos = u.uVortexPos.value[i];
      const ax = u.uVortexAxis.value[i];

      pos.set(
        v.centreAmp.x * osc(v.centrePeriod.x, v.centrePhase.x),
        v.centreAmp.y * osc(v.centrePeriod.y, v.centrePhase.y),
        v.centreAmp.z * osc(v.centrePeriod.z, v.centrePhase.z),
        v.strength * (0.55 + 0.45 * osc(v.strengthPeriod, v.strengthPhase)),
      );

      // Rotate the axis slowly about Y so the vortices change their plane of
      // rotation instead of spinning about a fixed one forever.
      const a = (time / v.axisPeriod) * Math.PI * 2;
      const ca = Math.cos(a);
      const sa = Math.sin(a);
      ax.set(
        v.axis.x * ca - v.axis.z * sa,
        v.axis.y,
        v.axis.x * sa + v.axis.z * ca,
        v.radius * (1 + 0.3 * osc(v.radiusPeriod, v.centrePhase.x)),
      );
    }
  }
}
