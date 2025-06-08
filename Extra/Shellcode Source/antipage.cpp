#include <Windows.h>


#pragma optimize("", off)

extern "C" __declspec(dllexport) uintptr_t AntiPage_Run(SIZE_T rcx_listCount, uintptr_t rdx, uintptr_t r8, uintptr_t r9) {
	auto pageList = (uintptr_t*)0xFAFAFAFAFAFAFAFA;
    auto virtualQuery = (decltype(&VirtualQuery))0xFBFBFBFBFBFBFBFB;
    if (rcx_listCount && rcx_listCount <= 4096) {
        SIZE_T count = 0;
        MEMORY_BASIC_INFORMATION mbi = {};
        for (SIZE_T i = 0; i < rcx_listCount; i++) {
            auto vaPageBase = pageList[i];
            // Check if the memory is committed and has the desired protection flags
            if (virtualQuery((LPCVOID)vaPageBase, &mbi, sizeof(MEMORY_BASIC_INFORMATION)) &&
                mbi.State == MEM_COMMIT &&
                (mbi.Protect & (PAGE_READONLY | PAGE_READWRITE | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE)) &&
                !(mbi.Protect & PAGE_GUARD)) {
                // Safely dereference
                volatile auto deref = *(unsigned char*)vaPageBase;
                count++;
            }
        }
        return count; // Return number of successfully accessed elements
    }
    // Access list to trigger page faults if necessary
    for (int i = 0; i < 4096; i += 512) {
        volatile auto deref = pageList[i]; // Dereference one element per page
    }
    return 0; // WARNING: Did not run
}

#pragma optimize("", on)