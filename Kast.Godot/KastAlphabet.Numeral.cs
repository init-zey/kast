using System;
using Kast.Core;
using O=System.Func<Kast.Core.Context,object>;

namespace Kast.Godot;

/// <summary>
/// 二进制数词：天 = 0、地 = 1，位序列最左位是最高位（<c>地天天地</c> 读作 9）。
/// </summary>
/// <remarks>
/// 位词条本身登记在 <c>KastAlphabet.Words.cs</c>（同时声明 N / Num / Bit 三个范畴），
/// 本文件负责把位拼成数，以及把数交给动词当宾语。
/// <para>
/// 与词条值的形状不同，数词的值不是"上下文变换"而是 <see cref="NumSlot"/>——
/// 一个能取出数字、并记住自己位数的槽。之所以要记位数，是因为拼接是左结合的，
/// 左侧那个数要按右侧的位数左移。
/// </para>
/// </remarks>
public partial class KastAlphabet
{
    /// <summary>
    /// 数词绑进 <c>.o</c> 时用的槽：值 + 位数。
    /// </summary>
    /// <remarks>
    /// 绑成 <c>slot.Get</c> 这个委托，拼接时靠 <c>Delegate.Target</c> 认出
    /// "上一个 .o 是不是一个数"——只靠旗标分不清"名词宾语"和"被名词覆盖过的数词宾语"。
    /// </remarks>
    sealed class NumSlot(int bits,O value)
    {
        public readonly int Bits=bits;
        public readonly O Value=value;
        public object Get(Context ctx)=>Value(ctx);
    }

    void AddNumerals()
    {
        // Num -> Num Bit 是左递归拼接：左边的数左移一位，再补上这一位。
        // "Num -> Bit"（一元规则）文法不支持，所以位词条是直接登记成 Num 的（见 Words）。
        Grammar.AddRule("Num -> Num Bit", AppendBit);

        // 数词作宾语：Num 的值是"取出这个数字"的槽（不像 N 的负载是上下文变换）。
        // 因为 Vt -> Vt Num 也是左递归的，一串位可能被切成好几段数词宾语，
        // 所以这里把各段拼成一个数（见 BindNumber）：无论怎么切，动词拿到的都是同一个值。
        Grammar.AddRule("Vt -> Vt Num",(verb, num) => (Context ctx) =>
        {
            var inner=BindNumber(ctx,num);
            return ((Func<Context, Context>)verb)(inner);
        });
    }

    /// <summary>把数词的值统一成槽：终结数词（单独一个位）的负载要加 <c>#bit</c> 才读得出 0/1。</summary>
    static NumSlot AsNum(object? value) => value as NumSlot
        ?? new NumSlot(1, c => ((O)value!)(c.WithFlag("#bit")));

    /// <summary>Num -&gt; Num Bit：左边的数左移一位，再补上这一位。</summary>
    static NumSlot AppendBit(object? high,object? low)
    {
        var upper=AsNum(high);
        return new NumSlot(upper.Bits+1, c =>
            Convert.ToInt32(upper.Value(c))*2 + Convert.ToInt32(((O)low!)(c.WithFlag("#bit"))));
    }

    /// <summary>
    /// Vt -&gt; Vt Num：本段在高位，接到"已经绑好的低位段"前面；没有低位段就自成一段。
    /// 这样 <c>地天天地</c> 无论被切成几段数词宾语，动词拿到的都是同一个 9。
    /// </summary>
    static Context BindNumber(Context ctx,object? num)
    {
        var segment=AsNum(num);
        var lower=ctx.GetItem(".o") is O value&&value.Target is NumSlot slot?slot:null;
        var combined=lower is null
            ? new NumSlot(segment.Bits,segment.Value)
            : new NumSlot(segment.Bits+lower.Bits, c =>
                Convert.ToInt32(segment.Value(c))*(1 << lower.Bits) + Convert.ToInt32(lower.Value(c)));

        var inner=new Context(ctx);
        inner.Add(".o",(O)combined.Get);
        return inner;
    }
}
