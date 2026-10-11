namespace ProtoForge.UI.Services
{
	using System;

	/// <summary>
	/// Where the project lives. Leave both addresses empty to hide the "homepage" and "check for updates" buttons.
	/// <see cref="LatestReleaseApiUrl"/> must answer with a JSON object that has a <c>tag_name</c> (GitHub releases API) or a <c>version</c> property.
	/// </summary>
	public static class AppInfo
	{
		public static readonly String HomepageUrl = String.Empty;

		public static readonly String LatestReleaseApiUrl = String.Empty;

		public static Boolean HasHomepage
		{
			get { return HomepageUrl.Length > 0; }
		}

		public static Boolean CanCheckForUpdates
		{
			get { return LatestReleaseApiUrl.Length > 0; }
		}
	}
}