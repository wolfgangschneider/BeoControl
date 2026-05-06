namespace WebKitGtk;

public record BlazorWebViewOptions
{
	public required Type RootComponent { get; init; }
	public string HostPath { get; init; } = Path.Combine("wwwroot", "index.html");
	public string ResolvedHostPath => ResolveHostPath(HostPath);
	public string ContentRoot => Path.GetDirectoryName(ResolvedHostPath)!;
	public string RelativeHostPath => Path.GetRelativePath(ContentRoot, ResolvedHostPath);

	static string ResolveHostPath(string hostPath)
	{
		foreach (var baseDirectory in GetBaseDirectories())
		{
			var resolvedPath = Path.GetFullPath(hostPath, baseDirectory);
			if (File.Exists(resolvedPath))
			{
				return resolvedPath;
			}
		}

		return Path.GetFullPath(hostPath, AppContext.BaseDirectory);
	}

	static IEnumerable<string> GetBaseDirectories()
	{
		return new string?[]
		{
			AppContext.BaseDirectory,
			AppDomain.CurrentDomain.BaseDirectory,
			GetDirectoryName(Environment.ProcessPath),
			GetDirectoryName(GetProcessTargetPath()),
			GetDirectoryName(Environment.GetCommandLineArgs().FirstOrDefault()),
			Directory.GetCurrentDirectory()
		}
		.Where(path => !string.IsNullOrWhiteSpace(path))
		.Select(path => path!)
		.Distinct(StringComparer.Ordinal);
	}

	static string? GetProcessTargetPath()
	{
		var processPath = Environment.ProcessPath;
		if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
		{
			return null;
		}

		return File.ResolveLinkTarget(processPath, returnFinalTarget: true)?.FullName;
	}

	static string? GetDirectoryName(string? path)
	{
		return string.IsNullOrWhiteSpace(path)
			? null
			: Path.GetDirectoryName(Path.GetFullPath(path));
	}
}
