using System.Collections.Generic;
using System.Linq;
using Godot;
using Kast.Core;

namespace Kast.Godot;

/// <summary>九个快捷咒语槽。数字键 1-9 直选，滚轮循环切换。</summary>
public partial class NSpellSlots : Control
{
    [Export] public Control Grid;
    [Export] public Color SlotColor=Colors.White;
    [Export] public Color EmptyColor=new(1f,1f,1f,0.25f);
    [Export] public Color ActiveColor=Colors.Yellow;
    [Export] public Color ActiveFill=new(1f,1f,1f,0.08f);
    [Export(PropertyHint.Range,"8,128,1")] public float SlotSize=56f;

    NSpellSymbol[] Symbols;
    /// <summary>当前选中的槽位，0..8。</summary>
    public int Slot=-1;
    public NCaster Caster;

    private NSpellSymbol CreateSlot(int number)
    {
        var size=new Vector2(SlotSize,SlotSize);
        var slot=new PanelContainer
        {
            CustomMinimumSize=size,
            TooltipText=$"{number}",
        };
        slot.AddThemeStyleboxOverride("panel", (StyleBox)GD.Load<StyleBoxFlat>("res://Materials/SlotBox.tres").Duplicate());

        var symbol=new NSpellSymbol
        {
            CustomMinimumSize=size,
            MouseFilter=MouseFilterEnum.Ignore,
            EdgeWidth=2
        };
        slot.AddChild(symbol);

        var label=new Label
        {
            Text=$"{number}",
            HorizontalAlignment=HorizontalAlignment.Right,
            VerticalAlignment=VerticalAlignment.Top,
        };
        label.AddThemeFontSizeOverride("font_size",12);
        label.AddThemeColorOverride("font_color",new Color(1f,1f,1f,0.6f));
        label.SetAnchorsPreset(LayoutPreset.TopRight);
        label.MouseFilter=MouseFilterEnum.Ignore;
        slot.AddChild(label);

        Grid.AddChild(slot);
        return symbol;
    }

    /// <summary>按当前施法者的九个槽位与选中项重画。</summary>
    // public void Refresh()
    // {
    //     if(caster==null||Symbols==null)
    //     {
    //         return;
    //     }
    //     for(int i=0;i<9;i++)
    //     {
    //         var content=caster.Slots[i];
    //         lastSlots[i]=content;
    //         var symbol=Symbols[i];
    //         // 每次都构造新数组：解析会枚举它，直接存 IEnumerable 会被读空
    //         symbol.SetSpell(content);
    //         symbol.Modulate=i==caster.Slot?ActiveColor:SlotColor;

    //         var slot=symbol.GetParent<PanelContainer>();
    //         var active=i==caster.Slot;
    //         slot.Modulate=content.HasFirstDir?Colors.White:EmptyColor;
    //         var style=(StyleBoxFlat)slot.GetThemeStylebox("panel");
    //         style.BorderColor=active?new Color(ActiveColor.R,ActiveColor.G,ActiveColor.B,0.9f):new Color(1f,1f,1f,0.15f);
    //         style.BgColor=active?ActiveFill:new Color(0f,0f,0f,0.35f);
    //     }
    //     lastSlot=caster.Slot;
    // }

    public override void _UnhandledInput(InputEvent @event)
    {
        if(Caster==null)
        {
            return;
        }
        if(@event is InputEventKey key&&key.Pressed&&!key.Echo)
        {
            var index=(int)key.Keycode-((int)Key.Key1);
            if(index>=0&&index<9)
            {
                SelectSlot(index);
                GetViewport().SetInputAsHandled();
            }
        }
        else if(@event is InputEventMouseButton wheel&&wheel.Pressed)
        {
            if(wheel.ButtonIndex==MouseButton.WheelUp)
            {
                CycleSlot(-1);
                GetViewport().SetInputAsHandled();
            }
            else if(wheel.ButtonIndex==MouseButton.WheelDown)
            {
                CycleSlot(1);
                GetViewport().SetInputAsHandled();
            }
        }
    }
    
    /// <summary>清空指定槽位，不传就清当前槽。返回是否真的清掉了东西。</summary>

    /// <summary>切换当前槽位（滚轮/数字键都走这里），会环绕。</summary>
    public void SelectSlot(int index)
    {
        if(Slot>-1)
        {
            var prevSymbol=Symbols[Slot];
            prevSymbol.Modulate=SlotColor;
            var prevSlot=prevSymbol.GetParent<PanelContainer>();
            var prevStyle=(StyleBoxFlat)prevSlot.GetThemeStylebox("panel");
            prevStyle.BorderColor=new Color(1f,1f,1f,0.15f);
            prevStyle.BgColor=new Color(0f,0f,0f,0.35f);
        }
        Slot=Mathf.PosMod(index,Caster.Spells.Length);
        var symbol=Symbols[Slot];
        symbol.Modulate=ActiveColor;
        var slot=symbol.GetParent<PanelContainer>();
        var style=(StyleBoxFlat)slot.GetThemeStylebox("panel");
        style.BorderColor=new Color(ActiveColor.R,ActiveColor.G,ActiveColor.B,0.9f);
        style.BgColor=ActiveFill;
    }

    /// <summary>滚轮切换：delta 为正往下一个槽。</summary>
    public void CycleSlot(int delta)
    {
        SelectSlot(Slot+delta);
    }

    /// <summary>按当前槽里的输入重新解析出候补咒语。</summary>

    /// <summary>施放当前选中的槽。</summary>
    public bool CastCurrent(List<NObject> targets=null)
    {
        return Caster.CastSlot(Slot,targets);
    }
    public void SetCaster(NCaster caster)
    {
        Helper.ClearChild(Grid);
        Caster=caster;
        Symbols=new NSpellSymbol[Caster.Spells.Length];
        for(int i=0;i<Caster.Spells.Length;i++)
        {
            var sym=CreateSlot(i+1);
            Symbols[i]=sym;
            sym.SetSpell(caster.Spells[i]);
        }
        if(Symbols.Length>0)
        {
            SelectSlot(0);
        }
    }
    public void _PhysicsProcess()
    {
        for(int i=0;i<Symbols.Length;i++)
        {
            var sym=Symbols[i];
            var slot=sym.GetParent<PanelContainer>();
            slot.Modulate=sym.Spell.HasFirstDir?Colors.White:EmptyColor;
        }
    }
}
