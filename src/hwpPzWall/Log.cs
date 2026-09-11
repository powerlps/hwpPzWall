namespace HwpPzWall;

/// <summary>문제 추적용 최소 로그. %APPDATA%\hwpPzWall\hwpPzWall.log</summary>
internal static class Log
{
    private static readonly object Gate = new();

    public static string Path => System.IO.Path.Combine(AppConfig.ConfigDirectory, "hwpPzWall.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppConfig.ConfigDirectory);
                var file = new FileInfo(Path);
                if (file.Exists && file.Length > 256 * 1024) file.Delete();
                File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
            }
        }
        catch { /* 로그 실패로 앱이 멈추면 안 된다 */ }
    }
}
