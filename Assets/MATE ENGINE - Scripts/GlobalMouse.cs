using System.Runtime.InteropServices;
using UnityEngine;

public static class GlobalMouse
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int x;
        public int y;
    }

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT lpPoint);

    public static Vector2 GetPosition()
    {
        GetCursorPos(out POINT p);
        return new Vector2(p.x, p.y);
    }
    const int VK_LBUTTON = 0x01;
    const int VK_RBUTTON = 0x02;

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);

    static bool prevLeftDown;

    /// <summary>
    /// True only on the frame the left mouse button is released
    /// </summary>
    public static bool LeftMouseUp()
    {
        bool isDown = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
        bool upThisFrame = prevLeftDown && !isDown;
        prevLeftDown = isDown;
        return upThisFrame;
    }
}
