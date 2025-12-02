using Godot;
using System;

public enum CameraMode
{
	Fly,
	Walk
}

public partial class Camera : CharacterBody3D
{
	[Export] public float FlySpeed = 100.0f;
	[Export] public float WalkSpeed = 20.0f;
	[Export] public float FlyFriction = 0.02f;
	[Export] public float WalkFriction = 0.03f;

	[Export] public float MouseSensitivity = 0.003f;
	[Export] public float MinPitch = -89.0f;
	[Export] public float MaxPitch = 89.0f;
	[Export] public float JumpVelocity = 5.0f;
	[Export] public float AlignmentSpeed = 2.0f;

	private float pitch = 0.0f;
	private float yaw = 0.0f;
	
	private CameraMode mode = CameraMode.Fly;
	private Vector3 velocity = Vector3.Zero;
	private Godot.Collections.Array<Planet> planets = [];
	
	private Basis gravityAlignedBasis = Basis.Identity;
	private Basis flyBasis = Basis.Identity;
	
	public override void _Ready()
	{
		FindAllPlanets();
		Input.MouseMode = Input.MouseModeEnum.Captured;
		flyBasis = Transform.Basis.Orthonormalized();
	}

	private void FindAllPlanets()
	{
		planets.Clear();
		FindPlanetsRecursive(GetTree().Root);
	}

	private void FindPlanetsRecursive(Node node)
	{
		if (node is Planet planet && node != this)
		{
			planets.Add(planet);
		}

		foreach (Node child in node.GetChildren())
		{
			FindPlanetsRecursive(child);
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey keyEvent && keyEvent.Pressed)
		{
			if (keyEvent.Keycode == Key.Escape)
			{
				Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured 
					? Input.MouseModeEnum.Visible 
					: Input.MouseModeEnum.Captured;
			}
			else if (keyEvent.Keycode == Key.Tab)
			{
				if (mode == CameraMode.Fly)
				{
					mode = CameraMode.Walk;
					gravityAlignedBasis = flyBasis;
					pitch = 0.0f;
					yaw = 0.0f;
				}
				else
				{
					mode = CameraMode.Fly;
					flyBasis = Transform.Basis.Orthonormalized();
				}
			}
		}
		
		if (@event is InputEventMouseMotion mouseMotion && Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			if (mode == CameraMode.Fly)
			{
				float deltaYaw = -mouseMotion.Relative.X * MouseSensitivity;
				float deltaPitch = -mouseMotion.Relative.Y * MouseSensitivity;
				
				Quaternion yawRot = new Quaternion(flyBasis.Y, deltaYaw);
				flyBasis = new Basis(yawRot) * flyBasis;
				
				Quaternion pitchRot = new Quaternion(flyBasis.X, deltaPitch);
				flyBasis = new Basis(pitchRot) * flyBasis;
				
				flyBasis = flyBasis.Orthonormalized();
			}
			else
			{
				yaw -= mouseMotion.Relative.X * MouseSensitivity;
				pitch -= mouseMotion.Relative.Y * MouseSensitivity;
				pitch = Mathf.Clamp(pitch, Mathf.DegToRad(MinPitch), Mathf.DegToRad(MaxPitch));
			}
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		Vector3 totalGravity = Vector3.Zero;
		foreach (Planet planet in planets)
		{
			totalGravity += planet.GetForce(GlobalPosition);
		}

		Vector3 upDirection = totalGravity.Length() > 0.001f ? -totalGravity.Normalized() : Vector3.Up;

		Basis finalBasis;
		if (mode == CameraMode.Walk)
		{
			UpdateGravityAlignment(totalGravity, (float)delta);
			velocity += totalGravity * (float)delta;
			finalBasis = ApplyMouseLookToGravityBasis();
		}
		else
		{
			finalBasis = flyBasis;
		}

		Vector3 inputDir = Vector3.Zero;
		if (Input.IsActionPressed("move_forward"))
			inputDir -= finalBasis.Z;
		if (Input.IsActionPressed("move_backward"))
			inputDir += finalBasis.Z;
		if (Input.IsActionPressed("move_left"))
			inputDir -= finalBasis.X;
		if (Input.IsActionPressed("move_right"))
			inputDir += finalBasis.X;
		
		if (mode == CameraMode.Fly)
		{
			if (Input.IsActionPressed("move_up"))
				inputDir += finalBasis.Y;
			if (Input.IsActionPressed("move_down"))
				inputDir -= finalBasis.Y;
		}
		else
		{
			if (Input.IsActionJustPressed("move_up") && IsOnFloor())
				velocity += upDirection * JumpVelocity;
		}

		if (inputDir.Length() > 0)
		{
			float speed = mode == CameraMode.Walk ? WalkSpeed : FlySpeed;
			inputDir = inputDir.Normalized();
			
			if (mode == CameraMode.Walk)
				inputDir = (inputDir - inputDir.Dot(upDirection) * upDirection).Normalized();
			
			velocity += inputDir * speed * (float)delta;
		}

		if (mode == CameraMode.Walk)
		{
			Vector3 horizontalVelocity = velocity - velocity.Dot(upDirection) * upDirection;
			horizontalVelocity *= Mathf.Pow(WalkFriction, (float)delta);
			velocity = horizontalVelocity + velocity.Dot(upDirection) * upDirection;
		}
		else
		{
			velocity *= Mathf.Pow(FlyFriction, (float)delta);
		}

		UpDirection = upDirection;
		Velocity = velocity;
		FloorMaxAngle = Mathf.DegToRad(45.0f);

		MoveAndSlide();

		velocity = Velocity;
		Transform = new Transform3D(finalBasis, Transform.Origin);
	}

	private void UpdateGravityAlignment(Vector3 gravity, float dt)
	{
		if (gravity.Length() < 0.001f)
			return;
			
		Vector3 targetUp = -gravity.Normalized();
		Vector3 currentUp = gravityAlignedBasis.Y.Normalized();
				
		Vector3 axis = currentUp.Cross(targetUp);
		if (axis.Length() < 0.01f)
			return;
			
		axis = axis.Normalized();
		float angle = currentUp.AngleTo(targetUp);
		var rotation = new Quaternion(axis, angle * dt * AlignmentSpeed);
		gravityAlignedBasis = new Basis(rotation) * gravityAlignedBasis;
		gravityAlignedBasis = gravityAlignedBasis.Orthonormalized();
	}

	private Basis ApplyMouseLookToGravityBasis()
	{
		Vector3 up = gravityAlignedBasis.Y.Normalized();
		Vector3 right = gravityAlignedBasis.X.Normalized();
		
		if (up.LengthSquared() < 0.0001f || right.LengthSquared() < 0.0001f)
		{
			GD.PushWarning("Invalid gravity-aligned basis, resetting");
			gravityAlignedBasis = Basis.Identity;
			up = Vector3.Up;
			right = Vector3.Right;
		}
		
		Quaternion yawRotation = new Quaternion(up, yaw);
		Vector3 rotatedRight = (yawRotation * right).Normalized();
		
		if (rotatedRight.LengthSquared() < 0.0001f)
			rotatedRight = right;
		
		Quaternion pitchRotation = new Quaternion(rotatedRight, pitch);
		Quaternion finalRotation = pitchRotation * yawRotation;
		Basis finalBasis = new Basis(finalRotation) * gravityAlignedBasis;
		
		return finalBasis.Orthonormalized();
	}
}