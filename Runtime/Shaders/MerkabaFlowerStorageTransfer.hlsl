#ifndef GENESIS_MERKABA_FLOWER_STORAGE_TRANSFER_INCLUDED
#define GENESIS_MERKABA_FLOWER_STORAGE_TRANSFER_INCLUDED

// The same staging allocation has a read view for M8 installation and a
// writable view only in this storage barrier. No payload clone is allocated.
RWStructuredBuffer<uint4> _M8LoadStagingStatesWrite;
uint _M8FlowerStorageMode; // 0 install, 1 acknowledge capture, 2 cancel load
#define M8_FLOWER_LOAD_HEADER_RECORDS 8u
#define M8_FLOWER_LOAD_BODY_WORDS ((M8_FLOWER_STORAGE_PACKET_RECORDS-8u)*4u)
#define M8_FLOWER_LOAD_PACKET_DONE 1u
#define M8_FLOWER_LOAD_IMAGE_DONE 2u
#define M8_FLOWER_LOAD_RETRY 3u
#define M8_FLOWER_LOAD_INVALID 4u
#define M8_FLOWER_LOAD_CANCELLED 5u
#define M8_FLOWER_CAPTURE_ACK_READY 2u

uint M8FlowerLoadWord(uint first,uint word)
{
    return _M8LoadStagingStatesWrite[first+M8_FLOWER_LOAD_HEADER_RECORDS+word/4u][word&3u];
}

uint M8FlowerPublishImportedRun(uint first,uint publishing,uint retired)
{
    uint4 owner=_M8LoadStagingStatesWrite[first+2u];
    uint4 a=_M8LoadStagingStatesWrite[first+3u];
    uint4 b=_M8LoadStagingStatesWrite[first+4u];
    uint4 group=_M8LoadStagingStatesWrite[first+5u];
    uint canonical[6]={a.x,a.y,a.z,a.w,b.x,b.y};
    uint result=M8FlowerInstallImportedRun(owner.w,canonical,group.w!=0u,b.z,b.w,
        publishing,retired);
    if(result==M8_FLOWER_ARENA_OK)
    {
        // Ownership of the allocation has moved into the resident run.
        _M8LoadStagingStatesWrite[first+2u].y=0u;
        _M8LoadStagingStatesWrite[first+4u].zw=0u.xx;
        _M8LoadStagingStatesWrite[first+5u]=0u.xxxx;
    }
    return result;
}

void M8InstallFlowerPacket(uint item)
{
    uint first=item*M8_LOAD_TILE_RECORDS+512u;
    uint4 packet=_M8LoadStagingStatesWrite[first];
    if(packet.w==M8_FLOWER_LOAD_IMAGE_DONE)return;
    uint encoded=_M8LoadStagingAddressesRead[item].localAddress;
    if((encoded&M8_LOAD_READY_BIT)==0u)
    { _M8LoadStagingStatesWrite[first].w=M8_FLOWER_LOAD_RETRY;return; }
    // An already HOT tile wins over an older SSD response; ancestor-only
    // packets also have no fine image to install.
    if((encoded&M8_LOAD_INSTALL_BIT)==0u)
    { _M8LoadStagingStatesWrite[first].w=M8_FLOWER_LOAD_IMAGE_DONE;return; }
    uint slot=(encoded>>M8_LOAD_SLOT_SHIFT)&0x7fffu;
    uint generation=M8LoadTileRuntimeRead(slot).w;
    uint4 tileMeta=M8LoadTileMetaRead(slot);
    uint4 state=_M8LoadStagingStatesWrite[first+1u];
    uint result=M8_FLOWER_ARENA_OK;
    bool invalid=packet.x>M8_FLOWER_LOAD_BODY_WORDS || packet.y>packet.x ||
        (packet.x&3u)!=0u || (packet.y&3u)!=0u || packet.z>1u || generation==0u ||
        (state.x!=0u && state.x!=generation) || tileMeta.x>=MERKABA_M8_CHUNK_CAPACITY ||
        tileMeta.y>=64u;
    if(!invalid)invalid=_M8ChunkTileRefsRead[tileMeta.x*64u+tileMeta.y]!=MERKABA_REF_LOADING;
    state.x=generation;
    _M8LoadStagingStatesWrite[first+1u]=state;
    uint cursor=packet.y;
    // At most the complete bounded packet is consumed, with whole-record
    // retry. Partial run/group allocations remain owned by this GPU cursor.
    [loop]for(uint step=0u;step<128u && cursor<packet.x && !invalid;step++)
    {
        uint4 owner=_M8LoadStagingStatesWrite[first+2u];
        uint4 group=_M8LoadStagingStatesWrite[first+5u];
        if(owner.y!=0u && group.y==group.z)
        {
            result=M8FlowerPublishImportedRun(first,_M8WorldPublishingGeneration,_M8WorldRetiredGeneration);
            if(result!=M8_FLOWER_ARENA_OK)break;
            owner=_M8LoadStagingStatesWrite[first+2u];
        }
        uint4 importProgram=_M8LoadStagingStatesWrite[first+6u];
        if(owner.y==0u && importProgram.w!=0u &&
            _M8LoadStagingStatesWrite[first+7u].x!=0u)
        {
            result=M8FlowerReleaseImportedProgram(importProgram.y);
            if(result!=M8_FLOWER_ARENA_OK)break;
            _M8LoadStagingStatesWrite[first+6u]=0u.xxxx;
            _M8LoadStagingStatesWrite[first+7u].x=0u;
        }
        uint4 header=_M8LoadStagingStatesWrite[first+8u+cursor/4u];
        uint body=(header.w+15u)/16u*4u,recordWords=4u+body;
        if(header.x<6u || header.x>12u || header.w>112u || (header.w&3u)!=0u ||
            recordWords>packet.x-cursor){invalid=true;break;}
        uint words[28];
        [unroll]for(uint word=0u;word<28u;word++)words[word]=0u;
        [loop]for(uint word=0u;word<header.w/4u;word++)
            words[word]=M8FlowerLoadWord(first,cursor+4u+word);
        if(header.x==6u)
        {
            if(header.w!=8u || header.y>=512u || header.z!=0u || words[0]!=header.y ||
                words[1]==0u || owner.y!=0u){invalid=true;break;}
            uint ownerRef;
            result=M8FlowerEnsureOwnerStorage(slot,header.y,generation,_M8WorldPublishingGeneration,ownerRef);
            if(result!=M8_FLOWER_ARENA_OK)break;
            if(!M8FlowerRestoreOwnerEpoch(ownerRef,words[1],_M8WorldPublishingGeneration))
            {invalid=true;break;}
            _M8LoadStagingStatesWrite[first+2u]=uint4(header.y,0u,words[1],ownerRef);
        }
        else if(header.x==10u)
        {
            if(header.w!=48u || header.y!=0xffffffffu || owner.y!=0u)
            {invalid=true;break;}
            M8ThreadProgramRecord program;
            program.Flags=words[0];program.Reserved0=words[1];
            program.Reserved1=words[2];program.Reserved2=words[3];
            program.OpticalLower=uint2(words[4],words[5]);
            program.OpticalUpper=uint2(words[6],words[7]);
            program.CaptureViewLower=uint2(words[8],words[9]);
            program.CaptureViewUpper=uint2(words[10],words[11]);
            uint programRef,allocation,capacity;
            result=M8FlowerInstallOpticalProgram(program,header.z,programRef,allocation,capacity);
            if(result!=M8_FLOWER_ARENA_OK)break;
            _M8LoadStagingStatesWrite[first+6u]=uint4(header.z,programRef,allocation,capacity);
        }
        else
        {
            if(header.y!=owner.x || owner.w==0u ||
                M8FlowerGetOwnerEpoch(owner.w)!=owner.z){invalid=true;break;}
            if(header.x==7u)
            {
                if(header.w!=16u || header.z!=words[0] || words[3]!=owner.z || owner.y!=0u)
                {invalid=true;break;}
                M8FlowerDetailRecord phase;
                phase.Key=words[0];phase.Lower=asint(words[1]);phase.Upper=asint(words[2]);
                phase.ParentEpoch=words[3];
                result=M8FlowerCommitPhase(owner.w,phase,_M8WorldPublishingGeneration,_M8WorldRetiredGeneration);
                if(result!=M8_FLOWER_ARENA_OK)break;
            }
            else if(header.x==8u || header.x==11u)
            {
                bool thread=header.x==11u;
                if(header.w!=24u || header.z!=words[0] || owner.y!=0u ||
                    words[thread?5u:4u]!=owner.z){invalid=true;break;}
                uint2 split=thread?uint2(words[3],words[4]):uint2(words[2],words[3]);
                if(!M8FlowerCanonicalSplit(split)){invalid=true;break;}
                uint count=countbits(split.x)+countbits(split.y);
                uint sourceBase=words[thread?2u:1u];
                uint4 program=_M8LoadStagingStatesWrite[first+6u];
                if(thread && words[1]!=0xffffffffu &&
                    (program.w==0u || program.x!=words[1])){invalid=true;break;}
                uint residentBase=0xffffffffu,allocation=0u,capacity=0u;
                if(count!=0u)
                {
                    result=M8FlowerAllocateImportedGroups(count,thread,residentBase,allocation,capacity);
                    if(result!=M8_FLOWER_ARENA_OK)break;
                }
                if(thread && words[1]!=0xffffffffu)
                {
                    words[1]=program.y;
                    _M8LoadStagingStatesWrite[first+7u].x=1u;
                }
                words[thread?2u:1u]=residentBase;
                _M8LoadStagingStatesWrite[first+2u].y=header.x;
                _M8LoadStagingStatesWrite[first+3u]=uint4(words[0],words[1],words[2],words[3]);
                _M8LoadStagingStatesWrite[first+4u]=uint4(words[4],words[5],allocation,capacity);
                _M8LoadStagingStatesWrite[first+5u]=uint4(sourceBase,0u,count,thread?1u:0u);
            }
            else
            {
                bool thread=header.x==12u;
                group=_M8LoadStagingStatesWrite[first+5u];
                uint4 a=_M8LoadStagingStatesWrite[first+3u];
                if(owner.y!=(thread?11u:8u) || header.w!=(thread?112u:56u) ||
                    group.y>=group.z || header.z!=group.x+group.y)
                {invalid=true;break;}
                uint4 allocation=_M8LoadStagingStatesWrite[first+4u];
                if(!M8FlowerStoreImportedGroup(thread,thread?a.z:a.y,group.z,group.y,
                    allocation.z,allocation.w,words))
                {invalid=true;break;}
                _M8LoadStagingStatesWrite[first+5u].y=group.y+1u;
            }
        }
        cursor+=recordWords;
    }
    if(!invalid && result==M8_FLOWER_ARENA_OK)
    {
        uint4 owner=_M8LoadStagingStatesWrite[first+2u];
        uint4 group=_M8LoadStagingStatesWrite[first+5u];
        if(owner.y!=0u && group.y==group.z)
            result=M8FlowerPublishImportedRun(first,_M8WorldPublishingGeneration,_M8WorldRetiredGeneration);
        uint4 program=_M8LoadStagingStatesWrite[first+6u];
        if(result==M8_FLOWER_ARENA_OK && _M8LoadStagingStatesWrite[first+2u].y==0u &&
            program.w!=0u && _M8LoadStagingStatesWrite[first+7u].x!=0u)
        {
            result=M8FlowerReleaseImportedProgram(program.y);
            if(result==M8_FLOWER_ARENA_OK)
            {
                _M8LoadStagingStatesWrite[first+6u]=0u.xxxx;
                _M8LoadStagingStatesWrite[first+7u].x=0u;
            }
        }
        if(packet.z!=0u && cursor==packet.x && result==M8_FLOWER_ARENA_OK &&
            _M8LoadStagingStatesWrite[first+2u].y!=0u)invalid=true;
    }
    uint status=invalid || result==M8_FLOWER_ARENA_INVALID ? M8_FLOWER_LOAD_INVALID :
        result!=M8_FLOWER_ARENA_OK ? M8_FLOWER_LOAD_RETRY : cursor!=packet.x ? 0u :
        packet.z!=0u ? M8_FLOWER_LOAD_IMAGE_DONE : M8_FLOWER_LOAD_PACKET_DONE;
    if(result==M8_FLOWER_ARENA_CAPACITY)
    {
        _M8Counters[M8_COUNTER_STORAGE_BACKPRESSURE]=1u;
        _M8Counters[M8_COUNTER_EVICTION_NEEDED]=1u;
    }
    _M8LoadStagingStatesWrite[first]=uint4(packet.x,cursor,packet.z,status);
}

void M8AcknowledgeFlowerCapture(uint item)
{
    uint first=item*M8_WRITEBACK_TILE_RECORDS+1u+512u;
    uint4 packet=_M8WritebackStaging[first];
    if(packet.z==M8_FLOWER_CAPTURE_ACK_READY)return;
    uint2 queued=_M8WritebackQueueRead[item];
    uint slot=queued.x,refIndex=queued.y&M8_WRITEBACK_REF_MASK;
    if(packet.y!=1u || packet.z!=0u || slot>=32768u ||
        packet.w!=M8LoadTileRuntimeRead(slot).w ||
        _M8ChunkTileRefsRead[refIndex]!=MERKABA_REF_EVICTING)return;
    bool keepHot=(queued.y&M8_WRITEBACK_KEEP_HOT)!=0u || M8TilePinnedByObservation(slot);
    if(!keepHot)
    {
        if(M8FlowerRetireTile(slot,packet.w,_M8WorldRetiredGeneration)!=M8_FLOWER_ARENA_OK)return;
    }
    else
    {
        uint2 tile=_M8FlowerDetailPages.Load2(M8_FLOWER_TILE_DIRECTORY+16u*slot);
        if(tile.x!=0u && tile.y!=packet.w)return;
        if(tile.x!=0u)
            [loop]for(uint local=0u;local<512u;local++)
            {
                uint owner=_M8FlowerDetailPages.Load(tile.x+4u*local);
                if(owner!=0u)
                {
                    uint flags=_M8FlowerDetailPages.Load(owner+60u);
                    _M8FlowerDetailPages.Store(owner+60u,flags&~2u);
                }
            }
    }
    DeviceMemoryBarrier();
    _M8WritebackStaging[first].z=M8_FLOWER_CAPTURE_ACK_READY;
}

void M8CancelFlowerPacket(uint item)
{
    uint first=item*M8_LOAD_TILE_RECORDS+512u;
    uint4 packet=_M8LoadStagingStatesWrite[first];
    if(packet.w==M8_FLOWER_LOAD_CANCELLED)return;
    uint encoded=_M8LoadStagingAddressesRead[item].localAddress;
    if((encoded&M8_LOAD_INSTALL_BIT)!=0u && packet.w!=M8_FLOWER_LOAD_IMAGE_DONE)
    {
        uint slot=(encoded>>M8_LOAD_SLOT_SHIFT)&0x7fffu;
        uint4 meta=M8LoadTileMetaRead(slot);
        uint generation=M8LoadTileRuntimeRead(slot).w;
        uint4 state=_M8LoadStagingStatesWrite[first+1u];
        if(meta.x>=MERKABA_M8_CHUNK_CAPACITY || meta.y>=64u || generation==0u ||
            (state.x!=0u && state.x!=generation) ||
            _M8ChunkTileRefsRead[meta.x*64u+meta.y]!=MERKABA_REF_LOADING)return;
        uint4 allocation=_M8LoadStagingStatesWrite[first+4u];
        if(allocation.w!=0u)
        {
            bool thread=_M8LoadStagingStatesWrite[first+5u].w!=0u;
            if(M8FlowerCancelImportedGroups(thread,allocation.z,allocation.w)!=M8_FLOWER_ARENA_OK)return;
            _M8LoadStagingStatesWrite[first+4u].zw=0u.xx;
        }
        uint4 program=_M8LoadStagingStatesWrite[first+6u];
        if(program.w!=0u)
        {
            if(M8FlowerReleaseImportedProgram(program.y)!=M8_FLOWER_ARENA_OK)return;
            _M8LoadStagingStatesWrite[first+6u]=0u.xxxx;
        }
        if(M8FlowerRetireTile(slot,generation,_M8WorldRetiredGeneration)!=M8_FLOWER_ARENA_OK)return;
    }
    DeviceMemoryBarrier();
    _M8LoadStagingStatesWrite[first].w=M8_FLOWER_LOAD_CANCELLED;
}

[numthreads(1,1,1)]
void TransferFlowerStorage(uint id:SV_DispatchThreadID)
{
    // A single bounded storage dispatch serializes the two page allocators;
    // no per-owner dispatch or cross-workgroup allocator contention is needed.
    [loop]for(uint item=0u;item<min(_M8StreamBatchCount,32u);item++)
        if(_M8FlowerStorageMode==0u)M8InstallFlowerPacket(item);
        else if(_M8FlowerStorageMode==1u)M8AcknowledgeFlowerCapture(item);
        else if(_M8FlowerStorageMode==2u)M8CancelFlowerPacket(item);
}
#endif
