// ---------------------------------------------------------------------------
// composite.frag.glsl - scene + bloom -> tone mapped sRGB.
//
// The black-point toe is the important part. Additive fibers plus a wide bloom
// will happily lift the entire frame to a dull pink haze; the toe pushes
// everything below the threshold back to *exact* zero, which is what keeps 40-70%
// of the image at true black on an OLED panel.
// ---------------------------------------------------------------------------

#include "lib/common.glsl"

in vec2 vUv;

uniform sampler2D uScene;
uniform sampler2D uBloom;

uniform float uBloomStrength;
uniform float uExposure;
uniform float uBlackPoint;
uniform float uSaturation;
uniform float uVignette;
uniform float uTime;

out vec4 fragColor;

// ACES filmic approximation (Stephen Hill's fit). Chosen over Reinhard because
// it keeps hue on the way to white, so hot pink highlights bleach to white
// rather than sliding through orange.
const mat3 ACES_IN = mat3(
    0.59719, 0.07600, 0.02840,
    0.35458, 0.90834, 0.13383,
    0.04823, 0.01566, 0.83777
);
const mat3 ACES_OUT = mat3(
     1.60475, -0.10208, -0.00327,
    -0.53108,  1.10813, -0.07276,
    -0.07367, -0.00605,  1.07602
);

vec3 rrtOdtFit(vec3 v) {
    vec3 a = v * (v + 0.0245786) - 0.000090537;
    vec3 b = v * (0.983729 * v + 0.4329510) + 0.238081;
    return a / b;
}

vec3 acesFitted(vec3 color) {
    return saturate3(ACES_OUT * rrtOdtFit(ACES_IN * color));
}

vec3 linearToSRGB(vec3 c) {
    c = max(c, vec3(0.0));
    return mix(c * 12.92,
               1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055,
               step(vec3(0.0031308), c));
}

void main() {
    vec3 scene = texture(uScene, vUv).rgb;
    vec3 bloom = texture(uBloom, vUv).rgb;

    vec3 col = scene + bloom * uBloomStrength;
    col *= uExposure;

    // Smooth toe: x^2 / (x + k). Behaves like x^2/k near black and like x well
    // above it, so the haze disappears with no visible clipping edge.
    col = (col * col) / (col + vec3(uBlackPoint));

    col = acesFitted(col);

    float luma = dot(col, vec3(0.2126, 0.7152, 0.0722));
    col = mix(vec3(luma), col, uSaturation);

    float r = length(vUv - 0.5) * 1.41421356;
    col *= 1.0 - uVignette * pow(saturate1(r), 2.6);

    col = linearToSRGB(col);

    // Ordered-ish dither at exactly one 8-bit step. Deep magenta-to-black
    // gradients band horribly without it, and banding is the fastest way to
    // make a slow gradient look cheap.
    float n = hash12(gl_FragCoord.xy + fract(uTime) * 37.0);
    col += (n - 0.5) * (1.0 / 255.0);

    fragColor = vec4(col, 1.0);
}
