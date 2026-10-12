using Godot;

/// <summary>
/// 主界面上"显示"出来的词形：出现时是全白，随后逐帧淡出，淡完自动隐藏。
/// </summary>
/// <remarks>
/// 由 <c>display</c> 动词驱动（见 <c>KastAlphabet.Verbs.cs</c>）：
/// 每次显示时把 <c>Modulate</c> 重置为不透明白色，这里的淡出就从头开始。
/// </remarks>
public partial class NDisplaySpellSymbol : NSpellSymbol
{
    /// <summary>按帧衰减不透明度；淡到接近 0 就隐藏，避免继续无谓重绘。</summary>
    public override void _PhysicsProcess(double d)
    {
        var delta=(float)d;
        if(Modulate.A<delta)
        {
            Visible=false;
        }
        else
        {
            Modulate=new Color(Modulate.R,Modulate.G,Modulate.B,Modulate.A-delta);
        }
    }
    /// <summary>初始为不显示的空词形，等 <c>display</c> 填内容并点亮。</summary>
    public override void _Ready()
    {
        base._Ready();
        SetSpell(new()
        {
            FirstDir=' ',
            Angles=new()
        });
    }

}
