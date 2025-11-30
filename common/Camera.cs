using Godot;
using System;

public enum CameraMode
{
	Fly,
	Walk
}

public partial class Camera : CharacterBody3D
{
	[Export] public float FlySpeed = 40.0f;
	[Export] public float WalkSpeed = 10.0f;
	[Export] public float MouseSensitivity = 0.003f;
	[Export] public float MinPitch = -89.0f;
	[Export] public float MaxPitch = 89.0f;
	[Export] public float JumpVelocity = 5.0f;
	[Export] public float AlignmentSpeed = 5.0f;
	[Export] public float MinGravityForAlignment = 0.1f;

	private Camera3D _camera;
	
	// Mouse look angles - meaning depends on mode
	private float _pitch = 0.0f;
	private float _yaw = 0.0f;
	
	private CameraMode _mode = CameraMode.Fly;
	private Vector3 _velocity = Vector3.Zero;
	private Godot.Collections.Array<Planet> _planets = new Godot.Collections.Array<Planet>();
	
	// For walk mode: cached gravity-aligned basis
	private Basis _gravityAlignedBasis = Basis.Identity;
	
	public override void _Ready()
	{
		_camera = GetNode<Camera3D>("Camera3D");
		FindAllPlanets();
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	private void FindAllPlanets()
	{
		_planets.Clear();
		FindPlanetsRecursive(GetTree().Root);
		GD.Print($"Found {_planets.Count} planet(s) in the scene");
	}

	private void FindPlanetsRecursive(Node node)
	{
		if (node is Planet planet && node != this)
		{
			_planets.Add(planet);
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
				_mode = (_mode == CameraMode.Fly) ? CameraMode.Walk : CameraMode.Fly;
				GD.Print($"Camera mode: {_mode}");
				
				// Reset angles when switching modes
				if (_mode == CameraMode.Walk)
				{
					// In walk mode, start with current orientation
					_gravityAlignedBasis = Transform.Basis;
					_pitch = 0;
					_yaw = 0;
				}
				else
				{
					// In fly mode, extract current pitch/yaw
					Vector3 euler = Transform.Basis.GetEuler();
					_pitch = euler.X;
					_yaw = euler.Y;
				}
			}
		}
		
		if (@event is InputEventMouseMotion mouseMotion && Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			_yaw -= mouseMotion.Relative.X * MouseSensitivity;
			_pitch -= mouseMotion.Relative.Y * MouseSensitivity;
			
			_pitch = Mathf.Clamp(_pitch, Mathf.DegToRad(MinPitch), Mathf.DegToRad(MaxPitch));
			
			// DON'T set rotation here - let _PhysicsProcess handle it
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_mode == CameraMode.Fly)
		{
			ProcessFlyMode(delta);
		}
		else
		{
			ProcessWalkMode(delta);
		}
	}

	private void ProcessFlyMode(double delta)
	{
		// 1. Update orientation (simple Euler in fly mode)
		Transform = new Transform3D(
			Basis.Identity.Rotated(Vector3.Up, _yaw).Rotated(Vector3.Right, _pitch),
			Transform.Origin
		);

		// 2. Calculate velocity based on orientation
		Vector3 velocity = Vector3.Zero;

		if (Input.IsActionPressed("move_forward"))
			velocity -= Transform.Basis.Z;
		if (Input.IsActionPressed("move_backward"))
			velocity += Transform.Basis.Z;
		if (Input.IsActionPressed("move_left"))
			velocity -= Transform.Basis.X;
		if (Input.IsActionPressed("move_right"))
			velocity += Transform.Basis.X;
		if (Input.IsActionPressed("move_up"))
			velocity += Transform.Basis.Y;
		if (Input.IsActionPressed("move_down"))
			velocity -= Transform.Basis.Y;

		if (velocity.Length() > 0)
		{
			velocity = velocity.Normalized();
		}

		Velocity = velocity * FlySpeed;
		MoveAndSlide();
	}

	private void ProcessWalkMode(double delta)
	{
		if (_planets.Count == 0)
		{
			GD.PrintErr("No planets found for Walk mode!");
			ProcessFlyMode(delta);
			return;
		}

		// === STEP 1: Calculate Gravity ===
		Vector3 totalGravity = Vector3.Zero;
		foreach (Planet planet in _planets)
		{
			totalGravity += planet.GetForce(GlobalPosition);
		}

		float gravityMagnitude = totalGravity.Length();
		Vector3 upDirection = gravityMagnitude > 0.001f ? -totalGravity.Normalized() : Vector3.Up;

		// === STEP 2: Align to Gravity ===
		UpdateGravityAlignment(totalGravity, (float)delta);

		// === STEP 3: Apply Mouse Look (in local space relative to gravity) ===
		Basis finalBasis = ApplyMouseLookToGravityBasis();

		// === STEP 4: Handle Movement ===
		// Apply gravity to velocity
		_velocity += totalGravity * (float)delta;

		// Get input direction relative to camera's FINAL orientation
		Vector3 inputDir = Vector3.Zero;
		if (Input.IsActionPressed("move_forward"))
			inputDir -= finalBasis.Z;
		if (Input.IsActionPressed("move_backward"))
			inputDir += finalBasis.Z;
		if (Input.IsActionPressed("move_left"))
			inputDir -= finalBasis.X;
		if (Input.IsActionPressed("move_right"))
			inputDir += finalBasis.X;

		// Project input direction onto the plane perpendicular to gravity
		if (inputDir.Length() > 0)
		{
			inputDir = inputDir.Normalized();
			inputDir = (inputDir - inputDir.Dot(upDirection) * upDirection).Normalized();
			_velocity += inputDir * WalkSpeed * (float)delta * 1.0f;
		}

		// Apply friction on the horizontal plane
		Vector3 horizontalVelocity = _velocity - _velocity.Dot(upDirection) * upDirection;
		horizontalVelocity *= Mathf.Pow(0.03f, (float)delta); // Frame-rate independent
		_velocity = horizontalVelocity + _velocity.Dot(upDirection) * upDirection;

		// Jump
		if (Input.IsActionJustPressed("move_up") && IsOnFloor())
		{
			_velocity += upDirection * JumpVelocity;
		}

		// === STEP 5: Apply Physics ===
		UpDirection = upDirection;
		Velocity = _velocity;
		// FloorStopOnSlope = true;
		FloorMaxAngle = Mathf.DegToRad(45.0f);

		MoveAndSlide();

		_velocity = Velocity;

		// === STEP 6: Set Final Transform ===
		Transform = new Transform3D(finalBasis, Transform.Origin);
	}

	private void UpdateGravityAlignment(Vector3 gravity, float dt)
	{
		if(gravity.Length() < 0.01f)
			return;
		gravity = -gravity.Normalized();
		Vector3 currentUp = _gravityAlignedBasis.Y.Normalized();
				
		Vector3 axis = currentUp.Cross(gravity);
		if (axis.Length() < 0.01f)
			return;
		axis = axis.Normalized();
		float angle = currentUp.AngleTo(gravity);
		var rotation = new Quaternion(axis, angle * dt * 2.0f);
		_gravityAlignedBasis = new Basis(rotation) * _gravityAlignedBasis;
		_gravityAlignedBasis = _gravityAlignedBasis.Orthonormalized();
	}

	private Basis ApplyMouseLookToGravityBasis()
	{
		// Extract axes from gravity-aligned basis
		Vector3 up = _gravityAlignedBasis.Y.Normalized();
		Vector3 right = _gravityAlignedBasis.X.Normalized();
		Vector3 forward = _gravityAlignedBasis.Z.Normalized();
		
		// Safety check: ensure basis is valid
		if (up.LengthSquared() < 0.0001f || right.LengthSquared() < 0.0001f)
		{
			GD.PushWarning("Invalid gravity-aligned basis, resetting");
			_gravityAlignedBasis = Basis.Identity;
			up = Vector3.Up;
			right = Vector3.Right;
			forward = Vector3.Forward;
		}
		
		// Apply yaw around the up axis
		Quaternion yawRotation = new Quaternion(up, _yaw);
		
		// After yaw, calculate the new right axis for pitch
		Vector3 rotatedRight = (yawRotation * right).Normalized();
		
		// Safety check: ensure rotatedRight is normalized
		if (rotatedRight.LengthSquared() < 0.0001f)
		{
			rotatedRight = right; // Fallback to original right
		}
		
		// Apply pitch around the rotated right axis
		Quaternion pitchRotation = new Quaternion(rotatedRight, _pitch);
		
		// Combine: pitch * yaw * gravityAligned
		Quaternion finalRotation = pitchRotation * yawRotation;
		Basis finalBasis = new Basis(finalRotation) * _gravityAlignedBasis;
		
		return finalBasis.Orthonormalized();
	}
}
