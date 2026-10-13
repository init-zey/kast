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
    /// <summary>本关卡的玩家对象（由 <see cref="Spawn"/> 生成的施法者）。</summary>
    public NObject Player;
    /// <summary>本关卡的相机，也是输入控制端。</summary>
    public PlayerCamera Camera;
    /// <summary>
    /// 生成一个刚体并挂到本关卡下，用 <paramref name="type"/> 作为它的外观与物理外形。
    /// 质量由 <see cref="NObject.SetType"/> 按 <c>Density × Scale²</c> 算出，不在这里给。
    /// </summary>
    public NObject Spawn(NObjectType type,Vector3 position,Quaternion quaternion)
    {
        var obj = new NObject
        {
            Position = position,
            Quaternion = quaternion
        };
        AddChild(obj);
        obj.SetType(type);
        return obj;
    }
    /// <summary>
    /// 取本关卡的出生点：场景里名为 <c>CasterSpanwer</c> 的节点（<c>%</c> 唯一名）。
    /// 没放这个节点的关卡（例如 <c>island.tscn</c>）退回用关卡原点，不视为错误——
    /// <c>GetNode</c> 找不到会直接抛异常，那样整个关卡都加载不了。
    /// </summary>
    Vector3 SpawnPosition()
    {
        return GetNodeOrNull<Node3D>("%CasterSpanwer")?.GlobalPosition ?? GlobalPosition;
    }

    /// <summary>
    /// 按模板场景造一个类型实例，可选地覆盖密度与缩放。
    /// 关卡里摆好的物体由 <see cref="ScanAndSpawn"/> 直接复用，不需要走这里。
    /// </summary>
    public NObjectType CreateType(float? densityOverride,Vector3? scaleOverride)
    {
        var type=Helper.LoadScene<NObjectType>("Types/NCaster");
        if(densityOverride.HasValue)
        {
            type.Density=densityOverride.Value;
        }
        if(scaleOverride.HasValue)
        {
            type.Scale=scaleOverride.Value;
        }
        return type;
    }
    /// <summary>装上相机与玩家，并把施法者交给咒语槽 UI。</summary>
    public override void _Ready()
    {
        // 先实体化关卡里的物体，再放玩家：玩家落到已就位的地形/物体上
        ScanAndSpawn(this);
        Camera = Helper.LoadScene<PlayerCamera>("PlayerCamera");
        AddChild(Camera);
        Player = Spawn(CreateType(0.8f, Vector3.One*1f), SpawnPosition(), Quaternion.Identity);
        var caster=(NCaster)Player.Type;
        caster.Spells=SaveSystem.Current.CasterSpells;
        Camera.Player = Player;
        NGame.Instance.ActiveCamera = Camera;
        NGame.Instance.SpellSlots.SetCaster(caster);
    }

    /// <summary>
    /// 深度优先扫描子树，把关卡里声明式的每个 <see cref="NObjectType"/> 就地转成一个 <see cref="NObject"/>：
    /// 类型节点被移交给新刚体（见 <see cref="NObject.SetType"/>），因此不再是场景节点的子级。
    /// </summary>
    private void ScanAndSpawn(Node root)
    {
        foreach(var child in root.GetChildren())
        {
            ScanAndSpawn(child);
            if(child is NObjectType objType)
            {
                Spawn(objType,objType.GlobalPosition,objType.Quaternion);
            }
        }
    }
}