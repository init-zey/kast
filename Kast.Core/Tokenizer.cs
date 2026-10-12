namespace Kast.Core;

/// <summary>
/// 词条表上的分词器与预测器工厂：持有"已编译"的词条表，提供提交用的整体分词
/// （<see cref="Tokenize"/>）、边画边显示用的前缀分词（<see cref="TokenizePrefix"/>），
/// 以及增量的候补预测器（<see cref="CreatePrediction"/>）。
/// </summary>
/// <remarks>
/// 本类不依赖 Godot，可在无引擎环境下测试。
/// <para>
/// 分词与预测共用同一套"逐段最长匹配 + 前缀树"规则，这是"界面上显示成词"与
/// "提交时能填进槽"始终一致的原因：两者走的是同一份词条表与同一套匹配约定，
/// 不需要在 UI 层再补一份校验。
/// </para>
/// <para>
/// <see cref="CompileTokens"/> 之后词条表变化，先前 <see cref="CreatePrediction"/> 取出的
/// 预测器与缓存的前缀树都会作废，需要重新获取。
/// </para>
/// </remarks>
public class Tokenizer
{
    /// <summary>已编译的词条表；未 <see cref="CompileTokens"/> 时为空表，分词一律读不出词。</summary>
    string[] tokens=[];
    PrefixTrie? trie;
    /// <summary>已编译的分词表，即预测函数里的 S。按词条长度降序排列。</summary>
    public IReadOnlyList<string> Tokens => tokens;
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
    /// 编译词条表：滤掉空串、按长度降序排列，供"第一个命中即最长匹配"的分词使用。
    /// 重复编译会作废此前缓存的前缀树（见 <see cref="CreatePrediction"/>）。
    /// </summary>
    public void CompileTokens(IEnumerable<string> tokenSet)
    {
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
}