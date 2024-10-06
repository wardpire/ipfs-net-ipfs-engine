using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using Ipfs.Core;
using System.IO;

namespace Ipfs.Engine
{
    public class InMemoryStore<TName, TValue> : IStore<TName, TValue> where TValue : class
    {
        private string _namespace;
        private Dictionary<string, Dictionary<TName, TValue>> _values = new();

        public InMemoryStore(string? @namespace = default)
        {
            SetNamespace(@namespace);
        }

        public IEnumerable<TValue> Values => _values[_namespace].Values;

        public IEnumerable<TName> Keys => throw new System.NotImplementedException();

        public Task<bool> ExistsAsync(TName name, CancellationToken cancel = default)
        {
            return Task.FromResult(_values[_namespace].ContainsKey(name));
        }

        public Task<TValue> GetAsync(TName name, CancellationToken cancel = default) => Task.FromResult(_values[_namespace][name]);

        public Task PutAsync(TName name, TValue value, CancellationToken cancel = default)
        {
            if (_values[_namespace].ContainsKey(name))
                _values[_namespace][name] = value;
            else
                _values[_namespace].Add(name, value);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(TName name, CancellationToken cancel = default)
        {
            _values[_namespace].Remove(name);
            return Task.CompletedTask;
        }

        public void SetNamespace(string? ns)
        {
            _namespace = ns ?? string.Empty;
            if (!_values.ContainsKey(_namespace))
                _values.Add(_namespace, new Dictionary<TName, TValue>());
        }

        public Task<ulong?> SizeOfAsync(TName name, CancellationToken cancel = default)
        {
            if (_values[_namespace].ContainsKey(name))
            {
                unsafe
                {
                    return Task.FromResult<ulong?>((ulong?)sizeof(TValue));
                }
            }
            else
                return Task.FromResult<ulong?>(default);
        }

        public Task<TValue?> TryGetAsync(TName name, CancellationToken cancel = default)
        {
            _values[_namespace].TryGetValue(name, out var value);
            return Task.FromResult(value);
        }
    }
}