using Godot;

[GlobalClass]
public partial class NJointMarker : Marker3D
{
    [Export] public JointType JointType;
}

public enum JointType
{
    Pin,
    SixD,
}