using Godot;

public partial class NSpider : Node3D
{
    [Export] public int LegCount=0;
    [Export] public Vector3 BodySize=new(0.8f,0.6f,1.0f);
    RigidBody3D MainBody;
    RigidBody3D[] LegParts;
    public override void _Ready()
    {
        MainBody=new RigidBody3D();
        AddChild(MainBody);
        MainBody.GlobalTransform=GlobalTransform;
        LegParts=new RigidBody3D[LegCount*2*2];
        for(int i=0;i<LegCount;i++)
        {
            for(int j=0;j<2;j++) // 0==Up, 1==Down
            {
                var left=new RigidBody3D();
                var right=new RigidBody3D();
                var index=(i*2+j)*2;
                LegParts[index]=left;
                LegParts[index+1]=right;
                GetParent().AddChild(left);
                GetParent().AddChild(right);
            }
        }
        BoxCollision(MainBody,BodySize);
    }
    static void BoxCollision(Node3D parent,Vector3 size)
    {
        var box=new BoxShape3D();
        box.Size=size;
        var collisionShape=new CollisionShape3D();
        collisionShape.Shape=box;
        collisionShape.DebugColor=Colors.Red;
        parent.AddChild(collisionShape);
    }
}
