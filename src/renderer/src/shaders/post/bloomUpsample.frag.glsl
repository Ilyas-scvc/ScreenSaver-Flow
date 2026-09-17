// 9-tap tent upsample, accumulated additively into the next larger mip.
// Chaining tent filters across six mips approximates a very wide gaussian at a
// fraction of the cost, and - unlike separable blurs at a fixed radius - the
// falloff stays smooth all the way out to the edge of the screen.
in vec2 vUv;

uniform sampler2D uSrc;
uniform vec2  uTexel;
uniform float uRadius;

out vec4 fragColor;

void main() {
    vec2 t = uTexel * uRadius;

    vec3 s  = texture(uSrc, vUv + vec2(-t.x,  t.y)).rgb;
    s += texture(uSrc, vUv + vec2( 0.0,  t.y)).rgb * 2.0;
    s += texture(uSrc, vUv + vec2( t.x,  t.y)).rgb;

    s += texture(uSrc, vUv + vec2(-t.x,  0.0)).rgb * 2.0;
    s += texture(uSrc, vUv                   ).rgb * 4.0;
    s += texture(uSrc, vUv + vec2( t.x,  0.0)).rgb * 2.0;

    s += texture(uSrc, vUv + vec2(-t.x, -t.y)).rgb;
    s += texture(uSrc, vUv + vec2( 0.0, -t.y)).rgb * 2.0;
    s += texture(uSrc, vUv + vec2( t.x, -t.y)).rgb;

    fragColor = vec4(s * (1.0 / 16.0), 1.0);
}
