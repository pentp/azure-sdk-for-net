// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

// <auto-generated/>

#nullable disable

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;

namespace Azure.ResourceManager.ContainerRegistry
{
    internal class ContainerRegistryTokenOperationSource : IOperationSource<ContainerRegistryTokenResource>
    {
        private readonly ArmClient _client;

        internal ContainerRegistryTokenOperationSource(ArmClient client)
        {
            _client = client;
        }

        ContainerRegistryTokenResource IOperationSource<ContainerRegistryTokenResource>.CreateResult(Response response, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(response.ContentStream);
            var data = ContainerRegistryTokenData.DeserializeContainerRegistryTokenData(document.RootElement);
            return new ContainerRegistryTokenResource(_client, data);
        }

        async ValueTask<ContainerRegistryTokenResource> IOperationSource<ContainerRegistryTokenResource>.CreateResultAsync(Response response, CancellationToken cancellationToken)
        {
            using var document = await JsonDocument.ParseAsync(response.ContentStream, default, cancellationToken).ConfigureAwait(false);
            var data = ContainerRegistryTokenData.DeserializeContainerRegistryTokenData(document.RootElement);
            return new ContainerRegistryTokenResource(_client, data);
        }
    }
}
