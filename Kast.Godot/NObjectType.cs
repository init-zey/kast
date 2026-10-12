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
    /// <summary>密度，换算质量时与尺寸一起使用（见 <see cref="NLevel.ScanAndSpawn"/>）。</summary>
    [Export] public float Density=2.0f;
    /// <summary>宿主刚体，由 <see cref="NObject.SetType"/> 反向注入。</summary>
    public NObject Object;
    /// <summary>设置轮廓颜色，由相机在选中/瞄准时驱动。</summary>
    public abstract void SetOutlineColor(Color color);
    
    /// <summary>质量变化的回调，供子类调整外观；默认什么都不做。</summary>
    public virtual void OnSetMass(float prevMass,float newMass) {}
    /// <summary>
    /// 本类型用于"显示"咒语的真实词名列表，由 <c>display</c> 动词逐词拼成词形画到界面上。
    /// 子类覆写以展示自己的名字。
    /// </summary>
    public virtual string[] GetDisplayRealName()=>["thing"];
}