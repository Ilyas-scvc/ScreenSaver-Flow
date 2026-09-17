// Soft-knee threshold. A hard threshold makes bloom pop on and off as fibers
// cross it; the knee gives a continuous ramp so the glow breathes smoothly.
#include "lib/common.glsl"

in vec2 vUv;

uniform sampler2D uSrc;
uniform vec4  uFilter;   // x = threshold, y = threshold-knee, z = 2*knee, w = 0.25/knee
uniform float uClampMax;

out vec4 fragColor;

void main() {
    vec3 c = texture(uSrc, vUv).rgb;
    c = min(c, vec3(uClampMax));   // stop a single hot fiber from flaring the frame

    float br = max(c.r, max(c.g, c.b));
    float rq = clamp(br - uFilter.y, 0.0, uFilter.z);
    rq = rq * rq * uFilter.w;

    float contrib = max(rq, br - uFilter.x) / max(br, 1e-5);
    fragColor = vec4(c * contrib, 1.0);
}
