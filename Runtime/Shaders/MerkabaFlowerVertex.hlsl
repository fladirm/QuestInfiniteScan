#ifndef GENESIS_MERKABA_FLOWER_VERTEX_INCLUDED
#define GENESIS_MERKABA_FLOWER_VERTEX_INCLUDED

#include "MerkabaWorld.hlsl"
#include "MerkabaFlowerPages.hlsl"
#define M8_FLOWER_HALO_READ_ONLY
#include "MerkabaFlowerTileHalo.hlsl"
#include "MerkabaFlowerGeometry.hlsl"
#include "MerkabaFlowerSkinReadout.hlsl"

float2 _M8FlowerPlaneErrorBounds;

struct M8FlowerGraphicsVertex
{
    float3 GridPosition;
    float2 Chart;
    M8FlowerSymbolRecord Symbol;
    uint PackedColor;
    uint ParentEpoch;
    uint PlaneFlags;
};

bool M8FlowerDrawRange(uint address,uint bytes)
{
    return address>=M8_FLOWER_DRAW_DATA && bytes<=67108864u &&
        address-M8_FLOWER_DRAW_DATA<=67108864u-bytes;
}

// The indexed draw's firstInstance identifies the physical page. Its
// vertexOffset is 7*FirstSymbol, so each of the seven complete VS outputs
// belongs to one carrier, independent of its incident wedge/normal/material.
bool M8FlowerReadGraphicsVertex(uint vertexId,uint pageSlot,
    out M8FlowerGraphicsVertex result)
{
    result=(M8FlowerGraphicsVertex)0;
    uint symbolIndex=vertexId/7u,site=vertexId%7u;
    M8FlowerPageHeader page;
    if(!M8FlowerFrontPage(pageSlot,page) || symbolIndex<page.FirstSymbol ||
        symbolIndex-page.FirstSymbol>=page.SymbolCount)return false;
    // The same source guard is evaluated by ALL seven shared vertices. A
    // changed parent cannot leave one updated ring vertex beside an old hub.
    // The serialized native draw lease excludes mutation after this check;
    // M8FlowerFrontPage keeps source invalidity independent of dirty queue
    // membership, until a complete replacement has actually been published.
    uint slotGeneration=M8LoadTileRuntimeRead(pageSlot).w;
    if(slotGeneration==0u || _M8FlowerPageDirectoryRead.Load(
        M8_FLOWER_PAGE_AUX_BASE+32u*pageSlot+28u)!=slotGeneration)return false;
    result.Symbol=M8FlowerLoadSymbol(symbolIndex);
    result.Chart=M8FlowerCarrierChartSite(site);
    if(M8FlowerDrawIsDirt(result.Symbol))
    {
        int3 cell;
        if(!M8FlowerTryDirtCell(result.Symbol,page.LogicalTile,cell))return false;
        // Only fan triangle 0=(H,R0,R1) is used by a DIRT half-face. The
        // remaining shared sites equal H, making the other five fans degenerate.
        uint corner=site<3u?site:0u;
        uint face=M8FlowerDrawCarrierId(result.Symbol);
        uint halfFace=result.Symbol.RootsAndWedges&1u;
        int3 lattice;
        if(!M8FlowerTryDirtVertex(cell,face,halfFace,corner,lattice))return false;
        result.GridPosition=M8FlowerDirtGridPosition(cell,face,halfFace,corner);
        return true;
    }
    if(!M8FlowerIsCanonicalCarrierSymbol(result.Symbol) ||
        any(page.LogicalTile < -268435456) || any(page.LogicalTile > 268435455) ||
        !all(M8FlowerIsFinite(_M8FlowerPlaneErrorBounds)) ||
        _M8FlowerPlaneErrorBounds.x<M8_FLOWER_NORMAL_QUANTIZATION_UPPER ||
        _M8FlowerPlaneErrorBounds.y<M8_FLOWER_OFFSET_QUANTIZATION_UPPER)return false;
    uint local=M8FlowerDrawKernelLocal(result.Symbol);
    int3 owner=page.LogicalTile*8+int3(local&7u,(local>>3u)&7u,local>>6u);
    KernelState state=M8LoadKernelStateRead(pageSlot,local);
    uint ownerRef=result.Symbol.DetailRef;
    if(ownerRef!=0u && M8FlowerFindOwner(pageSlot,local,slotGeneration)!=ownerRef)return false;
    result.ParentEpoch=M8FlowerGetOwnerEpoch(ownerRef);
    result.PackedColor=state.packedColor;
    result.PlaneFlags=state.flags;
    uint carrier=M8FlowerDrawCarrierId(result.Symbol);
    if(site!=0u)
    {
        uint adjacent=(1u<<(site-1u))|(1u<<((site+4u)%6u));
        // An unused site has no admitted branch. Collapse only that disposable
        // fan corner to H instead of evaluating a fabricated default sign or
        // stretching an inactive triangle to an invalid position.
        if((M8FlowerDrawActiveWedgeMask(result.Symbol)&adjacent)==0u)
        {site=0u;result.Chart=0.0;}
    }
    uint knot=M8FlowerL2CarrierKnot(carrier,site);
    bool plus=(M8FlowerDrawRootSigns(result.Symbol)&(1u<<site))!=0u;
    M8FlowerPhaseRootEvidence root;
    if(!M8FlowerReadL2Knot(pageSlot,ownerRef,owner,state.flags,knot,plus,
        _M8FlowerPlaneErrorBounds.x,_M8FlowerPlaneErrorBounds.y,root))return false;
    if(site==0u && ((root.Tag>>8u)&31u)!=M8FlowerDrawHubSector(result.Symbol))return false;
    return M8FlowerRootGridPosition(root,result.GridPosition);
}

float3 M8FlowerCapturedColor(uint packedColor)
{
    return float3(packedColor&255u,(packedColor>>8u)&255u,
        (packedColor>>16u)&255u)*(1.0/255.0);
}

// On this already rasterized planar L2 wedge, p=O+u*TU+v*TV.
// Therefore dp=TU*du+TV*dv for the SAME perspective-correct varyings.
// Inverting that 2x2 relation cancels screen coordinates and recovers the
// actual L2 material frame; it is not a surface fit or a finite-difference V
// normal map. All V derivatives below remain the analytic bubble derivatives.
bool M8FlowerGraphicsFrame(float3 worldPosition,float2 chart,uint wedge,
    float3 dpdx,float3 dpdy,float2 duvdx,float2 duvdy,
    out float3 tangentU,out float3 tangentV,out float3 normal,
    out float3 barycentricU,out float3 barycentricV)
{
    tangentU=tangentV=normal=barycentricU=barycentricV=0.0;
    precise float determinant=duvdx.x*duvdy.y-duvdx.y*duvdy.x;
    if(determinant==0.0 || !M8FlowerIsFinite(determinant))return false;
    precise float3 chartU=(dpdx*duvdy.y-dpdy*duvdx.y)/determinant;
    precise float3 chartV=(dpdy*duvdx.x-dpdx*duvdy.x)/determinant;
    precise float3 origin=worldPosition-chart.x*chartU-chart.y*chartV;
    float2 a=M8FlowerCarrierChartSite(1u+wedge);
    float2 b=M8FlowerCarrierChartSite(1u+(wedge+1u)%6u);
    precise float3 p1=origin+a.x*chartU+a.y*chartV;
    precise float3 p2=origin+b.x*chartU+b.y*chartV;
    if(!all(M8FlowerIsFinite(origin)) || !all(M8FlowerIsFinite(p1)) || !all(M8FlowerIsFinite(p2)))return false;
    return M8FlowerSkinRuntimeFrame(origin,p1,p2,tangentU,tangentV,normal,
        barycentricU,barycentricV);
}

void M8FlowerBaseGraphicsSample(float3 captured,out M8FlowerSkinDrawSample sample)
{
    sample.CapturedRgb=captured;sample.Flags=0u;
    sample.Amplitude3=sample.Amplitude4=sample.Amplitude5=0;
    sample.Reserved=0u;sample.Optical=sample.CaptureView=0.0;
}

bool M8FlowerReadGraphicsSkin(M8FlowerSymbolRecord symbol,uint parentEpoch,
    uint wedge,M8FlowerSkinLocality locality,float3 captured,
    out M8FlowerSkinDrawSample sample)
{
    M8FlowerBaseGraphicsSample(captured,sample);
    if(symbol.ThreadRef==0xffffffffu)return true;
    // All six wedges address the SAME carrier thread. Wedge affects only
    // locality descent, never the persistent key or a second appearance tape.
    uint address=symbol.ThreadRef;
    if(wedge>=6u || (address&63u)!=0u || !M8FlowerDrawRange(address,64u))return false;
    M8FlowerSkinDrawHeader header=M8FlowerLoadSkinDrawHeader(address);
    // Stale descendants have no appearance authority, exactly as in storage.
    if(parentEpoch==0u || header.ParentEpoch!=parentEpoch)return true;
    if(!M8FlowerSkinMaskValid(header.SplitBitsLo,header.SplitBitsHi))return false;
    uint index=M8FlowerSkinDrawIndex(header,locality.Child);
    if(index<header.FirstSample || index>0xffffffffu/64u ||
        !M8FlowerDrawRange(index*64u,64u))return false;
    sample=M8FlowerLoadSkinDrawSample(index);
    return sample.Reserved==0u && (sample.Flags&~1u)==0u &&
        all(M8FlowerIsFinite(sample.CapturedRgb)) && all(M8FlowerIsFinite(sample.Optical)) &&
        all(M8FlowerIsFinite(sample.CaptureView));
}

#endif
