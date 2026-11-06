#pragma once
// Included inside TrainerCheats. The helper is pinned until game exit.
struct NoiseVector {float x,y,z;};
using MakeNoiseFn=void (__thiscall*)(Byte*,float,int,float,NoiseVector);
static MakeNoiseFn originalMakeNoise=nullptr;
static void** noiseSlot=nullptr;
static bool noiseInstalled=false;

static void __fastcall FilterSamNoise(Byte* actor,void*,float radius,int type,float threshold,NoiseVector location) {
    // Retain the original call for every other actor and for non-footstep events.
    const LONG options=activeMask;
    if(actor==pawn&&((options&SilentSam)||((options&SilentMovement)&&(type==1||type==2))))return;
    originalMakeNoise(actor,radius,type,threshold,location);
}

static void NoiseRestore() {
    if(!noiseInstalled||!noiseSlot)return;
    if(*noiseSlot!=reinterpret_cast<void*>(&FilterSamNoise)){noiseInstalled=false;return;}
    DWORD protection=0;
    if(!VirtualProtect(noiseSlot,sizeof(void*),PAGE_READWRITE,&protection))return;
    InterlockedExchangePointer(noiseSlot,reinterpret_cast<void*>(originalMakeNoise));
    DWORD ignored=0;
    VirtualProtect(noiseSlot,sizeof(void*),protection,&ignored);
    noiseInstalled=false;
}

static void NoiseResolve() {
    if(noiseInstalled)return;
    auto engine=GetModuleHandleW(L"Engine.dll"),echelon=GetModuleHandleW(L"Echelon.dll");
    auto actorNoise=GetProcAddress(engine,"?MakeNoise@AActor@@UAEXMW4NoiseType@@MVFVector@@@Z");
    auto pawnNoise=GetProcAddress(echelon,"?MakeNoise@AEPawn@@UAEXMW4NoiseType@@MVFVector@@@Z");
    if(!actorNoise||!pawnNoise||!IsA(pawn,L"EPawn"))throw 1;

    // These exports and the table belong to the hash-validated supported binaries.
    auto actorTable=reinterpret_cast<void**>(GetProcAddress(engine,"??_7AActor@@6B@"));
    if(!actorTable)throw 1;
    auto table=*reinterpret_cast<void***>(pawn);
    MEMORY_BASIC_INFORMATION memory={};
    if(!VirtualQuery(actorTable,&memory,sizeof(memory))||memory.State!=MEM_COMMIT||
       (memory.Protect&(PAGE_GUARD|PAGE_NOACCESS)))throw 1;
    size_t available=(reinterpret_cast<Byte*>(memory.BaseAddress)+memory.RegionSize-reinterpret_cast<Byte*>(actorTable))/sizeof(void*);
    const size_t count=available<512?available:512;
    void** found=nullptr;
    for(size_t i=0;i<count;++i){
        if(actorTable[i]!=reinterpret_cast<void*>(actorNoise))continue;
        if(table[i]!=reinterpret_cast<void*>(actorNoise)&&table[i]!=reinterpret_cast<void*>(pawnNoise))throw 1;
        found=table+i;
        break;
    }
    if(!found)throw 1;
    auto expected=*found;
    DWORD protection=0;
    if(!VirtualProtect(found,sizeof(void*),PAGE_READWRITE,&protection))throw 1;
    originalMakeNoise=reinterpret_cast<MakeNoiseFn>(expected);
    auto previous=InterlockedCompareExchangePointer(found,reinterpret_cast<void*>(&FilterSamNoise),expected);
    DWORD ignored=0;
    VirtualProtect(found,sizeof(void*),protection,&ignored);
    if(previous!=expected)throw 1;
    noiseSlot=found;
    noiseInstalled=true;
}
