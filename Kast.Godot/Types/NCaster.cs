using Godot;
using System.Collections.Generic;
using Kast.Godot;
using Kast.Core;

/// <summary>
/// 施法者：持有一份自己的咒语槽数据。输入由 PlayerCamera 处理，这里只负责"存咒语 + 放咒语"。
/// </summary>
public partial class NCaster : NObjectType3D
{
    /// <summary>九个快捷槽里的咒语输入（null = 空槽）。</summary>
    public string[] Slots=new string[9];
    /// <summary>当前选中的槽位，0..8。</summary>
    public int Slot=0;
    /// <summary>当前槽解析出来的候补咒语。</summary>
    public Word[] Spells=[];

    [Export] public float Speed = 5f;
    [Export] public float JumpHeight = 0.4f;
    [Export] public StringName JumpAction = "ui_accept";
    [Export] public float Ease = 12f;

    [Export] public StringName MoveForwardAction = "move_forward";
    [Export] public StringName MoveBackAction = "move_back";
    [Export] public StringName MoveLeftAction = "move_left";
    [Export] public StringName MoveRightAction = "move_right";

    [Export] public AnimationTree AnimationTree;
    public PlayerCamera camera;
    private float gravity;
    private AnimationNodeStateMachinePlayback Playback;

    public override void NodeReady()
    {
        gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity",9.8f);
        Object.GravityScale = 0f;
        Object.AxisLockAngularX = true;
        Object.AxisLockAngularZ = true;
        Playback=(AnimationNodeStateMachinePlayback)AnimationTree.Get("parameters/playback");
        EvaluateSlot();
    }

    public override void _PhysicsProcess(double delta)
    {
        if(camera==null) return;
        var dt = (float)delta;

        var input = Input.GetVector(MoveLeftAction,MoveRightAction,MoveBackAction,MoveForwardAction);
        if(input.LengthSquared()>0)
        {
            Playback.Travel("walk");
        }
        else
        {
            Playback.Travel("stop");
        }
        var forward = new Vector3(camera.Basis.Z.X,0f,camera.Basis.Z.Z).Normalized();
        var right = new Vector3(camera.Basis.X.X,0f,camera.Basis.X.Z).Normalized();
        var target = (right * input.X + forward * -input.Y) * Speed;

        var velocity = Object.LinearVelocity;
        var horizontal = new Vector3(velocity.X,0f,velocity.Z).Lerp(target,1f - Mathf.Exp(-Ease * dt));

        var vertical = velocity.Y;
        if (OnFloor())
        {
            vertical = Input.IsActionJustPressed(JumpAction)
                ? Mathf.Sqrt(2f * gravity * JumpHeight) // v = sqrt(2gh)
                : Mathf.Max(vertical,0f);
        }
        else
        {
            vertical -= gravity * dt;
        }

        Object.LinearVelocity = new Vector3(horizontal.X,vertical,horizontal.Z);
        var targetYaw=Mathf.Atan2(-horizontal.X,-horizontal.Z);
        var error=Mathf.Wrap(targetYaw-Object.Rotation.Y,-Mathf.Pi,Mathf.Pi);
        const float TurnSpeed=8f;
        var desired=Mathf.Clamp(error*TurnSpeed,-TurnSpeed,TurnSpeed);
        Object.ApplyTorqueImpulse(new Vector3(0f,(desired-Object.AngularVelocity.Y)*Mathf.Max(Object.Inertia.Y,0.01f),0f));
    }

    /// <summary>把输入编译进槽位：优先填第一个空槽，都满了就替换当前选中槽。返回填入的槽位下标，失败返回 -1。</summary>
    public int SetSpellInput(string input)
    {
        if (input==null || input.Trim().Length==0)
        {
            // 空输入只是取消当前施法，不清掉已经存好的槽
            ClearSlot();
            return -1;
        }
        var idx=System.Array.IndexOf(Slots,null);
        if(idx<0)
        {
            idx=Slot;
        }
        var alphabet=NGame.Instance.ProgressAlphabet;
        if(alphabet.Parse(alphabet.Tokenize(input)).Count==0)
        {
            NGame.Instance.SetSpellState("spell_state.wrong");
            return -1;
        }
        Slots[idx]=input;
        Slot=idx;
        EvaluateSlot();
        return idx;
    }

    /// <summary>清空指定槽位，不传就清当前槽。返回是否真的清掉了东西。</summary>
    public bool ClearSlot(int? index=null)
    {
        var i=index??Slot;
        if(Slots[i]==null)
        {
            return false;
        }
        Slots[i]=null;
        if(i==Slot)
        {
            EvaluateSlot();
        }
        return true;
    }

    /// <summary>切换当前槽位（滚轮/数字键都走这里），会环绕。</summary>
    public void SelectSlot(int index)
    {
        Slot=Mathf.PosMod(index,Slots.Length);
        EvaluateSlot();
    }

    /// <summary>滚轮切换：delta 为正往下一个槽。</summary>
    public void CycleSlot(int delta)
    {
        SelectSlot(Slot+delta);
    }

    /// <summary>按当前槽里的输入重新解析出候补咒语，并刷新状态文字。</summary>
    public void EvaluateSlot()
    {
        var input=Slots[Slot];
        if(string.IsNullOrEmpty(input))
        {
            Spells=[];
            NGame.Instance.SetSpellState("spell_state.empty");
            return;
        }
        var alphabet=NGame.Instance.ProgressAlphabet;
        Spells=[..alphabet.Parse(alphabet.Tokenize(input))];
        NGame.Instance.SetSpellState(Spells.Length switch
        {
            0=>"spell_state.wrong",
            >1=>"spell_state.ambiguity",
            _=>"spell_state.ready",
        });
    }

    /// <summary>施放当前选中的槽。</summary>
    public bool CastCurrent(List<NObject> targets=null)
    {
        return NGame.Instance.CastWords(Spells,Object,targets);
    }

    /// <summary>施放指定槽（0..8）。越界、空槽或解析不出咒语时什么都不做，返回是否真的施放了。</summary>
    public bool CastSlot(int index,List<NObject> targets=null)
    {
        if(index<0||index>=Slots.Length) return false;
        var input=Slots[index];
        if(string.IsNullOrEmpty(input)) return false;
        var alphabet=NGame.Instance.ProgressAlphabet;
        var spells=alphabet.Parse(alphabet.Tokenize(input));
        if(spells.Count==0) return false;
        return NGame.Instance.CastWords([..spells],Object,targets);
    }

    private bool OnFloor()
    {
        var query = PhysicsRayQueryParameters3D.Create(
            Object.GlobalPosition + Vector3.Up * 0.5f,
            Object.GlobalPosition + Vector3.Down * 0.5f,
            Object.CollisionMask,
            new Godot.Collections.Array<Rid> { Object.GetRid() });
        return Object.GetWorld3D().DirectSpaceState.IntersectRay(query).Count > 0;
    }
}
