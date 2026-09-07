using AcKrovy.Core.Models.Roofs;
using Autodesk.AutoCAD.ApplicationServices;

namespace AcKrovy.AutoCAD.Infrastructure;

/// <summary>
/// Owns the current rafter transient set. Every refresh removes and disposes the
/// previous set before a replacement is created; a null layout clears preview.
/// </summary>
internal sealed class RoofRafterTransientPreviewController : IDisposable
{
    private readonly Func<RoofRafterLayout, IDisposable> _show;
    private IDisposable? _current;
    private bool _disposed;

    public RoofRafterTransientPreviewController(
        Document document,
        double sourceElevation)
        : this(layout => RoofTransientPreviewSession.ShowRafters(
            document,
            layout,
            sourceElevation))
    {
        ArgumentNullException.ThrowIfNull(document);
    }

    internal RoofRafterTransientPreviewController(
        Func<RoofRafterLayout, IDisposable> show)
    {
        _show = show ?? throw new ArgumentNullException(nameof(show));
    }

    public void Refresh(RoofRafterLayout? layout)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _current?.Dispose();
        _current = null;
        if (layout is not null)
        {
            _current = _show(layout);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _current?.Dispose();
        _current = null;
    }
}

/// <summary>
/// Owns the transient ordinary-rafter set generated from generic Hip topology.
/// This is preview-only; it creates no database entities or metadata.
/// </summary>
internal sealed class RoofFaceRafterTransientPreviewController : IDisposable
{
    private readonly Func<RoofFaceRafterLayout, IDisposable> _show;
    private IDisposable? _current;
    private bool _disposed;

    public RoofFaceRafterTransientPreviewController(
        Document document,
        double sourceElevation)
        : this(layout => RoofTransientPreviewSession.ShowRafters(
            document,
            layout,
            sourceElevation))
    {
        ArgumentNullException.ThrowIfNull(document);
    }

    internal RoofFaceRafterTransientPreviewController(
        Func<RoofFaceRafterLayout, IDisposable> show)
    {
        _show = show ?? throw new ArgumentNullException(nameof(show));
    }

    public void Refresh(RoofFaceRafterLayout? layout)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _current?.Dispose();
        _current = layout is null ? null : _show(layout);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _current?.Dispose();
        _current = null;
    }
}
