using System.Collections.Generic;
using Godot;

namespace Kast.Godot;

[GlobalClass]
public partial class ClientWorld : Node3D
{
    public HashSet<NObject> Objects=new();
    public NObject Player;
    public PlayerCamera Camera;
    public NObject Spawn(string typeid,Transform3D transform,float mass)
    {
        var obj=new NObject();
        Objects.Add(obj);
        AddChild(obj);
        obj.Transform=transform;
        obj.SetType(typeid);
        obj.SetMass(mass);
        return obj;
    }
    public override void _Ready()
    {
        Camera = Helper.LoadScene<PlayerCamera>("PlayerCamera");
        AddChild(Camera);
        Player = Spawn("Caster", Transform3D.Identity, 1);
        Camera.Player = Player;
        ((NCaster)Player.Type).camera = Camera;
        NGame.Instance.ActiveCamera = Camera;
        Spawn("Cube", At("Obstacle1"), 10);
        Spawn("Cube", At("Obstacle2"), 10);
        Spawn("Cube", At("Obstacle3"), 10);
    }

    private Transform3D At(string label)
    {
        return GetNode<Node3D>($"%{label}").GlobalTransform;
    }
}