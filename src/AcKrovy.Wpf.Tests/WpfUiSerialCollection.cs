using Xunit;

namespace AcKrovy.Wpf.Tests;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class WpfUiSerialCollection
{
    public const string CollectionName = "WPF UI serial";
}
