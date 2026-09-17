import * as THREE from 'three';
import { glsl } from '../shaders';
import type { FullscreenQuad } from '../core/FullscreenQuad';
import type { FlowScreenSettings } from '../config/types';

/** Tone-mapping and glow constants that are not exposed in the UI. */
const LOOK = {
  bloomStrength: 0.78,
  bloomThreshold: 0.40,
  bloomKnee: 0.32,
  bloomRadius: 1.0,
  bloomClamp: 12.0,
  exposure: 1.25,
  /** Toe knee. Higher pushes more of the frame to true black. */
  blackPoint: 0.06,
  saturation: 1.06,
  vignette: 0.22,
  trailSoftness: 0.5,
} as const;

const MAX_BLOOM_MIPS = 7;
const MIN_BLOOM_SIZE = 12;

/**
 * HDR post chain.
 *
 *   scene RT (persistence) -> prefilter -> down x N -> up x N (additive) -> composite
 *
 * The bloom is a progressive dual-filter chain rather than a pair of separable
 * gaussians: chaining a 13-tap downsample with a 9-tap tent upsample across six
 * mips approximates a very wide, very smooth glow for roughly the cost of two
 * fullscreen passes, and it does not flicker on the small bright features that
 * a field of thin fibers is made of.
 */
export class PostPipeline {
  private readonly quad: FullscreenQuad;

  private sceneTargets: [THREE.WebGLRenderTarget, THREE.WebGLRenderTarget];
  private bloomMips: THREE.WebGLRenderTarget[] = [];
  private front = 0;

  private readonly trailMaterial: THREE.RawShaderMaterial;
  private readonly prefilterMaterial: THREE.RawShaderMaterial;
  private readonly downsampleMaterial: THREE.RawShaderMaterial;
  private readonly upsampleMaterial: THREE.RawShaderMaterial;
  private readonly compositeMaterial: THREE.RawShaderMaterial;

  private width = 1;
  private height = 1;
  private persistence = 0;

  constructor(quad: FullscreenQuad, width: number, height: number) {
    this.quad = quad;

    this.sceneTargets = [PostPipeline.makeTarget(width, height), PostPipeline.makeTarget(width, height)];

    const common = {
      glslVersion: THREE.GLSL3,
      vertexShader: glsl.fullscreenVert,
      depthTest: false,
      depthWrite: false,
    };

    this.trailMaterial = new THREE.RawShaderMaterial({
      ...common,
      fragmentShader: glsl.trailFrag,
      uniforms: {
        uPrev: { value: null },
        uDecay: { value: 0 },
        uSoftness: { value: LOOK.trailSoftness },
        uTexel: { value: new THREE.Vector2() },
      },
    });

    this.prefilterMaterial = new THREE.RawShaderMaterial({
      ...common,
      fragmentShader: glsl.bloomPrefilterFrag,
      uniforms: {
        uSrc: { value: null },
        uFilter: { value: new THREE.Vector4() },
        uClampMax: { value: LOOK.bloomClamp },
      },
    });

    this.downsampleMaterial = new THREE.RawShaderMaterial({
      ...common,
      fragmentShader: glsl.bloomDownsampleFrag,
      uniforms: {
        uSrc: { value: null },
        uTexel: { value: new THREE.Vector2() },
      },
    });

    this.upsampleMaterial = new THREE.RawShaderMaterial({
      ...common,
      fragmentShader: glsl.bloomUpsampleFrag,
      blending: THREE.AdditiveBlending,
      uniforms: {
        uSrc: { value: null },
        uTexel: { value: new THREE.Vector2() },
        uRadius: { value: LOOK.bloomRadius },
      },
    });

    this.compositeMaterial = new THREE.RawShaderMaterial({
      ...common,
      fragmentShader: glsl.compositeFrag,
      uniforms: {
        uScene: { value: null },
        uBloom: { value: null },
        uBloomStrength: { value: LOOK.bloomStrength },
        uExposure: { value: LOOK.exposure },
        uBlackPoint: { value: LOOK.blackPoint },
        uSaturation: { value: LOOK.saturation },
        uVignette: { value: LOOK.vignette },
        uTime: { value: 0 },
      },
    });

    this.setThreshold(LOOK.bloomThreshold, LOOK.bloomKnee);
    this.resize(width, height);
  }

  private static makeTarget(width: number, height: number): THREE.WebGLRenderTarget {
    // Half float is plenty for radiance we are about to tone map, and it halves
    // the bandwidth of the dozen fullscreen passes in this chain.
    return new THREE.WebGLRenderTarget(Math.max(1, width), Math.max(1, height), {
      type: THREE.HalfFloatType,
      format: THREE.RGBAFormat,
      minFilter: THREE.LinearFilter,
      magFilter: THREE.LinearFilter,
      wrapS: THREE.ClampToEdgeWrapping,
      wrapT: THREE.ClampToEdgeWrapping,
      depthBuffer: false,
      stencilBuffer: false,
      generateMipmaps: false,
    });
  }

  private setThreshold(threshold: number, knee: number): void {
    const k = Math.max(knee, 1e-4);
    (this.prefilterMaterial.uniforms.uFilter.value as THREE.Vector4).set(
      threshold,
      threshold - k,
      2 * k,
      0.25 / k,
    );
  }

  resize(width: number, height: number): void {
    this.width = Math.max(1, Math.floor(width));
    this.height = Math.max(1, Math.floor(height));

    this.sceneTargets[0].setSize(this.width, this.height);
    this.sceneTargets[1].setSize(this.width, this.height);
    (this.trailMaterial.uniforms.uTexel.value as THREE.Vector2).set(1 / this.width, 1 / this.height);

    for (const mip of this.bloomMips) mip.dispose();
    this.bloomMips = [];

    let w = Math.max(1, this.width >> 1);
    let h = Math.max(1, this.height >> 1);
    for (let i = 0; i < MAX_BLOOM_MIPS; i++) {
      this.bloomMips.push(PostPipeline.makeTarget(w, h));
      if (w <= MIN_BLOOM_SIZE || h <= MIN_BLOOM_SIZE) break;
      w = Math.max(1, w >> 1);
      h = Math.max(1, h >> 1);
    }
  }

  applySettings(settings: FlowScreenSettings): void {
    this.compositeMaterial.uniforms.uBloomStrength.value = LOOK.bloomStrength * settings.glow;
    this.persistence = settings.trail;
  }

  /**
   * Runs the whole chain. `dt` is real elapsed time so the persistence decay is
   * frame-rate independent.
   */
  render(
    renderer: THREE.WebGLRenderer,
    scene: THREE.Scene,
    camera: THREE.Camera,
    time: number,
    dt: number,
  ): void {
    const prev = this.sceneTargets[this.front];
    const curr = this.sceneTargets[1 - this.front];

    // --- 1. persistence -----------------------------------------------------
    // A "trail" of p means the history keeps p of its energy after 1/60 s.
    // Raising it to dt*60 keeps that true at any frame rate, so the trail length
    // looks identical at 30, 60 and 144 Hz.
    const decay = this.persistence <= 0 ? 0 : Math.pow(Math.max(this.persistence, 1e-4), dt * 60);
    this.trailMaterial.uniforms.uPrev.value = prev.texture;
    this.trailMaterial.uniforms.uDecay.value = decay;
    this.quad.render(renderer, this.trailMaterial, curr);

    // --- 2. fibers, additively on top of the decayed history ----------------
    renderer.setRenderTarget(curr);
    renderer.render(scene, camera);

    // --- 3. bloom -----------------------------------------------------------
    this.prefilterMaterial.uniforms.uSrc.value = curr.texture;
    this.quad.render(renderer, this.prefilterMaterial, this.bloomMips[0]);

    for (let i = 1; i < this.bloomMips.length; i++) {
      const src = this.bloomMips[i - 1];
      this.downsampleMaterial.uniforms.uSrc.value = src.texture;
      (this.downsampleMaterial.uniforms.uTexel.value as THREE.Vector2).set(
        1 / src.width,
        1 / src.height,
      );
      this.quad.render(renderer, this.downsampleMaterial, this.bloomMips[i]);
    }

    for (let i = this.bloomMips.length - 2; i >= 0; i--) {
      const src = this.bloomMips[i + 1];
      this.upsampleMaterial.uniforms.uSrc.value = src.texture;
      (this.upsampleMaterial.uniforms.uTexel.value as THREE.Vector2).set(
        1 / src.width,
        1 / src.height,
      );
      this.quad.render(renderer, this.upsampleMaterial, this.bloomMips[i]);
    }

    // --- 4. composite to the back buffer ------------------------------------
    this.compositeMaterial.uniforms.uScene.value = curr.texture;
    this.compositeMaterial.uniforms.uBloom.value = this.bloomMips[0].texture;
    this.compositeMaterial.uniforms.uTime.value = time;
    this.quad.render(renderer, this.compositeMaterial, null);

    this.front = 1 - this.front;
  }

  dispose(): void {
    this.sceneTargets[0].dispose();
    this.sceneTargets[1].dispose();
    for (const mip of this.bloomMips) mip.dispose();
    this.trailMaterial.dispose();
    this.prefilterMaterial.dispose();
    this.downsampleMaterial.dispose();
    this.upsampleMaterial.dispose();
    this.compositeMaterial.dispose();
  }
}
