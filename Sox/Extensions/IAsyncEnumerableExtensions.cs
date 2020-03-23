using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sox.Extensions
{
    internal static class IAsyncEnumerableExtensions
    {
        internal static async Task<IEnumerable<T>> AsEnumerable<T>(this IAsyncEnumerable<T> asyncEnumerable)
        {
            var enumerable = new List<T>();
            await foreach (var item in asyncEnumerable)
            {
                enumerable.Add(item);
            }
            return enumerable;
        }
    }
}
