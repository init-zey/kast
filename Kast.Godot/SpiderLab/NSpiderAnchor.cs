using Godot;
using System;

[Tool]
public partial class NSpiderAnchor : Node3D
{
    [Export] int SnapDistance=6;
    [Export] bool IsForwardFoot=false;
    [Export] Node3D IKTarget;
    [Export] NSpiderAnchor PairFoot;
    float TargetDistance=0;
    float AutoSnapTimer=0;
    public override void _PhysicsProcess(double delta)
    {
        if(AutoSnapTimer>0)
        {
            AutoSnapTimer-=(float)delta;
        }
        var targetXZ=new Vector2(IKTarget.GlobalPosition.X,IKTarget.GlobalPosition.Z);
        var XZ=new Vector2(GlobalPosition.X,GlobalPosition.Z);
        TargetDistance=(targetXZ-XZ).LengthSquared();
        if(AutoSnapTimer<=0||TargetDistance>SnapDistance*SnapDistance*((Mathf.Abs(PairFoot.TargetDistance-TargetDistance)<1&&IsForwardFoot)?(0.2f*0.2f):1f))
        {
            AutoSnapTimer=1.0f;
            IKTarget.GlobalPosition=GlobalPosition;
        }
    }

}
