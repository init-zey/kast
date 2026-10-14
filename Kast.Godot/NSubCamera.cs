using Godot;
using Kast.Godot;

public partial class NSubCamera : Camera3D
{
    public override void _PhysicsProcess(double delta)
    {
        NGame.Instance.ActiveCamera.CameraUpdate(delta);
        GlobalTransform=NGame.Instance.ActiveCamera.Camera.GlobalTransform;
    }

}
