using System.Linq;
using System.Collections.Generic;
using Noggog;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using System;

namespace OblivionInteractionIconsPatcher
{
    public static class StringExtension
    {
        public static bool ContainsNullable(this string? str, string item, StringComparison comparisonType = default)
        {
            if (str.IsNullOrEmpty()) return false;
            return str.Contains(item, comparisonType);
        }
        public static bool Contains(this string? str, IEnumerable<string> collection, StringComparison comparisonType = default)
        {
            if (str.IsNullOrEmpty()) return false;
            return collection.Any(e => str.Contains(e, comparisonType));
        }
        public static bool EqualsNullable(this string? str, string item, StringComparison comparisonType = default)
        {
            if (str.IsNullOrEmpty()) return false;
            return str.Equals(item, comparisonType);
        }
        public static bool Equals(this string? str, IEnumerable<string> collection, StringComparison comparisonType = default)
        {
            return collection.Any(e => e.Equals(str, comparisonType));
        }
    }
}