using System.Text;

namespace Kast.Core;

/// <summary>
/// 一条预测结果：把缺的笔画补齐，<see cref="Token"/> 就成词。
/// <see cref="MatchedLength"/> 是"已经画出来的那一截"的长度，<see cref="Offset"/> 是这一截落在词条第几位
/// ——界面用这两个数把整个词条摆到正确的位置和朝向上（<see cref="FromStart"/> 为真时就是照直画）。
/// </summary>
/// <param name="Token">候选词条。</param>
/// <param name="MatchedLength">已画出的那一截的长度（严格小于词长：画满了就不算预测）。</param>
/// <param name="Offset">这一截的起点在词条里的位置，0 表示这一截就是词条的开头。</param>
public readonly record struct PredictionMatch(string Token,int MatchedLength,int Offset)
{
    /// <summary>
    /// 词条里还没被这一截覆盖的笔画数。偏移大于 0 时其中包含落在这一截<b>前面</b>的笔画，
    /// 那几笔接着往后画是补不上的。
    /// </summary>
    public int RemainingLength=>Token.Length-MatchedLength;

    /// <summary>接着往后画几笔能画到词条末尾；<see cref="FromStart"/> 为真时等于 <see cref="RemainingLength"/>。</summary>
    public int TrailingLength=>Token.Length-Offset-MatchedLength;

    /// <summary>
    /// 这一截就是词条的开头（偏移为 0）：整个词条可以从这一截的起点照直画出来，接着画就能得到它。
    /// 界面只显示这一种候补——偏移大于 0 的接着画也补不上，见 <see cref="PrefixTrie.Results"/>。
    /// </summary>
    public bool FromStart=>Offset==0;

    /// <summary>
    /// 整个词条的起笔方向（<paramref name="directions"/> 里的下标）：界面画的是整个词条，
    /// 它的起笔方向是"这一截起点<b>之前</b>那一笔"的方向，所以要从这一截第一笔的方向出发，
    /// 回退词条里排在它前面的转折（词条首笔到这一截起点，共 <see cref="Offset"/>+1 个）。
    /// <para>
    /// <paramref name="pieceStartAngle"/> 是这一截第一笔的绝对方向，调用方必须保证它已经包含刚画下的那一笔。
    /// 词条的字符都要在 <paramref name="directions"/> 里（游戏里词条就是按这张表生成的），
    /// 累加约定与 <c>GridHelper.GetAngle</c> 一致：下标相加后对表长取模。
    /// </para>
    /// </summary>
    public int RootAngle(int pieceStartAngle,IReadOnlyList<char> directions)
    {
        var angle=pieceStartAngle;
        for(var i=0;i<=Offset;i++)
        {
            angle-=SlotOf(directions,Token[i]);
            if(angle<0) angle+=directions.Count;
        }
        return angle;
    }

    /// <summary>字符在方向表里的下标；不在表里返回 -1（与 <c>Array.IndexOf</c> 一致）。</summary>
    static int SlotOf(IReadOnlyList<char> directions,char c)
    {
        for(var i=0;i<directions.Count;i++)
        {
            if(directions[i]==c) return i;
        }
        return -1;
    }

    public override string ToString()=>$"{Token}（已画 {MatchedLength}，词内第 {Offset} 位，还差 {RemainingLength}）";
}

/// <summary>
/// 输入过程中的预测器：把字符逐个喂进来，<see cref="PredictionResult"/> 始终是
/// "哪些词条里含有已经画出来的那一截"——即存在可分词位置 i，使已输入串 s[i..] 是词条 t 的
/// <b>一段连续子串</b>且 t 更长（已经整段画出来的词条除外，见 <see cref="PrefixTrie.Results"/>）。
/// 每项带这一截在词条里的偏移、这一截的长度、以及词条里还没画出来的笔画数，供"边画边显示"。
/// 请用 <see cref="Alphabet{T}.CreatePrediction"/> 获取实例；词典变更
/// （重新 <see cref="Alphabet{T}.CompileTokens"/>）后需重新获取。
/// </summary>
public sealed class Prediction
{
    PrefixTrie? trie;

    /// <summary>已输入的字符，用于判断可分词位置。</summary>
    readonly StringBuilder input=new();

    int length;

    /// <summary>挂起的高代理项：等下一个 char 组成完整码位再喂进去。</summary>
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

    /// <summary>
    /// 当前预测结果，按词条长度降序；取第一个即最长匹配，每个词条至多一条。
    /// 每项带 <see cref="PredictionMatch.Offset"/>（这一截落在词条第几位）与
    /// <see cref="PredictionMatch.MatchedLength"/>（这一截有多长）。
    /// 同一个词条若有多处匹配（这一段在词条里出现不止一次），取偏移最小的那一条。
    /// </summary>
    public IReadOnlyList<PredictionMatch> PredictionResult { get; private set; }=[];

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
