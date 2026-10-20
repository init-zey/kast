using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Kast.Core;

namespace Kast.Godot;

/// <summary>
/// 程序词形分配器：给每个真实词分配一条手绘笔画串（词形），并登记双向翻译。
/// </summary>
/// <remarks>
/// 分配是<b>定序枚举 + 顺序探测</b>，不再"哈希采样 + 冲突重试"：
/// <list type="bullet">
/// <item>长度为 L 的候选词形被编号成 <c>[0, 6·5^(L-1))</c>：首步 6 个绝对方向任选，之后每步 5 个方向
/// （跳过掉头方向）。<see cref="TryUnrank"/> 用混合进制把下标 O(L) 还原成笔画串，不必遍历整个空间。</item>
/// <item>起点取 <c>StableHash.Combine(real, seed) % 6·5^(L-1)</c>，从这里<b>向后探测</b>第一个可用词形。
/// 于是词形由 (real, seed) 的哈希起点决定，只有落在探测窗口里的冲突才会把它往后挤——登记顺序的影响是局部的。</item>
/// <item>某个长度探测窗口里确实没有可用词形，才把长度加一；不存在"烧完整个指数预算才肯加长度"。</item>
/// </list>
/// 词形保持这些不变量：
/// <list type="number">
/// <item><b>不重复用边</b>（笔画不自叠）：枚举只承认每条边最多走一次的走法，重复用边的下标在
/// <see cref="TryUnrank"/> 里被判掉。这就是 <see cref="GridHelper.CanExtendPoint"/> 那条规则，
/// 只是把边键换成了打包整数。</item>
/// <item><b>画出来的图形也不自叠</b>：UI 画的是 <c>[自由起笔, ...词形]</c>，而那段自由起笔在词形自身
/// 坐标系里恒定对应"原点朝掉头方向（字符 's'）的那条边"，与玩家选了哪个起笔方向无关。
/// 所以 <see cref="TryUnrank"/> 额外禁止走步碰这条边：图形永远不会和自由起笔重合，
/// 代价是词形里不再出现 's'（UI 的"掉头 = 撤销"因此也不会误吞笔画）。</item>
/// <item><b>两两互不为前缀</b>（分词无歧义）：由 <see cref="PrefixTrie"/> 在 O(L) 内判定；
/// 长度相同的两个词形不可能是彼此的真前缀，此时判据退化成"互不相等"。</item>
/// </list>
/// 重复登记同一个真实词是幂等的（返回已登记的词形），因此两张翻译表始终互逆。
/// <para>
/// 前缀无关有一个推论，它同时决定了词表容量：<b>某一层的词形一旦被全部占用，更长的词形都会因为
/// "以某个已用词形为前缀"而全部失效，整个词表随即冻结</b>（5 个单笔画词形就足以冻结一切）。
/// 所以 <c>minLength</c> 应当 ≥ 2，且词条数要远小于最小那一层的可用词形数 ——
/// 两条规则过滤后各层的可用数是：1 笔 5 个、2 笔 25 个、3 笔 123 个、4 笔 595 个、5 笔 2853 个、6 笔 13583 个。
/// 真的分配不出来时抛出 <see cref="InvalidOperationException"/> 并说明原因，而不是像旧实现那样无限重试。
/// </para>
/// <para>本类只用 <see cref="GridHelper"/> 的方向表，不碰 Godot 运行时，可在无引擎环境下测试。</para>
/// </remarks>
public class Database(int seed)
{
    /// <summary>候选数不超过这个量级时全扫该长度，保证"这个长度真的满了"是精确判断。</summary>
    const long FullScanLimit = 250_000;

    /// <summary>大空间里每个长度的探测上限。要误判"这个长度满了"，得先占满同量级的词形，远超游戏词表规模。</summary>
    const long ProbeLimit = 8192;

    /// <summary>长度上限。前缀无关下短词形占满会让长词形全部失效，撞到上限说明词表本身不合法。</summary>
    const int MaxLength = 64;

    public GameTranslation Translation=new();

    /// <summary>已用词形的前缀树，兼任"已占用"集合与"前缀冲突"判据。</summary>
    readonly PrefixTrie used=new();

    // —— 几何：全部从 GridHelper 取，不留第二份方向表 ——
    static readonly int[] Dx;
    static readonly int[] Dy;
    static readonly int[] SlotOf=new int[128];

    /// <summary>除掉头（'s'）以外的 5 个转折字符，与 UI 的方向集合共用同一张表。</summary>
    static readonly char[] NextAngles=GridHelper.NextAngles;

    /// <summary>掉头方向（角 Chars.Length/2，即字符 's'）的坐标增量，用于判断"图形与自由起笔重合"。</summary>
    static readonly int OppositeDx;
    static readonly int OppositeDy;

    // 复用的草稿缓冲（Database 只在主线程用），避免每次探测都分配
    readonly char[] chars=new char[MaxLength];
    readonly int[] digits=new int[MaxLength];
    readonly HashSet<long> edges=new();

    static Database()
    {
        var count=GridHelper.Chars.Length;
        Dx=new int[count];
        Dy=new int[count];
        Array.Fill(SlotOf,-1);
        for(var i=0;i<count;i++)
        {
            var c=GridHelper.Chars[i];
            var d=GridHelper.Axis[c];
            Dx[i]=d.dx;
            Dy[i]=d.dy;
            SlotOf[c]=i;
        }
        OppositeDx=Dx[count/2];
        OppositeDy=Dy[count/2];
    }

    /// <summary>
    /// 为一个真实词取一个尚未占用的程序词形，并登记双向翻译。
    /// 同一个 <paramref name="real"/> 重复调用返回同一个词形（幂等）。
    /// </summary>
    /// <param name="real">真实词。</param>
    /// <param name="minLength">词形的最小笔画数。</param>
    public string Gen(string real,int minLength=1)
    {
        ArgumentNullException.ThrowIfNull(real);
        ArgumentOutOfRangeException.ThrowIfLessThan(minLength,1);

        if(Translation.ToGame.TryGetValue(real,out var registered))
        {
            return registered;   // 幂等：重复登记不会让两张表失去互逆
        }

        var hash=StableHash.Combine(real,seed);
        for(var length=minLength;length<=MaxLength;length++)
        {
            var total=Total(length);
            var start=total>uint.MaxValue?hash:hash%(uint)total;
            var probes=total<=FullScanLimit?total:ProbeLimit;
            for(long step=0;step<probes;step++)
            {
                var index=start+step;
                if(index>=total)
                {
                    index-=total;
                }
                if(!TryUnrank(index,length,out var token))
                {
                    continue;   // 这个下标会重复用边，不是合法词形
                }
                if(used.Conflicts(token))
                {
                    continue;   // 已被占用，或与已用词形构成前缀关系
                }
                Translation.RegisterRealToGame(real,token);
                used.Add(token);
                return token;
            }
        }

        throw new InvalidOperationException(
            $"词形分配失败：{real} 从 {minLength} 笔试到 {MaxLength} 笔都没有可用词形。"+
            "两两互不为前缀 ⇒ 某一层的词形一旦被全部占用，更长的词形都会因为以它为前缀而失效，"+
            "整个词表随之冻结；请减少短词形的词条数，或给这些词更大的 minLength");
    }
    /// <summary>
    /// 一次分配一对互为镜像的词形（例如"天"与"地"），返回 <c>(词形, 镜像)</c>。
    /// </summary>
    /// <remarks>
    /// 只在 <paramref name="real1"/> 上跑一次 <see cref="Gen"/>，<paramref name="real2"/> 取它的
    /// <see cref="GridHelper.Flip(string)"/> 镜像——两个字于是共用同一份"形状"，只是手性相反。
    /// <para>
    /// 镜像同样要登记进 <see cref="used"/>：前缀树的键是<b>逐字符 Flip</b> 后的路径，
    /// 词形 t 落在键 <c>Flip(t)</c> 上，而它的镜像 Flip(t) 落在键 <c>t</c> 上，两者<b>不是</b>同一条路径。
    /// 所以只 Add 一次并不能覆盖镜像的子树；漏掉它，后续就可能分配出"以镜像为前缀"的词形，
    /// 破坏两两互不为前缀这个分词前提。
    /// </para>
    /// </remarks>
    public (string token,string flipped) PairGen(string real1,string real2,int length)
    {
        var token=Gen(real1,length);
        var flipped=GridHelper.Flip(token);
        Translation.RegisterRealToGame(real2,flipped);
        used.Add(flipped);
        return (token,flipped);
    }
    public string Combine(string[] realWords, string word)
    {
        var sb=new StringBuilder();
        foreach(var realWord in realWords)
        {
            sb.Append(Translation.GetGameWord(realWord));
        }
        var result=sb.ToString();
        Translation.RegisterRealToGame(word,result);
        return result;
    }

    /// <summary>
    /// 把一个整数写成虚构文字：二进制、高位在左，0/1 分别换成已登记的「天/地」词形后首尾相接。
    /// </summary>
    /// <remarks>
    /// 负数在前面加「negative」词形。只做"数 → 词形"的换算，不改翻译表、也不占用新词形，
    /// 因此同一个数每次写出来都一样。需要的词（0/1、negative、over）还没登记、或已被词典删掉时
    /// 返回空串——宁可让显示层什么都不画，也不要画出一个缺了几位的数。
    /// </remarks>
    /// <param name="value">要写出的整数。</param>
    /// <returns>可直接绘制到界面上的词形串；无法写出时为空串。</returns>
    public string NumberToGameWord(int value)=>NumberToGameWord(new BigInteger(value));

    /// <summary>把整数写成虚构文字，支持负数与超出 <see cref="int"/> 范围的大整数。</summary>
    public string NumberToGameWord(BigInteger value)
    {
        var text=new StringBuilder();
        if(value.Sign<0)
        {
            if(!AppendWord(text,"negative"))
            {
                return string.Empty;
            }
            value=BigInteger.Abs(value);
        }

        foreach(var bit in BinaryDigits(value))
        {
            if(!AppendWord(text,bit=='0'?"0":"1"))
            {
                return string.Empty;
            }
        }
        return text.ToString();
    }

    /// <summary>
    /// 把有理数写成虚构文字：整数只写二进制；分数写成「分子 over 分母」；
    /// 负数在最前面加「negative」。分母已经约到正数，所以分数线两边都不带符号。
    /// </summary>
    public string NumberToGameWord(Rational value)
    {
        var text=new StringBuilder();
        if(value.Numerator.Sign<0)
        {
            if(!AppendWord(text,"negative"))
            {
                return string.Empty;
            }
        }

        var numerator=NumberToGameWord(BigInteger.Abs(value.Numerator));
        if(numerator.Length==0)
        {
            return string.Empty;
        }
        text.Append(numerator);

        if(!value.Denominator.IsOne)
        {
            if(!AppendWord(text,"over"))
            {
                return string.Empty;
            }
            var denominator=NumberToGameWord(value.Denominator);
            if(denominator.Length==0)
            {
                return string.Empty;
            }
            text.Append(denominator);
        }
        return text.ToString();
    }

    /// <summary>取一个真实词的词形并追加；该词还没登记时返回 false。</summary>
    bool AppendWord(StringBuilder text,string realWord)
    {
        var glyph=Translation.GetGameWord(realWord);
        if(glyph is null)
        {
            return false;
        }
        text.Append(glyph);
        return true;
    }

    /// <summary>把一个非负整数写成二进制串；零写成单个 <c>0</c>。</summary>
    static string BinaryDigits(BigInteger value)
    {
        if(value.IsZero)
        {
            return "0";
        }

        var digits=new StringBuilder();
        while(!value.IsZero)
        {
            digits.Append((value&BigInteger.One).IsZero?'0':'1');
            value>>=1;
        }
        var chars=digits.ToString().ToCharArray();
        Array.Reverse(chars);
        return new string(chars);
    }

    /// <summary>长度为 <paramref name="length"/> 的候选词形总数：首步 6 选，之后每步 5 选。</summary>
    static long Total(int length)
    {
        var total=(long)GridHelper.Chars.Length;
        for(var i=1;i<length;i++)
        {
            if(total>long.MaxValue/NextAngles.Length)
            {
                return long.MaxValue;
            }
            total*=NextAngles.Length;
        }
        return total;
    }

    /// <summary>
    /// 把 <paramref name="index"/> 还原成长度为 <paramref name="length"/> 的笔画串。
    /// 走步中若重复使用同一条边，说明这个下标不是合法词形，返回 <see langword="false"/>。
    /// </summary>
    bool TryUnrank(long index,int length,out string token)
    {
        token=string.Empty;
        if(length<=0||length>MaxLength||index<0||index>=Total(length))
        {
            return false;
        }

        // 混合进制分解：最低位是最后一步，最高位（首位）有 6 种取值
        var rest=index;
        for(var i=length-1;i>=1;i--)
        {
            digits[i]=(int)(rest%NextAngles.Length);
            rest/=NextAngles.Length;
        }
        digits[0]=(int)rest;

        // 边键打包：坐标范围随长度线性增长，用 (x+bias, y+bias) 编号一个端点，再拼上两个端点
        var bias=length+1;
        var side=2*bias+1;
        var side2=(long)side*side;
        edges.Clear();

        var x=0;
        var y=0;
        var prev=0;
        for(var i=0;i<length;i++)
        {
            int dir;
            var turn='\0';
            if(i==0)
            {
                dir=digits[0];                                  // 首步：6 个绝对方向任选
            }
            else
            {
                turn=NextAngles[digits[i]];                      // 之后：5 个非掉头转折
                dir=(prev+SlotOf[turn])%GridHelper.Chars.Length;
            }

            var nx=x+Dx[dir];
            var ny=y+Dy[dir];

            // UI 画出来的图形是 [自由起笔, ...词形]：那段自由起笔在词形自身坐标系里恒定对应
            // "原点朝掉头方向（角 Chars.Length/2，即字符 's'）的那条边"，与玩家选了哪个起笔方向无关
            // （已用六种起笔方向逐一验证）。所以只要走步碰了这条边，图形必然与自由起笔重合 ——
            // 直接判为非法词形。它同时保证词形里不会出现 's'，UI 的"掉头 = 撤销"再也不会误吞笔画。
            if((x==0&&y==0&&nx==OppositeDx&&ny==OppositeDy)||
               (x==OppositeDx&&y==OppositeDy&&nx==0&&ny==0))
            {
                return false;
            }

            var a=(x+bias)*side+(y+bias);
            var b=(nx+bias)*side+(ny+bias);
            var edge=a<b?a*side2+b:b*side2+a;                    // 无向归一化：小端点在前
            if(!edges.Add(edge))
            {
                return false;                                    // 这条边用过了 → 画出来会自叠
            }

            // 存进词形的字符：首步是绝对方向（相对 'w'），之后是相对转折 —— 与 Spell/NSpellInput 的约定一致
            chars[i]=i==0?GridHelper.Chars[dir]:turn;
            x=nx;
            y=ny;
            prev=dir;
        }

        token=new string(chars,0,length);
        return true;
    }

    public int GetSeed()
    {
        return seed;
    }
    /// <summary>
    /// 已用词形的前缀树，只支持"插入词形"和"这个候选词形能不能用"：
    /// 冲突 = 已被占用，或与某个已用词形互为前缀（任一方向）。
    /// </summary>
    /// <remarks>
    /// 走路径时字符一律先过 <see cref="GridHelper.Flip"/>。这样做把"词形与它的镜像应当互不冲突"
    /// 变成了"两者恰好落在互为镜像的两条路径上"——<b>它们仍然是两条不同的路径</b>，
    /// 所以词形与镜像必须各 Add 一次（见 <see cref="PairGen"/>），只登记一侧会漏掉镜像的整棵子树。
    /// <para>
    /// 归一化必须是 <see cref="GridHelper.Flip"/> 这种<b>对合</b>，不能用多对一的投影：
    /// 投影会把无关词形并到一起，白白吃掉词表容量（实测 3 笔词形能从 123 个压到 26 个）。
    /// </para>
    /// </remarks>
    sealed class PrefixTrie
    {
        sealed class Node
        {
            public readonly Node[] Next=new Node[GridHelper.Chars.Length];
            /// <summary>这个节点本身是不是某个已用词形的末尾。</summary>
            public bool Terminal;
            /// <summary>子树里（含自身）有多少个已用词形经过或终止。</summary>
            public int Below;
        }

        readonly Node root=new();

        /// <summary>候选词形是否与已用词形冲突（被占用，或互为前缀）。</summary>
        public bool Conflicts(string token)
        {
            var node=root;
            for(var i=0;i<token.Length;i++)
            {
                if(node.Terminal)
                {
                    return true;        // 某个已用词形是它的真前缀
                }
                var slot=SlotOf[GridHelper.Flip(token[i])];
                if(slot<0)
                {
                    return true;        // 非法字符：保守拒绝
                }
                var next=node.Next[slot];
                if(next is null)
                {
                    return false;       // 树里没有这一段，往后再长也不可能冲突
                }
                node=next;
            }
            return node.Terminal||node.Below>0;   // 完全相等，或它是某个已用词形的真前缀
        }

        public void Add(string token)
        {
            var node=root;
            node.Below++;
            foreach(var c in token)
            {
                var slot=SlotOf[GridHelper.Flip(c)];
                node=node.Next[slot]??=new Node();
                node.Below++;
            }
            node.Terminal=true;
        }
    }
}
