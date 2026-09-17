// 13-tap downsample (Jimenez, "Next Generation Post Processing in Call of Duty
// Advanced Warfare", SIGGRAPH 2014). The partially overlapping 4-tap groups are
// what suppress the flickering that a naive box filter produces on small bright
// features - and a field of thin fibers is nothing but small bright features.
in vec2 vUv;

uniform sampler2D uSrc;
uniform vec2 uTexel;

out vec4 fragColor;

void main() {
    vec2 t = uTexel;

    vec3 a = texture(uSrc, vUv + t * vec2(-2.0,  2.0)).rgb;
    vec3 b = texture(uSrc, vUv + t * vec2( 0.0,  2.0)).rgb;
    vec3 c = texture(uSrc, vUv + t * vec2( 2.0,  2.0)).rgb;
    vec3 d = texture(uSrc, vUv + t * vec2(-2.0,  0.0)).rgb;
    vec3 e = texture(uSrc, vUv                       ).rgb;
    vec3 f = texture(uSrc, vUv + t * vec2( 2.0,  0.0)).rgb;
    vec3 g = texture(uSrc, vUv + t * vec2(-2.0, -2.0)).rgb;
    vec3 h = texture(uSrc, vUv + t * vec2( 0.0, -2.0)).rgb;
    vec3 i = texture(uSrc, vUv + t * vec2( 2.0, -2.0)).rgb;

    vec3 j = texture(uSrc, vUv + t * vec2(-1.0,  1.0)).rgb;
    vec3 k = texture(uSrc, vUv + t * vec2( 1.0,  1.0)).rgb;
    vec3 l = texture(uSrc, vUv + t * vec2(-1.0, -1.0)).rgb;
    vec3 m = texture(uSrc, vUv + t * vec2( 1.0, -1.0)).rgb;

    vec3 res = e * 0.125;
    res += (a + c + g + i) * 0.03125;
    res += (b + d + f + h) * 0.0625;
    res += (j + k + l + m) * 0.125;

    fragColor = vec4(res, 1.0);
}
