using System.ComponentModel;
using System.Windows;

namespace Tessalume.App.Features.Pets;

internal interface IPetMotionPreference
{
    bool IsReducedMotion { get; }

    event EventHandler? Changed;
}

internal sealed class SystemPetMotionPreference : IPetMotionPreference, IDisposable
{
    private bool _disposed;

    public SystemPetMotionPreference()
    {
        SystemParameters.StaticPropertyChanged += SystemParameters_StaticPropertyChanged;
    }

    public bool IsReducedMotion =>
        !SystemParameters.ClientAreaAnimation || !SystemParameters.UIEffects;

    public event EventHandler? Changed;

    private void SystemParameters_StaticPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemParameters.ClientAreaAnimation) or
            nameof(SystemParameters.UIEffects))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        SystemParameters.StaticPropertyChanged -= SystemParameters_StaticPropertyChanged;
        GC.SuppressFinalize(this);
    }
}
