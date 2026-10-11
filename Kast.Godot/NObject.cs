using Godot;

namespace Kast.Godot;

public partial class NObject : RigidBody3D
{
    public string TypeID;
    public NObjectType Type;
    float TurnSpeed=8f;
    [Signal] public delegate void MassChangedEventHandler(int mass);
    [Export] GpuParticlesAttractorSphere3D ParticleAttractor;
    private double attractorEnableTimer=0;


    public new void SetMass(float mass)
    {
        Mass=mass;
        EmitSignal(SignalName.MassChanged,mass);
    }
    public void SetType(string typeid)
    {
        if(typeid==TypeID) return;
        TypeID=typeid;
        if(Type!=null)
        {
            Type.QueueFree();
        }
        Type=Helper.LoadScene<NObjectType>($"Types/{typeid}");
        Type.Object=this;
        AddChild(Type);
        foreach(var child in Type.GetChildren())
        {
            if(child is CollisionShape3D)
            {
                child.Reparent(this);
            }
        }
    }

    /// <summary>本物体"看"向的世界坐标点。默认沿自身朝向打一条射线，取命中点；相机正操纵本对象时改用鼠标射线。</summary>
    public Vector3 GetAimPoint()
    {
        var camera=NGame.CameraOf(this);
        if(camera!=null)
        {
            return camera.AimPoint;
        }
        var from=GlobalPosition; // 自身刚体已排除，可以从原点起射
        var to=from-GlobalBasis.Z*100f;
        var exclude=new global::Godot.Collections.Array<Rid>{GetRid()};
        var query=PhysicsRayQueryParameters3D.Create(from,to,CollisionMask,exclude);
        var hit=GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count>0?hit["position"].AsVector3():to;
    }

    /// <summary>用角冲量转向指定位置，只绕 Y 轴。每物理帧调用一次。</summary>
    public void TurnTo(Vector3 position)
    {
        var to=position-GlobalPosition;
        to.Y=0f;
        if(to.LengthSquared()<0.000001f)
        {
            return;
        }
        var targetYaw=Mathf.Atan2(to.X,to.Z);
        var error=Mathf.Wrap(targetYaw-Rotation.Y,-Mathf.Pi,Mathf.Pi);
        var desired=Mathf.Clamp(error*TurnSpeed,-TurnSpeed,TurnSpeed);
        ApplyTorqueImpulse(new Vector3(0f,(desired-AngularVelocity.Y)*Mathf.Max(Inertia.Y,0.01f)*10,0f));
    }
    public override string ToString()
    {
        return $"{Name}<{Type.Name}>";
    }
    public override void _Ready()
    {
        ParticleAttractor=new();
        ParticleAttractor.Radius=100;
        AddChild(ParticleAttractor);
    }

    public void EnableAttractor()
    {
        ParticleAttractor.Strength=32;
        attractorEnableTimer=0.1;
    }

    public override void _PhysicsProcess(double delta)
    {
        if(attractorEnableTimer>0)
        {
            attractorEnableTimer-=delta;
            if(attractorEnableTimer<=0)
            {
                ParticleAttractor.Strength=0;
            }
        }
    }

}
