using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace Axiom.App;

internal static class NativeProbeInput
{
    internal static string Describe(Control control)
    {
        var target = PointerTarget(control);
        var name = new System.Text.StringBuilder(256);
        GetClassName(target,name,name.Capacity);
        return target.ToString() + " " + name + " root " + GetAncestor(target,2);
    }
    internal static nint PointerTarget(Control control)
    {
        var position = control.PointToScreen(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2));
        return WindowFromPoint(new NativeScreenPoint
        {
            X = position.X
          , Y = position.Y
        });
    }
    internal static async Task ClickAsync(Control control, bool right = false)
    {
        var position = control.PointToScreen(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2));
        var point = new NativeScreenPoint
        {
            X = position.X
          , Y = position.Y
        };
        var target = WindowFromPoint(point);
        if (target == 0) throw new IOException("Native pointer target is missing.");
        GetWindowThreadProcessId(target,out var process);
        if (process != Environment.ProcessId) throw new IOException("Native probe refuses to click another process.");
        GetCursorPos(out var previous);
        try
        {
            SetCursorPos(point.X,point.Y);
            mouse_event(right ? 8u : 2u,0,0,0,0); await Task.Delay(60); mouse_event(right ? 16u : 4u,0,0,0,0); await Task.Delay(150);
        }
        finally { SetCursorPos(previous.X,previous.Y); }
    }
    internal static void Type(Window window, string text)
    {
        GetWindowThreadProcessId(GetForegroundWindow(),out var process);
        if (process != Environment.ProcessId) throw new IOException("Native probe refuses to type into another process.");
        var inputs = text.SelectMany(character => new[]
        {
            new NativeProbeKeyboardInput
            {
                Type = 1
              , ScanCode = character
              , Flags = 4
            }
          , new NativeProbeKeyboardInput
            {
                Type = 1
              , ScanCode = character
              , Flags = 6
            }
        }).ToArray();
        if (SendInput((uint)inputs.Length,inputs,Marshal.SizeOf<NativeProbeKeyboardInput>()) != inputs.Length) throw new IOException("Native text input was rejected.");
    }
    internal static void Key(ushort key, bool shift = false)
    {
        GetWindowThreadProcessId(GetForegroundWindow(),out var process);
        if (process != Environment.ProcessId) throw new IOException("Native probe refuses to type into another process.");
        var inputs = new List<NativeProbeKeyboardInput>();
        if (shift) inputs.Add(new()
        {
            Type = 1
          , KeyCode = 0x10
        });
        inputs.Add(new()
        {
            Type = 1
          , KeyCode = key
        });
        inputs.Add(new()
        {
            Type = 1
          , KeyCode = key
          , Flags = 2
        });
        if (shift) inputs.Add(new()
        {
            Type = 1
          , KeyCode = 0x10
          , Flags = 2
        });
        if (SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<NativeProbeKeyboardInput>()) != inputs.Count) throw new IOException("Native keyboard input was rejected.");
    }
    internal static async Task DragAsync(Control source,Control target)
    {
        var origin=source.PointToScreen(new Point(source.Bounds.Width*.6,Math.Min(14,source.Bounds.Height/2)));
        var destination=target.PointToScreen(new Point(target.Bounds.Width*.6,Math.Min(14,target.Bounds.Height/2)));
        foreach(var point in new[] {origin,destination})
        {
            GetWindowThreadProcessId(WindowFromPoint(new NativeScreenPoint
            {
                X = point.X
              , Y = point.Y
            }),out var process);
            if(process!=Environment.ProcessId) throw new IOException("Native drag probe refuses another process.");
        }
        GetCursorPos(out var previous);
        try
        {
            SetCursorPos(origin.X,origin.Y); mouse_event(2,0,0,0,0); await Task.Delay(120);
            for(var step=1;step<=20;step++)
            {
                SetCursorPos(origin.X+(destination.X-origin.X)*step/20,origin.Y+(destination.Y-origin.Y)*step/20);
                await Task.Delay(30);
            }
            mouse_event(4,0,0,0,0); await Task.Delay(250);
        }
        finally { mouse_event(4,0,0,0,0); SetCursorPos(previous.X,previous.Y); }
    }
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativeScreenPoint point);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(nint window,System.Text.StringBuilder name,int maximum);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window,uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window,out uint process);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativeScreenPoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags,uint x,uint y,uint data,nuint extra);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count,NativeProbeKeyboardInput[] inputs,int size);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
