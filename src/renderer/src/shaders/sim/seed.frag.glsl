// Blits the CPU-generated initial state into the multi-target ping-pong pair.
// A dedicated pass is simpler (and more portable) than trying to upload
// directly into an MRT framebuffer's attachments.
in vec2 vUv;

layout(location = 0) out vec4 oPos;
layout(location = 1) out vec4 oVel;

uniform sampler2D uPosSeed;
uniform sampler2D uVelSeed;

void main() {
    oPos = texture(uPosSeed, vUv);
    oVel = texture(uVelSeed, vUv);
}
