using System.Drawing;
using HwpPzWall;

namespace HwpPzWall.Tests;

/// <summary>
/// 좌표 계산 검증. 실제 마우스나 화면 없이 돌아간다.
///   dotnet run --project tests\GeometryTests
/// </summary>
internal static class Program
{
    private static int _passed;
    private static int _failed;

    // 실제 환경과 비슷한 배치: 왼쪽에 1920×1080, 오른쪽(주)에 3440×1440
    private static readonly Rectangle Left = new(-1920, 341, 1920, 1080);
    private static readonly Rectangle Primary = new(0, 0, 3440, 1440);
    private static readonly Rectangle[] TwoMonitors = { Left, Primary };

    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("hwpPzWall 좌표 계산 검증");
        Console.WriteLine(new string('-', 62));

        JumpTests();
        FastMoveTests();
        FallbackTests();
        PushOutTests();
        ZoneBuildTests();
        MonitorMatchTests();

        Console.WriteLine(new string('-', 62));
        Console.WriteLine($"통과 {_passed}개 / 실패 {_failed}개");
        return _failed == 0 ? 0 : 1;
    }

    // ---------------- 뛰어넘기 ----------------

    private static void JumpTests()
    {
        Section("주 모니터 왼쪽 15px 금지 (한글 미리보기 시나리오)");

        var zones = new[] { new Rectangle(0, 0, 15, 1440) };

        CheckJump("왼쪽 모니터에서 오른쪽으로 진입 → 띠를 넘어 착지",
            zones, TwoMonitors, prev: new Point(-10, 700), cur: new Point(5, 700),
            expected: new Point(15, 700));

        CheckJump("주 모니터에서 왼쪽으로 진입 → 옆 모니터로 넘어감",
            zones, TwoMonitors, prev: new Point(50, 700), cur: new Point(5, 700),
            expected: new Point(-1, 700));

        CheckJump("띠 경계 바로 안쪽(x=14)도 잡힘",
            zones, TwoMonitors, prev: new Point(-1, 700), cur: new Point(14, 700),
            expected: new Point(15, 700));

        Check("띠 바깥(x=15)은 금지 영역이 아님",
            ZoneGeometry.FindZone(zones, 15, 700) == null, "통과해야 함");

        Check("띠 바깥(x=-1)은 금지 영역이 아님",
            ZoneGeometry.FindZone(zones, -1, 700) == null, "통과해야 함");

        Section("띠가 여러 겹이어도 한 번에 건너뜀");

        var stacked = new[] { new Rectangle(0, 0, 15, 1440), new Rectangle(15, 0, 10, 1440) };
        CheckJump("붙어 있는 두 띠를 연속으로 통과",
            stacked, TwoMonitors, prev: new Point(-10, 700), cur: new Point(5, 700),
            expected: new Point(25, 700));
    }

    // ---------------- 빠른 이동 (좌표 건너뛰기) ----------------

    private static void FastMoveTests()
    {
        Section("빠르게 움직여 좌표가 건너뛸 때의 경로 판정");

        var band = new[] { new Rectangle(0, 0, 15, 1440) };   // x 0~14

        Check("천천히 움직이면 경로가 띠에 닿지 않음 (60 → 58)",
            !ZoneGeometry.CrossesAnyZone(band, new Point(60, 700), new Point(58, 700)),
            "false");

        Check("빠른 이동으로 띠를 건너뛰어도 경로는 잡힘 (40 → -50)",
            ZoneGeometry.CrossesAnyZone(band, new Point(40, 700), new Point(-50, 700)),
            "true");

        Check("반대 방향 빠른 이동도 잡힘 (-50 → 40)",
            ZoneGeometry.CrossesAnyZone(band, new Point(-50, 700), new Point(40, 700)),
            "true");

        Check("띠 근처에도 안 간 빠른 이동은 안 잡힘 (200 → 100)",
            !ZoneGeometry.CrossesAnyZone(band, new Point(200, 700), new Point(100, 700)),
            "false");

        Check("띠 바로 옆까지만 와서 멈추면 안 잡힘 (16 → 15)",
            !ZoneGeometry.CrossesAnyZone(band, new Point(16, 700), new Point(15, 700)),
            "false");

        Check("한 픽셀만 들어와도 잡힘 (16 → 14)",
            ZoneGeometry.CrossesAnyZone(band, new Point(16, 700), new Point(14, 700)),
            "true");

        Check("띠와 나란히 움직이면 안 잡힘 (세로 이동)",
            !ZoneGeometry.CrossesAnyZone(band, new Point(100, 300), new Point(100, 1000)),
            "false");

        Check("대각선으로 띠 모서리를 스쳐도 잡힘",
            ZoneGeometry.CrossesAnyZone(band, new Point(30, 700), new Point(-10, 760)),
            "true");

        var shortBand = new[] { new Rectangle(0, 0, 15, 100) };  // y 0~99 만
        Check("대각선이 띠 높이를 비껴가면 안 잡힘",
            !ZoneGeometry.CrossesAnyZone(shortBand, new Point(30, 200), new Point(-10, 260)),
            "false");

        var topBand = new[] { new Rectangle(0, 0, 3440, 10) };
        Check("위쪽 띠를 빠르게 뚫고 올라가도 잡힘 (y 50 → -20)",
            ZoneGeometry.CrossesAnyZone(topBand, new Point(500, 50), new Point(500, -20)),
            "true");
    }

    // ---------------- 진행 방향이 막혔을 때 ----------------

    private static void FallbackTests()
    {
        Section("진행 방향으로 나갈 곳이 없을 때의 대안 경로");

        var zones = new[] { new Rectangle(0, 0, 15, 1440) };

        // 아래로 크게 움직이며 띠에 들어옴 → 아래쪽은 화면 밖이라 옆으로 빠진다.
        CheckJump("아래로 이동 중 진입, 아래가 화면 밖 → 옆 모니터로",
            zones, TwoMonitors, prev: new Point(20, 300), cur: new Point(5, 1000),
            expected: new Point(-1, 1000));

        var topBand = new[] { new Rectangle(0, 0, 3440, 10) };
        CheckJump("위쪽 띠에서 위로 진입, 위가 화면 밖 → 아래로 되밀림",
            topBand, TwoMonitors, prev: new Point(500, 50), cur: new Point(500, 3),
            expected: new Point(500, 10));

        Section("빠져나갈 곳이 전혀 없는 경우");

        var single = new[] { new Rectangle(0, 0, 1920, 1080) };
        var wholeScreen = new[] { new Rectangle(0, 0, 1920, 1080) };
        Check("모니터 전체가 금지 영역이면 뛰어넘기 실패(= 벽처럼 막힘)",
            !ZoneGeometry.TryComputeJumpTarget(wholeScreen, single, new Point(500, 500), new Point(400, 400), out _),
            "false 를 돌려줘야 함");
    }

    // ---------------- 차단 켤 때 커서 빼내기 ----------------

    private static void PushOutTests()
    {
        Section("차단을 켤 때 커서가 이미 금지 영역 안인 경우");

        var zones = new[] { new Rectangle(0, 0, 15, 1440) };

        Check("가장 가까운 바깥쪽(왼쪽 모니터)으로 밀려남",
            ZoneGeometry.TryPushToNearestOutside(zones, TwoMonitors, new Point(3, 700), out var p1) &&
            p1 == new Point(-1, 700), "(-1,700)");

        Check("띠 오른쪽에 가까우면 오른쪽으로 밀려남",
            ZoneGeometry.TryPushToNearestOutside(zones, TwoMonitors, new Point(13, 700), out var p2) &&
            p2 == new Point(15, 700), "(15,700)");

        // 왼쪽 모니터가 없으면 왼쪽으로는 못 나가므로 오른쪽으로만 밀린다.
        Check("옆 모니터가 없으면 화면 안쪽으로만 밀려남",
            ZoneGeometry.TryPushToNearestOutside(zones, new[] { Primary }, new Point(3, 700), out var p3) &&
            p3 == new Point(15, 700), "(15,700)");
    }

    // ---------------- 설정 → 사각형 ----------------

    private static void ZoneBuildTests()
    {
        Section("설정값에서 금지 사각형 만들기");

        var monitor = new MonitorInfo
        {
            DeviceName = @"\\.\DISPLAY2",
            FriendlyName = "테스트",
            Bounds = new Rectangle(0, 0, 3440, 1440),
            IsPrimary = true,
            DisplayIndex = 2,
        };
        var monitors = new List<MonitorInfo> { monitor };

        var config = new AppConfig();
        config.SetEdges(monitor, new EdgeZones { Left = 15, Right = 20, Top = 10, Bottom = 5 });

        var rects = config.BuildZoneRects(monitors);

        Check("네 변 모두 지정하면 사각형 4개", rects.Count == 4, "4개");
        Check("왼쪽", rects.Contains(new Rectangle(0, 0, 15, 1440)), "(0,0,15,1440)");
        Check("오른쪽", rects.Contains(new Rectangle(3420, 0, 20, 1440)), "(3420,0,20,1440)");
        Check("위쪽", rects.Contains(new Rectangle(0, 0, 3440, 10)), "(0,0,3440,10)");
        Check("아래쪽", rects.Contains(new Rectangle(0, 1435, 3440, 5)), "(0,1435,3440,5)");

        var wide = new AppConfig();
        wide.SetEdges(monitor, new EdgeZones { Left = 99999 });
        var wideRects = wide.BuildZoneRects(monitors);
        Check("모니터보다 넓은 값은 모니터 폭으로 잘림",
            wideRects.Count == 1 && wideRects[0] == new Rectangle(0, 0, 3440, 1440), "(0,0,3440,1440)");

        var cleared = new AppConfig();
        cleared.SetEdges(monitor, new EdgeZones { Left = 15 });
        cleared.SetEdges(monitor, new EdgeZones());
        Check("모두 0으로 바꾸면 설정 항목이 사라짐", cleared.Zones.Count == 0, "0개");

        var clone = config.Clone();
        clone.Zones[0].Edges.Left = 999;
        Check("Clone 은 깊은 복사(취소 눌러도 원본 안 바뀜)",
            config.Zones[0].Edges.Left == 15, "15");
    }

    // ---------------- 모니터 매칭 ----------------

    private static void MonitorMatchTests()
    {
        Section("저장된 설정을 현재 모니터에 연결하기");

        var m1 = new MonitorInfo { DeviceName = @"\\.\DISPLAY1", Bounds = new Rectangle(0, 0, 1920, 1080), DisplayIndex = 1 };
        var m2 = new MonitorInfo { DeviceName = @"\\.\DISPLAY2", Bounds = new Rectangle(1920, 0, 1920, 1080), DisplayIndex = 2 };
        var list = new List<MonitorInfo> { m1, m2 };

        var byName = new MonitorZone { DeviceName = @"\\.\DISPLAY2", DisplayIndex = 9 };
        byName.SetBounds(new Rectangle(0, 0, 1, 1));
        Check("장치 이름이 맞으면 그 모니터", AppConfig.ResolveMonitor(byName, list) == m2, "DISPLAY2");

        var byBounds = new MonitorZone { DeviceName = @"\\.\DISPLAY7", DisplayIndex = 9 };
        byBounds.SetBounds(new Rectangle(1920, 0, 1920, 1080));
        Check("이름이 바뀌었어도 좌표가 같으면 매칭", AppConfig.ResolveMonitor(byBounds, list) == m2, "DISPLAY2");

        var byIndex = new MonitorZone { DeviceName = @"\\.\DISPLAY7", DisplayIndex = 1 };
        byIndex.SetBounds(new Rectangle(5, 5, 5, 5));
        Check("이름·좌표 모두 달라도 번호로 매칭", AppConfig.ResolveMonitor(byIndex, list) == m1, "DISPLAY1");

        var noMatch = new MonitorZone { DeviceName = @"\\.\DISPLAY7", DisplayIndex = 9 };
        noMatch.SetBounds(new Rectangle(5, 5, 5, 5));
        Check("해당 모니터가 없으면 null", AppConfig.ResolveMonitor(noMatch, list) == null, "null");

        var config = new AppConfig();
        config.SetEdges(m2, new EdgeZones { Left = 15 });
        Check("연결이 끊긴 모니터의 설정은 조용히 건너뜀",
            config.BuildZoneRects(new List<MonitorInfo> { m1 }).Count == 0, "0개");
    }

    // ---------------- 도우미 ----------------

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"[{title}]");
    }

    private static void CheckJump(string name, Rectangle[] zones, Rectangle[] monitors,
        Point prev, Point cur, Point expected)
    {
        bool ok = ZoneGeometry.TryComputeJumpTarget(zones, monitors, prev, cur, out var actual);
        Report(name, ok && actual == expected, $"({expected.X},{expected.Y})",
            ok ? $"({actual.X},{actual.Y})" : "실패");
    }

    private static void Check(string name, bool condition, string expected)
        => Report(name, condition, expected, condition ? expected : "다름");

    private static void Report(string name, bool ok, string expected, string actual)
    {
        if (ok)
        {
            _passed++;
            Console.WriteLine($"  OK   {name}");
        }
        else
        {
            _failed++;
            Console.WriteLine($"  실패 {name}  (기대 {expected}, 실제 {actual})");
        }
    }
}
