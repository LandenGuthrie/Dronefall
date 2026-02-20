using System;
using UnityEngine;
using UnityEngine.Serialization;

public class GrassGenerator : MonoBehaviour
{
    private static readonly int ResolutionID = Shader.PropertyToID("resolution");
    private static readonly int TerrainSizeID = Shader.PropertyToID("terrain_size");
    private static readonly int StepSizeID = Shader.PropertyToID("step_size");
    private static readonly int TerrainPositionID = Shader.PropertyToID("terrain_position");
    private static readonly int GrassScaleID = Shader.PropertyToID("grass_scale");
    private static readonly int DensityMapID = Shader.PropertyToID("density_map");
    private static readonly int TransformBufferID = Shader.PropertyToID("transform_buffer");
    private static readonly int HeightBufferID = Shader.PropertyToID("height_buffer");
    
    public int ComputeShaderKernal => ComputeShader.FindKernel("CSMain");
    public bool IsInitialized { get; private set; }
    
    [Header("Settings")]
    [SerializeField] private Mesh GrasMesh;
    [SerializeField] private Material GrassMaterial;
    [SerializeField] private ComputeShader ComputeShader;
    [SerializeField] private Vector3 GrassScale = Vector3.one;
    
    private void Update()
    {
        if (IsInitialized && _argsBuffer != null && _transformBuffer != null)
        {
            Graphics.DrawMeshInstancedIndirect(GrasMesh, 0, GrassMaterial, _bounds, _argsBuffer);
        }
    }

    private void OnDisable() => ReleaseBuffers();
    public void OnDestroy() => ReleaseBuffers();

    public void InitializeGrassGenerator(int resolution, int instanceCount, int terrainSize, Vector3 terrainPosition, float density, float[] heights, Texture2D densityMask)
    {
        if (IsInitialized) return;
        
        _resolution = resolution;
        _instanceCount = instanceCount;
        _terrainSize = terrainSize;
        
        if (GrasMesh == null || GrassMaterial == null || ComputeShader == null) return;
        
        _transformBuffer = new ComputeBuffer(_instanceCount, 64, ComputeBufferType.Append);
        _transformBuffer.SetCounterValue(0); 
        
        _heightBuffer = new ComputeBuffer(_instanceCount, sizeof(float)); 
        if (heights.Length > 0) _heightBuffer.SetData(heights);

        _argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        var args = new uint[] {
            GrasMesh.GetIndexCount(0),
            0,
            GrasMesh.GetIndexStart(0),
            GrasMesh.GetBaseVertex(0),
            0
        };
        _argsBuffer.SetData(args);
        SetDataToComputeShader(_resolution, terrainSize, terrainPosition, density, densityMask);
        GrassMaterial.SetBuffer(TransformBufferID, _transformBuffer);
        
        _bounds = new Bounds(Vector3.zero, new Vector3(terrainSize, 100f, terrainSize));
        IsInitialized = true;
    }

    public void UpdateGrassMesh(Vector3 terrainPosition, float density, float[] heights, Texture2D densityMask)
    {
        if (!IsInitialized) return;
        
        GrassMaterial.SetBuffer(TransformBufferID, (ComputeBuffer)null);

        _heightBuffer.SetData(heights);
        SetDataToComputeShader(_resolution, _terrainSize, terrainPosition, density, densityMask);
        
        GrassMaterial.SetBuffer(TransformBufferID, _transformBuffer);
    }

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
    
    private void SetDataToComputeShader(int resolution, int terrainSize, Vector3 terrainPosition, float density, Texture2D densityMap)
    {
        // Reset the Append Buffer counter to 0 before dispatching
        _transformBuffer.SetCounterValue(0);
        
        ComputeShader.SetInt(ResolutionID, resolution);
        ComputeShader.SetInt(TerrainSizeID, terrainSize);
        ComputeShader.SetFloat(StepSizeID, density);
        
        ComputeShader.SetVector(TerrainPositionID, terrainPosition);
        ComputeShader.SetVector(GrassScaleID, GrassScale);
        
        ComputeShader.SetTexture(ComputeShaderKernal, DensityMapID, densityMap);
        
        ComputeShader.SetBuffer(ComputeShaderKernal, TransformBufferID, _transformBuffer);
        ComputeShader.SetBuffer(ComputeShaderKernal, HeightBufferID, _heightBuffer);

        var groups = Mathf.CeilToInt(_instanceCount / 64f);
        ComputeShader.Dispatch(ComputeShaderKernal, groups, 1, 1);

        // CRITICAL STEP: Copy the actual number of generated grass blades 
        // from the transform buffer counter into the args buffer (at byte offset 4)
        ComputeBuffer.CopyCount(_transformBuffer, _argsBuffer, 4);
    }

    private int _terrainSize;
    private int _instanceCount;
    private int _resolution;
    
    private ComputeBuffer _transformBuffer;
    private ComputeBuffer _argsBuffer;
    private ComputeBuffer _heightBuffer;
    private Bounds _bounds;
}