namespace WebKitGtk;

public record BlazorWebViewOptions
{
	public required Type RootComponent { get; init; }
	public string HostPath { get; init; } = Path.Combine("wwwroot", "index.html");
	public string ResolvedHostPath => Path.GetFullPath(HostPath, AppContext.BaseDirectory);
	public string ContentRoot => Path.GetDirectoryName(ResolvedHostPath)!;
	public string RelativeHostPath => Path.GetRelativePath(ContentRoot, ResolvedHostPath);
}
