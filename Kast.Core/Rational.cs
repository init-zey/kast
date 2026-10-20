using System;
using System.Numerics;

namespace Kast.Core;

/// <summary>
/// 以两个整数之比表示的有理数：分子、分母始终互质，分母为正，零统一写成 0/1。
/// </summary>
/// <remarks>
/// 游戏里的"浮点数"一律用它表示，四则运算保持精确，不引入二进制浮点误差。
/// 分母为零没有定义：构造函数与除法运算符都会抛 <see cref="DivideByZeroException"/>；
/// 语言层的除法会先检查除数，避免玩家写出"除以零"时让整句话求值失败。
/// </remarks>
public readonly struct Rational : IEquatable<Rational>
{
    /// <summary>分子，符号由它携带。</summary>
    public BigInteger Numerator { get; }

    /// <summary>分母，总是正数。</summary>
    public BigInteger Denominator { get; }

    /// <summary>构造并约分；分母为负时把符号移到分子，分母为零时抛异常。</summary>
    public Rational(BigInteger numerator,BigInteger denominator)
    {
        if(denominator.IsZero)
        {
            throw new DivideByZeroException("有理数的分母不能为零");
        }
        if(denominator.Sign<0)
        {
            numerator=-numerator;
            denominator=-denominator;
        }
        var gcd=BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator),denominator);
        if(!gcd.IsOne)
        {
            numerator/=gcd;
            denominator/=gcd;
        }
        Numerator=numerator;
        Denominator=denominator;
    }

    public static readonly Rational Zero=new(0,1);
    public static readonly Rational One=new(1,1);

    /// <summary>把整数写成 n/1。</summary>
    public static Rational FromInteger(BigInteger value)=>new(value,1);

    public bool IsZero=>Numerator.IsZero;
    public bool IsOne=>Numerator.IsOne&&Denominator.IsOne;
    public bool IsInteger=>Denominator.IsOne;
    public bool IsNegative=>Numerator.Sign<0;
    public int Sign=>Numerator.Sign;

    public static Rational operator +(Rational a,Rational b)
        =>new(a.Numerator*b.Denominator+b.Numerator*a.Denominator,a.Denominator*b.Denominator);

    public static Rational operator -(Rational a,Rational b)
        =>new(a.Numerator*b.Denominator-b.Numerator*a.Denominator,a.Denominator*b.Denominator);

    public static Rational operator -(Rational a)=>new(-a.Numerator,a.Denominator);

    public static Rational operator *(Rational a,Rational b)
        =>new(a.Numerator*b.Numerator,a.Denominator*b.Denominator);

    public static Rational operator /(Rational a,Rational b)
    {
        if(b.IsZero)
        {
            throw new DivideByZeroException("不能除以零");
        }
        return new(a.Numerator*b.Denominator,a.Denominator*b.Numerator);
    }

    public static bool operator ==(Rational a,Rational b)=>a.Equals(b);
    public static bool operator !=(Rational a,Rational b)=>!a.Equals(b);

    public static implicit operator Rational(int value)=>FromInteger(value);
    public static implicit operator Rational(long value)=>FromInteger(value);
    public static implicit operator Rational(BigInteger value)=>FromInteger(value);

    public bool Equals(Rational other)
        =>Numerator==other.Numerator&&Denominator==other.Denominator;

    public override bool Equals(object? obj)=>obj is Rational other&&Equals(other);

    public override int GetHashCode()=>HashCode.Combine(Numerator,Denominator);

    public override string ToString()
        =>Denominator.IsOne?Numerator.ToString():$"{Numerator}/{Denominator}";

    /// <summary>
    /// 交给游戏语义层的表示：整数（且落在 <see cref="int"/> 范围内）返回 <see cref="int"/>，
    /// 其余返回 <see cref="Rational"/> 本身。这样既保住旧动词对整数宾语的假设，
    /// 又让分数与超出 int 范围的数原样流到显示层。
    /// </summary>
    public object ToGameValue()
    {
        if(Denominator.IsOne&&Numerator>=int.MinValue&&Numerator<=int.MaxValue)
        {
            return (int)Numerator;
        }
        return this;
    }
}
