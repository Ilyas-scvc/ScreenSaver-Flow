import * as THREE from 'three';
import { glsl } from '../../shaders';
import type { FullscreenQuad } from '../../core/FullscreenQuad';
import { Rng } from '../../core/Rng';
import { log } from '../../core/Log';
import type { FlowFieldController} from './FlowFieldController';
import { SPAWN_EXTENT } from './FlowFieldController';

export interface SimulationOptions {
  readonly texSize: number;
  readonly seed: number;
}

/**
 * GPU particle simulation.
 *
 * State lives entirely in two RGBA float textures that ping-pong between two
 * multi-target framebuffers:
 *
 *   attachment 0 : xyz = position, w = life
 *   attachment 1 : xyz = velocity, w = colour coordinate
 *
 * Writing both attachments from a single fragment shader (WebGL2 MRT) halves
 * the pass count compared to the usual "one FBO per attribute" GPGPU layout,
 * and means the entire integration of up to 230 400 fibers is one draw call.
 */
export class FiberSimulation {
  readonly texSize: number;
  readonly count: number;

  private readonly quad: FullscreenQuad;
  private readonly targets: [THREE.WebGLRenderTarget, THREE.WebGLRenderTarget];
  private readonly material: THREE.RawShaderMaterial;
  private readonly seedTextures: { pos: THREE.DataTexture; vel: THREE.DataTexture };
  private readonly seedMaterial: THREE.RawShaderMaterial;
  private read = 0;
  private disposed = false;

  constructor(
    renderer: THREE.WebGLRenderer,
    quad: FullscreenQuad,
    field: FlowFieldController,
    options: SimulationOptions,
  ) {
    this.texSize = options.texSize;
    this.count = options.texSize * options.texSize;
    this.quad = quad;

    const dataType = FiberSimulation.pickDataType(renderer);

    const makeTarget = (): THREE.WebGLRenderTarget =>
      new THREE.WebGLRenderTarget(this.texSize, this.texSize, {
        count: 2,
        type: dataType,
        format: THREE.RGBAFormat,
        minFilter: THREE.NearestFilter,
        magFilter: THREE.NearestFilter,
        wrapS: THREE.ClampToEdgeWrapping,
        wrapT: THREE.ClampToEdgeWrapping,
        depthBuffer: false,
        stencilBuffer: false,
        generateMipmaps: false,
      });

    this.targets = [makeTarget(), makeTarget()];

    this.material = new THREE.RawShaderMaterial({
      glslVersion: THREE.GLSL3,
      vertexShader: glsl.fullscreenVert,
      fragmentShader: glsl.simulateFrag,
      depthTest: false,
      depthWrite: false,
      uniforms: {
        uPosTex: { value: null },
        uVelTex: { value: null },
        uTime: { value: 0 },
        uDt: { value: 1 / 60 },
        uSeed: { value: (options.seed % 977) + 0.317 },
        uFlowSpeed: { value: 0.55 },
        uInertia: { value: 1.6 },
        uMaxSpeed: { value: 3.0 },
        uSpawnExtent: { value: SPAWN_EXTENT.clone() },
        uLifeRange: { value: new THREE.Vector2(7, 19) },
        uColorScale: { value: 0.07 },
        uColorOffset: { value: new THREE.Vector3(13.7, -21.4, 8.9) },
        uAudio: { value: new THREE.Vector4() },
        ...field.uniforms,
      },
    });

    this.seedTextures = this.createSeedTextures(options.seed);
    this.seedMaterial = new THREE.RawShaderMaterial({
      glslVersion: THREE.GLSL3,
      vertexShader: glsl.fullscreenVert,
      fragmentShader: glsl.seedFrag,
      depthTest: false,
      depthWrite: false,
      uniforms: {
        uPosSeed: { value: this.seedTextures.pos },
        uVelSeed: { value: this.seedTextures.vel },
      },
    });

    this.reset(renderer);
    log.info(`simulation: ${this.texSize}x${this.texSize} = ${this.count} fibers (${dataType === THREE.FloatType ? 'float32' : 'float16'})`);
  }

  private static pickDataType(renderer: THREE.WebGLRenderer): THREE.TextureDataType {
    // Half float positions quantise to ~8 mm at our world scale, which shows up
    // as visible stepping in the motion. Only fall back if the driver truly
    // cannot render to RGBA32F.
    const gl = renderer.getContext();
    const ok = gl.getExtension('EXT_color_buffer_float') !== null;
    if (!ok) log.warn('EXT_color_buffer_float unavailable, falling back to half float positions');
    return ok ? THREE.FloatType : THREE.HalfFloatType;
  }

  /** Deterministic initial state. Same seed -> same opening frame. */
  private createSeedTextures(seed: number): { pos: THREE.DataTexture; vel: THREE.DataTexture } {
    const n = this.count;
    const posData = new Float32Array(n * 4);
    const velData = new Float32Array(n * 4);
    const rng = new Rng(seed ^ 0x5f3759df);

    for (let i = 0; i < n; i++) {
      const o = i * 4;
      const z = rng.signed();
      posData[o + 0] = rng.signed() * SPAWN_EXTENT.x;
      posData[o + 1] = rng.signed() * SPAWN_EXTENT.y;
      posData[o + 2] = z * z * z * SPAWN_EXTENT.z;
      // Staggered lives so the population does not all expire together.
      posData[o + 3] = rng.next();

      velData[o + 0] = 0;
      velData[o + 1] = 0;
      velData[o + 2] = 0;
      velData[o + 3] = rng.next();
    }

    const make = (data: Float32Array): THREE.DataTexture => {
      const t = new THREE.DataTexture(data, this.texSize, this.texSize, THREE.RGBAFormat, THREE.FloatType);
      t.minFilter = THREE.NearestFilter;
      t.magFilter = THREE.NearestFilter;
      t.generateMipmaps = false;
      t.needsUpdate = true;
      return t;
    };

    return { pos: make(posData), vel: make(velData) };
  }

  /** Re-seeds both ping-pong buffers from the deterministic initial state. */
  reset(renderer: THREE.WebGLRenderer): void {
    this.quad.render(renderer, this.seedMaterial, this.targets[0]);
    this.quad.render(renderer, this.seedMaterial, this.targets[1]);
    this.read = 0;
  }

  get positionTexture(): THREE.Texture {
    return this.targets[this.read].textures[0];
  }

  get velocityTexture(): THREE.Texture {
    return this.targets[this.read].textures[1];
  }

  get uniforms(): Record<string, THREE.IUniform> {
    return this.material.uniforms;
  }

  step(renderer: THREE.WebGLRenderer, time: number, dt: number): void {
    if (this.disposed) return;

    const u = this.material.uniforms;
    u.uTime.value = time;
    u.uDt.value = dt;
    u.uPosTex.value = this.targets[this.read].textures[0];
    u.uVelTex.value = this.targets[this.read].textures[1];

    const write = 1 - this.read;
    this.quad.render(renderer, this.material, this.targets[write]);
    this.read = write;
  }

  dispose(): void {
    if (this.disposed) return;
    this.disposed = true;
    this.targets[0].dispose();
    this.targets[1].dispose();
    this.material.dispose();
    this.seedMaterial.dispose();
    this.seedTextures.pos.dispose();
    this.seedTextures.vel.dispose();
  }
}
