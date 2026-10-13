using Godot;
using Kast.Godot;

/// <summary>临时验证：照 1.tscn 的水面配置 + NCube，先看浮不浮，再在漂浮时换一次类型。用完即删。</summary>
public partial class NFluidCheck : Node3D
{
    NObject obj;
    FluidArea3D water;
    int frame=0;

    public override void _Ready()
    {
        // 与 1.tscn 一致：Area3D 在原点，碰撞盒 100×20×100 偏移 (0,-10,0)，场景里写了 SurfaceHeight=10
        water=new FluidArea3D();
        water.SurfaceHeight=10f;
        water.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Size=new Vector3(100f,20f,100f)},Position=new Vector3(0f,-10f,0f)});
        AddChild(water);
        GD.Print($"[C] 场景写 SurfaceHeight=10 -> 实际水面 SurfaceHeight={water.SurfaceHeight} Override={water.OverrideSurfaceHeight}");

        var type=Helper.LoadScene<NObjectType>("Types/NCube");
        obj=new NObject{Position=new Vector3(0f,-5f,0f)};
        AddChild(obj);
        obj.SetType(type);
        GD.Print($"[C] SetType 后 floaters={type.FluidInteractor.Floaters.Count} volume={type.FluidInteractor.Volume} mass={obj.Mass}");
    }

    public override void _PhysicsProcess(double delta)
    {
        frame++;
        if(frame==150)
        {
            // 漂浮中换类型（相当于以后 "be <类型>"）：新类型有一整套新的求解器
            var next=Helper.LoadScene<NObjectType>("Types/NCube");
            obj.SetType(next);
            GD.Print($"[C] 换类型后 floaters={next.FluidInteractor.Floaters.Count} FluidArea={(next.FluidInteractor.FluidArea!=null)}");
        }
        if(frame%30==0)
        {
            GD.Print($"[C] f={frame} y={obj.GlobalPosition.Y:F3} vy={obj.LinearVelocity.Y:F3} inArea={obj.Type.FluidInteractor.FluidArea!=null} floaters={obj.Type.FluidInteractor.Floaters.Count} F={obj.Type.FluidInteractor.FloatForce.Y:F2} damp={obj.LinearDamp}");
        }
        if(frame>=330)
        {
            GetTree().Quit();
        }
    }
}
