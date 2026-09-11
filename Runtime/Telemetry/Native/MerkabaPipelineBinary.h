// Included in the executor namespace after logging. Manufacturing captures
// driver binaries; release consumes only immutable, exact-global-key bundles.
struct BinaryKey
{
    uint32_t size;
    uint8_t bytes[VK_MAX_PIPELINE_BINARY_KEY_SIZE_KHR];
};
static_assert(sizeof(BinaryKey)==36,"pipeline key file ABI");
struct PackedPipelineBinary { BinaryKey key; const uint8_t* data; uint32_t bytes; };
struct PackedPipeline
{
    BinaryKey key;
    uint32_t kind, count;
    const PackedPipelineBinary* const* binaries;
};
struct PipelinePack { BinaryKey global; uint32_t count; const PackedPipeline* pipelines; };
#include "MerkabaPipelinePack.inc"

struct PipelineCaptureHeader
{
    uint32_t magic, version, kind, count;
    BinaryKey global, pipeline;
    uint8_t build[32];
};
static_assert(sizeof(PipelineCaptureHeader)==120,"capture file ABI");
constexpr uint32_t kPipelineCaptureMagic=0x4250384du; // M8PB, little endian
constexpr uint32_t kMaxPipelineBinaries=128u;
constexpr size_t kMaxPipelineBinaryBytes=64u*1024u*1024u;

VkDevice g_binaryDevice=VK_NULL_HANDLE;
PFN_vkGetDeviceProcAddr g_nextGetDeviceProcAddr=nullptr;
PFN_vkCreateComputePipelines g_nextCreateComputePipelines=nullptr;
PFN_vkCreateGraphicsPipelines g_nextCreateGraphicsPipelines=nullptr;
PFN_vkGetPipelineKeyKHR g_getPipelineKey=nullptr;
PFN_vkCreatePipelineBinariesKHR g_createPipelineBinaries=nullptr;
PFN_vkGetPipelineBinaryDataKHR g_getPipelineBinaryData=nullptr;
PFN_vkDestroyPipelineBinaryKHR g_destroyPipelineBinary=nullptr;
PFN_vkReleaseCapturedPipelineDataKHR g_releaseCapturedData=nullptr;
BinaryKey g_pipelineGlobalKey={};
const PipelinePack* g_pipelinePack=nullptr;
std::string g_pipelineCaptureDirectory;
std::mutex g_pipelineCaptureMutex;
std::atomic<uint32_t> g_pipelineBinaryHits{0},g_pipelineBinaryMisses{0},
    g_nonBinaryPipelineAttempts{0},g_pipelineCaptureWrites{0};

BinaryKey BinaryKeyOf(const VkPipelineBinaryKeyKHR& key)
{
    BinaryKey value={};value.size=key.keySize;
    if(key.keySize<=sizeof(value.bytes))std::memcpy(value.bytes,key.key,key.keySize);
    return value;
}
bool BinaryKeyValid(const BinaryKey& key) {return key.size>0u && key.size<=32u;}
int CompareBinaryKeys(const BinaryKey& a,const BinaryKey& b)
{
    if(a.size!=b.size)return a.size<b.size?-1:1;
    return std::memcmp(a.bytes,b.bytes,a.size);
}
std::string BinaryKeyHex(const BinaryKey& key)
{
    std::string text; text.reserve(key.size*2u);
    for(uint32_t i=0;i<key.size && i<32u;i++)
    {text.push_back("0123456789abcdef"[key.bytes[i]>>4u]);text.push_back("0123456789abcdef"[key.bytes[i]&15u]);}
    return text;
}

VkResult PipelineBinaryFailure(const char* reason,VkResult result=VK_ERROR_INITIALIZATION_FAILED)
{
    if(result==VK_SUCCESS)result=VK_ERROR_INITIALIZATION_FAILED;
    LogError(reason,result);
    g_executorReady.store(false,std::memory_order_release);
    g_executorInitError.store(result,std::memory_order_relaxed);
    g_executorInitStatus.store(-1,std::memory_order_release);
    return result;
}

bool PreparePipelineBinaryDevice(VkPhysicalDevice physical,VkDeviceCreateInfo& modified,
    std::vector<const char*>& extensions,VkPhysicalDevicePipelineBinaryFeaturesKHR& added,
    VkPhysicalDeviceMaintenance5FeaturesKHR& maintenance)
{
    uint32_t count=0;
    if(vkEnumerateDeviceExtensionProperties(physical,nullptr,&count,nullptr)!=VK_SUCCESS)return false;
    std::vector<VkExtensionProperties> available(count);
    if(vkEnumerateDeviceExtensionProperties(physical,nullptr,&count,available.data())!=VK_SUCCESS)return false;
    auto enable=[&](const char* name)
    {
        if(std::none_of(available.begin(),available.end(),[&](const auto& e)
            {return std::strcmp(e.extensionName,name)==0;}))return false;
        if(std::none_of(extensions.begin(),extensions.end(),[&](const char* e)
            {return std::strcmp(e,name)==0;}))extensions.push_back(name);
        return true;
    };
    // Quest target. These extension dependencies are enabled explicitly even
    // when the physical device supports a newer core API than Unity requested.
    if(!enable(VK_KHR_PIPELINE_BINARY_EXTENSION_NAME) ||
        !enable(VK_KHR_MAINTENANCE_5_EXTENSION_NAME) ||
        !enable(VK_KHR_DYNAMIC_RENDERING_EXTENSION_NAME) ||
        !enable(VK_KHR_DEPTH_STENCIL_RESOLVE_EXTENSION_NAME) ||
        !enable(VK_KHR_CREATE_RENDERPASS_2_EXTENSION_NAME))return false;
    VkPhysicalDevicePipelineBinaryFeaturesKHR supported={};
    supported.sType=VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PIPELINE_BINARY_FEATURES_KHR;
    VkPhysicalDeviceMaintenance5FeaturesKHR supportedMaintenance={};
    supportedMaintenance.sType=VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_MAINTENANCE_5_FEATURES_KHR;
    supported.pNext=&supportedMaintenance;
    VkPhysicalDeviceFeatures2 features={};features.sType=VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2;
    features.pNext=&supported;vkGetPhysicalDeviceFeatures2(physical,&features);
    if(!supported.pipelineBinaries || !supportedMaintenance.maintenance5)return false;
    bool present=false,maintenancePresent=false;
    for(auto link=static_cast<const VkBaseInStructure*>(modified.pNext);link;link=link->pNext)
    {
        if(link->sType==VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PIPELINE_BINARY_FEATURES_KHR)
        {
            // Never mutate Unity's const chain or append a duplicate feature.
            if(!reinterpret_cast<const VkPhysicalDevicePipelineBinaryFeaturesKHR*>(link)->pipelineBinaries)return false;
            present=true;
        }
        if(link->sType==VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_MAINTENANCE_5_FEATURES_KHR)
        {
            if(!reinterpret_cast<const VkPhysicalDeviceMaintenance5FeaturesKHR*>(link)->maintenance5)return false;
            maintenancePresent=true;
        }
    }
    // flags2 is a maintenance5 feature, not just an advertised extension.
    if(!maintenancePresent)
    {
        maintenance={};maintenance.sType=VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_MAINTENANCE_5_FEATURES_KHR;
        maintenance.maintenance5=VK_TRUE;maintenance.pNext=const_cast<void*>(modified.pNext);
        modified.pNext=&maintenance;
    }
    if(!present)
    {
        added={};added.sType=VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PIPELINE_BINARY_FEATURES_KHR;
        added.pipelineBinaries=VK_TRUE;added.pNext=const_cast<void*>(modified.pNext);
        modified.pNext=&added;
    }
    modified.ppEnabledExtensionNames=extensions.data();
    modified.enabledExtensionCount=static_cast<uint32_t>(extensions.size());
    return true;
}

bool InitializePipelineBinaries(VkDevice device)
{
    if(g_binaryDevice!=VK_NULL_HANDLE)return false;
    g_nextGetDeviceProcAddr=reinterpret_cast<PFN_vkGetDeviceProcAddr>(
        g_nextGetInstanceProcAddr(g_interceptInstance,"vkGetDeviceProcAddr"));
    if(!g_nextGetDeviceProcAddr)return false;
#define M8_BINARY_PROC(field,type,name) field=reinterpret_cast<type>(g_nextGetDeviceProcAddr(device,name)); if(!field)return false
    M8_BINARY_PROC(g_nextCreateComputePipelines,PFN_vkCreateComputePipelines,"vkCreateComputePipelines");
    M8_BINARY_PROC(g_nextCreateGraphicsPipelines,PFN_vkCreateGraphicsPipelines,"vkCreateGraphicsPipelines");
    M8_BINARY_PROC(g_getPipelineKey,PFN_vkGetPipelineKeyKHR,"vkGetPipelineKeyKHR");
    M8_BINARY_PROC(g_createPipelineBinaries,PFN_vkCreatePipelineBinariesKHR,"vkCreatePipelineBinariesKHR");
    M8_BINARY_PROC(g_getPipelineBinaryData,PFN_vkGetPipelineBinaryDataKHR,"vkGetPipelineBinaryDataKHR");
    M8_BINARY_PROC(g_destroyPipelineBinary,PFN_vkDestroyPipelineBinaryKHR,"vkDestroyPipelineBinaryKHR");
    M8_BINARY_PROC(g_releaseCapturedData,PFN_vkReleaseCapturedPipelineDataKHR,"vkReleaseCapturedPipelineDataKHR");
#undef M8_BINARY_PROC
    VkPipelineBinaryKeyKHR key={};key.sType=VK_STRUCTURE_TYPE_PIPELINE_BINARY_KEY_KHR;
    if(g_getPipelineKey(device,nullptr,&key)!=VK_SUCCESS)return false;
    g_pipelineGlobalKey=BinaryKeyOf(key);
    if(!BinaryKeyValid(g_pipelineGlobalKey))return false;
    g_pipelinePack=nullptr;
    for(uint32_t i=0;i<kPipelinePackCount;i++)
        if(CompareBinaryKeys(kPipelinePacks[i].global,g_pipelineGlobalKey)==0)
        {g_pipelinePack=&kPipelinePacks[i];break;}
    char message[320];
    std::snprintf(message,sizeof(message),"Merkaba pipeline-binary mode=%s globalKey=%s globalKeyMatch=%u build=%s",
        kPipelineCaptureMode?"CAPTURE":"BINARY_ONLY",BinaryKeyHex(g_pipelineGlobalKey).c_str(),
        g_pipelinePack!=nullptr,kPipelineBuildHex);
    Log(message);
    if(!kPipelineCaptureMode && !g_pipelinePack)
    {
        g_pipelineBinaryMisses.fetch_add(1,std::memory_order_relaxed);
        PipelineBinaryFailure("no compatible bundled global pipeline key");return false;
    }
    g_binaryDevice=device;
    return true;
}

const PackedPipeline* FindPackedPipeline(const BinaryKey& key,uint32_t kind)
{
    if(!g_pipelinePack)return nullptr;
    uint32_t first=0,last=g_pipelinePack->count;
    while(first<last)
    {
        uint32_t middle=first+(last-first)/2u;
        const PackedPipeline& entry=g_pipelinePack->pipelines[middle];
        int order=CompareBinaryKeys(entry.key,key);
        if(order==0)order=entry.kind<kind?-1:entry.kind>kind?1:0;
        if(order<0)first=middle+1u;else last=middle;
    }
    if(first<g_pipelinePack->count)
    {
        const PackedPipeline* entry=&g_pipelinePack->pipelines[first];
        if(entry->kind==kind && CompareBinaryKeys(entry->key,key)==0)return entry;
    }
    return nullptr;
}

struct OwnedPipelineBinaries
{
    VkDevice device;
    std::vector<VkPipelineBinaryKHR> handles;
    explicit OwnedPipelineBinaries(VkDevice d):device(d){}
    ~OwnedPipelineBinaries()
    {for(VkPipelineBinaryKHR h:handles)if(h)g_destroyPipelineBinary(device,h,nullptr);}
    VkResult Load(const PackedPipeline& entry)
    {
        if(entry.count==0u || entry.count>kMaxPipelineBinaries)return VK_ERROR_INITIALIZATION_FAILED;
        std::vector<VkPipelineBinaryKeyKHR> keys(entry.count);
        std::vector<VkPipelineBinaryDataKHR> data(entry.count);
        for(uint32_t i=0;i<entry.count;i++)
        {
            const auto& binary=*entry.binaries[i];
            if(!BinaryKeyValid(binary.key) || binary.bytes==0 || binary.bytes>kMaxPipelineBinaryBytes)
                return VK_ERROR_INITIALIZATION_FAILED;
            keys[i].sType=VK_STRUCTURE_TYPE_PIPELINE_BINARY_KEY_KHR;
            keys[i].keySize=binary.key.size;std::memcpy(keys[i].key,binary.key.bytes,binary.key.size);
            data[i].dataSize=binary.bytes;data[i].pData=const_cast<uint8_t*>(binary.data);
        }
        VkPipelineBinaryKeysAndDataKHR input={entry.count,keys.data(),data.data()};
        VkPipelineBinaryCreateInfoKHR create={};create.sType=VK_STRUCTURE_TYPE_PIPELINE_BINARY_CREATE_INFO_KHR;
        create.pKeysAndDataInfo=&input;
        handles.resize(entry.count,VK_NULL_HANDLE);
        VkPipelineBinaryHandlesInfoKHR output={};output.sType=VK_STRUCTURE_TYPE_PIPELINE_BINARY_HANDLES_INFO_KHR;
        output.pipelineBinaryCount=entry.count;output.pPipelineBinaries=handles.data();
        VkResult result=g_createPipelineBinaries(device,&create,nullptr,&output);
        return result==VK_SUCCESS && output.pipelineBinaryCount!=entry.count?VK_ERROR_INITIALIZATION_FAILED:result;
    }
};

bool CapturePipeline(VkDevice device,VkPipeline pipeline,const BinaryKey& key,uint32_t kind)
{
    OwnedPipelineBinaries binaries(device);
    VkPipelineBinaryCreateInfoKHR input={};input.sType=VK_STRUCTURE_TYPE_PIPELINE_BINARY_CREATE_INFO_KHR;
    input.pipeline=pipeline;
    VkPipelineBinaryHandlesInfoKHR output={};output.sType=VK_STRUCTURE_TYPE_PIPELINE_BINARY_HANDLES_INFO_KHR;
    VkResult result=g_createPipelineBinaries(device,&input,nullptr,&output);
    if(result!=VK_SUCCESS || output.pipelineBinaryCount==0u || output.pipelineBinaryCount>kMaxPipelineBinaries)return false;
    binaries.handles.resize(output.pipelineBinaryCount,VK_NULL_HANDLE);
    output.pPipelineBinaries=binaries.handles.data();
    if(g_createPipelineBinaries(device,&input,nullptr,&output)!=VK_SUCCESS)return false;
    std::lock_guard<std::mutex> lock(g_pipelineCaptureMutex);
    if(g_pipelineCaptureDirectory.empty())return false; // bootstrap runs before Vulkan
    std::string directory=g_pipelineCaptureDirectory+"/"+kPipelineBuildHex;
    if(mkdir(directory.c_str(),0700)!=0 && errno!=EEXIST)return false;
    directory+="/"+BinaryKeyHex(g_pipelineGlobalKey);
    if(mkdir(directory.c_str(),0700)!=0 && errno!=EEXIST)return false;
    std::string path=directory+"/"+BinaryKeyHex(key)+"-"+std::to_string(kind)+".m8pb";
    std::string temporary=path+".tmp";
    std::unique_ptr<FILE,decltype(&std::fclose)> outputFile(std::fopen(temporary.c_str(),"wb"),std::fclose);
    if(!outputFile)return false;
    FILE* file=outputFile.get();
    PipelineCaptureHeader header={};header.magic=kPipelineCaptureMagic;header.version=1;
    header.kind=kind;header.count=static_cast<uint32_t>(binaries.handles.size());
    header.global=g_pipelineGlobalKey;header.pipeline=key;std::memcpy(header.build,kPipelineBuildId,32);
    bool saved=std::fwrite(&header,sizeof(header),1,file)==1;
    for(auto handle:binaries.handles)
    {
        VkPipelineBinaryDataInfoKHR info={};info.sType=VK_STRUCTURE_TYPE_PIPELINE_BINARY_DATA_INFO_KHR;
        info.pipelineBinary=handle;
        VkPipelineBinaryKeyKHR binaryKey={};binaryKey.sType=VK_STRUCTURE_TYPE_PIPELINE_BINARY_KEY_KHR;
        size_t bytes=0;
        if(g_getPipelineBinaryData(device,&info,&binaryKey,&bytes,nullptr)!=VK_SUCCESS ||
            bytes==0 || bytes>kMaxPipelineBinaryBytes){saved=false;break;}
        std::vector<uint8_t> data(bytes);
        if(g_getPipelineBinaryData(device,&info,&binaryKey,&bytes,data.data())!=VK_SUCCESS || bytes>data.size())
        {saved=false;break;}
        BinaryKey stored=BinaryKeyOf(binaryKey);uint32_t length=static_cast<uint32_t>(bytes);
        saved=saved && BinaryKeyValid(stored) && std::fwrite(&stored,sizeof(stored),1,file)==1 &&
            std::fwrite(&length,sizeof(length),1,file)==1 && std::fwrite(data.data(),1,length,file)==length;
    }
    saved=saved && std::fflush(file)==0 && fsync(fileno(file))==0;
    if(std::fclose(outputFile.release())!=0)saved=false;
    if(saved)saved=std::rename(temporary.c_str(),path.c_str())==0;
    if(!saved){std::remove(temporary.c_str());return false;}
    int directoryFd=open(directory.c_str(),O_RDONLY|O_DIRECTORY);
    if(directoryFd<0)return false;
    bool durable=fsync(directoryFd)==0;close(directoryFd);
    if(!durable)return false;
    g_pipelineCaptureWrites.fetch_add(1,std::memory_order_relaxed);
    char message[256];std::snprintf(message,sizeof(message),"Merkaba pipeline captured: kind=%u key=%s binaries=%u",
        kind,BinaryKeyHex(key).c_str(),header.count);Log(message);
    return true;
}

// Copy only the prefix through an existing flags2 node. Opaque tails stay
// intact; no guessed sizes, const writes or duplicate sTypes enter Unity's chain.
struct PipelineCreateChain
{
    VkPipelineCreateFlags2CreateInfoKHR flags={};
    VkPipelineBinaryInfoKHR binary={};
    std::vector<std::vector<uint64_t>> copies;
    const void* head=nullptr;
    bool Prepare(const void* source,VkPipelineCreateFlags original)
    {
        const VkBaseInStructure* target=nullptr;
        for(auto link=static_cast<const VkBaseInStructure*>(source);link;link=link->pNext)
        {
            if(link->sType==VK_STRUCTURE_TYPE_PIPELINE_BINARY_INFO_KHR)return false;
            if(link->sType==VK_STRUCTURE_TYPE_PIPELINE_CREATE_FLAGS_2_CREATE_INFO_KHR)target=link;
        }
        flags.sType=VK_STRUCTURE_TYPE_PIPELINE_CREATE_FLAGS_2_CREATE_INFO_KHR;
        flags.flags=target?reinterpret_cast<const VkPipelineCreateFlags2CreateInfoKHR*>(target)->flags:original;
        flags.flags&=~(VkPipelineCreateFlags2KHR(VK_PIPELINE_CREATE_FAIL_ON_PIPELINE_COMPILE_REQUIRED_BIT_EXT)|
            VkPipelineCreateFlags2KHR(VK_PIPELINE_CREATE_EARLY_RETURN_ON_FAILURE_BIT_EXT)|VK_PIPELINE_CREATE_2_CAPTURE_DATA_BIT_KHR);
        head=&flags;flags.pNext=source;
        if(!target)return true;
        VkBaseOutStructure* previous=nullptr;
        for(auto link=static_cast<const VkBaseInStructure*>(source);link;link=link->pNext)
        {
            if(link==target)
            {
                flags.pNext=link->pNext;
                if(previous)previous->pNext=reinterpret_cast<VkBaseOutStructure*>(&flags);
                return true;
            }
            size_t bytes=0;
#define M8_PIPELINE_CHAIN(tag,type) case tag: bytes=sizeof(type); break
            switch(link->sType)
            {
                M8_PIPELINE_CHAIN(VK_STRUCTURE_TYPE_PIPELINE_CREATION_FEEDBACK_CREATE_INFO,VkPipelineCreationFeedbackCreateInfo);
                M8_PIPELINE_CHAIN(VK_STRUCTURE_TYPE_PIPELINE_RENDERING_CREATE_INFO,VkPipelineRenderingCreateInfo);
                M8_PIPELINE_CHAIN(VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_LIBRARY_CREATE_INFO_EXT,VkGraphicsPipelineLibraryCreateInfoEXT);
                M8_PIPELINE_CHAIN(VK_STRUCTURE_TYPE_PIPELINE_LIBRARY_CREATE_INFO_KHR,VkPipelineLibraryCreateInfoKHR);
                M8_PIPELINE_CHAIN(VK_STRUCTURE_TYPE_PIPELINE_FRAGMENT_SHADING_RATE_STATE_CREATE_INFO_KHR,VkPipelineFragmentShadingRateStateCreateInfoKHR);
                M8_PIPELINE_CHAIN(VK_STRUCTURE_TYPE_PIPELINE_ROBUSTNESS_CREATE_INFO_EXT,VkPipelineRobustnessCreateInfoEXT);
                default:return false;
            }
#undef M8_PIPELINE_CHAIN
            copies.emplace_back((bytes+7u)/8u);std::memcpy(copies.back().data(),link,bytes);
            auto copy=reinterpret_cast<VkBaseOutStructure*>(copies.back().data());
            if(previous)previous->pNext=copy;else head=copy;
            previous=copy;
        }
        return false;
    }
    void Attach(const OwnedPipelineBinaries& owned)
    {
        binary.sType=VK_STRUCTURE_TYPE_PIPELINE_BINARY_INFO_KHR;binary.pNext=head;
        binary.binaryCount=static_cast<uint32_t>(owned.handles.size());
        binary.pPipelineBinaries=owned.handles.data();head=&binary;
    }
};

template<class Info,class Create>
VkResult CreateBinaryPipelines(VkDevice device,uint32_t count,const Info* infos,
    const VkAllocationCallbacks* allocator,VkPipeline* pipelines,Create create,uint32_t kind)
{
    if(count==0u)return VK_SUCCESS;
    if(!infos || !pipelines)return PipelineBinaryFailure("invalid pipeline batch");
    std::fill(pipelines,pipelines+count,VK_NULL_HANDLE);
    if(device!=g_binaryDevice || !create)
    {
        g_nonBinaryPipelineAttempts.fetch_add(count,std::memory_order_relaxed);
        return PipelineBinaryFailure("pipeline creation bypassed device binary negotiation");
    }
    try
    {
        // Keep the batch intact: derivative basePipelineIndex references and
        // Unity allocation callbacks retain the original Vulkan semantics.
        std::vector<Info> modified(infos,infos+count);
        std::vector<PipelineCreateChain> chains(count);
        std::vector<BinaryKey> keys(count);
        std::vector<std::unique_ptr<OwnedPipelineBinaries>> owned;
        owned.reserve(count);
        for(uint32_t i=0;i<count;i++)
        {
            if(!chains[i].Prepare(infos[i].pNext,infos[i].flags))
                return PipelineBinaryFailure("unsupported pipeline pNext prefix before flags2");
            modified[i].flags=0;modified[i].pNext=chains[i].head;
            VkPipelineCreateInfoKHR query={};query.sType=VK_STRUCTURE_TYPE_PIPELINE_CREATE_INFO_KHR;
            query.pNext=&modified[i];
            VkPipelineBinaryKeyKHR key={};key.sType=VK_STRUCTURE_TYPE_PIPELINE_BINARY_KEY_KHR;
            VkResult result=g_getPipelineKey(device,&query,&key);
            keys[i]=BinaryKeyOf(key);
            if(result!=VK_SUCCESS || !BinaryKeyValid(keys[i]))return PipelineBinaryFailure("vkGetPipelineKeyKHR",result);
            if(kPipelineCaptureMode)
            {
                char message[256];std::snprintf(message,sizeof(message),"Merkaba pipeline required: kind=%u key=%s",
                    kind,BinaryKeyHex(keys[i]).c_str());Log(message);
            }
            if(kPipelineCaptureMode)chains[i].flags.flags|=VK_PIPELINE_CREATE_2_CAPTURE_DATA_BIT_KHR;
            else
            {
                const PackedPipeline* entry=FindPackedPipeline(keys[i],kind);
                if(!entry)
                {
                    g_pipelineBinaryMisses.fetch_add(1,std::memory_order_relaxed);
                    char message[192];std::snprintf(message,sizeof(message),"missing bundled PSO kind=%u key=%s; no SPIR-V fallback",
                        kind,BinaryKeyHex(keys[i]).c_str());
                    return PipelineBinaryFailure(message);
                }
                owned.emplace_back(new OwnedPipelineBinaries(device));
                result=owned.back()->Load(*entry);
                if(result!=VK_SUCCESS)return PipelineBinaryFailure("vkCreatePipelineBinariesKHR",result);
                chains[i].Attach(*owned.back());modified[i].pNext=chains[i].head;
            }
        }
        VkResult result=create(device,VK_NULL_HANDLE,count,modified.data(),allocator,pipelines);
        bool captured=true;
        if(kPipelineCaptureMode)
        {
            for(uint32_t i=0;i<count;i++)if(pipelines[i]!=VK_NULL_HANDLE)
            {
                bool saved=CapturePipeline(device,pipelines[i],keys[i],kind);
                VkReleaseCapturedPipelineDataInfoKHR release={};
                release.sType=VK_STRUCTURE_TYPE_RELEASE_CAPTURED_PIPELINE_DATA_INFO_KHR;
                release.pipeline=pipelines[i];
                VkResult freed=g_releaseCapturedData(device,&release,allocator);
                captured=saved && freed==VK_SUCCESS && captured;
            }
        }
        else if(result==VK_SUCCESS)g_pipelineBinaryHits.fetch_add(count,std::memory_order_relaxed);
        if(!captured || result!=VK_SUCCESS)
            return PipelineBinaryFailure(!captured?"pipeline capture incomplete":"binary pipeline creation",result==VK_SUCCESS?VK_ERROR_INITIALIZATION_FAILED:result);
        return result;
    }
    catch(const std::exception& e){return PipelineBinaryFailure(e.what(),VK_ERROR_OUT_OF_HOST_MEMORY);}
    catch(...){return PipelineBinaryFailure("pipeline binary exception");}
}

VkResult VKAPI_PTR InterceptCreateComputePipelines(VkDevice device,VkPipelineCache,uint32_t count,
    const VkComputePipelineCreateInfo* infos,const VkAllocationCallbacks* allocator,VkPipeline* pipelines)
{return CreateBinaryPipelines(device,count,infos,allocator,pipelines,g_nextCreateComputePipelines,1u);}
VkResult VKAPI_PTR InterceptCreateGraphicsPipelines(VkDevice device,VkPipelineCache,uint32_t count,
    const VkGraphicsPipelineCreateInfo* infos,const VkAllocationCallbacks* allocator,VkPipeline* pipelines)
{return CreateBinaryPipelines(device,count,infos,allocator,pipelines,g_nextCreateGraphicsPipelines,2u);}

void VKAPI_PTR InterceptDestroyDevice(VkDevice device,const VkAllocationCallbacks* allocator)
{
    auto destroy=g_nextGetDeviceProcAddr?reinterpret_cast<PFN_vkDestroyDevice>(
        g_nextGetDeviceProcAddr(device,"vkDestroyDevice")):nullptr;
    if(!destroy)return;
    destroy(device,allocator);
    if(device==g_binaryDevice)
    {
        g_binaryDevice=VK_NULL_HANDLE;g_pipelinePack=nullptr;g_pipelineGlobalKey={};
    }
}

PFN_vkVoidFunction PipelineBinaryIntercept(const char* name)
{
    if(!name)return nullptr;
    if(std::strcmp(name,"vkDestroyDevice")==0)return reinterpret_cast<PFN_vkVoidFunction>(InterceptDestroyDevice);
    if(std::strcmp(name,"vkCreateComputePipelines")==0)return reinterpret_cast<PFN_vkVoidFunction>(InterceptCreateComputePipelines);
    if(std::strcmp(name,"vkCreateGraphicsPipelines")==0)return reinterpret_cast<PFN_vkVoidFunction>(InterceptCreateGraphicsPipelines);
    return nullptr;
}
PFN_vkVoidFunction VKAPI_PTR InterceptGetDeviceProcAddr(VkDevice device,const char* name)
{
    auto intercepted=PipelineBinaryIntercept(name);
    if(intercepted)return intercepted;
    return g_nextGetDeviceProcAddr?g_nextGetDeviceProcAddr(device,name):nullptr;
}
