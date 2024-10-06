using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ipfs.Core;
using Newtonsoft.Json;
using Nito.AsyncEx;

namespace Ipfs.Engine
{
    /// <summary>
    ///   A file based repository for name value pairs.
    /// </summary>
    /// <typeparam name="TKey">
    ///   The type used for a unique name.
    /// </typeparam>
    /// <typeparam name="TValue">
    ///   The type used for the value.
    /// </typeparam>
    /// <remarks>
    ///   All operations are atomic, a reader/writer lock is used.
    /// </remarks>
    public class FileStore<TKey, TValue> : IStore<TKey, TValue> where TValue : class
    {
        public enum InitSerialize
        {
            Json,
            Protobuf
        }

        private readonly AsyncReaderWriterLock storeLock = new();
        private readonly string _baseFolder;
        private string _folder = default!;

        public FileStore(IpfsEngineOptions options, string? @namespace) :
            this(options.Repository.Folder, @namespace)
        {
        }

        public FileStore(IpfsEngineOptions options, string? @namespace, InitSerialize initSerialize) :
            this(options.Repository.Folder, @namespace, initSerialize)
        {
        }

        public FileStore(string baseFolder, string? @namespace, InitSerialize initSerialize) :
            this(baseFolder, @namespace)
        {
            switch (initSerialize)
            {
                case InitSerialize.Json:
                    Serialize = JsonSerialize;
                    Deserialize = JsonDeserialize;
                    break;
                case InitSerialize.Protobuf:
                    Serialize = ProtobufSerialize;
                    Deserialize = ProtobufDeserialize;
                    break;
                default:
                    throw new InvalidOperationException("Unknwon InitSerialize");
            }
        }

        public FileStore(string baseFolder, string? @namespace)
        {
            _baseFolder = baseFolder;
            SetNamespace(@namespace);
            Serialize = JsonSerialize;
            Deserialize = JsonDeserialize;
        }

        /// <summary>
        ///   A function to write the Protobuf encoded entity to the stream.
        /// </summary>
        /// <remarks>
        ///   This is the default <see cref="Serialize"/>.
        /// </remarks>
        public static Func<Stream, TKey, TValue, CancellationToken, Task> ProtobufSerialize =
            async (stream, key, value, cancel) => ProtoBuf.Serializer.Serialize<TValue>(stream, value);

        /// <summary>
        ///  A function to read the Protobuf encoded entity from the stream.
        /// </summary>
        /// <remarks>
        ///   This is the default <see cref="Deserialize"/>.
        /// </remarks>
        public static Func<Stream, TKey, CancellationToken, Task<TValue>> ProtobufDeserialize =
            (stream, key, cancel) => Task.FromResult(ProtoBuf.Serializer.Deserialize<TValue>(stream));

        /// <summary>
        ///   A function to write the JSON encoded entity to the stream.
        /// </summary>
        /// <remarks>
        ///   This is the default <see cref="Serialize"/>.
        /// </remarks>
        public static Func<Stream, TKey, TValue, CancellationToken, Task> JsonSerialize =
            (stream, _, value, __) =>
            {
                using var writer = new StreamWriter(stream);
                using var jtw = new JsonTextWriter(writer) { Formatting = Formatting.Indented };
                var ser = new JsonSerializer();
                ser.Serialize(jtw, value);
                jtw.Flush();
                return Task.CompletedTask;
            };

        /// <summary>
        ///  A function to read the JSON encoded entity from the stream.
        /// </summary>
        /// <remarks>
        ///   This is the default <see cref="Deserialize"/>.
        /// </remarks>
        public static Func<Stream, TKey, CancellationToken, Task<TValue>> JsonDeserialize =
            (stream, _, __) =>
            {
                using var reader = new StreamReader(stream);
                using var jtr = new JsonTextReader(reader);
                var ser = new JsonSerializer();
                return Task.FromResult(ser.Deserialize<TValue>(jtr));
            };

        /// <summary>
        ///   A function that converts the name to a case insensitive key name.
        /// </summary>
        public required Func<TKey, string> KeyToFileName { get; set; }

        /// <summary>
        ///   A function that converts the case insensitive key to a name.
        /// </summary>
        public required Func<string, TKey> FileNameToKey { get; set; }

        /// <summary>
        ///   Sends the value to the stream.
        /// </summary>
        /// <value>
        ///   Defaults to using <see cref="JsonSerialize"/>.
        /// </value>
        public Func<Stream, TKey, TValue, CancellationToken, Task> Serialize
        { get; set; }

        /// <summary>
        ///   Retrieves the value from the stream.
        /// </summary>
        /// <value>
        ///   Defaults to using <see cref="JsonDeserialize"/>
        /// </value>
        public Func<Stream, TKey, CancellationToken, Task<TValue>> Deserialize
        { get; set; }

        /// <summary>
        ///   Try to get the value with the specified name.
        /// </summary>
        /// <param name="name">
        ///   The unique name of the entity.
        /// </param>
        /// <param name="cancel">
        ///   Is used to stop the task.  When cancelled, the <see cref="TaskCanceledException"/> is raised.
        /// </param>
        /// <returns>
        ///   A task that represents the asynchronous operation. The task's result is
        ///   a <typeparamref name="TValue"/> or <b>null</b> if the <paramref name="name"/>
        ///   does not exist.
        /// </returns>
        public async Task<TValue?> TryGetAsync(TKey name, CancellationToken cancel = default)
        {
            var path = GetPath(name);
            using (await storeLock.ReaderLockAsync().ConfigureAwait(false))
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                using (var content = File.OpenRead(path))
                {
                    return await Deserialize(content, name, cancel).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        ///   Get the value with the specified name.
        /// </summary>
        /// <param name="name">
        ///   The unique name of the entity.
        /// </param>
        /// <param name="cancel">
        ///   Is used to stop the task.  When cancelled, the <see cref="TaskCanceledException"/> is raised.
        /// </param>
        /// <returns>
        ///   A task that represents the asynchronous operation. The task's result is
        ///   a <typeparamref name="TValue"/>
        /// </returns>
        /// <exception cref="KeyNotFoundException">
        ///   When the <paramref name="name"/> does not exist.
        /// </exception>
        public async Task<TValue> GetAsync(TKey name, CancellationToken cancel = default)
        {
            var value = await TryGetAsync(name, cancel).ConfigureAwait(false);
            if (value == null)
                throw new KeyNotFoundException($"Missing '{name}'.");

            return value;
        }

        /// <summary>
        ///   Put the value with the specified name.
        /// </summary>
        /// <param name="name">
        ///   The unique name of the entity.
        /// </param>
        /// <param name="value">
        ///   The entity.
        /// </param>
        /// <param name="cancel">
        ///   Is used to stop the task.  When cancelled, the <see cref="TaskCanceledException"/> is raised.
        /// </param>
        /// <returns>
        ///   A task that represents the asynchronous operation.
        /// </returns>
        /// <remarks>
        ///   If <paramref name="name"/> already exists, it's value is overwriten.
        ///   <para>
        ///   The file is deleted if an exception is encountered.
        ///   </para>
        /// </remarks>
        public async Task PutAsync(TKey name, TValue value, CancellationToken cancel = default)
        {
            var path = GetPath(name);

            using (await storeLock.WriterLockAsync(cancel).ConfigureAwait(false))
            using (var stream = File.Create(path))
            {
                try
                {
                    await Serialize(stream, name, value, cancel).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    try
                    {
                        await stream.DisposeAsync();
                        File.Delete(path);
                    }
                    catch
                    {
                        // eat it.
                    }
                    throw;  // original exception
                }
            }
        }

        /// <summary>
        ///   Remove the value with the specified name.
        /// </summary>
        /// <param name="name">
        ///   The unique name of the entity.
        /// </param>
        /// <param name="cancel">
        ///   Is used to stop the task.  When cancelled, the <see cref="TaskCanceledException"/> is raised.
        /// </param>
        /// <returns>
        ///   A task that represents the asynchronous operation.
        /// </returns>
        /// <remarks>
        ///   A non-existent <paramref name="name"/> does nothing.
        /// </remarks>
        public async Task RemoveAsync(TKey name, CancellationToken cancel = default)
        {
            var path = GetPath(name);
            using (await storeLock.WriterLockAsync(cancel).ConfigureAwait(false))
            {
                File.Delete(path);
            }
        }

        /// <summary>
        ///   Determines if the name exists.
        /// </summary>
        /// <param name="name">
        ///   The unique name of the entity.
        /// </param>
        /// <param name="cancel">
        ///   Is used to stop the task.  When cancelled, the <see cref="TaskCanceledException"/> is raised.
        /// </param>
        /// <returns>
        ///   A task that represents the asynchronous operation. The task's result is
        ///   <b>true</b> if the <paramref name="name"/> exists.
        /// </returns>
        public async Task<bool> ExistsAsync(TKey name, CancellationToken cancel = default)
        {
            var path = GetPath(name);
            using (await storeLock.ReaderLockAsync(cancel).ConfigureAwait(false))
            {
                return File.Exists(path);
            }
        }

        /// <summary>
        ///   Gets keys in the file store.
        /// </summary>
        /// <value>
        ///   A sequence of <typeparamref name="TKey"/>.
        /// </value>
        public IEnumerable<TKey> Keys
        {
            get
            {
                return Directory
                    .EnumerateFiles(_folder)
                    .Select(path => FileNameToKey(Path.GetFileName(path)));
            }
        }

        /// <summary>
        ///   Gets the values in the file store.
        /// </summary>
        /// <value>
        ///   A sequence of <typeparamref name="TValue"/>.
        /// </value>
        public IEnumerable<TValue> Values
        {
            get
            {
                return Directory
                    .EnumerateFiles(_folder)
                    .Select(path =>
                    {
                        using (var content = File.OpenRead(path))
                        {
                            var name = FileNameToKey(Path.GetFileName(path));
                            return Deserialize(content, name, CancellationToken.None)
                                .ConfigureAwait(false)
                                .GetAwaiter()
                                .GetResult();
                        }
                    });
            }
        }

        /// <summary>
        ///   Gets the names in the file store.
        /// </summary>
        /// <value>
        ///   A sequence of <typeparamref name="TKey"/>.
        /// </value>
        public IEnumerable<TKey> Names
        {
            get
            {
                return Directory
                    .EnumerateFiles(_folder)
                    .Select(path => FileNameToKey(Path.GetFileName(path)));
            }
        }

        /// <summary>
        ///   Local file system path to the name.
        /// </summary>
        public string GetPath(TKey name)
        {
            return Path.Combine(_folder, KeyToFileName(name));
        }

        public async Task<ulong?> SizeOfAsync(TKey name, CancellationToken cancel = default)
        {
            var path = GetPath(name);

            using (await storeLock.ReaderLockAsync(cancel).ConfigureAwait(false))
            {
                var fi = new FileInfo(path);
                ulong? length = null;
                if (fi.Exists)
                    length = (ulong)fi.Length;
                return length;
            }
        }

        public void SetNamespace(string? ns)
        {
            _folder = string.IsNullOrEmpty(ns) ?
                Path.Combine(_baseFolder, "Default") :
                Path.Combine(_baseFolder, ns.Replace(".", "/"));

            if (!Directory.Exists(_folder))
                Directory.CreateDirectory(_folder);
        }
    }
}