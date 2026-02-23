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

    public void InitializeEnemyGenerator() => CreateEnemyCache();
    private void CreateEnemyCache()
    {
        var enemyTypesCount = Enum.GetNames(typeof(EnemyType)).Length;
        for (var i = 0; i < enemyTypesCount; i++)
        {
            for (var j = 0; j < CACHED_ENEMY_COUNT; j++)
            {
                var type = (EnemyType)i;
                var enemyInstance = CreateEnemy(type);
                _cachedEnemyPool.Add(enemyInstance, (true, type));
            }
        }
    }

    // --- PUBLIC FUNCTIONS ---
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
    public void PlaceEnemy(EnemyType type, Vector3 position, Quaternion rotation)
    {
        var enemy = GetCachedEnemyFromPool(type);
        _cachedEnemyPool[enemy] = (false, type);
        enemy.transform.position = position;
        enemy.transform.rotation = rotation;
        enemy.SetActive(true);
    }
    public void ReturnEnemyToPool(GameObject enemyInstance)
    {
        var enemyType = enemyInstance.TryGetComponent(out BaseEnemy enemyComp)
            ? enemyComp.Type
            : throw new Exception("Enemy does not contain a 'BaseEnemy' component.");

        enemyInstance.SetActive(false);
        enemyInstance.transform.position = Vector3.zero;
        enemyInstance.transform.rotation = Quaternion.identity;
        _cachedEnemyPool[enemyInstance] = (true, enemyType);
    }
    public void ReturnEnemyToPool(GameObject enemyInstance, EnemyType type)
    {
        enemyInstance.SetActive(false);
        enemyInstance.transform.position = Vector3.zero;
        enemyInstance.transform.rotation = Quaternion.identity;
        _cachedEnemyPool[enemyInstance] = (true, type);
    }
    public void ReturnAllEnemiesToPool()
    {
        for (var i = 0; i < _cachedEnemyPool.Count; i++)
        {
            var (instance, (inPool, type)) = _cachedEnemyPool.ElementAt(i);
            if (inPool) continue;
            ReturnEnemyToPool(instance, type);
        }
    }

    // --- UTIL ---
    private GameObject CreateEnemy(EnemyType type)
    {
        var prefab = GetEnemyPrefabFromType(type);
        var enemyInstance = Instantiate(prefab, transform.position, Quaternion.identity, transform);
        enemyInstance.SetActive(false);
        return enemyInstance;
    }
    private GameObject GetEnemyPrefabFromType(EnemyType type)
    {
        foreach (var enemy in EnemyTypes.Where(enemy => enemy.Type == type))
            return enemy.Prefab;
        throw new Exception("No enemy prefab found from the given type.");
    }
    private GameObject GetCachedEnemyFromPool(EnemyType type)
    {
        foreach (var enemy in _cachedEnemyPool.Where(enemy => enemy.Value.inPool && enemy.Value.enemyType == type))
            return enemy.Key;
        throw new Exception("No enemy available with the given type.");
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

    private readonly Dictionary<GameObject, (bool inPool, EnemyType enemyType)> _cachedEnemyPool = new();
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

[Serializable]
public struct Enemy
{
    public EnemyType Type;
    public GameObject Prefab;
}