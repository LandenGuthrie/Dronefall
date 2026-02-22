using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class TerrainGenerator : MonoBehaviour
{
    [Header("Initialization")]
    [SerializeField] private bool SelfInitialize;

    [Header("Terrain Settings")]
    public int TerrainMeshSize = 100;
    public List<TerrainLayer> TerrainLayers;
    [SerializeField] private AnimationCurve IslandHeightCurve;

    [Header("Biome Settings")]
    [SerializeField] [Range(0f, 1f)] private float BiomeBlendRange;

    [Header("Global Slope Settings")]
    [SerializeField] private Color GlobalSlopeColor;
    [SerializeField] [Range(0f, 1f)] public float GlobalSlopeThreshold;

    [Header("Noise Settings")]
    [SerializeField] private TerrainSettings OverlaySettings;
    [SerializeField] [Range(0f, 1f)] private float OverlayNoiseStrength;

    [Header("Material Settings")]
    [SerializeField] private Material TerrainMaterial;
    
    private void Start()
    {
        if (SelfInitialize) InitializeTerrainGenerator();
    }
    public void OnDestroy()
    {
        foreach (var layer in TerrainLayers.Where(layer => layer.LayerMask))
        {
            DestroyImmediate(layer.LayerMask);
        }
    }
    
    public void InitializeTerrainGenerator()
    {
        TerrainLayers.Sort((a, b) => a.StartHeight.CompareTo(b.StartHeight));
    }

    // --- GENERATION ---
    public void GenerateTerrain(int seed, TerrainSettings settings)
    {
        if (settings.UseSteppedHeights) Array.Sort(settings.AllowedSteppedHeights);
        
        GenerateHeightMap(seed, settings);
        GenerateLayerTextures(settings.TerrainHeightMultiplier);
        
        GenerateTerrainMesh(settings.TerrainHeightMultiplier);
        GetComponent<MeshRenderer>().material = TerrainMaterial;
    }
    private void GenerateHeightMap(int seed, TerrainSettings terrainSetting)
    {
        var mapSize = TerrainMeshSize + 1;
        _cachedHeightMap = new float[mapSize, mapSize];

        for (var x = 0; x < mapSize; x++)
        {
            for (var y = 0; y < mapSize; y++)
            {
                var u = (float)x / TerrainMeshSize;
                var v = (float)y / TerrainMeshSize;

                var baseNoiseVal = GetNoise(seed, u, v, terrainSetting);
                var distanceFromCenter = Vector2.Distance(new Vector2(0.5f, 0.5f), new Vector2(u, v)) * 2;
                var islandMask = IslandHeightCurve.Evaluate(distanceFromCenter);
                var finalBase = baseNoiseVal * islandMask;

                if (terrainSetting is { UseSteppedHeights: true, AllowedSteppedHeights: { Length: > 0 } })
                {
                    finalBase = GetClosestAllowedHeight(finalBase, terrainSetting.AllowedSteppedHeights);
                }

                var microNoiseVal = GetNoise(seed, u, v, OverlaySettings);
                microNoiseVal = (microNoiseVal - 0.5f) * OverlayNoiseStrength;

                _cachedHeightMap[x, y] = Mathf.Clamp01(finalBase + microNoiseVal);
            }
        }
    }
    private void GenerateLayerTextures(float terrainHeightMultiplier)
    {
        var textureSize = TerrainMeshSize * 4;

        // Build a lookup from full layer index -> pixel array index (-1 if no mask)
        var layerToPixelIndex = new int[TerrainLayers.Count];
        var count = 0;
        for (var i = 0; i < TerrainLayers.Count; i++)
        {
            if (TerrainLayers[i].LayerMask) DestroyImmediate(TerrainLayers[i].LayerMask);

            if (TerrainLayers[i].GenerateMask)
            {
                TerrainLayers[i].LayerMask = new Texture2D(textureSize, textureSize)
                {
                    wrapMode   = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Point
                };
                layerToPixelIndex[i] = count;
                count++;
            }
            else
            {
                layerToPixelIndex[i] = -1;
            }
        }

        if (count == 0) return;

        var pixelsPerLayer = new Color[count][];
        for (var i = 0; i < count; i++)
        {
            pixelsPerLayer[i] = new Color[textureSize * textureSize];
        }

        for (var z = 0; z < textureSize; z++)
        {
            for (var x = 0; x < textureSize; x++)
            {
                var pixelIndex = z * textureSize + x;

                // Map texture pixel to heightmap space (0 to TerrainMeshSize)
                var u = (float)x / (textureSize - 1) * TerrainMeshSize;
                var v = (float)z / (textureSize - 1) * TerrainMeshSize;

                // Bilinearly sample the heightmap at this UV
                var sampledHeight = SampleHeightMapBilinear(u, v);

                // Estimate slope in world space — scale height gradients by the multiplier
                // so the normal matches what the mesh actually looks like at 10x height etc.
                var hL = SampleHeightMapBilinear(u - 1f, v) * terrainHeightMultiplier;
                var hR = SampleHeightMapBilinear(u + 1f, v) * terrainHeightMultiplier;
                var hD = SampleHeightMapBilinear(u, v - 1f) * terrainHeightMultiplier;
                var hU = SampleHeightMapBilinear(u, v + 1f) * terrainHeightMultiplier;

                var estimatedNormal = new Vector3(hL - hR, 2f, hD - hU).normalized;
                var slopeAmount     = Vector3.Angle(Vector3.up, estimatedNormal) / 90f;

                if (GlobalSlopeThreshold > 0f && slopeAmount >= GlobalSlopeThreshold)
                {
                    for (var i = 0; i < count; i++)
                    {
                        pixelsPerLayer[i][pixelIndex] = Color.black;
                    }
                    continue;
                }

                var dominantLayerIndex = GetDominantLayerIndex(sampledHeight);

                for (var i = 0; i < TerrainLayers.Count; i++)
                {
                    var pixelArrayIndex = layerToPixelIndex[i];
                    if (pixelArrayIndex == -1) continue;

                    pixelsPerLayer[pixelArrayIndex][pixelIndex] = (i == dominantLayerIndex) ? Color.white : Color.black;
                }
            }
        }

        for (var i = 0; i < TerrainLayers.Count; i++)
        {
            var pixelArrayIndex = layerToPixelIndex[i];
            if (pixelArrayIndex == -1) continue;

            TerrainLayers[i].LayerMask.SetPixels(pixelsPerLayer[pixelArrayIndex]);
            TerrainLayers[i].LayerMask.Apply();
        }
    }
    private float SampleHeightMapBilinear(float u, float v)
    {
        var mapSize = TerrainMeshSize + 1;

        // Clamp to valid heightmap range
        u = Mathf.Clamp(u, 0f, TerrainMeshSize);
        v = Mathf.Clamp(v, 0f, TerrainMeshSize);

        var x0 = Mathf.Min(Mathf.FloorToInt(u), mapSize - 2);
        var z0 = Mathf.Min(Mathf.FloorToInt(v), mapSize - 2);
        var x1 = x0 + 1;
        var z1 = z0 + 1;

        var tx = u - x0;
        var tz = v - z0;

        // Bilinear interpolation across the 4 surrounding heightmap points
        var h00 = _cachedHeightMap[x0, z0];
        var h10 = _cachedHeightMap[x1, z0];
        var h01 = _cachedHeightMap[x0, z1];
        var h11 = _cachedHeightMap[x1, z1];

        return Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz);
    }    
    private void GenerateTerrainMesh(float terrainHeightMultiplier)
    {
        if (_cachedHeightMap == null) return;

        var mesh = new Mesh
        {
            name = "ProceduralTerrain",
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 // Vital for > 65k unshared vertices
        };

        var numQuads  = TerrainMeshSize * TerrainMeshSize;
        var vertices  = new Vector3[numQuads * 4];
        var colors    = new Color[numQuads * 4];
        var triangles = new int[numQuads * 6];

        var offset = TerrainMeshSize * 0.5f;

        var vIndex = 0;
        var tIndex = 0;

        for (var z = 0; z < TerrainMeshSize; z++)
        {
            for (var x = 0; x < TerrainMeshSize; x++)
            {
                // 1. Get heights for the 4 corners of this quad
                var hBL = _cachedHeightMap[x,     z    ] * terrainHeightMultiplier;
                var hTL = _cachedHeightMap[x,     z + 1] * terrainHeightMultiplier;
                var hBR = _cachedHeightMap[x + 1, z    ] * terrainHeightMultiplier;
                var hTR = _cachedHeightMap[x + 1, z + 1] * terrainHeightMultiplier;

                // 2. Define vertices
                var vBL = new Vector3(x - offset,       hBL, z - offset);
                var vTL = new Vector3(x - offset,       hTL, (z + 1) - offset);
                var vBR = new Vector3((x + 1) - offset, hBR, z - offset);
                var vTR = new Vector3((x + 1) - offset, hTR, (z + 1) - offset);

                vertices[vIndex + 0] = vBL;
                vertices[vIndex + 1] = vTL;
                vertices[vIndex + 2] = vBR;
                vertices[vIndex + 3] = vTR;

                // 3. Calculate the quad normal to evaluate slope
                var diag1  = vTR - vBL;
                var diag2  = vTL - vBR;
                var normal = Vector3.Cross(diag1, diag2).normalized;
                if (normal.y < 0) normal = -normal; // Ensure it points upward

                var slopeAngle  = Vector3.Angle(Vector3.up, normal);
                var slopeAmount = slopeAngle / 90f;

                // 4. Determine the color for this quad
                Color quadColor;
                if (GlobalSlopeThreshold > 0f && slopeAmount >= GlobalSlopeThreshold)
                {
                    quadColor = GlobalSlopeColor;
                }
                else
                {
                    // Use average height of the quad to determine biome
                    var centerHeight     = (hBL + hTL + hBR + hTR) / 4f;
                    var normalizedHeight = Mathf.Clamp01(centerHeight / terrainHeightMultiplier);
                    quadColor = GetBiomeColor(normalizedHeight);
                }

                // 5. Assign the same color to all 4 vertices — flat shading, no interpolation
                colors[vIndex + 0] = quadColor;
                colors[vIndex + 1] = quadColor;
                colors[vIndex + 2] = quadColor;
                colors[vIndex + 3] = quadColor;

                // 6. Assign triangles
                triangles[tIndex + 0] = vIndex + 0;
                triangles[tIndex + 1] = vIndex + 1;
                triangles[tIndex + 2] = vIndex + 2;

                triangles[tIndex + 3] = vIndex + 2;
                triangles[tIndex + 4] = vIndex + 1;
                triangles[tIndex + 5] = vIndex + 3;

                vIndex += 4;
                tIndex += 6;
            }
        }

        mesh.vertices  = vertices;
        mesh.colors    = colors;
        mesh.triangles = triangles;

        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();

        GetComponent<MeshFilter>().sharedMesh   = mesh;
        GetComponent<MeshCollider>().sharedMesh = mesh;
    }

    // --- UTILS ---
    public float[] CalculateHeightsID(int resolution, float terrainHeightMultiplier)
    {
        var grassHeights = new float[resolution * resolution];

        for (var row = 0; row < resolution; row++)
        {
            for (var col = 0; col < resolution; col++)
            {
                // Calculate UV coordinates that match the compute shader's logic
                var u = (float)col / resolution;
                var v = (float)row / resolution;

                // Sample the heightmap using grid indices — heightmap is (TerrainMeshSize + 1)^2
                var texX = Mathf.Clamp(Mathf.RoundToInt(u * TerrainMeshSize), 0, TerrainMeshSize);
                var texY = Mathf.Clamp(Mathf.RoundToInt(v * TerrainMeshSize), 0, TerrainMeshSize);

                // Store the actual world height (normalized height * multiplier)
                grassHeights[row * resolution + col] = _cachedHeightMap[texX, texY] * terrainHeightMultiplier;
            }
        }

        return grassHeights;
    }
    private Color GetBiomeColor(float normalizedHeight)
    {
        for (var i = TerrainLayers.Count - 1; i >= 0; i--)
        {
            var layerStart = TerrainLayers[i].StartHeight;

            if (normalizedHeight >= layerStart)
            {
                // Blend toward the next layer, scaled relative to the gap between layers
                // BiomeBlendRange of 0 = hard edge, 1 = blend across the entire layer gap
                if (i < TerrainLayers.Count - 1)
                {
                    var nextLayerStart = TerrainLayers[i + 1].StartHeight;
                    var layerGap       = nextLayerStart - layerStart;
                    var blendStart     = nextLayerStart - BiomeBlendRange * layerGap;

                    if (normalizedHeight >= blendStart)
                    {
                        var t = Mathf.InverseLerp(blendStart, nextLayerStart, normalizedHeight);
                        return Color.Lerp(TerrainLayers[i].TerrainColor, TerrainLayers[i + 1].TerrainColor, t);
                    }
                }

                return TerrainLayers[i].TerrainColor;
            }
        }
        return TerrainLayers[0].TerrainColor;
    }
    private float GetClosestAllowedHeight(float height, float[] allowedSteppedHeights)
    {
        var closest = allowedSteppedHeights[0];
        var minDiff = Mathf.Abs(height - closest);

        for (var i = 1; i < allowedSteppedHeights.Length; i++)
        {
            var diff = Mathf.Abs(height - allowedSteppedHeights[i]);
            if (diff < minDiff)
            {
                minDiff = diff;
                closest = allowedSteppedHeights[i];
            }
        }
        return closest;
    }
    private int GetDominantLayerIndex(float normalizedHeight)
    {
        for (var i = TerrainLayers.Count - 1; i >= 0; i--)
        {
            if (normalizedHeight >= TerrainLayers[i].StartHeight)
                return i;
        }
        return 0;
    }
    
    private static float GetNoise(int seed, float u, float v, TerrainSettings settings)
    {
        var prng = new System.Random(seed);
        var octaveOffsets = new Vector2[settings.Octaves];
        for (var i = 0; i < settings.Octaves; i++)
        {
            var offsetX = prng.Next(-100000, 100000);
            var offsetY = prng.Next(-100000, 100000);
            octaveOffsets[i] = new Vector2(offsetX, offsetY);
        }

        var totalNoise = 0f;
        var frequency  = settings.Frequency;
        var amplitude  = settings.Amplitude;
        var maxVal     = 0f;

        for (var i = 0; i < settings.Octaves; i++)
        {
            var sampleX = u * frequency + octaveOffsets[i].x;
            var sampleY = v * frequency + octaveOffsets[i].y;

            totalNoise += (Mathf.PerlinNoise(sampleX, sampleY) * amplitude);

            maxVal    += amplitude;
            frequency *= settings.Lacunarity;
            amplitude *= settings.Persistence;
        }

        if (maxVal > 0) totalNoise /= maxVal;

        return Mathf.Clamp01(totalNoise);
    }
    
    private float[,] _cachedHeightMap;
}

[Serializable]
public class TerrainLayer
{
    public string Name;
    [Range(0f, 1f)] public float StartHeight;
    public Color TerrainColor;
    public bool GenerateMask;
    public Texture2D LayerMask;
}

[Serializable]
public struct TerrainSettings
{
    public float Frequency;
    public float Amplitude;
    public int Octaves;
    public float Lacunarity;
    public float Persistence;
    public float TerrainHeightMultiplier;
    public bool UseSteppedHeights;
    public float[] AllowedSteppedHeights;
}