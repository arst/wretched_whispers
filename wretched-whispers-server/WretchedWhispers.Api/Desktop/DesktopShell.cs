using Photino.NET;

namespace WretchedWhispers.Api.Desktop;

public static class DesktopShell
{
    public static void Run(string url) =>
        new PhotinoWindow()
            .SetTitle("Wretched Whispers")
            .SetUseOsDefaultSize(false)
            .SetSize(1280, 860)
            .Center()
            .Load(new Uri(url))
            .WaitForClose();
}
