using Kast.Core;

namespace Kast.Test;

/// <summary>
/// 二进制数词（天 = 0、地 = 1）的设计验证。规则与负载形状与 <c>NGame.LoadProgress</c> 里登记的逐字对应，
/// 只把"天/地"的名词负载换成哨兵字符串，好让这套设计在没有 Godot 的环境里也能跑。
/// <para>
/// 两个已知的歧义来源，验证里分开处理：
/// <list type="number">
/// <item><c>Num -&gt; Num Bit</c> 与 <c>Vt -&gt; Vt Num</c> 都是左递归，一串位可以被切成好几个数词宾语。
/// 这里的数词值带位数，并且每段把自己当高位接到"已绑好的低位段"前面，所以<b>所有切法得到同一个值</b>。</item>
/// <item>天/地同时是名词与位，于是同一句话还能读成"名词宾语 + 数字宾语"等混合读法，那些读法给出的值不同。
/// 这是"同一个字两种词性"的固有代价，验证里单独统计。</item>
/// </list>
/// </para>
/// </summary>
static class NumeralTests
{
    /// <summary>#bit 旗标：与 <c>Kast.Godot/ContextHelper</c> 的 GetFlag/WithFlag 同义。</summary>
    static bool HasBit(Context ctx)=>ctx.GetItem("#bit")!=null;
    static Context WithBit(Context ctx)
    {
        var inner=new Context(ctx);
        inner.Set("#bit",true);
        return inner;
    }

    static readonly List<int> Numbers=[];
    static readonly List<object?> Nouns=[];

    public static int Run()
    {
        Console.WriteLine("—— 二进制数词（天=0 地=1）验证 ——");
        var failures=new List<string>();
        var checks=0;

        (string Bits,int Value)[] cases=
        [
            ("天",0),
            ("地",1),
            ("天地",1),
            ("地天",2),
            ("地地",3),
            ("天地天",2),
            ("地天天地",9),
            ("地地地地",15),
            ("地地地地地",31),
            ("天天天",0),
        ];

        // A. 位只当位（不兼名词）：所有切法必须给出同一个值
        var only=GameAlphabet(digitsAreNouns:false);
        foreach(var (bits,value) in cases)
        {
            checks++;
            var (numbers,nouns)=Read(only,"我记"+bits);
            var distinct=numbers.Distinct().OrderBy(n=>n).ToArray();
            Console.WriteLine($"  [只当位] {bits,-8} → 值 {string.Join("/",distinct),-4}（{numbers.Count} 种切法）名词读法 {nouns.Count}");
            if(numbers.Count==0||distinct.Length!=1||distinct[0]!=value)
            {
                failures.Add($"\"{bits}\" 应稳定读成 {value}，实际 [{string.Join(",",numbers)}]");
            }
        }

        // B. 位同时兼名词（游戏现在就是这样）：统计混合读法带来的偏差
        var dual=GameAlphabet(digitsAreNouns:true);
        Console.WriteLine("  —— 天/地 同时当名词时（同一句话的读法不止一种）——");
        foreach(var (bits,value) in cases)
        {
            checks++;
            var (numbers,nouns)=Read(dual,"我记"+bits);
            var right=numbers.Count(n=>n==value);
            var distinct=numbers.Distinct().OrderBy(n=>n).ToArray();
            Console.WriteLine($"  [兼名词] {bits,-8} → 读到过的值 {string.Join("/",distinct),-8}"+
                              $"（{numbers.Count} 种读法里 {right} 种给出 {value}，另 {nouns.Count} 种读成名词）");
            if(!numbers.Contains(value))
            {
                failures.Add($"[兼名词] \"{bits}\" 至少要能读成 {value}，实际 [{string.Join(",",numbers)}]");
            }
        }

        // C. 同一个字两种读法并存：天既是名词（天空）又能当数字 0
        checks++;
        var sky=Read(dual,"我记天");
        if(!sky.Numbers.Contains(0)||!sky.Nouns.Contains("sky"))
        {
            failures.Add($"\"我记天\" 应同时有数词读法 0 与名词读法 sky，实际 数={string.Join(",",sky.Numbers)} 名={string.Join(",",sky.Nouns)}");
        }

        // D. 一元组合产生式不被支持：这正是本设计把位词条直接登记成 Num 的原因
        checks++;
        try
        {
            dual.Grammar.AddRule("Num -> Bit",(left,right)=>left);
            failures.Add("一元组合产生式 \"Num -> Bit\" 本应被拒绝，却登记成功了");
        }
        catch(FormatException)
        {
            // 预期：Grammar 只支持右部一个（终结）或两个（组合）符号
        }

        Console.WriteLine(failures.Count==0
            ? $"二进制数词：{checks} 项检查全部通过"
            : $"二进制数词：{checks} 项检查，{failures.Count} 项失败");
        foreach(var failure in failures)
        {
            Console.WriteLine("  失败："+failure);
        }
        return failures.Count==0?0:1;
    }

    /// <summary>跑一句"我记X"：X 当数词就读出数字，当名词就把名词负载收进 Nouns。</summary>
    static (List<int> Numbers,List<object?> Nouns) Read(Alphabet<Func<Context,object>> a,string text)
    {
        Numbers.Clear();
        Nouns.Clear();
        foreach(var spell in a.Parse(a.Tokenize(text)))
        {
            spell.Eval(a);   // 词典条目挂在 Alphabet 这个 Context 上，与 Entry 的示例一致
        }
        return (Numbers,Nouns);
    }

    /// <summary>
    /// 数词绑进 .o 时用的槽：值 + 位数。绑成 <c>slot.Get</c> 这个委托，
    /// 拼接时用 <c>Delegate.Target</c> 认出"上一个 .o 是不是一个数"——只靠旗标分不清名词与被名词覆盖过的数。
    /// </summary>
    sealed class NumSlot(int bits,Func<Context,object> value)
    {
        public readonly int Bits=bits;
        public readonly Func<Context,object> Value=value;
        public object Get(Context ctx)=>Value(ctx);
    }

    /// <summary>把数词的值统一成 <see cref="NumSlot"/>：终结数词（单独一个位）的负载要加 #bit 才读得出 0/1。</summary>
    static NumSlot AsNum(object? value)=>value as NumSlot
        ?? new NumSlot(1,(Func<Context,object>)(c=>((Func<Context,object>)value!)(WithBit(c))));

    /// <summary>Num -> Num Bit：左子左移一位，再补上这一位。</summary>
    static NumSlot Append(object? high,object? low)
    {
        var upper=AsNum(high);
        return new NumSlot(upper.Bits+1,(Func<Context,object>)(c=>
            Convert.ToInt32(upper.Value(c))*2+Convert.ToInt32(((Func<Context,object>)low!)(WithBit(c)))));
    }

    /// <summary>Vt -> Vt Num 的动作：本段在高位，接到已经绑好的低位段前面；没有低位段就自成一段。</summary>
    static Context BindNumber(Context ctx,object? num)
    {
        var segment=AsNum(num);
        var lower=ctx.GetItem(".o") is Func<Context,object> value&&value.Target is NumSlot slot?slot:null;
        var combined=lower is null
            ? new NumSlot(segment.Bits,segment.Value)
            : new NumSlot(segment.Bits+lower.Bits,(Func<Context,object>)(c=>
                Convert.ToInt32(segment.Value(c))*(1<<lower.Bits)+Convert.ToInt32(lower.Value(c))));
        var inner=new Context(ctx);
        inner.Add(".o",(Func<Context,object>)combined.Get);
        return inner;
    }

    static Alphabet<Func<Context,object>> GameAlphabet(bool digitsAreNouns)
    {
        var a=Alphabet<Func<Context,object>>.Load();

        // 天 = 0、地 = 1：位读法靠 #bit 旗标（与名词的 #be 同一个套路），名词语义不受影响
        a.AddNoun("我",(Func<Context,object>)(ctx=>"me"));   // 句子主语
        Func<Context,object> sky=(ctx)=>HasBit(ctx)?0:"sky";
        Func<Context,object> earth=(ctx)=>HasBit(ctx)?1:"earth";
        if(digitsAreNouns)
        {
            a.AddNoun("天",sky);            // N -> 天（名词语义：天空）
            a.AddNoun("地",earth);          // N -> 地（名词语义：大地）
        }
        a.AddWord("天","Num",sky);      // Num -> 天（单个位就是一个数，终结规则）
        a.AddWord("天","Bit",sky);      // Bit -> 天（拼接时的右操作数）
        a.AddWord("地","Num",earth);
        a.AddWord("地","Bit",earth);

        // 二进制拼接：Num -> Num Bit（左递归，最左位是最高位）
        a.Grammar.AddRule("Num -> Num Bit",(high,low)=>Append(high,low));

        // 数词作宾语：把各段拼成一个数（见 BindNumber）
        a.Grammar.AddRule("Vt -> Vt Num",(verb,num)=>(Func<Context,Context>)(ctx=>
        {
            var inner=BindNumber(ctx,num);
            return ((Func<Context,Context>)verb!)(inner);
        }));

        // 测试用的及物动词：把宾语里的数字/名词读出来
        a.AddTVerb("记",(ctx,s,o)=>
        {
            if(o is null) return;
            var value=o(ctx);
            if(value is int number) Numbers.Add(number);
            else Nouns.Add(value);
        });
        a.CompileTokens();
        return a;
    }
}
