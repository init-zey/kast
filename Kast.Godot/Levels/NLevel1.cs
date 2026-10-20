using Godot;
using Kast.Godot;

public partial class NLevel1 : NLevel
{
    [Export] public Node3D Water;
    public override void _PhysicsProcess(double delta)
    {
        if(NGame.Instance.ActiveCamera.Player?.Type is NCaster caster && caster.Mouth.GlobalPosition.Y<Water.Position.Y)
        {
            NGame.Instance.PlayerDie();
        }
        if(NGame.Instance.ActiveCamera.Player.Position.DistanceSquaredTo(new())>100*100)
        {
            NGame.Instance.DemoEnd();
        }
    }
}
