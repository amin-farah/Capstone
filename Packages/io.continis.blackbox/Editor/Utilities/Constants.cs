using System.IO;

namespace BlackBox.Editor
{
    public static class Constants
    {
        internal const string MenuItemBaseName = "Tools/BlackBox/";
        private const string PackageName = "io.continis.blackbox";
        public const string LogPrefix = "(BlackBox)";
        
        internal static readonly string PackageFolder = Path.Combine("Packages", PackageName);
        internal static readonly string UITookitFolder = Path.Combine(PackageFolder, "UI", "UIToolkit");
        internal static readonly string UIImagesFolder = Path.Combine(PackageFolder, "UI", "Images");
        
        internal static readonly string StylesAssetPath = Path.Combine(UITookitFolder, "BB_Styles.uss");

        // URLs
        internal const string ReviewUrl = "https://assetstore.unity.com/packages/tools/utilities/blackbox-improved-prefab-workflows-274430#reviews";
        internal const string DiscordUrl = "https://discord.com/invite/rCRug7Szr8";
        internal const string DocumentationUrl = "https://tools.continis.io/v/black-box";
        internal const string OtherToolsUrl = "https://assetstore.unity.com/publishers/87819";
        internal const string SupportEmail = "mailto:buoybase@gmail.com";
    }
}