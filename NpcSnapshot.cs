using System.Globalization;

namespace SCTrainer;

public readonly record struct NpcPosition(float X,float Y,float Z)
{
    public bool IsValid => float.IsFinite(X)&&float.IsFinite(Y)&&float.IsFinite(Z)&&Math.Abs(X)<=1000000&&Math.Abs(Y)<=1000000&&Math.Abs(Z)<=1000000;
    public static NpcPosition Parse(string x,string y,string z)
    {
        var position=new NpcPosition(float.Parse(x,NumberStyles.Float,CultureInfo.InvariantCulture),float.Parse(y,NumberStyles.Float,CultureInfo.InvariantCulture),float.Parse(z,NumberStyles.Float,CultureInfo.InvariantCulture));
        if(!position.IsValid)throw new ArgumentException("Coordinates must be finite game-unit values between -1000000 and 1000000.");
        return position;
    }
}

public enum NpcActivityEdit { Idle=1, Walking=2, Running=3, Searching=4, Alerted=5 }

public enum NpcConditionEdit { Alive=1, KnockedOut=2, Dead=3 }

public sealed record NpcSnapshot(uint Id, bool Present, bool Removing, bool Controller, uint MoveFlags, int Health,
    float Speed, float Distance, string Type, string PawnState, string AiState, string Weapon, string Target, bool TrainerSpawned=true, float X=0,float Y=0,float Z=0,string InstanceName="")
{
    private bool StateIs(params string[] states) => states.Any(s =>
        s.Equals(PawnState, StringComparison.OrdinalIgnoreCase) || s.Equals(AiState, StringComparison.OrdinalIgnoreCase));

    public string Condition => !Present ? "Removed" : Removing ? "Removing" : StateIs("s_Groggy") ? "Reviving" :
        StateIs("s_Unconscious", "s_InitiallyUnconscious", "s_Electrocuted") ? "Knocked out" :
        StateIs("s_Dead", "s_Dying", "s_InitiallyDead") ? "Dead" : Health > 0 ? "Alive" : "Incapacitated";

    public string Movement => Condition != "Alive" ? "—" : !float.IsFinite(Speed) ? "Unknown" :
        Speed < 5 ? "Stationary" : MoveFlags is 4 or 6 or 9 ? "Running" :
        MoveFlags is 1 or 2 or 3 or 5 or 11 ? "Walking" : "Moving";

    public string Activity => Condition != "Alive" ? "—" : StateIs("s_Stunned") ? "Stunned" :
        StateIs("s_Grabbed") ? "Grabbed" : StateIs("s_Carried") ? "Carried" :
        StateIs("s_Investigate") ? "Searching" : StateIs("s_Alert") ? "Alerted" :
        StateIs("s_ClimbLadder", "s_ClimbLadderNEW") ? "Climbing" :
        Speed >= 5 ? Movement : StateIs("s_Default", "DefaultState") ? "Idle" : "Other";

    public bool CanSetCondition(NpcConditionEdit condition) => Present && !Removing && Controller && condition switch {
        NpcConditionEdit.Alive => Condition == "Knocked out",
        NpcConditionEdit.KnockedOut => Condition == "Alive",
        NpcConditionEdit.Dead => Condition is "Alive" or "Knocked out",
        _ => false
    };

    public bool CanEditActivity => Present && !Removing && Controller && Condition == "Alive" &&
        (AiState.Equals("s_Default",StringComparison.OrdinalIgnoreCase)||AiState.Equals("s_Investigate",StringComparison.OrdinalIgnoreCase)||AiState.Equals("s_Alert",StringComparison.OrdinalIgnoreCase));

    public string Origin => TrainerSpawned ? "Trainer spawned" : "Map NPC";
    public NpcPosition Position => new(X,Y,Z);
    public bool ShowInRoster => Present || !TrainerSpawned;
    public bool CanRemove => TrainerSpawned && Present && !Removing;
    public bool CanEditPosition => CanEditActivity && !StateIs("s_Grabbed","s_Carried");

    public bool CanEditHealth => Present && !Removing && Condition == "Alive";

    public static NpcSnapshot[] Parse(string response)
    {
        var lines = response.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0 || lines[0] != "NPCS:2" || lines.Length > 257)
            throw new FormatException("Invalid NPC snapshot. Restart using Launch Trainer.cmd.");
        var result = new List<NpcSnapshot>();
        var ids = new HashSet<uint>();
        foreach (var line in lines.Skip(1))
        {
            var p = line.Split('|');
            if (p.Length != 18) throw new FormatException("Incomplete NPC snapshot.");
            uint id = uint.Parse(p[0], CultureInfo.InvariantCulture);
            bool Flag(int i) => p[i] switch { "0" => false, "1" => true, _ => throw new FormatException("Invalid NPC flag.") };
            float Number(int i) => float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && float.IsFinite(n) && n >= 0
                ? n : throw new FormatException("Invalid NPC measurement.");
            if (id == 0 || !ids.Add(id)) throw new FormatException("Invalid NPC identity.");
            var position=NpcPosition.Parse(p[14],p[15],p[16]);
            result.Add(new(id, Flag(1), Flag(2), Flag(3), uint.Parse(p[4], CultureInfo.InvariantCulture), int.Parse(p[5], CultureInfo.InvariantCulture),
                Number(6), Number(7), p[8], p[9], p[10], p[11], p[12], Flag(13),position.X,position.Y,position.Z,p[17]));
        }
        return result.ToArray();
    }
}
