using System.Windows;

namespace CtrlZed;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if(e.Args.Any(a=>a.Equals("--self-test",StringComparison.OrdinalIgnoreCase)))
        {
            var code=SelfTest.Run();
            Shutdown(code);
            return;
        }

        new MainWindow().Show();
    }
}
