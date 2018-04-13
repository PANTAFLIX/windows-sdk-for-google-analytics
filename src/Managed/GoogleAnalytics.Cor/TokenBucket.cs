using System;

namespace GoogleAnalytics
{
    internal class TokenBucket
    {
        readonly object _locker = new object();

        double _capacity = 0;
        double _tokens = 0;
        double _fillRate = 0;
        DateTime _timeStamp;

        public TokenBucket(double tokens, double fillRate)
        {
            this._capacity = tokens;
            this._tokens = tokens;
            this._fillRate = fillRate;
            this._timeStamp = DateTime.UtcNow;
        }

        public bool Consume(double tokens = 1.0)
        {
            lock (_locker) // make thread safe
            {
                if (GetTokens() - tokens > 0)
                {
                    this._tokens -= tokens;
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        double GetTokens()
        {
            var now = DateTime.UtcNow;
            if (_tokens < _capacity)
            {
                var delta = _fillRate * (now - _timeStamp).TotalSeconds;
                _tokens = Math.Min(_capacity, _tokens + delta);
                _timeStamp = now;
            }
            return _tokens;
        }
    }
}
