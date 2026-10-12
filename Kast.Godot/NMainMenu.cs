using System;
using Godot;

namespace Kast.Godot;

/// <summary>
/// 主菜单逻辑：继续 / 新游戏 / 设置 / 退出。
/// 节点结构全部在 MainMenu.tscn 里，这里只负责引用、信号连接与状态刷新。
/// </summary>
public partial class NMainMenu : Control
{
    /// <summary>进入游戏的场景。</summary>
    public const string GameScene="res://Game.tscn";

    /// <summary>场景里用 metadata 记下每个控件的翻译 key，运行时据此重刷文本。</summary>
    public const string TextKeyMeta="text_key";
    const string DialogTitleKey="dialog.confirm_new_title";
    const string DialogBodyKey="dialog.confirm_new_body";

    /// <summary>全局设置缓存，菜单里改完立刻写回 user://settings.sav。</summary>
    public static GameSettings Settings{get;private set;}

    [Export] VBoxContainer MenuPanel;
    [Export] Button ContinueButton;
    [Export] Button NewGameButton;
    [Export] Button SettingsButton;
    [Export] Button QuitButton;
    [Export] Label SaveInfoLabel;
    [Export] PanelContainer SettingsPanel;
    [Export] Button LanguageButton;
    [Export] HSlider VolumeSlider;
    [Export] Label VolumeValueLabel;
    [Export] Button FullscreenButton;
    [Export] Button BackButton;
    [Export] Label VersionLabel;
    [Export] ConfirmationDialog ConfirmNewDialog;

    public override void _Ready()
    {
        var firstRun=!FileAccess.FileExists(SaveSystem.SettingsPath);
        Settings??=SaveSystem.LoadSettings();
        NLocalization.Register();   // 确保内置文案已注册，作为 CSV 之外的兜底
        ApplySettings(false);
        if(firstRun)
        {
            SaveSystem.SaveSettings(Settings);
        }

        VersionLabel.Text=$"Kast {ProjectSettings.GetSetting("application/config/version","0.1")}";
        VolumeSlider.Value=Settings.MasterVolume;

        ContinueButton.Pressed+=OnContinuePressed;
        NewGameButton.Pressed+=OnNewGamePressed;
        SettingsButton.Pressed+=OnSettingsPressed;
        QuitButton.Pressed+=OnQuitPressed;
        LanguageButton.Pressed+=OnLanguagePressed;
        FullscreenButton.Pressed+=OnFullscreenPressed;
        BackButton.Pressed+=ShowMenu;
        ConfirmNewDialog.Confirmed+=StartNewGame;
        // 放在赋值之后连接，避免启动时被 ValueChanged 触发一次多余的落盘
        VolumeSlider.ValueChanged+=OnVolumeChanged;

        ShowMenu();
    }

    /// <summary>取文案；场景里 text 写的是翻译 key，运行时由 Godot 自动翻译。</summary>
    static string T(string key)
    {
        return NLocalization.Tr(key);
    }

    /// <summary>
    /// 把设置同步到引擎，<paramref name="persist"/> 为 true 时同时写回磁盘。
    /// 语言变化后必须显式重刷文本：运行时（非编辑器）仅改 locale 并不会让
    /// 已经实例化的 Label/Button 重新翻译。
    /// </summary>
    void ApplySettings(bool persist=true)
    {
        Settings??=GameSettings.CreateDefault();
        NLocalization.SetLocale(Settings.Locale);
        RefreshStaticText();
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex("Master"),Mathf.LinearToDb(Mathf.Clamp(Settings.MasterVolume,0f,1f)));
        var mode=Settings.Fullscreen?DisplayServer.WindowMode.Fullscreen:DisplayServer.WindowMode.Windowed;
        if(DisplayServer.WindowGetMode()!=mode)
        {
            DisplayServer.WindowSetMode(mode);
        }
        if(persist)
        {
            SaveSystem.SaveSettings(Settings);
        }
    }

    /// <summary>
    /// 重刷“文本固定来自翻译表”的控件。场景里 text 写的只是 key，
    /// 运行时不会随 locale 自动更新，所以这里按 key 统一重翻一遍。
    /// </summary>
    void RefreshStaticText()
    {
        RefreshStoredText(this);
        ConfirmNewDialog.Title=T(DialogTitleKey);
        ConfirmNewDialog.DialogText=T(DialogBodyKey);
    }

    void RefreshStoredText(Node node)
    {
        if(node.HasMeta(TextKeyMeta))
        {
            var key=node.GetMeta(TextKeyMeta).AsString();
            switch(node)
            {
                case Label label:
                    label.Text=T(key);
                    break;
                case Button button:
                    button.Text=T(key);
                    break;
            }
        }
        foreach(var child in node.GetChildren())
        {
            RefreshStoredText(child);
        }
    }

    /// <summary>刷新随存档变化的文字。</summary>
    void RefreshLabels()
    {
        var hasSave=SaveSystem.HasSave();
        SaveInfoLabel.Text=hasSave?BuildSaveInfo():string.Empty;

        var usable=hasSave&&!SaveSystem.LoadFailed;
        ContinueButton.Disabled=!usable;
        ContinueButton.TooltipText=usable?string.Empty:T("menu.empty_slot");

        // 下面这些是运行时拼出来的，不走翻译表
        LanguageButton.Text=NLocalization.DisplayName(Settings.Locale);
        FullscreenButton.Text=T(Settings.Fullscreen?"settings.on":"settings.off");
        VolumeValueLabel.Text=$"{Mathf.RoundToInt(Settings.MasterVolume*100)}%";
    }

    string BuildSaveInfo()
    {
        var progress=SaveSystem.LoadProgress();
        if(progress==null)
        {
            return T("menu.broken_save");
        }
        return NLocalization.Format("menu.save_info",progress.Seed);
    }

    void ShowMenu()
    {
        MenuPanel.Visible=true;
        SettingsPanel.Visible=false;
        RefreshLabels();
        (ContinueButton.Disabled?NewGameButton:ContinueButton).GrabFocus();
    }

    void ShowSettings()
    {
        MenuPanel.Visible=false;
        SettingsPanel.Visible=true;
        RefreshLabels();
        LanguageButton.GrabFocus();
    }

    void OnContinuePressed()
    {
        if(!SaveSystem.HasSave())
        {
            RefreshLabels();
            return;
        }
        if(SaveSystem.LoadProgress()==null)
        {
            RefreshLabels(); // 存档损坏：禁用“继续”并显示提示
            return;
        }
        GetTree().ChangeSceneToFile(GameScene);
    }

    void OnNewGamePressed()
    {
        if(SaveSystem.HasSave())
        {
            ConfirmNewDialog.PopupCentered();
            return;
        }
        StartNewGame();
    }

    /// <summary>开新档：清掉旧存档、随机种子、从零开始（符号全部未解锁）。</summary>
    void StartNewGame()
    {
        SaveSystem.DeleteSave();
        SaveSystem.Current=new Progress
        {
            Seed=0,
            // Seed=(int)(GD.Randi()&0x7FFFFFFF),
            PlayerTranslation=new(),
            LevelID="1"
        };
        SaveSystem.SaveProgress();
        GetTree().ChangeSceneToFile(GameScene);
    }

    void OnSettingsPressed()
    {
        ShowSettings();
    }

    void OnQuitPressed()
    {
        GetTree().Quit();
    }

    void OnLanguagePressed()
    {
        Settings.Locale=Settings.Locale==NLocalization.Chinese?NLocalization.English:NLocalization.Chinese;
        ApplySettings();      // SetLocale 会让已显示的文本按新语言重算
        RefreshLabels();
        ShowSettings();
    }

    void OnVolumeChanged(double value)
    {
        Settings.MasterVolume=(float)value;
        ApplySettings();
        VolumeValueLabel.Text=$"{Mathf.RoundToInt(Settings.MasterVolume*100)}%";
    }

    void OnFullscreenPressed()
    {
        Settings.Fullscreen=!Settings.Fullscreen;
        ApplySettings();
        RefreshLabels();
    }

    public override void _Input(InputEvent @event)
    {
        if(@event.IsActionPressed("ui_cancel")&&SettingsPanel.Visible)
        {
            ShowMenu();
            GetViewport().SetInputAsHandled();
        }
    }
}
