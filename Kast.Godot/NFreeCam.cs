using Godot;

public partial class NFreeCam : Camera3D
{
    [Export] public float MoveSpeed = 8.0f;          // 基础移动速度
    [Export] public float MouseSensitivity = 0.003f; // 鼠标灵敏度
    [Export] public float Acceleration = 12.0f;      // 加减速平滑度
    [Export] public float FastMultiplier = 2.5f;     // Shift 加速倍率
    [Export] public float SlowMultiplier = 0.3f;     // Alt 减速倍率
    [Export] public bool RequireRightMouse = true;   // 是否必须按住右键才能旋转视角

    private Vector2 _mouseDelta = Vector2.Zero;
    private float _pitch = 0.0f;      // 垂直角度（弧度）
    private float _yaw = 0.0f;        // 水平角度（弧度）
    private Vector3 _velocity = Vector3.Zero;
    private float _speedMultiplier = 1.0f;

    public override void _Ready()
    {
        // 初始化当前朝向，避免启动时视角跳变
        var euler = Rotation;
        _pitch = euler.X;
        _yaw = euler.Y;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // 鼠标移动 → 记录偏移量
        if (@event is InputEventMouseMotion motion)
        {
            if (!RequireRightMouse || Input.MouseMode == Input.MouseModeEnum.Captured)
                _mouseDelta += motion.Relative;
        }

        // 右键按下/松开 → 捕获/释放鼠标
        if (@event is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Right)
            {
                if (button.Pressed)
                    Input.MouseMode = Input.MouseModeEnum.Captured;
                else
                    Input.MouseMode = Input.MouseModeEnum.Visible;
            }

            // 滚轮调速
            if (button.Pressed)
            {
                if (button.ButtonIndex == MouseButton.WheelUp)
                    _speedMultiplier = Mathf.Clamp(_speedMultiplier * 1.15f, 0.1f, 20.0f);
                else if (button.ButtonIndex == MouseButton.WheelDown)
                    _speedMultiplier = Mathf.Clamp(_speedMultiplier / 1.15f, 0.1f, 20.0f);
            }
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        UpdateRotation();
        UpdateMovement(dt);
    }

    private void UpdateRotation()
    {
        if (_mouseDelta == Vector2.Zero) return;

        float deltaYaw = -_mouseDelta.X * MouseSensitivity;
        float deltaPitch = -_mouseDelta.Y * MouseSensitivity;

        _yaw += deltaYaw;
        _pitch = Mathf.Clamp(_pitch + deltaPitch, -Mathf.Pi / 2.0f, Mathf.Pi / 2.0f);

        // 使用 YXZ 欧拉顺序，避免万向节问题
        Rotation = new Vector3(_pitch, _yaw, 0.0f);

        _mouseDelta = Vector2.Zero;
    }

    private void UpdateMovement(float dt)
    {
        // ====== 硬编码 WASDQE 按键检测 ======
        float inputX = 0.0f; // 左右（A/D）
        float inputY = 0.0f; // 上下（Q/E）
        float inputZ = 0.0f; // 前后（W/S）

        if (Input.IsPhysicalKeyPressed(Key.W)) inputZ -= 1.0f;
        if (Input.IsPhysicalKeyPressed(Key.S)) inputZ += 1.0f;
        if (Input.IsPhysicalKeyPressed(Key.A)) inputX -= 1.0f;
        if (Input.IsPhysicalKeyPressed(Key.D)) inputX += 1.0f;
        if (Input.IsPhysicalKeyPressed(Key.Q)) inputY -= 1.0f;
        if (Input.IsPhysicalKeyPressed(Key.E)) inputY += 1.0f;
        // ====================================

        // 以相机自身坐标系构建移动方向
        var basis = GlobalTransform.Basis;
        var direction = (basis * new Vector3(inputX, 0, inputZ)) + new Vector3(0, inputY, 0);
        direction = direction.Normalized();

        // 计算最终速度倍率
        float speed = MoveSpeed * _speedMultiplier;

        if (Input.IsPhysicalKeyPressed(Key.Shift)) speed *= FastMultiplier;
        if (Input.IsPhysicalKeyPressed(Key.Alt))   speed *= SlowMultiplier;

        // 平滑加减速
        var targetVelocity = direction * speed;
        _velocity = _velocity.Lerp(targetVelocity, Acceleration * dt);

        GlobalPosition += _velocity * dt;
    }
}