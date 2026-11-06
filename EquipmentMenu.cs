using System.Diagnostics;
using System.Globalization;
namespace SCTrainer;
internal sealed record EquipmentChoice(int Id,string Name,int Category,bool Stackable=true);
internal sealed record EquipmentStock(int Id,bool Owned,int Quantity,int Maximum)
{
    public static EquipmentStock[] Parse(string text){
        var lines=text.Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        if(lines.Length!=18||lines[0]!="EQUIPMENT:1")throw new FormatException("Invalid equipment snapshot.");
        var result=new List<EquipmentStock>();var seen=new HashSet<int>();
        foreach(var line in lines.Skip(1)){
            var p=line.Split('|');
            if(p.Length!=4||!int.TryParse(p[0],out int id)||id<1||id>17||!seen.Add(id)||
                (p[1]!="0"&&p[1]!="1")||!int.TryParse(p[2],out int quantity)||quantity<0||quantity>10000||
                !int.TryParse(p[3],out int maximum)||maximum<0||maximum>10000||(p[1]=="1"&&maximum<1))
                throw new FormatException("Invalid equipment entry.");
            result.Add(new(id,p[1]=="1",quantity,maximum));
        }
        return result.ToArray();
    }
}
internal sealed class EquipmentMenu : IDisposable
{
    internal static readonly EquipmentChoice[] Catalog={
        new(1,"SC Pistol",0,false),new(2,"SC-20K",0,false),
        new(3,"Sticky Shocker",1),new(4,"Ring Airfoil Round",1),new(5,"Sticky Camera",1),new(6,"Diversion Camera",1),new(7,"Smoke / Gas Grenade",1),
        new(8,"Wall Mine",2),new(9,"Frag Grenade",2),new(10,"Lockpick",2,false),new(11,"Disposable Pick",2),new(12,"Optic Cable",2,false),new(13,"Camera Jammer",2,false),new(14,"Laser Microphone",2,false),
        new(15,"Medkit",3),new(16,"Flare",3),new(17,"Chemical Flare",3)};
    private static readonly string[] Categories={"Weapons","Launcher ammunition","Gadgets and tools","Medical and flares"};
    private readonly Func<Process> _open;
    private readonly Func<Process,Task<EquipmentStock[]?>> _read;
    private readonly Func<Process,int,int,int,Task<string>> _action;
    private Process? _game;private EquipmentStock[] _stock=Array.Empty<EquipmentStock>();
    private int _page,_category,_item;private bool _stale=true,_disposed;private DateTime _next;
    public bool Busy{get;private set;}public bool Refreshing{get;private set;}
    public Func<bool> Available{get;set;}=()=>true;
    public EquipmentMenu(Func<Process>? open=null,Func<Process,Task<EquipmentStock[]?>>? read=null,Func<Process,int,int,int,Task<string>>? action=null){
        _open=open??OpenGame;_read=read??DirectLevelLoader.ReadEquipmentAsync;_action=action??DirectLevelLoader.EquipmentActionAsync;
    }
    private static Process OpenGame(){var games=Process.GetProcessesByName("SplinterCell");if(games.Length==1)return games[0];foreach(var g in games)g.Dispose();throw new InvalidOperationException("Start one copy of Splinter Cell and load a mission.");}
    private Process Game(){if(_disposed)throw new InvalidOperationException("Equipment menu closed.");if(_game is not null&&!_game.HasExited)return _game;_game?.Dispose();_stock=Array.Empty<EquipmentStock>();_stale=true;return _game=_open();}
    public void Open(){_page=0;}
    public bool Back(){if(_page==0)return false;--_page;return true;}
    private EquipmentStock? Stock(int id)=>_stock.FirstOrDefault(s=>s.Id==id);
    private static GameMenuEntry Row(string label,string key,bool enabled=true,string? value=null,bool navigation=false)=>new(label,false,enabled,key,value,false,navigation);
    public GameMenuFrame Frame{get{
        bool ready=!Busy&&!_stale&&Available();var rows=new List<GameMenuEntry>();string title="SAM EQUIPMENT";
        if(_page==0){for(int i=0;i<Categories.Length;++i)rows.Add(Row(Categories[i],"equipment-category:"+i,navigation:true));}
        else if(_page==1){title=Categories[_category].ToUpperInvariant();foreach(var item in Catalog.Where(i=>i.Category==_category)){
            var stock=Stock(item.Id);rows.Add(Row(item.Name,"equipment-item:"+item.Id,navigation:true,value:_stale?"--":stock?.Owned==true?$"{stock.Quantity} / {stock.Maximum}":"Not owned"));}}
        else{
            var item=Catalog.Single(i=>i.Id==_item);var stock=Stock(_item);string key=$"{_game?.Id??0}:{_item}";title=item.Name.ToUpperInvariant();
            rows.Add(Row(_stale?"Inventory unavailable":stock?.Owned==true?$"Quantity: {stock.Quantity} / {stock.Maximum}":"Not owned","equipment-info",false,""));
            bool full=stock?.Owned==true&&stock.Maximum>0&&stock.Quantity>=stock.Maximum;
            rows.Add(Row(full?"At game limit":item.Stackable?"Give 1":"Give item","equipment-add:"+key+":1",ready&&!full));
            if(item.Stackable){rows.Add(Row("Give 5","equipment-add:"+key+":5",ready&&!full));
                rows.Add(new("Set quantity",false,ready,"equipment-set:"+key,null,false,false,new("Item quantity (game limit applies)",Math.Clamp(stock?.Quantity??1,1,999).ToString(CultureInfo.InvariantCulture),MenuInputKind.Quantity,3)));}
            if(item.Id<=2)rows.Add(Row("Refill ammunition","equipment-refill:"+key+":0",ready&&stock?.Owned==true));
            rows.Add(Row("Equip / select","equipment-select:"+key+":0",ready&&stock?.Owned==true&&stock.Quantity>0));
        }
        rows.Add(Row("Back","back",navigation:true));return new($"equipment:{_page}:{_category}:{_item}",title,rows.ToArray());
    }}
    public async Task RefreshAsync(bool force=false){
        if(_disposed||Refreshing||(!force&&(Busy||DateTime.UtcNow<_next)))return;
        Refreshing=true;_next=DateTime.UtcNow.AddMilliseconds(1000);
        try{var snapshot=await _read(Game());if(snapshot is null){if(force)throw new InvalidOperationException("Wait for the current trainer request.");return;}if(!_disposed){_stock=snapshot;_stale=false;}}
        catch{_stale=true;throw;}finally{Refreshing=false;}
    }
    public async Task<string> ActivateAsync(string key){
        if(key.StartsWith("equipment-category:")&&int.TryParse(key.AsSpan(19),out int category)&&category>=0&&category<Categories.Length){_category=category;_page=1;return "";}
        if(key.StartsWith("equipment-item:")&&int.TryParse(key.AsSpan(15),out int id)&&Catalog.Any(i=>i.Id==id&&i.Category==_category)){_item=id;_page=2;return "";}
        if(Busy||!Available())return "Wait for the current trainer update to finish.";
        var p=key.Split(':');
        if(p.Length!=4||!int.TryParse(p[1],out int pid)||!int.TryParse(p[2],out int itemId)||!Catalog.Any(i=>i.Id==itemId)||!int.TryParse(p[3],NumberStyles.None,CultureInfo.InvariantCulture,out int quantity))throw new InvalidOperationException("Select a valid equipment action.");
        int op=p[0] switch{"equipment-add"=>0,"equipment-set"=>1,"equipment-select"=>2,"equipment-refill"=>3,_=>-1};
        if(op<0||(op>=2?quantity!=0:quantity<1||quantity>999))throw new InvalidOperationException("Invalid equipment quantity.");
        Busy=true;
        try{
            while(Refreshing){await Task.Delay(25);if(_disposed)throw new InvalidOperationException("Equipment menu closed.");}
            var game=Game();if(game.Id!=pid)throw new InvalidOperationException("The game session changed. Select the item again.");
            await RefreshAsync(true);
            var before=Stock(itemId);
            if(op==0&&before?.Owned==true&&before.Maximum>0&&before.Quantity>=before.Maximum)return $"Already at the game's limit: {before.Quantity} / {before.Maximum}.";
            if(op>=2&&Stock(itemId)?.Owned!=true)throw new InvalidOperationException("Give this item to Sam first.");
            string result=await _action(game,itemId,op,quantity);await RefreshAsync(true);
            if(op<2){
                var after=Stock(itemId);
                if(after?.Owned!=true||after.Quantity<1||after.Maximum<1)throw new InvalidOperationException("Grant not confirmed: the item is missing from Sam's inventory. No success was confirmed.");
                int target=Math.Min(after.Maximum,op==0?(before?.Owned==true?before.Quantity:0)+quantity:quantity);
                if(after.Quantity!=target)throw new InvalidOperationException($"Inventory update not confirmed: requested {target}, but the game reports {after.Quantity}. Check whether Unlimited gadgets is overriding quantities.");
                return $"{Catalog.Single(i=>i.Id==itemId).Name}: {after.Quantity} / {after.Maximum} confirmed in Sam's inventory.";
            }
            return result;
        }finally{Busy=false;}
    }
    public void Dispose(){_disposed=true;_game?.Dispose();}
}
