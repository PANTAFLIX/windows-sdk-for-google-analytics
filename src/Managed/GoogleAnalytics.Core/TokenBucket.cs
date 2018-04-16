using System;

namespace GoogleAnalytics
{
    internal class TokenBucket
    {
        private readonly object _locker = new object();

        private double _capacity;
        private double _tokens;
        private double _fillRate;
        private DateTime _timeStamp;

        public TokenBucket(double tokens, double fillRate)
        {
            _capacity = tokens;
            _tokens = tokens;
            _fillRate = fillRate;
            _timeStamp = DateTime.UtcNow;
        }

        public bool Consume(double tokens = 1.0)
        {
            lock (_locker) // make thread safe
            {
                if (!(GetTokens() - tokens > 0)) return false;
                _tokens -= tokens;
                return true;
            }
        }

        private double GetTokens()
        {
            var now = DateTime.UtcNow;
            if (!(_tokens < _capacity)) return _tokens;
            var delta = _fillRate * (now - _timeStamp).TotalSeconds;
            _tokens = Math.Min(_capacity, _tokens + delta);
            _timeStamp = now;
            return _tokens;
        }
    }
}
