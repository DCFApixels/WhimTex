// @whimtex-node Color/Adjustments/Contrast
// @preset("5498395203f646f7aa4671d8ca80ff34", 1)
// @param color(float4) Color = (1, 1, 1, 1)
// @param float Amount = 1 [0 .. 4]
// @param color(float4) Result
void WhimTexNodeContrast(float4 Color, float Amount, out float4 Result)
{
    Result = float4((Color.rgb - 0.5) * Amount + 0.5, Color.a);
}
