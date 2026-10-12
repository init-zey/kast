using Godot;

namespace Kast.Godot;

/// <summary>
/// 极简暂停菜单：继续 / 键位设置 / 放弃进度并返回主菜单。
/// 节点全部在 Game.tscn 里（<c>PauseMenu</c> 及其子树），这里只负责显隐、暂停状态与按钮响应。
/// </summary>
public partial class NPauseMenu : Control
{
    /// <summary>主菜单场景；放弃进度后回到这里。</summary>
    public const string MainMenuScene="res://MainMenu.tscn";

    /// <summary>当前实例，供其它脚本查询/打开暂停菜单。</summary>
    public static NPauseMenu Instance{get;private set;}

    [Export] PanelContainer PausePanel;
    [Export] Button ResumeButton;
    [Export] Button KeybindingButton;
    [Export] Button QuitButton;
    [Export] ConfirmationDialog QuitConfirm;

    public bool IsOpen=>Visible;

    public override void _Ready()
    {
        Instance=this;
        ProcessMode=ProcessModeEnum.WhenPaused;   // 暂停后仍要能按键、点按钮
        Visible=false;
        ResumeButton.Pressed+=Resume;
        KeybindingButton.Pressed+=OpenKeybinding;
        QuitButton.Pressed+=OnQuitPressed;
        QuitConfirm.Confirmed+=Quit;
    }

    public override void _ExitTree()
    {
        if(Instance==this)
        {
            Instance=null;
        }
    }

    public void Toggle()
    {
        if(Visible)
        {
            Resume();
        }
        else
        {
            Open();
        }
    }

    public void Open()
    {
        GetTree().Paused=true;
        Visible=true;
        ResumeButton.GrabFocus();
    }

    /// <summary>关闭菜单并恢复游戏。</summary>
    public void Resume()
    {
        Visible=false;
        GetTree().Paused=false;
    }

    /// <summary>把 keychain 的键位编辑器弹出来（节点在 PauseMenu/Keybinding）。</summary>
    void OpenKeybinding()
    {
        GetNode<AcceptDialog>("Keybinding").PopupCenteredRatio(0.9f);
    }

    void OnQuitPressed()
    {
        QuitConfirm.PopupCentered();
    }

    /// <summary>保存当前进度并返回主菜单（确认放弃时走这里）。</summary>
    void Quit()
    {
        NGame.Instance.SaveProgress(SaveSystem.Current);
        SaveSystem.SaveProgress();
        GetTree().Paused=false;
        GetTree().ChangeSceneToFile(MainMenuScene);
    }
}
