using System.Security.Principal;

namespace ProcessGovernor.Services;

public sealed class ElevationService : IElevationService
{
    public bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
