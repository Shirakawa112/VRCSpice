Shader "VRCSpice/Oscilloscope Copy"
{
    Properties
    {
        _MainTex ("Previous display dump", 2D) = "black" {}
        _SolverDump ("Solver output dump", 2D) = "black" {}
        _Row1 ("CH1 row", Integer) = -1
        _Row2 ("CH2 row", Integer) = -1
        _Stride ("Time stride", Integer) = 10
        _ConfigRevision ("Configuration revision", Integer) = 1
        _ForceRebuild ("Force rebuild", Integer) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always Blend Off
            CGPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            Texture2D<uint> _MainTex;
            Texture2D<uint> _SolverDump;
            float4 _MainTex_TexelSize;
            int _Row1,_Row2;
            uint _Stride,_ConfigRevision,_ForceRebuild;

            #define RAW_CAPACITY 1000u
            #define DISPLAY_CAPACITY 200u

            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; };
            v2f vert(appdata v){v2f o;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;return o;}
            uint DisplayLoad(uint x,uint y){return _MainTex.Load(int3(x,y,0));}
            uint SolverLoad(uint x,uint y){return _SolverDump.Load(int3(x,y,0));}

            uint FirstAligned(uint value,uint stride)
            {
                uint remainder=value%stride;
                return remainder==0u?value:value+stride-remainder;
            }

            uint CandidateCount(uint start,uint finish,uint stride)
            {
                if(finish<start)return 0u;
                uint first=FirstAligned(start,stride);
                uint last=finish-finish%stride;
                return last<first?0u:(last-first)/stride+1u;
            }

            uint ChannelValue(int row,uint dataN,uint sourceY,out uint valid)
            {
                if(row==-2){valid=1u;return asuint(0.0);}
                if(row>=0 && (uint)row<dataN){valid=1u;return SolverLoad((uint)row,sourceY);}
                valid=0u;return 0u;
            }

            uint frag(v2f input):SV_Target
            {
                uint2 pixel=(uint2)(input.uv*_MainTex_TexelSize.zw);
                uint rawCount=min(RAW_CAPACITY,SolverLoad(2u,0u));
                uint sequence=SolverLoad(3u,0u);
                uint generation=SolverLoad(6u,0u);
                uint oldCount=min(DISPLAY_CAPACITY,DisplayLoad(0u,0u));
                uint oldSequence=DisplayLoad(1u,0u);
                uint oldGeneration=DisplayLoad(2u,0u);
                uint oldConfig=DisplayLoad(3u,0u);
                uint stride=max(1u,_Stride);
                bool generationChanged=generation!=oldGeneration;
                bool overrun=!generationChanged && sequence>oldSequence && sequence-oldSequence>rawCount;
                bool rebuild=_ForceRebuild!=0u || _ConfigRevision!=oldConfig || generationChanged ||
                    sequence<oldSequence || overrun;
                uint oldest=rawCount==0u?sequence+1u:sequence-rawCount+1u;
                uint start=rebuild?oldest:oldSequence+1u;
                uint candidates=CandidateCount(start,sequence,stride);
                uint inserted=min(DISPLAY_CAPACITY,candidates);
                uint resultCount=min(DISPLAY_CAPACITY,inserted+(rebuild?0u:oldCount));

                if(pixel.y==0u)
                {
                    if(pixel.x==0u)return resultCount;
                    if(pixel.x==1u)return sequence;
                    if(pixel.x==2u)return generation;
                    if(pixel.x==3u)return _ConfigRevision;
                    return 0u;
                }

                uint history=pixel.y-1u;
                if(history>=resultCount || history>=DISPLAY_CAPACITY)return 0u;
                if(history>=inserted)
                {
                    uint oldHistory=history-inserted;
                    return oldHistory<oldCount?DisplayLoad(pixel.x,oldHistory+1u):0u;
                }

                uint lastAligned=sequence-sequence%stride;
                uint sampleSequence=lastAligned-history*stride;
                uint rawHistory=sequence-sampleSequence;
                uint sourceY=rawHistory+1u;
                uint dataN=SolverLoad(1u,0u);
                if(pixel.x==0u)return SolverLoad(dataN,sourceY);
                uint valid1,valid2;
                uint value1=ChannelValue(_Row1,dataN,sourceY,valid1);
                uint value2=ChannelValue(_Row2,dataN,sourceY,valid2);
                if(pixel.x==1u)return value1;
                if(pixel.x==2u)return value2;
                if(pixel.x==3u)return valid1|(valid2<<1u);
                return 0u;
            }
            ENDCG
        }
    }
}
