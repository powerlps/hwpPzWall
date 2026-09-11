using System.Text;

namespace HwpPzWall;

/// <summary>단축키 조합을 사람이 읽는 문자열로 바꾼다.</summary>
internal static class HotkeyText
{
    public static string Format(uint modifiers, uint virtualKey)
    {
        if (virtualKey == 0) return "(지정 안 됨)";

        var sb = new StringBuilder();
        if ((modifiers & Native.MOD_CONTROL) != 0) sb.Append("Ctrl + ");
        if ((modifiers & Native.MOD_ALT) != 0) sb.Append("Alt + ");
        if ((modifiers & Native.MOD_SHIFT) != 0) sb.Append("Shift + ");
        if ((modifiers & Native.MOD_WIN) != 0) sb.Append("Win + ");
        sb.Append(KeyName((Keys)virtualKey));
        return sb.ToString();
    }

    private static string KeyName(Keys key) => key switch
    {
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
        >= Keys.NumPad0 and <= Keys.NumPad9 => "숫자패드 " + (key - Keys.NumPad0),
        Keys.Oemtilde => "`",
        Keys.OemMinus => "-",
        Keys.Oemplus => "=",
        Keys.OemOpenBrackets => "[",
        Keys.OemCloseBrackets => "]",
        Keys.OemPipe => "\\",
        Keys.OemSemicolon => ";",
        Keys.OemQuotes => "'",
        Keys.Oemcomma => ",",
        Keys.OemPeriod => ".",
        Keys.OemQuestion => "/",
        Keys.Escape => "Esc",
        Keys.PageUp => "PageUp",
        Keys.PageDown => "PageDown",
        _ => key.ToString(),
    };
}

/// <summary>
/// 단축키를 눌러서 지정하는 입력칸.
/// ProcessCmdKey 를 가로채므로 Alt·Ctrl 조합도 그대로 잡힌다.
/// </summary>
internal sealed class HotkeyBox : TextBox
{
    public uint Modifiers { get; private set; }
    public uint VirtualKey { get; private set; }

    public event EventHandler? HotkeyChanged;

    public HotkeyBox()
    {
        ReadOnly = true;
        Cursor = Cursors.Hand;
        TextAlign = HorizontalAlignment.Center;
        BackColor = SystemColors.Window;
    }

    public void SetHotkey(uint modifiers, uint virtualKey)
    {
        Modifiers = modifiers;
        VirtualKey = virtualKey;
        RefreshText();
    }

    private void RefreshText() => Text = HotkeyText.Format(Modifiers, VirtualKey);

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        Text = "키를 누르세요...  (지우기: Backspace)";
    }

    protected override void OnLeave(EventArgs e)
    {
        base.OnLeave(e);
        RefreshText();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!Focused) return base.ProcessCmdKey(ref msg, keyData);

        var key = keyData & Keys.KeyCode;

        // 수식 키 단독 입력은 무시한다.
        if (key is Keys.None or Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
            return true;

        if (key is Keys.Back or Keys.Delete)
        {
            Modifiers = 0;
            VirtualKey = 0;
            RefreshText();
            HotkeyChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        if (key == Keys.Tab) return base.ProcessCmdKey(ref msg, keyData);   // 포커스 이동은 남겨둔다

        uint mods = 0;
        if ((keyData & Keys.Control) != 0) mods |= Native.MOD_CONTROL;
        if ((keyData & Keys.Alt) != 0) mods |= Native.MOD_ALT;
        if ((keyData & Keys.Shift) != 0) mods |= Native.MOD_SHIFT;

        // 수식 키 없는 단독 키는 F1~F24 만 허용한다(일반 타자를 뺏지 않도록).
        bool isFunctionKey = key is >= Keys.F1 and <= Keys.F24;
        if (mods == 0 && !isFunctionKey)
        {
            Text = "수식 키(Ctrl/Alt/Shift)와 함께 누르거나 F1~F12 를 누르세요";
            return true;
        }

        Modifiers = mods;
        VirtualKey = (uint)key;
        RefreshText();
        HotkeyChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        e.Handled = true;   // 비프음 방지
    }
}

/// <summary>
/// 전역 단축키와 디스플레이 변경 알림을 받는 숨은 창.
/// 메시지 전용 창(HWND_MESSAGE)은 WM_DISPLAYCHANGE 를 못 받으므로 일반 창을 만들되 표시하지 않는다.
/// </summary>
internal sealed class MessageWindow : NativeWindow, IDisposable
{
    private const int HotkeyId = 0xB17E;

    private bool _registered;

    public event Action? HotkeyPressed;
    public event Action? DisplayChanged;

    public MessageWindow()
    {
        CreateHandle(new CreateParams
        {
            Caption = "hwpPzWall.Messages",
            X = -32000,
            Y = -32000,
            Width = 0,
            Height = 0,
            Style = 0,        // WS_OVERLAPPED, 표시하지 않음
        });
    }

    /// <summary>단축키를 다시 등록한다. 실패 사유는 out 으로 돌려준다.</summary>
    public bool RegisterHotkey(uint modifiers, uint virtualKey, out string? error)
    {
        UnregisterHotkey();
        error = null;

        if (virtualKey == 0)
        {
            error = "단축키가 지정되지 않았습니다.";
            return false;
        }

        if (Native.RegisterHotKey(Handle, HotkeyId, modifiers | Native.MOD_NOREPEAT, virtualKey))
        {
            _registered = true;
            return true;
        }

        var code = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        error = code == 1409
            ? $"단축키 {HotkeyText.Format(modifiers, virtualKey)} 은(는) 다른 프로그램이 이미 쓰고 있습니다."
            : $"단축키 등록 실패 (Win32 오류 {code})";
        Log.Write(error);
        return false;
    }

    public void UnregisterHotkey()
    {
        if (!_registered) return;
        Native.UnregisterHotKey(Handle, HotkeyId);
        _registered = false;
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case Native.WM_HOTKEY when m.WParam.ToInt32() == HotkeyId:
                HotkeyPressed?.Invoke();
                return;

            case Native.WM_DISPLAYCHANGE:
                DisplayChanged?.Invoke();
                break;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterHotkey();
        if (Handle != IntPtr.Zero) DestroyHandle();
    }
}
