using System;

namespace GoogleAnalytics
{
    /// <inheritdoc />
    /// <summary>
    /// Represents an object capable of tracking events for a single Google Analytics property.
    /// </summary>
    public sealed class Tracker : SimpleTracker
    {
        private readonly IPlatformInfoProvider _platformInfoProvider;

        /// <inheritdoc />
        /// <summary>
        /// Instantiates a new instance of <see cref="T:GoogleAnalytics.Tracker" />.
        /// </summary>
        /// <param name="propertyId">the property ID to track to.</param>
        /// <param name="platformInfoProvider">An object capable of providing platform and environment specific information.</param>
        /// <param name="serviceManager">The object used to send <see cref="T:GoogleAnalytics.Hit" />s to the service.</param>
        public Tracker(string propertyId, IPlatformInfoProvider platformInfoProvider, IServiceManager serviceManager)
            :base(propertyId, serviceManager)
        {
            _platformInfoProvider = platformInfoProvider;
            if (platformInfoProvider == null) return;
            ClientId = platformInfoProvider.AnonymousClientId;
            ScreenColors = platformInfoProvider.ScreenColors;
            ScreenResolution = platformInfoProvider.ScreenResolution;
            Language = platformInfoProvider.UserLanguage;
            ViewportSize = platformInfoProvider.ViewPortResolution;
            platformInfoProvider.ViewPortResolutionChanged += PlatformTrackingInfo_ViewPortResolutionChanged;
            platformInfoProvider.ScreenResolutionChanged += PlatformTrackingInfo_ScreenResolutionChanged;
        }

        private void PlatformTrackingInfo_ViewPortResolutionChanged(object sender, EventArgs args)
        {
            ViewportSize = _platformInfoProvider.ViewPortResolution;
        }

        private void PlatformTrackingInfo_ScreenResolutionChanged(object sender, EventArgs args)
        {
            ScreenResolution = _platformInfoProvider.ScreenResolution;
        }
    }
}
