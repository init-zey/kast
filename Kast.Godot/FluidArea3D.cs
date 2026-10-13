using Godot;

namespace Kast.Godot;

/// <summary>
/// 流体区域：一块静止的水面（<see cref="Area3D"/>）。水体本身不参与物理，只负责两件事——
/// 把进出本区域的 <see cref="NObject"/> 登记/注销到它的浮力求解器，
/// 以及给出任意世界坐标处的水面高度（可叠一层随时间流动的噪声起伏）。
/// </summary>
/// <remarks>
/// 水面高度默认取本节点的世界高度；子节点里若放了碰撞体，则按形状细分——
/// <see cref="BoxShape3D"/> 取盒顶面（碰撞盒即水体体积），<see cref="WorldBoundaryShape3D"/> 取该无限平面。
/// 想要"检测体积比水面高出一截"时，把 <see cref="OverrideSurfaceHeight"/> 勾上直接填高度
/// （检测体积若正好止于水面，浮在水面的物体会反复进出，见该属性的说明）。
/// </remarks>
[GlobalClass]
public partial class FluidArea3D : Area3D
{
    /// <summary>流体密度，浮力与它成正比（水取 1）。</summary>
    [Export] public float Density=1f;
    /// <summary>是否让水面随噪声起伏；关掉则水面是严格平面（原插件只导出了这个开关，并未使用）。</summary>
    [Export] public bool SimulateTurbulence=true;

    /// <summary>起伏噪声的采样时间（秒），由物理帧累加。</summary>
    public double Time=0;
    /// <summary>
    /// 水面的世界坐标 Y。默认在 <see cref="_Ready"/> 里由 <see cref="RecalculateSurfaceHeight"/> 按碰撞体推算；
    /// 勾上 <see cref="OverrideSurfaceHeight"/> 则以这里填的值为准。
    /// </summary>
    [Export] public float SurfaceHeight=0f;
    /// <summary>手动指定水面高度，不从碰撞体推算。</summary>
    /// <remarks>
    /// 推荐打开：水的检测体积最好比水面高出一截（浮着的东西总有一半露在水面之上，
    /// 检测体积若正好止于水面，物体的碰撞体只会勉强擦到边界，<see cref="Area3D.BodyEntered"/>
    /// 与 <see cref="Area3D.BodyExited"/> 会随浮沉反复触发，浮力和进水/出水信号都会发抖）。
    /// 检测体积留了余量后，水面高度就用这个值钉住。
    /// 采样点是否真的入水由 <see cref="FluidInteractor3D"/> 按各自高度判断，与本值无关，所以信号仍然精确对应过水面那一刻。
    /// </remarks>
    [Export] public bool OverrideSurfaceHeight=false;

    readonly FastNoiseLite noise=new();

    public override void _Ready()
    {
        RecalculateSurfaceHeight();
        BodyEntered+=OnBodyEntered;
        BodyExited+=OnBodyExited;
    }

    public override void _PhysicsProcess(double delta)
    {
        Time+=delta;
    }

    /// <summary>
    /// 重算水面高度：先取本节点的世界高度，再按子节点碰撞体细分——
    /// <see cref="BoxShape3D"/> 取盒顶面（碰撞盒即水体体积），
    /// <see cref="WorldBoundaryShape3D"/> 取该无限平面（海面常用，平面可以靠 <c>Plane.D</c> 单独下沉）。
    /// 与浮力求解器相比对的是世界坐标，所以这里用全局变换而非局部变换。
    /// 勾了 <see cref="OverrideSurfaceHeight"/> 时什么都不做；
    /// 运行时若移动过水面节点，需要再调一次。
    /// </summary>
    public void RecalculateSurfaceHeight()
    {
        if(OverrideSurfaceHeight)
        {
            return;
        }
        SurfaceHeight=GlobalPosition.Y;
        foreach(uint ownerId in GetShapeOwners())
        {
            if(ShapeOwnerGetOwner(ownerId) is not CollisionShape3D collision)
            {
                continue;
            }
            if(collision.Shape is BoxShape3D box)
            {
                // 盒顶面中心的世界坐标：全局变换已经带上了本节点与碰撞体的平移/旋转
                SurfaceHeight=(collision.GlobalTransform*new Vector3(0f,box.Size.Y*0.5f,0f)).Y;
                break;
            }
            if(collision.Shape is WorldBoundaryShape3D boundary)
            {
                // 平面 normal·x = d，法线取单位长度时 normal*d 就是平面上离原点最近的点；
                // 平面是斜的时候这里只取它的世界高度，本组件的水面本来就是水平的
                var planeWorldPoint=boundary.Plane.Normal*boundary.Plane.D;
                SurfaceHeight=(collision.GlobalTransform*planeWorldPoint).Y;
                break;
            }
        }
    }

    /// <summary>给定世界坐标，返回该处的水面高度（世界坐标 Y）。</summary>
    public float GetHeight(Vector3 position)
    {
        if(!SimulateTurbulence)
        {
            return SurfaceHeight;
        }
        return SurfaceHeight+0.3f*noise.GetNoise3D((float)(Time*2.0+position.X),position.Y,(float)(Time*5.0+position.Z));
    }

    /// <summary>刚体进入水体：交给 <see cref="NObject"/> 记下本区域，此后每物理帧都会被算浮力。</summary>
    void OnBodyEntered(Node3D body)
    {
        if(body is NObject obj)
        {
            obj.FluidAreaEnter(this);
        }
    }

    /// <summary>刚体离开水体：撤销登记（同帧进入多个水体时以最后进入的为准）。</summary>
    void OnBodyExited(Node3D body)
    {
        if(body is NObject obj)
        {
            obj.FluidAreaExit(this);
        }
    }
}
