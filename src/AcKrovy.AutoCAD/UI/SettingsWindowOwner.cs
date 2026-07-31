using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace AcKrovy.AutoCAD.UI;

internal static class SettingsWindowOwner
{
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint MonitorDefaultToNull = 0x00000000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const int WindowPositionChanging = 0x0046;

    public static bool TryAssign(Window window, IntPtr ownerHandle)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var ownsAutoCadWindow = false;
        if (ownerHandle != IntPtr.Zero)
        {
            try
            {
                new WindowInteropHelper(window).Owner = ownerHandle;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                ownsAutoCadWindow = true;
            }
            catch (Exception)
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        PrepareInitialPlacement(
            window,
            ownsAutoCadWindow ? ownerHandle : IntPtr.Zero);
        return ownsAutoCadWindow;
    }

    public static IDisposable PreserveCurrentPosition(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        try
        {
            var windowHandle = new WindowInteropHelper(window).Handle;
            var source = windowHandle == IntPtr.Zero
                ? null
                : HwndSource.FromHwnd(windowHandle);
            if (source is null || !GetWindowRect(windowHandle, out var bounds))
            {
                return EmptyPlacementGuard.Instance;
            }

            HwndSourceHook placementHook = (
                IntPtr _,
                int message,
                IntPtr unusedParameter,
                IntPtr parameter,
                ref bool handled) =>
            {
                if (message == WindowPositionChanging && parameter != IntPtr.Zero)
                {
                    PreservePosition(parameter, bounds);
                }

                return IntPtr.Zero;
            };
            source.AddHook(placementHook);
            return new PlacementGuard(source, placementHook);
        }
        catch (Exception)
        {
            return EmptyPlacementGuard.Instance;
        }
    }

    public static T RunWithPreservedPlacement<T>(Window window, Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(action);
        var snapshot = CapturePlacement(window);
        var placementGuard = PreserveCurrentPosition(window);
        try
        {
            return action();
        }
        finally
        {
            ScheduleDeferredRestore(window, snapshot, placementGuard);
        }
    }

    private static void ScheduleDeferredRestore(
        Window window,
        SettingsWindowPlacementSnapshot snapshot,
        IDisposable placementGuard)
    {
        var released = 0;
        EventHandler? closedHandler = null;
        void ReleasePlacementGuard()
        {
            if (Interlocked.Exchange(ref released, 1) != 0)
            {
                return;
            }

            if (closedHandler is not null)
            {
                window.Closed -= closedHandler;
            }
            placementGuard.Dispose();
        }

        closedHandler = (_, _) => ReleasePlacementGuard();
        window.Closed += closedHandler;
        try
        {
            RestoreShowAndActivate(window, snapshot);
            _ = window.Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() =>
                {
                    if (Volatile.Read(ref released) != 0)
                    {
                        return;
                    }

                    RestoreShowAndActivate(window, snapshot);
                    _ = window.Dispatcher.BeginInvoke(
                        DispatcherPriority.ContextIdle,
                        new Action(() =>
                        {
                            try
                            {
                                if (Volatile.Read(ref released) == 0 &&
                                    !PlacementMatchesSnapshot(window, snapshot))
                                {
                                    RestoreShowAndActivate(window, snapshot);
                                }
                            }
                            finally
                            {
                                ReleasePlacementGuard();
                            }
                        }));
                }));
        }
        catch (Exception)
        {
            try
            {
                RestoreShowAndActivate(window, snapshot);
            }
            finally
            {
                ReleasePlacementGuard();
            }
        }
    }

    private static void RestoreShowAndActivate(
        Window window,
        SettingsWindowPlacementSnapshot snapshot)
    {
        RestorePlacement(window, snapshot);
        if (!window.IsVisible)
        {
            window.Show();
        }
        _ = window.Activate();
    }

    private static bool PlacementMatchesSnapshot(
        Window window,
        SettingsWindowPlacementSnapshot snapshot)
    {
        if (!snapshot.IsValid ||
            window.WindowStartupLocation != WindowStartupLocation.Manual ||
            window.WindowState !=
                (snapshot.WindowState == WindowState.Maximized
                    ? WindowState.Maximized
                    : WindowState.Normal))
        {
            return false;
        }

        var bounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight)
            : window.RestoreBounds;
        if (!AreClose(bounds.Left, snapshot.Left) ||
            !AreClose(bounds.Top, snapshot.Top) ||
            !AreClose(bounds.Width, snapshot.Width) ||
            !AreClose(bounds.Height, snapshot.Height))
        {
            return false;
        }

        return !snapshot.HasNativeBounds ||
            TryGetWindowBounds(window, out var nativeBounds) &&
            AreClose(nativeBounds, snapshot.NativeBounds);
    }

    private static bool AreClose(double first, double second) =>
        Math.Abs(first - second) <= 0.5d;

    private static bool AreClose(NativeRect first, NativeRect second) =>
        Math.Abs(first.Left - second.Left) <= 1 &&
        Math.Abs(first.Top - second.Top) <= 1 &&
        Math.Abs(first.Width - second.Width) <= 1 &&
        Math.Abs(first.Height - second.Height) <= 1;

    internal static SettingsWindowPlacementSnapshot CapturePlacement(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        try
        {
            var state = window.WindowState;
            var bounds = state == WindowState.Normal
                ? new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight)
                : window.RestoreBounds;
            var isValid = IsFinite(bounds.Left) &&
                IsFinite(bounds.Top) &&
                IsFinite(bounds.Width) &&
                IsFinite(bounds.Height) &&
                bounds.Width > 0d &&
                bounds.Height > 0d;
            var hasNativeBounds = TryGetWindowBounds(window, out var nativeBounds);
            return new SettingsWindowPlacementSnapshot(
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                state,
                isValid,
                nativeBounds,
                hasNativeBounds);
        }
        catch (Exception)
        {
            return SettingsWindowPlacementSnapshot.Invalid;
        }
    }

    internal static void RestorePlacement(
        Window window,
        SettingsWindowPlacementSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!snapshot.IsValid)
        {
            return;
        }

        try
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.WindowState = WindowState.Normal;
            window.Width = snapshot.Width;
            window.Height = snapshot.Height;
            window.Left = snapshot.Left;
            window.Top = snapshot.Top;
            ClampRestoredPlacement(window, snapshot);
            window.WindowState = snapshot.WindowState == WindowState.Maximized
                ? WindowState.Maximized
                : WindowState.Normal;
        }
        catch (Exception)
        {
            // Keep the current visible placement when restoration is unavailable.
        }
    }

    private static void PrepareInitialPlacement(Window window, IntPtr ownerHandle)
    {
        if (window.WindowState != WindowState.Normal)
        {
            return;
        }

        try
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            var windowHandle = new WindowInteropHelper(window).EnsureHandle();
            var source = HwndSource.FromHwnd(windowHandle);
            if (source is null)
            {
                return;
            }

            HwndSourceHook? placementHook = null;
            placementHook = (
                IntPtr handle,
                int message,
                IntPtr unusedParameter,
                IntPtr parameter,
                ref bool handled) =>
            {
                if (message == WindowPositionChanging && parameter != IntPtr.Zero)
                {
                    EnforceInitialPosition(handle, ownerHandle, parameter);
                }

                return IntPtr.Zero;
            };
            source.AddHook(placementHook);

            void RemovePlacementGuard()
            {
                if (placementHook is null)
                {
                    return;
                }

                source.RemoveHook(placementHook);
                placementHook = null;
            }

            EventHandler? renderedHandler = null;
            renderedHandler = (_, _) =>
            {
                window.ContentRendered -= renderedHandler;
                _ = window.Dispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(RemovePlacementGuard));
            };
            window.ContentRendered += renderedHandler;
            window.Closed += (_, _) => RemovePlacementGuard();

            CenterNativeWindow(window, ownerHandle);
        }
        catch (Exception)
        {
            // Keep WPF CenterOwner/CenterScreen as the safe fallback.
        }
    }

    private static void EnforceInitialPosition(
        IntPtr windowHandle,
        IntPtr ownerHandle,
        IntPtr parameter)
    {
        try
        {
            var requested = Marshal.PtrToStructure<NativeWindowPosition>(parameter);
            var usesCurrentSize = (requested.Flags & SwpNoSize) != 0;
            var currentBounds = default(NativeRect);
            if (usesCurrentSize && !GetWindowRect(windowHandle, out currentBounds))
            {
                return;
            }

            var width = usesCurrentSize ? currentBounds.Width : Math.Max(1, requested.Width);
            var height = usesCurrentSize ? currentBounds.Height : Math.Max(1, requested.Height);
            if (!TryGetCenteredPosition(
                windowHandle,
                ownerHandle,
                width,
                height,
                out var position))
            {
                return;
            }

            requested.X = (int)position.X;
            requested.Y = (int)position.Y;
            requested.Flags &= ~SwpNoMove;
            Marshal.StructureToPtr(requested, parameter, false);
        }
        catch (Exception)
        {
            // A failed guard must not block the native window message.
        }
    }

    private static void PreservePosition(IntPtr parameter, NativeRect bounds)
    {
        try
        {
            var requested = Marshal.PtrToStructure<NativeWindowPosition>(parameter);
            if ((requested.Flags & SwpNoMove) == 0)
            {
                requested.X = bounds.Left;
                requested.Y = bounds.Top;
            }
            if ((requested.Flags & SwpNoSize) == 0)
            {
                requested.Width = bounds.Width;
                requested.Height = bounds.Height;
            }
            Marshal.StructureToPtr(requested, parameter, false);
        }
        catch (Exception)
        {
            // A failed guard must not block the native window message.
        }
    }

    private static void CenterNativeWindow(Window window, IntPtr ownerHandle)
    {
        if (window.WindowState != WindowState.Normal)
        {
            return;
        }

        try
        {
            var windowHandle = new WindowInteropHelper(window).Handle;
            if (windowHandle == IntPtr.Zero ||
                !GetWindowRect(windowHandle, out var windowBounds))
            {
                return;
            }

            if (!TryGetCenteredPosition(
                windowHandle,
                ownerHandle,
                windowBounds.Width,
                windowBounds.Height,
                out var position))
            {
                return;
            }

            _ = SetWindowPos(
                windowHandle,
                IntPtr.Zero,
                (int)position.X,
                (int)position.Y,
                0,
                0,
                SwpNoSize | SwpNoZOrder | SwpNoActivate);
        }
        catch (Exception)
        {
            // Keep WPF CenterOwner/CenterScreen as the safe fallback.
        }
    }

    private static bool TryGetCenteredPosition(
        IntPtr windowHandle,
        IntPtr ownerHandle,
        int windowWidth,
        int windowHeight,
        out System.Windows.Point position)
    {
        var monitorHandle = MonitorFromWindow(
            ownerHandle != IntPtr.Zero ? ownerHandle : windowHandle,
            MonitorDefaultToNearest);
        var monitorInfo = MonitorInfo.Create();
        if (monitorHandle == IntPtr.Zero ||
            !GetMonitorInfo(monitorHandle, ref monitorInfo))
        {
            position = default;
            return false;
        }

        var targetBounds = ownerHandle != IntPtr.Zero &&
            !IsIconic(ownerHandle) &&
            GetWindowRect(ownerHandle, out var ownerBounds)
                ? ownerBounds
                : monitorInfo.WorkArea;
        position = CalculateCenteredPosition(
            targetBounds,
            monitorInfo.WorkArea,
            windowWidth,
            windowHeight);
        return true;
    }

    private static void ClampRestoredPlacement(
        Window window,
        SettingsWindowPlacementSnapshot snapshot)
    {
        var windowHandle = new WindowInteropHelper(window).Handle;
        if (windowHandle == IntPtr.Zero ||
            !GetWindowRect(windowHandle, out var restoredBounds))
        {
            return;
        }

        var monitorHandle = snapshot.HasNativeBounds
            ? MonitorFromPoint(snapshot.NativeBounds.Center, MonitorDefaultToNull)
            : MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        if (monitorHandle == IntPtr.Zero)
        {
            CenterNativeWindow(window, new WindowInteropHelper(window).Owner);
            return;
        }

        var monitorInfo = MonitorInfo.Create();
        if (!GetMonitorInfo(monitorHandle, ref monitorInfo))
        {
            return;
        }

        var position = CalculateClampedPosition(restoredBounds, monitorInfo.WorkArea);
        _ = SetWindowPos(
            windowHandle,
            IntPtr.Zero,
            (int)position.X,
            (int)position.Y,
            0,
            0,
            SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    internal static System.Windows.Point CalculateClampedPosition(
        NativeRect bounds,
        NativeRect workArea)
    {
        var maximumLeft = Math.Max(workArea.Left, workArea.Right - bounds.Width);
        var maximumTop = Math.Max(workArea.Top, workArea.Bottom - bounds.Height);
        return new System.Windows.Point(
            Math.Clamp(bounds.Left, workArea.Left, maximumLeft),
            Math.Clamp(bounds.Top, workArea.Top, maximumTop));
    }

    private static bool IsFinite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    internal static bool TryGetWindowBounds(Window window, out NativeRect bounds)
    {
        ArgumentNullException.ThrowIfNull(window);
        bounds = default;
        var handle = new WindowInteropHelper(window).Handle;
        return handle != IntPtr.Zero && GetWindowRect(handle, out bounds);
    }

    internal static System.Windows.Point CalculateCenteredPosition(
        NativeRect targetBounds,
        NativeRect workArea,
        int windowWidth,
        int windowHeight)
    {
        var left = targetBounds.Left + ((targetBounds.Width - windowWidth) / 2);
        var top = targetBounds.Top + ((targetBounds.Height - windowHeight) / 2);
        var maximumLeft = Math.Max(workArea.Left, workArea.Right - windowWidth);
        var maximumTop = Math.Max(workArea.Top, workArea.Bottom - windowHeight);
        return new System.Windows.Point(
            Math.Clamp(left, workArea.Left, maximumLeft),
            Math.Clamp(top, workArea.Top, maximumTop));
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect bounds);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct NativeRect
    {
        public NativeRect(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public readonly int Left;

        public readonly int Top;

        public readonly int Right;

        public readonly int Bottom;

        public int Width => Right - Left;

        public int Height => Bottom - Top;

        public NativePoint Center => new(
            Left + (Width / 2),
            Top + (Height / 2));
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct NativePoint(int x, int y)
    {
        public readonly int X = x;

        public readonly int Y = y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;

        public static MonitorInfo Create() => new()
        {
            Size = Marshal.SizeOf<MonitorInfo>(),
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeWindowPosition
    {
        public IntPtr WindowHandle;
        public IntPtr InsertAfter;
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public uint Flags;
    }

    private sealed class PlacementGuard(
        HwndSource source,
        HwndSourceHook placementHook) : IDisposable
    {
        private HwndSource? _source = source;

        public void Dispose()
        {
            var currentSource = Interlocked.Exchange(ref _source, null);
            if (currentSource is null)
            {
                return;
            }

            try
            {
                currentSource.RemoveHook(placementHook);
            }
            catch (Exception)
            {
                // The HWND may already be disposed while unwinding an interaction.
            }
        }
    }

    private sealed class EmptyPlacementGuard : IDisposable
    {
        public static EmptyPlacementGuard Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}

internal readonly record struct SettingsWindowPlacementSnapshot(
    double Left,
    double Top,
    double Width,
    double Height,
    WindowState WindowState,
    bool IsValid,
    SettingsWindowOwner.NativeRect NativeBounds,
    bool HasNativeBounds)
{
    public static SettingsWindowPlacementSnapshot Invalid { get; } = new(
        0d,
        0d,
        0d,
        0d,
        WindowState.Normal,
        false,
        default,
        false);
}
