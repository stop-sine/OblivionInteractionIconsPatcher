using System;
using System.Collections.Generic;
using System.Linq;

namespace OblivionInteractionIconsPatcher
{
    /// <summary>
    /// Extension methods for string and string collections.
    /// </summary>
    public static class StringExtension
    {
        /// <summary>
        /// Checks if the string contains the specified substring, handling nulls.
        /// </summary>
        public static bool ContainsNullable(this string? str, string item, StringComparison comparisonType = StringComparison.OrdinalIgnoreCase) =>
            !string.IsNullOrEmpty(str) && str.Contains(item, comparisonType);

        /// <summary>
        /// Checks if the string contains any of the specified substrings.
        /// </summary>
        public static bool Contains(this string? str, IEnumerable<string> collection, StringComparison comparisonType = StringComparison.OrdinalIgnoreCase) =>
            !string.IsNullOrEmpty(str) && collection.Any(e => str.Contains(e, comparisonType));

        /// <summary>
        /// Checks if the string equals the specified string, handling nulls.
        /// </summary>
        public static bool EqualsNullable(this string? str, string item, StringComparison comparisonType = StringComparison.OrdinalIgnoreCase) =>
            !string.IsNullOrEmpty(str) && str.Equals(item, comparisonType);

        /// <summary>
        /// Checks if the string equals any of the specified strings.
        /// </summary>
        public static bool Equals(this string? str, IEnumerable<string> collection, StringComparison comparisonType = StringComparison.OrdinalIgnoreCase) =>
            collection.Any(e => string.Equals(str, e, comparisonType));
    }
}