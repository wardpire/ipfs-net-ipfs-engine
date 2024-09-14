using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ipfs.CoreApi;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Collections.Concurrent;
using PeerTalk;
using System.Globalization;
using Ipfs.Core;

namespace Ipfs.Engine.CoreApi
{
    internal class BlockRepositoryApi : IBlockRepositoryApi
    {
        private readonly IpfsEngine ipfs;

        public BlockRepositoryApi(IpfsEngine ipfs)
        {
            this.ipfs = ipfs;
        }

        public async Task RemoveGarbageAsync(CancellationToken cancel = default)
        {
            var blockApi = (BlockApi)ipfs.Block;
            var pinApi = (PinApi)ipfs.Pin;
            foreach (var cid in blockApi.Store.Keys)
            {
                if (!await pinApi.IsPinnedAsync(cid, cancel).ConfigureAwait(false))
                {
                    await ipfs.Block.RemoveAsync(cid, ignoreNonexistent: true, cancel: cancel).ConfigureAwait(false);
                }
            }
        }

        public async Task<RepositoryData> StatisticsAsync(CancellationToken cancel = default)
        {
            ulong maxStorage = 10000000000;
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
            {
                DriveInfo[] allDrives = DriveInfo.GetDrives();
                var totalSize = allDrives.Sum(d => d.AvailableFreeSpace); // since most devices are single drive
                maxStorage = Convert.ToUInt64(totalSize * 0.45); // use 45% of total storage
            }

            var data = new RepositoryData
            {
                RepoPath = Path.GetFullPath(ipfs.Options.Repository.Folder),
                Version = await VersionAsync(cancel).ConfigureAwait(false),
                StorageMax = maxStorage
            };

            var blockApi = (BlockApi)ipfs.Block;
            GetDirStatsAsync(blockApi.Store, data, cancel);

            return data;
        }

        public Task VerifyAsync(CancellationToken cancel = default)
        {
            throw new NotImplementedException();
        }

        public Task<string> VersionAsync(CancellationToken cancel = default)
        {
            return Task.FromResult(ipfs.MigrationManager
                .CurrentVersion
                .ToString(CultureInfo.InvariantCulture));
        }

        private async Task GetDirStatsAsync(IStore<Cid, DataBlock> store, RepositoryData data, CancellationToken cancel)
        {
            foreach (var item in store.Keys)
            {
                cancel.ThrowIfCancellationRequested();
                ++data.NumObjects;
                data.RepoSize += (await store.SizeOfAsync(item,cancel))??0;
            }
        }
    }
}