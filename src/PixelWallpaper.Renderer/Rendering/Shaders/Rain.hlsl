struct RainPixel
{
    float4 position : SV_Position;
    float4 color : COLOR;
};

RainPixel VSMain(float2 position : POSITION, float4 color : COLOR)
{
    RainPixel output;
    output.position = float4(position, 0.0, 1.0);
    output.color = color;
    return output;
}

float4 PSMain(RainPixel input) : SV_Target
{
    return input.color;
}
