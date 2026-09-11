using System.Drawing;
using System.Runtime.InteropServices;

namespace HwpPzWall;

/// <summary>
/// 저수준 마우스 훅(WH_MOUSE_LL)으로 지정된 사각형 안으로 커서가 들어오는 이동 이벤트를
/// 다른 앱에 전달되기 전에 차단한다.
///
/// JumpOver 모드에서는 이동 방향으로 금지 영역 반대편에 커서를 다시 놓아
/// 커서가 그 영역을 "뛰어넘게" 만든다. 덕분에 모니터 간 이동은 그대로 되면서
/// 해당 좌표에는 한 번도 머무르지 않는다. 한글이 미리보기를 띄울 계기 자체가 사라진다.
///
/// 좌표 계산은 ZoneGeometry 에 있고, 여기서는 Win32 연결과 상태만 다룬다.
/// </summary>
internal static class BlockEngine
{
    // 훅 콜백이 GC 되지 않도록 정적으로 붙들어 둔다.
    private static readonly Native.HookProc HookDelegate = HookCallback;

    private static IntPtr _hookHandle = IntPtr.Zero;
    private static Rectangle[] _zones = Array.Empty<Rectangle>();
    private static Rectangle[] _monitors = Array.Empty<Rectangle>();
    private static BlockMode _mode = BlockMode.JumpOver;
    private static Point _lastPoint;

    // 우리가 SetCursorPos 로 만든 이동만 통과시키기 위한 표식.
    // (그 외의 주입된 이동 — 무선 프리젠터의 에어마우스, 원격 데스크톱 등 — 도 똑같이 막는다)
    private static bool _hasPendingJump;
    private static Point _pendingJump;
    private static int _reentry;

    public static bool IsRunning => _hookHandle != IntPtr.Zero;

    public static int ZoneCount => _zones.Length;

    /// <summary>훅 설치가 실패했을 때의 마지막 오류 메시지.</summary>
    public static string? LastError { get; private set; }

    public static bool Start(IReadOnlyList<Rectangle> zones, IReadOnlyList<Rectangle> monitors, BlockMode mode)
    {
        Stop();

        LastError = null;
        _zones = zones.Where(r => r.Width > 0 && r.Height > 0).ToArray();
        _monitors = monitors.ToArray();
        _mode = mode;

        if (_zones.Length == 0)
        {
            LastError = "지정된 금지 영역이 없습니다.";
            return false;
        }

        _hasPendingJump = false;
        _reentry = 0;
        Native.GetCursorPos(out var cursor);
        _lastPoint = new Point(cursor.X, cursor.Y);

        var module = Native.GetModuleHandle(null);
        _hookHandle = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, HookDelegate, module, 0);

        if (_hookHandle == IntPtr.Zero)
        {
            LastError = $"마우스 훅 설치 실패 (Win32 오류 {Marshal.GetLastWin32Error()})";
            Log.Write(LastError);
            return false;
        }

        // 켜는 순간 커서가 이미 금지 영역 안에 있으면 가장 가까운 바깥으로 밀어낸다.
        if (ZoneGeometry.FindZone(_zones, _lastPoint.X, _lastPoint.Y) != null &&
            ZoneGeometry.TryPushToNearestOutside(_zones, _monitors, _lastPoint, out var safe))
        {
            _lastPoint = safe;
            _pendingJump = safe;
            _hasPendingJump = true;
            Native.SetCursorPos(safe.X, safe.Y);
        }

        Log.Write($"차단 시작 - 영역 {_zones.Length}개, 모드 {_mode}");
        return true;
    }

    public static void Stop()
    {
        if (_hookHandle == IntPtr.Zero) return;
        Native.UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
        _hasPendingJump = false;
        Log.Write("차단 중지");
    }

    /// <summary>차단 중에 모니터 배치가 바뀌면 영역만 갈아끼운다.</summary>
    public static void UpdateZones(IReadOnlyList<Rectangle> zones, IReadOnlyList<Rectangle> monitors)
    {
        _zones = zones.Where(r => r.Width > 0 && r.Height > 0).ToArray();
        _monitors = monitors.ToArray();
    }

    // ---------------- 훅 콜백 ----------------

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || (int)wParam != Native.WM_MOUSEMOVE)
            return Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

        try
        {
            var data = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
            var point = new Point(data.pt.X, data.pt.Y);

            // 방금 우리가 옮겨 놓은 좌표라면 그대로 통과시킨다(무한 재귀 방지).
            bool isOurJump = _hasPendingJump && point == _pendingJump;
            _hasPendingJump = false;

            if (isOurJump)
            {
                _lastPoint = point;
                return Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
            }

            if (ZoneGeometry.FindZone(_zones, point.X, point.Y) == null)
            {
                // 도착점은 영역 밖이지만 "지나온 경로"가 영역을 가로지른 경우.
                // 빠르게 움직이면 좌표가 수십 px 씩 건너뛰므로 좁은 띠는 이렇게 통과된다.
                // 완전 차단 모드에서는 이것도 막아야 벽 구실을 한다.
                // (뛰어넘기 모드에서는 커서가 영역 안에 머문 적이 없으므로 그대로 통과시킨다.)
                if (_mode == BlockMode.HardWall &&
                    ZoneGeometry.CrossesAnyZone(_zones, _lastPoint, point))
                {
                    return (IntPtr)1;
                }

                _lastPoint = point;
                return Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
            }

            // 금지 영역 안이다.
            if (_mode == BlockMode.JumpOver && _reentry < 4 &&
                ZoneGeometry.TryComputeJumpTarget(_zones, _monitors, _lastPoint, point, out var target))
            {
                _lastPoint = target;
                _pendingJump = target;
                _hasPendingJump = true;

                _reentry++;
                try { Native.SetCursorPos(target.X, target.Y); }
                finally { _reentry--; }
            }

            // 1 을 돌려주면 이 이동 이벤트는 어느 앱에도 전달되지 않고, 커서도 움직이지 않는다.
            return (IntPtr)1;
        }
        catch (Exception ex)
        {
            Log.Write($"훅 콜백 예외: {ex.Message}");
            return Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }
    }
}
