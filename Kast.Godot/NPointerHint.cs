using System.Collections.Generic;
using Godot;

/// <summary>
/// 跟随鼠标的悬停提示浮层：把内容节点挂在鼠标右下角显示，同一时刻可以叠多份。
/// </summary>
/// <remarks>
/// 内容节点由调用方构造后交进来（见 <c>NDictionary.EntryHoverStarted</c>），
/// 本类只管按 id 收纳、定位与销毁。
/// </remarks>
public partial class NPointerHint : PanelContainer
{
    public static NPointerHint Instance;
    /// <summary>已挂上的内容节点，按 id 索引，避免同一目标重复挂载。</summary>
    public Dictionary<string,Node> Nodes=new();
    public override void _Ready()
    {
        Instance=this;
        Visible=false;
    }
    /// <summary>挂上一个内容节点并显示浮层；同一个 id 重复调用不会重复添加。</summary>
    public void AddNode(string id,Node node)
    {
        if(!Nodes.ContainsKey(id))
        {
            Nodes[id]=node;
            AddChild(node);
        }
        Visible=true;
    }
    /// <summary>移除指定 id 的内容节点；全部移除后浮层自动隐藏。</summary>
    public void RemoveNode(string id)
    {
        if(Nodes.TryGetValue(id,out var node))
        {
            Nodes.Remove(id);
            node.QueueFree();
        }
        if(Nodes.Count==0) Visible=false;
    }
    /// <summary>贴在鼠标右下角，留 8 像素偏移避免盖住指针。</summary>
    public override void _Process(double delta)
    {
        if(Visible)
        {
            Position=GetViewport().GetMousePosition()+new Vector2(8,8);
        }
    }

}
