using Microsoft.Win32;

namespace HwpPzWall;

/// <summary>Windows 시작 시 자동 실행 (HKCU\...\Run, 관리자 권한 불필요).</summary>
internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "hwpPzWall";

    private static string? ExecutablePath => Environment.ProcessPath;

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string s && !string.IsNullOrWhiteSpace(s);
        }
        catch (Exception ex)
        {
            Log.Write($"자동 실행 상태 확인 실패: {ex.Message}");
            return false;
        }
    }

    public static bool Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key == null) return false;

            if (enabled)
            {
                var path = ExecutablePath;
                if (string.IsNullOrEmpty(path)) return false;
                key.SetValue(ValueName, $"\"{path}\"", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Write($"자동 실행 설정 실패: {ex.Message}");
            MessageBox.Show($"자동 실행 설정을 바꾸지 못했습니다.\n\n{ex.Message}", "hwpPzWall",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }
}
