// ---------------------------------------------------------------------------
// curl.glsl - divergence-free vector fields.
//
// Why curl noise instead of "sample a noise vector directly": the curl of any
// vector potential is divergence-free, so the flow neither compresses the
// fibers into point sinks nor blows them apart into a uniform mist. That is the
// single most important reason the motion reads as *fluid* rather than as
// random drift, and it is what produces sheets, folds and vortices for free.
// ---------------------------------------------------------------------------

#ifndef FS_CURL
#define FS_CURL

#include "lib/noise.glsl"

// Three widely separated offsets turn one scalar noise into a vector potential
// whose components are effectively uncorrelated.
const vec3 FS_POT_O1 = vec3(31.416, -47.853, 12.793);
const vec3 FS_POT_O2 = vec3(-71.317, 23.198, -63.041);

vec3 potential3(vec3 p) {
    return vec3(snoise3(p), snoise3(p + FS_POT_O1), snoise3(p + FS_POT_O2));
}

vec3 potential4(vec3 p, float w) {
    return vec3(
        snoise4(vec4(p, w)),
        snoise4(vec4(p + FS_POT_O1, w + 19.31)),
        snoise4(vec4(p + FS_POT_O2, w - 41.77))
    );
}

// Central differences. Slightly more expensive than forward differences but
// unbiased, which matters because a biased curl introduces a constant drift
// that becomes obvious after a few minutes of watching.
vec3 curl3(vec3 p, float eps) {
    vec3 dx = vec3(eps, 0.0, 0.0);
    vec3 dy = vec3(0.0, eps, 0.0);
    vec3 dz = vec3(0.0, 0.0, eps);

    vec3 px0 = potential3(p - dx), px1 = potential3(p + dx);
    vec3 py0 = potential3(p - dy), py1 = potential3(p + dy);
    vec3 pz0 = potential3(p - dz), pz1 = potential3(p + dz);

    float x = (py1.z - py0.z) - (pz1.y - pz0.y);
    float y = (pz1.x - pz0.x) - (px1.z - px0.z);
    float z = (px1.y - px0.y) - (py1.x - py0.x);

    return vec3(x, y, z) / (2.0 * eps);
}

// Time-morphing variant: `w` is the 4th noise coordinate, so the structures
// evolve in place instead of translating past the camera.
vec3 curl4(vec3 p, float w, float eps) {
    vec3 dx = vec3(eps, 0.0, 0.0);
    vec3 dy = vec3(0.0, eps, 0.0);
    vec3 dz = vec3(0.0, 0.0, eps);

    vec3 px0 = potential4(p - dx, w), px1 = potential4(p + dx, w);
    vec3 py0 = potential4(p - dy, w), py1 = potential4(p + dy, w);
    vec3 pz0 = potential4(p - dz, w), pz1 = potential4(p + dz, w);

    float x = (py1.z - py0.z) - (pz1.y - pz0.y);
    float y = (pz1.x - pz0.x) - (px1.z - px0.z);
    float z = (px1.y - px0.y) - (py1.x - py0.x);

    return vec3(x, y, z) / (2.0 * eps);
}

// A soft, finite-radius rotational cell. Gaussian falloff keeps it perfectly
// smooth at the boundary so vortices blend into the surrounding flow instead of
// showing a hard edge.
vec3 vortexCell(vec3 p, vec3 center, vec3 axis, float strength, float radius) {
    vec3 d = p - center;
    float r2 = dot(d, d);
    float falloff = exp(-r2 / max(radius * radius, 1e-4));
    // A very slight inward pull gives the vortex a visible eye. Keep it small:
    // any real convergence makes fibers point at the focus, and a focus seen
    // head-on projects into a starburst of radial spokes.
    vec3 swirl = cross(axis, d);
    vec3 pull = -d * 0.05;
    return (swirl + pull) * strength * falloff;
}

#endif
