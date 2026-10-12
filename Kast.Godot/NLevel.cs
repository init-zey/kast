using System.Collections.Generic;
using Godot;

namespace Kast.Godot;

/// <summary>
/// 一个关卡：场景里摆好的 <see cref="NObjectType"/> 在进入场景树时被"实体化"成 <see cref="NObject"/>
/// 刚体，并在 <c>_Ready</c> 里装上玩家、相机与咒语槽。
/// </summary>
/// <remarks>
/// 关卡场景里只放声明式的 <see cref="NObjectType"/>（可带 Scale 与 Density），
/// 由 <see cref="ScanAndSpawn"/> 统一替换成运行时刚体——这样关卡数据与物理参数不必重复写两遍。
/// </remarks>
public partial class NLevel : Node3D
{
    /// <summary>本关卡里的全部刚体。</summary>
    public HashSet<NObject> Objects=new();
    /// <summary>本关卡的玩家对象（由 <see cref="Spawn"/> 生成的施法者）。</summary>
    public NObject Player;
    /// <summary>本关卡的相机，也是输入控制端。</summary>
    public PlayerCamera Camera;
    /// <summary>
    /// 生成一个刚体并挂到本关卡下。质量直接由调用方给出；
    /// <paramref name="transform"/> 会连同缩放一起被继承（质量已按尺寸换算过，见 <see cref="ScanAndSpawn"/>）。
    /// </summary>
    public NObject Spawn(string typeid,Transform3D transform,float mass)
    {
        var obj=new NObject();
        Objects.Add(obj);
        AddChild(obj);
        obj.Transform=transform;
        obj.SetType(typeid);
        obj.SetMass(mass);
        return obj;
    }
    /// <summary>先于 <c>_Ready</c> 把场景里声明式的物体实体化，保证关卡内容在装玩家之前就位。</summary>
    public override void _EnterTree()
    {
        ScanAndSpawn(this);
    }
    /// <summary>装上相机与玩家，并把施法者交给咒语槽 UI。</summary>
    public override void _Ready()
    {
        Camera = Helper.LoadScene<PlayerCamera>("PlayerCamera");
        AddChild(Camera);
        Player = Spawn("NCaster", Transform3D.Identity, 1);
        var caster=(NCaster)Player.Type;
        caster.Spells=SaveSystem.Current.CasterSpells;
        Camera.Player = Player;
        NGame.Instance.ActiveCamera = Camera;
        NGame.Instance.SpellSlots.SetCaster(caster);
    }

    /// <summary>
    /// 深度优先扫描子树，把每个 <see cref="NObjectType"/> 换成运行时的 <see cref="NObject"/>。
    /// 质量按 <c>Density × Scale²</c> 估算（用点积代替逐轴相乘），并把原节点释放掉，
    /// 因此场景里只保留"设计态"，运行态由这里的刚体承担。
    /// </summary>
    private void ScanAndSpawn(Node root)
    {
        foreach(var child in root.GetChildren())
        {
            ScanAndSpawn(child);
            if(child is NObjectType objType)
            {
                var aabbSize=objType.Scale;
                Spawn(objType.GetType().Name,objType.Transform,objType.Density*aabbSize.Dot(aabbSize));
                objType.QueueFree();
            }
        }
    }
}