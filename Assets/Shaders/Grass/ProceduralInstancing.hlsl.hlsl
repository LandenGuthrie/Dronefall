StructuredBuffer<float4x4> transform_buffer;

void ConfigureProcedural_float(float3 InPosition, float InstanceID, out float3 OutPosition)
{
    // 1. Convert the float ID to an integer
    uint id = (uint)InstanceID;

    // 2. Read the matrix from our buffer
    float4x4 data = transform_buffer[id];

    // 3. Extract the columns (Math magic to get position/rotation)
    float3 position = data._m03_m13_m23;
    float3 right    = data._m00_m10_m20;
    float3 up       = data._m01_m11_m21;
    float3 fwd      = data._m02_m12_m22;

    // 4. Apply the transform manually
    OutPosition = (InPosition.x * right) + (InPosition.y * up) + (InPosition.z * fwd) + position;
}