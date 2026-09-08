// The signal half of the skin stage, included once per signal. No include
// guard: MerkabaFlowerRefinement.hlsl defines M8_SKIN_PACKET_NAME and
// M8_SKIN_SIGNAL and includes it twice. It consumes the resolver's receipt
// and performs no parent prediction, residual application or carrier
// classification of its own.

void M8_SKIN_PACKET_NAME(uint slot,uint lane,uint generation,uint carrier,
    uint parentCursor,uint budget,float2 errors)
{
#if M8_SKIN_SIGNAL==0
    M8FlowerRestoreSkinCarrier(slot,lane,generation,M8_FLOWER_SKIN_RECEIPT_STATUS_RESOLVED);
#else
    // V consumes only a receipt whose RGB half committed.
    M8FlowerRestoreSkinCarrier(slot,lane,generation,M8_FLOWER_SKIN_RECEIPT_STATUS_RGB_OK);
#endif
    GroupMemoryBarrierWithGroupSync();
    [loop]while(true)
    {
        if(lane<m8FinePacketCount)
        {
            uint control=M8FlowerFineSkinControl(lane);
            bool ready=M8FlowerPacketLoadWord(control+4u)<57u &&
                M8FlowerPacketLoadWord(control+6u)<budget &&
                M8FlowerPacketLoadWord(control+8u)==M8_FLOWER_ARENA_OK;
            M8FlowerPacketStoreWord(control+16u,ready?1u:0u);
        }
        GroupMemoryBarrierWithGroupSync();
        if(lane==0u)
        {
            uint ready=0u;
            [unroll]for(uint ownerIndex=0u;ownerIndex<3u;ownerIndex++)
                if(ownerIndex<m8FinePacketCount && M8FlowerPacketLoadWord(M8FlowerFineSkinControl(ownerIndex)+16u)!=0u)
                    ready|=1u<<ownerIndex;
            M8FlowerPacketStoreWord(M8_FINE_PACKET_READY,ready);
        }
        GroupMemoryBarrierWithGroupSync();
        uint ready=M8FlowerPacketLoadWord(M8_FINE_PACKET_READY);
        if(ready==0u)break;
        // All lanes execute both signal barriers, including absent/pruned
        // owners. A BUSY RGB writer cannot fall through into V publication.
        // One signal per entry point. RGB writes ThreadAtlas, V writes the
        // FlowerDetail metric signal; they never share a module.
        {
            const uint signal=M8_SKIN_SIGNAL;
            if(lane<m8FinePacketCount)
            {
                uint control=M8FlowerFineSkinControl(lane),local=m8FinePacketFirst+lane;
                uint classification=M8_FLOWER_SKIN_AMBIGUOUS;
                M8FlowerSkinGroupContext context=(M8FlowerSkinGroupContext)0;
                if((ready&(1u<<lane))!=0u && M8FlowerPacketLoadWord(control+8u)==M8_FLOWER_ARENA_OK)
                {
                    uint next=M8FlowerPacketLoadWord(control+4u),parentOrdinal=0u;
                    if(next>0u && next<8u)parentOrdinal=1u+M8FlowerSkinL3ChildRankAt(next-1u);
                    else if(next>=8u)parentOrdinal=8u+M8FlowerSkinL4ParentThreadAt(next-8u);
                    M8FlowerPacketStoreWord(control+9u,parentOrdinal);
                    if(!M8FlowerSkinParentTouchesWedges(parentOrdinal,M8FlowerPacketLoadWord(control+2u)))
                    {
                        M8FlowerPhaseRootEvidence roots[7];M8FlowerFineSkinRoots(lane,roots);
                        uint status=M8FlowerPrepareSkinGroup(slot,local,generation,
                            M8FlowerPacketLoadWord(control),parentOrdinal,M8FlowerPacketLoadWord(control+1u),
                            roots,signal!=0u,context,classification);
                        M8FlowerPacketStoreWord(control+8u,status);
                        [unroll]for(uint site=0u;site<7u;site++)
                            if(M8FlowerPacketLoadWord(M8_FINE_PACKET_WORLD_VALID+7u*lane+site)==0u)
                                context.Measure=false;
                    }
                }
                M8FlowerFineStoreSkinContext(lane,context);
                M8FlowerPacketStoreWord(control+11u+signal,classification);
            }
            GroupMemoryBarrierWithGroupSync();
            if(lane<7u*m8FinePacketCount)
            {
                uint ownerIndex=lane/7u,child=lane%7u,control=M8FlowerFineSkinControl(ownerIndex);
                M8FlowerSkinGroupContext context=M8FlowerFineLoadSkinContext(ownerIndex);
                uint flags=0u;
                if(context.Measure)
                {
                    M8FlowerInterval3 sites[7];
                    [loop]for(uint site=0u;site<7u;site++)sites[site]=M8FlowerFineLoadWorld(7u*ownerIndex+site);
                    bool supported,certain;
                    uint parentOrdinal=M8FlowerPacketLoadWord(control+9u),active=M8FlowerPacketLoadWord(control+1u);
                    int3 owner=M8FlowerEndpointOwner(slot,m8FinePacketFirst+ownerIndex);
                    if(signal==0u)
                    {
                        M8ThreadColorInterval value;
                        certain=M8FlowerMeasureRgbSkinChild(sites,parentOrdinal,active,child,owner,
                            context.Flags,errors.x,errors.y,context.Inherited,value,supported);
                        uint address=M8_FINE_PACKET_SIGNAL_VALUES+4u*lane;
                        M8FlowerPacketStoreWord(address,value.LowerLinearRgba.x);
                        M8FlowerPacketStoreWord(address+1u,value.LowerLinearRgba.y);
                        M8FlowerPacketStoreWord(address+2u,value.UpperLinearRgba.x);
                        M8FlowerPacketStoreWord(address+3u,value.UpperLinearRgba.y);
                    }
                    else
                    {
                        M8FlowerSkinMetricRun run;
                        run.FlowerKey=M8FlowerPacketLoadWord(control);run.GroupBase=context.GroupBase;
                        run.SplitBitsLo=context.SplitBits.x;run.SplitBitsHi=context.SplitBits.y;
                        run.ParentEpoch=context.Epoch;run.Reserved=context.Reserved;
                        M8FlowerVInterval value;
                        certain=M8FlowerMeasureMetricSkinChild(sites,parentOrdinal,active,child,owner,
                            context.Flags,errors.x,errors.y,run,context.Existing,value,supported);
                        M8FlowerPacketStoreWord(M8_FINE_PACKET_SIGNAL_VALUES+2u*lane,asuint(value.Lower));
                        M8FlowerPacketStoreWord(M8_FINE_PACKET_SIGNAL_VALUES+2u*lane+1u,asuint(value.Upper));
                    }
                    flags=(certain?1u:0u)|(supported?2u:0u);
                }
                M8FlowerPacketStoreWord(M8_FINE_PACKET_SIGNAL_FLAGS+lane,flags);
            }
            GroupMemoryBarrierWithGroupSync();
            if(lane<m8FinePacketCount)
            {
                uint control=M8FlowerFineSkinControl(lane);
                M8FlowerSkinGroupContext context=M8FlowerFineLoadSkinContext(lane);
                if(context.Measure)
                {
                    uint certain=0u,support=0u;
                    [loop]for(uint child=0u;child<7u;child++)
                    {
                        uint flags=M8FlowerPacketLoadWord(M8_FINE_PACKET_SIGNAL_FLAGS+7u*lane+child);
                        if((flags&1u)!=0u)certain|=1u<<child;
                        if((flags&2u)!=0u)support|=1u<<child;
                    }
                    uint classification=M8_FLOWER_SKIN_AMBIGUOUS,status;
                    bool changed;
                    uint local=m8FinePacketFirst+lane,key=M8FlowerPacketLoadWord(control);
                    uint parentOrdinal=M8FlowerPacketLoadWord(control+9u);
                    if(signal==0u)
                    {
                        M8ThreadColorInterval children[7];
                        [loop]for(uint child=0u;child<7u;child++)
                        {
                            uint address=M8_FINE_PACKET_SIGNAL_VALUES+4u*(7u*lane+child);
                            children[child].LowerLinearRgba=uint2(M8FlowerPacketLoadWord(address),M8FlowerPacketLoadWord(address+1u));
                            children[child].UpperLinearRgba=uint2(M8FlowerPacketLoadWord(address+2u),M8FlowerPacketLoadWord(address+3u));
                        }
                        if(certain==127u)classification=M8FlowerClassifyMeasuredRgbSkin(children,certain,support);
                        status=M8FlowerCommitRgbSkinSplit(slot,local,generation,key,parentOrdinal,
                            context,children,classification,support,changed);
                    }
                    else
                    {
                        M8FlowerVInterval children[7];
                        [loop]for(uint child=0u;child<7u;child++)
                        {
                            uint address=M8_FINE_PACKET_SIGNAL_VALUES+2u*(7u*lane+child);
                            children[child].Lower=asint(M8FlowerPacketLoadWord(address));
                            children[child].Upper=asint(M8FlowerPacketLoadWord(address+1u));
                        }
                        if(certain==127u)classification=M8FlowerClassifyMeasuredMetricSkin(children,certain,support);
                        status=M8FlowerCommitMetricSkinSplit(slot,local,generation,key,parentOrdinal,
                            context,children,classification,support,changed);
                    }
                    M8FlowerPacketStoreWord(control+11u+signal,classification);
                    M8FlowerPacketStoreWord(control+8u,status);
#if M8_SKIN_SIGNAL==0
                    // A BUSY RGB writer must not fall through into V
                    // publication. The receipt carries that decision across
                    // the dispatch boundary the shared barrier used to carry.
                    _M8FlowerDetailPages.Store(
                        M8FlowerSkinReceipt(m8FineReceiptTile,local)+M8_FLOWER_SKIN_RECEIPT_STATUS,
                        status==M8_FLOWER_ARENA_OK?M8_FLOWER_SKIN_RECEIPT_STATUS_RGB_OK:
                            M8_FLOWER_SKIN_RECEIPT_STATUS_RGB_BUSY);
#endif
                    if(changed)M8FlowerPacketStoreWord(control+5u,M8FlowerPacketLoadWord(control+5u)+1u);
                }
            }
            // The next signal/parent sees this owner's complete group, never
            // a prefix. Its siblings remain independent owner-exclusive jobs.
            DeviceMemoryBarrierWithGroupSync();
        }
        if(lane<m8FinePacketCount && (ready&(1u<<lane))!=0u)
        {
            uint control=M8FlowerFineSkinControl(lane);
            if(M8FlowerPacketLoadWord(control+8u)==M8_FLOWER_ARENA_OK)
            {
                uint next=M8FlowerPacketLoadWord(control+4u)+1u;
                uint ambiguous=(M8FlowerPacketLoadWord(control+11u)==M8_FLOWER_SKIN_AMBIGUOUS?1u:0u)+
                    (M8FlowerPacketLoadWord(control+12u)==M8_FLOWER_SKIN_AMBIGUOUS?1u:0u);
                M8FlowerPacketStoreWord(control+7u,M8FlowerPacketLoadWord(control+7u)+ambiguous);
                M8FlowerPacketStoreWord(control+5u,M8FlowerPacketLoadWord(control+5u)+1u);
                M8FlowerPacketStoreWord(control+6u,M8FlowerPacketLoadWord(control+6u)+1u);
                if(next==1u && !M8FlowerSkinHasRootRefinement(slot,m8FinePacketFirst+lane,generation,
                    M8FlowerPacketLoadWord(control)))next=57u;
                M8FlowerPacketStoreWord(control+4u,next);
            }
        }
        GroupMemoryBarrierWithGroupSync();
    }
    if(lane<m8FinePacketCount)
    {
        uint control=M8FlowerFineSkinControl(lane);
        uint progress=M8FlowerPacketLoadWord(control+5u),ambiguous=M8FlowerPacketLoadWord(control+7u);
        if(progress!=0u)InterlockedAdd(_M8Counters[M8_COUNTER_REFINEMENT_WORK_PROGRESS],progress);
        if(ambiguous!=0u)InterlockedAdd(_M8Counters[M8_COUNTER_REFINEMENT_UNRESOLVED],ambiguous);
        InterlockedMax(m8FineSkinConsumed,min(budget,max(1u,progress)));
        InterlockedMin(m8FineSkinNext,M8FlowerPacketLoadWord(control+4u));
        M8FlowerFineSchedulingStatus(M8FlowerPacketLoadWord(control+8u));
    }
    DeviceMemoryBarrierWithGroupSync();
}
