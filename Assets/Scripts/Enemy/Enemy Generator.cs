using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

public class EnemyGenerator : MonoBehaviour
{
    public const int CACHED_ENEMY_COUNT = 16;
    public const int TARGET_Y_FINDER_HEIGHT = 100;

    [SerializeField] private List<Enemy> EnemyTypes;
    [SerializeField] private Bounds WorldSpawningBounds;
    [SerializeField] private LayerMask TerrainLayers;

    public LayerMask TowerLayers;
    
    public void InitializeEnemyGenerator() => CreateEnemyCache();
    private void CreateEnemyCache()
    {
        var enemyTypesCount = Enum.GetNames(typeof(EnemyType)).Length;
        for (var i = 0; i < enemyTypesCount; i++)
        {
            var type = (EnemyType)i;
            var pool = new EnemyPool(16, 16, transform, GetEnemyPrefabFromType(type));
            _enemyPools.Add(type, pool);
        }
    }

    // --- Execution ---
    public void SpawnEnemies(EnemySpawnSettings settings, Texture2D spawnMask = null)
    {
        var totalEnemies = settings.EnemySpawningAmount.Sum(kvp => kvp.Value);
        if (totalEnemies == 0) return;

        var sampler = new MitchellsBestCandidateSampler(WorldSpawningBounds, WorldSpawningBounds, spawnMask);

        foreach (var (type, amount) in settings.EnemySpawningAmount)
        {
            if (amount > CACHED_ENEMY_COUNT) throw new Exception("Cant request more enemies than what is cached.");

            var placed = 0;
            int maxAttempts = amount * 20; 
            int attempts = 0;

            while (placed < amount && attempts < maxAttempts)
            {
                attempts++;

                // TryGetNextCandidate inherently spaces things out uniformly
                if (sampler.TryGetNextCandidate(out var positionXZ))
                {
                    if (TryGetEnemyHeight(new Vector3(positionXZ.x, 0f, positionXZ.y), out var height))
                    {
                        PlaceEnemy(type, new Vector3(positionXZ.x, height, positionXZ.y), Quaternion.Euler(0, Random.Range(0, 360), 0));
                        
                        // CRITICAL: Only tell the sampler this spot is taken AFTER physics passes
                        sampler.RegisterAcceptedPosition(positionXZ);
                        placed++; 
                    }
                }
            }

            if (placed < amount)
                Debug.LogWarning($"[EnemyGenerator] Placed {placed}/{amount}. Out of physical space.");
        }
    }
    
    public void PlaceEnemy(EnemyType type, Vector3 position, Quaternion rotation) =>
        _enemyPools[type].GetEnemyFromPool(position, rotation);
    public void ReturnEnemyToPool(GameObject enemy)
    {
        var enemyComp = enemy.GetComponentInChildren<EnemyBase>();
        _enemyPools[enemyComp.Type].ReturnToPool(enemy);
    }
    public void ReturnAllEnemiesToPool()
    {
        foreach (var pool in _enemyPools) pool.Value.ReturnAllToPool();
    }
    
    // --- UTIL ---
    private GameObject GetEnemyPrefabFromType(EnemyType type)
    {
        foreach (var enemy in EnemyTypes.Where(enemy => enemy.Type == type))
            return enemy.Prefab;
        throw new Exception("No enemy prefab found from the given type.");
    }
    private bool TryGetEnemyHeight(Vector3 position, out float height)
    {
        var ray = new Ray(new Vector3(position.x, TARGET_Y_FINDER_HEIGHT, position.z), Vector3.down);
        if (Physics.Raycast(ray, out var hit, TARGET_Y_FINDER_HEIGHT * 2, TerrainLayers))
        {
            height = hit.point.y;
            return true;
        }
        height = 0f;
        return false;
    }

    private readonly Dictionary<EnemyType, EnemyPool> _enemyPools = new();
}

public struct EnemySpawnSettings
{
    public readonly Dictionary<EnemyType, int> EnemySpawningAmount;
    public Vector2 RandomOffset;

    public EnemySpawnSettings(Dictionary<EnemyType, int> enemySpawningAmount, Vector2 randomOffset)
    {
        EnemySpawningAmount = enemySpawningAmount;
        RandomOffset = randomOffset;
    }
}

public class EnemyPool : ObjectPool<GameObject>
{
    public EnemyPool(int count, int expansion, 
        Transform parent, GameObject prefab) 
        : base(count, expansion, () => CreateEnemy(parent, prefab)) {}
    
    public void GetEnemyFromPool(Vector3 position, Quaternion rotation)
    {
        var enemy = GetFromPool();
        enemy.transform.position = position;
        enemy.transform.rotation = rotation;
    }

    private static GameObject CreateEnemy(Transform parent, GameObject prefab)
    {
        var enemyInstance = GameObject.Instantiate(prefab, Vector3.zero, Quaternion.identity, parent);
        enemyInstance.SetActive(false);
        return enemyInstance;
    }
    
    public override void OnObjectTakenFromPool(GameObject obj)
    {
        base.OnObjectTakenFromPool(obj);
        obj.SetActive(true);
    }
    public override void OnObjectReturnedToPool(GameObject obj)
    {
        base.OnObjectReturnedToPool(obj);
        obj.SetActive(false);
        obj.transform.position = Vector3.zero;
        obj.transform.rotation = Quaternion.identity;
    }
    public override void OnObjectDestroyed(GameObject obj)
    {
        base.OnObjectDestroyed(obj);
        GameObject.Destroy(obj);
    }
}

[Serializable]
public struct Enemy
{
    public EnemyType Type;
    public GameObject Prefab;
}