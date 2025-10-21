shader_type canvas_item;

uniform float height_scale = 0.5;
uniform float noise_scale = 2.0;
uniform int octaves = 6;
uniform float persistence = 0.5;
uniform float lacunarity = 2.0;
uniform int seed_value = 12345;

// Simple hash function for noise generation
float hash(vec3 p) {
    p = fract(p * vec3(443.897, 441.423, 437.195));
    p += dot(p, p.yzx + 19.19);
    return fract((p.x + p.y) * p.z);
}

// 3D Value noise
float noise3D(vec3 p) {
    vec3 i = floor(p);
    vec3 f = fract(p);

    // Smooth interpolation
    f = f * f * (3.0 - 2.0 * f);

    // Sample 8 corners of the cube
    float n000 = hash(i + vec3(0.0, 0.0, 0.0));
    float n100 = hash(i + vec3(1.0, 0.0, 0.0));
    float n010 = hash(i + vec3(0.0, 1.0, 0.0));
    float n110 = hash(i + vec3(1.0, 1.0, 0.0));
    float n001 = hash(i + vec3(0.0, 0.0, 1.0));
    float n101 = hash(i + vec3(1.0, 0.0, 1.0));
    float n011 = hash(i + vec3(0.0, 1.0, 1.0));
    float n111 = hash(i + vec3(1.0, 1.0, 1.0));

    // Trilinear interpolation
    float nx00 = mix(n000, n100, f.x);
    float nx10 = mix(n010, n110, f.x);
    float nx01 = mix(n001, n101, f.x);
    float nx11 = mix(n011, n111, f.x);

    float nxy0 = mix(nx00, nx10, f.y);
    float nxy1 = mix(nx01, nx11, f.y);

    return mix(nxy0, nxy1, f.z);
}

// Fractal Brownian Motion (FBM) for layered noise
float fbm(vec3 p) {
    float value = 0.0;
    float amplitude = 1.0;
    float frequency = 1.0;

    for (int i = 0; i < octaves; i++) {
        value += amplitude * noise3D(p * frequency);
        frequency *= lacunarity;
        amplitude *= persistence;
    }

    return value;
}

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

    // Add seed offset to the sampling position
    vec3 sample_pos = unit * noise_scale + vec3(float(seed_value) * 0.1);

    // Generate height using layered noise
    float height = fbm(sample_pos);

    // Normalize height to 0-1 range
    height = height * 0.5 + 0.5;

    // Output to R channel (height)
    // GBA channels reserved for future use (biomes, moisture, temperature, etc.)
    COLOR = vec4(height, 0.0, 0.0, 1.0);
}
