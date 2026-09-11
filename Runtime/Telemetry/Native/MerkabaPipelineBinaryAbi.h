// SPDX-License-Identifier: MIT
// Khronos Vulkan-Headers v1.4.328, VK_KHR_pipeline_binary declarations.
// https://github.com/KhronosGroup/Vulkan-Headers/tree/v1.4.328
// Compatibility declarations only: the Unity Android NDK predates this KHR ABI.
#pragma once
#ifndef VK_KHR_pipeline_binary
#define VK_PIPELINE_BINARY_MISSING_KHR static_cast<VkResult>(1000483000)
#define VK_ERROR_NOT_ENOUGH_SPACE_KHR static_cast<VkResult>(-1000483000)
#define VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PIPELINE_BINARY_FEATURES_KHR static_cast<VkStructureType>(1000483000)
#define VK_STRUCTURE_TYPE_PIPELINE_BINARY_CREATE_INFO_KHR static_cast<VkStructureType>(1000483001)
#define VK_STRUCTURE_TYPE_PIPELINE_BINARY_INFO_KHR static_cast<VkStructureType>(1000483002)
#define VK_STRUCTURE_TYPE_PIPELINE_BINARY_KEY_KHR static_cast<VkStructureType>(1000483003)
#define VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PIPELINE_BINARY_PROPERTIES_KHR static_cast<VkStructureType>(1000483004)
#define VK_STRUCTURE_TYPE_RELEASE_CAPTURED_PIPELINE_DATA_INFO_KHR static_cast<VkStructureType>(1000483005)
#define VK_STRUCTURE_TYPE_PIPELINE_BINARY_DATA_INFO_KHR static_cast<VkStructureType>(1000483006)
#define VK_STRUCTURE_TYPE_PIPELINE_CREATE_INFO_KHR static_cast<VkStructureType>(1000483007)
#define VK_STRUCTURE_TYPE_DEVICE_PIPELINE_BINARY_INTERNAL_CACHE_CONTROL_KHR static_cast<VkStructureType>(1000483008)
#define VK_STRUCTURE_TYPE_PIPELINE_BINARY_HANDLES_INFO_KHR static_cast<VkStructureType>(1000483009)
static const VkPipelineCreateFlagBits2KHR VK_PIPELINE_CREATE_2_CAPTURE_DATA_BIT_KHR = 0x80000000ULL;
// VK_KHR_pipeline_binary is a preprocessor guard. Do not pass it to API calls.
#define VK_KHR_pipeline_binary 1
VK_DEFINE_NON_DISPATCHABLE_HANDLE(VkPipelineBinaryKHR)
#define VK_MAX_PIPELINE_BINARY_KEY_SIZE_KHR 32U
#define VK_KHR_PIPELINE_BINARY_SPEC_VERSION 1
#define VK_KHR_PIPELINE_BINARY_EXTENSION_NAME "VK_KHR_pipeline_binary"
typedef struct VkPhysicalDevicePipelineBinaryFeaturesKHR {
    VkStructureType    sType;
    void*              pNext;
    VkBool32           pipelineBinaries;
} VkPhysicalDevicePipelineBinaryFeaturesKHR;

typedef struct VkPhysicalDevicePipelineBinaryPropertiesKHR {
    VkStructureType    sType;
    void*              pNext;
    VkBool32           pipelineBinaryInternalCache;
    VkBool32           pipelineBinaryInternalCacheControl;
    VkBool32           pipelineBinaryPrefersInternalCache;
    VkBool32           pipelineBinaryPrecompiledInternalCache;
    VkBool32           pipelineBinaryCompressedData;
} VkPhysicalDevicePipelineBinaryPropertiesKHR;

typedef struct VkDevicePipelineBinaryInternalCacheControlKHR {
    VkStructureType    sType;
    const void*        pNext;
    VkBool32           disableInternalCache;
} VkDevicePipelineBinaryInternalCacheControlKHR;

typedef struct VkPipelineBinaryKeyKHR {
    VkStructureType    sType;
    void*              pNext;
    uint32_t           keySize;
    uint8_t            key[VK_MAX_PIPELINE_BINARY_KEY_SIZE_KHR];
} VkPipelineBinaryKeyKHR;

typedef struct VkPipelineBinaryDataKHR {
    size_t    dataSize;
    void*     pData;
} VkPipelineBinaryDataKHR;

typedef struct VkPipelineBinaryKeysAndDataKHR {
    uint32_t                          binaryCount;
    const VkPipelineBinaryKeyKHR*     pPipelineBinaryKeys;
    const VkPipelineBinaryDataKHR*    pPipelineBinaryData;
} VkPipelineBinaryKeysAndDataKHR;

typedef struct VkPipelineCreateInfoKHR {
    VkStructureType    sType;
    void*              pNext;
} VkPipelineCreateInfoKHR;

typedef struct VkPipelineBinaryCreateInfoKHR {
    VkStructureType                          sType;
    const void*                              pNext;
    const VkPipelineBinaryKeysAndDataKHR*    pKeysAndDataInfo;
    VkPipeline                               pipeline;
    const VkPipelineCreateInfoKHR*           pPipelineCreateInfo;
} VkPipelineBinaryCreateInfoKHR;

typedef struct VkPipelineBinaryInfoKHR {
    VkStructureType               sType;
    const void*                   pNext;
    uint32_t                      binaryCount;
    const VkPipelineBinaryKHR*    pPipelineBinaries;
} VkPipelineBinaryInfoKHR;

typedef struct VkReleaseCapturedPipelineDataInfoKHR {
    VkStructureType    sType;
    void*              pNext;
    VkPipeline         pipeline;
} VkReleaseCapturedPipelineDataInfoKHR;

typedef struct VkPipelineBinaryDataInfoKHR {
    VkStructureType        sType;
    void*                  pNext;
    VkPipelineBinaryKHR    pipelineBinary;
} VkPipelineBinaryDataInfoKHR;

typedef struct VkPipelineBinaryHandlesInfoKHR {
    VkStructureType         sType;
    const void*             pNext;
    uint32_t                pipelineBinaryCount;
    VkPipelineBinaryKHR*    pPipelineBinaries;
} VkPipelineBinaryHandlesInfoKHR;

typedef VkResult (VKAPI_PTR *PFN_vkCreatePipelineBinariesKHR)(VkDevice device, const VkPipelineBinaryCreateInfoKHR* pCreateInfo, const VkAllocationCallbacks* pAllocator, VkPipelineBinaryHandlesInfoKHR* pBinaries);
typedef void (VKAPI_PTR *PFN_vkDestroyPipelineBinaryKHR)(VkDevice device, VkPipelineBinaryKHR pipelineBinary, const VkAllocationCallbacks* pAllocator);
typedef VkResult (VKAPI_PTR *PFN_vkGetPipelineKeyKHR)(VkDevice device, const VkPipelineCreateInfoKHR* pPipelineCreateInfo, VkPipelineBinaryKeyKHR* pPipelineKey);
typedef VkResult (VKAPI_PTR *PFN_vkGetPipelineBinaryDataKHR)(VkDevice device, const VkPipelineBinaryDataInfoKHR* pInfo, VkPipelineBinaryKeyKHR* pPipelineBinaryKey, size_t* pPipelineBinaryDataSize, void* pPipelineBinaryData);
typedef VkResult (VKAPI_PTR *PFN_vkReleaseCapturedPipelineDataKHR)(VkDevice device, const VkReleaseCapturedPipelineDataInfoKHR* pInfo, const VkAllocationCallbacks* pAllocator);


#endif
