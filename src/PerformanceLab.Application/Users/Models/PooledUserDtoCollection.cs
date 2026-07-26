using System.Buffers;
using System.Collections;

namespace PerformanceLab.Application.Users.Models;

public sealed class PooledUserDtoCollection : IReadOnlyList<UserDto>, IDisposable
{
    private readonly UserDto[] _rentedArray;
    private readonly int _count;
    private bool _disposed;

    public PooledUserDtoCollection(UserDto[] rentedArray, int count)
    {
        _rentedArray = rentedArray;
        _count = count;
    }

    public int Count => _count;

    public UserDto this[int index]
    {
        get
        {
            if (index < 0 || index >= _count)
                throw new IndexOutOfRangeException();
            return _rentedArray[index];
        }
    }

    public IEnumerator<UserDto> GetEnumerator()
    {
        for (int i = 0; i < _count; i++)
        {
            yield return _rentedArray[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Dispose()
    {
        if (_disposed) return;
        
        // Clear DTO properties to avoid stale data in pool
        for (int i = 0; i < _count; i++)
        {
            _rentedArray[i].Id = 0;
            _rentedArray[i].Name = string.Empty;
        }
        
        ArrayPool<UserDto>.Shared.Return(_rentedArray);
        _disposed = true;
    }
}
