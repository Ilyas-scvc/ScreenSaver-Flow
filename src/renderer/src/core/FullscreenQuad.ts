import * as THREE from 'three';

/**
 * A single reusable screen-covering quad. Every fullscreen pass in the pipeline
 * (simulation, trail, bloom, composite) swaps its material onto this one mesh,
 * so the whole post chain allocates exactly one geometry.
 */
export class FullscreenQuad {
  private readonly scene = new THREE.Scene();
  private readonly camera = new THREE.OrthographicCamera(-1, 1, 1, -1, 0, 1);
  private readonly mesh: THREE.Mesh;

  constructor() {
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute(
      'position',
      new THREE.BufferAttribute(new Float32Array([-1, -1, 0, 3, -1, 0, -1, 3, 0]), 3),
    );
    geometry.setAttribute(
      'uv',
      new THREE.BufferAttribute(new Float32Array([0, 0, 2, 0, 0, 2]), 2),
    );
    // One oversized triangle instead of two triangles: no diagonal seam and one
    // fewer vertex, which matters when the pass runs a dozen times per frame.
    this.mesh = new THREE.Mesh(geometry, new THREE.MeshBasicMaterial());
    this.mesh.frustumCulled = false;
    this.scene.add(this.mesh);
  }

  render(
    renderer: THREE.WebGLRenderer,
    material: THREE.Material,
    target: THREE.WebGLRenderTarget | null,
    clear = false,
  ): void {
    this.mesh.material = material;
    renderer.setRenderTarget(target);
    if (clear) renderer.clear(true, false, false);
    renderer.render(this.scene, this.camera);
  }

  dispose(): void {
    this.mesh.geometry.dispose();
    (this.mesh.material as THREE.Material).dispose();
  }
}
