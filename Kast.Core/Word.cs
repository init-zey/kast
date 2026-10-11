namespace Kast.Core;

/// <summary>
/// 一个词：句法范畴（符号）加一份语义负载。
/// 这里没有继承树——符号是字符串，负载的形状由符号表决定，任何新范畴都不需要新增 C# 类型。
/// </summary>
/// <param name="Symbol">句法范畴名，例如 "S" / "N" / "V" / "A"。</param>
/// <param name="Value">负载。终结词是它在上下文里的键名；组合词是 <see cref="Func{Context,Context}"/>。</param>
public readonly record struct Word(string Symbol, object? Value)
{
    public override string ToString() => $"{Symbol}({Value})";

    /// <summary>把这个词当作上下文变换来执行。</summary>
    public Context Eval(Context ctx)
        => Value is Func<Context, Context> f
           ? f(ctx)
           : throw new InvalidOperationException($"符号 {Symbol} 的负载不是上下文变换：{Value?.GetType().Name ?? "null"}");
}

