using Godot;

namespace Kast.Godot;

public abstract partial class NObjectType : Node3D
{
    [Export] public float Density=2.0f;
    public NObject Object;
    public abstract void SetOutlineColor(Color color);
    
    public virtual void OnSetMass(float prevMass,float newMass) {}
}