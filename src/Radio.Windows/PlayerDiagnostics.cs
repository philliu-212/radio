namespace Radio.Windows;

internal interface IPlayerDiagnostics
{
    string DisplayName { get; }
    IReadOnlyList<int> ChildProcessIds { get; }
    object Describe();
}
