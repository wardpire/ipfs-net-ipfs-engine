using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using Ipfs.Core;
using System.IO;

namespace Ipfs.Engine
{
    public class InMemoryStore<TName, TValue> : IStore<TName, TValue> where TValue : class
    {
        private Dictionary<TName, TValue> _values = new();

        public IEnumerable<TValue> Values => _values.Values;

        public IEnumerable<TName> Keys => throw new System.NotImplementedException();

        public Task<bool> ExistsAsync(TName name, CancellationToken cancel = default)
        {
            return Task.FromResult(_values.ContainsKey(name));
        }

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

        public Task<ulong?> SizeOfAsync(TName name, CancellationToken cancel = default)
        {
            if (_values.ContainsKey(name))
            {
                unsafe
                {
                    return Task.FromResult<ulong?>((ulong?)sizeof(TValue));
                }
            }
            else
                return Task.FromResult<ulong?>(default);
        }

        public Task<TValue> TryGetAsync(TName name, CancellationToken cancel = default)
        {
            _values.TryGetValue(name, out var value);
            return Task.FromResult(value);
        }
    }
}