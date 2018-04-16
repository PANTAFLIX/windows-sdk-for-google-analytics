using System;
using System.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Navigation;

namespace GoogleAnalytics.Sample
{
    public sealed partial class MainPage
    {

        private bool _isRunningInDebugMode; 
             
        public MainPage()
        {
            InitializeComponent();
            if (IsDebugRequest.IsChecked != null) _isRunningInDebugMode = IsDebugRequest.IsChecked.Value;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            App.Tracker.ScreenName = "Main";

            // wire up to event for debugging purposes:
            AnalyticsManager.Current.HitSent += AnalyticsManager_HitSent;
            AnalyticsManager.Current.HitMalformed += AnalyticsManager_HitMalformed;
            AnalyticsManager.Current.HitFailed += AnalyticsManager_HitFailed;

        }

        private void AnalyticsManager_HitFailed(object sender, HitFailedEventArgs e)
        {
            Log(e.Hit, $"**Hit Failed** {Environment.NewLine} {e.Error.Message}");

        }

        private void AnalyticsManager_HitMalformed(object sender, HitMalformedEventArgs e)
        {
            Log(e.Hit, $"**Hit Malformed ** {Environment.NewLine} {e.HttpStatusCode}");
        }

        private void AnalyticsManager_HitSent(object sender, HitSentEventArgs e)
        {
            Log(e.Hit, e.Response);
        }

        private void ButtonException_Click(object sender, RoutedEventArgs e)
        {
            App.Tracker.Send(HitBuilder.CreateException("oops, something went wrong", false).Build());
        }

        private void ButtonEvent_Click(object sender, RoutedEventArgs e)
        {
            App.Tracker.Send(HitBuilder.CreateCustomEvent("testevent", "userId=7801").Build());
        }

        private void ButtonView_Click(object sender, RoutedEventArgs e)
        {
            App.Tracker.Send(HitBuilder.CreateScreenView("mainWindow").Build());
        }

        private void ButtonSocial_Click(object sender, RoutedEventArgs e)
        {
            App.Tracker.Send(HitBuilder.CreateSocialInteraction("facebook", "share", "http://googleanalyticssdk.codeplex.com").Build());
        }

        private void ButtonTiming_Click(object sender, RoutedEventArgs e)
        {
            App.Tracker.Send(HitBuilder.CreateTiming("someaction", "loadtime", TimeSpan.FromSeconds(2)).Build());
        }

        private void ButtonThrowException_Click(object sender, RoutedEventArgs e)
        {
            object y = 1;
            var x = (string)y;
        }

        private void IsDebugRequest_Checked(object sender, RoutedEventArgs e)
        {
            if (IsDebugRequest.IsChecked != null) _isRunningInDebugMode = IsDebugRequest.IsChecked.Value;
            var visibility = _isRunningInDebugMode  ? Visibility.Visible : Visibility.Collapsed;
            RequestPanel.Visibility = visibility;
            ResponsePanel.Visibility = visibility;

        }


        private async void Log(Hit hit, string message)
        {
            if (_isRunningInDebugMode)
            {
                if ( AnalyticsManager.Current.DispatchPeriod > TimeSpan.Zero )
                {
                    message = "Using dispatcher-which batches activity. Here is the last hit sent\n" + message ; 
                }

                if (Dispatcher.HasThreadAccess)
                {
                    Results.Text = message;
                    Request.Text = Parse(hit);
                }
                else
                {
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        Results.Text = message;
                        Request.Text = Parse(hit);
                    });
                } 
                 
            } 
            //Output to console regardless 
            System.Diagnostics.Debug.WriteLine(message);
        }

        private static string Parse(Hit hit)
        {
            var builder = new StringBuilder();
            if (hit == null) return builder.ToString();
            foreach (var param in hit.Data.Keys)
            {
                builder.Append($"{param}:{hit.Data[param]}{Environment.NewLine}");
            }
            return builder.ToString();
        }
    }
}
