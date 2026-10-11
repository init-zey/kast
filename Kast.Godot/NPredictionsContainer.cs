using Godot;
using System;

public partial class NPredictionsContainer : ScrollContainer
{
    public override void _PhysicsProcess(double delta)
    {
        var bar=GetVScrollBar();
        if(Input.IsActionJustPressed("scroll_down"))
        {
            bar.Value += 10;
        }
        else if(Input.IsActionJustPressed("scroll_up"))
        {
            bar.Value -= 10;
        }
    }


}
