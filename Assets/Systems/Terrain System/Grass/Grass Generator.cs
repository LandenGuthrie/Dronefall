using System;
using UnityEngine;
using UnityEngine.Serialization;

public class GrassGenerator : MonoBehaviour
{
    public const int THREAD_GROUP_SIZE = 64;

    private static readonly int ResolutionID = Shader.PropertyToID("resolution");
    private static readonly int GrassSizeID = Shader.PropertyToID("grass_size");
    private static readonly int StepSizeID = Shader.PropertyToID("step_size");
    private static readonly int GrassScaleID = Shader.PropertyToID("grass_scale");
    private static readonly int DensityMapID = Shader.PropertyToID("density_map");
    private static readonly int DensityMapSizeID = Shader.PropertyToID("density_map_size");
    private static readonly int ErodeRadiusID = Shader.PropertyToID("erode_radius");
    private static readonly int HeightBufferID = Shader.PropertyToID("height_buffer");
    private static readonly int TransformBufferID = Shader.PropertyToID("transform_buffer");
    
    [Header("Settings")]
    [SerializeField] private Mesh GrasMesh;
    [SerializeField] private Material GrassMaterial;
    [SerializeField] private ComputeShader ComputeShader;
    [SerializeField] private Vector3 GrassScale = Vector3.one;
    [SerializeField] private float Density = 0.2f;
    [SerializeField] private float DensityMapErosion = 3;
    
    public int ComputeShaderKernal => ComputeShader.FindKernel("CSMain");
    public bool IsInitialized { get; private set; }
    public int Resolution { get; private set; }
    public int InstanceCount { get; private set; }
    public Bounds Bounds { get; private set; }
    
    public Vector3 GrassPosition => Bounds.center; 
    public int GrassSize => (int)(Bounds.size.x + Bounds.size.z) / 2;
    
    // --- UNITY ---
    private void Update()
    {
        if (IsInitialized && _argsBuffer != null && _transformBuffer != null)
        {
            Graphics.DrawMeshInstancedIndirect(GrasMesh, 0, GrassMaterial, Bounds, _argsBuffer);
        }
    }
    public void OnDestroy() => ReleaseBuffers();

    // --- GENERATION ---
    public void SetCachedData(int terrainSize, Vector3 terrainPosition)
    {
        Resolution = Mathf.CeilToInt(terrainSize / Density);
        InstanceCount = Resolution * Resolution;
        Bounds = new Bounds(terrainPosition, new  Vector3(terrainSize, 100, terrainSize));
    }
    public void InitializeGrassGenerator(GrassSettings settings)
    {
        if (IsInitialized) return;
        
        if (GrasMesh == null || GrassMaterial == null || ComputeShader == null) return;
        
        _transformBuffer = new ComputeBuffer(InstanceCount, THREAD_GROUP_SIZE, ComputeBufferType.Append);
        _transformBuffer.SetCounterValue(0); 
        
        _heightBuffer = new ComputeBuffer(InstanceCount, sizeof(float)); 
        if (settings.Heights.Length > 0) _heightBuffer.SetData(settings.Heights);

        _argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        var args = new uint[] {
            GrasMesh.GetIndexCount(0),
            0,
            GrasMesh.GetIndexStart(0),
            GrasMesh.GetBaseVertex(0),
            0
        };
        _argsBuffer.SetData(args);
        InitializeComputeShader(settings.DensityMask);
        GrassMaterial.SetBuffer(TransformBufferID, _transformBuffer);
        
        IsInitialized = true;
    }
    public void UpdateGrassMesh(float[] heights, Texture2D densityMask)
    {
        if (!IsInitialized) return;
        
        GrassMaterial.SetBuffer(TransformBufferID, (ComputeBuffer)null);

        _heightBuffer.SetData(heights);
        UpdateComputeShader(densityMask);
        
        GrassMaterial.SetBuffer(TransformBufferID, _transformBuffer);
    }
    private void UpdateComputeShader(Texture2D densityMap)
    {
        _transformBuffer.SetCounterValue(0);
        ComputeShader.SetTexture(ComputeShaderKernal, DensityMapID, densityMap);
        ComputeShader.SetInt(DensityMapSizeID, densityMap.width);

        var groups = Mathf.CeilToInt(InstanceCount / (float)THREAD_GROUP_SIZE);
        ComputeShader.Dispatch(ComputeShaderKernal, groups, 1, 1);
        ComputeBuffer.CopyCount(_transformBuffer, _argsBuffer, 4);

    }
    private void InitializeComputeShader(Texture2D densityMap)
    {
        // Reset the Append Buffer counter to 0 before dispatching
        _transformBuffer.SetCounterValue(0);
        
        ComputeShader.SetInt(ResolutionID, Resolution);
        ComputeShader.SetInt(GrassSizeID, GrassSize);
        ComputeShader.SetFloat(StepSizeID, Density);
        
        ComputeShader.SetVector(GrassScaleID, GrassScale);
        
        ComputeShader.SetTexture(ComputeShaderKernal, DensityMapID, densityMap);
        ComputeShader.SetInt(DensityMapSizeID, densityMap.width);
        ComputeShader.SetFloat(ErodeRadiusID, DensityMapErosion);

        ComputeShader.SetBuffer(ComputeShaderKernal, TransformBufferID, _transformBuffer);
        ComputeShader.SetBuffer(ComputeShaderKernal, HeightBufferID, _heightBuffer);

        var groups = Mathf.CeilToInt(InstanceCount / (float)THREAD_GROUP_SIZE);
        ComputeShader.Dispatch(ComputeShaderKernal, groups, 1, 1);

        ComputeBuffer.CopyCount(_transformBuffer, _argsBuffer, 4);
    }
    
    // --- UTIL ---
    private void ReleaseBuffers()
    {
        _transformBuffer?.Release();
        _argsBuffer?.Release();
        _heightBuffer?.Release();
        _transformBuffer = null;
        _argsBuffer = null;
        _heightBuffer = null;
        IsInitialized = false;
    }

    
    private ComputeBuffer _transformBuffer;
    private ComputeBuffer _argsBuffer;
    private ComputeBuffer _heightBuffer;
}

public struct GrassSettings
{
    public readonly float[] Heights;
    public readonly Texture2D DensityMask;

    public GrassSettings(float[] heights, Texture2D densityMask)
    {
        Heights = heights;
        DensityMask = densityMask;
    }
}