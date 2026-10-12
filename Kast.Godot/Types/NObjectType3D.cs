using Godot;

namespace Kast.Godot;

/// <summary>
/// 3D 物体的类型基类：在 <see cref="NObjectType"/> 之上补一套通用外观——
/// 给子树里所有 <see cref="MeshInstance3D"/> 挂上轮廓着色器，并实现"质量变大即整体变大"。
/// </summary>
/// <remarks>
/// 子类不要覆写 <see cref="_Ready"/>（它是 <see langword="sealed"/> 的，负责外观装配），
/// 需要初始化时改用 <see cref="NodeReady"/>。
/// </remarks>
public abstract partial class NObjectType3D : NObjectType
{
    ShaderMaterial Material=new ShaderMaterial();
    Color outlineColor=Colors.Transparent;
    /// <summary>装配轮廓材质并回调 <see cref="NodeReady"/>；密封以免子类漏掉装配。</summary>
    public sealed override void _Ready()
    {
        Material.Shader=GD.Load<Shader>("res://Shaders/Outline.gdshader");
        SetMaterial(this);
        SetOutlineColor(outlineColor);
        NodeReady();
    }

    /// <summary>递归给子树里每个 mesh 挂上作为 overlay 的轮廓材质。</summary>
    private void SetMaterial(Node node)
    {
        if(node is MeshInstance3D meshInstance)
        {
            meshInstance.MaterialOverlay=Material;
        }
        foreach(var child in node.GetChildren())
        {
            SetMaterial(child);
        }
    }

    /// <summary>子类的初始化回调，替代被密封的 <see cref="_Ready"/>。</summary>
    public virtual void NodeReady()
    {
        
    }
    /// <summary>记录颜色并写进着色器，供未瞄准/瞄准/选中几档状态切换。</summary>
    public override void SetOutlineColor(Color color)
    {
        outlineColor=color;
        Material.SetShaderParameter("outline_color",color);
    }
    /// <summary>质量变化时按比例缩放整体尺寸，让"更重 = 更大"在视觉上成立。</summary>
    public override void OnSetMass(float prevMass,float newMass)
    {
        Scale*=newMass/prevMass;
    }
}