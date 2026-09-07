#ifndef GENESIS_MERKABA_FLOWER_STORAGE_INCLUDED
#define GENESIS_MERKABA_FLOWER_STORAGE_INCLUDED

#include "MerkabaFlowerSidecar.hlsl"

#define M8_FLOWER_STORAGE_PACKET_RECORDS 256u
#define M8_FLOWER_STORAGE_HEADER_RECORDS 2u
#define M8_FLOWER_STORAGE_BODY_WORDS ((M8_FLOWER_STORAGE_PACKET_RECORDS-2u)*4u)
uint _M8FlowerCaptureContinue;

bool M8FlowerCaptureRecord(uint first,uint4 header,uint address,bool thread,
    inout uint used,out bool invalid)
{
    invalid=thread ? !M8FlowerThreadRange(address,header.w) :
        !M8FlowerDetailRange(address,header.w);
    if(invalid)return false;
    uint rows=1u+(header.w+15u)/16u;
    if(used+rows*4u>M8_FLOWER_STORAGE_BODY_WORDS)return false;
    uint destination=first+M8_FLOWER_STORAGE_HEADER_RECORDS+used/4u;
    _M8WritebackStaging[destination]=header;
    [loop]for(uint row=1u;row<rows;row++)
    {
        uint4 value=0u.xxxx;
        [unroll]for(uint word=0u;word<4u;word++)
        {
            uint offset=(row-1u)*16u+word*4u;
            if(offset<header.w)value[word]=thread ?
                _M8ThreadAtlasPagesRead.Load(address+offset) :
                _M8FlowerDetailPagesRead.Load(address+offset);
        }
        _M8WritebackStaging[destination+row]=value;
    }
    used+=rows*4u;
    return true;
}

// EVICTING pins both canonical sidecars until the complete M8+dual+fine
// append succeeds. The cursor is transport state, never a stored hierarchy.
void M8CaptureFlowerTile(uint first,uint slot)
{
    uint generation=M8LoadTileRuntimeRead(slot).w;
    uint4 previous=_M8WritebackStaging[first];
    bool continuation=_M8FlowerCaptureContinue!=0u;
    uint4 cursor=continuation ? _M8WritebackStaging[first+1u] : 0u.xxxx;
    uint used=0u,error=0u;
    if(continuation && (previous.w!=generation || previous.z!=0u))error=1u;
    uint4 meta=M8LoadTileMetaRead(slot);
    if(meta.x>=MERKABA_M8_CHUNK_CAPACITY || meta.y>=64u || generation==0u ||
        _M8ChunkTileRefsRead[meta.x*64u+meta.y]!=MERKABA_REF_EVICTING)error=1u;
    [unroll]for(uint word=0u;word<16u;word++)
        if(_M8TileBitsRead[M8TileWordIndex(slot,word)].w!=0u)error=1u;
    uint2 tile=_M8FlowerDetailPagesRead.Load2(M8_FLOWER_TILE_DIRECTORY+
        M8_FLOWER_TILE_DIRECTORY_STRIDE*slot);
    if(tile.x==0u)cursor=uint4(512u,0u,0u,0u);
    else if(tile.y!=generation || !M8FlowerDetailRange(tile.x,2048u))error=1u;
    if(cursor.x>512u || cursor.y>3u)error=1u;
    bool full=false;
    [loop]for(uint step=0u;step<256u && cursor.x<512u && error==0u && !full;step++)
    {
        uint owner=_M8FlowerDetailPagesRead.Load(tile.x+4u*cursor.x);
        if(owner==0u){cursor=uint4(cursor.x+1u,0u,0u,0u);continue;}
        if(!M8FlowerDetailRange(owner,64u) ||
            _M8FlowerDetailPagesRead.Load(owner)!=cursor.x){error=1u;break;}
        if((_M8FlowerDetailPagesRead.Load(owner+60u)&1u)==0u)
        {cursor=uint4(cursor.x+1u,0u,0u,0u);continue;}
        uint epoch=_M8FlowerDetailPagesRead.Load(owner+4u);
        if(epoch==0u){error=1u;break;}
        bool invalid=false;
        if(cursor.y==0u)
        {
            uint rebased=(_M8FlowerDetailPagesRead.Load(owner+60u)>>1u)&1u;
            if(!M8FlowerCaptureRecord(first,uint4(6u,cursor.x,rebased,8u),owner,false,used,invalid))
            {error=invalid?1u:0u;full=!invalid;continue;}
            cursor.y=1u;cursor.z=0u;cursor.w=0u;
            continue;
        }
        bool thread=cursor.y==3u;
        uint offset=cursor.y==1u?8u:thread?32u:20u;
        uint2 span=_M8FlowerDetailPagesRead.Load2(owner+offset);
        uint stride=cursor.y==1u?16u:32u;
        if(span.y>M8_FLOWER_PERSISTENT_BYTES/stride ||
            (span.y!=0u && (thread ? !M8FlowerThreadRange(span.x,span.y*stride) :
                !M8FlowerDetailRange(span.x,span.y*stride)))){error=1u;break;}
        if(cursor.z>=span.y)
        {
            if(cursor.y==3u)cursor=uint4(cursor.x+1u,0u,0u,0u);
            else cursor=uint4(cursor.x,cursor.y+1u,0u,0u);
            continue;
        }
        uint address=span.x+cursor.z*stride;
        uint4 a=thread ? _M8ThreadAtlasPagesRead.Load4(address) :
            _M8FlowerDetailPagesRead.Load4(address);
        if(cursor.y==1u)
        {
            if(a.w!=epoch){cursor.z++;continue;}
            if(!M8FlowerCaptureRecord(first,uint4(7u,cursor.x,a.x,16u),address,false,used,invalid))
            {error=invalid?1u:0u;full=!invalid;continue;}
            cursor.z++;continue;
        }
        uint2 b=thread ? _M8ThreadAtlasPagesRead.Load2(address+16u) :
            _M8FlowerDetailPagesRead.Load2(address+16u);
        if((thread?b.y:b.x)!=epoch){cursor.z++;cursor.w=0u;continue;}
        uint2 split=thread?uint2(a.w,b.x):a.zw;
        uint count=countbits(split.x)+countbits(split.y);
        if((split.y&0xfe000000u)!=0u || count>57u){error=1u;break;}
        uint groupBase=thread?a.z:a.y;
        uint firstGroup=thread?2u:1u;
        if(thread && cursor.w==0u)
        {
            if(a.y!=0xffffffffu)
            {
                if(a.y>0xffffffffu/48u){error=1u;break;}
                uint logicalProgram;
                if(!M8FlowerLogicalProgramRef(a.y,logicalProgram,true)){error=1u;break;}
                if(!M8FlowerCaptureRecord(first,uint4(10u,0xffffffffu,logicalProgram,48u),
                    a.y*48u,true,used,invalid))
                {error=invalid?1u:0u;full=!invalid;continue;}
            }
            cursor.w++;continue;
        }
        if(cursor.w==firstGroup-1u)
        {
            uint begin=used;
            if(!M8FlowerCaptureRecord(first,uint4(thread?11u:8u,cursor.x,a.x,24u),
                address,thread,used,invalid))
            {error=invalid?1u:0u;full=!invalid;continue;}
            if(thread && a.y!=0xffffffffu)
            {
                uint logicalProgram;
                if(!M8FlowerLogicalProgramRef(a.y,logicalProgram,true)){error=1u;break;}
                _M8WritebackStaging[first+M8_FLOWER_STORAGE_HEADER_RECORDS+begin/4u+1u].y=logicalProgram;
            }
            cursor.w++;continue;
        }
        uint group=cursor.w-firstGroup;
        if(group>=count){cursor.z++;cursor.w=0u;continue;}
        uint bytes=thread?112u:56u;
        if(groupBase>0xffffffffu-group || groupBase+group>0xffffffffu/bytes)
        {error=1u;break;}
        uint key=groupBase+group;
        if(!M8FlowerCaptureRecord(first,uint4(thread?12u:9u,cursor.x,key,bytes),
            key*bytes,thread,used,invalid))
        {error=invalid?1u:0u;full=!invalid;continue;}
        cursor.w++;
    }
    _M8WritebackStaging[first+1u]=cursor;
    _M8WritebackStaging[first]=uint4(used,cursor.x==512u?1u:0u,error,generation);
}

#endif
