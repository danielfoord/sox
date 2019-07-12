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
    }
}