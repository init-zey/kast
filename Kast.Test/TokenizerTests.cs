using Kast.Core;

namespace Kast.Test;

/// <summary>
/// 分词回归验证：相邻词条的公共前后缀必须强制合并
/// （前一个词条的真后缀 = 后一个词条的真前缀时，这段重叠只写一次）。
/// 由 <see cref="Entry"/> 调用，返回进程退出码。
/// </summary>
static class TokenizerTests
{
    static readonly List<string> failures=[];
    static int checks;

    public static int Run()
    {
        Console.WriteLine("—— 分词（公共前后缀强制合并）验证 ——");
        SpecExamples();
        Chains();
        Unaffected();
        PinnedPredictions();
        Sweep();
        GameLike();
        Stress();
        Console.WriteLine(failures.Count==0
            ? $"分词验证：{checks} 项检查全部通过"
            : $"分词验证：{checks} 项检查，{failures.Count} 项失败");
        foreach(var failure in failures)
        {
            Console.WriteLine("  失败："+failure);
        }
        return failures.Count==0?0:1;
    }

    /// <summary>需求给出的两个用例。</summary>
    static void SpecExamples()
    {
        var a=Lexicon("abc","bcd");
        Show("abc,bcd","abcd",a);
        Show("abc,bcd","abcbcd",a);
        Eq("{abc,bcd}：abcd 是 abc 与 bcd 合并 \"bc\" 的写法",a.Tokenize("abcd"),"abc","bcd");
        Null("{abc,bcd}：abcbcd 是 abc 与 bcd 首尾相接，重叠没有合并",a.Tokenize("abcbcd"));
        // 单个词条不受影响
        Eq("{abc,bcd}：abc",a.Tokenize("abc"),"abc");
        Eq("{abc,bcd}：bcd",a.Tokenize("bcd"),"bcd");
        // 合并只发生一次，后面再出现的 bcd 与前者没有公共边界，正常相接
        Eq("{abc,bcd}：abcdbcd",a.Tokenize("abcdbcd"),"abc","bcd","bcd");
        Null("{abc,bcd}：abcbd（重叠处对不上）",a.Tokenize("abcbd"));
    }

    /// <summary>多词条的合并链，以及"能接就读、接不上才重叠"的取舍。</summary>
    static void Chains()
    {
        var two=Lexicon("ab","bc");
        Eq("{ab,bc}：abc 共享 b",two.Tokenize("abc"),"ab","bc");
        Null("{ab,bc}：abbc 未合并",two.Tokenize("abbc"));

        // 三级链：每一级都只能靠重叠接上，且没有别的读法
        var chain=Lexicon("ab","bcd","de");
        Show("ab,bcd,de","abcde",chain);
        Eq("{ab,bcd,de}：abcde 是三级合并链",chain.Tokenize("abcde"),"ab","bcd","de");

        // 歧义表：{ab,ba} 下 "abab" 既能读成 ab+ab（相接），也能读成 ab+ba+ab（合并两次）。
        // 取前者：先把输入按原样读下去，读不动了才动用重叠。
        var swap=Lexicon("ab","ba");
        Show("ab,ba","abab",swap);
        Eq("{ab,ba}：abab 取不重叠的读法",swap.Tokenize("abab"),"ab","ab");
        Null("{ab,ba}：abba",swap.Tokenize("abba"));

        // 同一取舍：{ab,bc,cd} 下 "abcd" 也可以读成 ab+bc+cd，但 ab+cd 不需要重叠，故取后者
        var three=Lexicon("ab","bc","cd");
        Show("ab,bc,cd","abcd",three);
        Eq("{ab,bc,cd}：abcd 取不重叠的读法",three.Tokenize("abcd"),"ab","cd");
    }

    /// <summary>没有公共边界的词条表：行为与逐段最长匹配一致。</summary>
    static void Unaffected()
    {
        var a=Lexicon("dog","cat","look","cattle","lock");
        Eq("{dog,cat,look,cattle,lock}：dogcat",a.Tokenize("dogcat"),"dog","cat");
        Eq("{dog,cat,look,cattle,lock}：catcattle",a.Tokenize("catcattle"),"cat","cattle");
        Null("{dog,cat,look,cattle,lock}：cattlock",a.Tokenize("cattlock"));

        // 单字符词条之间不可能有真前后缀重叠，中文分词完全不受影响
        var cn=Lexicon("我","看","你");
        Eq("中文：我看你",cn.Tokenize("我看你"),"我","看","你");
        Eq("中文：我看",cn.Tokenize("我看"),"我","看");
        Null("中文：我瞧",cn.Tokenize("我瞧"));
        Eq("空输入返回空切分而不是失败",cn.Tokenize(""),[]);
    }

    /// <summary>预测（前缀树）未被分词的改动影响。</summary>
    static void PinnedPredictions()
    {
        var a=Lexicon("dog","cat","look","cattle","lock");
        Set("Predict(dogca)",a.Predict("dogca"),"cat","cattle");
        Set("Predict(catlo)",a.Predict("catlo"),"look","lock");
        Set("Predict(cat)",a.Predict("cat"),"cattle");
        Set("Predict(cattl)",a.Predict("cattl"),"cattle");
    }

    /// <summary>
    /// 小规模穷举：所有"词条表 × 输入"与参考实现比对是否可接受，
    /// 并检查成功返回的切分在结构上确实满足合并规则；同时与旧的最长匹配实现对照。
    /// </summary>
    static void Sweep()
    {
        (char[] Alphabet,int PoolLength)[] pools=
        [
            (['a','b'],3),
            (['a','b','c'],2),
        ];
        var compared=0;
        var accepted=0;
        var oldOnly=0;          // 旧实现接受、新实现拒绝：应当都是"没合并"的写法
        var merged=0;           // 旧实现拒绝、新实现接受，且切分里确实用到了重叠
        var backtracked=0;      // 旧实现拒绝、新实现接受，但切分不含重叠（靠回溯找回来的）

        foreach(var (alphabet,poolLength) in pools)
        {
            var pool=Strings(alphabet,1,poolLength).ToArray();
            var inputs=Strings(alphabet,1,5).ToArray();
            foreach(var lex in Subsets(pool,2).Concat(Subsets(pool,3)))
            {
                var table=Lexicon(lex);
                foreach(var input in inputs)
                {
                    compared++;
                    var segment=table.Tokenize(input);
                    var reference=Accepts(lex,input);
                    if(reference!=(segment is not null))
                    {
                        failures.Add($"[{string.Join("/",lex)}] \"{input}\"：参考实现={(reference?"接受":"拒绝")}，"+
                                     $"实际={(segment is null?"拒绝":"接受 "+string.Join("+",segment))}");
                        continue;
                    }
                    if(segment is null) continue;
                    accepted++;
                    var error=Validate(lex,segment,input);
                    if(error is not null)
                    {
                        failures.Add($"[{string.Join("/",lex)}] \"{input}\"：返回 {string.Join("+",segment)} 非法（{error}）");
                    }
                    // 与旧实现对照
                    var old=OldTokenize(table.Tokens,input);
                    if(old is null)
                    {
                        if(MergeFree(segment)) backtracked++;
                        else merged++;
                    }
                    else if(!MergeFree(old))
                    {
                        oldOnly++;
                    }
                    else if(!segment.SequenceEqual(old))
                    {
                        failures.Add($"[{string.Join("/",lex)}] \"{input}\"：无重叠的切分应与旧实现一致，"+
                                     $"旧 {string.Join("+",old)}，新 {string.Join("+",segment)}");
                    }
                }
            }
        }
        checks++;
        Console.WriteLine($"  穷举比对：{compared} 组（词条表 × 输入），接受 {accepted} 组；"+
                          $"其中 {oldOnly} 组因「未合并」被新规则拒绝；"+
                          $"旧实现漏掉而新实现接受的：重叠续读 {merged} 组，纯回溯 {backtracked} 组");
    }

    /// <summary>
    /// 游戏式的词典：词形是 6 字母表上长度 2–5 的随机串（<c>Database.Gen</c> 就是这么生成的）。
    /// 检查两件事：每个"规范合并写法"都能读回原来的词序；每个"未合并写法"都读不回原来的词序。
    /// 顺带统计这种词典下同一字符串有几种合法读法。
    /// </summary>
    static void GameLike()
    {
        var random=new Random(20240607);
        var pool=Strings(['w','q','a','s','d','e'],2,5).ToArray();
        var lex=pool.OrderBy(_=>random.Next()).Take(40).ToArray();
        var table=Lexicon(lex);

        var overlapPairs=0;
        foreach(var first in lex)
        {
            foreach(var second in lex)
            {
                if(K(first,second)>0) overlapPairs++;
            }
        }
        checks++;
        Console.WriteLine($"  模拟游戏词典：{lex.Length} 个词形，有公共边界的相邻对 {overlapPairs}/{lex.Length*lex.Length}"+
                          $"（只要前一个词的末字母等于后一个词的首字母就有）");

        int trials=0, rejected=0, sameWords=0, otherWords=0, ambiguous=0;
        var mostReadings=0L;
        int unmergedRejected=0, unmergedOther=0;
        for(var n=0;n<500;n++)
        {
            var m=2+random.Next(2);                    // 2–3 个词
            var words=new string[m];
            for(var i=0;i<m;i++) words[i]=lex[random.Next(lex.Length)];
            var merged=words[0];
            var plain=words[0];
            var boundary=false;
            for(var i=1;i<m;i++)
            {
                var k=K(words[i-1],words[i]);
                boundary|=k>0;
                merged+=words[i][k..];                 // 公共边界只写一次
                plain+=words[i];                       // 未合并：整词接上去
            }
            trials++;

            var segment=table.Tokenize(merged);
            if(segment is null)
            {
                rejected++;
                continue;
            }
            if(segment.SequenceEqual(words)) sameWords++;
            else
            {
                otherWords++;
                var error=Validate(lex,segment,merged);
                if(error is not null) failures.Add($"模拟词典：\"{merged}\" 返回非法切分（{error}）");
            }
            var readings=CountReadings(lex,merged);
            if(readings>1) ambiguous++;
            mostReadings=Math.Max(mostReadings,readings);

            if(!boundary) continue;                    // 没有公共边界时未合并写法就是规范写法
            var unmerged=table.Tokenize(plain);
            if(unmerged is null) unmergedRejected++;
            else
            {
                unmergedOther++;
                if(unmerged.SequenceEqual(words))
                {
                    failures.Add($"模拟词典：\"{plain}\" 是未合并写法，却被读成同一个词序");
                }
            }
        }
        checks++;
        if(rejected>0) failures.Add($"模拟词典：{rejected} 个规范合并写法被拒绝，本应全部可读");
        Console.WriteLine($"  规范合并写法 {trials} 例：拒绝 {rejected}（应为 0），读回原词序 {sameWords}，"+
                          $"读成别的合法词序 {otherWords}；有多种读法的 {ambiguous} 例，最多 {mostReadings} 种");
        Console.WriteLine($"  未合并写法：拒绝 {unmergedRejected}，另有 {unmergedOther} 例被读成别的词序（都不是原词序）");
    }

    /// <summary>一个字符串有多少种合法读法（按 (前一词条, 覆盖位置) 记忆化计数）。</summary>
    static long CountReadings(IReadOnlyList<string> lex,string input)
    {
        var memo=new Dictionary<(int Last,int End),long>();
        long Ways(int last,int end)
        {
            if(end==input.Length) return 1;
            if(memo.TryGetValue((last,end),out var known)) return known;
            var total=0L;
            for(var next=0;next<lex.Count;next++)
            {
                var start=end-K(lex[last],lex[next]);
                if(start<0||start+lex[next].Length>input.Length) continue;
                if(!input.AsSpan(start,lex[next].Length).SequenceEqual(lex[next])) continue;
                total+=Ways(next,start+lex[next].Length);
            }
            return memo[(last,end)]=total;
        }
        var sum=0L;
        for(var first=0;first<lex.Count;first++)
        {
            if(first+lex[first].Length>input.Length) continue;
            if(!input.AsSpan(0,lex[first].Length).SequenceEqual(lex[first])) continue;
            sum+=Ways(first,lex[first].Length);
        }
        return sum;
    }

    /// <summary>长输入下的开销：回溯必须靠记忆化收敛，不能指数爆炸。</summary>
    static void Stress()
    {
        var lex=Strings(['a','b'],1,4).ToArray();   // 30 个词条，彼此共享大量前后缀
        var table=Lexicon(lex);
        foreach(var input in new[] { new string('a',400), new string('a',400)+"c" })
        {
            var watch=System.Diagnostics.Stopwatch.StartNew();
            var segment=table.Tokenize(input);
            watch.Stop();
            checks++;
            var description=segment is null?"拒绝":$"{segment.Count} 个词条";
            Console.WriteLine($"  压力：{lex.Length} 词条 × {input.Length} 字符 → {description}，{watch.Elapsed.TotalMilliseconds:F2} ms");
            if(watch.Elapsed.TotalMilliseconds>2000)
            {
                failures.Add($"压力用例（{input.Length} 字符）耗时 {watch.Elapsed.TotalMilliseconds:F1} ms，超出预期");
            }
            if(segment is not null)
            {
                var error=Validate(lex,segment,input);
                if(error is not null) failures.Add("压力用例返回非法切分："+error);
            }
        }
    }

    // ————— 参考实现：按定义穷举，不做记忆化、不排序、不剪枝 —————

    /// <summary>是否存在满足合并规则的词条序列。</summary>
    static bool Accepts(IReadOnlyList<string> lex,string input)
    {
        bool Walk(string? last,int end,int depth)
        {
            if(last is not null&&end==input.Length) return true;
            if(depth>input.Length) return false;
            foreach(var token in lex)
            {
                var start=last is null?0:end-K(last,token);
                if(start<0||start+token.Length>input.Length) continue;
                if(!input.AsSpan(start,token.Length).SequenceEqual(token)) continue;
                if(Walk(token,start+token.Length,depth+1)) return true;
            }
            return false;
        }
        return Walk(null,0,0);
    }

    /// <summary>检查切分是否满足合并规则；合法返回 null。</summary>
    static string? Validate(IReadOnlyList<string> lex,IReadOnlyList<string> segment,string input)
    {
        if(segment.Count==0) return "空切分";
        var end=0;
        for(var i=0;i<segment.Count;i++)
        {
            var token=segment[i];
            if(!lex.Contains(token)) return $"词条 {token} 不在词条表里";
            var start=i==0?0:end-K(segment[i-1],token);
            if(start<0) return $"词条 {token} 起点为负";
            if(start+token.Length>input.Length) return $"词条 {token} 越过输入末尾";
            if(!input.AsSpan(start,token.Length).SequenceEqual(token)) return $"输入第 {start} 位起不是词条 {token}";
            var next=start+token.Length;
            if(next<=end) return $"词条 {token} 没有前进";
            end=next;
        }
        return end==input.Length?null:$"只覆盖到 {end}/{input.Length}";
    }

    /// <summary>相邻词条之间都没有公共边界，即"未合并"担心的情况不存在。</summary>
    static bool MergeFree(IReadOnlyList<string> segment)
    {
        for(var i=1;i<segment.Count;i++)
        {
            if(K(segment[i-1],segment[i])!=0) return false;
        }
        return true;
    }

    /// <summary>a 的真后缀同时是 b 的真前缀时的最长长度，没有则为 0。</summary>
    static int K(string a,string b)
    {
        for(var k=Math.Min(a.Length,b.Length)-1;k>0;k--)
        {
            if(string.CompareOrdinal(a,a.Length-k,b,0,k)==0) return k;
        }
        return 0;
    }

    /// <summary>改动前的逐段最长匹配实现，用作对照。</summary>
    static List<string>? OldTokenize(IReadOnlyList<string> tokens,string input)
    {
        var result=new List<string>();
        var i=0;
        while(i<input.Length)
        {
            string? matched=null;
            foreach(var token in tokens)
            {
                if(i+token.Length<=input.Length&&string.CompareOrdinal(input,i,token,0,token.Length)==0)
                {
                    matched=token;
                    break;
                }
            }
            if(matched is null) return null;
            result.Add(matched);
            i+=matched.Length;
        }
        return result;
    }

    // ————— 工具 —————

    static Alphabet<string> Lexicon(params string[] tokens)
    {
        var alphabet=new Alphabet<string>();
        foreach(var token in tokens)
        {
            alphabet.AddWord(token,"N",token);
        }
        alphabet.CompileTokens();
        return alphabet;
    }

    /// <summary>字母表上长度在 [min,max] 内的所有串。</summary>
    static IEnumerable<string> Strings(char[] alphabet,int min,int max)
    {
        for(var length=min;length<=max;length++)
        {
            var total=1;
            for(var i=0;i<length;i++) total*=alphabet.Length;
            for(var n=0;n<total;n++)
            {
                var chars=new char[length];
                var value=n;
                for(var i=0;i<length;i++)
                {
                    chars[i]=alphabet[value%alphabet.Length];
                    value/=alphabet.Length;
                }
                yield return new string(chars);
            }
        }
    }

    /// <summary>pool 中所有大小为 size 的子集。</summary>
    static IEnumerable<string[]> Subsets(string[] pool,int size)
    {
        var index=new int[size];
        for(var i=0;i<size;i++) index[i]=i;
        while(true)
        {
            yield return [..index.Select(i=>pool[i])];
            var p=size-1;
            while(p>=0&&index[p]==pool.Length-size+p) p--;
            if(p<0) yield break;
            index[p]++;
            for(var q=p+1;q<size;q++) index[q]=index[q-1]+1;
        }
    }

    static void Show(string lexicon,string input,Alphabet<string> table)
    {
        var segment=table.Tokenize(input);
        Console.WriteLine($"  {{{lexicon}}} \"{input}\" → "+
                          (segment is null?"解析失败":string.Join("+",segment)));
    }

    static void Eq(string what,List<string>? actual,params string[] expected)
    {
        checks++;
        if(actual is null)
        {
            failures.Add($"{what}：期望 [{string.Join(" ",expected)}]，实际解析失败");
            return;
        }
        if(!actual.SequenceEqual(expected))
        {
            failures.Add($"{what}：期望 [{string.Join(" ",expected)}]，实际 [{string.Join(" ",actual)}]");
        }
    }

    static void Null(string what,List<string>? actual)
    {
        checks++;
        if(actual is not null)
        {
            failures.Add($"{what}：期望解析失败，实际 [{string.Join(" ",actual)}]");
        }
    }

    static void Set(string what,IReadOnlyList<string> actual,params string[] expected)
    {
        checks++;
        var sorted=actual.OrderBy(t=>t,StringComparer.Ordinal).ToArray();
        if(!sorted.SequenceEqual(expected.OrderBy(t=>t,StringComparer.Ordinal)))
        {
            failures.Add($"{what}：期望 {{{string.Join(",",expected)}}}，实际 {{{string.Join(",",sorted)}}}");
        }
    }
}
