using Godot;
using System;

public partial class TerrainPlanet : Node3D
{
	[Export] public float RotationSpeed = 0.1f;
	[Export] public float PlanetRadius = 5.0f;
	[Export] public float HeightScale = 0.5f;
	[Export] public int TextureWidth = 2048;
	[Export] public int TextureHeight = 1024;
	[Export] public int VisualSubdivisionsRadial = 128;
	[Export] public int VisualSubdivisionsHeight = 256;
	[Export] public int CollisionSubdivisionsRadial = 24;
	[Export] public int CollisionSubdivisionsHeight = 48;

	private SubViewport _terrainViewport;
	private ColorRect _terrainColorRect;
	private ViewportTexture _terrainTexture;
	private Image _heightmapImage;
	private MeshInstance3D _visualMesh;
	private StaticBody3D _collisionBody;

	public override void _Ready()
	{
		GD.Print("TerrainPlanet initializing...");

		// Create the terrain texture generation pipeline
		CreateTerrainTexture();

		// Wait one frame for viewport to render
		CallDeferred(MethodName.CreateMeshes);
	}

	public override void _Process(double delta)
	{
		RotateY(RotationSpeed * (float)delta);
	}

	private void CreateTerrainTexture()
	{
		// Create SubViewport for terrain texture generation
		_terrainViewport = new SubViewport();
		_terrainViewport.Size = new Vector2I(TextureWidth, TextureHeight);
		_terrainViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
		_terrainViewport.TransparentBg = true;
		AddChild(_terrainViewport);

		// Create ColorRect with shader material
		_terrainColorRect = new ColorRect();
		_terrainColorRect.Size = new Vector2(TextureWidth, TextureHeight);

		// Load terrain generation shader
		var terrainShader = GD.Load<Shader>("res://shaders/terrain_generation.gdshader");
		var terrainMaterial = new ShaderMaterial();
		terrainMaterial.Shader = terrainShader;
		terrainMaterial.SetShaderParameter("height_scale", HeightScale);

		_terrainColorRect.Material = terrainMaterial;
		_terrainViewport.AddChild(_terrainColorRect);

		// Get the viewport texture
		_terrainTexture = _terrainViewport.GetTexture();
	}

	private void CreateMeshes()
	{
		GD.Print("Creating meshes...");

		// Extract heightmap data from viewport texture
		_heightmapImage = _terrainTexture.GetImage();

		// Create visual mesh
		CreateVisualMesh();

		// Create collision mesh
		CreateCollisionMesh();

		GD.Print("TerrainPlanet initialization complete!");
	}

	private void CreateVisualMesh()
	{
		_visualMesh = new MeshInstance3D();

		// Create sphere mesh
		var sphereMesh = new SphereMesh();
		sphereMesh.Radius = PlanetRadius;
		sphereMesh.Height = PlanetRadius * 2.0f;
		sphereMesh.RadialSegments = VisualSubdivisionsRadial;
		sphereMesh.Rings = VisualSubdivisionsHeight;

		_visualMesh.Mesh = sphereMesh;

		// Apply visual shader
		var visualShader = GD.Load<Shader>("res://shaders/planet_visual.gdshader");
		var visualMaterial = new ShaderMaterial();
		visualMaterial.Shader = visualShader;
		visualMaterial.SetShaderParameter("terrain_texture", _terrainTexture);
		visualMaterial.SetShaderParameter("height_scale", HeightScale);
		visualMaterial.SetShaderParameter("planet_radius", PlanetRadius);

		_visualMesh.MaterialOverride = visualMaterial;
		AddChild(_visualMesh);
	}

	private void CreateCollisionMesh()
	{
		_collisionBody = new StaticBody3D();

		// Generate collision mesh data
		var surfaceTool = new SurfaceTool();
		surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

		int radialSegments = CollisionSubdivisionsRadial;
		int heightSegments = CollisionSubdivisionsHeight;

		// Generate vertices with height displacement
		for (int y = 0; y <= heightSegments; y++)
		{
			float v = (float)y / heightSegments;
			float theta = v * Mathf.Pi;

			for (int x = 0; x <= radialSegments; x++)
			{
				float u = (float)x / radialSegments;
				float phi = u * Mathf.Pi * 2.0f;

				// Calculate sphere position
				Vector3 unitPos = new Vector3(
					Mathf.Sin(phi) * Mathf.Sin(theta),
					-Mathf.Cos(theta),
					Mathf.Cos(phi) * Mathf.Sin(theta)
				);

				// Sample height from texture
				float height = SampleHeightFromUV(u, v);
				Vector3 vertexPos = unitPos * (PlanetRadius + height);

				surfaceTool.AddVertex(vertexPos);
			}
		}

		// Generate indices for triangles
		for (int y = 0; y < heightSegments; y++)
		{
			for (int x = 0; x < radialSegments; x++)
			{
				int current = y * (radialSegments + 1) + x;
				int next = current + radialSegments + 1;

				// First triangle
				surfaceTool.AddIndex(current);
				surfaceTool.AddIndex(next);
				surfaceTool.AddIndex(current + 1);

				// Second triangle
				surfaceTool.AddIndex(current + 1);
				surfaceTool.AddIndex(next);
				surfaceTool.AddIndex(next + 1);
			}
		}

		surfaceTool.GenerateNormals();
		var collisionMesh = surfaceTool.Commit();

		// Create collision shape
		var collisionShape = new CollisionShape3D();
		collisionShape.Shape = collisionMesh.CreateTrimeshShape();

		_collisionBody.AddChild(collisionShape);
		AddChild(_collisionBody);
	}

	private float SampleHeightFromUV(float u, float v)
	{
		if (_heightmapImage == null)
			return 0.0f;

		// Wrap U coordinate
		u = u % 1.0f;
		if (u < 0) u += 1.0f;

		// Clamp V coordinate
		v = Mathf.Clamp(v, 0.0f, 1.0f);

		// Convert to pixel coordinates
		int px = (int)(u * _heightmapImage.GetWidth()) % _heightmapImage.GetWidth();
		int py = (int)(v * _heightmapImage.GetHeight());
		py = Mathf.Clamp(py, 0, _heightmapImage.GetHeight() - 1);

		// Sample height (R channel)
		Color pixel = _heightmapImage.GetPixel(px, py);
		return pixel.R * HeightScale;
	}

	/// <summary>
	/// Query height at a specific latitude and longitude on the planet surface.
	/// </summary>
	/// <param name="latitude">Latitude in radians (-PI/2 to PI/2)</param>
	/// <param name="longitude">Longitude in radians (-PI to PI)</param>
	/// <returns>Height value scaled by HeightScale</returns>
	public float GetHeightAtLatLong(float latitude, float longitude)
	{
		if (_heightmapImage == null)
		{
			GD.PrintErr("Heightmap image not yet generated!");
			return 0.0f;
		}

		// Convert lat-long to UV coordinates
		float u = longitude / (2.0f * Mathf.Pi) + 0.5f;
		float v = latitude / Mathf.Pi + 0.5f;

		// Ensure UV coordinates wrap correctly
		u = u % 1.0f;
		if (u < 0) u += 1.0f;
		v = Mathf.Clamp(v, 0.0f, 1.0f);

		// Convert to pixel coordinates with bilinear sampling
		float fx = u * (_heightmapImage.GetWidth() - 1);
		float fy = v * (_heightmapImage.GetHeight() - 1);

		int x0 = (int)Mathf.Floor(fx);
		int y0 = (int)Mathf.Floor(fy);
		int x1 = (x0 + 1) % _heightmapImage.GetWidth();
		int y1 = Mathf.Min(y0 + 1, _heightmapImage.GetHeight() - 1);

		float tx = fx - x0;
		float ty = fy - y0;

		// Bilinear interpolation
		float h00 = _heightmapImage.GetPixel(x0, y0).R;
		float h10 = _heightmapImage.GetPixel(x1, y0).R;
		float h01 = _heightmapImage.GetPixel(x0, y1).R;
		float h11 = _heightmapImage.GetPixel(x1, y1).R;

		float h0 = Mathf.Lerp(h00, h10, tx);
		float h1 = Mathf.Lerp(h01, h11, tx);
		float height = Mathf.Lerp(h0, h1, ty);

		return height * HeightScale;
	}
}
