using Godot;

/// <summary>
/// 游戏内 HUD 根节点：把施法界面与当前咒语笔画符号挂在一起，供场景引用。
/// </summary>
/// <remarks>
/// 节点结构在场景文件里，这里只保留引用；<see cref="_Ready"/> 里的旧调试按钮（直接输入文本施法）
/// 已随文本输入接口一起停用，保留注释作为参考。
/// </remarks>
public partial class NHud : Control
{
    [Export] public NSpellInput SpellInput;
    [Export] public NSpellSymbol SpellSymbol;
    public override void _Ready()
    {
        // CompileButton.Pressed += () =>
        // {
        //     if(LineEdit.Text.Length>0)
        //     {
        //         StateLabel.Text=NGame.Instance.SetSpellInput(LineEdit.Text);
        //     }
        //     else
        //     {
        //         StateLabel.Text=NGame.Instance.ClearSpell();
        //     }
        // };
    }

}
