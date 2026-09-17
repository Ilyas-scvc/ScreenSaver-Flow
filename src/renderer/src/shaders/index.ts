/**
 * Tiny GLSL `#include` resolver.
 *
 * Vite can import any file as a string with `?raw`, so a 30-line preprocessor
 * removes the need for a shader-loader plugin entirely. Chunks are inlined at
 * most once per program, which is also what the `#ifndef` guards in the .glsl
 * files enforce on the driver side.
 */

import common from './lib/common.glsl?raw';
import noise from './lib/noise.glsl?raw';
import curl from './lib/curl.glsl?raw';
import flowField from './lib/flowField.glsl?raw';

import fullscreenVert from './sim/fullscreen.vert.glsl?raw';
import simulateFrag from './sim/simulate.frag.glsl?raw';
import seedFrag from './sim/seed.frag.glsl?raw';

import fiberVert from './fiber/fiber.vert.glsl?raw';
import fiberFrag from './fiber/fiber.frag.glsl?raw';

import trailFrag from './post/trail.frag.glsl?raw';
import bloomPrefilterFrag from './post/bloomPrefilter.frag.glsl?raw';
import bloomDownsampleFrag from './post/bloomDownsample.frag.glsl?raw';
import bloomUpsampleFrag from './post/bloomUpsample.frag.glsl?raw';
import compositeFrag from './post/composite.frag.glsl?raw';

const CHUNKS: Readonly<Record<string, string>> = {
  'lib/common.glsl': common,
  'lib/noise.glsl': noise,
  'lib/curl.glsl': curl,
  'lib/flowField.glsl': flowField,
};

const INCLUDE_RE = /^[ \t]*#include[ \t]+"([^"]+)"[ \t]*$/gm;

function resolve(source: string, seen: Set<string>, stack: string[]): string {
  return source.replace(INCLUDE_RE, (_match, name: string) => {
    if (stack.includes(name)) {
      throw new Error(`Circular GLSL include: ${[...stack, name].join(' -> ')}`);
    }
    if (seen.has(name)) return '';
    const chunk = CHUNKS[name];
    if (chunk === undefined) {
      throw new Error(`Unknown GLSL chunk "${name}" (available: ${Object.keys(CHUNKS).join(', ')})`);
    }
    seen.add(name);
    return resolve(chunk, seen, [...stack, name]);
  });
}

/** Inlines every `#include` and prepends the precision qualifiers that
 *  RawShaderMaterial does not add for us. */
export function buildShader(source: string): string {
  const body = resolve(source, new Set<string>(), []);
  return `precision highp float;\nprecision highp int;\nprecision highp sampler2D;\n\n${body}`;
}

export const glsl = {
  fullscreenVert: buildShader(fullscreenVert),
  simulateFrag: buildShader(simulateFrag),
  seedFrag: buildShader(seedFrag),
  fiberVert: buildShader(fiberVert),
  fiberFrag: buildShader(fiberFrag),
  trailFrag: buildShader(trailFrag),
  bloomPrefilterFrag: buildShader(bloomPrefilterFrag),
  bloomDownsampleFrag: buildShader(bloomDownsampleFrag),
  bloomUpsampleFrag: buildShader(bloomUpsampleFrag),
  compositeFrag: buildShader(compositeFrag),
} as const;
