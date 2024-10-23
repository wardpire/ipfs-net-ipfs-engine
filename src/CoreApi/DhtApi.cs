using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Ipfs.CoreApi;

namespace Ipfs.Engine.CoreApi
{
    internal class DhtApi : IDhtApi
    {
        private readonly IpfsEngine ipfs;

        public DhtApi(IpfsEngine ipfs)
        {
            this.ipfs = ipfs;
        }

        public async Task<Peer?> FindPeerAsync(MultiHash id, CancellationToken cancel = default)
        {
            var dht = await ipfs.DhtService.ConfigureAwait(false);
            return await dht.FindPeerAsync(id, cancel).ConfigureAwait(false);
        }

        public async IAsyncEnumerable<Peer> FindProvidersAsync(Cid id, int limit = 21, [EnumeratorCancellation]CancellationToken cancel = default)
        {
            var dht = await ipfs.DhtService.ConfigureAwait(false);
            await foreach (var provider in dht.FindProvidersAsync(id, limit, cancel).ConfigureAwait(false).WithCancellation(cancel))
                yield return provider;
        }

        public async Task ProvideAsync(Cid cid, bool advertise = true, CancellationToken cancel = default)
        {
            var dht = await ipfs.DhtService.ConfigureAwait(false);
            await dht.ProvideAsync(cid, advertise, cancel).ConfigureAwait(false);
        }

        public async Task<IEnumerable<byte[]>> FindSimilarValuesAsync(string @namespace, MultiHash key, CancellationToken cancel = default)
        {
            var dht = await ipfs.DhtService.ConfigureAwait(false);
            return await dht.FindSimilarValuesAsync(@namespace, key, cancel);
        }

        public async Task<string?> TryGetValueStringAsync(string @namespace, MultiHash key, CancellationToken cancel = default)
        {
            var dht = await ipfs.DhtService.ConfigureAwait(false);
            return await dht.TryGetValueStringAsync(@namespace, key, cancel);
        }

        public async Task<byte[]?> TryGetValueAsync(string @namespace, MultiHash key, CancellationToken cancel = default)
        {
            var dht = await ipfs.DhtService.ConfigureAwait(false);
            return await dht.TryGetValueAsync(@namespace, key, cancel);
        }

        public async Task PutValueAsync(string @namespace, MultiHash key, string UTF8Value, CancellationToken cancellationToken = default)
        {
            var dht = await ipfs.DhtService.ConfigureAwait(false);
            await dht.PutValueAsync(@namespace, key, UTF8Value, cancellationToken);
        }

        public async Task PutValueAsync(string @namespace, MultiHash key, byte[] value, CancellationToken cancellationToken = default)
        {
            var dht = await ipfs.DhtService.ConfigureAwait(false);
            await dht.PutValueAsync(@namespace, key, value, cancellationToken);
        }
    }
}