using System.Security.Cryptography;
using System.Text;

namespace GPTAutoResume.Automation;

public sealed record ConversationTargetIdentity(
    string SurfaceType,
    string ConversationTitleHash,
    string SelectedItemIdentity,
    string ContainerAutomationId,
    string ContainerNameHash,
    string ComposerIdentity)
{
    public string NavigationRowIdentity { get; init; } = "";
    public bool HasUsableSignal =>
        !string.IsNullOrWhiteSpace(ConversationTitleHash)
        || !string.IsNullOrWhiteSpace(SelectedItemIdentity)
        || !string.IsNullOrWhiteSpace(ContainerAutomationId)
        || !string.IsNullOrWhiteSpace(ContainerNameHash)
        || !string.IsNullOrWhiteSpace(ComposerIdentity);

    public static string Hash(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}

public interface IConversationIdentityProvider
{
    ConversationTargetIdentity? CaptureConversationIdentity(nint hwnd);
    bool IsConversationStillActive(nint hwnd, ConversationTargetIdentity identity);
}

public interface IActiveWorkTitleProvider
{
    string GetActiveWorkDisplayName(nint hwnd);
}
