Texture2D<float4> _KuwSource, _KuwSum, _KuwSquare;
float4 _KuwSize; // output tile dimensions, halo radius, active component count
int _KuwTables;

float4 GraphKuwPrefix(int2 p, bool square)
{
    if(any(p<0)) return 0;
    return square?_KuwSquare.Load(int3(p,0)):_KuwSum.Load(int3(p,0));
}
float4 GraphKuwRect(int2 low,int2 high,bool square)
{
    return GraphKuwPrefix(high,square)-GraphKuwPrefix(int2(low.x-1,high.y),square)
        -GraphKuwPrefix(int2(high.x,low.y-1),square)+GraphKuwPrefix(low-1,square);
}
float4 GraphKuwahara(float2 uv)
{
    int radius=(int)_KuwSize.z;
    int2 center=min((int2)(uv*_KuwSize.xy),(int2)_KuwSize.xy-1)+radius;
    float4 reference=_KuwSource.Load(int3(int2(((uint2)_KuwSize.xy+2u*(uint)radius)>>1),0));
    float4 best=0; float bestVariance=0;
    [unroll] for(int quadrant=0;quadrant<4;quadrant++)
    {
        int2 low=center-int2((quadrant&1)==0?radius:0,(quadrant&2)==0?radius:0);
        int2 high=low+radius;
        float4 mean=0,variance=0;
        if(_KuwTables!=0)
        {
            float count=(radius+1.0)*(radius+1.0);
            mean=GraphKuwRect(low,high,false)/count;
            variance=max(GraphKuwRect(low,high,true)/count-mean*mean,0);
            mean+=reference;
        }
        else
        {
            // Centered, compensated moments retain low-contrast detail in HDR inputs.
            // precise prevents reassociation from removing the compensation terms.
            precise float4 sum=0, squareSum=0, sumError=0, squareError=0;
            [loop] for(int y=low.y;y<=high.y;y++)
            [loop] for(int x=low.x;x<=high.x;x++)
            {
                float4 value=_KuwSource.Load(int3(x,y,0))-reference;
                precise float4 delta=value-sumError, next=sum+delta;
                sumError=(next-sum)-delta; sum=next;
                precise float4 squareDelta=value*value-squareError, squareNext=squareSum+squareDelta;
                squareError=(squareNext-squareSum)-squareDelta; squareSum=squareNext;
            }
            float count=(radius+1.0)*(radius+1.0);
            mean=sum/count; variance=max(squareSum/count-mean*mean,0); mean+=reference;
        }
        float score=variance.x+(_KuwSize.w>1?variance.y:0)+(_KuwSize.w>2?variance.z:0)+(_KuwSize.w>3?variance.w:0);
        if(quadrant==0 || score<bestVariance) { best=mean; bestVariance=score; }
    }
    return best;
}
