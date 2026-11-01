using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;
using Forms=System.Windows.Forms;
namespace SCTrainer;

internal sealed class TrayApplication : IDisposable
{
    public const string ShowStatusEvent=@"Local\SplinterAssaultTrainer.ShowStatus";
    private readonly MainWindow _controller;
    private readonly Action _shutdown;
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ContextMenuStrip _menu=new();
    private readonly Forms.ToolStripMenuItem _gameItem=new("Checking game…");
    private readonly Forms.ToolStripMenuItem _restartItem=new("Restart overlay");
    private readonly Forms.ToolStripMenuItem _exitItem=new("Exit");
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromSeconds(1)};
    private readonly Icon _waiting=CreateIcon(Color.Gray),_connected=CreateIcon(Color.FromArgb(181,239,101)),_error=CreateIcon(Color.OrangeRed);
    private readonly EventWaitHandle _showStatus;
    private readonly RegisteredWaitHandle _showWait;
    private TrainerStatusWindow? _status;
    private bool _disposed,_exiting;
    public TrayApplication(MainWindow controller,Action shutdown)
    {
        _controller=controller;_shutdown=shutdown;
        _icon=new Forms.NotifyIcon {Icon=_waiting,Text="Splinter Assault / waiting for game",ContextMenuStrip=_menu};
        _gameItem.Click+=(_,_)=>ShowStatus();_menu.Items.Add(_gameItem);_menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("Status…",null,(_,_)=>ShowStatus());
        _restartItem.Click+=(_,_)=>Restart();_menu.Items.Add(_restartItem);
        _menu.Items.Add(new Forms.ToolStripSeparator());_exitItem.Click+=async(_,_)=>await ExitAsync();_menu.Items.Add(_exitItem);
        _icon.DoubleClick+=(_,_)=>ShowStatus();
        _showStatus=new EventWaitHandle(false,EventResetMode.AutoReset,ShowStatusEvent);
        _showWait=ThreadPool.RegisterWaitForSingleObject(_showStatus,(_,_)=>{
            if(!_disposed)_controller.Dispatcher.BeginInvoke(new Action(()=>{if(!_disposed)ShowStatus();}));
        },null,Timeout.Infinite,false);
        _controller.ExitRequested+=OnExitRequested;
        _timer.Tick+=Tick;
    }
    public void Start()
    {
        _icon.Visible=true;_controller.StartBackground();Update();_timer.Start();
        _icon.ShowBalloonTip(3500,"Splinter Assault Trainer","Running in the system tray. Press F6 in windowed Splinter Cell. Double-click the tray icon for status.",Forms.ToolTipIcon.Info);
        if(_controller.Status.Error)ShowStatus();
    }
    private void Tick(object? sender,EventArgs e)=>Update();
    private void Update()
    {
        if(_disposed)return;
        var status=_controller.Status;
        _icon.Icon=status.Error?_error:status.Connected?_connected:_waiting;
        _icon.Text=status.Stopping?"Splinter Assault / stopping":status.Error?"Splinter Assault / overlay unavailable":status.Connected?"Splinter Assault / game detected / F6 menu":"Splinter Assault / waiting for game";
        _gameItem.Text=status.Stopping?"Stopping trainer…":status.Game;
        _restartItem.Enabled=_exitItem.Enabled=!_exiting;
        _status?.Update(status);
    }
    public void ShowStatus()
    {
        if(_disposed)return;
        _status??=new TrainerStatusWindow(Restart,OnExitRequested);
        _status.Update(_controller.Status);_status.Show();
        if(_status.WindowState==System.Windows.WindowState.Minimized)_status.WindowState=System.Windows.WindowState.Normal;
        _status.Activate();
    }
    private void Restart()
    {
        if(_disposed||_exiting)return;
        _controller.RestartOverlay();Update();ShowStatus();
    }
    private async void OnExitRequested()=>await ExitAsync();
    private async Task ExitAsync()
    {
        if(_disposed||_exiting)return;
        _exiting=true;Update();
        bool stopped=false;
        try {
            stopped=await _controller.ShutdownTrainerAsync();
            if(stopped){Dispose();_shutdown();}
        }
        catch(Exception ex) when(ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException or UnauthorizedAccessException) {
            Forms.MessageBox.Show("Could not restore trainer state: "+ex.Message,"Splinter Assault Trainer",Forms.MessageBoxButtons.OK,Forms.MessageBoxIcon.Error);
        }
        finally {if(!stopped&&!_disposed){_exiting=false;Update();ShowStatus();}}
    }
    public void Dispose()
    {
        if(_disposed)return;_disposed=true;
        _timer.Stop();_timer.Tick-=Tick;_controller.ExitRequested-=OnExitRequested;
        _showWait.Unregister(null);_showStatus.Dispose();
        _icon.Visible=false;_icon.Dispose();_menu.Dispose();_status?.DisposeWindow();
        _waiting.Dispose();_connected.Dispose();_error.Dispose();
    }
    private static Icon CreateIcon(Color color)
    {
        using var bitmap=new Bitmap(32,32);using var graphics=Graphics.FromImage(bitmap);
        graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);using var background=new SolidBrush(Color.FromArgb(11,16,14));using var dot=new SolidBrush(color);
        graphics.FillEllipse(background,1,1,30,30);graphics.FillEllipse(dot,7,7,18,18);
        IntPtr handle=bitmap.GetHicon();try{return (Icon)Icon.FromHandle(handle).Clone();}finally{DestroyIcon(handle);}
    }
    [DllImport("user32.dll")]private static extern bool DestroyIcon(IntPtr icon);
}
