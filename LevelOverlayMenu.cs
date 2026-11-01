namespace SCTrainer;

internal sealed record LevelMenuChoice(string Map,string Label)
{
    public string Mission {get {int split=Label.IndexOf("  /  ",StringComparison.Ordinal);return split<0?"Additional maps":Label[..split];}}
}

internal sealed class LevelOverlayMenu
{
    private readonly Func<LevelMenuChoice[]> _catalog;
    private readonly Func<LevelMenuChoice,Task<string>> _load;
    private string? _mission,_confirm;
    public bool Busy {get;private set;}
    public LevelOverlayMenu(Func<LevelMenuChoice[]> catalog,Func<LevelMenuChoice,Task<string>> load){_catalog=catalog;_load=load;}
    private static GameMenuEntry Row(string label,string key,bool available=true,bool navigation=false)=>new(label,false,available,key,null,false,navigation);
    public GameMenuFrame Frame
    {
        get {
            var rows=new List<GameMenuEntry>();
            string title="LOAD LEVEL / MAIN MENU ONLY";
            var maps=_catalog();
            if(_confirm is not null){
                title="LOAD SELECTED MAP?";
                rows.Add(Row("Cancel","level-cancel",navigation:true));
                rows.Add(Row($"Load {_confirm}","load:"+_confirm,!Busy));
            } else if(_mission is not null){
                title="LOAD LEVEL / "+_mission;
                foreach(var map in maps.Where(m=>m.Mission==_mission))rows.Add(Row(map.Map,"level:"+map.Map,navigation:true));
            } else {
                foreach(var mission in maps.Select(m=>m.Mission).Distinct())rows.Add(Row(mission,"mission:"+mission,navigation:true));
                if(maps.Length==0)rows.Add(Row("No installed maps found","no-levels",false));
            }
            rows.Add(Row("Back","back",navigation:true));
            return new($"levels:{_mission}:{_confirm}",title,rows.ToArray());
        }
    }
    public void Open(){_mission=null;_confirm=null;}
    public bool Back(){if(_confirm is not null){_confirm=null;return true;}if(_mission is not null){_mission=null;return true;}return false;}
    public async Task<string> ActivateAsync(string key)
    {
        var maps=_catalog();
        if(key=="level-cancel"){_confirm=null;return "Load cancelled.";}
        if(key.StartsWith("mission:",StringComparison.Ordinal)){
            var mission=key[8..];
            if(!maps.Any(m=>m.Mission==mission))throw new InvalidOperationException("That mission is no longer installed.");
            _mission=mission;_confirm=null;return "Return to the game's main menu before loading a map.";
        }
        if(key.StartsWith("level:",StringComparison.Ordinal)){
            string map=key[6..];
            if(!maps.Any(m=>m.Map==map&&m.Mission==_mission))throw new InvalidOperationException("That map is no longer available in this mission.");
            _confirm=map;return "Load only from the game's main menu. Unsaved progress will be lost.";
        }
        if(!key.StartsWith("load:",StringComparison.Ordinal)||key[5..]!=_confirm)throw new InvalidOperationException("Select and confirm a map first.");
        if(Busy)return "A level is already loading.";
        var selected=maps.FirstOrDefault(m=>m.Map==_confirm&&m.Mission==_mission)??throw new InvalidOperationException("The selected map is no longer installed.");
        Busy=true;
        try {return await _load(selected);}
        finally {Busy=false;}
    }
}
