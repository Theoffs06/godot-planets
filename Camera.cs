using Godot;
using System;

public partial class Camera : CharacterBody3D
{
	[Export] public float MoveSpeed = 10.0f;
	[Export] public float MouseSensitivity = 0.003f;
	[Export] public float MinPitch = -89.0f;
	[Export] public float MaxPitch = 89.0f;
	
	private Camera3D _camera;
	private float _pitch = 0.0f;
	private float _yaw = 0.0f;
	
	public override void _Ready()
	{
		_camera = GetNode<Camera3D>("Camera3D");
		
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.Escape)
		{
			if (Input.MouseMode == Input.MouseModeEnum.Captured)
			{
				Input.MouseMode = Input.MouseModeEnum.Visible;
			}
			else
			{
				Input.MouseMode = Input.MouseModeEnum.Captured;
			}
		}
		
		if (@event is InputEventMouseMotion mouseMotion && Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			_yaw -= mouseMotion.Relative.X * MouseSensitivity;
			_pitch -= mouseMotion.Relative.Y * MouseSensitivity;
			
			_pitch = Mathf.Clamp(_pitch, Mathf.DegToRad(MinPitch), Mathf.DegToRad(MaxPitch));
			
			Rotation = new Vector3(_pitch, _yaw, 0);
		}
	}

	public override void _PhysicsProcess(double delta)
	{
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
		
		// Use CharacterBody3D's velocity property and MoveAndSlide for collision handling
		Velocity = velocity * MoveSpeed;
		MoveAndSlide();
	}
}
