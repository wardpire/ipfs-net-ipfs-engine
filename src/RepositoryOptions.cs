﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ipfs.Engine
{
    /// <summary>
    ///   Configuration options for the repository.
    /// </summary>
    /// <seealso cref="IpfsEngineOptions"/>
    public class RepositoryOptions
    {
        /// <summary>
        ///   Creates a new instance of the <see cref="RepositoryOptions"/> class
        ///   with the default values.
        /// </summary>
        public RepositoryOptions()
        {
            var path = Environment.GetEnvironmentVariable("IPFS_PATH");
            if (string.IsNullOrWhiteSpace(path))
            {
                Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".csipfs");
            }
            else
            {
                Folder = path;
            }
        }

        /// <summary>
        ///   The directory of the repository.
        /// </summary>
        /// <value>
        ///   The default value is <c>$IPFS_PATH</c> or <c>$HOME/.csipfs</c> or
        ///   <c>$HOMEPATH/.csipfs</c>.
        /// </value>
        public string Folder { get; set; }

        /// <summary>
        ///   Get the existing directory of the repository.
        /// </summary>
        /// <returns>
        ///   An existing directory.
        /// </returns>
        /// <remarks>
        ///   Creates the <see cref="Folder"/> if it does not exist.
        /// </remarks>
        public string ExistingFolder()
        {
            var path = Folder;
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            return path;
        }
    }
}