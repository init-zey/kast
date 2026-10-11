using System;
using Godot;
using Kast.Core;

namespace Kast.Godot;

public static class ContextHelper
{
    public static Vector3 GetPosition(this object o)
    {
        var vec=new Vector3();
        if(o is Vector3 ovec)
        {
            vec+=ovec;
        }
        else if (o is NObject oobj)
        {
            vec+=oobj.GlobalPosition;
        }
        else
        {
            GD.Print($"don't know how to get position from {o}({o.GetType()})");
            vec+=new Vector3(GD.Randf()-0.5f,GD.Randf()-0.5f,GD.Randf()-0.5f)*10f;
        }
        return vec;
    }
    public static float GetEffectValue(this Context ctx,string id)
    {
        if(ctx.GetItem(id) is float v)
        {
            return v;
        }
        return 0;
    }
    public static void MulEffectValue(this Context ctx,string id,float scale)
    {
        ctx.Set(id,GetEffectValue(ctx,id)*scale);
    }
    public static bool GetFlag(this Context ctx,string flag)
    {
        return ctx.GetItem(flag)!=null;
    }
    public static Context WithFlag(this Context ctx,params string[] flags)
    {
        var inner=new Context(ctx);
        foreach(var flag in flags)
        {
            inner.Set(flag,true);
        }
        return inner;
    }
}