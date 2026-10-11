using System.Collections.Generic;
using Godot;

namespace Kast.Godot;

public partial class NLevel : Node3D
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
    public override void _EnterTree()
    {
        ScanAndSpawn(this);
    }
    public override void _Ready()
    {
        Camera = Helper.LoadScene<PlayerCamera>("PlayerCamera");
        AddChild(Camera);
        Player = Spawn("NCaster", Transform3D.Identity, 1);
        Camera.Player = Player;
        NGame.Instance.ActiveCamera = Camera;
        NGame.Instance.SpellSlots.SetCaster((NCaster)Player.Type);
    }

    private void ScanAndSpawn(Node root)
    {
        foreach(var child in root.GetChildren())
        {
            ScanAndSpawn(child);
            if(child is NObjectType objType)
            {
                var aabbSize=objType.Scale;
                Spawn(objType.GetType().Name,objType.Transform,objType.Density*aabbSize.Dot(aabbSize));
                objType.QueueFree();
            }
        }
    }
}