namespace BeoControlMaui;

/// <summary>
/// Window size for the remote UI on Windows.
/// Adjust these values if the remote CSS dimensions change.
/// Remote body width is defined in Beo4.razor.css / Beolink1000.razor.css (.beo4 / .bl1000 → width: 170px).
/// </summary>
internal static class RemoteWindow
{
    /// <summary>Total window width in physical pixels (remote 170px + shell padding + OS chrome).</summary>
    public const int Width = 220;

    /// <summary>Total window height in physical pixels.</summary>
    public const int Height = 750;
}
