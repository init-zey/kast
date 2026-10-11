using Godot;
using Kast.Godot;

public partial class NDictionary : ScrollContainer
{
    [Export] public Control Container;
    public void Open()
    {
        Visible=true;
        Helper.ClearChild(Container);
        foreach(var word in NGame.Instance.ProgressAlphabet.GetKeys())
        {
            Helper.LoadScene<NDictEntry>("DictEntry", (n) =>
            {
                Container.AddChild(n);
                n.Init(word);
            });
        }
    }
    public override void _UnhandledInput(InputEvent @event)
    {
        if(@event.IsActionPressed("dict_toggle"))
        {
            if(Visible)
            {
                Visible=false;
            }
            else
            {
                Open();
            }
        }
    }

}
