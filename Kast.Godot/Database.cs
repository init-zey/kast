using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Candidate=(char ch,(int x,int y) point);

namespace Kast.Godot;

public class Database(int seed)
{
    Dictionary<string,string> ToRealTranslations=new();
    Dictionary<string,string> ToGameTranslations=new();
    /// <summary>
    /// 为一个真实词取一个尚未占用的程序词形，并登记双向翻译。
    /// </summary>
    /// <remarks>
    /// 从 <paramref name="minLength"/> 个字符起算：先固定长度、依次换 <c>seedOffset</c> 重试
    /// （同一长度最多试 <c>5^length+1</c> 次，每次都是一条不同的随机路径）；这个长度上取不到
    /// 空闲词形时才把长度加一、重新从 <c>seedOffset=0</c> 试起，所以越晚登记的真实词词形可能越长。
    /// <para>
    /// 只保证程序词形唯一：<c>ToRealTranslations</c> 的键不会重复，因此一个词形永远只对应一个
    /// 真实词；但不保证 <paramref name="real"/> 只被登记一次 —— 对同一个真实词重复调用会拿到新的
    /// 词形并覆盖 <c>ToGameTranslations</c>，而旧词形仍留在 <c>ToRealTranslations</c> 里（它同时
    /// 兼任“已用词形表”，不能删，否则该词形会被再次分配）。结果是两张表不再互逆：
    /// <c>ToRealTranslations[ToGameTranslations[real]] == real</c> 仍成立，但
    /// <c>ToGameTranslations[ToRealTranslations[旧词形]]</c> 会返回新词形而不是旧词形。
    /// 需要幂等请在调用前先查 <see cref="GetGameWord"/>。
    /// </para>
    /// </remarks>
    public string Gen(string real, int minLength = 2)
    {
        var length = minLength;
        var seedOffset=0;
        string game;
        do
        {
            game = Build(real, length, seedOffset);
            seedOffset++;
            if(seedOffset>Math.Pow(GridHelper.Chars.Length-1,length))
            {
                seedOffset=0;
                length++;
            }
        }
        while(ToRealTranslations.ContainsKey(game));
        ToRealTranslations[game] = real;
        ToGameTranslations[real] = game;
        return game;
    }

    string Build(string real, int length, int seedOffset)
    {
        var rseed=(ulong)StableHash.Combine(real,seed+seedOffset+length);
        var gamearr=new char[length];
        var edges=new HashSet<((int,int),(int,int))>();
        var x=0;
        var y=0;
        var n=0;
        while(n<length)
        {
            var u=GD.RandFromSeed(ref rseed);
            var candidates = GridHelper.Chars.Select<char,Candidate>(c => (c,GridHelper.ExtendPoint(x,y,c)))
                .Where(candidate=>GridHelper.CanExtendPoint(edges,x,y,candidate.point)).ToArray();
            var result=0;
            while(Helper.GetUnbiasedIndex(u,candidates.Length,out result))
            {
                u=GD.RandFromSeed(ref rseed);
            }
            var candidate=candidates[result];
            edges.Add(GridHelper.NormalizeEdge((x,y),candidate.point));
            x=candidate.point.x;
            y=candidate.point.y;
            gamearr[n]=candidate.ch;
            n++;
        }
        return new string(gamearr);
    }
    public string GetGameWord(string realWord)
    {
        return ToGameTranslations.TryGetValue(realWord,out var game)?game:null;
    }
    public string GetRealWord(string gameWord)
    {
        return ToRealTranslations.TryGetValue(gameWord,out var real)?real:null;
    }

    public int GetSeed()
    {
        return seed;
    }

    public string AsText()
    {
        return "Real Word,Game Word\n"+string.Join('\n',ToGameTranslations.Select(kvp=>$"{kvp.Key},{kvp.Value}"));
    }
}
