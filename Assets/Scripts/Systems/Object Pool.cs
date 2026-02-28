using System;
using System.Collections.Generic;
using System.Linq;

public class ObjectPool<T> where T : class
{
    public ObjectPool(int count, int expansion, Func<T> factory)
    {
        _expansion = expansion;
        _factory = factory;
        Expand(count);
    }

    public virtual void OnObjectTakenFromPool(T obj) {}
    public virtual void OnObjectReturnedToPool(T obj) {}
    public virtual void OnObjectDestroyed(T obj) {}

    public T[] GetObjectsOutOfPool()
    {
        return (from obj in _objects where !obj.Value select obj.Key).ToArray();
    }
    public T[] GetObjectsInPool()
    {
        return (from obj in _objects where obj.Value select obj.Key).ToArray();
    }

    public T GetFromPool()
    {
        // Expanding if there are no objects in pool
        if (ObjectsInPool() <= 0) Expand(_expansion);
        
        // Getting the next object in pool
        var obj = _objects.Where(o => o.Value).Select(o => o.Key).FirstOrDefault();
        if (obj == null) throw new Exception("Max capacity has been reached.");

        // Marking object as not in pool
        _objects[obj] = false;
        
        // Returning object
        OnObjectTakenFromPool(obj);
        return obj;
    }
    public void ReturnToPool(T obj)
    {
        OnObjectReturnedToPool(obj);
        _objects[obj] = true;
    }
    public void ReturnAllToPool()
    {
        for (var i = 0; i < _objects.Count; i++)
        {
            ReturnToPool(_objects.ElementAt(i).Key);
        }
    }

    public void DestroyPool()
    {
        foreach (var obj in _objects) OnObjectDestroyed(obj.Key);
    }
    public int ObjectsInPool()
    {
        return _objects.Count(obj => obj.Value);
    }

    private void Expand(int count)
    {
        for (var i = 0; i < count; i++)
        {
            _objects.Add(_factory(), true);
        }
    }
    
    private readonly Func<T> _factory;
    private readonly int _expansion;
    private readonly Dictionary<T, bool> _objects = new();
}
