using Godot;

namespace planets.common;

public enum CameraMode { Fly, Walk }

public partial class Camera : CharacterBody3D {
	[Export] public float FlySpeed = 200.0f;
	[Export] public float WalkSpeed = 20.0f;
	[Export] public float FlyFriction = 0.02f;
	[Export] public float WalkFriction = 0.03f;

	[Export] public float MouseSensitivity = 0.003f;
	[Export] public float MinPitch = -89.0f;
	[Export] public float MaxPitch = 89.0f;
	[Export] public float JumpVelocity = 5.0f;
	[Export] public float AlignmentSpeed = 2.0f;

	private float _pitch;
	private float _yaw;
	
	private CameraMode _mode = CameraMode.Fly;
	private Vector3 _velocity = Vector3.Zero;
	private Godot.Collections.Array<Planet> _planets = [];
	
	private Basis _gravityAlignedBasis = Basis.Identity;
	private Basis _flyBasis = Basis.Identity;
	
	public override void _Ready() {
		FindAllPlanets();
		
		Input.MouseMode = Input.MouseModeEnum.Captured;
		_flyBasis = Transform.Basis.Orthonormalized();
	}

	private void FindAllPlanets() {
		_planets.Clear();
		
		FindPlanetsRecursive(GetTree().Root);
	}

	private void FindPlanetsRecursive(Node node) {
		if (node is Planet planet && node != this) {
			_planets.Add(planet);
		}

		foreach (var child in node.GetChildren()) {
			FindPlanetsRecursive(child);
		}
	}

	public override void _Input(InputEvent @event) {
		if (@event is InputEventKey { Pressed: true } keyEvent) {
			if (keyEvent.Keycode == Key.Escape) {
				Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured 
					? Input.MouseModeEnum.Visible 
					: Input.MouseModeEnum.Captured;
			}
			else if (keyEvent.Keycode == Key.Tab) {
				if (_mode == CameraMode.Fly) {
					_mode = CameraMode.Walk;
					_gravityAlignedBasis = _flyBasis;
					_pitch = 0.0f;
					_yaw = 0.0f;
				}
				else {
					_mode = CameraMode.Fly;
					_flyBasis = Transform.Basis.Orthonormalized();
				}
			}
		}
		
		if (@event is InputEventMouseMotion mouseMotion && Input.MouseMode == Input.MouseModeEnum.Captured) {
			if (_mode == CameraMode.Fly) {
				var deltaYaw = -mouseMotion.Relative.X * MouseSensitivity;
				var deltaPitch = -mouseMotion.Relative.Y * MouseSensitivity;
				
				var yawRot = new Quaternion(_flyBasis.Y, deltaYaw);
				_flyBasis = new Basis(yawRot) * _flyBasis;
				
				var pitchRot = new Quaternion(_flyBasis.X, deltaPitch);
				_flyBasis = new Basis(pitchRot) * _flyBasis;
				
				_flyBasis = _flyBasis.Orthonormalized();
			}
			else {
				_yaw -= mouseMotion.Relative.X * MouseSensitivity;
				_pitch -= mouseMotion.Relative.Y * MouseSensitivity;
				_pitch = Mathf.Clamp(_pitch, Mathf.DegToRad(MinPitch), Mathf.DegToRad(MaxPitch));
			}
		}
	}

	public override void _PhysicsProcess(double delta) {
		var totalGravity = Vector3.Zero;
		foreach (var planet in _planets) {
			totalGravity += planet.GetForce(GlobalPosition);
		}

		var upDirection = totalGravity.Length() > 0.001f ? -totalGravity.Normalized() : Vector3.Up;

		Basis finalBasis;
		if (_mode == CameraMode.Walk) {
			UpdateGravityAlignment(totalGravity, (float)delta);
			_velocity += totalGravity * (float)delta;
			finalBasis = ApplyMouseLookToGravityBasis();
		}
		else {
			finalBasis = _flyBasis;
		}

		var inputDir = Vector3.Zero;
		if (Input.IsActionPressed("move_forward")) inputDir -= finalBasis.Z;
		if (Input.IsActionPressed("move_backward")) inputDir += finalBasis.Z;
		if (Input.IsActionPressed("move_left")) inputDir -= finalBasis.X;
		if (Input.IsActionPressed("move_right")) inputDir += finalBasis.X;
		
		if (_mode == CameraMode.Fly) {
			if (Input.IsActionPressed("move_up")) inputDir += finalBasis.Y;
			if (Input.IsActionPressed("move_down")) inputDir -= finalBasis.Y;
		}
		else {
			if (Input.IsActionJustPressed("move_up") && IsOnFloor()) 
				_velocity += upDirection * JumpVelocity;
		}

		if (inputDir.Length() > 0) {
			var speed = _mode == CameraMode.Walk ? WalkSpeed : FlySpeed;
			inputDir = inputDir.Normalized();
			
			if (_mode == CameraMode.Walk)
				inputDir = (inputDir - inputDir.Dot(upDirection) * upDirection).Normalized();
			
			_velocity += inputDir * speed * (float) delta;
		}

		if (_mode == CameraMode.Walk) {
			var horizontalVelocity = _velocity - _velocity.Dot(upDirection) * upDirection;
			horizontalVelocity *= Mathf.Pow(WalkFriction, (float) delta);
			
			_velocity = horizontalVelocity + _velocity.Dot(upDirection) * upDirection;
		}
		else {
			_velocity *= Mathf.Pow(FlyFriction, (float) delta);
		}

		UpDirection = upDirection;
		Velocity = _velocity;
		FloorMaxAngle = Mathf.DegToRad(45.0f);

		MoveAndSlide();

		_velocity = Velocity;
		Transform = new Transform3D(finalBasis, Transform.Origin);
	}

	private void UpdateGravityAlignment(Vector3 gravity, float dt) {
		if (gravity.Length() < 0.001f) return;
			
		var targetUp = -gravity.Normalized();
		var currentUp = _gravityAlignedBasis.Y.Normalized();
				
		var axis = currentUp.Cross(targetUp);
		if (axis.Length() < 0.01f) return;
			
		axis = axis.Normalized();
		var angle = currentUp.AngleTo(targetUp);
		var rotation = new Quaternion(axis, angle * dt * AlignmentSpeed);
		_gravityAlignedBasis = new Basis(rotation) * _gravityAlignedBasis;
		_gravityAlignedBasis = _gravityAlignedBasis.Orthonormalized();
	}

	private Basis ApplyMouseLookToGravityBasis() {
		var up = _gravityAlignedBasis.Y.Normalized();
		var right = _gravityAlignedBasis.X.Normalized();
		
		if (up.LengthSquared() < 0.0001f || right.LengthSquared() < 0.0001f) {
			GD.PushWarning("Invalid gravity-aligned basis, resetting");
			_gravityAlignedBasis = Basis.Identity;
			up = Vector3.Up;
			right = Vector3.Right;
		}
		
		var yawRotation = new Quaternion(up, _yaw);
		var rotatedRight = (yawRotation * right).Normalized();
		if (rotatedRight.LengthSquared() < 0.0001f) rotatedRight = right;
		
		var pitchRotation = new Quaternion(rotatedRight, _pitch);
		var finalRotation = pitchRotation * yawRotation;
		var finalBasis = new Basis(finalRotation) * _gravityAlignedBasis;
		return finalBasis.Orthonormalized();
	}
}