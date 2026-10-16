Texture2D spriteTexture : register(t0);
SamplerState pointSampler : register(s0);

struct SpritePixel
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD;
    float4 color : COLOR;
};

SpritePixel VSMain(float2 position : POSITION, float2 uv : TEXCOORD, float4 color : COLOR)
{
    SpritePixel output;
    output.position = float4(position, 0.0, 1.0);
    output.uv = uv;
    output.color = color;
    return output;
}

float4 PSMain(SpritePixel input) : SV_Target
{
    return spriteTexture.Sample(pointSampler, input.uv) * input.color;
}
