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

    /// <summary>
    /// 边界合并表：<c>boundaries[i][j]</c> 是 tokens[i] 的真后缀与 tokens[j] 的真前缀重合的最大长度，没有则为 0。
    /// 与 <see cref="trie"/> 一样在 <see cref="CompileTokens"/> 之后作废，下次分词时重建。
    /// </summary>
    int[][]? boundaries;

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
        boundaries=null;  // 边界合并表同理
    }

    /// <summary>
    /// 分词：把输入切成一个词条序列。相邻词条的公共前后缀<b>强制合并</b>——
    /// 前一个词条的真后缀若等于后一个词条的真前缀，这段重叠只写一次，
    /// 于是后一个词条必须从重叠处开始，而不是接在前一个词条末尾。
    /// 把这样一对词条首尾相接写成的"未合并"形式不是合法字符串。
    /// <para>
    /// 例：词条表 {abc, bcd} 里，"abcd" 是 abc 与 bcd 合并公共的 "bc" 后的写法，
    /// 故解析为 [abc, bcd]；"abcbcd" 是二者首尾相接的写法（重叠没有合并），故解析失败。
    /// </para>
    /// <para>
    /// 每一步按词条表顺序（长的在前）尝试，失败时回溯；先试"接在已覆盖位置继续读"的词条，
    /// 都不行才试"重叠续读"的词条。于是不含未合并边界的输入结果与逐段最长匹配完全一致，
    /// 重叠只在原样读不下去时才启用。同一输入存在多种合法切分时（词条互为前后缀之类的歧义表）
    /// 取这个次序下的第一个。无解返回 null。
    /// </para>
    /// </summary>
    public List<string>? Tokenize(string input)
    {
        // 空输入没有任何词条，与旧实现一致地返回空切分而不是失败
        if (string.IsNullOrEmpty(input)) return [];
        var table=BoundaryTable();
        var path=new List<int>();
        // 已经证明走不通的 (前一词条, 已覆盖到的位置)；状态只由这两项决定，故可安全记忆
        var dead=new HashSet<(int Last, int End)>();

        // 词条 index 能否正好落在 [start, start+len)
        bool Occurs(int index,int start)
        {
            var token=tokens[index];
            return start>=0
                && start+token.Length<=input.Length
                && input.AsSpan(start,token.Length).SequenceEqual(token);
        }

        // 刚匹配完 tokens[last]、输入已覆盖到 end（不含）；挑下一个词条直到恰好用完
        bool Extend(int last,int end)
        {
            if(end==input.Length) return true;
            if(!dead.Add((last,end))) return false;
            // 两轮：先试"接在覆盖到的位置继续读"（与前一词条没有公共边界），
            // 再试"重叠续读"（起点提前到公共边界处）。
            // 前者优先，使本来就不含未合并边界的切分原样返回：只有读不下去时才动用重叠。
            for(var pass=0;pass<2;pass++)
            {
                for(var next=0;next<tokens.Length;next++)
                {
                    var boundary=table[last][next];
                    if((boundary!=0)!=(pass==1)) continue;
                    // 强制合并：起点由两个词条的最大公共边界唯一确定
                    var start=end-boundary;
                    if(!Occurs(next,start)) continue;
                    path.Add(next);
                    if(Extend(next,start+tokens[next].Length)) return true;
                    path.RemoveAt(path.Count-1);
                }
            }
            return false;
        }

        for(var first=0;first<tokens.Length;first++)
        {
            // 首词条只能从 0 开始：它前面没有词条可供合并
            if(!Occurs(first,0)) continue;
            path.Add(first);
            if(Extend(first,tokens[first].Length)) return [..path.Select(i=>tokens[i])];
            path.RemoveAt(path.Count-1);
        }
        return null;
    }

    /// <summary>取（首次使用时构建）边界合并表。</summary>
    int[][] BoundaryTable()
    {
        if(boundaries is not null) return boundaries;
        var table=new int[tokens.Length][];
        for(var i=0;i<tokens.Length;i++)
        {
            table[i]=new int[tokens.Length];
            for(var j=0;j<tokens.Length;j++)
            {
                table[i][j]=CommonBoundary(tokens[i],tokens[j]);
            }
        }
        return boundaries=table;
    }

    /// <summary>
    /// 两个词条的最大公共边界：a 的真后缀同时是 b 的真前缀时的最长重合长度，没有则为 0。
    /// 上界取较短词条的长度减一，因为两边都必须是<b>真</b>前缀/真后缀：
    /// 取满会让 b 完全落在 a 的覆盖范围内（重合后位置不前进），或让 b 与前一个词条本身同形
    /// （与"单个词条"的读法冲突）。
    /// </summary>
    static int CommonBoundary(string a,string b)
    {
        for(var k=Math.Min(a.Length,b.Length)-1;k>0;k--)
        {
            if(string.CompareOrdinal(a,a.Length-k,b,0,k)==0) return k;
        }
        return 0;
    }

    /// <summary>
    /// 一次性前缀预测：返回所有 t，使 t 的某个真前缀是 <paramref name="input"/> 的真后缀（S 即 <see cref="tokens"/>）。
    /// 结果按词条长度降序，取第一个即最长匹配；无匹配、空输入或尚未 <see cref="CompileTokens"/> 时为空。
    /// 需要逐字符增量预测请用 <see cref="CreatePrediction"/>。
    /// </summary>
    public IReadOnlyList<string> Predict(string input)
    {
        if(string.IsNullOrEmpty(input)||tokens.Length==0) return [];
        var prediction=CreatePrediction();
        foreach(var c in input)
        {
            prediction.PushChar(c);
        }
        return prediction.PredictionResult;
    }

    /// <summary>
    /// 取一个新的增量预测器：逐字符喂入，<see cref="Prediction.PredictionResult"/> 随之刷新。
    /// 返回的预测器绑定当前前缀树，<see cref="CompileTokens"/> 之后需重新获取。
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
/// 词条前缀树 + 失败链，用于"NFA 式"的最长后缀匹配。
/// 每个节点都代表某个词条的一个前缀，因此扫描结束时的活跃节点恰好就是
/// "是 input 的后缀、又是某词条前缀"的那些串。
/// </summary>
class PrefixTrie
{
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

    public static PrefixTrie Build(string[] tokens)
    {
        var trie=new PrefixTrie();
        trie.nodes.Add(new Node());   // 0 号即根节点，全树只用这一份
        foreach(var token in tokens)
        {
            trie.Insert(token);
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
    /// 判据：存在一个<b>可分词的位置</b> i（s[0..i] 能切成完整词条），使 s[i..] 是词条 t 的<b>真前缀</b>。
    /// 也就是说，补全 t 之后剩下的部分必须拼成一个合法字符串；像 "cattl" 里那个 "l" 不算，
    /// 因为 "cat" 已吃掉前三个字符，它前面还压着 "t"。
    /// <para>
    /// 实现上有两处关键：<b>(1)</b> 先用 trie 走查剪枝——走不通的 tail 立刻跳过，不必去算可达性；
    /// <b>(2)</b> 可达位置用一次动态规划算清后查表，而不是对每个 i 重新递归展开（后者是指数级的）。
    /// </para>
    /// 结果按词条长度降序。
    /// </summary>
    internal IReadOnlyList<string> Results(string input)
    {
        var reachable=Reachable(input);
        var hits=new HashSet<string>();
        for(var i=0;i<=input.Length;i++)   // tail 必须非空，i=0（整个输入就是前缀）是合法起点
        {
            var tail=input[i..];
            if(tail.Length==0) continue;
            // 从根沿 tail 走；走不动就说明没有词条以 tail 为前缀（便宜的先做）
            var node=0;
            foreach(var rune in tail.EnumerateRunes())
            {
                if(!nodes[node].Next.TryGetValue(rune,out var next))
                {
                    node=-1;
                    break;
                }
                node=next;
            }
            if(node<0) continue;
            if(!reachable[i]) continue;
            // tail 本身若不是词条（或只是同名的更短条目），子树里就是所有合法补全
            foreach(var (_,child) in nodes[node].Next)
            {
                Gather(child,hits);
            }
        }
        return [.. hits.OrderByDescending(t => t.Length)];
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

    /// <summary>从 start 起恰好是一个词条的所有结束位置；顺着 trie 走，遇终止节点即命中。</summary>
    IEnumerable<int> ConsumedEnds(string s,int start)
    {
        var node=0;
        for(var i=start;i<s.Length;i++)
        {
            // 代理对：无法单独构成码位的 char 直接判定"此处不成词"（原实现在这里会抛异常）
            if(!Rune.TryCreate(s[i],out var rune)||!nodes[node].Next.TryGetValue(rune,out var next))
            {
                yield break;
            }
            node=next;
            if(nodes[node].Terms.Count>0)
            {
                yield return i+1;
            }
        }
    }

    /// <summary>收集以 node 为根的子树的全部词条。</summary>
    void Gather(int node,HashSet<string> into)
    {
        foreach(var term in nodes[node].Terms)
        {
            into.Add(term);
        }
        foreach(var (_,child) in nodes[node].Next)
        {
            Gather(child,into);
        }
    }
}

