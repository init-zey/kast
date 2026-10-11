using Godot;
using Kast.Godot;

public partial class NDictEntry : PanelContainer
{
    [Export] NSpellSymbol Symbol;
    [Export] Label Label;
    public void Init(string token)
    {
        Symbol.SetSpell(token);
        Label.Text=NGame.Instance.DB.GetRealWord(token);
    }
}
