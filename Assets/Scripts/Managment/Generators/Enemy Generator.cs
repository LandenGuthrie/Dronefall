using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

public class EnemyGenerator : MonoBehaviour
{
    public const int CACHED_ENEMY_COUNT = 16; // Perfect square
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
    public void SpawnEnemies(EnemySpawnSettings settings)
    {
        var totalEnemies = settings.EnemySpawningAmount.Sum(kvp => kvp.Value);
        if (totalEnemies == 0) return;

        var spawnPointsXZ = GenerateUniformGridPoints(totalEnemies);

        var pointIndex = 0;
        foreach (var (type, amount) in settings.EnemySpawningAmount)
        {
            if (amount > CACHED_ENEMY_COUNT) throw new Exception("Cant request more enemies than what is cached.");
            
            for (var i = 0; i < amount; i++)
            {
                if (pointIndex >= spawnPointsXZ.Count) break; 

                var positionXZ = spawnPointsXZ[pointIndex];
                pointIndex++;

                // Adding random offset
                var xRange = Random.Range(settings.RandomOffset.x, settings.RandomOffset.y);
                var zRange = Random.Range(settings.RandomOffset.x, settings.RandomOffset.y);

                // 2. Flip a coin for the X and Z directions independently!
                // Random.value > 0.5f gives a true 50% chance to be positive or negative
                float signX = Random.value > 0.5f ? 1f : -1f;
                float signZ = Random.value > 0.5f ? 1f : -1f;

                // Apply the offset and the randomized direction
                positionXZ += new Vector2(xRange * signX, zRange * signZ);
                
                var height = GetTargetEnemyHeight(new Vector3(positionXZ.x, 0, positionXZ.y));
                var finalPosition = new Vector3(positionXZ.x, height, positionXZ.y);
                
                float randomRotation = Random.Range(0, 360);
                
                
                PlaceEnemy(type, finalPosition, Quaternion.Euler(0, randomRotation, 0));
            }
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
        
        var enemyInstance = Instantiate(prefab, transform.position, Quaternion.identity);
        enemyInstance.SetActive(false);

        return enemyInstance;
    }
    private GameObject GetEnemyPrefabFromType(EnemyType type)
    {
        foreach (var enemy in EnemyTypes.Where(enemy => enemy.Type == type))
        {
            return enemy.Prefab;
        }

        throw new Exception("No enemy prefab found from the given type.");
    }
    private GameObject GetCachedEnemyFromPool(EnemyType type)
    {
        foreach (var enemy in _cachedEnemyPool.Where(enemy => enemy.Value.inPool && enemy.Value.enemyType == type))
        {
            return enemy.Key;
        }
        throw new Exception("No enemy available with the given type.");
    }
    
    private List<Vector2> GenerateUniformGridPoints(int totalEnemies)
    {
        var points = new List<Vector2>();

        var width = WorldSpawningBounds.size.x;
        var depth = WorldSpawningBounds.size.z;

        // 1. Calculate the optimal number of columns and rows to fit the exact amount of enemies
        // while keeping the grid proportionally scaled to the bounds (so cells stay roughly square)
        var ratio = width / depth;
        var cols = Mathf.CeilToInt(Mathf.Sqrt(totalEnemies * ratio));
        var rows = Mathf.CeilToInt((float)totalEnemies / cols);

        // 2. Fix the target value by using the size of the bounds.
        // This naturally solves your rule: it spans the whole island, and shrinks ONLY if it needs to.
        var cellWidth = width / cols;
        var cellDepth = depth / rows;

        // 3. Find our starting point (the exact center of the bottom-leftmost cell)
        var startX = WorldSpawningBounds.min.x + (cellWidth / 2f);
        var startZ = WorldSpawningBounds.min.z + (cellDepth / 2f);

        // 4. Loop through and create a point for every enemy requested
        for (var i = 0; i < totalEnemies; i++)
        {
            var row = i / cols;
            var col = i % cols;

            var x = startX + (col * cellWidth);
            var z = startZ + (row * cellDepth);

            points.Add(new Vector2(x, z));
        }

        return points;
    }
    private float GetTargetEnemyHeight(Vector3 position)
    {
        var ray = new Ray(new Vector3(position.x, TARGET_Y_FINDER_HEIGHT, position.z), Vector3.down);
        if (Physics.Raycast(ray, out var hit, TARGET_Y_FINDER_HEIGHT * 2, TerrainLayers))
        {
            return hit.point.y;
        }

        throw new Exception("Unable to find height at position.");
    }
    
    private readonly Dictionary<GameObject, (bool inPool, EnemyType enemyType)> _cachedEnemyPool = new();
}

public struct EnemySpawnSettings
{
    public readonly Dictionary<EnemyType, int> EnemySpawningAmount;
    public float TargetEnemySpawnDistance;
    public Vector2 RandomOffset;
    
    public EnemySpawnSettings(Dictionary<EnemyType, int> enemySpawningAmount, float targetSpawnDistance, Vector2 randomOffset)
    {
        EnemySpawningAmount = enemySpawningAmount;
        TargetEnemySpawnDistance = targetSpawnDistance;
        RandomOffset = randomOffset;
    }
}

[Serializable]
public struct Enemy
{
    public EnemyType Type;
    public GameObject Prefab;
}
