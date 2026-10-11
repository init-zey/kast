using Godot;

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
    int lastSlot=-1;
    readonly string[] lastSlots=new string[9];

    public override void _Ready()
    {
        Symbols=new NSpellSymbol[9];
        for(int i=0;i<9;i++)
        {
            Symbols[i]=CreateSlot(i+1);
        }
        Refresh();
    }

    private NSpellSymbol CreateSlot(int number)
    {
        var size=new Vector2(SlotSize,SlotSize);
        var slot=new PanelContainer
        {
            CustomMinimumSize=size,
            TooltipText=$"{number}",
        };
        var style=new StyleBoxFlat { BgColor=new Color(0f,0f,0f,0.35f) };
        style.SetCornerRadiusAll(6);
        style.SetBorderWidthAll(2);
        style.BorderColor=new Color(1f,1f,1f,0.15f);
        slot.AddThemeStyleboxOverride("panel",style);

        var symbol=new NSpellSymbol
        {
            CustomMinimumSize=size,
            MouseFilter=MouseFilterEnum.Ignore,
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

    public override void _Process(double delta)
    {
        var caster=NGame.ActiveCaster;
        if(caster==null)
        {
            return;
        }
        if(caster.Slot==lastSlot&&SameSlots(caster))
        {
            return;
        }
        Refresh();
    }

    private bool SameSlots(NCaster caster)
    {
        for(int i=0;i<9;i++)
        {
            if(caster.Slots[i]!=lastSlots[i])
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>按当前施法者的九个槽位与选中项重画。</summary>
    public void Refresh()
    {
        var caster=NGame.ActiveCaster;
        if(caster==null||Symbols==null)
        {
            return;
        }
        for(int i=0;i<9;i++)
        {
            var content=caster.Slots[i];
            lastSlots[i]=content;
            var symbol=Symbols[i];
            // 每次都构造新数组：解析会枚举它，直接存 IEnumerable 会被读空
            symbol.SetSpell(string.IsNullOrEmpty(content)?null:[..content]);
            symbol.Modulate=i==caster.Slot?ActiveColor:SlotColor;

            var slot=symbol.GetParent<PanelContainer>();
            var active=i==caster.Slot;
            slot.Modulate=string.IsNullOrEmpty(content)?EmptyColor:Colors.White;
            var style=(StyleBoxFlat)slot.GetThemeStylebox("panel");
            style.BorderColor=active?new Color(ActiveColor.R,ActiveColor.G,ActiveColor.B,0.9f):new Color(1f,1f,1f,0.15f);
            style.BgColor=active?ActiveFill:new Color(0f,0f,0f,0.35f);
            slot.TooltipText=$"{i+1}{(string.IsNullOrEmpty(content)?"":$"  {content}")}";
        }
        lastSlot=caster.Slot;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        var caster=NGame.ActiveCaster;
        if(caster==null)
        {
            return;
        }
        if(@event is InputEventKey key&&key.Pressed&&!key.Echo)
        {
            var index=(int)key.Keycode-((int)Key.Key1);
            if(index>=0&&index<9)
            {
                caster.SelectSlot(index);
                GetViewport().SetInputAsHandled();
            }
        }
        else if(@event is InputEventMouseButton wheel&&wheel.Pressed)
        {
            if(wheel.ButtonIndex==MouseButton.WheelUp)
            {
                caster.CycleSlot(-1);
                GetViewport().SetInputAsHandled();
            }
            else if(wheel.ButtonIndex==MouseButton.WheelDown)
            {
                caster.CycleSlot(1);
                GetViewport().SetInputAsHandled();
            }
        }
    }
}
