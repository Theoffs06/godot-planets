shader_type spatial;

uniform sampler2D terrain_texture : filter_linear, repeat_disable;
uniform float height_scale = 0.5;
uniform float planet_radius = 5.0;

// Convert 3D position on sphere to UV coordinates
vec2 positionToUV(vec3 pos) {
    // Normalize to unit sphere
    vec3 unit = normalize(pos);

    // Convert to spherical coordinates
    float theta = acos(-unit.y); // 0 to PI
    float phi = atan(unit.x, unit.z); // -PI to PI

    // Convert to UV (0 to 1)
    float u = phi / (2.0 * PI) + 0.5;
    float v = theta / PI;

    return vec2(u, v);
}

void vertex() {
    // Get UV coordinates for this vertex position
    vec3 local_pos = VERTEX;
    vec2 uv = positionToUV(local_pos);

    // Sample height from terrain texture
    float height = texture(terrain_texture, uv).r;

    // Displace vertex along normal
    vec3 displaced_pos = normalize(local_pos) * (planet_radius + height * height_scale);

    VERTEX = displaced_pos;
}

void fragment() {
    // Get UV coordinates for fragment
    vec2 uv = positionToUV(VERTEX);

    // Sample terrain data
    vec4 terrain_data = texture(terrain_texture, uv);
    float height = terrain_data.r;

    ALBEDO = vec3(height, height, height);
    ROUGHNESS = 0.8;
    SPECULAR = 0.2;
}
