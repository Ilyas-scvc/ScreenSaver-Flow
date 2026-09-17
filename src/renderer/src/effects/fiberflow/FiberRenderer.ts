import * as THREE from 'three';
import { glsl } from '../../shaders';
import { FIELD_BOUNDS } from './FlowFieldController';

/**
 * Draws the entire fiber population in one instanced draw call.
 *
 * The geometry is a single unit quad; the vertex shader reads the fiber's state
 * out of the simulation textures and expands the quad into a camera-facing
 * capsule oriented along the velocity. The fragment shader evaluates the capsule
 * analytically as a signed distance, which is why the fibers stay smooth at any
 * resolution without MSAA and without GL_LINES.
 */
export class FiberRenderer {
  readonly mesh: THREE.Mesh;
  readonly material: THREE.RawShaderMaterial;

  private readonly geometry: THREE.InstancedBufferGeometry;
  private readonly capacity: number;

  constructor(texSize: number, palette: THREE.Texture) {
    this.capacity = texSize * texSize;

    this.geometry = new THREE.InstancedBufferGeometry();
    this.geometry.setAttribute(
      'position',
      new THREE.BufferAttribute(new Float32Array([-1, -1, 0, 1, -1, 0, 1, 1, 0, -1, 1, 0]), 3),
    );
    this.geometry.setIndex([0, 1, 2, 0, 2, 3]);
    this.geometry.setAttribute(
      'aRef',
      new THREE.InstancedBufferAttribute(FiberRenderer.buildRefs(texSize), 2),
    );
    this.geometry.instanceCount = this.capacity;
    // The population is authored to fill the frustum; culling it as a single
    // bounding volume could only ever produce false negatives.
    this.geometry.boundingSphere = new THREE.Sphere(new THREE.Vector3(), 1e6);

    this.material = new THREE.RawShaderMaterial({
      glslVersion: THREE.GLSL3,
      vertexShader: glsl.fiberVert,
      fragmentShader: glsl.fiberFrag,
      transparent: true,
      blending: THREE.AdditiveBlending,
      depthTest: false,
      depthWrite: false,
      uniforms: {
        uPosTex: { value: null },
        uVelTex: { value: null },
        uPalette: { value: palette },
        uBounds: { value: FIELD_BOUNDS.clone() },
        uFiberLength: { value: 0.3 },
        uFiberWidth: { value: 0.0082 },
        uMinPixelWidth: { value: 0.82 },
        uPixelScale: { value: 0.001 },
        uSpeedRef: { value: 1.05 },
        uDepthRange: { value: new THREE.Vector2(5, 27) },
        uBrightness: { value: 0.34 },
        uMaskScale: { value: new THREE.Vector3(0.046, 0.078, 0.052) },
        uMaskOffset: { value: new THREE.Vector3() },
        uMaskRange: { value: new THREE.Vector2(0.28, 0.74) },
        uPaletteShift: { value: 0 },
        uWhitePoint: { value: 0.62 },
        uAudio: { value: new THREE.Vector4() },
      },
    });

    this.mesh = new THREE.Mesh(this.geometry, this.material);
    this.mesh.frustumCulled = false;
  }

  /** Per-instance texel address, laid out so instance i maps to texel i. */
  private static buildRefs(texSize: number): Float32Array {
    const refs = new Float32Array(texSize * texSize * 2);
    const inv = 1 / texSize;
    let o = 0;
    for (let y = 0; y < texSize; y++) {
      for (let x = 0; x < texSize; x++) {
        refs[o++] = (x + 0.5) * inv;
        refs[o++] = (y + 0.5) * inv;
      }
    }
    return refs;
  }

  get activeCount(): number {
    return this.geometry.instanceCount;
  }

  get maxCount(): number {
    return this.capacity;
  }

  /** Adaptive quality lever: shrinking the instance count is free. No buffer
   *  reallocation, no shader recompile, effective on the very next frame. */
  setActiveFraction(fraction: number): void {
    const n = Math.max(1024, Math.min(this.capacity, Math.round(this.capacity * fraction)));
    this.geometry.instanceCount = n;
  }

  setSimulationTextures(position: THREE.Texture, velocity: THREE.Texture): void {
    this.material.uniforms.uPosTex.value = position;
    this.material.uniforms.uVelTex.value = velocity;
  }

  dispose(): void {
    this.geometry.dispose();
    this.material.dispose();
  }
}
