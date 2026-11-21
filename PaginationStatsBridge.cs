namespace CtrlZed;

public partial class MainWindow
{
    internal int GetBeta35PageCountForStats()
        =>Math.Max(1,beta35PaginationMap?.PageCount??1);
}
