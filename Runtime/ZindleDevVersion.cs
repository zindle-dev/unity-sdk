namespace ZindleDevSDK
{
    /// <summary>
    /// SDK version string sent in report metadata as <c>sdkVersion</c>.
    /// This is the single source compiled into builds; it must equal the <c>version</c>
    /// field in package.json. Drift is caught on editor load by ZindleDevVersionCheck,
    /// and Tools/Zindle Dev/Sync Version from package.json updates this value for you.
    /// </summary>
    public static class ZindleDevVersion
    {
        public const string Value = "0.1.0";
    }
}
