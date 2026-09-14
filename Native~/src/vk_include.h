// Single Vulkan include point. Android builds use the platform header (AHB import);
// host unit tests use the platform-neutral core header without prototypes.
#pragma once
#if defined(__ANDROID__)
#ifndef VK_USE_PLATFORM_ANDROID_KHR
#define VK_USE_PLATFORM_ANDROID_KHR 1
#endif
#include <vulkan/vulkan.h>
#else
#ifndef VK_NO_PROTOTYPES
#define VK_NO_PROTOTYPES 1
#endif
#include <vulkan/vulkan_core.h>
#endif
#include "vk_compat.h"
