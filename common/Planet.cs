using Godot;
using System;

public partial class Planet : Node3D
{
	[Export] public float RotationSpeed = 0.5f;

	public override void _Ready()
	{
		GD.Print("Planet initialized");
	}

	public override void _Process(double delta)
	{
		RotateY(RotationSpeed * (float)delta);
	}

	/// <summary>
	/// Get the gravitational force at a given position in world space.
	/// Returns a force vector pointing towards the planet's center of gravity.
	/// </summary>
	/// <param name="position">World space position to query</param>
	/// <returns>Force vector (direction and magnitude)</returns>
	public virtual Vector3 GetForce(Vector3 position)
	{
		// Default implementation: simple radial gravity towards planet center
		Vector3 toPlanet = GlobalPosition - position;
		float distance = toPlanet.Length();

		if (distance < 0.001f)
			return Vector3.Zero;

		return toPlanet.Normalized() * 9.8f; // Standard gravity magnitude
	}
}
