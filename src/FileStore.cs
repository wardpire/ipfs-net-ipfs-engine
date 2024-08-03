﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Nito.AsyncEx;

namespace Ipfs.Engine
{
    /// <summary>
    ///   A file based repository for name value pairs.
    /// </summary>
    /// <typeparam name="TName">
    ///   The type used for a unique name.
    /// </typeparam>
    /// <typeparam name="TValue">
    ///   The type used for the value.
    /// </typeparam>
    /// <remarks>
    ///   All operations are atomic, a reader/writer lock is used.
    /// </remarks>
    public class FileStore<TName, TValue> where TValue : class
    {
        private readonly AsyncReaderWriterLock storeLock = new();

        /// <summary>
        ///   A function to write the JSON encoded entity to the stream.
        /// </summary>
        /// <remarks>
        ///   This is the default <see cref="Serialize"/>.
        /// </remarks>
        public static Func<Stream, TName, TValue, CancellationToken, Task> JsonSerialize =
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
        public static Func<Stream, TName, CancellationToken, Task<TValue>> JsonDeserialize =
            (stream, _, __) =>
            {
                using var reader = new StreamReader(stream);
                using var jtr = new JsonTextReader(reader);
                var ser = new JsonSerializer();
                return Task.FromResult(ser.Deserialize<TValue>(jtr));
            };

        /// <summary>
        ///   The fully qualififed path to a directory
        ///   that stores the name value pairs.
        /// </summary>
        /// <value>
        ///   A fully qualified path.
        /// </value>
        /// <remarks>
        ///   The directory must already exist.
        /// </remarks>
        public string Folder { get; set; }

        /// <summary>
        ///   A function that converts the name to a case insensitive key name.
        /// </summary>
        public Func<TName, string> NameToKey { get; set; }

        /// <summary>
        ///   A function that converts the case insensitive key to a name.
        /// </summary>
        public Func<string, TName> KeyToName { get; set; }

        /// <summary>
        ///   Sends the value to the stream.
        /// </summary>
        /// <value>
        ///   Defaults to using <see cref="JsonSerialize"/>.
        /// </value>
        public Func<Stream, TName, TValue, CancellationToken, Task> Serialize
        { get; set; } = JsonSerialize;

        /// <summary>
        ///   Retrieves the value from the stream.
        /// </summary>
        /// <value>
        ///   Defaults to using <see cref="JsonDeserialize"/>
        /// </value>
        public Func<Stream, TName, CancellationToken, Task<TValue>> Deserialize
        { get; set; } = JsonDeserialize;

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
        public async Task<TValue> TryGetAsync(TName name, CancellationToken cancel = default)
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
        public async Task<TValue> GetAsync(TName name, CancellationToken cancel = default)
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
        public async Task PutAsync(TName name, TValue value, CancellationToken cancel = default)
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
        public async Task RemoveAsync(TName name, CancellationToken cancel = default)
        {
            var path = GetPath(name);
            using (await storeLock.WriterLockAsync(cancel).ConfigureAwait(false))
            {
                File.Delete(path);
            }
        }

        /// <summary>
        ///   Get's the serialised length of the entity.
        /// </summary>
        /// <param name="name">
        ///   The unique name of the entity.
        /// </param>
        /// <param name="cancel">
        ///   Is used to stop the task.  When cancelled, the <see cref="TaskCanceledException"/> is raised.
        /// </param>
        /// <returns>
        ///   A task that represents the asynchronous operation. The task's result is
        ///   a nullable long.
        /// </returns>
        /// <remarks>
        ///   Return a null when the <paramref name="name"/> does not exist.
        /// </remarks>
        public async Task<long?> LengthAsync(TName name, CancellationToken cancel = default)
        {
            var path = GetPath(name);

            using (await storeLock.ReaderLockAsync(cancel).ConfigureAwait(false))
            {
                var fi = new FileInfo(path);
                long? length = null;
                if (fi.Exists)
                    length = fi.Length;
                return length;
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
        public async Task<bool> ExistsAsync(TName name, CancellationToken cancel = default)
        {
            var path = GetPath(name);
            using (await storeLock.ReaderLockAsync(cancel).ConfigureAwait(false))
            {
                return File.Exists(path);
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
                    .EnumerateFiles(Folder)
                    .Select(path =>
                    {
                        using (var content = File.OpenRead(path))
                        {
                            var name = KeyToName(Path.GetFileName(path));
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
        ///   A sequence of <typeparamref name="TName"/>.
        /// </value>
        public IEnumerable<TName> Names
        {
            get
            {
                return Directory
                    .EnumerateFiles(Folder)
                    .Select(path => KeyToName(Path.GetFileName(path)));
            }
        }

        /// <summary>
        ///   Local file system path to the name.
        /// </summary>
        public string GetPath(TName name)
        {
            return Path.Combine(Folder, NameToKey(name));
        }
    }
}