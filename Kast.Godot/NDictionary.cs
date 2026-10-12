using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Kast.Core;
using Kast.Godot;

/// <summary>
/// 词典面板：列出玩家当前持有的全部词条，可改名、删除、新增，按快捷键开关。
/// </summary>
/// <remarks>
/// 编辑在 <see cref="Translation"/> 这份<b>副本</b>上进行，关面板时才整体写回存档
/// （见 <see cref="_UnhandledInput"/>），所以中途放弃修改不会污染真实词表。
/// 词条与分词、预测用的是同一份表，所以词典里看到的词就是能画出来并被接受的词。
/// </remarks>
public partial class NDictionary : Control
{
    /// <summary>条目容器，每次打开都整体重建。</summary>
    [Export] public Control Container;
    /// <summary>展示当前选中词形的大图。</summary>
    [Export] public NSpellSymbol MainSpellSymbol;
    /// <summary>"我包含了哪些词"（当前词形是别人的子串）的说明文本。</summary>
    [Export] public RichTextLabel ContainsInfo;
    /// <summary>"哪些词包含了我"的说明文本。</summary>
    [Export] public RichTextLabel ContainedInfo;
    /// <summary>本面板正在编辑的翻译表副本；关闭时写回 <c>SaveSystem.Current.PlayerTranslation</c>。</summary>
    public GameTranslation Translation=new();
    /// <summary>用当前词表重建并显示面板。</summary>
    public void Open()
    {
        Visible=true;
        SetActiveEntry(null);
        Helper.ClearChild(Container);
        Translation.Copy(SaveSystem.Current.PlayerTranslation);
        foreach(var word in Translation.Tokens)
        {
            Helper.LoadScene<NSpellEntry>("SpellEntry", (n) =>
            {
                Container.AddChild(n);
                n.Translation=Translation;
                n.Init(this,new Spell{FirstDir=' ',Angles=word.ToList()});   // 词典里 ' ' 当根旋转：条目自己慢慢转，不跟任何已画出的笔画对齐
            });
        }
        AddIncreaser();
    }

    /// <summary>
    /// 显示某条词条的详情：大图 + 它与其它词条的包含关系。
    /// 传 null 清空详情区（没有选中条目时）。
    /// </summary>
    public void SetActiveEntry(NSpellEntry entry)
    {
        if(entry==null)
        {
            MainSpellSymbol.SetSpell(null);
            ContainsInfo.Text="";
            ContainedInfo.Text="";
        }
        else
        {
            var entryGame=entry.Symbol.Spell.AsString();
            MainSpellSymbol.SetSpell(entry.Symbol.Spell);
            var contains=new StringBuilder();
            var contained=new StringBuilder();
            foreach(var pair in Translation.ToGame)
            {
                if(pair.Value==entryGame) continue;
                if(pair.Value.Contains(entryGame))
                {
                    contained.Append($" [url={pair.Key}][color=lightblue]{pair.Key}[/color][/url] ");
                }
                else if (entryGame.Contains(pair.Value))
                {
                    contains.Append($" [url={pair.Key}][color=pink]{pair.Key}[/color][/url] ");
                }
            }
            ContainsInfo.Text=NLocalization.Format("info.contains",contains);
            ContainedInfo.Text=NLocalization.Format("info.contained",contained);
        }
    }

    /// <summary>快捷键开/关面板；关闭时把编辑结果写回存档并让快捷键槽重新获得选择。</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if(@event.IsActionPressed("dict_toggle") || Visible&&@event.IsActionPressed("deselect"))
        {
            if(Visible)
            {
                Visible=false;
                NGame.Instance.SelecteSlotFromSlots();
                NGame.Instance.SetTranslation(Translation);
            }
            else
            {
                Open();
            }
        }
        if(Visible)
        {
            GetViewport().SetInputAsHandled();
        }
    }
    /// <summary>面板默认隐藏，由快捷键或 <see cref="Open"/> 打开。</summary>
    public override void _Ready()
    {
        Visible=false;
    }
    /// <summary>按名字选中某条词条（说明文本里的链接点击走这里）。</summary>
    public void OpenEntryByName(string name)
    {
        NGame.Instance.SetSelectedSymbolSlot(GetEntryByName(name));
    }

    private NSpellSymbolSlot GetEntryByName(string name)
    {
        return (NSpellSymbolSlot)Container.GetNode(name);
    }
    /// <summary>鼠标离开某条词条：撤掉它的悬停预览。</summary>
    public void EntryHoverEnded(string name)
    {
        NPointerHint.Instance.RemoveNode(name);
    }
    /// <summary>鼠标悬停在某条词条上：用同一条词形生成一份只读预览挂在鼠标旁。</summary>
    public void EntryHoverStarted(string name)
    {
        var entry=GetEntryByName(name);
        NPointerHint.Instance.AddNode(name, Helper.LoadScene<NSpellEntry>("SpellEntry", (n) =>
        {
            n.Translation=Translation;
            n.Init(null,new Spell(){FirstDir=' ',Angles=entry.Symbol.Spell.Angles});
            n.MouseFilter= MouseFilterEnum.Ignore;
        }));
    }
    /// <summary>在列表末尾补一个空白条目，供玩家画新词并命名。</summary>
    public void AddIncreaser()
    {
        Helper.LoadScene<NSpellEntry>("SpellEntry", (n) =>
        {
            n.IsIncreaser=true;
            n.Translation=Translation;
            n.Init(this,new(){FirstDir='\0',Angles=new()});
            Container.AddChild(n);
        });
    }
}
