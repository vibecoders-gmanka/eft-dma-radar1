#include <Windows.h>

using Generic4Param_t = uintptr_t(*)(uintptr_t, uintptr_t, uintptr_t, uintptr_t);
using GenericVoid_t = uintptr_t(*)();

struct ShellCodeData_t {
    volatile bool sync;
    Generic4Param_t calledFunction;
    uintptr_t rcx;
    uintptr_t rdx;
    uintptr_t r8;
    uintptr_t r9;

    bool executed;
    uintptr_t result;

    uintptr_t hookedMonoFuncAddress;
    GenericVoid_t hookedMonoFunc;
};

#pragma optimize("", off)

inline volatile bool AtomicExchange(volatile bool* sync) {
    return _InterlockedExchange8(reinterpret_cast<volatile char*>(sync), 0); // 0 = false
}

extern "C" __declspec(dllexport) uintptr_t NH_InvokeHook() {
    ShellCodeData_t* data = (ShellCodeData_t*)0xFAFAFAFAFAFAFAFA;

    if (AtomicExchange(&data->sync)) {
        *(uintptr_t*)(data->hookedMonoFuncAddress) = (uintptr_t)data->hookedMonoFunc;

        data->result = data->calledFunction(data->rcx, data->rdx, data->r8, data->r9);
        data->executed = true;
    }

    return data->hookedMonoFunc();
}

#pragma optimize("", on)