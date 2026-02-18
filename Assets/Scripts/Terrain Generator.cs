using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class TerrainGenerator : MonoBehaviour
{
    private static readonly Vector2Int TerrainTextureSize = new(512, 512);

    [Header("Terrain Settings")] 
    public int TerrainMeshSize = 100;
    [SerializeField] private List<float> AllowedNormalizedHeights;
    [SerializeField] private AnimationCurve IslandHeightCurve;
    [SerializeField] private List<TerrainLayer> TerrainLayers;
    [Range(1f, 50f)] [SerializeField] private float ColorBlendSharpness = 1.0f;
    [Range(0, 10)] [SerializeField] private int SlopeWidth = 3; 

    [Header("Noise Settings")]
    [SerializeField] private TerrainNoiseSettings OverlayNoiseSettings;
    [SerializeField] [Range(0f, 1f)] private float OverlayNoiseStrength;
    
    [Header("Material Settings")] 
    [SerializeField] private Material TerrainMaterial;
    [SerializeField] private string TerrainMaterialTextureID = "_MainTex";
    
    [Header("Grass Settings")] 
    [SerializeField] private GrassGenerator GrassGenerator;
    [SerializeField] private float GrassDensity = 0.5f;
    [Range(0, 1)] [SerializeField] private float GrassGenerationThreshold = 0.5f;

    public void OnDestroy()
    {
        DestroyImmediate(_terrainColorMap);
        DestroyImmediate(_terrainGrassMask);
    }

    public void InitializeTerrainGenerator()
    {
        // Sort layers and heights for consistent logic
        TerrainLayers.Sort((a, b) => a.StartHeight.CompareTo(b.StartHeight));
        AllowedNormalizedHeights.Sort();
        
        // Updating grass with height buffer - pass density so resolution matches
        _grassResolution = Mathf.CeilToInt(TerrainMeshSize / GrassDensity);
        _grassInstanceCount = _grassResolution * _grassResolution;
    }
    
    public void GenerateTerrain(int terrainMeshResolution, int seed,
        TerrainNoiseSettings noiseSettings, float terrainHeightMultiplier)
    {
        // Destroying old textures
        if (_terrainColorMap) DestroyImmediate(_terrainColorMap);
        if (_terrainColorMap) DestroyImmediate(_terrainGrassMask);
        
        GenerateTerrainTextures(noiseSettings, terrainHeightMultiplier, seed);
        GenerateTerrainMesh(terrainMeshResolution, terrainHeightMultiplier);

        var grassHeights = CalculateGrassHeights(terrainHeightMultiplier);
        
        // Updating material texture
        TerrainMaterial.SetTexture(TerrainMaterialTextureID, _terrainColorMap);
        GetComponent<MeshRenderer>().material = TerrainMaterial;
        
        if (!GrassGenerator.IsInitialized)
        {
            GrassGenerator.InitializeGrassGenerator(_grassResolution, _grassInstanceCount,
                TerrainMeshSize, 
                transform.position,
                GrassDensity,
                grassHeights,
                _terrainGrassMask);
            return;
        }
        GrassGenerator.UpdateGrassMesh(
            transform.position,
            GrassDensity,
            grassHeights,
            _terrainGrassMask); 
            
    }
    
    private void GenerateTerrainMesh(int terrainMeshResolution, float terrainHeightMultiplier)
    {
        if (_cachedHeightMap == null) return;

        var mesh = new Mesh
        {
            name = "ProceduralTerrain"
        };

        var vertices = new Vector3[(terrainMeshResolution + 1) * (terrainMeshResolution + 1)];
        var uvs = new Vector2[vertices.Length];
        var triangles = new int[terrainMeshResolution * terrainMeshResolution * 6];

        var offset = TerrainMeshSize * 0.5f;

        for (int i = 0, z = 0; z <= terrainMeshResolution; z++)
        {
            for (var x = 0; x <= terrainMeshResolution; x++)
            {
                var u = (float)x / terrainMeshResolution;
                var v = (float)z / terrainMeshResolution;

                var texX = Mathf.Clamp(Mathf.RoundToInt(u * (TerrainTextureSize.x - 1)), 0, TerrainTextureSize.x - 1);
                var texY = Mathf.Clamp(Mathf.RoundToInt(v * (TerrainTextureSize.y - 1)), 0, TerrainTextureSize.y - 1);

                var height = _cachedHeightMap[texX, texY] * terrainHeightMultiplier;
                
                vertices[i] = new Vector3((u * TerrainMeshSize) - offset, height, (v * TerrainMeshSize) - offset);
                uvs[i] = new Vector2(u, v);
                i++;
            }
        }

        var tri = 0;
        var vert = 0;

        for (var z = 0; z < terrainMeshResolution; z++)
        {
            for (var x = 0; x < terrainMeshResolution; x++)
            {
                triangles[tri + 0] = vert + 0;
                triangles[tri + 1] = vert + terrainMeshResolution + 1;
                triangles[tri + 2] = vert + 1;
                triangles[tri + 3] = vert + 1;
                triangles[tri + 4] = vert + terrainMeshResolution + 1;
                triangles[tri + 5] = vert + terrainMeshResolution + 2;
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
    
    private float[] CalculateGrassHeights(float terrainHeightMultiplier)
    {
        var resolution = Mathf.CeilToInt(TerrainMeshSize / GrassDensity);
        var grassHeights = new float[resolution * resolution];
        
        for (int row = 0; row < resolution; row++)
        {
            for (int col = 0; col < resolution; col++)
            {
                // Calculate UV coordinates that match the compute shader's logic
                float u = (float)col / resolution;
                float v = (float)row / resolution;
                
                // Sample the height map using the same method as the terrain mesh
                var texX = Mathf.Clamp(Mathf.RoundToInt(u * (TerrainTextureSize.x - 1)), 0, TerrainTextureSize.x - 1);
                var texY = Mathf.Clamp(Mathf.RoundToInt(v * (TerrainTextureSize.y - 1)), 0, TerrainTextureSize.y - 1);
                
                // Store the actual world height (normalized height * multiplier)
                grassHeights[row * resolution + col] = _cachedHeightMap[texX, texY] * terrainHeightMultiplier;
            }
        }
        
        return grassHeights;
    }
    private void GenerateTerrainTextures(TerrainNoiseSettings terrainNoiseSetting, float terrainHeightMultiplier, int seed)
    {
        _terrainColorMap = new Texture2D(TerrainTextureSize.x, TerrainTextureSize.y)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        _terrainGrassMask = new Texture2D(TerrainTextureSize.x, TerrainTextureSize.y)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        _cachedHeightMap = new float[TerrainTextureSize.x, TerrainTextureSize.y];

        var rawSlopeMap = new float[TerrainTextureSize.x, TerrainTextureSize.y];
        var pixelWorldSize = TerrainMeshSize / (float)TerrainTextureSize.x; // Ensure float division
        var grassMaskData = new Color[TerrainTextureSize.x * TerrainTextureSize.y];

        // --- PASS 1: Calculate MACRO Height (Base Shape + Snapping ONLY) ---
        // We do NOT add micro noise yet. We want the slope to ignore the tiny bumps.
        for (var x = 0; x < TerrainTextureSize.x; x++)
        {
            for (var y = 0; y < TerrainTextureSize.y; y++)
            {
                var u = (float)x / TerrainTextureSize.x;
                var v = (float)y / TerrainTextureSize.y;

                // 1. Get Base Shape
                var baseNoiseVal = GetNoise(seed, u, v, terrainNoiseSetting);
                var distanceFromCenter = Vector2.Distance(new Vector2(0.5f, 0.5f), new Vector2(u, v)) * 2;
                var islandMask = IslandHeightCurve.Evaluate(distanceFromCenter);
                var finalBase = baseNoiseVal * islandMask;

                // 2. Snap to Allowed Heights
                var snappedHeight = finalBase;
                if (AllowedNormalizedHeights.Count > 0)
                {
                    snappedHeight = GetClosestAllowedHeight(finalBase);
                }

                // Store the clean "Macro" height for slope calculation
                _cachedHeightMap[x, y] = snappedHeight;
            }
        }

        // --- PASS 2: Calculate Slopes (On the Clean Macro Height) ---
        for (var x = 0; x < TerrainTextureSize.x; x++)
        {
            for (var y = 0; y < TerrainTextureSize.y; y++)
            {
                float currentHeight = _cachedHeightMap[x, y];
                float maxDiff = 0f;

                // Check neighbors
                if (x < TerrainTextureSize.x - 1) maxDiff = Mathf.Max(maxDiff, Mathf.Abs(currentHeight - _cachedHeightMap[x + 1, y]));
                if (y < TerrainTextureSize.y - 1) maxDiff = Mathf.Max(maxDiff, Mathf.Abs(currentHeight - _cachedHeightMap[x, y + 1]));
                if (x > 0) maxDiff = Mathf.Max(maxDiff, Mathf.Abs(currentHeight - _cachedHeightMap[x - 1, y]));
                if (y > 0) maxDiff = Mathf.Max(maxDiff, Mathf.Abs(currentHeight - _cachedHeightMap[x, y - 1]));

                var rise = maxDiff * terrainHeightMultiplier;
                var angle = Mathf.Atan(rise / pixelWorldSize) * Mathf.Rad2Deg;

                rawSlopeMap[x, y] = angle / 90f;
            }
        }

        // --- PASS 3: Dilate Slopes (Thicken Lines) ---
        var dilatedSlopeMap = new float[TerrainTextureSize.x, TerrainTextureSize.y];

        for (var x = 0; x < TerrainTextureSize.x; x++)
        {
            for (var y = 0; y < TerrainTextureSize.y; y++)
            {
                var maxSlope = rawSlopeMap[x, y];

                for (var dx = -SlopeWidth; dx <= SlopeWidth; dx++)
                {
                    for (var dy = -SlopeWidth; dy <= SlopeWidth; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var nx = x + dx;
                        var ny = y + dy;

                        if (nx < 0 || nx >= TerrainTextureSize.x || ny < 0 || ny >= TerrainTextureSize.y) continue;
                        if (rawSlopeMap[nx, ny] > maxSlope) maxSlope = rawSlopeMap[nx, ny];
                    }
                }
                dilatedSlopeMap[x, y] = maxSlope;
            }
        }

        // --- PASS 4: Generate Colors & ADD MICRO NOISE ---
        var textureColors = new Color[TerrainTextureSize.x * TerrainTextureSize.y];
        for (var x = 0; x < TerrainTextureSize.x; x++)
        {
            for (var y = 0; y < TerrainTextureSize.y; y++)
            {
                var i = x + (y * TerrainTextureSize.x);
                
                // 1. Retrieve the clean Macro Height
                var macroHeight = _cachedHeightMap[x, y]; 

                // 2. Calculate Color based on MACRO height and MACRO slope
                var pixelData = GetTerrainData(macroHeight);
                var finalColor = pixelData.BiomeColor;
                var slopeAmount = dilatedSlopeMap[x, y];

                if (slopeAmount >= pixelData.SlopeThreshold)
                {
                    var range = Mathf.Max(pixelData.SlopeBlend, 0.0001f);
                    var rockMix = (slopeAmount - pixelData.SlopeThreshold) / range;
                    rockMix = Mathf.Clamp01(rockMix);
                    finalColor = Color.Lerp(finalColor, pixelData.SlopeColor, rockMix);
                    
                    // Optional: Remove grass on slopes
                    // We set this here, but we will write the value to the array later
                    grassMaskData[i] = Color.black; 
                }
                else
                {
                    // If not slope, check height for grass
                    // Note: We use macroHeight here to prevent grass appearing patchily on micro-bumps
                     if (macroHeight > GrassGenerationThreshold)
                        grassMaskData[i] = Color.white;
                    else 
                        grassMaskData[i] = Color.black;
                }
                
                // 3. NOW Add Micro Noise (The Bumps) to the height map
                // We do this AFTER calculating slope/color so the bumps don't count as "cliffs"
                var u = (float)x / TerrainTextureSize.x;
                var v = (float)y / TerrainTextureSize.y;
                
                var microNoiseVal = GetNoise(seed, u, v, OverlayNoiseSettings);
                microNoiseVal = (microNoiseVal - 0.5f) * OverlayNoiseStrength;

                var finalHeight = Mathf.Clamp01(macroHeight + microNoiseVal);
                _cachedHeightMap[x, y] = finalHeight;

                textureColors[i] = finalColor;
            }
        }

        _terrainGrassMask.SetPixels(grassMaskData);
        _terrainGrassMask.Apply();

        _terrainColorMap.SetPixels(textureColors);
        _terrainColorMap.Apply();
    }    
    private float GetClosestAllowedHeight(float height)
    {
        // Basic closest-value search
        float closest = AllowedNormalizedHeights[0];
        float minDiff = Mathf.Abs(height - closest);

        for (int i = 1; i < AllowedNormalizedHeights.Count; i++)
        {
            float diff = Mathf.Abs(height - AllowedNormalizedHeights[i]);
            if (diff < minDiff)
            {
                minDiff = diff;
                closest = AllowedNormalizedHeights[i];
            }
        }
        return closest;
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
    
    private static TerrainData LayerToData(TerrainLayer layer)
    {
        return new TerrainData
        {
            BiomeColor = layer.TerrainColor,
            SlopeColor = layer.SlopeColor,
            SlopeThreshold = layer.SlopeThreshold,
            SlopeBlend = layer.SlopeBlend
        };
    }
    private static float GetNoise(int seed, float u, float v, TerrainNoiseSettings settings)
    {
        // 1. Initialize a PRNG with your seed
        // Using System.Random is deterministic based on the seed provided.
        System.Random prng = new System.Random(seed);
    
        // 2. Generate random offsets for each octave
        // We create a separate offset for X and Y to avoid diagonal symmetry.
        // We also generate unique offsets for each octave to prevent them from stacking directly on top of each other.
        Vector2[] octaveOffsets = new Vector2[settings.Octaves];
        for (int i = 0; i < settings.Octaves; i++)
        {
            // Keep the range reasonable (e.g., -100,000 to 100,000) to avoid floating point errors.
            float offsetX = prng.Next(-100000, 100000); 
            float offsetY = prng.Next(-100000, 100000);
            octaveOffsets[i] = new Vector2(offsetX, offsetY);
        }

        var totalNoise = 0f;
        var frequency = settings.Frequency;
        var amplitude = settings.Amplitude;
        var maxVal = 0f;

        for (var i = 0; i < settings.Octaves; i++)
        {
            // 3. Apply the offsets
            // Note: We add the offset from our array, NOT the raw seed.
            float sampleX = u * frequency + octaveOffsets[i].x;
            float sampleY = v * frequency + octaveOffsets[i].y;

            totalNoise += Mathf.PerlinNoise(sampleX, sampleY) * amplitude;
        
            maxVal += amplitude;
            frequency *= settings.Lacunarity;
            amplitude *= settings.Persistence;
        }
    
        if (maxVal > 0) totalNoise /= maxVal;
    
        return Mathf.Clamp01(totalNoise);
    }

    private int _grassResolution;
    private int _grassInstanceCount;
    
    private Texture2D _terrainColorMap;
    private Texture2D _terrainGrassMask;
    private float[,] _cachedHeightMap;
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