using Godot;
using Kast.Core;
using Kast.Godot;

/// <summary>
/// "一个框 + 一条词形"的通用基类：咒语槽与词典条目都由它派生，
/// 负责选中/未选中的描边与底色切换，以及点按选中。
/// </summary>
/// <remarks>
/// 三个 <see langword="required"/> 字段由创建方（<c>NSpellSlots.CreateSlot</c>、
/// <c>NDictionary</c>）赋值，不用 <c>[Export]</c>——它们指向运行时构造的对象，
/// 场景里无法预先连线。
/// </remarks>
public partial class NSpellSymbolSlot : PanelContainer
{
    /// <summary>在九宫格里的序号（0 起）；词典条目、预览条目为 -1。</summary>
    public int SlotIndex=-1;
    /// <summary>未选中时的词形颜色。</summary>
    public static Color SymbolNormalColor=Colors.White;
    /// <summary>选中时的词形颜色。</summary>
    public static Color SymbolActiveColor=Colors.Yellow;
    /// <summary>选中时的填充色。</summary>
    public static Color ActiveFill=new(1f,1f,1f,0.08f);
    /// <summary>选中时的描边色。</summary>
    public static Color ActiveBorder=new(1f,1f,0f,0.8f);
    /// <summary>槽里显示的词形。</summary>
    public required NSpellSymbol Symbol;
    /// <summary>本槽所绑定的翻译表（词典编辑期间可能是副本，见 <see cref="GameTranslation.Copy"/>）。</summary>
    public required GameTranslation Translation;
    /// <summary>本槽独立的样式实例；每个槽要能各自改描边，所以从资源复制一份。</summary>
    StyleBoxFlat PanelStyle=(StyleBoxFlat)GD.Load<StyleBoxFlat>("res://Materials/SlotBox.tres").Duplicate();
    /// <summary>套用本槽的样式实例。</summary>
    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", PanelStyle);
    }

    /// <summary>
    /// 点按选中：触摸按下或鼠标左键（<c>select</c> 动作）都走这里。
    /// 咒语槽（<see cref="SlotIndex"/> ≥ 0）切到对应槽位，其余条目按普通条目选中。
    /// </summary>
    public override void _GuiInput(InputEvent @event)
    {
        if(!IsSelectPress(@event))
        {
            return;
        }
        if(SlotIndex>=0&&NGame.Instance.SpellSlots!=null)
        {
            NGame.Instance.SpellSlots.SelectSlot(SlotIndex);
        }
        else
        {
            NGame.Instance.SetSelectedSymbolSlot(this);
        }
        AcceptEvent();
    }

    /// <summary>
    /// 触摸按下总是可选中；鼠标左键只在词典条目（<see cref="SlotIndex"/> &lt; 0）上生效，
    /// 咒语槽在非移动端仍沿用原有的数字键/滚轮切换。
    /// </summary>
    bool IsSelectPress(InputEvent @event)
    {
        if(@event is InputEventScreenTouch touch)
        {
            return touch.Pressed;
        }
        return SlotIndex<0&&@event.IsActionPressed("select");
    }

    /// <summary>切回未选中外观。</summary>
    public virtual void Deactive()
    {
        Symbol.Modulate=SymbolNormalColor;
        PanelStyle.BorderColor=new Color(1f,1f,1f,0.15f);
        PanelStyle.BgColor=new Color(0f,0f,0f,0.35f);
    }
    /// <summary>切到选中外观。</summary>
    public virtual void Active()
    {
        Symbol.Modulate=SymbolActiveColor;
        PanelStyle.BorderColor=ActiveBorder;
        PanelStyle.BgColor=ActiveFill;
    }

}