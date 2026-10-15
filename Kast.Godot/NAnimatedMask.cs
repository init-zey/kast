using System.Threading.Tasks;
using Godot;

public partial class NAnimatedMask : ColorRect
{
    public ShaderMaterial ShaderMaterial;
    public static NAnimatedMask Instance;
    public override void _Ready()
    {
        ShaderMaterial= (ShaderMaterial)Material;
        Instance=this;
    }
    public async Task Play(Texture mask,Vector2 p0,float v0,float v1,float timeSeconds)
    {
        ShaderMaterial.SetShaderParameter("mask",mask);
        ShaderMaterial.SetShaderParameter("p0",p0);
        var tw=CreateTween();
        tw.TweenMethod(Callable.From((float t) =>
        {
            ShaderMaterial.SetShaderParameter("val",t);
        }),v0,v1,timeSeconds);
        tw.TweenCallback(Callable.From(()=>Visible=false));
        Visible=true;
        await ToSignal(tw,Tween.SignalName.Finished);
    }
}
