// ---------------------------------------------------------------------------
// simulate.frag.glsl - one integration step for every fiber.
//
// Runs once per frame over an S x S texture where each texel is one fiber.
// Multiple render targets let position and velocity be written in a single
// pass, so the whole simulation of 25k-230k fibers costs exactly one draw call.
//
//   target 0 : xyz = world position, w = life (1 at birth -> 0 at death)
//   target 1 : xyz = velocity,       w = colour coordinate
// ---------------------------------------------------------------------------

#include "lib/common.glsl"
#include "lib/flowField.glsl"

in vec2 vUv;

layout(location = 0) out vec4 oPos;
layout(location = 1) out vec4 oVel;

uniform sampler2D uPosTex;
uniform sampler2D uVelTex;

uniform float uDt;
uniform float uSeed;
uniform float uFlowSpeed;
uniform float uInertia;      // 1/s - how quickly a fiber adopts the field
uniform float uMaxSpeed;
uniform vec3  uSpawnExtent;
uniform vec2  uLifeRange;    // seconds: min, max
uniform float uColorScale;
uniform vec3  uColorOffset;

// Fibers are seeded into a slab rather than a cube: cubing the z coordinate
// keeps the sign but concentrates mass near the middle, which is what makes the
// population read as one enormous surface instead of a fog bank.
vec3 spawnPosition(vec3 r) {
    vec3 q = r * 2.0 - 1.0;
    q.z = q.z * q.z * q.z;
    return q * uSpawnExtent;
}

void main() {
    vec4 P = texture(uPosTex, vUv);
    vec4 V = texture(uVelTex, vUv);

    vec3  pos        = P.xyz;
    float life       = P.w;
    vec3  vel        = V.xyz;
    float colorCoord = V.w;

    float dt = uDt;

    // Per-fiber constants derived from the texel address. Deriving them instead
    // of storing them keeps the state at exactly two RGBA channels.
    float h        = hash12(vUv * 1024.0 + uSeed);
    float lifeSpan = mix(uLifeRange.x, uLifeRange.y, h);
    float speedVar = mix(0.70, 1.35, hash12(vUv * 733.0 + uSeed + 3.7));

    vec3  target = flowField(pos) * uFlowSpeed * speedVar;
    float tlen   = length(target);
    target *= tlen > uMaxSpeed ? uMaxSpeed / tlen : 1.0;

    // Inertia rather than direct assignment. A fiber lags behind the field,
    // which is what bends its path into a smooth arc instead of a polyline.
    vel = mix(vel, target, expApproach(uInertia, dt));
    pos += vel * dt;

    life -= dt / lifeSpan;

    // Large-scale scalar field driving colour, advected with the fiber. Because
    // it is low frequency, neighbouring fibers share a hue and the image gets
    // broad colour regions instead of confetti.
    float cTarget = fbm3(pos * uColorScale + uColorOffset, 2, 2.1, 0.5) * 0.5 + 0.5;
    colorCoord = mix(colorCoord, cTarget, expApproach(0.5, dt));

    // ---- respawn -----------------------------------------------------------
    // Branchless on purpose: an `if` here would diverge inside every warp that
    // contains a single dying fiber and cost more than always evaluating both.
    float outside = max(max(abs(pos.x) - uBounds.x * 1.30,
                            abs(pos.y) - uBounds.y * 1.30),
                            abs(pos.z) - uBounds.z * 1.45);
    float dead = clamp(step(life, 0.0) + step(0.0, outside), 0.0, 1.0);

    vec3 r     = hash33(vec3(vUv * 4096.0, uSeed + floor(uTime * 2.7)));
    vec3 spawn = spawnPosition(r);

    pos        = mix(pos, spawn, dead);
    vel        = mix(vel, vec3(0.0), dead);
    life       = mix(life, 1.0, dead);
    colorCoord = mix(colorCoord, r.x, dead);

    oPos = vec4(pos, life);
    oVel = vec4(vel, colorCoord);
}
