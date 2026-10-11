using System;
using System.Collections.Generic;
using System.Linq;
using MessagePack;

namespace Kast.Godot;

[MessagePackObject]
public class Spell
{
    [Key(0)]
    public char FirstDir='\0';
    [Key(1)]
    public List<char> Angles;

    public Spell()
    {
        FirstDir='\0';
        Angles=new();
    }

    [IgnoreMember]
    public bool HasFirstDir => FirstDir != '\0';

    public char Last()
    {
        return Angles.Count>0?Angles.Last():FirstDir;
    }

    internal void Add(char finaldir)
    {
        if(FirstDir=='\0')
        {
            FirstDir=finaldir;
        }
        else
        {
            Angles.Add(finaldir);
        }
    }

    internal void RemoveLast()
    {
        if(Angles.Count>0)
        {
            Angles.RemoveAt(Angles.Count-1);
        }
        else
        {
            FirstDir='\0';
        }
    }
    public char[] ToArray()
    {
        return [FirstDir,..Angles];
    }
    public void Clear()
    {
        FirstDir='\0';
        Angles.Clear();
    }
}