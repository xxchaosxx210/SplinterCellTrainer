using System.Threading;
using System.Windows;

namespace SCTrainer;

public partial class App : Application
{
    private Mutex? _instance;
    private bool _ownsInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instance=new Mutex(false,@"Local\SplinterAssaultTrainer.Gui");
        try { _ownsInstance=_instance.WaitOne(0); }
        catch(AbandonedMutexException) { _ownsInstance=true; }
        if(!_ownsInstance)
        {
            MessageBox.Show("The trainer is already running. Close its window before starting another copy.",
                "Splinter Assault Trainer",MessageBoxButton.OK,MessageBoxImage.Information);
            Shutdown();
            return;
        }
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if(_ownsInstance)_instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
