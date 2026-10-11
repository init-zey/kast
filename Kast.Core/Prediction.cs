using System.Text;

namespace Kast.Core;

/// <summary>
/// 输入过程中的前缀预测器：把字符逐个喂进来，<see cref="PredictionResult"/> 始终是
/// "补全哪些词条能让已输入串拼成一个合法字符串"——即存在可分词位置 i，
/// 使已输入串 s[i..] 是词条 t 的真前缀（补上 t 的剩余部分后 s 仍可切成完整词条）。
/// 请用 <see cref="Alphabet{T}.CreatePrediction"/> 获取实例；词典变更
/// （重新 <see cref="Alphabet{T}.CompileTokens"/>）后需重新获取。
/// </summary>
public sealed class Prediction
{
    PrefixTrie? trie;

    /// <summary>已输入的字符，用于判断可分词位置。</summary>
    readonly StringBuilder input=new();

    int length;

    /// <summary>挂起的高代理项：等下一个 char 组成完整码位再推进自动机。</summary>
    char pendingHighSurrogate;

    /// <summary>
    /// 空构造：供需要无参构造的场合使用（Godot 导出属性、反序列化等）。
    /// 它不绑定任何词典，请用 <see cref="Alphabet{T}.CreatePrediction"/> 获得可用的预测器。
    /// </summary>
    public Prediction()
    {
    }

    internal Prediction(PrefixTrie trie)
    {
        this.trie=trie;
    }

    /// <summary>供 <see cref="Alphabet{T}"/> 注入当前词典的前缀树。</summary>
    internal void Attach(PrefixTrie tree)
    {
        trie=tree;
        Reset();
    }

    /// <summary>是否已绑定词典。</summary>
    public bool IsBound => trie is not null;

    /// <summary>当前预测结果，按词条长度降序；取第一个即最长匹配。</summary>
    public IReadOnlyList<string> PredictionResult { get; private set; }=[];

    /// <summary>已喂入的字符数。</summary>
    public int Length => length;

    /// <summary>喂入一个字符并刷新 <see cref="PredictionResult"/>。</summary>
    /// <exception cref="InvalidOperationException">未绑定词典时抛出——请改用 <see cref="Alphabet{T}.CreatePrediction"/>。</exception>
    public void PushChar(char c)
    {
        if(trie is null)
        {
            throw new InvalidOperationException(
                "Prediction 尚未绑定词典；无参构造只用于需要空构造的场合，可用实例请从 Alphabet.CreatePrediction() 获取。");
        }
        // 代理对：高代理项先挂起，凑齐后作为一个码位推进，避免把补充平面字符拆成两半
        if(char.IsHighSurrogate(c))
        {
            pendingHighSurrogate=c;
            return;
        }
        if(char.IsLowSurrogate(c)&&pendingHighSurrogate!=0)
        {
            var high=pendingHighSurrogate;
            pendingHighSurrogate='\0';
            Step(new Rune(high,c));
            return;
        }
        pendingHighSurrogate='\0';
        Step(new Rune(c));
    }

    /// <summary>清空已输入内容，回到初始状态。</summary>
    public void Reset()
    {
        input.Clear();
        length=0;
        pendingHighSurrogate='\0';
        PredictionResult=[];
    }

    /// <summary>复制一份预测器，用于尝试性输入而不影响当前状态。</summary>
    public Prediction Clone()
    {
        var copy=new Prediction
        {
            trie=trie,
            length=length,
            pendingHighSurrogate=pendingHighSurrogate,
            PredictionResult=PredictionResult,
        };
        copy.input.Append(input);
        return copy;
    }

    void Step(Rune rune)
    {
        input.Append(rune.ToString());
        length++;
        PredictionResult=trie!.Results(input.ToString());
    }
}
