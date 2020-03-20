using System;
using System.Collections.Generic;

namespace Sox.Core.Extensions
{
    /// <summary>
    /// Extension methods for IEnumerable
    /// </summary>
    public static class EnumerableExtensions
    {
        /// <summary>
        /// Iterate over an Enumerable and execute and Action for each element
        /// </summary>
        /// <param name="source">The IEnumerable to iterate over</param>
        /// <param name="action">The action to execute on each element</param>
        /// <typeparam name="T">The type of object being iterated over</typeparam>
        public static void ForEach<T>(this IEnumerable<T> source, Action<T> action)
        {
            if (source == null) throw new NullReferenceException(nameof(source));
            if (action == null) throw new NullReferenceException(nameof(action));

            foreach (T item in source)
            {
                action.Invoke(item);
            }
        }

        /// <summary>
        /// Map an Enumerable of one type to another
        /// </summary>
        /// <typeparam name="InT">The type to map from</typeparam>
        /// <typeparam name="OutT">The type to map to</typeparam>
        /// <param name="source">The source Enumerable</param>
        /// <param name="mapper">The Func that maps one type to the other</param>
        /// <returns>An enumerable of the target type</returns>
        public static IEnumerable<OutT> Map<InT, OutT>(this IEnumerable<InT> source, Func<InT, OutT> mapper)
        {
            if (source == null) throw new NullReferenceException(nameof(source));
            if (mapper == null) throw new NullReferenceException(nameof(mapper));

            var mapped = new List<OutT>();
            source.ForEach(item => mapped.Add(mapper.Invoke(item)));
            return mapped;
        }
    }
}