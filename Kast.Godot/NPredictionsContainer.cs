using Godot;

/// <summary>
/// 候补预览的滚动容器：按快捷键一次滚动一个条目的高度，并用滚轮无关的方式接管输入。
/// </summary>
/// <remarks>
/// 步长取第一个子节点的实际高度，而不是固定像素，这样换皮肤或改字号后滚动仍然对齐条目边界。
/// </remarks>
public partial class NPredictionsContainer : ScrollContainer
{
    /// <summary>条目容器，用来取步长与判断是否还有内容。</summary>
    [Export] Control PredictionsContainer;
    /// <summary>处理上下滚动键：到顶/到底时不响应。</summary>
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
