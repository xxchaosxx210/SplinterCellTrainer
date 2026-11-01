using System.Threading;
using System.Windows;

namespace SCTrainer;

public partial class App : Application
{
    private Mutex? _instance;
    private bool _ownsInstance;
    private TrayApplication? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instance=new Mutex(false,@"Local\SplinterAssaultTrainer.Gui");
        try { _ownsInstance=_instance.WaitOne(0); }
        catch(AbandonedMutexException) { _ownsInstance=true; }
        if(!_ownsInstance)
        {
            try {using var signal=EventWaitHandle.OpenExisting(TrayApplication.ShowStatusEvent);signal.Set();}
            catch(WaitHandleCannotBeOpenedException) {
                MessageBox.Show("The trainer is already running. Use its tray icon or press F6 in the game. Close any older trainer before starting this version.",
                    "Splinter Assault Trainer",MessageBoxButton.OK,MessageBoxImage.Information);
            }
            Shutdown();
            return;
        }
        base.OnStartup(e);
        try {
            var controller=new MainWindow();
            MainWindow=controller;
            _tray=new TrayApplication(controller,()=>Shutdown());
            _tray.Start();
        }
        catch(Exception ex) when(ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException or UnauthorizedAccessException) {
            MessageBox.Show("Could not start the tray trainer: "+ex.Message,"Splinter Assault Trainer",MessageBoxButton.OK,MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        if(_ownsInstance)_instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
