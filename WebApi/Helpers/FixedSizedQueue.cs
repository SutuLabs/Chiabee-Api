namespace WebApi.Helpers
{
    using System.Collections.Concurrent;

    public class FixedSizedQueue<T>
    {
        public FixedSizedQueue()
            : this(100)
        {
        }

        public FixedSizedQueue(int limit)
        {
            this.Limit = limit;
        }

        private readonly ConcurrentQueue<T> q = new();

        public int Limit { get; init; }

        public void Enqueue(T obj)
        {
            q.Enqueue(obj);
            while (q.Count > Limit && q.TryDequeue(out _)) ;
        }

        public T[] ToArray()
        {
            return q.ToArray();
        }
    }
}