namespace Kast.Core;

/// <summary>一条已解析的产生式。</summary>
/// <param name="Pattern">原始模式串。</param>
/// <param name="Result">结果符号（箭头左侧）。</param>
/// <param name="Left">左子符号；终结产生式为 null。</param>
/// <param name="Right">右子符号；终结产生式为 null。</param>
public sealed record GrammarRule(string Pattern, string Result, string? Left, string? Right)
{
    /// <summary>终结产生式 "A -&gt; a"：值由词法条目提供。</summary>
    public bool IsTerminal => Left is null;

    /// <summary>组合产生式 "A -&gt; B C" 的动作；终结产生式不使用。</summary>
    public Func<Context, object?, object?, object?>? Compose { get; init; }

    public override string ToString() => Pattern;
}

/// <summary>
/// 字符串模式 DSL 下的文法。符号是字符串，规则是数据：
/// 组合产生式 "S -&gt; N V" 由 <see cref="AddRule(string, Func{object?, object?, object?})"/> 声明，
/// 终结产生式 "N -&gt; 我" 由 <see cref="BindTerminal"/> 与词法条目绑定后自动登记。
/// </summary>
public sealed class Grammar
{
    readonly Dictionary<string, List<GrammarRule>> byPair = [];
    readonly List<GrammarRule> allRules = [];
    readonly HashSet<string> terminalPatterns = [];
    readonly Dictionary<(string Symbol, string Token), Func<Context, object?>> terminals = [];

    /// <summary>已登记的产生式，含由其词法条目生成的终结产生式。</summary>
    public IReadOnlyList<GrammarRule> Rules => allRules;

    /// <summary>解析模式串。"S -&gt; N V" 是组合产生式，"N -&gt; 我" 是终结产生式。</summary>
    public static (string Result, string? First, string? Second) ParsePattern(string pattern)
    {
        var arrow = pattern.IndexOf("->", StringComparison.Ordinal);
        if (arrow < 0)
        {
            throw new FormatException($"产生式缺少 '->'：\"{pattern}\"");
        }
        var result = pattern[..arrow].Trim();
        var parts = pattern[(arrow + 2)..]
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (result.Length == 0 || parts.Length == 0)
        {
            throw new FormatException($"产生式左右两侧都不能为空：\"{pattern}\"");
        }
        return parts.Length switch
        {
            1 => (result, null, null),
            2 => (result, parts[0], parts[1]),
            _ => throw new FormatException(
                     $"右部只支持一个（终结）或两个（组合）符号，实际 {parts.Length} 个：\"{pattern}\""),
        };
    }

    /// <summary>登记二元组合产生式 "A -&gt; B C"，动作接收 B、C 两个值并产出 A 的值。</summary>
    public Grammar AddRule(string pattern, Func<object?, object?, object?> action)
        => AddRule(pattern, (_, left, right) => action(left, right));

    /// <summary>登记二元组合产生式 "A -&gt; B C"，动作额外接收上下文。</summary>
    public Grammar AddRule(string pattern, Func<Context, object?, object?, object?> action)
    {
        var (result, left, right) = ParsePattern(pattern);
        if (left is null || right is null)
        {
            throw new FormatException($"组合产生式的右部需要两个符号：\"{pattern}\"");
        }
        Bind(result, left, right, action);
        return this;
    }

    void Bind(string result, string left, string right,
              Func<Context, object?, object?, object?> action)
    {
        var pattern = $"{result} -> {left} {right}";
        var rule = new GrammarRule(pattern, result, left, right)
        {
            Compose = action,
        };
        var key = Key(left, right);
        if (!byPair.TryGetValue(key, out var list))
        {
            list = [];
            byPair[key] = list;
        }
        list.Add(rule);
        allRules.Add(rule);
    }

    /// <summary>
    /// 登记终结产生式 "A -&gt; a"：<paramref name="value"/> 是取该终结符语义负载的工厂。
    /// </summary>
    public Grammar BindTerminal(string symbol, string token, Func<Context, object?> value)
    {
        var pattern = $"{symbol} -> {token}";
        if (!terminalPatterns.Add(pattern))
        {
            throw new InvalidOperationException($"终结产生式重复：\"{pattern}\"");
        }
        allRules.Add(new GrammarRule(pattern, symbol, null, null));
        terminals[(symbol, token)] = value;
        return this;
    }

    /// <summary>取某个终结符的语义负载工厂；没有绑定时返回 null。</summary>
    public Func<Context, object?>? TerminalValue(string symbol, string token)
        => terminals.TryGetValue((symbol, token), out var value) ? value : null;

    /// <summary>取出 (左子符号, 右子符号) 上可用的产生式；没有时返回空表。</summary>
    public List<GrammarRule> RulesFor(string left, string right)
        => byPair.TryGetValue(Key(left, right), out var list) ? list : [];

    static string Key(string left, string right) => string.Concat(left, "\u0000", right);
}
