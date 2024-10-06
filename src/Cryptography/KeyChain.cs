﻿using Common.Logging;
using Ipfs.Core;
using Ipfs.Engine.Cryptography.Proto;
using Ipfs.Registry;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using PeerTalk.Cryptography;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Ipfs.Engine.Cryptography
{
    /// <summary>
    ///   A secure key chain.
    /// </summary>
    public partial class KeyChain : Ipfs.CoreApi.IKeyApi
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(KeyChain));

        private readonly KeyChainOptions _keyChainOptions;
        private char[] dek;
        private IStore<string, EncryptedKey> _store;
        private static byte[] hashedBytes;

        /// <summary>
        ///   Create a new instance of the <see cref="KeyChain"/> class.
        /// </summary>
        public KeyChain(IpfsEngineOptions ipfsOptions)
        {
            _keyChainOptions = ipfsOptions.KeyChain ?? new KeyChainOptions();
            _store = new FileStore<string, EncryptedKey>(ipfsOptions.Repository.Folder, "keys", FileStore<string, EncryptedKey>.InitSerialize.Json)
            {
                KeyToFileName = (key) => Encoding.UTF8.GetBytes(key).ToBase32(),
                FileNameToKey = (fileName) => Encoding.UTF8.GetString(Base32.Decode(fileName))
            };
        }

        /// <summary>
        ///   Create a new instance of the <see cref="KeyChain"/> class.
        /// </summary>
        public KeyChain(IStore<string, EncryptedKey> keyStore, IpfsEngineOptions ipfsOptions)
        {
            _store = keyStore;
            _keyChainOptions = ipfsOptions.KeyChain ?? new KeyChainOptions();
        }

        /// <summary>
        ///   Create a new instance of the <see cref="KeyChain"/> class.
        /// </summary>
        public KeyChain(IStore<string, EncryptedKey> keyStore, KeyChainOptions? keyChainOptions = default)
        {
            _store = keyStore;
            _keyChainOptions = keyChainOptions ?? new KeyChainOptions();
        }

        /// <summary>
        ///   Sets the passphrase for the key chain.
        /// </summary>
        /// <param name="passphrase"></param>
        /// <param name="cancel">
        ///   Is used to stop the task.  When cancelled, the <see cref="TaskCanceledException"/> is raised.
        /// </param>
        /// <returns>
        ///   A task that represents the asynchronous operation.
        /// </returns>
        /// <exception cref="UnauthorizedAccessException">
        ///   When the <paramref name="passphrase"/> is wrong.
        /// </exception>
        /// <remarks>
        ///   The <paramref name="passphrase"/> is used to generate a DEK (derived encryption
        ///   key).  The DEK is then used to encrypt the stored keys.
        ///   <para>
        ///   Neither the <paramref name="passphrase"/> nor the DEK are stored.
        ///   </para>
        /// </remarks>
        public async Task SetPassphraseAsync(SecureString passphrase, CancellationToken cancel = default)
        {
            // TODO: Verify DEK options.
            // TODO: get digest based on Options.Hash
            passphrase.UseSecretBytes(plain =>
            {
                var pdb = new Pkcs5S2ParametersGenerator(new Sha256Digest());
                pdb.Init(
                    plain,
                    Encoding.UTF8.GetBytes(_keyChainOptions.Dek.Salt),
                    _keyChainOptions.Dek.IterationCount);
                var key = (KeyParameter)pdb.GenerateDerivedMacParameters(_keyChainOptions.Dek.KeyLength * 8);
                dek = key.GetKey().ToBase64NoPad().ToCharArray();
            });

            // Verify that that pass phrase is okay, by reading a key.
            var akey = await _store.TryGetAsync("self", cancel).ConfigureAwait(false);
            if (akey != null)
            {
                try
                {
                    UseEncryptedKey(akey, _ => { });
                }
                catch (Exception e)
                {
                    throw new UnauthorizedAccessException("The pass phrase is wrong.", e);
                }
            }

            log.Debug("Pass phrase is okay");
        }

        /// <summary>
        ///   Find a key by its name.
        /// </summary>
        /// <param name="name">
        ///   The local name of the key.
        /// </param>
        /// <param name="cancel">
        ///   Is used to stop the task.  When cancelled, the <see cref="TaskCanceledException"/> is raised.
        /// </param>
        /// <returns>
        ///   A task that represents the asynchronous operation. The task's result is
        ///   an <see cref="IKey"/> or <b>null</b> if the key is not defined.
        /// </returns>
        public async Task<IKey> FindKeyByNameAsync(string name, CancellationToken cancel = default)
        {
            var key = await _store.TryGetAsync(name, cancel).ConfigureAwait(false);
            if (key == null)
                return null;
            return new KeyInfo { Id = key.Id, Name = key.Name };
        }

        /// <summary>
        ///   Gets the IPFS encoded public key for the specified key.
        /// </summary>
        /// <param name="name">
        ///   The local name of the key.
        /// </param>
        /// <param name="cancel">
        ///   Is used to stop the task.  When cancelled, the <see cref="TaskCanceledException"/> is raised.
        /// </param>
        /// <returns>
        ///   A task that represents the asynchronous operation. The task's result is
        ///   the IPFS encoded public key.
        /// </returns>
        /// <remarks>
        ///   The IPFS public key is the base-64 encoding of a protobuf encoding containing
        ///   a type and the DER encoding of the PKCS Subject Public Key Info.
        /// </remarks>
        /// <seealso href="https://tools.ietf.org/html/rfc5280#section-4.1.2.7"/>
        public async Task<string> GetIpfsPublicKeyAsync(string name, CancellationToken cancel = default)
        {
            //
            string result = null;
            var ekey = await _store.TryGetAsync(name, cancel).ConfigureAwait(false);
            if (ekey != null)
            {
                UseEncryptedKey(ekey, key =>
                {
                    var kp = GetKeyPairFromPrivateKey(key);
                    var spki = SubjectPublicKeyInfoFactory
                        .CreateSubjectPublicKeyInfo(kp.Public)
                        .GetDerEncoded();
                    // Add protobuf cruft.
                    var publicKey = new Proto.PublicKey
                    {
                        Data = spki
                    };
                    if (kp.Public is RsaKeyParameters)
                        publicKey.Type = Proto.KeyType.RSA;
                    else if (kp.Public is Ed25519PublicKeyParameters)
                        publicKey.Type = Proto.KeyType.Ed25519;
                    else if (kp.Public is ECPublicKeyParameters)
                        publicKey.Type = Proto.KeyType.Secp256k1;
                    else
                        throw new NotSupportedException($"The key type {kp.Public.GetType().Name} is not supported.");

                    using (var ms = new MemoryStream())
                    {
                        ProtoBuf.Serializer.Serialize(ms, publicKey);
                        result = Convert.ToBase64String(ms.ToArray());
                    }
                });
            }
            return result;
        }

        /// <summary>
        /// Get IPFS shared key
        /// </summary>
        /// <param name="cancel"></param>
        public static async Task<byte[]> GetSharedKeyAsync(CancellationToken cancel = default)
        {
            // returh existing shared key
            if (hashedBytes?.Length >= 32)
            {
                return hashedBytes;
            }

            var localSharedKey = new SharedKey();
            await localSharedKey.GenerateAsync("IPFS_XKEY", cancel);

            // update member varaiable
            hashedBytes = localSharedKey.Key;

            // return AES key
            return hashedBytes;
        }

        /// <inheritdoc />
        public async Task<IKey> GeneratePrivateKeyAsync(string name, string keyType, int size, CancellationToken cancel = default)
        {
            // Apply defaults.
            if (string.IsNullOrWhiteSpace(keyType))
                keyType = _keyChainOptions.DefaultKeyType;
            if (size < 1)
                size = _keyChainOptions.DefaultKeySize;
            keyType = keyType.ToLowerInvariant();

            // Create the key pair.
            log.DebugFormat("Creating {0} key named '{1}'", keyType, name);

            IAsymmetricCipherKeyPairGenerator g;
            switch (keyType)
            {
                case "rsa":
                    g = GeneratorUtilities.GetKeyPairGenerator("RSA");
                    g.Init(new RsaKeyGenerationParameters(
                        BigInteger.ValueOf(0x10001), new SecureRandom(), size, 25));
                    break;

                case "ed25519":
                    g = GeneratorUtilities.GetKeyPairGenerator("Ed25519");
                    g.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
                    break;

                case "secp256k1":
                    g = GeneratorUtilities.GetKeyPairGenerator("EC");
                    g.Init(new ECKeyGenerationParameters(SecObjectIdentifiers.SecP256k1, new SecureRandom()));
                    break;

                default:
                    throw new Exception($"Invalid key type '{keyType}'.");
            }
            var keyPair = g.GenerateKeyPair();
            log.Debug("Created key");
            return await AddPrivateKeyAsync(name, keyPair, cancel).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<string> ExportAsync(string name, char[] password, CancellationToken cancel = default)
        {
            string pem = "";
            var key = await _store.GetAsync(name, cancel).ConfigureAwait(false);
            UseEncryptedKey(key, pkey =>
            {
                using (var sw = new StringWriter())
                {
                    var pkcs8 = new Pkcs8Generator(pkey, Pkcs8Generator.PbeSha1_3DES)
                    {
                        Password = password
                    };
                    var pw = new PemWriter(sw);
                    pw.WriteObject(pkcs8);
                    pw.Writer.Flush();
                    pem = sw.ToString();
                }
            });

            return pem;
        }

        /// <inheritdoc />
        public async Task<IKey> ImportAsync(string name, string pem, char[]? password = null, CancellationToken cancel = default)
        {
            AsymmetricKeyParameter? key;
            using (var sr = new StringReader(pem))
            using (var pf = new PasswordFinder { Password = password })
            {
                var reader = new PemReader(sr, pf);
                try
                {
                    key = reader.ReadObject() as AsymmetricKeyParameter;
                }
                catch (Exception e)
                {
                    throw new UnauthorizedAccessException("The password is wrong.", e);
                }
                if (key?.IsPrivate != true)
                    throw new InvalidDataException("Not a valid PEM private key");
            }

            return await AddPrivateKeyAsync(name, GetKeyPairFromPrivateKey(key), cancel).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task<IEnumerable<IKey>> ListAsync(CancellationToken cancel = default)
        {
            var keys = _store
                .Values
                .Select(key => (IKey)new KeyInfo { Id = key.Id, Name = key.Name });
            return Task.FromResult(keys);
        }

        /// <inheritdoc />
        public async Task<IKey> RemoveAsync(string name, CancellationToken cancel = default)
        {
            var key = await _store.TryGetAsync(name, cancel).ConfigureAwait(false);
            if (key == null)
                return null;

            await _store.RemoveAsync(name, cancel).ConfigureAwait(false);
            return new KeyInfo { Id = key.Id, Name = key.Name };
        }

        /// <inheritdoc />
        public async Task<IKey> RenameAsync(string oldName, string newName, CancellationToken cancel = default)
        {
            var key = await _store.TryGetAsync(oldName, cancel).ConfigureAwait(false);
            if (key == null)
                return null;
            key.Name = newName;
            await _store.PutAsync(newName, key, cancel).ConfigureAwait(false);
            await _store.RemoveAsync(oldName, cancel).ConfigureAwait(false);

            return new KeyInfo { Id = key.Id, Name = newName };
        }

        /// <summary>
        ///   Gets the Bouncy Castle representation of the private key.
        /// </summary>
        /// <param name="name">
        ///   The local name of key.
        /// </param>
        /// <param name="cancel">
        ///   Is used to stop the task.  When cancelled, the <see cref="TaskCanceledException"/> is raised.
        /// </param>
        /// <returns>
        ///   A task that represents the asynchronous operation. The task's result is
        ///   the private key as an <b>AsymmetricKeyParameter</b>.
        /// </returns>
        public async Task<AsymmetricKeyParameter> GetPrivateKeyAsync(string name, CancellationToken cancel = default)
        {
            var key = await _store.TryGetAsync(name, cancel).ConfigureAwait(false);
            if (key == null)
            {
                throw new KeyNotFoundException($"The key '{name}' does not exist.");
            }

            AsymmetricKeyParameter kp = null;
            UseEncryptedKey(key, pkey => kp = pkey);
            return kp;
        }

        private void UseEncryptedKey(EncryptedKey key, Action<AsymmetricKeyParameter> action)
        {
            using var sr = new StringReader(key.Pem);
            using var pf = new PasswordFinder { Password = dek };
            var reader = new PemReader(sr, pf);
            var privateKey = (AsymmetricKeyParameter)reader.ReadObject();
            action(privateKey);
        }

        private async Task<IKey> AddPrivateKeyAsync(string name, AsymmetricCipherKeyPair keyPair, CancellationToken cancel)
        {
            // Create the key ID
            var keyId = CreateKeyId(keyPair.Public);

            // Create the PKCS #8 container for the key
            string pem;
            using (var sw = new StringWriter())
            {
                var pkcs8 = new Pkcs8Generator(keyPair.Private, Pkcs8Generator.PbeSha1_3DES)
                {
                    Password = dek
                };
                var pw = new PemWriter(sw);
                pw.WriteObject(pkcs8);
                await pw.Writer.FlushAsync();
                pem = sw.ToString();
            }

            // Store the key in the repository.
            var key = new EncryptedKey
            {
                Id = keyId.ToBase58(),
                Name = name,
                Pem = pem
            };
            await _store.PutAsync(name, key).ConfigureAwait(false);
            log.DebugFormat("Added key '{0}' with ID {1}", name, keyId);

            return new KeyInfo { Id = key.Id, Name = key.Name };
        }

        /// <summary>
        ///   Create a key ID for the key.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        /// <remarks>
        ///   The key id is the SHA-256 multihash of its public key. The public key is
        ///   a protobuf encoding containing a type and
        ///   the DER encoding of the PKCS SubjectPublicKeyInfo.
        /// </remarks>
        private static MultiHash CreateKeyId(AsymmetricKeyParameter key)
        {
            var spki = SubjectPublicKeyInfoFactory
                .CreateSubjectPublicKeyInfo(key)
                .GetDerEncoded();

            // Add protobuf cruft.
            var publicKey = new Proto.PublicKey
            {
                Data = spki
            };
            if (key is RsaKeyParameters)
                publicKey.Type = Proto.KeyType.RSA;
            else if (key is ECPublicKeyParameters)
                publicKey.Type = Proto.KeyType.Secp256k1;
            else if (key is Ed25519PublicKeyParameters)
                publicKey.Type = Proto.KeyType.Ed25519;
            else
                throw new NotSupportedException($"The key type {key.GetType().Name} is not supported.");

            using (var ms = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(ms, publicKey);

                // If the length of the serialized bytes <= 42, then we compute the "identity" multihash of
                // the serialized bytes. The idea here is that if the serialized byte array
                // is short enough, we can fit it in a multihash verbatim without having to
                // condense it using a hash function.
                var alg = (ms.Length <= 48) ? AlgorithmNames.identity : AlgorithmNames.sha2_256;

                ms.Position = 0;
                return MultiHash.ComputeHash(ms, alg);
            }
        }

        private AsymmetricCipherKeyPair GetKeyPairFromPrivateKey(AsymmetricKeyParameter privateKey)
        {
            AsymmetricCipherKeyPair keyPair = null;
            if (privateKey is RsaPrivateCrtKeyParameters rsa)
            {
                var pub = new RsaKeyParameters(false, rsa.Modulus, rsa.PublicExponent);
                keyPair = new AsymmetricCipherKeyPair(pub, privateKey);
            }
            else if (privateKey is Ed25519PrivateKeyParameters ed)
            {
                var pub = ed.GeneratePublicKey();
                keyPair = new AsymmetricCipherKeyPair(pub, privateKey);
            }
            else if (privateKey is ECPrivateKeyParameters ec)
            {
                var q = ec.Parameters.G.Multiply(ec.D);
                var pub = new ECPublicKeyParameters(ec.AlgorithmName, q, ec.PublicKeyParamSet);
                keyPair = new AsymmetricCipherKeyPair(pub, ec);
            }
            if (keyPair == null)
                throw new NotSupportedException($"The key type {privateKey.GetType().Name} is not supported.");

            return keyPair;
        }

        private class PasswordFinder : IPasswordFinder, IDisposable
        {
            public char[]? Password;

            public void Dispose()
            {
                Password = null;
            }

            public char[]? GetPassword()
            {
                return Password;
            }
        }
    }
}