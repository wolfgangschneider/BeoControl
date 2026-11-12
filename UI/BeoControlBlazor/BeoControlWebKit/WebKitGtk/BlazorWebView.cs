using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using System.Runtime.Versioning;
using System.Web;
using WebKit;
using MemoryInputStream = Gio.Internal.MemoryInputStream;
using Uri = System.Uri;

namespace WebKitGtk;

[UnsupportedOSPlatform("OSX")]
[UnsupportedOSPlatform("Windows")]
public class BlazorWebView : WebView
{
	/// <param name="startPath">First page, e.g. "/setupwindow" for a second window.</param>
	public BlazorWebView(IServiceProvider serviceProvider, string startPath = "/")
	{
		_ = new WebViewManager(this, serviceProvider, startPath, message => HostMessageReceived?.Invoke(message));
	}

	/// <summary>Raised for messages the page posts to <c>window.webkit.messageHandlers.beocontrol</c> (e.g. "open-setup").</summary>
	public event Action<string>? HostMessageReceived;
}

[UnsupportedOSPlatform("OSX")]
[UnsupportedOSPlatform("Windows")]
class WebViewManager : Microsoft.AspNetCore.Components.WebView.WebViewManager
{
	const string Scheme = "app";
	static readonly Uri BaseUri = new($"{Scheme}://localhost/");

	// all web views share the default WebContext, where a URI scheme can be registered only once:
	// one handler, each request goes to the manager of the web view that sent it
	static readonly List<WebViewManager> Managers = [];

	public WebViewManager(WebView webView, IServiceProvider serviceProvider, string startPath, Action<string> hostMessage) : base(
		serviceProvider,
		Dispatcher.CreateDefault(),
		BaseUri,
		new PhysicalFileProvider(serviceProvider.GetRequiredService<BlazorWebViewOptions>().ContentRoot),
		new(),
		serviceProvider.GetRequiredService<BlazorWebViewOptions>().RelativeHostPath)
	{
		var options = serviceProvider.GetRequiredService<BlazorWebViewOptions>();
		_relativeHostPath = options.RelativeHostPath;
		var rootComponent = options.RootComponent;
		_logger = serviceProvider.GetService<ILogger<WebViewManager>>();

		_webView = webView;

		// This is necessary to automatically serve the files in the `_framework` virtual folder.
		// Using `file://` will cause the webview to look for the `_framework` files on the file system,
		// and it won't find them.
		if (_webView.WebContext is null)
		{
			throw new Exception("WebView.WebContext is null");
		}
		if (Managers.Count == 0)
			_webView.WebContext.RegisterUriScheme(Scheme, request => FindManager(request)?.HandleUriScheme(request));
		Managers.Add(this);
		_webView.OnDestroy += (_, _) =>
		{
			Managers.Remove(this);
			_ = DisposeAsync();
		};

		Dispatcher.InvokeAsync(async () =>
		{
			await AddRootComponentAsync(rootComponent, "#app", ParameterView.Empty);
			// <HeadContent> of pages (e.g. setup lifts the remote's fixed body width) needs a HeadOutlet, like MAUI and Blazor Server
			await AddRootComponentAsync(typeof(Microsoft.AspNetCore.Components.Web.HeadOutlet), "head::after", ParameterView.Empty);
		});

		var ucm = webView.GetUserContentManager();
		ucm.AddScript(UserScript.New(
			source:
			"""
				window.__receiveMessageCallbacks = [];

				window.__dispatchMessageCallback = function(message) {
				    window.__receiveMessageCallbacks.forEach(function(callback) { callback(message); });
				};

				window.external = {
				    sendMessage: function(message) {
				        window.webkit.messageHandlers.webview.postMessage(message);
				    },
				    receiveMessage: function(callback) {
				        window.__receiveMessageCallbacks.push(callback);
				    }
				};
			""",
			injectedFrames: UserContentInjectedFrames.AllFrames,
			injectionTime: UserScriptInjectionTime.Start,
			null,
			null)
		);

		UserContentManager.ScriptMessageReceivedSignal.Connect(ucm, (_, signalArgs) =>
		{
			var result = signalArgs.Value;
			MessageReceived(BaseUri, result.ToString());
		}, true, "webview");

		if (!ucm.RegisterScriptMessageHandler("webview", null))
		{
			throw new Exception("Could not register script message handler");
		}

		// app messages to the host (open/close the setup window), apart from Blazor's own channel
		UserContentManager.ScriptMessageReceivedSignal.Connect(ucm, (_, signalArgs) =>
			hostMessage(signalArgs.Value.ToString() ?? ""), true, "beocontrol");
		if (!ucm.RegisterScriptMessageHandler("beocontrol", null))
		{
			throw new Exception("Could not register beocontrol message handler");
		}

		Navigate(startPath);
	}

	readonly WebView _webView;

	// newest first: a destroyed view's handle may be reused by a newer one
	static WebViewManager? FindManager(URISchemeRequest request)
	{
		var handle = request.GetWebView().Handle.DangerousGetHandle();
		return Managers.LastOrDefault(m => m._webView.Handle.DangerousGetHandle() == handle);
	}
	readonly string _relativeHostPath;
	readonly ILogger<WebViewManager>? _logger;

	void HandleUriScheme(URISchemeRequest request)
	{
		if (request.GetScheme() != Scheme)
		{
			throw new Exception($"Invalid scheme \"{request.GetScheme()}\"");
		}

		// page routes ("/", "/setupwindow") get the host page, Blazor's router then shows the page;
		// not allowFallbackOnHostPage: that also answers missing files (blazor.modules.json) with index.html and breaks the start
		var uri = request.GetUri();
		if (!Path.HasExtension(request.GetPath()))
		{
			uri = BaseUri + _relativeHostPath;
		}

		_logger?.LogDebug($"Fetching \"{uri}\"");

		if (TryGetResponseContent(uri, false, out var statusCode, out var statusMessage, out var content, out var headers))
		{
			using var ms = new MemoryStream();
			content.CopyTo(ms);
			var streamPtr = MemoryInputStream.NewFromData(ref ms.GetBuffer()[0], (nint)ms.Length, _ => { });
			var inputStream = new Gio.InputStream(new Gio.Internal.InputStreamHandle(streamPtr, false));
			request.Finish(inputStream, ms.Length, headers["Content-Type"]);
		}
		else
		{
			throw new Exception($"Failed to serve \"{uri}\". {statusCode} - {statusMessage}");
		}
	}

	protected override void NavigateCore(Uri absoluteUri)
	{
		_logger?.LogDebug($"Navigating to \"{absoluteUri}\"");
		_webView.LoadUri(absoluteUri.ToString());
	}

	protected override async void SendMessage(string message)
	{
		var script = $"__dispatchMessageCallback(\"{HttpUtility.JavaScriptStringEncode(message)}\")";
		_logger?.LogDebug($"Dispatching `{script}`");
		_ = await _webView.EvaluateJavascriptAsync(script);
	}
}