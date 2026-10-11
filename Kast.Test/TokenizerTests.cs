using Kast.Core;

namespace Kast.Test;

/// <summary>
/// 分词回归验证：逐段最长匹配（贪心）。
/// 从左到右每一步取当前能匹配的最长词条，读不动就停下；
/// 相邻词条之间<b>不做</b>任何公共前后缀合并——两个词条首尾相接写成的串就是它们的直接拼接。
/// 由 <see cref="Entry"/> 调用，返回进程退出码。
/// </summary>
static class TokenizerTests
{
    static readonly List<string> failures=[];
    static int checks;

    public static int Run()
    {
        Console.WriteLine("—— 分词（逐段最长匹配）验证 ——");
        SpecExamples();
        Greedy();
        Unaffected();
        Prefix();
        PinnedPredictions();
        PredictionMatches();
        PredictionSpec();
        PredictionOffsets();
        PredictionDrawn();
        PredictionAstral();
        PreviewRotation();
        DrawThrough();
        Wysiwyg();
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

    /// <summary>基本读法：能读的读对，读不动的判失败。</summary>
    static void SpecExamples()
    {
        var a=Lexicon("abc","bcd");
        Show("abc,bcd","abcbcd",a);
        Eq("{abc,bcd}：abc",a.Tokenize("abc"),"abc");
        Eq("{abc,bcd}：bcd",a.Tokenize("bcd"),"bcd");
        Eq("{abc,bcd}：abcbcd 是两个词直接拼接",a.Tokenize("abcbcd"),"abc","bcd");
        Null("{abc,bcd}：abcd 在 'd' 处读不动",a.Tokenize("abcd"));
        Null("{abc,bcd}：abcdbcd 同样在半途读不动",a.Tokenize("abcdbcd"));
        Null("{abc,bcd}：abcbd 读不动",a.Tokenize("abcbd"));
    }

    /// <summary>贪心最长匹配的具体取舍，以及"不再合并边界"带来的行为。</summary>
    static void Greedy()
    {
        // 同一个串里有等长的两个词条时，按词条表顺序取第一个（表按长度降序）
        var two=Lexicon("ab","bc");
        Eq("{ab,bc}：ab",two.Tokenize("ab"),"ab");
        Eq("{ab,bc}：abbc 是 ab 与 bc 的直接拼接",two.Tokenize("abbc"),"ab","bc");
        Null("{ab,bc}：abc 在 'c' 处读不动（不再靠重叠读成 ab+bc）",two.Tokenize("abc"));

        // 最长优先：能吃掉更多字符的词条胜出
        var longest=Lexicon("ab","abc");
        Eq("{ab,abc}：abc 取更长的 abc",longest.Tokenize("abc"),"abc");
        Eq("{ab,abc}：abab 先取 ab 再取 ab",longest.Tokenize("abab"),"ab","ab");

        // 贪心的固有代价：更长的词条可能跨过本该属于下一个词的位置
        var spanning=Lexicon("ab","bca");
        Null("{ab,bca}：abca 读不动（bca 与开头对不上，尾部 'a' 也不是词条）",spanning.Tokenize("abca"));
        Eq("{ab,bca,b}：bcab 取 bca 再取 b",Lexicon("ab","bca","b").Tokenize("bcab"),"bca","b");

        // 单字符词条与长词条混排
        var mixed=Lexicon("a","ab","b");
        Eq("{a,ab,b}：ab 取 ab",mixed.Tokenize("ab"),"ab");
        Eq("{a,ab,b}：abb 取 ab 再取 b",mixed.Tokenize("abb"),"ab","b");
        Eq("{a,ab,b}：aba 取 ab 再取 a",mixed.Tokenize("aba"),"ab","a");

        // 与旧（合并边界）规则相反的两个例子，钉住这次的方向调整
        var swap=Lexicon("ab","ba");
        Eq("{ab,ba}：abab 读作 ab+ab",swap.Tokenize("abab"),"ab","ab");
        Eq("{ab,ba}：abba 读作 ab+ba（旧规则会判失败）",swap.Tokenize("abba"),"ab","ba");
        var three=Lexicon("ab","bc","cd");
        Eq("{ab,bc,cd}：abcd 读作 ab+cd",three.Tokenize("abcd"),"ab","cd");
        Eq("{ab,bc,cd}：abbccd 读作 ab+bc+cd",three.Tokenize("abbccd"),"ab","bc","cd");
    }

    /// <summary>没有公共边界的词条表：行为与以前完全一致。</summary>
    static void Unaffected()
    {
        var a=Lexicon("dog","cat","look","cattle","lock");
        Eq("{dog,cat,look,cattle,lock}：dogcat",a.Tokenize("dogcat"),"dog","cat");
        Eq("{dog,cat,look,cattle,lock}：catcattle",a.Tokenize("catcattle"),"cat","cattle");
        Null("{dog,cat,look,cattle,lock}：cattlock",a.Tokenize("cattlock"));

        // 单字符词条
        var cn=Lexicon("我","看","你");
        Eq("中文：我看你",cn.Tokenize("我看你"),"我","看","你");
        Eq("中文：我看",cn.Tokenize("我看"),"我","看");
        Null("中文：我瞧",cn.Tokenize("我瞧"));
        Eq("空输入返回空切分而不是失败",cn.Tokenize(""),[]);
    }

    /// <summary>
    /// <see cref="Alphabet{O}.TokenizePrefix"/>：界面"边画边显示"用的部分切分。
    /// 它与 <see cref="Alphabet{O}.Tokenize"/> 是同一套贪心规则，这里钉住：
    /// (1) 两者词序相同；(2) <c>Tokenize</c> 可读 ⇔ 前缀版正好覆盖满整串。
    /// </summary>
    static void Prefix()
    {
        var two=Lexicon("ab","bc");
        PrefixEq("{ab,bc}：abbc 整串可读",two,"abbc",4,"ab","bc");
        PrefixEq("{ab,bc}：abc 只读到 ab（'c' 处读不动）",two,"abc",2,"ab");
        PrefixEq("{ab,bc}：abb 只读到 ab",two,"abb",2,"ab");
        PrefixEq("{ab,bc}：a 一个词都读不出",two,"a",0);
        PrefixEq("{ab,bc}：空输入",two,"",0);

        var spec=Lexicon("abc","bcd");
        PrefixEq("{abc,bcd}：abcbcd 整串可读",spec,"abcbcd",6,"abc","bcd");
        PrefixEq("{abc,bcd}：abcd 只读到 abc",spec,"abcd",3,"abc");
        PrefixEq("{abc,bcd}：abcbcdbcd 整串可读",spec,"abcbcdbcd",9,"abc","bcd","bcd");

        var compared=0;
        (char[] Alphabet,int PoolLength)[] pools=
        [
            (['a','b'],3),
            (['a','b','c'],2),
        ];
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
                    var full=table.Tokenize(input);
                    var segment=table.TokenizePrefix(input,out var consumed);
                    var reference=ReferenceGreedy(table.Tokens,input,out var referenceConsumed);
                    if(consumed!=referenceConsumed||!segment.SequenceEqual(reference))
                    {
                        failures.Add($"[{string.Join("/",lex)}] \"{input}\"：前缀版 consumed={consumed} "+
                                     $"[{string.Join("+",segment)}]，参考实现 consumed={referenceConsumed} "+
                                     $"[{string.Join("+",reference)}]");
                        continue;
                    }
                    var fullReadable=full is not null;
                    var completelyCovered=consumed==input.Length;
                    if(fullReadable!=completelyCovered)
                    {
                        failures.Add($"[{string.Join("/",lex)}] \"{input}\"：Tokenize {(fullReadable?"可读":"判失败")}，"+
                                     $"前缀版覆盖 {consumed}/{input.Length}");
                        continue;
                    }
                    if(fullReadable&&!segment.SequenceEqual(full!))
                    {
                        failures.Add($"[{string.Join("/",lex)}] \"{input}\"：整串可读时两者词序不同，"+
                                     $"{string.Join("+",full!)} vs {string.Join("+",segment)}");
                    }
                }
            }
        }
        checks++;
        Console.WriteLine($"  前缀分词穷举：{compared} 组（词条表 × 输入）；与参考贪心一致，"+
                          $"且 Tokenize 可读 ⇔ 前缀版覆盖满整串");
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
    /// 预测的判据：已画出的那一截必须是候选词条里的一段连续子串，且词条更长；
    /// 从最长的一截开始找，第一截有候选的就是答案。报出的数字是词条里还没画出来的笔画数
    /// （<see cref="PredictionMatch.RemainingLength"/>）：偏移为 0 时就是"接着往后画几笔"，
    /// 偏移大于 0 时其中含落在这一截前面的笔画。
    /// </summary>
    static void PredictionSpec()
    {
        var a=Lexicon("abbc","cbbc");
        foreach(var input in new[]{"ab","abc","bbc","abbc"})
        {
            var prediction=a.CreatePrediction();
            foreach(var c in input)
            {
                prediction.PushChar(c);
            }
            Console.WriteLine($"  预测 \"{input}\" → "+(prediction.PredictionResult.Count==0
                ?"（空）"
                :string.Join(" ",prediction.PredictionResult.Select(m=>$"({m.Token},{m.RemainingLength})"))));
        }
        Remaining("Spec(ab)",a,"ab",("abbc",2));
        Remaining("Spec(abc)",a,"abc");
        Remaining("Spec(bbc)",a,"bbc",("abbc",1),("cbbc",1));
        Remaining("Spec(abbc)",a,"abbc");
    }

    /// <summary>预测结果里"已画出的那一截"有多长：界面据此算候补词条的根旋转。</summary>
    static void PredictionMatches()
    {
        var a=Lexicon("dog","cat","look","cattle","lock");
        Matches("Matches(dogca)",a,"dogca",("cat",2),("cattle",2));
        Matches("Matches(catlo)",a,"catlo",("lock",2),("look",2));
        Matches("Matches(cat)",a,"cat",("cattle",3));
        Matches("Matches(cattl)",a,"cattl",("cattle",5));
        Matches("Matches(dog)",a,"dog");
        Matches("Matches(lockcat)",a,"lockcat",("cattle",3));
        Matches("Matches(aa)",Lexicon("a","aaa"),"aa",("aaa",2));
    }

    /// <summary>
    /// 偏移：这一截落在词条第几位。界面用它回推整词的根旋转，所以它必须真的是那一段在词条里的位置；
    /// 同一个词条匹配多处时取偏移最小的那一次。这里也钉住"接着往后画还差几笔"（Trailing）。
    /// </summary>
    static void PredictionOffsets()
    {
        // 这一截 "aa" 在 "aaa" 里出现在第 0 位和第 1 位：取第 0 位，接着往后画一笔就成词
        Offsets("Offsets(aa@{aaa})",Lexicon("aaa"),"aa",("aaa",2,0));
        // 落在词条中间：偏移 1，词条开头那笔没画过，后面还剩一笔可以接
        Offsets("Offsets(ab@{xabc})",Lexicon("xabc"),"ab",("xabc",2,1));
        // 用户给的四个用例：只有 "ab" 落在词首，"bbc" 落在两个词条的第 1 位
        var spec=Lexicon("abbc","cbbc");
        Offsets("Offsets(ab@{abbc,cbbc})",spec,"ab",("abbc",2,0));
        Offsets("Offsets(bbc@{abbc,cbbc})",spec,"bbc",("abbc",3,1),("cbbc",3,1));
        Offsets("Offsets(abc@{abbc,cbbc})",spec,"abc");
        Offsets("Offsets(abbc@{abbc,cbbc})",spec,"abbc");
    }

    /// <summary>
    /// 已经整段画出来的词条不再算预测：缺的笔画该画在这一截前面，而它们已经在输入里了。
    /// 这几组里整词都恰好落在已画串的末尾（"ab" 的 "b"、"abc" 的 "bc"），报"还差几笔"会让人以为还有笔画要补。
    /// 与之相对，"bbc" 是 "abbc" 中间的一段、词条开头那笔确实没画过，仍要报出来（见 <see cref="PredictionOffsets"/>）。
    /// </summary>
    static void PredictionDrawn()
    {
        Offsets("Drawn(ab@{a,b,ab})",Lexicon("a","b","ab"),"ab");
        Offsets("Drawn(abc@{a,bc,abc})",Lexicon("a","bc","abc"),"abc");
        Offsets("Drawn(abc@{ab,bc})",Lexicon("ab","bc"),"abc");
        Offsets("Kept(bbc@{bbc,abbc})",Lexicon("bbc","abbc"),"bbc",("abbc",3,1));
    }

    /// <summary>
    /// 非 BMP 词条：预测的走步和建树一样按码位走，能穿过代理对（分词器同样读得出，两者不能各说各话）。
    /// </summary>
    static void PredictionAstral()
    {
        var a=Lexicon("𝕏","ab","abc");
        Eq("非 BMP：𝕏ab 读作 𝕏+ab",a.Tokenize("𝕏ab"),"𝕏","ab");
        Offsets("Offsets(𝕏ab@{𝕏,ab,abc})",a,"𝕏ab",("abc",2,0));
    }

    /// <summary>
    /// 候补预览的朝向：界面画的是整个词条，玩家要能把预览的形状对上自己的笔画。
    /// 按游戏的约定模拟一次作画——先一笔自由起笔，之后每一笔给出词条的一个转折——
    /// 那么不管这一截是词条里哪一段（起点 offset、长度 matched），算出来的根旋转都应当正好是那一笔
    /// 自由起笔的方向；照这个根旋转把整个词条画出来，第 j 段的方向也必须和玩家画的第 j 笔重合。
    /// </summary>
    static void PreviewRotation()
    {
        const string token="wqe";
        var directions="wqasde".ToCharArray();   // 与 GridHelper.Chars 同一张表
        var strokes=new List<int>{2};             // 每一笔的绝对方向，[0] 是那一笔自由起笔（朝 'a'，下标 2）
        foreach(var c in token)
        {
            strokes.Add(Turn(strokes[^1],c,directions));
        }
        var tested=0;
        for(var offset=0;offset<token.Length;offset++)
        {
            for(var matched=1;offset+matched<=token.Length;matched++)
            {
                checks++;
                tested++;
                var match=new PredictionMatch(token,matched,offset);
                // 这一截第一笔就是词条第 offset 位的笔画，也就是玩家画的第 offset+1 笔
                var root=match.RootAngle(strokes[offset+1],directions);
                if(root!=strokes[0])
                {
                    failures.Add($"预览朝向：\"{token}\" 第 {offset} 位起 {matched} 笔，"+
                                 $"根旋转应为 {strokes[0]}（自由起笔），实际 {root}");
                    continue;
                }
                // 照这个根旋转画整个词条：第 j 段（从 1 数）应当就是玩家画的第 j 笔
                var angle=root;
                for(var j=0;j<token.Length;j++)
                {
                    angle=Turn(angle,token[j],directions);
                    if(angle!=strokes[j+1])
                    {
                        failures.Add($"预览朝向：\"{token}\" 的第 {j+1} 段应为 {strokes[j+1]}，实际 {angle}");
                    }
                }
            }
        }
        Console.WriteLine($"  预览朝向：\"{token}\" 的每一段都能对上玩家的笔画（穷举 {tested} 种截法）");
    }

    /// <summary>转折累加：角度下标加上转折字符在方向表里的位置——与 <c>GridHelper.GetAngle</c> 同一约定（超出就减一轮）。</summary>
    static int Turn(int angle,char c,char[] directions)
    {
        var next=angle+Array.IndexOf(directions,c);
        return next>=directions.Length?next-directions.Length:next;
    }

    /// <summary>
    /// 照玩家的画法走一遍：先一笔自由起笔（不进预测），之后逐笔给出词条的转折。
    /// 每一步的候补都应当正好是这个正在画的词条；画满了就不再报，让位给更长的词条。
    /// </summary>
    static void DrawThrough()
    {
        var a=Lexicon("wqe","wqea");
        Offsets("作画(w)",a,"w",("wqe",1,0),("wqea",1,0));
        Offsets("作画(wq)",a,"wq",("wqe",2,0),("wqea",2,0));
        Offsets("作画(wqe)",a,"wqe",("wqea",3,0));
    }

    /// <summary>
    /// 界面只显示"接着画就能得到"的候补（偏移 0）。游戏词表里真的存在一个词形套在另一个里面：
    /// seed=0 时 i 的词形 da 套在 go 的词形 ada 里、you 的词形 dw 套在 that 的 wqwdw 和 earth 的 ddww 里。
    /// 它们的偏移不为 0——预览只能把手上这一截画在词条中间，前面那几笔补不上去，所以不是所见即所得。
    /// </summary>
    static void Wysiwyg()
    {
        var a=Lexicon("ada","da");
        Offsets("Wysiwyg(d@{ada,da})",a,"d",("ada",1,1),("da",1,0));   // 内核照旧报两条
        Display("Wysiwyg 显示(d@{ada,da})",a,"d","da");                 // 界面只显示还接得上的 da
        Offsets("Wysiwyg(da@{ada,da})",a,"da",("ada",2,1));             // 画满 da 之后只剩 ada，而它的开头在 da 之前
        Display("Wysiwyg 显示(da@{ada,da})",a,"da");
        var b=Lexicon("da","dad");
        Offsets("Wysiwyg(da@{da,dad})",b,"da",("dad",2,0));
        Display("Wysiwyg 显示(da@{da,dad})",b,"da","dad");
    }

    /// <summary>
    /// 小规模穷举：所有"词条表 × 输入"都和独立写的参考贪心实现比对（可读性与词序都要一致），
    /// 并检查返回的切分确实是输入的一段一段拼接、没有重叠。
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
        var mismatched=0;
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
                    var reference=ReferenceGreedy(table.Tokens,input,out var consumed);
                    var referenceFull=consumed==input.Length?reference:null;
                    if((segment is null)!=(referenceFull is null)
                       ||(segment is not null&&!segment.SequenceEqual(referenceFull!)))
                    {
                        mismatched++;
                        if(mismatched<=5)
                        {
                            failures.Add($"[{string.Join("/",lex)}] \"{input}\"：实际 "+
                                         $"{(segment is null?"判失败":string.Join("+",segment))}，参考 "+
                                         $"{(referenceFull is null?"判失败":string.Join("+",referenceFull))}");
                        }
                        continue;
                    }
                    if(segment is null) continue;
                    accepted++;
                    var error=Validate(table.Tokens,segment,input);
                    if(error is not null)
                    {
                        failures.Add($"[{string.Join("/",lex)}] \"{input}\"：返回 {string.Join("+",segment)} 非法（{error}）");
                    }
                }
            }
        }
        checks++;
        if(mismatched>0)
        {
            failures.Add($"穷举比对：{mismatched} 组与参考贪心实现不一致");
        }
        Console.WriteLine($"  穷举比对：{compared} 组（词条表 × 输入），可读 {accepted} 组，"+
                          $"与参考贪心不一致 {mismatched} 组");
    }

    /// <summary>
    /// 游戏式的词典：词形是 6 字母表上长度 2–5 的随机串（<c>Database.Gen</c> 就是这么生成的）。
    /// 贪心规则下没有"规范写法"可言，玩家把词逐个画出来就是直接拼接，于是这里统计：
    /// 单独一个词能否读回自己；多个词直接拼接时有几成能读回原来的词序、几成读成别的词序、几成读不动。
    /// 后两者是贪心最长匹配的固有代价（更长的词条可能跨过词的边界），只统计不判失败。
    /// </summary>
    static void GameLike()
    {
        var random=new Random(20240607);
        var pool=Strings(['w','q','a','s','d','e'],2,5).ToArray();
        var lex=pool.OrderBy(_=>random.Next()).Take(40).ToArray();
        var table=Lexicon(lex);

        var notSelf=0;
        foreach(var word in lex)
        {
            var segment=table.Tokenize(word);
            if(segment is null||segment.Count!=1||segment[0]!=word) notSelf++;
        }
        checks++;
        if(notSelf>0)
        {
            failures.Add($"模拟词典：{notSelf}/{lex.Length} 个词形没能读回自己");
        }

        int trials=0, sameWords=0, otherWords=0, rejected=0, longestDiffers=0;
        for(var n=0;n<500;n++)
        {
            var m=2+random.Next(2);                    // 2–3 个词
            var words=new string[m];
            for(var i=0;i<m;i++) words[i]=lex[random.Next(lex.Length)];
            var plain=string.Concat(words);            // 直接拼接：这就是玩家逐词画出来的串
            trials++;

            var segment=table.Tokenize(plain);
            if(segment is null)
            {
                rejected++;
                continue;
            }
            if(segment.SequenceEqual(words)) sameWords++;
            else
            {
                otherWords++;
                if(!segment.SequenceEqual(ReferenceGreedy(table.Tokens,plain,out _))) longestDiffers++;
            }
        }
        checks++;
        if(longestDiffers>0)
        {
            failures.Add($"模拟词典：{longestDiffers} 例与参考贪心实现不一致");
        }
        Console.WriteLine($"  模拟游戏词典：{lex.Length} 个词形；单个词形读回自己 {lex.Length-notSelf}/{lex.Length}");
        Console.WriteLine($"  多词直接拼接 {trials} 例：读回原词序 {sameWords}，读成别的词序 {otherWords}，"+
                          $"读不动 {rejected}（后两者是贪心最长匹配的固有代价）");
    }

    /// <summary>长输入下的开销：贪心是一遍扫描，必须远低于上限。</summary>
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
                var error=Validate(table.Tokens,segment,input);
                if(error is not null) failures.Add("压力用例返回非法切分："+error);
            }
        }
    }

    // ————— 参考实现：逐段最长匹配 —————

    /// <summary>
    /// 逐段最长匹配的独立实现：<paramref name="tokens"/> 必须按长度降序（<see cref="Alphabet{O}.CompileTokens"/> 的排序），
    /// 于是"第一个命中的词条"就是最长匹配。读不动就停，<paramref name="consumed"/> 是已经覆盖到的字符数。
    /// </summary>
    static List<string> ReferenceGreedy(IReadOnlyList<string> tokens,string input,out int consumed)
    {
        var result=new List<string>();
        consumed=0;
        while(consumed<input.Length)
        {
            string? matched=null;
            foreach(var token in tokens)
            {
                if(consumed+token.Length<=input.Length
                   &&string.CompareOrdinal(input,consumed,token,0,token.Length)==0)
                {
                    matched=token;
                    break;
                }
            }
            if(matched is null) break;
            result.Add(matched);
            consumed+=matched.Length;
        }
        return result;
    }

    /// <summary>检查切分是不是输入上一段一段的直接拼接（词条都在表里、位置连续、正好用完全部字符）；合法返回 null。</summary>
    static string? Validate(IReadOnlyList<string> tokens,IReadOnlyList<string> segment,string input)
    {
        if(segment.Count==0) return "空切分";
        var at=0;
        foreach(var token in segment)
        {
            if(!tokens.Contains(token)) return $"词条 {token} 不在词条表里";
            if(at+token.Length>input.Length) return $"词条 {token} 越过输入末尾";
            if(!input.AsSpan(at,token.Length).SequenceEqual(token)) return $"输入第 {at} 位起不是词条 {token}";
            at+=token.Length;
        }
        return at==input.Length?null:$"只覆盖到 {at}/{input.Length}";
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

    static void PrefixEq(string what,Alphabet<string> table,string input,int expectedConsumed,params string[] expected)
    {
        checks++;
        var actual=table.TokenizePrefix(input,out var consumed);
        if(consumed!=expectedConsumed||!actual.SequenceEqual(expected))
        {
            failures.Add($"{what}：期望 consumed={expectedConsumed} [{string.Join(" ",expected)}]，"+
                         $"实际 consumed={consumed} [{string.Join(" ",actual)}]");
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

    /// <summary>
    /// 比对预测结果里的 (词条, 还没画出来的笔画数)。
    /// <see cref="PredictionMatch.MatchedLength"/> 是已画出的那一截的长度，两者相加应当等于词长；
    /// 界面还要用 <see cref="PredictionMatch.Offset"/> 把这一截摆回词条里的正确位置。
    /// </summary>
    static void Remaining(string what,Alphabet<string> table,string input,params (string Token,int Remaining)[] expected)
    {
        checks++;
        var prediction=table.CreatePrediction();
        foreach(var c in input)
        {
            prediction.PushChar(c);
        }
        foreach(var match in prediction.PredictionResult)
        {
            if(match.MatchedLength<=0||match.MatchedLength+match.RemainingLength!=match.Token.Length)
            {
                failures.Add($"{what}：\"{match.Token}\" 的已画长度不对（{match}）");
                return;
            }
        }
        var actual=prediction.PredictionResult
            .Select(m=>(m.Token,m.RemainingLength))
            .OrderBy(m=>m.Token,StringComparer.Ordinal)
            .ToArray();
        var want=expected.OrderBy(m=>m.Token,StringComparer.Ordinal).ToArray();
        if(!actual.SequenceEqual(want))
        {
            failures.Add($"{what}：期望 [{string.Join(",",want.Select(m=>$"{m.Token}:{m.Remaining}"))}]，"+
                         $"实际 [{string.Join(",",actual.Select(m=>$"{m.Token}:{m.RemainingLength}"))}]");
        }
    }

    /// <summary>比对预测结果里的 (词条, 已画出的长度) 集合。</summary>
    static void Matches(string what,Alphabet<string> table,string input,params (string Token,int Matched)[] expected)
    {
        checks++;
        var prediction=table.CreatePrediction();
        foreach(var c in input)
        {
            prediction.PushChar(c);
        }
        var actual=prediction.PredictionResult
            .Select(m=>(m.Token,m.MatchedLength))
            .OrderBy(m=>m.Token,StringComparer.Ordinal)
            .ToArray();
        var want=expected.OrderBy(m=>m.Token,StringComparer.Ordinal).ToArray();
        if(!actual.SequenceEqual(want))
        {
            failures.Add($"{what}：期望 [{string.Join(",",want.Select(m=>$"{m.Token}:{m.Matched}"))}]，"+
                         $"实际 [{string.Join(",",actual.Select(m=>$"{m.Token}:{m.MatchedLength}"))}]");
        }
    }

    /// <summary>
    /// 比对预测结果的 (词条, 已画长度, 词内偏移) 集合，并顺手检查每条结果自洽：
    /// 词条第 <c>Offset</c> 位起的那一段确实是输入末尾这一截，几个笔画数也对得上。
    /// </summary>
    static void Offsets(string what,Alphabet<string> table,string input,params (string Token,int Matched,int Offset)[] expected)
    {
        checks++;
        var prediction=table.CreatePrediction();
        foreach(var c in input)
        {
            prediction.PushChar(c);
        }
        var actual=prediction.PredictionResult
            .Select(m=>(m.Token,m.MatchedLength,m.Offset))
            .OrderBy(m=>m.Token,StringComparer.Ordinal)
            .ToArray();
        var want=expected.OrderBy(m=>m.Token,StringComparer.Ordinal).ToArray();
        Console.WriteLine($"  {what} → "+(prediction.PredictionResult.Count==0
            ?"（空）"
            :string.Join(" ",prediction.PredictionResult.Select(m=>$"({m.Token},已画{m.MatchedLength}@{m.Offset})"))));
        if(!actual.SequenceEqual(want))
        {
            failures.Add($"{what}：期望 [{string.Join(",",want.Select(m=>$"{m.Token}:{m.Matched}@{m.Offset}"))}]，"+
                         $"实际 [{string.Join(",",actual.Select(m=>$"{m.Token}:{m.MatchedLength}@{m.Offset}"))}]");
            return;
        }
        foreach(var match in prediction.PredictionResult)
        {
            var piece=input[^match.MatchedLength..];
            if(match.Token.Substring(match.Offset,match.MatchedLength)!=piece
               ||match.MatchedLength+match.RemainingLength!=match.Token.Length
               ||match.TrailingLength!=match.Token.Length-match.Offset-match.MatchedLength
               ||match.FromStart!=(match.Offset==0))
            {
                failures.Add($"{what}：\"{match.Token}\" 的偏移或笔画数不自洽（{match}）");
            }
        }
    }

    /// <summary>
    /// 比对界面实际会显示的候补：只有"接着画就能得到"的那些（偏移 0），见 <see cref="Wysiwyg"/>。
    /// </summary>
    static void Display(string what,Alphabet<string> table,string input,params string[] expected)
    {
        checks++;
        var prediction=table.CreatePrediction();
        foreach(var c in input)
        {
            prediction.PushChar(c);
        }
        var actual=prediction.PredictionResult
            .Where(m=>m.FromStart)
            .Select(m=>m.Token)
            .OrderBy(t=>t,StringComparer.Ordinal)
            .ToArray();
        Console.WriteLine($"  {what} → {(actual.Length==0?"（空）":string.Join(" ",actual))}");
        var want=expected.OrderBy(t=>t,StringComparer.Ordinal).ToArray();
        if(!actual.SequenceEqual(want))
        {
            failures.Add($"{what}：期望 {{{string.Join(",",want)}}}，实际 {{{string.Join(",",actual)}}}");
        }
    }
}
