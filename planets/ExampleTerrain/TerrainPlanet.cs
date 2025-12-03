using Godot;
using System;

public partial class TerrainPlanet : Planet
{
	static readonly string ResPath = "res://Planets/ExampleTerrain/";
	[Export] public float PlanetRadius = 50.0f;
	[Export] public float GravityStrength = 9.8f;
	[Export] public float HeightScale = 10.0f;
	[Export] public int TextureWidth = 2048;
	[Export] public int TextureHeight = 1024;
	[Export] public int VisualSubdivisionsRadial = 256;
	[Export] public int VisualSubdivisionsHeight = 512;
	[Export] public bool ShowCollisionMesh = false;
	private bool _regenerateRequested = false;

	[Export]
	public bool Regenerate
	{
		get => false;
		set
		{
			if (value)
			{
				_regenerateRequested = true;
				NotifyPropertyListChanged();
			}
		}
	}

	private SubViewport _terrainViewport;
	private ColorRect _terrainColorRect;
	private ViewportTexture _terrainTexture;
	private Image _heightmapImage;
	private MeshInstance3D _visualMesh;
	private StaticBody3D _collisionBody;
	private Node3D _propsContainer;

	public override void _Ready()
	{
		CreateTerrainTexture();
		WaitForViewportAndCreateMeshes();
	}
	
	private async void WaitForViewportAndCreateMeshes()
	{
		// Wait for the next frame
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		
		// Force the rendering server to render
		RenderingServer.ForceSync();
		
		// Wait one more frame to be safe
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		
		// Now create the meshes
		CreateMeshes();
	}

	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint() && _regenerateRequested)
		{
			_regenerateRequested = false;
			RegeneratePlanet();
		}
	}

	async void RegeneratePlanet()
	{
		GD.Print("Regenerating planet...");

		// Clean up existing children
		foreach (Node child in GetChildren())
		{
			child.QueueFree();
		}

		// Wait for cleanup to complete
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		
		// Recreate everything
		CreateTerrainTexture();
		
		// Wait for viewport to render, then create meshes
		WaitForViewportAndCreateMeshes();
	}

	void CreateTerrainTexture()
	{
		_terrainViewport = new SubViewport();
		_terrainViewport.Size = new Vector2I(TextureWidth, TextureHeight);
		_terrainViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
		_terrainViewport.TransparentBg = true;
		_terrainViewport.UseHdr2D = true;
		AddChild(_terrainViewport);

		_terrainColorRect = new ColorRect();
		_terrainColorRect.Size = new Vector2(TextureWidth, TextureHeight);

		var terrainShader = GD.Load<Shader>(ResPath + "terrain_generation.gdshader");
		var terrainMaterial = new ShaderMaterial();
		terrainMaterial.Shader = terrainShader;
		terrainMaterial.SetShaderParameter("height_scale", HeightScale);

		_terrainColorRect.Material = terrainMaterial;
		_terrainViewport.AddChild(_terrainColorRect);

		_terrainTexture = _terrainViewport.GetTexture();
	}

	void CreateMeshes()
	{
		_heightmapImage = _terrainTexture.GetImage();
		CreateVisualMesh();
		CreateCollisionMesh();
		SpawnTrees();
	}

	void CreateVisualMesh()
	{
		_visualMesh = new MeshInstance3D();

		// Create sphere mesh
		var sphereMesh = new SphereMesh();
		sphereMesh.Radius = PlanetRadius;
		sphereMesh.Height = PlanetRadius * 2.0f;
		sphereMesh.RadialSegments = VisualSubdivisionsRadial;
		sphereMesh.Rings = VisualSubdivisionsHeight;

		_visualMesh.Mesh = sphereMesh;
		_visualMesh.ExtraCullMargin = HeightScale;

		// Apply visual shader
		var visualShader = GD.Load<Shader>(ResPath + "planet_visual.gdshader");
		var visualMaterial = new ShaderMaterial();
		visualMaterial.Shader = visualShader;
		visualMaterial.SetShaderParameter("terrain_texture", _terrainTexture);
		visualMaterial.SetShaderParameter("height_scale", HeightScale);
		visualMaterial.SetShaderParameter("planet_radius", PlanetRadius);

		_visualMesh.MaterialOverride = visualMaterial;
		AddChild(_visualMesh);
	}

	void CreateCollisionMesh()
	{
		_collisionBody = new StaticBody3D();

		// Generate collision mesh data
		var surfaceTool = new SurfaceTool();
		surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

		int radialSegments = VisualSubdivisionsRadial >> 1;
		int heightSegments = VisualSubdivisionsHeight >> 1;

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
		
		// Add debug visualization if enabled
		if (ShowCollisionMesh)
		{
			var debugMesh = new MeshInstance3D();
			debugMesh.Mesh = collisionMesh;
			
			// Create a semi-transparent material for visualization
			var debugMaterial = new StandardMaterial3D();
			debugMaterial.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			debugMaterial.AlbedoColor = new Color(0, 1, 0, 0.3f); // Green with 30% opacity
			debugMaterial.CullMode = BaseMaterial3D.CullModeEnum.Disabled; // Show both sides
			debugMaterial.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			
			debugMesh.MaterialOverride = debugMaterial;
			_collisionBody.AddChild(debugMesh);
		}
		
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

	private Vector3 GetNormalAtUV(float u, float v)
	{
		if (_heightmapImage == null)
			return Vector3.Up;

		// Sample height at the point and nearby points
		float epsilon = 1.0f / _heightmapImage.GetWidth();

		float h_center = SampleHeightFromUV(u, v);
		float h_right = SampleHeightFromUV(u + epsilon, v);
		float h_up = SampleHeightFromUV(u, v + epsilon);

		// Calculate sphere position at center
		float theta = v * Mathf.Pi;
		float phi = u * Mathf.Pi * 2.0f;

		Vector3 centerUnitPos = new Vector3(
			Mathf.Sin(phi) * Mathf.Sin(theta),
			-Mathf.Cos(theta),
			Mathf.Cos(phi) * Mathf.Sin(theta)
		);

		// Calculate positions for tangent vectors
		float theta_right = v * Mathf.Pi;
		float phi_right = (u + epsilon) * Mathf.Pi * 2.0f;
		Vector3 rightUnitPos = new Vector3(
			Mathf.Sin(phi_right) * Mathf.Sin(theta_right),
			-Mathf.Cos(theta_right),
			Mathf.Cos(phi_right) * Mathf.Sin(theta_right)
		);

		float theta_up = (v + epsilon) * Mathf.Pi;
		float phi_up = u * Mathf.Pi * 2.0f;
		Vector3 upUnitPos = new Vector3(
			Mathf.Sin(phi_up) * Mathf.Sin(theta_up),
			-Mathf.Cos(theta_up),
			Mathf.Cos(phi_up) * Mathf.Sin(theta_up)
		);

		// Apply height displacement
		Vector3 centerPos = centerUnitPos * (PlanetRadius + h_center);
		Vector3 rightPos = rightUnitPos * (PlanetRadius + h_right);
		Vector3 upPos = upUnitPos * (PlanetRadius + h_up);

		// Calculate tangent vectors
		Vector3 tangentU = (rightPos - centerPos).Normalized();
		Vector3 tangentV = (upPos - centerPos).Normalized();

		// Calculate normal using cross product
		Vector3 normal = tangentU.Cross(tangentV).Normalized();

		return normal;
	}

	public override Vector3 GetForce(Vector3 position)
	{
		Vector3 toPlanet = GlobalPosition - position;
		float distance = toPlanet.Length();

		if (distance < 0.001f)
			return Vector3.Zero;

		return toPlanet.Normalized() * GravityStrength;
	}

	void SpawnTrees()
	{
		if (_heightmapImage == null)
		{
			GD.PrintErr("Cannot spawn trees: heightmap not available");
			return;
		}

		// Load the 4 tree scenes
		PackedScene[] treeScenes = new PackedScene[]
		{
			GD.Load<PackedScene>("res://Props/Tree01.tscn"),
			GD.Load<PackedScene>("res://Props/Tree02.tscn"),
			GD.Load<PackedScene>("res://Props/Tree03.tscn"),
			GD.Load<PackedScene>("res://Props/Tree04.tscn")
		};

		// Create container for props
		_propsContainer = new Node3D();
		_propsContainer.Name = "Props";
		AddChild(_propsContainer);

		Random random = new Random();
		int treesSpawned = 0;
		int count = 300;
		int attempts = 0;

		while (treesSpawned < 300 && attempts < count * 10)
		{
			attempts++;

			// Generate random UV coordinates
			float u = (float)random.NextDouble();
			float v = (float)random.NextDouble();

			// Sample height at this location
			float height = SampleHeightFromUV(u, v);
			GD.Print($"height {height}");

			// Only spawn if height is negative
			if (height < HeightScale/2)
			{
				// Calculate sphere position
				float theta = v * Mathf.Pi;
				float phi = u * Mathf.Pi * 2.0f;

				Vector3 unitPos = new Vector3(
					Mathf.Sin(phi) * Mathf.Sin(theta),
					-Mathf.Cos(theta),
					Mathf.Cos(phi) * Mathf.Sin(theta)
				);

				Vector3 position = unitPos * (PlanetRadius + height);

				// Get normal at this location
				Vector3 normal = GetNormalAtUV(u, v);

				// Pick a random tree scene
				PackedScene treeScene = treeScenes[random.Next(treeScenes.Length)];
				Node3D tree = treeScene.Instantiate<Node3D>();
				_propsContainer.AddChild(tree);

				// Set position
				tree.GlobalPosition = position;

				// Orient the tree to align with the terrain normal
				// The tree's up direction should match the terrain normal
				Vector3 up = normal;
				Vector3 forward = up.Cross(Vector3.Right);
				if (forward.LengthSquared() < 0.001f)
				{
					forward = up.Cross(Vector3.Forward);
				}
				forward = forward.Normalized();
				Vector3 right = forward.Cross(up).Normalized();

				// Create basis from the orthogonal vectors
				Basis basis = new Basis(right, up, -forward);
				tree.GlobalBasis = basis;

				treesSpawned++;
			}
		}

		GD.Print($"Spawned {treesSpawned} trees after {attempts} attempts");
	}
}
