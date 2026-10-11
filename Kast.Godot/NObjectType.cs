using Godot;

namespace Kast.Godot;

public abstract partial class NObjectType : Node
{
    public NObject Object;
    public abstract void SetOutlineColor(Color color);
}