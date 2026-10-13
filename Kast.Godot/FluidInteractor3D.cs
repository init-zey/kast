using System.Collections.Generic;
using Godot;

namespace Kast.Godot;

/// <summary>
/// 3D 浮力求解器：把刚体上的一串浮力采样点（<see cref="Floater3D"/>）逐个与水面高度比较，
/// 算出浸没部分的浮力大小、浮力作用点，以及每个采样点"刚入水/刚出水"的状态迁移。
/// </summary>
/// <remarks>
/// 由 <see cref="NObject"/> 持有并每物理帧调用 <see cref="Process"/>；
/// <see cref="FluidArea3D"/> 负责把刚体登记进/移出流体区域。
/// 采样点与排水体积在装配阶段由 <see cref="AddCollisionShape"/> 按碰撞体外形估算。
/// </remarks>
public class FluidInteractor3D
{
    /// <summary>一个浮力采样点：刚体坐标系下的位置、半径，以及入水状态的两帧历史。</summary>
    public class Floater3D
    {
        /// <summary>刚体坐标系下的采样点位置。</summary>
        public Vector3 Position;
        /// <summary>采样点半径（米），用来判断"半个球浸没"这类部分入水。</summary>
        public float Radius;
        /// <summary>入水状态位：bit1 表示当前帧浸没；每帧右移一位，于是 bit0 是上一帧的浸没状态。</summary>
        public int State;

        public Floater3D(Vector3 position,float radius=0f)
        {
            Position=position;
            Radius=radius;
        }

        /// <summary>当前帧是否浸没在水中。</summary>
        public bool IsInFluid()=>State>=2;
        /// <summary>本帧刚入水（上一帧尚未浸没）。</summary>
        public bool IsJustEnteredToFluid()=>State==2;
        /// <summary>本帧刚出水（上一帧还浸没着）。</summary>
        public bool IsJustExitedFromFluid()=>State==1;
    }

    /// <summary>全部浮力采样点（刚体坐标系）。</summary>
    public readonly List<Floater3D> Floaters=new();
    /// <summary>按碰撞体外形估算的排水体积（立方米）。</summary>
    public float Volume=0f;
    /// <summary>当前所在的流体区域，不在水中时为 <see langword="null"/>。</summary>
    public FluidArea3D FluidArea=null;
    /// <summary>最近一次 <see cref="Process"/> 求出的浮力作用点：世界坐标系下相对刚体原点的偏移。</summary>
    public Vector3 FloatPosition=Vector3.Zero;
    /// <summary>最近一次 <see cref="Process"/> 求出的浮力（世界方向，牛顿）。</summary>
    public Vector3 FloatForce=Vector3.Zero;

    /// <summary>
    /// 按当前姿态求一次浮力。每物理帧调用一次：先刷新各采样点的入水状态，
    /// 再把"浸没深度 × 排水体积 × 流体密度"折算成竖直向上的力与作用点。
    /// </summary>
    /// <param name="transform">刚体的世界变换，用来把采样点搬到世界坐标去和水面比较。</param>
    /// <param name="mass">刚体质量，浮力按它归一化（沿用原插件的算法）。</param>
    /// <param name="gravityScale">刚体的重力缩放，跟着一起缩放浮力，保证加减重力时浮沉手感一致。</param>
    public void Process(Transform3D transform,float mass=1f,float gravityScale=1f)
    {
        FloatPosition=Vector3.Zero;
        FloatForce=Vector3.Zero;

        if(FluidArea==null||!GodotObject.IsInstanceValid(FluidArea))
        {
            return;
        }

        var sumPositions=Vector3.Zero;
        float sumWeights=0f;
        float maxDiff=0f;
        float minDiff=1000000f;
        foreach(var floater in Floaters)
        {
            var floaterPosition=transform*floater.Position;
            var surfaceHeight=FluidArea.GetHeight(floaterPosition);
            float diffB=floaterPosition.Y-floater.Radius-surfaceHeight;
            float diffU=floaterPosition.Y+floater.Radius-surfaceHeight;

            maxDiff=Mathf.Max(maxDiff,diffU);
            minDiff=Mathf.Min(minDiff,diffB);
            // 状态位右移一位：上一帧的浸没状态退到 bit0，bit1 留给本帧重新判定
            floater.State>>=1;
            if(diffB<0f)
            {
                // 按浸没深度加权，深的一侧对浮力作用点贡献更大
                sumPositions+=floaterPosition*-diffB;
                sumWeights+=-diffB;
                floater.State|=2;
            }
        }

        if(sumWeights>0f)
        {
            float floatVolume=Volume;
            float floatDiff=Mathf.Abs(minDiff-maxDiff);
            if(maxDiff>0f)
            {
                // 还有采样点露在水面上（没全淹）：只按真正浸没的那部分算体积
                floatVolume*=Mathf.Abs(minDiff)/floatDiff;
                floatDiff=Mathf.Abs(minDiff);
            }

            FloatPosition=sumPositions/sumWeights-transform.Origin;
            FloatForce=Vector3.Up*floatDiff*floatVolume/mass*FluidArea.Density*1000f*gravityScale;
        }
    }

    /// <summary>
    /// 用碰撞体外形追加采样点与排水体积：盒体取底面四角，球取球心，圆柱/胶囊取两端圆心。
    /// </summary>
    /// <param name="collision">要采样的碰撞体。</param>
    /// <param name="localTransform">
    /// 采样点写进哪个坐标系（即刚体坐标系）。默认取碰撞体的父级局部变换，前提是碰撞体就挂在刚体名下；
    /// 碰撞体嵌在类型子节点里时，调用方要传入"刚体全局变换的逆 × 碰撞体全局变换"。
    /// </param>
    public void AddCollisionShape(CollisionShape3D collision,Transform3D? localTransform=null)
    {
        var xform=localTransform??collision.Transform;
        var shape=collision.Shape;
        if(shape is BoxShape3D box)
        {
            var extent=box.Size*0.5f;
            AddFloater(xform*new Vector3(-extent.X,0f,-extent.Z));
            AddFloater(xform*new Vector3(-extent.X,0f,extent.Z));
            AddFloater(xform*new Vector3(extent.X,0f,-extent.Z));
            AddFloater(xform*new Vector3(extent.X,0f,extent.Z));
            Volume+=box.Size.X*box.Size.Y*box.Size.Z;
        }
        else if(shape is SphereShape3D sphere)
        {
            AddFloater(xform*Vector3.Zero,sphere.Radius);
            Volume+=4f/3f*Mathf.Pi*Mathf.Pow(sphere.Radius,3f);
        }
        else if(shape is CylinderShape3D cylinder)
        {
            AddFloater(xform*new Vector3(0f,-cylinder.Height*0.5f+cylinder.Radius,0f),cylinder.Radius);
            AddFloater(xform*new Vector3(0f,cylinder.Height*0.5f-cylinder.Radius,0f),cylinder.Radius);
            Volume+=Mathf.Pi*Mathf.Pow(cylinder.Radius,2f)*cylinder.Height;
        }
        else if(shape is CapsuleShape3D capsule)
        {
            // Height 只是中段圆柱的高，两端半球另算
            AddFloater(xform*new Vector3(0f,-capsule.Height*0.5f,0f),capsule.Radius);
            AddFloater(xform*new Vector3(0f,capsule.Height*0.5f,0f),capsule.Radius);
            Volume+=4f/3f*Mathf.Pi*Mathf.Pow(capsule.Radius,3f)+Mathf.Pi*Mathf.Pow(capsule.Radius,2f)*capsule.Height;
        }
    }

    /// <summary>手动追加一个采样点（绕开碰撞体扫描自定义浮力布局时用）。</summary>
    public void AddFloater(Vector3 position,float radius=0f)
    {
        Floaters.Add(new Floater3D(position,radius));
    }

    /// <summary>
    /// 清空采样点与排水体积。重新扫描碰撞体前必须调用：
    /// 原插件只清列表不清体积，换一次外形体积就会翻一倍。
    /// </summary>
    public void ClearFloaters()
    {
        Floaters.Clear();
        Volume=0f;
    }

    /// <summary>由 <see cref="FluidArea3D"/> 在刚体进入水体时调用。</summary>
    public void FluidAreaEnter(FluidArea3D area)
    {
        FluidArea=area;
    }

    /// <summary>由 <see cref="FluidArea3D"/> 在刚体离开水体时调用；离开的不是当前水体则忽略。</summary>
    public void FluidAreaExit(FluidArea3D area)
    {
        if(FluidArea==area)
        {
            FluidArea=null;
        }
    }
}
