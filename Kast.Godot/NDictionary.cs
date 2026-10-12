using Godot;
using Kast.Godot;

/// <summary>
/// 词典面板：列出玩家当前持有的全部词条，按快捷键开关。
/// </summary>
/// <remarks>
/// 词条来自存档里的 <c>PlayerTranslation</c>，与分词、预测用的是同一份表，
/// 所以词典里看到的词就是能画出来并被接受的词。
/// </remarks>
public partial class NDictionary : ScrollContainer
{
    /// <summary>条目容器，每次打开都整体重建。</summary>
    [Export] public Control Container;
    /// <summary>用当前词表重建并显示面板。</summary>
    public void Open()
    {
        Visible=true;
        Helper.ClearChild(Container);
        foreach(var word in SaveSystem.Current.PlayerTranslation.Entrys())
        {
            Helper.LoadScene<NDictEntry>("DictEntry", (n) =>
            {
                Container.AddChild(n);
                n.Init(' ',word);   // 词典里 ' ' 当根旋转：条目自己慢慢转，不跟任何已画出的笔画对齐
            });
        }
    }
    /// <summary>快捷键开/关面板。</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if(@event.IsActionPressed("dict_toggle"))
        {
            if(Visible)
            {
                Visible=false;
            }
            else
            {
                Open();
            }
        }
    }

}
