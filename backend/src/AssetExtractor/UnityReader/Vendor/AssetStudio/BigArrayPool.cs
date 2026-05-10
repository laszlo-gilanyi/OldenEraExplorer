using System.Buffers;

namespace AssetStudio
{
    internal static class BigArrayPool<T>
    {
        public static ArrayPool<T> Shared { get; }

        static BigArrayPool()
        {
            Shared = ArrayPool<T>.Create(256 * 1024 * 1024, 5);
        }
    }
}
