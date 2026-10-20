using System;
using Kast.Core;
using O=System.Func<Kast.Core.Context,object>;

namespace Kast.Godot;

/// <summary>
/// 二进制数词：天 = 0、地 = 1，位序列最左位是最高位（<c>地天天地</c> 读作 9）。
/// </summary>
/// <remarks>
/// 位词条本身登记在 <c>KastAlphabet.Words.cs</c>（同时声明 N / Num / Bit / Factor / Term / Expr 六个范畴），
/// 本文件负责把位拼成数、做四则运算，以及把数交给动词当宾语。
/// <para>
/// 与词条值的形状不同，数词的值不是"上下文变换"而是 <see cref="NumSlot"/>——
/// 一个能取出精确有理数、并记住自己位数的槽。之所以要记位数，是因为二进制拼接是左结合的，
/// 左侧那个数要按右侧的位数左移。
/// </para>
/// <para>
/// 运算结果一律用 <see cref="Rational"/>（整数之比）表示，分数不经过浮点数，因此不会掉精度。
/// 文法只允许二元产生式，所以乘除与加减各用一个"运算符 + 右操作数"的中间范畴来搭出优先级：
/// <c>Expr -&gt; Expr AddTerm</c>、<c>AddTerm -&gt; AddOp Term</c>、
/// <c>Term -&gt; Term MulFactor</c>、<c>MulFactor -&gt; MulOp Factor</c>。
/// 乘除的右操作数必须是原子 Factor；若右边也写成 Term，<c>a / b / c</c> 会多出
/// <c>a / (b / c)</c> 这种右结合读法。每个层级都左结合，和常见的算术读法一致。
/// </para>
/// </remarks>
public partial class KastAlphabet
{
    /// <summary>语言里的四则运算符；<c>over</c> 是除法的另一种写法，也用来显示分数。</summary>
    enum ArithmeticOp
    {
        Add,
        Subtract,
        Multiply,
        Divide,
    }

    /// <summary>
    /// 数词绑进 <c>.o</c> 时用的槽：精确值 + 二进制位数。
    /// </summary>
    /// <remarks>
    /// 绑成 <c>slot.Get</c> 这个委托，宾语求值时直接调用它。
    /// 位数只在 <c>Num -&gt; Num Bit</c> 的二进制拼接里用到；四则运算的结果位数记 0，它已经是一个完整值。
    /// </remarks>
    sealed class NumSlot(int bits,Func<Context,Rational?> value)
    {
        public readonly int Bits=bits;

        /// <summary>取出精确有理数；除以零等无法求值的情况返回 null。</summary>
        public Rational? RationalOf(Context ctx)=>value(ctx);

        /// <summary>交给动词的旧接口：整数仍返回 int，分数/大数返回 <see cref="Rational"/>。</summary>
        public object Get(Context ctx)
        {
            var result=value(ctx);
            return result is null?null!:result.Value.ToGameValue();
        }
    }

    /// <summary>
    /// 一个运算符连同它的右操作数，例如 <c>add 3</c> 或 <c>multiply 4</c>。
    /// 乘除层与加减层各用一次，避免三元产生式。
    /// </summary>
    sealed class OpTerm(ArithmeticOp op,NumSlot operand)
    {
        public readonly ArithmeticOp Op=op;
        public readonly NumSlot Operand=operand;
    }

    void AddNumerals()
    {
        AddArithmeticWords();

        // 二进制拼接：Num -> Num Bit（左边的数左移一位，再补上这一位）。
        // "Num -> Bit"（一元规则）文法不支持，所以位词条直接登记成 Num（见 Words）。
        Grammar.AddRule("Num -> Num Bit",AppendBit);

        // 乘除层：Term -> Term MulFactor，右操作数由 MulFactor -> MulOp Factor 包起来。
        // 右边必须是原子 Factor 而不是 Term，否则 a / b / c 会同时有两种结合法（右结合的读法会算出 a/(b/c)）。
        Grammar.AddRule("MulFactor -> MulOp Factor",(op,factor)=>new OpTerm((ArithmeticOp)op!,AsNum(factor)));
        Grammar.AddRule("Term -> Term MulFactor",(left,factor)=>ApplyOp(AsNum(left),(OpTerm)factor!));
        // 把一次乘法/除法的结果提升到加法层（Term -> Expr 需要一元规则，CNF 不支持）。
        Grammar.AddRule("Expr -> Term MulFactor",(term,factor)=>ApplyOp(AsNum(term),(OpTerm)factor!));

        // 加减层：Expr -> Expr AddTerm，右操作数由 AddTerm -> AddOp Term 包起来。
        // 右边的 Term 不含加减，所以 a - b - c 只有左结合一种读法。
        Grammar.AddRule("AddTerm -> AddOp Term",(op,term)=>new OpTerm((ArithmeticOp)op!,AsNum(term)));
        Grammar.AddRule("Expr -> Expr AddTerm",(left,term)=>ApplyOp(AsNum(left),(OpTerm)term!));

        // 负数：一元符号只作用于原子 Factor，绑定比乘除、加减都紧（-1 * 2 读作 (-1) * 2）。
        // Term / Expr 各自有一条把负 Factor 提升上来的规则。
        Grammar.AddRule("Factor -> Neg Factor",(_,factor)=>Negate(factor));
        Grammar.AddRule("Term -> Neg Factor",(_,factor)=>Negate(factor));
        Grammar.AddRule("Expr -> Neg Factor",(_,factor)=>Negate(factor));

        // 直接写出的多位二进制数也要能当 Factor/Term/Expr；单个位词条已在 Words 里登记。
        Grammar.AddRule("Factor -> Num Bit",AppendBit);
        Grammar.AddRule("Term -> Num Bit",AppendBit);
        Grammar.AddRule("Expr -> Num Bit",AppendBit);

        // 数词作宾语：Num 是"取出这个数字"的槽（不像 N 的负载是上下文变换）。
        // VP -> Vt X 保证一句话只挂一个宾语；X 可以是原始位串 Num，也可以是算术表达式 Expr。
        Grammar.AddRule("VP -> Vt Num",MakeVerbWithNumber);
        Grammar.AddRule("VP -> Vt Expr",MakeVerbWithNumber);
    }

    /// <summary>登记四则运算符、负号与分数线的词形；这些词也用于 <c>display</c> 的结果文字。</summary>
    void AddArithmeticWords()
    {
        AddWord(db.Gen("add",4),"AddOp",ArithmeticOp.Add);
        AddWord(db.Gen("subtract",4),"AddOp",ArithmeticOp.Subtract);
        AddWord(db.Gen("multiply",4),"MulOp",ArithmeticOp.Multiply);
        AddWord(db.Gen("divide",4),"MulOp",ArithmeticOp.Divide);
        AddWord(db.Gen("negative",4),"Neg",true);
        AddWord(db.Gen("over",4),"MulOp",ArithmeticOp.Divide);   // 分数写法，也是显示时的分数线
    }

    /// <summary>把数词的值统一成槽：终结位词条的负载要加 <c>#bit</c> 才读得出 0/1。</summary>
    static NumSlot AsNum(object value)
    {
        if(value is NumSlot slot)
        {
            return slot;
        }
        if(value is null)
        {
            throw new InvalidOperationException("数词的值不能为空");
        }
        var bit=(O)value;
        return new NumSlot(1,c=>Rational.FromInteger(Convert.ToInt32(bit(c.WithFlag("#bit")))));
    }

    /// <summary>Num -&gt; Num Bit：左边的数左移一位，再补上这一位。</summary>
    static NumSlot AppendBit(object high,object low)
    {
        var upper=AsNum(high);
        var lower=AsNum(low);
        return new NumSlot(upper.Bits+1,c =>
        {
            var upperValue=upper.RationalOf(c);
            var lowerValue=lower.RationalOf(c);
            if(upperValue is null||lowerValue is null)
            {
                return null;
            }
            return upperValue.Value*2+lowerValue.Value;
        });
    }

    /// <summary>把一个"运算符 + 右操作数"应用到左操作数上；除以零返回空槽。</summary>
    static NumSlot ApplyOp(NumSlot left,OpTerm term)
        =>new(0,c =>
        {
            var leftValue=left.RationalOf(c);
            var rightValue=term.Operand.RationalOf(c);
            if(leftValue is null||rightValue is null)
            {
                return null;
            }
            return Apply(term.Op,leftValue.Value,rightValue.Value);
        });

    /// <summary>一元负号：只翻转符号，仍保持精确有理数。</summary>
    static NumSlot Negate(object value)
    {
        var operand=AsNum(value);
        return new NumSlot(0,c => operand.RationalOf(c) is Rational number?-number:null);
    }

    /// <summary>执行一次四则运算；除数为零时返回 null（语言层不应让整句话崩掉）。</summary>
    static Rational? Apply(ArithmeticOp op,Rational left,Rational right)
        =>op switch
        {
            ArithmeticOp.Add=>left+right,
            ArithmeticOp.Subtract=>left-right,
            ArithmeticOp.Multiply=>left*right,
            ArithmeticOp.Divide=>right.IsZero?null:left/right,
            _=>null,
        };

    /// <summary>VP -&gt; Vt Num / VP -&gt; Vt Expr：把唯一的数词宾语绑进 <c>.o</c>，再执行动词。</summary>
    static object MakeVerbWithNumber(object verb,object num)
        =>(Context ctx) =>
        {
            var inner=new Context(ctx);
            inner.Add(".o",(O)AsNum(num).Get);
            return((Func<Context,Context>)verb!)(inner);
        };
}
