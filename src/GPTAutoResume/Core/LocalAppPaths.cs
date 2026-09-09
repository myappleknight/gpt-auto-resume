using System.IO;

namespace GPTAutoResume.Core;

public static class LocalAppPaths
{
    public static string AppDataRoot
    {
        get
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                return Path.GetFullPath(Path.Combine(localAppData, "GPTAutoResume"));
            }

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.GetFullPath(Path.Combine(userProfile, "AppData", "Local", "GPTAutoResume"));
        }
    }

    public static string PossibleLimitDiagnosticsDirectory =>
        Path.Combine(AppDataRoot, "possible-limits");
}
