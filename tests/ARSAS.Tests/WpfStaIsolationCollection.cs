namespace ARSAS.Tests;

/// <summary>
/// WPF uses process-wide font, theme, Dispatcher and PresentationFramework
/// initialization. The real WPF/STA regression cases retain every assertion
/// but execute exclusively; protocol/data regressions remain parallel.
/// </summary>
[CollectionDefinition("WpfStaIsolation", DisableParallelization = true)]
public sealed class WpfStaIsolationCollection
{
}
