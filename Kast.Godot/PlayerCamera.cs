using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Kast.Godot;

/// <summary>
/// 第三人称环绕相机，也是唯一的控制端：操纵对象是任意一个 NObject（<see cref="Player"/>）。
/// 移动、动画、选中、施法等游戏输入都在这里处理；咒语槽数据本身存在施法者（NCaster）上。
/// </summary>
public partial class PlayerCamera : Node3D
{
    [Signal]
    public delegate void AimChangedEventHandler(NObject aim);

    /// <summary>被操纵的对象。相机跟随它，输入作用在它身上。</summary>
    public NObject Player;

    [Export]
    public Camera3D Camera;
    [Export]
    public SpringArm3D SpringArm;

    /// <summary>鼠标射线最大长度（米）。长度 0 会让 Godot 收到 to==from 的零长度射线，永远打不中，所以这里兜底成 1000。</summary>
    [Export]
    public float AimRayLength;
    /// <summary>鼠标射线检测的碰撞层。</summary>
    [Export(PropertyHint.Layers3DPhysics)]
    public uint AimCollisionMask=1;

    /// <summary>未瞄准时恢复的轮廓色。</summary>
    [Export]
    public Color OutlineColor=Colors.Transparent;
    /// <summary>被瞄准时高亮的轮廓色。</summary>
    [Export]
    public Color AimColor=Colors.White;
    [Export]
    public Color SelectedColor=Colors.Yellow;
    [Export]
    public Color JustSelectedColor=Colors.Orange;

    /// <summary>当前被鼠标瞄准的 NObject，没瞄到为 null。</summary>
    private NObject Aim;
    public List<NObject> Selection=new();
    public NObject SelectionHead() => Selection.LastOrDefault();
    /// <summary>鼠标射线的命中点，没瞄到时是射线终点。</summary>
    public Vector3 AimPoint{get;private set;}

    [Export]
    public StringName YawLeftAction = "cam_left";
    [Export]
    public StringName YawRightAction = "cam_right";

    [Export]
    public StringName RadiusInAction = "cam_up";

    [Export]
    public StringName RadiusOutAction = "cam_down";

    [Export]
    public StringName SelectAction = "select";
    [Export]
    public StringName DeselectAction = "deselect";
    [Export]
    public StringName MultiSelectAction = "multiselect";
    [Export]
    public StringName CastAction = "cast";

    [Export]
    public float YawSpeed = 1.5f;

    [Export]
    public float RadiusSpeed = 4f;

    [Export]
    public float Radius = 2f;

    [Export]
    public float MinRadius = 0.25f;

    [Export]
    public float MaxRadius = 10f;

    [Export]
    public float Yaw;
    [Export]
    public float Pitch;
    [Export]
    public float YawEaseSpeed = 12f;
    [Export]
    public float RadiusEaseSpeed = 12f;
    public float TargetYaw;
    public float TargetRadius;

    // ---- 被操纵对象的运动参数 ----
    [Export] public float Speed = 3f;
    [Export] public float JumpHeight = 0.4f;
    [Export] public StringName JumpAction = "ui_accept";
    [Export] public float Ease = 12f;

    [Export] public StringName MoveForwardAction = "move_forward";
    [Export] public StringName MoveBackAction = "move_back";
    [Export] public StringName MoveLeftAction = "move_left";
    [Export] public StringName MoveRightAction = "move_right";

    float gravity;
    AnimationNodeStateMachinePlayback Playback;
    private float LastInputYaw;


    public bool IsEasing=>!Mathf.IsEqualApprox(Yaw,TargetYaw)||!Mathf.IsEqualApprox(Radius,TargetRadius);

    public override void _Ready()
    {
        SpringArm??=GetNodeOrNull<SpringArm3D>("SpringArm3D");
        gravity=(float)ProjectSettings.GetSetting("physics/3d/default_gravity",9.8f);
        Snap();
        ApplyCameraTransform();
    }

    public void CameraUpdate(double delta)
    {
        var dt=(float)delta;

        float inputRotation;
        if(GetViewport().IsInputHandled())
        {
            inputRotation=0;
        }
        else
        {
            inputRotation=Input.GetAxis(YawRightAction,YawLeftAction);
        }
        TargetYaw-=inputRotation*YawSpeed*dt;
        TargetRadius=Mathf.Clamp(TargetRadius-Input.GetAxis(RadiusOutAction,RadiusInAction)*RadiusSpeed*dt,MinRadius,MaxRadius);

        Yaw=Mathf.Lerp(Yaw,TargetYaw,EaseWeight(YawEaseSpeed,dt));
        Radius=Mathf.Lerp(Radius,TargetRadius,EaseWeight(RadiusEaseSpeed,dt));

        Follow();
        ApplyCameraTransform();
        UpdateAim();
        HandleSelectionInput();
    }

    /// <summary>驱动被操纵对象的刚体。刚体取对象的 Object 根节点。</summary>
    public override void _PhysicsProcess(double delta)
    {
        if(Player==null) return;
        var dt=(float)delta;

        var input=GetViewport().IsInputHandled()?new():Input.GetVector(MoveLeftAction,MoveRightAction,MoveBackAction,MoveForwardAction);

        // 移动方向基于相机自身朝向（压平到水平面）
        var forward=new Vector3(Basis.Z.X,0f,Basis.Z.Z).Normalized();
        var right=new Vector3(Basis.X.X,0f,Basis.X.Z).Normalized();
        var target=(right * input.X + forward * -input.Y) * Speed;

        var velocity=Player.LinearVelocity;
        var horizontal=new Vector3(velocity.X,0f,velocity.Z).Lerp(target,1f - Mathf.Exp(-Ease * dt));
        var vertical=velocity.Y;
        if(Player.Type is IJumpable jumpable && jumpable.OnFloor())
        {
            vertical=Input.IsActionJustPressed(JumpAction)
                ? Mathf.Sqrt(2f * gravity * JumpHeight) // v = sqrt(2gh)
                : vertical;
        }

        Player.LinearVelocity=new Vector3(horizontal.X,vertical,horizontal.Z);

        if(input.LengthSquared()>0)
        {
            Player.Type.PlayAnimation("walk");
            LastInputYaw=Mathf.Atan2(-target.X,-target.Z);
        }
        else
        {
            Player.Type.PlayAnimation("stop");
        }
        // 转向移动方向
        // var error=Mathf.Wrap(targetYaw-Body.Rotation.Y,-Mathf.Pi,Mathf.Pi);
        // const float TurnSpeed=8f;
        // var desired=Mathf.Clamp(error*TurnSpeed,-TurnSpeed,TurnSpeed);
        // Body.ApplyTorqueImpulse(new Vector3(0f,(desired-Body.AngularVelocity.Y)*Mathf.Max(Body.Inertia.Y,0.01f),0f));
        Player.Rotation=new(0,Mathf.LerpAngle(Player.Rotation.Y,LastInputYaw,0.3f),0);
    }

    /// <summary>跟随被操纵对象（只同步位置，不动相机朝向）。</summary>
    private void Follow()
    {
        if(Player!=null)
        {
            GlobalPosition=Player.GlobalPosition;
        }
    }

    /// <summary>选中 / 取消选中 / 施法。施法者取被操纵对象上挂的 NCaster。</summary>
    private void HandleSelectionInput()
    {
        if(Input.IsActionJustPressed(SelectAction))
        {
            if(!Input.IsActionPressed(MultiSelectAction))
            {
                SelectionClear();
            }
            SelectionAdd();
        }
        else if(Input.IsActionJustPressed(DeselectAction))
        {
            SelectionClear();
        }
        else if(Player?.Type is NCaster && Input.IsActionPressed(CastAction))
        {
            NGame.Instance.SpellSlots.CastCurrent(Selection);
        }
    }

    /// <summary>鼠标位置投一条射线，取命中碰撞体的父节点作为被瞄准的 NObject。</summary>
    public void UpdateAim()
    {
        var mouse=GetViewport().GetMousePosition();
        var from=Camera.ProjectRayOrigin(mouse);
        var length=AimRayLength>0f?AimRayLength:1000f;
        var to=from+Camera.ProjectRayNormal(mouse)*length;

        var query=PhysicsRayQueryParameters3D.Create(from,to,AimCollisionMask);
        var hit=GetWorld3D().DirectSpaceState.IntersectRay(query);

        var collider=hit.Count>0?((Node3D)hit["collider"]):null;
        NObject aim=null;
        if(collider is NObject aimObj)
        {
            if(aimObj==Aim) return;
            aim=aimObj;
        }
        AimPoint=hit.Count>0?hit["position"].AsVector3():to;
        if(Aim!=null)
        {
            Aim.Type?.SetOutlineColor(SelectionHead() == Aim ? JustSelectedColor : (Selection.Contains(Aim) ? SelectedColor : OutlineColor));
        }
        Aim =aim;
        if(Aim!=null&&!Selection.Contains(Aim))
        {
            Aim.Type?.SetOutlineColor(AimColor);
        }
        EmitSignal(SignalName.AimChanged,aim);
    }
    public void ApplyCameraTransform()
    {
        Rotation=new(Pitch,Yaw,0);
        if(SpringArm!=null)
        {
            SpringArm.SpringLength=Radius;
        }
    }

    /// <summary>清掉缓动，立刻把当前值对齐到目标值（初始化、传送、切镜头时用）。</summary>
    public void Snap()
    {
        TargetYaw=Yaw;
        TargetRadius=Mathf.Clamp(Radius,MinRadius,MaxRadius);
    }

    /// <summary>与帧率无关的缓动权重：dt 越大权重越高，等价于固定时间常数 1/speed 的指数逼近。</summary>
    private static float EaseWeight(float speed,float dt)
    {
        if(speed<=0f||dt<=0f)
        {
            return 1f;
        }
        return 1f-Mathf.Exp(-speed*dt);
    }

    public void SelectionAdd()
    {
        if(Aim==null) return;
        if(SelectionHead()!=null)
        {
            SelectionHead().Type?.SetOutlineColor(SelectedColor);
        }
        Selection.Add(Aim);
        Aim.Type?.SetOutlineColor(JustSelectedColor);
    }
    public void SelectionRemove()
    {
        if(Aim==null) return;
        Aim.Type?.SetOutlineColor(AimColor);
        Selection.Remove(Aim);
    }
    public void SelectionClear()
    {
        foreach(var selectedItem in Selection)
        {
            selectedItem.Type?.SetOutlineColor(OutlineColor);
        }
        Selection.Clear();
        if(Aim==null) return;
        Aim.Type?.SetOutlineColor(AimColor);
    }
}
