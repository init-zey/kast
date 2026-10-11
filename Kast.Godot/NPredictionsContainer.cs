using Godot;

public partial class NPredictionsContainer : ScrollContainer
{
    [Export] Control PredictionsContainer;
    public override void _PhysicsProcess(double delta)
    {
        var bar=GetVScrollBar();
        if(PredictionsContainer.GetChildCount()>0)
        {
            var stride=((Control)PredictionsContainer.GetChild(0)).Size.Y;
            if(PredictionsContainer.Size.Y-bar.Value>0 && Input.IsActionJustPressed("scroll_down"))
            {
                bar.Value += stride;
            }
            else if(bar.Value>0 &&Input.IsActionJustPressed("scroll_up"))
            {
                bar.Value -= stride;
            }
            GetViewport().SetInputAsHandled();
        }
    }
}
