// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

// <auto-generated/>

#nullable disable

using System;

namespace Azure.ResourceManager.MySql.FlexibleServers.Models
{
    /// <summary> Import source related properties. </summary>
    public partial class ImportSourceProperties
    {
        /// <summary> Initializes a new instance of ImportSourceProperties. </summary>
        public ImportSourceProperties()
        {
        }

        /// <summary> Initializes a new instance of ImportSourceProperties. </summary>
        /// <param name="storageType"> Storage type of import source. </param>
        /// <param name="storageUri"> Uri of the import source storage. </param>
        /// <param name="sasToken"> Sas token for accessing source storage. Read and list permissions are required for sas token. </param>
        /// <param name="dataDirPath"> Relative path of data directory in storage. </param>
        internal ImportSourceProperties(ImportSourceStorageType? storageType, Uri storageUri, string sasToken, string dataDirPath)
        {
            StorageType = storageType;
            StorageUri = storageUri;
            SasToken = sasToken;
            DataDirPath = dataDirPath;
        }

        /// <summary> Storage type of import source. </summary>
        public ImportSourceStorageType? StorageType { get; set; }
        /// <summary> Uri of the import source storage. </summary>
        public Uri StorageUri { get; set; }
        /// <summary> Sas token for accessing source storage. Read and list permissions are required for sas token. </summary>
        public string SasToken { get; set; }
        /// <summary> Relative path of data directory in storage. </summary>
        public string DataDirPath { get; set; }
    }
}
