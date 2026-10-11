using Godot;
using Kast.Godot;
using System;

public partial class NMassLabel : Label3D
{
    public override void _Ready()
    {
        GetParent<NObjectType>().Object.MassChanged += (mass) =>
        {
            Text=$"{mass}kg";
        };
    }

}
