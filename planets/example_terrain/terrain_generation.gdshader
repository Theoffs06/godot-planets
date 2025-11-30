shader_type canvas_item;

void fragment() {
    // Convert UV to equirectangular coordinates
    float theta = UV.y * PI; // 0 to PI (top to bottom)
    float phi = UV.x * PI * 2.0; // 0 to 2*PI (around)

    // Convert to 3D unit sphere position
    vec3 unit = vec3(
        sin(phi) * sin(theta),
        cos(theta) * -1.0,
        cos(phi) * sin(theta)
    );

    float height = 1.0;
    float freq = PI * 4.0;
    float amp = 0.3;
    height += sin(unit.x * freq + 0.1) * amp;
    height += sin(unit.y * freq + 0.7) * amp;
    height += sin(unit.z * freq + 1.4) * amp;

    // R: height
    // GBA: custom layers (biomes, water etc)
    COLOR = vec4(height, 0.0, 0.0, 1.0);
}
