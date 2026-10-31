using System.Globalization;

namespace SCTrainer;

public sealed record NpcSnapshot(uint Id, bool Present, bool Removing, bool Controller, uint MoveFlags, int Health,
    float Speed, float Distance, string Type, string PawnState, string AiState, string Weapon, string Target)
{
    private bool StateIs(params string[] states) => states.Any(s =>
        s.Equals(PawnState, StringComparison.OrdinalIgnoreCase) || s.Equals(AiState, StringComparison.OrdinalIgnoreCase));

    public string Condition => !Present ? "Removed" : Removing ? "Removing" :
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

    public bool CanEditHealth => Present && !Removing && Condition == "Alive";

    public static NpcSnapshot[] Parse(string response)
    {
        var lines = response.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0 || lines[0] != "NPCS:1" || lines.Length > 33)
            throw new FormatException("Invalid NPC snapshot. Restart using Launch Trainer.cmd.");
        var result = new List<NpcSnapshot>();
        var ids = new HashSet<uint>();
        foreach (var line in lines.Skip(1))
        {
            var p = line.Split('|');
            if (p.Length != 13) throw new FormatException("Incomplete NPC snapshot.");
            uint id = uint.Parse(p[0], CultureInfo.InvariantCulture);
            bool Flag(int i) => p[i] switch { "0" => false, "1" => true, _ => throw new FormatException("Invalid NPC flag.") };
            float Number(int i) => float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && float.IsFinite(n) && n >= 0
                ? n : throw new FormatException("Invalid NPC measurement.");
            if (id == 0 || !ids.Add(id)) throw new FormatException("Invalid NPC identity.");
            result.Add(new(id, Flag(1), Flag(2), Flag(3), uint.Parse(p[4], CultureInfo.InvariantCulture), int.Parse(p[5], CultureInfo.InvariantCulture),
                Number(6), Number(7), p[8], p[9], p[10], p[11], p[12]));
        }
        return result.ToArray();
    }
}
