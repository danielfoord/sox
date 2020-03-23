using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sox.Core.Extensions
{
    public static class IAsyncEnumerableExtensions
    {
        public static async Task<IEnumerable<T>> AsEnumerable<T>(this IAsyncEnumerable<T> asyncEnumerable)
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
