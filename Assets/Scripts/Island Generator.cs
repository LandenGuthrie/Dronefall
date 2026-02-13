using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class IslandGenerator : MonoBehaviour
{
    private static readonly Vector2Int TerrainTextureSize = new(512, 512);

    [Header("Height Settings")]
    [Tooltip("The terrain will snap to these specific height values (0.0 to 1.0).")]
    public List<float> AllowedHeights = new List<float> { 0.2f, 0.4f, 0.6f, 0.8f };
    
    [Header("Macro Noise (Shape)")]
    [SerializeField] private TerrainNoiseSettings BaseNoiseSettings;
    [SerializeField] private AnimationCurve IslandCurve;

    [Header("Micro Noise (Bumpiness)")]
    [Tooltip("Small details added ON TOP of the flat heights.")]
    [SerializeField] private TerrainNoiseSettings MicroNoiseSettings;
    [Range(0f, 1f)] public float MicroNoiseStrength = 0.02f;

    [Header("Visual Settings")]
    [Range(1f, 50f)] public float ColorBlendSharpness = 1.0f;
    [SerializeField] private List<TerrainLayer> TerrainLayers;

    [Header("Slope Settings")]
    [Range(0, 10)] public int SlopeWidth = 3; 

    [Header("Mesh Settings")]
    [Range(10, 255)] public int MeshResolution = 100; 
    public float MeshSize = 100f;
    public float HeightMultiplier = 20f;

    [Header("Material Settings")] 
    public Material TerrainMaterial;
    public string TerrainMaterialTextureID = "_MainTex";
    
    private float[,] _cachedHeightMap;

    private void Start()
    {
        // Sort layers and heights for consistent logic
        TerrainLayers.Sort((a, b) => a.StartHeight.CompareTo(b.StartHeight));
        AllowedHeights.Sort();
        
        var texture = GenerateTerrainTexture();
        GenerateTerrainMesh();

        if (TerrainMaterial != null)
        {
            TerrainMaterial.SetTexture(TerrainMaterialTextureID, texture);
            GetComponent<MeshRenderer>().material = TerrainMaterial;
        }
    }

    private Texture2D GenerateTerrainTexture()
    {
        var texture = new Texture2D(TerrainTextureSize.x, TerrainTextureSize.y)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        _cachedHeightMap = new float[TerrainTextureSize.x, TerrainTextureSize.y];
        float[,] rawSlopeMap = new float[TerrainTextureSize.x, TerrainTextureSize.y];
        float pixelWorldSize = MeshSize / TerrainTextureSize.x;

        // --- PASS 1: Generate Heights (Snap + Micro Noise) ---
        for (var x = 0; x < TerrainTextureSize.x; x++)
        {
            for (var y = 0; y < TerrainTextureSize.y; y++)
            {
                var u = (float)x / TerrainTextureSize.x;
                var v = (float)y / TerrainTextureSize.y;
                
                // 1. Get Base Shape
                var baseNoiseVal = GetNoise(u, v, BaseNoiseSettings);
                var distanceFromCenter = Vector2.Distance(new Vector2(0.5f, 0.5f), new Vector2(u, v)) * 2;
                var islandMask = IslandCurve.Evaluate(distanceFromCenter);
                var finalBase = baseNoiseVal * islandMask;
                
                // 2. Snap to Allowed Heights
                float snappedHeight = finalBase;
                if (AllowedHeights.Count > 0)
                {
                    snappedHeight = GetClosestAllowedHeight(finalBase);
                }

                // 3. Add Micro Noise (Bumps)
                // We add this AFTER snapping so the flat areas get bumpy
                var microNoiseVal = GetNoise(u, v, MicroNoiseSettings);
                // Center the noise around 0 so it pushes up AND down
                microNoiseVal = (microNoiseVal - 0.5f) * MicroNoiseStrength; 

                // 4. Save Final Height
                // Clamp to ensure we don't go below 0 or crazy high
                _cachedHeightMap[x, y] = Mathf.Clamp01(snappedHeight + microNoiseVal);
            }
        }

        // --- PASS 2: Calculate Slopes (Normalized Angle) ---
        for (var x = 0; x < TerrainTextureSize.x; x++)
        {
            for (var y = 0; y < TerrainTextureSize.y; y++)
            {
                float currentHeight = _cachedHeightMap[x, y];
                float maxDiff = 0f;

                if (x < TerrainTextureSize.x - 1) maxDiff = Mathf.Max(maxDiff, Mathf.Abs(currentHeight - _cachedHeightMap[x + 1, y]));
                if (y < TerrainTextureSize.y - 1) maxDiff = Mathf.Max(maxDiff, Mathf.Abs(currentHeight - _cachedHeightMap[x, y + 1]));
                if (x > 0) maxDiff = Mathf.Max(maxDiff, Mathf.Abs(currentHeight - _cachedHeightMap[x - 1, y]));
                if (y > 0) maxDiff = Mathf.Max(maxDiff, Mathf.Abs(currentHeight - _cachedHeightMap[x, y - 1]));

                float rise = maxDiff * HeightMultiplier;
                float run = pixelWorldSize;
                float angle = Mathf.Atan(rise / run) * Mathf.Rad2Deg;
                
                rawSlopeMap[x, y] = angle / 90f; 
            }
        }

        // --- PASS 3: Dilate Slopes (Thicken Lines) ---
        float[,] dilatedSlopeMap = new float[TerrainTextureSize.x, TerrainTextureSize.y];
        
        for (var x = 0; x < TerrainTextureSize.x; x++)
        {
            for (var y = 0; y < TerrainTextureSize.y; y++)
            {
                float maxSlope = rawSlopeMap[x, y];

                for (int dx = -SlopeWidth; dx <= SlopeWidth; dx++)
                {
                    for (int dy = -SlopeWidth; dy <= SlopeWidth; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx;
                        int ny = y + dy;

                        if (nx >= 0 && nx < TerrainTextureSize.x && ny >= 0 && ny < TerrainTextureSize.y)
                        {
                            if (rawSlopeMap[nx, ny] > maxSlope) maxSlope = rawSlopeMap[nx, ny];
                        }
                    }
                }
                dilatedSlopeMap[x, y] = maxSlope;
            }
        }

        // --- PASS 4: Generate Colors ---
        var textureColors = new Color[TerrainTextureSize.x * TerrainTextureSize.y];

        for (var x = 0; x < TerrainTextureSize.x; x++)
        {
            for (var y = 0; y < TerrainTextureSize.y; y++)
            {
                var currentHeight = _cachedHeightMap[x, y];
                var pixelData = GetTerrainData(currentHeight);
                var finalColor = pixelData.BiomeColor;

                var slopeAmount = dilatedSlopeMap[x, y];

                if (slopeAmount >= pixelData.SlopeThreshold)
                {
                    float range = Mathf.Max(pixelData.SlopeBlend, 0.0001f);
                    float rockMix = (slopeAmount - pixelData.SlopeThreshold) / range;
                    rockMix = Mathf.Clamp01(rockMix);
                    finalColor = Color.Lerp(finalColor, pixelData.SlopeColor, rockMix);
                }

                textureColors[x + y * TerrainTextureSize.x] = finalColor;
            }
        }

        texture.SetPixels(textureColors);
        texture.Apply();
        return texture;
    }

    private float GetClosestAllowedHeight(float height)
    {
        // Basic closest-value search
        float closest = AllowedHeights[0];
        float minDiff = Mathf.Abs(height - closest);

        for (int i = 1; i < AllowedHeights.Count; i++)
        {
            float diff = Mathf.Abs(height - AllowedHeights[i]);
            if (diff < minDiff)
            {
                minDiff = diff;
                closest = AllowedHeights[i];
            }
        }
        return closest;
    }

    private void GenerateTerrainMesh()
    {
        if (_cachedHeightMap == null) return;

        Mesh mesh = new Mesh();
        mesh.name = "ProceduralTerrain";

        Vector3[] vertices = new Vector3[(MeshResolution + 1) * (MeshResolution + 1)];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[MeshResolution * MeshResolution * 6];

        float offset = MeshSize * 0.5f;

        for (int i = 0, z = 0; z <= MeshResolution; z++)
        {
            for (int x = 0; x <= MeshResolution; x++)
            {
                float u = (float)x / MeshResolution;
                float v = (float)z / MeshResolution;

                int texX = Mathf.Clamp(Mathf.RoundToInt(u * (TerrainTextureSize.x - 1)), 0, TerrainTextureSize.x - 1);
                int texY = Mathf.Clamp(Mathf.RoundToInt(v * (TerrainTextureSize.y - 1)), 0, TerrainTextureSize.y - 1);

                float height = _cachedHeightMap[texX, texY] * HeightMultiplier;

                vertices[i] = new Vector3((u * MeshSize) - offset, height, (v * MeshSize) - offset);
                uvs[i] = new Vector2(u, v);
                i++;
            }
        }

        int tri = 0;
        int vert = 0;

        for (int z = 0; z < MeshResolution; z++)
        {
            for (int x = 0; x < MeshResolution; x++)
            {
                triangles[tri + 0] = vert + 0;
                triangles[tri + 1] = vert + MeshResolution + 1;
                triangles[tri + 2] = vert + 1;
                triangles[tri + 3] = vert + 1;
                triangles[tri + 4] = vert + MeshResolution + 1;
                triangles[tri + 5] = vert + MeshResolution + 2;

                vert++;
                tri += 6;
            }
            vert++;
        }

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();

        GetComponent<MeshFilter>().mesh = mesh;
        GetComponent<MeshCollider>().sharedMesh = mesh;
    }

    private TerrainData GetTerrainData(float height)
    {
        for (var i = 0; i < TerrainLayers.Count - 1; i++)
        {
            var layerA = TerrainLayers[i];
            var layerB = TerrainLayers[i+1];

            if (!(height >= layerA.StartHeight) || !(height <= layerB.StartHeight)) continue;
            
            var t = (height - layerA.StartHeight) / (layerB.StartHeight - layerA.StartHeight);
            t = (t - 0.5f) * ColorBlendSharpness + 0.5f;
            t = Mathf.Clamp01(t);

            return new TerrainData
            {
                BiomeColor = Color.Lerp(layerA.TerrainColor, layerB.TerrainColor, t),
                SlopeColor = Color.Lerp(layerA.SlopeColor, layerB.SlopeColor, t),
                SlopeThreshold = Mathf.Lerp(layerA.SlopeThreshold, layerB.SlopeThreshold, t),
                SlopeBlend = Mathf.Lerp(layerA.SlopeBlend, layerB.SlopeBlend, t)
            };
        }

        return LayerToData(height < TerrainLayers[0].StartHeight 
            ? TerrainLayers[0] 
            : TerrainLayers[^1]);
    }

    private TerrainData LayerToData(TerrainLayer layer)
    {
        return new TerrainData
        {
            BiomeColor = layer.TerrainColor,
            SlopeColor = layer.SlopeColor,
            SlopeThreshold = layer.SlopeThreshold,
            SlopeBlend = layer.SlopeBlend
        };
    }

    // Now accepts "settings" as a parameter so we can reuse it
    private float GetNoise(float u, float v, TerrainNoiseSettings settings)
    {
        var totalNoise = 0f;
        var frequency = settings.Frequency;
        var amplitude = settings.Amplitude;
        var maxVal = 0f;

        for (var i = 0; i < settings.Octaves; i++)
        {
            totalNoise += Mathf.PerlinNoise(u * frequency, v * frequency) * amplitude;
            maxVal += amplitude;
            frequency *= settings.Lacunarity;
            amplitude *= settings.Persistence;
        }
        
        if (maxVal > 0) totalNoise /= maxVal;
        
        return Mathf.Clamp01(totalNoise);
    }
}

[Serializable]
public struct TerrainLayer
{
    public string Name;
    [Range(0f, 1f)] public float StartHeight;
    public Color TerrainColor;

    [Header("Slope Settings")]
    public Color SlopeColor;
    [Range(0f, 1f)] public float SlopeThreshold;
    [Range(0f, 0.2f)] public float SlopeBlend;
}

[Serializable]
public struct TerrainNoiseSettings
{
    public float Frequency;
    public float Amplitude;
    public int Octaves;
    public float Lacunarity;
    public float Persistence;
}

public struct TerrainData
{
    public Color BiomeColor;
    public Color SlopeColor;
    public float SlopeThreshold;
    public float SlopeBlend;
}