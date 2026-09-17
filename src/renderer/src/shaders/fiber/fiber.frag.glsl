// ---------------------------------------------------------------------------
// fiber.frag.glsl - analytic capsule, shaded from the palette LUT.
//
// The capsule is evaluated as a signed distance in "half-width" units, which
// gives resolution-independent antialiasing for free: no MSAA, no line
// primitives, no edge stair-stepping at any zoom level.
// ---------------------------------------------------------------------------

#include "lib/common.glsl"

in vec2  vQuad;
in float vAspect;
in float vAlpha;
in float vColorT;
in float vSpeedT;
in float vDepthT;
in float vSeed;

uniform sampler2D uPalette;
uniform float uPaletteShift;
uniform float uWhitePoint;
uniform vec4  uAudio;

out vec4 fragColor;

void main() {
    // Distance from the capsule spine, in half-width units.
    float x    = abs(vQuad.x);
    float yw   = abs(vQuad.y) * (vAspect + 1.0);
    float over = max(yw - vAspect, 0.0);
    float d    = length(vec2(x, over));

    if (d > 1.0) discard;

    // Gaussian core plus a soft shoulder. A plain smoothstep edge reads as
    // plastic; the gaussian is what makes the fiber look lit from inside.
    float core     = exp(-2.5 * d * d);
    float shoulder = 1.0 - smoothstep(0.45, 1.0, d);

    // Thin the fiber toward its tips and brighten the leading end, so each
    // streak has direction instead of being a symmetric dash.
    float alongT = saturate1(yw / max(vAspect, 1e-3));
    float taper  = 1.0 - 0.55 * alongT * alongT;
    float head   = smoothstep(0.15, 1.0, vQuad.y);

    float intensity = core * shoulder * taper * (1.0 + 0.85 * head * head);

    // An fbm clusters hard around its mean, so feeding it in raw would only ever
    // exercise the middle of the ramp. Expanding it first is what lets the
    // palette actually reach both its violet floor and its white ceiling.
    float base = smoothstep(0.18, 0.82, vColorT);
    float t = saturate1(base * 0.66 + 0.30 * vSpeedT + 0.10 * vSeed + uPaletteShift);
    // Distant fibers sit lower on the ramp: aerial perspective in hue, not only
    // in brightness. Scaling rather than wrapping avoids a hue discontinuity.
    t = mix(t, t * 0.70, vDepthT);
    vec3 col = texture(uPalette, vec2(t, 0.5)).rgb;

    // Where the flow is fastest the fibers bleach toward white. Combined with
    // additive blending this is what lights up dense regions.
    float hot = saturate1(pow(vSpeedT, 2.2)) * uWhitePoint * (1.0 + uAudio.z * 0.5);
    col = mix(col, vec3(1.0), saturate1(hot) * 0.75);

    // Distance desaturation.
    col *= mix(1.0, 0.66, vDepthT);

    fragColor = vec4(col * intensity * vAlpha, 1.0);
}
