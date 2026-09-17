// Fullscreen pass vertex shader. Used by the simulation and every post step.
in vec3 position;
in vec2 uv;

out vec2 vUv;

void main() {
    vUv = uv;
    gl_Position = vec4(position.xy, 0.0, 1.0);
}
