using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Core;
using Windows.Networking.Connectivity;
using Windows.Storage;
using Windows.UI.Xaml;

namespace GoogleAnalytics
{
    /// <summary>
    /// Provides shared/platform specific infrastrcuture for GoogleAnalytics.Core Tracker
    /// </summary>
    public sealed class AnalyticsManager : TrackerManager
    {
        private const string KeyAppOptOut = "GoogleAnaltyics.AppOptOut";

        static AnalyticsManager _current;

        private bool _isAppOptOutSet;
        private readonly Application _application;
        private bool _reportUncaughtExceptions;
        private bool _autoTrackNetworkConnectivity;
        private bool _autoAppLifetimeMonitoring;

        /// <summary>
        /// Instantiates a new instance of <see cref="AnalyticsManager"/> 
        /// </summary>
        /// <param name="platformInfoProvider"> The platform info provider to be used by this Analytics Manager. Can not be null.</param>
        public AnalyticsManager ( IPlatformInfoProvider platformInfoProvider) : base (platformInfoProvider )
        {
            _application = Application.Current; 
        }
        
        AnalyticsManager(Application application) : base(new PlatformInfoProvider())
        {
            this._application = application;
        }

        /// <summary>
        /// Shared, singleton instance of AnalyticsManager 
        /// </summary>
        public static AnalyticsManager Current
        {
            get
            {
                if (_current == null)
                {
                    _current = new AnalyticsManager(Application.Current);
                }
                return _current;
            }
        }

        /// <summary>
        /// True when the user has opted out of analytics, this disables all tracking activities.
        /// </summary>
        /// <remarks>See Google Analytics usage guidelines for more information.</remarks>
        public override bool AppOptOut
        {
            get
            {
                if (!_isAppOptOutSet) LoadAppOptOut();
                return base.AppOptOut;
            }
            set
            {
                base.AppOptOut = value;
                _isAppOptOutSet = true;
                ApplicationData.Current.LocalSettings.Values[KeyAppOptOut] = value;
                if (value) Clear();
            }
        }

        /// <summary>
        /// Enables (when set to true) automatic catching and tracking of Unhandled Exceptions.
        /// </summary>
        public bool ReportUncaughtExceptions
        {
            get
            {
                return _reportUncaughtExceptions;
            }
            set
            {
                if (_reportUncaughtExceptions != value)
                {
                    _reportUncaughtExceptions = value;
                    if (_reportUncaughtExceptions)
                    {
                        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
                        CoreApplication.UnhandledErrorDetected += CoreApplication_UnhandledErrorDetected;
                    }
                    else
                    {
                        TaskScheduler.UnobservedTaskException -= TaskScheduler_UnobservedTaskException;
                        CoreApplication.UnhandledErrorDetected -= CoreApplication_UnhandledErrorDetected;
                    }
                }
            }
        }
        /// <summary>
        /// Enables (when set to true) automatic dispatching of queued <see cref="Hit"/> on app Suspend, and automatic restart of dispatch timer upon resume.         
        /// </summary>
        /// <remarks>
        /// Default value is false, since we default to immediate dispatching. See <see cref="ServiceManager.DispatchPeriod"/>
        /// </remarks>
        public bool AutoAppLifetimeMonitoring
        {
            get
            {
                return _autoAppLifetimeMonitoring;
            }
            set
            {
                if (_autoAppLifetimeMonitoring != value)
                {
                    _autoAppLifetimeMonitoring = value;
                    if (_autoAppLifetimeMonitoring)
                    {
                        _application.Suspending += Application_Suspending;
                        _application.Resuming += Application_Resuming;
                    }
                    else
                    {
                        _application.Suspending -= Application_Suspending;
                        _application.Resuming -= Application_Resuming;
                    }
                }
            }
        }

        /// <summary>
        /// Enables (when set to true) listening to network connectivity events to have trackers behave accordingly to their connectivity status.
        /// </summary>
        public bool AutoTrackNetworkConnectivity
        {
            get
            {
                return _autoTrackNetworkConnectivity;
            }
            set
            {
                if (_autoTrackNetworkConnectivity != value)
                {
                    _autoTrackNetworkConnectivity = value;
                    if (_autoTrackNetworkConnectivity)
                    {
                        UpdateConnectionStatus();
                        NetworkInformation.NetworkStatusChanged += NetworkInformation_NetworkStatusChanged;
                    }
                    else
                    {
                        NetworkInformation.NetworkStatusChanged -= NetworkInformation_NetworkStatusChanged;
                        base.IsEnabled = true;
                    }
                }
            }
        }




        /// <summary>
        /// Creates a new Tracker using a given property ID. 
        /// </summary>
        /// <param name="propertyId">The property ID that the <see cref="Tracker"/> should log to.</param>
        /// <returns>The new or existing instance keyed on the property ID.</returns>
        public override Tracker CreateTracker(string propertyId)
        {
            var tracker = base.CreateTracker(propertyId);
            tracker.AppName = Package.Current.Id.Name;
            tracker.AppVersion = $"{Package.Current.Id.Version.Major}.{Package.Current.Id.Version.Minor}.{Package.Current.Id.Version.Build}.{Package.Current.Id.Version.Revision}";
            return tracker;
        }

        void LoadAppOptOut()
        {
            if (ApplicationData.Current.LocalSettings.Values.ContainsKey(KeyAppOptOut))
            {
                base.AppOptOut = (bool)ApplicationData.Current.LocalSettings.Values[KeyAppOptOut];
            }
            else
            {
                base.AppOptOut = false;
            }
            _isAppOptOutSet = true;
        }

        void NetworkInformation_NetworkStatusChanged(object sender)
        {
            UpdateConnectionStatus();
        }

        void UpdateConnectionStatus()
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile != null)
            {
                switch (profile.GetNetworkConnectivityLevel())
                {
                    case NetworkConnectivityLevel.InternetAccess:
                    case NetworkConnectivityLevel.ConstrainedInternetAccess:
                        IsEnabled = true;
                        break;
                    default:
                        IsEnabled = false;
                        break;
                }
            }
        }

        void Application_Resuming(object sender, object e)
        {
            Resume();
        }

        async void Application_Suspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            try
            {
                await SuspendAsync();
            }
            finally
            {
                deferral.Complete();
            }
        }

        void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            var ex = e.Exception.InnerException ?? e.Exception; // inner exception contains better info for unobserved tasks
            foreach (var tracker in Trackers)
            {
                tracker.Send(HitBuilder.CreateException(ex.ToString(), false).Build());
            }
        }

        void CoreApplication_UnhandledErrorDetected(object sender, UnhandledErrorDetectedEventArgs e)
        {
            try
            {
                e.UnhandledError.Propagate();
            }
            catch (Exception ex)
            {
                foreach (var tracker in Trackers)
                {
                    tracker.Send(HitBuilder.CreateException(ex.Message, true).Build());
                }
                var t = DispatchAsync();
                throw;
            }
        }
    }
}
