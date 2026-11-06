using System.Windows;

namespace CtrlZed;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if(e.Args.Any(a=>a.Equals("--self-test",StringComparison.OrdinalIgnoreCase)))
        {
            var beta32=Beta32SelfTest.Run();
            if(beta32!=0)
            {
                Shutdown(beta32);
                return;
            }

            var code=SelfTest.Run();
            Shutdown(code);
            return;
        }

        new MainWindow().Show();
    }
}
