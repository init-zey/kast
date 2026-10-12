using Godot;
using Kast.Core;
using Kast.Godot;

/// <summary>
/// 词典里的一条可编辑词条：左侧画出整个词形，右侧是可改名的文本框。
/// </summary>
/// <remarks>
/// 文本被改过才写回翻译表（见 <see cref="Deactive"/>）：改空或把笔画删光即视为删除该词条。
/// <see cref="IsIncreaser"/> 标出列表末尾那个"空白条目"，用来新增词；它提交一次后会自动再补一个。
/// </remarks>
public partial class NSpellEntry : NSpellSymbolSlot
{
    [Export] public NSpellSymbol InitialSymbol;
    /// <summary>可编辑的真实词文本框。</summary>
    [Export] public LineEdit Real;
    /// <summary>是否是列表末尾那个用于新增的空白条目。</summary>
    public bool IsIncreaser=false;
    /// <summary>进入编辑前的文本，用于 Esc 撤销改名。</summary>
    string initalRealText;
    NDictionary PlayerDictionary;
    /// <summary>
    /// 绑定一条词条：<b>整个词条都画出来</b>，并把词形翻译出的真实词填进文本框。
    /// <paramref name="spell"/> 的 <see cref="Spell.FirstDir"/> 是根旋转（整体朝向），
    /// <see cref="Spell.Angles"/> 里都是相对转角——只画剩下的一截会让玩家以为词条就剩那么长，
    /// 所以这里不做截断。
    /// </summary>
    /// <param name="dictionary">所属词典；悬停预览用的临时条目传 null。</param>
    public void Init(NDictionary dictionary,Spell spell)
    {
        Symbol=InitialSymbol;
        Symbol.SetSpell(spell);
        if(dictionary!=null)
        {
            PlayerDictionary=dictionary;
        }
        Real.Text=Translation.GetRealWord(spell.AsString())??"";
        initalRealText=Real.Text;
    }
    public override void _UnhandledInput(InputEvent @event)
    {
        if(Real.HasFocus())
        {
            if(@event.IsActionPressed("deselect"))
            {
                NGame.Instance.SetSelectedSymbolSlot(null);
                Real.Text=initalRealText;
            }
            GetViewport().SetInputAsHandled();
        }
    }
    /// <summary>请求把这条词条切到编辑态（点选或从悬停提示点进来）。</summary>
    public override void _GuiInput(InputEvent @event)
    {
        if(@event.IsActionPressed("select"))
        {
            NGame.Instance.SetSelectedSymbolSlot(this);
        }
    }
    /// <summary>进入编辑态：输入框可编辑并取得焦点，同时让词典面板显示包含关系。</summary>
    public override void Active()
    {
        base.Active();
        initalRealText=Real.Text;
        Real.Editable=true;
        Real.GrabFocus();
        PlayerDictionary.SetActiveEntry(this);
    }
    /// <summary>
    /// 退出编辑态并把改动落到翻译表：文本空或笔画被删光就删除该词条，
    /// 否则按新名字登记；"新增条目"提交后补一个新的空白条目。
    /// </summary>
    public override void Deactive()
    {
        base.Deactive();
        Real.ReleaseFocus();
        Real.Editable=false;
        if(Real.Text.Length==0||Symbol.Spell.Angles.Count==0)
        {
            if(!IsIncreaser)
            {
                QueueFree();
                PlayerDictionary.SetActiveEntry(null);
                PlayerDictionary.Translation.RemoveReal(initalRealText);
            }
        }
        else
        {
            if(IsIncreaser)
            {
                IsIncreaser=false;
                PlayerDictionary.AddIncreaser();
            }
            Name=Real.Text;
            PlayerDictionary.Translation.RegisterRealToGame(Real.Text,Symbol.Spell.AsString());
        }
    }

    /// <summary>文本框回车提交：等同于结束编辑。</summary>
    public void OnRealSubmitted(string text)
    {
        if(Real.HasFocus()) NGame.Instance.SetSelectedSymbolSlot(null);
    }
    /// <summary>文本框失焦：等同于结束编辑。</summary>
    public void OnRealFocusExited()
    {
        NGame.Instance.SetSelectedSymbolSlot(null);
    }
}
