using MessagePack;

namespace Kast.Godot;

[MessagePackObject]
public class Progress
{
    [Key(0)]
    public int Seed;
    [Key(1)]
    public string[] UnlockedSymbols;
}