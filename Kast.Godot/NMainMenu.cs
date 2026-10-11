using System;
using Godot;

namespace Kast.Godot;

/// <summary>
/// 主菜单：继续 / 新游戏 / 设置 / 退出。
/// 整个界面在代码里搭建，MainMenu.tscn 只挂这个脚本，避免手写大面积场景文件。
/// </summary>
public partial class NMainMenu : Control
{
    /// <summary>进入游戏的场景。</summary>
    public const string GameScene="res://Game.tscn";

    /// <summary>全局设置缓存，菜单里改完立刻写回 user://settings.sav。</summary>
    public static GameSettings Settings{get;private set;}

    const int TitleSize=72;
    const int SubtitleSize=18;
    const int ButtonSize=24;
    const int SmallSize=14;
    const int SectionSize=26;

    static readonly Color BackdropTop=new(0.05f,0.06f,0.09f);
    static readonly Color BackdropBottom=new(0.01f,0.01f,0.02f);
    static readonly Color PanelColor=new(0.09f,0.10f,0.14f,0.94f);
    static readonly Color AccentColor=new(0.55f,0.78f,1f);
    static readonly Color MutedColor=new(0.62f,0.65f,0.72f);

    Button ContinueButton;
    Button NewGameButton;
    Button SettingsButton;
    Button QuitButton;
    Button LanguageButton;
    Button FullscreenButton;
    Control MenuPanel;
    Control SettingsPanel;
    Label VolumeValueLabel;
    Label SaveInfoLabel;
    ConfirmationDialog ConfirmNewDialog;
    VBoxContainer Root;

    public override void _Ready()
    {
        Settings??=SaveSystem.LoadSettings();
        NLocalization.Register();
        ApplySettings();
        BuildUi();
        RefreshLabels();
    }

    /// <summary>设置项被改动的唯一入口：改缓存 + 立即生效 + 立刻写回磁盘。</summary>
    void ApplySettings()
    {
        Settings??=GameSettings.CreateDefault();
        NLocalization.SetLocale(Settings.Locale);
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex("Master"),Mathf.LinearToDb(Mathf.Clamp(Settings.MasterVolume,0f,1f)));
        var mode=Settings.Fullscreen?DisplayServer.WindowMode.Fullscreen:DisplayServer.WindowMode.Windowed;
        if(DisplayServer.WindowGetMode()!=mode)
        {
            DisplayServer.WindowSetMode(mode);
        }
        SaveSystem.SaveSettings(Settings);
    }

    void BuildUi()
    {
        Root=new VBoxContainer
        {
            Name="Root",
            Alignment=BoxContainer.AlignmentMode.Center,
            AnchorRight=1f,
            AnchorBottom=1f,
            MouseFilter=MouseFilterEnum.Stop,
        };
        Root.AddThemeConstantOverride("separation",12);
        AddChild(Root);

        BuildBackdrop();

        // 主菜单面板
        MenuPanel=new VBoxContainer { Name="MenuPanel", SizeFlagsHorizontal=SizeFlags.ShrinkCenter };
        ((VBoxContainer)MenuPanel).AddThemeConstantOverride("separation",10);
        Root.AddChild(MenuPanel);

        var title=new Label
        {
            Text=NLocalization.Tr("menu.title"),
            HorizontalAlignment=HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size",TitleSize);
        title.AddThemeColorOverride("font_color",AccentColor);
        MenuPanel.AddChild(title);

        var subtitle=new Label
        {
            Text=NLocalization.Tr("menu.subtitle"),
            HorizontalAlignment=HorizontalAlignment.Center,
        };
        subtitle.AddThemeFontSizeOverride("font_size",SubtitleSize);
        subtitle.AddThemeColorOverride("font_color",MutedColor);
        MenuPanel.AddChild(subtitle);

        MenuPanel.AddChild(new Control { CustomMinimumSize=new Vector2(0,24) });

        ContinueButton=MakeMenuButton("menu.continue");
        ContinueButton.Connect(BaseButton.SignalName.Pressed,Callable.From(OnContinuePressed));
        NewGameButton=MakeMenuButton("menu.new_game");
        NewGameButton.Connect(BaseButton.SignalName.Pressed,Callable.From(OnNewGamePressed));
        SettingsButton=MakeMenuButton("menu.settings");
        SettingsButton.Connect(BaseButton.SignalName.Pressed,Callable.From(OnSettingsPressed));
        QuitButton=MakeMenuButton("menu.quit");
        QuitButton.Connect(BaseButton.SignalName.Pressed,Callable.From(OnQuitPressed));

        SaveInfoLabel=new Label
        {
            HorizontalAlignment=HorizontalAlignment.Center,
            AutowrapMode=TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize=new Vector2(420,0),
        };
        SaveInfoLabel.AddThemeFontSizeOverride("font_size",SmallSize);
        SaveInfoLabel.AddThemeColorOverride("font_color",MutedColor);
        MenuPanel.AddChild(SaveInfoLabel);

        // 设置面板
        SettingsPanel=BuildSettingsPanel();
        Root.AddChild(SettingsPanel);

        var version=new Label
        {
            Text=$"Kast {ProjectSettings.GetSetting("application/config/version","0.1")}",
            AnchorLeft=0f,
            AnchorTop=1f,
            AnchorRight=1f,
            AnchorBottom=1f,
            OffsetTop=-28f,
            OffsetLeft=12f,
            HorizontalAlignment=HorizontalAlignment.Left,
        };
        version.AddThemeFontSizeOverride("font_size",SmallSize);
        version.AddThemeColorOverride("font_color",MutedColor);
        AddChild(version);

        // 新游戏覆盖存档的确认框
        ConfirmNewDialog=new ConfirmationDialog
        {
            Title=NLocalization.Tr("dialog.confirm_new_title"),
            DialogText=NLocalization.Tr("dialog.confirm_new_body"),
            OkButtonText=NLocalization.Tr("dialog.ok"),
            CancelButtonText=NLocalization.Tr("dialog.cancel"),
        };
        ConfirmNewDialog.Connect(ConfirmationDialog.SignalName.Confirmed,Callable.From(StartNewGame));
        AddChild(ConfirmNewDialog);

        ShowMenu();
    }

    void BuildBackdrop()
    {
        var rect=new TextureRect
        {
            Name="Backdrop",
            AnchorRight=1f,
            AnchorBottom=1f,
            ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode=TextureRect.StretchModeEnum.Scale,
            MouseFilter=MouseFilterEnum.Ignore,
        };
        var gradient=new Gradient();
        gradient.SetColor(0,BackdropTop);
        gradient.SetColor(1,BackdropBottom);
        var texture=new GradientTexture2D
        {
            Gradient=gradient,
            FillFrom=new Vector2(0.5f,0f),
            FillTo=new Vector2(0.5f,1f),
        };
        rect.Texture=texture;
        AddChild(rect);
        MoveChild(rect,0); // 放到 Root 之下、最底层
    }

    Button MakeMenuButton(string key)
    {
        var button=new Button
        {
            Text=NLocalization.Tr(key),
            CustomMinimumSize=new Vector2(300,52),
            FocusMode=FocusModeEnum.All,
        };
        button.AddThemeFontSizeOverride("font_size",ButtonSize);
        MenuPanel.AddChild(button);
        return button;
    }

    Control BuildSettingsPanel()
    {
        var background=new PanelContainer { Name="SettingsPanel", SizeFlagsHorizontal=SizeFlags.ShrinkCenter };
        background.AddThemeStyleboxOverride("panel",MakePanelStyle());

        var box=new VBoxContainer { CustomMinimumSize=new Vector2(460,0) };
        box.AddThemeConstantOverride("separation",14);
        background.AddChild(box);

        var title=new Label
        {
            Text=NLocalization.Tr("settings.title"),
            HorizontalAlignment=HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size",SectionSize);
        title.AddThemeColorOverride("font_color",AccentColor);
        box.AddChild(title);

        box.AddChild(new HSeparator());

        // 语言
        LanguageButton=new Button { Name="LanguageButton", CustomMinimumSize=new Vector2(180,40) };
        LanguageButton.AddThemeFontSizeOverride("font_size",ButtonSize-4);
        LanguageButton.Connect(BaseButton.SignalName.Pressed,Callable.From(OnLanguagePressed));
        box.AddChild(MakeSettingRow("settings.language",LanguageButton));

        // 音量
        var volume=new HSlider
        {
            MinValue=0,
            MaxValue=1,
            Step=0.01,
            Value=Settings.MasterVolume,
            CustomMinimumSize=new Vector2(180,24),
            SizeFlagsVertical=SizeFlags.ShrinkCenter,
        };
        volume.Connect(global::Godot.Range.SignalName.ValueChanged,Callable.From((double value)=>OnVolumeChanged((float)value)));
        VolumeValueLabel=new Label { CustomMinimumSize=new Vector2(52,0), HorizontalAlignment=HorizontalAlignment.Right };
        VolumeValueLabel.AddThemeFontSizeOverride("font_size",ButtonSize-6);
        VolumeValueLabel.AddThemeColorOverride("font_color",MutedColor);
        box.AddChild(MakeSettingRow("settings.volume",MakeRow(volume,VolumeValueLabel)));

        // 全屏
        FullscreenButton=new Button { Name="FullscreenButton", CustomMinimumSize=new Vector2(180,40) };
        FullscreenButton.AddThemeFontSizeOverride("font_size",ButtonSize-4);
        FullscreenButton.Connect(BaseButton.SignalName.Pressed,Callable.From(OnFullscreenPressed));
        box.AddChild(MakeSettingRow("settings.fullscreen",FullscreenButton));

        var hint=new Label
        {
            Text=NLocalization.Tr("settings.hint"),
            HorizontalAlignment=HorizontalAlignment.Center,
            AutowrapMode=TextServer.AutowrapMode.WordSmart,
        };
        hint.AddThemeFontSizeOverride("font_size",SmallSize);
        hint.AddThemeColorOverride("font_color",MutedColor);
        box.AddChild(hint);

        var back=new Button
        {
            Text=NLocalization.Tr("menu.back"),
            CustomMinimumSize=new Vector2(0,48),
            SizeFlagsHorizontal=SizeFlags.ExpandFill,
        };
        back.AddThemeFontSizeOverride("font_size",ButtonSize-2);
        back.Connect(BaseButton.SignalName.Pressed,Callable.From(ShowMenu));
        box.AddChild(back);

        return background;
    }

    /// <summary>一行设置：左边标题，右边控件。</summary>
    Control MakeSettingRow(string key,Control control)
    {
        var row=new HBoxContainer();
        row.AddThemeConstantOverride("separation",16);
        var label=new Label
        {
            Text=NLocalization.Tr(key),
            SizeFlagsHorizontal=SizeFlags.ExpandFill,
            VerticalAlignment=VerticalAlignment.Center,
        };
        label.AddThemeFontSizeOverride("font_size",ButtonSize-4);
        row.AddChild(label);
        row.AddChild(control);
        return row;
    }

    static HBoxContainer MakeRow(params Control[] children)
    {
        var row=new HBoxContainer();
        row.AddThemeConstantOverride("separation",8);
        foreach(var child in children)
        {
            row.AddChild(child);
        }
        return row;
    }

    static StyleBoxFlat MakePanelStyle()
    {
        var style=new StyleBoxFlat
        {
            BgColor=PanelColor,
            CornerRadiusTopLeft=12,
            CornerRadiusTopRight=12,
            CornerRadiusBottomLeft=12,
            CornerRadiusBottomRight=12,
            ContentMarginLeft=32,
            ContentMarginRight=32,
            ContentMarginTop=28,
            ContentMarginBottom=28,
        };
        return style;
    }

    /// <summary>刷新所有随语言/存档变化的文字。</summary>
    void RefreshLabels()
    {
        var hasSave=SaveSystem.HasSave();
        ContinueButton.Disabled=!hasSave;
        ContinueButton.TooltipText=hasSave?string.Empty:NLocalization.Tr("menu.empty_slot");
        SaveInfoLabel.Text=hasSave?BuildSaveInfo(hasSave):string.Empty;

        LanguageButton.Text=NLocalization.DisplayName(Settings.Locale);
        FullscreenButton.Text=NLocalization.Tr(Settings.Fullscreen?"settings.on":"settings.off");
        VolumeValueLabel.Text=$"{Mathf.RoundToInt(Settings.MasterVolume*100)}%";
    }

    string BuildSaveInfo(bool hasSave)
    {
        if(!hasSave)
        {
            return string.Empty;
        }
        var progress=SaveSystem.LoadProgress();
        if(progress==null)
        {
            return NLocalization.Tr("menu.empty_slot");
        }
        var count=progress.UnlockedSymbols?.Length??0;
        return NLocalization.Tr("menu.save_info",progress.Seed,count);
    }

    void ShowMenu()
    {
        MenuPanel.Visible=true;
        SettingsPanel.Visible=false;
        RefreshLabels();
        ContinueButton.GrabFocus();
    }

    void ShowSettings()
    {
        MenuPanel.Visible=false;
        SettingsPanel.Visible=true;
        RefreshLabels();
        LanguageButton.GrabFocus();
    }

    /// <summary>语言切换后整块界面需要重建，才能让所有 Label 用上新文案。</summary>
    void RebuildUi()
    {
        foreach(var child in GetChildren())
        {
            child.QueueFree();
        }
        MenuPanel=null;
        SettingsPanel=null;
        BuildUi();
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
            SaveInfoLabel.Text=NLocalization.Tr("menu.empty_slot");
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
        var pg=new Progress
        {
            Seed=(int)(GD.Randi()&0x7FFFFFFF),
            UnlockedSymbols=Array.Empty<string>(),
        };
        SaveSystem.SaveProgress(pg);
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
        ApplySettings();
        RebuildUi();
        ShowSettings();
        ToastLanguageChanged();
    }

    void OnVolumeChanged(float value)
    {
        Settings.MasterVolume=value;
        ApplySettings();
        VolumeValueLabel.Text=$"{Mathf.RoundToInt(value*100)}%";
    }

    void OnFullscreenPressed()
    {
        Settings.Fullscreen=!Settings.Fullscreen;
        ApplySettings();
        RefreshLabels();
    }

    void ToastLanguageChanged()
    {
        ContinueButton?.GrabFocus();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if(@event.IsActionPressed("ui_cancel")&&SettingsPanel!=null&&SettingsPanel.Visible)
        {
            ShowMenu();
            GetViewport().SetInputAsHandled();
        }
    }
}
