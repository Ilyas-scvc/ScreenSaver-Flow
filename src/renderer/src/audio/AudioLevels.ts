import type { AudioLevels } from '../config/types';

export const SILENCE: Readonly<AudioLevels> = Object.freeze({
  bass: 0,
  mid: 0,
  treble: 0,
  volume: 0,
});

/**
 * Source of audio analysis levels driving the shader uniforms.
 *
 * Audio reactivity is deliberately not implemented yet, but the plumbing is: the
 * levels reach `uAudio` in both the simulation and the fiber shaders every
 * frame. Adding a real source later means implementing this one interface and
 * handing it to the app - no shader or effect change.
 */
export interface AudioSource {
  /** Called once per frame, before the effect updates. */
  sample(dt: number): AudioLevels;
  dispose(): void;
}

/** The default source: perfectly silent, allocation-free. */
export class NullAudioSource implements AudioSource {
  sample(): AudioLevels {
    return SILENCE;
  }

  dispose(): void {
    /* nothing to release */
  }
}
