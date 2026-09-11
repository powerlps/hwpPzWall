using System.Threading;

namespace HwpPzWall;

internal static class Program
{
    private const string MutexName = @"Local\hwpPzWall.SingleInstance";

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirstInstance);

        if (!isFirstInstance)
        {
            MessageBox.Show(
                "hwpPzWall 이 이미 실행 중입니다.\n작업 표시줄 오른쪽 트레이 아이콘을 확인하세요.",
                "hwpPzWall", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.ThreadException += (_, e) =>
        {
            Log.Write($"처리되지 않은 예외: {e.Exception}");
            MessageBox.Show($"예기치 않은 오류가 발생했습니다.\n\n{e.Exception.Message}\n\n로그: {Log.Path}",
                "hwpPzWall", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Write($"치명적 예외: {e.ExceptionObject}");
            BlockEngine.Stop();
        };

        bool firstRun = !File.Exists(AppConfig.ConfigPath);

        try
        {
            Application.Run(new TrayApp(firstRun));
        }
        finally
        {
            // 훅이 남아 있으면 시스템 전체 마우스에 영향을 주므로 반드시 해제한다.
            BlockEngine.Stop();
        }
    }
}
