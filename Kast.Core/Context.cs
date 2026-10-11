namespace Kast.Core;

public class Context
{
    Context? Parent;
    Dictionary<string,object> Items=new();
    public Context()
    {
    }
    public Context(Context parent)
    {
        Parent=parent;
    }

    public bool GetItem(string key, out object result)
    {
        return Items.TryGetValue(key,out result!) || (Parent?.GetItem(key, out result) ?? false);
    }
    public object? GetItem(string key)
    {
        return GetItem(key, out var item)?item:null;
    }
    public void Add(string key,object item)
    {
        Items.Add(key,item);
    }
    /// <summary>
    /// 写入或改写<b>本层</b>的键，父层已有的同名键不动（与 <see cref="GetItem"/> 的就近遮蔽语义一致）。
    /// </summary>
    /// <returns>本层原本已有该键并被改写时为 true；新插入时为 false。</returns>
    public bool Set(string key,object item)
    {
        var existed=Items.ContainsKey(key);
        Items[key]=item;
        return existed;
    }
    public IEnumerable<string> GetKeys(bool union=true)
    {
        var result=Items.Keys;
        if(union&&Parent!=null)
        {
            return result.Union(Parent.GetKeys());
        }
        else
        {
            return result;
        }
    }
}