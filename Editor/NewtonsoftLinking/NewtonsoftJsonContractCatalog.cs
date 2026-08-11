using System;
using System.Collections.Generic;

namespace Deucarian.BuildPipeline
{
    internal sealed class NewtonsoftJsonContractCatalog
    {
        private readonly SortedDictionary<string, SortedSet<string>> contracts =
            new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        public int Count { get; private set; }

        public IEnumerable<KeyValuePair<string, SortedSet<string>>> Entries => contracts;

        public void Add(string assemblyName, string typeName)
        {
            if (string.IsNullOrWhiteSpace(assemblyName))
            {
                throw new ArgumentException("An assembly name is required.", nameof(assemblyName));
            }

            if (string.IsNullOrWhiteSpace(typeName))
            {
                throw new ArgumentException("A type name is required.", nameof(typeName));
            }

            if (!contracts.TryGetValue(assemblyName, out SortedSet<string> types))
            {
                types = new SortedSet<string>(StringComparer.Ordinal);
                contracts.Add(assemblyName, types);
            }

            if (types.Add(typeName))
            {
                Count++;
            }
        }

        public bool Contains(string assemblyName, string typeName)
        {
            return contracts.TryGetValue(assemblyName, out SortedSet<string> types)
                && types.Contains(typeName);
        }
    }
}
