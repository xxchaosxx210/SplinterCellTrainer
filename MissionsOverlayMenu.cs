namespace SCTrainer;

internal sealed class MissionsOverlayMenu
{
    private readonly LevelOverlayMenu _levels;
    private readonly Func<Task<string>> _complete;
    private readonly Func<bool> _available;
    private bool _showLevels;
    public bool Busy {get;private set;}
    public MissionsOverlayMenu(LevelOverlayMenu levels,Func<Task<string>> complete,Func<bool> available){_levels=levels;_complete=complete;_available=available;}
    public GameMenuFrame Frame => _showLevels?_levels.Frame:new("missions","MISSIONS",new[]{
        new GameMenuEntry("Load level",false,true,"levels",null,false,true),
        new GameMenuEntry("Complete next objective",false,!Busy&&_available(),"objective-next",null,false),
        new GameMenuEntry("Back",false,true,"back",null,false,true)});
    public void Open(){_showLevels=false;_levels.Open();}
    public bool Back(){if(!_showLevels)return false;if(!_levels.Back())_showLevels=false;return true;}
    public async Task<string> ActivateAsync(string key){
        if(_showLevels)return await _levels.ActivateAsync(key);
        if(key=="levels"){_showLevels=true;_levels.Open();return "Load maps from the game's main menu.";}
        if(key!="objective-next")return "Select an option.";
        if(Busy||!_available())return "Please wait for the current update.";
        Busy=true;
        try{return await _complete();}finally{Busy=false;}
    }
}
