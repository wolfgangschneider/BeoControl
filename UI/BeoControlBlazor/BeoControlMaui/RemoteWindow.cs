namespace BeoControlMaui;

/// <summary>
/// Window size for the remote UI on Windows.
/// Adjust these values if the remote CSS dimensions change.
/// Remote body width is defined in Beo4.razor.css / Beolink1000.razor.css (.beo4 / .bl1000 → width: 170px).
/// </summary>
internal static class RemoteWindow
{
    /// <summary>Total window width in physical pixels (matches --body-width: 210px in app.css).</summary>
    public const int Width = 210;

    /// <summary>Total window height in physical pixels.</summary>
    public const int Height = 750;
}
