using System.Windows;
using System.Windows.Threading;
using SCTrainer;

internal static class Program
{
    private static void Check(bool condition,string label){if(!condition)throw new Exception(label);Console.WriteLine("PASS "+label);}
    [STAThread]
    private static int Main()
    {
        var app=new App();app.InitializeComponent();
        try {
            var controller=new MainWindow();bool closed=false;int exits=0;
            controller.Closed+=(_,_)=>closed=true;controller.ExitRequested+=()=>++exits;
            Check(!controller.IsVisible&&PresentationSource.FromVisual(controller) is null,"background controller does not create or show the old trainer window");
            controller.Close();
            Check(exits==1&&!closed,"close requests route through graceful exit instead of terminating the background controller");
            controller.StartBackground();
            var first=app.Windows.Cast<Window>().Single(w=>w.GetType().Name=="GameMenuOverlay");
            Check(!controller.IsVisible,"starting the overlay keeps the old trainer window hidden");
            string status=controller.RestartOverlay();
            var second=app.Windows.Cast<Window>().Single(w=>w.GetType().Name=="GameMenuOverlay");
            Check(!ReferenceEquals(first,second)&&!first.IsVisible&&status.Contains("F6"),"restart replaces only the overlay and restores its shortcut");
            var shutdown=controller.ShutdownTrainerAsync();
            if(!shutdown.IsCompleted){
                var frame=new DispatcherFrame();
                shutdown.ContinueWith(_=>app.Dispatcher.BeginInvoke(new Action(()=>frame.Continue=false)));
                Dispatcher.PushFrame(frame);
            }
            Check(shutdown.GetAwaiter().GetResult()&&closed,"graceful shutdown completes and closes the hidden controller");
            Check(!app.Windows.Cast<Window>().Any(w=>w.GetType().Name=="GameMenuOverlay"),"shutdown closes overlay and releases its keyboard hook window");
            app.Shutdown();return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);app.Shutdown();return 1;}
    }
}
