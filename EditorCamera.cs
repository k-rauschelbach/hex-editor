using Godot;
using System;

// Orbit style camera

public partial class EditorCamera : Node3D
{
    // Tuning
    
    // Mouse Rotational Speed
    private const float OrbitSensitivity = 0.005f;
    // Mouse Pan Speed
    private const float PanSensitivity = 0.02f;
    // Zoom Speed
    private const float ZoomStep = 1.0f;
    // Zoom limits
    private const float MinDistance = 3.0f;
    private const float MaxDistance = 80.0f;
    // Orbit pitch limits -- in radians
    private const float MinPitch = 0.1f;
    private const float MaxPitch = 1.4f;
    
    // ------ State ------- //
    
    // Focus Point
    private Vector3 _focusPoint = Vector3.Zero;
    // Yaw
    private float _yaw = 0f;
    // Pitch
    private float _pitch = 0.8f;
    // Zoom Distance
    private float _distance = 20.0f;
    
    // Track mouse buttons held for orbit and pan
    private bool _isOrbiting = false;
    private bool _isPanning = false;
    
    // Camera3D Node
    private Camera3D _camera;
    
    public Camera3D Camera => _camera;

    public override void _Ready()
    {
        // Get the Camera3D node
        _camera = GetNode<Camera3D>("Camera3D");
        
        // Initial Positioning
        UpdateCameraTransform();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseBtn)
        {
            switch (mouseBtn.ButtonIndex)
            {
                case MouseButton.Right:
                    // Orbit
                    _isOrbiting = mouseBtn.Pressed;
                    break;
                
                case MouseButton.Middle:
                    // Pan
                    _isPanning = mouseBtn.Pressed;
                    break;
                
                case MouseButton.WheelUp:
                    // Zoom in
                    _distance = Mathf.Max(MinDistance, _distance - ZoomStep);
                    UpdateCameraTransform();
                    break;
                
                case MouseButton.WheelDown:
                    // Zoom out
                    _distance = Mathf.Min(MaxDistance, _distance + ZoomStep);
                    UpdateCameraTransform();
                    break;
            }
        }

        if (@event is InputEventMouseMotion mouseMotion)
        {
            if (_isOrbiting)
            {
                // Horizontal mouse movement = yaw
                // Vertical mouse movement = pitch
                _yaw -= mouseMotion.Relative.X * OrbitSensitivity;
                _pitch += mouseMotion.Relative.Y * OrbitSensitivity;
                // Clamp pitch to prevent flipping
                _pitch = Mathf.Clamp(_pitch, MinPitch, MaxPitch);
                UpdateCameraTransform();
            }
            else if (_isPanning)
            {
                // Move focus point along ground
                Vector3 right = _camera.GlobalTransform.Basis.X;
                Vector3 forward = _camera.GlobalTransform.Basis.Z;

                // Zero Y movement to keep focus point on ground
                right.Y = 0;
                right = right.Normalized();
                forward.Y = 0;
                forward = forward.Normalized();
                
                // Scale by pan speed
                float scaledPanSpeed = PanSensitivity * (_distance / 20.0f);
                
                // Apply pan movement
                _focusPoint -= right * mouseMotion.Relative.X * scaledPanSpeed;
                _focusPoint -= forward * mouseMotion.Relative.Y * scaledPanSpeed;
                UpdateCameraTransform();
            }
        }
    }
    
    // Recalculate camera position
    private void UpdateCameraTransform()
    {
        // convert global xyz coordinates to cartesian coordinates from focus point
        float x = _distance * Mathf.Cos(_pitch) * Mathf.Sin(_yaw);
        float y = _distance * Mathf.Sin(_pitch);
        float z = _distance * Mathf.Cos(_pitch) * Mathf.Cos(_yaw);

        Vector3 cameraPos = _focusPoint + new Vector3(x, y, z);
        
        // Position pivot node then have camera look at focus point
        _camera.GlobalPosition = cameraPos;
        _camera.LookAt(_focusPoint, Vector3.Up);
    }

    public void SetFocusPoint(Vector3 focusPoint)
    {
        _focusPoint = focusPoint;
        UpdateCameraTransform();
    }

}
