using Godot;
using MessagePack;

namespace Kast.Godot;

/// <summary>
/// 玩家偏好设置（与游戏进度 <see cref="Progress"/> 分开存档）。
/// </summary>
[MessagePackObject]
public class GameSettings
{
    /// <summary>界面语言，例如 "en"、"zh_CN"。</summary>
    [Key(0)]
    public string Locale="en";

    /// <summary>主音量，0..1。</summary>
    [Key(1)]
    public float MasterVolume=1f;

    /// <summary>窗口模式：false = 窗口化，true = 全屏。</summary>
    [Key(2)]
    public bool Fullscreen=false;

    /// <summary>Volatile 表示不参与序列化，仅用于运行时缓存。</summary>
    [IgnoreMember]
    public bool IsNew=true;

    public static GameSettings CreateDefault()
    {
        var locale=TranslationServer.GetLocale();
        if(string.IsNullOrEmpty(locale))
        {
            locale="en";
        }
        return new GameSettings(){Locale=locale};
    }
}
