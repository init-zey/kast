using Godot;
using System;

public partial class NSpiderSkeleton : Skeleton3D
{
    NLegRayCast[] RayCasts;
    public override void _Ready()
    {
        RayCasts=new NLegRayCast[6];
        int i=0;
        foreach(var child in GetChildren())
        {
            if(child is NLegRayCast rayCast)
            {
                RayCasts[i++]=rayCast;
            }
        }
    }

}
