// Included for RGB and V independently. RGB capacity/ambiguity never vetoes V.
void M8_SKIN_PACKET_NAME(uint3 group,uint lane)
{
    if(_M8Counters[M8_COUNTER_OBSERVATION_COMPLETED]!=0u)return;
    uint4 ownerWork;
    if(!M8FlowerReadSignalOwner(group,ownerWork))return;
    uint slot=ownerWork.x>>9u,local=ownerWork.x&511u,generation=ownerWork.w;
    if(slot>=_M8ObservationHotSlotCount)return;
    uint4 runtime=_M8TileRecords[M8TileRuntimeIndex(slot)];
    if(runtime.w!=generation || runtime.x!=_M8ObservationToken)return;
    const bool metric=M8_SKIN_SIGNAL!=0;
    float2 errors=float2(M8FlowerNext(_M8PlaneErrorBounds.x+M8_FLOWER_NORMAL_QUANTIZATION_UPPER),
        M8FlowerNext(_M8PlaneErrorBounds.y+M8_FLOWER_OFFSET_QUANTIZATION_UPPER));
    uint item=ownerWork.y;
    // Actual carriers of one measured owner, never the 128 possible carriers.
    // Keeping its writers together protects the owner's sorted run directory.
    [loop]for(uint carrier=0u;carrier<ownerWork.z;carrier++)
    {
        if(item<M8_FLOWER_SIGNAL_ITEMS_BASE ||
            item>M8_FLOWER_SIGNAL_BUFFER_BYTES-M8_FLOWER_SIGNAL_ITEM_BYTES ||
            (item-M8_FLOWER_SIGNAL_ITEMS_BASE)%M8_FLOWER_SIGNAL_ITEM_BYTES!=0u)
        {if(lane==0u)M8FlowerBinFailure(M8_OBSERVATION_FAILURE_MEASUREMENT_IDENTITY);return;}
        uint4 source=_M8FlowerSignalItemsRead.Load4(item+M8_FLOWER_SIGNAL_SOURCE);
        uint4 symbol=_M8FlowerSignalItemsRead.Load4(item+M8_FLOWER_SIGNAL_SYMBOL);
        uint4 reach=_M8FlowerSignalItemsRead.Load4(item+M8_FLOWER_SIGNAL_REACH);
        uint2 reachedParents=_M8FlowerSignalItemsRead.Load2(item+M8_FLOWER_SIGNAL_PARENTS);
        uint next=reach.w;
        KernelState state=M8LoadKernelStateRead(slot,local);
        uint ownerRef=M8FlowerFindOwner(slot,local,generation);
        bool live=source.x==ownerWork.x && source.y==generation &&
            source.z==_M8ObservationToken && source.w==state.flags &&
            (reach.x&symbol.w)==reach.x && symbol.y!=0u &&
            (reach.z==0u || reach.z==M8FlowerGetOwnerEpoch(ownerRef));
        if(!live || !any(reachedParents!=0u)){item=next;continue;}
        if(!M8FlowerCanonicalSplit(reachedParents))
        {if(lane==0u)M8FlowerBinFailure(M8_OBSERVATION_FAILURE_MEASUREMENT_IDENTITY);item=next;continue;}
        M8FlowerInterval3 sites[7];
        if(lane<7u)
        {
            [loop]for(uint site=0u;site<7u;site++)
            {
                uint address=item+M8_FLOWER_SIGNAL_WORLD+24u*site;
                float4 xy=asfloat(_M8FlowerSignalItemsRead.Load4(address));
                float2 z=asfloat(_M8FlowerSignalItemsRead.Load2(address+16u));
                sites[site].x=M8FlowerI(xy.x,xy.y);
                sites[site].y=M8FlowerI(xy.z,xy.w);
                sites[site].z=M8FlowerI(z.x,z.y);
            }
        }
        if(lane==0u){m8SignalParents=uint2(reachedParents.x&1u,0u);m8SignalStatus=M8_FLOWER_ARENA_OK;}
        GroupMemoryBarrierWithGroupSync();
        // Three fixed substitutions. Masks die with this dispatch; no cursor,
        // quantum, level mode or future observation participates in evaluation.
        [loop]for(uint depth=0u;depth<3u;depth++)
        {
            uint2 parents=m8SignalParents;
            GroupMemoryBarrierWithGroupSync();
            if(lane==0u)m8SignalParents=0u;
            GroupMemoryBarrierWithGroupSync();
            [loop]while(any(parents!=0u))
            {
                uint word=parents.x!=0u?0u:1u;
                uint bit=(uint)firstbitlow(parents[word]);parents[word]&=parents[word]-1u;
                uint parent=32u*word+bit;
                if(lane==0u)
                {
                    uint classification;
                    m8SignalContext=(M8FlowerSkinGroupContext)0;
                    if(m8SignalStatus==M8_FLOWER_ARENA_OK &&
                        !M8FlowerSkinParentTouchesWedges(parent,symbol.z))
                        m8SignalStatus=M8FlowerPrepareSkinGroup(slot,local,generation,
                            symbol.x,parent,symbol.y,source.w,metric,m8SignalContext,classification);
                }
                GroupMemoryBarrierWithGroupSync();
                if(lane<7u)
                {
                    uint4 words=0u;bool supported=false,certain=false;
                    if(m8SignalContext.Measure)
                    {
#if M8_SKIN_SIGNAL==0
                        M8ThreadColorInterval value;
                        certain=M8FlowerMeasureRgbSkinChild(sites,parent,symbol.y,lane,
                            M8FlowerEndpointOwner(slot,local),source.w,errors.x,errors.y,
                            m8SignalContext.Inherited,value,supported);
                        words=uint4(value.LowerLinearRgba,value.UpperLinearRgba);
#else
                        M8FlowerSkinMetricRun run;
                        run.FlowerKey=symbol.x;run.GroupBase=m8SignalContext.GroupBase;
                        run.SplitBitsLo=m8SignalContext.SplitBits.x;
                        run.SplitBitsHi=m8SignalContext.SplitBits.y;
                        run.ParentEpoch=m8SignalContext.Epoch;run.Reserved=m8SignalContext.Reserved;
                        M8FlowerVInterval value;
                        certain=M8FlowerMeasureMetricSkinChild(sites,parent,symbol.y,lane,
                            M8FlowerEndpointOwner(slot,local),source.w,errors.x,errors.y,
                            run,m8SignalContext.Existing,value,supported);
                        words=uint4(asuint(value.Lower),asuint(value.Upper),0u,0u);
#endif
                    }
                    m8SignalValues[lane]=words;
                    m8SignalFlags[lane]=(certain?1u:0u)|(supported?2u:0u);
                }
                GroupMemoryBarrierWithGroupSync();
                if(lane==0u && m8SignalContext.Measure)
                {
                    uint certain=0u,support=0u,classification=M8_FLOWER_SKIN_AMBIGUOUS;
                    [unroll]for(uint child=0u;child<7u;child++)
                    {
                        if((m8SignalFlags[child]&1u)!=0u)certain|=1u<<child;
                        if((m8SignalFlags[child]&2u)!=0u)support|=1u<<child;
                    }
                    bool changed;
#if M8_SKIN_SIGNAL==0
                    M8ThreadColorInterval children[7];
                    [unroll]for(uint child=0u;child<7u;child++)
                    {
                        children[child].LowerLinearRgba=m8SignalValues[child].xy;
                        children[child].UpperLinearRgba=m8SignalValues[child].zw;
                    }
                    if(certain==127u)classification=M8FlowerClassifyMeasuredRgbSkin(children,certain,support);
                    m8SignalStatus=M8FlowerCommitRgbSkinSplit(slot,local,generation,symbol.x,parent,
                        m8SignalContext,children,classification,support,changed);
#else
                    M8FlowerVInterval children[7];
                    [unroll]for(uint child=0u;child<7u;child++)
                    {children[child].Lower=asint(m8SignalValues[child].x);children[child].Upper=asint(m8SignalValues[child].y);}
                    if(certain==127u)classification=M8FlowerClassifyMeasuredMetricSkin(children,certain,support);
                    m8SignalStatus=M8FlowerCommitMetricSkinSplit(slot,local,generation,symbol.x,parent,
                        m8SignalContext,children,classification,support,changed);
#endif
                    if(classification==M8_FLOWER_SKIN_AMBIGUOUS)M8CounterIncrement(M8_COUNTER_REFINEMENT_UNRESOLVED);
                    if(changed)M8CounterIncrement(M8_COUNTER_REFINEMENT_WORK_PROGRESS);
                    if(m8SignalStatus!=M8_FLOWER_ARENA_OK)M8CounterIncrement(M8_COUNTER_REFINEMENT_BACKPRESSURE);
                    bool split=(m8SignalContext.SplitBits[parent>>5u]&(1u<<(parent&31u)))!=0u || changed;
                    if(split && depth<2u && m8SignalStatus==M8_FLOWER_ARENA_OK)
                    {
                        uint level,c3,c4;M8FlowerSkinParentAddress(parent,level,c3,c4);
                        [unroll]for(uint child=0u;child<7u;child++)
                        {
                            if((support&(1u<<child))==0u)continue;
                            uint ordinal=depth==0u?1u+M8FlowerSkinL3ChildRankAt(child):
                                8u+7u*(parent-1u)+M8FlowerSkinL4ChildRankAt(7u*c3+child);
                            uint bit=1u<<(ordinal&31u);
                            m8SignalParents[ordinal>>5u]|=reachedParents[ordinal>>5u]&bit;
                        }
                    }
                }
                DeviceMemoryBarrierWithGroupSync();
            }
        }
        item=next;
    }
}
