﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GoogleAnalytics
{
    /// <inheritdoc />
    /// <summary>
    ///     Implements a service manager used to send <see cref="T:GoogleAnalytics.Hit" />s to Google Analytics.
    /// </summary>
    public class ServiceManager : IServiceManager
    {
        private static Random _random;
        private static readonly Uri EndPointUnsecureDebug = new Uri("http://www.google-analytics.com/debug/collect");
        private static readonly Uri EndPointSecureDebug = new Uri("https://ssl.google-analytics.com/debug/collect");
        private static readonly Uri EndPointUnsecure = new Uri("http://www.google-analytics.com/collect");
        private static readonly Uri EndPointSecure = new Uri("https://ssl.google-analytics.com/collect");
        private readonly IList<Task> _dispatchingTasks;

        private readonly Queue<Hit> _hits;
        private readonly TokenBucket _hitTokenBucket;
        private TimeSpan _dispatchPeriod;
        private bool _isEnabled = true;

        private Timer _timer;

        /// <summary>
        ///     Instantiates a new instance of <see cref="ServiceManager" />.
        /// </summary>
        public ServiceManager()
        {
            PostData = true;
            _dispatchingTasks = new List<Task>();
            _hits = new Queue<Hit>();
            DispatchPeriod = TimeSpan.Zero;
            IsSecure = true;
            _hitTokenBucket = new TokenBucket(60, .5);
        }

        /// <summary>
        ///     Gets or sets whether <see cref="Hit" />s should be sent via SSL. Default is true.
        /// </summary>
        public bool IsSecure { get; set; }

        /// <summary>
        ///     Gets or sets whether <see cref="Hit" />s should be sent to the debug endpoint. Default is false.
        /// </summary>
        public bool IsDebug { get; set; }

        /// <summary>
        ///     Gets or sets whether throttling should be used. Default is false.
        /// </summary>
        public bool ThrottlingEnabled { get; set; }

        /// <summary>
        ///     Gets or sets whether data should be sent via POST or GET method. Default is POST.
        /// </summary>
        public bool PostData { get; set; }

        /// <summary>
        ///     Gets or sets whether a cache buster should be applied to all requests. Default is false.
        /// </summary>
        public bool BustCache { get; set; }

        /// <summary>
        ///     Gets or sets the user agent request header used by Google Analytics to determine the platform and device generating
        ///     the hits.
        /// </summary>
        public string UserAgent { get; set; }

        /// <summary>
        ///     Gets or sets the frequency at which hits should be sent to the service. Default is immediate.
        /// </summary>
        /// <remarks>Setting to TimeSpan.Zero will cause the hit to get sent immediately.</remarks>
        public TimeSpan DispatchPeriod
        {
            get => _dispatchPeriod;
            set
            {
                if (_dispatchPeriod == value) return;
                _dispatchPeriod = value;
                if (_timer != null)
                {
                    _timer.Dispose();
                    _timer = null;
                }

                if (_dispatchPeriod > TimeSpan.Zero)
                    _timer = new Timer(Timer_Tick, null, DispatchPeriod, DispatchPeriod);
            }
        }

        /// <summary>
        ///     Gets or sets whether the dispatcher is enabled. If disabled, hits will be queued but not dispatched.
        /// </summary>
        /// <remarks>Typically this is used to indicate whether or not the network is available.</remarks>
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled == value) return;
                _isEnabled = value;
                if (!_isEnabled) return;
                if (DispatchPeriod >= TimeSpan.Zero)
                {
                    var nowait = DispatchAsync();
                }
            }
        }

        /// <inheritdoc />
        public virtual void EnqueueHit(IDictionary<string, string> @params)
        {
            var hit = new Hit(@params);
            if (DispatchPeriod == TimeSpan.Zero && IsEnabled)
            {
                var t = RunDispatchingTask(DispatchImmediateHit(hit));
            }
            else
            {
                lock (_hits)
                {
                    _hits.Enqueue(hit);
                }
            }
        }

        /// <summary>
        ///     Provides notification that a <see cref="Hit" /> has been been successfully sent.
        /// </summary>
        public event EventHandler<HitSentEventArgs> HitSent;

        /// <summary>
        ///     Provides notification that a <see cref="Hit" /> failed to send.
        /// </summary>
        /// <remarks>Failed <see cref="Hit" />s will be added to the queue in order to reattempt at the next dispatch time.</remarks>
        public event EventHandler<HitFailedEventArgs> HitFailed;

        /// <summary>
        ///     Provides notification that a <see cref="Hit" /> was malformed and rejected by Google Analytics.
        /// </summary>
        public event EventHandler<HitMalformedEventArgs> HitMalformed;

        /// <summary>
        ///     Empties the queue of <see cref="Hit" />s waiting to be dispatched.
        /// </summary>
        /// <remarks>If a <see cref="Hit" /> is actively beeing sent, this will not abort the request.</remarks>
        public void Clear()
        {
            lock (_hits)
            {
                _hits.Clear();
            }
        }

        /// <summary>
        ///     Dispatches all hits in the queue.
        /// </summary>
        /// <returns>Returns once all items that were in the queue at the time the method was called have finished being sent.</returns>
        public async Task DispatchAsync()
        {
            if (!_isEnabled) return;

            Task allDispatchingTasks = null;
            lock (_dispatchingTasks)
            {
                if (_dispatchingTasks.Any()) allDispatchingTasks = Task.WhenAll(_dispatchingTasks);
            }

            if (allDispatchingTasks != null) await allDispatchingTasks;

            if (!_isEnabled) return;

            Hit[] hitsToSend;
            lock (_hits)
            {
                hitsToSend = _hits.ToArray();
            }

            if (hitsToSend.Any()) await RunDispatchingTask(DispatchQueuedHits(hitsToSend));
        }

        /// <summary>
        ///     Suspends operations and flushes the queue.
        /// </summary>
        /// <remarks>Call <see cref="Resume" /> when returning from a suspended state to resume operations.</remarks>
        /// <returns>Operation returns when all <see cref="Hit" />s have been flushed.</returns>
        public async Task SuspendAsync()
        {
            await DispatchAsync(); // flush all pending hits in the queue

            // shut down the timer if enabled
            if (_timer != null)
            {
                _timer.Dispose();
                _timer = null;
            }
        }

        /// <summary>
        ///     Resumes operations after <see cref="SuspendAsync" /> is called.
        /// </summary>
        public void Resume()
        {
            // restore the timer if appropriate.
            if (_dispatchPeriod > TimeSpan.Zero) _timer = new Timer(Timer_Tick, null, DispatchPeriod, DispatchPeriod);
        }

        private async void Timer_Tick(object sender)
        {
            await DispatchAsync();
        }

        private async Task RunDispatchingTask(Task newDispatchingTask)
        {
            lock (_dispatchingTasks)
            {
                _dispatchingTasks.Add(newDispatchingTask);
            }

            try
            {
                await newDispatchingTask;
            }
            finally
            {
                lock (_dispatchingTasks)
                {
                    _dispatchingTasks.Remove(newDispatchingTask);
                }
            }
        }

        private async Task DispatchQueuedHits(IEnumerable<Hit> hits)
        {
            using (var httpClient = GetHttpClient())
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var hit in hits)
                    if (_isEnabled && (!ThrottlingEnabled || _hitTokenBucket.Consume()))
                    {
                        // clone the data
                        var hitData = hit.Data.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                        hitData.Add("qt", ((long) now.Subtract(hit.TimeStamp).TotalMilliseconds).ToString());
                        await DispatchHitData(hit, httpClient, hitData);
                    }
                    else
                    {
                        lock (hits) // add back to queue
                        {
                            _hits.Enqueue(hit);
                        }
                    }
            }
        }

        private async Task DispatchImmediateHit(Hit hit)
        {
            using (var httpClient = GetHttpClient())
            {
                // clone the data
                var hitData = hit.Data.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                await DispatchHitData(hit, httpClient, hitData);
            }
        }

        private async Task DispatchHitData(Hit hit, HttpClient httpClient, IDictionary<string, string> hitData)
        {
            if (BustCache) hitData.Add("z", GetCacheBuster());
            try
            {
                using (var response = await SendHitAsync(hit, httpClient, hitData))
                {
                    try
                    {
                        response.EnsureSuccessStatusCode();
                        await OnHitSentAsync(hit, response);
                    }
                    catch // If you do not get a 2xx status code, you should NOT retry the request. Instead, you should stop and correct any errors in your HTTP request.
                    {
                        OnHitMalformed(hit, response);
                    }
                }
            }
            catch (Exception ex)
            {
                OnHitFailed(hit, ex);
            }
        }

        private async Task<HttpResponseMessage> SendHitAsync(Hit hit, HttpClient httpClient,
            IDictionary<string, string> hitData)
        {
            var endPoint = IsDebug
                ? (IsSecure ? EndPointSecureDebug : EndPointUnsecureDebug)
                : (IsSecure ? EndPointSecure : EndPointUnsecure);
            if (!PostData) return await httpClient.GetAsync(endPoint + "?" + GetUrlEncodedString(hitData));

            using (var content = GetEncodedContent(hitData))
            {
                return await httpClient.PostAsync(endPoint, content);
            }
        }

        private void OnHitMalformed(Hit hit, HttpResponseMessage response)
        {
            HitMalformed?.Invoke(this, new HitMalformedEventArgs(hit, (int) response.StatusCode));
        }

        private void OnHitFailed(Hit hit, Exception exception)
        {
            HitFailed?.Invoke(this, new HitFailedEventArgs(hit, exception));
        }

        private async Task OnHitSentAsync(Hit hit, HttpResponseMessage response)
        {
            HitSent?.Invoke(this, new HitSentEventArgs(hit, await response.Content.ReadAsStringAsync()));
        }

        private HttpClient GetHttpClient()
        {
            var result = new HttpClient();
            if (!string.IsNullOrEmpty(UserAgent)) result.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            return result;
        }

        private static string GetCacheBuster()
        {
            if (_random == null) _random = new Random();
            return _random.Next().ToString();
        }

        private static ByteArrayContent GetEncodedContent(IEnumerable<KeyValuePair<string, string>> nameValueCollection)
        {
            return new StringContent(GetUrlEncodedString(nameValueCollection));
        }

        private static string GetUrlEncodedString(IEnumerable<KeyValuePair<string, string>> nameValueCollection)
        {
            const int maxUriStringSize = 65519;

            return string.Join("&", nameValueCollection
                .Where(item => item.Value != null)
                .Select(item => item.Key + "=" + Uri.EscapeDataString(item.Value.Length > maxUriStringSize
                                    ? item.Value.Substring(0, maxUriStringSize)
                                    : item.Value)));
        }
    }

    /// <summary>
    ///     Supplies additional information when <see cref="Hit" />s fail to send.
    /// </summary>
    public sealed class HitFailedEventArgs : EventArgs
    {
        internal HitFailedEventArgs(Hit hit, Exception error)
        {
            Error = error;
            Hit = hit;
        }

        /// <summary>
        ///     Gets the <see cref="Exception" /> thrown when the failure occurred.
        /// </summary>
        public Exception Error { get; }

        /// <summary>
        ///     Gets the <see cref="Hit" /> associated with the event.
        /// </summary>
        public Hit Hit { get; }
    }

    /// <summary>
    ///     Supplies additional information when <see cref="Hit" />s are successfully sent.
    /// </summary>
    public sealed class HitSentEventArgs : EventArgs
    {
        internal HitSentEventArgs(Hit hit, string response)
        {
            Response = response;
            Hit = hit;
        }

        /// <summary>
        ///     Gets the response text.
        /// </summary>
        public string Response { get; }

        /// <summary>
        ///     Gets the <see cref="Hit" /> associated with the event.
        /// </summary>
        public Hit Hit { get; }
    }

    /// <summary>
    ///     Supplies additional information when <see cref="Hit" />s are malformed and cannot be sent.
    /// </summary>
    public sealed class HitMalformedEventArgs : EventArgs
    {
        internal HitMalformedEventArgs(Hit hit, int httpStatusCode)
        {
            HttpStatusCode = httpStatusCode;
            Hit = hit;
        }

        /// <summary>
        ///     Gets the HTTP status code that may provide more information about the problem.
        /// </summary>
        public int HttpStatusCode { get; }

        /// <summary>
        ///     Gets the <see cref="Hit" /> associated with the event.
        /// </summary>
        public Hit Hit { get; }
    }
}