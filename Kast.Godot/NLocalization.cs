using System.Collections.Generic;
using Godot;

namespace Kast.Godot;

/// <summary>
/// 界面文案。Godot 的 CSV 翻译需要编辑器重新导入才会生成 .translation 资源，
/// 这里以运行时翻译作为兜底，保证菜单在任何情况下都有正确文案；
/// <see cref="TranslationServer"/> 已注册的同 key 资源翻译优先级更高，可以覆盖这里。
/// <para>可用的 locale 列表，以及 CSV 源文件里同步的 key。</para>
/// </summary>
public static class NLocalization
{
    public const string English="en";
    public const string Chinese="zh_CN";

    /// <summary>语言下拉/切换的顺序。</summary>
    public static readonly string[] Locales={English,Chinese};

    static readonly (string Locale,string Key,string Text)[] Entries=
    {
        // 主菜单
        (English,"menu.title","KAST"),
        (English,"menu.subtitle","Speak the words. Reshape the world."),
        (English,"menu.continue","Continue"),
        (English,"menu.new_game","New Game"),
        (English,"menu.settings","Settings"),
        (English,"menu.quit","Quit"),
        (English,"menu.back","Back"),
        (English,"menu.empty_slot","No save found"),
        (English,"menu.save_info","Seed {0} · {1} symbols"),
        // 设置
        (English,"settings.title","Settings"),
        (English,"settings.language","Language"),
        (English,"settings.volume","Master Volume"),
        (English,"settings.fullscreen","Fullscreen"),
        (English,"settings.on","On"),
        (English,"settings.off","Off"),
        (English,"settings.hint","Esc / click Back to save automatically"),
        // 通用对话框
        (English,"dialog.ok","OK"),
        (English,"dialog.cancel","Cancel"),
        (English,"dialog.confirm_new_title","Start a new game?"),
        (English,"dialog.confirm_new_body","The existing save will be deleted and a new world seed will be generated."),
        // 语言名
        (English,"language.en","English"),
        (English,"language.zh_CN","简体中文"),

        (Chinese,"menu.title","KAST"),
        (Chinese,"menu.subtitle","以语言重塑世界"),
        (Chinese,"menu.continue","继续游戏"),
        (Chinese,"menu.new_game","新游戏"),
        (Chinese,"menu.settings","设置"),
        (Chinese,"menu.quit","退出"),
        (Chinese,"menu.back","返回"),
        (Chinese,"menu.empty_slot","没有找到存档"),
        (Chinese,"menu.save_info","世界种子 {0} · 已解锁 {1} 个符号"),
        (Chinese,"settings.title","设置"),
        (Chinese,"settings.language","语言"),
        (Chinese,"settings.volume","主音量"),
        (Chinese,"settings.fullscreen","全屏"),
        (Chinese,"settings.on","开"),
        (Chinese,"settings.off","关"),
        (Chinese,"settings.hint","按 Esc 或点“返回”会自动保存设置"),
        (Chinese,"dialog.ok","确定"),
        (Chinese,"dialog.cancel","取消"),
        (Chinese,"dialog.confirm_new_title","开始新游戏？"),
        (Chinese,"dialog.confirm_new_body","现有存档会被删除，并生成新的世界种子。"),
        (Chinese,"language.en","English"),
        (Chinese,"language.zh_CN","简体中文"),
    };

    static bool Registered;

    /// <summary>
    /// 把内置文案注册进 <see cref="TranslationServer"/>（只做一次），
    /// 这样菜单既能用 <see cref="Tr"/>，也能被 <c>tr()</c>/自动翻译覆盖。
    /// </summary>
    public static void Register()
    {
        if(Registered)
        {
            return;
        }
        Registered=true;
        var translations=new Dictionary<string,Translation>();
        foreach(var (locale,key,text) in Entries)
        {
            if(!translations.TryGetValue(locale,out var translation))
            {
                translation=new Translation();
                translation.Locale=locale;
                translations[locale]=translation;
                TranslationServer.AddTranslation(translation);
            }
            translation.AddMessage(key,text);
        }
    }

    /// <summary>
    /// 取 key 对应的文案，找不到就返回 key 本身（方便发现漏翻译）。
    /// </summary>
    public static string Tr(string key)
    {
        Register();
        return TranslationServer.Translate(key).ToString();
    }

    /// <summary>
    /// 取 key 对应的文案并做 <see cref="string.Format(string,object[])"/> 占位替换。
    /// </summary>
    public static string Tr(string key,params object[] args)
    {
        return string.Format(Tr(key),args);
    }

    /// <summary>当前界面语言。</summary>
    public static string CurrentLocale
    {
        get
        {
            var locale=TranslationServer.GetLocale();
            return string.IsNullOrEmpty(locale)?English:locale;
        }
    }

    /// <summary>切换界面语言并立即生效。</summary>
    public static void SetLocale(string locale)
    {
        Register();
        TranslationServer.SetLocale(locale);
    }

    /// <summary>语言在设置界面里显示的名字。</summary>
    public static string DisplayName(string locale)
    {
        return locale switch
        {
            Chinese=>"简体中文",
            _=>"English",
        };
    }
}
