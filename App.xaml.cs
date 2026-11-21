using System.Windows;

namespace CtrlZed;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if(e.Args.Any(a=>a.Equals("--paged-editing-test",StringComparison.OrdinalIgnoreCase)))
        {
            Shutdown(CtrlZed.MainWindow.RunPagedEditingSelfTest());
            return;
        }

        if(e.Args.Any(a=>a.Equals("--style45-test",StringComparison.OrdinalIgnoreCase)))
        {
            Shutdown(CtrlZed.MainWindow.RunRelease45StyleSelfTest());
            return;
        }
        if(e.Args.Any(a=>a.Equals("--plain-text-paste-test",StringComparison.OrdinalIgnoreCase)))
        {
            Shutdown(CtrlZed.MainWindow.RunPlainTextPasteSelfTest());
            return;
        }

        if(e.Args.Any(a=>a.Equals("--menu-performance-test",StringComparison.OrdinalIgnoreCase)))
        {
            Shutdown(CtrlZed.MainWindow.RunMenuResponsivenessSelfTest());
            return;
        }

        if(e.Args.Any(a=>a.Equals("--following-style-test",StringComparison.OrdinalIgnoreCase)))
        {
            Shutdown(CtrlZed.MainWindow.RunFollowingStyleInputSelfTest());
            return;
        }

        if(e.Args.Any(a=>a.Equals("--self-test",StringComparison.OrdinalIgnoreCase)))
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(),"CtrlZedSelfTestProgress.txt"),"Beta35 pagination");
            var beta35=Beta35SelfTest.Run();
            if(beta35!=0)
            {
                Shutdown(beta35);
                return;
            }

            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(),"CtrlZedSelfTestProgress.txt"),"Beta341");
            var beta341=Beta341SelfTest.Run();
            if(beta341!=0)
            {
                Shutdown(beta341);
                return;
            }

            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(),"CtrlZedSelfTestProgress.txt"),"Beta34");
            var beta34=Beta34SelfTest.Run();
            if(beta34!=0)
            {
                Shutdown(beta34);
                return;
            }

            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(),"CtrlZedSelfTestProgress.txt"),"Beta33");
            var beta33=Beta33SelfTest.Run();
            if(beta33!=0)
            {
                Shutdown(beta33);
                return;
            }

            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(),"CtrlZedSelfTestProgress.txt"),"Beta32");
            var beta32=Beta32SelfTest.Run();
            if(beta32!=0)
            {
                Shutdown(beta32);
                return;
            }

            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(),"CtrlZedSelfTestProgress.txt"),"Core document tests");
            var code=SelfTest.Run();
            Shutdown(code);
            return;
        }

        new MainWindow().Show();
    }
}

