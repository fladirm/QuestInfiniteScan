#ifndef GENESIS_MERKABA_FLOWER_SUPPORT_INCLUDED
#define GENESIS_MERKABA_FLOWER_SUPPORT_INCLUDED

#include "MerkabaFlowerGeometry.hlsl"

// Page-local direct decode scratch, dead when this workgroup retires.
#if defined(M8_FLOWER_SUPPORT_SCRATCH_WORDS)
groupshared uint m8FlowerSupportScratch[M8_FLOWER_SUPPORT_SCRATCH_WORDS];
#endif

// Canonical draw ownership is generated incidence, not a negative-volume test.
// Classification and metric values are exactly the direct Flower evaluator's.
uint M8FlowerReadSelectedCarrier(uint slot,uint local,uint carrier,float2 errors,
    out M8FlowerSymbolRecord symbol,out uint unresolved,out uint directWedges);

uint M8FlowerPageCarrier(uint slot,uint local,uint carrier,float2 errors,
    out M8FlowerSymbolRecord symbol,out uint unresolved,
    out uint directWedges)
{
    uint status=M8FlowerReadSelectedCarrier(slot,local,carrier,errors,
        symbol,unresolved,directWedges);
    uint owned=M8FlowerL2CarrierOwnedMask(carrier);
    unresolved &= owned;
    if(status!=1u)return status==2u && unresolved!=0u?2u:0u;
    uint active=M8FlowerDrawActiveWedgeMask(symbol)&owned;
    symbol.OwnerAndCarrier=(symbol.OwnerAndCarrier&
        ~(M8_FLOWER_DRAW_WEDGE_MASK<<M8_FLOWER_DRAW_ACTIVE_SHIFT))|
        (active<<M8_FLOWER_DRAW_ACTIVE_SHIFT);
    uint reverse=M8FlowerDrawReverseWedgeMask(symbol)&active;
    uint used=M8FlowerCarrierUsedSites(active);
    symbol.RootsAndWedges=(M8FlowerDrawHubSector(symbol)<<M8_FLOWER_DRAW_HUB_SECTOR_SHIFT)|
        (M8FlowerDrawRootSigns(symbol)&used)|(reverse<<M8_FLOWER_DRAW_REVERSE_SHIFT);
    return active!=0u?1u:unresolved!=0u?2u:0u;
}

#endif
