using System;
using Godot;

namespace Kast.Godot;

/// <summary>
/// 物体的"类型/外观"节点：挂在 <see cref="NObject"/> 刚体下面，负责网格、轮廓与质量变化的外观响应。
/// </summary>
/// <remarks>
/// 关卡场景与 <c>Types/*.tscn</c> 里放的都是本类的子类，由 <see cref="NLevel"/> 在运行时
/// 实体化成刚体；<see cref="NObject.SetType"/> 换类型时会整体换掉这个子节点。
/// </remarks>
public abstract partial class NObjectType : Node3D
{
    [Export] public string TypeName="object";
        /// <summary>是否按碰撞体外形自动摆放浮力采样点；关掉后只能自己往 <see cref="FluidInteractor"/> 里加。</summary>
    [Export] public bool UseCollisionShapes=true;
    /// <summary>泡在水里时的线性与角阻尼；出水后清零。</summary>
    [Export] public float FluidDamp=4f;
    /// <summary>浮力求解器，采样点在碰撞体就位后由 <see cref="RefreshFloaters"/> 重建。</summary>
    public FluidInteractor3D FluidInteractor=new();
    /// <summary>密度，换算质量时与尺寸一起使用（见 <see cref="NLevel.ScanAndSpawn"/>）。</summary>
    [Export] public float Density=2.0f;
    [Export] public bool AxisLockAngularXZ=false;
    [Export] public bool IsStatic=false;
    /// <summary>宿主刚体，由 <see cref="NObject.SetType"/> 反向注入。</summary>
    public NObject Object;
    public bool IsFloaterInited=false;

    /// <summary>设置轮廓颜色，由相机在选中/瞄准时驱动。</summary>
    public abstract void SetOutlineColor(Color color);

    public virtual void PlayAnimation(string v) {}

    /// <summary>
    /// 本类型用于"显示"咒语的真实词名列表，由 <c>display</c> 动词逐词拼成词形画到界面上。
    /// 子类覆写以展示自己的名字。
    /// </summary>
}

public interface IJumpable
{
    public bool OnFloor();
}