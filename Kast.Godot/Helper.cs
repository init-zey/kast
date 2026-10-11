using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

public static class Helper
{
    public static T LoadScene<T>(string id, T fallback=null)
        where T:Node
    {
        var scenePath=$"res://{id}.tscn";
        if (!ResourceLoader.Exists(scenePath)){
            if(fallback==null)
            {
                GD.PushError($"场景不存在：{scenePath}");
            }
            return fallback;
        }
        return GD.Load<PackedScene>(scenePath).Instantiate<T>();
    }
    public static T LoadScene<T>(string id,Action<T> init,Func<T> fallbackGetter=null)
        where T:Node
    {
        var node=LoadScene(id,fallbackGetter?.Invoke());
        if(node!=null)
        {
            init(node);
        }
        return node;
    }
    public static void ClearChild(Node node)
    {
        foreach(var child in node.GetChildren())
        {
            child.QueueFree();
        }
    }
    public static float FNVStringToFloat(string input)
    {
        if (string.IsNullOrEmpty(input)) return 0f;
        const uint fnvOffsetBasis = 2166136261;
        const uint fnvPrime = 16777619;
        
        uint hash = fnvOffsetBasis;
        foreach (char c in input)
        {
            hash ^= (byte)(c & 0xFF);
            hash *= fnvPrime;
            hash ^= (byte)((c >> 8) & 0xFF);
            hash *= fnvPrime;
        }
        return hash / (float)uint.MaxValue;
    }

    public static IEnumerable<string> ScanSceneResourceInDirTrimmed(string path)
    {
        return ScanResourceInDir(path,".tscn").Select(p=>p.TrimPrefix("res://").TrimSuffix(".tscn").TrimSuffix(".scn"));
    }
    public static IEnumerable<string> ScanResourceInDir(string path,string suffix)
    {
        using var dir = DirAccess.Open(path);
        if (dir == null) yield break;

        dir.ListDirBegin();
        string fileName = dir.GetNext();
        while (fileName != "")
        {
            string fullPath = Path.Combine(path, fileName);
            if (dir.CurrentIsDir())
            {
                if (fileName != "." && fileName != "..")
                    foreach (var result in ScanResourceInDir(fullPath,suffix))
                    {
                        yield return result;
                    }
            }
            else if (fileName.EndsWith(suffix))
            {
                yield return fullPath;
            }
            fileName = dir.GetNext();
        }
        dir.ListDirEnd();
    }
    public static Tween Shake(Node3D node, float halfTime)
    {
        var tw = node.CreateTween();
        var initPos = node.Position;
        tw.Parallel().TweenProperty(node, Control.PropertyName.Position.ToString(), initPos + new Vector3(GD.Randf(), GD.Randf(), GD.Randf()) * 0.05f, halfTime);
        tw.Parallel().TweenProperty(node, Control.PropertyName.Position.ToString(), initPos, halfTime);
        return tw;
    }

    internal static bool FreeViewInputHandler(Control n, InputEvent @event)
    {
        if(@event is InputEventMouseMotion motion)
        {
            if(Input.IsMouseButtonPressed(MouseButton.Middle))
            {
                n.Position+=motion.Relative;
                return true;
            }
        }
        else if (@event is InputEventMouseButton mb)
        {
            var mousePos=n.GetLocalMousePosition();
            if(mb.ButtonIndex==MouseButton.WheelUp)
            {
                n.Scale*=1.1f;
            }
            else if (mb.ButtonIndex==MouseButton.WheelDown)
            {
                n.Scale*=0.9f;
            }
            else
            {
                return false;
            }
            var newMousePos=n.GetLocalMousePosition();
            n.Position+=(newMousePos-mousePos)*n.Scale;
            return true;
        }
        return false;
    }
    public static bool GetUnbiasedIndex(uint randomValue, int length, out int result)
    {
        if (length <= 0)
            throw new ArgumentOutOfRangeException(nameof(length));

        uint range = (uint)length;

        // 2^32 % range
        uint threshold = (uint)((0x1_0000_0000UL - range) % range);

        if (randomValue < threshold)
        {
            result=0;
            return true;
        }
        result=(int)(randomValue % range);
        return false;
    }
}