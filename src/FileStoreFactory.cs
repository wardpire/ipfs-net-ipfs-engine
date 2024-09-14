using Ipfs.Core;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Ipfs.Engine
{
	public class FileStoreFactory : IStoreFactory
	{
		public IStore<TName, TValue> CreateStore<TName, TValue>(
			string @namespace,
			Func<TName, string>? nameToKey = null,
			Func<string, TName>? keyToName = null,
			Func<Stream, TName, TValue, CancellationToken, Task>? Serialize = null,
			Func<Stream, TName, CancellationToken,
			Task<TValue>>? deserialize = null
			) where TValue : class
		{
			return new FileStore<TName, TValue>()
			{
				Folder = @namespace,
				KeyToFileName = nameToKey,
				FileNameToKey = keyToName,
				Serialize = Serialize,
				Deserialize = deserialize
			};
		}
	}
}