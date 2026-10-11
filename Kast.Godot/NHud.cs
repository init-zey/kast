using Godot;

public partial class NHud : Control
{
    [Export] public NSpellInput SpellInput;
    [Export] public NSpellSymbol SpellSymbol;
    [Export] public Label StateLabel;
    public override void _Ready()
    {
        // CompileButton.Pressed += () =>
        // {
        //     if(LineEdit.Text.Length>0)
        //     {
        //         StateLabel.Text=NGame.Instance.SetSpellInput(LineEdit.Text);
        //     }
        //     else
        //     {
        //         StateLabel.Text=NGame.Instance.ClearSpell();
        //     }
        // };
    }

}
