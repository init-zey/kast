using Kast.Core;

namespace Kast.Godot;

using O=System.Func<Context,object>;

public partial class KastAlphabet : Alphabet<O>
{
    public void AddObjectTypes()
    {
        AddNoun(db.Gen("object",5),_=>typeof(NObjectType));
        AddNoun(db.Combine(["cast","thing"],"caster"),_=>typeof(NCaster));
        AddNoun(db.Combine(["1","thing"],"rock"),_=>typeof(NRock));
        AddNoun(db.Combine(["big","plant"],"tree"),_=>typeof(NTree));
        AddNoun(db.Combine(["arm","thing"],"broken_arm"),_=>typeof(NArm));
        AddNoun(db.Combine(["road","cast"],"roadsign"),_=>typeof(NRoadSign));
        AddNoun(db.Combine(["sea","road"],"dock"),_=>typeof(NRoadSign));
    }
}