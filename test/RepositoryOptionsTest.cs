using Ipfs.Engine.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Ipfs.Engine
{
    [TestClass]
    public class RepositoryOptionsTest
    {
        [TestMethod]
        public void Defaults()
        {
            var options = new RepositoryOptions();
            Assert.IsNotNull(options.Folder);
        }

        [TestMethod]
        public void Repository_Directory()
        {
            var names = new string[] { "IPFS_PATH", "HOME", "HOMEPATH" };
            var values = names.Select(Environment.GetEnvironmentVariable);

            foreach (var name in names)
            {
                Environment.SetEnvironmentVariable(name, null);
            }

            var repoPath = Environment.GetEnvironmentVariable("IPFS_PATH");
            if (string.IsNullOrWhiteSpace(repoPath))
            {
                repoPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".csipfs");
            }

            var options = new RepositoryOptions();
            Assert.AreEqual(repoPath, options.Folder);
        }

        [TestMethod]
        public void Environment_IpfsPath()
        {
            var names = new string[] { "IPFS_PATH", "HOME", "HOMEPATH" };
            var values = names.Select(n => Environment.GetEnvironmentVariable(n));
            var sep = Path.DirectorySeparatorChar;
            try
            {
                foreach (var name in names)
                {
                    Environment.SetEnvironmentVariable(name, null);
                }
                Environment.SetEnvironmentVariable("IPFS_PATH", $"{sep}x1");
                var options = new RepositoryOptions();
                Assert.AreEqual($"{sep}x1", options.Folder);

                Environment.SetEnvironmentVariable("IPFS_PATH", $"{sep}x2{sep}");
                options = new RepositoryOptions();
                Assert.AreEqual($"{sep}x2{sep}", options.Folder);
            }
            finally
            {
                var pairs = names.Zip(values, (name, value) => new { name = name, value = value });
                foreach (var pair in pairs)
                {
                    Environment.SetEnvironmentVariable(pair.name, pair.value);
                }
            }
        }
    }
}