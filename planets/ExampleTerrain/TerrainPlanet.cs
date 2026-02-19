using System;
using Godot;
using planets.common;

namespace planets.planets.ExampleTerrain;

public partial class TerrainPlanet : Planet {
	private const string ResPath = "res://Planets/ExampleTerrain/";
	
	[Export] public int Seed = 50;
	[Export] public bool AutoSeed = true;
	[Export] public float PlanetRadius = 50.0f;
	[Export] public float GravityStrength = 9.8f;
	[Export] public float HeightScale = 10.0f;
	[Export] public int TextureWidth = 2048;
	[Export] public int TextureHeight = 1024;
	[Export] public int VisualSubdivisionsRadial = 256;
	[Export] public int VisualSubdivisionsHeight = 512;
	[Export] public bool ShowCollisionMesh;
	
	private bool _regenerateRequested;

	[Export]
	public bool Regenerate {
		get => false;
		set {
			if (!value) return;
			_regenerateRequested = true;
			NotifyPropertyListChanged();
		}
	}

	private SubViewport _terrainViewport;
	private ColorRect _terrainColorRect;
	private ViewportTexture _terrainTexture;
	private Image _heightmapImage;
	private MeshInstance3D _visualMesh;
	private StaticBody3D _collisionBody;
	private Node3D _propsContainer;

	public override void _Ready() {
		CreateTerrainTexture();
		WaitForViewportAndCreateMeshes();
	}
	
	private async void WaitForViewportAndCreateMeshes() {
		// Wait for the next frame
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		
		// Force the rendering server to render
		RenderingServer.ForceSync();
		
		// Wait one more frame to be safe
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		
		// Now create the meshes
		CreateMeshes();
	}

	public override void _Process(double delta) {
		if (Engine.IsEditorHint() && _regenerateRequested) {
			_regenerateRequested = false;
			RegeneratePlanet();
		}
	}

	private async void RegeneratePlanet() {
		GD.Print("Regenerating planet...");

		// Clean up existing children
		foreach (var child in GetChildren()) {
			child.QueueFree();
		}

		// Wait for cleanup to complete
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		
		// Recreate everything
		CreateTerrainTexture();
		
		// Wait for viewport to render, then create meshes
		WaitForViewportAndCreateMeshes();
	}

	private void CreateTerrainTexture() {
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

	private void CreateMeshes() {
		_heightmapImage = _terrainTexture.GetImage();
		CreateVisualMesh();
		CreateCollisionMesh();
		SpawnTrees();
	}

	private void CreateVisualMesh() {
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

	private void CreateCollisionMesh() {
		_collisionBody = new StaticBody3D();

		// Generate collision mesh data
		var surfaceTool = new SurfaceTool();
		surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

		var radialSegments = VisualSubdivisionsRadial >> 1;
		var heightSegments = VisualSubdivisionsHeight >> 1;

		// Generate vertices with height displacement
		for (var y = 0; y <= heightSegments; y++) {
			var v = (float) y / heightSegments;
			var theta = v * Mathf.Pi;

			for (var x = 0; x <= radialSegments; x++) {
				var u = (float) x / radialSegments;
				var phi = u * Mathf.Pi * 2.0f;

				// Calculate sphere position
				var unitPos = new Vector3(
					Mathf.Sin(phi) * Mathf.Sin(theta),
					-Mathf.Cos(theta),
					Mathf.Cos(phi) * Mathf.Sin(theta)
				);

				// Sample height from texture
				var height = SampleHeightFromUV(u, v);
				var vertexPos = unitPos * (PlanetRadius + height);

				surfaceTool.AddVertex(vertexPos);
			}
		}

		// Generate indices for triangles
		for (var y = 0; y < heightSegments; y++) {
			for (var x = 0; x < radialSegments; x++) {
				var current = y * (radialSegments + 1) + x;
				var next = current + radialSegments + 1;

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

		// Create a collision shape
		var collisionShape = new CollisionShape3D();
		collisionShape.Shape = collisionMesh.CreateTrimeshShape();

		_collisionBody.AddChild(collisionShape);
		
		// Add debug visualization if enabled
		if (ShowCollisionMesh) {
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

	private float SampleHeightFromUV(float u, float v) {
		if (_heightmapImage == null) return 0.0f;

		// Wrap U coordinate
		u %= 1.0f;
		if (u < 0) u += 1.0f;

		// Clamp V coordinate
		v = Mathf.Clamp(v, 0.0f, 1.0f);

		// Convert to pixel coordinates
		var px = (int) (u * _heightmapImage.GetWidth()) % _heightmapImage.GetWidth();
		var py = (int) (v * _heightmapImage.GetHeight());
		py = Mathf.Clamp(py, 0, _heightmapImage.GetHeight() - 1);

		// Sample height (R channel)
		var pixel = _heightmapImage.GetPixel(px, py);
		return pixel.R * HeightScale;
	}

	private Vector3 GetNormalAtUV(float u, float v) {
		if (_heightmapImage == null) return Vector3.Up;

		// Sample height at the point and nearby points
		var epsilon = 1.0f / _heightmapImage.GetWidth();

		var hCenter = SampleHeightFromUV(u, v);
		var hRight = SampleHeightFromUV(u + epsilon, v);
		var hUp = SampleHeightFromUV(u, v + epsilon);

		// Calculate sphere position at center
		var theta = v * Mathf.Pi;
		var phi = u * Mathf.Pi * 2.0f;

		var centerUnitPos = new Vector3(
			Mathf.Sin(phi) * Mathf.Sin(theta),
			-Mathf.Cos(theta),
			Mathf.Cos(phi) * Mathf.Sin(theta)
		);

		// Calculate positions for tangent vectors
		var thetaRight = v * Mathf.Pi;
		var phiRight = (u + epsilon) * Mathf.Pi * 2.0f;
		var rightUnitPos = new Vector3(
			Mathf.Sin(phiRight) * Mathf.Sin(thetaRight),
			-Mathf.Cos(thetaRight),
			Mathf.Cos(phiRight) * Mathf.Sin(thetaRight)
		);

		var thetaUp = (v + epsilon) * Mathf.Pi;
		var phiUp = u * Mathf.Pi * 2.0f;
		var upUnitPos = new Vector3(
			Mathf.Sin(phiUp) * Mathf.Sin(thetaUp),
			-Mathf.Cos(thetaUp),
			Mathf.Cos(phiUp) * Mathf.Sin(thetaUp)
		);

		// Apply height displacement
		var centerPos = centerUnitPos * (PlanetRadius + hCenter);
		var rightPos = rightUnitPos * (PlanetRadius + hRight);
		var upPos = upUnitPos * (PlanetRadius + hUp);

		// Calculate tangent vectors
		var tangentU = (rightPos - centerPos).Normalized();
		var tangentV = (upPos - centerPos).Normalized();

		return tangentU.Cross(tangentV).Normalized();
	}

	public override Vector3 GetForce(Vector3 position) {
		var toPlanet = GlobalPosition - position;
		var distance = toPlanet.Length();

		if (distance < 0.001f) return Vector3.Zero;
		return toPlanet.Normalized() * GravityStrength;
	}

	private void SpawnTrees() {
		if (_heightmapImage == null) {
			GD.PrintErr("Cannot spawn trees: heightmap not available");
			return;
		}

		// Load the 4 tree scenes
		PackedScene[] treeScenes = [
			GD.Load<PackedScene>("res://Props/Tree01.tscn"),
			GD.Load<PackedScene>("res://Props/Tree02.tscn"),
			GD.Load<PackedScene>("res://Props/Tree03.tscn"),
			GD.Load<PackedScene>("res://Props/Tree04.tscn")
		];

		// Create container for props
		_propsContainer = new Node3D();
		_propsContainer.Name = "Props";
		AddChild(_propsContainer);

		const int count = 300;
		
		if (AutoSeed) Seed = (int) DateTime.Now.Ticks;
		var random = new Random(Seed);
		
		var treesSpawned = 0;
		var attempts = 0;

		while (treesSpawned < 300 && attempts < count * 10) {
			attempts++;

			// Generate random UV coordinates
			var u = (float) random.NextDouble();
			var v = (float) random.NextDouble();

			// Sample height at this location
			var height = SampleHeightFromUV(u, v);

			// Only spawn if height is negative
			if (height < HeightScale / 2) {
				// Calculate sphere position
				var theta = v * Mathf.Pi;
				var phi = u * Mathf.Pi * 2.0f;

				var unitPos = new Vector3(
					Mathf.Sin(phi) * Mathf.Sin(theta),
					-Mathf.Cos(theta),
					Mathf.Cos(phi) * Mathf.Sin(theta)
				);

				var position = unitPos * (PlanetRadius + height);

				// Get normal at this location
				var normal = GetNormalAtUV(u, v);

				// Pick a random tree scene
				var treeScene = treeScenes[random.Next(treeScenes.Length)];
				var tree = treeScene.Instantiate<Node3D>();
				_propsContainer.AddChild(tree);

				// Set position
				tree.GlobalPosition = position;

				// Orient the tree to align with the terrain normal
				// The tree's up direction should match the terrain normal
				var forward = normal.Cross(Vector3.Right);
				if (forward.LengthSquared() < 0.001f) {
					forward = normal.Cross(Vector3.Forward);
				}
				
				forward = forward.Normalized();
				var right = forward.Cross(normal).Normalized();

				// Create basis from the orthogonal vectors
				var basis = new Basis(right, normal, -forward);
				tree.GlobalBasis = basis;

				treesSpawned++;
			}
		}

		GD.Print($"Spawned {treesSpawned} trees after {attempts} attempts");
	}
}