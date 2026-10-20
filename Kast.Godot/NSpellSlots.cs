using System.Collections.Generic;
using Godot;

namespace Kast.Godot;

/// <summary>九个快捷咒语槽。数字键 1-9 直选，滚轮循环切换。</summary>
public partial class NSpellSlots : Control
{
    [Export] public Control Grid;
    [Export] public Color EmptyColor=new(1f,1f,1f,0.25f);
    [Export(PropertyHint.Range,"8,128,1")] public float SlotSize=67f;

    public NSpellSymbolSlot[] Slots;
    /// <summary>当前选中的槽位，0..8。</summary>
    public int Slot=-1;
    public NCaster Caster;

    private NSpellSymbolSlot CreateSlot(int number,Spell spell)
    {
        var size=new Vector2(SlotSize,SlotSize);

        var symbol=new NSpellSymbol
        {
            CustomMinimumSize=size,
            MouseFilter=MouseFilterEnum.Ignore,
            EdgeWidth=1,
        };
        symbol.SetSpell(spell);
        var slot=new NSpellSymbolSlot
        {
            Symbol=symbol,
            CustomMinimumSize=size,
            Translation=SaveSystem.Current.PlayerTranslation,
            SlotIndex=number-1,
        };
        slot.AddChild(symbol);
        Grid.AddChild(slot);
        return slot;
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
    
    /// <summary>切换当前槽位（滚轮/数字键都走这里），会环绕。</summary>
    public void SelectSlot(int index)
    {
        Slot=Mathf.PosMod(index,Caster.Spells.Length);
        NGame.Instance.SetSelectedSymbolSlot(Slots[Slot]);
    }

    /// <summary>滚轮切换：delta 为正往下一个槽。</summary>
    public void CycleSlot(int delta)
    {
        SelectSlot(Slot+delta);
    }

    /// <summary>施放当前选中的槽。</summary>
    public bool CastCurrent(List<NObject> targets=null)
    {
        return Caster.CastSlot(Slot,targets);
    }
    /// <summary>把施法者的槽位数据交给本 UI：重建九个槽位控件并选中第一个。</summary>
    public void SetCaster(NCaster caster)
    {
        Helper.ClearChild(Grid);
        Caster=caster;
        Slots=new NSpellSymbolSlot[Caster.Spells.Length];
        for(int i=0;i<Caster.Spells.Length;i++)
        {
            Slots[i]=CreateSlot(i+1,caster.Spells[i]);
        }
        if(Slots.Length>0)
        {
            SelectSlot(0);
        }
    }
    // /// <summary>每物理帧按槽里是否已有起笔刷新"空槽"灰显，使画完/清空能立刻反映到界面上。</summary>
    // public void _PhysicsProcess()
    // {
    //     if(Slots==null)
    //     {
    //         return;   // 还没 SetCaster：场景刚加载的头1帧
    //     }
    //     for(int i=0;i<Slots.Length;i++)
    //     {
    //         var slot=Slots[i];
    //         slot.Modulate=slot.Symbol.Spell.HasFirstDir?Colors.White:EmptyColor;
    //     }
    // }
}
