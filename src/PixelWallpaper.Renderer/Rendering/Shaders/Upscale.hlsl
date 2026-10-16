Texture2D sceneTexture : register(t0);
SamplerState pointSampler : register(s0);

struct UpscalePixel
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD;
};

// One full-screen triangle: no vertex buffer needed for the upscale pass.
UpscalePixel VSMain(uint vertexId : SV_VertexID)
{
    UpscalePixel output;
    output.uv = float2((vertexId << 1) & 2, vertexId & 2);
    output.position = float4(output.uv.x * 2.0 - 1.0, 1.0 - output.uv.y * 2.0, 0.0, 1.0);
    return output;
}

float4 PSMain(UpscalePixel input) : SV_Target
{
    return sceneTexture.Sample(pointSampler, input.uv);
}
