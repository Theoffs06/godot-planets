shader_type spatial;

render_mode unshaded; //, wireframe, cull_disabled;
uniform sampler2D terrain_texture : filter_linear, repeat_disable;
uniform float height_scale;
uniform float planet_radius;

varying vec3 local_pos;

// Convert 3D position on sphere to UV coordinates
vec2 positionToUV(vec3 pos) {
    // Normalize to unit sphere
    vec3 unit = normalize(pos);

    // Convert to spherical coordinates
    float theta = acos(unit.y); // 0 to PI
    float phi = atan(unit.x, unit.z); // -PI to PI

    // Convert to UV (0 to 1)
    // Use fract to handle negative phi values correctly
    float u = fract(phi / (2.0 * PI));
    float v = 1.0-theta / PI;

    return vec2(u, v);
}

void vertex() {
    // Get UV coordinates for this vertex position
    vec3 lpos = vec3(VERTEX);
    local_pos = lpos;
    vec2 uv = positionToUV(lpos);

    // Sample height from terrain texture
    float height = texture(terrain_texture, uv).r;

    // Displace vertex along normal
    vec3 displaced_pos = normalize(local_pos) * (planet_radius + height * height_scale);

    VERTEX = displaced_pos;
}

void fragment() {
    vec2 uv = positionToUV(local_pos);

    vec4 terrain_data = texture(terrain_texture, uv);
    float height = terrain_data.r;
    float v = height - 1.0;

    ALBEDO = vec3(v,v, v);
    //ALBEDO = vec3(uv.x, uv.y, 0);
    ROUGHNESS = 0.8;
    SPECULAR = 0.2;
}
