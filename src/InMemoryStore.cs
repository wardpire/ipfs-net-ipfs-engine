using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;

namespace Ipfs.Engine
{
    public class InMemoryStore<TName, TValue> : IStore<TName, TValue> where TValue : class
    {
        private Dictionary<TName, TValue> _values = new();

        public IEnumerable<TValue> Values => _values.Values;

        public Task<TValue> GetAsync(TName name, CancellationToken cancel = default) => Task.FromResult(_values[name]);

        public Task PutAsync(TName name, TValue value, CancellationToken cancel = default)
        {
            if (_values.ContainsKey(name))
                _values[name] = value;
            else
                _values.Add(name, value);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(TName name, CancellationToken cancel = default)
        {
            _values.Remove(name);
            return Task.CompletedTask;
        }

        public Task<TValue> TryGetAsync(TName name, CancellationToken cancel = default)
        {
            _values.TryGetValue(name, out var value);
            return Task.FromResult(value);
        }
    }
}