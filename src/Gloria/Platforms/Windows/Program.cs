using Microsoft.UI.Dispatching;
using Velopack;

namespace Gloria.WinUI;

/// <summary>
/// Replaces the XAML-generated Main (disabled with DISABLE_XAML_GENERATED_MAIN) so Velopack
/// runs before anything else.
/// </summary>
public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Must be first: Velopack's install/update/uninstall hooks may run here and exit the process.
        VelopackApp.Build().Run();

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(callbackParams =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }
}
