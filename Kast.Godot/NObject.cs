using System.Collections.Generic;
using Godot;

namespace Kast.Godot;

public partial class NObject : RigidBody3D
{
    [Export] public NObjectType Type;
    float TurnSpeed=8f;
    [Export] GpuParticlesAttractorSphere3D ParticleAttractor;
    private double attractorEnableTimer=0;
    /// <summary>当前类型搬到本刚体名下的碰撞体，换类型时按这张表回收。</summary>
    readonly List<CollisionShape3D> typeCollisionShapes=new();
    /// <summary>本刚体当前所在的水体（由 <see cref="FluidArea3D"/> 登记）。求解器归类型所有，换类型时要转交给新类型。</summary>
    public FluidArea3D CurrentFluidArea;
    /// <summary>有采样点刚入水时触发，参数是该采样点在刚体坐标系下的位置。</summary>
    [Signal] public delegate void EnteredToFluidEventHandler(Vector3 position);
    /// <summary>有采样点刚出水时触发，参数同上。</summary>
    [Signal] public delegate void ExitedToFluidEventHandler(Vector3 position);
    public void UpdateMass()
    {
        Mass=Type.Scale.Dot(Type.Scale)*Type.Density;
    }
    public void SetType(NObjectType type)
    {
        if(type==Type) return;
        if(Type!=null)
        {
            ClearTypeCollisionShapes();
            RemoveChild(Type);
            Type.QueueFree();
        }
        Type=type;
        Type.Object=this;
        var prevParent=Type.GetParentOrNull<Node>();
        if(prevParent!=null)
        {
            prevParent.RemoveChild(Type);
        }
        Type.Position=new();
        Type.Rotation=new();
        AddChild(Type);
        foreach(var child in Type.GetChildren())
        {
            if(child is CollisionShape3D collision)
            {
                collision.Reparent(this);
                typeCollisionShapes.Add(collision);
            }
        }
        InitFloaters();
        // 泡在水里时换类型：body_entered 不会再响一次，必须把当前水体补给新类型的求解器，
        // 否则新求解器的 FluidArea 一直是 null，浮力从此失效
        if(CurrentFluidArea!=null)
        {
            Type.FluidInteractor.FluidAreaEnter(CurrentFluidArea);
        }
        UpdateMass();
    }

    /// <summary>
    /// 立刻摘掉上一批从类型节点搬过来的碰撞体。
    /// 必须先 <see cref="Node.RemoveChild"/> 再 <see cref="Node.QueueFree"/>：QueueFree 要等到帧末才生效，
    /// 只靠它的话同一帧里的 <see cref="InitFloaters"/> 还会把这些旧碰撞体扫成采样点。
    /// </summary>
    void ClearTypeCollisionShapes()
    {
        foreach(var collision in typeCollisionShapes)
        {
            if(!IsInstanceValid(collision))
            {
                continue;
            }
            if(collision.GetParent()==this)
            {
                RemoveChild(collision);
            }
            collision.QueueFree();
        }
        typeCollisionShapes.Clear();
    }

    /// <summary>
    /// 按挂在刚体下的碰撞体外形重建浮力采样点与排水体积。
    /// 换类型（<see cref="SetType"/>）会换掉整批碰撞体，所以每次换完都要重建一次。
    /// </summary>
    public void InitFloaters()
    {
        // 同一个类型实例只装配一次：重复调用会把采样点与排水体积重复累加
        if(Type.IsFloaterInited) return;
        Type.IsFloaterInited=true;
        if(!Type.UseCollisionShapes)
        {
            return;
        }
        var toBodyLocal=GlobalTransform.AffineInverse();
        foreach(uint ownerId in GetShapeOwners())
        {
            // 只认还在场景树里的碰撞体：摘掉的碰撞体会在 shape owner 里留个空壳
            if(ShapeOwnerGetOwner(ownerId) is CollisionShape3D collision&&collision.IsInsideTree())
            {
                // 采样点要落在刚体坐标系里：碰撞体嵌在类型子节点下时，父级局部变换并不等于刚体局部变换
                Type.FluidInteractor.AddCollisionShape(collision,toBodyLocal*collision.GlobalTransform);
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
        if(Type==null) return $"{Name}<null>";
        return $"{Name}<{Type.Name}>";
    }
    public override void _Ready()
    {
        ParticleAttractor=new();
        ParticleAttractor.Radius=100;
        ParticleAttractor.Strength=0;
        AddChild(ParticleAttractor);
    }

    public void EnableAttractor()
    {
        ParticleAttractor.Strength=10;
        attractorEnableTimer=0.5;
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

        ProcessFluid(delta);
    }

    /// <summary>
    /// 浮力：先让求解器按当前姿态算一次，再播报采样点的入水/出水，最后施力并调阻尼。
    /// 不在水中时把阻尼清回 0——这是原插件的做法，会覆盖别处设的阻尼值。
    /// </summary>
    private void ProcessFluid(double delta)
    {
        Type.FluidInteractor.Process(GlobalTransform,Mass,GravityScale);
        foreach(var floater in Type.FluidInteractor.Floaters)
        {
            if(floater.IsJustEnteredToFluid())
            {
                EmitSignal(SignalName.EnteredToFluid,floater.Position);
            }
            if(floater.IsJustExitedFromFluid())
            {
                EmitSignal(SignalName.ExitedToFluid,floater.Position);
            }
        }

        if(!Type.FluidInteractor.FloatForce.IsZeroApprox())
        {
            // Bouyancy
            // 沿用原插件：浮力再乘一次 delta。Godot 的 ApplyForce 本身已按帧长积分，这里等于多乘一次
            ApplyForce(Type.FluidInteractor.FloatForce,Type.FluidInteractor.FloatPosition);
            // Damping
            LinearDamp=Type.FluidDamp;
            AngularDamp=Type.FluidDamp;
        }
        else
        {
            LinearDamp=0f;
            AngularDamp=0f;
        }
    }

    /// <summary>由 <see cref="FluidArea3D"/> 在本刚体进入水体时调用。</summary>
    public void FluidAreaEnter(FluidArea3D area)
    {
        CurrentFluidArea=area;
        Type.FluidInteractor.FluidAreaEnter(area);
    }

    /// <summary>由 <see cref="FluidArea3D"/> 在本刚体离开水体时调用。</summary>
    public void FluidAreaExit(FluidArea3D area)
    {
        if(CurrentFluidArea==area)
        {
            CurrentFluidArea=null;
        }
        Type.FluidInteractor.FluidAreaExit(area);
    }

}
