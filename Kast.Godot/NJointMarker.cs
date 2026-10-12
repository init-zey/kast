using Godot;

/// <summary>
/// 关卡场景里标记"此处应当生成一个关节"的空节点：本身不参与物理，
/// 由关卡装配逻辑读取它的位置与 <see cref="JointType"/> 来连接相邻刚体。
/// </summary>
/// <remarks>
/// 继承 <see cref="Marker3D"/> 只是为了在编辑器的 3D 视图里可见、可拖动，
/// 真正的关节参数（两个连接对象、锚点）在运行时才确定。
/// </remarks>
[GlobalClass]
public partial class NJointMarker : Marker3D
{
    /// <summary>要生成的关节种类。</summary>
    [Export] public JointType JointType;
}

/// <summary>可生成的关节种类，对应 Godot 的两种 <c>Joint3D</c>。</summary>
public enum JointType
{
    /// <summary>铰链关节（<c>PinJoint3D</c>）：只保留一个旋转自由度。</summary>
    Pin,
    /// <summary>六自由度关节（<c>Generic6DOFJoint3D</c>）：可分别限制各轴。</summary>
    SixD,
}