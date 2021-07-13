namespace Timekeeper.Client.Model
{
    /// <summary>
    /// When you create a new branch, use the data in this class
    /// to customize the branding.
    ///
    /// Other things you can customize:
    /// - branch.css file in wwwroot/css
    /// - logo.png file in wwwroot/images
    /// - favicon.ico file in wwwroot
    ///
    /// You can also define a session template in appsettings.json
    /// </summary>
    public class Branding
    {
        public const string AboutPageTitle = "Channel9: About";
        public const bool AllowSessionSelection = true;
        public const bool CanEditGuestName = true;
        public const string ConfigurePageTitle = "Channel9: Configure";
        public const string GuestPageTitle = "Channel9 Guest Page";
        public const string LoginPageTitle = "Channel9: Login";
        public const string MainPageTitle = "Channel9";
        public const string SessionPageTitle = "Channel9: Sessions";
        public const string WindowTitle = "Channel9";

#if DEBUG
        public const bool MustAuthorize = false;
#else
        public const bool MustAuthorize = true;
#endif

        public static string HeaderClass => "header";

        public static string ImagePath => "images/header-logo.png";

        public static string ForegroundClass => "foreground";

        public static string FooterClass => "footer";
    }
}