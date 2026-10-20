using System.Collections.Generic;
using Godot;

namespace Kast.Godot;

/// <summary>
/// 触屏 HUD 的模式控制：只连接按钮信号、切换作画/施法两个界面的可见性，
/// 不接收也不合成任何输入事件——输入由各界面自己的 <c>_GuiInput</c> 与
/// 节点的 <c>MouseFilter</c>、场景树顺序决定归属。
/// </summary>
/// <remarks>
/// 作画模式：<see cref="SpellInput"/> 可见（全屏 ColorRect，MouseFilter=Stop）接收触摸作画；
/// 施法模式：<see cref="CastSurface"/> 可见（全屏透明 Button）在空白区接收触摸并直接施放。
/// 模式按钮在 <c>Hud</c> 里声明于两者之后，因此始终位于最上层、不会被作画层挡住。
/// </remarks>
public partial class NTouchControls : Control
{
    /// <summary>作画界面；可见时才接收触摸。</summary>
    [Export] public NSpellInput SpellInput;
    /// <summary>模式切换按钮。</summary>
    [Export] public Button ModeButton;
    /// <summary>施法模式的空白区按钮。</summary>
    [Export] public Button CastSurface;
    /// <summary>true = 作画模式，false = 施法模式。</summary>
    public bool DrawMode = true;

    const string KeyModeDraw = "touch.mode.draw";
    const string KeyModeCast = "touch.mode.cast";

    static bool ModeStringsRegistered;

    public override void _Ready()
    {
        RegisterModeStrings();
        if(!NSpellInput.IsTouchPlatform)
        {
            // 非移动端：隐藏触屏控件，沿用原有键鼠输入（右键作画、F 施法、数字键/滚轮切槽）
            if(ModeButton!=null)
            {
                ModeButton.Visible=false;
            }
            if(CastSurface!=null)
            {
                CastSurface.Visible=false;
            }
            return;
        }
        if(ModeButton!=null)
        {
            ModeButton.Pressed += ToggleMode;
        }
        if(CastSurface!=null)
        {
            CastSurface.Pressed += Cast;
        }
        ApplyMode();
    }

    /// <summary>切换作画 / 施法模式。</summary>
    public void ToggleMode()
    {
        DrawMode=!DrawMode;
        ApplyMode();
    }

    void ApplyMode()
    {
        SpellInput?.SetActive(DrawMode);
        if(CastSurface!=null)
        {
            CastSurface.Visible=!DrawMode;
        }
        if(ModeButton!=null)
        {
            ModeButton.Text=NLocalization.Tr(DrawMode?KeyModeDraw:KeyModeCast);
        }
    }

    /// <summary>施放当前槽；目标沿用相机当前选中项。</summary>
    void Cast()
    {
        NGame.Instance?.SpellSlots?.CastCurrent(NGame.Instance.ActiveCamera?.Selection);
    }

    /// <summary>
    /// 注册模式按钮文案。沿用 <see cref="NLocalization"/> 的运行时兜底做法，
    /// 使文案不依赖编辑器对 CSV 的重新导入；场景与代码里始终只出现占位符 key。
    /// </summary>
    static void RegisterModeStrings()
    {
        if(ModeStringsRegistered)
        {
            return;
        }
        ModeStringsRegistered=true;
        var byLocale=new Dictionary<string,Translation>();
        void Add(string locale,string key,string text)
        {
            if(!byLocale.TryGetValue(locale,out var translation))
            {
                translation=new Translation{Locale=locale};
                byLocale[locale]=translation;
                TranslationServer.AddTranslation(translation);
            }
            translation.AddMessage(key,text);
        }
        Add(NLocalization.English,KeyModeDraw,"Draw");
        Add(NLocalization.English,KeyModeCast,"Cast");
        Add(NLocalization.Chinese,KeyModeDraw,"作画");
        Add(NLocalization.Chinese,KeyModeCast,"施法");
    }
}
