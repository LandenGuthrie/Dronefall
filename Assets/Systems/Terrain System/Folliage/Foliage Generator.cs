using System;
using System.Collections.Generic;
using UnityEngine;

public class FoliageGenerator : MonoBehaviour
{
    public const int THREAD_GROUP_SIZE = 64;
    public const float RAYCAST_START_HEIGHT = 500f;
    public const float RAYCAST_MAX_DISTANCE = 1000f;
    
    private static readonly int MapSizeID = Shader.PropertyToID("map_size");
    private static readonly int PaddingID = Shader.PropertyToID("padding");
    private static readonly int JitterAmountID = Shader.PropertyToID("jitter_amount");
    private static readonly int ConfigIndexID = Shader.PropertyToID("config_index");
    private static readonly int ModelConfigID = Shader.PropertyToID("model_config");
    private static readonly int TransformBufferID = Shader.PropertyToID("transform_buffer");
    
    [Header("Dependencies")]
    [SerializeField] private ComputeShader SpawnerShader;
    
    [Header("Configuration")]
    [SerializeField] private bool SelfInitialize;
    [SerializeField] private ScatterSettings ScatterConfig;
    [SerializeField] private List<ModelDescription> FoliageModels;
    
    public bool IsInitialized { get; private set; }
    
    // --- Unity ---
    private void Start()
    {
        if (SelfInitialize) InitializeComputeShader();
    }
    private void Update()
    {
        if (!IsInitialized) return;
        RenderFoliage();
    }
    private void OnDestroy()
    {
        ReleaseBuffers();
    }
    
    //--- Initialization ---
    public void InitializeComputeShader()
    {
        if (IsInitialized) return;
        ConvertModelDescriptionsToGPU();
        _instanceGroups = new FoliageInstanceGroup[_gpuModelDescriptions.Length];
        _configBuffer = new ComputeBuffer(_gpuModelDescriptions.Length, 40);
        _configBuffer.SetData(_gpuModelDescriptions);
        for (var i = 0; i < FoliageModels.Count; i++)
        {
            var model = FoliageModels[i];
            var args = new uint[5] { 0, 0, 0, 0, 0 };
            args[0] = model.InstanceMesh.GetIndexCount(0);
            args[1] = (uint)model.SpawnCount;
            args[2] = model.InstanceMesh.GetIndexStart(0);
            args[3] = model.InstanceMesh.GetBaseVertex(0);
            var group = new FoliageInstanceGroup();
            group.MatrixBuffer = new ComputeBuffer(model.SpawnCount, sizeof(float) * 16);
            group.ArgsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
            group.ArgsBuffer.SetData(args);
            FoliageModels[i].InstanceMaterial.SetBuffer(TransformBufferID, group.MatrixBuffer);
            _instanceGroups[i] = group;
        }
        IsInitialized = true;
    }
    private void ConvertModelDescriptionsToGPU()
    {
        _gpuModelDescriptions = new BittableModelDescription[FoliageModels.Count];
        for (var i = 0; i < FoliageModels.Count; i++)
        {
            var model = FoliageModels[i];
            _gpuModelDescriptions[i] = new BittableModelDescription
            {
                SpawnCount = model.SpawnCount,
                HeightOffset = model.HeightOffset,
                MinRotation = model.MinRotation,
                MaxRotation = model.MaxRotation,
                ScaleRange = model.ScaleRange,
            };
        }
    }
    
    //--- Execution ---
    public void UpdateComputeShader()
    {
        if (!IsInitialized) return;
        var kernel = SpawnerShader.FindKernel("CSMain");
        SpawnerShader.SetFloat(MapSizeID, ScatterConfig.TerrainSize);
        SpawnerShader.SetFloat(PaddingID, ScatterConfig.Padding);
        SpawnerShader.SetFloat(JitterAmountID, ScatterConfig.JitterAmount);
        SpawnerShader.SetBuffer(kernel, ModelConfigID, _configBuffer);
        for (var i = 0; i < FoliageModels.Count; i++)
        {
            var model = FoliageModels[i];
            var group = _instanceGroups[i];
            var threadGroups = Mathf.CeilToInt((float)model.SpawnCount / THREAD_GROUP_SIZE);
            SpawnerShader.SetInt(ConfigIndexID, i);
            SpawnerShader.SetBuffer(kernel, TransformBufferID, group.MatrixBuffer);
            SpawnerShader.Dispatch(kernel, threadGroups, 1, 1);
            SnapToSurface(model, group.MatrixBuffer);
        }
    }
    private void SnapToSurface(ModelDescription model, ComputeBuffer matrixBuffer)
    {
        var matrices = new Matrix4x4[model.SpawnCount];
        matrixBuffer.GetData(matrices);
        for (var i = 0; i < matrices.Length; i++)
        {
            var pos = matrices[i].GetColumn(3);
            var rayStart = new Vector3(pos.x, RAYCAST_START_HEIGHT, pos.z);
            var ray = new Ray(rayStart, Vector3.down);
            if (!Physics.Raycast(ray, out var hit, RAYCAST_MAX_DISTANCE, model.SpawnableLayer)) continue;
            pos.y = hit.point.y + model.HeightOffset;
            matrices[i].SetColumn(3, new Vector4(pos.x, pos.y, pos.z, 1f));
        }
        matrixBuffer.SetData(matrices);
    }
    private void RenderFoliage()
    {
        var bounds = new Bounds(Vector3.zero, new Vector3(ScatterConfig.TerrainSize, 1000f, ScatterConfig.TerrainSize));
        for (var i = 0; i < FoliageModels.Count; i++)
        {
            var model = FoliageModels[i];
            var group = _instanceGroups[i];
            Graphics.DrawMeshInstancedIndirect(model.InstanceMesh, 0, model.InstanceMaterial, bounds, group.ArgsBuffer);
        }
    }
    
    //--- Cleanup ---
    private void ReleaseBuffers()
    {
        if (_configBuffer != null) _configBuffer.Release();
        if (_instanceGroups == null) return;
        foreach (var group in _instanceGroups)
        {
            if (group.MatrixBuffer != null) group.MatrixBuffer.Release();
            if (group.ArgsBuffer != null) group.ArgsBuffer.Release();
        }
    }
    private BittableModelDescription[] _gpuModelDescriptions;
    private FoliageInstanceGroup[] _instanceGroups;
    private ComputeBuffer _configBuffer;
}

[Serializable]
public struct ScatterSettings
{
    public float TerrainSize;
    public float Padding;
    public float JitterAmount;
}
[Serializable]
public struct ModelDescription
{
    public string Name;
    public Mesh InstanceMesh;
    public Material InstanceMaterial;
    public int SpawnCount;
    public float HeightOffset;
    public LayerMask SpawnableLayer;
    public Vector3 MinRotation;
    public Vector3 MaxRotation;
    public Vector2 ScaleRange;
}
public struct BittableModelDescription
{
    public int SpawnCount;
    public float HeightOffset;
    public Vector3 MinRotation;
    public Vector3 MaxRotation;
    public Vector2 ScaleRange;
}
public struct FoliageInstanceGroup
{
    public ComputeBuffer MatrixBuffer;
    public ComputeBuffer ArgsBuffer;
}