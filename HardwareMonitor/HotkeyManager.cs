using System.Runtime.InteropServices;

namespace HardwareMonitor;

public sealed class HotkeyManager : IDisposable
{
    public const int OverlayAction = 1;
    public const int LockAction = 2;
    private readonly IntPtr _handle;
    private readonly Func<IntPtr, int, uint, uint, bool> _register;
    private readonly Func<IntPtr, int, bool> _unregister;
    private readonly Dictionary<int, Binding> _bindings = new();
    private int _nextId = 9000;
    private bool _disposed;
    private sealed record Binding(int NativeId, HotkeySettings Settings);

    public HotkeyManager(IntPtr handle) : this(handle, RegisterHotKey, UnregisterHotKey) { }
    public HotkeyManager(IntPtr handle, Func<IntPtr, int, uint, uint, bool> register, Func<IntPtr, int, bool> unregister)
    {
        _handle = handle;
        _register = register;
        _unregister = unregister;
    }

    public bool TrySet(int action, HotkeySettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _bindings.TryGetValue(action, out var previous);
        if (previous?.Settings.Modifiers == settings.Modifiers && previous.Settings.VirtualKey == settings.VirtualKey)
            return true;
        if (settings.VirtualKey is 0 or > 255 || (settings.Modifiers & ~0x000Fu) != 0) return false;
        if (_nextId > 0xBFFF) _nextId = 9000;
        while (_bindings.Values.Any(binding => binding.NativeId == _nextId)) _nextId++;
        int id = _nextId++;
        // Reserve the new combination first; a failed reservation cannot remove the old one.
        if (!_register(_handle, id, settings.Modifiers | 0x4000u, settings.VirtualKey)) return false;
        if (previous != null && !_unregister(_handle, previous.NativeId))
        {
            _unregister(_handle, id);
            return false;
        }
        _bindings[action] = new Binding(id, settings);
        return true;
    }

    public int? FindAction(int nativeId)
    {
        foreach (var (action, binding) in _bindings)
            if (binding.NativeId == nativeId) return action;
        return null;
    }

    public void Clear(int action)
    {
        if (_bindings.Remove(action, out var binding)) _unregister(_handle, binding.NativeId);
    }

    public void Dispose()
    {
        if (_disposed) return;
        foreach (var binding in _bindings.Values) _unregister(_handle, binding.NativeId);
        _bindings.Clear();
        _disposed = true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
