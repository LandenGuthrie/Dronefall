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

    // --- Initialization ---
    public void InitializeComputeShader()
    {
        if (IsInitialized) return;
        ConvertModelDescriptionsToGPU();
        _instanceGroups = new FoliageInstanceGroup[_gpuModelDescriptions.Length];
        _configBuffer = new ComputeBuffer(_gpuModelDescriptions.Length, 48);
        _configBuffer.SetData(_gpuModelDescriptions);

        for (var i = 0; i < FoliageModels.Count; i++)
        {
            var model = FoliageModels[i];
            var submeshCount = model.InstanceMesh.subMeshCount;
            var group = new FoliageInstanceGroup();
            group.MatrixBuffer = new ComputeBuffer(model.SpawnCount, sizeof(float) * 16);
            group.ArgsBuffers = new ComputeBuffer[submeshCount];
            group.PropertyBlocks = new MaterialPropertyBlock[submeshCount];

            for (var s = 0; s < submeshCount; s++)
            {
                var args = new uint[5] { 0, 0, 0, 0, 0 };
                args[0] = model.InstanceMesh.GetIndexCount(s);
                args[1] = (uint)model.SpawnCount;
                args[2] = model.InstanceMesh.GetIndexStart(s);
                args[3] = model.InstanceMesh.GetBaseVertex(s);
                group.ArgsBuffers[s] = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
                group.ArgsBuffers[s].SetData(args);
                group.PropertyBlocks[s] = new MaterialPropertyBlock();
                group.PropertyBlocks[s].SetBuffer(TransformBufferID, group.MatrixBuffer);
            }
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
                MinRotation = new Vector4(model.MinRotation.x, model.MinRotation.y, model.MinRotation.z, 0f),
                MaxRotation = new Vector4(model.MaxRotation.x, model.MaxRotation.y, model.MaxRotation.z, 0f),
                ScaleRange = model.ScaleRange,
                SpawnCount = model.SpawnCount,
                HeightOffset = model.HeightOffset,
            };
        }
    }

    // --- Execution ---
    public void UpdateComputeShader(Texture2D spawnMask = null)
    {
        if (!IsInitialized) return;
        var kernel = SpawnerShader.FindKernel("CSMain");
        SpawnerShader.SetFloat(MapSizeID, ScatterConfig.TerrainSize);
        SpawnerShader.SetFloat(PaddingID, ScatterConfig.Padding);
        SpawnerShader.SetFloat(JitterAmountID, ScatterConfig.JitterAmount);
        SpawnerShader.SetBuffer(kernel, ModelConfigID, _configBuffer);

        // Calculate bounds once for all models
        var fullTerrainBounds = new Bounds(
            Vector3.zero,
            new Vector3(ScatterConfig.TerrainSize, 1000f, ScatterConfig.TerrainSize)
        );

        var spawnBoundsSize = ScatterConfig.TerrainSize - ScatterConfig.Padding * 2f;
        var spawnBounds = new Bounds(
            Vector3.zero,
            new Vector3(spawnBoundsSize, 0f, spawnBoundsSize)
        );

        // --- NEW: Dictionary to hold shared samplers based on SpatialGroupID ---
        var samplersByGroup = new Dictionary<int, MitchellsBestCandidateSampler>();

        for (var i = 0; i < FoliageModels.Count; i++)
        {
            var model = FoliageModels[i];
            var group = _instanceGroups[i];

            // Get or create the shared sampler for this specific Spatial Group
            if (!samplersByGroup.TryGetValue(model.SpatialGroupID, out var sampler))
            {
                sampler = new MitchellsBestCandidateSampler(spawnBounds, fullTerrainBounds, spawnMask);
                samplersByGroup[model.SpatialGroupID] = sampler;
            }

            var threadGroups = Mathf.CeilToInt((float)model.SpawnCount / THREAD_GROUP_SIZE);
            SpawnerShader.SetInt(ConfigIndexID, i);
            SpawnerShader.SetBuffer(kernel, TransformBufferID, group.MatrixBuffer);
            SpawnerShader.Dispatch(kernel, threadGroups, 1, 1);
            
            // Pass the shared sampler directly into the method
            SnapToSurface(model, group, sampler);
        }
    }
    
    private void SnapToSurface(ModelDescription model, FoliageInstanceGroup group, MitchellsBestCandidateSampler sampler)
    {
        var sourceMatrices = new Matrix4x4[model.SpawnCount];
        group.MatrixBuffer.GetData(sourceMatrices);
        var finalMatrices = new Matrix4x4[model.SpawnCount];

        var validCount = 0;
        int maxAttempts = model.SpawnCount * 25; 
        int attempts = 0;

        while (validCount < model.SpawnCount && attempts < maxAttempts)
        {
            attempts++;

            if (sampler.TryGetNextCandidate(out var positionXZ))
            {
                var pos = new Vector4(positionXZ.x, 0f, positionXZ.y, 1f);
                
                if (!TrySnapPosition(ref pos, model)) continue;

                // Because this sampler is SHARED, registering this point tells ALL models 
                // in this SpatialGroupID to stay away from this exact spot.
                sampler.RegisterAcceptedPosition(positionXZ);

                var srcMatrix = sourceMatrices[validCount]; 
                var scaleX = new Vector3(srcMatrix.m00, srcMatrix.m10, srcMatrix.m20).magnitude;
                var scaleY = new Vector3(srcMatrix.m01, srcMatrix.m11, srcMatrix.m21).magnitude;
                var scaleZ = new Vector3(srcMatrix.m02, srcMatrix.m12, srcMatrix.m22).magnitude;

                var rotation = Quaternion.LookRotation(
                    new Vector3(srcMatrix.m02 / scaleZ, srcMatrix.m12 / scaleZ, srcMatrix.m22 / scaleZ),
                    new Vector3(srcMatrix.m01 / scaleY, srcMatrix.m11 / scaleY, srcMatrix.m21 / scaleY)
                );

                finalMatrices[validCount] = Matrix4x4.TRS(
                    new Vector3(pos.x, pos.y, pos.z),
                    rotation,
                    new Vector3(scaleX, scaleY, scaleZ)
                );
                validCount++;
            }
        }

        group.MatrixBuffer.SetData(finalMatrices);

        var argsReadback = new uint[5];
        for (var s = 0; s < group.ArgsBuffers.Length; s++)
        {
            group.ArgsBuffers[s].GetData(argsReadback);
            argsReadback[1] = (uint)validCount;
            group.ArgsBuffers[s].SetData(argsReadback);
        }
    }

    private bool TrySnapPosition(ref Vector4 pos, ModelDescription model)
    {
        var rayStart = new Vector3(pos.x, RAYCAST_START_HEIGHT, pos.z);
        if (!Physics.Raycast(new Ray(rayStart, Vector3.down), out var hit, RAYCAST_MAX_DISTANCE, model.SpawnableLayer))
            return false;

        pos.y = hit.point.y + model.HeightOffset;
        return true;
    }

    private void RenderFoliage()
    {
        var bounds = new Bounds(Vector3.zero, new Vector3(ScatterConfig.TerrainSize, 1000f, ScatterConfig.TerrainSize));
        for (var i = 0; i < FoliageModels.Count; i++)
        {
            var model = FoliageModels[i];
            var group = _instanceGroups[i];
            var submeshCount = model.InstanceMesh.subMeshCount;
            for (var s = 0; s < submeshCount; s++)
            {
                var material = s < model.InstanceMaterials.Length
                    ? model.InstanceMaterials[s]
                    : model.InstanceMaterials[model.InstanceMaterials.Length - 1];
                Graphics.DrawMeshInstancedIndirect(model.InstanceMesh, s, material, bounds, group.ArgsBuffers[s], 0, group.PropertyBlocks[s]);
            }
        }
    }

    // --- Cleanup ---
    private void ReleaseBuffers()
    {
        if (_configBuffer != null) _configBuffer.Release();
        if (_instanceGroups == null) return;
        foreach (var group in _instanceGroups)
        {
            if (group.MatrixBuffer != null) group.MatrixBuffer.Release();
            if (group.ArgsBuffers == null) continue;
            foreach (var argsBuffer in group.ArgsBuffers)
            {
                if (argsBuffer != null) argsBuffer.Release();
            }
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
    public int SpatialGroupID;
    public Material[] InstanceMaterials;
    public int SpawnCount;
    public float HeightOffset;
    public LayerMask SpawnableLayer;
    public Vector3 MinRotation;
    public Vector3 MaxRotation;
    public Vector2 ScaleRange;
}
public struct BittableModelDescription
{
    public Vector4 MinRotation;
    public Vector4 MaxRotation;
    public Vector2 ScaleRange;
    public int SpawnCount;
    public float HeightOffset;
}
public struct FoliageInstanceGroup
{
    public ComputeBuffer MatrixBuffer;
    public ComputeBuffer[] ArgsBuffers;
    public MaterialPropertyBlock[] PropertyBlocks;
}