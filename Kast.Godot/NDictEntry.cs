using System.Linq;
using Godot;
using Kast.Godot;

public partial class NDictEntry : PanelContainer
{
    [Export] NSpellSymbol Symbol;
    [Export] Label Label;
    /// <summary>
    /// 展示一个候补词条：<b>整个词条都画出来</b>。
    /// <paramref name="rootRotation"/> 是根旋转（整体朝向），对应 <see cref="Spell.FirstDir"/>；
    /// 词条的字符整体放进 <see cref="Spell.Angles"/>，它们都是相对转角——
    /// 只画剩下的一截会让玩家以为词条就剩那么长，所以这里不做截断。
    /// </summary>
    public void Init(char rootRotation,string token)
    {
        Symbol.SetSpell(new Spell()
        {
            FirstDir=rootRotation,
            Angles=token.ToCharArray().ToList()
        });
        Label.Text=NGame.Instance.DB.GetRealWord(token);
    }
}
