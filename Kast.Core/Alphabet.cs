using System.Text;

namespace Kast.Core;

public enum WordType
{
    Noun,
    Verb,
    Adjective
}

/// <summary>
/// 一部语法：词法条目 + 字符串模式 DSL 声明的句法规则 + 一个 CYK 分析器。
/// 句法范畴是字符串符号，词法只保留三条终结规则（N / V / A -&gt; 词），
/// 其余范畴与规则都以数据形式加入 <see cref="Grammar"/>，不需要任何 C# 继承。
/// </summary>
/// <remarks>TODO: 待办——见下方 // TODO: 标记。</remarks>
public class Alphabet<O> : Context
    where O:class
{
    string[] tokens;

    /// <summary>已编译的分词表，即预测函数里的 S。</summary>
    public IReadOnlyList<string> Tokens => tokens;
    PrefixTrie? trie;

    /// <summary>句法规则表。</summary>
    public Grammar Grammar { get; } = new();

    /// <summary>起始符号：<see cref="Parse"/> 只从它取结果。</summary>
    public string StartSymbol { get; init; } = "S";

    // TODO: 词缀机制（AdjectiveAffix / NounAffix / VerbAffix 三条变格规则）尚未移植到 DSL，
    //       需要时用 Grammar.AddRule("N -> N <词缀词>", ...) 补回。
    // TODO: 缺少不及物动词的谓词规则（如 "V -> V"），目前单独的动词无法充当谓语。

    /// <summary>词法条目：词形 → 它作为终结符时所属的范畴（一个词形可以身兼多个）。</summary>
    public IReadOnlyDictionary<string, List<string>> Lexicon => lexicon;
    readonly Dictionary<string, List<string>> lexicon = [];

    public Alphabet(Alphabet<O> parent) : base(parent)
    {
        tokens=parent.tokens;
    }

    public Alphabet()
    {
    }



    /// <summary>
    /// 登记一个词法条目并声明它的范畴，即一条终结产生式 "范畴 -&gt; 词"。
    /// 同一个词形可以多次登记以声明它身兼的多个范畴；词典条目以第一次登记为准，
    /// 这样先前声明的范畴在求值时仍能按原条目类型取到正确的值形状。
    /// </summary>
    public void AddWord(string token, string symbol, object item)
    {
        if (GetItem(token) is null) Set(token, item);
        Declare(token, symbol);
    }

    /// <summary>登记一个名词条目（同时登记词法范畴，故有意隐藏基类的同名成员）。</summary>
    public void AddNoun(string name, O obj) => AddWord(name, "N", obj);

    /// <summary>
    /// 登记一个<b>及物</b>动词条目。及物动词同时属于 Vt 和 Vi 两个范畴：
    /// Vt 供 "Vt -&gt; Vt N" 带宾语，Vi 供 "S -&gt; N V" 不带宾语。两个范畴都是一条终结产生式，
    /// 因此不需要一元产生式，文法保持 CNF。
    /// </summary>
    public void AddTVerb(string name, Action<Context,O,O?> run)
    {
        AddWord(name, "Vt", (Context ctx)=>run(ctx,(O)ctx.GetItem(".s")!,(O?)ctx.GetItem(".o")));
        AddWord(name, "Vi", (Context ctx)=>run(ctx,(O)ctx.GetItem(".s")!,null));
    }

    /// <summary>
    /// 登记一个<b>不及物</b>动词条目，只声明 Vi。带宾语的产生式 "Vi -&gt; Vi N" 没有被声明，
    /// 所以不及物动词带宾语会在解析阶段就被拒绝，而不是等到求值。
    /// </summary>
    public void AddIVerb(string name, Action<Context,O> run)=>
        AddWord(name, "Vi", (Context ctx) => run(ctx,(O)ctx.GetItem(".s")!));

    /// <summary>登记一个形容词条目并声明为 A，负载是上下文变换。</summary>
    public void AddAdjective(string name, Func<Context, Context> extend) => AddWord(name, "A", extend);

    /// <summary>
    /// 从当前语法派生一个只含 <paramref name="keys"/> 这些词条的语法：规则照搬，词典只留子集。
    /// 词条按引用共享，适合"完整语法固定、派生集是它的一个视图"的用法；
    /// 若派生集要独立演化属性值，需自行深拷贝条目。
    /// </summary>
    /// <param name="keys">要保留的词条键，每个键都必须存在于当前语法中。</param>
    /// <exception cref="KeyNotFoundException">某个键没有词法范畴或没有条目值。</exception>
    public Alphabet<O> Derive(IEnumerable<string> keys)
    {
        var derived = new Alphabet<O> { StartSymbol = StartSymbol };
        // 组合产生式是纯数据，整份照搬；终结产生式由下面的 AddWord 重新绑定
        foreach (var rule in Grammar.Rules)
        {
            if (rule.Compose is not null)
            {
                derived.Grammar.AddRule(rule.Pattern, rule.Compose);
            }
        }
        foreach (var key in keys)
        {
            if (!lexicon.TryGetValue(key, out var symbols))
            {
                throw new KeyNotFoundException($"派生失败：词条 {key} 没有登记范畴");
            }
            var item = GetItem(key)
                ?? throw new KeyNotFoundException($"派生失败：词条 {key} 没有条目值");
            // 一个词形可能身兼多个范畴（例如及物动词同时是 Vt 与 Vi），逐一声明
            foreach (var symbol in symbols)
            {
                derived.AddWord(key, symbol, item);
            }
        }
        derived.CompileTokens();
        return derived;
    }

    void Declare(string token, string symbol)
    {
        if (!lexicon.TryGetValue(token, out var symbols))
        {
            symbols = [];
            lexicon[token] = symbols;
        }
        if (!symbols.Contains(symbol))
        {
            symbols.Add(symbol);
            Grammar.BindTerminal(symbol, token, ctx => Terminal(ctx, token, symbol));
        }
    }

    /// <summary>
    /// 终结规则 A -&gt; a：词法条目的语义负载，按<b>符号</b>取形状（不靠条目类型猜范畴，
    /// 因为泛型词典类型可能与 Func/Action 载荷重叠）。与旧 Named* 词一致，查表发生在求值时。
    /// </summary>
    object? Terminal(Context ctx, string token, string symbol)
    {
        if (ctx.GetItem(token) is null)
        {
            throw new InvalidOperationException($"词法条目 {token} 不存在");
        }
        return symbol switch
        {
            "N" => NounTerminal(token),
            "A" => AdjectiveTerminal(token),
            "Vt" or "Vi" => VerbTerminal(token),
            _ => ctx.GetItem(token),
        };
    }

    static Func<Context, Context> NounTerminal(string token) => ctx =>
    {
        var obj = ctx.GetItem(token) as O
            ?? throw new InvalidOperationException($"名词{token}不存在");
        var scope = new Context(ctx);
        scope.Add(".n", obj);
        return scope;
    };

    static Func<Context, Context> AdjectiveTerminal(string token) => ctx =>
        ctx.GetItem(token) is Func<Context, Context> extend
            ? extend(ctx)
            : throw new InvalidOperationException($"形容词{token}不存在");

    /// <summary>
    /// 动词谓词：宾语已绑成 .o 就用它，否则看有没有"待绑的名词"（.o 缺席时的 .np，由关系从句提供）。
    /// 及物/不及物共用这套求值，因此 <where> 对两类动词都成立。
    /// </summary>
    static Func<Context, Context> VerbTerminal(string token) => ctx =>
    {
        var inner = new Context(ctx);
        if (ctx.GetItem(".o") is O obj)
        {
            inner.Add(".o", obj);
        }
        var item=ctx.GetItem(token)
            ?? throw new InvalidOperationException($"动词{token}不存在");
        switch(item)
        {
            case Action<Context> run:
                run(inner);
                break;
            case Func<Context,O,Context> withObject:
                var argument=inner.GetItem(".np") as O
                    ?? throw new InvalidOperationException($"动词{token}需要宾语，但上下文里没有");
                withObject(inner,argument);
                break;
            default:
                throw new InvalidOperationException($"动词{token}的值形状不受支持：{item.GetType().Name}");
        }
        return inner;
    };

    /// <summary>把当前文法装进一个实例：终结范畴 + 句法规则。</summary>
    public static Alphabet<O> Load()
    {
        var a = new Alphabet<O>();
        // 形容词修饰名词：N -> A N
        a.Grammar.AddRule("N -> A N",
            (adj, noun) => NounOf((Context ctx) => NounOf(noun)(AdjectiveOf(adj)(ctx))));
        // 及物动词带宾语：Vt -> Vt N，宾语先求值并绑成 .o，再执行动词
        a.Grammar.AddRule("Vt -> Vt N", (verb, obj) => PredicateOf((Context ctx) =>
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
        a.Grammar.AddRule("S -> N Vi", (subject, predicate) => Sentence(subject, predicate));
        // 主语加及物谓语成句：S -> N Vt
        a.Grammar.AddRule("S -> N Vt", (subject, predicate) => Sentence(subject, predicate));
        return a;
    }

    /// <summary>句子：主语先求值并绑成 .s，再执行谓词。</summary>
    static Func<Context, Context> Sentence(object? subject, object? predicate) => NounOf((Context ctx) =>
    {
        var s = NounOf(subject)(ctx).GetItem(".n") as O
            ?? throw new InvalidOperationException("主语不是对象");
        var inner = new Context(ctx);
        inner.Add(".s", s);
        return PredicateOf(predicate)(inner);
    });

    /// <summary>N 的值：把对象绑进上下文。</summary>
    static Func<Context, Context> NounOf(object? value)
        => value as Func<Context, Context>
           ?? throw new InvalidOperationException($"名词的值应为上下文变换，实际 {Describe(value)}");

    /// <summary>A 的值：上下文变换。</summary>
    static Func<Context, Context> AdjectiveOf(object? value)
        => value as Func<Context, Context>
           ?? throw new InvalidOperationException($"形容词的值应为上下文变换，实际 {Describe(value)}");

    /// <summary>V 的值：谓词，按当前上下文执行动词。</summary>
    static Func<Context, Context> PredicateOf(object? value)
        => value as Func<Context, Context>
           ?? throw new InvalidOperationException($"谓词的值应为上下文变换，实际 {Describe(value)}");

    static string Describe(object? value) => value?.GetType().Name ?? "null";

    public void CompileTokens()
    {
        var tokenSet = new HashSet<string>([
            ..GetKeys(),
            ..lexicon.Keys,
        ]);
        tokens = tokenSet
            .Where(t => !string.IsNullOrEmpty(t))
            .OrderByDescending(t => t.Length)
            .ToArray();
        trie=null;        // tokens 变了，前缀树作废
    }

    /// <summary>
    /// 分词：从左到右贪心取最长匹配，读不动就整体判失败（返回 null）。
    /// <para>
    /// 词条表按长度降序排列，"第一个命中的词条"就是该位置上最长的词条。
    /// 相邻词条之间的公共前后缀<b>不做</b>任何合并：两个词条首尾相接写成的串就是它们的直接拼接。
    /// 例：词条表 {abc, bcd} 里 "abcbcd" 读作 [abc, bcd]；而 "abcd" 在 'd' 处读不动，返回 null。
    /// </para>
    /// <para>
    /// 代价是同一串可能有多种读法时只取贪心的那一种（例如 {ab, b} 下 "ab" 读作 [ab] 而不是其它切法）；
    /// 好处是它与"边画边显示"用的前缀版 <see cref="TokenizePrefix"/> 是同一套规则，
    /// 不需要额外约定就能保证"看到的读法 = 提交时会被接受的读法"。
    /// </para>
    /// </summary>
    public List<string>? Tokenize(string input)
    {
        var segment=TokenizePrefix(input,out var consumed);
        // 空输入返回空切分而不是失败；末尾有读不动的残片就整体失败
        return consumed==input.Length?segment:null;
    }

    /// <summary>
    /// 分词的前缀版本：从左到右贪心取最长匹配，读不动就停在那个位置。
    /// 并用 <paramref name="consumed"/> 输出已经覆盖到的字符数（末尾那段残片不算）。
    /// 连一个词都读不出时返回空表、<paramref name="consumed"/> 为 0。
    /// <para>
    /// 这是给"边画边显示"用的：<paramref name="consumed"/> 等于输入长度时结果与
    /// <see cref="Tokenize"/> 完全相同，所以界面上显示的词序就是提交时会被接受的那一种，
    /// 不会出现"看着已经成词、却填不进槽"。
    /// </para>
    /// </summary>
    public List<string> TokenizePrefix(string input,out int consumed)
    {
        var segment=new List<string>();
        consumed=0;
        if(string.IsNullOrEmpty(input)||tokens.Length==0) return segment;
        while(consumed<input.Length)
        {
            var matched=-1;
            for(var index=0;index<tokens.Length;index++)   // 词条按长度降序，第一个命中即最长匹配
            {
                if(Matches(index,input,consumed))
                {
                    matched=index;
                    break;
                }
            }
            if(matched<0)
            {
                break;   // 这个位置读不出任何词条：从这儿起都是残片
            }
            segment.Add(tokens[matched]);
            consumed+=tokens[matched].Length;
        }
        return segment;
    }

    /// <summary>词条 <paramref name="index"/> 能否正好落在 <paramref name="input"/> 的 [start, start+len)。</summary>
    bool Matches(int index,string input,int start)
    {
        var token=tokens[index];
        return start>=0
            && start+token.Length<=input.Length
            && input.AsSpan(start,token.Length).SequenceEqual(token);
    }

    /// <summary>
    /// 一次性预测：返回所有"补齐就能成词"的候选词条名。
    /// 判据见 <see cref="PrefixTrie.Results"/>——已画出的这一截须是词条的一段连续子串、词条更长，
    /// 且该词条不能已经被整段画在输入里。结果按词条长度降序，取第一个即最长匹配；
    /// 无匹配、空输入或尚未 <see cref="CompileTokens"/> 时为空。
    /// 这里只要词名；需要偏移与笔画数（界面据此画候补）请用
    /// <see cref="CreatePrediction"/> 看 <see cref="Prediction.PredictionResult"/>。
    /// </summary>
    public IReadOnlyList<string> Predict(string input)
    {
        if(string.IsNullOrEmpty(input)||tokens.Length==0) return [];
        var prediction=CreatePrediction();
        foreach(var c in input)
        {
            prediction.PushChar(c);
        }
        return [..prediction.PredictionResult.Select(match=>match.Token)];
    }

    /// <summary>
    /// 取一个新的增量预测器：逐字符喂入，<see cref="Prediction.PredictionResult"/> 随之刷新。
    /// 返回的预测器绑定当前词条索引（前缀树 + 子串表），<see cref="CompileTokens"/> 之后需重新获取。
    /// </summary>
    public Prediction CreatePrediction()
    {
        trie??=PrefixTrie.Build(tokens);
        return new Prediction(trie);
    }

    /// <summary>CYK 分析，返回起始符号上的全部候选解析。</summary>
    public HashSet<Word> Parse(List<string>? tokens)
    {
        var spells=new HashSet<Word>();
        if (tokens==null||tokens.Count == 0) return spells;
        var n = tokens.Count;
        var words = new HashSet<Word>[n * n];
        for (int i = 0; i < n; i++)
        {
            var tok = tokens[i];
            var cell = new HashSet<Word>();
            // 一个词形可能身兼多个范畴，每个范畴都是一条终结产生式
            foreach (var symbol in SymbolsOf(tok))
            {
                var factory = Grammar.TerminalValue(symbol, tok)
                    ?? throw new InvalidOperationException($"符号 {symbol} 没有绑定终结符 {tok}");
                cell.Add(new Word(symbol, factory(this)));
            }
            // 即使一个范畴都没有也必须写入空表，否则槽位为 null，后续遍历会抛 NullReferenceException
            words[i * n + i] = cell;
        }
        for (int l = 2; l <= n; l++)
        {
            for (int i = 0; i + l <= n; i++)
            {
                var v = new HashSet<Word>();
                // 二元规则：一个切分点 k，左右两段
                for (int k = i; k < i + l - 1; k++)
                {
                    foreach (var y in words[i * n + k])
                    {
                        foreach (var z in words[(k + 1) * n + (i + l - 1)])
                        {
                            foreach (var rule in Grammar.RulesFor(y.Symbol, z.Symbol))
                            {
                                var compose = rule.Compose
                                    ?? throw new InvalidOperationException($"产生式 {rule.Pattern} 没有动作");
                                v.Add(new Word(rule.Result, compose(this, y.Value, z.Value)));
                            }
                        }
                    }
                }
                words[i * n + (i + l - 1)] = v;
            }
        }
        foreach (var word in words[n - 1])
        {
            if (word.Symbol == StartSymbol)
            {
                spells.Add(word);
            }
        }
        return spells;
    }

    /// <summary>某个词形可作为终结符出现的全部范畴。</summary>
    IEnumerable<string> SymbolsOf(string token)
        => lexicon.TryGetValue(token, out var symbols) ? symbols : [];
}

/// <summary>
/// 词条索引：一棵词条前缀树 + 一张"词条的全部真子串 → (词条, 偏移)"的表。
/// 走步用前者判断"到这儿为止能不能切成若干完整词条"（<see cref="ConsumedEnds"/>），
/// 查表用后者回答"已画出的这一截是哪些词条里的一段、落在第几位"（<see cref="Results"/>）。
/// <para>
/// 树上的失败链机制（<see cref="Node.Fail"/>、<see cref="Step"/>、<see cref="Close"/>）目前没有调用者，
/// 留作以后做增量匹配用；每层的节点都代表某个词条的一个前缀。
/// </para>
/// </summary>
class PrefixTrie
{
    /// <summary>词条里的一段：<paramref name="Offset"/> 是这一段的起点在词条里的位置（UTF-16 下标）。</summary>
    readonly record struct Occurrence(string Token,int Offset);

    sealed class Node
    {
        public readonly Dictionary<Rune,int> Next=[];
        /// <summary>以该节点代表的前缀结尾的词条（可能不止一个，例如同一词形重复登记）。</summary>
        public readonly List<string> Terms=[];
        public int Fail;
        /// <summary>节点代表的前缀长度，用于判定"真后缀"。</summary>
        public int Depth;
        /// <summary>子树中终止节点的最大深度；大于 <see cref="Depth"/> 表示存在更长的词以本前缀开头。</summary>
        public int Deepest;
    }

    readonly List<Node> nodes=[];

    /// <summary>词条的全部真子串 → 它在哪些词条里出现、出现在第几位（同一子串在同一个词条里出现多次就登记多条，偏移升序）。</summary>
    readonly Dictionary<string,List<Occurrence>> occurrences=new(StringComparer.Ordinal);

    public static PrefixTrie Build(string[] tokens)
    {
        var trie=new PrefixTrie();
        trie.nodes.Add(new Node());   // 0 号即根节点，全树只用这一份
        foreach(var token in tokens)
        {
            trie.Insert(token);
            trie.Index(token);
        }
        trie.LinkFailures();
        trie.MarkDeepest(0);
        return trie;
    }

    void Insert(string token)
    {
        var node=0;
        foreach(var rune in token.EnumerateRunes())
        {
            if(!nodes[node].Next.TryGetValue(rune,out var next))
            {
                next=nodes.Count;
                nodes[node].Next[rune]=next;
                nodes.Add(new Node { Depth=nodes[node].Depth+1 });
            }
            node=next;
        }
        nodes[node].Terms.Add(token);
    }

    /// <summary>
    /// 登记一个词条的全部<b>真子串</b>（整词不算它自己的子串，因为画满了就不成其为预测）。
    /// 预测时拿"已画出的那一截"直接查表，既省掉每次按键扫全表的子串搜索，又顺带拿到偏移——
    /// 界面要用它回推整个词条的根旋转。代理对中间的位置不登记，避免把码位切成两半。
    /// </summary>
    void Index(string token)
    {
        for(var offset=0;offset<token.Length;offset++)
        {
            if(char.IsLowSurrogate(token[offset]))
            {
                continue;   // 起点落在代理对中间
            }
            for(var length=1;length<=token.Length-offset;length++)
            {
                if(offset==0&&length==token.Length)
                {
                    continue;   // 整词本身
                }
                if(char.IsHighSurrogate(token[offset+length-1]))
                {
                    continue;   // 终点落在代理对中间
                }
                var piece=token.Substring(offset,length);
                if(!occurrences.TryGetValue(piece,out var found))
                {
                    found=[];
                    occurrences[piece]=found;
                }
                found.Add(new Occurrence(token,offset));
            }
        }
    }

    /// <summary>BFS 建失败链：fail(x) 是 x 的最长真后缀，且本身也是树上一个节点。</summary>
    void LinkFailures()
    {
        var queue=new Queue<int>();
        foreach(var (_,child) in nodes[0].Next)
        {
            nodes[child].Fail=0;
            queue.Enqueue(child);
        }
        while(queue.Count>0)
        {
            var node=queue.Dequeue();
            foreach(var (rune,child) in nodes[node].Next)
            {
                var fallback=nodes[node].Fail;
                while(fallback!=0&&!nodes[fallback].Next.ContainsKey(rune))
                {
                    fallback=nodes[fallback].Fail;
                }
                nodes[child].Fail=nodes[fallback].Next.TryGetValue(rune,out var target)&&target!=child
                    ? target
                    : 0;
                queue.Enqueue(child);
            }
        }
    }

    /// <summary>自底向上算出每个节点子树中终止节点的最大深度。</summary>
    void MarkDeepest(int node)
    {
        var deepest=nodes[node].Terms.Count>0?nodes[node].Depth:0;
        foreach(var (_,child) in nodes[node].Next)
        {
            MarkDeepest(child);
            deepest=Math.Max(deepest,nodes[child].Deepest);
        }
        nodes[node].Deepest=deepest;
    }

    /// <summary>读入一个字符：转移 + 补空后缀 + 失败链封闭。</summary>
    internal HashSet<int> Step(HashSet<int> active,Rune rune)
    {
        var extended=new HashSet<int> { 0 };   // 空前缀永远是后缀
        foreach(var node in active)
        {
            if(nodes[node].Next.TryGetValue(rune,out var next))
            {
                extended.Add(next);
            }
        }
        return Close(extended);
    }

    /// <summary>沿失败链取闭包，使活跃集合对"真后缀"封闭。</summary>
    internal HashSet<int> Close(IEnumerable<int> seeds)
    {
        var closed=new HashSet<int>();
        var stack=new Stack<int>(seeds);
        while(stack.Count>0)
        {
            var node=stack.Pop();
            if(!closed.Add(node)) continue;
            if(node!=0) stack.Push(nodes[node].Fail);
        }
        return closed;
    }

    /// <summary>
    /// 答案。
    /// 判据：存在一个<b>可分词的位置</b> i（s[0..i) 能切成完整词条），使 s[i..] 是词条 t 的<b>一段连续子串</b>，
    /// 且 t 比它长——已经画出来的这一截是词条的一部分，补齐剩下的笔画就能成词。
    /// 从最长的那一截开始找，<b>第一截有候选的就定下来</b>，这样每条结果都对应"手上正画着的这一截"。
    /// <para>
    /// 每条结果带三个数：<see cref="PredictionMatch.Offset"/> = 这一截落在词条第几位、
    /// <see cref="PredictionMatch.MatchedLength"/> = 这一截的长度（界面据此回推整词的根旋转）、
    /// <see cref="PredictionMatch.RemainingLength"/> = 词条里还没画出来的笔画数。
    /// 同一个词条可能匹配多处（这一截在词条里出现不止一次），取<b>偏移最小</b>的那一次：偏移 0 时界面照直画，
    /// 偏移大于 0 时界面沿词条开头的转折回退若干步得到整词朝向。
    /// </para>
    /// <para>
    /// <b>偏移大于 0 的候补接着画也得不到</b>，界面据此只显示 <see cref="PredictionMatch.FromStart"/> 的那些：
    /// 要么词条开头那几笔还没画、补不到这一截前面去，要么那个起点落在不可分词的位置上（读不出这个词）。
    /// 反过来说，若它开头那几笔已经画在一个可分词的位置上，那么更靠前的那个起点上就会报出这一截更长的前缀
    /// （以偏移 0 的形式），扫描根本走不到现在的这一截。
    /// </para>
    /// <para>
    /// <b>已经整段画出来的词条不算预测</b>：若这一截本身就是词条的尾巴、且它前面的笔画正好补齐了词条的开头
    /// （即整个词条就落在已画串的末尾），说明玩家已经把这个词画完了，"还差几笔"没有意义。
    /// 例：词条表 {a, b, ab} 下 "ab" → 空（"b" 是 "ab" 的一段，但前面的 "a" 已经把词条补齐了）。
    /// </para>
    /// 例：词条表 {abbc, cbbc} 下 "ab"→{abbc（偏移 0，已画 2，还差 2）}；"bbc"→{abbc、cbbc（偏移都是 1，
    /// 已画 3，还差 1——缺的这一笔在两个词条的<b>开头</b>，接着往后画补不上）}；
    /// "abc"→空（"a"/"ab" 都不是词条，"abc" 也不是任何词条的子串）；"abbc"→空（已经画满了）。
    /// 结果按词条长度降序，每个词条至多一条。
    /// </summary>
    internal IReadOnlyList<PredictionMatch> Results(string input)
    {
        var reachable=Reachable(input);
        var chosen=new Dictionary<string,PredictionMatch>(StringComparer.Ordinal);
        for(var start=0;start<input.Length;start++)   // tail 必须非空，start=0（整串就是那一截）是合法起点
        {
            if(!reachable[start]) continue;
            var tail=input[start..];
            if(!occurrences.TryGetValue(tail,out var found)) continue;
            // 这一截是哪些词条里的一段：按可分词位置从长到短试，命中最长的那一截就收工
            foreach(var occurrence in found)
            {
                if(AlreadyDrawn(input,start,tail.Length,occurrence)) continue;
                if(chosen.TryGetValue(occurrence.Token,out var previous)&&previous.Offset<=occurrence.Offset)
                {
                    continue;   // 同一个词条已经取了偏移更小的那一次
                }
                chosen[occurrence.Token]=new PredictionMatch(occurrence.Token,tail.Length,occurrence.Offset);
            }
            if(chosen.Count>0) break;
        }
        return [.. chosen.Values.OrderByDescending(m=>m.Token.Length)
            .ThenBy(m=>m.Token,StringComparer.Ordinal)];
    }

    /// <summary>
    /// 这一截连同它前面的笔画，是不是已经把整个词条画在已画串的末尾了。
    /// 成立时"还差几笔"无从谈起——缺的那几笔本来就该画在这一截前面，而它们已经在输入里。
    /// </summary>
    static bool AlreadyDrawn(string input,int start,int tailLength,Occurrence occurrence)
    {
        var token=occurrence.Token;
        // 这一截必须是词条的尾巴，词条才可能整段落在已画串里（已画串到这一截就结束了）
        if(occurrence.Offset+tailLength!=token.Length) return false;
        // 词条开头落在输入之前：缺的就是那几笔，接不上去，仍要报出来
        if(start<occurrence.Offset) return false;
        // 词条恰好是已画串末尾的那一段
        return input.AsSpan(start-occurrence.Offset).SequenceEqual(token);
    }

    /// <summary>
    /// 可达位置表：<c>result[end]</c> 表示 s[0..end] 能切成若干个完整词条。
    /// 标准的从左到右分词 DP，O(n²·L)，取代原先对每个 end 重新展开的指数级递归。
    /// </summary>
    bool[] Reachable(string s)
    {
        var reachable=new bool[s.Length+1];
        reachable[0]=true;   // 空前缀平凡可分
        for(var start=0;start<s.Length;start++)
        {
            if(!reachable[start]) continue;
            foreach(var end in ConsumedEnds(s,start))
            {
                reachable[end]=true;
            }
        }
        return reachable;
    }

    /// <summary>
    /// 从 start 起恰好是一个词条的所有结束位置；顺着 trie 走，遇终止节点即命中。
    /// 与建树一样<b>按码位走步</b>：代理对整体当一个字符，落单的代理项判定"此处不成词"。
    /// 返回的下标是 UTF-16 位置，且一定落在码位边界上，可以直接拿来切 <c>s[i..]</c>。
    /// </summary>
    IEnumerable<int> ConsumedEnds(string s,int start)
    {
        var node=0;
        var index=start;
        while(index<s.Length)
        {
            if(!Rune.TryGetRuneAt(s,index,out var rune)||!nodes[node].Next.TryGetValue(rune,out var next))
            {
                yield break;
            }
            node=next;
            index+=rune.Utf16SequenceLength;
            if(nodes[node].Terms.Count>0)
            {
                yield return index;
            }
        }
    }
}

