// ---------------------------------------------------------------------------
// fiber.vert.glsl - expands one instanced quad into a camera-facing capsule
// oriented along the fiber's velocity.
//
// Everything the vertex shader needs comes out of two texture fetches, so the
// CPU never touches particle data and the whole population is one draw call.
// ---------------------------------------------------------------------------

#include "lib/common.glsl"
#include "lib/noise.glsl"

in vec3 position;   // unit quad corner, xy in [-1, 1]
in vec2 aRef;       // per-instance: texel address in the simulation textures

uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;

uniform sampler2D uPosTex;
uniform sampler2D uVelTex;

uniform vec3  uBounds;
uniform float uFiberLength;
uniform float uFiberWidth;
uniform float uMinPixelWidth;   // never let a fiber fall below this many pixels
uniform float uPixelScale;      // world units per pixel per unit of view depth
uniform float uSpeedRef;        // speed that maps to "fast"
uniform vec2  uDepthRange;      // view-space distances used for attenuation
uniform float uBrightness;

// Large-scale visibility field. This is what carves the population into a
// luminous body surrounded by real black instead of an even carpet of fibers.
uniform vec3  uMaskScale;
uniform vec3  uMaskOffset;
uniform vec2  uMaskRange;

uniform vec4  uAudio;

out vec2  vQuad;
out float vAspect;
out float vAlpha;
out float vColorT;
out float vSpeedT;
out float vDepthT;
out float vSeed;

void main() {
    vec4 P = texture(uPosTex, aRef);
    vec4 V = texture(uVelTex, aRef);

    vec3  wpos = P.xyz;
    float life = clamp(P.w, 0.0, 1.0);
    vec3  wvel = V.xyz;

    float speed = length(wvel);
    float h     = hash12(aRef * 991.0);

    vec3  vpos     = (modelViewMatrix * vec4(wpos, 1.0)).xyz;
    float viewDist = max(-vpos.z, 1e-3);

    vec3  vdir = (modelViewMatrix * vec4(wvel, 0.0)).xyz;
    float vlen = length(vdir);
    vdir = vlen > 1e-5 ? vdir / vlen : vec3(1.0, 0.0, 0.0);

    // Camera-facing frame: `side` is perpendicular to both the fiber and the
    // eye ray, so the capsule always presents its full width to the viewer.
    vec3  toEye   = normalize(-vpos);
    vec3  side    = cross(vdir, toEye);
    float sideLen = length(side);
    // Degenerate case: fiber pointing straight down the eye ray. Any
    // perpendicular is correct, it is a single dot on screen anyway.
    side = sideLen > 1e-4
        ? side / sideLen
        : normalize(cross(vdir, vec3(0.0, 1.0, 0.0)) + vec3(1e-3, 0.0, 0.0));

    float speedT = saturate1(speed / max(uSpeedRef, 1e-4));
    float depthT = saturate1((viewDist - uDepthRange.x)
                             / max(uDepthRange.y - uDepthRange.x, 1e-4));

    // Fast fibers stretch, slow ones stay short. This alone removes most of the
    // "uniform particle system" feeling.
    float halfLen = uFiberLength * (0.55 + 0.62 * speedT) * (0.60 + 0.85 * h)
                    * (1.0 + uAudio.x * 0.4);
    float halfWid = uFiberWidth * (0.62 + 0.76 * hash12(aRef * 317.0 + 5.0));

    // Sub-pixel fibers are widened to ~uMinPixelWidth and dimmed by exactly the
    // amount of area that was added. Energy is preserved, so the far field stays
    // the right brightness instead of shimmering as fibers cross pixel centres.
    float pxWorld  = viewDist * uPixelScale;
    float wid      = max(halfWid, uMinPixelWidth * pxWorld);
    float thinFade = halfWid / wid;

    // life runs 1 -> 0, so "just born" is near 1.
    float lifeFade = smoothstep(1.0, 0.86, life) * smoothstep(0.0, 0.18, life);

    // Two octaves of very low frequency noise decide where fibers are allowed to
    // exist at all. Fibers drift through the field and dissolve at its edges, so
    // the body of light keeps reshaping itself without anything popping.
    float maskN = snoise3(wpos * uMaskScale + uMaskOffset) * 0.5 + 0.5;
    maskN = maskN * 0.72
          + 0.28 * (snoise3(wpos * uMaskScale * 2.63 + uMaskOffset * 1.71 + 31.0) * 0.5 + 0.5);
    // Floor rather than a hard cut. A pure 0/1 mask makes the covered fraction of
    // the frame swing with the drifting field, so the composition would empty out
    // for a while every few minutes. Dimming instead of deleting keeps a faint
    // background layer everywhere - which also supplies the depth haze - while
    // the tone curve still crushes it to true black where nothing else is near.
    float mask = 0.15 + 0.85 * smoothstep(uMaskRange.x, uMaskRange.y, maskN);

    // Soft edge of the working volume: the box must never become visible.
    vec3  e        = abs(wpos) / uBounds;
    float edgeFade = 1.0 - smoothstep(0.76, 1.0, max(max(e.x, e.y), e.z));

    // Aerial perspective. The far field decaying into black is what creates the
    // impression of real volume rather than a flat sheet of sprites.
    float depthFade = mix(1.0, 0.095, depthT * depthT);

    vAlpha  = lifeFade * edgeFade * depthFade * thinFade * mask * uBrightness;
    vColorT = V.w;
    vSpeedT = speedT;
    vDepthT = depthT;
    vSeed   = h;

    // Fibers that would contribute nothing are collapsed outside the clip
    // volume so the rasteriser never touches them.
    if (vAlpha < 0.0025) {
        gl_Position = vec4(2.0, 2.0, 2.0, 1.0);
        return;
    }

    float halfSpan = halfLen + wid;   // extra room for the round caps
    vAspect = halfLen / wid;
    vQuad   = position.xy;

    vec3 offset = vdir * (halfSpan * position.y) + side * (wid * position.x);
    gl_Position = projectionMatrix * vec4(vpos + offset, 1.0);
}
