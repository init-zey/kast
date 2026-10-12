using System;
using Godot;
using Kast.Core;
using O=System.Func<Kast.Core.Context,object>;

namespace Kast.Godot;

/// <summary>
/// 游戏这一侧的具体字母表：把 <see cref="Alphabet{O}"/> 的负载类型固定成
/// <c>Func&lt;Context,object&gt;</c>，并在 <see cref="Init"/> 里装上本作的文法规则与固有权能词
/// （that / 我 / 你 / 天）。
/// </summary>
/// <remarks>
/// 从旧版 <c>Alphabet.Load()</c> 迁出：文法装配属于"这一款游戏"，不属于通用的
/// <see cref="Alphabet{O}"/>，搬到这里之后 <c>Kast.Core</c> 不再认识任何具体词。
/// 其余玩法词（地、到、看、成为、显示、施放）涉及 Godot 对象与场景，仍由
/// <see cref="NGame.LoadProgress"/> 现场登记。
/// <para>
/// <c>Kast.Core</c> 侧的负载类型是 <c>object</c>，取值前必须按范畴还原成委托形状，
/// 下面的 <see cref="NounOf"/> / <see cref="AdjectiveOf"/> / <see cref="PredicateOf"/>
/// 就是这层转换，形状不对时直接抛异常而不是静默返回 null——词表装配错误应当在启动时暴露。
/// </para>
/// </remarks>
public class KastAlphabet : Alphabet<O>
{
    /// <summary>出错信息里用的类型名，值为 null 时给出 "null"。</summary>
    static string Describe(object value) => value?.GetType().Name ?? "null";
    /// <summary>N 的值：把对象绑进上下文。</summary>
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
    /// <summary>句子：主语先求值并绑成 .s，再执行谓词。</summary>
    static Func<Context, Context> Sentence(object? subject, object? predicate) => NounOf((Context ctx) =>
    {
        var s = NounOf(subject)(ctx).GetItem(".n") as O
            ?? throw new InvalidOperationException("主语不是对象");
        var inner = new Context(ctx);
        inner.Add(".s", s);
        return PredicateOf(predicate)(inner);
    });
    /// <summary>
    /// 装配本作的文法与固有权能词。必须在登记任何玩法词之前调用一次；
    /// <paramref name="node"/> 用来取物理帧间隔（"天"的质量语义需要），
    /// <paramref name="db"/> 用来取词形。
    /// </summary>
    public void Init(Node node,Database db)
    {
        // 形容词修饰名词：N -> A N
        Grammar.AddRule("N -> A N",
            (adj, noun) => NounOf((Context ctx) => NounOf(noun)(AdjectiveOf(adj)(ctx))));
        // 及物动词带宾语：Vt -> Vt N，宾语先求值并绑成 .o，再执行动词
        Grammar.AddRule("Vt -> Vt N", (verb, obj) => PredicateOf((Context ctx) =>
        {
            var o = NounOf(obj)(ctx).GetItem(".n") as O
                ?? throw new InvalidOperationException("宾语不是对象");
            var inner = new Context(ctx);
            inner.Add(".o", o);
            return PredicateOf(verb)(inner);
        }));
        // 组合产生式保持 CNF：只有 A -> B C 与终结 A -> a，没有一元规则。
        // 因此"及物动词也可不带宾语"不能靠 Vi -> Vt 表达，而是让及物动词同时声明 Vt 与 Vi 两个范畴，
        // 并各自提供一条二元规则。
        // 主语加不及物谓语成句：S -> N Vi
        Grammar.AddRule("S -> N Vi", Sentence);
        // 主语加及物谓语成句：S -> N Vt
        Grammar.AddRule("S -> N Vt", Sentence);
        Grammar.AddRule("N -> Adv S",(adv, sentence) =>
        {
            return (Func<Context,Context>)sentence;
        });
        AddWord(db.Gen("that",5),"Adv",typeof(Vector3));
        AddNoun(db.Gen("i",2),(ctx)=>ctx.GetItem(".me") as NObject); // 我
        AddNoun(db.Gen("you",2),(ctx)=> ctx.GetItem(".you") as NObject); // 你
        // 天 = 0、地 = 1：同一个字既是名词（天空/大地，原来的方向与质量语义），又能当二进制位用。
        // 位读法靠 #bit 旗标区分，和名词读法的 #be 是同一个套路：只是"问法"不同，词条本身不用改。
        var sky=db.Gen("sky/air",4);
        O skyValue =(ctx) =>{
            if(ctx.GetFlag("#bit")) return 0;   // 数词读法：天 = 0
            var vec=Vector3.Up*10f;
            if(ctx.GetItem(".s") is O snoun && snoun!=null)
            {
                if(ctx.GetFlag("#be"))
                {
                    return (NObject obj)=>obj.SetMass(obj.Mass-(float)node.GetPhysicsProcessDeltaTime());
                }
                return snoun(ctx).GetPosition()+vec;
            }
            return null;
        };
        AddNoun(sky,skyValue);          // N   -> 天（名词语义）
        AddWord(sky,"Num",skyValue);    // Num -> 天（单独一个位也是一个数）
        AddWord(sky,"Bit",skyValue);    // Bit -> 天（拼接时的右操作数）
    }
}