using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("Dependencies")]
    public TerrainGenerator TerrainGenerator;
    public GrassGenerator GrassGenerator;
    public FoliageGenerator FoliageGenerator;
    public EnemyGenerator EnemyGenerator;
    
    [Header("Settings")]
    public RandomRangeNoiseSettings TerrainSettings; 
    public int GrassLayerIndex;
    
    [Header("Testing")]
    public Image GrassMaskViewImage;
    
    public void Start()
    {
        Instance = this;
        
        // Generating random island
        var terrainSize = TerrainGenerator.TerrainMeshSize;
        var terrainPosition = TerrainGenerator.transform.position;
        
        GrassGenerator.SetCachedData(TerrainGenerator.TerrainMeshSize, TerrainGenerator.transform.position);
        GrassSettings grassSettings = new(new float[GrassGenerator.InstanceCount], Texture2D.whiteTexture);
        GrassGenerator.InitializeGrassGenerator(grassSettings);
        
        TerrainGenerator.InitializeTerrainGenerator();
        FoliageGenerator.InitializeComputeShader();
        EnemyGenerator.InitializeEnemyGenerator();
        GenerateRandomIsland();
    }

    public void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space)) GenerateRandomIsland();
    }

    public void GenerateRandomIsland()
    {
        // Terrain
        var randomSeed = Random.Range(0, int.MaxValue);
        var frequency = Random.Range(TerrainSettings.FrequencyRange.x, TerrainSettings.FrequencyRange.y);
        var amplitude = Random.Range(TerrainSettings.AmplitudeRange.x, TerrainSettings.AmplitudeRange.y);
        var octaves = Random.Range(TerrainSettings.OctavesRange.x, TerrainSettings.OctavesRange.y);
        var lacunarity = Random.Range(TerrainSettings.LacunarityRange.x, TerrainSettings.LacunarityRange.y);
        var persistence = Random.Range(TerrainSettings.PersistenceRange.x, TerrainSettings.PersistenceRange.y);
        var terrainHeightMultiplier = Random.Range(TerrainSettings.TerrainHeightMultiplier.x, TerrainSettings.TerrainHeightMultiplier.y);
        var allowedSteppedHeights = TerrainSettings.AllowedSteppedHeights[Random.Range(0, TerrainSettings.AllowedSteppedHeights.Count)];
        
        var terrainNoiseSettings = new TerrainSettings()
        {
            Frequency = frequency,
            Amplitude = amplitude,
            Octaves = octaves,
            Lacunarity = lacunarity,
            Persistence = persistence,
            TerrainHeightMultiplier = terrainHeightMultiplier,
            UseSteppedHeights = true,
            AllowedSteppedHeights = allowedSteppedHeights.Values
        };
        
        TerrainGenerator.GenerateTerrain(randomSeed, terrainNoiseSettings);
        GrassGenerator.UpdateGrassMesh(TerrainGenerator.CalculateHeightsID(GrassGenerator.Resolution, terrainHeightMultiplier), TerrainGenerator.TerrainLayers[GrassLayerIndex].LayerMask);
        FoliageGenerator.UpdateComputeShader();
        
        // Creating grass mask view image texture
        if (GrassMaskViewImage.isActiveAndEnabled)
        {
            var tex = TerrainGenerator.TerrainLayers[GrassLayerIndex].LayerMask;
            var sprite = Sprite.Create(tex, new Rect(Vector2.zero, new Vector2(tex.width, tex.height)), new Vector2(0.5f, 0.5f));
            GrassMaskViewImage.sprite = sprite;
        }
        
        // Enemies
        EnemyGenerator.ReturnAllEnemiesToPool();
        var randomWeakEnemyCount = EnemyGenerator.CACHED_ENEMY_COUNT;
        var spawnSettings = new EnemySpawnSettings(
            new Dictionary<EnemyType, int>()
            {
                { EnemyType.Weak, randomWeakEnemyCount }
            }, 10, new Vector2(4, 8));
        EnemyGenerator.SpawnEnemies(spawnSettings);
    }
}

[Serializable]
public struct RandomRangeNoiseSettings
{
    public Vector2 FrequencyRange;
    public Vector2 AmplitudeRange;
    public Vector2Int OctavesRange;
    public Vector2 LacunarityRange;
    public Vector2 PersistenceRange;

    public Vector2 TerrainHeightMultiplier;
    
    public List<ArrayWrapper> AllowedSteppedHeights;
}

[Serializable]
public struct ArrayWrapper
{
    public string Name;
    public float[] Values;
}
