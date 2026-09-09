namespace GPTAutoResume.Automation;

public interface IAccountQuotaReader
{
    // Only a verified account quota surface under this target, never conversation text.
    string ReadAccountQuotaSurfaceText(nint hwnd);
}
