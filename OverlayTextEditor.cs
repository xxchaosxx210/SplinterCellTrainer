using System.Globalization;
namespace SCTrainer;
internal enum MenuInputKind { Name, Health, Coordinate }
internal sealed record GameMenuInput(string Label,string Initial,MenuInputKind Kind,int MaxLength=32);
internal sealed class OverlayTextEditor
{
    public string Key {get;}
    public GameMenuInput Input {get;}
    public string Text {get;private set;}
    public int Caret {get;private set;}
    private bool _all=true;
    public OverlayTextEditor(string key,GameMenuInput input){Key=key;Input=input;Text=input.Initial;Caret=Text.Length;}
    public void SelectAll()=>_all=true;
    public void Insert(string value){if(value.Any(char.IsControl))return;string text=_all?value:Text.Insert(Caret,value);if(text.Length>Input.MaxLength)return;Caret=_all?value.Length:Caret+value.Length;Text=text;_all=false;}
    public void Backspace(){if(_all){Text="";Caret=0;}else if(Caret>0){Text=Text.Remove(--Caret,1);}_all=false;}
    public void Delete(){if(_all){Text="";Caret=0;}else if(Caret<Text.Length)Text=Text.Remove(Caret,1);_all=false;}
    public void Move(int delta){_all=false;Caret=Math.Clamp(Caret+delta,0,Text.Length);}
    public void Home(){_all=false;Caret=0;}
    public void End(){_all=false;Caret=Text.Length;}
    public string Display=>Text.Insert(Caret,"|");
    public string Value()
    {
        string value=Text.Trim();
        if(Input.Kind==MenuInputKind.Name){if(value.Length==0||value.Length>32||value.Any(char.IsControl))throw new ArgumentException("Enter a name from 1 to 32 characters.");}
        else if(Input.Kind==MenuInputKind.Health){if(!int.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out int hp)||hp is <1 or >100)throw new ArgumentException("Health must be a whole number from 1 to 100.");}
        else if(!float.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out float coordinate)||!float.IsFinite(coordinate)||Math.Abs(coordinate)>1000000)throw new ArgumentException("Use a finite coordinate between -1000000 and 1000000; use a decimal point.");
        return value;
    }
}
