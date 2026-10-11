using System;
using Godot;
using MessagePack;

namespace Kast.Godot;

/// <summary>
/// user:// 下的存档与设置读写。进度存档用 MessagePack 序列化 <see cref="Progress"/>。
/// </summary>
public static class SaveSystem
{
    /// <summary>当前存档路径（单存档槽）。</summary>
    public const string SavePath="user://save.sav";
    /// <summary>设置路径。</summary>
    public const string SettingsPath="user://settings.sav";

    static readonly MessagePackSerializerOptions Options=MessagePackSerializerOptions.Standard
        .WithResolver(MessagePack.Resolvers.ContractlessStandardResolver.Instance);

    /// <summary>进程内缓存的进度存档，避免每次读盘。</summary>
    public static Progress Current{get;private set;} = null;

    /// <summary>最近一次读档是否因文件损坏而失败；用于让菜单禁用“继续”并给出提示。</summary>
    public static bool LoadFailed{get;private set;}

    /// <summary>是否存在可继续的存档。</summary>
    public static bool HasSave()
    {
        return FileAccess.FileExists(SavePath);
    }

    /// <summary>
    /// 读进度存档：优先返回进程内缓存的实例，否则从磁盘反序列化。
    /// 读盘失败或数据损坏时返回 null（调用方应当提示并禁止“继续”）。
    /// </summary>
    public static Progress LoadProgress()
    {
        if(Current!=null)
        {
            return Current;
        }
        if(LoadFailed)
        {
            return null; // 已经确认损坏，不要每次刷新都刷屏报错
        }
        if(!HasSave())
        {
            return null;
        }
        byte[] bytes;
        using(var file=FileAccess.Open(SavePath,FileAccess.ModeFlags.Read))
        {
            if(file==null)
            {
                GD.PushError($"读取存档失败：{FileAccess.GetOpenError()}");
                LoadFailed=true;
                return null;
            }
            bytes=file.GetBuffer((long)file.GetLength());
        }
        try
        {
            Current=MessagePackSerializer.Deserialize<Progress>(bytes,Options);
            LoadFailed=false;
        }
        catch(Exception e)
        {
            GD.PushError($"存档解析失败：{e.Message}");
            Current=null;
            LoadFailed=true;
        }
        return Current;
    }

    /// <summary>写入进度存档，并更新 <see cref="Current"/>。</summary>
    public static bool SaveProgress(Progress progress)
    {
        if(progress==null)
        {
            return false;
        }
        Current=progress;
        LoadFailed=false;
        byte[] bytes;
        try
        {
            bytes=MessagePackSerializer.Serialize(progress,Options);
        }
        catch(Exception e)
        {
            GD.PushError($"存档序列化失败：{e.Message}");
            return false;
        }
        using var file=FileAccess.Open(SavePath,FileAccess.ModeFlags.Write);
        if(file==null)
        {
            GD.PushError($"写入存档失败：{FileAccess.GetOpenError()}");
            return false;
        }
        file.StoreBuffer(bytes);
        return true;
    }

    /// <summary>删除存档（开始新游戏前调用）。</summary>
    public static void DeleteSave()
    {
        Current=null;
        LoadFailed=false;
        if(!HasSave())
        {
            return;
        }
        if(FileAccess.FileExists(SavePath))
        {
            var err=DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SavePath));
            if(err!=Error.Ok)
            {
                GD.PushError($"删除存档失败：{err}");
            }
        }
    }

    /// <summary>读设置；不存在或损坏时返回默认设置。</summary>
    public static GameSettings LoadSettings()
    {
        if(!FileAccess.FileExists(SettingsPath))
        {
            return GameSettings.CreateDefault();
        }
        GameSettings settings=null;
        using(var file=FileAccess.Open(SettingsPath,FileAccess.ModeFlags.Read))
        {
            if(file!=null)
            {
                try
                {
                    settings=MessagePackSerializer.Deserialize<GameSettings>(file.GetBuffer((long)file.GetLength()),Options);
                }
                catch(Exception e)
                {
                    GD.PushError($"设置解析失败：{e.Message}");
                }
            }
        }
        settings??=GameSettings.CreateDefault();
        settings.IsNew=false;
        return settings;
    }

    /// <summary>写设置。</summary>
    public static bool SaveSettings(GameSettings settings)
    {
        if(settings==null)
        {
            return false;
        }
        byte[] bytes;
        try
        {
            bytes=MessagePackSerializer.Serialize(settings,Options);
        }
        catch(Exception e)
        {
            GD.PushError($"设置序列化失败：{e.Message}");
            return false;
        }
        using var file=FileAccess.Open(SettingsPath,FileAccess.ModeFlags.Write);
        if(file==null)
        {
            GD.PushError($"写入设置失败：{FileAccess.GetOpenError()}");
            return false;
        }
        file.StoreBuffer(bytes);
        settings.IsNew=false;
        return true;
    }
}
