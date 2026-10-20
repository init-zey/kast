using Godot;
using Kast.Godot;

/// <summary>
/// 蜘蛛：在 <see cref="NLivingObject"/>（移动 + 全身布娃娃）之上接管六条腿的 IK / 步态状态机。
/// 全身布娃娃时关闭腿部状态机并停止移动；恢复站立时重建各腿 IK。
/// </summary>
public partial class NSpider : NLivingObject
{
    /// <summary>六足状态机。留空会在 <see cref="OnLivingReady"/> 里自动查找。</summary>
    [Export] public NSpiderLegController Legs;
    /// <summary>六足 IK。留空自动查找。</summary>
    [Export] public JacobianIK3D Ik;
    /// <summary>骨架。留空自动查找。</summary>
    [Export] public Skeleton3D Skeleton;
    /// <summary>可选动画播放器（预留）。</summary>
    [Export] public AnimationPlayer AnimationPlayer;

    protected override void OnLivingReady()
    {
        if (Legs == null) Legs = FindFirst<NSpiderLegController>(this);
        if (Ik == null) Ik = FindFirst<JacobianIK3D>(this);
        if (Skeleton == null) Skeleton = FindFirst<Skeleton3D>(this);
        if (AnimationPlayer == null) AnimationPlayer = FindFirst<AnimationPlayer>(this);

        // 正常情况下 NSpiderLegController 在自己的 _Ready 里已经接好六条腿。
        // 只有当层级被改动导致 target_node 相对路径失效（LegCount == 0）时，才在这里按脚节点名重接。
        if (Legs != null && Legs.LegCount == 0 && Ik != null)
        {
            RewireIkTargets();
            Legs.Rebuild();
        }
    }

    /// <summary>把每个 IK setting 的 target_node 重设为同名的脚节点（按旧路径的最后一段取名字）。</summary>
    void RewireIkTargets()
    {
        int count = Ik.SettingCount;
        for (int i = 0; i < count; i++)
        {
            NodePath old = Ik.GetTargetNode(i);
            int names = old.GetNameCount();
            if (names == 0)
            {
                continue;
            }
            string footName = old.GetName(names - 1);
            Node3D foot = FindByName<Node3D>(this, footName);
            if (foot == null)
            {
                continue;
            }
            Ik.SetTargetNode(i, Ik.GetPathTo(foot));
        }
    }

    static T FindByName<T>(Node root, string name) where T : class
    {
        if (root.Name.ToString() == name && root is T typed)
        {
            return typed;
        }
        foreach (Node child in root.GetChildren())
        {
            T found = FindByName<T>(child, name);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }

    /// <summary>全身布娃娃：同时关/开腿部状态机。</summary>
    public override void SetRagdoll(bool enabled)
    {
        base.SetRagdoll(enabled);
        if (Legs == null)
        {
            return;
        }
        if (enabled)
        {
            Legs.EnableStateMachine = false;
        }
        else
        {
            Legs.EnableStateMachine = true;
            Legs.Rebuild();
        }
    }

    /// <summary>游戏侧的动画入口（PlayerCamera 会调 "walk"/"stop"）。</summary>
    public override void PlayAnimation(string v)
    {
        if (AnimationPlayer != null && AnimationPlayer.HasAnimation(v))
        {
            AnimationPlayer.Play(v);
        }
    }
}
