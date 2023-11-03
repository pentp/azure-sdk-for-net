// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

// <auto-generated/>

#nullable disable

using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.MySql.FlexibleServers;

namespace Azure.ResourceManager.MySql.FlexibleServers.Mocking
{
    /// <summary> A class to add extension methods to ArmClient. </summary>
    public partial class MockableMySqlFlexibleServersArmClient : ArmResource
    {
        /// <summary> Initializes a new instance of the <see cref="MockableMySqlFlexibleServersArmClient"/> class for mocking. </summary>
        protected MockableMySqlFlexibleServersArmClient()
        {
        }

        /// <summary> Initializes a new instance of the <see cref="MockableMySqlFlexibleServersArmClient"/> class. </summary>
        /// <param name="client"> The client parameters to use in these operations. </param>
        /// <param name="id"> The identifier of the resource that is the target of operations. </param>
        internal MockableMySqlFlexibleServersArmClient(ArmClient client, ResourceIdentifier id) : base(client, id)
        {
        }

        internal MockableMySqlFlexibleServersArmClient(ArmClient client) : this(client, ResourceIdentifier.Root)
        {
        }

        private string GetApiVersionOrNull(ResourceType resourceType)
        {
            TryGetApiVersion(resourceType, out string apiVersion);
            return apiVersion;
        }

        /// <summary>
        /// Gets an object representing a <see cref="MySqlFlexibleServerAadAdministratorResource" /> along with the instance operations that can be performed on it but with no data.
        /// You can use <see cref="MySqlFlexibleServerAadAdministratorResource.CreateResourceIdentifier" /> to create a <see cref="MySqlFlexibleServerAadAdministratorResource" /> <see cref="ResourceIdentifier" /> from its components.
        /// </summary>
        /// <param name="id"> The resource ID of the resource to get. </param>
        /// <returns> Returns a <see cref="MySqlFlexibleServerAadAdministratorResource" /> object. </returns>
        public virtual MySqlFlexibleServerAadAdministratorResource GetMySqlFlexibleServerAadAdministratorResource(ResourceIdentifier id)
        {
            MySqlFlexibleServerAadAdministratorResource.ValidateResourceId(id);
            return new MySqlFlexibleServerAadAdministratorResource(Client, id);
        }

        /// <summary>
        /// Gets an object representing a <see cref="MySqlFlexibleServerBackupResource" /> along with the instance operations that can be performed on it but with no data.
        /// You can use <see cref="MySqlFlexibleServerBackupResource.CreateResourceIdentifier" /> to create a <see cref="MySqlFlexibleServerBackupResource" /> <see cref="ResourceIdentifier" /> from its components.
        /// </summary>
        /// <param name="id"> The resource ID of the resource to get. </param>
        /// <returns> Returns a <see cref="MySqlFlexibleServerBackupResource" /> object. </returns>
        public virtual MySqlFlexibleServerBackupResource GetMySqlFlexibleServerBackupResource(ResourceIdentifier id)
        {
            MySqlFlexibleServerBackupResource.ValidateResourceId(id);
            return new MySqlFlexibleServerBackupResource(Client, id);
        }

        /// <summary>
        /// Gets an object representing a <see cref="MySqlFlexibleServerConfigurationResource" /> along with the instance operations that can be performed on it but with no data.
        /// You can use <see cref="MySqlFlexibleServerConfigurationResource.CreateResourceIdentifier" /> to create a <see cref="MySqlFlexibleServerConfigurationResource" /> <see cref="ResourceIdentifier" /> from its components.
        /// </summary>
        /// <param name="id"> The resource ID of the resource to get. </param>
        /// <returns> Returns a <see cref="MySqlFlexibleServerConfigurationResource" /> object. </returns>
        public virtual MySqlFlexibleServerConfigurationResource GetMySqlFlexibleServerConfigurationResource(ResourceIdentifier id)
        {
            MySqlFlexibleServerConfigurationResource.ValidateResourceId(id);
            return new MySqlFlexibleServerConfigurationResource(Client, id);
        }

        /// <summary>
        /// Gets an object representing a <see cref="MySqlFlexibleServerDatabaseResource" /> along with the instance operations that can be performed on it but with no data.
        /// You can use <see cref="MySqlFlexibleServerDatabaseResource.CreateResourceIdentifier" /> to create a <see cref="MySqlFlexibleServerDatabaseResource" /> <see cref="ResourceIdentifier" /> from its components.
        /// </summary>
        /// <param name="id"> The resource ID of the resource to get. </param>
        /// <returns> Returns a <see cref="MySqlFlexibleServerDatabaseResource" /> object. </returns>
        public virtual MySqlFlexibleServerDatabaseResource GetMySqlFlexibleServerDatabaseResource(ResourceIdentifier id)
        {
            MySqlFlexibleServerDatabaseResource.ValidateResourceId(id);
            return new MySqlFlexibleServerDatabaseResource(Client, id);
        }

        /// <summary>
        /// Gets an object representing a <see cref="MySqlFlexibleServerFirewallRuleResource" /> along with the instance operations that can be performed on it but with no data.
        /// You can use <see cref="MySqlFlexibleServerFirewallRuleResource.CreateResourceIdentifier" /> to create a <see cref="MySqlFlexibleServerFirewallRuleResource" /> <see cref="ResourceIdentifier" /> from its components.
        /// </summary>
        /// <param name="id"> The resource ID of the resource to get. </param>
        /// <returns> Returns a <see cref="MySqlFlexibleServerFirewallRuleResource" /> object. </returns>
        public virtual MySqlFlexibleServerFirewallRuleResource GetMySqlFlexibleServerFirewallRuleResource(ResourceIdentifier id)
        {
            MySqlFlexibleServerFirewallRuleResource.ValidateResourceId(id);
            return new MySqlFlexibleServerFirewallRuleResource(Client, id);
        }

        /// <summary>
        /// Gets an object representing a <see cref="MySqlFlexibleServerResource" /> along with the instance operations that can be performed on it but with no data.
        /// You can use <see cref="MySqlFlexibleServerResource.CreateResourceIdentifier" /> to create a <see cref="MySqlFlexibleServerResource" /> <see cref="ResourceIdentifier" /> from its components.
        /// </summary>
        /// <param name="id"> The resource ID of the resource to get. </param>
        /// <returns> Returns a <see cref="MySqlFlexibleServerResource" /> object. </returns>
        public virtual MySqlFlexibleServerResource GetMySqlFlexibleServerResource(ResourceIdentifier id)
        {
            MySqlFlexibleServerResource.ValidateResourceId(id);
            return new MySqlFlexibleServerResource(Client, id);
        }

        /// <summary>
        /// Gets an object representing a <see cref="MySqlFlexibleServersCapabilityResource" /> along with the instance operations that can be performed on it but with no data.
        /// You can use <see cref="MySqlFlexibleServersCapabilityResource.CreateResourceIdentifier" /> to create a <see cref="MySqlFlexibleServersCapabilityResource" /> <see cref="ResourceIdentifier" /> from its components.
        /// </summary>
        /// <param name="id"> The resource ID of the resource to get. </param>
        /// <returns> Returns a <see cref="MySqlFlexibleServersCapabilityResource" /> object. </returns>
        public virtual MySqlFlexibleServersCapabilityResource GetMySqlFlexibleServersCapabilityResource(ResourceIdentifier id)
        {
            MySqlFlexibleServersCapabilityResource.ValidateResourceId(id);
            return new MySqlFlexibleServersCapabilityResource(Client, id);
        }
    }
}
