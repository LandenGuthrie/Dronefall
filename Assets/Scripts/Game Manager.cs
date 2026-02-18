using System;
using UnityEngine;
using Random = UnityEngine.Random;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("Island Settings")]
    public TerrainGenerator TerrainGenerator;
    public RandomRangeNoiseSettings TerrainSettings;
    
    public void Start()
    {
        Instance = this;
        
        // Generating random island
        TerrainGenerator.InitializeTerrainGenerator();
        GenerateRandomIsland();
    }

    public void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space)) GenerateRandomIsland();
    }

    public void GenerateRandomIsland()
    {
        var randomSeed = Random.Range(0, int.MaxValue);
        
        var frequency = Random.Range(TerrainSettings.FrequencyRange.x, TerrainSettings.FrequencyRange.y);
        var amplitude = Random.Range(TerrainSettings.AmplitudeRange.x, TerrainSettings.AmplitudeRange.y);
        var octaves = Random.Range(TerrainSettings.OctavesRange.x, TerrainSettings.OctavesRange.y);
        var lacunarity = Random.Range(TerrainSettings.LacunarityRange.x, TerrainSettings.LacunarityRange.y);
        var persistence = Random.Range(TerrainSettings.PersistenceRange.x, TerrainSettings.PersistenceRange.y);

        var terrainHeightMultiplier = Random.Range(TerrainSettings.TerrainHeightMultiplier.x, TerrainSettings.TerrainHeightMultiplier.y);
        
        var terrainNoiseSettings = new TerrainNoiseSettings()
        {
            Frequency = frequency,
            Amplitude = amplitude,
            Octaves = octaves,
            Lacunarity = lacunarity,
            Persistence = persistence
        };
        
        TerrainGenerator.GenerateTerrain(TerrainGenerator.TerrainMeshSize, randomSeed, terrainNoiseSettings, terrainHeightMultiplier);
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
}
