using Godot;

namespace Kast.Godot;

public abstract partial class NObjectType3D : NObjectType
{
    ShaderMaterial Material=new ShaderMaterial();
    Color outlineColor=Colors.Transparent;
    public sealed override void _Ready()
    {
        Material.Shader=GD.Load<Shader>("res://Shaders/Outline.gdshader");
        SetMaterial(this);
        SetOutlineColor(outlineColor);
        NodeReady();
    }

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

    public virtual void NodeReady()
    {
        
    }
    public override void SetOutlineColor(Color color)
    {
        outlineColor=color;
        Material.SetShaderParameter("outline_color",color);
    }
}