// ---------------------------------------------------------------------------
// common.glsl - small helpers shared by every FlowScreen shader.
// ---------------------------------------------------------------------------

#ifndef FS_COMMON
#define FS_COMMON

const float FS_PI  = 3.14159265359;
const float FS_TAU = 6.28318530718;

float saturate1(float x) { return clamp(x, 0.0, 1.0); }
vec3  saturate3(vec3 x)  { return clamp(x, vec3(0.0), vec3(1.0)); }

float remap(float v, float a, float b, float c, float d) {
    return c + (d - c) * (v - a) / (b - a);
}

// Integer-ish hashes (Dave Hoskins, "Hash without Sine"). Cheap, no texture
// lookups, stable across drivers because they avoid sin() precision drift.
float hash11(float p) {
    p = fract(p * 0.1031);
    p *= p + 33.33;
    p *= p + p;
    return fract(p);
}

float hash12(vec2 p) {
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

vec3 hash32(vec2 p) {
    vec3 p3 = fract(vec3(p.xyx) * vec3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yxz + 33.33);
    return fract((p3.xxy + p3.yzz) * p3.zyx);
}

vec3 hash33(vec3 p3) {
    p3 = fract(p3 * vec3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yxz + 33.33);
    return fract((p3.xxy + p3.yxx) * p3.zyx);
}

// Frame-rate independent exponential approach: returns the lerp factor that
// moves a value a fixed *proportion* of the way per second, whatever dt is.
float expApproach(float rate, float dt) {
    return 1.0 - exp(-rate * dt);
}

// Rotation helpers used to de-correlate noise octaves so the layers do not
// line up into a visible grid.
mat3 rotY(float a) {
    float c = cos(a), s = sin(a);
    return mat3(c, 0.0, -s, 0.0, 1.0, 0.0, s, 0.0, c);
}

mat3 rotAxis(vec3 axis, float angle) {
    float c = cos(angle), s = sin(angle), t = 1.0 - c;
    vec3 a = normalize(axis);
    return mat3(
        t * a.x * a.x + c,       t * a.x * a.y - s * a.z, t * a.x * a.z + s * a.y,
        t * a.x * a.y + s * a.z, t * a.y * a.y + c,       t * a.y * a.z - s * a.x,
        t * a.x * a.z - s * a.y, t * a.y * a.z + s * a.x, t * a.z * a.z + c
    );
}

#endif
