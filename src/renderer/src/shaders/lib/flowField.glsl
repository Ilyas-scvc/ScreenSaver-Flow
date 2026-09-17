// ---------------------------------------------------------------------------
// flowField.glsl - the FiberFlow velocity field.
//
// Structure (in order of spatial scale):
//   0. domain warp      - bends the whole sampling space, destroys the tell-tale
//                         "simplex blobs" signature and creates folds / creases
//   1. macro curl       - the big waves, ribbons and valleys (4D: morphs in place)
//   2. meso curl        - turbulence that gives the ribbons internal texture
//   3. micro curl       - fine detail so individual fibers are not parallel
//   4. vortex cells     - a handful of slowly drifting rotational attractors
//   5. sheet attraction - pulls fibers toward one huge undulating surface
//   6. containment      - soft walls, so nothing ever escapes the working volume
//
// Anisotropic sampling (p * vec3(1, 1.9, 1)) is deliberate: squashing the noise
// along Y makes the flow stratified, which is what turns "smoke" into "hair".
// ---------------------------------------------------------------------------

#ifndef FS_FLOWFIELD
#define FS_FLOWFIELD

#include "lib/common.glsl"
#include "lib/curl.glsl"

#define FS_VORTEX_COUNT 4

uniform float uTime;          // unbounded seconds since scene epoch
uniform vec3  uSeedOffset;    // deterministic per-seed translation of the field
uniform vec3  uBounds;        // half-extents of the working volume

uniform float uFlowScale;     // base spatial frequency
uniform float uWarpAmount;    // strength of the domain warp
uniform float uTurbulence;    // multiplier on the meso + micro layers
uniform float uMacroAmp;
uniform float uSheetAmp;      // amplitude of the undulating sheet
uniform float uSheetFreq;
uniform float uSheetPull;     // how strongly fibers stick to the sheet
uniform float uContain;       // soft-wall strength
uniform vec3  uDrift;         // global translation of the whole body of fibers

// xyz = centre, w = strength   /   xyz = axis, w = radius
uniform vec4  uVortexPos[FS_VORTEX_COUNT];
uniform vec4  uVortexAxis[FS_VORTEX_COUNT];

// bass / mid / treble / volume. Always supplied (zero when audio is disabled)
// so that enabling audio reactivity later needs no shader change.
uniform vec4  uAudio;

vec3 flowField(vec3 p) {
    float t = uTime;
    vec3 sp = p * uFlowScale + uSeedOffset;

    // --- 0. domain warp -----------------------------------------------------
    // Very low frequency, very slow. Warping the *input* rather than adding to
    // the output is what produces creases and folds instead of blur.
    vec3 warp = curl4(sp * 0.34, t * 0.019, 0.75);
    vec3 wp = sp + warp * uWarpAmount;

    // --- 1. macro: the structures you actually see --------------------------
    vec3 f = curl4(wp * vec3(1.0, 1.9, 1.0), t * 0.031, 0.55) * uMacroAmp;

    // --- 2. meso turbulence -------------------------------------------------
    vec3 mesoP = rotY(0.9) * (wp * 2.63) + vec3(11.7, -5.2, 3.4);
    f += curl4(mesoP, t * 0.074, 0.30) * (0.155 * uTurbulence);

    // --- 3. micro detail ----------------------------------------------------
    // 3D (cheaper) with a slow translation - at this scale the eye cannot tell
    // morphing from sliding, so the extra cost of 4D would be wasted.
    vec3 microP = rotY(-1.7) * (wp * 6.1) + vec3(0.0, t * 0.045, -t * 0.03);
    f += curl3(microP, 0.16) * (0.032 * uTurbulence);

    // --- 4. vortex attractors ----------------------------------------------
    for (int i = 0; i < FS_VORTEX_COUNT; ++i) {
        f += vortexCell(p, uVortexPos[i].xyz, uVortexAxis[i].xyz,
                        uVortexPos[i].w, uVortexAxis[i].w);
    }

    // --- 5. sheet attraction ------------------------------------------------
    // One enormous warped surface. Without this the fibers fill the box evenly
    // and the image loses its "single continuous object" quality.
    float sheetZ = uSheetAmp * (
          snoise3(vec3(p.x * uSheetFreq, p.y * uSheetFreq * 0.8, t * 0.023))
        + 0.45 * snoise3(vec3(p.x * uSheetFreq * 2.3 + 17.0, p.y * uSheetFreq * 1.7, t * 0.041))
    );
    // Saturating, not linear. A linear spring overwhelms the curl field for any
    // fiber more than a couple of units off the sheet and points it straight at
    // the camera, which is what turns silky flow into a bed of spikes.
    f.z += tanh((sheetZ - p.z) * 0.3) * uSheetPull;

    // --- 6. soft containment ------------------------------------------------
    vec3 over = max(abs(p) - uBounds, vec3(0.0));
    f -= sign(p) * over * over * uContain;

    // --- global drift -------------------------------------------------------
    f += uDrift;

    // Reserved audio coupling. Zero-cost while uAudio stays at zero.
    f *= 1.0 + uAudio.w * 0.35;

    return f;
}

#endif
