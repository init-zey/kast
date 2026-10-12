using Godot;
using Kast.Core;

/// <summary>
/// "一个框 + 一条词形"的通用基类：咒语槽与词典条目都由它派生，
/// 负责选中/未选中的描边与底色切换。
/// </summary>
/// <remarks>
/// 三个 <see langword="required"/> 字段由创建方（<c>NSpellSlots.CreateSlot</c>、
/// <c>NDictionary</c>）赋值，不用 <c>[Export]</c>——它们指向运行时构造的对象，
/// 场景里无法预先连线。
/// </remarks>
public partial class NSpellSymbolSlot : PanelContainer
{
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
    /// <summary>本槽是"咒语"还是"整句"，决定输入时的解析方式。</summary>
    public required SlotType SlotType;
    /// <summary>本槽所绑定的翻译表（词典编辑期间可能是副本，见 <see cref="GameTranslation.Copy"/>）。</summary>
    public required GameTranslation Translation;
    /// <summary>本槽独立的样式实例；每个槽要能各自改描边，所以从资源复制一份。</summary>
    StyleBoxFlat PanelStyle=(StyleBoxFlat)GD.Load<StyleBoxFlat>("res://Materials/SlotBox.tres").Duplicate();
    /// <summary>套用本槽的样式实例。</summary>
    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", PanelStyle);
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

/// <summary>槽位内容的种类。</summary>
public enum SlotType
{
    /// <summary>单个词（词典条目用）。</summary>
    Token,
    /// <summary>整句咒语（九个快捷槽用）。</summary>
    Sentence
}