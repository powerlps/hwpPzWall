using System.Drawing;

namespace HwpPzWall;

/// <summary>
/// 금지 영역 좌표 계산. Win32 와 무관한 순수 함수라서 단독으로 테스트할 수 있다.
/// BlockEngine 은 이 계산 결과를 실제 커서에 적용하기만 한다.
/// </summary>
internal static class ZoneGeometry
{
    public static Rectangle? FindZone(Rectangle[] zones, int x, int y)
    {
        for (int i = 0; i < zones.Length; i++)
            if (zones[i].Contains(x, y)) return zones[i];
        return null;
    }

    /// <summary>모니터 목록이 비어 있으면 판정을 보류하고 통과시킨다.</summary>
    public static bool IsOnAnyMonitor(Rectangle[] monitors, int x, int y)
    {
        if (monitors.Length == 0) return true;
        for (int i = 0; i < monitors.Length; i++)
            if (monitors[i].Contains(x, y)) return true;
        return false;
    }

    /// <summary>
    /// prev → cur 이동 "경로"가 금지 영역을 가로지르는지 본다.
    ///
    /// 마우스를 빠르게 움직이면 Windows 의 포인터 가속 때문에 보고 한 번에 좌표가 수십 px 씩 건너뛴다.
    /// (천천히: 60→58→56, 빠르게: 200→120→40→-50)
    /// 그래서 도착점만 보면 좁은 띠는 그대로 통과해 버린다. 완전 차단 모드에서는 경로로 판정해야 벽이 된다.
    /// </summary>
    public static bool CrossesAnyZone(Rectangle[] zones, Point from, Point to)
    {
        for (int i = 0; i < zones.Length; i++)
            if (SegmentIntersectsRect(zones[i], from, to)) return true;
        return false;
    }

    /// <summary>선분과 사각형의 교차 판정 (Liang–Barsky).</summary>
    public static bool SegmentIntersectsRect(Rectangle rect, Point a, Point b)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return false;

        double dx = b.X - a.X;
        double dy = b.Y - a.Y;

        // 픽셀 단위이므로 오른쪽·아래 경계는 Right-1, Bottom-1 이 마지막 픽셀이다.
        double left = rect.Left, right = rect.Right - 1;
        double top = rect.Top, bottom = rect.Bottom - 1;

        Span<double> p = stackalloc double[] { -dx, dx, -dy, dy };
        Span<double> q = stackalloc double[] { a.X - left, right - a.X, a.Y - top, bottom - a.Y };

        double t0 = 0, t1 = 1;

        for (int i = 0; i < 4; i++)
        {
            if (p[i] == 0)
            {
                // 해당 축으로 움직이지 않음 — 시작부터 범위 밖이면 교차할 수 없다.
                if (q[i] < 0) return false;
                continue;
            }

            double t = q[i] / p[i];
            if (p[i] < 0)
            {
                if (t > t1) return false;
                if (t > t0) t0 = t;
            }
            else
            {
                if (t < t0) return false;
                if (t < t1) t1 = t;
            }
        }

        return true;
    }

    /// <summary>
    /// prev 에서 cur 로 움직여 금지 영역에 들어왔을 때, 어디에 커서를 다시 놓을지 계산한다.
    /// 진행 방향을 먼저 시도하고, 그쪽이 막혀 있으면 나머지 방향을 차례로 본다.
    /// </summary>
    public static bool TryComputeJumpTarget(
        Rectangle[] zones, Rectangle[] monitors, Point prev, Point cur, out Point result)
    {
        int dx = cur.X - prev.X;
        int dy = cur.Y - prev.Y;

        var directions = new List<(int X, int Y)>(6);

        if (Math.Abs(dx) >= Math.Abs(dy))
        {
            if (dx != 0) directions.Add((Math.Sign(dx), 0));
            if (dy != 0) directions.Add((0, Math.Sign(dy)));
        }
        else
        {
            if (dy != 0) directions.Add((0, Math.Sign(dy)));
            if (dx != 0) directions.Add((Math.Sign(dx), 0));
        }

        // 진행 방향으로 나갈 곳이 없으면(예: 데스크톱 바깥 경계) 나머지 방향도 시도한다.
        foreach (var d in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            if (!directions.Contains(d)) directions.Add(d);

        foreach (var d in directions)
            if (TryPush(zones, monitors, cur, d.X, d.Y, out result)) return true;

        result = default;
        return false;
    }

    /// <summary>한 방향으로 금지 영역들을 연속으로 건너뛴다.</summary>
    public static bool TryPush(
        Rectangle[] zones, Rectangle[] monitors, Point start, int dx, int dy, out Point result)
    {
        var q = start;

        for (int guard = 0; guard < 32; guard++)
        {
            var zone = FindZone(zones, q.X, q.Y);
            if (zone == null) break;
            var r = zone.Value;

            if (dx > 0) q.X = r.Right;              // Right 는 경계 바깥 첫 픽셀
            else if (dx < 0) q.X = r.Left - 1;
            else if (dy > 0) q.Y = r.Bottom;
            else q.Y = r.Top - 1;
        }

        result = q;
        return FindZone(zones, q.X, q.Y) == null && IsOnAnyMonitor(monitors, q.X, q.Y);
    }

    /// <summary>네 방향 중 가장 가까운 바깥 지점. 차단을 켜는 순간 커서를 빼낼 때 쓴다.</summary>
    public static bool TryPushToNearestOutside(
        Rectangle[] zones, Rectangle[] monitors, Point start, out Point result)
    {
        Point best = default;
        long bestDistance = long.MaxValue;
        bool found = false;

        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            if (!TryPush(zones, monitors, start, dx, dy, out var candidate)) continue;
            long ddx = candidate.X - start.X;
            long ddy = candidate.Y - start.Y;
            long distance = ddx * ddx + ddy * ddy;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
                found = true;
            }
        }

        result = best;
        return found;
    }
}
