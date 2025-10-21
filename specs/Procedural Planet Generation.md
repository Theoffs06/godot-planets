# Updated Procedural Planet Generation Spec

## Core Components

1. **Terrain Texture Generation**
   - Use `SubViewport` with `ColorRect` and `ShaderMaterial` to create 2048×1024 RGBA32F texture
   - R: height (3D noise at sphere positions)
   - GBA: additional channels for planet characteristics (leave blank)
   - Implement equirectangular to 3D position mapping to prevent seams and pole pinching

2. **Visual Mesh**
   - `MeshInstance3D` with `SphereMesh` (128×256 subdivisions)
   - Apply `ShaderMaterial` reading the terrain texture (placeholder color based on height)

3. **Collision Mesh**
   - `StaticBody3D` with `ConcavePolygonShape3D` (trimesh)
   - Lower resolution (24×48 subdivisions) for performance
   - Sample height channel (R) from terrain texture

## Implementation Pipeline

1. **Generate Terrain Texture**:
   ```
   SubViewport (with "Transparent Bg" enabled) → 
   ColorRect → ShaderMaterial (with equirectangular mapping and layered noise) → 
   ViewportTexture
   ```

2. **Coordinate Mapping**:
   ```glsl
   // In shader: Convert UVs to 3D sphere position
   float theta = UV.y * PI;
   float phi = UV.x * PI * 2.0;
   vec3 unit = vec3(
       sin(phi) * sin(theta),
       cos(theta) * -1.0,
       cos(phi) * sin(theta)
   );
   // Sample noise at this 3D position
   ```

3. **Create Meshes**:
   ```
   ViewportTexture → read height data (R channel) → 
   build high-res visual mesh and low-res collision mesh with same coordinate mapping
   ```

4. **Height Query Function**:
   ```csharp
   // Function that converts lat-long to UV, then samples heightmap
   public float GetHeightAtLatLong(float latitude, float longitude)
   {
       float u = longitude / (2.0f * Mathf.Pi) + 0.5f;
       float v = latitude / Mathf.Pi + 0.5f;
       
       // Ensure UV coordinates wrap correctly
       u = u % 1.0f;
       if (u < 0) u += 1.0f;
       v = Mathf.Clamp(v, 0, 1.0f);
       
       return _heightmapTexture.GetPixelBilinear(u, v).r * _heightScale;
   }
   ```
