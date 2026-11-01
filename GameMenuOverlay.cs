using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace SCTrainer;

internal sealed record GameMenuEntry(string Label, bool On, bool Available, string Key, string? Value=null, bool Toggle=true,bool Navigation=false);
internal sealed record GameMenuFrame(string Key,string Title,GameMenuEntry[] Entries);

// Desktop overlay for the windowed game. Keyboard handling never enters the game process.
internal sealed class GameMenuOverlay : Window
{
    private readonly Func<GameMenuFrame> _entries;
    private readonly Func<string,Task<string>> _activate;
    private readonly DispatcherTimer _timer=new() { Interval=TimeSpan.FromMilliseconds(100) };
    private readonly StackPanel _rows=new();
    private readonly TextBlock _message=new() { Foreground=new SolidColorBrush(Color.FromRgb(158,174,153)), FontSize=11, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,10,0,0) };
    private readonly Border _panel;
    private readonly Func<Task> _refresh;
    private readonly TextBlock _title=new() {Foreground=Brushes.White,FontSize=11,Margin=new Thickness(0,3,0,12)};
    private GameMenuFrame _frame=new("main","TRAINER MENU",Array.Empty<GameMenuEntry>());
    private bool _refreshing;
    private string? _selectedKey;
    private readonly HookProc _hookProc;
    private readonly HashSet<int> _held=new();
    private IntPtr _hook,_hwnd,_game;
    private bool _open,_applying,_closed;
    private int _selected;
    private DateTime _nextDiscovery,_lastMove;
    private static readonly Brush Accent=new SolidColorBrush(Color.FromRgb(181,239,101));

    public GameMenuOverlay(Func<GameMenuFrame> entries,Func<string,Task<string>> activate,Func<Task> refresh)
    {
        _entries=entries;_activate=activate;_refresh=refresh;_hookProc=Keyboard;
        WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;
        ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;ShowActivated=false;Topmost=true;
        Width=1;Height=1;Left=-10000;Top=-10000;
        var body=new StackPanel();
        body.Children.Add(new TextBlock {Text="SPLINTER ASSAULT",Foreground=Accent,FontSize=16,FontWeight=FontWeights.Bold});
        body.Children.Add(_title);
        body.Children.Add(_rows);
        body.Children.Add(new TextBlock {Text="↑ / ↓  Select     Enter  Select\n← / Esc  Back     F6  Close",Foreground=Accent,FontSize=11,Margin=new Thickness(0,12,0,0)});
        body.Children.Add(_message);
        _panel=new Border { Background=new SolidColorBrush(Color.FromArgb(238,11,19,14)),BorderBrush=Accent,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(6),Padding=new Thickness(14),Child=new ScrollViewer {Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled},Visibility=Visibility.Hidden };
        Content=_panel;
        SourceInitialized+=(_,_)=>{
            _hwnd=new WindowInteropHelper(this).Handle;
            SetWindowLong(_hwnd,-20,GetWindowLong(_hwnd,-20)|0x20|0x80|0x08000000);
            _hook=SetWindowsHookEx(13,_hookProc,GetModuleHandle(null),0);
            if(_hook==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
        };
        _timer.Tick+=Tick;
        Closed+=(_,_)=>{_closed=true;_open=false;_timer.Stop();_timer.Tick-=Tick;if(_hook!=IntPtr.Zero){UnhookWindowsHookEx(_hook);_hook=IntPtr.Zero;}};
        _timer.Start();
    }

    private IntPtr Keyboard(int code,IntPtr message,IntPtr data)
    {
        if(code<0||_closed||_game==IntPtr.Zero||GetForegroundWindow()!=_game||IsIconic(_game))
            return CallNextHookEx(_hook,code,message,data);
        int key=Marshal.ReadInt32(data);
        bool down=message.ToInt64() is 0x100 or 0x104;
        bool up=message.ToInt64() is 0x101 or 0x105;
        if(key!=0x75&&(!_open||key is not (0x26 or 0x28 or 0x25 or 0x0d or 0x1b))&&!_held.Contains(key))
            return CallNextHookEx(_hook,code,message,data);
        if(up){_held.Remove(key);return new IntPtr(1);}
        if(!down)return CallNextHookEx(_hook,code,message,data);
        bool repeat=!_held.Add(key);
        if(key==0x75&&!repeat){_open=!_open;_message.Text="";}
        else if(_open&&key is 0x1b or 0x25&&!repeat) {
            if(_frame.Key=="main")_open=false;
            else Dispatcher.BeginInvoke(new Action(async()=>await ApplyAsync("back")));
        }
        else if(_open&&key is 0x26 or 0x28) {
            int count=_frame.Entries.Length;
            if(count>0&&(!repeat||DateTime.UtcNow-_lastMove>TimeSpan.FromMilliseconds(140))){_selected=(_selected+(key==0x28?1:count-1))%count;_selectedKey=_frame.Entries[_selected].Key;_lastMove=DateTime.UtcNow;}
        }
        else if(_open&&key==0x0d&&!repeat) {
            // Return from the hook immediately; native cheat requests run asynchronously later.
            string? selected=_selected>=0&&_selected<_frame.Entries.Length?_frame.Entries[_selected].Key:null;
            if(selected is not null)Dispatcher.BeginInvoke(new Action(async()=>await ApplyAsync(selected)));
        }
        return new IntPtr(1);
    }

    private async Task ApplyAsync(string selected)
    {
        if(_closed||!_open||GetForegroundWindow()!=_game)return;
        var entry=_entries().Entries.FirstOrDefault(e=>e.Key==selected);
        bool navigation=selected=="back"||entry?.Navigation==true;
        if(selected!="back"&&entry?.Available!=true)return;
        if(_applying&&!navigation)return;
        if(navigation) {
            try {_message.Text=await _activate(selected);}
            catch(Exception ex) when(ex is InvalidOperationException or Win32Exception or System.IO.IOException or UnauthorizedAccessException or FormatException or ArgumentException){_message.Text=ex.Message;}
            return;
        }
        _applying=true;_message.Text="Applying…";
        try {_message.Text=await _activate(selected);}
        catch(Exception ex) when(ex is InvalidOperationException or Win32Exception or System.IO.IOException or UnauthorizedAccessException or FormatException or ArgumentException) {_message.Text=ex.Message;}
        finally {_applying=false;}
    }

    private async void Tick(object? sender,EventArgs e)
    {
        if(_closed)return;
        if(DateTime.UtcNow>=_nextDiscovery){
            _nextDiscovery=DateTime.UtcNow.AddSeconds(1);
            var games=Process.GetProcessesByName("SplinterCell");
            try {_game=games.Length==1?games[0].MainWindowHandle:IntPtr.Zero;}
            catch(InvalidOperationException){_game=IntPtr.Zero;}
            finally{foreach(var game in games)game.Dispose();}
        }
        bool focused=_game!=IntPtr.Zero&&IsWindow(_game)&&!IsIconic(_game)&&GetForegroundWindow()==_game;
        if(!focused){_open=false;_held.Clear();_panel.Visibility=Visibility.Hidden;return;}
        if(!_open){_panel.Visibility=Visibility.Hidden;return;}
        if(!GetClientRect(_game,out var rect))return;
        var origin=new PointNative();if(!ClientToScreen(_game,ref origin))return;
        double scale=VisualTreeHelper.GetDpi(this).DpiScaleX;
        int width=Math.Min((int)(310*scale),Math.Max(1,rect.Right-24));
        int height=Math.Min((int)(470*scale),Math.Max(1,rect.Bottom-32));
        SetWindowPos(_hwnd,new IntPtr(-1),origin.X+12,origin.Y+16,width,height,0x10|0x40);
        _panel.Visibility=Visibility.Visible;
        var frame=_entries();
        if(frame.Key!=_frame.Key){_selected=0;_selectedKey=null;}
        var items=frame.Entries;
        if(_selectedKey is not null){int index=Array.FindIndex(items,e=>e.Key==_selectedKey);if(index>=0)_selected=index;}
        _frame=frame;_title.Text=frame.Title;_rows.Children.Clear();
        _selected=Math.Clamp(_selected,0,Math.Max(0,items.Length-1));
        _selectedKey=items.Length>0?items[_selected].Key:null;
        for(int i=0;i<items.Length;++i){
            var item=items[i];
            var line=new DockPanel();
            var state=new TextBlock {Text=item.Toggle?(item.On?"ON":"OFF"):(item.Value??"›"),Foreground=item.On?Accent:Brushes.Gray,FontWeight=FontWeights.Bold,FontSize=11,Margin=new Thickness(8,0,0,0)};
            DockPanel.SetDock(state,Dock.Right);line.Children.Add(state);
            line.Children.Add(new TextBlock {Text=(i==_selected?"›  ":"   ")+item.Label,Foreground=item.Available?Brushes.White:Brushes.Gray,FontSize=12,TextWrapping=TextWrapping.Wrap});
            _rows.Children.Add(new Border {Child=line,Padding=new Thickness(5,7,5,7),CornerRadius=new CornerRadius(3),Background=i==_selected?new SolidColorBrush(Color.FromRgb(42,64,30)):Brushes.Transparent});
        }
        if(_rows.Children.Count>_selected)((FrameworkElement)_rows.Children[_selected]).BringIntoView();
        if(!_refreshing&&!_applying) {
            _refreshing=true;
            try {await _refresh();}
            catch(Exception ex) when(ex is InvalidOperationException or Win32Exception or System.IO.IOException or UnauthorizedAccessException or FormatException or ArgumentException) {_message.Text=ex.Message;}
            finally {_refreshing=false;}
        }
    }

    private delegate IntPtr HookProc(int code,IntPtr message,IntPtr data);
    [StructLayout(LayoutKind.Sequential)]private struct RectNative {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]private struct PointNative {public int X,Y;}
    [DllImport("user32.dll",SetLastError=true)]private static extern IntPtr SetWindowsHookEx(int id,HookProc callback,IntPtr module,uint thread);
    [DllImport("user32.dll")]private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]private static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")]private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")]private static extern bool GetClientRect(IntPtr window,out RectNative rect);
    [DllImport("user32.dll")]private static extern bool ClientToScreen(IntPtr window,ref PointNative point);
    [DllImport("user32.dll")]private static extern int GetWindowLong(IntPtr window,int index);
    [DllImport("user32.dll")]private static extern int SetWindowLong(IntPtr window,int index,int value);
    [DllImport("user32.dll")]private static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
}
