#ifndef GENESIS_MERKABA_FLOWER_EVIDENCE_PACKET_INCLUDED
#define GENESIS_MERKABA_FLOWER_EVIDENCE_PACKET_INCLUDED

// The caller owns a bounded, workgroup-local scratch lifetime and validates
// its exact owner/carrier address. This codec preserves all evidence bits;
// it performs no root evaluation, interval reduction or dependency admission.
#define M8_FLOWER_PACKET_ROOT_WORDS 9u
#define M8_FLOWER_PACKET_ORIGINAL_WORDS 19u
#define M8_FLOWER_PACKET_SITE_WORDS 10u
#define M8_FLOWER_PACKET_RECEIPT_MASK 0x07ffffffu
#define M8_FLOWER_PACKET_CONTROL_BIT 0x80000000u

uint M8FlowerPacketLoadWord(uint address);
void M8FlowerPacketStoreWord(uint address,uint value);

void M8FlowerPacketStoreRoot(uint address,M8FlowerPhaseRootEvidence root)
{
    M8FlowerPacketStoreWord(address,asuint(root.Junction.x));
    M8FlowerPacketStoreWord(address+1u,asuint(root.Junction.y));
    M8FlowerPacketStoreWord(address+2u,asuint(root.Junction.z));
    M8FlowerPacketStoreWord(address+3u,root.Tag);
    M8FlowerPacketStoreWord(address+4u,asuint(root.Root.x.lo));
    M8FlowerPacketStoreWord(address+5u,asuint(root.Root.x.hi));
    M8FlowerPacketStoreWord(address+6u,asuint(root.Root.y.lo));
    M8FlowerPacketStoreWord(address+7u,asuint(root.Root.y.hi));
    M8FlowerPacketStoreWord(address+8u,root.Classification);
}

M8FlowerPhaseRootEvidence M8FlowerPacketLoadRoot(uint address)
{
    M8FlowerPhaseRootEvidence root;
    root.Junction=asint(uint3(M8FlowerPacketLoadWord(address),
        M8FlowerPacketLoadWord(address+1u),M8FlowerPacketLoadWord(address+2u)));
    root.Tag=M8FlowerPacketLoadWord(address+3u);
    root.Root.x.lo=asfloat(M8FlowerPacketLoadWord(address+4u));
    root.Root.x.hi=asfloat(M8FlowerPacketLoadWord(address+5u));
    root.Root.y.lo=asfloat(M8FlowerPacketLoadWord(address+6u));
    root.Root.y.hi=asfloat(M8FlowerPacketLoadWord(address+7u));
    root.Classification=M8FlowerPacketLoadWord(address+8u);
    return root;
}

void M8FlowerPacketStoreOriginal(uint address,M8FlowerPhaseRootEvidence root,
    M8FlowerPhaseRootEvidence rawLocalBase,bool provisional,uint receipt)
{
    M8FlowerPacketStoreRoot(address,root);
    M8FlowerPacketStoreRoot(address+9u,rawLocalBase);
    M8FlowerPacketStoreWord(address+18u,(receipt&M8_FLOWER_PACKET_RECEIPT_MASK)|
        (provisional?M8_FLOWER_PACKET_CONTROL_BIT:0u));
}

uint M8FlowerPacketReadOriginal(uint address,out M8FlowerPhaseRootEvidence root,
    out M8FlowerPhaseRootEvidence rawLocalBase,out bool provisional,out uint receipt)
{
    root=M8FlowerPacketLoadRoot(address);
    rawLocalBase=M8FlowerPacketLoadRoot(address+9u);
    uint control=M8FlowerPacketLoadWord(address+18u);
    provisional=(control&M8_FLOWER_PACKET_CONTROL_BIT)!=0u;
    receipt=control&M8_FLOWER_PACKET_RECEIPT_MASK;
    return root.Classification;
}

void M8FlowerPacketStoreSite(uint address,M8FlowerPhaseRootEvidence root,bool read,uint receipt)
{
    M8FlowerPacketStoreRoot(address,root);
    M8FlowerPacketStoreWord(address+9u,(receipt&M8_FLOWER_PACKET_RECEIPT_MASK)|
        (read?M8_FLOWER_PACKET_CONTROL_BIT:0u));
}

bool M8FlowerPacketReadSite(uint address,out M8FlowerPhaseRootEvidence root,out uint receipt)
{
    root=M8FlowerPacketLoadRoot(address);
    uint control=M8FlowerPacketLoadWord(address+9u);
    receipt=control&M8_FLOWER_PACKET_RECEIPT_MASK;
    return (control&M8_FLOWER_PACKET_CONTROL_BIT)!=0u;
}

#endif
