public interface IWriter<T> : IDisposable
{
    void Write(T type);
}