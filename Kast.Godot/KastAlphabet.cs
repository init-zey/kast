using System;
using System.Collections.Generic;
using Godot;
using Kast.Core;
using O=System.Func<Kast.Core.Context,object>;

namespace Kast.Godot;

/// <summary>
/// 本作的字母表：把 <see cref="Alphabet{O}"/> 的负载类型固定成 <c>Func&lt;Context,object&gt;</c>，
/// 并装配全部语法规则与词条。构造后调用一次 <see cref="Init"/> 即可用。
/// </summary>
/// <remarks>
/// 分四个文件：
/// <list type="bullet">
/// <item>本文件——装配入口、文法规则、负载形状转换；</item>
/// <item><c>KastAlphabet.Words.cs</c>——名词（我/你/天/地）；</item>
/// <item><c>KastAlphabet.Verbs.cs</c>——动词（到/看/成为/显示/施放）；</item>
/// <item><c>KastAlphabet.Numeral.cs</c>——二进制数词（天=0、地=1）。</item>
/// </list>
/// <para>
/// <c>Kast.Core</c> 侧的负载类型是 <c>object</c>，取值前必须按范畴还原成委托形状，
/// 见 <see cref="NounOf"/> / <see cref="AdjectiveOf"/> / <see cref="PredicateOf"/>；
/// 形状不对直接抛异常，词表装配错误应当在启动时暴露。
/// </para>
/// </remarks>
public partial class KastAlphabet : Alphabet<O>
{
    /// <summary>本局的游戏节点，词条需要它取节点、帧间隔与当前选中的槽（由 <see cref="Init"/> 注入）。</summary>
    NGame game=null!;
    /// <summary>词形分配器，登记词条时取程序词形（由 <see cref="Init"/> 注入）。</summary>
    Database db=null!;

    /// <summary>
    /// 装配文法与全部词条。必须在注册任何外部词之前调用一次。
    /// </summary>
    public void Init(NGame game,Database db)
    {
        this.game=game;
        this.db=db;

        AddGrammar();
        AddNouns();
        AddVerbs();
        AddNumerals();
    }

    // —— 文法 ——
    // 组合产生式保持 CNF：只有 A -> B C 与终结 A -> a，没有一元规则。
    // 因此"及物动词也可不带宾语"不能靠 Vi -> Vt 表达，而是让及物动词同时声明 Vt 与 Vi 两个范畴。

    void AddGrammar()
    {
        // 形容词修饰名词：N -> A N
        Grammar.AddRule("N -> A N",
            (adj, noun) => NounOf((Context ctx) => NounOf(noun)(AdjectiveOf(adj)(ctx))));

        // 及物动词带宾语：宾语先求值并绑成 .o，再执行动词
        Grammar.AddRule("Vt -> Vt N", (verb, obj) => PredicateOf((Context ctx) =>
        {
            var inner=new Context(ctx);
            inner.Add(".o",ObjectOf(obj,ctx));
            return PredicateOf(verb)(inner);
        }));

        // 主语加谓语成句
        Grammar.AddRule("S -> N Vi", Sentence);
        Grammar.AddRule("S -> N Vt", Sentence);

        // 关系从句：Adv 只作占位，语义完全来自后面的句子（Adv 的负载不会被求值，见 Alphabet.Terminal）
        Grammar.AddRule("N -> Adv S", (adv, sentence) => (Func<Context,Context>)sentence);

        AddWord(db.Gen("that",5),"Adv",typeof(Vector3));
        AddWord(db.Gen("thing",5),"Adv",typeof(NObjectType));
    }

    // —— 负载形状转换 ——

    /// <summary>出错信息里用的类型名。</summary>
    static string Describe(object value) => value?.GetType().Name ?? "null";

    /// <summary>N 的值：上下文变换，其中 .n 是本对象。</summary>
    protected static Func<Context, Context> NounOf(object value)
        => value as Func<Context, Context>
           ?? throw new InvalidOperationException($"名词的值应为上下文变换，实际 {Describe(value)}");

    /// <summary>A 的值：上下文变换。</summary>
    protected static Func<Context, Context> AdjectiveOf(object value)
        => value as Func<Context, Context>
           ?? throw new InvalidOperationException($"形容词的值应为上下文变换，实际 {Describe(value)}");

    /// <summary>V 的值：谓词，按当前上下文执行动词。</summary>
    protected static Func<Context, Context> PredicateOf(object value)
        => value as Func<Context, Context>
           ?? throw new InvalidOperationException($"谓词的值应为上下文变换，实际 {Describe(value)}");

    /// <summary>把名词求值成它绑定的对象（.n）。</summary>
    static O ObjectOf(object noun,Context ctx)
        => NounOf(noun)(ctx).GetItem(".n") as O
           ?? throw new InvalidOperationException("名词没有绑定对象");

    /// <summary>句子：主语先求值并绑成 .s，再执行谓词。</summary>
    static Func<Context, Context> Sentence(object? subject, object? predicate) => NounOf((Context ctx) =>
    {
        var inner=new Context(ctx);
        inner.Add(".s",ObjectOf(subject,ctx));
        return PredicateOf(predicate)(inner);
    });
}
