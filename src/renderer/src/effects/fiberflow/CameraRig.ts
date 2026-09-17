import * as THREE from 'three';
import { Rng } from '../../core/Rng';
import type { ViewContext } from '../../config/types';

interface Axis {
  readonly amp: number;
  readonly periodA: number;
  readonly periodB: number;
  readonly phaseA: number;
  readonly phaseB: number;
}

/**
 * Very slow camera drift.
 *
 * Two sine terms per axis with mutually incommensurate periods trace a path that
 * never closes, so the camera never returns to exactly where it has been. Every
 * period is over a minute: at any instant the motion sits below the threshold
 * where the eye reads it as movement, yet a minute later the composition has
 * completely changed.
 */
export class CameraRig {
  readonly camera: THREE.PerspectiveCamera;

  private readonly origin = new THREE.Vector3(0, 0, 16.5);
  private readonly posAxes: Axis[];
  private readonly targetAxes: Axis[];
  private readonly rollAxis: Axis;
  private readonly fovAxis: Axis;
  private readonly baseFov = 44;

  private readonly target = new THREE.Vector3();
  private readonly size = new THREE.Vector2(1920, 1080);
  private view: ViewContext | null = null;

  constructor(seed: number) {
    const rng = new Rng(seed ^ 0x2545f491);

    const axis = (amp: number, min: number, max: number): Axis => ({
      amp,
      periodA: rng.range(min, max),
      periodB: rng.range(min * 1.7, max * 1.9),
      phaseA: rng.range(0, Math.PI * 2),
      phaseB: rng.range(0, Math.PI * 2),
    });

    this.posAxes = [axis(2.6, 71, 149), axis(1.7, 83, 167), axis(2.2, 97, 191)];
    this.targetAxes = [axis(2.1, 103, 211), axis(1.5, 127, 233), axis(0.8, 149, 277)];
    this.rollAxis = axis(0.09, 181, 349);
    this.fovAxis = axis(2.6, 223, 419);

    this.camera = new THREE.PerspectiveCamera(this.baseFov, 16 / 9, 0.5, 120);
    this.camera.position.copy(this.origin);
  }

  private static wave(a: Axis, t: number): number {
    const twoPi = Math.PI * 2;
    return (
      a.amp *
      (0.65 * Math.sin((t / a.periodA) * twoPi + a.phaseA) +
        0.35 * Math.sin((t / a.periodB) * twoPi + a.phaseB))
    );
  }

  resize(width: number, height: number, view: ViewContext): void {
    this.size.set(Math.max(1, width), Math.max(1, height));
    this.view = view;
    this.applyProjection();
  }

  private get spansMultipleMonitors(): boolean {
    const v = this.view;
    return (
      v !== null && v.synchronized && v.monitorCount > 1 && v.virtual.w > 0 && v.virtual.h > 0 && v.bounds.h > 0
    );
  }

  private applyProjection(): void {
    const view = this.view;
    if (view !== null && this.spansMultipleMonitors) {
      // One continuous frustum spanning the whole virtual desktop, of which this
      // window renders exactly its own rectangle. Fibers therefore cross the
      // bezel and line up with their continuation on the next monitor.
      this.camera.aspect = view.virtual.w / view.virtual.h;
      this.camera.setViewOffset(
        view.virtual.w,
        view.virtual.h,
        view.bounds.x - view.virtual.x,
        view.bounds.y - view.virtual.y,
        view.bounds.w,
        view.bounds.h,
      );
    } else {
      this.camera.clearViewOffset();
      this.camera.aspect = this.size.x / this.size.y;
    }
    this.camera.updateProjectionMatrix();
  }

  /**
   * Vertical pixels that the camera's full field of view maps onto. Under a view
   * offset the window is only a slice of that, so the fiber shader's pixel-size
   * maths has to use this figure rather than the buffer height.
   */
  fullFovPixelHeight(): number {
    const view = this.view;
    if (view !== null && this.spansMultipleMonitors) {
      return this.size.y * (view.virtual.h / view.bounds.h);
    }
    return this.size.y;
  }

  update(time: number): void {
    this.camera.position.set(
      this.origin.x + CameraRig.wave(this.posAxes[0], time),
      this.origin.y + CameraRig.wave(this.posAxes[1], time),
      this.origin.z + CameraRig.wave(this.posAxes[2], time),
    );

    this.target.set(
      CameraRig.wave(this.targetAxes[0], time),
      CameraRig.wave(this.targetAxes[1], time),
      CameraRig.wave(this.targetAxes[2], time),
    );

    const roll = CameraRig.wave(this.rollAxis, time);
    this.camera.up.set(Math.sin(roll), Math.cos(roll), 0);
    this.camera.lookAt(this.target);

    const fov = this.baseFov + CameraRig.wave(this.fovAxis, time);
    if (Math.abs(fov - this.camera.fov) > 1e-4) {
      this.camera.fov = fov;
      this.applyProjection();
    }
  }
}
