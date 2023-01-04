using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Ipfs.Engine.Cryptography
{
    /// <summary>
    /// Shared key
    /// </summary>
    public class SharedKey
    {
        /// <summary>
        /// Key
        /// </summary>
        public byte[] Key { get; set; } = Array.Empty<byte>();

        //
        /// <summary>
        /// Generate key
        /// </summary>
        /// <param name="keyFile"></param>
        /// <param name="cancel"></param>
        /// <returns></returns>
        public async Task GenerateAsync(string keyFile, CancellationToken cancel = default)
        {
            var sharedKey = Environment.GetEnvironmentVariable(keyFile);
            if (string.IsNullOrWhiteSpace(sharedKey))
            {
                // TODO: Find a more secure way of generating a unique but consisten pass pharse
                //       for the shared key.
                string inputText = "findarecipientwhosekeyweholdWeonlydealwithrecipientnames".Trim();

                byte[] hashedBytes;
                using (var hash = SHA512.Create())
                {
                    //
                    var inputBytes = Encoding.UTF8.GetBytes(inputText);
                    using var ms = new MemoryStream(inputBytes);
                    hashedBytes = await hash.ComputeHashAsync(ms, cancel);
                }

                // set variable
                Environment.SetEnvironmentVariable(keyFile, Encoding.UTF8.GetString(hashedBytes));

                // update key
                Key = hashedBytes.Take(32).ToArray();
            }
            else
            {
                //
                var readKey = Encoding.UTF8.GetBytes(sharedKey);

                // update key
                Key = readKey.Take(32).ToArray();
            }
        }
    }
}