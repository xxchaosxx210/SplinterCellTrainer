using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace SCTrainer;

internal sealed record TrainerStatusSnapshot(string Game,string GameDetail,string Overlay,string Cheats,string Health,string LastAction,bool Connected,bool Error,bool Stopping);

internal sealed class TrainerStatusWindow : Window
{
    private readonly TextBlock _game=new(){FontSize=17,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock _details=new(){Foreground=Brushes.LightGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)};
    private readonly TextBlock _overlay=new(){Foreground=new SolidColorBrush(Color.FromRgb(181,239,101)),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)};
    private readonly TextBlock _cheats=new(){Foreground=Brushes.LightGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)};
    private readonly TextBlock _action=new(){Foreground=Brushes.LightGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,0)};
    private readonly Button _restart=new(){Content="Restart overlay",Padding=new Thickness(10,7,10,7)};
    private readonly Button _exit=new(){Content="Exit trainer",Padding=new Thickness(10,7,10,7),Margin=new Thickness(8,0,0,0)};
    private bool _dispose;
    public TrainerStatusWindow(Action restart,Action exit)
    {
        Title=VersionInfo.TrainerLabel+" / Status";Width=480;Height=410;MinWidth=340;MinHeight=280;
        MaxWidth=SystemParameters.WorkArea.Width;MaxHeight=SystemParameters.WorkArea.Height;
        Width=Math.Min(Width,MaxWidth);Height=Math.Min(Height,MaxHeight);
        WindowStartupLocation=WindowStartupLocation.CenterScreen;Background=new SolidColorBrush(Color.FromRgb(11,16,14));Foreground=Brushes.White;FontFamily=new FontFamily("Segoe UI");
        var root=new DockPanel {Margin=new Thickness(20)};
        var buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,16,0,0)};
        var hide=new Button {Content="Hide",Padding=new Thickness(10,7,10,7),Margin=new Thickness(8,0,0,0)};
        _restart.Click+=(_,_)=>restart();_exit.Click+=(_,_)=>exit();hide.Click+=(_,_)=>Hide();
        buttons.Children.Add(_restart);buttons.Children.Add(hide);buttons.Children.Add(_exit);DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
        var body=new StackPanel();body.Children.Add(_game);body.Children.Add(_details);body.Children.Add(_overlay);body.Children.Add(_cheats);body.Children.Add(_action);
        body.Children.Add(new TextBlock {Text=VersionInfo.TrainerLabel+"\n"+VersionInfo.GameLabel,Foreground=Brushes.Gray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,18,0,0)});
        root.Children.Add(new ScrollViewer {Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});Content=root;
        Closing+=OnClosing;
    }
    public void Update(TrainerStatusSnapshot status)
    {
        _game.Text=status.Stopping?"Stopping trainer…":status.Game;
        _game.Foreground=status.Error?Brushes.OrangeRed:status.Connected?new SolidColorBrush(Color.FromRgb(181,239,101)):Brushes.LightGray;
        _details.Text=status.GameDetail;_overlay.Text=status.Overlay;
        _cheats.Text=status.Health+"\n"+status.Cheats;_action.Text=status.LastAction;
        _restart.IsEnabled=_exit.IsEnabled=!status.Stopping;
    }
    private void OnClosing(object? sender,CancelEventArgs e){if(!_dispose){e.Cancel=true;Hide();}}
    public void DisposeWindow(){_dispose=true;Close();}
}
