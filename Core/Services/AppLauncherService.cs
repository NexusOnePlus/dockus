using dockus.Core.Interop;
using dockus.Core.Models;
using System;
using System.Diagnostics;
using System.Windows;

namespace dockus.Core.Services;

public class AppLauncherService
{
    public void LaunchApp(WindowItem item)
    {
        try
        {
            if (item.IdentifierType == PinnedAppType.Path)
            {
                Process.Start(new ProcessStartInfo(item.Identifier) { UseShellExecute = true });
            }
            else
            {
                IApplicationActivationManager activationManager = new ApplicationActivationManager();
                activationManager.ActivateApplication(item.Identifier, null!, 0, out _);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not start the application: {ex.Message}");
        }
    }
}