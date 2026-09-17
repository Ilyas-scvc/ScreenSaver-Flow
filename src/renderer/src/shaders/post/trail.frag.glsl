// ---------------------------------------------------------------------------
// trail.frag.glsl - temporal persistence.
//
// The previous HDR frame is re-blitted at a reduced amplitude before the new
// fibers are drawn on top. This is what turns discrete moving dots into silky
// continuous strands, and it costs one fullscreen pass instead of storing
// history per particle.
// ---------------------------------------------------------------------------

#include "lib/common.glsl"

in vec2 vUv;

uniform sampler2D uPrev;
uniform float uDecay;
uniform float uSoftness;   // 0 = crisp trail, 1 = fully blurred trail
uniform vec2  uTexel;

out vec4 fragColor;

void main() {
    vec3 c = texture(uPrev, vUv).rgb;

    // Four bilinear taps at half-texel diagonals = a cheap 3x3 box. Softening
    // the history slightly stops the trail from turning into visible ghost
    // copies of the fiber and makes it read as motion blur instead.
    vec2 o = uTexel * 0.75;
    vec3 blurred =
          texture(uPrev, vUv + vec2( o.x,  o.y)).rgb
        + texture(uPrev, vUv + vec2(-o.x,  o.y)).rgb
        + texture(uPrev, vUv + vec2( o.x, -o.y)).rgb
        + texture(uPrev, vUv + vec2(-o.x, -o.y)).rgb;
    blurred *= 0.25;

    fragColor = vec4(mix(c, blurred, uSoftness) * uDecay, 1.0);
}
