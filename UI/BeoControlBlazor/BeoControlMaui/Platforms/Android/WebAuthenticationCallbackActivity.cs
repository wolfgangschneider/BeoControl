using Android.App;
using Android.Content.PM;

using Microsoft.Maui.Authentication;

namespace BeoControlMaui;

[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter(
    [Android.Content.Intent.ActionView],
    Categories = [Android.Content.Intent.CategoryDefault, Android.Content.Intent.CategoryBrowsable],
    DataScheme = CallbackScheme,
    DataHost = CallbackHost)]
public sealed class WebAuthenticationCallbackActivity : WebAuthenticatorCallbackActivity
{
    public const string CallbackScheme = "beocontrolspotify";
    public const string CallbackHost = "callbac";
}
