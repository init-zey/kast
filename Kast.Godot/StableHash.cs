namespace Kast.Godot;

/// <summary>
/// 稳定哈希：结果不依赖运行时的随机种子（区别于 string.GetHashCode），
/// 同一组输入在任何进程 / 机器 / .NET 版本下结果都一致。
/// 算法：FNV-1a（32 位），字节按固定的小端顺序混入。
/// </summary>
public static class StableHash
{
    private const uint OffsetBasis = 2166136261u;
    private const uint Prime       = 16777619u;

    /// <summary>计算 1 个 string + 1 个 int 的组合哈希。</summary>
    public static uint Combine(string text, int value)
    {
        uint hash = OffsetBasis;

        // 1) 先混入"是否为 null"的标志，使 null 与 "" 得到不同结果
        hash = MixByte(hash, text is null ? (byte)0 : (byte)1);

        // 2) 字符串按 UTF-16 的字符逐个拆成 2 个字节（小端固定顺序）
        if (text is not null)
        {
            foreach (char c in text)
            {
                hash = MixByte(hash, (byte)(c & 0xFF));   // 低字节
                hash = MixByte(hash, (byte)(c >> 8));     // 高字节
            }
        }

        // 3) 分隔符：消除边界歧义，例如 ("a", ...) 与 ("a\0", ...) 不会撞车
        hash = MixByte(hash, 0xFF);

        // 4) int 固定按小端 4 字节混入（与平台字节序无关）
        unchecked
        {
            hash = MixByte(hash, (byte)value);
            hash = MixByte(hash, (byte)(value >> 8));
            hash = MixByte(hash, (byte)(value >> 16));
            hash = MixByte(hash, (byte)(value >> 24));
        }

        return hash;
    }

    private static uint MixByte(uint hash, byte b)
    {
        unchecked
        {
            hash ^= b;
            hash *= Prime;
            return hash;
        }
    }
}